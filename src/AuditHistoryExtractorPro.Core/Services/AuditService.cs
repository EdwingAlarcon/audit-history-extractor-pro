using AuditHistoryExtractorPro.Core.Models;
using AuditHistoryExtractorPro.Domain.Interfaces;
using AuditHistoryExtractorPro.Domain.ValueObjects;
using DataverseServiceClient = Microsoft.PowerPlatform.Dataverse.Client.ServiceClient;
using Microsoft.Crm.Sdk.Messages;
using Microsoft.Extensions.Logging;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Metadata;
using Microsoft.Xrm.Sdk.Query;
using Polly;
using Polly.Retry;
using System.Globalization;
using System.Net;
using System.ServiceModel;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace AuditHistoryExtractorPro.Core.Services;

public class AuditService : IAuditService
{
    private const int MaxDataversePageSize = 5000;
    private readonly AuthHelper _authHelper;
    private readonly QueryBuilderService _queryBuilderService;
    private readonly IExcelExportService _excelExportService;
    private readonly IMetadataTranslationService _metadataTranslationService;
    private readonly Microsoft.Extensions.Logging.ILogger<AuditService> _logger;
    private const int MaxThrottlingRetries = 3;
    private const int ExtraRetryDelaySeconds = 1;
    private const int DefaultRetryAfterSeconds = 5;
    private readonly SemaphoreSlim _connectionLock = new(1, 1);
    private readonly SemaphoreSlim _nameResolutionLock = new(1, 1);
    private DataverseServiceClient? _serviceClient;
    private IAuthenticationProvider? _authenticationProvider;
    private AuthenticationConfiguration? _authenticationConfiguration;
    private Dictionary<string, Dictionary<int, string>> _optionSetCache = new(StringComparer.OrdinalIgnoreCase);
    private Dictionary<int, string> _attributeByColumnNumber = new();
    private Dictionary<string, string> _lookupTargetByAttribute = new(StringComparer.OrdinalIgnoreCase);
    private Dictionary<string, string> _primaryNameAttributeCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _nameResolutionCache = new(StringComparer.OrdinalIgnoreCase);
    private IProgress<string>? _currentProgress;

    public bool IsConnected { get; private set; }
    public string OrganizationName { get; private set; } = string.Empty;
    internal DataverseServiceClient? ServiceClient => _serviceClient;

    public AuditService(
        AuthHelper authHelper,
        QueryBuilderService queryBuilderService,
        IExcelExportService excelExportService,
        IMetadataTranslationService metadataTranslationService,
        Microsoft.Extensions.Logging.ILogger<AuditService> logger)
    {
        _authHelper = authHelper;
        _queryBuilderService = queryBuilderService;
        _excelExportService = excelExportService;
        _metadataTranslationService = metadataTranslationService;
        _logger = logger;
    }

    public async Task WarmupEntityMetadataAsync(string entityLogicalName, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(entityLogicalName))
        {
            return;
        }

        // Tolerancia a Fallos Individuales (Graceful Degradation):
        // Si la entidad tiene metadatos corruptos en el servidor (ej. soluciones
        // desinstaladas que dejan "fantasmas" con Guid.Empty), el SDK lanza una
        // FaultException que NO debe abortar el flujo de conexión completo.
        // Registramos el aviso y continuamos: la extracción seguirá funcionando
        // para el resto de entidades; solo esta perderá el pre-calentamiento.
        try
        {
            await LoadEntityMetadataContextAsync(entityLogicalName.Trim(), cancellationToken);
        }
        catch (OperationCanceledException)
        {
            // Cancelación explícita del usuario: se propaga sin silenciar.
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "[WarmupEntityMetadataAsync] No se pudo pre-cargar '{EntityName}'",
                entityLogicalName);
            // continue: el warmup de esta entidad falla de forma silenciosa,
            // el resto del flujo de conexión continúa con normalidad.
        }
    }

    public async Task ConnectAsync(ConnectionSettings settings, CancellationToken cancellationToken = default)
    {
        await _connectionLock.WaitAsync(cancellationToken);
        try
        {
            IsConnected = false;
            OrganizationName = string.Empty;

            _authenticationConfiguration = _authHelper.BuildConfiguration(settings);
            _authenticationProvider = _authHelper.CreateProvider(_authenticationConfiguration);

            var dataverseUri = new Uri(_authenticationConfiguration.EnvironmentUrl, UriKind.Absolute);
            _serviceClient = new DataverseServiceClient(
                dataverseUri,
                async _ => await _authenticationProvider.GetAccessTokenAsync(cancellationToken),
                useUniqueInstance: true,
                logger: null);

            if (!_serviceClient.IsReady)
            {
                throw new InvalidOperationException(string.IsNullOrWhiteSpace(_serviceClient.LastError)
                    ? "No fue posible inicializar el cliente Dataverse."
                    : _serviceClient.LastError);
            }

            var whoAmIResponse = await _serviceClient.ExecuteAsync(new WhoAmIRequest(), cancellationToken);
            if (whoAmIResponse is null)
            {
                throw new InvalidOperationException("WhoAmI no devolvió respuesta.");
            }

            IsConnected = true;
            OrganizationName = string.IsNullOrWhiteSpace(_serviceClient.ConnectedOrgFriendlyName)
                ? dataverseUri.Host
                : _serviceClient.ConnectedOrgFriendlyName;

            _optionSetCache.Clear();
            _attributeByColumnNumber.Clear();
            _lookupTargetByAttribute.Clear();
            _primaryNameAttributeCache.Clear();
            _nameResolutionCache.Clear();
        }
        catch
        {
            IsConnected = false;
            OrganizationName = string.Empty;
            throw;
        }
        finally
        {
            _connectionLock.Release();
        }
    }

    public async Task<IReadOnlyList<LookupItem>> GetUsersAsync(CancellationToken cancellationToken = default)
    {
        if (_serviceClient is null || !_serviceClient.IsReady)
        {
            return Array.Empty<LookupItem>();
        }

        var query = new QueryExpression("systemuser")
        {
            ColumnSet = new ColumnSet("systemuserid", "fullname"),
            Criteria = new FilterExpression(LogicalOperator.And)
            {
                Conditions =
                {
                    new ConditionExpression("isdisabled", ConditionOperator.Equal, false)
                }
            },
            TopCount = 200
        };

        query.Orders.Add(new OrderExpression("fullname", OrderType.Ascending));

        var users = await Task.Run(() => _serviceClient.RetrieveMultiple(query), cancellationToken);
        return users.Entities
            .Select(e => new LookupItem
            {
                Id = e.GetAttributeValue<Guid>("systemuserid"),
                Name = e.GetAttributeValue<string>("fullname") ?? "(sin nombre)"
            })
            .Where(u => u.Id != Guid.Empty)
            .ToList();
    }

    public async Task<IReadOnlyList<AuditExportRow>> GetPreviewRowsAsync(
        ExtractionRequest request,
        int maxRows = 50,
        CancellationToken cancellationToken = default)
    {
        if (_serviceClient is null || !_serviceClient.IsReady)
        {
            throw new InvalidOperationException("No hay conexión activa a Dataverse.");
        }

        // Aseguramos que los metadatos de la entidad estén cargados
        await LoadEntityMetadataContextAsync(request.EntityName, cancellationToken);

        // Construimos una copia de la solicitud con MaxRecords acotado al límite de vista previa.
        // Esto garantiza que StreamRowsAsync se detenga exactamente en maxRows filas,
        // sin importar el MaxRecords original de la solicitud completa.
        var previewRequest = new ExtractionRequest
        {
            EntityName         = request.EntityName,
            RecordId           = request.RecordId,
            SelectedDateRange  = request.SelectedDateRange,
            SelectedDateFrom   = request.SelectedDateFrom,
            SelectedDateTo     = request.SelectedDateTo,
            IsFullDay          = request.IsFullDay,
            StartDate          = request.StartDate,
            EndDate            = request.EndDate,
            SelectedUser       = request.SelectedUser,
            SelectedOperations = request.SelectedOperations,
            SelectedActions    = request.SelectedActions,
            SelectedAttributes = request.SelectedAttributes,
            SearchValue        = request.SearchValue,
            MaxRecords         = maxRows,  // ← límite de seguridad anti-OOM
            SelectedView       = request.SelectedView
        };

        // Paso 1: resolver IDs desde la Vista seleccionada (si aplica)
        var objectIds = await ResolveViewObjectIdsAsync(previewRequest.SelectedView, cancellationToken);

        var rows = new List<AuditExportRow>(maxRows);
        await foreach (var row in StreamRowsAsync(previewRequest, objectIds, progress: null, updateCount: _ => { }, cancellationToken))
        {
            rows.Add(row);
        }

        return rows;
    }

    public async Task<AuditHistoryExtractorPro.Core.Models.ExtractionResult> ExtractAuditHistoryAsync(
        ExtractionRequest request,
        string outputFilePath,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        return await Task.Run(async () =>
        {
            if (_serviceClient is null || !_serviceClient.IsReady)
            {
                return AuditHistoryExtractorPro.Core.Models.ExtractionResult.Fail("No hay conexión activa a Dataverse.");
            }

            var filePath = ResolveOutputPath(outputFilePath, request.EntityName);
            var directory = Path.GetDirectoryName(filePath);
            if (string.IsNullOrWhiteSpace(directory))
            {
                return AuditHistoryExtractorPro.Core.Models.ExtractionResult.Fail("La ruta de salida no es válida.");
            }

            Directory.CreateDirectory(directory);
            if (!filePath.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase))
            {
                filePath = Path.ChangeExtension(filePath, ".xlsx");
            }

            // Si los metadatos de la entidad no se pueden cargar (ej. solución desinstalada),
            // se continúa sin pre-calentamiento; StreamRowsAsync gestionará los fallos por fila.
            try
            {
                await LoadEntityMetadataContextAsync(request.EntityName, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception metaEx)
            {
                _logger.LogWarning(metaEx,
                    "[ExtractAuditHistoryAsync] Metadatos '{EntityName}' no disponibles; continuando sin pre-calentamiento",
                    request.EntityName);
            }

            progress?.Report("Iniciando extracción de auditoría...");

            // Paso 1: resolver IDs desde la Vista seleccionada (si aplica)
            var objectIds = await ResolveViewObjectIdsAsync(request.SelectedView, cancellationToken);
            if (request.SelectedView is not null && objectIds.Count == 0)
            {
                // La Vista no devuelve ningún registro → no hay auditoría posible
                progress?.Report("La Vista seleccionada no devuelve registros. Extracción finalizada.");
                return AuditHistoryExtractorPro.Core.Models.ExtractionResult.Ok(0, filePath, "La Vista seleccionada no devuelve registros.");
            }

            var totalWritten = 0;
            // Paso 2 & 3: streaming paginado sobre 'audit', con RetrieveAuditDetailsRequest por fila
            IAsyncEnumerable<AuditExportRow> asyncRows;
            if (objectIds.Count == 0)
            {
                asyncRows = StreamRowsAsync(request, null, progress, count => totalWritten = count, cancellationToken);
            }
            else
            {
                asyncRows = StreamAllChunksAsync(request, objectIds, progress, count => totalWritten = count, cancellationToken);
            }

            await _excelExportService.ExportAsync(filePath, asyncRows, cancellationToken);

            progress?.Report($"Extracción completada. Total: {totalWritten} registros.");
            return AuditHistoryExtractorPro.Core.Models.ExtractionResult.Ok(totalWritten, filePath, $"Extracción completada. Archivo generado en: {filePath}");
        }, cancellationToken);
    }

    private static string ResolveOutputPath(string outputFilePath, string entityName)
    {
        if (!string.IsNullOrWhiteSpace(outputFilePath))
        {
            return outputFilePath;
        }

        var safeEntity = string.IsNullOrWhiteSpace(entityName) ? "audit" : entityName.Trim();
        var fileName = $"audit_{safeEntity}_{DateTime.UtcNow:yyyyMMdd_HHmmss}.csv";
        return Path.Combine(Path.GetTempPath(), "AuditHistoryExtractorPro", "exports", fileName);
    }

    private async IAsyncEnumerable<AuditExportRow> StreamRowsAsync(
        ExtractionRequest request,
        IReadOnlyList<Guid>? objectIds,
        IProgress<string>? progress,
        Action<int> updateCount,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var totalWritten = 0;
        var pageNumber = 1;
        var moreRecords = true;
        string? pagingCookie = null;

        var filters = new AuditQueryFilters
        {
            EntityName = request.EntityName,
            SelectedDateRange = request.SelectedDateRange,
            SelectedDateFrom = request.SelectedDateFrom,
            SelectedDateTo = request.SelectedDateTo,
            IsFullDay = request.IsFullDay,
            SelectedUser = request.SelectedUser,
            SelectedOperation = request.SelectedOperation,
            SelectedOperations = request.SelectedOperations,
            SelectedActions = request.SelectedActions,
            SelectedAttributes = request.SelectedAttributes,
            SearchValue = request.SearchValue,
            RecordId = request.RecordId,
            StartDate = request.StartDate,
            EndDate = request.EndDate,
            ObjectIds = objectIds ?? Array.Empty<Guid>()
        };

        var selectedAttributes = new HashSet<string>(request.SelectedAttributes, StringComparer.OrdinalIgnoreCase);
        var searchValue = request.SearchValue?.Trim() ?? string.Empty;

        while (moreRecords && !cancellationToken.IsCancellationRequested && totalWritten < request.MaxRecords)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var remaining = request.MaxRecords - totalWritten;
            var pageSize = Math.Min(MaxDataversePageSize, remaining);

            progress?.Report($"Consultando página {pageNumber}...");

            _currentProgress = progress;
            var page = await FetchPageWithFallbackAsync(
                filters, pageNumber, pagingCookie, pageSize, progress, cancellationToken);

            if (page.Entities.Count == 0)
            {
                if (!page.MoreRecords)
                {
                    yield break;
                }
                // La página quedó vacía (todos los registros eran corruptos)
                // pero hay más páginas: avanzamos sin detener el proceso.
                moreRecords = true;
                pageNumber++;
                pagingCookie = page.NextPagingCookie;
                continue;
            }

            var startIndex = totalWritten + 1;

            // ── Paso 3: Iterar cada entidad → llamar RetrieveAuditDetailsRequest ─────
            var stopped = false;
            foreach (var entity in page.Entities)
            {
                if (stopped || totalWritten >= request.MaxRecords) break;
                cancellationToken.ThrowIfCancellationRequested();

                // ── Segunda línea de defensa: las filas se recopilan en lista dentro
                // del try-catch, y se emiten fuera de él.
                // En C# yield return no está permitido dentro de un bloque try-catch,
                // por lo que este patrón buffer→emit garantiza que NINGUNA fila se
                // pierda aunque BuildDetailRowsAsync lance inesperadamente.
                var entityRows = new List<AuditExportRow>(4);
                try
                {
                    await foreach (var detailRow in BuildDetailRowsAsync(entity, selectedAttributes, cancellationToken))
                    {
                        if (MatchesSearchValue(detailRow, searchValue))
                            entityRows.Add(detailRow);
                    }
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception entityEx)
                {
                    var rawAuditId = entity.Contains("auditid")
                        ? entity.GetAttributeValue<Guid>("auditid").ToString()
                        : entity.Id.ToString();
                    _logger.LogWarning(entityEx,
                        "[StreamRows] Error no capturado en BuildDetailRowsAsync auditid={AuditId} — emitiendo fila de rescate",
                        rawAuditId);
                    if (entityRows.Count == 0)
                    {
                        entityRows.Add(new AuditExportRow
                        {
                            AuditId       = rawAuditId,
                            CreatedOn     = string.Empty,
                            EntityName    = "[Error Interno — ver log]",
                            RecordId      = string.Empty,
                            LogicalName   = string.Empty,
                            RecordUrl     = string.Empty,
                            ActionCode    = 0,
                            ActionName    = "[Error Interno]",
                            UserId        = string.Empty,
                            UserName      = "[Error Interno]",
                            RealActor     = "[Error Interno]",
                            TransactionId = string.Empty,
                            ChangedField  = "[Error Interno]",
                            OldValue      = "[Error al mapear — ver log]",
                            NewValue      = $"[{entityEx.GetType().Name}: {entityEx.Message}]"
                        });
                    }
                }

                // Emitir fuera del bloque try-catch (yield not allowed in try-catch in C#).
                foreach (var row in entityRows)
                {
                    totalWritten++;
                    updateCount(totalWritten);
                    yield return row;

                    if (totalWritten >= request.MaxRecords)
                    {
                        stopped = true;
                        break;
                    }
                }

                if (stopped) break;
            }
            if (stopped) yield break;

            progress?.Report($"Escribiendo registros {startIndex}-{totalWritten}...");

            moreRecords = page.MoreRecords && totalWritten < request.MaxRecords;
            if (moreRecords)
            {
                pageNumber++;
                pagingCookie = page.NextPagingCookie;
            }
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // FALLBACK PAGING
    // Encapsula el resultado de una página (entidades + cursor + flag MoreRecords).
    // ─────────────────────────────────────────────────────────────────────────
    private readonly record struct PageResult(
        IReadOnlyList<Entity> Entities,
        string? NextPagingCookie,
        bool MoreRecords);

    /// <summary>
    /// Intenta obtener una página completa de registros de auditoría usando
    /// <see cref="RetrieveMultipleRequest"/> vía Execute (evita validaciones del
    /// MetadataCache). Si la página falla por un registro corrupto, activa el
    /// modo de rescate: itera registro a registro (pageSize=1) para recuperar
    /// los registros sanos e ignorar los corruptos sin abortar la extracción.
    /// </summary>
    private async Task<PageResult> FetchPageWithFallbackAsync(
        AuditQueryFilters filters,
        int pageNumber,
        string? pagingCookie,
        int pageSize,
        IProgress<string>? progress,
        CancellationToken cancellationToken)
    {
        // ── RUTA PRINCIPAL ──────────────────────────────────────────────────────────
        // Usando Execute(RetrieveMultipleRequest) en lugar de RetrieveMultiple:
        // en algunas versiones del SDK, Execute omite las validaciones agresivas
        // del MetadataCache del cliente que causan FaultException en entidades
        // con LogicalNames corruptos (ej. "Concepto Factura" con espacios).
        var query = _queryBuilderService.BuildQueryExpression(
            filters, pageNumber, pagingCookie, pageSize);

        // ── TELEMETRÍA: imprimir la consulta como FetchXML antes de enviarla ──────
        try
        {
            var toFetchReq = new QueryExpressionToFetchXmlRequest { Query = query };
            var toFetchRes = (QueryExpressionToFetchXmlResponse)_serviceClient!.Execute(toFetchReq);
            _logger.LogDebug(
                "[FetchPage] Pág={Page} PageSize={PageSize} FetchXML=\n{FetchXml}",
                pageNumber, pageSize, toFetchRes.FetchXml);
        }
        catch (Exception telEx)
        {
            _logger.LogDebug(telEx, "[FetchPage] No se pudo convertir QueryExpression a FetchXML (solo diagnóstico)");
        }
        // ─────────────────────────────────────────────────────────────────────

        try
        {
            var ec = await ExecuteWithRetryAsync(
                () => Task.Run(() =>
                {
                    var req = new RetrieveMultipleRequest { Query = query };
                    var res = (RetrieveMultipleResponse)_serviceClient!.Execute(req);
                    return res.EntityCollection;
                }, cancellationToken),
                cancellationToken);

            return new PageResult(
                ec.Entities.Cast<Entity>().ToList(),
                ec.PagingCookie,
                ec.MoreRecords);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception pageEx)
        {
            // ── RUTA DE RESCATE: Fallback Paging (1 registro por consulta) ────
            // La página completa falló (típicamente por MetadataCache corrupto).
            // Iteramos registro a registro para recuperar los sanos y saltar
            // los corruptos de forma silenciosa, preservando la paginación.
            progress?.Report(
                $"[Rescate] Página {pageNumber} falló ({pageEx.GetType().Name}). " +
                $"Activando modo 1 registro/consulta ({pageSize} intentos)...");
            _logger.LogWarning(pageEx,
                "[FetchPage] Página {PageNumber} falló — activando Fallback Paging 1x1 ({PageSize} intentos)",
                pageNumber, pageSize);

            var recovered   = new List<Entity>(pageSize);
            string? subCookie  = pagingCookie;
            bool   subMore     = true;
            int    subPage     = pageNumber;
            int    attempts    = 0;

            while (subMore && attempts < pageSize && !cancellationToken.IsCancellationRequested)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var subQuery = _queryBuilderService.BuildQueryExpression(
                    filters, subPage, subCookie, 1);
                try
                {
                    var subEc = await Task.Run(() =>
                    {
                        var req = new RetrieveMultipleRequest { Query = subQuery };
                        var res = (RetrieveMultipleResponse)_serviceClient!.Execute(req);
                        return res.EntityCollection;
                    }, cancellationToken);

                    if (subEc.Entities.Count > 0)
                    {
                        recovered.Add(subEc.Entities[0]);
                    }

                    subMore   = subEc.MoreRecords;
                    subCookie = subEc.PagingCookie;
                    subPage++;
                    attempts++;
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception subEx)
                {
                    _logger.LogWarning(subEx,
                        "[FetchPage] Registro {Attempt} en pág {SubPage} ignorado (corrompido)",
                        attempts + 1, subPage);
                    progress?.Report(
                        $"  R{subPage}: registro corrupto ignorado. Reanudando desde cursor anterior...");
                    break;
                }
            }

            return new PageResult(
                recovered,
                subCookie,
                subMore && attempts >= pageSize);
        }
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // HELPER: Aísla cada acceso al diccionario de atributos del SDK.
    // GetAttributeValue<T> puede lanzar FaultException / NullReferenceException
    // cuando el MetadataCache interno del SDK tiene entradas corruptas.
    // Este helper garantiza que NUNCA se propague una excepción al llamador.
    // ─────────────────────────────────────────────────────────────────────────────
    private T? SafeGet<T>(Entity entity, string attributeName)
    {
        try
        {
            return entity.GetAttributeValue<T>(attributeName);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "[SafeGet] Error leyendo atributo '{Attribute}'", attributeName);
            return default;
        }
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // HELPER: Resolución async de nombre con aislamiento total.
    // Cada llamada al SDK que intenta resolver un Lookup / PrimaryName se envuelve
    // en su propio try-catch para no interrumpir el procesamiento de la fila.
    // ─────────────────────────────────────────────────────────────────────────────
    private async Task<string> SafeResolveNameIfReferenceAsync(
        string value, string fieldName, CancellationToken cancellationToken)
    {
        try
        {
            return await ResolveNameIfReferenceAsync(value, fieldName, cancellationToken);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "[SafeResolveNameIfReference] Campo '{Field}'", fieldName);
            return "[Datos Corruptos en Dataverse]";
        }
    }

    private async Task<string?> SafeResolveEntityPrimaryNameAsync(
        string entityLogicalName, Guid id, CancellationToken cancellationToken)
    {
        try
        {
            return await ResolveEntityPrimaryNameAsync(entityLogicalName, id, cancellationToken);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "[SafeResolveEntityPrimaryName] '{Entity}' {Id}", entityLogicalName, id);
            return "[Datos Corruptos en Dataverse]";
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // PASO 3 — Motor central: un registro audit → N filas (una por campo cambiado)
    // Usa RetrieveAuditDetailsRequest para obtener OldValue/NewValue reales.
    // Si la llamada falla (registro borrado, FaultException), degrada al XML
    // de changedata como fallback para no perder ninguna fila.
    // ─────────────────────────────────────────────────────────────────────────
    private async IAsyncEnumerable<AuditExportRow> BuildDetailRowsAsync(
        Entity auditEntity,
        HashSet<string> selectedAttributes,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        // ── Cabecera del registro de auditoría (lectura segura) ──────────────────
        var auditId        = SafeGet<Guid>(auditEntity, "auditid");
        var createdOn      = SafeGet<DateTime>(auditEntity, "createdon");
        var objectRef      = SafeGet<EntityReference>(auditEntity, "objectid");
        var actionOptSet   = SafeGet<OptionSetValue>(auditEntity, "action");
        var userRef        = SafeGet<EntityReference>(auditEntity, "userid");
        var callingUserRef = SafeGet<EntityReference>(auditEntity, "callinguserid");
        var transactionId  = SafeGet<Guid?>(auditEntity, "transactionid");

        // GUARD: objectid corrupto (Guid.Empty) → fila de diagnóstico
        if (objectRef != null && (objectRef.Id == Guid.Empty || string.IsNullOrWhiteSpace(objectRef.LogicalName)))
        {
            yield return new AuditExportRow
            {
                AuditId       = auditId.ToString(),
                CreatedOn     = createdOn == default ? string.Empty : createdOn.ToUniversalTime().ToString("O"),
                EntityName    = "[Registro No Encontrado o Eliminado]",
                RecordId      = "[Guid.Empty]",
                LogicalName   = "[Registro No Encontrado o Eliminado]",
                RecordUrl     = string.Empty,
                ActionCode    = actionOptSet?.Value ?? 0,
                ActionName    = "[Referencia Corrupta - Guid.Empty]",
                UserId        = string.Empty,
                UserName      = "[Registro No Encontrado o Eliminado]",
                RealActor     = "[Registro No Encontrado o Eliminado]",
                TransactionId = string.Empty,
                ChangedField  = string.Empty,
                OldValue      = "[Registro No Encontrado o Eliminado]",
                NewValue      = "[Registro No Encontrado o Eliminado]"
            };
            yield break;
        }

        // GUARD: userid/callinguserid vacíos → no llamar al SDK con Guid.Empty
        if (userRef is not null && userRef.Id == Guid.Empty)
            auditEntity.Attributes.Remove("userid");
        if (callingUserRef is not null && callingUserRef.Id == Guid.Empty)
            auditEntity.Attributes.Remove("callinguserid");
        // Re-leer tras limpieza
        userRef        = SafeGet<EntityReference>(auditEntity, "userid");
        callingUserRef = SafeGet<EntityReference>(auditEntity, "callinguserid");

        var logicalName  = objectRef?.LogicalName ?? SafeGet<string>(auditEntity, "objecttypecode") ?? string.Empty;
        var recordId     = objectRef?.Id.ToString("D") ?? string.Empty;
        var actionCode   = actionOptSet?.Value ?? 0;
        var auditIdStr   = auditId != Guid.Empty ? auditId.ToString() : string.Empty;
        var createdOnStr = createdOn == default ? string.Empty : createdOn.ToUniversalTime().ToString("O");
        var txIdStr      = transactionId?.ToString() ?? string.Empty;
        var userIdStr    = userRef?.Id.ToString() ?? string.Empty;
        var recordUrl    = BuildRecordUrl(logicalName, recordId);

        // ── Buffer de filas: se rellenan en el try y se emiten SIEMPRE al final ──
        // Garantía estructural: por más que falle la resolución de metadatos,
        // Lookups, traducciones de OptionSet, etc., la línea
        //   foreach (var row in rows) yield return row
        // se ejecuta SIEMPRE — ningún registro descargado se descarta.
        var rows = new List<AuditExportRow>(4);

        try
        {
            // Resolución de usuario
            var userName  = await ResolveUserAsync(userRef, cancellationToken);
            var realActor = await ResolveRealActorAsync(userRef, callingUserRef, userName, cancellationToken);

            // ── RetrieveAuditDetailsRequest: obtener OldValue/NewValue reales ─────
            AttributeAuditDetail? detail = null;
            try
            {
                if (auditId != Guid.Empty && _serviceClient is not null)
                {
                    var detailReq  = new RetrieveAuditDetailsRequest { AuditId = auditId };
                    var detailResp = (RetrieveAuditDetailsResponse)await Task.Run(
                        () => _serviceClient.Execute(detailReq), cancellationToken);
                    detail = detailResp.AuditDetail as AttributeAuditDetail;
                }
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "[BuildDetailRows] RetrieveAuditDetailsRequest falló auditid={AuditId} — usando fallback changedata",
                    auditId);
            }

            if (detail is not null)
            {
                // ── Recopilar todos los atributos cambiados ──────────────────────
                var changedAttrs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                if (detail.OldValue?.Attributes is { Count: > 0 } oldA)
                    foreach (var k in oldA.Keys) changedAttrs.Add(k);
                if (detail.NewValue?.Attributes is { Count: > 0 } newA)
                    foreach (var k in newA.Keys) changedAttrs.Add(k);

                // Aplicar filtro de atributos seleccionados por el usuario
                if (selectedAttributes.Count > 0)
                    changedAttrs.IntersectWith(selectedAttributes);

                if (changedAttrs.Count == 0)
                {
                    // Evento sin campos detallados (Create sin tracking, etc.)
                    rows.Add(new AuditExportRow
                    {
                        AuditId = auditIdStr, CreatedOn = createdOnStr, EntityName = logicalName,
                        RecordId = recordId, LogicalName = logicalName, RecordUrl = recordUrl,
                        ActionCode = actionCode, ActionName = GetOperationName(actionCode),
                        UserId = userIdStr, UserName = userName, RealActor = realActor,
                        TransactionId = txIdStr, ChangedField = string.Empty,
                        OldValue = string.Empty, NewValue = string.Empty
                    });
                }
                else
                {
                    // Una fila por cada atributo cambiado
                    foreach (var attrName in changedAttrs)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        var oldVal = TranslateAuditAttributeValue(attrName,
                            detail.OldValue?.Contains(attrName) == true
                                ? detail.OldValue.GetAttributeValue<object>(attrName) : null);
                        var newVal = TranslateAuditAttributeValue(attrName,
                            detail.NewValue?.Contains(attrName) == true
                                ? detail.NewValue.GetAttributeValue<object>(attrName) : null);

                        rows.Add(new AuditExportRow
                        {
                            AuditId = auditIdStr, CreatedOn = createdOnStr, EntityName = logicalName,
                            RecordId = recordId, LogicalName = logicalName, RecordUrl = recordUrl,
                            ActionCode = actionCode, ActionName = GetOperationName(actionCode),
                            UserId = userIdStr, UserName = userName, RealActor = realActor,
                            TransactionId = txIdStr, ChangedField = attrName,
                            OldValue = oldVal, NewValue = newVal
                        });
                    }
                }
            }
            else
            {
                // ── Fallback: parsear el XML de changedata ───────────────────────
                var changeData = SafeGet<string>(auditEntity, "changedata") ?? string.Empty;
                var parsed     = ParseChangeData(changeData);

                if (selectedAttributes.Count > 0
                    && !string.IsNullOrEmpty(parsed.field)
                    && !selectedAttributes.Contains(parsed.field))
                {
                    // Campo excluido por filtro de atributos — no se descarta
                    // el registro; simplemente no produce fila (filtro explícito del usuario).
                }
                else
                {
                    var oldVal = string.IsNullOrEmpty(parsed.oldValue) ? string.Empty
                        : await SafeResolveNameIfReferenceAsync(parsed.oldValue, parsed.field, cancellationToken);
                    var newVal = string.IsNullOrEmpty(parsed.newValue) ? string.Empty
                        : await SafeResolveNameIfReferenceAsync(parsed.newValue, parsed.field, cancellationToken);

                    rows.Add(new AuditExportRow
                    {
                        AuditId = auditIdStr, CreatedOn = createdOnStr, EntityName = logicalName,
                        RecordId = recordId, LogicalName = logicalName, RecordUrl = recordUrl,
                        ActionCode = actionCode, ActionName = GetOperationName(actionCode),
                        UserId = userIdStr, UserName = userName, RealActor = realActor,
                        TransactionId = txIdStr, ChangedField = parsed.field,
                        OldValue = oldVal, NewValue = newVal
                    });
                }
            }
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception outerEx)
        {
            // Metadatos rotos, Lookup eliminado, excepción de traducción, etc.
            // Si el try ya añadió filas parciales las conservamos; si está vacío
            // emitimos una fila de diagnóstico para que el registro sea VISIBLE.
            _logger.LogWarning(outerEx,
                "[BuildDetailRows] Error inesperado procesando auditid={AuditId} entidad='{Entity}' — emitiendo fila de diagnóstico",
                auditId, logicalName);

            if (rows.Count == 0)
            {
                rows.Add(new AuditExportRow
                {
                    AuditId       = auditIdStr,
                    CreatedOn     = createdOnStr,
                    EntityName    = string.IsNullOrEmpty(logicalName) ? "[Entidad Desconocida]" : logicalName,
                    RecordId      = recordId,
                    LogicalName   = logicalName,
                    RecordUrl     = string.Empty,
                    ActionCode    = actionCode,
                    ActionName    = GetOperationName(actionCode),
                    UserId        = string.Empty,
                    UserName      = "[Metadatos No Disponibles]",
                    RealActor     = "[Metadatos No Disponibles]",
                    TransactionId = txIdStr,
                    ChangedField  = "[Error al mapear campos]",
                    OldValue      = "[Metadatos Rotos — ver log]",
                    NewValue      = $"[{outerEx.GetType().Name}]"
                });
            }
        }

        // ── Garantía de emisión ─────────────────────────────────────────────────
        // Esta sección se ejecuta SIEMPRE, independientemente de lo que ocurrió
        // en el bloque try-catch. yield return no está permitido en catch, por lo
        // que la emisión ocurre aquí, una vez que el flujo de control es seguro.
        foreach (var row in rows)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return row;
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Traducción de valores de atributo obtenidos de AttributeAuditDetail
    // ─────────────────────────────────────────────────────────────────────────
    private string TranslateAuditAttributeValue(string attributeName, object? value)
    {
        try
        {
            return value switch
            {
                null                      => string.Empty,
                string s                  => s,
                bool b                    => b ? "Sí" : "No",
                OptionSetValue osv        => _optionSetCache.TryGetValue(attributeName, out var labels)
                                               && labels.TryGetValue(osv.Value, out var lbl)
                                               ? lbl : osv.Value.ToString(),
                OptionSetValueCollection mc => string.Join("; ", mc.Select(o =>
                                               _optionSetCache.TryGetValue(attributeName, out var labels)
                                               && labels.TryGetValue(o.Value, out var lbl) ? lbl : o.Value.ToString())),
                EntityReference er        => !string.IsNullOrWhiteSpace(er.Name) ? er.Name : er.Id.ToString("D"),
                EntityCollection ec       => string.Join("; ", ec.Entities.Select(e => e.Id.ToString("D"))),
                DateTime dt               => dt.ToUniversalTime().ToString("O"),
                Money m                   => m.Value.ToString("F2", CultureInfo.InvariantCulture),
                decimal d                 => d.ToString("F4", CultureInfo.InvariantCulture),
                double d                  => d.ToString("F4", CultureInfo.InvariantCulture),
                _                         => value.ToString() ?? string.Empty
            };
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex,
                "[TranslateAuditAttributeValue] Atributo '{Attribute}'",
                attributeName);
            return value?.ToString() ?? string.Empty;
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Resolución de nombre de usuario (usuario que realizó la acción)
    // ─────────────────────────────────────────────────────────────────────────
    private async Task<string> ResolveUserAsync(EntityReference? userRef, CancellationToken cancellationToken)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(userRef?.Name)) return userRef!.Name;
            if (userRef?.Id is Guid uid && uid != Guid.Empty)
                return await SafeResolveEntityPrimaryNameAsync("systemuser", uid, cancellationToken) ?? string.Empty;
            return string.Empty;
        }
        catch (OperationCanceledException) { throw; }
        catch { return "[Datos Corruptos en Dataverse]"; }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Resolución del actor real (puede ser distinto si se usa impersonación)
    // ─────────────────────────────────────────────────────────────────────────
    private async Task<string> ResolveRealActorAsync(
        EntityReference? userRef,
        EntityReference? callingUserRef,
        string userName,
        CancellationToken cancellationToken)
    {
        try
        {
            if (callingUserRef?.Id is Guid callingId
                && callingId != Guid.Empty
                && callingId != (userRef?.Id ?? Guid.Empty))
            {
                var callerName = !string.IsNullOrWhiteSpace(callingUserRef.Name)
                    ? callingUserRef.Name
                    : await SafeResolveEntityPrimaryNameAsync("systemuser", callingId, cancellationToken)
                        ?? callingId.ToString("D");
                return string.IsNullOrWhiteSpace(userName)
                    ? $"(via {callerName})"
                    : $"{userName} (via {callerName})";
            }
            return userName;
        }
        catch (OperationCanceledException) { throw; }
        catch { return string.IsNullOrWhiteSpace(userName) ? "[Datos Corruptos en Dataverse]" : userName; }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // PASO 1 — Resolución de Vista: ejecuta el FetchXML de la Vista y devuelve
    // la lista de IDs de los registros que coinciden con sus filtros.
    // Vacío si no hay Vista seleccionada o si el FetchXML está en blanco.
    // ─────────────────────────────────────────────────────────────────────────
    private async Task<IReadOnlyList<Guid>> ResolveViewObjectIdsAsync(
        ViewDTO? view,
        CancellationToken cancellationToken)
    {
        if (view is null || string.IsNullOrWhiteSpace(view.FetchXml) || _serviceClient is null)
            return Array.Empty<Guid>();

        var ids       = new List<Guid>();
        var page      = 1;
        string? cookie = null;
        bool   more   = true;

        while (more)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var paged = InjectPagingIntoFetchXml(view.FetchXml, page, cookie);
            try
            {
                var ec = await ExecuteWithRetryAsync(
                    () => Task.Run(() =>
                    {
                        var req = new RetrieveMultipleRequest { Query = new FetchExpression(paged) };
                        return ((RetrieveMultipleResponse)_serviceClient.Execute(req)).EntityCollection;
                    }, cancellationToken),
                    cancellationToken);

                foreach (var e in ec.Entities)
                    if (e.Id != Guid.Empty) ids.Add(e.Id);

                more   = ec.MoreRecords;
                cookie = ec.PagingCookie;
                page++;
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "[ResolveViewObjectIds] Error en pág {Page} del FetchXML de Vista; retornando IDs parciales",
                    page);
                break; // Retornar los IDs recuperados hasta el momento
            }
        }

        return ids;
    }

    private static string InjectPagingIntoFetchXml(string fetchXml, int page, string? pagingCookie)
    {
        try
        {
            var doc   = XDocument.Parse(fetchXml);
            var fetch = doc.Root!;
            fetch.SetAttributeValue("page",  page.ToString());
            fetch.SetAttributeValue("count", "5000");
            if (!string.IsNullOrEmpty(pagingCookie))
                fetch.SetAttributeValue("paging-cookie", pagingCookie);
            return doc.ToString(SaveOptions.DisableFormatting);
        }
        catch { return fetchXml; }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // PASO 2 — Chunked streaming: divide la lista de IDs en lotes de 500 para
    // no exceder el límite de condición IN en Dataverse, y encadena los streams.
    // ─────────────────────────────────────────────────────────────────────────
    private async IAsyncEnumerable<AuditExportRow> StreamAllChunksAsync(
        ExtractionRequest request,
        IReadOnlyList<Guid> objectIds,
        IProgress<string>? progress,
        Action<int> updateCount,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        const int ChunkSize = 500;
        var chunks      = ChunkList(objectIds, ChunkSize);
        var totalChunks = chunks.Count;
        var totalSoFar  = 0;

        for (var ci = 0; ci < totalChunks && totalSoFar < request.MaxRecords; ci++)
        {
            progress?.Report($"Procesando lote {ci + 1}/{totalChunks} ({chunks[ci].Count} registros de la Vista)...");

            var remaining    = request.MaxRecords - totalSoFar;
            var chunkRequest = new ExtractionRequest
            {
                EntityName         = request.EntityName,
                MaxRecords         = remaining,
                SelectedDateRange  = request.SelectedDateRange,
                SelectedDateFrom   = request.SelectedDateFrom,
                SelectedDateTo     = request.SelectedDateTo,
                IsFullDay          = request.IsFullDay,
                SelectedUser       = request.SelectedUser,
                SelectedOperations = request.SelectedOperations,
                SelectedActions    = request.SelectedActions,
                SelectedAttributes = request.SelectedAttributes,
                SearchValue        = request.SearchValue,
                StartDate          = request.StartDate,
                EndDate            = request.EndDate
            };

            var chunkWritten = 0;
            await foreach (var row in StreamRowsAsync(chunkRequest, chunks[ci], progress,
                count => { chunkWritten = count; updateCount(totalSoFar + count); },
                cancellationToken))
            {
                yield return row;
            }
            totalSoFar += chunkWritten;
        }
    }

    private static List<IReadOnlyList<Guid>> ChunkList(IReadOnlyList<Guid> source, int chunkSize)
    {
        var result = new List<IReadOnlyList<Guid>>();
        for (var i = 0; i < source.Count; i += chunkSize)
            result.Add(source.Skip(i).Take(chunkSize).ToList());
        return result;
    }

    private string BuildRecordUrl(string logicalName, string recordId)
    {
        if (string.IsNullOrWhiteSpace(logicalName) || string.IsNullOrWhiteSpace(recordId))
        {
            return string.Empty;
        }

        var baseUrl = _authenticationConfiguration?.EnvironmentUrl?.TrimEnd('/');
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            return string.Empty;
        }

        return $"{baseUrl}/main.aspx?etn={WebUtility.UrlEncode(logicalName)}&id={WebUtility.UrlEncode(recordId)}&pagetype=entityrecord";
    }

    private (string field, string oldValue, string newValue) ParseChangeData(string changeData)
    {
        if (string.IsNullOrWhiteSpace(changeData))
        {
            return (string.Empty, string.Empty, string.Empty);
        }

        try
        {
            var document = XDocument.Parse(changeData);
            var firstAttribute = document.Descendants("attribute").FirstOrDefault();
            if (firstAttribute is null)
            {
                return (string.Empty, string.Empty, string.Empty);
            }

            var fieldName = firstAttribute.Attribute("name")?.Value ?? string.Empty;
            var oldValue = firstAttribute.Element("oldValue")?.Value ?? string.Empty;
            var newValue = firstAttribute.Element("newValue")?.Value ?? string.Empty;

            return (fieldName, oldValue, newValue);
        }
        catch
        {
            return (string.Empty, string.Empty, string.Empty);
        }
    }

    private async Task LoadEntityMetadataContextAsync(
        string entityName,
        CancellationToken cancellationToken)
    {
        if (_serviceClient is null)
        {
            _optionSetCache = new Dictionary<string, Dictionary<int, string>>(StringComparer.OrdinalIgnoreCase);
            _attributeByColumnNumber = new Dictionary<int, string>();
            _lookupTargetByAttribute = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            return;
        }

        var request = new RetrieveEntityRequest
        {
            LogicalName = entityName,
            EntityFilters = EntityFilters.Attributes | EntityFilters.Entity,
            RetrieveAsIfPublished = true
        };

        // ── BLINDAJE: ExecuteAsync puede lanzar FaultException si la entidad tiene
        // metadatos corruptos en el servidor (ej. ID Guid.Empty, LogicalName con
        // espacios como "Concepto Factura", soluciones desinstaladas con restos).
        // En ese caso degradamos silenciosamente a cachés vacíos: la extracción
        // continúa y las columnas de metadatos mostrarán valores por defecto.
        // PROHIBIDO: throw / re-lanzar la excepción al método llamador.
        RetrieveEntityResponse response;
        try
        {
            response = (RetrieveEntityResponse)await _serviceClient.ExecuteAsync(request, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            // Cancelación explícita del usuario: se propaga sin silenciar.
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "[LoadEntityMetadata] Metadatos de '{EntityName}' no disponibles; usando cachés vacías",
                entityName);

            // Dejar cachés en estado vacío/seguro para esta entidad.
            _optionSetCache = new Dictionary<string, Dictionary<int, string>>(StringComparer.OrdinalIgnoreCase);
            _attributeByColumnNumber = new Dictionary<int, string>();
            _lookupTargetByAttribute = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            return;  // Continuar sin metadatos; los campos mostrarán valores raw.
        }

        _metadataTranslationService.CacheEntityMetadata(entityName, response.EntityMetadata);

        var cache = new Dictionary<string, Dictionary<int, string>>(StringComparer.OrdinalIgnoreCase);
        var attributeByColumnNumber = new Dictionary<int, string>();
        var lookupTargets = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        if (!string.IsNullOrWhiteSpace(response.EntityMetadata.PrimaryNameAttribute))
        {
            _primaryNameAttributeCache[entityName] = response.EntityMetadata.PrimaryNameAttribute;
        }

        foreach (var attribute in response.EntityMetadata.Attributes)
        {
            if (attribute.ColumnNumber.HasValue && !string.IsNullOrWhiteSpace(attribute.LogicalName))
            {
                attributeByColumnNumber[attribute.ColumnNumber.Value] = attribute.LogicalName;
            }

            if (attribute is LookupAttributeMetadata lookupAttribute
                && !string.IsNullOrWhiteSpace(attribute.LogicalName)
                && lookupAttribute.Targets is { Length: > 0 })
            {
                lookupTargets[attribute.LogicalName] = lookupAttribute.Targets[0];
            }

            if (attribute is not EnumAttributeMetadata enumAttribute)
            {
                continue;
            }

            var labels = enumAttribute.OptionSet?.Options?
                .Where(o => o.Value.HasValue)
                .ToDictionary(
                    o => o.Value!.Value,
                    o => o.Label?.UserLocalizedLabel?.Label
                        ?? o.Label?.LocalizedLabels?.FirstOrDefault()?.Label
                        ?? o.Value!.Value.ToString())
                ?? new Dictionary<int, string>();

            if (labels.Count > 0 && !string.IsNullOrWhiteSpace(enumAttribute.LogicalName))
            {
                cache[enumAttribute.LogicalName] = labels;
            }
        }

        _optionSetCache = cache;
        _attributeByColumnNumber = attributeByColumnNumber;
        _lookupTargetByAttribute = lookupTargets;
    }

    private bool ShouldIncludeRecord(
        string attributeMask,
        string changedField,
        HashSet<string> selectedAttributes)
    {
        if (selectedAttributes.Count == 0)
        {
            return true;
        }

        if (!string.IsNullOrWhiteSpace(changedField) && selectedAttributes.Contains(changedField))
        {
            return true;
        }

        if (string.IsNullOrWhiteSpace(attributeMask))
        {
            return false;
        }

        var tokens = attributeMask.Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries);
        foreach (var token in tokens)
        {
            if (!int.TryParse(token, NumberStyles.Integer, CultureInfo.InvariantCulture, out var columnNumber))
            {
                continue;
            }

            if (_attributeByColumnNumber.TryGetValue(columnNumber, out var logicalName)
                && selectedAttributes.Contains(logicalName))
            {
                return true;
            }
        }

        return false;
    }

    private async Task<string> ResolveNameIfReferenceAsync(string value, string fieldName, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return value;
        }

        var guid = ExtractGuid(value);
        if (!guid.HasValue || guid.Value == Guid.Empty)
        {
            return "[Registro Eliminado o Desconocido]";
        }

        var targetEntity = ResolveTargetEntity(fieldName);
        if (string.IsNullOrWhiteSpace(targetEntity))
        {
            return value;
        }

        try
        {
            var resolvedName = await ResolveEntityPrimaryNameAsync(targetEntity, guid.Value, cancellationToken);
            return string.IsNullOrWhiteSpace(resolvedName) ? "[Registro Eliminado o Desconocido]" : resolvedName;
        }
        catch (FaultException)
        {
            return "[Registro Eliminado o Desconocido]";
        }
        catch
        {
            return value;
        }
    }

    private string? ResolveTargetEntity(string fieldName)
    {
        if (_lookupTargetByAttribute.TryGetValue(fieldName, out var target))
        {
            return target;
        }

        return null;
    }

    private async Task<string?> ResolveEntityPrimaryNameAsync(string entityLogicalName, Guid id, CancellationToken cancellationToken)
    {
        // Guard: un Guid.Empty causaría FaultException en el SDK ("entity with id 00000000...")
        if (id == Guid.Empty)
        {
            return null;
        }

        if (_serviceClient is null || !_serviceClient.IsReady)
        {
            return null;
        }

        var cacheKey = $"{entityLogicalName}:{id:D}";
        if (_nameResolutionCache.TryGetValue(cacheKey, out var cachedName))
        {
            return cachedName;
        }

        await _nameResolutionLock.WaitAsync(cancellationToken);
        try
        {
            if (_nameResolutionCache.TryGetValue(cacheKey, out cachedName))
            {
                return cachedName;
            }

            var primaryNameAttribute = await GetPrimaryNameAttributeAsync(entityLogicalName, cancellationToken);
            if (string.IsNullOrWhiteSpace(primaryNameAttribute))
            {
                return null;
            }

            var entity = await Task.Run(() => _serviceClient.Retrieve(entityLogicalName, id, new ColumnSet(primaryNameAttribute)), cancellationToken);
            var resolvedName = entity.GetAttributeValue<string>(primaryNameAttribute);
            if (!string.IsNullOrWhiteSpace(resolvedName))
            {
                _nameResolutionCache[cacheKey] = resolvedName;
            }

            return resolvedName;
        }
        catch
        {
            return null;
        }
        finally
        {
            _nameResolutionLock.Release();
        }
    }

    private async Task<string?> GetPrimaryNameAttributeAsync(string entityLogicalName, CancellationToken cancellationToken)
    {
        if (_primaryNameAttributeCache.TryGetValue(entityLogicalName, out var cachedPrimaryName))
        {
            return cachedPrimaryName;
        }

        if (_serviceClient is null || !_serviceClient.IsReady)
        {
            return null;
        }

        try
        {
            var request = new RetrieveEntityRequest
            {
                LogicalName = entityLogicalName,
                EntityFilters = EntityFilters.Entity,
                RetrieveAsIfPublished = true
            };

            var response = (RetrieveEntityResponse)await _serviceClient.ExecuteAsync(request, cancellationToken);
            var primaryNameAttribute = response.EntityMetadata.PrimaryNameAttribute;
            if (!string.IsNullOrWhiteSpace(primaryNameAttribute))
            {
                _primaryNameAttributeCache[entityLogicalName] = primaryNameAttribute;
                return primaryNameAttribute;
            }
        }
        catch
        {
        }

        return null;
    }

    private static Guid? ExtractGuid(string value)
    {
        if (Guid.TryParse(value.Trim('{', '}'), out var directGuid))
        {
            return directGuid;
        }

        var match = Regex.Match(value, "[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}");
        if (match.Success && Guid.TryParse(match.Value, out var embeddedGuid))
        {
            return embeddedGuid;
        }

        return null;
    }

    private static bool MatchesSearchValue(AuditExportRow row, string searchValue)
    {
        if (string.IsNullOrWhiteSpace(searchValue))
        {
            return true;
        }

        return (!string.IsNullOrWhiteSpace(row.NewValue)
                && row.NewValue.Contains(searchValue, StringComparison.OrdinalIgnoreCase))
            || (!string.IsNullOrWhiteSpace(row.OldValue)
                && row.OldValue.Contains(searchValue, StringComparison.OrdinalIgnoreCase));
    }

    private static string GetOperationName(int operationCode)
    {
        return operationCode switch
        {
            1 => "Create",
            2 => "Update",
            3 => "Delete",
            4 => "Associate",
            5 => "Disassociate",
            27 => "Archive",
            28 => "Restore",
            _ => "Unknown"
        };
    }

    // ======================== Smart Retry Policy ========================

    /// <summary>
    /// Executes a Dataverse operation with a smart retry policy that handles
    /// Throttling (429) errors, extracting Retry-After and notifying the UI.
    /// </summary>
    private async Task<T> ExecuteWithRetryAsync<T>(
        Func<Task<T>> operation,
        CancellationToken cancellationToken)
    {
        var retryPolicy = BuildThrottlingRetryPolicy<T>();
        return await retryPolicy.ExecuteAsync(async (ct) => await operation(), cancellationToken);
    }

    private AsyncRetryPolicy<T> BuildThrottlingRetryPolicy<T>()
    {
        return Policy<T>
            .Handle<FaultException>(IsThrottlingException)
            .Or<FaultException>(IsThrottlingException)
            .WaitAndRetryAsync(
                retryCount: MaxThrottlingRetries,
                sleepDurationProvider: (retryAttempt, outcome, _) =>
                {
                    var retryAfter = ExtractRetryAfterSeconds(outcome.Exception);
                    var waitSeconds = retryAfter + ExtraRetryDelaySeconds;
                    return TimeSpan.FromSeconds(waitSeconds);
                },
                onRetryAsync: (outcome, waitDuration, retryAttempt, _) =>
                {
                    var totalSeconds = (int)Math.Ceiling(waitDuration.TotalSeconds);
                    _currentProgress?.Report(
                        $"Detectado límite de velocidad API. Esperando {totalSeconds} segundos... (Reintento {retryAttempt}/{MaxThrottlingRetries})");
                    return Task.CompletedTask;
                });
    }

    /// <summary>
    /// Determines whether an exception is a Dataverse throttling (429) error.
    /// Checks the ErrorCode (-2147015902 = 0x80072326) which Dataverse uses for
    /// "Number of requests exceeded the limit", and also checks for common
    /// throttling message patterns.
    /// </summary>
    private static bool IsThrottlingException(FaultException ex)
    {
        var message = ex.Message ?? string.Empty;
        var innerMessage = ex.InnerException?.Message ?? string.Empty;

        return message.Contains("429")
            || message.Contains("Too Many Requests", StringComparison.OrdinalIgnoreCase)
            || message.Contains("Number of requests exceeded the limit", StringComparison.OrdinalIgnoreCase)
            || message.Contains("Rate limit", StringComparison.OrdinalIgnoreCase)
            || message.Contains("-2147015902")
            || innerMessage.Contains("429")
            || innerMessage.Contains("Too Many Requests", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Extracts the Retry-After value (in seconds) from the exception message.
    /// Dataverse throttling responses typically include a "Retry after {N} seconds"
    /// or "Retry-After: {N}" pattern in the error message.
    /// Returns a sensible default if the value cannot be parsed.
    /// </summary>
    private static int ExtractRetryAfterSeconds(Exception? exception)
    {
        if (exception is null)
        {
            return DefaultRetryAfterSeconds;
        }

        var message = exception.Message ?? string.Empty;

        // Pattern: "Retry after {X} seconds"
        var match = Regex.Match(message, @"[Rr]etry\s+after\s+(\d+)\s+seconds?", RegexOptions.IgnoreCase);
        if (match.Success && int.TryParse(match.Groups[1].Value, out var seconds))
        {
            return seconds;
        }

        // Pattern: "Retry-After: {N}"
        match = Regex.Match(message, @"[Rr]etry-[Aa]fter:\s*(\d+)");
        if (match.Success && int.TryParse(match.Groups[1].Value, out seconds))
        {
            return seconds;
        }

        // Check inner exception too
        if (exception.InnerException is not null)
        {
            var innerResult = ExtractRetryAfterSeconds(exception.InnerException);
            if (innerResult != DefaultRetryAfterSeconds)
            {
                return innerResult;
            }
        }

        return DefaultRetryAfterSeconds;
    }
}

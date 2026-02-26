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
    private readonly IAuditProcessingService _auditProcessingService;
    private readonly IExcelExportService _excelExportService;
    private readonly IAuditComparisonService _auditComparisonService;
    private readonly IMetadataTranslationService _metadataTranslationService;
    private readonly Microsoft.Extensions.Logging.ILogger<AuditService> _logger;
    private const int MaxThrottlingRetries = 3;
    private const int ExtraRetryDelaySeconds = 1;
    private const int DefaultRetryAfterSeconds = 5;
    private readonly SemaphoreSlim _connectionLock = new(1, 1);
    private readonly SemaphoreSlim _nameResolutionLock = new(1, 1);
    // Entidades cuya llamada RetrieveEntityRequest ya fallÃ³ en esta sesiÃ³n.
    // Cualquier llamada posterior a LoadEntityMetadataContextAsync para esa
    // entidad retorna inmediatamente sin tocar Dataverse (fallo instantÃ¡neo).
    private readonly HashSet<string> _entidadesCorruptasCache = new(StringComparer.OrdinalIgnoreCase);
    // Caché de ObjectTypeCode (entero) por nombre lógico de entidad.
    // Dataverse requiere el entero en condiciones FetchXML sobre 'audit.objecttypecode'.
    private readonly Dictionary<string, int> _entityTypeCodeCache = new(StringComparer.OrdinalIgnoreCase);
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
        IAuditProcessingService auditProcessingService,
        IExcelExportService excelExportService,
        IAuditComparisonService auditComparisonService,
        IMetadataTranslationService metadataTranslationService,
        Microsoft.Extensions.Logging.ILogger<AuditService> logger)
    {
        _authHelper = authHelper;
        _queryBuilderService = queryBuilderService;
        _auditProcessingService = auditProcessingService;
        _excelExportService = excelExportService;
        _auditComparisonService = auditComparisonService;
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

        // Filtra SOLO usuarios humanos activos:
        //   isdisabled  == false → cuenta no desactivada
        //   accessmode  == 0     → Read-Write (usuario normal)
        //                          Excluye: 1=Admin-Only, 2=Non-interactive,
        //                          3=Support User, 4=Delegated Admin
        var query = new QueryExpression("systemuser")
        {
            ColumnSet = new ColumnSet("systemuserid", "fullname"),
            Criteria = new FilterExpression(LogicalOperator.And)
            {
                Conditions =
                {
                    new ConditionExpression("isdisabled",  ConditionOperator.Equal, false),
                    new ConditionExpression("accessmode",  ConditionOperator.Equal, 0)
                }
            },
            TopCount = 500
        };

        query.Orders.Add(new OrderExpression("fullname", OrderType.Ascending));

        var users = await Task.Run(() => _serviceClient.RetrieveMultiple(query), cancellationToken);
        return users.Entities
            .Select(e => new LookupItem
            {
                Id   = e.GetAttributeValue<Guid>("systemuserid"),
                Name = e.GetAttributeValue<string>("fullname") ?? "(sin nombre)"
            })
            .Where(u => u.Id != Guid.Empty && !string.IsNullOrWhiteSpace(u.Name))
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
            SelectedView       = request.SelectedView,
            CustomFetchXml     = request.CustomFetchXml
        };

        // Paso 1: resolver IDs desde la Vista seleccionada (si aplica)
        var objectIds = await ResolveViewObjectIdsAsync(previewRequest.SelectedView, cancellationToken);
        HashSet<Guid>? viewIdsHash = null;
        if (previewRequest.SelectedView is not null)
        {
            if (objectIds.Count == 0)
            {
                return Array.Empty<AuditExportRow>();
            }

            // Pre-alocación exacta para evitar re-hashes durante la carga de IDs.
            viewIdsHash = new HashSet<Guid>(objectIds.Count, EqualityComparer<Guid>.Default);
            foreach (var objectId in objectIds)
                viewIdsHash.Add(objectId);
        }
        else if (!string.IsNullOrWhiteSpace(previewRequest.CustomFetchXml))
        {
            // Paso 1b: resolver IDs desde FetchXML manual (igual que Vista seleccionada)
            var fetchIds = await ResolveFetchXmlObjectIdsAsync(previewRequest.CustomFetchXml, cancellationToken);
            if (fetchIds.Count == 0)
            {
                return Array.Empty<AuditExportRow>();
            }

            viewIdsHash = new HashSet<Guid>(fetchIds.Count, EqualityComparer<Guid>.Default);
            foreach (var id in fetchIds)
                viewIdsHash.Add(id);
        }

        var rows = new List<AuditExportRow>(maxRows);

        IAsyncEnumerable<AuditExportRow> previewStream =
            StreamRowsAsync(previewRequest, viewIdsHash, progress: null, updateCount: _ => { }, cancellationToken);

        await foreach (var row in previewStream)
        {
            rows.Add(row);
            if (rows.Count >= maxRows) break;  // hard cap de seguridad
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
            HashSet<Guid>? viewIdsHash = null;
            if (request.SelectedView is not null && objectIds.Count == 0)
            {
                // La Vista no devuelve ningún registro → no hay auditoría posible
                progress?.Report("La Vista seleccionada no devuelve registros. Extracción finalizada.");
                return AuditHistoryExtractorPro.Core.Models.ExtractionResult.Ok(0, filePath, "La Vista seleccionada no devuelve registros.");
            }
            if (request.SelectedView is not null)
            {
                // Pre-alocación exacta para evitar re-hashes durante la carga de IDs.
                viewIdsHash = new HashSet<Guid>(objectIds.Count, EqualityComparer<Guid>.Default);
                foreach (var objectId in objectIds)
                    viewIdsHash.Add(objectId);
            }
            else if (!string.IsNullOrWhiteSpace(request.CustomFetchXml))
            {
                // Paso 1b: resolver IDs desde FetchXML manual (igual que Vista seleccionada)
                progress?.Report("Ejecutando FetchXML personalizado para obtener registros...");
                var fetchIds = await ResolveFetchXmlObjectIdsAsync(request.CustomFetchXml, cancellationToken);
                if (fetchIds.Count == 0)
                {
                    progress?.Report("El FetchXML personalizado no devuelve registros. Extracción finalizada.");
                    return AuditHistoryExtractorPro.Core.Models.ExtractionResult.Ok(0, filePath, "El FetchXML personalizado no devuelve registros.");
                }

                viewIdsHash = new HashSet<Guid>(fetchIds.Count, EqualityComparer<Guid>.Default);
                foreach (var id in fetchIds)
                    viewIdsHash.Add(id);
            }

            var totalWritten = 0;
            // Paso 2 & 3: streaming paginado sobre 'audit' + intersección en memoria por Vista.
            IAsyncEnumerable<AuditExportRow> asyncRows =
                StreamRowsAsync(request, viewIdsHash, progress, count => totalWritten = count, cancellationToken);

            AuditComparisonResult? comparisonResult = null;
            if (!string.IsNullOrWhiteSpace(request.LegacyComparisonFilePath))
            {
                progress?.Report("Materializando filas para validación contra Excel legacy...");
                var materializedRows = new List<AuditExportRow>();
                await foreach (var row in asyncRows.WithCancellation(cancellationToken))
                {
                    materializedRows.Add(row);
                }

                progress?.Report("Ejecutando cotejo QA (legacy vs actual)...");
                comparisonResult = await _auditComparisonService.CompareWithLegacyAsync(
                    request.LegacyComparisonFilePath,
                    materializedRows,
                    cancellationToken);

                asyncRows = EnumerateRowsAsync(materializedRows, cancellationToken);
            }

            await _excelExportService.ExportAsync(filePath, asyncRows, comparisonResult, cancellationToken);

            progress?.Report($"Extracción completada. Total: {totalWritten} registros.");
            return AuditHistoryExtractorPro.Core.Models.ExtractionResult.Ok(totalWritten, filePath, $"Extracción completada. Archivo generado en: {filePath}");
        }, cancellationToken);
    }

    private static async IAsyncEnumerable<AuditExportRow> EnumerateRowsAsync(
        IReadOnlyList<AuditExportRow> rows,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        foreach (var row in rows)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return row;
            await Task.Yield();
        }
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
        HashSet<Guid>? viewIdsHash,
        IProgress<string>? progress,
        Action<int> updateCount,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        // ── Modo Vista / FetchXML manual: rutear a consulta por objectid IN ─────────────
        // Cuando se dispone de un HashSet de IDs (desde Vista o FetchXML manual),
        // se usa StreamRowsByObjectIdChunksAsync que consulta audit directamente
        // por objectid IN (lotes de 500) SIN filtrar por objecttypecode.
        // Motivo: los registros de auditoría legacy pueden tener un objecttypecode
        // diferente al código actual de la entidad; filtrar por objecttypecode
        // descarta esos registros y produce un recuento incorrecto (≈50% del real).
        if (viewIdsHash != null && viewIdsHash.Count > 0)
        {
            await foreach (var row in StreamRowsByObjectIdChunksAsync(
                request, viewIdsHash, progress, updateCount, cancellationToken))
            {
                yield return row;
            }
            yield break;
        }

        var totalWritten = 0;
        var pageNumber = 1;
        string? pagingCookie = null;
        var hasMore = false;

        // Resolver el ObjectTypeCode (entero) de la entidad UNA VEZ antes del loop.
        // La tabla 'audit' almacena 'objecttypecode' como picklist (int); Dataverse
        // rechaza el nombre lógico como string con FormatException en FetchXML.
        var entityTypeCode = await ResolveEntityTypeCodeAsync(request.EntityName, cancellationToken);

        var filters = new AuditQueryFilters
        {
            EntityName = request.EntityName,
            EntityTypeCode = entityTypeCode,
            SelectedDateRange = request.SelectedDateRange,
            SelectedDateFrom = request.SelectedDateFrom,
            SelectedDateTo = request.SelectedDateTo,
            IsFullDay = request.IsFullDay,
            SelectedUser = request.SelectedUser,
            SelectedOperation = request.CompatibilityMode ? null : request.SelectedOperation,
            SelectedOperations = request.CompatibilityMode ? Array.Empty<int>() : request.SelectedOperations,
            SelectedActions = request.CompatibilityMode ? Array.Empty<int>() : request.SelectedActions,
            SelectedAttributes = request.SelectedAttributes,
            SearchValue = request.SearchValue,
            // Estrictamente prohibido enviar filtros por objectid al backend
            // cuando se usa arquitectura de intersección en memoria.
            RecordId = string.Empty,
            StartDate = request.StartDate,
            EndDate = request.EndDate,
            ObjectIds = Array.Empty<Guid>()
        };

        var selectedAttributes = new HashSet<string>(request.SelectedAttributes, StringComparer.OrdinalIgnoreCase);
        var searchValue = request.SearchValue?.Trim() ?? string.Empty;

        // ── Paginación do-while: el cursor (pagingCookie / pageNumber) se avanza SIEMPRE
        // antes de procesar entidades para garantizar que una interrupción a medio-página
        // no reutilice un cookie stale. El tamaño de página es siempre MaxDataversePageSize
        // (5000) — desacoplado del presupuesto de filas (MaxRecords), que solo controla
        // el límite de filas de SALIDA, no cuántos registros de auditoría se descargan.
        do
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (totalWritten >= request.MaxRecords) break;

            progress?.Report($"Consultando página {pageNumber}...");

            _currentProgress = progress;
            var page = await FetchPageWithFallbackAsync(
                filters, pageNumber, pagingCookie, MaxDataversePageSize, progress, cancellationToken);

            // Avanzar cursor SIEMPRE — antes de cualquier break/continue.
            hasMore      = page.MoreRecords;
            pagingCookie = page.NextPagingCookie;

            _logger.LogInformation(
                "[StreamRows] Página {Page} recuperada | Brutos={Raw} | MoreRecords={More} | TotalFilasAcum={Total}",
                pageNumber, page.Entities.Count, hasMore, totalWritten);

            var currentPage = pageNumber;
            pageNumber++;

            if (page.Entities.Count == 0)
            {
                // Página vacía (posible fallback 1×1 que saltó todos los registros).
                // El while-condition manejará la terminación si hasMore=false.
                continue;
            }

            var intersectionProgress = progress is null
                ? null
                : new Progress<int>(matched =>
                    progress.Report($"Intersección en memoria: {matched} coincidencias en página {currentPage}..."));

            var (matchedEntities, metrics) = await _auditProcessingService.IntersectPageAsync(
                page.Entities,
                viewIdsHash,
                intersectionProgress,
                cancellationToken);

            // ── Telemetría de paridad: Brutos / Pasaron HashSet / Descartados / TotalAcum ──
            _logger.LogInformation(
                "[StreamRows] Pág {Page} | Brutos={Raw} | Pasaron HashSet={Matched} | Descartados={Discarded} | TotalFilasAcum={Total}",
                currentPage, page.Entities.Count, metrics.Matched, metrics.Discarded, totalWritten);

            // ── Paso 3: Iterar cada entidad → llamar RetrieveAuditDetailsRequest ─────
            // No-destructivo: múltiples registros de auditoría para el mismo objectid
            // pasan todos — el HashSet actúa solo como validador, no como deduplicador.
            var stopped = false;
            foreach (var entity in matchedEntities)
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
                            AuditId        = rawAuditId,
                            CreatedOn      = string.Empty,
                            EntityName     = "[Error Interno — ver log]",
                            LogicalName    = string.Empty,
                            RecordId       = string.Empty,
                            RecordKeyValue = string.Empty,
                            RecordUrl      = string.Empty,
                            ActionCode     = 0,
                            ActionName     = "[Error Interno]",
                            OperationId    = 0,
                            Operation      = string.Empty,
                            UserId         = string.Empty,
                            UserName       = "[Error Interno]",
                            RealActor      = "[Error Interno]",
                            TransactionId  = string.Empty,
                            ChangedField   = "[Error Interno]",
                            OldValue       = "[Error al mapear — ver log]",
                            NewValue       = $"[{entityEx.GetType().Name}: {entityEx.Message}]",
                            LookupOldValue = string.Empty,
                            LookupNewValue = string.Empty
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

            progress?.Report($"Página {currentPage} procesada. Total acumulado: {totalWritten} filas.");
        }
        while (hasMore && totalWritten < request.MaxRecords && !cancellationToken.IsCancellationRequested);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // MODO VISTA: Consulta directa por objectid IN (paridad con app legacy)
    // Consulta audit filtrando por objectid IN (lotes de 500) en lugar de
    // objecttypecode. Esto garantiza recuperar TODOS los registros de audítoría
    // de esos objetos independientemente del código almacenado (que puede
    // diferir entre registros legados y nuevos de la misma entidad).
    // ─────────────────────────────────────────────────────────────────────────
    private async IAsyncEnumerable<AuditExportRow> StreamRowsByObjectIdChunksAsync(
        ExtractionRequest request,
        HashSet<Guid> viewIdsHash,
        IProgress<string>? progress,
        Action<int> updateCount,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var totalWritten = 0;
        var selectedAttributes = new HashSet<string>(request.SelectedAttributes, StringComparer.OrdinalIgnoreCase);
        var searchValue = request.SearchValue?.Trim() ?? string.Empty;

        // Partir los IDs de la Vista en lotes de máx. 500 (límite práctico de Dataverse IN).
        // .Chunk() es .NET 6+; dado que el proyecto es .NET 8, es seguro utilizarlo.
        var chunks = viewIdsHash.Chunk(500).ToList();

        _logger.LogInformation(
            "[StreamRowsByChunks] Vista con {Total} IDs dividida en {Chunks} lote(s) de ≤500",
            viewIdsHash.Count, chunks.Count);

        for (var chunkIdx = 0; chunkIdx < chunks.Count; chunkIdx++)
        {
            if (totalWritten >= request.MaxRecords) yield break;
            cancellationToken.ThrowIfCancellationRequested();

            var chunk = chunks[chunkIdx];
            progress?.Report($"Lote {chunkIdx + 1}/{chunks.Count} ({chunk.Length} registros)...");

            // SIN objecttypecode — se filtra por objectid IN directamente.
            // EntityName vacío = QueryBuilderService no añade condición objecttypecode.
            // ObjectIdsEntityType pasa el nombre lógico para el atributo uitype del IN,
            // necesario para resolver el lookup polimórfico 'objectid' en la tabla audit.
            var filters = new AuditQueryFilters
            {
                EntityName        = string.Empty,  // No filtrar por objecttypecode (usa objectid IN)
                EntityTypeCode    = null,
                ObjectIdsEntityType = request.EntityName,  // uitype para lookup polimórfico
                SelectedDateRange = request.SelectedDateRange,
                SelectedDateFrom  = request.SelectedDateFrom,
                SelectedDateTo    = request.SelectedDateTo,
                IsFullDay         = request.IsFullDay,
                SelectedUser      = request.SelectedUser,
                SelectedOperation  = request.CompatibilityMode ? null : request.SelectedOperation,
                SelectedOperations = request.CompatibilityMode ? Array.Empty<int>() : request.SelectedOperations,
                SelectedActions    = request.CompatibilityMode ? Array.Empty<int>() : request.SelectedActions,
                SelectedAttributes = request.SelectedAttributes,
                SearchValue        = request.SearchValue,
                RecordId           = string.Empty,
                StartDate          = request.StartDate,
                EndDate            = request.EndDate,
                ObjectIds          = chunk  // ← el filtro principal: objectid IN (chunk)
            };

            var pageNumber   = 1;
            string? pagingCookie = null;
            bool hasMore;

            do
            {
                if (totalWritten >= request.MaxRecords) yield break;
                cancellationToken.ThrowIfCancellationRequested();

                _currentProgress = progress;
                var page = await FetchPageWithFallbackAsync(
                    filters, pageNumber, pagingCookie, MaxDataversePageSize, progress, cancellationToken);

                hasMore      = page.MoreRecords;
                pagingCookie = page.NextPagingCookie;

                _logger.LogInformation(
                    "[StreamRowsByChunks] Lote={Chunk}/{Total} Pág={Page} | Brutos={Raw} | MoreRecords={More} | TotalAcum={TotalWritten}",
                    chunkIdx + 1, chunks.Count, pageNumber, page.Entities.Count, hasMore, totalWritten);

                pageNumber++;

                if (page.Entities.Count == 0) continue;

                // objectid IN ya filtra en Dataverse — no se necesita intersección en memoria.
                var stopped = false;
                foreach (var entity in page.Entities)
                {
                    if (stopped || totalWritten >= request.MaxRecords) break;
                    cancellationToken.ThrowIfCancellationRequested();

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
                            "[StreamRowsByChunks] Error en BuildDetailRowsAsync auditid={AuditId} — emitiendo fila de rescate",
                            rawAuditId);
                        if (entityRows.Count == 0)
                        {
                            entityRows.Add(new AuditExportRow
                            {
                                AuditId        = rawAuditId,
                                CreatedOn      = string.Empty,
                                EntityName     = "[Error Interno — ver log]",
                                LogicalName    = string.Empty,
                                RecordId       = string.Empty,
                                RecordKeyValue = string.Empty,
                                RecordUrl      = string.Empty,
                                ActionCode     = 0,
                                ActionName     = "[Error Interno]",
                                OperationId    = 0,
                                Operation      = string.Empty,
                                UserId         = string.Empty,
                                UserName       = "[Error Interno]",
                                RealActor      = "[Error Interno]",
                                TransactionId  = string.Empty,
                                ChangedField   = "[Error Interno]",
                                OldValue       = "[Error al mapear — ver log]",
                                NewValue       = $"[{entityEx.GetType().Name}: {entityEx.Message}]",
                                LookupOldValue = string.Empty,
                                LookupNewValue = string.Empty
                            });
                        }
                    }

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

                progress?.Report($"Lote {chunkIdx + 1}, página {pageNumber - 1} procesada. Total: {totalWritten} filas.");
            }
            while (hasMore && totalWritten < request.MaxRecords && !cancellationToken.IsCancellationRequested);
        }

        _logger.LogInformation(
            "[StreamRowsByChunks] Completado — {Total} filas extraídas de {Chunks} lote(s)",
            totalWritten, chunks.Count);
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
        // ── RUTA PRINCIPAL: FetchXML directo (FetchExpression) ──────────────────────
        // Motivo del cambio de QueryExpression a FetchExpression:
        // La tabla 'audit' almacena 'objecttypecode' como un campo picklist (entero).
        // Cuando se envía un QueryExpression con el valor string "sp_conceptofactura",
        // Dataverse NO resuelve el nombre lógico al código entero → 0 resultados.
        // Con FetchExpression, Dataverse SÍ resuelve el string al código en server-side,
        // igual que lo hace QueryExpressionToFetchXmlRequest (que mostraba el valor
        // correcto "10142" en los logs pero cuya conversión NO se reutilizaba).
        // Adicionalmente, FetchXML usa 'on-or-after'/'on-or-before' para fechas,
        // que manejan timezone mejor que 'ge'/'le' con datetime completo.
        var fetchXml = _queryBuilderService.BuildFetchXml(filters, pageNumber, pageSize, pagingCookie);

        _logger.LogDebug(
            "[FetchPage] Pág={Page} PageSize={PageSize} FetchXML=\n{FetchXml}",
            pageNumber, pageSize, fetchXml);

        try
        {
            var ec = await ExecuteWithRetryAsync(
                () => Task.Run(() =>
                {
                    var req = new RetrieveMultipleRequest { Query = new FetchExpression(fetchXml) };
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

                var subFetchXml = _queryBuilderService.BuildFetchXml(filters, subPage, 1, subCookie);
                try
                {
                    var subEc = await Task.Run(() =>
                    {
                        var req = new RetrieveMultipleRequest { Query = new FetchExpression(subFetchXml) };
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
        var actionOptSet    = SafeGet<OptionSetValue>(auditEntity, "action");
        var operationOptSet = SafeGet<OptionSetValue>(auditEntity, "operation");
        var userRef         = SafeGet<EntityReference>(auditEntity, "userid");
        var callingUserRef = SafeGet<EntityReference>(auditEntity, "callinguserid");
        var transactionId  = SafeGet<Guid?>(auditEntity, "transactionid");

        // GUARD: objectid corrupto (Guid.Empty) → fila de diagnóstico
        if (objectRef != null && (objectRef.Id == Guid.Empty || string.IsNullOrWhiteSpace(objectRef.LogicalName)))
        {
            var guardAction = actionOptSet?.Value ?? 0;
            var guardOp     = operationOptSet?.Value ?? 0;
            yield return new AuditExportRow
            {
                AuditId        = auditId.ToString(),
                CreatedOn      = createdOn == default
                    ? string.Empty
                    : createdOn.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss"),
                EntityName     = "[Registro No Encontrado o Eliminado]",
                LogicalName    = "[Registro No Encontrado o Eliminado]",
                RecordId       = "[Guid.Empty]",
                RecordKeyValue = string.Empty,
                RecordUrl      = string.Empty,
                ActionCode     = guardAction,
                ActionName     = "[Referencia Corrupta - Guid.Empty]",
                OperationId    = guardOp,
                Operation      = GetAuditOperationName(guardOp),
                UserId         = string.Empty,
                UserName       = "[Registro No Encontrado o Eliminado]",
                RealActor      = "[Registro No Encontrado o Eliminado]",
                TransactionId  = string.Empty,
                ChangedField   = string.Empty,
                OldValue       = "[Registro No Encontrado o Eliminado]",
                NewValue       = "[Registro No Encontrado o Eliminado]",
                LookupOldValue = string.Empty,
                LookupNewValue = string.Empty
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

        var logicalName    = objectRef?.LogicalName ?? SafeGet<string>(auditEntity, "objecttypecode") ?? string.Empty;
        var recordId       = objectRef?.Id.ToString("D") ?? string.Empty;
        var actionCode     = actionOptSet?.Value ?? 0;
        var operationCode  = operationOptSet?.Value ?? 0;
        // RecordKeyValue: valor primary-name del registro auditado (ej. fullname para contact).
        // Dataverse suele rellenar objectRef.Name cuando el campo primary de la entidad está indexado.
        var recordKeyValue = objectRef?.Name ?? string.Empty;
        var auditIdStr   = auditId != Guid.Empty ? auditId.ToString() : string.Empty;
        // ToLocalTime(): Dataverse devuelve createdon como UTC (Kind=Utc).
        // Lo convertimos a la zona local del usuario para que las fechas en el
        // Excel coincidan con lo que el usuario ve en la UI de Dynamics.
        var createdOnStr = createdOn == default
            ? string.Empty
            : createdOn.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");
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
                rows.AddRange(ProcessAuditToFlatList(
                    detail,
                    selectedAttributes,
                    auditIdStr,
                    createdOnStr,
                    logicalName,
                    recordId,
                    recordKeyValue,
                    recordUrl,
                    actionCode,
                    operationCode,
                    userIdStr,
                    userName,
                    realActor,
                    txIdStr));
            }
            else
            {
                // ── Fallback: parsear el XML de changedata ───────────────────────
                var changeData = SafeGet<string>(auditEntity, "changedata") ?? string.Empty;
                var parsedChanges = ParseChangeData(changeData);

                if (parsedChanges.Count == 0)
                {
                    rows.Add(new AuditExportRow
                    {
                        AuditId        = auditIdStr,
                        CreatedOn      = createdOnStr,
                        EntityName     = logicalName,
                        LogicalName    = logicalName,
                        RecordId       = recordId,
                        RecordKeyValue = recordKeyValue,
                        RecordUrl      = recordUrl,
                        ActionCode     = actionCode,
                        ActionName     = GetAuditActionName(actionCode),
                        OperationId    = operationCode,
                        Operation      = GetAuditOperationName(operationCode),
                        UserId         = userIdStr,
                        UserName       = userName,
                        RealActor      = realActor,
                        TransactionId  = txIdStr,
                        ChangedField   = string.Empty,
                        OldValue       = string.Empty,
                        NewValue       = string.Empty,
                        LookupOldValue = string.Empty,
                        LookupNewValue = string.Empty
                    });
                }
                else
                {
                    foreach (var parsed in parsedChanges)
                    {
                        cancellationToken.ThrowIfCancellationRequested();

                        if (selectedAttributes.Count > 0
                            && !string.IsNullOrEmpty(parsed.field)
                            && !selectedAttributes.Contains(parsed.field))
                        {
                            continue;
                        }

                        var oldVal = string.IsNullOrEmpty(parsed.oldValue) ? string.Empty
                            : await SafeResolveNameIfReferenceAsync(parsed.oldValue, parsed.field, cancellationToken);
                        var newVal = string.IsNullOrEmpty(parsed.newValue) ? string.Empty
                            : await SafeResolveNameIfReferenceAsync(parsed.newValue, parsed.field, cancellationToken);

                        rows.Add(new AuditExportRow
                        {
                            AuditId        = auditIdStr,
                            CreatedOn      = createdOnStr,
                            EntityName     = logicalName,
                            LogicalName    = logicalName,
                            RecordId       = recordId,
                            RecordKeyValue = recordKeyValue,
                            RecordUrl      = recordUrl,
                            ActionCode     = actionCode,
                            ActionName     = GetAuditActionName(actionCode),
                            OperationId    = operationCode,
                            Operation      = GetAuditOperationName(operationCode),
                            UserId         = userIdStr,
                            UserName       = userName,
                            RealActor      = realActor,
                            TransactionId  = txIdStr,
                            ChangedField   = parsed.field,
                            OldValue       = oldVal,
                            NewValue       = newVal,
                            LookupOldValue = oldVal,
                            LookupNewValue = newVal
                        });
                    }
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
                    AuditId        = auditIdStr,
                    CreatedOn      = createdOnStr,
                    EntityName     = string.IsNullOrEmpty(logicalName) ? "[Entidad Desconocida]" : logicalName,
                    LogicalName    = logicalName,
                    RecordId       = recordId,
                    RecordKeyValue = recordKeyValue,
                    RecordUrl      = string.Empty,
                    ActionCode     = actionCode,
                    ActionName     = GetAuditActionName(actionCode),
                    OperationId    = operationCode,
                    Operation      = GetAuditOperationName(operationCode),
                    UserId         = string.Empty,
                    UserName       = "[Metadatos No Disponibles]",
                    RealActor      = "[Metadatos No Disponibles]",
                    TransactionId  = txIdStr,
                    ChangedField   = "[Error al mapear campos]",
                    OldValue       = "[Metadatos Rotos — ver log]",
                    NewValue       = $"[{outerEx.GetType().Name}]",
                    LookupOldValue = string.Empty,
                    LookupNewValue = string.Empty
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

    private List<AuditExportRow> ProcessAuditToFlatList(
        AttributeAuditDetail detail,
        HashSet<string> selectedAttributes,
        string auditId,
        string createdOn,
        string entityName,
        string recordId,
        string recordKeyValue,
        string recordUrl,
        int actionCode,
        int operationCode,
        string userId,
        string username,
        string realActor,
        string transactionId)
    {
        // ── Recopilar todos los atributos cambiados ───────────────────────────
        var changedAttrs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (detail.OldValue?.Attributes is { Count: > 0 } oldA)
            foreach (var k in oldA.Keys) changedAttrs.Add(k);
        if (detail.NewValue?.Attributes is { Count: > 0 } newA)
            foreach (var k in newA.Keys) changedAttrs.Add(k);

        if (selectedAttributes.Count > 0)
            changedAttrs.IntersectWith(selectedAttributes);

        if (changedAttrs.Count == 0)
        {
            return new List<AuditExportRow>
            {
                new()
                {
                    AuditId        = auditId,
                    CreatedOn      = createdOn,
                    EntityName     = entityName,
                    LogicalName    = entityName,
                    RecordId       = recordId,
                    RecordKeyValue = recordKeyValue,
                    RecordUrl      = recordUrl,
                    ActionCode     = actionCode,
                    ActionName     = GetAuditActionName(actionCode),
                    OperationId    = operationCode,
                    Operation      = GetAuditOperationName(operationCode),
                    UserId         = userId,
                    UserName       = username,
                    RealActor      = realActor,
                    TransactionId  = transactionId,
                    ChangedField   = string.Empty,
                    OldValue       = string.Empty,
                    NewValue       = string.Empty,
                    LookupOldValue = string.Empty,
                    LookupNewValue = string.Empty
                }
            };
        }

        var rows = new List<AuditExportRow>(changedAttrs.Count);
        foreach (var attrName in changedAttrs)
        {
            var rawOld = detail.OldValue?.Contains(attrName) == true
                ? detail.OldValue.GetAttributeValue<object>(attrName) : null;
            var rawNew = detail.NewValue?.Contains(attrName) == true
                ? detail.NewValue.GetAttributeValue<object>(attrName) : null;

            rows.Add(new AuditExportRow
            {
                AuditId        = auditId,
                CreatedOn      = createdOn,
                EntityName     = entityName,
                LogicalName    = entityName,
                RecordId       = recordId,
                RecordKeyValue = recordKeyValue,
                RecordUrl      = recordUrl,
                ActionCode     = actionCode,
                ActionName     = GetAuditActionName(actionCode),
                OperationId    = operationCode,
                Operation      = GetAuditOperationName(operationCode),
                UserId         = userId,
                UserName       = username,
                RealActor      = realActor,
                TransactionId  = transactionId,
                ChangedField   = attrName,
                OldValue       = TranslateAuditAttributeValue(attrName, rawOld),
                NewValue       = TranslateAuditAttributeValue(attrName, rawNew),
                LookupOldValue = ExtractLookupName(rawOld),
                LookupNewValue = ExtractLookupName(rawNew)
            });
        }

        return rows;
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
                DateTime dt               => dt.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss"),
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
        if (view is null || view.Id == Guid.Empty || _serviceClient is null)
            return Array.Empty<Guid>();

        // ── PASO 1A: asegurar que tenemos el FetchXML ────────────────────────
        // Si el ViewDTO ya trajo el FetchXML desde MetadataService, lo usamos.
        // Si está vacío (vistas personales / userquery no cargadas), lo
        // recuperamos en caliente usando RetrieveRequest contra savedquery y,
        // como fallback, contra userquery.
        var fetchXml = view.FetchXml;
        if (string.IsNullOrWhiteSpace(fetchXml))
        {
            fetchXml = await ResolveFetchXmlFromViewIdAsync(view.Id, cancellationToken);
            if (string.IsNullOrWhiteSpace(fetchXml))
            {
                _logger.LogWarning(
                    "[ResolveViewObjectIds] No se pudo obtener el FetchXML para ViewId={ViewId}",
                    view.Id);
                return Array.Empty<Guid>();
            }
        }

        // ── PASO 1B: ejecutar el FetchXML paginado para obtener los IDs ──────
        var ids       = new List<Guid>();
        var page      = 1;
        string? cookie = null;
        bool   more   = true;

        _logger.LogDebug(
            "[ResolveViewObjectIds] Ejecutando FetchXML de Vista '{ViewName}' (Id={ViewId})",
            view.Name, view.Id);

        while (more)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var paged = InjectPagingIntoFetchXml(fetchXml, page, cookie);
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
                break;
            }
        }

        _logger.LogInformation(
            "[ResolveViewObjectIds] Vista '{ViewName}' → {Count} registros",
            view.Name, ids.Count);

        return ids;
    }

    /// <summary>
    /// Ejecuta un FetchXML paginado (escrito manualmente por el usuario) y devuelve
    /// todos los IDs de los registros obtenidos. Funciona igual que ResolveViewObjectIdsAsync
    /// pero recibe directamente el texto del FetchXML en lugar de un ViewDTO.
    /// </summary>
    private async Task<IReadOnlyList<Guid>> ResolveFetchXmlObjectIdsAsync(
        string fetchXml,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(fetchXml) || _serviceClient is null)
            return Array.Empty<Guid>();

        var ids    = new List<Guid>();
        var page   = 1;
        string? cookie = null;
        bool   more   = true;

        _logger.LogDebug("[ResolveFetchXmlObjectIds] Ejecutando FetchXML manual paginado");

        while (more)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var paged = InjectPagingIntoFetchXml(fetchXml, page, cookie);
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
                    "[ResolveFetchXmlObjectIds] Error en pág {Page}; retornando IDs parciales", page);
                break;
            }
        }

        _logger.LogInformation("[ResolveFetchXmlObjectIds] FetchXML manual → {Count} registros", ids.Count);
        return ids;
    }

    // ── Recupera el FetchXML de una Vista desde Dataverse por su ID.
    // Intenta primero savedquery (vistas del sistema), luego userquery (personales).
    private async Task<string?> ResolveFetchXmlFromViewIdAsync(
        Guid viewId,
        CancellationToken cancellationToken)
    {
        if (_serviceClient is null || viewId == Guid.Empty) return null;

        // Intento 1: savedquery (vista del sistema)
        try
        {
            var r = await Task.Run(() =>
                _serviceClient.Retrieve("savedquery", viewId, new ColumnSet("fetchxml")),
                cancellationToken);
            var xml = r.GetAttributeValue<string>("fetchxml");
            if (!string.IsNullOrWhiteSpace(xml))
            {
                _logger.LogDebug("[ResolveFetchXml] Obtenido desde savedquery Id={ViewId}", viewId);
                return xml;
            }
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "[ResolveFetchXml] savedquery Id={ViewId} no encontrado; probando userquery", viewId);
        }

        // Intento 2: userquery (vista personal del usuario)
        try
        {
            var r = await Task.Run(() =>
                _serviceClient.Retrieve("userquery", viewId, new ColumnSet("fetchxml")),
                cancellationToken);
            var xml = r.GetAttributeValue<string>("fetchxml");
            if (!string.IsNullOrWhiteSpace(xml))
            {
                _logger.LogDebug("[ResolveFetchXml] Obtenido desde userquery Id={ViewId}", viewId);
                return xml;
            }
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "[ResolveFetchXml] userquery Id={ViewId} tampoco encontrado", viewId);
        }

        return null;
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

    private IReadOnlyList<(string field, string oldValue, string newValue)> ParseChangeData(string changeData)
    {
        if (string.IsNullOrWhiteSpace(changeData))
        {
            return Array.Empty<(string field, string oldValue, string newValue)>();
        }

        try
        {
            var document = XDocument.Parse(changeData);
            var attributes = document.Descendants("attribute")
                .Select(a => (
                    field: a.Attribute("name")?.Value ?? string.Empty,
                    oldValue: a.Element("oldValue")?.Value ?? string.Empty,
                    newValue: a.Element("newValue")?.Value ?? string.Empty))
                .ToList();

            return attributes;
        }
        catch
        {
            return Array.Empty<(string field, string oldValue, string newValue)>();
        }
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // HELPER: Resuelve el ObjectTypeCode (entero) de una entidad desde Dataverse.
    // Los registros de 'audit' usan el campo 'objecttypecode' como picklist entero;
    // Dataverse rechaza strings en condiciones FetchXML sobre ese campo con
    // FormatException. Este método obtiene el código una sola vez y lo cachea.
    // ─────────────────────────────────────────────────────────────────────────────
    private async Task<int?> ResolveEntityTypeCodeAsync(string entityName, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(entityName) || _serviceClient is null) return null;
        var key = entityName.Trim().ToLowerInvariant();
        if (_entityTypeCodeCache.TryGetValue(key, out var cached)) return cached;
        try
        {
            var req = new RetrieveEntityRequest
            {
                LogicalName = key,
                EntityFilters = EntityFilters.Entity,
                RetrieveAsIfPublished = true
            };
            var resp = (RetrieveEntityResponse)await _serviceClient.ExecuteAsync(req, cancellationToken);
            var code = resp.EntityMetadata.ObjectTypeCode;
            if (code.HasValue)
            {
                _entityTypeCodeCache[key] = code.Value;
                _logger.LogDebug(
                    "[ResolveEntityTypeCode] '{EntityName}' → ObjectTypeCode={Code}",
                    entityName, code.Value);
            }
            return code;
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "[ResolveEntityTypeCode] No se pudo resolver ObjectTypeCode para '{EntityName}'; se usará nombre lógico como fallback",
                entityName);
            return null;
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

        // ── CACHÉ DE FALLOS: si esta entidad ya lanzÃ³ excepciÃ³n antes en esta
        // sesiÃ³n, retornamos inmediatamente sin llamar a Dataverse.
        // Esto evita que acumulaciÃ³n de timeouts congele el hilo UI.
        if (_entidadesCorruptasCache.Contains(entityName))
        {
            _logger.LogDebug(
                "[LoadEntityMetadata] '{EntityName}' estÃ¡ en lista negra de entidades corruptas; omitiendo llamada a Dataverse",
                entityName);
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
                "[LoadEntityMetadata] Metadatos de '{EntityName}' no disponibles; aÃ±adiendo a lista negra y usando cachÃ©s vacÃ­as",
                entityName);

            // AÃ±adir a lista negra para que futuras llamadas fallen instantÃ¡neamente
            // sin esperar el timeout de Dataverse.
            _entidadesCorruptasCache.Add(entityName);

            // Dejar cachÃ©s en estado vacÃ­o/seguro para esta entidad.
            _optionSetCache = new Dictionary<string, Dictionary<int, string>>(StringComparer.OrdinalIgnoreCase);
            _attributeByColumnNumber = new Dictionary<int, string>();
            _lookupTargetByAttribute = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            return;  // Continuar sin metadatos; los campos mostrarÃ¡n valores raw.
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

    // ─────────────────────────────────────────────────────────────────────────────
    // Mapeo del campo 'action' de Dataverse (audit_action) según referencia oficial.
    // Códigos fuera del enum: "Acción Desconocida (ID)".
    // ─────────────────────────────────────────────────────────────────────────────
    public static string GetAuditActionName(int code)
    {
        if (!Enum.IsDefined(typeof(AuditAction), code))
        {
            return $"Acción Desconocida ({code})";
        }

        return code switch
        {
            0  => "Unknown",
            1  => "Create",
            2  => "Update",
            3  => "Delete",
            4  => "Activate",
            5  => "Deactivate",
            6  => "Upsert",
            11 => "Cascade",
            12 => "Merge",
            13 => "Assign",
            14 => "Share",
            15 => "Unshare",
            16 => "Close",
            17 => "Cancel",
            18 => "Complete",
            20 => "Resolve",
            21 => "Reopen",
            22 => "Fulfill",
            23 => "Paid",
            24 => "Qualify",
            25 => "Disqualify",
            26 => "Send",
            27 => "Reject",
            28 => "Approve",
            29 => "Invoice",
            30 => "Set State",
            31 => "Add Member",
            32 => "Remove Member",
            33 => "Associate Entities",
            34 => "Disassociate Entities",
            35 => "Add Members",
            36 => "Remove Members",
            37 => "Add Item",
            38 => "Remove Item",
            39 => "Add Substitute",
            40 => "Remove Substitute",
            41 => "Set State",
            42 => "Renew",
            43 => "Revise",
            44 => "Win",
            45 => "Lose",
            46 => "Internal Processing",
            47 => "Reschedule",
            48 => "Modify Share",
            49 => "Unshare",
            50 => "Book",
            51 => "Generate Quote From Opportunity",
            52 => "Add To Queue",
            53 => "Assign Role To Team",
            54 => "Remove Role From Team",
            55 => "Assign Role To User",
            56 => "Remove Role From User",
            57 => "Add Privileges To Role",
            58 => "Remove Privileges From Role",
            59 => "Replace Privileges In Role",
            60 => "Import Mappings",
            61 => "Clone",
            62 => "Send Direct Email",
            63 => "Organization Enabled",
            64 => "User Access Via Web",
            65 => "User Access Via Web Services",
            100 => "Delete Entity",
            101 => "Delete Attribute",
            102 => "Audit Change At Entity Level",
            103 => "Audit Change At Attribute Level",
            104 => "Audit Change At Organization Level",
            105 => "Entity Audit Started",
            106 => "Attribute Audit Started",
            107 => "Audit Enabled",
            108 => "Entity Audit Stopped",
            109 => "Attribute Audit Stopped",
            110 => "Audit Disabled",
            111 => "Audit Log Deletion",
            112 => "User Access Audit Started",
            113 => "User Access Audit Stopped",
            115 => "Archive",
            116 => "Retain",
            117 => "RollbackRetain",
            118 => "IPFirewallAccessDenied",
            119 => "IPFirewallAccessAllowed",
            120 => "Restore",
            _  => $"Acción {code}"
        };
    }

    /// <summary>Backward-compat alias — prefer GetAuditActionName in new code.</summary>
    private static string GetOperationName(int code) => GetAuditActionName(code);

    // ─────────────────────────────────────────────────────────────────────────────
    // Mapeo del campo 'operation' de Dataverse (tipo de operación DML).
    // ─────────────────────────────────────────────────────────────────────────────
    public static string GetAuditOperationName(int code) => code switch
    {
        1 => "Create",
        2 => "Update",
        3 => "Delete",
        4 => "Access",
        5 => "Upsert",
        115 => "Archive",
        116 => "Retain",
        117 => "RollbackRetain",
        118 => "Restore",
        200 => "CustomOperation",
        _ => $"Operation_{code}"
    };

    // ─────────────────────────────────────────────────────────────────────────────
    // ExtractLookupName: extrae el Name de un EntityReference.
    // Retorna string.Empty para cualquier tipo que no sea un Lookup.
    // Usado para poblar LookupOldValue / LookupNewValue separados de OldValue/NewValue.
    // ─────────────────────────────────────────────────────────────────────────────
    private static string ExtractLookupName(object? value)
    {
        if (value is not EntityReference er) return string.Empty;
        return !string.IsNullOrWhiteSpace(er.Name) ? er.Name : er.Id.ToString("D");
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

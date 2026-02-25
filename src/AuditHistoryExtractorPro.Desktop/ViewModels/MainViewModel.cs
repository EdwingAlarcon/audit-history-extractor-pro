using AuditHistoryExtractorPro.Core.Models;
using AuditHistoryExtractorPro.Core.Services;
using AuditHistoryExtractorPro.Desktop.Models;
using AuditHistoryExtractorPro.Desktop.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows;

namespace AuditHistoryExtractorPro.Desktop.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly IAuditService _auditService;
    private readonly IMetadataService _metadataService;
    private readonly IDataService _dataService;
    private readonly ConnectionManagerService _connectionManagerService;
    private readonly ILogger<MainViewModel> _logger;
    private CancellationTokenSource? _userSearchCts;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(PreviewCommand))]
    [NotifyCanExecuteChangedFor(nameof(ExtractCommand))]
    private bool isConnected;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ConnectCommand))]
    [NotifyCanExecuteChangedFor(nameof(ExtractCommand))]
    [NotifyCanExecuteChangedFor(nameof(PreviewCommand))]
    private bool isBusy;

    [ObservableProperty]
    private string statusMessage = "Listo.";

    [ObservableProperty]
    private double progressValue;

    [ObservableProperty]
    private string crmUrl = "https://yourorg.crm.dynamics.com";

    [ObservableProperty]
    private string profileName = string.Empty;

    [ObservableProperty]
    private string profileUserName = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(DeleteProfileCommand))]
    private ConnectionProfile? selectedConnectionProfile;

    [ObservableProperty]
    private string entityName = "account";

    [ObservableProperty]
    private EntityDTO? selectedEntity;

    [ObservableProperty]
    private ViewDTO? selectedView;

    [ObservableProperty]
    private DateTime selectedDateFrom = DateTime.Today;

    [ObservableProperty]
    private DateTime selectedDateTo = DateTime.Today;

    [ObservableProperty]
    private bool isFullDay = true;

    [ObservableProperty]
    private string fromTimeText = "00:00";

    [ObservableProperty]
    private string toTimeText = "23:59";

    [ObservableProperty]
    private string outputPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "AuditHistoryExtractorPro",
        "exports",
        $"audit_{DateTime.UtcNow:yyyyMMdd_HHmmss}.xlsx");

    [ObservableProperty]
    private DateRangeFilter selectedDateRange = DateRangeFilter.Todo;

    [ObservableProperty]
    private LookupItem? selectedUser;

    [ObservableProperty]
    private string userSearchText = string.Empty;

    [ObservableProperty]
    private string manualFetchXml = string.Empty;

    [ObservableProperty]
    private string searchValue = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CopyGuidCommand))]
    [NotifyCanExecuteChangedFor(nameof(OpenRecordCommand))]
    private AuditExportRow? selectedPreviewRecord;

    public IReadOnlyList<DateRangeFilter> DateRangeOptions { get; } = Enum.GetValues<DateRangeFilter>();
    public ObservableCollection<LookupItem> AvailableUsers { get; } = new();
    public ObservableCollection<ConnectionProfile> ConnectionProfiles { get; } = new();
    public ObservableCollection<EntityDTO> AvailableEntities { get; } = new();
    public ObservableCollection<ViewDTO> AvailableViews { get; } = new();
    public ObservableCollection<CheckableItem<AuditOperation>> OperationsList { get; } = new();
    public ObservableCollection<CheckableItem<AuditAction>> ActionsList { get; } = new();
    public ObservableCollection<CheckableItem<string>> AttributesList { get; } = new();
    public ObservableCollection<AuditExportRow> PreviewRecords { get; } = new();
    public bool IsManualTimeEnabled => !IsFullDay;

    public MainViewModel(
        IAuditService auditService,
        IMetadataService metadataService,
        IDataService dataService,
        ConnectionManagerService connectionManagerService,
        ILogger<MainViewModel> logger)
    {
        _auditService = auditService;
        _metadataService = metadataService;
        _dataService = dataService;
        _connectionManagerService = connectionManagerService;
        _logger = logger;

        foreach (var operation in AuditMetadataService.GetAuditOperations())
        {
            OperationsList.Add(operation);
        }

        foreach (var action in AuditMetadataService.GetAuditActions())
        {
            ActionsList.Add(action);
        }

        _ = LoadConnectionProfilesAsync();
    }

    private bool CanConnect() => !IsBusy;

    [RelayCommand(CanExecute = nameof(CanConnect))]
    private async Task ConnectAsync()
    {
        IsBusy = true;
        StatusMessage = "Conectando a Dataverse...";
        ProgressValue = 10;

        try
        {
            var settings = new ConnectionSettings
            {
                EnvironmentUrl = CrmUrl
            };

            await Task.Run(async () => await _auditService.ConnectAsync(settings), CancellationToken.None);

            IsConnected = _auditService.IsConnected;
            ProgressValue = 100;
            StatusMessage = IsConnected
                ? $"Conectado a: {_auditService.OrganizationName}"
                : "No se pudo conectar.";

            await SaveOrUpdateCurrentProfileAsync(markAsUsed: true);
            await LoadConnectionProfilesAsync();

            await LoadAuditableEntitiesAsync();
            await _auditService.WarmupEntityMetadataAsync(EntityName);
            await SearchUsersAsync(string.Empty);
        }
        catch (Exception ex)
        {
            IsConnected = false;
            ProgressValue = 0;
            StatusMessage = $"Error de conexión: {ex.Message}";

            _logger.LogError(ex, "[ConnectAsync] Error de conexión a Dataverse");
            System.Windows.MessageBox.Show(
                $"La conexión a Dataverse falló.\n\nDetalle: {ex.Message}\n\nRevisa el log en: Logs/AuditExtractor-*.txt",
                "Error de Conexión — AuditHistoryExtractorPro",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Error);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private bool CanExtract() => !IsBusy && IsConnected;

    [RelayCommand(CanExecute = nameof(CanExtract))]
    private async Task ExtractAsync()
    {
        // ── Selector de ruta de guardado ───────────────────────────────────────────────────
        // Nombre sugerido: usa el nombre de la Vista si hay una seleccionada,
        // o el nombre lógico de la entidad como fallback.
        var baseName = !string.IsNullOrWhiteSpace(SelectedView?.Name)
            ? $"Auditoria_{SanitizeFileName(SelectedView!.Name)}_{DateTime.Now:yyyyMMdd_HHmm}"
            : $"Auditoria_{EntityName}_{DateTime.Now:yyyyMMdd_HHmm}";

        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title           = "Guardar archivo de auditoría",
            Filter          = "Excel Files (*.xlsx)|*.xlsx",
            DefaultExt      = ".xlsx",
            FileName        = baseName + ".xlsx",
            OverwritePrompt = true
        };

        if (dialog.ShowDialog() != true)
        {
            return;   // El usuario canceló: abortamos silenciosamente.
        }

        OutputPath = dialog.FileName;
        IsBusy = true;
        ProgressValue = 0;
        StatusMessage = "Preparando extracción...";

        try
        {
            var request = new ExtractionRequest
            {
                EntityName = EntityName,
                MaxRecords = 10000,
                SelectedDateRange = SelectedDateRange,
                SelectedDateFrom = SelectedDateFrom,
                SelectedDateTo = SelectedDateTo,
                IsFullDay = IsFullDay,
                SelectedUser = SelectedUser,
                SelectedOperations = GetSelectedOperations(),
                SelectedActions = GetSelectedActions(),
                SelectedAttributes = GetSelectedAttributes(),
                SearchValue = SearchValue,
                StartDate = BuildStartDateTime(),
                EndDate = BuildEndDateTime(),
                SelectedView = SelectedView
            };

            var progress = new Progress<string>(message =>
            {
                StatusMessage = message;

                if (message.StartsWith("Consultando página", StringComparison.OrdinalIgnoreCase))
                {
                    ProgressValue = Math.Max(ProgressValue, 20);
                }
                else if (message.StartsWith("Escribiendo registros", StringComparison.OrdinalIgnoreCase))
                {
                    ProgressValue = Math.Min(95, ProgressValue + 10);
                }
                else if (message.StartsWith("Extracción completada", StringComparison.OrdinalIgnoreCase))
                {
                    ProgressValue = 100;
                }
            });

            var result = await _auditService.ExtractAuditHistoryAsync(request, OutputPath, progress);
            if (!result.Success)
            {
                StatusMessage = result.Message;
                ProgressValue = 0;
                return;
            }

            OutputPath = result.OutputFilePath;
            StatusMessage = result.Message;
            ProgressValue = 100;
            // ── La exportación NO toca PreviewRecords.
            // La Vista Previa es un estado independiente del motor de exportación.
        }
        catch (Exception ex)
        {
            ProgressValue = 0;
            StatusMessage = $"Error durante extracción: {ex.Message}";
            _logger.LogError(ex, "[ExtractAsync] Error durante la exportación de '{EntityName}'", EntityName);
            MessageBox.Show(
                $"Se produjo un error durante la exportación.\nRevisa el log en: Logs/AuditExtractor-*.txt\n\nDetalle: {ex.Message}",
                "Error de Extracción",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            IsBusy = false;
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // COMMAND: Vista Previa (máx. 50 registros, sin escribir archivo)
    // ─────────────────────────────────────────────────────────────────────────
    private const int PreviewLimit = 50;

    private bool CanPreview() => !IsBusy && IsConnected;

    [RelayCommand(CanExecute = nameof(CanPreview))]
    private async Task PreviewAsync()
    {
        IsBusy = true;
        ProgressValue = 10;
        StatusMessage = $"Cargando vista previa (máx. {PreviewLimit} registros)...";
        // Clear en el UI thread ANTES de lanzar el hilo de fondo.
        PreviewRecords.Clear();

        try
        {
            var request = new ExtractionRequest
            {
                EntityName         = EntityName,
                MaxRecords         = PreviewLimit,
                SelectedDateRange  = SelectedDateRange,
                SelectedDateFrom   = SelectedDateFrom,
                SelectedDateTo     = SelectedDateTo,
                IsFullDay          = IsFullDay,
                SelectedUser       = SelectedUser,
                SelectedOperations = GetSelectedOperations(),
                SelectedActions    = GetSelectedActions(),
                SelectedAttributes = GetSelectedAttributes(),
                SearchValue        = SearchValue,
                StartDate          = BuildStartDateTime(),
                EndDate            = BuildEndDateTime(),
                SelectedView       = SelectedView
            };

            // Ejecutar en hilo de fondo — UI sigue respondiendo.
            var rows = await Task.Run(
                () => _auditService.GetPreviewRowsAsync(request, PreviewLimit));

            // ── DIAGNÓSTICO: conteo estricto antes de tocar la UI ────────────
            var count = rows?.Count ?? 0;
            _logger.LogInformation(
                "[PreviewAsync] GetPreviewRowsAsync devolvió {Count} filas para entidad='{Entity}'",
                count, EntityName);

            if (count == 0)
            {
                ProgressValue = 100;
                StatusMessage = "Vista previa: no se encontraron registros con los filtros actuales.";
                MessageBox.Show(
                    "El servicio devolvió 0 registros de auditoría para los filtros seleccionados.\n\n" +
                    "Posibles causas:\n" +
                    "  • El rango de fechas no coincide con registros existentes.\n" +
                    "  • La Vista seleccionada no devuelve registros.",
                    "Sin Resultados",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            MessageBox.Show(
                $"Se descargaron {count} registros del servicio.\nActualizando interfaz...",
                "Datos Recibidos",
                MessageBoxButton.OK,
                MessageBoxImage.Information);

            // ── LLENADO DE ObservableCollection ──────────────────────────────
            // Regla WPF: NO reasignar PreviewRecords = new ObservableCollection<>()
            // (rompe el binding XAML). Usar Clear() + Add() en el UI thread.
            // Después del await estamos de vuelta en el SynchronizationContext
            // original (UI thread), así que un único Dispatcher.Invoke basta.
            System.Windows.Application.Current.Dispatcher.Invoke(() =>
            {
                PreviewRecords.Clear();
                foreach (var row in rows!)
                    PreviewRecords.Add(row);
            });

            ProgressValue = 100;
            StatusMessage = $"Vista previa: {PreviewRecords.Count} registros. Revisa los datos y pulsa Exportar.";
        }
        catch (Exception ex)
        {
            ProgressValue = 0;
            StatusMessage = $"Error en vista previa: {ex.Message}";
            _logger.LogError(ex, "[PreviewAsync] Error en vista previa de '{EntityName}'", EntityName);
            MessageBox.Show(
                $"Se produjo un error durante la vista previa.\nRevisa el log en: Logs/AuditExtractor-*.txt\n\nDetalle: {ex.Message}",
                "Error de Vista Previa",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            IsBusy = false;
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // El log de emergencia ahora es manejado por Serilog (archivo rodante
    // en Logs/AuditExtractor-*.txt). WriteEmergencyLog fue eliminado.
    // ─────────────────────────────────────────────────────────────────────────

    partial void OnUserSearchTextChanged(string value)
    {
        _ = SearchUsersAsync(value);
    }

    partial void OnIsFullDayChanged(bool value)
    {
        OnPropertyChanged(nameof(IsManualTimeEnabled));
    }

    partial void OnSelectedEntityChanged(EntityDTO? value)
    {
        if (value is null)
        {
            return;
        }

        EntityName = value.LogicalName;
        _ = _auditService.WarmupEntityMetadataAsync(value.LogicalName);
        _ = LoadSystemViewsAsync(value.LogicalName);
        _ = LoadEntityAttributesAsync(value.LogicalName);
    }

    // Guard: si alguien escribe manualmente en el ComboBox editable (o cualquier
    // otra ruta establece EntityName), detectamos si el valor es un DisplayName
    // en lugar del LogicalName y lo corregimos automáticamente.
    // Ejemplo: usuario teclea "Concepto Factura" → se convierte a "new_conceptofactura".
    partial void OnEntityNameChanged(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || AvailableEntities.Count == 0)
            return;

        // Si el valor ya es un LogicalName conocido, no hacemos nada.
        var byLogical = AvailableEntities.FirstOrDefault(e =>
            string.Equals(e.LogicalName, value, StringComparison.OrdinalIgnoreCase));
        if (byLogical is not null)
        {
            // Sincroniza SelectedEntity si es distinto (sin recursión: ya es LogicalName).
            if (!ReferenceEquals(SelectedEntity, byLogical))
                SelectedEntity = byLogical;
            return;
        }

        // Si el valor coincide con un DisplayName, reemplazar por el LogicalName.
        var byDisplay = AvailableEntities.FirstOrDefault(e =>
            string.Equals(e.DisplayName, value, StringComparison.OrdinalIgnoreCase));
        if (byDisplay is not null)
        {
            _logger.LogDebug(
                "[OnEntityNameChanged] DisplayName '{DisplayName}' → LogicalName '{LogicalName}'",
                value, byDisplay.LogicalName);
            EntityName   = byDisplay.LogicalName;  // Re-entra, pero byLogical lo captura
            SelectedEntity = byDisplay;
        }
    }

    partial void OnSelectedViewChanged(ViewDTO? value)
    {
        if (value is null)
        {
            return;
        }

        ManualFetchXml = value.FetchXml;
    }

    partial void OnSelectedConnectionProfileChanged(ConnectionProfile? value)
    {
        if (value is null)
        {
            return;
        }

        ProfileName = value.Name;
        ProfileUserName = value.UserName;
        CrmUrl = value.Url;
    }

    private async Task LoadAuditableEntitiesAsync()
    {
        AvailableEntities.Clear();
        AvailableViews.Clear();
        AttributesList.Clear();

        try
        {
            var entities = await _metadataService.GetAuditableEntitiesAsync();
            foreach (var entity in entities)
            {
                AvailableEntities.Add(entity);
            }

            if (AvailableEntities.Count == 0)
            {
                StatusMessage = "No se encontraron entidades auditables.";
                return;
            }

            SelectedEntity = AvailableEntities.FirstOrDefault(e =>
                string.Equals(e.LogicalName, EntityName, StringComparison.OrdinalIgnoreCase))
                ?? AvailableEntities[0];
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error al cargar entidades auditables: {ex.Message}";
        }
    }

    private async Task LoadSystemViewsAsync(string entityLogicalName)
    {
        if (string.IsNullOrWhiteSpace(entityLogicalName) || !IsConnected)
        {
            AvailableViews.Clear();
            SelectedView = null;
            return;
        }

        try
        {
            var views = await _metadataService.GetSystemViewsAsync(entityLogicalName);

            AvailableViews.Clear();
            foreach (var view in views)
            {
                AvailableViews.Add(view);
            }

            SelectedView = AvailableViews.FirstOrDefault();
        }
        catch (Exception ex)
        {
            AvailableViews.Clear();
            SelectedView = null;
            StatusMessage = $"No se pudieron cargar vistas del sistema: {ex.Message}";
        }
    }

    private async Task LoadEntityAttributesAsync(string entityLogicalName)
    {
        if (string.IsNullOrWhiteSpace(entityLogicalName) || !IsConnected)
        {
            AttributesList.Clear();
            return;
        }

        try
        {
            var attributes = await _metadataService.GetEntityAttributesAsync(entityLogicalName);
            AttributesList.Clear();
            foreach (var attribute in attributes)
            {
                AttributesList.Add(new CheckableItem<string>
                {
                    Value = attribute.LogicalName,
                    Label = string.IsNullOrWhiteSpace(attribute.DisplayName)
                        ? attribute.LogicalName
                        : $"{attribute.DisplayName} ({attribute.LogicalName})",
                    IsSelected = false
                });
            }
        }
        catch (Exception ex)
        {
            AttributesList.Clear();
            StatusMessage = $"No se pudieron cargar atributos: {ex.Message}";
        }
    }

    [RelayCommand]
    private void SelectAllOperations()
    {
        foreach (var item in OperationsList)
        {
            item.IsSelected = true;
        }
    }

    [RelayCommand]
    private void DeselectAllOperations()
    {
        foreach (var item in OperationsList)
        {
            item.IsSelected = false;
        }
    }

    [RelayCommand]
    private void SelectAllActions()
    {
        foreach (var item in ActionsList)
        {
            item.IsSelected = true;
        }
    }

    [RelayCommand]
    private void DeselectAllActions()
    {
        foreach (var item in ActionsList)
        {
            item.IsSelected = false;
        }
    }

    [RelayCommand]
    private void SelectAllAttributes()
    {
        foreach (var item in AttributesList)
        {
            item.IsSelected = true;
        }
    }

    [RelayCommand]
    private void DeselectAllAttributes()
    {
        foreach (var item in AttributesList)
        {
            item.IsSelected = false;
        }
    }

    [RelayCommand]
    private async Task SaveProfileAsync()
    {
        await SaveOrUpdateCurrentProfileAsync(markAsUsed: false);
        await LoadConnectionProfilesAsync();
        StatusMessage = "Perfil guardado correctamente.";
    }

    private bool CanDeleteProfile() => SelectedConnectionProfile is not null;

    [RelayCommand(CanExecute = nameof(CanDeleteProfile))]
    private async Task DeleteProfileAsync()
    {
        if (SelectedConnectionProfile is null)
        {
            return;
        }

        var name = SelectedConnectionProfile.Name;
        await _connectionManagerService.DeleteProfileAsync(name);
        await LoadConnectionProfilesAsync();

        if (string.Equals(ProfileName, name, StringComparison.OrdinalIgnoreCase))
        {
            ProfileName = string.Empty;
            ProfileUserName = string.Empty;
        }

        SelectedConnectionProfile = null;
        StatusMessage = "Perfil eliminado.";
    }

    private bool CanCopyGuid()
    {
        return SelectedPreviewRecord is not null
            && !string.IsNullOrWhiteSpace(SelectedPreviewRecord.RecordId)
            && !string.Equals(SelectedPreviewRecord.RecordId, "N/A", StringComparison.OrdinalIgnoreCase);
    }

    [RelayCommand(CanExecute = nameof(CanCopyGuid))]
    private void CopyGuid()
    {
        if (SelectedPreviewRecord is null)
        {
            return;
        }

        Clipboard.SetText(SelectedPreviewRecord.RecordId);
        StatusMessage = $"GUID copiado: {SelectedPreviewRecord.RecordId}";
    }

    private bool CanOpenRecord()
    {
        return SelectedPreviewRecord is not null
            && !string.IsNullOrWhiteSpace(SelectedPreviewRecord.RecordUrl);
    }

    [RelayCommand(CanExecute = nameof(CanOpenRecord))]
    private void OpenRecord()
    {
        if (SelectedPreviewRecord is null || string.IsNullOrWhiteSpace(SelectedPreviewRecord.RecordUrl))
        {
            return;
        }

        Process.Start(new ProcessStartInfo
        {
            FileName = SelectedPreviewRecord.RecordUrl,
            UseShellExecute = true
        });
    }

    private async Task SearchUsersAsync(string? query)
    {
        if (!IsConnected)
        {
            return;
        }

        _userSearchCts?.Cancel();
        _userSearchCts?.Dispose();
        _userSearchCts = new CancellationTokenSource();

        try
        {
            var users = await _dataService.SearchUsersAsync(query ?? string.Empty, _userSearchCts.Token);
            var current = SelectedUser;

            AvailableUsers.Clear();

            // ── Opción "Todos los usuarios" (valor nulo = sin filtro de usuario) ──────────────────
            // Se inserta como primer elemento para que sea la selección por defecto.
            // Cuando SelectedUser es null, QueryBuilderService no agrega ninguna
            // condición de userid y se descarga la auditoría completa de la entidad.
            AvailableUsers.Add(new LookupItem { Id = Guid.Empty, Name = "(Todos los usuarios)" });

            foreach (var user in users)
            {
                AvailableUsers.Add(new LookupItem { Id = user.Id, Name = user.Name });
            }

            // Restaurar la selección previa si sigue en la lista.
            // Si no había ninguna o no se encuentra, dejar null (= sin filtro de usuario).
            if (current is not null && current.Id != Guid.Empty
                && AvailableUsers.Any(u => u.Id == current.Id))
            {
                SelectedUser = AvailableUsers.First(u => u.Id == current.Id);
            }
            else
            {
                // null = "Todos los usuarios"; el ComboBox quedará en blanco o
                // mostrará el placeholder según el estilo XAML.
                SelectedUser = null;
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error buscando usuarios: {ex.Message}";
        }
    }

    private IReadOnlyList<int> GetSelectedOperations()
    {
        return OperationsList
            .Where(item => item.IsSelected)
            .Select(item => (int)item.Value)
            .Distinct()
            .ToList();
    }

    private IReadOnlyList<int> GetSelectedActions()
    {
        return ActionsList
            .Where(item => item.IsSelected)
            .Select(item => (int)item.Value)
            .Distinct()
            .ToList();
    }

    private IReadOnlyList<string> GetSelectedAttributes()
    {
        return AttributesList
            .Where(item => item.IsSelected && !string.IsNullOrWhiteSpace(item.Value))
            .Select(item => item.Value)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private DateTime BuildStartDateTime()
    {
        if (IsFullDay)
        {
            return SelectedDateFrom.Date;
        }

        var time = ParseTimeOrDefault(FromTimeText, new TimeSpan(0, 0, 0));
        return SelectedDateFrom.Date.Add(time);
    }

    private DateTime BuildEndDateTime()
    {
        if (IsFullDay)
        {
            return SelectedDateTo.Date;
        }

        var time = ParseTimeOrDefault(ToTimeText, new TimeSpan(23, 59, 0));
        return SelectedDateTo.Date.Add(time);
    }

    private static TimeSpan ParseTimeOrDefault(string? value, TimeSpan fallback)
    {
        if (TimeSpan.TryParseExact(value?.Trim(), @"hh\:mm", CultureInfo.InvariantCulture, out var parsed))
        {
            return parsed;
        }

        return fallback;
    }

    private async Task LoadConnectionProfilesAsync()
    {
        var profiles = await _connectionManagerService.GetProfilesAsync();
        ConnectionProfiles.Clear();

        foreach (var profile in profiles)
        {
            ConnectionProfiles.Add(profile);
        }
    }

    private async Task SaveOrUpdateCurrentProfileAsync(bool markAsUsed)
    {
        if (string.IsNullOrWhiteSpace(CrmUrl))
        {
            return;
        }

        var normalizedName = string.IsNullOrWhiteSpace(ProfileName)
            ? BuildProfileNameFromUrl(CrmUrl)
            : ProfileName.Trim();

        var profile = new ConnectionProfile
        {
            Name = normalizedName,
            Url = CrmUrl.Trim(),
            UserName = ProfileUserName.Trim(),
            LastUsed = markAsUsed ? DateTime.UtcNow : SelectedConnectionProfile?.LastUsed ?? DateTime.UtcNow
        };

        await _connectionManagerService.SaveProfileAsync(profile);
        ProfileName = profile.Name;
        SelectedConnectionProfile = profile;
    }

    private static string BuildProfileNameFromUrl(string url)
    {
        if (Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return uri.Host;
        }

        return "Perfil";
    }

    /// <summary>
    /// Reemplaza caracteres inválidos para nombre de archivo por guion bajo.
    /// </summary>
    private static string SanitizeFileName(string name)
    {
        var invalid = System.IO.Path.GetInvalidFileNameChars();
        return string.Concat(name.Select(c => invalid.Contains(c) ? '_' : c));
    }
}

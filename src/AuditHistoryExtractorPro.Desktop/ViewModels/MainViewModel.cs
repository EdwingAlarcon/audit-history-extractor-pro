using AuditHistoryExtractorPro.Core.Models;
using AuditHistoryExtractorPro.Core.Services;
using AuditHistoryExtractorPro.Desktop.Models;
using AuditHistoryExtractorPro.Desktop.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
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
        ConnectionManagerService connectionManagerService)
    {
        _auditService = auditService;
        _metadataService = metadataService;
        _dataService = dataService;
        _connectionManagerService = connectionManagerService;

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

            // ── LOG DE EMERGENCIA ────────────────────────────────────────────
            try
            {
                var sb = new System.Text.StringBuilder();
                sb.AppendLine("=====================================================");
                sb.AppendLine($"AuditHistoryExtractorPro — Error de Conexión");
                sb.AppendLine($"Fecha y Hora : {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                sb.AppendLine("=====================================================");
                sb.AppendLine();
                sb.AppendLine($"[Mensaje]       {ex.Message}");
                sb.AppendLine();
                sb.AppendLine($"[StackTrace]");
                sb.AppendLine(ex.StackTrace);

                if (ex.InnerException is { } inner)
                {
                    sb.AppendLine();
                    sb.AppendLine($"[InnerException.Mensaje]  {inner.Message}");
                    sb.AppendLine($"[InnerException.StackTrace]");
                    sb.AppendLine(inner.StackTrace);
                }

                var logPath = System.IO.Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
                    "AuditApp_ErrorLog.txt");

                System.IO.File.WriteAllText(logPath, sb.ToString(), System.Text.Encoding.UTF8);

                System.Windows.MessageBox.Show(
                    $"La conexión a Dataverse falló.\n\n" +
                    $"Mensaje: {ex.Message}\n\n" +
                    $"Se ha generado un log detallado en tu Escritorio:\n{logPath}",
                    "Error de Conexión — AuditHistoryExtractorPro",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Error);
            }
            catch
            {
                // Si el propio log falla, mostramos al menos el MessageBox básico
                System.Windows.MessageBox.Show(
                    $"La conexión a Dataverse falló y no se pudo escribir el log de error.\n\nDetalle: {ex.Message}",
                    "Error de Conexión",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Error);
            }
            // ────────────────────────────────────────────────────────────────
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
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title       = "Guardar archivo de auditoría",
            Filter      = "Excel Files (*.xlsx)|*.xlsx|CSV Files (*.csv)|*.csv",
            DefaultExt  = ".xlsx",
            FileName    = $"audit_{EntityName}_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx",
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
                EndDate = BuildEndDateTime()
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

            PreviewRecords.Clear();
            PreviewRecords.Add(new AuditExportRow
            {
                AuditId = "N/A",
                CreatedOn = DateTime.UtcNow.ToString("O"),
                EntityName = EntityName,
                RecordId = "N/A",
                LogicalName = EntityName,
                RecordUrl = string.Empty,
                ActionCode = 0,
                ActionName = "Export",
                UserId = SelectedUser?.Id.ToString() ?? string.Empty,
                UserName = SelectedUser?.Name ?? string.Empty,
                TransactionId = "N/A",
                ChangedField = "Archivo generado",
                OldValue = string.Empty,
                NewValue = result.OutputFilePath
            });
        }
        catch (Exception ex)
        {
            ProgressValue = 0;
            StatusMessage = $"Error durante extracción: {ex.Message}";
            var logPath = WriteEmergencyLog("Exportación", ex);
            MessageBox.Show(
                $"Se produjo un error durante la exportación.\nSe ha generado un log de diagnóstico en:\n{logPath}",
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
                EndDate            = BuildEndDateTime()
            };

            var rows = await _auditService.GetPreviewRowsAsync(request, PreviewLimit);

            foreach (var row in rows)
            {
                PreviewRecords.Add(row);
            }

            ProgressValue = 100;
            StatusMessage = PreviewRecords.Count == 0
                ? "Vista previa: no se encontraron registros con los filtros actuales."
                : $"Vista previa: {PreviewRecords.Count} de los primeros {PreviewLimit} registros. Revisa los datos y pulsa Exportar.";
        }
        catch (Exception ex)
        {
            ProgressValue = 0;
            StatusMessage = $"Error en vista previa: {ex.Message}";
            var logPath = WriteEmergencyLog("Vista Previa", ex);
            MessageBox.Show(
                $"Se produjo un error durante la vista previa.\nSe ha generado un log de diagnóstico en:\n{logPath}",
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
    // HELPER: Log de emergencia físico en el Escritorio del usuario
    // ─────────────────────────────────────────────────────────────────────────
    private static string WriteEmergencyLog(string operacion, Exception ex)
    {
        try
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("========================================================");
            sb.AppendLine($"AuditHistoryExtractorPro — Error de {operacion}");
            sb.AppendLine($"Fecha y Hora : {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine("========================================================");
            sb.AppendLine();
            sb.AppendLine($"[Tipo de Excepción]  {ex.GetType().FullName}");
            sb.AppendLine($"[Mensaje]            {ex.Message}");
            sb.AppendLine();
            sb.AppendLine("[StackTrace]");
            sb.AppendLine(ex.StackTrace);

            var inner = ex.InnerException;
            var depth = 1;
            while (inner != null)
            {
                sb.AppendLine();
                sb.AppendLine($"[InnerException #{depth}]");
                sb.AppendLine($"  Tipo    : {inner.GetType().FullName}");
                sb.AppendLine($"  Mensaje : {inner.Message}");
                sb.AppendLine("  StackTrace:");
                sb.AppendLine(inner.StackTrace);
                inner = inner.InnerException;
                depth++;
            }

            var logPath = System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
                "AuditApp_ExtractionError.txt");

            System.IO.File.WriteAllText(logPath, sb.ToString(), System.Text.Encoding.UTF8);
            return logPath;
        }
        catch
        {
            // Si falla el propio logger, no interrumpir el flujo de la aplicación.
            return "(no se pudo escribir el log)";
        }
    }

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
}

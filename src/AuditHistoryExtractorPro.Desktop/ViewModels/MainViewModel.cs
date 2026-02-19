using AuditHistoryExtractorPro.Core.Models;
using AuditHistoryExtractorPro.Core.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
using System.IO;

namespace AuditHistoryExtractorPro.Desktop.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly IAuditService _auditService;
    private readonly IMetadataService _metadataService;
    private readonly List<LookupItem> _allUsers = new();

    [ObservableProperty]
    private bool isConnected;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ConnectCommand))]
    [NotifyCanExecuteChangedFor(nameof(ExtractCommand))]
    private bool isBusy;

    [ObservableProperty]
    private string statusMessage = "Listo.";

    [ObservableProperty]
    private double progressValue;

    [ObservableProperty]
    private string crmUrl = "https://yourorg.crm.dynamics.com";

    [ObservableProperty]
    private string entityName = "account";

    [ObservableProperty]
    private EntityDTO? selectedEntity;

    [ObservableProperty]
    private ViewDTO? selectedView;

    [ObservableProperty]
    private DateTime? startDate;

    [ObservableProperty]
    private DateTime? endDate;

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
    private OperationFilter selectedOperation = OperationFilter.Update;

    [ObservableProperty]
    private string userSearchText = string.Empty;

    [ObservableProperty]
    private bool includeCreate = true;

    [ObservableProperty]
    private bool includeUpdate = true;

    [ObservableProperty]
    private bool includeDelete = true;

    [ObservableProperty]
    private string manualFetchXml = string.Empty;

    public IReadOnlyList<DateRangeFilter> DateRangeOptions { get; } = Enum.GetValues<DateRangeFilter>();
    public IReadOnlyList<OperationFilter> OperationOptions { get; } = Enum.GetValues<OperationFilter>();
    public ObservableCollection<LookupItem> AvailableUsers { get; } = new();
    public ObservableCollection<EntityDTO> AvailableEntities { get; } = new();
    public ObservableCollection<ViewDTO> AvailableViews { get; } = new();
    public ObservableCollection<AuditExportRow> PreviewRecords { get; } = new();

    public MainViewModel(IAuditService auditService, IMetadataService metadataService)
    {
        _auditService = auditService;
        _metadataService = metadataService;
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

            AvailableUsers.Clear();
            _allUsers.Clear();

            var users = await _auditService.GetUsersAsync();
            foreach (var user in users)
            {
                _allUsers.Add(user);
                AvailableUsers.Add(user);
            }

            if (AvailableUsers.Count > 0 && SelectedUser is null)
            {
                SelectedUser = AvailableUsers[0];
            }

            await LoadAuditableEntitiesAsync();
        }
        catch (Exception ex)
        {
            IsConnected = false;
            ProgressValue = 0;
            StatusMessage = $"Error de conexión: {ex.Message}";
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
        IsBusy = true;
        ProgressValue = 0;
        StatusMessage = "Preparando extracción...";

        try
        {
            var request = new ExtractionRequest
            {
                EntityName = EntityName,
                MaxRecords = 10000,
                IncludeCreate = IncludeCreate,
                IncludeUpdate = IncludeUpdate,
                IncludeDelete = IncludeDelete,
                SelectedDateRange = SelectedDateRange,
                SelectedUser = SelectedUser,
                SelectedOperation = ResolveSelectedOperation(),
                StartDate = StartDate,
                EndDate = EndDate
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
            StatusMessage = $"Error durante extracción: {ex.Message}";
            ProgressValue = 0;
        }
        finally
        {
            IsBusy = false;
        }
    }

    partial void OnUserSearchTextChanged(string value)
    {
        if (_allUsers.Count == 0)
        {
            return;
        }

        var query = value?.Trim() ?? string.Empty;
        var filtered = string.IsNullOrWhiteSpace(query)
            ? _allUsers
            : _allUsers.Where(u => u.Name.Contains(query, StringComparison.OrdinalIgnoreCase)).ToList();

        var currentSelection = SelectedUser;
        AvailableUsers.Clear();
        foreach (var user in filtered)
        {
            AvailableUsers.Add(user);
        }

        if (currentSelection is not null && AvailableUsers.Any(u => u.Id == currentSelection.Id))
        {
            SelectedUser = AvailableUsers.First(u => u.Id == currentSelection.Id);
        }
        else if (AvailableUsers.Count > 0)
        {
            SelectedUser = AvailableUsers[0];
        }
    }

    partial void OnSelectedEntityChanged(EntityDTO? value)
    {
        if (value is null)
        {
            return;
        }

        EntityName = value.LogicalName;
        _ = LoadSystemViewsAsync(value.LogicalName);
    }

    partial void OnSelectedViewChanged(ViewDTO? value)
    {
        if (value is null)
        {
            return;
        }

        ManualFetchXml = value.FetchXml;
    }

    private async Task LoadAuditableEntitiesAsync()
    {
        AvailableEntities.Clear();
        AvailableViews.Clear();

        var entities = await _metadataService.GetAuditableEntitiesAsync();
        foreach (var entity in entities)
        {
            AvailableEntities.Add(entity);
        }

        if (AvailableEntities.Count == 0)
        {
            return;
        }

        SelectedEntity = AvailableEntities.FirstOrDefault(e =>
            string.Equals(e.LogicalName, EntityName, StringComparison.OrdinalIgnoreCase))
            ?? AvailableEntities[0];
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

    private OperationFilter? ResolveSelectedOperation()
    {
        var selected = new List<OperationFilter>();
        if (IncludeCreate) selected.Add(OperationFilter.Create);
        if (IncludeUpdate) selected.Add(OperationFilter.Update);
        if (IncludeDelete) selected.Add(OperationFilter.Delete);

        return selected.Count == 1 ? selected[0] : null;
    }
}

using AuditHistoryExtractorPro.Core.Models;
using AuditHistoryExtractorPro.Core.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using WpfApplication = System.Windows.Application;

namespace AuditHistoryExtractorPro.Desktop.ViewModels;

/// <summary>
/// ViewModel ligero para extraer auditoría con MVVM limpio.
/// Usa GetPreviewRowsAsync para poblar una lista observable y reporta progreso/cancelación.
/// </summary>
public partial class QuickExtractViewModel : ObservableObject
{
    private readonly IAuditService _auditService;
    private CancellationTokenSource? _cts;

    [ObservableProperty]
    private bool isBusy;

    [ObservableProperty]
    private int progressValue;

    [ObservableProperty]
    private string statusMessage = "Listo";

    [ObservableProperty]
    private string entityName = "account";

    [ObservableProperty]
    private DateTime? startDate = DateTime.Today.AddDays(-7);

    [ObservableProperty]
    private DateTime? endDate = DateTime.Today;

    [ObservableProperty]
    private int maxRows = 200;

    [ObservableProperty]
    private bool includeCreate = true;

    [ObservableProperty]
    private bool includeUpdate = true;

    [ObservableProperty]
    private bool includeDelete = true;

    public ObservableCollection<AuditExportRow> Results { get; } = new();

    public IAsyncRelayCommand ExtractCommand { get; }
    public IRelayCommand CancelCommand { get; }

    public QuickExtractViewModel(IAuditService auditService)
    {
        _auditService = auditService;
        ExtractCommand = new AsyncRelayCommand(ExecuteExtractAsync, CanExtract);
        CancelCommand = new RelayCommand(Cancel, () => IsBusy);

        PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(IsBusy))
            {
                ExtractCommand.NotifyCanExecuteChanged();
                CancelCommand.NotifyCanExecuteChanged();
            }
        };
    }

    private bool CanExtract() => !IsBusy;

    private async Task ExecuteExtractAsync()
    {
        IsBusy = true;
        ProgressValue = 0;
        StatusMessage = "Preparando extracción...";
        Results.Clear();
        _cts = new CancellationTokenSource();

        try
        {
            var request = new ExtractionRequest
            {
                EntityName = EntityName,
                StartDate = StartDate,
                EndDate = EndDate,
                IncludeCreate = IncludeCreate,
                IncludeUpdate = IncludeUpdate,
                IncludeDelete = IncludeDelete,
                MaxRecords = Math.Max(1, MaxRows)
            };

            ProgressValue = 5;
            StatusMessage = "Consultando auditoría...";

            // Ejecuta en hilo de fondo pero conserva las actualizaciones en el hilo UI.
            var rows = await Task.Run(() => _auditService.GetPreviewRowsAsync(request, MaxRows, _cts.Token), _cts.Token);

            ProgressValue = 50;
            StatusMessage = "Procesando resultados...";

            foreach (var row in rows)
            {
                _cts.Token.ThrowIfCancellationRequested();
                await WpfApplication.Current.Dispatcher.InvokeAsync(() => Results.Add(row));
                ProgressValue = Math.Min(95, ProgressValue + 1);
            }

            ProgressValue = 100;
            StatusMessage = $"Completado. Registros: {Results.Count}";
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "Extracción cancelada por el usuario.";
        }
        catch (Exception ex)
        {
            StatusMessage = "Error durante la extracción.";
            MessageBox.Show($"Error durante la extracción:\n{ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            _cts?.Dispose();
            _cts = null;
            IsBusy = false;
        }
    }

    private void Cancel()
    {
        if (!IsBusy)
        {
            return;
        }

        _cts?.Cancel();
    }
}

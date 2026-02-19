using AuditHistoryExtractorPro.UI.ViewModels;

namespace AuditHistoryExtractorPro.UI.Services;

public class HistoryPageCoordinator
{
    private readonly AuditSessionState _sessionState;
    private readonly HistoryViewService _historyViewService;

    public HistoryPageCoordinator(
        AuditSessionState sessionState,
        HistoryViewService historyViewService)
    {
        _sessionState = sessionState;
        _historyViewService = historyViewService;
    }

    public void LoadFromSession(HistoryPageViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);

        viewModel.AllAuditRecords.Clear();
        viewModel.AllAuditRecords.AddRange(_sessionState.GetRecordsCopy());

        Refresh(viewModel, resetPage: true);

        if (!viewModel.AllAuditRecords.Any())
        {
            viewModel.Message = "ℹ️ No hay datos en sesión. Ejecuta una extracción para ver historial.";
        }
    }

    public void ApplyFilters(HistoryPageViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);

        Refresh(viewModel, resetPage: true);

        if (!viewModel.IsError && !viewModel.FilteredRecords.Any())
        {
            viewModel.Message = "ℹ️ No hay resultados para los filtros aplicados.";
        }
    }

    public void ClearFilters(HistoryPageViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);

        viewModel.EntityFilter = string.Empty;
        viewModel.RecordIdFilter = string.Empty;
        viewModel.UserFilter = string.Empty;
        viewModel.OperationFilter = string.Empty;
        viewModel.DateFrom = DateTime.Now.AddDays(-30);
        viewModel.DateTo = DateTime.Now;
        viewModel.Message = string.Empty;
        viewModel.IsError = false;

        Refresh(viewModel, resetPage: true);
    }

    public void GoToPreviousPage(HistoryPageViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);

        if (viewModel.CurrentPage <= 1)
        {
            return;
        }

        viewModel.CurrentPage--;
        Refresh(viewModel, resetPage: false);
    }

    public void GoToNextPage(HistoryPageViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);

        if (viewModel.CurrentPage >= viewModel.TotalPages)
        {
            return;
        }

        viewModel.CurrentPage++;
        Refresh(viewModel, resetPage: false);
    }

    private void Refresh(HistoryPageViewModel viewModel, bool resetPage)
    {
        var requestedPage = resetPage ? 1 : viewModel.CurrentPage;
        var result = _historyViewService.BuildView(
            viewModel.AllAuditRecords,
            CreateFilter(viewModel),
            requestedPage,
            viewModel.PageSize);

        if (!string.IsNullOrWhiteSpace(result.ValidationError))
        {
            viewModel.IsError = true;
            viewModel.Message = result.ValidationError;
            return;
        }

        viewModel.FilteredRecords = result.FilteredRecords;
        viewModel.PagedRecords = result.PagedRecords;
        viewModel.Stats = result.Stats;
        viewModel.CurrentPage = result.CurrentPage;
        viewModel.TotalPages = result.TotalPages;
    }

    private static HistoryFilter CreateFilter(HistoryPageViewModel viewModel)
    {
        return new HistoryFilter
        {
            Entity = viewModel.EntityFilter,
            RecordId = viewModel.RecordIdFilter,
            User = viewModel.UserFilter,
            Operation = viewModel.OperationFilter,
            DateFrom = viewModel.DateFrom,
            DateTo = viewModel.DateTo
        };
    }
}

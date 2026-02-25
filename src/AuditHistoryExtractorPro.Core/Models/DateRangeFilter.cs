namespace AuditHistoryExtractorPro.Core.Models;

public enum DateRangeFilter
{
    Hoy,
    Semana,
    Mes,
    Todo,
    /// <summary>
    /// El usuario especificó un intervalo de fechas concreto en los date-pickers.
    /// ResolveDateRange usará StartDate/EndDate/SelectedDateFrom/SelectedDateTo.
    /// </summary>
    Personalizado
}

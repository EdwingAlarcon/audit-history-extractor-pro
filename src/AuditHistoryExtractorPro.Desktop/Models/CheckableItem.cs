using CommunityToolkit.Mvvm.ComponentModel;

namespace AuditHistoryExtractorPro.Desktop.Models;

public partial class CheckableItem<T> : ObservableObject
{
    [ObservableProperty]
    private T value = default!;

    [ObservableProperty]
    private string label = string.Empty;

    [ObservableProperty]
    private bool isSelected;
}

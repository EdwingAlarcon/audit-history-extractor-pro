using System.Windows;
using AuditHistoryExtractorPro.Desktop.ViewModels;

namespace AuditHistoryExtractorPro.Desktop.Views;

public partial class QuickExtractWindow : Window
{
    public QuickExtractWindow(QuickExtractViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }
}

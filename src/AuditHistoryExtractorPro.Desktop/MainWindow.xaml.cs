using System.Windows;
using AuditHistoryExtractorPro.Desktop.ViewModels;

namespace AuditHistoryExtractorPro.Desktop;

/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow : Window
{
    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }
}
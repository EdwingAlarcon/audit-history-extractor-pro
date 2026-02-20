using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using AuditHistoryExtractorPro.Core.Models;
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

    private void DataGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is not DataGrid dataGrid)
        {
            return;
        }

        if (dataGrid.SelectedItem is not AuditExportRow row)
        {
            return;
        }

        // Only open diff if there is content to compare
        if (string.IsNullOrWhiteSpace(row.OldValue) && string.IsNullOrWhiteSpace(row.NewValue))
        {
            return;
        }

        var diffWindow = new DiffViewerWindow(
            row.OldValue ?? string.Empty,
            row.NewValue ?? string.Empty,
            row.ChangedField)
        {
            Owner = this
        };

        diffWindow.ShowDialog();
    }
}

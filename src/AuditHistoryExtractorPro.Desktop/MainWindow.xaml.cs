using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.ComponentModel;
using AuditHistoryExtractorPro.Core.Models;
using AuditHistoryExtractorPro.Desktop.ViewModels;

namespace AuditHistoryExtractorPro.Desktop;

/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow : Window
{
    private bool _syncingPassword;

    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        viewModel.PropertyChanged += OnViewModelPropertyChanged;
        ProfilePasswordBox.Password = viewModel.GetProfileCredential();
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is not MainViewModel vm)
        {
            return;
        }

        if (e.PropertyName == nameof(MainViewModel.ProfileCredential)
            && ProfilePasswordBox.Password != vm.GetProfileCredential())
        {
            _syncingPassword = true;
            ProfilePasswordBox.Password = vm.GetProfileCredential();
            _syncingPassword = false;
        }
    }

    private void ProfilePasswordBox_PasswordChanged(object sender, RoutedEventArgs e)
    {
        if (_syncingPassword)
        {
            return;
        }

        if (DataContext is MainViewModel vm && sender is PasswordBox passwordBox)
        {
            vm.UpdateProfileCredential(passwordBox.Password);
        }
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

    protected override void OnClosed(EventArgs e)
    {
        if (DataContext is MainViewModel vm)
        {
            vm.PropertyChanged -= OnViewModelPropertyChanged;
        }

        base.OnClosed(e);
    }
}

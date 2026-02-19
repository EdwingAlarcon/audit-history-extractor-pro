using AuditHistoryExtractorPro.Core.Services;
using AuditHistoryExtractorPro.Desktop.Services;
using AuditHistoryExtractorPro.Desktop.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using System.Windows;

namespace AuditHistoryExtractorPro.Desktop;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : System.Windows.Application
{
	private ServiceProvider? _serviceProvider;

	protected override void OnStartup(StartupEventArgs e)
	{
		base.OnStartup(e);

		var services = new ServiceCollection();
		ConfigureServices(services);

		_serviceProvider = services.BuildServiceProvider();

		var mainWindow = _serviceProvider.GetRequiredService<MainWindow>();
		mainWindow.Show();
	}

	protected override void OnExit(ExitEventArgs e)
	{
		_serviceProvider?.Dispose();
		base.OnExit(e);
	}

	private static void ConfigureServices(IServiceCollection services)
	{
		services.AddSingleton<AuthHelper>();
		services.AddSingleton<QueryBuilderService>();
		services.AddSingleton<IMetadataTranslationService, MetadataTranslationService>();
		services.AddSingleton<IExcelExportService, ExcelExportService>();
		services.AddSingleton<AuditService>();
		services.AddSingleton<IAuditService>(sp => sp.GetRequiredService<AuditService>());
		services.AddSingleton<IDataService, DataService>();
		services.AddSingleton<IMetadataService, MetadataService>();
		services.AddSingleton<ConnectionManagerService>();
		services.AddSingleton<MainViewModel>();
		services.AddSingleton<MainWindow>();
	}
}


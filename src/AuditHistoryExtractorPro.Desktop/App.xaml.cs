using AuditHistoryExtractorPro.Core.Services;
using AuditHistoryExtractorPro.Desktop.Services;
using AuditHistoryExtractorPro.Desktop.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Serilog;
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

		// ── SERILOG: configuración de archivo rodante ─────────────────────────
		var logDir = System.IO.Path.Combine(
			AppDomain.CurrentDomain.BaseDirectory, "Logs");
		System.IO.Directory.CreateDirectory(logDir);

		Log.Logger = new LoggerConfiguration()
			.MinimumLevel.Debug()
			.Enrich.FromLogContext()
			.WriteTo.File(
				path: System.IO.Path.Combine(logDir, "AuditExtractor-.txt"),
				rollingInterval: Serilog.RollingInterval.Day,
				retainedFileCountLimit: 14,
				outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{Level:u3}] {Message:lj}{NewLine}{Exception}")
			.CreateLogger();

		Log.Information("=== AuditHistoryExtractorPro iniciado ===");
		// ─────────────────────────────────────────────────────────────────────

		// ── MANEJADORES GLOBALES DE EXCEPCIONES NO CONTROLADAS ───────────────
		DispatcherUnhandledException += (_, args) =>
		{
			Log.Fatal(args.Exception, "[DispatcherUnhandledException] Excepción no controlada en el hilo UI");
			Log.CloseAndFlush();
			WriteEmergencyLog(args.Exception, "DispatcherUnhandledException");
			args.Handled = true;
		};

		AppDomain.CurrentDomain.UnhandledException += (_, args) =>
		{
			if (args.ExceptionObject is Exception ex)
			{
				Log.Fatal(ex, "[AppDomain.UnhandledException] Excepción no controlada en hilo de fondo");
				Log.CloseAndFlush();
				WriteEmergencyLog(ex, "AppDomain.UnhandledException");
			}
		};

		System.Threading.Tasks.TaskScheduler.UnobservedTaskException += (_, args) =>
		{
			Log.Error(args.Exception, "[UnobservedTaskException] Tarea no observada");
			WriteEmergencyLog(args.Exception, "UnobservedTaskException");
			args.SetObserved();
		};
		// ────────────────────────────────────────────────────────────────────

		var services = new ServiceCollection();
		ConfigureServices(services);

		_serviceProvider = services.BuildServiceProvider();

		var mainWindow = _serviceProvider.GetRequiredService<MainWindow>();
		mainWindow.Show();
	}

	private static void WriteEmergencyLog(Exception ex, string source)
	{
		try
		{
			var sb = new System.Text.StringBuilder();
			sb.AppendLine("=====================================================");
			sb.AppendLine($"AuditHistoryExtractorPro — Excepción No Controlada");
			sb.AppendLine($"Origen      : {source}");
			sb.AppendLine($"Fecha y Hora: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
			sb.AppendLine("=====================================================");
			sb.AppendLine();
			sb.AppendLine($"[Mensaje]   {ex.Message}");
			sb.AppendLine();
			sb.AppendLine($"[StackTrace]");
			sb.AppendLine(ex.StackTrace);

			if (ex.InnerException is { } inner)
			{
				sb.AppendLine();
				sb.AppendLine($"[InnerException.Mensaje]  {inner.Message}");
				sb.AppendLine($"[InnerException.StackTrace]");
				sb.AppendLine(inner.StackTrace);
			}

			var logPath = System.IO.Path.Combine(
				Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
				"AuditApp_ErrorLog.txt");

			System.IO.File.WriteAllText(logPath, sb.ToString(), System.Text.Encoding.UTF8);

			System.Windows.MessageBox.Show(
				$"La aplicación encontró un error inesperado ({source}).\n\n" +
				$"Mensaje: {ex.Message}\n\n" +
				$"Se ha generado un log detallado en tu Escritorio:\n{logPath}",
				"Error Inesperado — AuditHistoryExtractorPro",
				System.Windows.MessageBoxButton.OK,
				System.Windows.MessageBoxImage.Error);
		}
		catch { /* Si el propio log falla, no podemos hacer nada más */ }
	}

	protected override void OnExit(ExitEventArgs e)
	{
		Log.Information("=== AuditHistoryExtractorPro cerrando ===");
		Log.CloseAndFlush();
		_serviceProvider?.Dispose();
		base.OnExit(e);
	}

	private static void ConfigureServices(IServiceCollection services)
	{
		// ── Serilog como proveedor de ILogger<T> para todo el árbol DI ────────
		services.AddLogging(lb =>
		{
			lb.ClearProviders();
			lb.SetMinimumLevel(LogLevel.Debug);
			lb.AddSerilog(Log.Logger, dispose: false);
		});
		// ─────────────────────────────────────────────────────────────────────

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


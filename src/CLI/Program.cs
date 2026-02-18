using AuditHistoryExtractorPro.CLI.Commands;
using AuditHistoryExtractorPro.Services.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;
using System.CommandLine;

namespace AuditHistoryExtractorPro.CLI;

class Program
{
    static async Task<int> Main(string[] args)
    {
        // Configurar Serilog
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Information()
            .WriteTo.Console()
            .WriteTo.File("logs/audit-extractor-.log", rollingInterval: RollingInterval.Day)
            .CreateLogger();

        try
        {
            Log.Information("Audit History Extractor Pro - CLI");
            Log.Information("Version 1.0.0");
            Log.Information("=========================================");

            var host = CreateHostBuilder(args).Build();

            var rootCommand = new RootCommand("Audit History Extractor Pro - Extract and export Dataverse audit history");

            // Agregar comandos
            rootCommand.AddCommand(ExtractCommand.Create(host.Services));
            rootCommand.AddCommand(ExportCommand.Create(host.Services));
            rootCommand.AddCommand(CompareCommand.Create(host.Services));
            rootCommand.AddCommand(ConfigCommand.Create());
            rootCommand.AddCommand(ValidateCommand.Create(host.Services));

            return await rootCommand.InvokeAsync(args);
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "Application terminated unexpectedly");
            return 1;
        }
        finally
        {
            Log.CloseAndFlush();
        }
    }

    static IHostBuilder CreateHostBuilder(string[] args) =>
        Host.CreateDefaultBuilder(args)
            .UseSerilog()
            .ConfigureServices((context, services) =>
            {
                // Registro de servicios de infraestructura
                services.AddMemoryCache();
                services.AddMediatR(cfg => 
                    cfg.RegisterServicesFromAssembly(typeof(Application.UseCases.ExtractAudit.ExtractAuditCommand).Assembly));
                
                // Registro de servicios personalizados
                RegisterServices(services);
            });

    static void RegisterServices(IServiceCollection services)
    {
        // Interfaces del dominio
        services.AddSingleton(typeof(Models.ILogger<>), typeof(SerilogAdapter<>));
        services.AddSingleton<Models.ICacheService, MemoryCacheService>();
        services.AddSingleton<Models.IAuditProcessor, AuditProcessor>();

        // Servicios de exportación
        services.AddTransient<Services.Core.ExcelExportService>();
        services.AddTransient<Services.Core.CsvExportService>();
        services.AddTransient<Services.Core.JsonExportService>();
        services.AddTransient<Models.IExportService, Services.Core.CompositeExportService>();
    }
}

/// <summary>
/// Adaptador de Serilog a nuestra interfaz ILogger
/// </summary>
public class SerilogAdapter<T> : AuditHistoryExtractorPro.Models.ILogger<T>
{
    public void LogInformation(string message, params object[] args)
    {
        Log.Information(message, args);
    }

    public void LogWarning(string message, params object[] args)
    {
        Log.Warning(message, args);
    }

    public void LogError(Exception? exception, string message, params object[] args)
    {
        if (exception != null)
            Log.Error(exception, message, args);
        else
            Log.Error(message, args);
    }

    public void LogDebug(string message, params object[] args)
    {
        Log.Debug(message, args);
    }
}

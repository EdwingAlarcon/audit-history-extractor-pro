using AuditHistoryExtractorPro.Domain.Interfaces;
using AuditHistoryExtractorPro.Infrastructure.Services;
using AuditHistoryExtractorPro.Infrastructure.Services.Export;
using MudBlazor.Services;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

// Configurar Serilog
Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .WriteTo.Console()
    .WriteTo.File("logs/ui-.log", rollingInterval: RollingInterval.Day)
    .CreateLogger();

builder.Host.UseSerilog();

// Add services to the container.
builder.Services.AddRazorPages();
builder.Services.AddServerSideBlazor();
builder.Services.AddMudServices();

// Registro de servicios de la aplicación
builder.Services.AddMemoryCache();
builder.Services.AddSingleton(typeof(AuditHistoryExtractorPro.Domain.Interfaces.ILogger<>), typeof(SerilogAdapter<>));
builder.Services.AddSingleton<ICacheService, MemoryCacheService>();
builder.Services.AddSingleton<IAuditProcessor, AuditProcessor>();

// Servicios de exportación
builder.Services.AddTransient<ExcelExportService>();
builder.Services.AddTransient<CsvExportService>();
builder.Services.AddTransient<JsonExportService>();
builder.Services.AddTransient<IExportService, CompositeExportService>();

// MediatR
builder.Services.AddMediatR(cfg => 
    cfg.RegisterServicesFromAssembly(typeof(AuditHistoryExtractorPro.Application.UseCases.ExtractAudit.ExtractAuditCommand).Assembly));

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();

app.MapBlazorHub();
app.MapFallbackToPage("/_Host");

app.UseAntiforgery();

Log.Information("Audit History Extractor Pro UI starting...");
app.Run();

// Adaptador de Serilog
public class SerilogAdapter<T> : AuditHistoryExtractorPro.Domain.Interfaces.ILogger<T>
{
    public void LogInformation(string message, params object[] args) => Log.Information(message, args);
    public void LogWarning(string message, params object[] args) => Log.Warning(message, args);
    public void LogError(Exception? exception, string message, params object[] args)
    {
        if (exception != null) Log.Error(exception, message, args);
        else Log.Error(message, args);
    }
    public void LogDebug(string message, params object[] args) => Log.Debug(message, args);
}

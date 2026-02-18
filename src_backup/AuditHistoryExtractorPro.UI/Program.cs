using AuditHistoryExtractorPro.Domain.Interfaces;
using AuditHistoryExtractorPro.Domain.ValueObjects;
using AuditHistoryExtractorPro.Infrastructure.Services;
using AuditHistoryExtractorPro.Infrastructure.Services.Export;
using AuditHistoryExtractorPro.Infrastructure.Repositories;
using AuditHistoryExtractorPro.Infrastructure.Authentication;
using MudBlazor.Services;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

// Configurar Serilog desde appsettings.json
Log.Logger = new LoggerConfiguration()
    .ReadFrom.Configuration(builder.Configuration)
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

// Configuración de Dataverse desde appsettings.json
var dataverseConfig = builder.Configuration.GetSection("Dataverse");
var authConfig = new AuthenticationConfiguration
{
    EnvironmentUrl = dataverseConfig["EnvironmentUrl"] ?? throw new InvalidOperationException("Dataverse:EnvironmentUrl not configured"),
    TenantId = dataverseConfig["TenantId"],
    ClientId = dataverseConfig["ClientId"],
    ClientSecret = dataverseConfig["ClientSecret"],
    CertificatePath = dataverseConfig["CertificatePath"],
    CertificateThumbprint = dataverseConfig["CertificateThumbprint"],
    UseManagedIdentity = bool.Parse(dataverseConfig["UseManagedIdentity"] ?? "false"),
    Type = Enum.Parse<AuthenticationType>(dataverseConfig["Type"] ?? "OAuth2", ignoreCase: true)
};

builder.Services.AddSingleton(authConfig);

// Registro de proveedor de autenticación usando la factory
builder.Services.AddSingleton<IAuthenticationProvider>(sp =>
{
    var config = sp.GetRequiredService<AuthenticationConfiguration>();
    
    // Crear proveedores usando las firmas correctas con el ILogger del dominio
    return config.Type switch
    {
        AuthenticationType.OAuth2 => new OAuth2AuthenticationProvider(config,
            sp.GetRequiredService<AuditHistoryExtractorPro.Domain.Interfaces.ILogger<OAuth2AuthenticationProvider>>()),
        AuthenticationType.ClientSecret => new ClientSecretAuthenticationProvider(config, null, 
            sp.GetRequiredService<AuditHistoryExtractorPro.Domain.Interfaces.ILogger<ClientSecretAuthenticationProvider>>()),
        AuthenticationType.Certificate => new CertificateAuthenticationProvider(config, 
            sp.GetRequiredService<AuditHistoryExtractorPro.Domain.Interfaces.ILogger<CertificateAuthenticationProvider>>()),
        AuthenticationType.ManagedIdentity => new ManagedIdentityAuthenticationProvider(config, 
            sp.GetRequiredService<AuditHistoryExtractorPro.Domain.Interfaces.ILogger<ManagedIdentityAuthenticationProvider>>()),
        _ => throw new NotSupportedException($"Authentication type {config.Type} is not supported")
    };
});

// Registro del repositorio de auditoría
builder.Services.AddSingleton<IAuditRepository, DataverseAuditRepository>();

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

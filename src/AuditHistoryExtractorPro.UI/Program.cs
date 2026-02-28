using AuditHistoryExtractorPro.Domain.Interfaces;
using AuditHistoryExtractorPro.Domain.ValueObjects;
using AuditHistoryExtractorPro.Infrastructure.Services;
using Microsoft.Extensions.Logging;
using AuditHistoryExtractorPro.Infrastructure.Repositories;
using AuditHistoryExtractorPro.Infrastructure.Authentication;
using AuditHistoryExtractorPro.UI.Services;
using CoreAuditServiceInterface = AuditHistoryExtractorPro.Core.Services.IAuditService;
using CoreAuditService = AuditHistoryExtractorPro.Core.Services.AuditService;
using CoreAuthHelper = AuditHistoryExtractorPro.Core.Services.AuthHelper;
using CoreQueryBuilderService = AuditHistoryExtractorPro.Core.Services.QueryBuilderService;
using CoreExcelExportServiceInterface = AuditHistoryExtractorPro.Core.Services.IExcelExportService;
using CoreExcelExportService = AuditHistoryExtractorPro.Core.Services.ExcelExportService;
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
builder.Services.AddSingleton<ICacheService, MemoryCacheService>();
builder.Services.AddSingleton<IAuditProcessor, AuditProcessor>();
builder.Services.AddScoped<AuditSessionState>();
builder.Services.AddSingleton<HistoryViewService>();
builder.Services.AddSingleton<ExtractViewService>();
builder.Services.AddSingleton<ExportViewService>();
builder.Services.AddSingleton<IUserConfigService, UserConfigService>();
builder.Services.AddSingleton<CoreAuthHelper>();
builder.Services.AddSingleton<CoreQueryBuilderService>();
builder.Services.AddSingleton<CoreExcelExportServiceInterface, CoreExcelExportService>();
// Scoped: cada sesión de Blazor Server obtiene su propia instancia con estado de conexión aislado.
builder.Services.AddScoped<CoreAuditServiceInterface, CoreAuditService>();
builder.Services.AddScoped<ExtractPageCoordinator>();
builder.Services.AddScoped<IAuditService, AuditService>();
builder.Services.AddScoped<ExportPageCoordinator>();
builder.Services.AddScoped<HistoryPageCoordinator>();

// Registro del servicio de traducción de metadatos
builder.Services.AddSingleton<AuditHistoryExtractorPro.Core.Services.IMetadataTranslationService, AuditHistoryExtractorPro.Core.Services.MetadataTranslationService>();

// Configuración de Dataverse desde appsettings.json
var dataverseConfig = builder.Configuration.GetSection("Dataverse");
var configuredEnvironmentUrl = dataverseConfig["EnvironmentUrl"];
var environmentUrl = string.IsNullOrWhiteSpace(configuredEnvironmentUrl)
    ? "https://yourorg.crm.dynamics.com"
    : configuredEnvironmentUrl;

if (string.IsNullOrWhiteSpace(configuredEnvironmentUrl))
{
    Log.Warning("Dataverse:EnvironmentUrl no configurado. Se usará URL placeholder para permitir arranque local.");
}

var useManagedIdentity = bool.TryParse(dataverseConfig["UseManagedIdentity"], out var parsedUseManagedIdentity)
    ? parsedUseManagedIdentity
    : false;

var authenticationType = Enum.TryParse<AuthenticationType>(
    dataverseConfig["Type"],
    ignoreCase: true,
    out var parsedAuthType)
        ? parsedAuthType
        : AuthenticationType.OAuth2;

var authConfig = new AuthenticationConfiguration
{
    EnvironmentUrl = environmentUrl,
    TenantId = dataverseConfig["TenantId"],
    ClientId = dataverseConfig["ClientId"],
    ClientSecret = dataverseConfig["ClientSecret"],
    CertificatePath = dataverseConfig["CertificatePath"],
    CertificateThumbprint = dataverseConfig["CertificateThumbprint"],
    UseManagedIdentity = useManagedIdentity,
    Type = authenticationType
};

builder.Services.AddSingleton(authConfig);

// Registro de proveedor de autenticación usando la factory
builder.Services.AddSingleton<IAuthenticationProvider>(sp =>
{
    var config = sp.GetRequiredService<AuthenticationConfiguration>();
    
    return config.Type switch
    {
        AuthenticationType.OAuth2 => new OAuth2AuthenticationProvider(config,
            sp.GetRequiredService<ILogger<OAuth2AuthenticationProvider>>()),
        AuthenticationType.ClientSecret => new ClientSecretAuthenticationProvider(config, null,
            sp.GetRequiredService<ILogger<ClientSecretAuthenticationProvider>>()),
        AuthenticationType.Certificate => new CertificateAuthenticationProvider(config,
            sp.GetRequiredService<ILogger<CertificateAuthenticationProvider>>()),
        AuthenticationType.ManagedIdentity => new ManagedIdentityAuthenticationProvider(config,
            sp.GetRequiredService<ILogger<ManagedIdentityAuthenticationProvider>>()),
        _ => throw new NotSupportedException($"Authentication type {config.Type} is not supported")
    };
});

// Registro del repositorio de auditoría
// DataverseAuditRepository implementa tanto IAuditRepository como ISyncStateStore.
// Se registra la instancia concreta para que ambas interfaces compartan el mismo Singleton.
builder.Services.AddSingleton<DataverseAuditRepository>();
builder.Services.AddSingleton<IAuditRepository>(sp => sp.GetRequiredService<DataverseAuditRepository>());
builder.Services.AddSingleton<ISyncStateStore>(sp => sp.GetRequiredService<DataverseAuditRepository>());

// Servicios de exportación
builder.Services.AddTransient<ExcelExportService>();
builder.Services.AddTransient<CsvExportService>();
builder.Services.AddTransient<JsonExportService>();
builder.Services.AddTransient<IExportService>(sp =>
    new CompositeExportService(
        new IExportService[]
        {
            sp.GetRequiredService<ExcelExportService>(),
            sp.GetRequiredService<CsvExportService>(),
            sp.GetRequiredService<JsonExportService>()
        },
        sp.GetRequiredService<ILogger<CompositeExportService>>()));

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

// SerilogAdapter eliminado: builder.Host.UseSerilog() ya registra Serilog como
// proveedor de Microsoft.Extensions.Logging.ILogger<T> para toda la aplicación.

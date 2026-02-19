using AuditHistoryExtractorPro.Domain.ValueObjects;

namespace AuditHistoryExtractorPro.UI.Services;

public class ExportViewService
{
    public ExportConfigurationBuildResult BuildConfiguration(ExportInputModel input)
    {
        ArgumentNullException.ThrowIfNull(input);

        if (string.IsNullOrWhiteSpace(input.FileName))
        {
            return ExportConfigurationBuildResult.Fail("⚠️ Por favor ingrese un nombre para el archivo.");
        }

        if (input.TotalRecords <= 0)
        {
            return ExportConfigurationBuildResult.Fail("⚠️ No hay registros para exportar. Realiza una extracción primero.");
        }

        var normalizedFileName = input.FileName.Trim();
        if (normalizedFileName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            return ExportConfigurationBuildResult.Fail("⚠️ El nombre del archivo contiene caracteres inválidos.");
        }

        var configuration = new ExportConfiguration
        {
            Format = input.Format switch
            {
                "Excel" => ExportFormat.Excel,
                "CSV" => ExportFormat.Csv,
                "JSON" => ExportFormat.Json,
                _ => ExportFormat.Excel
            },
            OutputPath = string.IsNullOrWhiteSpace(input.OutputPath)
                ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "AuditExports")
                : input.OutputPath.Trim(),
            FileName = normalizedFileName,
            CompressOutput = input.CompressFile,
            IncludeTimestamp = true
        };

        return ExportConfigurationBuildResult.Ok(configuration);
    }
}

public class ExportInputModel
{
    public string Format { get; init; } = "Excel";
    public string FileName { get; init; } = string.Empty;
    public string OutputPath { get; init; } = string.Empty;
    public bool CompressFile { get; init; }
    public int TotalRecords { get; init; }
}

public class ExportConfigurationBuildResult
{
    public bool Success { get; init; }
    public string? ErrorMessage { get; init; }
    public ExportConfiguration? Configuration { get; init; }

    public static ExportConfigurationBuildResult Ok(ExportConfiguration configuration)
    {
        return new ExportConfigurationBuildResult
        {
            Success = true,
            Configuration = configuration
        };
    }

    public static ExportConfigurationBuildResult Fail(string errorMessage)
    {
        return new ExportConfigurationBuildResult
        {
            Success = false,
            ErrorMessage = errorMessage
        };
    }
}

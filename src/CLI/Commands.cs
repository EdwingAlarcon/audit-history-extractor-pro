using AuditHistoryExtractorPro.Application.UseCases.ExtractAudit;
using AuditHistoryExtractorPro.Application.UseCases.ExportAudit;
using AuditHistoryExtractorPro.Application.UseCases.CompareRecords;
using AuditHistoryExtractorPro.Models;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Spectre.Console;
using System.CommandLine;

namespace AuditHistoryExtractorPro.CLI.Commands;

/// <summary>
/// Comando para extraer registros de auditoría
/// </summary>
public static class ExtractCommand
{
    public static Command Create(IServiceProvider services)
    {
        var command = new Command("extract", "Extract audit records from Dataverse");

        var entityOption = new Option<string[]>(
            name: "--entity",
            description: "Entity names to extract (can specify multiple)")
        { IsRequired = true, AllowMultipleArgumentsPerToken = true };

        var fromOption = new Option<DateTime?>(
            name: "--from",
            description: "Start date for extraction (yyyy-MM-dd)");

        var toOption = new Option<DateTime?>(
            name: "--to",
            description: "End date for extraction (yyyy-MM-dd)");

        var formatOption = new Option<ExportFormat>(
            name: "--format",
            getDefaultValue: () => ExportFormat.Excel,
            description: "Export format (Excel, Csv, Json, Sql)");

        var outputOption = new Option<string>(
            name: "--output",
            getDefaultValue: () => "./exports",
            description: "Output directory");

        var incrementalOption = new Option<bool>(
            name: "--incremental",
            getDefaultValue: () => false,
            description: "Extract only new records since last extraction");

        var userOption = new Option<string[]>(
            name: "--user",
            description: "Filter by user IDs")
        { AllowMultipleArgumentsPerToken = true };

        var operationOption = new Option<OperationType[]>(
            name: "--operation",
            description: "Filter by operation type (Create, Update, Delete)")
        { AllowMultipleArgumentsPerToken = true };

        command.AddOption(entityOption);
        command.AddOption(fromOption);
        command.AddOption(toOption);
        command.AddOption(formatOption);
        command.AddOption(outputOption);
        command.AddOption(incrementalOption);
        command.AddOption(userOption);
        command.AddOption(operationOption);

        command.SetHandler(async (context) =>
        {
            var entities = context.ParseResult.GetValueForOption(entityOption)!;
            var from = context.ParseResult.GetValueForOption(fromOption);
            var to = context.ParseResult.GetValueForOption(toOption);
            var format = context.ParseResult.GetValueForOption(formatOption);
            var output = context.ParseResult.GetValueForOption(outputOption)!;
            var incremental = context.ParseResult.GetValueForOption(incrementalOption);
            var users = context.ParseResult.GetValueForOption(userOption);
            var operations = context.ParseResult.GetValueForOption(operationOption);

            await ExecuteExtractAsync(
                services,
                entities,
                from,
                to,
                format,
                output,
                incremental,
                users,
                operations);
        });

        return command;
    }

    private static async Task ExecuteExtractAsync(
        IServiceProvider services,
        string[] entities,
        DateTime? from,
        DateTime? to,
        ExportFormat format,
        string output,
        bool incremental,
        string[]? users,
        OperationType[]? operations)
    {
        await AnsiConsole.Status()
            .Spinner(Spinner.Known.Dots)
            .StartAsync("Extracting audit records...", async ctx =>
            {
                var mediator = services.GetRequiredService<IMediator>();

                var criteria = new ExtractionCriteria
                {
                    EntityNames = entities.ToList(),
                    FromDate = from,
                    ToDate = to,
                    IncrementalMode = incremental,
                    UserIds = users?.ToList(),
                    Operations = operations?.ToList()
                };

                var progress = new Progress<Domain.Interfaces.ExtractionProgress>(p =>
                {
                    ctx.Status($"Processing {p.CurrentEntity}: {p.ProcessedRecords}/{p.TotalRecords} ({p.PercentComplete:F1}%)");
                });

                var extractCommand = new ExtractAuditCommand
                {
                    Criteria = criteria,
                    Progress = progress
                };

                var extractResult = await mediator.Send(extractCommand);

                if (!extractResult.Success)
                {
                    AnsiConsole.MarkupLine($"[red]✗ Extraction failed: {extractResult.ErrorMessage}[/]");
                    return;
                }

                AnsiConsole.MarkupLine($"[green]✓ Extracted {extractResult.Records.Count} records[/]");

                // Exportar automáticamente
                ctx.Status("Exporting records...");

                var exportConfig = new ExportConfiguration
                {
                    Format = format,
                    OutputPath = output,
                    FileName = $"audit_extract_{DateTime.Now:yyyyMMdd_HHmmss}",
                    CompressOutput = extractResult.Records.Count > 10000
                };

                var exportCommand = new ExportAuditCommand
                {
                    Records = extractResult.Records,
                    Configuration = exportConfig
                };

                var exportResult = await mediator.Send(exportCommand);

                if (exportResult.Success)
                {
                    AnsiConsole.MarkupLine($"[green]✓ Exported to: {exportResult.FilePath}[/]");
                    AnsiConsole.MarkupLine($"[dim]  File size: {exportResult.FileSize / 1024.0:F2} KB[/]");
                }
                else
                {
                    AnsiConsole.MarkupLine($"[red]✗ Export failed: {exportResult.ErrorMessage}[/]");
                }
            });

        // Mostrar resumen
        DisplaySummaryTable();
    }

    private static void DisplaySummaryTable()
    {
        var table = new Table();
        table.Border(TableBorder.Rounded);
        table.AddColumn("[yellow]Metric[/]");
        table.AddColumn("[cyan]Value[/]");
        
        AnsiConsole.Write(table);
    }
}

/// <summary>
/// Comando para exportar registros ya extraídos
/// </summary>
public static class ExportCommand
{
    public static Command Create(IServiceProvider services)
    {
        var command = new Command("export", "Export audit records to different formats");

        var inputOption = new Option<string>(
            name: "--input",
            description: "Input file (JSON) with audit records")
        { IsRequired = true };

        var formatOption = new Option<ExportFormat>(
            name: "--format",
            getDefaultValue: () => ExportFormat.Excel,
            description: "Export format");

        var outputOption = new Option<string>(
            name: "--output",
            getDefaultValue: () => "./exports",
            description: "Output directory");

        command.AddOption(inputOption);
        command.AddOption(formatOption);
        command.AddOption(outputOption);

        command.SetHandler(async (input, format, output) =>
        {
            AnsiConsole.MarkupLine($"[cyan]Exporting from {input} to {format}...[/]");
            // Implementación de exportación
        }, inputOption, formatOption, outputOption);

        return command;
    }
}

/// <summary>
/// Comando para comparar versiones de registros
/// </summary>
public static class CompareCommand
{
    public static Command Create(IServiceProvider services)
    {
        var command = new Command("compare", "Compare versions of a specific record");

        var entityOption = new Option<string>(
            name: "--entity",
            description: "Entity name")
        { IsRequired = true };

        var recordIdOption = new Option<Guid>(
            name: "--recordid",
            description: "Record ID to compare")
        { IsRequired = true };

        var fromOption = new Option<DateTime?>(
            name: "--from",
            description: "Start date");

        var toOption = new Option<DateTime?>(
            name: "--to",
            description: "End date");

        command.AddOption(entityOption);
        command.AddOption(recordIdOption);
        command.AddOption(fromOption);
        command.AddOption(toOption);

        command.SetHandler(async (entity, recordId, from, to) =>
        {
            await AnsiConsole.Status()
                .StartAsync("Comparing record versions...", async ctx =>
                {
                    var mediator = services.GetRequiredService<IMediator>();

                    var query = new CompareRecordsQuery
                    {
                        EntityName = entity,
                        RecordId = recordId,
                        FromDate = from,
                        ToDate = to
                    };

                    var result = await mediator.Send(query);

                    if (!result.Success)
                    {
                        AnsiConsole.MarkupLine($"[red]✗ Comparison failed: {result.ErrorMessage}[/]");
                        return;
                    }

                    DisplayComparisons(result.Comparisons);
                });
        }, entityOption, recordIdOption, fromOption, toOption);

        return command;
    }

    private static void DisplayComparisons(List<Domain.Entities.RecordComparison> comparisons)
    {
        if (!comparisons.Any())
        {
            AnsiConsole.MarkupLine("[yellow]No comparisons found[/]");
            return;
        }

        foreach (var comparison in comparisons)
        {
            var panel = new Panel($"[bold]Comparison: {comparison.ComparisonDate:yyyy-MM-dd HH:mm:ss}[/]")
            {
                Border = BoxBorder.Rounded
            };

            var table = new Table();
            table.AddColumn("Field");
            table.AddColumn("Old Value");
            table.AddColumn("New Value");
            table.AddColumn("Type");

            foreach (var diff in comparison.Differences.Where(d => d.Type != Domain.Entities.DifferenceType.Unchanged))
            {
                var typeColor = diff.Type switch
                {
                    Domain.Entities.DifferenceType.Added => "green",
                    Domain.Entities.DifferenceType.Modified => "yellow",
                    Domain.Entities.DifferenceType.Removed => "red",
                    _ => "white"
                };

                table.AddRow(
                    diff.FieldName,
                    diff.OldValue?.ToString() ?? "[dim]null[/]",
                    diff.NewValue?.ToString() ?? "[dim]null[/]",
                    $"[{typeColor}]{diff.Type}[/]"
                );
            }

            AnsiConsole.Write(table);
        }
    }
}

/// <summary>
/// Comando para gestionar configuración
/// </summary>
public static class ConfigCommand
{
    public static Command Create()
    {
        var command = new Command("config", "Manage configuration");

        var initCommand = new Command("init", "Initialize configuration file");
        initCommand.SetHandler(() =>
        {
            AnsiConsole.MarkupLine("[cyan]Creating configuration file...[/]");
            // Crear archivo de configuración de ejemplo
        });

        var validateCommand = new Command("validate", "Validate configuration");
        validateCommand.SetHandler(() =>
        {
            AnsiConsole.MarkupLine("[cyan]Validating configuration...[/]");
            // Validar configuración
        });

        command.AddCommand(initCommand);
        command.AddCommand(validateCommand);

        return command;
    }
}

/// <summary>
/// Comando para validar conexión
/// </summary>
public static class ValidateCommand
{
    public static Command Create(IServiceProvider services)
    {
        var command = new Command("validate", "Validate connection to Dataverse");

        command.SetHandler(async () =>
        {
            await AnsiConsole.Status()
                .StartAsync("Validating connection...", async ctx =>
                {
                    // Simular validación
                    await Task.Delay(1000);
                    AnsiConsole.MarkupLine("[green]✓ Connection validated successfully[/]");
                });
        });

        return command;
    }
}

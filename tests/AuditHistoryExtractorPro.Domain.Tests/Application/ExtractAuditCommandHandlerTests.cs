using AuditHistoryExtractorPro.Application.UseCases.ExtractAudit;
using AuditHistoryExtractorPro.Domain.Entities;
using AuditHistoryExtractorPro.Domain.Interfaces;
using AuditHistoryExtractorPro.Domain.ValueObjects;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Xunit;

namespace AuditHistoryExtractorPro.Domain.Tests.Application;

/// <summary>
/// Tests unitarios para ExtractAuditCommandHandler.
/// Verifica que el handler orqueste correctamente el repositorio, el procesador
/// y el almacén de estado de sincronización, sin depender de infraestructura real.
/// </summary>
public class ExtractAuditCommandHandlerTests
{
    private readonly IAuditRepository _auditRepository = Substitute.For<IAuditRepository>();
    private readonly IAuditProcessor _auditProcessor = Substitute.For<IAuditProcessor>();
    private readonly ICacheService _cacheService = Substitute.For<ICacheService>();
    private readonly ISyncStateStore _syncStateStore = Substitute.For<ISyncStateStore>();
    private readonly ILogger<ExtractAuditCommandHandler> _logger =
        Substitute.For<ILogger<ExtractAuditCommandHandler>>();

    private ExtractAuditCommandHandler CreateHandler() => new(
        _auditRepository,
        _auditProcessor,
        _cacheService,
        _syncStateStore,
        _logger);

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static ExtractionCriteria ValidCriteria(bool incremental = false) => new()
    {
        EntityNames = new List<string> { "account" },
        PageSize = 500,
        IncrementalMode = incremental
    };

    private static List<AuditRecord> SomeRecords(int count = 3) =>
        Enumerable.Range(0, count)
            .Select(_ => new AuditRecord
            {
                AuditId = Guid.NewGuid(),
                EntityName = "account",
                Operation = "Update"
            })
            .ToList();

    // ── Happy path ────────────────────────────────────────────────────────────

    [Fact]
    public async Task Handle_ShouldReturnSuccess_WhenExtractionSucceeds()
    {
        // Arrange
        var records = SomeRecords(5);
        _auditRepository
            .ExtractAuditRecordsAsync(Arg.Any<ExtractionCriteria>(), Arg.Any<IProgress<ExtractionProgress>>(), Arg.Any<CancellationToken>())
            .Returns(records);
        _auditProcessor
            .NormalizeRecordsAsync(records, Arg.Any<CancellationToken>())
            .Returns(records);
        _auditProcessor
            .EnrichRecordsAsync(records, Arg.Any<CancellationToken>())
            .Returns(records);

        var handler = CreateHandler();
        var command = new ExtractAuditCommand { Criteria = ValidCriteria() };

        // Act
        var response = await handler.Handle(command, CancellationToken.None);

        // Assert
        response.Success.Should().BeTrue();
        response.Records.Should().HaveCount(5);
        response.ErrorMessage.Should().BeNull();
    }

    [Fact]
    public async Task Handle_ShouldReturnFailure_WhenRepositoryThrows()
    {
        // Arrange
        _auditRepository
            .ExtractAuditRecordsAsync(Arg.Any<ExtractionCriteria>(), Arg.Any<IProgress<ExtractionProgress>>(), Arg.Any<CancellationToken>())
            .Throws(new InvalidOperationException("Dataverse unreachable"));

        var handler = CreateHandler();
        var command = new ExtractAuditCommand { Criteria = ValidCriteria() };

        // Act
        var response = await handler.Handle(command, CancellationToken.None);

        // Assert
        response.Success.Should().BeFalse();
        response.Records.Should().BeEmpty();
        response.ErrorMessage.Should().Contain("Dataverse unreachable");
    }

    [Fact]
    public async Task Handle_ShouldThrow_WhenCriteriaIsInvalid()
    {
        // Arrange: EntityNames vacío → Validate() lanza ArgumentException
        var handler = CreateHandler();
        var command = new ExtractAuditCommand
        {
            Criteria = new ExtractionCriteria { EntityNames = new List<string>() }
        };

        // Act
        var response = await handler.Handle(command, CancellationToken.None);

        // Assert: el handler captura la excepción y devuelve Failure en lugar de propagarla
        response.Success.Should().BeFalse();
        response.ErrorMessage.Should().Contain("entity name");
    }

    // ── ISyncStateStore integration ───────────────────────────────────────────

    [Fact]
    public async Task Handle_ShouldConsult_SyncStateStore_WhenIncrementalMode()
    {
        // Arrange
        var lastDate = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        _syncStateStore
            .GetLastExtractionDateAsync("account", Arg.Any<CancellationToken>())
            .Returns(lastDate);

        var records = SomeRecords();
        _auditRepository
            .ExtractAuditRecordsAsync(Arg.Any<ExtractionCriteria>(), Arg.Any<IProgress<ExtractionProgress>>(), Arg.Any<CancellationToken>())
            .Returns(records);
        _auditProcessor.NormalizeRecordsAsync(records, Arg.Any<CancellationToken>()).Returns(records);
        _auditProcessor.EnrichRecordsAsync(records, Arg.Any<CancellationToken>()).Returns(records);

        var handler = CreateHandler();
        var command = new ExtractAuditCommand { Criteria = ValidCriteria(incremental: true) };

        // Act
        await handler.Handle(command, CancellationToken.None);

        // Assert: el handler consultó la fecha incremental en el syncStateStore, no en el repositorio
        await _syncStateStore.Received(1).GetLastExtractionDateAsync("account", Arg.Any<CancellationToken>());
        await _syncStateStore.Received(1).SaveLastExtractionDateAsync("account", Arg.Any<DateTime>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ShouldNot_AccessSyncStateStore_WhenNotIncrementalMode()
    {
        // Arrange
        var records = SomeRecords();
        _auditRepository
            .ExtractAuditRecordsAsync(Arg.Any<ExtractionCriteria>(), Arg.Any<IProgress<ExtractionProgress>>(), Arg.Any<CancellationToken>())
            .Returns(records);
        _auditProcessor.NormalizeRecordsAsync(records, Arg.Any<CancellationToken>()).Returns(records);
        _auditProcessor.EnrichRecordsAsync(records, Arg.Any<CancellationToken>()).Returns(records);

        var handler = CreateHandler();
        var command = new ExtractAuditCommand { Criteria = ValidCriteria(incremental: false) };

        // Act
        await handler.Handle(command, CancellationToken.None);

        // Assert: ISyncStateStore no debe tocarse si no es modo incremental
        await _syncStateStore.DidNotReceive().GetLastExtractionDateAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _syncStateStore.DidNotReceive().SaveLastExtractionDateAsync(Arg.Any<string>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>());
    }

    // ── Resultado ExtractionResult ────────────────────────────────────────────

    [Fact]
    public async Task Handle_ShouldPopulateExtractionResult_WithCorrectCounts()
    {
        // Arrange
        var records = SomeRecords(7);
        _auditRepository
            .ExtractAuditRecordsAsync(Arg.Any<ExtractionCriteria>(), Arg.Any<IProgress<ExtractionProgress>>(), Arg.Any<CancellationToken>())
            .Returns(records);
        _auditProcessor.NormalizeRecordsAsync(records, Arg.Any<CancellationToken>()).Returns(records);
        _auditProcessor.EnrichRecordsAsync(records, Arg.Any<CancellationToken>()).Returns(records);

        var handler = CreateHandler();
        var command = new ExtractAuditCommand { Criteria = ValidCriteria() };

        // Act
        var response = await handler.Handle(command, CancellationToken.None);

        // Assert
        response.Result.Should().NotBeNull();
        response.Result.RecordsExtracted.Should().Be(7);
        response.Result.Success.Should().BeTrue();
        response.Result.StartTime.Should().BeBefore(response.Result.EndTime);
    }
}

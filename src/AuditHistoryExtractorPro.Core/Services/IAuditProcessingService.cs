using Microsoft.Xrm.Sdk;

namespace AuditHistoryExtractorPro.Core.Services;

public readonly record struct IntersectionMetrics(
    long Processed,
    long Matched,
    long Discarded,
    long ElapsedMilliseconds,
    int Batches);

public interface IAuditProcessingService
{
    Task<(IReadOnlyList<Entity> MatchedEntities, IntersectionMetrics Metrics)> IntersectPageAsync(
        IReadOnlyList<Entity> pageEntities,
        HashSet<Guid>? viewIdsHash,
        IProgress<int>? progress,
        CancellationToken cancellationToken,
        int batchSize = 1024,
        int progressStep = 500,
        int maxDegreeOfParallelism = 0);
}

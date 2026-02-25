using Microsoft.Xrm.Sdk;
using Serilog;
using Serilog.Context;
using System.Collections.Concurrent;
using System.Diagnostics;

namespace AuditHistoryExtractorPro.Core.Services;

public sealed class AuditProcessingService : IAuditProcessingService
{
    public Task<(IReadOnlyList<Entity> MatchedEntities, IntersectionMetrics Metrics)> IntersectPageAsync(
        IReadOnlyList<Entity> pageEntities,
        HashSet<Guid>? viewIdsHash,
        IProgress<int>? progress,
        CancellationToken cancellationToken,
        int batchSize = 1024,
        int progressStep = 500,
        int maxDegreeOfParallelism = 0)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (pageEntities.Count == 0)
        {
            return Task.FromResult<(IReadOnlyList<Entity>, IntersectionMetrics)>(
                (Array.Empty<Entity>(), new IntersectionMetrics(0, 0, 0, 0, 0)));
        }

        // Si no hay filtro de vista, devolvemos la página intacta sin costo adicional.
        if (viewIdsHash is null)
        {
            var passthrough = new List<Entity>(pageEntities.Count);
            passthrough.AddRange(pageEntities);
            return Task.FromResult<(IReadOnlyList<Entity>, IntersectionMetrics)>(
                (passthrough, new IntersectionMetrics(pageEntities.Count, pageEntities.Count, 0, 0, 1)));
        }

        var effectiveBatchSize = Math.Clamp(batchSize, 128, 5000);
        var effectiveProgressStep = Math.Max(1, progressStep);
        var effectiveDop = maxDegreeOfParallelism > 0
            ? maxDegreeOfParallelism
            : Environment.ProcessorCount;

        long processed = 0;
        long matched = 0;
        long discarded = 0;
        var batchCount = 0;

        var timer = Stopwatch.StartNew();

        using var ctxA = LogContext.PushProperty("PageEntityCount", pageEntities.Count);
        using var ctxB = LogContext.PushProperty("ViewIdsCount", viewIdsHash.Count);
        using var ctxC = LogContext.PushProperty("BatchSize", effectiveBatchSize);
        using var ctxD = LogContext.PushProperty("MaxDegreeOfParallelism", effectiveDop);

        var result = new List<Entity>(Math.Min(pageEntities.Count, viewIdsHash.Count));

        for (var offset = 0; offset < pageEntities.Count; offset += effectiveBatchSize)
        {
            cancellationToken.ThrowIfCancellationRequested();
            batchCount++;

            var count = Math.Min(effectiveBatchSize, pageEntities.Count - offset);
            var localMatches = new ConcurrentBag<Entity>();

            var options = new ParallelOptions
            {
                CancellationToken = cancellationToken,
                MaxDegreeOfParallelism = effectiveDop
            };

            Parallel.For(0, count, options, i =>
            {
                var entity = pageEntities[offset + i];
                var auditObjectId = entity.GetAttributeValue<EntityReference>("objectid")?.Id ?? Guid.Empty;

                Interlocked.Increment(ref processed);

                if (auditObjectId == Guid.Empty || !viewIdsHash.Contains(auditObjectId))
                {
                    Interlocked.Increment(ref discarded);
                    return;
                }

                localMatches.Add(entity);
                var currentMatched = Interlocked.Increment(ref matched);
                if (progress is not null && (currentMatched == 1 || currentMatched % effectiveProgressStep == 0))
                {
                    progress.Report((int)Math.Min(currentMatched, int.MaxValue));
                }
            });

            if (!localMatches.IsEmpty)
            {
                result.AddRange(localMatches);
            }
        }

        timer.Stop();

        var metrics = new IntersectionMetrics(
            Processed: processed,
            Matched: matched,
            Discarded: discarded,
            ElapsedMilliseconds: timer.ElapsedMilliseconds,
            Batches: batchCount);

        Log.Information(
            "[AuditProcessingService] Intersección completada en {ElapsedMs}ms | Processed={Processed} Matched={Matched} Discarded={Discarded} Batches={Batches}",
            metrics.ElapsedMilliseconds,
            metrics.Processed,
            metrics.Matched,
            metrics.Discarded,
            metrics.Batches);

        return Task.FromResult<(IReadOnlyList<Entity>, IntersectionMetrics)>((result, metrics));
    }
}

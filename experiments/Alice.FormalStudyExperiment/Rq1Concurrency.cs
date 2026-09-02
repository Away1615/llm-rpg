using System.Diagnostics;
using System.Net;
using System.Text.Json;
using Alice.Activities;
using Alice.Actors;
using Alice.Authority;
using Alice.Capabilities;
using Alice.Cognition;
using Alice.Commitments;
using Alice.Damage;
using Alice.Execution;
using Alice.Interaction;
using Alice.Items;
using Alice.LivingTown;
using Alice.Memory;
using Alice.ModelRuntime;
using Alice.Navigation;
using Alice.Npc;
using Alice.Perception;
using Alice.ProductRuntime;
using Alice.Social;
using Alice.Validation;
using Alice.World;

// Engineering-only RQ1 concurrency infrastructure.
//
// This file is deliberately independent of RQ1's admission/ranking/scoring domain logic. It
// exists to make RQ1 matched-pair execution safely concurrent (worker count configurable,
// pair -> worker mapping fixed and deterministic, no intra-pair concurrency, no shared mutable
// credential env-var state, thread-safe shared writers) and to prove that machinery end-to-end
// against the 30-distinct-block x 1-pair RQ1 fixture shape using a fake/delayed provider - never a real
// network call.

/// <summary>The frozen RQ1 redesign contains thirty distinct blocks and one matched pair per block.</summary>
internal static class Rq1ExperimentShape
{
    public const int BlockCount = 30;
    public const int PairsPerBlock = 1;
    public const int PairCount = BlockCount * PairsPerBlock;

    public static int BlockNumberForPair(int zeroBasedPairIndex)
    {
        if (zeroBasedPairIndex < 0 || zeroBasedPairIndex >= PairCount)
            throw new ArgumentOutOfRangeException(nameof(zeroBasedPairIndex));
        return zeroBasedPairIndex + 1;
    }

    public static void ValidateThirtyDistinctBlockSuite(
        IReadOnlyList<FormalExperimentSuitePairEntry> pairs)
    {
        ArgumentNullException.ThrowIfNull(pairs);
        if (pairs.Count != PairCount)
        {
            throw new InvalidDataException(
                $"RQ1 requires {BlockCount} distinct blocks with one matched pair each; found {pairs.Count} pairs.");
        }

        var fixtureIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (FormalExperimentSuitePairEntry pair in pairs)
        {
            if (!fixtureIds.Add(pair.FixtureId))
            {
                throw new InvalidDataException(
                    "RQ1 contains a repeated fixture identity; the 30-block redesign permits one pair per distinct block: "
                    + pair.FixtureId);
            }

            if (!StringComparer.Ordinal.Equals(pair.RepeatId, "repeat-01"))
            {
                throw new InvalidDataException(
                    $"RQ1 block {pair.FixtureId} must contain exactly one pair identified as repeat-01.");
            }
        }
    }
}
/// <summary>
/// RQ1 concurrency configuration: how many workers process matched pairs, and (optionally) which
/// pre-supplied credential environment-variable NAME each worker should construct its Provider
/// client with. The default (single worker, no override) reproduces the historical fully
/// sequential behavior exactly.
/// </summary>
internal sealed class Rq1ConcurrencyOptions
{
    public static readonly Rq1ConcurrencyOptions Default = new(1, null);

    public Rq1ConcurrencyOptions(int workerCount, Func<int, string?>? credentialEnvironmentNameForWorker)
    {
        if (workerCount <= 0)
            throw new ArgumentOutOfRangeException(nameof(workerCount), "RQ1 worker count must be positive.");
        WorkerCount = workerCount;
        CredentialEnvironmentNameForWorker = credentialEnvironmentNameForWorker;
    }

    /// <summary>Configured number of concurrent RQ1 pair workers. 1 == the historical sequential run.</summary>
    public int WorkerCount { get; }

    /// <summary>
    /// Optional per-worker credential environment-variable NAME resolver. Never returns or
    /// touches a secret value - only the NAME of the environment variable a worker's Provider
    /// client should read from. Null means "use the caller's existing default resolution",
    /// preserving current behavior exactly.
    /// </summary>
    public Func<int, string?>? CredentialEnvironmentNameForWorker { get; }

    public string? CredentialEnvironmentNameFor(int workerIndex) =>
        CredentialEnvironmentNameForWorker?.Invoke(workerIndex);
}

/// <summary>One matched pair's worker failure: which pair, which worker, and why.</summary>
internal sealed record Rq1PairFailure(int PairIndex, int WorkerIndex, Exception Exception);

/// <summary>
/// Outcome of one <see cref="Rq1ConcurrencyScheduler"/> run: which zero-based pair indices
/// completed exactly once, which failed (and on which worker), and the deterministic
/// pair-to-worker assignment that was actually used.
/// </summary>
internal sealed class Rq1ConcurrencySchedulerResult
{
    internal Rq1ConcurrencySchedulerResult(
        int pairCount,
        int workerCount,
        IReadOnlyList<int> completedPairIndices,
        IReadOnlyList<Rq1PairFailure> failures,
        IReadOnlyDictionary<int, int> workerByPairIndex)
    {
        PairCount = pairCount;
        WorkerCount = workerCount;
        CompletedPairIndices = completedPairIndices;
        Failures = failures;
        WorkerByPairIndex = workerByPairIndex;
    }

    public int PairCount { get; }
    public int WorkerCount { get; }
    public IReadOnlyList<int> CompletedPairIndices { get; }
    public IReadOnlyList<Rq1PairFailure> Failures { get; }
    public IReadOnlyDictionary<int, int> WorkerByPairIndex { get; }

    /// <summary>
    /// True only when every pair completed exactly once and no worker failed. A caller must
    /// never treat a run with any failure as complete - this is the single source of truth
    /// that keeps a failed worker's pairs from being silently reported as covered.
    /// </summary>
    public bool IsFullyComplete => Failures.Count == 0 && CompletedPairIndices.Count == PairCount;
}

/// <summary>
/// Deterministic, fixed-assignment concurrent scheduler for RQ1 matched pairs. Pair index -&gt;
/// worker is always <c>pairIndex % workerCount</c> - never dynamic work-stealing - so the same
/// (pairCount, workerCount) always yields the same assignment. Each worker processes its
/// assigned pairs strictly in ascending pair-index order, one at a time (never two pairs
/// concurrently on the same worker), which is what guarantees a single pair's two conditions -
/// run by whatever the caller's per-pair delegate does - can never overlap with another pair on
/// that same worker either.
/// </summary>
internal static class Rq1ConcurrencyScheduler
{
    public static int WorkerForPair(int zeroBasedPairIndex, int workerCount)
    {
        if (zeroBasedPairIndex < 0)
            throw new ArgumentOutOfRangeException(nameof(zeroBasedPairIndex));
        if (workerCount <= 0)
            throw new ArgumentOutOfRangeException(nameof(workerCount));
        return zeroBasedPairIndex % workerCount;
    }

    public static async Task<Rq1ConcurrencySchedulerResult> RunAsync(
        int pairCount,
        int workerCount,
        Func<int, int, CancellationToken, Task> runPairAsync,
        CancellationToken cancellationToken)
    {
        if (pairCount < 0)
            throw new ArgumentOutOfRangeException(nameof(pairCount));
        if (workerCount <= 0)
            throw new ArgumentOutOfRangeException(nameof(workerCount));
        ArgumentNullException.ThrowIfNull(runPairAsync);

        var pairsByWorker = new List<int>[workerCount];
        for (int worker = 0; worker < workerCount; worker++) pairsByWorker[worker] = [];
        var workerByPairIndex = new Dictionary<int, int>(pairCount);
        for (int pairIndex = 0; pairIndex < pairCount; pairIndex++)
        {
            int worker = WorkerForPair(pairIndex, workerCount);
            pairsByWorker[worker].Add(pairIndex);
            workerByPairIndex[pairIndex] = worker;
        }

        var completed = new System.Collections.Concurrent.ConcurrentBag<int>();
        var failures = new System.Collections.Concurrent.ConcurrentBag<Rq1PairFailure>();
        var startGate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        async Task RunWorkerAsync(int workerIndex)
        {
            await startGate.Task.ConfigureAwait(false);
            foreach (int pairIndex in pairsByWorker[workerIndex])
            {
                if (cancellationToken.IsCancellationRequested) return;
                try
                {
                    await runPairAsync(pairIndex, workerIndex, cancellationToken).ConfigureAwait(false);
                    completed.Add(pairIndex);
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    // A failed pair stops only this worker's remaining pairs; other workers keep
                    // going so we collect the maximum honest evidence, and the failed (and any
                    // never-attempted) pairs simply never appear in CompletedPairIndices - which
                    // is what makes downstream coverage reporting correctly show them as
                    // incomplete rather than falsely complete.
                    failures.Add(new Rq1PairFailure(pairIndex, workerIndex, exception));
                    return;
                }
            }
        }

        var workerTasks = new Task[workerCount];
        for (int worker = 0; worker < workerCount; worker++)
        {
            int capturedWorker = worker;
            workerTasks[capturedWorker] = RunWorkerAsync(capturedWorker);
        }
        startGate.SetResult(true);
        await Task.WhenAll(workerTasks).ConfigureAwait(false);

        return new Rq1ConcurrencySchedulerResult(
            pairCount,
            workerCount,
            completed.OrderBy(value => value).ToArray(),
            failures.OrderBy(value => value.PairIndex).ToArray(),
            workerByPairIndex);
    }
}

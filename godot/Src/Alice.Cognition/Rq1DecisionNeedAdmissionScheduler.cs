using System.Collections.ObjectModel;
using Alice.Activities;
using Alice.Actors;

namespace Alice.Cognition;

public enum FormalRq1Treatment
{
    AgentCentric,
    EventCentric
}

public enum AgentCentricRankBand
{
    A0,
    A1,
    A2,
    A3,
    A4
}

/// <summary>The ephemeral AgentCentric treatment-local ordering tuple.</summary>
public sealed class AgentCentricTreatmentRank
{
    public AgentCentricTreatmentRank(
        AgentCentricRankBand rankBand,
        ActorId actorId,
        DecisionNeedFingerprint fingerprint)
    {
        if (!Enum.IsDefined(rankBand))
        {
            throw new ArgumentOutOfRangeException(nameof(rankBand));
        }

        ArgumentNullException.ThrowIfNull(actorId);
        ArgumentNullException.ThrowIfNull(fingerprint);
        RankBand = rankBand;
        ActorId = actorId;
        Fingerprint = fingerprint;
    }

    public AgentCentricRankBand RankBand { get; }
    public ActorId ActorId { get; }
    public DecisionNeedFingerprint Fingerprint { get; }
}

/// <summary>One exact Store-retained candidate and its optional closed treatment-rank evidence.</summary>
public sealed class Rq1DecisionNeedAdmissionCandidate
{
    public Rq1DecisionNeedAdmissionCandidate(DecisionNeed need)
        : this(need, null, null)
    {
    }

    public Rq1DecisionNeedAdmissionCandidate(
        DecisionNeed need,
        AgentCentricTreatmentRank treatmentRank)
        : this(need, treatmentRank, null)
    {
        ArgumentNullException.ThrowIfNull(treatmentRank);
    }

    public Rq1DecisionNeedAdmissionCandidate(
        DecisionNeed need,
        EventCentricTreatmentRank treatmentRank)
        : this(need, null, treatmentRank)
    {
        ArgumentNullException.ThrowIfNull(treatmentRank);
    }

    private Rq1DecisionNeedAdmissionCandidate(
        DecisionNeed need,
        AgentCentricTreatmentRank? agentCentricRank,
        EventCentricTreatmentRank? eventCentricRank)
    {
        ArgumentNullException.ThrowIfNull(need);
        Need = need;
        AgentCentricRank = agentCentricRank;
        EventCentricRank = eventCentricRank;
    }

    public DecisionNeed Need { get; }
    public AgentCentricTreatmentRank? AgentCentricRank { get; }
    public EventCentricTreatmentRank? EventCentricRank { get; }
}

/// <summary>Immutable derived evidence for one candidate in the shared formal-RQ1 order.</summary>
public sealed class Rq1DecisionNeedAdmissionEntry
{
    internal Rq1DecisionNeedAdmissionEntry(
        Rq1DecisionNeedAdmissionCandidate candidate,
        SimTime? urgencyKey,
        SimTime? starvationDeadline,
        bool isStarvationPromoted)
    {
        Candidate = candidate;
        UrgencyKey = urgencyKey;
        StarvationDeadline = starvationDeadline;
        IsStarvationPromoted = isStarvationPromoted;
    }

    public Rq1DecisionNeedAdmissionCandidate Candidate { get; }
    public DecisionNeed Need => Candidate.Need;
    public SimTime? UrgencyKey { get; }
    public SimTime? StarvationDeadline { get; }
    public bool IsStarvationPromoted { get; }
}

/// <summary>Immutable complete order and logical-session budget split for one projection.</summary>
public sealed class Rq1DecisionNeedAdmissionResult
{
    private readonly ReadOnlyCollection<Rq1DecisionNeedAdmissionEntry> _orderedEntries;
    private readonly ReadOnlyCollection<Rq1DecisionNeedAdmissionEntry> _selectedForAdmission;
    private readonly ReadOnlyCollection<Rq1DecisionNeedAdmissionEntry> _missedDueToBudget;

    internal Rq1DecisionNeedAdmissionResult(
        Rq1DecisionNeedAdmissionEntry[] orderedEntries,
        Rq1DecisionNeedAdmissionEntry[] selectedForAdmission,
        Rq1DecisionNeedAdmissionEntry[] missedDueToBudget)
    {
        _orderedEntries = Array.AsReadOnly(orderedEntries);
        _selectedForAdmission = Array.AsReadOnly(selectedForAdmission);
        _missedDueToBudget = Array.AsReadOnly(missedDueToBudget);
    }

    public IReadOnlyList<Rq1DecisionNeedAdmissionEntry> OrderedEntries => _orderedEntries;
    public IReadOnlyList<Rq1DecisionNeedAdmissionEntry> SelectedForAdmission => _selectedForAdmission;
    public IReadOnlyList<Rq1DecisionNeedAdmissionEntry> MissedDueToBudget => _missedDueToBudget;
}

/// <summary>Stateless mutation-free formal-RQ1 urgency, starvation and budget admission projection.</summary>
public static class Rq1DecisionNeedAdmissionScheduler
{
    public static Rq1DecisionNeedAdmissionResult Project(
        DecisionNeedStore store,
        IEnumerable<Rq1DecisionNeedAdmissionCandidate> candidates,
        FormalRq1Treatment treatment,
        SimTime now,
        long starvationAgeTicks,
        int remainingSessionBudget)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(candidates);
        if (!Enum.IsDefined(treatment))
        {
            throw new ArgumentOutOfRangeException(nameof(treatment));
        }

        if (starvationAgeTicks <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(starvationAgeTicks));
        }

        if (remainingSessionBudget < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(remainingSessionBudget));
        }

        Rq1DecisionNeedAdmissionCandidate[] candidateSnapshot = candidates.ToArray();
        var seenNeedIds = new HashSet<DecisionNeedId>();
        var seenNeedReferences = new HashSet<DecisionNeed>(ReferenceEqualityComparer.Instance);
        var entries = new Rq1DecisionNeedAdmissionEntry[candidateSnapshot.Length];
        for (int index = 0; index < candidateSnapshot.Length; index++)
        {
            Rq1DecisionNeedAdmissionCandidate candidate = candidateSnapshot[index]
                ?? throw new ArgumentException("The candidate sequence cannot contain null.", nameof(candidates));
            DecisionNeed need = candidate.Need;
            ValidateUniqueNeed(need, seenNeedIds, seenNeedReferences, candidates);
            ValidateRetainedQueuedNeed(store, need, candidates);
            ValidateTreatmentRank(candidate, treatment, candidates);
            entries[index] = DeriveEntry(candidate, now, starvationAgeTicks);
        }

        Array.Sort(entries, new AdmissionEntryComparer(treatment));
        int selectedCount = Math.Min(remainingSessionBudget, entries.Length);
        var selected = new Rq1DecisionNeedAdmissionEntry[selectedCount];
        var missed = new Rq1DecisionNeedAdmissionEntry[entries.Length - selectedCount];
        Array.Copy(entries, selected, selectedCount);
        Array.Copy(entries, selectedCount, missed, 0, missed.Length);
        return new Rq1DecisionNeedAdmissionResult(entries, selected, missed);
    }

    private static void ValidateUniqueNeed(
        DecisionNeed need,
        HashSet<DecisionNeedId> seenNeedIds,
        HashSet<DecisionNeed> seenNeedReferences,
        IEnumerable<Rq1DecisionNeedAdmissionCandidate> candidates)
    {
        if (!seenNeedIds.Add(need.NeedId) || !seenNeedReferences.Add(need))
        {
            throw new ArgumentException(
                "Candidates must be unique by exact NeedId and DecisionNeed reference.",
                nameof(candidates));
        }
    }

    private static void ValidateRetainedQueuedNeed(
        DecisionNeedStore store,
        DecisionNeed need,
        IEnumerable<Rq1DecisionNeedAdmissionCandidate> candidates)
    {
        if (need.State != DecisionNeedState.Queued)
        {
            throw new ArgumentException("Every admission candidate must currently be Queued.", nameof(candidates));
        }

        if (store.Lookup(need.NeedId) is not FoundDecisionNeed found
            || !ReferenceEquals(found.Need, need))
        {
            throw new ArgumentException(
                "Every admission candidate must be the exact DecisionNeed retained by the supplied Store.",
                nameof(candidates));
        }
    }

    private static void ValidateTreatmentRank(
        Rq1DecisionNeedAdmissionCandidate candidate,
        FormalRq1Treatment treatment,
        IEnumerable<Rq1DecisionNeedAdmissionCandidate> candidates)
    {
        DecisionNeed need = candidate.Need;
        bool isMandatoryResponse = need.MandatoryResponseSubject is not null;
        if (isMandatoryResponse)
        {
            if (candidate.AgentCentricRank is not null || candidate.EventCentricRank is not null)
            {
                throw new ArgumentException(
                    "A mandatory-response candidate cannot carry treatment-rank evidence.",
                    nameof(candidates));
            }

            return;
        }

        DecisionNeedDiscoveryRoute expectedRoute = treatment == FormalRq1Treatment.AgentCentric
            ? DecisionNeedDiscoveryRoute.AgentCentric
            : DecisionNeedDiscoveryRoute.EventCentric;
        if (need.DiscoveryTrace.Route != expectedRoute)
        {
            throw new ArgumentException(
                "A non-mandatory candidate must belong to the invocation treatment.",
                nameof(candidates));
        }

        if (treatment == FormalRq1Treatment.AgentCentric)
        {
            AgentCentricTreatmentRank rank = candidate.AgentCentricRank
                ?? throw new ArgumentException(
                    "An AgentCentric candidate requires AgentCentric rank evidence.",
                    nameof(candidates));
            if (candidate.EventCentricRank is not null
                || rank.ActorId != need.NpcId
                || rank.Fingerprint != need.Fingerprint)
            {
                throw new ArgumentException(
                    "AgentCentric rank Actor and Fingerprint must exactly match the candidate Need.",
                    nameof(candidates));
            }

            return;
        }

        EventCentricTreatmentRank eventRank = candidate.EventCentricRank
            ?? throw new ArgumentException(
                "An EventCentric candidate requires EventCentric rank evidence.",
                nameof(candidates));
        if (candidate.AgentCentricRank is not null
            || eventRank.ActorId != need.NpcId
            || eventRank.Fingerprint != need.Fingerprint)
        {
            throw new ArgumentException(
                "EventCentric rank Actor and Fingerprint must exactly match the candidate Need.",
                nameof(candidates));
        }
    }

    private static Rq1DecisionNeedAdmissionEntry DeriveEntry(
        Rq1DecisionNeedAdmissionCandidate candidate,
        SimTime now,
        long starvationAgeTicks)
    {
        DecisionNeed need = candidate.Need;
        long ageTicks = checked(now.Ticks - need.CreatedAt.Ticks);
        if (ageTicks < 0)
        {
            throw new ArgumentException("A future-created DecisionNeed cannot be scheduled.", nameof(candidate));
        }

        long starvationDeadlineTicks = checked(need.CreatedAt.Ticks + starvationAgeTicks);
        if (ageTicks < starvationAgeTicks)
        {
            return new Rq1DecisionNeedAdmissionEntry(candidate, need.Deadline, null, false);
        }

        var starvationDeadline = new SimTime(starvationDeadlineTicks);
        SimTime urgencyKey = need.Deadline is SimTime deadline && deadline.CompareTo(starvationDeadline) <= 0
            ? deadline
            : starvationDeadline;
        return new Rq1DecisionNeedAdmissionEntry(candidate, urgencyKey, starvationDeadline, true);
    }

    private sealed class AdmissionEntryComparer : IComparer<Rq1DecisionNeedAdmissionEntry>
    {
        private readonly FormalRq1Treatment _treatment;

        public AdmissionEntryComparer(FormalRq1Treatment treatment)
        {
            _treatment = treatment;
        }

        public int Compare(Rq1DecisionNeedAdmissionEntry? left, Rq1DecisionNeedAdmissionEntry? right)
        {
            DecisionNeed leftNeed = left!.Need;
            DecisionNeed rightNeed = right!.Need;
            int mandatoryComparison = IsMandatory(rightNeed).CompareTo(IsMandatory(leftNeed));
            if (mandatoryComparison != 0)
            {
                return mandatoryComparison;
            }

            int urgencyComparison = CompareUrgency(left.UrgencyKey, right.UrgencyKey);
            if (urgencyComparison != 0)
            {
                return urgencyComparison;
            }

            int treatmentComparison = CompareTreatmentRank(left.Candidate, right.Candidate);
            if (treatmentComparison != 0)
            {
                return treatmentComparison;
            }

            int createdAtComparison = leftNeed.CreatedAt.CompareTo(rightNeed.CreatedAt);
            return createdAtComparison != 0
                ? createdAtComparison
                : StringComparer.Ordinal.Compare(leftNeed.NeedId.Value, rightNeed.NeedId.Value);
        }

        private static bool IsMandatory(DecisionNeed need) => need.MandatoryResponseSubject is not null;

        private static int CompareUrgency(SimTime? left, SimTime? right)
        {
            if (left is null)
            {
                return right is null ? 0 : 1;
            }

            return right is null ? -1 : left.Value.CompareTo(right.Value);
        }

        private int CompareTreatmentRank(
            Rq1DecisionNeedAdmissionCandidate left,
            Rq1DecisionNeedAdmissionCandidate right)
        {
            if (IsMandatory(left.Need) && IsMandatory(right.Need))
            {
                return 0;
            }

            return _treatment == FormalRq1Treatment.AgentCentric
                ? CompareAgentCentricRank(left.AgentCentricRank!, right.AgentCentricRank!)
                : CompareEventCentricRank(left.EventCentricRank!, right.EventCentricRank!);
        }

        private static int CompareAgentCentricRank(
            AgentCentricTreatmentRank left,
            AgentCentricTreatmentRank right)
        {
            int bandComparison = left.RankBand.CompareTo(right.RankBand);
            if (bandComparison != 0)
            {
                return bandComparison;
            }

            int actorComparison = StringComparer.Ordinal.Compare(left.ActorId.Value, right.ActorId.Value);
            return actorComparison != 0
                ? actorComparison
                : StringComparer.Ordinal.Compare(left.Fingerprint.Value, right.Fingerprint.Value);
        }

        private static int CompareEventCentricRank(
            EventCentricTreatmentRank left,
            EventCentricTreatmentRank right)
        {
            int bandComparison = left.RankBand.CompareTo(right.RankBand);
            if (bandComparison != 0)
            {
                return bandComparison;
            }

            int sourceComparison = StringComparer.Ordinal.Compare(
                left.CanonicalSourceIdentity,
                right.CanonicalSourceIdentity);
            if (sourceComparison != 0)
            {
                return sourceComparison;
            }

            int actorComparison = StringComparer.Ordinal.Compare(left.ActorId.Value, right.ActorId.Value);
            return actorComparison != 0
                ? actorComparison
                : StringComparer.Ordinal.Compare(left.Fingerprint.Value, right.Fingerprint.Value);
        }
    }
}

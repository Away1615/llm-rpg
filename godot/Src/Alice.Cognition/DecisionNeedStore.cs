using System.Collections.ObjectModel;
using Alice.Activities;
using Alice.Actors;
using Alice.Npc;
using Alice.Social;

namespace Alice.Cognition;

public sealed class DecisionNeedStore
{
    private readonly List<DecisionNeed> _registrationOrder = [];
    private readonly Dictionary<DecisionNeedId, DecisionNeed> _needsById = [];
    private readonly Dictionary<DecisionNeedFingerprint, DecisionNeed> _firstNeedByFingerprint = [];
    private readonly Dictionary<CurrentStepKey, DecisionNeedId> _currentNeedBySubject = [];
    private readonly Dictionary<ActorId, DecisionNeedId> _currentPlanlessStrategicNeedByActor = [];
    private readonly Dictionary<MandatoryResponseDecisionSubject, DecisionNeedId> _mandatoryResponseNeedBySubject = [];

    public DecisionNeedRegistrationOutcome Register(
        ActorId actorId,
        PlanId planId,
        PlanStepId planStepId,
        DecisionNeedKind needKind,
        CurrentStepDecisionProblemDescriptor problemDescriptor,
        DecisionNeedDiscoveryTrace discoveryTrace,
        DecisionNeedWorldRevision firstObservedWorldRevision,
        SimTime createdAt,
        SimTime? deadline = null)
    {
        ArgumentNullException.ThrowIfNull(actorId);
        ArgumentNullException.ThrowIfNull(planId);
        ArgumentNullException.ThrowIfNull(planStepId);
        ArgumentNullException.ThrowIfNull(needKind);
        ArgumentNullException.ThrowIfNull(problemDescriptor);
        ArgumentNullException.ThrowIfNull(discoveryTrace);
        ArgumentNullException.ThrowIfNull(firstObservedWorldRevision);
        DecisionNeedIdentityValidation.ValidateActorAndStepCorrelation(
            actorId,
            planId,
            planStepId,
            problemDescriptor);
        if (problemDescriptor.PlanStepId != planStepId)
        {
            throw new ArgumentException("The current-step descriptor must match the registered Plan Step.", nameof(problemDescriptor));
        }

        var subject = new CurrentStepKey(actorId, planId, planStepId);
        DecisionNeedFingerprint fingerprint = DecisionNeedCanonicalJson.CreateFingerprint(
            actorId,
            planId,
            planStepId,
            needKind,
            problemDescriptor.DescriptorHash);
        if (_firstNeedByFingerprint.TryGetValue(fingerprint, out DecisionNeed? existing))
        {
            return IsCurrent(existing, subject) && IsActive(existing)
                ? new DuplicateActive(existing)
                : new StalePreviouslySeen(existing);
        }

        DecisionNeed candidate = DecisionNeed.Create(
            actorId,
            planId,
            planStepId,
            needKind,
            problemDescriptor,
            discoveryTrace,
            firstObservedWorldRevision,
            createdAt,
            deadline);

        if (_needsById.ContainsKey(candidate.NeedId))
        {
            throw new InvalidOperationException("Decision Need identity collision cannot be safely registered.");
        }

        if (!_currentNeedBySubject.TryGetValue(subject, out DecisionNeedId? currentNeedId))
        {
            candidate.Queue();
            AddNew(candidate, subject);
            return new RegisteredNew(candidate);
        }

        if (!_needsById.TryGetValue(currentNeedId, out DecisionNeed? currentNeed))
        {
            throw new InvalidOperationException("Current-step index refers to a missing Decision Need.");
        }

        if (currentNeed.State == DecisionNeedState.Queued)
        {
            DecisionNeed replacement = CreateQueuedReplacement(candidate, currentNeed.NeedId, firstObservedWorldRevision);
            currentNeed.Supersede();
            AddNew(replacement, subject);
            return new QueuedSupersession(currentNeed, replacement);
        }

        if (currentNeed.State == DecisionNeedState.InFlight)
        {
            DecisionNeed replacement = CreateQueuedReplacement(candidate, currentNeed.NeedId, firstObservedWorldRevision);
            AddNew(replacement, subject);
            return new InFlightRevalidationPending(currentNeed, replacement);
        }

        if (IsTerminal(currentNeed))
        {
            candidate.Queue();
            AddNew(candidate, subject);
            return new RegisteredNew(candidate);
        }

        throw new InvalidOperationException("Current-step index refers to an impossible Decision Need state.");
    }

    public DecisionNeedLookupOutcome Lookup(DecisionNeedId needId)
    {
        ArgumentNullException.ThrowIfNull(needId);
        return _needsById.TryGetValue(needId, out DecisionNeed? need)
            ? new FoundDecisionNeed(need)
            : new MissingDecisionNeed(needId);
    }

    internal DecisionNeedRegistrationOutcome RegisterPlanlessStrategic(
        ActorId actorId,
        DecisionNeedKind needKind,
        PlanlessStrategicDecisionProblemDescriptor problemDescriptor,
        DecisionNeedDiscoveryTrace discoveryTrace,
        DecisionNeedWorldRevision firstObservedWorldRevision,
        SimTime createdAt,
        SimTime? deadline = null)
    {
        ArgumentNullException.ThrowIfNull(actorId);
        ArgumentNullException.ThrowIfNull(needKind);
        ArgumentNullException.ThrowIfNull(problemDescriptor);
        ArgumentNullException.ThrowIfNull(discoveryTrace);
        ArgumentNullException.ThrowIfNull(firstObservedWorldRevision);
        DecisionNeedIdentityValidation.ValidateActorAndStepCorrelation(
            actorId,
            null,
            null,
            problemDescriptor);

        DecisionNeedFingerprint fingerprint = DecisionNeedCanonicalJson.CreateFingerprint(
            actorId,
            null,
            null,
            needKind,
            problemDescriptor.DescriptorHash);
        if (_firstNeedByFingerprint.TryGetValue(fingerprint, out DecisionNeed? existing))
        {
            return IsCurrentPlanlessStrategic(existing, actorId) && IsActive(existing)
                ? new DuplicateActive(existing)
                : new StalePreviouslySeen(existing);
        }

        DecisionNeed candidate = DecisionNeed.Create(
            actorId,
            null,
            null,
            needKind,
            problemDescriptor,
            discoveryTrace,
            firstObservedWorldRevision,
            createdAt,
            deadline);
        if (_needsById.ContainsKey(candidate.NeedId))
        {
            throw new InvalidOperationException("Decision Need identity collision cannot be safely registered.");
        }

        if (!_currentPlanlessStrategicNeedByActor.TryGetValue(actorId, out DecisionNeedId? currentNeedId))
        {
            candidate.Queue();
            AddPlanlessStrategic(candidate, actorId);
            return new RegisteredNew(candidate);
        }

        if (!_needsById.TryGetValue(currentNeedId, out DecisionNeed? currentNeed))
        {
            throw new InvalidOperationException("Planless-strategic index refers to a missing Decision Need.");
        }

        if (currentNeed.State == DecisionNeedState.Queued)
        {
            DecisionNeed replacement = CreateQueuedReplacement(candidate, currentNeed.NeedId, firstObservedWorldRevision);
            currentNeed.Supersede();
            AddPlanlessStrategic(replacement, actorId);
            return new QueuedSupersession(currentNeed, replacement);
        }

        if (currentNeed.State == DecisionNeedState.InFlight)
        {
            DecisionNeed replacement = CreateQueuedReplacement(candidate, currentNeed.NeedId, firstObservedWorldRevision);
            AddPlanlessStrategic(replacement, actorId);
            return new InFlightRevalidationPending(currentNeed, replacement);
        }

        if (IsTerminal(currentNeed))
        {
            candidate.Queue();
            AddPlanlessStrategic(candidate, actorId);
            return new RegisteredNew(candidate);
        }

        throw new InvalidOperationException("Planless-strategic index refers to an impossible Decision Need state.");
    }

    internal DecisionNeedRegistrationOutcome RegisterMandatoryResponse(
        RoutineSemanticResponseContext context,
        InviteResponseDecisionProblemDescriptor problemDescriptor,
        DecisionNeedDiscoveryTrace discoveryTrace,
        DecisionNeedWorldRevision firstObservedWorldRevision,
        SimTime createdAt)
    {
        DecisionNeed candidate = DecisionNeed.CreateMandatoryResponse(
            context,
            problemDescriptor,
            discoveryTrace,
            firstObservedWorldRevision,
            createdAt);
        MandatoryResponseDecisionSubject subject = candidate.MandatoryResponseSubject!;
        if (_firstNeedByFingerprint.TryGetValue(candidate.Fingerprint, out DecisionNeed? existing))
        {
            return IsCurrentMandatoryResponse(existing, subject) && IsActive(existing)
                ? new DuplicateActive(existing)
                : new StalePreviouslySeen(existing);
        }

        if (_needsById.ContainsKey(candidate.NeedId))
        {
            throw new InvalidOperationException("Decision Need identity collision cannot be safely registered.");
        }

        if (!_mandatoryResponseNeedBySubject.TryGetValue(subject, out DecisionNeedId? retainedNeedId))
        {
            candidate.Queue();
            AddMandatoryResponse(candidate, subject);
            return new RegisteredNew(candidate);
        }

        if (!_needsById.TryGetValue(retainedNeedId, out DecisionNeed? retainedNeed))
        {
            throw new InvalidOperationException("Mandatory-response index refers to a missing Decision Need.");
        }

        return new MandatoryResponseSubjectConflict(retainedNeed, candidate.Fingerprint);
    }

    internal MandatoryResponseDecisionOwnershipInspection InspectMandatoryResponse(
        RoutineSemanticResponseContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        MandatoryResponseDecisionSubject subject = MandatoryResponseDecisionSubject.Create(context);
        if (!_mandatoryResponseNeedBySubject.TryGetValue(subject, out DecisionNeedId? retainedNeedId))
        {
            return MandatoryResponseDecisionOwnershipInspection.None();
        }

        if (!_needsById.TryGetValue(retainedNeedId, out DecisionNeed? retainedNeed)
            || !IsCurrentMandatoryResponse(retainedNeed, subject))
        {
            throw new InvalidOperationException("Mandatory-response ownership index is incomplete or corrupt.");
        }

        return retainedNeed.State is DecisionNeedState.Created or DecisionNeedState.Queued or DecisionNeedState.InFlight
            ? MandatoryResponseDecisionOwnershipInspection.Active(retainedNeed)
            : MandatoryResponseDecisionOwnershipInspection.TerminalConflict(retainedNeed);
    }

    public DecisionNeedStoreSnapshot GetRegistrationOrderSnapshot()
    {
        return new DecisionNeedStoreSnapshot(_registrationOrder);
    }

    public DecisionNeedRevalidationOutcome RevalidateStoreCurrent(DecisionNeedId needId)
    {
        ArgumentNullException.ThrowIfNull(needId);
        if (!_needsById.TryGetValue(needId, out DecisionNeed? need))
        {
            throw new InvalidOperationException("Cannot revalidate a missing Decision Need.");
        }

        if (need.State != DecisionNeedState.InFlight)
        {
            throw new InvalidOperationException("Only an InFlight Decision Need can be store-current revalidated.");
        }

        if (need.ProblemDescriptor is PlanlessStrategicDecisionProblemDescriptor)
        {
            if (need.PlanId is not null || need.PlanStepId is not null)
            {
                throw new InvalidOperationException("A retained planless strategic Need has fabricated Plan identity.");
            }

            if (!_currentPlanlessStrategicNeedByActor.TryGetValue(need.NpcId, out DecisionNeedId? currentPlanlessNeedId)
                || !_needsById.ContainsKey(currentPlanlessNeedId))
            {
                throw new InvalidOperationException("Planless-strategic index is incomplete or corrupt.");
            }

            if (currentPlanlessNeedId == needId)
            {
                return new Current(need);
            }

            need.Supersede();
            return new StaleSuperseded(need);
        }

        if (need.PlanId is null || need.PlanStepId is null
            || need.ProblemDescriptor is not CurrentStepDecisionProblemDescriptor)
        {
            throw new InvalidOperationException("A retained Decision Need lacks the required current-step identity.");
        }

        var subject = new CurrentStepKey(need.NpcId, need.PlanId, need.PlanStepId);
        if (!_currentNeedBySubject.TryGetValue(subject, out DecisionNeedId? currentNeedId) ||
            !_needsById.ContainsKey(currentNeedId))
        {
            throw new InvalidOperationException("Current-step index is incomplete or corrupt.");
        }

        if (currentNeedId == needId)
        {
            return new Current(need);
        }

        need.Supersede();
        return new StaleSuperseded(need);
    }

    internal bool IsCurrentMandatoryResponse(DecisionNeed need)
    {
        ArgumentNullException.ThrowIfNull(need);
        MandatoryResponseDecisionSubject? subject = need.MandatoryResponseSubject;
        return subject is not null
            && _needsById.TryGetValue(need.NeedId, out DecisionNeed? retained)
            && ReferenceEquals(retained, need)
            && _mandatoryResponseNeedBySubject.TryGetValue(subject, out DecisionNeedId? currentNeedId)
            && currentNeedId == need.NeedId;
    }

    internal bool IsCurrentPlanlessStrategic(DecisionNeed need)
    {
        ArgumentNullException.ThrowIfNull(need);
        return need.ProblemDescriptor is PlanlessStrategicDecisionProblemDescriptor
            && need.PlanId is null
            && need.PlanStepId is null
            && _needsById.TryGetValue(need.NeedId, out DecisionNeed? retained)
            && ReferenceEquals(retained, need)
            && IsCurrentPlanlessStrategic(need, need.NpcId);
    }

    private static bool IsActive(DecisionNeed need)
    {
        return need.State is DecisionNeedState.Created or DecisionNeedState.Queued or DecisionNeedState.InFlight;
    }

    private static bool IsTerminal(DecisionNeed need)
    {
        return need.State is DecisionNeedState.Resolved or DecisionNeedState.Superseded or DecisionNeedState.Aborted;
    }

    private bool IsCurrent(DecisionNeed need, CurrentStepKey subject)
    {
        return _currentNeedBySubject.TryGetValue(subject, out DecisionNeedId? currentNeedId) && currentNeedId == need.NeedId;
    }

    private bool IsCurrentMandatoryResponse(DecisionNeed need, MandatoryResponseDecisionSubject subject)
    {
        return _mandatoryResponseNeedBySubject.TryGetValue(subject, out DecisionNeedId? currentNeedId)
            && currentNeedId == need.NeedId;
    }

    private bool IsCurrentPlanlessStrategic(DecisionNeed need, ActorId actorId)
    {
        return _currentPlanlessStrategicNeedByActor.TryGetValue(actorId, out DecisionNeedId? currentNeedId)
            && currentNeedId == need.NeedId;
    }

    private static DecisionNeed CreateQueuedReplacement(
        DecisionNeed candidate,
        DecisionNeedId supersedesNeedId,
        DecisionNeedWorldRevision firstObservedWorldRevision)
    {
        DecisionNeed replacement = DecisionNeed.Create(
            candidate.NpcId,
            candidate.PlanId,
            candidate.PlanStepId,
            candidate.Kind,
            candidate.ProblemDescriptor,
            candidate.DiscoveryTrace,
            firstObservedWorldRevision,
            candidate.CreatedAt,
            candidate.Deadline,
            supersedesNeedId);
        replacement.Queue();
        return replacement;
    }

    private void AddNew(DecisionNeed need, CurrentStepKey subject)
    {
        _registrationOrder.Add(need);
        _needsById.Add(need.NeedId, need);
        _firstNeedByFingerprint.Add(need.Fingerprint, need);
        _currentNeedBySubject[subject] = need.NeedId;
    }

    private void AddMandatoryResponse(DecisionNeed need, MandatoryResponseDecisionSubject subject)
    {
        _registrationOrder.Add(need);
        _needsById.Add(need.NeedId, need);
        _firstNeedByFingerprint.Add(need.Fingerprint, need);
        _mandatoryResponseNeedBySubject.Add(subject, need.NeedId);
    }

    private void AddPlanlessStrategic(DecisionNeed need, ActorId actorId)
    {
        _registrationOrder.Add(need);
        _needsById.Add(need.NeedId, need);
        _firstNeedByFingerprint.Add(need.Fingerprint, need);
        _currentPlanlessStrategicNeedByActor[actorId] = need.NeedId;
    }

    private readonly record struct CurrentStepKey(ActorId ActorId, PlanId PlanId, PlanStepId PlanStepId);
}

public abstract record DecisionNeedRegistrationOutcome
{
    private protected DecisionNeedRegistrationOutcome()
    {
    }
}

public sealed record RegisteredNew(DecisionNeed Need) : DecisionNeedRegistrationOutcome;
public sealed record DuplicateActive(DecisionNeed Need) : DecisionNeedRegistrationOutcome;
public sealed record StalePreviouslySeen(DecisionNeed Need) : DecisionNeedRegistrationOutcome;
public sealed record QueuedSupersession(DecisionNeed SupersededNeed, DecisionNeed Replacement) : DecisionNeedRegistrationOutcome;
public sealed record InFlightRevalidationPending(DecisionNeed InFlightNeed, DecisionNeed Replacement) : DecisionNeedRegistrationOutcome;
public sealed record MandatoryResponseSubjectConflict(
    DecisionNeed RetainedNeed,
    DecisionNeedFingerprint RejectedFingerprint) : DecisionNeedRegistrationOutcome;

public enum MandatoryResponseDecisionOwnershipOutcome
{
    NoRetainedNeed,
    ActiveRetainedNeed,
    TerminalNeedConflict
}

/// <summary>Typed owner-first inspection for one exact mandatory response subject.</summary>
public sealed record MandatoryResponseDecisionOwnershipInspection
{
    private MandatoryResponseDecisionOwnershipInspection(
        MandatoryResponseDecisionOwnershipOutcome outcome,
        DecisionNeed? retainedNeed)
    {
        Outcome = outcome;
        RetainedNeed = retainedNeed;
    }

    public MandatoryResponseDecisionOwnershipOutcome Outcome { get; }
    public DecisionNeed? RetainedNeed { get; }

    internal static MandatoryResponseDecisionOwnershipInspection None() =>
        new(MandatoryResponseDecisionOwnershipOutcome.NoRetainedNeed, null);

    internal static MandatoryResponseDecisionOwnershipInspection Active(DecisionNeed retainedNeed) =>
        new(MandatoryResponseDecisionOwnershipOutcome.ActiveRetainedNeed, retainedNeed);

    internal static MandatoryResponseDecisionOwnershipInspection TerminalConflict(DecisionNeed retainedNeed) =>
        new(MandatoryResponseDecisionOwnershipOutcome.TerminalNeedConflict, retainedNeed);
}

public abstract record DecisionNeedLookupOutcome
{
    private protected DecisionNeedLookupOutcome()
    {
    }
}

public sealed record FoundDecisionNeed(DecisionNeed Need) : DecisionNeedLookupOutcome;
public sealed record MissingDecisionNeed(DecisionNeedId NeedId) : DecisionNeedLookupOutcome;

public sealed class DecisionNeedStoreSnapshot
{
    private readonly ReadOnlyCollection<DecisionNeed> _needs;

    internal DecisionNeedStoreSnapshot(IEnumerable<DecisionNeed> needs)
    {
        _needs = Array.AsReadOnly(needs.ToArray());
    }

    public IReadOnlyList<DecisionNeed> Needs => _needs;
}

public abstract record DecisionNeedRevalidationOutcome
{
    private protected DecisionNeedRevalidationOutcome()
    {
    }
}

public sealed record Current(DecisionNeed Need) : DecisionNeedRevalidationOutcome;
public sealed record StaleSuperseded(DecisionNeed Need) : DecisionNeedRevalidationOutcome;

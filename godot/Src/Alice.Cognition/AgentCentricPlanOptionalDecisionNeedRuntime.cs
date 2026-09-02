using System.Collections.ObjectModel;
using Alice.Activities;
using Alice.Actors;
using Alice.Npc;

namespace Alice.Cognition;

public readonly record struct AgentCentricTriggerId
{
    public AgentCentricTriggerId(string value)
    {
        DependencyContractIdentity.Validate(value, nameof(value));
        Value = value;
    }

    public string Value { get; }
}

/// <summary>A caller-proven actor-local trigger nomination. It contains no dependency path or numeric threshold.</summary>
public sealed record AgentCentricTriggerNomination
{
    public AgentCentricTriggerNomination(
        AgentCentricPlanOptionalDecisionBinding binding,
        AgentCentricRankBand rankBand,
        AgentCentricTriggerId triggerId)
    {
        ArgumentNullException.ThrowIfNull(binding);
        if (!Enum.IsDefined(rankBand))
        {
            throw new ArgumentOutOfRangeException(nameof(rankBand));
        }

        Binding = binding;
        RankBand = rankBand;
        TriggerId = triggerId;
    }

    public AgentCentricPlanOptionalDecisionBinding Binding { get; }
    public ActorId ActorId => Binding.ActorId;
    public AgentCentricRankBand RankBand { get; }
    public AgentCentricTriggerId TriggerId { get; }
}

public abstract class AgentCentricPlanOptionalDecisionBinding
{
    private protected AgentCentricPlanOptionalDecisionBinding(ActorId actorId)
    {
        ArgumentNullException.ThrowIfNull(actorId);
        ActorId = actorId;
    }

    public ActorId ActorId { get; }
    internal abstract ActorId ViewActorId { get; }
    internal abstract bool IsStructurallyValid { get; }
    internal abstract bool HasActiveGoal { get; }
    internal abstract DecisionNeedFingerprint Fingerprint { get; }
    internal abstract DecisionNeedRegistrationOutcome Register(
        DecisionNeedDiscoveryRegistrar registrar,
        DecisionNeedDiscoveryTrace trace);
}

public sealed class AgentCentricPlanlessStrategicDecisionBinding : AgentCentricPlanOptionalDecisionBinding
{
    public AgentCentricPlanlessStrategicDecisionBinding(
        ActorId actorId,
        ActorDecisionView view,
        DecisionNeedKind needKind,
        DecisionProblemCode problemCode,
        DecisionNeedWorldRevision firstObservedWorldRevision,
        SimTime createdAt,
        SimTime? deadline = null)
        : base(actorId)
    {
        ArgumentNullException.ThrowIfNull(view);
        ArgumentNullException.ThrowIfNull(needKind);
        ArgumentNullException.ThrowIfNull(problemCode);
        ArgumentNullException.ThrowIfNull(firstObservedWorldRevision);
        if (deadline is SimTime value && value.Ticks < createdAt.Ticks)
        {
            throw new ArgumentOutOfRangeException(nameof(deadline));
        }

        View = view;
        NeedKind = needKind;
        ProblemCode = problemCode;
        FirstObservedWorldRevision = firstObservedWorldRevision;
        CreatedAt = createdAt;
        Deadline = deadline;
    }

    public ActorDecisionView View { get; }
    public DecisionNeedKind NeedKind { get; }
    public DecisionProblemCode ProblemCode { get; }
    public DecisionNeedWorldRevision FirstObservedWorldRevision { get; }
    public SimTime CreatedAt { get; }
    public SimTime? Deadline { get; }
    internal override ActorId ViewActorId => View.ActorId;
    internal override bool IsStructurallyValid => View.CurrentPlan is null && View.CurrentStep is null;
    internal override bool HasActiveGoal => View.ActiveGoals.Count != 0;
    internal override DecisionNeedFingerprint Fingerprint
    {
        get
        {
            DecisionProblemDescriptor descriptor =
                DecisionProblemDescriptorBuilder.CreatePlanlessStrategic(View, ProblemCode);
            return DecisionNeedCanonicalJson.CreateFingerprint(
                ActorId,
                null,
                null,
                NeedKind,
                DecisionNeedCanonicalJson.HashProblemDescriptor(descriptor));
        }
    }

    internal override DecisionNeedRegistrationOutcome Register(
        DecisionNeedDiscoveryRegistrar registrar,
        DecisionNeedDiscoveryTrace trace)
    {
        return registrar.RegisterPlanlessStrategic(
            View,
            NeedKind,
            ProblemCode,
            trace,
            FirstObservedWorldRevision,
            CreatedAt,
            Deadline);
    }
}

public enum AgentCentricBindingFailureKind
{
    ActorMismatch,
    InvalidPlanOptionalView
}

public sealed record AgentCentricBindingFailure(
    ActorId ActorId,
    AgentCentricBindingFailureKind Kind);

public abstract class AgentCentricPlanOptionalResult
{
    private protected AgentCentricPlanOptionalResult()
    {
    }
}

public sealed class AgentCentricBindingFailureResult : AgentCentricPlanOptionalResult
{
    private readonly ReadOnlyCollection<AgentCentricBindingFailure> _failures;

    internal AgentCentricBindingFailureResult(IEnumerable<AgentCentricBindingFailure> failures)
    {
        _failures = Array.AsReadOnly(failures.ToArray());
    }

    public IReadOnlyList<AgentCentricBindingFailure> Failures => _failures;
}

public abstract class AgentCentricActorOutcome
{
    private protected AgentCentricActorOutcome(
        AgentCentricTriggerNomination trigger,
        AgentCentricPlanOptionalDecisionBinding binding)
    {
        Trigger = trigger;
        Binding = binding;
    }

    public AgentCentricTriggerNomination Trigger { get; }
    public AgentCentricPlanOptionalDecisionBinding Binding { get; }
}

public sealed class AgentCentricRegistrationReceipt : AgentCentricActorOutcome
{
    internal AgentCentricRegistrationReceipt(
        AgentCentricTriggerNomination trigger,
        AgentCentricPlanOptionalDecisionBinding binding,
        DecisionNeedRegistrationOutcome registrationOutcome,
        DecisionNeed selectedNeed,
        AgentCentricTreatmentRank treatmentRank)
        : base(trigger, binding)
    {
        RegistrationOutcome = registrationOutcome;
        SelectedNeed = selectedNeed;
        TreatmentRank = treatmentRank;
    }

    public DecisionNeedRegistrationOutcome RegistrationOutcome { get; }
    public DecisionNeed SelectedNeed { get; }
    public AgentCentricTreatmentRank TreatmentRank { get; }
}

public sealed class AgentCentricNoActiveGoalReceipt : AgentCentricActorOutcome
{
    internal AgentCentricNoActiveGoalReceipt(
        AgentCentricTriggerNomination trigger,
        AgentCentricPlanlessStrategicDecisionBinding binding)
        : base(trigger, binding)
    {
    }
}

public sealed class AgentCentricPlanOptionalCompleted : AgentCentricPlanOptionalResult
{
    private readonly ReadOnlyCollection<AgentCentricActorOutcome> _actorOutcomes;
    private readonly ReadOnlyCollection<AgentCentricRegistrationReceipt> _queuedSchedule;

    internal AgentCentricPlanOptionalCompleted(
        IEnumerable<AgentCentricActorOutcome> actorOutcomes,
        IEnumerable<AgentCentricRegistrationReceipt> queuedSchedule)
    {
        _actorOutcomes = Array.AsReadOnly(actorOutcomes.ToArray());
        _queuedSchedule = Array.AsReadOnly(queuedSchedule.ToArray());
    }

    public IReadOnlyList<AgentCentricActorOutcome> ActorOutcomes => _actorOutcomes;
    public IReadOnlyList<AgentCentricRegistrationReceipt> QueuedSchedule => _queuedSchedule;
}

/// <summary>Aggregates typed actor-local triggers and registers through the shared plan-optional Store path.</summary>
public sealed class AgentCentricPlanOptionalDecisionNeedRuntime
{
    private const string SourceNamespace = "agent_trigger_v1";
    private readonly DecisionNeedDiscoveryRegistrar _registrar;

    public AgentCentricPlanOptionalDecisionNeedRuntime(DecisionNeedDiscoveryRegistrar registrar)
    {
        ArgumentNullException.ThrowIfNull(registrar);
        _registrar = registrar;
    }

    public AgentCentricPlanOptionalResult Run(
        IEnumerable<AgentCentricTriggerNomination> triggers)
    {
        AgentCentricTriggerNomination[] triggerSnapshot = SnapshotTriggers(triggers);
        AgentCentricBindingFailure[] failures = ValidateBindings(triggerSnapshot);
        if (failures.Length != 0)
        {
            return new AgentCentricBindingFailureResult(failures);
        }

        Dictionary<ActorId, AgentCentricTriggerNomination> bestTriggerByActor = SelectBestTriggers(triggerSnapshot);
        AgentCentricTriggerNomination[] orderedTriggers = bestTriggerByActor.Values.ToArray();
        Array.Sort(orderedTriggers, TriggerComparer.Instance);
        var outcomes = new List<AgentCentricActorOutcome>(orderedTriggers.Length);
        foreach (AgentCentricTriggerNomination trigger in orderedTriggers)
        {
            AgentCentricPlanOptionalDecisionBinding binding = trigger.Binding;
            if (binding is AgentCentricPlanlessStrategicDecisionBinding planless && !binding.HasActiveGoal)
            {
                outcomes.Add(new AgentCentricNoActiveGoalReceipt(trigger, planless));
                continue;
            }

            DecisionNeedRegistrationOutcome registration = binding.Register(_registrar, CreateTrace(trigger));
            DecisionNeed selectedNeed = SelectRegisteredNeed(registration);
            var rank = new AgentCentricTreatmentRank(
                trigger.RankBand,
                trigger.ActorId,
                selectedNeed.Fingerprint);
            outcomes.Add(new AgentCentricRegistrationReceipt(
                trigger,
                binding,
                registration,
                selectedNeed,
                rank));
        }

        var queued = new List<AgentCentricRegistrationReceipt>();
        foreach (AgentCentricActorOutcome outcome in outcomes)
        {
            if (outcome is AgentCentricRegistrationReceipt receipt
                && receipt.SelectedNeed.State == DecisionNeedState.Queued)
            {
                queued.Add(receipt);
            }
        }

        queued.Sort(RegistrationComparer.Instance);
        return new AgentCentricPlanOptionalCompleted(outcomes, queued);
    }

    private static AgentCentricTriggerNomination[] SnapshotTriggers(
        IEnumerable<AgentCentricTriggerNomination> triggers)
    {
        ArgumentNullException.ThrowIfNull(triggers);
        AgentCentricTriggerNomination[] snapshot = triggers.ToArray();
        var unique = new HashSet<AgentCentricTriggerNomination>();
        foreach (AgentCentricTriggerNomination? trigger in snapshot)
        {
            if (trigger is null)
            {
                throw new ArgumentException("AgentCentric triggers cannot contain null.", nameof(triggers));
            }

            if (!unique.Add(trigger))
            {
                throw new ArgumentException("AgentCentric triggers cannot contain duplicate nominations.", nameof(triggers));
            }
        }

        return snapshot;
    }

    private static Dictionary<ActorId, AgentCentricTriggerNomination> SelectBestTriggers(
        IEnumerable<AgentCentricTriggerNomination> triggers)
    {
        var bestByActor = new Dictionary<ActorId, AgentCentricTriggerNomination>();
        foreach (AgentCentricTriggerNomination trigger in triggers)
        {
            if (!bestByActor.TryGetValue(trigger.ActorId, out AgentCentricTriggerNomination? current)
                || TriggerComparer.Instance.Compare(trigger, current) < 0)
            {
                bestByActor[trigger.ActorId] = trigger;
            }
        }

        return bestByActor;
    }

    private static AgentCentricBindingFailure[] ValidateBindings(
        IEnumerable<AgentCentricTriggerNomination> triggers)
    {
        var failures = new HashSet<AgentCentricBindingFailure>();
        foreach (AgentCentricTriggerNomination trigger in triggers)
        {
            AgentCentricPlanOptionalDecisionBinding binding = trigger.Binding;
            if (binding.ActorId != binding.ViewActorId)
            {
                failures.Add(new AgentCentricBindingFailure(
                    binding.ActorId,
                    AgentCentricBindingFailureKind.ActorMismatch));
            }

            if (!binding.IsStructurallyValid)
            {
                failures.Add(new AgentCentricBindingFailure(
                    binding.ActorId,
                    AgentCentricBindingFailureKind.InvalidPlanOptionalView));
            }
        }

        AgentCentricBindingFailure[] result = failures.ToArray();
        Array.Sort(result, BindingFailureComparer.Instance);
        return result;
    }

    private static DecisionNeedDiscoveryTrace CreateTrace(AgentCentricTriggerNomination trigger)
    {
        string source = string.Concat(
            SourceNamespace,
            "/",
            trigger.ActorId.Value,
            "/",
            trigger.RankBand.ToString(),
            "/",
            trigger.TriggerId.Value);
        return new DecisionNeedDiscoveryTrace(
            DecisionNeedDiscoveryRoute.AgentCentric,
            new DecisionNeedDiscoverySourceId(source),
            []);
    }

    private static DecisionNeed SelectRegisteredNeed(DecisionNeedRegistrationOutcome outcome)
    {
        return outcome switch
        {
            RegisteredNew registered => registered.Need,
            DuplicateActive duplicate => duplicate.Need,
            QueuedSupersession supersession => supersession.Replacement,
            InFlightRevalidationPending pending => pending.Replacement,
            StalePreviouslySeen stale => stale.Need,
            _ => throw new InvalidOperationException("AgentCentric registration returned an unsupported Store outcome.")
        };
    }

    private sealed class TriggerComparer : IComparer<AgentCentricTriggerNomination>
    {
        public static TriggerComparer Instance { get; } = new();

        public int Compare(AgentCentricTriggerNomination? left, AgentCentricTriggerNomination? right)
        {
            int rankComparison = left!.RankBand.CompareTo(right!.RankBand);
            if (rankComparison != 0)
            {
                return rankComparison;
            }

            int actorComparison = StringComparer.Ordinal.Compare(left.ActorId.Value, right!.ActorId.Value);
            if (actorComparison != 0)
            {
                return actorComparison;
            }

            int fingerprintComparison = StringComparer.Ordinal.Compare(
                left.Binding.Fingerprint.Value,
                right.Binding.Fingerprint.Value);
            return fingerprintComparison != 0
                ? fingerprintComparison
                : StringComparer.Ordinal.Compare(left.TriggerId.Value, right.TriggerId.Value);
        }
    }

    private sealed class RegistrationComparer : IComparer<AgentCentricRegistrationReceipt>
    {
        public static RegistrationComparer Instance { get; } = new();

        public int Compare(AgentCentricRegistrationReceipt? left, AgentCentricRegistrationReceipt? right)
        {
            AgentCentricTreatmentRank leftRank = left!.TreatmentRank;
            AgentCentricTreatmentRank rightRank = right!.TreatmentRank;
            int rankComparison = leftRank.RankBand.CompareTo(rightRank.RankBand);
            if (rankComparison != 0)
            {
                return rankComparison;
            }

            int actorComparison = StringComparer.Ordinal.Compare(leftRank.ActorId.Value, rightRank.ActorId.Value);
            return actorComparison != 0
                ? actorComparison
                : StringComparer.Ordinal.Compare(leftRank.Fingerprint.Value, rightRank.Fingerprint.Value);
        }
    }

    private sealed class BindingFailureComparer : IComparer<AgentCentricBindingFailure>
    {
        public static BindingFailureComparer Instance { get; } = new();

        public int Compare(AgentCentricBindingFailure? left, AgentCentricBindingFailure? right)
        {
            int actorComparison = StringComparer.Ordinal.Compare(left!.ActorId.Value, right!.ActorId.Value);
            return actorComparison != 0
                ? actorComparison
                : left.Kind.CompareTo(right.Kind);
        }
    }
}

using System.Collections.ObjectModel;
using Alice.Actors;
using Alice.Interaction;
using Alice.Items;
using Alice.Npc;

namespace Alice.Cognition;

/// <summary>Version-free immutable self projection visible to a future local model.</summary>
public sealed class LocalReasonerSelfView : IEquatable<LocalReasonerSelfView>
{
    private readonly ReadOnlyCollection<InventoryEntry> _inventoryEntries;

    internal LocalReasonerSelfView(SharedActorState source)
    {
        ArgumentNullException.ThrowIfNull(source);
        ActorId actorId = source.Identity.ActorId;
        if (source.Body.ActorId != actorId ||
            source.Traversal.ActorId != actorId ||
            source.Inventory.ActorId != actorId ||
            source.Equipment.ActorId != actorId)
        {
            throw new ArgumentException("Filtered self components must belong to one ActorId.", nameof(source));
        }

        Identity = source.Identity;
        Body = source.Body;
        Traversal = source.Traversal;
        _inventoryEntries = Array.AsReadOnly(source.Inventory.Entries.ToArray());
        HandItem = source.Equipment.HandItemRef;
    }

    public ActorIdentity Identity { get; }
    public ActorBodyState Body { get; }
    public ActorTraversalState Traversal { get; }
    public IReadOnlyList<InventoryEntry> InventoryEntries => _inventoryEntries;
    public HandItemRef? HandItem { get; }

    public bool Equals(LocalReasonerSelfView? other)
    {
        return other is not null &&
            Identity == other.Identity &&
            Body == other.Body &&
            Traversal == other.Traversal &&
            InventoryEntries.SequenceEqual(other.InventoryEntries) &&
            HandItem == other.HandItem;
    }

    public override bool Equals(object? obj) => Equals(obj as LocalReasonerSelfView);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Identity);
        hash.Add(Body);
        hash.Add(Traversal);
        foreach (InventoryEntry entry in InventoryEntries)
        {
            hash.Add(entry);
        }

        hash.Add(HandItem);
        return hash.ToHashCode();
    }
}

/// <summary>Closed score-free model-visible local option.</summary>
public abstract record LocalReasonerOption
{
    private protected LocalReasonerOption(LocalCandidateId candidateId, GameActionSpec action)
    {
        ArgumentNullException.ThrowIfNull(candidateId);
        ArgumentNullException.ThrowIfNull(action);
        CandidateId = candidateId;
        Action = action;
    }

    public LocalCandidateId CandidateId { get; }
    public GameActionSpec Action { get; }
}

public sealed record LocalReasonerDamageOption : LocalReasonerOption
{
    internal LocalReasonerDamageOption(
        LocalCandidateId candidateId,
        GameActionSpec action,
        KnownDamageOpportunity knownOpportunity)
        : base(candidateId, action)
    {
        ArgumentNullException.ThrowIfNull(knownOpportunity);
        KnownOpportunity = knownOpportunity;
    }

    public KnownDamageOpportunity KnownOpportunity { get; }
}

public sealed record LocalReasonerConsumptionOption : LocalReasonerOption
{
    internal LocalReasonerConsumptionOption(
        LocalCandidateId candidateId,
        GameActionSpec action,
        KnownConsumptionOpportunity knownOpportunity)
        : base(candidateId, action)
    {
        ArgumentNullException.ThrowIfNull(knownOpportunity);
        KnownOpportunity = knownOpportunity;
    }

    public KnownConsumptionOpportunity KnownOpportunity { get; }
}

public sealed record LocalReasonerPickupOption : LocalReasonerOption
{
    internal LocalReasonerPickupOption(
        LocalCandidateId candidateId,
        GameActionSpec action,
        KnownPickupOpportunity knownOpportunity)
        : base(candidateId, action)
    {
        ArgumentNullException.ThrowIfNull(knownOpportunity);
        KnownOpportunity = knownOpportunity;
    }

    public KnownPickupOpportunity KnownOpportunity { get; }
}

/// <summary>Bounded score-hidden current-step payload for a future stateless local reasoner.</summary>
public sealed class LocalReasonerContext : IEquatable<LocalReasonerContext>
{
    private readonly IPersonalityPriorView _personality;
    private readonly ReadOnlyCollection<LocalReasonerOption> _options;

    internal LocalReasonerContext(
        LocalReasonerSelfView self,
        IPersonalityPriorView personality,
        NpcGoal currentGoal,
        PlanStep currentStep,
        IEnumerable<LocalReasonerOption> options)
    {
        ArgumentNullException.ThrowIfNull(self);
        ArgumentNullException.ThrowIfNull(personality);
        ArgumentNullException.ThrowIfNull(currentGoal);
        ArgumentNullException.ThrowIfNull(currentStep);
        ArgumentNullException.ThrowIfNull(options);
        LocalReasonerOption[] snapshot = options.ToArray();
        if (snapshot.Length < 2 || snapshot.Length > DecisionGate.MAX_L1_CANDIDATES)
        {
            throw new ArgumentOutOfRangeException(nameof(options));
        }

        for (int index = 0; index < snapshot.Length; index++)
        {
            LocalReasonerOption option = snapshot[index] ?? throw new ArgumentNullException(nameof(options));
            if (option.Action.ActorId != self.Identity.ActorId)
            {
                throw new ArgumentException("Every local option must belong to the filtered self ActorId.", nameof(options));
            }

            for (int previous = 0; previous < index; previous++)
            {
                if (snapshot[previous].CandidateId == option.CandidateId)
                {
                    throw new ArgumentException("Local reasoner options must have unique CandidateIds.", nameof(options));
                }
            }
        }

        Self = self;
        _personality = personality;
        CurrentGoal = currentGoal;
        CurrentStep = currentStep;
        _options = Array.AsReadOnly(snapshot);
    }

    public ActorId ActorId => Self.Identity.ActorId;
    public LocalReasonerSelfView Self { get; }
    public IPersonalityPriorView Personality => _personality;
    public NpcGoal CurrentGoal { get; }
    public PlanStep CurrentStep { get; }
    public IReadOnlyList<LocalReasonerOption> Options => _options;

    public bool Equals(LocalReasonerContext? other)
    {
        return other is not null &&
            Self.Equals(other.Self) &&
            Equals(Personality, other.Personality) &&
            CurrentGoal == other.CurrentGoal &&
            CurrentStep.Equals(other.CurrentStep) &&
            Options.SequenceEqual(other.Options);
    }

    public override bool Equals(object? obj) => Equals(obj as LocalReasonerContext);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Self);
        hash.Add(Personality);
        hash.Add(CurrentGoal);
        hash.Add(CurrentStep);
        foreach (LocalReasonerOption option in Options)
        {
            hash.Add(option);
        }

        return hash.ToHashCode();
    }
}

using System.Collections.ObjectModel;
using Alice.Damage;
using Alice.Interaction;
using Alice.Items;
using Alice.Navigation;
using Alice.Validation;
using Alice.World;

namespace Alice.Authority;

/// <summary>Single synchronous owner for one target's validated damage, instrument and drop mutations.</summary>
public sealed class DamageAuthorityRuntime
{
    private readonly ReadOnlyCollection<ItemDefinition> _itemDefinitions;
    private readonly WorldDropId _terminalWorldDropId;
    private ReadOnlyCollection<ItemInstance> _itemInstances;
    private DestructibleState _destructibleState;
    private TerminalRepresentationId? _terminalRepresentation;
    private WorldDrop? _worldDrop;

    public DamageAuthorityRuntime(
        TargetRef targetRef,
        DamageContract currentContract,
        WorldPosition targetPosition,
        DestructibleState destructibleState,
        DestructionProfile destructionProfile,
        IEnumerable<ItemInstance> itemInstances,
        IEnumerable<ItemDefinition> itemDefinitions,
        WorldDropId terminalWorldDropId)
    {
        ArgumentNullException.ThrowIfNull(targetRef);
        ArgumentNullException.ThrowIfNull(currentContract);
        ArgumentNullException.ThrowIfNull(destructibleState);
        ArgumentNullException.ThrowIfNull(destructionProfile);
        ArgumentNullException.ThrowIfNull(itemInstances);
        ArgumentNullException.ThrowIfNull(itemDefinitions);
        ArgumentNullException.ThrowIfNull(terminalWorldDropId);
        if (targetRef != currentContract.ContractRef.TargetRef)
        {
            throw new ArgumentException("Authority target must match its exact damage contract.", nameof(targetRef));
        }

        if (destructibleState.CurrentHP == 0)
        {
            throw new ArgumentException("Authority runtime cannot start terminal.", nameof(destructibleState));
        }

        WorldPositionValidation.Validate(targetPosition, nameof(targetPosition));

        ItemInstance[] instanceSnapshot = itemInstances.ToArray();
        ItemDefinition[] definitionSnapshot = itemDefinitions.ToArray();
        EnsureUniqueInstances(instanceSnapshot);
        EnsureUniqueDefinitions(definitionSnapshot);

        TargetRef = targetRef;
        CurrentContract = currentContract;
        TargetPosition = targetPosition;
        _destructibleState = destructibleState;
        DestructionProfile = destructionProfile;
        _itemInstances = Array.AsReadOnly(instanceSnapshot);
        _itemDefinitions = Array.AsReadOnly(definitionSnapshot);
        _terminalWorldDropId = terminalWorldDropId;
    }

    public TargetRef TargetRef { get; }
    public DamageContract CurrentContract { get; }
    public WorldPosition TargetPosition { get; }
    public DestructibleState DestructibleState => _destructibleState;
    public DestructionProfile DestructionProfile { get; }
    public IReadOnlyList<ItemInstance> ItemInstances => _itemInstances;
    public IReadOnlyList<ItemDefinition> ItemDefinitions => _itemDefinitions;
    public TerminalRepresentationId? TerminalRepresentation => _terminalRepresentation;
    public WorldDrop? WorldDrop => _worldDrop;

    public DamageCommitResult TryCommitDamage(GameActionSpec action, GameActionId gameActionId, DamageValidationContext context)
    {
        ArgumentNullException.ThrowIfNull(gameActionId);
        DamageValidationResult validation = DamageActionValidator.Validate(
            action,
            context,
            CurrentContract,
            TargetPosition,
            _destructibleState,
            _itemInstances,
            _itemDefinitions);
        if (!validation.IsValid || validation.Commit is null)
        {
            return new DamageCommitResult(null, validation.Failure);
        }

        ValidatedDamageCommit commit = validation.Commit;
        int previousHP = _destructibleState.CurrentHP;
        int currentHP = Math.Max(0, previousHP - commit.FinalDamage);
        ItemInstance? replacementInstrument = null;
        if (commit.Instrument is not null)
        {
            try
            {
                replacementInstrument = new ItemInstance(
                    commit.Instrument.ItemInstanceId,
                    commit.Instrument.ItemTypeId,
                    commit.Instrument.Durability!.Value - 1,
                    checked(commit.Instrument.Version + 1));
            }
            catch (OverflowException)
            {
                return new DamageCommitResult(null, DamageValidationFailure.CheckedNumericOverflow);
            }
        }

        TerminalRepresentationId? terminalRepresentation = null;
        WorldDrop? worldDrop = null;
        if (currentHP == 0)
        {
            terminalRepresentation = DestructionProfile.TerminalRepresentation;
            worldDrop = new WorldDrop(_terminalWorldDropId, DestructionProfile.Yields, TargetPosition, commit.ActorId, 1);
        }

        DestructibleState replacementState = new(_destructibleState.MaxHP, currentHP);
        ReadOnlyCollection<ItemInstance> replacementInstances = replacementInstrument is null
            ? _itemInstances
            : ReplaceInstrument(replacementInstrument);
        var receipt = new DamageCommitReceipt(
            new CommitOrigin.ActorAction(commit.ActorId, gameActionId),
            commit.Contract.ContractRef,
            commit.Contract.Version,
            commit.DamageType,
            commit.BaseDamage,
            commit.FinalDamage,
            previousHP,
            currentHP,
            commit.Instrument?.ItemInstanceId,
            commit.Instrument?.Durability,
            replacementInstrument?.Durability,
            commit.Instrument?.Version,
            replacementInstrument?.Version,
            terminalRepresentation,
            worldDrop);

        _destructibleState = replacementState;
        _itemInstances = replacementInstances;
        _terminalRepresentation = terminalRepresentation;
        _worldDrop = worldDrop;
        return new DamageCommitResult(receipt, null);
    }

    internal void ReleaseTerminalWorldDrop(WorldDrop expectedWorldDrop)
    {
        ArgumentNullException.ThrowIfNull(expectedWorldDrop);
        if (!ReferenceEquals(_worldDrop, expectedWorldDrop))
        {
            throw new InvalidOperationException("Only the exact currently owned terminal drop may be released.");
        }

        _worldDrop = null;
    }

    private ReadOnlyCollection<ItemInstance> ReplaceInstrument(ItemInstance replacementInstrument)
    {
        var replacements = new ItemInstance[_itemInstances.Count];
        for (int index = 0; index < _itemInstances.Count; index++)
        {
            ItemInstance current = _itemInstances[index];
            replacements[index] = current.ItemInstanceId == replacementInstrument.ItemInstanceId ? replacementInstrument : current;
        }

        return Array.AsReadOnly(replacements);
    }

    private static void EnsureUniqueInstances(IEnumerable<ItemInstance> itemInstances)
    {
        var identities = new HashSet<string>(StringComparer.Ordinal);
        foreach (ItemInstance itemInstance in itemInstances)
        {
            ArgumentNullException.ThrowIfNull(itemInstance);
            if (!identities.Add(itemInstance.ItemInstanceId.Value))
            {
                throw new ArgumentException("Item instance snapshots must be unique.", nameof(itemInstances));
            }
        }
    }

    private static void EnsureUniqueDefinitions(IEnumerable<ItemDefinition> itemDefinitions)
    {
        var identities = new HashSet<string>(StringComparer.Ordinal);
        foreach (ItemDefinition itemDefinition in itemDefinitions)
        {
            ArgumentNullException.ThrowIfNull(itemDefinition);
            if (!identities.Add(itemDefinition.ItemTypeId.Value))
            {
                throw new ArgumentException("Item definition snapshots must be unique.", nameof(itemDefinitions));
            }
        }
    }
}

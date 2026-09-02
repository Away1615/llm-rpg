using System.Collections.ObjectModel;
using Alice.Identity;
using Alice.Items;

namespace Alice.Damage;

/// <summary>Typed content identity for the terminal representation of a destructible target.</summary>
public sealed class TerminalRepresentationId : IEquatable<TerminalRepresentationId>
{
    public TerminalRepresentationId(string value)
    {
        NonEmptyIdentityValue.Validate(value, nameof(value), "Terminal representation identity must be non-empty.");
        Value = value;
    }

    public string Value { get; }

    public bool Equals(TerminalRepresentationId? other) => other is not null && Value == other.Value;

    public override bool Equals(object? obj) => Equals(obj as TerminalRepresentationId);

    public override int GetHashCode() => Value.GetHashCode(StringComparison.Ordinal);
}

/// <summary>One future terminal yield specification, not an inventory entry or world drop.</summary>
public sealed class DestructionYield : IEquatable<DestructionYield>
{
    public DestructionYield(ItemTypeId itemTypeId, int quantity)
    {
        ArgumentNullException.ThrowIfNull(itemTypeId);
        if (quantity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(quantity));
        }

        ItemTypeId = itemTypeId;
        Quantity = quantity;
    }

    public ItemTypeId ItemTypeId { get; }
    public int Quantity { get; }

    public bool Equals(DestructionYield? other)
    {
        return other is not null && ItemTypeId == other.ItemTypeId && Quantity == other.Quantity;
    }

    public override bool Equals(object? obj) => Equals(obj as DestructionYield);

    public override int GetHashCode() => HashCode.Combine(ItemTypeId, Quantity);
}

/// <summary>Frozen terminal settlement policy vocabulary.</summary>
public enum DestructionDropPolicy
{
    ClaimantWorldDrop
}

/// <summary>Immutable terminal representation and yield policy for a destructible target.</summary>
public sealed class DestructionProfile : IEquatable<DestructionProfile>
{
    private readonly ReadOnlyCollection<DestructionYield> _yields;

    public DestructionProfile(
        TerminalRepresentationId terminalRepresentation,
        IEnumerable<DestructionYield> yields,
        DestructionDropPolicy dropPolicy)
    {
        ArgumentNullException.ThrowIfNull(terminalRepresentation);
        ArgumentNullException.ThrowIfNull(yields);
        if (!Enum.IsDefined(dropPolicy))
        {
            throw new ArgumentOutOfRangeException(nameof(dropPolicy));
        }

        DestructionYield[] snapshot = yields.ToArray();
        var itemTypes = new HashSet<ItemTypeId>();
        foreach (DestructionYield yield in snapshot)
        {
            ArgumentNullException.ThrowIfNull(yield);
            if (!itemTypes.Add(yield.ItemTypeId))
            {
                throw new ArgumentException("Destruction yields must have unique item types.", nameof(yields));
            }
        }

        Array.Sort(snapshot, CompareYields);
        TerminalRepresentation = terminalRepresentation;
        _yields = Array.AsReadOnly(snapshot);
        DropPolicy = dropPolicy;
    }

    public TerminalRepresentationId TerminalRepresentation { get; }
    public IReadOnlyList<DestructionYield> Yields => _yields;
    public DestructionDropPolicy DropPolicy { get; }

    public bool Equals(DestructionProfile? other)
    {
        return other is not null &&
            TerminalRepresentation.Equals(other.TerminalRepresentation) &&
            DropPolicy == other.DropPolicy &&
            Yields.SequenceEqual(other.Yields);
    }

    public override bool Equals(object? obj) => Equals(obj as DestructionProfile);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(TerminalRepresentation);
        hash.Add(DropPolicy);
        foreach (DestructionYield yield in Yields)
        {
            hash.Add(yield);
        }

        return hash.ToHashCode();
    }

    private static int CompareYields(DestructionYield left, DestructionYield right)
    {
        return StringComparer.Ordinal.Compare(left.ItemTypeId.Value, right.ItemTypeId.Value);
    }
}

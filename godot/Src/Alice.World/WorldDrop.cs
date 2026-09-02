using System.Collections.ObjectModel;
using Alice.Actors;
using Alice.Damage;
using Alice.Identity;
using Alice.Navigation;

namespace Alice.World;

/// <summary>Stable Authority-preallocated identity for one terminal world drop.</summary>
public sealed class WorldDropId : IEquatable<WorldDropId>
{
    public WorldDropId(string value)
    {
        NonEmptyIdentityValue.Validate(value, nameof(value), "World drop identity must be non-empty.");
        Value = value;
    }

    public string Value { get; }
    public bool Equals(WorldDropId? other) => other is not null && StringComparer.Ordinal.Equals(Value, other.Value);
    public override bool Equals(object? obj) => Equals(obj as WorldDropId);
    public override int GetHashCode() => Value.GetHashCode(StringComparison.Ordinal);
}

/// <summary>Immutable claimant-owned terminal yield snapshot; this slice never performs pickup.</summary>
public sealed class WorldDrop : IEquatable<WorldDrop>
{
    private readonly ReadOnlyCollection<DestructionYield> _items;

    public WorldDrop(
        WorldDropId dropId,
        IEnumerable<DestructionYield> items,
        WorldPosition position,
        ActorId? claimantActorId,
        int version)
    {
        ArgumentNullException.ThrowIfNull(dropId);
        ArgumentNullException.ThrowIfNull(items);
        WorldPositionValidation.Validate(position, nameof(position));

        if (claimantActorId is ActorId claimant && string.IsNullOrWhiteSpace(claimant.Value))
        {
            throw new ArgumentException("Claimant actor identity must be non-empty.", nameof(claimantActorId));
        }

        if (version <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(version));
        }

        DestructionYield[] snapshot = items.ToArray();
        var itemTypes = new HashSet<string>(StringComparer.Ordinal);
        foreach (DestructionYield item in snapshot)
        {
            ArgumentNullException.ThrowIfNull(item);
            if (!itemTypes.Add(item.ItemTypeId.Value))
            {
                throw new ArgumentException("World drop items must have unique item types.", nameof(items));
            }
        }

        Array.Sort(snapshot, DestructionYieldComparer.Instance);
        DropId = dropId;
        _items = Array.AsReadOnly(snapshot);
        Position = position;
        ClaimantActorId = claimantActorId;
        Version = version;
    }

    public WorldDropId DropId { get; }
    public IReadOnlyList<DestructionYield> Items => _items;
    public WorldPosition Position { get; }
    public ActorId? ClaimantActorId { get; }
    public int Version { get; }

    public bool Equals(WorldDrop? other)
    {
        return other is not null &&
            DropId.Equals(other.DropId) &&
            Items.SequenceEqual(other.Items) &&
            Position == other.Position &&
            ClaimantActorId == other.ClaimantActorId &&
            Version == other.Version;
    }

    public override bool Equals(object? obj) => Equals(obj as WorldDrop);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(DropId);
        foreach (DestructionYield item in Items)
        {
            hash.Add(item);
        }

        hash.Add(Position);
        hash.Add(ClaimantActorId);
        hash.Add(Version);
        return hash.ToHashCode();
    }

    private sealed class DestructionYieldComparer : IComparer<DestructionYield>
    {
        public static DestructionYieldComparer Instance { get; } = new();

        public int Compare(DestructionYield? left, DestructionYield? right)
        {
            return StringComparer.Ordinal.Compare(left?.ItemTypeId.Value, right?.ItemTypeId.Value);
        }
    }
}

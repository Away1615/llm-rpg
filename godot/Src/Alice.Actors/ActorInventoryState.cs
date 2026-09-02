using System.Collections.ObjectModel;
using Alice.Items;

namespace Alice.Actors;

/// <summary>Immutable controller-neutral carried-entry state for one Actor.</summary>
public sealed class InventoryState : IEquatable<InventoryState>
{
    private readonly ReadOnlyCollection<InventoryEntry> _entries;

    public InventoryState(ActorId actorId, IEnumerable<InventoryEntry> entries, int version)
    {
        ActorIdentity.ValidateActorId(actorId);
        ArgumentNullException.ThrowIfNull(entries);
        if (version <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(version));
        }

        InventoryEntry[] snapshot = entries.ToArray();
        var carriedInstances = new HashSet<ItemInstanceId>();
        foreach (InventoryEntry entry in snapshot)
        {
            ArgumentNullException.ThrowIfNull(entry);
            if (entry is InstanceEntry instanceEntry && !carriedInstances.Add(instanceEntry.ItemInstanceId))
            {
                throw new ArgumentException("Inventory cannot carry the same item instance twice.", nameof(entries));
            }
        }

        ActorId = actorId;
        _entries = Array.AsReadOnly(snapshot);
        Version = version;
    }

    public ActorId ActorId { get; }
    public IReadOnlyList<InventoryEntry> Entries => _entries;
    public int Version { get; }

    public bool Equals(InventoryState? other)
    {
        return other is not null &&
            ActorId == other.ActorId &&
            Version == other.Version &&
            Entries.SequenceEqual(other.Entries);
    }

    public override bool Equals(object? obj) => Equals(obj as InventoryState);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(ActorId);
        hash.Add(Version);
        foreach (InventoryEntry entry in Entries)
        {
            hash.Add(entry);
        }

        return hash.ToHashCode();
    }
}

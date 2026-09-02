using Alice.Interaction;

namespace Alice.Activities;

/// <summary>Closed-runtime marker for immutable activity startup correlation evidence.</summary>
public interface IActivityDependencySnapshot
{
}

/// <summary>Immutable startup correlation evidence, never a later validation verdict.</summary>
public sealed class ActivityDependencySnapshot : IActivityDependencySnapshot, IEquatable<ActivityDependencySnapshot>
{
    public ActivityDependencySnapshot(
        ExpectedContractVersion expectedContractVersion,
        int startingInventoryVersion,
        int startingEquipmentVersion,
        int? selectedInstrumentVersion)
    {
        if (expectedContractVersion.Value <= 0 || startingInventoryVersion <= 0 || startingEquipmentVersion <= 0 || selectedInstrumentVersion is <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(expectedContractVersion));
        }

        ExpectedContractVersion = expectedContractVersion;
        StartingInventoryVersion = startingInventoryVersion;
        StartingEquipmentVersion = startingEquipmentVersion;
        SelectedInstrumentVersion = selectedInstrumentVersion;
    }

    public ExpectedContractVersion ExpectedContractVersion { get; }
    public int StartingInventoryVersion { get; }
    public int StartingEquipmentVersion { get; }
    public int? SelectedInstrumentVersion { get; }

    public bool Equals(ActivityDependencySnapshot? other)
    {
        return other is not null &&
            ExpectedContractVersion == other.ExpectedContractVersion &&
            StartingInventoryVersion == other.StartingInventoryVersion &&
            StartingEquipmentVersion == other.StartingEquipmentVersion &&
            SelectedInstrumentVersion == other.SelectedInstrumentVersion;
    }

    public override bool Equals(object? obj) => Equals(obj as ActivityDependencySnapshot);
    public override int GetHashCode() => HashCode.Combine(ExpectedContractVersion, StartingInventoryVersion, StartingEquipmentVersion, SelectedInstrumentVersion);
}

namespace Alice.Damage;

/// <summary>Immutable authoritative health snapshot for one destructible target.</summary>
public sealed class DestructibleState : IEquatable<DestructibleState>
{
    public DestructibleState(int maxHP, int currentHP)
    {
        if (maxHP <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxHP));
        }

        if (currentHP < 0 || currentHP > maxHP)
        {
            throw new ArgumentOutOfRangeException(nameof(currentHP));
        }

        MaxHP = maxHP;
        CurrentHP = currentHP;
    }

    public int MaxHP { get; }
    public int CurrentHP { get; }

    public bool Equals(DestructibleState? other)
    {
        return other is not null && MaxHP == other.MaxHP && CurrentHP == other.CurrentHP;
    }

    public override bool Equals(object? obj) => Equals(obj as DestructibleState);

    public override int GetHashCode() => HashCode.Combine(MaxHP, CurrentHP);
}

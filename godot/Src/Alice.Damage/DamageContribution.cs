namespace Alice.Damage;

/// <summary>Closed damage vocabulary carried beside, but outside, capability arithmetic.</summary>
public enum DamageType
{
    Slashing,
    Bludgeoning,
    Piercing
}

/// <summary>One positive typed base-damage contribution with no target/effect semantics.</summary>
public sealed record DamageContribution
{
    public DamageContribution(DamageType damageType, int baseDamage)
    {
        if (baseDamage <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(baseDamage));
        }

        DamageType = damageType;
        BaseDamage = baseDamage;
    }

    public DamageType DamageType { get; }
    public int BaseDamage { get; }
}

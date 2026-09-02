namespace Alice.Interaction;

/// <summary>A binding-owned interaction stop range.</summary>
public readonly record struct InteractionRange
{
    public InteractionRange(double value)
    {
        if (!double.IsFinite(value) || value < 0.0)
        {
            throw new ArgumentOutOfRangeException(nameof(value), "Interaction range must be finite and non-negative.");
        }

        Value = value;
    }

    public double Value { get; }
}

/// <summary>Resolves range only for the exact selected typed binding.</summary>
public interface IInteractionRangeQuery
{
    bool TryResolve(InteractionBinding binding, out InteractionRange range);
}

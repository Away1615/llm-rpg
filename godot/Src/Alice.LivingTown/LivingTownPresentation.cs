namespace Alice.LivingTown;

public sealed record LivingTownCharacterProjection(string? ActivityLabel);

/// <summary>Deterministic semantic-to-visual projection. No model-authored display text enters this boundary.</summary>
public static class LivingTownPresentationProjector
{
    public static LivingTownCharacterProjection Project(LivingTownActivityKind activityKind)
    {
        return activityKind switch
        {
            LivingTownActivityKind.None => new LivingTownCharacterProjection(null),
            LivingTownActivityKind.Travel => Activity("Travelling"),
            LivingTownActivityKind.Waiting => new LivingTownCharacterProjection(null),
            LivingTownActivityKind.Work => Activity("Working"),
            LivingTownActivityKind.Sleep => Activity("Sleeping"),
            LivingTownActivityKind.Gather => Activity("Gathering"),
            LivingTownActivityKind.Consumption => Activity("Eating"),
            LivingTownActivityKind.Social => Activity("Talking"),
            LivingTownActivityKind.Experience => Activity("Reflecting"),
            _ => throw new ArgumentOutOfRangeException(nameof(activityKind))
        };
    }

    private static LivingTownCharacterProjection Activity(string activityLabel) => new(activityLabel);
}

namespace Alice.Activities;

public enum ActivityRuntimeKind
{
    Damage,
    Travel
}

public enum ActivityProgressMode
{
    OffscreenSimTime,
    Projected
}

public enum ActivityRuntimeStatus
{
    Active,
    AwaitingSettlement,
    Suspended,
    Completed,
    Rejected,
    Cancelled
}

namespace Alice.Navigation;

/// <summary>
/// An immutable point in world coordinates at the domain/navigation boundary.
/// </summary>
public readonly record struct WorldPosition(double X, double Y);

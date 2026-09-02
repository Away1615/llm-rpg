namespace Alice.Identity;

/// <summary>Shared non-blank validation for the many typed identity/name wrappers that only require a non-blank string.</summary>
internal static class NonEmptyIdentityValue
{
    public static void Validate(string? value, string paramName, string message)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException(message, paramName);
        }
    }
}

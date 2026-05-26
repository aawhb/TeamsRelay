namespace TeamsRelay.Core;

public static class TextUtilities
{
    public static string TruncateWithEllipsis(string value, int maxLength)
    {
        ArgumentNullException.ThrowIfNull(value);

        if (value.Length <= maxLength)
        {
            return value;
        }

        return maxLength >= 4
            ? value[..(maxLength - 3)] + "..."
            : value[..maxLength];
    }
}

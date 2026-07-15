namespace TeamsRelay.Core;

public static class TextUtilities
{
    public static string NormalizeWhitespace(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : string.Join(' ', value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    }

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

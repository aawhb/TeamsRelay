namespace TeamsRelay.Core;

public static class TeamsProcessNames
{
    public const string Teams = "ms-teams";
    public const string WebView2 = "msedgewebview2";

    public static bool IsTeamsRelated(string? processName)
    {
        if (string.IsNullOrWhiteSpace(processName))
        {
            return false;
        }

        return string.Equals(processName, Teams, StringComparison.OrdinalIgnoreCase)
            || string.Equals(processName, WebView2, StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsWebView2(string? processName)
    {
        return string.Equals(processName, WebView2, StringComparison.OrdinalIgnoreCase);
    }
}

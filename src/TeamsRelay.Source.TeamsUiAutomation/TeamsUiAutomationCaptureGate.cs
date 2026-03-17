namespace TeamsRelay.Source.TeamsUiAutomation;

internal static class TeamsUiAutomationCaptureGate
{
    private const int BroadRectMinWidth = 120;
    private const int BroadRectMinHeight = 40;
    private const int BroadRectMaxWidth = 1200;
    private const int BroadRectMaxHeight = 500;

    public static bool ShouldExtractText(
        UiAutomationCaptureContext context,
        string? processName,
        bool applyBroadRectGate,
        out string reason)
    {
        if (applyBroadRectGate
            && (context.RectEmpty
                || context.Width < BroadRectMinWidth
                || context.Height < BroadRectMinHeight
                || context.Width > BroadRectMaxWidth
                || context.Height > BroadRectMaxHeight))
        {
            reason = "broad_rect_gate";
            return false;
        }

        return TryValidateProcess(processName, context.TopLevelWindowName, out reason);
    }

    private static bool TryValidateProcess(string? processName, string topLevelWindowName, out string reason)
    {
        if (string.IsNullOrWhiteSpace(processName))
        {
            reason = "process_not_found";
            return false;
        }

        if (!string.Equals(processName, "ms-teams", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(processName, "msedgewebview2", StringComparison.OrdinalIgnoreCase))
        {
            reason = "not_teams_process";
            return false;
        }

        if (string.Equals(processName, "msedgewebview2", StringComparison.OrdinalIgnoreCase)
            && !topLevelWindowName.Contains("Teams", StringComparison.OrdinalIgnoreCase))
        {
            reason = "not_teams_window";
            return false;
        }

        reason = string.Empty;
        return true;
    }
}

internal readonly record struct UiAutomationCaptureContext(
    bool RectEmpty,
    double Width,
    double Height,
    string TopLevelWindowName);

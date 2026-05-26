namespace TeamsRelay.Core;

public static class TeamsNotificationSurfaceClassifier
{
    public static bool IsFromNotificationToast(UiElementSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        if (snapshot.AutomationId.StartsWith("toast", StringComparison.OrdinalIgnoreCase))
            return true;

        var topLevel = TeamsNotificationTextAnalyzer.NormalizeVisibleText(snapshot.TopLevelWindowName);

        if (topLevel.Contains("notification", StringComparison.OrdinalIgnoreCase))
            return true;

        if (string.IsNullOrWhiteSpace(topLevel))
            return false;

        if (IsMainTeamsWindowTitle(topLevel))
            return false;

        if (topLevel.Equals("Microsoft Teams", StringComparison.OrdinalIgnoreCase))
            return !IsKnownMainPaneAutomationId(snapshot.AutomationId);

        return true;
    }

    public static bool IsMainTeamsWindowTitle(string topLevel)
    {
        if (topLevel.StartsWith("Microsoft Teams -", StringComparison.OrdinalIgnoreCase)
            || topLevel.StartsWith("Microsoft Teams –", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return !topLevel.Equals("Microsoft Teams", StringComparison.OrdinalIgnoreCase)
            && topLevel.EndsWith("Microsoft Teams", StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsKnownMainPaneAutomationId(string automationId)
    {
        if (string.IsNullOrWhiteSpace(automationId))
        {
            return false;
        }

        return automationId.Equals("chat-pane", StringComparison.OrdinalIgnoreCase)
            || automationId.Equals("app-root", StringComparison.OrdinalIgnoreCase);
    }
}

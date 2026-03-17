namespace TeamsRelay.Source.TeamsUiAutomation;

internal static class TeamsUiAutomationSubscriptionMode
{
    public static bool IncludesWindowOpened(string mode)
    {
        return !string.Equals(mode, "structure_changed_only", StringComparison.OrdinalIgnoreCase);
    }

    public static bool IncludesStructureChanged(string mode)
    {
        return !string.Equals(mode, "window_opened_only", StringComparison.OrdinalIgnoreCase);
    }
}

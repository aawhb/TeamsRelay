namespace TeamsRelay.Core;

public abstract record UiElementSnapshot
{
    public int ProcessId { get; init; }

    public string WindowName { get; init; } = string.Empty;

    public string ClassName { get; init; } = string.Empty;

    public string RootControlType { get; init; } = string.Empty;

    public string AutomationId { get; init; } = string.Empty;

    public string TopLevelWindowName { get; init; } = string.Empty;

    public string TopLevelClassName { get; init; } = string.Empty;

    public bool RectEmpty { get; init; }

    public double Left { get; init; }

    public double Top { get; init; }

    public double Width { get; init; }

    public double Height { get; init; }

    public string ExtractedText { get; init; } = string.Empty;
}

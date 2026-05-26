namespace TeamsRelay.Core;

public static class RelayEventKinds
{
    public const string WindowOpened = "window_opened";
    public const string StructureChanged = "structure_changed";
}

public static class RelayCapturePaths
{
    public const string WindowOpened = "window_opened";
    public const string WindowOpenedEnriched = "window_opened_enriched";
    public const string StructureChangedBanner = "structure_changed_banner";
    public const string StructureChangedTest = "structure_changed_test";
}

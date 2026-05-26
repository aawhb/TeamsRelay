using TeamsRelay.Core;
using TeamsRelay.Source.TeamsUiAutomation;

namespace TeamsRelay.Tests;

public sealed class TeamsUiAutomationSourceAdapterTests
{
    [Fact]
    public void AdapterStartsAndStopsWithoutThrowing()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        using var adapter = new TeamsUiAutomationSourceAdapter(new FakeProcessNameResolver());
        adapter.Start();

        var canRead = adapter.TryDequeue(out var record);

        adapter.Stop();

        Assert.False(canRead);
        Assert.Null(record);
    }

    [Fact]
    public void CaptureGateRejectsNonTeamsProcessBeforeExtraction()
    {
        var context = new UiAutomationCaptureContext(
            RectEmpty: false,
            Width: 320,
            Height: 120,
            TopLevelWindowName: "Microsoft Teams notification");

        var result = TeamsUiAutomationCaptureGate.ShouldExtractText(
            context,
            processName: "explorer",
            applyBroadRectGate: true,
            out var reason);

        Assert.False(result);
        Assert.Equal("not_teams_process", reason);
    }

    [Fact]
    public void CaptureGateRejectsWebViewWithoutTeamsWindow()
    {
        var context = new UiAutomationCaptureContext(
            RectEmpty: false,
            Width: 320,
            Height: 120,
            TopLevelWindowName: "Outlook");

        var result = TeamsUiAutomationCaptureGate.ShouldExtractText(
            context,
            processName: "msedgewebview2",
            applyBroadRectGate: false,
            out var reason);

        Assert.False(result);
        Assert.Equal("not_teams_window", reason);
    }

    [Fact]
    public void TextExtractorStopsAtPartLimitAndDeduplicates()
    {
        var root = new FakeUiAutomationNode(
            "Microsoft Teams",
            "ControlType.Pane",
            Enumerable.Range(1, 20)
                .Select(index => new FakeUiAutomationNode(
                    index % 2 == 0 ? $"Person {index / 2}" : $"Person {index}",
                    "ControlType.Text"))
                .Cast<IUiAutomationNode>()
                .ToArray());

        var text = UiAutomationTextExtractor.ExtractText(root);
        var parts = text.Split(" | ", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        Assert.Equal(UiAutomationTextExtractor.MaxParts, parts.Length);
        Assert.Equal("Microsoft Teams", parts[0]);
        Assert.Equal(parts.Distinct(StringComparer.OrdinalIgnoreCase).Count(), parts.Length);
    }

    [Fact]
    public void TextExtractorReachesTeamsToastBodyTextThroughNestedGroups()
    {
        var bodyText = new FakeUiAutomationNode("This is a test notification", "ControlType.Text");
        var preview = new FakeUiAutomationNode("Message preview.", "ControlType.Text");
        var bodyGroup = new FakeUiAutomationNode("body", "ControlType.Group", [bodyText, preview]);
        var headerGroup = new FakeUiAutomationNode("header", "ControlType.Group", [bodyGroup]);
        var webDocument = new FakeUiAutomationNode("Microsoft Teams - Web content", "ControlType.Pane", [headerGroup]);
        var toastRoot = new FakeUiAutomationNode("Microsoft Teams", "ControlType.Pane", [webDocument]);

        var text = UiAutomationTextExtractor.ExtractText(toastRoot);

        Assert.Contains("This is a test notification", text, StringComparison.Ordinal);
    }

    [Fact]
    public void TextExtractorRespectsDepthAndVisitedElementLimits()
    {
        var deepBranch = CreateDeepBranch("Depth", UiAutomationTextExtractor.MaxDepth + 2);
        var wideChildren = Enumerable.Range(1, UiAutomationTextExtractor.MaxVisitedElements + 10)
            .Select(index => new FakeUiAutomationNode($"Wide {index}", "ControlType.Text"))
            .Cast<IUiAutomationNode>()
            .ToArray();
        var root = new FakeUiAutomationNode("Root", "ControlType.Pane", [deepBranch, .. wideChildren]);

        var text = UiAutomationTextExtractor.ExtractText(root);

        Assert.DoesNotContain($"Depth {UiAutomationTextExtractor.MaxDepth + 2}", text, StringComparison.Ordinal);
        Assert.DoesNotContain($"Wide {UiAutomationTextExtractor.MaxVisitedElements + 1}", text, StringComparison.Ordinal);
    }

    private static FakeUiAutomationNode CreateDeepBranch(string prefix, int levels)
    {
        FakeUiAutomationNode current = new($"{prefix} {levels}", "ControlType.Text");
        for (var level = levels - 1; level >= 1; level--)
        {
            current = new FakeUiAutomationNode($"{prefix} {level}", "ControlType.Text", [current]);
        }

        return current;
    }

    private sealed class FakeUiAutomationNode : IUiAutomationNode
    {
        private readonly IReadOnlyList<IUiAutomationNode> _children;

        public FakeUiAutomationNode(string name, string controlTypeProgrammaticName, IReadOnlyList<IUiAutomationNode>? children = null)
        {
            Name = name;
            ControlTypeProgrammaticName = controlTypeProgrammaticName;
            _children = children ?? [];
        }

        public string Name { get; }

        public string ControlTypeProgrammaticName { get; }

        public IEnumerable<IUiAutomationNode> EnumerateChildren()
        {
            return _children;
        }
    }
}

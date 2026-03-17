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
        var canReadDiagnostic = adapter.TryDequeueDiagnostic(out var diagnostic);

        adapter.Stop();

        Assert.False(canRead);
        Assert.Null(record);
        Assert.False(canReadDiagnostic);
        Assert.Null(diagnostic);
    }

    [Theory]
    [InlineData("both", true, true)]
    [InlineData("window_opened_only", true, false)]
    [InlineData("structure_changed_only", false, true)]
    public void SubscriptionModeSelectsExpectedHandlers(
        string mode,
        bool expectWindowOpened,
        bool expectStructureChanged)
    {
        Assert.Equal(expectWindowOpened, TeamsUiAutomationSubscriptionMode.IncludesWindowOpened(mode));
        Assert.Equal(expectStructureChanged, TeamsUiAutomationSubscriptionMode.IncludesStructureChanged(mode));
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
    public void TextExtractorRespectsDepthAndVisitedElementLimits()
    {
        var deepBranch = CreateDeepBranch("Depth", UiAutomationTextExtractor.MaxDepth + 2);
        var wideChildren = Enumerable.Range(1, UiAutomationTextExtractor.MaxVisitedElements + 10)
            .Select(index => new FakeUiAutomationNode($"Wide {index}", "ControlType.Text"))
            .Cast<IUiAutomationNode>()
            .ToArray();
        var root = new FakeUiAutomationNode("Root", "ControlType.Pane", [deepBranch, ..wideChildren]);

        var text = UiAutomationTextExtractor.ExtractText(root);

        Assert.DoesNotContain("Depth 5", text, StringComparison.Ordinal);
        Assert.DoesNotContain($"Wide {UiAutomationTextExtractor.MaxVisitedElements + 1}", text, StringComparison.Ordinal);
    }

    [Fact]
    public void RuntimeCountersTrackSeenAndRejectedBuckets()
    {
        var counters = new RelaySourceRuntimeCounters();

        counters.IncrementSeen("window_opened");
        counters.IncrementSeen("structure_changed");
        counters.IncrementTextExtractionAttempt();
        counters.IncrementTextExtractionFailure();
        counters.IncrementCandidatesEmitted(2);
        counters.IncrementRejection("broad_rect_gate");
        counters.IncrementRejection("process_not_found");
        counters.IncrementRejection("not_teams_process");
        counters.IncrementRejection("not_teams_window");
        counters.IncrementRejection("classifier_rejected");
        counters.IncrementRejection("suppressed_content");
        counters.IncrementRejection("not_message_like");

        var snapshot = counters.CreateSnapshot(queueDepth: 3, diagnosticQueueDepth: 4, pendingCandidateCount: 5, droppedCount: 6);

        Assert.Equal(3, snapshot.QueueDepth);
        Assert.Equal(4, snapshot.DiagnosticQueueDepth);
        Assert.Equal(5, snapshot.PendingCandidateCount);
        Assert.Equal(6, snapshot.DroppedCount);
        Assert.Equal(1, snapshot.WindowOpenedSeen);
        Assert.Equal(1, snapshot.StructureChangedSeen);
        Assert.Equal(1, snapshot.TextExtractionAttempts);
        Assert.Equal(1, snapshot.TextExtractionFailures);
        Assert.Equal(2, snapshot.CandidatesEmitted);
        Assert.Equal(1, snapshot.RejectedBroadRect);
        Assert.Equal(1, snapshot.RejectedProcessNotFound);
        Assert.Equal(1, snapshot.RejectedNotTeamsProcess);
        Assert.Equal(1, snapshot.RejectedNotTeamsWindow);
        Assert.Equal(1, snapshot.RejectedClassifier);
        Assert.Equal(1, snapshot.RejectedSuppressedContent);
        Assert.Equal(1, snapshot.RejectedNotMessageLike);
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
        private readonly IReadOnlyList<IUiAutomationNode> children;

        public FakeUiAutomationNode(string name, string controlTypeProgrammaticName, IReadOnlyList<IUiAutomationNode>? children = null)
        {
            Name = name;
            ControlTypeProgrammaticName = controlTypeProgrammaticName;
            this.children = children ?? [];
        }

        public string Name { get; }

        public string ControlTypeProgrammaticName { get; }

        public IEnumerable<IUiAutomationNode> EnumerateChildren()
        {
            return children;
        }
    }
}

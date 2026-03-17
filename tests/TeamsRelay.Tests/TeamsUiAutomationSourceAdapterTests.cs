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

        using var adapter = new TeamsUiAutomationSourceAdapter();
        adapter.Start();

        var canRead = adapter.TryDequeue(out var record);
        var canReadDiagnostic = adapter.TryDequeueDiagnostic(out var diagnostic);

        adapter.Stop();

        Assert.False(canRead);
        Assert.Null(record);
        Assert.False(canReadDiagnostic);
        Assert.Null(diagnostic);
    }
}

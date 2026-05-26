namespace TeamsRelay.Core;

public sealed class PartialTargetSendException : Exception
{
    public PartialTargetSendException(int successCount, IReadOnlyList<string> failures)
        : base(BuildMessage(successCount, failures))
    {
        SuccessCount = successCount;
        Failures = failures;
    }

    public int SuccessCount { get; }

    public IReadOnlyList<string> Failures { get; }

    private static string BuildMessage(int successCount, IReadOnlyList<string> failures)
    {
        var failureCount = failures.Count;
        return $"KDE delivery partially succeeded (success={successCount}, failed={failureCount}): {string.Join(" | ", failures)}";
    }
}

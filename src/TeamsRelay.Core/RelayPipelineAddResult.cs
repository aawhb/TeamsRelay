namespace TeamsRelay.Core;

public sealed record RelayPipelineAddResult
{
    private RelayPipelineAddResult(string status, string reason)
    {
        Status = status;
        Reason = reason;
    }

    public string Status { get; }

    public string Reason { get; }

    public static RelayPipelineAddResult Accepted() => new("accepted", "accepted");

    public static RelayPipelineAddResult Merged() => new("merged", "coalesced");

    public static RelayPipelineAddResult Rejected(string reason) => new("rejected", reason);
}

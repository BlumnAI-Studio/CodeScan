namespace CodeScan.Models;

public sealed class SourceDependency
{
    public required string FromKind { get; init; }
    public required string FromName { get; init; }
    public required string EdgeKind { get; init; }
    public required string ToKind { get; init; }
    public required string ToName { get; init; }
    public required string Strategy { get; init; }
    public string Detail { get; init; } = "";
    public int Line { get; init; }

    public string StableKey =>
        $"{FromKind}:{FromName}:{EdgeKind}:{ToKind}:{ToName}:{Line}".ToLowerInvariant();
}

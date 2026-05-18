using System.Text.Json;
using CodeScan.Models;

namespace CodeScan.Services.Semantic;

/// <summary>
/// Parses NDJSON emitted by a semantic-docker image into <see cref="SourceDependency"/>
/// records. NDJSON spec (see harness/knowledge/semantic-analyzer-docker.md):
/// <code>
/// {"kind":"edge","from":{"type":"class","name":"X"},"to":{"type":"type","name":"Y"},"rel":"inherits","line":3}
/// {"kind":"edge","from":{"type":"file","name":"Main.cs"},"to":{"type":"module","name":"NS"},"rel":"imports","line":1}
/// </code>
/// Node lines are tolerated and ignored — the host already discovers classes from
/// regex extraction; semantic adds <c>edge</c> records that regex couldn't reach.
/// Uses <see cref="JsonDocument"/> (AOT-safe — no reflection-based deserialization).
/// </summary>
public static class SemanticResultMerger
{
    public static List<SourceDependency> Parse(string ndjson, string strategy = "semantic")
    {
        var list = new List<SourceDependency>();
        if (string.IsNullOrWhiteSpace(ndjson)) return list;

        foreach (var raw in ndjson.Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0) continue;

            SourceDependency? dep;
            try
            {
                dep = ParseLine(line, strategy);
            }
            catch (JsonException)
            {
                continue;
            }

            if (dep is not null) list.Add(dep);
        }

        return list;
    }

    private static SourceDependency? ParseLine(string line, string strategy)
    {
        using var doc = JsonDocument.Parse(line);
        var root = doc.RootElement;

        if (!root.TryGetProperty("kind", out var kind)) return null;
        if (kind.GetString() != "edge") return null;

        if (!root.TryGetProperty("from", out var fromEl) ||
            !root.TryGetProperty("to", out var toEl) ||
            !root.TryGetProperty("rel", out var relEl))
            return null;

        var fromKind = TryGetString(fromEl, "type");
        var fromName = TryGetString(fromEl, "name");
        var toKind = TryGetString(toEl, "type");
        var toName = TryGetString(toEl, "name");
        var rel = relEl.GetString();

        if (string.IsNullOrWhiteSpace(fromKind) || string.IsNullOrWhiteSpace(fromName) ||
            string.IsNullOrWhiteSpace(toKind) || string.IsNullOrWhiteSpace(toName) ||
            string.IsNullOrWhiteSpace(rel))
            return null;

        var detail = TryGetString(root, "detail");
        var lineNumber = root.TryGetProperty("line", out var lineEl) && lineEl.TryGetInt32(out var n) ? n : 0;

        return new SourceDependency
        {
            FromKind = fromKind!,
            FromName = fromName!,
            EdgeKind = rel!,
            ToKind = toKind!,
            ToName = toName!,
            Strategy = strategy,
            Detail = string.IsNullOrWhiteSpace(detail) ? "semantic" : detail!,
            Line = lineNumber
        };
    }

    private static string? TryGetString(JsonElement el, string property) =>
        el.ValueKind == JsonValueKind.Object && el.TryGetProperty(property, out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString()
            : null;

    /// <summary>
    /// Merges semantic dependencies into a regex-derived list. Semantic edges are
    /// authoritative when both strategies report the same (from, edge, to) triple;
    /// regex edges that semantic missed are preserved as fallback signal.
    /// </summary>
    public static List<SourceDependency> Merge(List<SourceDependency> regex, List<SourceDependency> semantic)
    {
        var keyed = new Dictionary<string, SourceDependency>(StringComparer.OrdinalIgnoreCase);

        foreach (var d in regex)
            keyed[$"{d.FromKind}:{d.FromName}:{d.EdgeKind}:{d.ToKind}:{d.ToName}"] = d;

        foreach (var d in semantic)
            keyed[$"{d.FromKind}:{d.FromName}:{d.EdgeKind}:{d.ToKind}:{d.ToName}"] = d;

        return keyed.Values.ToList();
    }
}

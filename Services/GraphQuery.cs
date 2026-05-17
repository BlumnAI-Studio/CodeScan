using System.Text.RegularExpressions;

namespace CodeScan.Services;

public sealed class GraphQuerySpec
{
    public string LeftAlias { get; init; } = "n";
    public string? LeftKind { get; init; }
    public bool HasEdge { get; init; }
    public string EdgeAlias { get; init; } = "r";
    public string? EdgeKind { get; init; }
    public string RightAlias { get; init; } = "m";
    public string? RightKind { get; init; }
    public List<GraphQueryCondition> Conditions { get; init; } = [];
    public int? Limit { get; init; }
}

public sealed class GraphQueryCondition
{
    public required string Alias { get; init; }
    public required string Field { get; init; }
    public required GraphQueryOperator Operator { get; init; }
    public required string Value { get; init; }
}

public enum GraphQueryOperator
{
    Equals,
    Contains,
    StartsWith,
    EndsWith
}

public sealed class GraphQueryParseException : Exception
{
    public GraphQueryParseException(string message) : base(message) { }
}

public static partial class GraphQueryParser
{
    private static readonly HashSet<string> NodeFields = new(StringComparer.OrdinalIgnoreCase)
    {
        "kind", "label", "path", "detail"
    };

    private static readonly HashSet<string> EdgeFields = new(StringComparer.OrdinalIgnoreCase)
    {
        "kind", "label"
    };

    public static bool LooksLikeQuery(string query)
        => query.TrimStart().StartsWith("MATCH ", StringComparison.OrdinalIgnoreCase);

    public static GraphQuerySpec Parse(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
            throw new GraphQueryParseException("Graph query is empty.");

        var match = MatchPattern().Match(query);
        if (!match.Success)
        {
            throw new GraphQueryParseException(
                "Unsupported graph query. Use: MATCH (n:kind) or MATCH (n:kind)-[r:edge]->(m:kind) WHERE n.label CONTAINS 'text' LIMIT 50");
        }

        var leftAlias = ValueOr(match, "leftAlias", "n");
        var edgeAlias = ValueOr(match, "edgeAlias", "r");
        var rightAlias = ValueOr(match, "rightAlias", "m");
        var hasEdge = match.Groups["edge"].Success;
        var tail = match.Groups["tail"].Value;
        var limit = ParseLimit(tail);
        var conditions = ParseConditions(tail);

        ValidateConditions(conditions, leftAlias, hasEdge ? edgeAlias : null, hasEdge ? rightAlias : null);

        return new GraphQuerySpec
        {
            LeftAlias = leftAlias,
            LeftKind = EmptyToNull(match.Groups["leftKind"].Value),
            HasEdge = hasEdge,
            EdgeAlias = edgeAlias,
            EdgeKind = EmptyToNull(match.Groups["edgeKind"].Value),
            RightAlias = rightAlias,
            RightKind = EmptyToNull(match.Groups["rightKind"].Value),
            Conditions = conditions,
            Limit = limit
        };
    }

    private static List<GraphQueryCondition> ParseConditions(string tail)
    {
        var where = ExtractClause(tail, "WHERE", ["RETURN", "LIMIT"]);
        if (string.IsNullOrWhiteSpace(where)) return [];

        var list = new List<GraphQueryCondition>();
        var parts = Regex.Split(where, @"\s+AND\s+", RegexOptions.IgnoreCase);
        foreach (var rawPart in parts)
        {
            var part = rawPart.Trim();
            if (part.Length == 0) continue;

            var match = ConditionPattern().Match(part);
            if (!match.Success)
            {
                throw new GraphQueryParseException(
                    $"Unsupported WHERE condition: {part}. Supported operators: =, CONTAINS, STARTS WITH, ENDS WITH.");
            }

            list.Add(new GraphQueryCondition
            {
                Alias = match.Groups["alias"].Value,
                Field = match.Groups["field"].Value,
                Operator = ParseOperator(match.Groups["op"].Value),
                Value = Unquote(match.Groups["value"].Value)
            });
        }
        return list;
    }

    private static void ValidateConditions(List<GraphQueryCondition> conditions, string leftAlias, string? edgeAlias, string? rightAlias)
    {
        foreach (var condition in conditions)
        {
            var isLeft = condition.Alias.Equals(leftAlias, StringComparison.OrdinalIgnoreCase);
            var isRight = rightAlias != null && condition.Alias.Equals(rightAlias, StringComparison.OrdinalIgnoreCase);
            var isEdge = edgeAlias != null && condition.Alias.Equals(edgeAlias, StringComparison.OrdinalIgnoreCase);

            if (!isLeft && !isRight && !isEdge)
                throw new GraphQueryParseException($"Unknown alias in WHERE: {condition.Alias}.");

            if ((isLeft || isRight) && !NodeFields.Contains(condition.Field))
                throw new GraphQueryParseException($"Unsupported node field: {condition.Field}. Use kind, label, path, or detail.");

            if (isEdge && !EdgeFields.Contains(condition.Field))
                throw new GraphQueryParseException($"Unsupported edge field: {condition.Field}. Use kind or label.");
        }
    }

    private static int? ParseLimit(string tail)
    {
        var match = LimitPattern().Match(tail);
        if (!match.Success) return null;
        return int.TryParse(match.Groups["limit"].Value, out var limit) ? limit : null;
    }

    private static string ExtractClause(string text, string startKeyword, string[] endKeywords)
    {
        var start = IndexOfKeyword(text, startKeyword);
        if (start < 0) return "";

        start += startKeyword.Length;
        var end = text.Length;
        foreach (var keyword in endKeywords)
        {
            var idx = IndexOfKeyword(text[start..], keyword);
            if (idx >= 0)
                end = Math.Min(end, start + idx);
        }
        return text[start..end].Trim();
    }

    private static int IndexOfKeyword(string text, string keyword)
        => Regex.Match(text, $@"\b{Regex.Escape(keyword)}\b", RegexOptions.IgnoreCase).Success
            ? Regex.Match(text, $@"\b{Regex.Escape(keyword)}\b", RegexOptions.IgnoreCase).Index
            : -1;

    private static GraphQueryOperator ParseOperator(string value)
    {
        var normalized = Regex.Replace(value.Trim(), @"\s+", " ").ToUpperInvariant();
        return normalized switch
        {
            "=" => GraphQueryOperator.Equals,
            "CONTAINS" => GraphQueryOperator.Contains,
            "STARTS WITH" => GraphQueryOperator.StartsWith,
            "ENDS WITH" => GraphQueryOperator.EndsWith,
            _ => throw new GraphQueryParseException($"Unsupported operator: {value}")
        };
    }

    private static string ValueOr(Match match, string groupName, string fallback)
        => string.IsNullOrWhiteSpace(match.Groups[groupName].Value) ? fallback : match.Groups[groupName].Value;

    private static string? EmptyToNull(string value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string Unquote(string value)
    {
        value = value.Trim();
        if (value.Length >= 2 && ((value[0] == '"' && value[^1] == '"') || (value[0] == '\'' && value[^1] == '\'')))
            return value[1..^1];
        return value;
    }

    [GeneratedRegex(@"^\s*MATCH\s*\(\s*(?<leftAlias>[A-Za-z_]\w*)?\s*(?::\s*(?<leftKind>[\w-]+))?\s*\)\s*(?<edge>-\s*\[\s*(?<edgeAlias>[A-Za-z_]\w*)?\s*(?::\s*(?<edgeKind>[\w-]+))?\s*\]\s*(?:->|--)\s*\(\s*(?<rightAlias>[A-Za-z_]\w*)?\s*(?::\s*(?<rightKind>[\w-]+))?\s*\))?\s*(?<tail>.*)$", RegexOptions.IgnoreCase)]
    private static partial Regex MatchPattern();

    [GeneratedRegex(@"\bLIMIT\s+(?<limit>\d+)\b", RegexOptions.IgnoreCase)]
    private static partial Regex LimitPattern();

    [GeneratedRegex(@"^(?<alias>[A-Za-z_]\w*)\.(?<field>[A-Za-z_]\w*)\s*(?<op>CONTAINS|STARTS\s+WITH|ENDS\s+WITH|=)\s*(?<value>""[^""]*""|'[^']*'|[^\s]+)$", RegexOptions.IgnoreCase)]
    private static partial Regex ConditionPattern();
}

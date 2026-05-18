using System.Text;
using System.Text.RegularExpressions;
using CodeScan.Models;
using static CodeScan.Services.Analyzers.AnalyzerHelpers;

namespace CodeScan.Services.Analyzers.Languages;

internal sealed partial class KotlinAnalyzer : ILanguageAnalyzer
{
    public string Language => "kotlin";

    public IReadOnlySet<string> SupportedExtensions { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        ".kt", ".kts"
    };

    public List<MethodEntry> ExtractMethods(string[] lines)
    {
        var methods = new List<MethodEntry>();

        WalkBraceLanguage(
            lines,
            classPattern: ClassDecl(),
            isCommentLine: line => line.StartsWith("//") || line.StartsWith("*"),
            onClassEnter: (_, _, _) => { },
            onLine: (currentClass, lineNumber, line) =>
            {
                var mm = MethodDecl().Match(line);
                if (!mm.Success || ControlFlow().IsMatch(line)) return;

                methods.Add(new MethodEntry
                {
                    ClassName = currentClass,
                    MethodName = mm.Groups[1].Value,
                    StartLine = lineNumber,
                    EndLine = FindBraceEnd(lines, lineNumber - 1)
                });
            });

        return methods;
    }

    public List<SourceDependency> ExtractDependencies(FileEntry file, string[] lines)
    {
        var list = new List<SourceDependency>();

        // Multi-line class header pre-pass.
        // Kotlin headers commonly span several lines when a primary constructor
        // is broken up, e.g.
        //     class X private constructor(
        //         arg: T
        //     ) : Base(args) {
        // We accumulate the lines until the opening brace, then extract inherits
        // from the combined header. Every line that participated in the header
        // is recorded so the regular walk skips `creates`/`uses_type` on them
        // (otherwise the super-call `Base(args)` would be a false `creates`).
        var headerLines = new HashSet<int>();

        for (var i = 0; i < lines.Length; i++)
        {
            var trimmed = lines[i].Trim();
            if (trimmed.Length == 0 || trimmed.StartsWith("//")) continue;
            if (!ClassDecl().IsMatch(trimmed)) continue;

            var combined = new StringBuilder();
            var end = i;
            for (var j = i; j < lines.Length; j++)
            {
                combined.Append(' ').Append(lines[j]);
                headerLines.Add(j);
                end = j;
                if (lines[j].Contains('{')) break;
            }

            var header = combined.ToString();
            var m = ClassWithBase().Match(header);
            if (m.Success && m.Groups[1].Success && m.Groups[2].Success)
            {
                var className = m.Groups[1].Value;
                foreach (var target in SplitTypeList(m.Groups[2].Value))
                    list.Add(Dep(Language, "class", className, "inherits_or_implements", "type", target, i + 1, "class declaration"));
            }

            i = end;
        }

        WalkBraceLanguage(
            lines,
            classPattern: ClassDecl(),
            isCommentLine: line => line.StartsWith("//"),
            onClassEnter: (_, _, _) => { },
            onLine: (currentClass, lineNumber, line) =>
            {
                if (ImportPattern().Match(line) is { Success: true } im)
                    list.Add(Dep(Language, "file", file.Name, "imports", "module", im.Groups[1].Value, lineNumber, "import"));

                // Skip every line that was part of a (possibly multi-line) class
                // header — inherits already emitted in the pre-pass, and the
                // super-call `Base(args)` must not be re-interpreted as creates.
                if (headerLines.Contains(lineNumber - 1)) return;

                // Skip function declarations — fun foo(x: Bar) is not a `creates Bar`.
                if (FunctionDecl().IsMatch(line)) return;

                // Kotlin has no `new` keyword: any `Type(...)` call is a construction.
                foreach (Match c in CreateCall().Matches(line))
                {
                    var target = NormalizeTypeName(c.Groups[1].Value);
                    if (!string.Equals(target, currentClass, StringComparison.Ordinal))
                        list.Add(Dep(Language, "class", currentClass, "creates", "type", target, lineNumber, "object creation"));
                }

                foreach (Match t in TypeUse().Matches(line))
                {
                    var typeName = NormalizeTypeName(t.Groups[1].Value);
                    if (IsLikelyType(typeName) && !string.Equals(typeName, currentClass, StringComparison.Ordinal))
                        list.Add(Dep(Language, "class", currentClass, "uses_type", "type", typeName, lineNumber, "type reference"));
                }
            });

        return list;
    }

    // Class-like declaration with optional Kotlin modifiers (abstract, open,
    // inner, final, enum, data, sealed, value, inline, visibility). The
    // modifiers may appear in any order before `class`/`interface`/`object`.
    [GeneratedRegex(@"(?:(?:abstract|open|inner|final|enum|data|sealed|value|inline|private|protected|internal|public)\s+)*(?:class|interface|object)\s+(\w+)", RegexOptions.Compiled)]
    private static partial Regex ClassDecl();

    [GeneratedRegex(@"(?:fun|suspend\s+fun|override\s+fun|private\s+fun|internal\s+fun|protected\s+fun)\s+(?:<[^>]*>\s+)?(\w+)\s*\(", RegexOptions.Compiled)]
    private static partial Regex MethodDecl();

    [GeneratedRegex(@"^\s*(?:if|else|for|while|when|catch|finally|do|try)\s*[\(\{]?", RegexOptions.Compiled)]
    private static partial Regex ControlFlow();

    // Same modifier prefix as ClassDecl, plus an optional visibility-qualified
    // constructor (`private constructor`, `protected constructor`, etc.) and
    // an optional primary-constructor parameter list, before the `:` that
    // introduces the supertype list.
    [GeneratedRegex(@"(?:(?:abstract|open|inner|final|enum|data|sealed|value|inline|private|protected|internal|public)\s+)*(?:class|interface|object)\s+(\w+)(?:\s+(?:private|protected|internal|public)\s+constructor)?(?:\s*\([^)]*\))?(?:\s*:\s*([^{]+))?", RegexOptions.Compiled)]
    private static partial Regex ClassWithBase();

    [GeneratedRegex(@"^\s*(?:fun|suspend\s+fun|override\s+fun|private\s+fun|internal\s+fun|protected\s+fun)\b", RegexOptions.Compiled)]
    private static partial Regex FunctionDecl();

    [GeneratedRegex(@"^\s*import\s+([\w\.\*]+)", RegexOptions.Compiled)]
    private static partial Regex ImportPattern();

    [GeneratedRegex(@"\b([A-Z]\w*)\s*\(", RegexOptions.Compiled)]
    private static partial Regex CreateCall();

    [GeneratedRegex(@"\b\w+\s*:\s*([A-Z]\w*)", RegexOptions.Compiled)]
    private static partial Regex TypeUse();
}

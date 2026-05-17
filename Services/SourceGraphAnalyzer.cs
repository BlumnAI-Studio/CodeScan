using System.Text.RegularExpressions;
using CodeScan.Models;

namespace CodeScan.Services;

public sealed class HybridSourceGraphAnalyzer
{
    private readonly List<ISourceDependencyStrategy> _strategies;

    public HybridSourceGraphAnalyzer()
    {
        _strategies =
        [
            new SemanticProbeStrategy(),
            new RegexSourceDependencyStrategy()
        ];
    }

    public void Enrich(string rootPath, IEnumerable<FileEntry> sourceFiles)
    {
        var context = new SourceDependencyContext(rootPath, SemanticCapabilityDetector.Detect(rootPath));

        foreach (var file in sourceFiles)
        {
            var strategy = _strategies.FirstOrDefault(s => s.CanAnalyze(file, context));
            if (strategy == null) continue;

            var dependencies = strategy.Analyze(file, context)
                .Where(d => !string.IsNullOrWhiteSpace(d.ToName))
                .GroupBy(d => d.StableKey)
                .Select(g => g.First())
                .ToList();

            file.Dependencies = dependencies;
        }
    }
}

public sealed class SourceDependencyContext
{
    public SourceDependencyContext(string rootPath, IReadOnlyDictionary<string, SemanticCapability> semanticCapabilities)
    {
        RootPath = rootPath;
        SemanticCapabilities = semanticCapabilities;
    }

    public string RootPath { get; }
    public IReadOnlyDictionary<string, SemanticCapability> SemanticCapabilities { get; }
}

public sealed class SemanticCapability
{
    public required string Language { get; init; }
    public required bool ProjectModelFound { get; init; }
    public required string StrategyName { get; init; }
    public string Reason { get; init; } = "";
}

public interface ISourceDependencyStrategy
{
    string Name { get; }
    bool CanAnalyze(FileEntry file, SourceDependencyContext context);
    List<SourceDependency> Analyze(FileEntry file, SourceDependencyContext context);
}

public static class SemanticCapabilityDetector
{
    public static IReadOnlyDictionary<string, SemanticCapability> Detect(string rootPath)
    {
        var map = new Dictionary<string, SemanticCapability>(StringComparer.OrdinalIgnoreCase)
        {
            ["csharp"] = Capability("csharp", "Roslyn", HasAny(rootPath, "*.sln", "*.csproj"),
                "Requires a solution/project model for exact symbols."),
            ["java"] = Capability("java", "JDT/Spoon", HasAny(rootPath, "pom.xml", "build.gradle", "build.gradle.kts"),
                "Requires Maven/Gradle or a JDT project model."),
            ["typescript"] = Capability("typescript", "TypeScript Compiler API", HasAny(rootPath, "tsconfig.json", "jsconfig.json"),
                "Requires tsconfig/jsconfig for type checker context."),
            ["go"] = Capability("go", "go/packages", HasAny(rootPath, "go.mod", "go.work"),
                "Requires Go module/workspace metadata."),
            ["rust"] = Capability("rust", "rust-analyzer", HasAny(rootPath, "Cargo.toml"),
                "Requires Cargo metadata."),
            ["cpp"] = Capability("cpp", "Clang LibTooling", HasAny(rootPath, "compile_commands.json"),
                "Requires compile_commands.json for include/define flags.")
        };

        return map;
    }

    private static SemanticCapability Capability(string language, string strategy, bool found, string reason) => new()
    {
        Language = language,
        StrategyName = strategy,
        ProjectModelFound = found,
        Reason = reason
    };

    private static bool HasAny(string rootPath, params string[] patterns)
    {
        foreach (var pattern in patterns)
        {
            try
            {
                if (Directory.EnumerateFiles(rootPath, pattern, SearchOption.AllDirectories)
                    .Any(p => !IsUnderIgnoredDirectory(rootPath, p)))
                    return true;
            }
            catch { }
        }
        return false;
    }

    private static bool IsUnderIgnoredDirectory(string rootPath, string path)
    {
        var relative = Path.GetRelativePath(rootPath, path).Replace('\\', '/');
        return relative.Split('/').Any(part => part is ".git" or "bin" or "obj" or "node_modules" or "dist" or "build");
    }
}

/// <summary>
/// Placeholder selector for future compiler-backed analyzers. It detects whether
/// semantic analysis is possible, then deliberately yields to regex until a
/// language-specific implementation is installed.
/// </summary>
internal sealed class SemanticProbeStrategy : ISourceDependencyStrategy
{
    public string Name => "semantic-probe";

    public bool CanAnalyze(FileEntry file, SourceDependencyContext context)
    {
        var language = GetLanguage(file.Extension);
        if (language == null) return false;
        if (!context.SemanticCapabilities.TryGetValue(language, out var capability)) return false;
        if (!capability.ProjectModelFound) return false;

        // Semantic implementations plug in here later. Until then regex remains
        // the active fallback even when project metadata is present.
        return false;
    }

    public List<SourceDependency> Analyze(FileEntry file, SourceDependencyContext context) => [];

    private static string? GetLanguage(string extension) => extension switch
    {
        ".cs" => "csharp",
        ".java" => "java",
        ".ts" or ".tsx" or ".js" or ".jsx" => "typescript",
        ".go" => "go",
        ".rs" => "rust",
        ".c" or ".cc" or ".cpp" or ".cxx" or ".h" or ".hpp" => "cpp",
        _ => null
    };
}

internal sealed partial class RegexSourceDependencyStrategy : ISourceDependencyStrategy
{
    public string Name => "regex";

    public bool CanAnalyze(FileEntry file, SourceDependencyContext context)
        => SourceAnalyzer.IsSourceFile(file.Extension);

    public List<SourceDependency> Analyze(FileEntry file, SourceDependencyContext context)
    {
        string[] lines;
        try { lines = File.ReadAllLines(file.FullPath); }
        catch { return []; }

        return file.Extension switch
        {
            ".cs" => AnalyzeBraceLanguage(file, lines, CsClassWithBase(), CsImport(), NewObject(), CsTypeUse()),
            ".java" => AnalyzeBraceLanguage(file, lines, JavaClassWithBase(), JavaImport(), NewObject(), JavaTypeUse()),
            ".kt" or ".kts" => AnalyzeBraceLanguage(file, lines, KtClassWithBase(), KtImport(), NewObject(), KtTypeUse()),
            ".ts" or ".tsx" or ".js" or ".jsx" => AnalyzeBraceLanguage(file, lines, TsClassWithBase(), TsImport(), NewObject(), TsTypeUse()),
            ".php" => AnalyzeBraceLanguage(file, lines, PhpClassWithBase(), PhpImport(), NewObject(), PhpTypeUse()),
            ".py" => AnalyzePython(file, lines),
            ".go" => AnalyzeBraceLanguage(file, lines, GoTypeDecl(), GoImport(), GoNewObject(), GoTypeUse()),
            ".rs" => AnalyzeBraceLanguage(file, lines, RustTypeDecl(), RustImport(), RustNewObject(), RustTypeUse()),
            ".c" or ".cc" or ".cpp" or ".cxx" or ".h" or ".hpp" or ".hh" or ".hxx"
                => AnalyzeBraceLanguage(file, lines, CppClassWithBase(), CppImport(), NewObject(), CppTypeUse()),
            _ => []
        };
    }

    private List<SourceDependency> AnalyzeBraceLanguage(
        FileEntry file,
        string[] lines,
        Regex classPattern,
        Regex importPattern,
        Regex creationPattern,
        Regex typeUsePattern)
    {
        var list = new List<SourceDependency>();
        var currentClass = "Global";

        for (int i = 0; i < lines.Length; i++)
        {
            var line = StripLineComment(lines[i]).Trim();
            if (line.Length == 0) continue;

            var import = importPattern.Match(line);
            if (import.Success)
            {
                list.Add(Dep("file", file.Name, "imports", "module", FirstSuccessfulGroup(import, 1, 2), i + 1, "import"));
            }

            var cls = classPattern.Match(line);
            if (cls.Success)
            {
                currentClass = cls.Groups[1].Value;
                var baseList = cls.Groups.Count > 2 ? cls.Groups[2].Value : "";
                foreach (var target in SplitTypeList(baseList))
                    list.Add(Dep("class", currentClass, "inherits_or_implements", "type", target, i + 1, "class declaration"));
            }

            foreach (Match created in creationPattern.Matches(line))
                list.Add(Dep("class", currentClass, "creates", "type", FirstSuccessfulGroup(created, 1, 2), i + 1, "object creation"));

            foreach (Match typeUse in typeUsePattern.Matches(line))
            {
                var typeName = NormalizeTypeName(FirstSuccessfulGroup(typeUse, 1, 2));
                if (IsLikelyType(typeName) && !string.Equals(typeName, currentClass, StringComparison.Ordinal))
                    list.Add(Dep("class", currentClass, "uses_type", "type", typeName, i + 1, "type reference"));
            }
        }

        return list;
    }

    private List<SourceDependency> AnalyzePython(FileEntry file, string[] lines)
    {
        var list = new List<SourceDependency>();
        var currentClass = "Global";

        for (int i = 0; i < lines.Length; i++)
        {
            var line = StripPythonComment(lines[i]).Trim();
            if (line.Length == 0) continue;

            var import = PyImport().Match(line);
            if (import.Success)
            {
                var module = import.Groups[1].Success ? import.Groups[1].Value : import.Groups[2].Value;
                list.Add(Dep("file", file.Name, "imports", "module", module, i + 1, "import"));
            }

            var cls = PyClassWithBase().Match(line);
            if (cls.Success)
            {
                currentClass = cls.Groups[1].Value;
                foreach (var target in SplitTypeList(cls.Groups[2].Value))
                    list.Add(Dep("class", currentClass, "inherits_or_implements", "type", target, i + 1, "class declaration"));
                continue;
            }

            foreach (Match created in PyCallLikeType().Matches(line))
            {
                var target = created.Groups[1].Value;
                if (!string.Equals(target, currentClass, StringComparison.Ordinal))
                    list.Add(Dep("class", currentClass, "creates", "type", target, i + 1, "constructor-like call"));
            }
        }

        return list;
    }

    private SourceDependency Dep(string fromKind, string fromName, string edgeKind, string toKind, string toName, int line, string detail) => new()
    {
        FromKind = fromKind,
        FromName = fromName,
        EdgeKind = edgeKind,
        ToKind = toKind,
        ToName = toKind == "module" ? toName.Trim() : NormalizeTypeName(toName),
        Strategy = Name,
        Detail = detail,
        Line = line
    };

    private static IEnumerable<string> SplitTypeList(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) yield break;

        foreach (var part in value.Split([',', ':'], StringSplitOptions.RemoveEmptyEntries))
        {
            var normalized = NormalizeTypeName(part);
            if (IsLikelyType(normalized))
                yield return normalized;
        }
    }

    private static string NormalizeTypeName(string value)
    {
        var text = value.Trim();
        var generic = text.IndexOfAny(['<', '(', '?', '[']);
        if (generic >= 0) text = text[..generic];
        text = text.Trim().TrimStart('\\');
        return text.Split('.').Last().Split(':').Last().Trim();
    }

    private static bool IsLikelyType(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return false;
        if (value is "string" or "int" or "long" or "bool" or "double" or "float" or "decimal" or "void" or "var" or "object")
            return false;
        return char.IsUpper(value[0]) || value.StartsWith('I') && value.Length > 1 && char.IsUpper(value[1]);
    }

    private static string FirstSuccessfulGroup(Match match, params int[] groupIndexes)
    {
        foreach (var index in groupIndexes)
        {
            if (index < match.Groups.Count && match.Groups[index].Success)
                return match.Groups[index].Value;
        }
        return "";
    }

    private static string StripLineComment(string line)
    {
        var idx = line.IndexOf("//", StringComparison.Ordinal);
        return idx >= 0 ? line[..idx] : line;
    }

    private static string StripPythonComment(string line)
    {
        var idx = line.IndexOf('#');
        return idx >= 0 ? line[..idx] : line;
    }

    [GeneratedRegex(@"(?:class|struct|record|interface)\s+(\w+)(?:\s*:\s*([^{]+))?", RegexOptions.Compiled)]
    private static partial Regex CsClassWithBase();

    [GeneratedRegex(@"^\s*using\s+([\w\.]+)\s*;", RegexOptions.Compiled)]
    private static partial Regex CsImport();

    [GeneratedRegex(@"\bnew\s+([A-Z]\w*)\s*(?:<|\()", RegexOptions.Compiled)]
    private static partial Regex NewObject();

    [GeneratedRegex(@"\b([A-Z]\w*(?:<[^>]+>)?)\s+\w+\s*(?:[;=,\)])", RegexOptions.Compiled)]
    private static partial Regex CsTypeUse();

    [GeneratedRegex(@"(?:class|interface|enum)\s+(\w+)(?:\s+(?:extends|implements)\s+([^{]+))?", RegexOptions.Compiled)]
    private static partial Regex JavaClassWithBase();

    [GeneratedRegex(@"^\s*import\s+([\w\.\*]+)\s*;", RegexOptions.Compiled)]
    private static partial Regex JavaImport();

    [GeneratedRegex(@"\b([A-Z]\w*(?:<[^>]+>)?)\s+\w+\s*(?:[;=,\)])", RegexOptions.Compiled)]
    private static partial Regex JavaTypeUse();

    [GeneratedRegex(@"(?:class|interface|object|enum\s+class|data\s+class|sealed\s+class)\s+(\w+)(?:[^{:]*:\s*([^{]+))?", RegexOptions.Compiled)]
    private static partial Regex KtClassWithBase();

    [GeneratedRegex(@"^\s*import\s+([\w\.\*]+)", RegexOptions.Compiled)]
    private static partial Regex KtImport();

    [GeneratedRegex(@"\b\w+\s*:\s*([A-Z]\w*)", RegexOptions.Compiled)]
    private static partial Regex KtTypeUseRaw();

    private static Regex KtTypeUse() => KtTypeUseRaw();

    [GeneratedRegex(@"class\s+(\w+)(?:\s+(?:extends|implements)\s+([^{]+))?", RegexOptions.Compiled)]
    private static partial Regex TsClassWithBase();

    [GeneratedRegex(@"^\s*import\s+.*?\s+from\s+['""]([^'""]+)['""]|^\s*import\s+['""]([^'""]+)['""]", RegexOptions.Compiled)]
    private static partial Regex TsImportRaw();

    private static Regex TsImport() => TsImportRaw();

    [GeneratedRegex(@"\b([A-Z]\w*)\s*[;=,\)]|\b:\s*([A-Z]\w*)", RegexOptions.Compiled)]
    private static partial Regex TsTypeUseRaw();

    private static Regex TsTypeUse() => TsTypeUseRaw();

    [GeneratedRegex(@"(?:class|interface|trait)\s+(\w+)(?:\s+(?:extends|implements)\s+([^{]+))?", RegexOptions.Compiled)]
    private static partial Regex PhpClassWithBase();

    [GeneratedRegex(@"^\s*use\s+([\w\\]+)\s*;", RegexOptions.Compiled)]
    private static partial Regex PhpImport();

    [GeneratedRegex(@"\b([A-Z]\w*)\s+\$\w+|\bfunction\s+\w+\s*\([^)]*([A-Z]\w*)\s+\$\w+", RegexOptions.Compiled)]
    private static partial Regex PhpTypeUse();

    [GeneratedRegex(@"^class\s+(\w+)(?:\(([^)]*)\))?", RegexOptions.Compiled)]
    private static partial Regex PyClassWithBase();

    [GeneratedRegex(@"^\s*(?:from\s+([\w\.]+)\s+import|import\s+([\w\.]+))", RegexOptions.Compiled)]
    private static partial Regex PyImport();

    [GeneratedRegex(@"\b([A-Z]\w*)\s*\(", RegexOptions.Compiled)]
    private static partial Regex PyCallLikeType();

    [GeneratedRegex(@"type\s+(\w+)\s+(?:struct|interface)", RegexOptions.Compiled)]
    private static partial Regex GoTypeDecl();

    [GeneratedRegex(@"^\s*import\s+(?:\(\s*)?""([^""]+)""", RegexOptions.Compiled)]
    private static partial Regex GoImport();

    [GeneratedRegex(@"\b(?:new|make)\s*\(\s*([A-Z]\w*)|\b([A-Z]\w*)\s*\{", RegexOptions.Compiled)]
    private static partial Regex GoNewObjectRaw();

    private static Regex GoNewObject() => GoNewObjectRaw();

    [GeneratedRegex(@"\b\w+\s+[\*\[\]]*([A-Z]\w*)", RegexOptions.Compiled)]
    private static partial Regex GoTypeUse();

    [GeneratedRegex(@"(?:struct|enum|trait)\s+(\w+)", RegexOptions.Compiled)]
    private static partial Regex RustTypeDecl();

    [GeneratedRegex(@"^\s*use\s+([\w:]+)", RegexOptions.Compiled)]
    private static partial Regex RustImport();

    [GeneratedRegex(@"\b([A-Z]\w*)::(?:new|default)\s*\(", RegexOptions.Compiled)]
    private static partial Regex RustNewObject();

    [GeneratedRegex(@"\b(?:let\s+\w+\s*:\s*|->\s*|Box<|Vec<)([A-Z]\w*)", RegexOptions.Compiled)]
    private static partial Regex RustTypeUse();

    [GeneratedRegex(@"(?:class|struct)\s+(\w+)(?:\s*:\s*(?:public|private|protected)?\s*([^{]+))?", RegexOptions.Compiled)]
    private static partial Regex CppClassWithBase();

    [GeneratedRegex(@"^\s*#include\s+[<""]([^>""]+)[>""]", RegexOptions.Compiled)]
    private static partial Regex CppImport();

    [GeneratedRegex(@"\b([A-Z]\w*(?:::\w+)?(?:<[^>]+>)?)\s+[\*&]?\w+\s*(?:[;=,\)])", RegexOptions.Compiled)]
    private static partial Regex CppTypeUse();
}

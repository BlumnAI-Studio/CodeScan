// Semantic analyzer for C# — runs inside codescan/semantic-csharp:latest.
// Contract (see harness/knowledge/semantic-analyzer-docker.md):
//   INPUT:  /work mounted read-only (project root containing *.sln or *.csproj)
//   OUTPUT: stdout NDJSON, one node/edge per line
//   EXIT:   0 on success, non-zero on failure (diagnostics on stderr)
//
// Special mode: `--self-check` prints a static NDJSON sample plus tool version
// info and exits 0. Used by `codescan semantic self-check csharp`.

using Microsoft.Build.Locator;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.MSBuild;

namespace CodeScan.Semantic.CSharp;

internal static class Program
{
    private const string ToolVersion = "roslyn-4.13";

    public static async Task<int> Main(string[] args)
    {
        if (args.Length > 0 && args[0] == "--self-check")
            return SelfCheck();

        try
        {
            MSBuildLocator.RegisterDefaults();
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"MSBuildLocator: {ex.Message}");
            return 1;
        }

        var workDir = args.Length > 0 ? args[0] : "/work";
        if (!Directory.Exists(workDir))
        {
            Console.Error.WriteLine($"Work directory not found: {workDir}");
            return 1;
        }

        var sln = Directory.EnumerateFiles(workDir, "*.sln", SearchOption.TopDirectoryOnly).FirstOrDefault();
        var csproj = Directory.EnumerateFiles(workDir, "*.csproj", SearchOption.AllDirectories).FirstOrDefault();

        if (sln is null && csproj is null)
        {
            Console.Error.WriteLine("No .sln or .csproj under /work — semantic analysis requires a project model.");
            return 2;
        }

        using var workspace = MSBuildWorkspace.Create();
        workspace.WorkspaceFailed += (_, e) =>
        {
            if (e.Diagnostic.Kind == WorkspaceDiagnosticKind.Failure)
                Console.Error.WriteLine($"workspace: {e.Diagnostic.Message}");
        };

        Solution solution;
        try
        {
            if (sln is not null)
                solution = await workspace.OpenSolutionAsync(sln);
            else
                solution = (await workspace.OpenProjectAsync(csproj!)).Solution;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Failed to open project model: {ex.Message}");
            return 3;
        }

        await EmitEdgesAsync(solution);
        return 0;
    }

    private static async Task EmitEdgesAsync(Solution solution)
    {
        foreach (var project in solution.Projects)
        {
            var compilation = await project.GetCompilationAsync();
            if (compilation is null) continue;

            foreach (var tree in compilation.SyntaxTrees)
            {
                var model = compilation.GetSemanticModel(tree);
                var root = await tree.GetRootAsync();
                var fileName = Path.GetFileName(tree.FilePath);

                foreach (var typeDecl in root.DescendantNodes().OfType<TypeDeclarationSyntax>())
                {
                    var typeSymbol = model.GetDeclaredSymbol(typeDecl);
                    if (typeSymbol is null) continue;

                    // inherits_or_implements — base type + interfaces, resolved by symbols.
                    if (typeSymbol.BaseType is { SpecialType: SpecialType.None } baseType)
                        EmitEdge("class", typeSymbol.Name, "type", baseType.Name, "inherits_or_implements",
                            typeDecl.Identifier.GetLocation().GetLineSpan().StartLinePosition.Line + 1);

                    foreach (var iface in typeSymbol.Interfaces)
                        EmitEdge("class", typeSymbol.Name, "type", iface.Name, "inherits_or_implements",
                            typeDecl.Identifier.GetLocation().GetLineSpan().StartLinePosition.Line + 1);
                }

                // creates — every ObjectCreationExpression resolved to a symbol.
                foreach (var creation in root.DescendantNodes().OfType<ObjectCreationExpressionSyntax>())
                {
                    var owner = creation.AncestorsAndSelf().OfType<TypeDeclarationSyntax>().FirstOrDefault();
                    if (owner is null) continue;
                    if (model.GetSymbolInfo(creation.Type).Symbol is not INamedTypeSymbol target) continue;
                    EmitEdge("class", owner.Identifier.Text, "type", target.Name, "creates",
                        creation.GetLocation().GetLineSpan().StartLinePosition.Line + 1);
                }

                // imports — using directives, FQN preserved.
                foreach (var u in root.DescendantNodes().OfType<UsingDirectiveSyntax>())
                {
                    if (u.Name is null) continue;
                    EmitEdge("file", fileName, "module", u.Name.ToString(), "imports",
                        u.GetLocation().GetLineSpan().StartLinePosition.Line + 1);
                }
            }
        }
    }

    private static void EmitEdge(string fromKind, string fromName, string toKind, string toName, string rel, int line)
    {
        Console.Out.WriteLine(
            $"{{\"kind\":\"edge\",\"from\":{{\"type\":\"{fromKind}\",\"name\":\"{Esc(fromName)}\"}},\"to\":{{\"type\":\"{toKind}\",\"name\":\"{Esc(toName)}\"}},\"rel\":\"{rel}\",\"detail\":\"semantic\",\"line\":{line}}}");
    }

    private static string Esc(string s) =>
        s.Replace("\\", "\\\\").Replace("\"", "\\\"");

    private static int SelfCheck()
    {
        Console.WriteLine($"{{\"kind\":\"selfcheck\",\"tool\":\"{ToolVersion}\",\"ok\":true}}");
        Console.WriteLine("""{"kind":"edge","from":{"type":"class","name":"DemoChild"},"to":{"type":"type","name":"DemoBase"},"rel":"inherits_or_implements","detail":"semantic","line":1}""");
        return 0;
    }
}

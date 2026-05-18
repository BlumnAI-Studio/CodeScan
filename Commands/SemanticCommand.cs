using CodeScan.Services;
using CodeScan.Services.Semantic;

namespace CodeScan.Commands;

public sealed class SemanticCommand
{
    private static readonly string[] SupportedLanguages =
    [
        "csharp",      // Phase 1   — Roslyn Workspaces (full matcher)
        "typescript",  // Phase 2-A — ts.createProgram + TypeChecker (full matcher)
        "kotlin",      // Phase 1-B — Kotlin Analysis API + Pekko Typed (stub, self-check only)
        "go",          // Phase 2-B — go/packages + go/types (stub, self-check only)
        "python"       // Phase 2-C — libcst + jedi (stub, self-check only)
        // Phase 3+: "rust" (rust-analyzer), "java" (JDT/Spoon)
        // Phase 4+: "cpp" (Clang LibTooling), "php" (nikic/PHP-Parser)
    ];

    public int Execute(string[] args)
    {
        if (args.Length == 0)
        {
            PrintHelp();
            return 0;
        }

        var sub = args[0].ToLowerInvariant();
        var rest = args[1..];

        return sub switch
        {
            "status" => RunStatus(),
            "install" => RunInstall(rest),
            "self-check" => RunSelfCheck(rest),
            "analyze" => RunAnalyze(rest),
            "clear" => RunClear(rest),
            "-h" or "--help" or "help" => Help(0),
            _ => Help(1, $"Unknown subcommand: {sub}")
        };
    }

    private static int RunStatus()
    {
        Console.WriteLine($"Semantic dir: {AppPaths.GetSemanticDir()}");
        Console.WriteLine($"Docker available: {(SemanticDockerRunner.DockerAvailable() ? "yes" : "no")}");
        Console.WriteLine();
        Console.WriteLine("Languages:");
        Console.WriteLine("  language     enabled  image");

        foreach (var lang in SupportedLanguages)
        {
            var enabled = SemanticCache.IsConfiguredFor(lang) ? "✓" : " ";
            Console.WriteLine($"  {lang,-12} {enabled,-8} {SemanticDockerRunner.ImageFor(lang)}");
        }

        return 0;
    }

    private static int RunInstall(string[] rest)
    {
        if (rest.Length == 0)
        {
            Console.Error.WriteLine("Error: language required. Usage: codescan semantic install <language>");
            return 1;
        }

        var language = rest[0].ToLowerInvariant();
        if (!IsSupported(language)) return 1;

        if (!SemanticDockerRunner.DockerAvailable())
        {
            Console.Error.WriteLine("Error: docker is not available. Install Docker Desktop or Podman first.");
            return 1;
        }

        Console.WriteLine($"Pulling {SemanticDockerRunner.ImageFor(language)} ...");
        var pull = SemanticDockerRunner.Pull(language);
        if (pull.ExitCode != 0)
        {
            Console.Error.WriteLine("Pull failed:");
            Console.Error.WriteLine(pull.Stderr);
            Console.WriteLine();
            Console.WriteLine("(If the image hasn't been published yet, build it locally first:");
            Console.WriteLine($"  docker build -t {SemanticDockerRunner.ImageFor(language)} docker/semantic-{language})");
            return pull.ExitCode;
        }

        SemanticCache.Enable(language);
        Console.WriteLine($"Enabled: {language}");
        return 0;
    }

    private static int RunSelfCheck(string[] rest)
    {
        if (rest.Length == 0)
        {
            Console.Error.WriteLine("Error: language required. Usage: codescan semantic self-check <language>");
            return 1;
        }

        var language = rest[0].ToLowerInvariant();
        if (!IsSupported(language)) return 1;

        var result = SemanticDockerRunner.SelfCheck(language);
        Console.Write(result.Stdout);
        if (!string.IsNullOrEmpty(result.Stderr)) Console.Error.Write(result.Stderr);
        return result.ExitCode;
    }

    private static int RunAnalyze(string[] rest)
    {
        if (rest.Length < 2)
        {
            Console.Error.WriteLine("Error: language and path required. Usage: codescan semantic analyze <language> <path>");
            return 1;
        }

        var language = rest[0].ToLowerInvariant();
        if (!IsSupported(language)) return 1;

        var path = Path.GetFullPath(rest[1]);
        if (!Directory.Exists(path))
        {
            Console.Error.WriteLine($"Error: directory not found: {path}");
            return 1;
        }

        if (!SemanticDockerRunner.DockerAvailable())
        {
            Console.Error.WriteLine("Error: docker is not available.");
            return 1;
        }

        Console.WriteLine($"Running {SemanticDockerRunner.ImageFor(language)} on {path} ...");
        var result = SemanticDockerRunner.Run(language, path);
        if (result.ExitCode != 0)
        {
            Console.Error.WriteLine($"Analyzer exited with code {result.ExitCode}:");
            Console.Error.WriteLine(result.Stderr);
            return result.ExitCode;
        }

        var deps = SemanticResultMerger.Parse(result.Stdout);
        Console.WriteLine();
        Console.WriteLine($"=== Semantic Analysis ({language}) ===");
        Console.WriteLine($"Edges: {deps.Count}");
        foreach (var d in deps)
            Console.WriteLine($"  [{d.FromKind}] {d.FromName} -[{d.EdgeKind}]-> [{d.ToKind}] {d.ToName}  (line {d.Line})");

        return 0;
    }

    private static int RunClear(string[] rest)
    {
        if (rest.Length == 0)
        {
            SemanticCache.Clear();
            Console.WriteLine("Cleared all semantic cache.");
            return 0;
        }

        var language = rest[0].ToLowerInvariant();
        SemanticCache.Clear(language);
        Console.WriteLine($"Cleared semantic cache for: {language}");
        return 0;
    }

    private static bool IsSupported(string language)
    {
        if (Array.IndexOf(SupportedLanguages, language) < 0)
        {
            Console.Error.WriteLine($"Error: unsupported language '{language}'. Supported: {string.Join(", ", SupportedLanguages)}");
            return false;
        }
        return true;
    }

    private static int Help(int exitCode, string? message = null)
    {
        if (message is not null) Console.Error.WriteLine(message);
        PrintHelp();
        return exitCode;
    }

    private static void PrintHelp()
    {
        Console.WriteLine("""
            codescan semantic — compiler-backed analysis via docker images (Phase 1 PoC).

            Subcommands:
              status                       Show enabled languages and docker availability
              install <language>           Pull the docker image and enable the language
              self-check <language>        Run the image's --self-check entrypoint
              analyze <language> <path>    Run semantic analysis on a project path (stdout NDJSON parsed)
              clear [language]             Drop the cache (all languages if none given)

            Supported:
              csharp       Phase 1   — Roslyn Workspaces (full matcher)
              typescript   Phase 2-A — ts.createProgram + TypeChecker (full matcher)
              kotlin       Phase 1-B — Pekko Typed matchers planned (stub)
              go           Phase 2-B — go/packages planned (stub)
              python       Phase 2-C — libcst + jedi planned (stub)

            Images:    codescan/semantic-<language>:latest
            Cache dir: ~/.codescan/semantic/

            Build images locally (until ghcr publish lands):
              docker build -t codescan/semantic-csharp:latest      docker/semantic-csharp
              docker build -t codescan/semantic-typescript:latest  docker/semantic-typescript
              docker build -t codescan/semantic-kotlin:latest      docker/semantic-kotlin
              docker build -t codescan/semantic-go:latest          docker/semantic-go
              docker build -t codescan/semantic-python:latest      docker/semantic-python
            """);
    }
}

using System.Diagnostics;

namespace CodeScan.Services.Semantic;

/// <summary>
/// Invokes a semantic-docker image with the project root mounted as <c>/work</c>
/// and captures NDJSON from stdout. The host process is AOT-safe — we use
/// <see cref="Process"/> directly instead of any docker SDK that needs reflection.
/// </summary>
public static class SemanticDockerRunner
{
    public sealed record Result(int ExitCode, string Stdout, string Stderr);

    public static string ImageFor(string language) => $"codescan/semantic-{language}:latest";

    public static Result Run(string language, string projectRoot, TimeSpan? timeout = null)
    {
        var psi = new ProcessStartInfo("docker")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        psi.ArgumentList.Add("run");
        psi.ArgumentList.Add("--rm");
        psi.ArgumentList.Add("-v");
        psi.ArgumentList.Add($"{projectRoot}:/work:ro");
        psi.ArgumentList.Add(ImageFor(language));

        return Execute(psi, timeout ?? TimeSpan.FromMinutes(2));
    }

    public static Result SelfCheck(string language, TimeSpan? timeout = null)
    {
        var psi = new ProcessStartInfo("docker")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        psi.ArgumentList.Add("run");
        psi.ArgumentList.Add("--rm");
        psi.ArgumentList.Add(ImageFor(language));
        psi.ArgumentList.Add("--self-check");

        return Execute(psi, timeout ?? TimeSpan.FromSeconds(30));
    }

    public static Result Pull(string language, TimeSpan? timeout = null)
    {
        var psi = new ProcessStartInfo("docker")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        psi.ArgumentList.Add("pull");
        psi.ArgumentList.Add(ImageFor(language));

        return Execute(psi, timeout ?? TimeSpan.FromMinutes(10));
    }

    public static bool DockerAvailable()
    {
        try
        {
            var psi = new ProcessStartInfo("docker", "--version")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var p = Process.Start(psi);
            if (p is null) return false;
            p.WaitForExit(2000);
            return p.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    private static Result Execute(ProcessStartInfo psi, TimeSpan timeout)
    {
        try
        {
            using var p = Process.Start(psi) ?? throw new InvalidOperationException("Failed to start docker.");
            var stdout = p.StandardOutput.ReadToEndAsync();
            var stderr = p.StandardError.ReadToEndAsync();

            if (!p.WaitForExit((int)timeout.TotalMilliseconds))
            {
                try { p.Kill(entireProcessTree: true); } catch { }
                return new Result(124, stdout.Result, stderr.Result + "\n[timed out]");
            }

            return new Result(p.ExitCode, stdout.Result, stderr.Result);
        }
        catch (Exception ex)
        {
            return new Result(127, "", ex.Message);
        }
    }
}

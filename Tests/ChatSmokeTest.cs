using CodeScan.Services;
using CodeScan.Services.Llm;
using CodeScan.Services.Llm.Tools;
using Xunit;
using Xunit.Abstractions;

namespace CodeScan.Tests;

/// <summary>
/// End-to-end smoke tests that hit the real Gemma 4 GGUF on disk + LLamaSharp.
/// Gated behind `CODESCAN_LLM_SMOKE=1` so CI runs (no model file, limited
/// time / RAM) stay green.
///
/// Run locally with:
///   $env:CODESCAN_LLM_SMOKE="1"
///   dotnet test --filter "FullyQualifiedName~ChatSmokeTest"
/// </summary>
public sealed class ChatSmokeTest
{
    private readonly ITestOutputHelper _out;

    public ChatSmokeTest(ITestOutputHelper output) => _out = output;

    [Fact]
    public async Task Greeting_returns_done_call()
    {
        if (Environment.GetEnvironmentVariable("CODESCAN_LLM_SMOKE") != "1")
            return;  // skip silently in normal test runs

        var modelPath = ModelLocator.FindModel(ChatModelCatalog.Default);
        Assert.True(modelPath != null && File.Exists(modelPath),
            $"Gemma 4 E4B GGUF not found. Drop it at {ModelLocator.ModelsDir} or the dev fallback path.");

        await using var host = await LlmHost.LoadAsync(modelPath!, contextSize: 2048);
        using var db = new SqliteStore(AppPaths.DbPath);
        await using var loop = new AgentChatLoop(host, new CodeScanToolbelt(db, null),
            maxIterations: 3, maxTokensPerTurn: 256);

        string? doneText = null;
        var phases = new List<string>();
        await foreach (var update in loop.SendAsync("안녕! 너 누구야?", CancellationToken.None))
        {
            phases.Add(update.Phase);
            _out.WriteLine($"[{update.Phase}] {update.Text}");
            if (update.Phase == "done") doneText = update.Text;
            if (update.Phase == "error")
                Assert.Fail($"agent loop errored: {update.Text}");
        }

        Assert.NotNull(doneText);
        Assert.Contains("done", phases);  // sanity: at least one done emitted
        Assert.False(string.IsNullOrWhiteSpace(doneText), "done message was empty");
    }

    /// <summary>
    /// Regression test for the Gemma 4 chat-template fix series
    /// (commits 07527fb + ad37e24). The exact same prompt the user used
    /// in chat-20260529_18{1843,2103}.log — "AgentZero 어플리케이션 레이아웃 분석" —
    /// reproduces the failure path: model issues a project_tree tool call,
    /// receives ~4 KB of tree text back, then ought to produce a synthesised
    /// summary. Pre-fix the second turn died with an empty raw because we
    /// were leaking the EOG token <c>&lt;|tool_response&gt;</c> into user
    /// input. This test FAILS if that regresses.
    ///
    /// Configuration mirrors the production TUI default for this hardware:
    /// GPU Vulkan (full offload) + 32K ctx. Requires the AgentZeroLite
    /// project to be indexed in the local DB (it is on the dev box).
    /// </summary>
    [Fact]
    public async Task LayoutAnalysis_completes_through_project_tree()
    {
        if (Environment.GetEnvironmentVariable("CODESCAN_LLM_SMOKE") != "1")
            return;  // skip silently in normal test runs

        var modelPath = ModelLocator.FindModel(ChatModelCatalog.Default);
        Assert.True(modelPath != null && File.Exists(modelPath),
            $"Gemma 4 E4B GGUF not found at {ModelLocator.ModelsDir}.");

        // GPU/Vulkan with 32K ctx — same knob position as the failing TUI
        // sessions in the logs. If a Vulkan device isn't actually available
        // LLamaSharp will throw and the test will surface that explicitly.
        await using var host = await LlmHost.LoadAsync(
            modelPath!,
            contextSize: 32768,
            gpuLayerCount: 999,
            mainGpu: 0);

        using var db = new SqliteStore(AppPaths.DbPath);
        await using var loop = new AgentChatLoop(
            host,
            new CodeScanToolbelt(db, projectRoot: null),
            projectRoot: null,
            maxIterations: 6,
            maxTokensPerTurn: 1024);

        string? doneText = null;
        var phases = new List<string>();
        var toolCalls = new List<string>();
        await foreach (var update in loop.SendAsync(
                           "AgentZero 어플리케이션 레이아웃 분석",
                           CancellationToken.None))
        {
            phases.Add(update.Phase);
            // Cap each line so the xUnit output stays readable even when a
            // tool_result lands here.
            var preview = update.Text.Length > 240
                ? update.Text[..240] + "…"
                : update.Text;
            _out.WriteLine($"[{update.Phase}] {preview}");

            if (update.Phase == "tool") toolCalls.Add(update.Text);
            if (update.Phase == "done") doneText = update.Text;
            if (update.Phase == "error")
                Assert.Fail($"agent loop errored: {update.Text}");
        }

        // 1. Reached done — i.e. didn't iterate to the max-iterations cap.
        Assert.Contains("done", phases);
        Assert.False(string.IsNullOrWhiteSpace(doneText), "done message was empty");

        // 2. EMPTY-RAW REGRESSION GUARD. The pre-fix failure mode landed in
        //    the synthesised fallback whose marker text starts with
        //    "(모델이 빈 응답" — if we ever see that, the chat template /
        //    EOG handling has regressed.
        Assert.False(doneText!.StartsWith("(모델이 빈 응답"),
            $"empty-raw fallback fired — chat template regression. doneText:\n{doneText}");

        // 3. At least one tool call happened. The prompt is abstract enough
        //    that without project_tree (or db_search) the model can't
        //    sensibly answer; an immediate done would mean the search-
        //    strategy prompt instruction is being ignored, which would
        //    point at a separate regression.
        Assert.NotEmpty(toolCalls);
    }
}

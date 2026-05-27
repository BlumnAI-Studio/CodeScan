using CodeScan.Services;
using CodeScan.Services.Llm;
using CodeScan.Services.Llm.Tools;
using Xunit;
using Xunit.Abstractions;

namespace CodeScan.Tests;

/// <summary>
/// End-to-end smoke tests that hit the real Gemma 4 GGUF on disk + LLamaSharp
/// CPU backend. Gated behind `CODESCAN_LLM_SMOKE=1` so CI runs (no model file,
/// limited time / RAM) stay green.
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
}

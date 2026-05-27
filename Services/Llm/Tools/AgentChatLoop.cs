using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using LLama;
using LLama.Common;
using LLama.Sampling;

namespace CodeScan.Services.Llm.Tools;

public sealed record ToolCall(string Tool, JsonObject Args);

public sealed record ToolTurn(ToolCall Call, string ToolResult);

public sealed record ChatTurnUpdate(
    string Phase,         // "thinking" | "tool" | "tool_result" | "done" | "error"
    string Text);

/// <summary>
/// Drives the Gemma 4 chat through a JSON tool-call loop. Each user
/// message kicks off N iterations:
///   model emits {"tool": "...", "args": {...}} → toolbelt runs it →
///   result fed back → repeat until `done` or iteration cap reached.
///
/// GBNF sampler-level grammar guarantees structurally valid JSON every
/// turn so the parser never has to wrestle with free prose. Across user
/// sends within ONE <see cref="AgentChatLoop"/> instance the KV cache is
/// preserved, so follow-up questions ("show me method X you just found")
/// keep the previous turn's context cheaply.
/// </summary>
public sealed class AgentChatLoop : IAsyncDisposable
{
    private readonly LlmHost _host;
    private readonly CodeScanToolbelt _toolbelt;
    private readonly LLamaContext _context;
    private readonly InteractiveExecutor _executor;
    private readonly Grammar _grammar;
    private readonly int _maxIterations;
    private readonly int _maxTokensPerTurn;
    private readonly float _temperature;

    private bool _firstMarkerSeen;   // tracks chat-template first vs continuation
    private bool _firstUserSend = true;  // emits system prompt only on first send
    private bool _disposed;

    public AgentChatLoop(
        LlmHost host,
        CodeScanToolbelt toolbelt,
        int maxIterations = 6,
        int maxTokensPerTurn = 512,
        float temperature = 0.0f)
    {
        _host = host;
        _toolbelt = toolbelt;
        _maxIterations = maxIterations;
        _maxTokensPerTurn = maxTokensPerTurn;
        _temperature = temperature;

        var (weights, modelParams) = host.GetInternals();
        _context = weights.CreateContext(modelParams);
        _executor = new InteractiveExecutor(_context);
        _grammar = new Grammar(CodeScanToolGrammar.Gbnf, CodeScanToolGrammar.GrammarRootRule);
    }

    /// <summary>
    /// Process one user message. Yields incremental updates so the TUI can
    /// stream the agent's tool chain as it unfolds. The final update has
    /// <see cref="ChatTurnUpdate.Phase"/> == "done" (success) or "error".
    /// </summary>
    public async IAsyncEnumerable<ChatTurnUpdate> SendAsync(
        string userMessage,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(AgentChatLoop));

        for (var iter = 0; iter < _maxIterations; iter++)
        {
            ct.ThrowIfCancellationRequested();

            string turnInput;
            if (iter == 0)
            {
                turnInput = _firstUserSend
                    ? $"{CodeScanToolGrammar.SystemPrompt}\n\n--- USER ---\n{userMessage}"
                    : $"--- USER ---\n{userMessage}";
                _firstUserSend = false;
            }
            else
            {
                // continuation: previous tool result is already part of the last
                // yielded turn; rebuild the prompt fragment here.
                // (We don't keep a history list because the KV cache already holds
                // it — we just need to feed THIS turn's text.)
                turnInput = ""; // overridden below from the captured tool result
            }

            // For continuation turns the caller-side state is captured in
            // _lastToolResult. We thread it through a local variable instead.
            if (iter > 0)
                turnInput = $"--- TOOL RESULT ---\n{_lastToolResult}\n\n(Reply with the next single JSON tool call. Call \"done\" when satisfied.)";

            yield return new ChatTurnUpdate("thinking", iter == 0 ? "Reading the question…" : "Reasoning…");

            string? rawJson = null;
            string? generateErr = null;
            bool cancelled = false;
            try { rawJson = await GenerateOneTurnAsync(turnInput, ct); }
            catch (OperationCanceledException) { cancelled = true; }
            catch (Exception ex) { generateErr = $"model error: {ex.Message}"; }
            if (cancelled) yield break;
            if (generateErr != null)
            {
                yield return new ChatTurnUpdate("error", generateErr);
                yield break;
            }

            ToolCall? call = null;
            string? parseErr = null;
            try { call = ParseToolCall(rawJson!); }
            catch (JsonException ex) { parseErr = $"model returned unparseable JSON: {ex.Message}\nraw: {Truncate(rawJson!, 200)}"; }
            if (parseErr != null)
            {
                yield return new ChatTurnUpdate("error", parseErr);
                yield break;
            }

            var c = call!;
            if (!CodeScanToolGrammar.KnownTools.Contains(c.Tool))
            {
                yield return new ChatTurnUpdate("error", $"model called unknown tool '{c.Tool}'");
                yield break;
            }

            if (c.Tool == CodeScanToolGrammar.DoneToolName)
            {
                var msg = c.Args.TryGetPropertyValue("message", out var m) && m is JsonValue v
                    ? v.GetValue<string>()
                    : "(no message)";
                yield return new ChatTurnUpdate("done", msg);
                yield break;
            }

            yield return new ChatTurnUpdate("tool",
                $"{c.Tool}({Truncate(c.Args.ToJsonString(), 200)})");

            var toolResult = _toolbelt.Execute(c.Tool, c.Args);
            _lastToolResult = toolResult;

            yield return new ChatTurnUpdate("tool_result", Truncate(toolResult, 600));
        }

        yield return new ChatTurnUpdate("error",
            $"max iterations ({_maxIterations}) reached without 'done'");
    }

    private string _lastToolResult = "";

    private async Task<string> GenerateOneTurnAsync(string turnText, CancellationToken ct)
    {
        var prompt = GemmaChatTemplate.Format(turnText, !_firstMarkerSeen);
        _firstMarkerSeen = true;

        var inferenceParams = new InferenceParams
        {
            MaxTokens = _maxTokensPerTurn,
            AntiPrompts = GemmaChatTemplate.AntiPrompts,
            SamplingPipeline = new DefaultSamplingPipeline
            {
                Temperature = _temperature,
                Grammar = _grammar,
                GrammarOptimization = DefaultSamplingPipeline.GrammarOptimizationMode.Extended,
            },
        };

        var sb = new StringBuilder();
        await foreach (var tok in _executor.InferAsync(prompt, inferenceParams, ct))
            sb.Append(tok);

        return StripTrailingAntiPrompt(sb.ToString()).Trim();
    }

    private static string StripTrailingAntiPrompt(string text)
    {
        foreach (var anti in GemmaChatTemplate.AntiPrompts)
        {
            for (var len = anti.Length; len > 0; len--)
                if (text.EndsWith(anti[..len], StringComparison.Ordinal))
                    return text[..^len];
        }
        return text;
    }

    internal static ToolCall ParseToolCall(string rawJson)
    {
        using var doc = JsonDocument.Parse(rawJson);
        var root = doc.RootElement;
        if (!root.TryGetProperty("tool", out var toolEl))
            throw new JsonException("missing 'tool' field");
        if (toolEl.ValueKind != JsonValueKind.String)
            throw new JsonException($"'tool' must be a string, got {toolEl.ValueKind}");
        var tool = toolEl.GetString() ?? throw new JsonException("'tool' is null");
        var args = root.TryGetProperty("args", out var argsEl)
            ? JsonNode.Parse(argsEl.GetRawText())?.AsObject() ?? new JsonObject()
            : new JsonObject();
        return new ToolCall(tool, args);
    }

    private static string Truncate(string s, int max)
        => string.IsNullOrEmpty(s) || s.Length <= max ? s : s[..max] + "…";

    public ValueTask DisposeAsync()
    {
        if (_disposed) return ValueTask.CompletedTask;
        _disposed = true;
        _context.Dispose();
        return ValueTask.CompletedTask;
    }
}

namespace CodeScan.Services.Llm.Tools;

/// <summary>
/// Gemma 4 chat-template markers. We used to emit Gemma 2/3 tokens
/// (<c>&lt;start_of_turn&gt;</c> / <c>&lt;end_of_turn&gt;</c>) here — but the
/// Gemma 4 GGUF's <c>tokenizer.chat_template</c> metadata and its EOS
/// token (id 106 = <c>&lt;turn|&gt;</c>) make it clear those names are
/// gone in this generation. The old format silently degraded on big
/// tool_result turns: the model couldn't parse the unfamiliar markers
/// and emitted EOS as its very first token, producing empty raw.
///
/// Reference: the chat_template macros extracted from the GGUF use:
///   • <c>&lt;|turn&gt;role\n…&lt;turn|&gt;\n</c> for every role
///     (system / user / model / tool)
///   • <c>&lt;|tool_response&gt;…&lt;tool_response|&gt;</c> for tool replies
///   • <c>&lt;|tool_call&gt;…&lt;tool_call|&gt;</c> for tool calls
///
/// We don't use the native tool_call/response brackets yet (our JSON
/// tool protocol is grammar-enforced on the model side and the toolbelt
/// reads JSON; switching is a follow-up) but the surrounding turn
/// markers MUST be correct or the model loses its place.
/// </summary>
public static class GemmaChatTemplate
{
    // EOS token = id 106, surfaced as the literal string "<turn|>" by the
    // tokenizer. We also list "<eos>" (id 1, marked EOG) as a safety net.
    public static readonly string[] AntiPrompts = new[] { "<turn|>", "<eos>" };

    /// <summary>
    /// First turn of a session: emit the system prompt as its own turn,
    /// then the user message, then leave the cursor inside an open model
    /// turn so generation continues directly.
    /// </summary>
    public static string FormatFirstTurn(string systemPrompt, string userMessage) =>
        $"<|turn>system\n{systemPrompt}<turn|>\n" +
        $"<|turn>user\n{userMessage}<turn|>\n" +
        $"<|turn>model\n";

    /// <summary>
    /// Continuation user turn — KV cache still warm from a prior turn,
    /// no system prompt needed.
    /// </summary>
    public static string FormatUserTurn(string userMessage) =>
        $"<|turn>user\n{userMessage}<turn|>\n" +
        $"<|turn>model\n";

    /// <summary>
    /// Tool-result turn. We wrap the JSON payload with the Gemma 4 native
    /// tool_response brackets so the model parses it as a structured tool
    /// reply rather than free user text. <c>toolName</c> is the name of
    /// the tool the model just called — taken from the prior model turn.
    /// </summary>
    public static string FormatToolResult(string toolName, string toolResultJson) =>
        $"<|turn>user\n" +
        $"<|tool_response>response:{toolName}{toolResultJson}<tool_response|>" +
        $"<turn|>\n" +
        $"<|turn>model\n";
}

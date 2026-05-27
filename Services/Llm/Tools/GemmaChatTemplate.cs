namespace CodeScan.Services.Llm.Tools;

/// <summary>
/// Gemma 2/3/4 chat-template markers. Inlined here (instead of pulling
/// LLamaSharp's template engine) so AOT trimming has nothing to chase and
/// the first-turn / continuation split is explicit.
/// </summary>
public static class GemmaChatTemplate
{
    public const string FirstTurnFormat =
        "<start_of_turn>user\n{0}<end_of_turn>\n<start_of_turn>model\n";

    public const string ContinuationFormat =
        "\n<start_of_turn>user\n{0}<end_of_turn>\n<start_of_turn>model\n";

    public static readonly string[] AntiPrompts = new[] { "<end_of_turn>", "<eos>" };

    public static string Format(string content, bool isFirstTurn)
        => string.Format(isFirstTurn ? FirstTurnFormat : ContinuationFormat, content);
}

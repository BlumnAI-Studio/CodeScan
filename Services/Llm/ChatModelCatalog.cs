namespace CodeScan.Services.Llm;

public sealed record ChatModelEntry(
    string Id,
    string DisplayName,
    string FileName,
    string DownloadUrl,
    long ApproxBytes);

public static class ChatModelCatalog
{
    public static readonly ChatModelEntry Gemma4E4B = new(
        Id: "gemma-4-E4B-UD-Q4_K_XL",
        DisplayName: "Gemma 4 E4B (UD-Q4_K_XL, ~5.1 GB)",
        FileName: "gemma-4-E4B-it-UD-Q4_K_XL.gguf",
        DownloadUrl: "https://huggingface.co/unsloth/gemma-4-E4B-it-GGUF/resolve/main/gemma-4-E4B-it-UD-Q4_K_XL.gguf",
        ApproxBytes: 5_101_718_208L);

    public static readonly ChatModelEntry Gemma4E2B = new(
        Id: "gemma-4-E2B-UD-Q4_K_XL",
        DisplayName: "Gemma 4 E2B (UD-Q4_K_XL, ~3.2 GB)",
        FileName: "gemma-4-E2B-it-UD-Q4_K_XL.gguf",
        DownloadUrl: "https://huggingface.co/unsloth/gemma-4-E2B-it-GGUF/resolve/main/gemma-4-E2B-it-UD-Q4_K_XL.gguf",
        ApproxBytes: 3_174_043_296L);

    public static readonly IReadOnlyList<ChatModelEntry> All = new[]
    {
        Gemma4E4B,
        Gemma4E2B,
    };

    public static ChatModelEntry Default => Gemma4E4B;
}

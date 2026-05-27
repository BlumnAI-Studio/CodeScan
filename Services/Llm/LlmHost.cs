using LLama;
using LLama.Common;

namespace CodeScan.Services.Llm;

/// <summary>
/// Owns the <see cref="LLamaWeights"/> + <see cref="ModelParams"/> for the
/// duration of a TUI Chat session. CPU-only (Backend.Cpu loads the right
/// native library per OS via runtimes/), single-context. Disposing releases
/// the model from memory.
/// </summary>
public sealed class LlmHost : IAsyncDisposable
{
    private readonly LLamaWeights _weights;
    private readonly ModelParams _modelParams;

    public string ModelPath { get; }

    private LlmHost(string modelPath, LLamaWeights weights, ModelParams modelParams)
    {
        ModelPath = modelPath;
        _weights = weights;
        _modelParams = modelParams;
    }

    public static async Task<LlmHost> LoadAsync(
        string modelPath,
        uint contextSize = 4096,
        IProgress<float>? progress = null,
        CancellationToken ct = default)
    {
        if (!File.Exists(modelPath))
            throw new FileNotFoundException($"GGUF model not found: {modelPath}", modelPath);

        var modelParams = new ModelParams(modelPath)
        {
            ContextSize = contextSize,
            // CPU-only: 0 GPU layers — Backend.Cpu has no GPU offload anyway,
            // but being explicit prevents surprises if a Vulkan/CUDA backend
            // ever lands on the PATH.
            GpuLayerCount = 0,
            UseMemorymap = true,
        };

        var weights = await LLamaWeights.LoadFromFileAsync(modelParams, ct, progress);
        return new LlmHost(modelPath, weights, modelParams);
    }

    internal (LLamaWeights weights, ModelParams modelParams) GetInternals() => (_weights, _modelParams);

    public ValueTask DisposeAsync()
    {
        _weights.Dispose();
        return ValueTask.CompletedTask;
    }
}

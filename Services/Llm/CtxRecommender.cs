namespace CodeScan.Services.Llm;

public sealed record CtxRecommendation(
    int RecommendedCtx,
    int ModelMaxCtx,
    long PerTokenKvBytes,
    long ModelBytes,
    long DeviceBytes,
    string Rationale);

/// <summary>
/// Translates "this model on this device" into a sensible context-size knob
/// position. We never block the user from picking higher — Vulkan/llama.cpp
/// will fail loudly if it actually can't allocate — but the recommended value
/// keeps first-time users out of the OOM ditch.
/// </summary>
public static class CtxRecommender
{
    /// <summary>Bound the recommendation so the slider never lands below 2K.</summary>
    private const int MinCtx = 2048;

    /// <summary>
    /// CPU mode: we don't have a sharp memory ceiling (system RAM is usually
    /// vast vs. model size), so just hand back the model's trained max.
    /// </summary>
    private const int CpuDefaultCap = 32 * 1024;

    public static CtxRecommendation For(GgufMetadata model, GpuDevice? device, int gpuLayerCount)
    {
        var modelMax = model.ContextLength ?? 8192;
        var perToken = GgufReader.KvBytesPerToken(model) ?? 0L;

        // CPU mode — no VRAM constraint to respect, but stay sane.
        if (gpuLayerCount <= 0 || device is null || device.VramBytes <= 0)
        {
            var ctx = Math.Min(modelMax, CpuDefaultCap);
            return new CtxRecommendation(
                Pow2Floor(ctx),
                modelMax,
                perToken,
                model.FileSize,
                device?.VramBytes ?? 0,
                $"CPU mode — default cap {Format(CpuDefaultCap)} (model max {Format(modelMax)}).");
        }

        if (perToken == 0)
        {
            // Missing dims — can't model VRAM pressure. Recommend model max but
            // capped at 32K so we don't blindly tell the user to allocate
            // 128K KV cache on a 6GB card.
            var ctx = Math.Min(modelMax, 32 * 1024);
            return new CtxRecommendation(
                Pow2Floor(ctx),
                modelMax,
                0,
                model.FileSize,
                device.VramBytes,
                $"GPU mode — KV size unknown; conservative cap {Format(ctx)}.");
        }

        // KV must fit alongside the weights. Use ~70% of the device-local
        // heap budget to leave room for activations + workspace, and round
        // down to a power of two for parity with the radio UI.
        var weightsCost = (long)(model.FileSize * 1.10);
        var available = (long)(device.VramBytes * 0.70) - weightsCost;
        if (available <= 0)
        {
            // Model alone almost fills the heap — fall back to the smallest
            // safe context.
            return new CtxRecommendation(
                MinCtx,
                modelMax,
                perToken,
                model.FileSize,
                device.VramBytes,
                $"Tight fit — model {Format(model.FileSize)} vs heap {Format(device.VramBytes)}. Falling back to {Format(MinCtx)}.");
        }

        var maxCtxByVram = (int)(available / perToken);
        var ctxByModel = Math.Min(modelMax, maxCtxByVram);
        var pow2 = Pow2Floor(Math.Max(MinCtx, ctxByModel));

        var kvAtPick = pow2 * perToken;
        var rationale =
            $"Model {model.Architecture} max {Format(modelMax)} · " +
            $"KV {Format(perToken)}/token → {Format(kvAtPick)} at {Format(pow2)} · " +
            $"device-local {Format(device.VramBytes)}.";
        return new CtxRecommendation(
            pow2,
            modelMax,
            perToken,
            model.FileSize,
            device.VramBytes,
            rationale);
    }

    /// <summary>
    /// Builds the dynamic ctx options list for the TUI radio. Starts at 4K
    /// and doubles up to (and including) the model's trained max. The cap
    /// option is labelled "(model max)" so the user sees the hard ceiling.
    /// </summary>
    public static List<(int Tokens, string Label)> ContextChoices(int modelMaxCtx)
    {
        var list = new List<(int, string)>();
        for (int ctx = 4096; ctx < modelMaxCtx; ctx *= 2)
            list.Add((ctx, FormatK(ctx)));
        list.Add((modelMaxCtx, $"{FormatK(modelMaxCtx)} (max)"));
        return list;
    }

    public static int Pow2Floor(int n)
    {
        if (n < 1) return 1;
        var p = 1;
        while (p * 2 <= n) p *= 2;
        return p;
    }

    private static string FormatK(int n) => n >= 1024
        ? $"{n / 1024}K"
        : n.ToString();

    private static string Format(long bytes)
    {
        if (bytes <= 0) return "?";
        const double KiB = 1024, MiB = KiB * 1024, GiB = MiB * 1024;
        if (bytes >= GiB) return $"{bytes / GiB:F2} GiB";
        if (bytes >= MiB) return $"{bytes / MiB:F1} MiB";
        if (bytes >= KiB) return $"{bytes / KiB:F1} KiB";
        return $"{bytes} B";
    }
}

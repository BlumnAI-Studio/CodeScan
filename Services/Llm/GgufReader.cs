using System.Text;

namespace CodeScan.Services.Llm;

/// <summary>
/// Subset of GGUF header metadata we surface to the TUI: the model's trained
/// context length, layer / head dimensions for KV-cache sizing, and the file
/// size so the recommender can subtract weights from available VRAM.
/// Any field may be null when the GGUF doesn't carry that key.
/// </summary>
public sealed record GgufMetadata(
    string Architecture,
    int? ContextLength,
    int? BlockCount,
    int? EmbeddingLength,
    int? HeadCount,
    int? HeadCountKv,
    long FileSize);

/// <summary>
/// Minimal GGUF v1/v2/v3 header parser. Reads only the metadata key-values we
/// need to advise the user on max context and KV memory pressure — no tensor
/// decode, no LLamaSharp dependency, so it's safe to call before the user
/// commits to a full ~10–30s model load.
/// </summary>
public static class GgufReader
{
    // 'G''G''U''F' little-endian.
    private const uint Magic = 0x46554747;

    // Cap the byte range we'll scan looking for our keys. The metadata block
    // sits right after the header, so 8 MB is generous even for 100+ KV pairs.
    private const long ScanCap = 8L * 1024 * 1024;

    public static GgufMetadata? Read(string path)
    {
        try
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            using var br = new BinaryReader(fs, Encoding.UTF8, leaveOpen: false);

            var fileSize = fs.Length;
            if (br.ReadUInt32() != Magic) return null;
            var version = br.ReadUInt32();
            if (version is < 1 or > 3) return null;

            ulong _tensorCount, kvCount;
            if (version == 1)
            {
                _tensorCount = br.ReadUInt32();
                kvCount = br.ReadUInt32();
            }
            else
            {
                _tensorCount = br.ReadUInt64();
                kvCount = br.ReadUInt64();
            }

            string? arch = null;
            int? ctxLen = null, blockCount = null, embedLen = null, headCount = null, headCountKv = null;

            for (ulong i = 0; i < kvCount; i++)
            {
                if (fs.Position > ScanCap) break;

                var key = ReadString(br, version);
                var type = br.ReadUInt32();
                var raw = ReadValue(br, type, version);

                if (key == "general.architecture" && raw is string s)
                    arch = s;
                else if (arch != null)
                {
                    // Most modern architectures (llama, gemma2, gemma3, qwen2)
                    // namespace their keys with the architecture name.
                    var ctxKey = $"{arch}.context_length";
                    var blkKey = $"{arch}.block_count";
                    var embKey = $"{arch}.embedding_length";
                    var hcKey  = $"{arch}.attention.head_count";
                    var hckvKey = $"{arch}.attention.head_count_kv";

                    if (key == ctxKey) ctxLen = ToInt(raw);
                    else if (key == blkKey) blockCount = ToInt(raw);
                    else if (key == embKey) embedLen = ToInt(raw);
                    else if (key == hcKey) headCount = ToInt(raw);
                    else if (key == hckvKey) headCountKv = ToInt(raw);
                }

                // Early-out: we've found everything we want.
                if (arch != null && ctxLen != null && blockCount != null
                    && embedLen != null && headCount != null)
                    break;
            }

            return new GgufMetadata(
                arch ?? "unknown",
                ctxLen, blockCount, embedLen, headCount, headCountKv,
                fileSize);
        }
        catch
        {
            return null;
        }
    }

    private static int? ToInt(object? o) => o switch
    {
        byte b => b,
        sbyte sb => sb,
        ushort us => us,
        short s => s,
        uint u => (int)u,
        int i => i,
        ulong ul => (int)ul,
        long l => (int)l,
        _ => null,
    };

    private static string ReadString(BinaryReader br, uint version)
    {
        var len = version == 1 ? br.ReadUInt32() : br.ReadUInt64();
        // Defensive: a corrupt length here would otherwise read gigabytes.
        if (len > 64_000) { br.BaseStream.Position += (long)len; return ""; }
        var bytes = br.ReadBytes((int)len);
        return Encoding.UTF8.GetString(bytes);
    }

    // GGUF metadata value type tags (see ggml/docs/gguf.md).
    private static object? ReadValue(BinaryReader br, uint type, uint version)
    {
        switch (type)
        {
            case 0:  return br.ReadByte();
            case 1:  return (sbyte)br.ReadByte();
            case 2:  return br.ReadUInt16();
            case 3:  return br.ReadInt16();
            case 4:  return br.ReadUInt32();
            case 5:  return br.ReadInt32();
            case 6:  br.ReadSingle(); return null;
            case 7:  return br.ReadByte() != 0;
            case 8:  return ReadString(br, version);
            case 9:
                var elemType = br.ReadUInt32();
                var arrLen = version == 1 ? br.ReadUInt32() : br.ReadUInt64();
                // Skip the array — we don't surface array values, but we MUST
                // advance the stream past each element correctly to keep the
                // next key aligned.
                for (ulong i = 0; i < arrLen; i++)
                    ReadValue(br, elemType, version);
                return null;
            case 10: return br.ReadUInt64();
            case 11: return br.ReadInt64();
            case 12: br.ReadDouble(); return null;
            default: throw new InvalidDataException($"unknown gguf value type {type}");
        }
    }

    /// <summary>
    /// Approximate KV cache bytes for a single token, in fp16 (2 bytes per
    /// element). Returns null if the metadata didn't carry the dimensions.
    /// Formula: 2 (K+V tensors) × n_layers × n_kv_heads × head_dim × 2 bytes.
    /// head_dim falls back to embedding/head_count when head_count_kv missing.
    /// </summary>
    public static long? KvBytesPerToken(GgufMetadata m)
    {
        if (m.BlockCount is not int layers || layers <= 0) return null;
        if (m.HeadCount is not int heads || heads <= 0) return null;
        if (m.EmbeddingLength is not int embed || embed <= 0) return null;
        var headDim = embed / heads;
        var kvHeads = m.HeadCountKv ?? heads;
        return 2L * layers * kvHeads * headDim * 2L;
    }
}

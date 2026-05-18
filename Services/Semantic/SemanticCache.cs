using System.Security.Cryptography;
using System.Text;

namespace CodeScan.Services.Semantic;

/// <summary>
/// Cache for semantic-docker analysis results. Layout under
/// <see cref="AppPaths.SemanticDir"/>:
/// <code>
///   ~/.codescan/semantic/
///     csharp/
///       enabled              ← presence marker (created by SemanticCommand install)
///       cache/&lt;sha256&gt;.ndjson
///     java/
///       ...
/// </code>
/// A cache hit lets us skip a (slow) docker run and merge the cached NDJSON directly.
/// </summary>
public static class SemanticCache
{
    public static string LanguageDir(string language)
    {
        var dir = Path.Combine(AppPaths.GetSemanticDir(), language);
        Directory.CreateDirectory(dir);
        return dir;
    }

    public static string CacheDir(string language)
    {
        var dir = Path.Combine(LanguageDir(language), "cache");
        Directory.CreateDirectory(dir);
        return dir;
    }

    public static bool IsConfiguredFor(string language)
        => File.Exists(Path.Combine(LanguageDir(language), "enabled"));

    public static void Enable(string language)
        => File.WriteAllText(Path.Combine(LanguageDir(language), "enabled"), "");

    public static void Disable(string language)
    {
        var marker = Path.Combine(LanguageDir(language), "enabled");
        if (File.Exists(marker)) File.Delete(marker);
    }

    public static void Clear(string? language = null)
    {
        if (language is null)
        {
            if (Directory.Exists(AppPaths.SemanticDir))
                Directory.Delete(AppPaths.SemanticDir, recursive: true);
            return;
        }

        var dir = Path.Combine(AppPaths.SemanticDir, language);
        if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
    }

    /// <summary>
    /// Cache key is sha256(file content + tool version). Source-only hashing
    /// keeps the host independent of how the docker analyzer was invoked.
    /// </summary>
    public static string ComputeKey(string filePath, string toolVersion)
    {
        var bytes = File.ReadAllBytes(filePath);
        Span<byte> hash = stackalloc byte[32];
        SHA256.HashData(bytes, hash);

        var sb = new StringBuilder(64 + toolVersion.Length);
        foreach (var b in hash) sb.Append(b.ToString("x2"));
        sb.Append('-').Append(toolVersion);
        return sb.ToString();
    }

    public static string KeyToPath(string language, string key)
        => Path.Combine(CacheDir(language), key + ".ndjson");

    public static bool TryRead(string language, string key, out string ndjson)
    {
        var path = KeyToPath(language, key);
        if (File.Exists(path))
        {
            ndjson = File.ReadAllText(path);
            return true;
        }
        ndjson = "";
        return false;
    }

    public static void Write(string language, string key, string ndjson)
        => File.WriteAllText(KeyToPath(language, key), ndjson);
}

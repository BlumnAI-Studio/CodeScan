namespace CodeScan.Services.Llm;

public static class ModelLocator
{
    public static string ModelsDir
    {
        get
        {
            var dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".codescan", "models");
            Directory.CreateDirectory(dir);
            return dir;
        }
    }

    // Developer fallback path — kept for back-compat with the AgentZeroLite
    // download location so a single Gemma download serves both projects on
    // the dev box. Not used on machines without this directory.
    private const string DevModelsDir = @"D:\Code\AI\GemmaNet\models";

    public static string TargetPath(ChatModelEntry entry)
        => Path.Combine(ModelsDir, entry.FileName);

    public static string? FindModel(ChatModelEntry entry)
    {
        var target = TargetPath(entry);
        if (File.Exists(target)) return target;
        var dev = Path.Combine(DevModelsDir, entry.FileName);
        return File.Exists(dev) ? dev : null;
    }

    public static List<(ChatModelEntry Entry, string Path)> EnumerateAvailable()
    {
        var found = new List<(ChatModelEntry, string)>();
        foreach (var entry in ChatModelCatalog.All)
        {
            var path = FindModel(entry);
            if (path != null) found.Add((entry, path));
        }
        return found;
    }

    public static List<string> EnumerateGgufFiles(string dir)
    {
        if (!Directory.Exists(dir)) return new List<string>();
        try
        {
            return Directory.GetFiles(dir, "*.gguf", SearchOption.TopDirectoryOnly)
                .OrderByDescending(File.GetLastWriteTimeUtc).ToList();
        }
        catch { return new List<string>(); }
    }
}

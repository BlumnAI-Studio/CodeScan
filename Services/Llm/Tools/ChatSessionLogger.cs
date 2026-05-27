namespace CodeScan.Services.Llm.Tools;

/// <summary>
/// Append-only per-session chat log. One file per chat lifetime, written to
/// <c>~/.codescan/logs/chat-YYYYMMDD_HHmmss.log</c>. Records every event
/// the UI shows plus the raw model output before it is parsed, so we can
/// reconstruct exactly what the LLM emitted (e.g. when JSON fails to parse
/// and the UI only sees a truncated 200-char snippet).
///
/// Thread-safe via a per-instance lock — the agent loop runs on a single
/// task at a time, but UI marshalling makes serialization cheap insurance.
/// </summary>
public sealed class ChatSessionLogger : IDisposable
{
    private readonly StreamWriter _writer;
    private readonly object _gate = new();
    private bool _disposed;

    public string LogPath { get; }

    private ChatSessionLogger(string path, StreamWriter writer)
    {
        LogPath = path;
        _writer = writer;
    }

    public static ChatSessionLogger Create(string modelPath, string? projectRoot)
    {
        var dir = AppPaths.GetLogDir();
        var fileName = $"chat-{DateTime.Now:yyyyMMdd_HHmmss}.log";
        var path = Path.Combine(dir, fileName);
        var writer = new StreamWriter(path, append: false) { AutoFlush = true };
        var log = new ChatSessionLogger(path, writer);
        log.Write("session", $"start  model={Path.GetFileName(modelPath)}  project={projectRoot ?? "(none)"}");
        return log;
    }

    public void Write(string tag, string text)
    {
        if (_disposed) return;
        lock (_gate)
        {
            try
            {
                _writer.Write('[');
                _writer.Write(DateTime.Now.ToString("HH:mm:ss"));
                _writer.Write("] [");
                _writer.Write(tag);
                _writer.Write("] ");
                // Collapse newlines so each event stays on one line and the
                // log is grep-friendly. Raw JSON multiline payloads become
                // single-line strings here — fine for forensic reading.
                _writer.WriteLine(text.Replace('\r', ' ').Replace('\n', ' '));
            }
            catch { /* logging failure must not break the chat */ }
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        lock (_gate)
        {
            try { Write("session", "end"); } catch { }
            try { _writer.Dispose(); } catch { }
        }
    }
}

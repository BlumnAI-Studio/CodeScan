using System.Collections.ObjectModel;
using Terminal.Gui;
using CodeScan.Services;
using CodeScan.Services.Llm;
using CodeScan.Services.Llm.Tools;

namespace CodeScan.Tui;

/// <summary>
/// Chat panel for the TUI — wraps a Gemma 4 e4b CPU agent loop.
///
/// State machine:
///   Start  → choose model + (optional) project context
///   Loading → load GGUF into memory (slow, single-thread CPU)
///   Chat   → interactive conversation
/// All transitions show/hide the same set of views so the parent
/// <see cref="MainView"/> can host this as a single Toplevel panel without
/// stacking nested windows.
/// </summary>
public sealed class ChatView : IAsyncDisposable
{
    private readonly Toplevel _root;
    private readonly Action _onExit;

    // Start screen
    private readonly Label _titleLabel;
    private readonly Label _hintLabel;
    private readonly ListView _modelList;
    private readonly Label _modelPathLabel;
    private readonly Label _projectPathLabel;
    private readonly Button _startBtn;
    private readonly Button _pickProjectBtn;
    private readonly Button _downloadBtn;

    // Download screen
    private readonly Label _downloadTitleLabel;
    private readonly Label _downloadStatusLabel;
    private readonly Label _downloadBarLabel;
    private readonly Button _cancelDownloadBtn;

    // Chat screen
    private readonly TextView _historyView;
    private readonly Label _statusLabel;
    private readonly TextField _inputField;
    private readonly Button _sendBtn;

    private readonly ObservableCollection<string> _modelListItems = new();
    private readonly List<string> _modelPaths = new();

    private string? _selectedModelPath;
    private string? _selectedProjectRoot;
    private long _selectedProjectId;

    private LlmHost? _host;
    private AgentChatLoop? _loop;
    private SqliteStore? _db;
    private ChatSessionLogger? _logger;

    private enum Mode { Start, Downloading, Loading, Chat, TearingDown }
    private Mode _mode = Mode.Start;
    private volatile bool _busy;
    private CancellationTokenSource? _cts;
    private CancellationTokenSource? _downloadCts;

    public ChatView(Toplevel root, Action onExit)
    {
        _root = root;
        _onExit = onExit;

        _titleLabel = new Label
        {
            Text = "Chat (Gemma 4 on-device — CPU only)",
            X = 1, Y = 3,
            Visible = false,
        };

        _hintLabel = new Label
        {
            Text = "",
            X = 1, Y = 4,
            Width = Dim.Fill(2),
            Visible = false,
        };

        _modelList = new ListView
        {
            X = 1, Y = 6,
            Width = Dim.Fill(2),
            Height = 6,
            Visible = false,
        };
        _modelList.SetSource(_modelListItems);
        _modelList.OpenSelectedItem += OnModelPicked;

        _modelPathLabel = new Label
        {
            Text = "Model: (none selected)",
            X = 1, Y = 13,
            Width = Dim.Fill(2),
            Visible = false,
        };

        _projectPathLabel = new Label
        {
            Text = "Project context: (none — file paths will be absolute only)",
            X = 1, Y = 14,
            Width = Dim.Fill(2),
            Visible = false,
        };

        _pickProjectBtn = new Button
        {
            Text = "[Pick Project Context]",
            X = 1, Y = 16,
            Visible = false,
        };
        _pickProjectBtn.Accepting += (_, _) => PickProject();

        _startBtn = new Button
        {
            Text = ">>> Load Model & Start Chat <<<",
            X = 26, Y = 16,
            Visible = false,
        };
        _startBtn.Accepting += (_, _) => _ = StartChatAsync();

        _downloadBtn = new Button
        {
            Text = "[Download Default Model (Gemma 4 E4B, ~5GB)]",
            X = 1, Y = 18,
            Visible = false,
        };
        _downloadBtn.Accepting += (_, _) => _ = DownloadDefaultModelAsync();

        // Download screen widgets ------------------------------------------
        _downloadTitleLabel = new Label
        {
            Text = "Downloading default model…",
            X = 1, Y = 3,
            Width = Dim.Fill(2),
            Visible = false,
        };

        _downloadStatusLabel = new Label
        {
            Text = "",
            X = 1, Y = 5,
            Width = Dim.Fill(2),
            Height = 3,
            Visible = false,
        };

        _downloadBarLabel = new Label
        {
            Text = "",
            X = 1, Y = 9,
            Width = Dim.Fill(2),
            Visible = false,
        };

        _cancelDownloadBtn = new Button
        {
            Text = "[Cancel Download]",
            X = 1, Y = 11,
            Visible = false,
        };
        _cancelDownloadBtn.Accepting += (_, _) =>
        {
            try { _downloadCts?.Cancel(); } catch { }
        };

        // Chat screen widgets ----------------------------------------------
        _historyView = new TextView
        {
            X = 1, Y = 3,
            Width = Dim.Fill(2),
            Height = Dim.Fill(5),
            ReadOnly = true,
            WordWrap = true,
            Visible = false,
        };

        _statusLabel = new Label
        {
            Text = "ready",
            X = 1, Y = Pos.AnchorEnd(4),
            Width = Dim.Fill(2),
            Visible = false,
        };

        _inputField = new TextField
        {
            Text = "",
            X = 1, Y = Pos.AnchorEnd(3),
            Width = Dim.Fill(15),
            Visible = false,
        };

        _sendBtn = new Button
        {
            Text = "Send (Enter)",
            X = Pos.AnchorEnd(14), Y = Pos.AnchorEnd(3),
            Visible = false,
        };
        _sendBtn.Accepting += (_, _) => _ = SendAsync();

        // Enter inside the input field triggers send too — both the field's
        // Accepting event and a KeyDown fallback fire so we handle either
        // Terminal.Gui v2 binding path.
        _inputField.Accepting += (_, _) => _ = SendAsync();
        _inputField.KeyDown += (_, k) =>
        {
            if (k == Key.Enter)
            {
                k.Handled = true;
                _ = SendAsync();
            }
        };

        root.Add(_titleLabel, _hintLabel, _modelList, _modelPathLabel,
            _projectPathLabel, _pickProjectBtn, _startBtn, _downloadBtn,
            _downloadTitleLabel, _downloadStatusLabel, _downloadBarLabel, _cancelDownloadBtn,
            _historyView, _statusLabel, _inputField, _sendBtn);
    }

    // ------------------------------------------------------------------
    // Public surface called by MainView
    // ------------------------------------------------------------------
    public void Show()
    {
        _mode = Mode.Start;
        RefreshModels();
        ShowStart();
    }

    public void Hide()
    {
        foreach (var v in new View[]
                 { _titleLabel, _hintLabel, _modelList, _modelPathLabel,
                   _projectPathLabel, _pickProjectBtn, _startBtn, _downloadBtn,
                   _downloadTitleLabel, _downloadStatusLabel, _downloadBarLabel, _cancelDownloadBtn,
                   _historyView, _statusLabel, _inputField, _sendBtn })
        {
            v.Visible = false;
        }
    }

    /// <summary>True if the user is mid-chat (so HandleBack should not exit immediately).</summary>
    public bool IsActive => _mode != Mode.Start || _busy;

    /// <summary>True if the chat input field currently has keyboard focus. Used
    /// by MainView's global Q/H handler to let Korean ('ㅂ'/'ㅎ') keystrokes
    /// flow into the text field instead of triggering Back/Home.</summary>
    public bool IsInputFocused => _inputField.HasFocus;

    public bool HandleBack()
    {
        if (_mode == Mode.TearingDown)
            return true;  // already unloading — ignore extra Q presses

        if (_mode == Mode.Downloading)
        {
            // Q during download = cancel. The partial file stays on disk so a
            // later retry resumes from where we stopped.
            try { _downloadCts?.Cancel(); } catch { }
            return true;
        }

        // Download finished/cancelled with the post-download screen still up
        // (waiting for the user to read the success or error message). Swap
        // back to the model picker instead of bubbling Q to MainView.
        if (_mode == Mode.Start && _downloadTitleLabel.Visible)
        {
            _cancelDownloadBtn.Text = "[Cancel Download]";
            RefreshModels();
            ShowStart();
            return true;
        }

        if (_busy)
        {
            _cts?.Cancel();
            UpdateStatus("cancelling…");
            return true;  // swallow Back during in-flight inference
        }

        if (_mode == Mode.Chat)
        {
            // Teardown directly. Earlier builds showed a MessageBox.Query
            // confirmation here ("Unload" / "Stay"), but it broke in
            // Terminal.Gui v2 — pressing Enter on the focused "Unload"
            // button silently returned a non-zero index, so the popup
            // appeared and went away without ever invoking teardown. The
            // user explicitly pressed Q to exit chat; trust the intent.
            _mode = Mode.TearingDown;
            UpdateStatus("unloading model…");
            _ = TeardownAsync();
            return true;
        }
        return false;
    }

    public async ValueTask DisposeAsync() => await TeardownAsync();

    // ------------------------------------------------------------------
    // Start screen
    // ------------------------------------------------------------------
    private void ShowStart()
    {
        _titleLabel.Visible = true;
        _hintLabel.Visible = true;
        _modelList.Visible = true;
        _modelPathLabel.Visible = true;
        _projectPathLabel.Visible = true;
        _pickProjectBtn.Visible = true;
        _startBtn.Visible = true;
        // _downloadBtn visibility is decided by RefreshModels() based on
        // whether the default model is already present on disk.

        _downloadTitleLabel.Visible = false;
        _downloadStatusLabel.Visible = false;
        _downloadBarLabel.Visible = false;
        _cancelDownloadBtn.Visible = false;

        _historyView.Visible = false;
        _statusLabel.Visible = false;
        _inputField.Visible = false;
        _sendBtn.Visible = false;

        _modelList.SetFocus();
    }

    private void RefreshModels()
    {
        _modelListItems.Clear();
        _modelPaths.Clear();

        // 1) Catalog entries that already have a downloaded file on disk.
        foreach (var (entry, path) in ModelLocator.EnumerateAvailable())
        {
            _modelListItems.Add($"  {entry.DisplayName}");
            _modelPaths.Add(path);
        }

        // 2) Any other *.gguf files dropped into ~/.codescan/models/ manually.
        foreach (var extra in ModelLocator.EnumerateGgufFiles(ModelLocator.ModelsDir))
        {
            if (_modelPaths.Contains(extra)) continue;
            _modelListItems.Add($"  {Path.GetFileName(extra)}  (custom)");
            _modelPaths.Add(extra);
        }

        if (_modelListItems.Count == 0)
        {
            var defaultEntry = ChatModelCatalog.Default;
            _modelListItems.Add("  (no GGUF model found)");
            _modelPaths.Add("");
            _hintLabel.Text =
                $"No model found. Click the download button below to fetch '{defaultEntry.FileName}'\n" +
                $"to {ModelLocator.ModelsDir}, or drop a GGUF there manually.";
        }
        else
        {
            _hintLabel.Text =
                "Select a model with Enter, then click '>>> Load Model & Start Chat <<<'.\n" +
                "First load takes ~10–30s on CPU (~5 GB RAM for E4B).";
            // Default selection: first available.
            _selectedModelPath = _modelPaths[0];
            _modelPathLabel.Text = $"Model: {_selectedModelPath}";
        }

        // Surface the download button whenever the catalog default isn't
        // already on disk. We check FindModel (not just the local dir) so the
        // dev fallback path in ModelLocator counts as "present".
        _downloadBtn.Visible = ModelLocator.FindModel(ChatModelCatalog.Default) == null;

        _modelList.SetSource(_modelListItems);
        _modelList.SelectedItem = 0;
    }

    private void OnModelPicked(object? sender, ListViewItemEventArgs e)
    {
        var idx = e.Item;
        if (idx < 0 || idx >= _modelPaths.Count) return;
        var path = _modelPaths[idx];
        if (string.IsNullOrEmpty(path)) return;
        _selectedModelPath = path;
        _modelPathLabel.Text = $"Model: {_selectedModelPath}";
    }

    private void PickProject()
    {
        try
        {
            using var db = new SqliteStore(AppPaths.DbPath);
            var projects = db.GetProjects();
            if (projects.Count == 0)
            {
                MessageBox.Query("No projects",
                    "No indexed projects. Run 'codescan scan <path>' or use the Projects menu first.",
                    "OK");
                return;
            }

            var items = projects.Select(p => $"#{p.Id}  {p.RootPath}").ToArray();
            var pick = MessageBox.Query("Pick project context",
                "File paths in tool calls will resolve against this root.",
                items);
            if (pick < 0 || pick >= projects.Count) return;

            _selectedProjectId = projects[pick].Id;
            _selectedProjectRoot = projects[pick].RootPath;
            _projectPathLabel.Text =
                $"Project context: #{_selectedProjectId}  {_selectedProjectRoot}";
        }
        catch (Exception ex)
        {
            MessageBox.ErrorQuery("Error", $"Project pick failed: {ex.Message}", "OK");
        }
    }

    // ------------------------------------------------------------------
    // Download default model (Gemma 4 E4B GGUF)
    // ------------------------------------------------------------------
    private void ShowDownloading()
    {
        _titleLabel.Visible = false;
        _hintLabel.Visible = false;
        _modelList.Visible = false;
        _modelPathLabel.Visible = false;
        _projectPathLabel.Visible = false;
        _pickProjectBtn.Visible = false;
        _startBtn.Visible = false;
        _downloadBtn.Visible = false;

        _downloadTitleLabel.Visible = true;
        _downloadStatusLabel.Visible = true;
        _downloadBarLabel.Visible = true;
        _cancelDownloadBtn.Visible = true;

        _cancelDownloadBtn.SetFocus();
    }

    private async Task DownloadDefaultModelAsync()
    {
        if (_busy || _mode == Mode.Downloading) return;

        var entry = ChatModelCatalog.Default;
        var dest = ModelLocator.TargetPath(entry);

        // Sanity: if the model raced into existence (e.g. user dropped it in),
        // skip the download entirely.
        if (File.Exists(dest))
        {
            RefreshModels();
            return;
        }

        _busy = true;
        _mode = Mode.Downloading;
        _downloadCts = new CancellationTokenSource();

        ShowDownloading();
        _downloadTitleLabel.Text = $"Downloading {entry.DisplayName}";
        UpdateDownloadStatus(
            $"Source: {entry.DownloadUrl}\n" +
            $"Target: {dest}\n" +
            "Preparing…");
        _downloadBarLabel.Text = "";

        var startedAt = DateTime.UtcNow;
        var progress = new Progress<ModelDownloadProgress>(p =>
        {
            var pct = p.TotalBytes > 0 ? (double)p.BytesReceived / p.TotalBytes : 0.0;
            var bar = RenderBar(pct, 40);
            var eta = EstimateEta(p, startedAt);
            UpdateDownloadStatus(
                $"Source: {entry.DownloadUrl}\n" +
                $"Target: {dest}\n" +
                $"{FormatSize(p.BytesReceived)} / {FormatSize(p.TotalBytes)}  " +
                $"({pct * 100:F1}%)  speed: {FormatSpeed(p.BytesPerSecond)}  ETA: {eta}");
            UpdateDownloadBar($"  {bar}");
        });

        try
        {
            await Task.Run(() => ModelDownloader.DownloadAsync(
                entry.DownloadUrl, dest, progress, _downloadCts.Token));

            UpdateDownloadStatus(
                $"Downloaded to:\n  {dest}\n" +
                "Returning to model selection…");
            await Task.Delay(700);
            _mode = Mode.Start;
            RefreshModels();
            ShowStart();
        }
        catch (OperationCanceledException)
        {
            UpdateDownloadStatus(
                "Cancelled. A partial .part file was kept on disk so the next\n" +
                "download attempt will resume from where you stopped.\n" +
                "Press Q to go back.");
            _mode = Mode.Start;
            // Leave the download screen up until the user presses Q so they
            // can read the message; HandleBack will pop back to Start.
            _cancelDownloadBtn.Text = "[Back]";
        }
        catch (Exception ex)
        {
            UpdateDownloadStatus($"Download failed: {ex.Message}\nPress Q to go back.");
            _mode = Mode.Start;
            _cancelDownloadBtn.Text = "[Back]";
        }
        finally
        {
            _busy = false;
            _downloadCts?.Dispose();
            _downloadCts = null;
        }
    }

    private void UpdateDownloadStatus(string text)
    {
        Application.Invoke(() =>
        {
            try { _downloadStatusLabel.Text = text; }
            catch { /* ignore */ }
        });
    }

    private void UpdateDownloadBar(string text)
    {
        Application.Invoke(() =>
        {
            try { _downloadBarLabel.Text = text; }
            catch { /* ignore */ }
        });
    }

    private static string RenderBar(double fraction, int width)
    {
        var clamped = Math.Clamp(fraction, 0.0, 1.0);
        var filled = (int)Math.Round(clamped * width);
        return "[" + new string('#', filled) + new string('.', width - filled) + "]";
    }

    private static string FormatSize(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        double v = bytes;
        string[] units = { "KB", "MB", "GB", "TB" };
        int idx = -1;
        do { v /= 1024.0; idx++; } while (v >= 1024 && idx < units.Length - 1);
        return $"{v:F2} {units[idx]}";
    }

    private static string FormatSpeed(double bytesPerSecond)
    {
        if (bytesPerSecond <= 0) return "—";
        return $"{FormatSize((long)bytesPerSecond)}/s";
    }

    private static string EstimateEta(ModelDownloadProgress p, DateTime startedAt)
    {
        if (p.BytesPerSecond <= 0 || p.TotalBytes <= 0) return "—";
        var remaining = p.TotalBytes - p.BytesReceived;
        if (remaining <= 0) return "0s";
        var seconds = remaining / p.BytesPerSecond;
        if (seconds < 60) return $"{seconds:F0}s";
        if (seconds < 3600) return $"{seconds / 60:F0}m {seconds % 60:F0}s";
        return $"{seconds / 3600:F0}h {(seconds % 3600) / 60:F0}m";
    }

    // ------------------------------------------------------------------
    // Loading + chat
    // ------------------------------------------------------------------
    private async Task StartChatAsync()
    {
        if (string.IsNullOrEmpty(_selectedModelPath) || !File.Exists(_selectedModelPath))
        {
            MessageBox.ErrorQuery("Model not found",
                "Pick a valid GGUF model first, or download one to ~/.codescan/models/.",
                "OK");
            return;
        }
        if (_busy) return;
        _busy = true;
        _mode = Mode.Loading;

        // Switch UI immediately so the user sees status.
        ShowChat();
        _historyView.Text = "";
        AppendHistory($"Loading model: {Path.GetFileName(_selectedModelPath)} …\n");
        UpdateStatus("loading model (CPU init, can take 10–30s)…");

        try
        {
            _db = new SqliteStore(AppPaths.DbPath);
            _logger = ChatSessionLogger.Create(_selectedModelPath, _selectedProjectRoot);

            // Wire the load-progress callback so the user sees movement
            // during the 10–30s GGUF mmap + tensor decode on CPU.
            var progress = new Progress<float>(pct =>
                UpdateStatus($"loading model… {pct * 100:F0}%"));

            _host = await Task.Run(() =>
                LlmHost.LoadAsync(_selectedModelPath, contextSize: 4096, progress: progress));
            _loop = new AgentChatLoop(
                _host,
                new CodeScanToolbelt(_db, _selectedProjectRoot),
                projectRoot: _selectedProjectRoot,
                logger: _logger,
                maxIterations: 6);

            AppendHistory(
                $"Model loaded.\n" +
                (_selectedProjectRoot != null
                    ? $"Project context: #{_selectedProjectId} {_selectedProjectRoot}\n"
                    : "Project context: (none — only absolute paths can be read)\n") +
                $"Chat log: {_logger.LogPath}\n" +
                "Ask me anything about this codebase. Type a question and press Enter.\n\n");
            UpdateStatus("ready");
            _mode = Mode.Chat;
            _inputField.Text = "";
            _inputField.SetFocus();
        }
        catch (Exception ex)
        {
            AppendHistory($"\nModel load failed: {ex.Message}\n");
            UpdateStatus("load failed — press Q to go back");
            _mode = Mode.Start;
        }
        finally
        {
            _busy = false;
        }
    }

    private void ShowChat()
    {
        _titleLabel.Visible = false;
        _hintLabel.Visible = false;
        _modelList.Visible = false;
        _modelPathLabel.Visible = false;
        _projectPathLabel.Visible = false;
        _pickProjectBtn.Visible = false;
        _startBtn.Visible = false;

        _historyView.Visible = true;
        _statusLabel.Visible = true;
        _inputField.Visible = true;
        _sendBtn.Visible = true;
    }

    private async Task SendAsync()
    {
        if (_busy || _loop == null) return;
        var text = _inputField.Text?.ToString()?.Trim() ?? "";
        if (string.IsNullOrEmpty(text)) return;

        _busy = true;
        _cts = new CancellationTokenSource();
        AppendHistory($"\n[You] {text}\n");
        _inputField.Text = "";
        UpdateStatus("thinking…");

        try
        {
            await foreach (var update in _loop.SendAsync(text, _cts.Token))
            {
                switch (update.Phase)
                {
                    case "thinking":
                        UpdateStatus(update.Text);
                        break;
                    case "progress":
                        // Token-count heartbeats — status-line only so the
                        // transcript stays readable.
                        UpdateStatus(update.Text);
                        break;
                    case "raw":
                        // Raw model JSON is captured in the disk log; we don't
                        // surface it in the transcript to keep the UI clean.
                        break;
                    case "tool":
                        AppendHistory($"  → {update.Text}\n");
                        UpdateStatus($"running tool: {update.Text}");
                        break;
                    case "tool_result":
                        AppendHistory($"  ← {update.Text}\n");
                        UpdateStatus("reasoning…");
                        break;
                    case "done":
                        AppendHistory($"\n[Gemma] {update.Text}\n");
                        UpdateStatus("ready");
                        break;
                    case "error":
                        AppendHistory($"\n[error] {update.Text}\n");
                        UpdateStatus("error — see log above");
                        break;
                }
            }
        }
        catch (OperationCanceledException)
        {
            AppendHistory("\n[cancelled]\n");
            UpdateStatus("ready");
        }
        catch (Exception ex)
        {
            AppendHistory($"\n[error] {ex.Message}\n");
            UpdateStatus("error");
        }
        finally
        {
            _busy = false;
            _cts?.Dispose();
            _cts = null;
            _inputField.SetFocus();
        }
    }

    private async Task TeardownAsync()
    {
        try { _cts?.Cancel(); } catch { }
        if (_loop != null) { await _loop.DisposeAsync(); _loop = null; }
        if (_host != null) { await _host.DisposeAsync(); _host = null; }
        _db?.Dispose();
        _db = null;
        _logger?.Dispose();
        _logger = null;
        _mode = Mode.Start;
        _busy = false;
        Application.Invoke(() =>
        {
            Hide();
            _onExit();
        });
    }

    // ------------------------------------------------------------------
    // UI helpers (must marshal to UI thread)
    // ------------------------------------------------------------------
    private void AppendHistory(string text)
    {
        Application.Invoke(() =>
        {
            try
            {
                _historyView.Text += text;
                _historyView.MoveEnd();
            }
            catch { /* UI update failed, ignore */ }
        });
    }

    private void UpdateStatus(string text)
    {
        Application.Invoke(() =>
        {
            try { _statusLabel.Text = $"[status] {text}"; }
            catch { /* ignore */ }
        });
    }
}

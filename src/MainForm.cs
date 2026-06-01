namespace ThumbnailPrimer;

/// <summary>
/// Main application window. Lets the user pick a folder, configure options,
/// and start priming the Windows Explorer thumbnail cache.
/// </summary>
public sealed class MainForm : Form
{
    private readonly TextBox _folderBox;
    private readonly Button _browseBtn;
    private readonly CheckBox _recurseCheck;
    private readonly ComboBox _sizeCombo;
    private readonly Button _primeBtn;
    private readonly Button _cancelBtn;
    private readonly ProgressBar _progressBar;
    private readonly Label _statusLabel;
    private readonly TextBox _logBox;

    private CancellationTokenSource? _cts;

    private static readonly (string Label, uint Value)[] SizeOptions =
    [
        ("96 px  — small icons", 96),
        ("256 px — large icons (recommended)", 256),
        ("1024 px — extra-large icons", 1024),
    ];

    public MainForm()
    {
        Text = "Thumbnail Cache Primer";
        Size = new Size(640, 460);
        MinimumSize = new Size(520, 400);
        StartPosition = FormStartPosition.CenterScreen;
        AutoScaleMode = AutoScaleMode.Font;
        Font = new Font("Segoe UI", 9f);

        // ── folder row: Label | TextBox (fill) | Browse button ──────────────
        var folderRow = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            RowCount = 1,
            AutoSize = true,
            Margin = Padding.Empty,
        };
        folderRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        folderRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        folderRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        _folderBox = new TextBox
        {
            ReadOnly = true,
            Dock = DockStyle.Fill,
            PlaceholderText = "Select a folder to prime…",
            Margin = new Padding(6, 0, 6, 0),
        };
        _browseBtn = new Button { Text = "Browse…", AutoSize = true };
        _browseBtn.Click += OnBrowse;

        folderRow.Controls.Add(new Label { Text = "Folder:", AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 3, 0, 0) }, 0, 0);
        folderRow.Controls.Add(_folderBox, 1, 0);
        folderRow.Controls.Add(_browseBtn, 2, 0);

        // ── options row ──────────────────────────────────────────────────────
        _recurseCheck = new CheckBox { Text = "Include subfolders", AutoSize = true, Margin = new Padding(0, 0, 16, 0) };
        _sizeCombo = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 230 };
        foreach (var (lbl, _) in SizeOptions) _sizeCombo.Items.Add(lbl);
        _sizeCombo.SelectedIndex = 1;

        var optionsFlow = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            WrapContents = false,
            Margin = new Padding(0, 8, 0, 0),
        };
        optionsFlow.Controls.AddRange(new Control[]
        {
            _recurseCheck,
            new Label { Text = "Size:", AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 3, 6, 0) },
            _sizeCombo,
        });

        // ── buttons row ──────────────────────────────────────────────────────
        _primeBtn = new Button { Text = "Prime Cache", AutoSize = true, Enabled = false };
        _primeBtn.Click += OnPrime;
        _cancelBtn = new Button { Text = "Cancel", AutoSize = true, Enabled = false };
        _cancelBtn.Click += OnCancel;

        var buttonsFlow = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            WrapContents = false,
            Margin = new Padding(0, 8, 0, 0),
        };
        buttonsFlow.Controls.AddRange(new Control[] { _primeBtn, _cancelBtn });

        // ── progress row ─────────────────────────────────────────────────────
        _progressBar = new ProgressBar { Dock = DockStyle.Fill };
        _statusLabel = new Label
        {
            Text = "Ready — select a folder and click Prime Cache.",
            AutoSize = true,
            ForeColor = SystemColors.GrayText,
            Margin = new Padding(0, 3, 0, 0),
        };

        var progressLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            AutoSize = true,
            Margin = new Padding(0, 10, 0, 0),
        };
        progressLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        progressLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        progressLayout.Controls.Add(_progressBar, 0, 0);
        progressLayout.Controls.Add(_statusLabel, 0, 1);

        // ── log row ───────────────────────────────────────────────────────────
        _logBox = new TextBox
        {
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Vertical,
            Dock = DockStyle.Fill,
            Font = new Font("Consolas", 8.5f),
            BackColor = SystemColors.Window,
        };

        var logLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Margin = new Padding(0, 8, 0, 0),
        };
        logLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        logLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        logLayout.Controls.Add(new Label { Text = "Skipped files:", AutoSize = true }, 0, 0);
        logLayout.Controls.Add(_logBox, 0, 1);

        // ── main layout ───────────────────────────────────────────────────────
        var main = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 5,
            Padding = new Padding(12),
        };
        main.RowStyles.Add(new RowStyle(SizeType.AutoSize));     // folder
        main.RowStyles.Add(new RowStyle(SizeType.AutoSize));     // options
        main.RowStyles.Add(new RowStyle(SizeType.AutoSize));     // buttons
        main.RowStyles.Add(new RowStyle(SizeType.AutoSize));     // progress
        main.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); // log

        main.Controls.Add(folderRow, 0, 0);
        main.Controls.Add(optionsFlow, 0, 1);
        main.Controls.Add(buttonsFlow, 0, 2);
        main.Controls.Add(progressLayout, 0, 3);
        main.Controls.Add(logLayout, 0, 4);

        Controls.Add(main);
        FormClosing += OnFormClosing;
    }

    // ── event handlers ────────────────────────────────────────────────────────

    private void OnBrowse(object? sender, EventArgs e)
    {
        using var dlg = new FolderBrowserDialog
        {
            Description = "Select folder to pre-warm thumbnails for",
            UseDescriptionForTitle = true,
            ShowNewFolderButton = false,
            InitialDirectory = _folderBox.Text.Length > 0 ? _folderBox.Text : string.Empty,
        };

        if (dlg.ShowDialog(this) == DialogResult.OK)
        {
            _folderBox.Text = dlg.SelectedPath;
            _primeBtn.Enabled = true;
        }
    }

    private async void OnPrime(object? sender, EventArgs e)
    {
        string folder = _folderBox.Text;
        if (!Directory.Exists(folder))
        {
            MessageBox.Show("Selected folder does not exist.", "Invalid Folder",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        _logBox.Clear();
        _cts = new CancellationTokenSource();
        SetRunningState(true);

        int sizeIdx = _sizeCombo.SelectedIndex >= 0 && _sizeCombo.SelectedIndex < SizeOptions.Length
            ? _sizeCombo.SelectedIndex : 1;
        uint size = SizeOptions[sizeIdx].Value;
        bool recurse = _recurseCheck.Checked;
        int lastTotal = 0;

        var progress = new Progress<PrimeProgress>(p =>
        {
            if (!IsHandleCreated || IsDisposed) return;

            lastTotal = p.Total;
            _progressBar.Maximum = Math.Max(p.Total, 1);
            _progressBar.Value = Math.Min(p.Done, _progressBar.Maximum);

            string skipped = p.ErrorCount > 0 ? $" — {p.ErrorCount:N0} skipped" : string.Empty;
            string current = p.LastFile.Length > 0 ? $"  ({p.LastFile})" : string.Empty;
            _statusLabel.Text = $"Primed {p.Done:N0} / {p.Total:N0}{skipped}{current}";
            _statusLabel.ForeColor = SystemColors.ControlText;

            if (p.SkippedFile is { } sf)
                AppendLog(sf);
        });

        try
        {
            bool? cacheVerified = await ThumbnailCachePrimer.PrimeAsync(folder, recurse, size, progress, _cts.Token);

            if (lastTotal == 0)
                _statusLabel.Text = "No image files found in the selected folder.";
            else if (cacheVerified == false)
                _statusLabel.Text = $"Done — {lastTotal:N0} primed. " +
                    "Warning: cache write could not be verified — Explorer may still regenerate thumbnails.";
            else
                _statusLabel.Text = $"Done — {lastTotal:N0} thumbnail(s) primed. " +
                    "Open folder in Explorer (Extra Large Icons) to confirm.";
        }
        catch (OperationCanceledException)
        {
            _statusLabel.Text = "Cancelled.";
        }
        catch (Exception ex)
        {
            _statusLabel.Text = $"Error: {ex.Message}";
            AppendLog($"[Error] {ex.Message}");
        }
        finally
        {
            var cts = Interlocked.Exchange(ref _cts, null);
            cts?.Dispose();
            if (IsHandleCreated && !IsDisposed)
                SetRunningState(false);
        }
    }

    private void OnCancel(object? sender, EventArgs e) => _cts?.Cancel();

    private void OnFormClosing(object? sender, FormClosingEventArgs e) => _cts?.Cancel();

    // ── helpers ───────────────────────────────────────────────────────────────

    private void SetRunningState(bool running)
    {
        _primeBtn.Enabled = !running;
        _cancelBtn.Enabled = running;
        _browseBtn.Enabled = !running;
        _recurseCheck.Enabled = !running;
        _sizeCombo.Enabled = !running;
    }

    private void AppendLog(string line)
    {
        if (_logBox.TextLength > 0) _logBox.AppendText(Environment.NewLine);
        _logBox.AppendText(line);
    }
}

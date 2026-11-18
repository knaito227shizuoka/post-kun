namespace PostKun;

/// <summary>
/// メインウィンドウ。左に保存済みリクエストの一覧(名前順)、右上に編集欄、右下にレスポンス。
/// 保存済みリクエストは <c>%APPDATA%\post-kun\requests.json</c>、ウィンドウ状態は settings.json に保存する。
/// </summary>
public class MainForm : Form
{
    private readonly Storage _storage = new();
    private readonly HttpSender _sender = new();
    private readonly List<RequestDef> _requests;
    private readonly AppSettings _settings;

    private readonly SplitContainer _mainSplit = new();
    private readonly SplitContainer _rightSplit = new();
    private readonly ListBox _list = new();
    private readonly Button _new = new();
    private readonly Button _save = new();
    private readonly Button _delete = new();
    private readonly RequestEditor _editor = new();
    private readonly ResponseViewer _response = new();
    private readonly ContextMenuStrip _listMenu = new();

    /// <summary>編集欄に読み込んでいる保存済みリクエストの名前(未保存の新規なら null)。</summary>
    private string? _currentName;
    /// <summary>読み込み・保存した時点の内容。これと違えば「未保存」。</summary>
    private string _snapshot = "";
    /// <summary>右クリックメニューの対象。</summary>
    private RequestDef? _menuTarget;
    private bool _ignoreSelection;
    private CancellationTokenSource? _cts;

    /// <summary>埋め込みリソースの post-kun.ico を読む(全サイズ入り。読めなければ null)。</summary>
    private static Icon? LoadAppIcon()
    {
        using var stream = typeof(MainForm).Assembly.GetManifestResourceStream("PostKun.post-kun.ico");
        return stream == null ? null : new Icon(stream);
    }

    public MainForm()
    {
        Text          = Program.WindowTitle;   // 2個目の起動時に FindWindow で探すので変えない
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize    = new Size(1180, 780);
        MinimumSize   = new Size(760, 480);
        StartPosition = FormStartPosition.CenterScreen;
        KeyPreview    = true;
        Font          = new Font("Yu Gothic UI", 9.75f);
        Icon          = LoadAppIcon() ?? Icon;   // タイトルバー・タスクバーのアイコン(exe のアイコンは csproj の ApplicationIcon)

        _requests = _storage.LoadRequests();
        _settings = _storage.Load<AppSettings>(Storage.SettingsFile);

        BuildLayout();
        RestoreWindowBounds();
        RefreshList(null);

        _editor.Changed += (_, _) => UpdateDirty();
        _editor.SendClicked += async (_, _) => await SendAsync();
        KeyDown += OnFormKeyDown;
        Load += (_, _) => OnLoaded();
        Shown += (_, _) => RefreshList(_currentName); // 表示時のハンドル作成で選択が外れても戻す
        FormClosing += OnFormClosing;
    }

    // ── レイアウト ──

    private void BuildLayout()
    {
        _new.Text = "新規";
        _save.Text = "保存";
        _delete.Text = "削除";
        foreach (var b in new[] { _new, _save, _delete })
        {
            b.AutoSize = true;
            b.Margin = new Padding(0, 0, 4, 0);
        }
        _new.Click += (_, _) => NewRequest();
        _save.Click += (_, _) => TrySave();
        _delete.Click += (_, _) => DeleteRequest(_list.SelectedItem as RequestDef);
        var tip = new ToolTip();
        tip.SetToolTip(_new, "新しいリクエスト (Ctrl+N)");
        tip.SetToolTip(_save, "保存 (Ctrl+S)");

        var buttons = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, WrapContents = false, Padding = new Padding(6, 8, 4, 6) };
        buttons.Controls.AddRange(new Control[] { _new, _save, _delete });

        _list.Dock = DockStyle.Fill;
        _list.BorderStyle = BorderStyle.None;
        _list.DrawMode = DrawMode.OwnerDrawFixed;
        // ハンドル作成後に ItemHeight を変えるとハンドルが作り直されて選択が消えるので、ここで決める
        _list.ItemHeight = (int)Math.Round(28 * DeviceDpi / 96.0);
        _list.IntegralHeight = false;
        _list.DrawItem += DrawListItem;
        _list.SelectedIndexChanged += OnListSelectionChanged;
        _list.MouseDown += (_, e) =>
        {
            var index = _list.IndexFromPoint(e.Location);
            _menuTarget = index >= 0 ? (RequestDef)_list.Items[index] : null;
        };
        _listMenu.Items.Add("複製", null, (_, _) => DuplicateRequest(_menuTarget));
        _listMenu.Items.Add("削除", null, (_, _) => DeleteRequest(_menuTarget));
        _listMenu.Opening += (_, e) => e.Cancel = _menuTarget == null;
        _list.ContextMenuStrip = _listMenu;

        var listHost = new Panel { Dock = DockStyle.Fill, Padding = new Padding(4, 0, 0, 4) };
        listHost.Controls.Add(_list);
        _mainSplit.Panel1.Controls.Add(listHost);
        _mainSplit.Panel1.Controls.Add(buttons);
        _mainSplit.Panel1.BackColor = SystemColors.Window;

        _editor.Dock = DockStyle.Fill;
        _response.Dock = DockStyle.Fill;
        _rightSplit.Orientation = Orientation.Horizontal;
        _rightSplit.Dock = DockStyle.Fill;
        _rightSplit.SplitterWidth = 6;
        _rightSplit.Panel1.Controls.Add(_editor);
        _rightSplit.Panel2.Controls.Add(_response);

        _mainSplit.Dock = DockStyle.Fill;
        _mainSplit.SplitterWidth = 5;
        _mainSplit.FixedPanel = FixedPanel.Panel1;  // ウィンドウを広げたら右側が広がる
        _mainSplit.Panel2.Controls.Add(_rightSplit);
        Controls.Add(_mainSplit);
    }

    private void OnLoaded()
    {
        // SplitterDistance はサイズ確定後に設定する
        var scale = DeviceDpi / 96.0;
        TrySetSplitter(_mainSplit, _settings.LeftPaneWidth ?? (int)(240 * scale));
        TrySetSplitter(_rightSplit, _settings.EditorHeight ?? (int)(_rightSplit.Height * 0.55));

        var last = _requests.FirstOrDefault(r => r.Name == _settings.LastSelected);
        if (last != null) LoadIntoEditor(last);
        else LoadIntoEditor(null);
        _editor.FocusUrl();
    }

    private static void TrySetSplitter(SplitContainer split, int distance)
    {
        var max = (split.Orientation == Orientation.Vertical ? split.Width : split.Height) - split.Panel2MinSize - split.SplitterWidth;
        if (max <= split.Panel1MinSize) return;
        split.SplitterDistance = Math.Clamp(distance, split.Panel1MinSize, max);
    }

    private static readonly Dictionary<string, Color> MethodColors = new()
    {
        ["GET"] = Color.FromArgb(0, 128, 72),
        ["POST"] = Color.FromArgb(204, 112, 0),
        ["PUT"] = Color.FromArgb(0, 92, 197),
        ["PATCH"] = Color.FromArgb(128, 64, 176),
        ["DELETE"] = Color.FromArgb(200, 40, 40),
    };

    private static readonly Font MethodFont = new("Consolas", 8.5f, FontStyle.Bold);
    /// <summary>一覧のメソッド列の幅(px)。0 なら次の描画で測り直す(一覧の作り直し時にリセット)。</summary>
    private int _methodWidth;

    private void DrawListItem(object? sender, DrawItemEventArgs e)
    {
        if (e.Index < 0) return;
        var def = (RequestDef)_list.Items[e.Index];
        var selected = (e.State & DrawItemState.Selected) != 0;
        using (var bg = new SolidBrush(selected ? Color.FromArgb(204, 228, 247) : SystemColors.Window))
            e.Graphics.FillRectangle(bg, e.Bounds);

        var scale = DeviceDpi / 96.0;
        var pad = (int)(6 * scale);
        if (_methodWidth == 0)
        {
            // メソッド列の幅 = 一覧にある最も長いメソッド名 + 少しの間隔(名前の開始位置はそろえる)
            var widest = _list.Items.Cast<RequestDef>().Select(r => r.Method).DefaultIfEmpty("GET")
                .Max(m => TextRenderer.MeasureText(e.Graphics, m, MethodFont, Size.Empty, TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix).Width);
            _methodWidth = widest + (int)(8 * scale);
        }
        var methodRect = new Rectangle(e.Bounds.X + pad, e.Bounds.Y, _methodWidth, e.Bounds.Height);
        var nameRect = new Rectangle(methodRect.Right, e.Bounds.Y, e.Bounds.Width - methodRect.Right - pad, e.Bounds.Height);
        var color = MethodColors.TryGetValue(def.Method, out var c) ? c : Color.DimGray;
        const TextFormatFlags flags = TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix;
        TextRenderer.DrawText(e.Graphics, def.Method, MethodFont, methodRect, color, flags | TextFormatFlags.NoPadding);
        TextRenderer.DrawText(e.Graphics, def.Name, _list.Font, nameRect, SystemColors.ControlText, flags);
    }

    // ── 一覧と編集欄 ──

    /// <summary>一覧を名前順で作り直し、<paramref name="selectName"/> を選択する(イベントは発火させない)。</summary>
    private void RefreshList(string? selectName)
    {
        _ignoreSelection = true;
        _list.BeginUpdate();
        _list.Items.Clear();
        _requests.Sort(RequestDef.ByName);
        foreach (var r in _requests) _list.Items.Add(r);
        _methodWidth = 0;  // メソッドの種類が変わったかもしれないので測り直す
        _list.SelectedIndex = selectName == null ? -1 : _requests.FindIndex(r => r.Name == selectName);
        _list.EndUpdate();
        _ignoreSelection = false;
        _delete.Enabled = _list.SelectedIndex >= 0;
    }

    private void LoadIntoEditor(RequestDef? def)
    {
        _editor.SetRequest(def == null ? new RequestDef() : Storage.Clone(def));
        _currentName = def?.Name;
        _snapshot = Storage.Serialize(_editor.GetRequest());
        RefreshList(_currentName);
        UpdateDirty();
    }

    private bool IsDirty => Storage.Serialize(_editor.GetRequest(commit: false)) != _snapshot;

    private void UpdateDirty() => _editor.SetDirty(IsDirty);

    private void OnListSelectionChanged(object? sender, EventArgs e)
    {
        if (_ignoreSelection) return;
        if (_list.SelectedItem is not RequestDef target || target.Name == _currentName) return;
        if (!ConfirmLeave())
        {
            RefreshList(_currentName); // 選択を元に戻す
            return;
        }
        LoadIntoEditor(_requests.FirstOrDefault(r => r.Name == target.Name));
    }

    /// <summary>未保存の変更があれば保存するか尋ねる。続けてよければ true。</summary>
    private bool ConfirmLeave()
    {
        if (!IsDirty) return true;
        var label = string.IsNullOrEmpty(_currentName) ? "新しいリクエスト" : $"「{_currentName}」";
        var answer = MessageBox.Show(this, $"{label}の変更を保存しますか?", Program.WindowTitle,
            MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
        return answer switch
        {
            DialogResult.Yes => TrySave(),
            DialogResult.No => true,
            _ => false,
        };
    }

    private void NewRequest()
    {
        if (!ConfirmLeave()) return;
        // 「無名のリクエスト」(重複時は「… 2」)として一覧に追加し、すぐ保存する。名前は編集欄で変えて保存し直す。
        var def = new RequestDef { Name = RequestDef.MakeUniqueName(RequestDef.UntitledName, _requests.Select(r => r.Name)) };
        _requests.Add(def);
        if (!TryWriteRequests())
        {
            _requests.Remove(def);
            return;
        }
        LoadIntoEditor(def);
        _editor.FocusName();
    }

    private bool TrySave()
    {
        var def = _editor.GetRequest();
        if (def.Name.Length == 0)
        {
            MessageBox.Show(this, "名前を入力してください。", Program.WindowTitle, MessageBoxButtons.OK, MessageBoxIcon.Information);
            _editor.FocusName();
            return false;
        }
        if (_requests.Any(r => string.Equals(r.Name, def.Name, StringComparison.OrdinalIgnoreCase) && r.Name != _currentName))
        {
            MessageBox.Show(this, $"「{def.Name}」という名前はすでにあります。別の名前にしてください。", Program.WindowTitle, MessageBoxButtons.OK, MessageBoxIcon.Information);
            _editor.FocusName();
            return false;
        }

        var backup = _requests.ToList();
        _requests.RemoveAll(r => r.Name == _currentName);
        _requests.Add(def);
        if (!TryWriteRequests())
        {
            _requests.Clear();
            _requests.AddRange(backup);
            return false;
        }
        _currentName = def.Name;
        _snapshot = Storage.Serialize(def);
        RefreshList(_currentName);
        UpdateDirty();
        return true;
    }

    private void DeleteRequest(RequestDef? target)
    {
        if (target == null) return;
        if (MessageBox.Show(this, $"「{target.Name}」を削除しますか?", Program.WindowTitle,
                MessageBoxButtons.OKCancel, MessageBoxIcon.Warning) != DialogResult.OK)
            return;
        _requests.Remove(target);
        if (!TryWriteRequests())
        {
            _requests.Add(target);
            return;
        }
        if (target.Name == _currentName)
        {
            // 編集中のものを消したら、一覧の先頭(名前順)を開く。1件もなければ空の新規。
            // 消した内容を「未保存」で残すと、切替・終了時の「保存しますか?」で「はい」を押したときに復活してしまう。
            _requests.Sort(RequestDef.ByName);
            LoadIntoEditor(_requests.FirstOrDefault());
            return;
        }
        RefreshList(_currentName);
        UpdateDirty();
    }

    private void DuplicateRequest(RequestDef? source)
    {
        if (source == null) return;
        var copy = Storage.Clone(source);
        copy.Name = RequestDef.MakeCopyName(source.Name, _requests.Select(r => r.Name));
        _requests.Add(copy);
        if (!TryWriteRequests())
        {
            _requests.Remove(copy);
            return;
        }
        RefreshList(_currentName);
    }

    private bool TryWriteRequests()
    {
        try
        {
            _storage.SaveRequests(_requests);
            return true;
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"保存できませんでした。\r\n{ex.Message}", Program.WindowTitle, MessageBoxButtons.OK, MessageBoxIcon.Error);
            return false;
        }
    }

    // ── 送信 ──

    private async Task SendAsync()
    {
        if (_cts != null)
        {
            _cts.Cancel();  // 送信中ならキャンセル
            return;
        }
        var def = _editor.GetRequest();
        if (def.Url.Length == 0)
        {
            _response.ShowError("URL を入力してください。");
            _editor.FocusUrl();
            return;
        }

        HttpRequestMessage message;
        try
        {
            message = RequestBuilder.BuildMessage(def, Array.Empty<KeyValueItem>(), out var missing);
            if (missing.Count > 0 &&
                MessageBox.Show(this, $"未定義の変数があります: {string.Join(", ", missing.Select(m => "{{" + m + "}}"))}\r\nそのまま送信しますか?",
                    Program.WindowTitle, MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK)
            {
                message.Dispose();
                return;
            }
        }
        catch (RequestBuildException ex)
        {
            _response.ShowError(ex.Message);
            return;
        }
        catch (Exception ex)
        {
            _response.ShowError(HttpSender.DescribeError(ex));
            return;
        }

        _cts = new CancellationTokenSource();
        _editor.SetSending(true);
        _response.ShowSending();
        try
        {
            using (message)
                _response.ShowResponse(await _sender.SendAsync(message, _cts.Token));
        }
        catch (Exception ex)
        {
            _response.ShowError(HttpSender.DescribeError(ex));
        }
        finally
        {
            _cts.Dispose();
            _cts = null;
            if (!IsDisposed) _editor.SetSending(false);
        }
    }

    // ── キー操作 ──

    private void OnFormKeyDown(object? sender, KeyEventArgs e)
    {
        switch (e.KeyData)
        {
            case Keys.Control | Keys.Enter:
                e.SuppressKeyPress = true;
                _ = SendAsync();
                break;
            case Keys.Control | Keys.S:
                e.SuppressKeyPress = true;
                TrySave();
                break;
            case Keys.Control | Keys.N:
                e.SuppressKeyPress = true;
                NewRequest();
                break;
            case Keys.Control | Keys.L:
                e.SuppressKeyPress = true;
                _editor.FocusUrl();
                break;
        }
    }

    // ── ウィンドウ状態の保存・復元 ──

    private void RestoreWindowBounds()
    {
        if (_settings is not { WindowX: { } x, WindowY: { } y, WindowWidth: { } w, WindowHeight: { } h }) return;
        var rect = new Rectangle(x, y, w, h);
        // どの画面にも十分見えていなければ既定位置のまま(モニタを外したときなど)
        if (!Screen.AllScreens.Any(s => Rectangle.Intersect(s.WorkingArea, rect) is { Width: > 100, Height: > 100 })) return;
        StartPosition = FormStartPosition.Manual;
        Bounds = rect;
        if (_settings.Maximized) WindowState = FormWindowState.Maximized;
    }

    private void OnFormClosing(object? sender, FormClosingEventArgs e)
    {
        if (!ConfirmLeave())
        {
            e.Cancel = true;
            return;
        }
        _cts?.Cancel();

        var bounds = WindowState == FormWindowState.Normal ? Bounds : RestoreBounds;
        _settings.WindowX = bounds.X;
        _settings.WindowY = bounds.Y;
        _settings.WindowWidth = bounds.Width;
        _settings.WindowHeight = bounds.Height;
        _settings.Maximized = WindowState == FormWindowState.Maximized;
        _settings.LeftPaneWidth = _mainSplit.SplitterDistance;
        _settings.EditorHeight = _rightSplit.SplitterDistance;
        _settings.LastSelected = _currentName;
        try { _storage.Save(Storage.SettingsFile, _settings); }
        catch { /* ウィンドウ状態の保存失敗で終了を妨げない */ }
        _sender.Dispose();
    }
}

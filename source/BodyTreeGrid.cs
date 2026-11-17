namespace PostKun;

/// <summary>
/// ボディ用のツリー型の表。有効 / キー / 型 / 値 の列を持ち、オブジェクト・配列の行は子要素を持てる。
/// モデル(<see cref="BodyField"/> のツリー)が正で、表はそれを平坦化して字下げ表示したもの。
/// セルの編集はその場でノードへ書き戻し、構造の変更(追加・移動・削除・型変更)のたびに表を作り直す。
/// </summary>
public sealed class BodyTreeGrid : UserControl
{
    private const int ColEnabled = 0, ColKey = 1, ColType = 2, ColValue = 3;

    private sealed record RowInfo(BodyField Node, BodyField? Parent, int Depth, int Index);

    private readonly DataGridView _grid = new();
    private readonly ToolStripButton _add = new("＋ 行を追加");
    private readonly ToolStripButton _addChild = new("＋ 子を追加");
    private readonly ToolStripButton _up = new("↑");
    private readonly ToolStripButton _down = new("↓");
    private readonly ToolStripButton _delete = new("削除");
    private readonly ToolStripButton _import = new("JSON から取り込み");
    private readonly ToolStripMenuItem _menuAdd = new("行を追加 (Insert)");
    private readonly ToolStripMenuItem _menuAddChild = new("子を追加");
    private readonly ToolStripMenuItem _menuUp = new("上へ (Alt+↑)");
    private readonly ToolStripMenuItem _menuDown = new("下へ (Alt+↓)");
    private readonly ToolStripMenuItem _menuDelete = new("削除 (Ctrl+Delete)");

    private List<BodyField> _roots = new();
    private bool _rootIsArray;
    private readonly List<RowInfo> _rows = new();
    private bool _loading;

    /// <summary>内容が変わったとき。</summary>
    public event EventHandler? Changed;

    /// <summary>「JSON から取り込み」でルートの種類が決まったとき(true = 配列)。</summary>
    public event EventHandler<bool>? Imported;

    public BodyTreeGrid()
    {
        Dock = DockStyle.Fill;
        GridStyle.Apply(_grid);
        _grid.AllowUserToAddRows = false;   // 行の追加はボタン・Insert で(ツリーのどこに足すかを明示するため)
        _grid.AllowUserToDeleteRows = false;
        _grid.Columns.Add(new DataGridViewCheckBoxColumn { HeaderText = "有効", FillWeight = 8, MinimumWidth = 40 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "キー", FillWeight = 34 });
        var typeColumn = new DataGridViewComboBoxColumn
        {
            HeaderText = "型",
            FillWeight = 16,
            MinimumWidth = 90,
            DisplayStyle = DataGridViewComboBoxDisplayStyle.DropDownButton,
            DisplayStyleForCurrentCellOnly = false, // プルダウンの矢印を全行に常に表示
        };
        typeColumn.Items.AddRange(BodyField.TypeLabels);
        _grid.Columns.Add(typeColumn);
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "値", FillWeight = 42 });
        foreach (DataGridViewColumn c in _grid.Columns) c.SortMode = DataGridViewColumnSortMode.NotSortable;

        _grid.CurrentCellDirtyStateChanged += (_, _) =>
        {
            // チェックボックスと型の選択は即確定させる
            if (_grid.IsCurrentCellDirty && _grid.CurrentCell is DataGridViewCheckBoxCell or DataGridViewComboBoxCell)
                _grid.CommitEdit(DataGridViewDataErrorContexts.Commit);
        };
        _grid.CellValueChanged += OnCellValueChanged;
        _grid.CellClick += (_, e) =>
        {
            // 型のセルは1回のクリックでプルダウンを開く(既定ではセル選択 → 編集開始 → 展開で複数回クリックが要る)
            if (e.RowIndex < 0 || e.ColumnIndex != ColType) return;
            _grid.BeginEdit(true);
            if (_grid.EditingControl is ComboBox combo) combo.DroppedDown = true;
        };
        _grid.CurrentCellChanged += (_, _) => UpdateButtons();
        _grid.DataError += (_, e) => e.ThrowException = false;
        _grid.CellMouseDown += (_, e) =>
        {
            if (e.Button == MouseButtons.Right && e.RowIndex >= 0 && e.ColumnIndex >= 0)
                _grid.CurrentCell = _grid.Rows[e.RowIndex].Cells[e.ColumnIndex];
        };
        _grid.MouseDoubleClick += (_, e) =>
        {
            // 行のない余白をダブルクリックしたらルートの末尾に追加
            if (_grid.HitTest(e.X, e.Y).Type == DataGridViewHitTestType.None) AddRow(atRootEnd: true);
        };

        var tools = new ToolStrip { GripStyle = ToolStripGripStyle.Hidden, RenderMode = ToolStripRenderMode.System, Dock = DockStyle.Top };
        tools.Items.AddRange(new ToolStripItem[] { _add, _addChild, new ToolStripSeparator(), _up, _down, new ToolStripSeparator(), _delete, new ToolStripSeparator(), _import });
        _add.Click += (_, _) => AddRow();
        _addChild.Click += (_, _) => AddChild();
        _up.Click += (_, _) => MoveRow(-1);
        _down.Click += (_, _) => MoveRow(1);
        _delete.Click += (_, _) => DeleteRow();
        _import.Click += (_, _) => ImportJson();
        _add.ToolTipText = "選択中の行の下に、同じ階層の行を追加 (Insert)";
        _addChild.ToolTipText = "選択中の連想配列{} / 配列[] の中に行を追加";

        var menu = new ContextMenuStrip();
        menu.Items.AddRange(new ToolStripItem[] { _menuAdd, _menuAddChild, new ToolStripSeparator(), _menuUp, _menuDown, new ToolStripSeparator(), _menuDelete });
        _menuAdd.Click += (_, _) => AddRow();
        _menuAddChild.Click += (_, _) => AddChild();
        _menuUp.Click += (_, _) => MoveRow(-1);
        _menuDown.Click += (_, _) => MoveRow(1);
        _menuDelete.Click += (_, _) => DeleteRow();
        _grid.ContextMenuStrip = menu;

        Controls.Add(_grid);
        Controls.Add(tools);
        UpdateButtons();
    }

    /// <summary>ルートを配列にするか(配列なら一番上の階層のキーは使わない)。</summary>
    public bool RootIsArray
    {
        get => _rootIsArray;
        set
        {
            if (_rootIsArray == value) return;
            CommitEdit();
            _rootIsArray = value;
            Rebuild(CurrentInfo?.Node);
        }
    }

    public void SetFields(IEnumerable<BodyField> fields, bool rootIsArray)
    {
        _roots = fields.Select(DeepClone).ToList();
        _rootIsArray = rootIsArray;
        Rebuild(null);
    }

    /// <summary>ツリーの複製を返す(入力中のセルの確定は呼び出し側で <see cref="CommitEdit"/>)。</summary>
    public List<BodyField> GetFields() => _roots.Select(DeepClone).ToList();

    /// <summary>有効なルート要素の数(タブの件数表示用)。</summary>
    public int ActiveCount => _roots.Count(f => f.Enabled && (_rootIsArray || !string.IsNullOrWhiteSpace(f.Key)));

    public void CommitEdit()
    {
        if (_grid.IsCurrentCellInEditMode) _grid.EndEdit();
    }

    private static BodyField DeepClone(BodyField f) => new(f.Key, f.Value, f.Type, f.Enabled)
    {
        Children = f.Children?.Select(DeepClone).ToList(),
    };

    // ── 表の作り直し ──

    private RowInfo? CurrentInfo =>
        _grid.CurrentCell is { RowIndex: >= 0 } c && c.RowIndex < _rows.Count ? _rows[c.RowIndex] : null;

    private bool IsArrayChild(RowInfo info) =>
        info.Parent?.Type == BodyValueType.Array || (info.Parent == null && _rootIsArray);

    private void Flatten(List<BodyField> nodes, BodyField? parent, int depth)
    {
        for (var i = 0; i < nodes.Count; i++)
        {
            _rows.Add(new RowInfo(nodes[i], parent, depth, i));
            if (nodes[i].IsContainer) Flatten(nodes[i].Children ??= new List<BodyField>(), nodes[i], depth + 1);
        }
    }

    /// <summary>モデルから表を作り直し、<paramref name="focus"/> の行(のセル)を選択する。</summary>
    private void Rebuild(BodyField? focus, int focusColumn = -1, bool beginEdit = false)
    {
        var keepColumn = _grid.CurrentCell?.ColumnIndex ?? ColKey;
        _loading = true;
        _grid.Rows.Clear();
        _rows.Clear();
        Flatten(_roots, null, 0);

        var indent = (int)Math.Round(18 * DeviceDpi / 96.0);
        var gray = SystemColors.GrayText;
        foreach (var info in _rows)
        {
            var node = info.Node;
            var row = _grid.Rows[_grid.Rows.Add()];
            row.Cells[ColEnabled].Value = node.Enabled;

            var key = row.Cells[ColKey];
            key.Style.Padding = new Padding(info.Depth * indent, 0, 0, 0);
            if (IsArrayChild(info))
            {
                key.Value = $"[{info.Index}]";
                key.ReadOnly = true;
                key.Style.ForeColor = gray;
            }
            else key.Value = node.Key;
            if (node.IsContainer) key.Style.Font = new Font(_grid.DefaultCellStyle.Font, FontStyle.Bold);

            row.Cells[ColType].Value = BodyField.ToLabel(node.Type);

            var value = row.Cells[ColValue];
            switch (node.Type)
            {
                case BodyValueType.Object:
                    value.Value = $"{{ {node.Children!.Count} 件 }}";
                    value.ReadOnly = true;
                    value.Style.ForeColor = gray;
                    break;
                case BodyValueType.Array:
                    value.Value = $"[ {node.Children!.Count} 件 ]";
                    value.ReadOnly = true;
                    value.Style.ForeColor = gray;
                    break;
                case BodyValueType.Null:
                    value.Value = "null";
                    value.ReadOnly = true;
                    value.Style.ForeColor = gray;
                    break;
                default:
                    value.Value = node.Value;
                    break;
            }
            if (!node.Enabled) row.DefaultCellStyle.ForeColor = gray;
        }
        _loading = false;

        var index = focus == null ? -1 : _rows.FindIndex(r => ReferenceEquals(r.Node, focus));
        if (index >= 0)
        {
            var col = focusColumn >= 0 ? focusColumn : keepColumn;
            var cell = _grid.Rows[index].Cells[col];
            _grid.CurrentCell = cell;
            if (beginEdit && !cell.ReadOnly)
            {
                _grid.Focus();
                _grid.BeginEdit(true);
            }
        }
        UpdateButtons();
    }

    private void UpdateButtons()
    {
        var info = CurrentInfo;
        var has = info != null;
        var container = info?.Node.IsContainer == true;
        _addChild.Enabled = _menuAddChild.Enabled = container;
        _up.Enabled = _menuUp.Enabled = has && info!.Index > 0;
        _down.Enabled = _menuDown.Enabled = has && info!.Index < SiblingsOf(info).Count - 1;
        _delete.Enabled = _menuDelete.Enabled = has;
    }

    // ── セル編集 ──

    private void OnCellValueChanged(object? sender, DataGridViewCellEventArgs e)
    {
        if (_loading || e.RowIndex < 0 || e.RowIndex >= _rows.Count) return;
        var info = _rows[e.RowIndex];
        var node = info.Node;
        var cellValue = _grid.Rows[e.RowIndex].Cells[e.ColumnIndex].Value;
        switch (e.ColumnIndex)
        {
            case ColEnabled:
                node.Enabled = cellValue is true;
                _grid.Rows[e.RowIndex].DefaultCellStyle.ForeColor = node.Enabled ? Color.Empty : SystemColors.GrayText;
                break;
            case ColKey:
                if (!IsArrayChild(info)) node.Key = cellValue as string ?? "";
                break;
            case ColValue:
                if (!node.IsContainer && node.Type != BodyValueType.Null) node.Value = cellValue as string ?? "";
                break;
            case ColType:
                var newType = BodyField.FromLabel(cellValue as string);
                // 表の作り直しや確認ダイアログはセル確定イベントの外で行う
                if (newType != node.Type) BeginInvoke(() => ChangeType(node, newType));
                return;
        }
        RaiseChanged();
    }

    private void ChangeType(BodyField node, BodyValueType newType)
    {
        var willBeContainer = newType is BodyValueType.Object or BodyValueType.Array;
        if (node.IsContainer && !willBeContainer && node.Children is { Count: > 0 } children)
        {
            var answer = MessageBox.Show(this, $"中の {children.Count} 件の要素は削除されます。型を変更しますか?",
                Program.WindowTitle, MessageBoxButtons.OKCancel, MessageBoxIcon.Question);
            if (answer != DialogResult.OK)
            {
                Rebuild(node);  // 型の表示を元に戻す
                return;
            }
        }

        if (willBeContainer)
        {
            node.Children ??= new List<BodyField>();
            node.Value = "";
            // 配列 → オブジェクトのとき、キーのない要素に連番のキーを付けて消えないようにする
            if (newType == BodyValueType.Object)
                for (var i = 0; i < node.Children.Count; i++)
                    if (string.IsNullOrWhiteSpace(node.Children[i].Key)) node.Children[i].Key = i.ToString();
        }
        else
        {
            node.Children = null;
            if (newType == BodyValueType.Boolean && !bool.TryParse(node.Value.Trim(), out _)) node.Value = "true";
            if (newType == BodyValueType.Null) node.Value = "";
        }
        node.Type = newType;
        Rebuild(node);
        RaiseChanged();
    }

    // ── 構造の操作 ──

    private List<BodyField> SiblingsOf(RowInfo info) => info.Parent?.Children ?? _roots;

    private void AddRow(bool atRootEnd = false)
    {
        CommitEdit();
        var info = atRootEnd ? null : CurrentInfo;
        var node = new BodyField();
        if (info == null) _roots.Add(node);
        else
        {
            var siblings = SiblingsOf(info);
            siblings.Insert(siblings.IndexOf(info.Node) + 1, node);
        }
        FocusNew(node, info == null ? _rootIsArray : IsArrayChild(info));
    }

    private void AddChild()
    {
        CommitEdit();
        if (CurrentInfo is not { Node.IsContainer: true } info) return;
        var node = new BodyField();
        info.Node.Children!.Add(node);
        FocusNew(node, info.Node.Type == BodyValueType.Array);
    }

    private void FocusNew(BodyField node, bool inArray)
    {
        // オブジェクトの中ならキーから、配列の中なら値から入力を始める
        Rebuild(node, inArray ? ColValue : ColKey, beginEdit: true);
        RaiseChanged();
    }

    private void MoveRow(int delta)
    {
        CommitEdit();
        if (CurrentInfo is not { } info) return;
        var siblings = SiblingsOf(info);
        var i = siblings.IndexOf(info.Node);
        var j = i + delta;
        if (j < 0 || j >= siblings.Count) return;
        (siblings[i], siblings[j]) = (siblings[j], siblings[i]);
        Rebuild(info.Node);
        RaiseChanged();
    }

    private void DeleteRow()
    {
        CommitEdit();
        if (CurrentInfo is not { } info) return;
        var siblings = SiblingsOf(info);
        var i = siblings.IndexOf(info.Node);
        siblings.RemoveAt(i);
        // 削除後は同じ階層の次(なければ前、なければ親)を選択
        var next = i < siblings.Count ? siblings[i] : i > 0 ? siblings[i - 1] : info.Parent;
        Rebuild(next);
        RaiseChanged();
    }

    private void ImportJson()
    {
        CommitEdit();
        using var dialog = new JsonImportDialog();
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        if (!RequestBuilder.TryParseJsonToFields(dialog.JsonText, out var fields, out var rootIsArray, out var error))
        {
            MessageBox.Show(this, error, Program.WindowTitle, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        if (_roots.Count > 0 &&
            MessageBox.Show(this, "今のボディを取り込んだ内容で置き換えますか?", Program.WindowTitle,
                MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK)
            return;

        _roots = fields;
        _rootIsArray = rootIsArray;
        Rebuild(_roots.FirstOrDefault());
        Imported?.Invoke(this, rootIsArray);
        RaiseChanged();
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (_grid.ContainsFocus && !_grid.IsCurrentCellInEditMode)
        {
            switch (keyData)
            {
                case Keys.Insert: AddRow(); return true;
                case Keys.Control | Keys.Delete: DeleteRow(); return true;
                case Keys.Alt | Keys.Up: MoveRow(-1); return true;
                case Keys.Alt | Keys.Down: MoveRow(1); return true;
            }
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    private void RaiseChanged()
    {
        if (!_loading) Changed?.Invoke(this, EventArgs.Empty);
    }
}

/// <summary>「JSON から取り込み」の入力ダイアログ。</summary>
internal sealed class JsonImportDialog : Form
{
    private readonly TextBox _text = new();

    public string JsonText => _text.Text;

    public JsonImportDialog()
    {
        Text = "JSON から取り込み";
        StartPosition = FormStartPosition.CenterParent;
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(560, 420);
        MinimizeBox = MaximizeBox = false;
        ShowInTaskbar = false;
        Font = new Font("Yu Gothic UI", 9.75f);

        var hint = new Label
        {
            Text = "JSON を貼り付けてください。連想配列{} / 配列[] の階層がそのまま表になります。",
            Dock = DockStyle.Top,
            AutoSize = true,
            Padding = new Padding(8, 8, 8, 4),
        };
        _text.Multiline = true;
        _text.ScrollBars = ScrollBars.Both;
        _text.WordWrap = false;
        _text.AcceptsTab = true;
        _text.Dock = DockStyle.Fill;
        _text.Font = new Font("Consolas", 10f);

        var ok = new Button { Text = "取り込む", DialogResult = DialogResult.OK, AutoSize = true };
        var cancel = new Button { Text = "キャンセル", DialogResult = DialogResult.Cancel, AutoSize = true };
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(8) };
        buttons.Controls.Add(cancel);
        buttons.Controls.Add(ok);
        AcceptButton = null; // 複数行入力なので Enter で閉じない
        CancelButton = cancel;

        var host = new Panel { Dock = DockStyle.Fill, Padding = new Padding(8, 0, 8, 0) };
        host.Controls.Add(_text);
        Controls.Add(host);
        Controls.Add(buttons);
        Controls.Add(hint);

        // クリップボードに JSON らしきものがあれば最初から入れておく
        var clip = Clipboard.ContainsText() ? Clipboard.GetText() : "";
        if (clip.TrimStart().StartsWith("{") || clip.TrimStart().StartsWith("[")) _text.Text = clip;
    }
}

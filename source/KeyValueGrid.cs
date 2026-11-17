namespace PostKun;

/// <summary>
/// 有効 / キー / 値 のフラットな表(パラメータ・ヘッダー用)。
/// 最終行の空行に入力すると行が増える。行の削除は右クリックメニューか、行を選んで Delete。
/// </summary>
public sealed class KeyValueGrid : UserControl
{
    private const int ColEnabled = 0, ColKey = 1, ColValue = 2;

    private readonly DataGridView _grid = new();
    private bool _loading;

    /// <summary>内容が変わったとき(セル確定・行の追加削除)。</summary>
    public event EventHandler? Changed;

    public KeyValueGrid(string keyHeader = "キー", string valueHeader = "値")
    {
        Dock = DockStyle.Fill;
        GridStyle.Apply(_grid);
        _grid.AllowUserToAddRows = true;
        _grid.Columns.Add(new DataGridViewCheckBoxColumn { HeaderText = "有効", FillWeight = 8, MinimumWidth = 40 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = keyHeader, FillWeight = 35 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = valueHeader, FillWeight = 57 });

        _grid.DefaultValuesNeeded += (_, e) => e.Row.Cells[ColEnabled].Value = true;
        _grid.CurrentCellDirtyStateChanged += (_, _) =>
        {
            // チェックボックスはクリックした瞬間に確定させる
            if (_grid.IsCurrentCellDirty && _grid.CurrentCell is DataGridViewCheckBoxCell)
                _grid.CommitEdit(DataGridViewDataErrorContexts.Commit);
        };
        _grid.CellValueChanged += (_, _) => RaiseChanged();
        _grid.RowsRemoved += (_, _) => RaiseChanged();
        _grid.DataError += (_, e) => e.ThrowException = false;

        var menu = new ContextMenuStrip();
        menu.Items.Add("行を削除", null, (_, _) => DeleteCurrentRows());
        _grid.ContextMenuStrip = menu;
        _grid.CellMouseDown += (_, e) =>
        {
            // 右クリックした行を選択してからメニューを出す
            if (e.Button == MouseButtons.Right && e.RowIndex >= 0 && e.ColumnIndex >= 0)
                _grid.CurrentCell = _grid.Rows[e.RowIndex].Cells[e.ColumnIndex];
        };

        Controls.Add(_grid);
    }

    public void SetItems(IEnumerable<KeyValueItem> items)
    {
        _loading = true;
        _grid.Rows.Clear();
        foreach (var item in items)
            _grid.Rows.Add(item.Enabled, item.Key, item.Value);
        _loading = false;
    }

    /// <summary>行を追加する(URL に貼り付けられたクエリを移すときなど)。</summary>
    public void AddItems(IEnumerable<KeyValueItem> items)
    {
        _loading = true;
        foreach (var item in items)
            _grid.Rows.Add(item.Enabled, item.Key, item.Value);
        _loading = false;
        RaiseChanged();
    }

    /// <summary>入力中のセルも含めて確定した内容を返す。キーも値も空の行は除く。</summary>
    public List<KeyValueItem> GetItems()
    {
        var list = new List<KeyValueItem>();
        foreach (DataGridViewRow row in _grid.Rows)
        {
            if (row.IsNewRow) continue;
            var key = row.Cells[ColKey].Value as string ?? "";
            var value = row.Cells[ColValue].Value as string ?? "";
            if (key.Length == 0 && value.Length == 0) continue;
            list.Add(new KeyValueItem(key, value, row.Cells[ColEnabled].Value is true));
        }
        return list;
    }

    /// <summary>有効でキーのある行の数(タブの件数表示用)。</summary>
    public int ActiveCount => GetItems().Count(i => i.Enabled && !string.IsNullOrWhiteSpace(i.Key));

    /// <summary>編集中のセルを確定する(保存・送信の直前に呼ぶ)。</summary>
    public void CommitEdit()
    {
        if (_grid.IsCurrentCellInEditMode) _grid.EndEdit();
    }

    private void DeleteCurrentRows()
    {
        var rows = _grid.SelectedCells.Cast<DataGridViewCell>().Select(c => c.OwningRow)
            .Concat(_grid.SelectedRows.Cast<DataGridViewRow>())
            .Distinct().Where(r => !r.IsNewRow).ToList();
        if (rows.Count == 0 && _grid.CurrentRow is { IsNewRow: false } current) rows.Add(current);
        _grid.CancelEdit();
        foreach (var r in rows) _grid.Rows.Remove(r);
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        // 編集中でなく、行ヘッダーで行全体を選んでいるときの Delete は DataGridView 標準で行削除される。
        // セル選択中の Ctrl+Delete でも行を削除できるようにする。
        if (keyData == (Keys.Control | Keys.Delete) && !_grid.IsCurrentCellInEditMode)
        {
            DeleteCurrentRows();
            return true;
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    private void RaiseChanged()
    {
        if (!_loading) Changed?.Invoke(this, EventArgs.Empty);
    }
}

/// <summary>表の見た目をそろえる共通設定。</summary>
internal static class GridStyle
{
    public static void Apply(DataGridView grid)
    {
        grid.Dock = DockStyle.Fill;
        grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill; // 列幅は比率で決めて DPI に依存させない
        grid.BackgroundColor = SystemColors.Window;
        grid.BorderStyle = BorderStyle.None;
        grid.RowHeadersWidth = 24;
        grid.RowHeadersWidthSizeMode = DataGridViewRowHeadersWidthSizeMode.DisableResizing;
        grid.AllowUserToResizeRows = false;
        grid.AllowUserToDeleteRows = true;
        grid.SelectionMode = DataGridViewSelectionMode.CellSelect;
        grid.EditMode = DataGridViewEditMode.EditOnKeystrokeOrF2;
        grid.EnableHeadersVisualStyles = false;
        grid.ColumnHeadersDefaultCellStyle.BackColor = SystemColors.Control;
        grid.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.AutoSize;
        grid.DefaultCellStyle.Font = new Font("Consolas", 10f);
        grid.DefaultCellStyle.SelectionBackColor = Color.FromArgb(204, 228, 247);
        grid.DefaultCellStyle.SelectionForeColor = SystemColors.ControlText;
        grid.GridColor = Color.FromArgb(225, 225, 225);
    }
}

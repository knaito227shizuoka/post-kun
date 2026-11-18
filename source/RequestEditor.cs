namespace PostKun;

/// <summary>
/// リクエストの編集欄。名前 / メソッド / URL / 送信ボタンと、その下のタブ(パラメータ・ヘッダー・ボディ)。
/// URL 欄では ? を入力できない。? 付きの URL が貼り付けられたらクエリをパラメータの表へ移す。
/// クエリ文字列と JSON はアプリが表から組み立て、送信 URL とボディをプレビュー表示する。
/// </summary>
public sealed class RequestEditor : UserControl
{
    private static readonly string[] BodyTypeLabels = { "なし", "JSON 連想配列{}", "JSON 配列[]", "フォーム (x-www-form-urlencoded)" };

    private readonly TextBox _name = new();
    private readonly Label _dirty = new();
    private readonly ComboBox _method = new();
    private readonly TextBox _url = new();
    private readonly Button _send = new();
    private readonly TabControl _tabs = new();
    private readonly TabPage _paramsPage = new();
    private readonly TabPage _headersPage = new();
    private readonly TabPage _bodyPage = new();
    private readonly KeyValueGrid _params = new("パラメータ名", "値");
    private readonly KeyValueGrid _headers = new("ヘッダー名", "値");
    private readonly ComboBox _bodyType = new();
    private readonly BodyTreeGrid _body = new();
    private readonly TextBox _bodyPreview = new();
    private readonly TextBox _finalUrl = new();
    private readonly ToolTip _tip = new();

    private bool _loading;

    /// <summary>何か編集されたとき。</summary>
    public event EventHandler? Changed;

    /// <summary>送信ボタン(送信中はキャンセル)が押されたとき。</summary>
    public event EventHandler? SendClicked;

    public RequestEditor()
    {
        BuildLayout();
        WireEvents();
        SetRequest(new RequestDef());
    }

    private void BuildLayout()
    {
        var mono = new Font("Consolas", 10.5f);

        // ── 名前行 ──
        _name.Dock = DockStyle.Fill;
        _name.PlaceholderText = "名前(保存すると左の一覧に出ます)";
        _dirty.AutoSize = true;
        _dirty.ForeColor = Color.DarkOrange;
        _dirty.Margin = new Padding(8, 6, 0, 0);
        var nameRow = Row(new ColumnStyle(SizeType.AutoSize), new ColumnStyle(SizeType.Percent, 100), new ColumnStyle(SizeType.AutoSize));
        nameRow.Controls.Add(new Label { Text = "名前", AutoSize = true, Margin = new Padding(0, 6, 8, 0) }, 0, 0);
        nameRow.Controls.Add(_name, 1, 0);
        nameRow.Controls.Add(_dirty, 2, 0);

        // ── リクエスト行: [メソッド] [URL] [送信] ──
        _method.DropDownStyle = ComboBoxStyle.DropDownList;
        _method.Items.AddRange(RequestDef.Methods);
        _method.Dock = DockStyle.Fill;
        _url.Dock = DockStyle.Fill;
        _url.Font = mono;
        _url.PlaceholderText = "http://localhost:3000/api/users   (? 以降は下のパラメータへ)";
        _send.Text = "送信";
        _send.AutoSize = true;
        _send.MinimumSize = new Size(88, 0);
        _send.Anchor = AnchorStyles.Left | AnchorStyles.Right; // Dock.Fill + AutoSize だと行が縦に伸びる
        _send.Margin = new Padding(6, 0, 0, 0);
        var requestRow = Row(new ColumnStyle(SizeType.Absolute, 100), new ColumnStyle(SizeType.Percent, 100), new ColumnStyle(SizeType.AutoSize));
        requestRow.Controls.Add(_method, 0, 0);
        requestRow.Controls.Add(_url, 1, 0);
        requestRow.Controls.Add(_send, 2, 0);

        // ── タブ ──
        _paramsPage.Controls.Add(_params);
        _headersPage.Controls.Add(Hint(_headers, "Content-Type はボディの形式に合わせて自動で付きます(ここに書けば上書き)。"));

        _bodyType.DropDownStyle = ComboBoxStyle.DropDownList;
        _bodyType.Items.AddRange(BodyTypeLabels);
        _bodyType.Width = 260;
        var bodyBar = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, WrapContents = false, Padding = new Padding(0, 4, 0, 2) };
        bodyBar.Controls.Add(new Label { Text = "形式", AutoSize = true, Margin = new Padding(4, 6, 8, 0) });
        bodyBar.Controls.Add(_bodyType);

        _bodyPreview.Multiline = true;
        _bodyPreview.ReadOnly = true;
        _bodyPreview.ScrollBars = ScrollBars.Both;
        _bodyPreview.WordWrap = false;
        _bodyPreview.Dock = DockStyle.Fill;
        _bodyPreview.Font = new Font("Consolas", 10f);
        _bodyPreview.BackColor = Color.FromArgb(248, 248, 248);

        var bodySplit = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Vertical, SplitterWidth = 6 };
        bodySplit.Panel1.Controls.Add(_body);
        bodySplit.Panel2.Controls.Add(_bodyPreview);
        bodySplit.Panel2.Controls.Add(new Label { Text = "送信されるボディ(自動生成)", Dock = DockStyle.Top, AutoSize = true, ForeColor = SystemColors.GrayText, Padding = new Padding(2, 2, 0, 2) });
        // 左右の比率は表示後に決める(SplitContainer は幅が確定する前に SplitterDistance を入れると例外になりやすい)
        bodySplit.HandleCreated += (_, _) => BeginInvoke(() =>
        {
            if (bodySplit.Width > 200) bodySplit.SplitterDistance = (int)(bodySplit.Width * 0.62);
        });

        _bodyPage.Controls.Add(bodySplit);
        _bodyPage.Controls.Add(bodyBar);

        foreach (var page in new[] { _paramsPage, _headersPage, _bodyPage })
        {
            page.Padding = new Padding(4);
            page.UseVisualStyleBackColor = true;
            _tabs.TabPages.Add(page);
        }
        _tabs.Dock = DockStyle.Fill;

        // ── 送信 URL(自動生成) ──
        _finalUrl.ReadOnly = true;
        _finalUrl.BorderStyle = BorderStyle.None;
        _finalUrl.Dock = DockStyle.Fill;
        _finalUrl.Font = new Font("Consolas", 9.5f);
        _finalUrl.BackColor = SystemColors.Control;
        _finalUrl.ForeColor = SystemColors.GrayText;
        var finalRow = Row(new ColumnStyle(SizeType.AutoSize), new ColumnStyle(SizeType.Percent, 100));
        finalRow.Controls.Add(new Label { Text = "送信URL", AutoSize = true, ForeColor = SystemColors.GrayText, Margin = new Padding(0, 0, 8, 0) }, 0, 0);
        finalRow.Controls.Add(_finalUrl, 1, 0);

        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, Padding = new Padding(8, 8, 8, 4) };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.Controls.Add(nameRow, 0, 0);
        root.Controls.Add(requestRow, 0, 1);
        root.Controls.Add(_tabs, 0, 2);
        root.Controls.Add(finalRow, 0, 3);
        Controls.Add(root);
    }

    private static TableLayoutPanel Row(params ColumnStyle[] columns)
    {
        var row = new TableLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, ColumnCount = columns.Length, Margin = new Padding(0, 0, 0, 4) };
        foreach (var c in columns) row.ColumnStyles.Add(c);
        return row;
    }

    private static Control Hint(Control content, string text)
    {
        var panel = new Panel { Dock = DockStyle.Fill };
        content.Dock = DockStyle.Fill;
        panel.Controls.Add(content);
        panel.Controls.Add(new Label { Text = text, Dock = DockStyle.Top, AutoSize = true, ForeColor = SystemColors.GrayText, Padding = new Padding(2, 2, 0, 4) });
        return panel;
    }

    private void WireEvents()
    {
        _name.TextChanged += (_, _) => RaiseChanged();
        _method.SelectedIndexChanged += (_, _) =>
        {
            // 利用者が POST に切り替えたら Content-Type: application/json をヘッダーの表に足す(読み込み時は足さない)
            if (!_loading && RequestBuilder.DefaultHeaderForMethod((string?)_method.SelectedItem ?? "", SelectedBodyType, _headers.GetItems()) is { } header)
                _headers.AddItems(new[] { header });   // AddItems が Changed を出す
            RaiseChanged();
        };
        _url.KeyPress += OnUrlKeyPress;
        _url.TextChanged += OnUrlTextChanged;
        _send.Click += (_, _) => SendClicked?.Invoke(this, EventArgs.Empty);
        _params.Changed += (_, _) => RaiseChanged();
        _headers.Changed += (_, _) => RaiseChanged();
        _body.Changed += (_, _) => RaiseChanged();
        _body.Imported += (_, isArray) =>
        {
            // 取り込んだ JSON のルートに合わせて形式を切り替える
            _bodyType.SelectedIndex = (int)(isArray ? BodyType.JsonArray : BodyType.Json);
        };
        _bodyType.SelectedIndexChanged += (_, _) =>
        {
            _body.RootIsArray = SelectedBodyType == BodyType.JsonArray;
            _body.Enabled = SelectedBodyType != BodyType.None;
            RaiseChanged();
        };
    }

    // ── URL 欄: ? 禁止 ──

    private void OnUrlKeyPress(object? sender, KeyPressEventArgs e)
    {
        if (e.KeyChar != '?') return;
        e.Handled = true;
        System.Media.SystemSounds.Beep.Play();
        _tip.Show("URL に ? は使えません。パラメータは下の「パラメータ」タブの表に書いてください。", _url, 0, _url.Height, 3000);
        _tabs.SelectedTab = _paramsPage;
    }

    private void OnUrlTextChanged(object? sender, EventArgs e)
    {
        // 貼り付けなどで ? が入ったら、クエリを表へ移して URL から取り除く
        if (!_loading && _url.Text.Contains('?'))
        {
            var baseUrl = RequestBuilder.SplitQuery(_url.Text, out var moved);
            _url.Text = baseUrl;               // ここで TextChanged が再入するが ? は無いので素通り
            _url.SelectionStart = _url.TextLength;
            if (moved.Count > 0)
            {
                _params.AddItems(moved);
                _tabs.SelectedTab = _paramsPage;
                _tip.Show($"クエリの {moved.Count} 件をパラメータの表へ移しました。", _url, 0, _url.Height, 3000);
            }
            return;
        }
        RaiseChanged();
    }

    // ── 入出力 ──

    private BodyType SelectedBodyType => (BodyType)Math.Max(0, _bodyType.SelectedIndex);

    public void SetRequest(RequestDef def)
    {
        _loading = true;
        _name.Text = def.Name;
        var methodIndex = Array.IndexOf(RequestDef.Methods, def.Method.ToUpperInvariant());
        _method.SelectedIndex = methodIndex >= 0 ? methodIndex : 0;
        // 古いデータや手書きの requests.json に ? が残っていても表へ分ける
        _url.Text = RequestBuilder.SplitQuery(def.Url, out var extra);
        _params.SetItems(def.Params.Concat(extra));
        _headers.SetItems(def.Headers);
        _bodyType.SelectedIndex = (int)def.BodyType;
        _body.SetFields(def.BodyFields, def.BodyType == BodyType.JsonArray);
        _body.Enabled = def.BodyType != BodyType.None;
        _loading = false;
        UpdateDerived();
    }

    /// <summary>
    /// 現在の内容を返す。<paramref name="commit"/> なら入力中のセルを確定させてから読む
    /// (表のイベント処理中から呼ぶプレビュー更新では確定させない。再入で例外になるため)。
    /// </summary>
    public RequestDef GetRequest(bool commit = true)
    {
        if (commit)
        {
            _params.CommitEdit();
            _headers.CommitEdit();
            _body.CommitEdit();
        }
        return new RequestDef
        {
            Name = _name.Text.Trim(),
            Method = (string?)_method.SelectedItem ?? "GET",
            Url = _url.Text.Trim(),
            Params = _params.GetItems(),
            Headers = _headers.GetItems(),
            BodyType = SelectedBodyType,
            BodyFields = _body.GetFields(),
        };
    }

    public void SetSending(bool sending) => _send.Text = sending ? "キャンセル" : "送信";

    public void SetDirty(bool dirty) => _dirty.Text = dirty ? "● 未保存" : "";

    public void FocusUrl()
    {
        _url.Focus();
        _url.SelectAll();
    }

    public void FocusName()
    {
        _name.Focus();
        _name.SelectAll();  // 「無名のリクエスト」をそのまま打ち替えられるように
    }

    private void RaiseChanged()
    {
        if (_loading) return;
        UpdateDerived();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>タブの件数・送信 URL・ボディのプレビューを更新する。</summary>
    private void UpdateDerived()
    {
        var def = GetRequest(commit: false);
        _paramsPage.Text = $"パラメータ ({_params.ActiveCount})";
        _headersPage.Text = $"ヘッダー ({_headers.ActiveCount})";
        _bodyPage.Text = def.BodyType == BodyType.None ? "ボディ" : $"ボディ ({_body.ActiveCount})";

        _finalUrl.Text = def.Url.Length == 0 ? "" : RequestBuilder.BuildUrl(def.Url, def.Params);

        switch (def.BodyType)
        {
            case BodyType.Json:
            case BodyType.JsonArray:
                var json = RequestBuilder.BuildJsonBody(def.BodyFields, def.BodyType == BodyType.JsonArray, out var errors);
                _bodyPreview.ForeColor = errors.Count > 0 ? Color.Firebrick : SystemColors.WindowText;
                _bodyPreview.Text = (errors.Count > 0 ? "⚠ " + string.Join("\n⚠ ", errors) + "\n\n" : "")
                    .ReplaceLineEndings("\r\n") + json.ReplaceLineEndings("\r\n");
                break;
            case BodyType.Form:
                _bodyPreview.ForeColor = SystemColors.WindowText;
                _bodyPreview.Text = RequestBuilder.BuildFormBody(def.BodyFields);
                break;
            default:
                _bodyPreview.ForeColor = SystemColors.GrayText;
                _bodyPreview.Text = "ボディなし(形式を選ぶと表から自動で組み立てます)";
                break;
        }
    }
}

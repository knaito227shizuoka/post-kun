namespace PostKun;

/// <summary>レスポンス表示。ステータス / 時間・サイズ / 整形 / コピー と、本文・ヘッダーのタブ。</summary>
public sealed class ResponseViewer : UserControl
{
    private readonly Label _status = new();
    private readonly Label _meta = new();
    private readonly CheckBox _pretty = new();
    private readonly Button _copy = new();
    private readonly TextBox _body = new();
    private readonly ListView _headers = new();
    private readonly TabPage _headersPage = new("ヘッダー");

    private ResponseResult? _last;

    public ResponseViewer()
    {
        _status.AutoSize = true;
        _status.Font = new Font("Yu Gothic UI", 10.5f, FontStyle.Bold);
        _status.Text = "未送信";
        _status.Margin = new Padding(0, 6, 16, 0);

        _meta.AutoSize = true;
        _meta.ForeColor = SystemColors.GrayText;
        _meta.Margin = new Padding(0, 8, 16, 0);

        _pretty.Text = "整形";
        _pretty.AutoSize = true;
        _pretty.Checked = true;
        _pretty.Margin = new Padding(0, 6, 8, 0);
        _pretty.CheckedChanged += (_, _) => RenderBody();

        _copy.Text = "コピー";
        _copy.AutoSize = true;
        _copy.Click += (_, _) =>
        {
            if (_body.TextLength > 0) Clipboard.SetText(_body.Text);
        };

        var infoRow = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, WrapContents = false, Padding = new Padding(0, 0, 0, 4) };
        infoRow.Controls.AddRange(new Control[] { _status, _meta, _pretty, _copy });

        _body.Multiline = true;
        _body.ReadOnly = true;
        _body.ScrollBars = ScrollBars.Both;
        _body.WordWrap = false;
        _body.Dock = DockStyle.Fill;
        _body.Font = new Font("Consolas", 10f);
        _body.BackColor = SystemColors.Window;

        _headers.View = View.Details;
        _headers.FullRowSelect = true;
        _headers.Dock = DockStyle.Fill;
        _headers.Columns.Add("ヘッダー名", 220);
        _headers.Columns.Add("値", 600);
        _headers.Font = new Font("Consolas", 10f);

        var bodyPage = new TabPage("本文") { Padding = new Padding(2) };
        bodyPage.Controls.Add(_body);
        _headersPage.Padding = new Padding(2);
        _headersPage.Controls.Add(_headers);
        var tabs = new TabControl { Dock = DockStyle.Fill };
        tabs.TabPages.Add(bodyPage);
        tabs.TabPages.Add(_headersPage);

        var host = new Panel { Dock = DockStyle.Fill, Padding = new Padding(8, 4, 8, 8) };
        host.Controls.Add(tabs);
        host.Controls.Add(infoRow);
        Controls.Add(host);
    }

    public void ShowSending()
    {
        _status.Text = "送信中…";
        _status.ForeColor = SystemColors.ControlText;
        _meta.Text = "";
    }

    public void ShowResponse(ResponseResult r)
    {
        _last = r;
        _status.Text = $"{r.StatusCode} {r.ReasonPhrase}";
        _status.ForeColor = StatusColor(r.StatusCode);
        _meta.Text = $"{r.ElapsedMs} ms   {FormatSize(r.SizeBytes)}";
        _headers.BeginUpdate();
        _headers.Items.Clear();
        foreach (var h in r.Headers) _headers.Items.Add(new ListViewItem(new[] { h.Key, h.Value }));
        _headers.EndUpdate();
        _headersPage.Text = $"ヘッダー ({r.Headers.Count})";
        RenderBody();
    }

    public void ShowError(string message)
    {
        _last = null;
        _status.Text = "エラー";
        _status.ForeColor = Color.Firebrick;
        _meta.Text = "";
        _headers.Items.Clear();
        _headersPage.Text = "ヘッダー";
        _body.Text = message.ReplaceLineEndings("\r\n");
    }

    private void RenderBody()
    {
        if (_last == null) return;
        var text = _last.BodyText;
        if (_pretty.Checked && JsonFormatter.TryFormat(text, out var formatted)) text = formatted;
        _body.Text = text.ReplaceLineEndings("\r\n");
    }

    internal static Color StatusColor(int code) => code switch
    {
        >= 200 and < 300 => Color.SeaGreen,
        >= 300 and < 400 => Color.RoyalBlue,
        >= 400 and < 500 => Color.DarkOrange,
        >= 500           => Color.Firebrick,
        _                => SystemColors.ControlText,
    };

    internal static string FormatSize(long bytes) => bytes switch
    {
        < 1024        => $"{bytes} B",
        < 1024 * 1024 => $"{bytes / 1024.0:0.0} KB",
        _             => $"{bytes / (1024.0 * 1024):0.0} MB",
    };
}

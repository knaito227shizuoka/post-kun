using System.Net.Sockets;
using PostKun;

namespace PostKun.Tests;

// 純粋関数のテスト。オフライン制約でテスト SDK が使えないため、依存ゼロの簡易ランナーで回す。
// 実行: dotnet run --project tests  (終了コード = 失敗数)

internal static class Program
{
    private static int _passed;
    private static int _failed;

    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Contains("--ui"))
        {
            // 画面部品のスモークテスト(透明なフォームに載せて操作する。設定ファイルには触れない)
            Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
            Application.EnableVisualStyles();
            UiSmoke_Cases();
            Console.WriteLine($"結果: {_passed} passed, {_failed} failed");
            return _failed;
        }
        ExpandVariables_Cases();
        NormalizeUrl_Cases();
        BuildUrl_Cases();
        BuildFormBody_Cases();
        BuildMessage_Cases();
        JsonFormatter_Cases();
        DecodeBody_Cases();
        DescribeError_Cases();
        FormatSize_Cases();
        BuildJsonBody_Cases();
        ParseJsonToFields_Cases();
        SplitQuery_Cases();
        Storage_Cases();
        Naming_Cases();
        DefaultHeader_Cases();
        HttpSender_LocalServer_Cases().GetAwaiter().GetResult();

        Console.WriteLine();
        Console.WriteLine($"結果: {_passed} passed, {_failed} failed");
        return _failed;
    }

    private static KeyValueItem[] Vars(params (string Key, string Value, bool Enabled)[] items) =>
        items.Select(i => new KeyValueItem(i.Key, i.Value, i.Enabled)).ToArray();

    private static void ExpandVariables_Cases()
    {
        Section(nameof(RequestBuilder.ExpandVariables));
        var vars = Vars(("baseUrl", "http://localhost:3000", true), ("token", "abc", true), ("off", "x", false));

        Eq("http://localhost:3000/api", RequestBuilder.ExpandVariables("{{baseUrl}}/api", vars, out var m1));
        EqInt(0, m1.Count);
        Eq("http://localhost:3000/abc", RequestBuilder.ExpandVariables("{{baseUrl}}/{{ token }}", vars, out _)); // 空白許容
        Eq("{{nope}}/{{off}}", RequestBuilder.ExpandVariables("{{nope}}/{{off}}", vars, out var m2));          // 未定義・無効は残す
        Eq("nope,off", string.Join(",", m2));
        Eq("{{nope}}{{nope}}", RequestBuilder.ExpandVariables("{{nope}}{{nope}}", vars, out var m3));
        EqInt(1, m3.Count);                                                                                  // 重複は1回だけ報告
        Eq("{{baseUrl", RequestBuilder.ExpandVariables("{{baseUrl", vars, out _));                             // 不完全な記法はそのまま
        Eq("1", RequestBuilder.ExpandVariables("{{a}}", Vars(("a", "1", true), ("a", "2", true)), out _));    // 先勝ち
    }

    private static void NormalizeUrl_Cases()
    {
        Section(nameof(RequestBuilder.NormalizeUrl));
        Eq("http://localhost:3000", RequestBuilder.NormalizeUrl("localhost:3000"));
        Eq("http://localhost:3000", RequestBuilder.NormalizeUrl("  http://localhost:3000  "));
        Eq("https://example.com", RequestBuilder.NormalizeUrl("https://example.com"));
        Eq("", RequestBuilder.NormalizeUrl("   "));
    }

    private static void BuildUrl_Cases()
    {
        Section(nameof(RequestBuilder.BuildUrl));
        var none = Array.Empty<KeyValueItem>();
        Eq("http://h/api", RequestBuilder.BuildUrl("http://h/api", none));
        Eq("http://h/api?a=1&b=2", RequestBuilder.BuildUrl("http://h/api", Vars(("a", "1", true), ("b", "2", true))));
        Eq("http://h/api?x=0&a=1", RequestBuilder.BuildUrl("http://h/api?x=0", Vars(("a", "1", true))));
        Eq("http://h/api?a=1", RequestBuilder.BuildUrl("http://h/api?", Vars(("a", "1", true))));
        Eq("http://h/api?q=%E3%81%82%20b%26c", RequestBuilder.BuildUrl("http://h/api", Vars(("q", "あ b&c", true))));
        Eq("http://h/api?b=2", RequestBuilder.BuildUrl("http://h/api", Vars(("a", "1", false), ("b", "2", true), ("", "x", true))));
        Eq("http://h/api?a=1#top", RequestBuilder.BuildUrl("http://h/api#top", Vars(("a", "1", true))));
        Eq("http://localhost/api?a=1", RequestBuilder.BuildUrl("localhost/api", Vars(("a", "1", true))));
    }

    private static void BuildFormBody_Cases()
    {
        Section(nameof(RequestBuilder.BuildFormBody));
        Eq("name=taro&age=20", RequestBuilder.BuildFormBody(Vars(("name", "taro", true), ("age", "20", true))));
        Eq("msg=%E3%81%93%E3%82%93%3D%26", RequestBuilder.BuildFormBody(Vars(("msg", "こん=&", true))));
        Eq("", RequestBuilder.BuildFormBody(Vars(("a", "1", false))));
    }

    private static void BuildMessage_Cases()
    {
        Section(nameof(RequestBuilder.BuildMessage));
        var def = new RequestDef
        {
            Method = "post",
            Url = "{{baseUrl}}/users",
            Params = { new KeyValueItem("v", "{{ver}}") },
            Headers = { new KeyValueItem("Authorization", "Bearer {{token}}"), new KeyValueItem("Content-Type", "application/vnd.api+json") },
            BodyType = BodyType.Json,
            BodyFields =
            {
                new BodyField("name", "{{name}}"),
                BodyField.Container("tags", BodyValueType.Array, new BodyField("", "{{token}}")),
            },
        };
        var vars = Vars(("baseUrl", "http://localhost:3000", true), ("token", "abc", true), ("ver", "2", true));
        using var msg = RequestBuilder.BuildMessage(def, vars, out var missing);
        Eq("POST", msg.Method.Method);
        Eq("http://localhost:3000/users?v=2", msg.RequestUri!.ToString());
        Eq("Bearer abc", string.Join(",", msg.Headers.GetValues("Authorization")));
        Eq("application/vnd.api+json", msg.Content!.Headers.ContentType!.MediaType!); // Content-Type 上書き
        Eq("{\n  \"name\": \"{{name}}\",\n  \"tags\": [\n    \"abc\"\n  ]\n}", msg.Content.ReadAsStringAsync().Result); // 子要素の変数も置換
        Eq("name", string.Join(",", missing));

        using var get = RequestBuilder.BuildMessage(new RequestDef { Url = "localhost:8080" }, Array.Empty<KeyValueItem>(), out _);
        Eq("GET", get.Method.Method);
        EqInt(1, get.Content == null ? 1 : 0);

        // ボディの値が不正なら送らずに例外
        try
        {
            RequestBuilder.BuildMessage(new RequestDef { Url = "h", BodyType = BodyType.Json, BodyFields = { new BodyField("n", "abc", BodyValueType.Number) } },
                Array.Empty<KeyValueItem>(), out _).Dispose();
            Eq("RequestBuildException", "no exception");
        }
        catch (RequestBuildException ex) { EqInt(1, ex.Message.Contains("「n」: 数値として読めません") ? 1 : 0); }

        // ルートが配列
        using var arr = RequestBuilder.BuildMessage(new RequestDef { Url = "h", BodyType = BodyType.JsonArray, BodyFields = { new BodyField("", "1", BodyValueType.Number) } },
            Array.Empty<KeyValueItem>(), out _);
        Eq("[\n  1\n]", arr.Content!.ReadAsStringAsync().Result);

        // フォーム
        using var form = RequestBuilder.BuildMessage(new RequestDef { Url = "h", Method = "POST", BodyType = BodyType.Form, BodyFields = { new BodyField("a", "1 2") } },
            Array.Empty<KeyValueItem>(), out _);
        Eq("application/x-www-form-urlencoded", form.Content!.Headers.ContentType!.MediaType!);
        Eq("a=1%202", form.Content.ReadAsStringAsync().Result);
    }

    private static void BuildJsonBody_Cases()
    {
        Section(nameof(RequestBuilder.BuildJsonBody));
        string Compact(string s) => s.Replace("\n", "").Replace(" ", "");

        // 型ごとの書き出し
        var json = RequestBuilder.BuildJsonBody(new[]
        {
            new BodyField("s", "こんにちは"),
            new BodyField("n", "1.5e3", BodyValueType.Number),
            new BodyField("b", "TRUE", BodyValueType.Boolean),
            new BodyField("z", "無視される", BodyValueType.Null),
            new BodyField("off", "x", enabled: false),     // 無効
            new BodyField("", "x"),                         // キーなし(未入力)
        }, false, out var e1);
        Eq("{\"s\":\"こんにちは\",\"n\":1.5e3,\"b\":true,\"z\":null}", Compact(json));
        EqInt(0, e1.Count);

        // 階層: オブジェクトの中に配列、配列の中にオブジェクト
        var nested = RequestBuilder.BuildJsonBody(new[]
        {
            BodyField.Container("user", BodyValueType.Object,
                new BodyField("name", "taro"),
                BodyField.Container("tags", BodyValueType.Array,
                    new BodyField("ignored-key", "a"),       // 配列の子のキーは使わない
                    new BodyField("", "2", BodyValueType.Number))),
            BodyField.Container("items", BodyValueType.Array,
                BodyField.Container("", BodyValueType.Object, new BodyField("id", "1", BodyValueType.Number)),
                BodyField.Container("", BodyValueType.Array),
                BodyField.Container("", BodyValueType.Object)),
        }, false, out var e2);
        Eq("{\"user\":{\"name\":\"taro\",\"tags\":[\"a\",2]},\"items\":[{\"id\":1},[],{}]}", Compact(nested));
        EqInt(0, e2.Count);

        // 整形される(インデント2・改行\n)
        Eq("{\n  \"a\": [\n    1\n  ]\n}", RequestBuilder.BuildJsonBody(new[] { BodyField.Container("a", BodyValueType.Array, new BodyField("", "1", BodyValueType.Number)) }, false, out _));

        // ルートが配列
        Eq("[\"x\",{\"k\":\"v\"}]", Compact(RequestBuilder.BuildJsonBody(new[]
        {
            new BodyField("", "x"),
            BodyField.Container("", BodyValueType.Object, new BodyField("k", "v")),
        }, true, out _)));
        Eq("{}", RequestBuilder.BuildJsonBody(Array.Empty<BodyField>(), false, out _));
        Eq("[]", RequestBuilder.BuildJsonBody(Array.Empty<BodyField>(), true, out _));

        // エラーは場所(パス)付きで報告し、その要素は出さない
        var bad = RequestBuilder.BuildJsonBody(new[]
        {
            new BodyField("age", "abc", BodyValueType.Number),
            new BodyField("ok", "yes", BodyValueType.Boolean),
            new BodyField("dup", "1"),
            new BodyField("dup", "2"),
            BodyField.Container("list", BodyValueType.Array, new BodyField("", "1"), new BodyField("", "01", BodyValueType.Number)),
        }, false, out var e3);
        Eq("{\"dup\":\"1\",\"list\":[\"1\"]}", Compact(bad));
        Eq("「age」: 数値として読めません (abc)|「ok」: 真偽値は true / false で書いてください (yes)|「dup」: キーが重複しています|「list[1]」: 数値として読めません (01)",
            string.Join("|", e3));

        // 入れ子のパス表記
        RequestBuilder.BuildJsonBody(new[]
        {
            BodyField.Container("a", BodyValueType.Object,
                BodyField.Container("b", BodyValueType.Array,
                    BodyField.Container("", BodyValueType.Object, new BodyField("c", "x", BodyValueType.Number)))),
        }, false, out var e4);
        Eq("「a.b[0].c」: 数値として読めません (x)", string.Join("|", e4));

        // フォームではオブジェクト / 配列を1行の JSON 文字列にする
        Eq("name=taro&tags=%5B%22a%22%2C1%5D&n=", RequestBuilder.BuildFormBody(new[]
        {
            new BodyField("name", "taro"),
            BodyField.Container("tags", BodyValueType.Array, new BodyField("", "a"), new BodyField("", "1", BodyValueType.Number)),
            new BodyField("n", "x", BodyValueType.Null),
        }));
    }

    private static void ParseJsonToFields_Cases()
    {
        Section(nameof(RequestBuilder.TryParseJsonToFields));
        const string src = "{\"name\":\"太郎\",\"age\":20,\"admin\":false,\"memo\":null,\"tags\":[\"a\",1.5],\"addr\":{\"zip\":\"100\"}}";
        EqInt(1, RequestBuilder.TryParseJsonToFields(src, out var fields, out var isArray, out _) ? 1 : 0);
        EqInt(0, isArray ? 1 : 0);
        Eq("name:String,age:Number,admin:Boolean,memo:Null,tags:Array,addr:Object", string.Join(",", fields.Select(f => $"{f.Key}:{f.Type}")));
        Eq("20", fields[1].Value);
        Eq("false", fields[2].Value);
        Eq("1.5", fields[4].Children![1].Value);
        Eq("zip", fields[5].Children![0].Key);

        // 往復: 取り込んで組み立て直すと同じ JSON になる
        var rebuilt = RequestBuilder.BuildJsonBody(fields, isArray, out var errors);
        EqInt(0, errors.Count);
        Eq(src, rebuilt.Replace("\n", "").Replace("  ", "").Replace("\": ", "\":"));

        EqInt(1, RequestBuilder.TryParseJsonToFields("[1, {\"a\": []}]", out var arr, out var arrIsArray, out _) ? 1 : 0);
        EqInt(1, arrIsArray ? 1 : 0);
        Eq("Number,Object", string.Join(",", arr.Select(f => f.Type)));
        EqInt(1, RequestBuilder.TryParseJsonToFields("{\"a\":1,}", out _, out _, out _) ? 1 : 0); // 末尾カンマは許す

        EqInt(0, RequestBuilder.TryParseJsonToFields("\"str\"", out _, out _, out var e1) ? 1 : 0);
        StartsWith("ルートは", e1);
        EqInt(0, RequestBuilder.TryParseJsonToFields("{oops", out _, out _, out var e2) ? 1 : 0);
        StartsWith("JSON として読めません", e2);
    }

    private static void SplitQuery_Cases()
    {
        Section(nameof(RequestBuilder.SplitQuery));
        Eq("http://h/api", RequestBuilder.SplitQuery("http://h/api", out var p0));
        EqInt(0, p0.Count);
        Eq("http://h/api", RequestBuilder.SplitQuery("http://h/api?a=1&b=&c&q=%E3%81%82+b#frag", out var p1));
        Eq("a=1|b=|c=|q=あ b", string.Join("|", p1.Select(p => $"{p.Key}={p.Value}")));
        Eq("http://h/api", RequestBuilder.SplitQuery("http://h/api?", out var p2));
        EqInt(0, p2.Count);
        Eq("h", RequestBuilder.SplitQuery("h?x=%ZZ", out var p3));            // 壊れたエンコードはそのまま
        Eq("%ZZ", p3[0].Value);
    }

    private static void Storage_Cases()
    {
        Section(nameof(Storage));
        var dir = Path.Combine(Path.GetTempPath(), "post-kun-tests-" + Guid.NewGuid().ToString("N"));
        try
        {
            var storage = new Storage(dir);
            EqInt(0, storage.LoadRequests().Count);                              // ファイルなし → 空

            var list = new List<RequestDef>
            {
                new() { Name = "ユーザ登録", Method = "POST", BodyType = BodyType.Json, BodyFields = { BodyField.Container("tags", BodyValueType.Array, new BodyField("", "a")) } },
                new() { Name = "ありがとう" },
                new() { Name = "Beta" },
                new() { Name = "alpha" },
            };
            storage.SaveRequests(list);
            var loaded = storage.LoadRequests();
            Eq("alpha,Beta,ありがとう,ユーザ登録", string.Join(",", loaded.Select(r => r.Name)));  // 名前順
            Eq("a", loaded[3].BodyFields[0].Children![0].Value);                             // 階層も保存される
            var text = File.ReadAllText(Path.Combine(dir, Storage.RequestsFile));
            EqInt(1, text.Contains("\"BodyType\": \"Json\"") ? 1 : 0);              // enum は文字列で保存
            EqInt(1, text.Contains("ユーザ登録") ? 1 : 0);                           // 日本語はエスケープしない
            EqInt(0, text.Contains("\"Children\": null") ? 1 : 0);                   // スカラーの Children は書かない

            // 壊れたファイルは .bak に退避して空で続行
            File.WriteAllText(Path.Combine(dir, Storage.RequestsFile), "{ broken");
            EqInt(0, storage.LoadRequests().Count);
            Eq("{ broken", File.ReadAllText(Path.Combine(dir, Storage.RequestsFile + ".bak")));

            // 設定
            storage.Save(Storage.SettingsFile, new AppSettings { WindowWidth = 900, LastSelected = "alpha" });
            var settings = storage.Load<AppSettings>(Storage.SettingsFile);
            EqInt(900, settings.WindowWidth ?? 0);
            Eq("alpha", settings.LastSelected ?? "");

            // 複製は深いコピー
            var original = loaded[3];
            var clone = Storage.Clone(original);
            clone.BodyFields[0].Children![0].Value = "changed";
            Eq("a", original.BodyFields[0].Children![0].Value);
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { }
        }
    }

    private static void DefaultHeader_Cases()
    {
        Section(nameof(RequestBuilder.DefaultHeaderForMethod));
        string Show(KeyValueItem? h) => h == null ? "null" : $"{h.Key}={h.Value}";
        Eq("Content-Type=application/json", Show(RequestBuilder.DefaultHeaderForMethod("POST", BodyType.None, Vars())));
        Eq("Content-Type=application/json", Show(RequestBuilder.DefaultHeaderForMethod("post", BodyType.None, Vars(("Accept", "*/*", true)))));
        Eq("null", Show(RequestBuilder.DefaultHeaderForMethod("GET", BodyType.None, Vars())));
        Eq("null", Show(RequestBuilder.DefaultHeaderForMethod("PUT", BodyType.None, Vars())));
        Eq("null", Show(RequestBuilder.DefaultHeaderForMethod("POST", BodyType.None, Vars((" content-type ", "text/plain", true)))));   // 既にある(大文字小文字・空白無視)
        Eq("null", Show(RequestBuilder.DefaultHeaderForMethod("POST", BodyType.None, Vars(("Content-Type", "text/plain", false)))));    // 無効の行もあるとみなす
        Eq("null", Show(RequestBuilder.DefaultHeaderForMethod("POST", BodyType.Form, Vars())));                                // フォームには付けない
        Eq("Content-Type=application/json", Show(RequestBuilder.DefaultHeaderForMethod("POST", BodyType.Json, Vars())));
    }

    private static void Naming_Cases()
    {
        Section("RequestDef naming");
        Eq("ログイン のコピー", RequestDef.MakeCopyName("ログイン", new[] { "ログイン" }));
        Eq("ログイン のコピー 3", RequestDef.MakeCopyName("ログイン", new[] { "ログイン", "ログイン のコピー", "ログイン のコピー 2" }));
        Eq("無名のリクエスト", RequestDef.MakeUniqueName(RequestDef.UntitledName, new[] { "ログイン" }));
        Eq("無名のリクエスト 3", RequestDef.MakeUniqueName(RequestDef.UntitledName, new[] { "無名のリクエスト", "無名のリクエスト 2" }));
        Eq("x 2", RequestDef.MakeUniqueName("x", new[] { "X" }));                     // 大文字小文字無視
        var list = new[] { "b", "B2", "あ", "ア2", "a" }.Select(n => new RequestDef { Name = n }).ToList();
        list.Sort(RequestDef.ByName);
        Eq("a,b,B2,あ,ア2", string.Join(",", list.Select(r => r.Name)));       // 大文字小文字・かなの種類を無視
    }

    private static void JsonFormatter_Cases()
    {
        Section(nameof(JsonFormatter));
        var nl = Environment.NewLine;
        EqInt(1, JsonFormatter.TryFormat("{\"a\":1,\"b\":[true,null]}", out var f1) ? 1 : 0);
        Eq($"{{{nl}  \"a\": 1,{nl}  \"b\": [{nl}    true,{nl}    null{nl}  ]{nl}}}", f1);
        EqInt(1, JsonFormatter.TryFormat("{\"msg\":\"こんにちは\"}", out var f2) ? 1 : 0);
        EqInt(1, f2.Contains("こんにちは") ? 1 : 0);                                 // 日本語がエスケープされない
        EqInt(0, JsonFormatter.TryFormat("<html></html>", out var f3) ? 1 : 0);
        Eq("<html></html>", f3);
        EqInt(0, JsonFormatter.TryFormat("", out _) ? 1 : 0);

        EqInt(1, JsonFormatter.IsJsonContentType("application/json; charset=utf-8") ? 1 : 0);
        EqInt(1, JsonFormatter.IsJsonContentType("application/problem+json") ? 1 : 0);
        EqInt(0, JsonFormatter.IsJsonContentType("text/html") ? 1 : 0);
        EqInt(0, JsonFormatter.IsJsonContentType(null) ? 1 : 0);
    }

    private static void DecodeBody_Cases()
    {
        Section(nameof(HttpSender.DecodeBody));
        var utf8 = System.Text.Encoding.UTF8.GetBytes("日本語");
        Eq("日本語", HttpSender.DecodeBody(utf8, null));
        Eq("日本語", HttpSender.DecodeBody(utf8, "unknown-charset"));
        var sjis = System.Text.Encoding.GetEncoding("shift_jis").GetBytes("日本語");
        Eq("日本語", HttpSender.DecodeBody(sjis, "Shift_JIS"));
    }

    private static void DescribeError_Cases()
    {
        Section(nameof(HttpSender.DescribeError));
        StartsWith("接続を拒否", HttpSender.DescribeError(new HttpRequestException("x", new SocketException((int)SocketError.ConnectionRefused))));
        StartsWith("ホスト名が見つかりません", HttpSender.DescribeError(new HttpRequestException("x", new SocketException((int)SocketError.HostNotFound))));
        StartsWith("タイムアウト", HttpSender.DescribeError(new TimeoutException("30 秒")));
        StartsWith("キャンセル", HttpSender.DescribeError(new TaskCanceledException()));
        StartsWith("URL の形式", HttpSender.DescribeError(new UriFormatException()));
        StartsWith("通信エラー", HttpSender.DescribeError(new HttpRequestException("boom")));
    }

    private static void FormatSize_Cases()
    {
        Section(nameof(ResponseViewer.FormatSize));
        Eq("512 B", ResponseViewer.FormatSize(512));
        Eq("1.5 KB", ResponseViewer.FormatSize(1536));
        Eq("2.0 MB", ResponseViewer.FormatSize(2 * 1024 * 1024));
    }

    /// <summary>テスト内に HttpListener のローカルサーバを立てて、実際に送受信する結合テスト。</summary>
    private static async Task HttpSender_LocalServer_Cases()
    {
        Section("HttpSender (local server)");
        var port = FreePort();
        var prefix = $"http://localhost:{port}/";
        using var listener = new System.Net.HttpListener();
        listener.Prefixes.Add(prefix);
        listener.Start();

        // 受信したリクエストを JSON でそのまま返すエコーサーバ。/slow は 3 秒待つ。
        var serverTask = Task.Run(async () =>
        {
            while (listener.IsListening)
            {
                System.Net.HttpListenerContext ctx;
                try { ctx = await listener.GetContextAsync(); } catch { return; }
                _ = Task.Run(async () =>
                {
                    try
                    {
                        if (ctx.Request.Url!.AbsolutePath == "/slow") await Task.Delay(3000);
                        using var reader = new StreamReader(ctx.Request.InputStream, System.Text.Encoding.UTF8);
                        var body = await reader.ReadToEndAsync();
                        var json = System.Text.Json.JsonSerializer.Serialize(new
                        {
                            method = ctx.Request.HttpMethod,
                            query = ctx.Request.Url.Query,
                            contentType = ctx.Request.ContentType,
                            auth = ctx.Request.Headers["Authorization"],
                            body,
                        });
                        var bytes = System.Text.Encoding.UTF8.GetBytes(json);
                        ctx.Response.StatusCode = ctx.Request.Url.AbsolutePath == "/missing" ? 404 : 200;
                        ctx.Response.ContentType = "application/json; charset=utf-8";
                        await ctx.Response.OutputStream.WriteAsync(bytes);
                        ctx.Response.Close();
                    }
                    catch { /* キャンセルでクライアントが切断した場合など */ }
                });
            }
        });

        using var sender = new HttpSender();
        var vars = Vars(("baseUrl", $"localhost:{port}", true), ("token", "abc", true));

        // GET + Params
        using (var get = RequestBuilder.BuildMessage(new RequestDef { Url = "{{baseUrl}}/api", Params = { new KeyValueItem("q", "あ") } }, vars, out _))
        {
            var r = await sender.SendAsync(get, CancellationToken.None);
            EqInt(200, r.StatusCode);
            Eq("application/json; charset=utf-8", r.ContentType ?? "");
            using var doc = System.Text.Json.JsonDocument.Parse(r.BodyText);
            Eq("GET", doc.RootElement.GetProperty("method").GetString()!);
            Eq("?q=%E3%81%82", doc.RootElement.GetProperty("query").GetString()!);
            EqInt(1, r.SizeBytes > 0 ? 1 : 0);
        }

        // POST JSON + ヘッダ
        var post = new RequestDef
        {
            Method = "POST",
            Url = "{{baseUrl}}/users",
            Headers = { new KeyValueItem("Authorization", "Bearer {{token}}") },
            BodyType = BodyType.Json,
            BodyFields = { new BodyField("name", "太郎") },
        };
        using (var msg = RequestBuilder.BuildMessage(post, vars, out _))
        {
            var r = await sender.SendAsync(msg, CancellationToken.None);
            using var doc = System.Text.Json.JsonDocument.Parse(r.BodyText);
            Eq("POST", doc.RootElement.GetProperty("method").GetString()!);
            Eq("application/json; charset=utf-8", doc.RootElement.GetProperty("contentType").GetString()!);
            Eq("Bearer abc", doc.RootElement.GetProperty("auth").GetString()!);
            using var sent = System.Text.Json.JsonDocument.Parse(doc.RootElement.GetProperty("body").GetString()!);
            Eq("太郎", sent.RootElement.GetProperty("name").GetString()!);   // サーバに日本語のまま届く
        }

        // 404 も例外にせずレスポンスとして返す
        using (var msg = RequestBuilder.BuildMessage(new RequestDef { Url = "{{baseUrl}}/missing" }, vars, out _))
            EqInt(404, (await sender.SendAsync(msg, CancellationToken.None)).StatusCode);

        // タイムアウト
        sender.Timeout = TimeSpan.FromMilliseconds(300);
        try
        {
            using var msg = RequestBuilder.BuildMessage(new RequestDef { Url = "{{baseUrl}}/slow" }, vars, out _);
            await sender.SendAsync(msg, CancellationToken.None);
            Eq("TimeoutException", "no exception");
        }
        catch (Exception ex) { Eq(nameof(TimeoutException), ex.GetType().Name); }

        // 利用者のキャンセルはタイムアウトと区別する
        sender.Timeout = TimeSpan.FromSeconds(30);
        try
        {
            using var cts = new CancellationTokenSource(200);
            using var msg = RequestBuilder.BuildMessage(new RequestDef { Url = "{{baseUrl}}/slow" }, vars, out _);
            await sender.SendAsync(msg, cts.Token);
            Eq("OperationCanceled", "no exception");
        }
        catch (Exception ex) { StartsWith("キャンセル", HttpSender.DescribeError(ex)); }

        listener.Stop();
        await serverTask;

        // 接続拒否(誰も待ち受けていないポート)
        try
        {
            using var msg = RequestBuilder.BuildMessage(new RequestDef { Url = $"http://localhost:{FreePort()}/" }, vars, out _);
            await sender.SendAsync(msg, CancellationToken.None);
            Eq("HttpRequestException", "no exception");
        }
        catch (Exception ex) { StartsWith("接続を拒否", HttpSender.DescribeError(ex)); }
    }

    private static void UiSmoke_Cases()
    {
        Section("UI smoke: RequestEditor / BodyTreeGrid");
        var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
        using var form = new Form { Opacity = 0, ShowInTaskbar = false, Size = new Size(1000, 700) };
        var editor = new RequestEditor { Dock = DockStyle.Fill };
        form.Controls.Add(editor);
        form.Show();
        Application.DoEvents();

        // 階層付きの定義を読み込んで、そのまま取り出せる
        var def = new RequestDef
        {
            Name = "テスト",
            Method = "POST",
            Url = "localhost:3000/api?x=1",        // ? 付きはパラメータへ分けられる
            Params = { new KeyValueItem("a", "1") },
            BodyType = BodyType.Json,
            BodyFields =
            {
                new BodyField("name", "taro"),
                BodyField.Container("tags", BodyValueType.Array, new BodyField("", "a")),
            },
        };
        editor.SetRequest(def);
        Application.DoEvents();
        var got = editor.GetRequest();
        Eq("localhost:3000/api", got.Url);
        Eq("a=1|x=1", string.Join("|", got.Params.Select(p => $"{p.Key}={p.Value}")));
        Eq("{\"name\":\"taro\",\"tags\":[\"a\"]}", Compact(RequestBuilder.BuildJsonBody(got.BodyFields, false, out _)));

        // URL 欄に ? 付きを貼り付けるとクエリが表へ移る
        var url = (TextBox)typeof(RequestEditor).GetField("_url", flags)!.GetValue(editor)!;
        url.Text = "http://h/p?q=%E3%81%82&z=2";
        Application.DoEvents();
        got = editor.GetRequest();
        Eq("http://h/p", got.Url);
        Eq("a=1|x=1|q=あ|z=2", string.Join("|", got.Params.Select(p => $"{p.Key}={p.Value}")));

        // POST で読み込んだだけでは足さない / GET → POST に切り替えたら Content-Type を足す(2回目は足さない)
        EqInt(0, got.Headers.Count);
        var method = (ComboBox)typeof(RequestEditor).GetField("_method", flags)!.GetValue(editor)!;
        method.SelectedItem = "GET";
        method.SelectedItem = "POST";
        Application.DoEvents();
        Eq("Content-Type=application/json", string.Join("|", editor.GetRequest().Headers.Select(h => $"{h.Key}={h.Value}")));
        method.SelectedItem = "GET";
        method.SelectedItem = "POST";
        EqInt(1, editor.GetRequest().Headers.Count);

        // ツリー操作: tags(配列)を選んで子を追加 → 値を入れる → 型をオブジェクトに変えて子を追加
        var tree = (BodyTreeGrid)typeof(RequestEditor).GetField("_body", flags)!.GetValue(editor)!;
        var grid = (DataGridView)typeof(BodyTreeGrid).GetField("_grid", flags)!.GetValue(tree)!;
        void Invoke(string name, params object[] a) => typeof(BodyTreeGrid).GetMethod(name, flags)!.Invoke(tree, a);
        EqInt(3, grid.Rows.Count);                                   // name / tags / [0]
        Eq("[0]", (string)grid.Rows[2].Cells[1].Value);
        grid.CurrentCell = grid.Rows[1].Cells[1];                    // tags
        Invoke("AddChild");
        Application.DoEvents();
        EqInt(4, grid.Rows.Count);
        Eq("[1]", (string)grid.Rows[3].Cells[1].Value);
        EqInt(3, grid.CurrentCell.RowIndex);                         // 追加した行が選択されている
        grid.EndEdit();
        grid.Rows[3].Cells[3].Value = "b";                           // 値を入力
        grid.Rows[3].Cells[2].Value = BodyField.ToLabel(BodyValueType.Object); // 型をオブジェクトへ
        Application.DoEvents();                                      // 型変更は BeginInvoke で反映される
        grid.CurrentCell = grid.Rows[3].Cells[1];
        Invoke("AddChild");
        Application.DoEvents();
        grid.EndEdit();
        grid.Rows[4].Cells[1].Value = "id";
        grid.Rows[4].Cells[2].Value = BodyField.ToLabel(BodyValueType.Number);
        Application.DoEvents();
        grid.Rows[4].Cells[3].Value = "7";
        got = editor.GetRequest();
        Eq("{\"name\":\"taro\",\"tags\":[\"a\",{\"id\":7}]}", Compact(RequestBuilder.BuildJsonBody(got.BodyFields, false, out var errs)));
        EqInt(0, errs.Count);

        // 上へ移動・削除
        grid.CurrentCell = grid.Rows[3].Cells[1];                    // tags[1]
        Invoke("MoveRow", -1);
        got = editor.GetRequest();
        Eq("{\"name\":\"taro\",\"tags\":[{\"id\":7},\"a\"]}", Compact(RequestBuilder.BuildJsonBody(got.BodyFields, false, out _)));
        grid.CurrentCell = grid.Rows[0].Cells[1];                    // name
        Invoke("DeleteRow");
        got = editor.GetRequest();
        Eq("{\"tags\":[{\"id\":7},\"a\"]}", Compact(RequestBuilder.BuildJsonBody(got.BodyFields, false, out _)));

        // ルートを配列に切り替えるとキー列は [0] 表示になる
        var bodyType = (ComboBox)typeof(RequestEditor).GetField("_bodyType", flags)!.GetValue(editor)!;
        bodyType.SelectedIndex = (int)BodyType.JsonArray;
        Application.DoEvents();
        Eq("[0]", (string)grid.Rows[0].Cells[1].Value);
        EqInt((int)BodyType.JsonArray, (int)editor.GetRequest().BodyType);

        // プレビュー
        var preview = (TextBox)typeof(RequestEditor).GetField("_bodyPreview", flags)!.GetValue(editor)!;
        EqInt(1, preview.Text.TrimStart().StartsWith("[") ? 1 : 0);
        var finalUrl = (TextBox)typeof(RequestEditor).GetField("_finalUrl", flags)!.GetValue(editor)!;
        Eq("http://h/p?a=1&x=1&q=%E3%81%82&z=2", finalUrl.Text);
        form.Close();
    }

    private static string Compact(string s) => s.Replace("\n", "").Replace(" ", "");

    private static int FreePort()
    {
        var l = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        l.Start();
        var port = ((System.Net.IPEndPoint)l.LocalEndpoint).Port;
        l.Stop();
        return port;
    }

    // ── 簡易ランナー ──

    private static void Section(string name) => Console.WriteLine($"[{name}]");

    private static void Eq(string expected, string actual, [System.Runtime.CompilerServices.CallerLineNumber] int line = 0)
    {
        if (expected == actual) { _passed++; return; }
        _failed++;
        Console.WriteLine($"  FAIL (line {line}): expected <{expected}> but was <{actual}>");
    }

    private static void EqInt(int expected, int actual, [System.Runtime.CompilerServices.CallerLineNumber] int line = 0)
        => Eq(expected.ToString(), actual.ToString(), line);

    private static void StartsWith(string prefix, string actual, [System.Runtime.CompilerServices.CallerLineNumber] int line = 0)
    {
        if (actual.StartsWith(prefix, StringComparison.Ordinal)) { _passed++; return; }
        _failed++;
        Console.WriteLine($"  FAIL (line {line}): expected to start with <{prefix}> but was <{actual}>");
    }
}

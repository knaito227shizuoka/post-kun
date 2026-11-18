using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace PostKun;

/// <summary>
/// リクエスト組み立ての純粋関数群(UI・通信に依存しない。tests で検証する)。
/// </summary>
public static class RequestBuilder
{
    private static readonly Regex VariablePattern = new(@"\{\{\s*([A-Za-z0-9_.\-]+)\s*\}\}", RegexOptions.Compiled);

    /// <summary>
    /// <c>{{key}}</c> を有効な変数の値で置換する。未定義(または無効)の変数は置換せず残し、名前を <paramref name="missing"/> に集める。
    /// 同名の変数が複数ある場合は先に出てきた有効なものを使う。
    /// </summary>
    public static string ExpandVariables(string text, IEnumerable<KeyValueItem> variables, out List<string> missing)
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var v in variables)
        {
            if (!v.Enabled || string.IsNullOrWhiteSpace(v.Key)) continue;
            map.TryAdd(v.Key.Trim(), v.Value);
        }

        var notFound = new List<string>();
        var result = VariablePattern.Replace(text, m =>
        {
            var name = m.Groups[1].Value;
            if (map.TryGetValue(name, out var value)) return value;
            if (!notFound.Contains(name)) notFound.Add(name);
            return m.Value;
        });
        missing = notFound;
        return result;
    }

    /// <summary>スキームがなければ <c>http://</c> を補い、前後の空白を除く。</summary>
    public static string NormalizeUrl(string url)
    {
        var trimmed = url.Trim();
        if (trimmed.Length == 0) return trimmed;
        return Regex.IsMatch(trimmed, @"^[A-Za-z][A-Za-z0-9+.\-]*://") ? trimmed : "http://" + trimmed;
    }

    /// <summary>
    /// URL を <c>?</c> の前後で分け、クエリをパラメータの行にする(値は URL デコード、<c>+</c> は空白)。
    /// URL 欄で <c>?</c> を禁止しているため、貼り付けられた URL をこれで分解して表へ移す。<c>#fragment</c> は捨てる。
    /// </summary>
    public static string SplitQuery(string url, out List<KeyValueItem> queryParams)
    {
        queryParams = new List<KeyValueItem>();
        var q = url.IndexOf('?');
        if (q < 0) return url;

        var query = url[(q + 1)..];
        var hash = query.IndexOf('#');
        if (hash >= 0) query = query[..hash];

        foreach (var pair in query.Split('&'))
        {
            if (pair.Length == 0) continue;
            var eq = pair.IndexOf('=');
            var key = eq >= 0 ? pair[..eq] : pair;
            var value = eq >= 0 ? pair[(eq + 1)..] : "";
            queryParams.Add(new KeyValueItem(Decode(key), Decode(value)));
        }
        return url[..q];
    }

    private static string Decode(string s)
    {
        try { return Uri.UnescapeDataString(s.Replace('+', ' ')); }
        catch (UriFormatException) { return s; }
    }

    /// <summary>
    /// URL に有効なパラメータをクエリとして付加する(キー・値は URL エンコード)。
    /// 既存のクエリには <c>&amp;</c> で追記し、<c>#fragment</c> は末尾に残す。
    /// </summary>
    public static string BuildUrl(string url, IEnumerable<KeyValueItem> queryParams)
    {
        var normalized = NormalizeUrl(url);
        var query = Encode(queryParams);
        if (query.Length == 0) return normalized;

        var fragment = "";
        var hashIndex = normalized.IndexOf('#');
        if (hashIndex >= 0)
        {
            fragment = normalized[hashIndex..];
            normalized = normalized[..hashIndex];
        }

        var separator = !normalized.Contains('?') ? "?"
            : normalized.EndsWith("?") || normalized.EndsWith("&") ? ""
            : "&";
        return normalized + separator + query + fragment;
    }

    /// <summary>
    /// <c>application/x-www-form-urlencoded</c> の本文を作る。トップレベルの行だけを使い、値は文字列として送る。
    /// null は空文字、オブジェクト / 配列はその中身を1行の JSON 文字列にする。
    /// </summary>
    public static string BuildFormBody(IEnumerable<KeyValueItem> fields) =>
        Encode(fields.Select(f => f switch
        {
            BodyField { Type: BodyValueType.Null } => new KeyValueItem(f.Key, "", f.Enabled),
            BodyField { IsContainer: true } c => new KeyValueItem(f.Key, BuildJson(c.Children ?? new(), c.Type == BodyValueType.Array, false, out _), f.Enabled),
            _ => f,
        }));

    private static string Encode(IEnumerable<KeyValueItem> items)
    {
        var sb = new StringBuilder();
        foreach (var item in items)
        {
            if (!item.Enabled || string.IsNullOrWhiteSpace(item.Key)) continue;
            if (sb.Length > 0) sb.Append('&');
            sb.Append(Uri.EscapeDataString(item.Key.Trim())).Append('=').Append(Uri.EscapeDataString(item.Value));
        }
        return sb.ToString();
    }

    /// <summary>
    /// ボディのツリーから JSON を組み立てる(整形あり、改行は \n)。<paramref name="rootIsArray"/> ならルートを配列にする。
    /// 読めない値・重複キーは <paramref name="errors"/> に「場所: 理由」の日本語で積む(その要素は出力しない)。
    /// オブジェクト内でキーが空の行は未入力とみなして飛ばす。配列の要素はキーを使わない。
    /// </summary>
    public static string BuildJsonBody(IEnumerable<BodyField> fields, bool rootIsArray, out List<string> errors) =>
        BuildJson(fields, rootIsArray, true, out errors);

    private static string BuildJson(IEnumerable<BodyField> fields, bool rootIsArray, bool indented, out List<string> errors)
    {
        var errs = new List<string>();
        var options = new JsonWriterOptions
        {
            Indented = indented,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping, // 日本語をそのまま送る・見せる
        };
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, options))
            WriteContainer(writer, fields, rootIsArray, "", errs);
        errors = errs;
        return Encoding.UTF8.GetString(stream.ToArray()).ReplaceLineEndings("\n");
    }

    private static void WriteContainer(Utf8JsonWriter writer, IEnumerable<BodyField> children, bool isArray, string path, List<string> errors)
    {
        if (isArray)
        {
            writer.WriteStartArray();
            var index = 0;
            foreach (var child in children)
            {
                if (!child.Enabled) continue;
                var childPath = $"{path}[{index++}]";
                if (TryValidate(child, childPath, errors)) WriteValue(writer, child, childPath, errors);
            }
            writer.WriteEndArray();
            return;
        }

        writer.WriteStartObject();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var child in children)
        {
            if (!child.Enabled || string.IsNullOrWhiteSpace(child.Key)) continue;
            var key = child.Key.Trim();
            var childPath = path.Length == 0 ? key : $"{path}.{key}";
            if (!seen.Add(key))
            {
                errors.Add($"「{childPath}」: キーが重複しています");
                continue;
            }
            if (!TryValidate(child, childPath, errors)) continue;
            writer.WritePropertyName(key);
            WriteValue(writer, child, childPath, errors);
        }
        writer.WriteEndObject();
    }

    /// <summary>プロパティ名を書く前に、スカラー値が読めるか確かめる(読めない要素は出力しない)。</summary>
    private static bool TryValidate(BodyField f, string path, List<string> errors)
    {
        var value = f.Value.Trim();
        switch (f.Type)
        {
            case BodyValueType.Boolean when !bool.TryParse(value, out _):
                errors.Add($"「{path}」: 真偽値は true / false で書いてください ({f.Value})");
                return false;
            case BodyValueType.Number when !IsJsonNumber(value):
                errors.Add($"「{path}」: 数値として読めません ({f.Value})");
                return false;
            default:
                return true;
        }
    }

    private static void WriteValue(Utf8JsonWriter writer, BodyField f, string path, List<string> errors)
    {
        switch (f.Type)
        {
            case BodyValueType.String: writer.WriteStringValue(f.Value); break;
            case BodyValueType.Null: writer.WriteNullValue(); break;
            case BodyValueType.Boolean: writer.WriteBooleanValue(bool.Parse(f.Value.Trim())); break;
            case BodyValueType.Number:
                // WriteRawValue は .NET 6 だと配列内で改行・インデントが付かないので、要素として書く(1.5e3 などの表記は保たれる)
                using (var num = JsonDocument.Parse(f.Value.Trim())) num.RootElement.WriteTo(writer);
                break;
            case BodyValueType.Object:
            case BodyValueType.Array:
                WriteContainer(writer, f.Children ?? new List<BodyField>(), f.Type == BodyValueType.Array, path, errors);
                break;
        }
    }

    private static bool IsJsonNumber(string text)
    {
        try
        {
            using var doc = JsonDocument.Parse(text);
            return doc.RootElement.ValueKind == JsonValueKind.Number;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    /// <summary>
    /// JSON 文字列をボディのツリーに変換する(「JSON から取り込み」用)。
    /// ルートはオブジェクトか配列のみ受け付ける。不正なら false。
    /// </summary>
    public static bool TryParseJsonToFields(string json, out List<BodyField> fields, out bool rootIsArray, out string error)
    {
        fields = new List<BodyField>();
        rootIsArray = false;
        error = "";
        try
        {
            using var doc = JsonDocument.Parse(json, new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
            var root = doc.RootElement;
            if (root.ValueKind is not (JsonValueKind.Object or JsonValueKind.Array))
            {
                error = "ルートは連想配列{} か配列[] にしてください。";
                return false;
            }
            rootIsArray = root.ValueKind == JsonValueKind.Array;
            fields = ToFields(root);
            return true;
        }
        catch (JsonException ex)
        {
            error = $"JSON として読めません: {ex.Message}";
            return false;
        }
    }

    private static List<BodyField> ToFields(JsonElement container) =>
        container.ValueKind == JsonValueKind.Array
            ? container.EnumerateArray().Select(e => ToField("", e)).ToList()
            : container.EnumerateObject().Select(p => ToField(p.Name, p.Value)).ToList();

    private static BodyField ToField(string key, JsonElement e) => e.ValueKind switch
    {
        JsonValueKind.Object => new BodyField(key, "", BodyValueType.Object) { Children = ToFields(e) },
        JsonValueKind.Array => new BodyField(key, "", BodyValueType.Array) { Children = ToFields(e) },
        JsonValueKind.String => new BodyField(key, e.GetString() ?? ""),
        JsonValueKind.Number => new BodyField(key, e.GetRawText(), BodyValueType.Number),
        JsonValueKind.True => new BodyField(key, "true", BodyValueType.Boolean),
        JsonValueKind.False => new BodyField(key, "false", BodyValueType.Boolean),
        _ => new BodyField(key, "", BodyValueType.Null),
    };

    /// <summary>
    /// リクエスト定義と環境変数から、送信用の <see cref="HttpRequestMessage"/> を作る。
    /// ボディの値が不正なときは <see cref="RequestBuildException"/>。
    /// </summary>
    public static HttpRequestMessage BuildMessage(RequestDef def, IEnumerable<KeyValueItem> variables, out List<string> missing)
    {
        var vars = variables.ToList();
        var allMissing = new List<string>();
        string Expand(string s)
        {
            var r = ExpandVariables(s, vars, out var m);
            foreach (var name in m) if (!allMissing.Contains(name)) allMissing.Add(name);
            return r;
        }
        List<KeyValueItem> ExpandItems(IEnumerable<KeyValueItem> items) =>
            items.Select(i => new KeyValueItem(Expand(i.Key), Expand(i.Value), i.Enabled)).ToList();
        List<BodyField> ExpandFields(IEnumerable<BodyField> items) =>
            items.Select(i => new BodyField(Expand(i.Key), Expand(i.Value), i.Type, i.Enabled)
            {
                Children = i.Children == null ? null : ExpandFields(i.Children),
            }).ToList();

        var url = BuildUrl(Expand(def.Url), ExpandItems(def.Params));
        var message = new HttpRequestMessage(new HttpMethod(def.Method.ToUpperInvariant()), url);

        switch (def.BodyType)
        {
            case BodyType.Json:
            case BodyType.JsonArray:
                var json = BuildJsonBody(ExpandFields(def.BodyFields), def.BodyType == BodyType.JsonArray, out var errors);
                if (errors.Count > 0)
                {
                    message.Dispose();
                    throw new RequestBuildException("ボディを組み立てられません。\r\n" + string.Join("\r\n", errors));
                }
                message.Content = new StringContent(json, Encoding.UTF8, "application/json");
                break;
            case BodyType.Form:
                message.Content = new StringContent(BuildFormBody(ExpandFields(def.BodyFields)), Encoding.UTF8, "application/x-www-form-urlencoded");
                break;
        }

        foreach (var h in ExpandItems(def.Headers))
        {
            if (!h.Enabled || string.IsNullOrWhiteSpace(h.Key)) continue;
            var key = h.Key.Trim();
            // Content-Type などのコンテンツヘッダは Content 側へ入れる必要がある
            if (!message.Headers.TryAddWithoutValidation(key, h.Value) && message.Content != null)
            {
                message.Content.Headers.Remove(key);
                message.Content.Headers.TryAddWithoutValidation(key, h.Value);
            }
        }

        missing = allMissing;
        return message;
    }

    /// <summary>
    /// メソッドを切り替えたときに自動で足すヘッダー。POST で Content-Type がまだ無ければ
    /// <c>Content-Type: application/json</c>。足す必要がなければ null(無効の行も「ある」とみなす)。
    /// フォーム形式のボディでは足さない(表のヘッダーが優先され、フォームを JSON として送ってしまうため)。
    /// </summary>
    public static KeyValueItem? DefaultHeaderForMethod(string method, BodyType bodyType, IEnumerable<KeyValueItem> headers)
    {
        if (!string.Equals(method, "POST", StringComparison.OrdinalIgnoreCase) || bodyType == BodyType.Form) return null;
        if (headers.Any(h => string.Equals(h.Key.Trim(), "Content-Type", StringComparison.OrdinalIgnoreCase))) return null;
        return new KeyValueItem("Content-Type", "application/json", true);
    }
}

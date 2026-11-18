using System.Globalization;
using System.Text.Json.Serialization;

namespace PostKun;

/// <summary>key / value / 有効 の1行。パラメータ・ヘッダーで使う。</summary>
public class KeyValueItem
{
    public string Key { get; set; } = "";
    public string Value { get; set; } = "";
    public bool Enabled { get; set; } = true;

    public KeyValueItem() { }
    public KeyValueItem(string key, string value, bool enabled = true)
    {
        Key = key;
        Value = value;
        Enabled = enabled;
    }
}

/// <summary>ボディの値の型。Object / Array は子要素(<see cref="BodyField.Children"/>)を持つ。</summary>
public enum BodyValueType { String, Number, Boolean, Null, Object, Array }

/// <summary>
/// ボディの1行(ツリーの1ノード)。値の型と、オブジェクト・配列なら子要素を持つ。
/// 配列の子のキーは使わない(表では [0] [1] … と表示)。
/// </summary>
public class BodyField : KeyValueItem
{
    public BodyValueType Type { get; set; } = BodyValueType.String;

    /// <summary>子要素。Object / Array のときだけ使う(それ以外は null にして保存ファイルを簡潔にする)。</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<BodyField>? Children { get; set; }

    [JsonIgnore]
    public bool IsContainer => Type is BodyValueType.Object or BodyValueType.Array;

    public BodyField() { }
    public BodyField(string key, string value, BodyValueType type = BodyValueType.String, bool enabled = true)
        : base(key, value, enabled) => Type = type;

    /// <summary>オブジェクト / 配列のノードを子要素付きで作る。</summary>
    public static BodyField Container(string key, BodyValueType type, params BodyField[] children) =>
        new(key, "", type) { Children = children.ToList() };

    /// <summary>表の「型」列に表示する名前(enum の順と対応)。</summary>
    public static readonly string[] TypeLabels = { "文字列", "数値", "真偽値", "null", "連想配列{}", "配列[]" };

    public static string ToLabel(BodyValueType type) => TypeLabels[(int)type];

    public static BodyValueType FromLabel(string? label)
    {
        var index = Array.IndexOf(TypeLabels, label);
        return index >= 0 ? (BodyValueType)index : BodyValueType.String;
    }
}

/// <summary>ボディの形式。Json はルートがオブジェクト、JsonArray はルートが配列。</summary>
public enum BodyType { None, Json, JsonArray, Form }

/// <summary>編集・保存の単位となるリクエスト定義。</summary>
public class RequestDef
{
    public string Name { get; set; } = "";
    public string Method { get; set; } = "GET";
    /// <summary>クエリ(?以降)を含まない URL。クエリは <see cref="Params"/> に持つ。</summary>
    public string Url { get; set; } = "";
    public List<KeyValueItem> Params { get; set; } = new();
    public List<KeyValueItem> Headers { get; set; } = new();
    public BodyType BodyType { get; set; } = BodyType.None;
    public List<BodyField> BodyFields { get; set; } = new();

    public static readonly string[] Methods = { "GET", "POST", "PUT", "PATCH", "DELETE", "HEAD", "OPTIONS" };

    private static readonly CompareInfo JaCompare = CultureInfo.GetCultureInfo("ja-JP").CompareInfo;

    /// <summary>名前順(日本語の並び・大文字小文字無視)。同順なら序数比較で安定させる。</summary>
    public static readonly Comparison<RequestDef> ByName = (a, b) =>
    {
        var c = JaCompare.Compare(a.Name, b.Name, CompareOptions.IgnoreCase | CompareOptions.IgnoreWidth | CompareOptions.IgnoreKanaType);
        return c != 0 ? c : string.CompareOrdinal(a.Name, b.Name);
    };

    /// <summary>「名前 のコピー」「名前 のコピー 2」… の重複しない名前を作る。</summary>
    public static string MakeCopyName(string name, IEnumerable<string> existing)
        => MakeUniqueName($"{name} のコピー", existing);

    /// <summary>「新規」で作るリクエストの名前。</summary>
    public const string UntitledName = "無名のリクエスト";

    /// <summary><paramref name="name"/> が空いていればそのまま、使われていれば「name 2」「name 3」… の空いている名前(大文字小文字無視)。</summary>
    public static string MakeUniqueName(string name, IEnumerable<string> existing)
    {
        var used = new HashSet<string>(existing, StringComparer.OrdinalIgnoreCase);
        var candidate = name;
        for (var i = 2; used.Contains(candidate); i++) candidate = $"{name} {i}";
        return candidate;
    }
}

/// <summary>受信したレスポンスの表示用データ。</summary>
public class ResponseResult
{
    public int StatusCode { get; init; }
    public string ReasonPhrase { get; init; } = "";
    public long ElapsedMs { get; init; }
    public long SizeBytes { get; init; }
    public string? ContentType { get; init; }
    public List<KeyValueItem> Headers { get; init; } = new();
    public string BodyText { get; init; } = "";
}

/// <summary>リクエストを組み立てられない(ボディの値が不正など)ときの例外。メッセージはそのまま利用者に見せる。</summary>
public class RequestBuildException : Exception
{
    public RequestBuildException(string message) : base(message) { }
}

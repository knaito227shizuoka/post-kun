using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PostKun;

/// <summary>保存済みリクエスト一覧(requests.json)。</summary>
public class RequestStore
{
    public List<RequestDef> Requests { get; set; } = new();
}

/// <summary>ウィンドウ状態など(settings.json)。</summary>
public class AppSettings
{
    public int? WindowX { get; set; }
    public int? WindowY { get; set; }
    public int? WindowWidth { get; set; }
    public int? WindowHeight { get; set; }
    public bool Maximized { get; set; }
    public int? LeftPaneWidth { get; set; }
    public int? EditorHeight { get; set; }
    public string? LastSelected { get; set; }
}

/// <summary>
/// JSON ファイルの読み書き。保存先は既定で <c>%APPDATA%\post-kun\</c>(テストでは一時フォルダを渡す)。
/// 読めないファイルは <c>*.bak</c> へ退避して初期値を返す。書き込みは一時ファイル経由で行い、途中失敗で壊さない。
/// </summary>
public sealed class Storage
{
    public const string RequestsFile = "requests.json";
    public const string SettingsFile = "settings.json";

    public string Directory { get; }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping, // 日本語の名前をファイル上でも読めるように
        Converters = { new JsonStringEnumConverter() },
    };

    public Storage(string? directory = null)
    {
        Directory = directory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "post-kun");
    }

    public T Load<T>(string fileName) where T : new()
    {
        var path = Path.Combine(Directory, fileName);
        try
        {
            if (!File.Exists(path)) return new T();
            return JsonSerializer.Deserialize<T>(File.ReadAllText(path), JsonOptions) ?? new T();
        }
        catch
        {
            // 次の保存で上書きされて失う前に退避しておく(ベストエフォート)
            try { File.Copy(path, path + ".bak", overwrite: true); } catch { }
            return new T();
        }
    }

    public void Save<T>(string fileName, T value)
    {
        System.IO.Directory.CreateDirectory(Directory);
        var path = Path.Combine(Directory, fileName);
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(value, JsonOptions));
        File.Move(tmp, path, overwrite: true);
    }

    /// <summary>保存済みリクエストを読み込む(名前順)。</summary>
    public List<RequestDef> LoadRequests()
    {
        var list = Load<RequestStore>(RequestsFile).Requests;
        list.Sort(RequestDef.ByName);
        return list;
    }

    /// <summary>保存済みリクエストを書き込む(名前順に並べ替えてから)。</summary>
    public void SaveRequests(List<RequestDef> requests)
    {
        requests.Sort(RequestDef.ByName);
        Save(RequestsFile, new RequestStore { Requests = requests });
    }

    /// <summary>比較・複製用のシリアライズ(保存形式と同じ)。</summary>
    public static string Serialize(RequestDef def) => JsonSerializer.Serialize(def, JsonOptions);

    public static RequestDef Clone(RequestDef def) =>
        JsonSerializer.Deserialize<RequestDef>(Serialize(def), JsonOptions)!;
}

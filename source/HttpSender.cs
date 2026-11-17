using System.Diagnostics;
using System.Net.Sockets;
using System.Text;

namespace PostKun;

/// <summary>
/// HttpClient の管理と送信。HttpClient はアプリ内で使い回す。
/// タイムアウトは HttpClient.Timeout ではなく CancellationToken で行い、利用者のキャンセルと区別する。
/// </summary>
public sealed class HttpSender : IDisposable
{
    private readonly HttpClient _client;

    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(30);

    // Shift_JIS / EUC-JP など日本の自作アプリが返しがちな charset を読めるようにする
    static HttpSender() => Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

    public HttpSender()
    {
        var handler = new HttpClientHandler { AllowAutoRedirect = true };
        _client = new HttpClient(handler) { Timeout = System.Threading.Timeout.InfiniteTimeSpan };
    }

    /// <summary>送信してレスポンスを返す。タイムアウト時は <see cref="TimeoutException"/>、キャンセル時は <see cref="OperationCanceledException"/>。</summary>
    public async Task<ResponseResult> SendAsync(HttpRequestMessage request, CancellationToken userToken)
    {
        using var timeoutCts = new CancellationTokenSource(Timeout);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(userToken, timeoutCts.Token);
        var sw = Stopwatch.StartNew();
        try
        {
            using var response = await _client.SendAsync(request, HttpCompletionOption.ResponseContentRead, linked.Token);
            var bytes = await response.Content.ReadAsByteArrayAsync(linked.Token);
            sw.Stop();

            var headers = new List<KeyValueItem>();
            foreach (var h in response.Headers) headers.Add(new KeyValueItem(h.Key, string.Join(", ", h.Value)));
            foreach (var h in response.Content.Headers) headers.Add(new KeyValueItem(h.Key, string.Join(", ", h.Value)));

            return new ResponseResult
            {
                StatusCode = (int)response.StatusCode,
                ReasonPhrase = response.ReasonPhrase ?? "",
                ElapsedMs = sw.ElapsedMilliseconds,
                SizeBytes = bytes.LongLength,
                ContentType = response.Content.Headers.ContentType?.ToString(),
                Headers = headers,
                BodyText = DecodeBody(bytes, response.Content.Headers.ContentType?.CharSet),
            };
        }
        catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested && !userToken.IsCancellationRequested)
        {
            throw new TimeoutException($"{Timeout.TotalSeconds:0} 秒以内に応答がありませんでした。");
        }
    }

    /// <summary>charset に従って本文をデコードする。不明・未指定なら UTF-8。</summary>
    internal static string DecodeBody(byte[] bytes, string? charset)
    {
        var encoding = Encoding.UTF8;
        if (!string.IsNullOrWhiteSpace(charset))
        {
            try { encoding = Encoding.GetEncoding(charset.Trim('"', ' ')); }
            catch (ArgumentException) { /* 未知の charset は UTF-8 で読む */ }
        }
        return encoding.GetString(bytes);
    }

    /// <summary>送信時の例外を利用者向けの日本語メッセージにする。</summary>
    public static string DescribeError(Exception ex)
    {
        switch (ex)
        {
            case TimeoutException:
                return "タイムアウト: " + ex.Message;
            case OperationCanceledException:
                return "キャンセルしました。";
            case UriFormatException:
            case InvalidOperationException when ex.Message.Contains("URI", StringComparison.OrdinalIgnoreCase):
                return "URL の形式が正しくありません。";
            case HttpRequestException hre:
                var socket = FindInner<SocketException>(hre);
                if (socket != null)
                {
                    return socket.SocketErrorCode switch
                    {
                        SocketError.ConnectionRefused => "接続を拒否されました。サーバが起動しているか、ポート番号が正しいか確認してください。",
                        SocketError.HostNotFound or SocketError.NoData => "ホスト名が見つかりません。URL を確認してください。",
                        SocketError.TimedOut => "接続がタイムアウトしました。",
                        _ => $"通信エラー: {socket.Message}",
                    };
                }
                if (FindInner<System.Security.Authentication.AuthenticationException>(hre) != null)
                    return "SSL/TLS の検証に失敗しました(自己署名証明書の可能性があります)。";
                return $"通信エラー: {hre.Message}";
            default:
                return $"エラー: {ex.Message}";
        }
    }

    private static T? FindInner<T>(Exception ex) where T : Exception
    {
        for (var e = ex.InnerException; e != null; e = e.InnerException)
            if (e is T t) return t;
        return null;
    }

    public void Dispose() => _client.Dispose();
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace Drops.Services;

public sealed class DropFile
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("size")] public long Size { get; set; }
    [JsonPropertyName("expires")] public long Expires { get; set; } // unix seconds
    [JsonPropertyName("inline")] public bool Inline { get; set; }
    [JsonPropertyName("text")] public string? Text { get; set; }
}

public sealed class DropList
{
    [JsonPropertyName("files")] public DropFile[] Files { get; set; } = [];
    [JsonPropertyName("used")] public long Used { get; set; }
    [JsonPropertyName("max")] public long Max { get; set; }
    [JsonPropertyName("ttl")] public long Ttl { get; set; }
    [JsonPropertyName("user")] public string User { get; set; } = "";
    [JsonPropertyName("admin")] public bool Admin { get; set; }
}

/// <summary>What came back from a call: the HTTP status (0 when the server did not answer) and its text.</summary>
public readonly record struct Reply(int Status, string Text)
{
    public bool Ok => Status is >= 200 and < 300;
    public bool NoAnswer => Status == 0;
}

/// <summary>
/// The Drops server API, as the web page uses it. The session is the server's own
/// drop_session cookie, kept here by hand so it can be saved between runs.
/// </summary>
public sealed class DropsClient
{
    const string CookieName = "drop_session";
    static readonly TimeSpan Short = TimeSpan.FromSeconds(20);

    readonly HttpClient http;
    public Uri Base { get; }
    public string? Token { get; private set; }

    public DropsClient(Uri baseUri, string? token)
    {
        Base = baseUri;
        Token = token;
        // Cookies are handled here, not by the handler; uploads and downloads have no overall time limit.
        http = new HttpClient(new SocketsHttpHandler { UseCookies = false, ConnectTimeout = TimeSpan.FromSeconds(15) })
        {
            BaseAddress = baseUri,
            Timeout = Timeout.InfiniteTimeSpan,
        };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("DropsDesktop/" + AppInfo.Version);
    }

    string Origin => Base.GetLeftPart(UriPartial.Authority);

    HttpRequestMessage Request(HttpMethod method, string path)
    {
        var req = new HttpRequestMessage(method, path);
        if (Token != null) req.Headers.TryAddWithoutValidation("Cookie", CookieName + "=" + Token);
        // The server refuses password changes that do not name their own origin.
        if (method != HttpMethod.Get) req.Headers.TryAddWithoutValidation("Origin", Origin);
        return req;
    }

    /// <summary>Picks a fresh session cookie out of a response. An emptied cookie signs out.</summary>
    bool TakeCookie(HttpResponseMessage res)
    {
        if (!res.Headers.TryGetValues("Set-Cookie", out var cookies)) return false;
        foreach (var c in cookies)
        {
            if (!c.StartsWith(CookieName + "=", StringComparison.Ordinal)) continue;
            var value = c[(CookieName.Length + 1)..].Split(';')[0].Trim();
            Token = value.Length == 0 ? null : value;
            if (Token != null) SessionStore.Save(Base, Token);
            else SessionStore.Clear();
            return true;
        }
        return false;
    }

    async Task<Reply> Send(HttpRequestMessage req, CancellationToken ct = default)
    {
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(ct);
        limit.CancelAfter(Short);
        try
        {
            using (req)
            using (var res = await http.SendAsync(req, limit.Token).ConfigureAwait(false))
            {
                TakeCookie(res);
                var text = await res.Content.ReadAsStringAsync(limit.Token).ConfigureAwait(false);
                return new Reply((int)res.StatusCode, text.Trim());
            }
        }
        catch (Exception e) when (e is HttpRequestException or IOException or OperationCanceledException)
        {
            return new Reply(0, "");
        }
    }

    static StringContent JsonBody(object value) => new(JsonSerializer.Serialize(value), Encoding.UTF8, "application/json");

    public void Forget()
    {
        Token = null;
        SessionStore.Clear();
    }

    public async Task<Reply> Login(string user, string pass)
    {
        var req = Request(HttpMethod.Post, "api/login");
        req.Content = JsonBody(new { user, pass });
        return await Send(req).ConfigureAwait(false);
    }

    public async Task Logout()
    {
        await Send(Request(HttpMethod.Post, "api/logout")).ConfigureAwait(false);
        Forget();
    }

    public async Task<(Reply reply, DropList? list)> List()
    {
        var reply = await Send(Request(HttpMethod.Get, "api/list")).ConfigureAwait(false);
        if (!reply.Ok) return (reply, null);
        try
        {
            return (reply, JsonSerializer.Deserialize<DropList>(reply.Text));
        }
        catch (JsonException)
        {
            // Not Drops answering (a captive portal, a proxy error page): treat as no answer.
            return (new Reply(0, ""), null);
        }
    }

    public Task<Reply> Delete(string id) => Send(Request(HttpMethod.Delete, "api/file/" + id));
    public Task<Reply> DeleteAll() => Send(Request(HttpMethod.Delete, "api/all"));

    public Task<Reply> ChangePassword(string current, string @new)
    {
        var req = Request(HttpMethod.Post, "api/password");
        req.Content = JsonBody(new Dictionary<string, string> { ["current"] = current, ["new"] = @new });
        return Send(req);
    }

    /// <summary>Sends one file. <paramref name="progress"/> gets (sent, total) as the bytes go out.</summary>
    public async Task<Reply> Upload(Stream source, long size, string name, Action<long, long> progress, CancellationToken ct = default)
    {
        try
        {
            using var req = Request(HttpMethod.Post, "api/upload?name=" + Uri.EscapeDataString(name));
            req.Content = new ProgressContent(source, size, progress);
            using var res = await http.SendAsync(req, ct).ConfigureAwait(false);
            var text = await res.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            return new Reply((int)res.StatusCode, text.Trim());
        }
        catch (Exception e) when (e is HttpRequestException or IOException or OperationCanceledException)
        {
            return new Reply(0, "");
        }
    }

    /// <summary>Fetches a stored file into <paramref name="target"/>.</summary>
    public async Task<Reply> Download(string id, Stream target, CancellationToken ct = default)
    {
        try
        {
            using var req = Request(HttpMethod.Get, "api/file/" + id);
            using var res = await http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
            if (!res.IsSuccessStatusCode) return new Reply((int)res.StatusCode, "");
            await using var body = await res.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            await body.CopyToAsync(target, ct).ConfigureAwait(false);
            return new Reply(200, "");
        }
        catch (Exception e) when (e is HttpRequestException or IOException or OperationCanceledException)
        {
            return new Reply(0, "");
        }
    }

    /// <summary>
    /// Starts fetching everything in the drop as one zip (GET /api/all.zip). Only the headers are read,
    /// so the caller can ask where to save it before any of the body is fetched. On success the open
    /// response is handed back with the server's suggested file name; the caller disposes it.
    /// A 404 with the text "the drop is empty" means there is nothing to send. Any other 404 means
    /// the server is older than "Download all".
    /// </summary>
    public async Task<(Reply reply, HttpResponseMessage? response, string fileName)> OpenAll(CancellationToken ct = default)
    {
        var req = Request(HttpMethod.Get, "api/all.zip");
        HttpResponseMessage? res = null;
        try
        {
            res = await http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
            if (!res.IsSuccessStatusCode)
            {
                using var limit = CancellationTokenSource.CreateLinkedTokenSource(ct);
                limit.CancelAfter(Short);
                var text = (await res.Content.ReadAsStringAsync(limit.Token).ConfigureAwait(false)).Trim();
                var reply = new Reply((int)res.StatusCode, text);
                res.Dispose();
                return (reply, null, "");
            }
            var name = res.Content.Headers.ContentDisposition?.FileNameStar
                       ?? res.Content.Headers.ContentDisposition?.FileName?.Trim('"');
            if (string.IsNullOrWhiteSpace(name))
                name = "drops-" + DateTime.Now.ToString("yyyyMMdd-HHmm", System.Globalization.CultureInfo.InvariantCulture) + ".zip";
            return (new Reply(200, ""), res, Path.GetFileName(name));
        }
        catch (Exception e) when (e is HttpRequestException or IOException or OperationCanceledException)
        {
            res?.Dispose();
            return (new Reply(0, ""), null, "");
        }
        finally
        {
            req.Dispose();
        }
    }

    /// <summary>Copies the body of a response from <see cref="OpenAll"/> into <paramref name="target"/>.</summary>
    public static async Task<Reply> Finish(HttpResponseMessage response, Stream target, CancellationToken ct = default)
    {
        try
        {
            await using var body = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            await body.CopyToAsync(target, ct).ConfigureAwait(false);
            return new Reply(200, "");
        }
        catch (Exception e) when (e is HttpRequestException or IOException or OperationCanceledException)
        {
            return new Reply(0, "");
        }
    }

    /// <summary>A request body of known length that reports how much of it has been written.</summary>
    sealed class ProgressContent : HttpContent
    {
        readonly Stream source;
        readonly long size;
        readonly Action<long, long> progress;

        public ProgressContent(Stream source, long size, Action<long, long> progress)
        {
            this.source = source;
            this.size = size;
            this.progress = progress;
            Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
            Headers.ContentLength = size;
        }

        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) =>
            SerializeToStreamAsync(stream, context, CancellationToken.None);

        protected override async Task SerializeToStreamAsync(Stream stream, TransportContext? context, CancellationToken ct)
        {
            var buffer = new byte[81920];
            long sent = 0;
            int n;
            while ((n = await source.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
            {
                await stream.WriteAsync(buffer.AsMemory(0, n), ct).ConfigureAwait(false);
                sent += n;
                progress(sent, size);
            }
        }

        protected override bool TryComputeLength(out long length)
        {
            length = size;
            return true;
        }
    }
}

public static class AppInfo
{
    /// <summary>The version from the .csproj, read back off the assembly.</summary>
    public static string Version { get; } =
        (typeof(AppInfo).Assembly.GetName().Version is { } v ? $"{v.Major}.{v.Minor}.{v.Build}" : "dev");
}

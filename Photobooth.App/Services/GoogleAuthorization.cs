using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Photobooth.App.Services;

public sealed class GoogleAuthorization
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(30) };
    private sealed record Client(string Id, string Secret);
    private sealed record Token(string ClientId, string RefreshToken);
    private string TokenPath => Path.Combine(SettingsStore.DataDirectory, "google.tokens");

    private static Client ReadClient(string path)
    {
        if (!File.Exists(path)) throw new InvalidOperationException("Chọn file OAuth Desktop App JSON trong cài đặt trước.");
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        if (!doc.RootElement.TryGetProperty("installed", out var app))
            throw new InvalidDataException("Cần JSON OAuth loại Desktop App, không phải service account hoặc Web App.");
        return new Client(app.GetProperty("client_id").GetString()!, app.GetProperty("client_secret").GetString()!);
    }

    public async Task SignInAsync(string credentialsPath, CancellationToken token)
    {
        var client = ReadClient(credentialsPath);
        var verifier = Base64Url(RandomNumberGenerator.GetBytes(48));
        var challenge = Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
        var state = Base64Url(RandomNumberGenerator.GetBytes(32));
        var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start(); var port = ((IPEndPoint)probe.LocalEndpoint).Port; probe.Stop();
        var redirect = $"http://127.0.0.1:{port}/";
        using var listener = new HttpListener();
        listener.Prefixes.Add(redirect); listener.Start();
        var parameters = new Dictionary<string, string>
        {
            ["client_id"] = client.Id, ["redirect_uri"] = redirect, ["response_type"] = "code",
            ["scope"] = "https://www.googleapis.com/auth/drive", ["access_type"] = "offline",
            ["prompt"] = "consent", ["code_challenge"] = challenge, ["code_challenge_method"] = "S256", ["state"] = state
        };
        var url = "https://accounts.google.com/o/oauth2/v2/auth?" + string.Join("&", parameters.Select(p => $"{Uri.EscapeDataString(p.Key)}={Uri.EscapeDataString(p.Value)}"));
        Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromMinutes(3));
        string? code = null;
        while (code is null)
        {
            var context = await listener.GetContextAsync().WaitAsync(timeout.Token);
            var valid = context.Request.QueryString["state"] == state;
            code = valid ? context.Request.QueryString["code"] : null;
            var denied = valid && context.Request.QueryString["error"] is not null;
            var message = code is not null ? "Đã nhận đăng nhập. Bạn có thể quay lại Photobooth VHU." : "Yêu cầu đăng nhập không hợp lệ hoặc đã bị từ chối.";
            var bytes = Encoding.UTF8.GetBytes(message);
            context.Response.ContentType = "text/plain; charset=utf-8";
            context.Response.StatusCode = code is not null ? 200 : 400;
            await context.Response.OutputStream.WriteAsync(bytes, timeout.Token);
            context.Response.Close();
            if (denied) throw new InvalidOperationException("Bạn đã từ chối quyền Google Drive.");
        }
        using var result = await PostTokenAsync(new()
        {
            ["client_id"] = client.Id, ["client_secret"] = client.Secret, ["code"] = code,
            ["code_verifier"] = verifier, ["redirect_uri"] = redirect, ["grant_type"] = "authorization_code"
        }, timeout.Token);
        if (!result.RootElement.TryGetProperty("refresh_token", out var refresh))
            throw new InvalidOperationException("Google không trả refresh token. Hãy đăng nhập lại và cấp quyền.");
        Directory.CreateDirectory(SettingsStore.DataDirectory);
        var serialized = JsonSerializer.SerializeToUtf8Bytes(new Token(client.Id, refresh.GetString()!));
        var encrypted = ProtectedData.Protect(serialized, null, DataProtectionScope.CurrentUser);
        var temporary = TokenPath + ".tmp";
        await File.WriteAllBytesAsync(temporary, encrypted, token);
        File.Move(temporary, TokenPath, true);
    }

    public async Task<string> AccessTokenAsync(string credentialsPath, CancellationToken token)
    {
        var client = ReadClient(credentialsPath);
        if (!File.Exists(TokenPath)) throw new InvalidOperationException("Chưa đăng nhập Google. Mở cài đặt và bấm Đăng nhập Google.");
        var stored = JsonSerializer.Deserialize<Token>(ProtectedData.Unprotect(await File.ReadAllBytesAsync(TokenPath, token), null, DataProtectionScope.CurrentUser));
        if (stored is null || stored.ClientId != client.Id) throw new InvalidOperationException("Cấu hình OAuth đã đổi. Hãy đăng nhập Google lại.");
        using var result = await PostTokenAsync(new()
        {
            ["client_id"] = client.Id, ["client_secret"] = client.Secret,
            ["refresh_token"] = stored.RefreshToken, ["grant_type"] = "refresh_token"
        }, token);
        return result.RootElement.GetProperty("access_token").GetString()!;
    }

    private static async Task<JsonDocument> PostTokenAsync(Dictionary<string, string> values, CancellationToken token)
    {
        using var body = new FormUrlEncodedContent(values);
        using var response = await Http.PostAsync("https://oauth2.googleapis.com/token", body, token);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"Google OAuth lỗi {(int)response.StatusCode}. Kiểm tra ứng dụng OAuth và đăng nhập lại.");
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync(token));
    }
    private static string Base64Url(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}

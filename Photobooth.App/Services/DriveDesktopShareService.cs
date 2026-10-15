using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using Photobooth.App.Models;

namespace Photobooth.App.Services;

public sealed class DriveDesktopShareService : IDisposable
{
    private readonly GoogleAuthorization _authorization = new();
    private readonly HttpClient _http = new() { BaseAddress = new Uri("https://www.googleapis.com/drive/v3/"), Timeout = TimeSpan.FromSeconds(30) };
    private string? _access;
    private string _credentialsPath = "";
    private sealed record LocalFile(string Name, long Size, string Md5);

    public async Task<string> WaitAndShareAsync(string directory, SessionSnapshot session, IProgress<string> progress, CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(session.DriveParentId)) throw new InvalidOperationException("Chưa cấu hình Folder ID thư mục cha trên Drive.");
        _credentialsPath = session.GoogleCredentialsPath;
        var expected = new List<LocalFile>();
        foreach (var name in new[] { "photo_01.jpg", "photo_02.jpg", "photo_03.jpg", "photo_04.jpg", "photobooth_vhu.jpg" })
        {
            var path = Path.Combine(directory, name);
            using var file = File.OpenRead(path);
            expected.Add(new LocalFile(name, file.Length, Convert.ToHexString(await MD5.HashDataAsync(file, token)).ToLowerInvariant()));
        }
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(TimeSpan.FromSeconds(session.SyncTimeoutSeconds));
        try
        {
            string? folderId = null;
            while (true)
            {
                deadline.Token.ThrowIfCancellationRequested();
                try
                {
                    progress.Report("Đang chờ Drive for desktop đồng bộ thư mục...");
                    var folders = await ListFilesAsync($"'{Escape(session.DriveParentId)}' in parents and trashed = false and mimeType = 'application/vnd.google-apps.folder' and name = '{Escape(Path.GetFileName(directory))}'", deadline.Token);
                    if (folders.Count > 1) throw new InvalidOperationException("Có nhiều thư mục phiên trùng tên trên Drive. Không thể tự chọn để chia sẻ an toàn.");
                    if (folders.Count == 1)
                    {
                        folderId = folders[0].GetProperty("id").GetString()!;
                        var remote = await ListFilesAsync($"'{Escape(folderId)}' in parents and trashed = false", deadline.Token);
                        var matched = expected.Count(local => remote.Count(file => Matches(file, local)) == 1
                            && remote.Count(file => file.GetProperty("name").GetString() == local.Name) == 1);
                        progress.Report($"Đang đồng bộ: {matched}/5 file đã khớp nội dung trên Drive...");
                        if (matched == 5 && remote.Count == 5) break;
                    }
                }
                catch (HttpRequestException ex) when (ex.StatusCode is null or HttpStatusCode.TooManyRequests || (int)ex.StatusCode.Value >= 500)
                { progress.Report("Kết nối Drive gián đoạn, đang thử lại..."); }
                await Task.Delay(2000, deadline.Token);
            }
            progress.Report("Đã đồng bộ đủ 5 file. Đang tạo liên kết chia sẻ...");
            using var permissions = await GetAsync($"files/{Uri.EscapeDataString(folderId!)}/permissions?supportsAllDrives=true&fields=permissions(id,type,role)", deadline.Token);
            var hasLink = permissions.RootElement.GetProperty("permissions").EnumerateArray().Any(p =>
                p.TryGetProperty("type", out var type) && type.GetString() == "anyone" &&
                p.TryGetProperty("role", out var role) && role.GetString() is "reader" or "writer" or "commenter");
            if (!hasLink)
            {
                using var permission = await RequestAsync(() => new HttpRequestMessage(HttpMethod.Post,
                    $"files/{Uri.EscapeDataString(folderId!)}/permissions?supportsAllDrives=true")
                { Content = JsonContent.Create(new { type = "anyone", role = "reader", allowFileDiscovery = false }) }, deadline.Token);
            }
            using var folder = await GetAsync($"files/{Uri.EscapeDataString(folderId!)}?supportsAllDrives=true&fields=webViewLink", deadline.Token);
            var url = folder.RootElement.GetProperty("webViewLink").GetString();
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != "https" || uri.Host != "drive.google.com")
                throw new InvalidDataException("Drive chưa trả về liên kết thư mục hợp lệ.");
            return uri.AbsoluteUri;
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested)
        { throw new TimeoutException("Hết thời gian chờ Drive. Ảnh vẫn được giữ trên máy. Kiểm tra Drive for desktop và mạng rồi bấm Thử lại."); }
    }

    private static bool Matches(JsonElement remote, LocalFile local) =>
        remote.GetProperty("name").GetString() == local.Name &&
        remote.TryGetProperty("size", out var size) && long.TryParse(size.GetString(), out var bytes) && bytes == local.Size &&
        remote.TryGetProperty("md5Checksum", out var md5) && string.Equals(md5.GetString(), local.Md5, StringComparison.OrdinalIgnoreCase);

    private async Task<List<JsonElement>> ListFilesAsync(string query, CancellationToken token)
    {
        var files = new List<JsonElement>();
        string? page = null;
        do
        {
            var path = "files?supportsAllDrives=true&includeItemsFromAllDrives=true&pageSize=100&fields=" + Uri.EscapeDataString("nextPageToken,files(id,name,size,md5Checksum)") + "&q=" + Uri.EscapeDataString(query);
            if (page is not null) path += "&pageToken=" + Uri.EscapeDataString(page);
            using var result = await GetAsync(path, token);
            files.AddRange(result.RootElement.GetProperty("files").EnumerateArray().Select(file => file.Clone()));
            page = result.RootElement.TryGetProperty("nextPageToken", out var next) ? next.GetString() : null;
        } while (page is not null);
        return files;
    }

    private Task<JsonDocument> GetAsync(string path, CancellationToken token) =>
        RequestAsync(() => new HttpRequestMessage(HttpMethod.Get, path), token);

    private async Task<JsonDocument> RequestAsync(Func<HttpRequestMessage> create, CancellationToken token)
    {
        for (var attempt = 0; attempt < 2; attempt++)
        {
            _access ??= await _authorization.AccessTokenAsync(_credentialsPath, token);
            using var request = create();
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _access);
            using var response = await _http.SendAsync(request, token);
            if (response.StatusCode == HttpStatusCode.Unauthorized && attempt == 0) { _access = null; continue; }
            if (!response.IsSuccessStatusCode)
            {
                var detail = response.StatusCode == HttpStatusCode.Forbidden
                    ? "Tài khoản không có quyền hoặc chính sách Drive không cho chia sẻ bằng liên kết."
                    : "Kiểm tra đăng nhập, Folder ID, mạng và Drive API.";
                throw new HttpRequestException($"Google Drive lỗi {(int)response.StatusCode}. {detail}", null, response.StatusCode);
            }
            return JsonDocument.Parse(await response.Content.ReadAsStringAsync(token));
        }
        throw new InvalidOperationException("Phiên đăng nhập Google hết hiệu lực. Đăng nhập lại trong cài đặt.");
    }
    private static string Escape(string text) => text.Replace("\\", "\\\\").Replace("'", "\\'");
    public void Dispose() => _http.Dispose();
}

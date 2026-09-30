using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;

namespace PromptixCapture.Services;

public sealed record UpdateRelease(Version Version, Uri Page, Uri? InstallerUrl, long Size, string? Sha256)
{
    public bool CanInstall => InstallerUrl is not null && Sha256 is not null;
}

// Trust release metadata only from this repository over HTTPS. The hash detects
// damaged/substituted downloads; it is not a replacement for publisher signing.
public sealed class UpdateService : IDisposable
{
    public const string Repository = "iHoooK/lovi-kadr";
    public const string InstallerName = "LoviKadr-Setup-x64.exe";
    public const string ReleasesPage = "https://github.com/" + Repository + "/releases";
    public const long MaximumInstallerSize = 512L * 1024 * 1024;
    private readonly HttpClient _http;

    public UpdateService() : this(new HttpClient(new HttpClientHandler { AllowAutoRedirect = false })) { }

    internal UpdateService(HttpClient http)
    {
        _http = http;
        _http.Timeout = TimeSpan.FromSeconds(30);
        var version = typeof(UpdateService).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("LoviKadr-Updater/" + version);
    }

    public async Task<UpdateRelease?> CheckAsync(Version current, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get,
            "https://api.github.com/repos/" + Repository + "/releases/latest");
        request.Headers.Accept.ParseAdd("application/vnd.github+json");
        request.Headers.Add("X-GitHub-Api-Version", "2026-03-10");
        using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.NotFound)
            throw new InvalidOperationException("Опубликованный релиз не найден. Возможно, первая версия ещё не опубликована или репозиторий недоступен.");
        if (response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.TooManyRequests)
            throw new InvalidOperationException("GitHub временно ограничил запросы. Попробуйте проверить обновления позже.");
        response.EnsureSuccessStatusCode();
        return ParseRelease(await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false), current);
    }

    internal static UpdateRelease? ParseRelease(string json, Version current)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (root.GetProperty("draft").GetBoolean() || root.GetProperty("prerelease").GetBoolean()) return null;
        var tag = root.GetProperty("tag_name").GetString() ?? "";
        var versionText = tag.StartsWith('v') ? tag[1..] : tag;
        if (!Version.TryParse(versionText, out var version) || version.Build < 0 || version.Revision >= 0)
            throw new InvalidOperationException("У релиза неверный номер версии. Ожидается тег вида v0.1.1.");
        var installed = new Version(current.Major, current.Minor, Math.Max(0, current.Build));
        if (version <= installed) return null;
        var page = new Uri(ReleasesPage + "/tag/" + Uri.EscapeDataString(tag));
        var expected = "https://github.com/" + Repository + "/releases/download/" + Uri.EscapeDataString(tag) + "/" + InstallerName;
        foreach (var asset in root.GetProperty("assets").EnumerateArray())
        {
            if (asset.GetProperty("name").GetString() != InstallerName) continue;
            var size = asset.GetProperty("size").GetInt64();
            var digest = asset.TryGetProperty("digest", out var hash) ? hash.GetString() : null;
            var url = asset.GetProperty("browser_download_url").GetString();
            if (asset.GetProperty("state").GetString() != "uploaded" || size <= 0 || size > MaximumInstallerSize ||
                url != expected || digest is null || !digest.StartsWith("sha256:", StringComparison.Ordinal) ||
                digest.Length != 71 || !digest[7..].All(Uri.IsHexDigit))
                return new UpdateRelease(version, page, null, 0, null);
            return new UpdateRelease(version, page, new Uri(url), size, digest[7..]);
        }
        return new UpdateRelease(version, page, null, 0, null);
    }

    public async Task<DownloadedInstaller> DownloadAsync(UpdateRelease release, string folder,
        IProgress<int>? progress, CancellationToken cancellationToken)
    {
        // Validate again at the execution boundary, independently of the UI.
        if (!release.CanInstall || release.Size <= 0 || release.Size > MaximumInstallerSize ||
            release.Sha256!.Length != 64 || !release.Sha256.All(Uri.IsHexDigit) ||
            !IsTrustedDownload(release.InstallerUrl!))
            throw new InvalidOperationException("Релиз не содержит проверенного установщика.");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromMinutes(15));
        var token = timeout.Token;
        using var response = await GetDownloadResponseAsync(release.InstallerUrl!, token).ConfigureAwait(false);
        if (response.Content.Headers.ContentLength is long length && length != release.Size)
            throw new InvalidOperationException("Размер установщика не совпадает с данными релиза.");
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, Guid.NewGuid().ToString("N") + "-" + InstallerName);
        // Keep this handle through Process.Start: other processes cannot change
        // or replace the verified executable between verification and launch.
        var file = new FileStream(path, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.Read,
            81920, FileOptions.Asynchronous);
        try
        {
            using var source = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
            var buffer = new byte[81920];
            long received = 0;
            int lastProgress = -1;
            while (true)
            {
                int count = await source.ReadAsync(buffer, token).ConfigureAwait(false);
                if (count == 0) break;
                received += count;
                if (received > release.Size) throw new InvalidOperationException("Установщик больше заявленного размера.");
                await file.WriteAsync(buffer.AsMemory(0, count), token).ConfigureAwait(false);
                int percent = (int)(received * 100 / release.Size);
                if (percent != lastProgress) { progress?.Report(percent); lastProgress = percent; }
            }
            if (received != release.Size) throw new InvalidOperationException("Установщик скачан не полностью. Попробуйте снова.");
            await file.FlushAsync(token).ConfigureAwait(false);
            file.Dispose();
            file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
                81920, FileOptions.Asynchronous);
            var actual = await SHA256.HashDataAsync(file, token).ConfigureAwait(false);
            if (!CryptographicOperations.FixedTimeEquals(actual, Convert.FromHexString(release.Sha256!)))
                throw new InvalidOperationException("Проверка SHA-256 не пройдена. Установщик удалён; установка отменена.");
            file.Position = 0;
            return new DownloadedInstaller(path, file);
        }
        catch
        {
            file.Dispose();
            File.Delete(path);
            throw;
        }
    }

    private async Task<HttpResponseMessage> GetDownloadResponseAsync(Uri url, CancellationToken token)
    {
        for (int redirect = 0; redirect < 5; redirect++)
        {
            if (!IsTrustedDownload(url)) throw new InvalidOperationException("Небезопасный адрес скачивания обновления.");
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
            if ((int)response.StatusCode is 301 or 302 or 303 or 307 or 308)
            {
                var location = response.Headers.Location;
                response.Dispose();
                if (location is null) throw new InvalidOperationException("GitHub не вернул адрес установщика.");
                url = location.IsAbsoluteUri ? location : new Uri(url, location);
                continue;
            }
            try { response.EnsureSuccessStatusCode(); return response; }
            catch { response.Dispose(); throw; }
        }
        throw new InvalidOperationException("Слишком много перенаправлений при скачивании обновления.");
    }

    internal static bool IsTrustedDownload(Uri url) =>
        url.Scheme == Uri.UriSchemeHttps && url.IsDefaultPort && url.UserInfo.Length == 0 &&
        (url.Host is "release-assets.githubusercontent.com" or "objects.githubusercontent.com" ||
         (url.Host == "github.com" && url.AbsolutePath.StartsWith("/" + Repository + "/releases/download/", StringComparison.Ordinal)));

    public void Dispose() => _http.Dispose();
}

public sealed class DownloadedInstaller : IDisposable
{
    private readonly FileStream _file;
    public string Path { get; }
    internal DownloadedInstaller(string path, FileStream file) { Path = path; _file = file; }
    public void Dispose() => _file.Dispose();
    public void Delete() { Dispose(); File.Delete(Path); }
}

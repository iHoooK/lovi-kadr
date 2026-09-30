using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using PromptixCapture.Services;

internal static class UpdateTests
{
    public static void Run(Action<string, Action> test, Action<bool, string> assert)
    {
        var bytes = "Synthetic installer fixture"u8.ToArray();
        var hash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        const string assetUrl = "https://github.com/iHoooK/lovi-kadr/releases/download/v0.1.2/LoviKadr-Setup-x64.exe";
        string Release(string tag = "v0.1.2", bool draft = false, bool prerelease = false,
            string? digest = "default", string url = assetUrl, long? size = null, bool hasAsset = true) =>
            JsonSerializer.Serialize(new
            {
                tag_name = tag, draft, prerelease,
                // An untrusted html_url must not become a clickable arbitrary URL.
                html_url = "https://evil.example/",
                assets = hasAsset ? new[] { new { name = UpdateService.InstallerName, state = "uploaded",
                    browser_download_url = url, size = size ?? bytes.Length,
                    digest = digest == "default" ? "sha256:" + hash : digest } } : []
            });
        var current = new Version(0, 1, 1, 0);
        UpdateRelease Parse(string json) => UpdateService.ParseRelease(json, current)!;
        void Reject(Action action)
        {
            try { action(); }
            catch (InvalidOperationException) { return; }
            throw new Exception("Expected rejection");
        }
        void Async(string name, Func<Task> run) => test(name, () => run().GetAwaiter().GetResult());
        string TempFolder() => Path.Combine(Path.GetTempPath(), "lovikadr-update-test-" + Guid.NewGuid().ToString("N"));
        void Cleanup(string folder)
        {
            if (!Directory.Exists(folder)) return;
            foreach (var file in Directory.GetFiles(folder)) File.Delete(file);
            Directory.Delete(folder);
        }

        test("Updater compares three-part versions numerically", () =>
        {
            assert(Parse(Release("v0.1.10", hasAsset: false)).Version == new Version(0, 1, 10), "Numeric version");
            assert(UpdateService.ParseRelease(Release("v0.1.1"), current) is null, "Same version must not update");
            assert(UpdateService.ParseRelease(Release("v0.1.0"), current) is null, "Must not downgrade");
        });
        test("Updater ignores draft and prerelease", () =>
        {
            assert(UpdateService.ParseRelease(Release(draft: true), current) is null, "Draft ignored");
            assert(UpdateService.ParseRelease(Release(prerelease: true), current) is null, "Prerelease ignored");
        });
        test("Updater rejects malformed version tags", () =>
        {
            foreach (var tag in new[] { "latest", "v0.1", "v0.1.2-beta", "v0.1.2.3" })
                Reject(() => Parse(Release(tag, hasAsset: false)));
        });
        test("Updater restricts release and installer URLs", () =>
        {
            var good = Parse(Release());
            assert(good.CanInstall, "Valid asset");
            assert(good.Page.ToString() == "https://github.com/iHoooK/lovi-kadr/releases/tag/v0.1.2", "Ignore untrusted html_url");
            foreach (var url in new[] { "http://github.com/iHoooK/lovi-kadr/releases/download/v0.1.2/LoviKadr-Setup-x64.exe",
                "https://evil.example/LoviKadr-Setup-x64.exe", assetUrl.Replace("iHoooK", "other"),
                assetUrl.Replace("v0.1.2", "v0.1.3"), assetUrl + "?extra=1" })
                assert(!Parse(Release(url: url)).CanInstall, "Invalid asset URL rejected");
        });
        test("Updater requires installer digest and bounded size", () =>
        {
            foreach (var digest in new[] { null, "", "sha256:broken", "sha256:" + new string('z', 64), "sha512:" + hash })
                assert(!Parse(Release(digest: digest)).CanInstall, "Malformed digest rejected");
            assert(!Parse(Release(size: 0)).CanInstall, "Empty asset rejected");
            assert(!Parse(Release(size: UpdateService.MaximumInstallerSize + 1)).CanInstall, "Large asset rejected");
            assert(!Parse(Release(hasAsset: false)).CanInstall, "Missing asset handled");
        });
        Async("Updater handles API release, 404 and rate limit", async () =>
        {
            using var service = new UpdateService(new HttpClient(new Handler(request =>
            {
                assert(request.RequestUri!.ToString() == "https://api.github.com/repos/iHoooK/lovi-kadr/releases/latest", "API URL");
                assert(request.Headers.UserAgent.Count > 0, "GitHub requires User-Agent");
                assert(request.Headers.Authorization is null, "No embedded credential");
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(Release()) };
            })));
            assert((await service.CheckAsync(current, CancellationToken.None))!.CanInstall, "API parses asset");
            foreach (var status in new[] { HttpStatusCode.NotFound, HttpStatusCode.Forbidden, HttpStatusCode.TooManyRequests })
            {
                using var failure = new UpdateService(new HttpClient(new Handler(_ => new HttpResponseMessage(status))));
                try { await failure.CheckAsync(current, CancellationToken.None); throw new Exception("Expected API failure"); }
                catch (InvalidOperationException) { }
            }
        });
        Async("Updater follows GitHub CDN, verifies hash and locks installer", async () =>
        {
            int calls = 0;
            var folder = TempFolder();
            using var service = new UpdateService(new HttpClient(new Handler(request =>
            {
                calls++;
                if (calls == 1)
                {
                    var redirect = new HttpResponseMessage(HttpStatusCode.Found);
                    redirect.Headers.Location = new Uri("https://release-assets.githubusercontent.com/test");
                    return redirect;
                }
                assert(request.RequestUri!.Host == "release-assets.githubusercontent.com", "CDN requested");
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) };
            })));
            try
            {
                using var file = await service.DownloadAsync(Parse(Release()), folder, null, CancellationToken.None);
                assert(File.ReadAllBytes(file.Path).SequenceEqual(bytes), "Verified file content");
                try { File.WriteAllText(file.Path, "tamper"); throw new Exception("Expected file lock"); }
                catch (IOException) { }
                assert(calls == 2, "One redirect");
            }
            finally { Cleanup(folder); }
        });
        Async("Updater rejects unsafe download redirects", async () =>
        {
            foreach (var url in new[] { "https://evil.example/file", "http://release-assets.githubusercontent.com/file",
                "https://release-assets.githubusercontent.com.evil.example/file", "https://github.com/other/repo/releases/download/v1/file",
                "https://user@release-assets.githubusercontent.com/file", "https://release-assets.githubusercontent.com:8443/file" })
            {
                int calls = 0;
                using var service = new UpdateService(new HttpClient(new Handler(_ =>
                {
                    calls++;
                    var redirect = new HttpResponseMessage(HttpStatusCode.Found);
                    redirect.Headers.Location = new Uri(url);
                    return redirect;
                })));
                var folder = TempFolder();
                try
                {
                    try { await service.DownloadAsync(Parse(Release()), folder, null, CancellationToken.None); throw new Exception("Expected unsafe URL failure"); }
                    catch (InvalidOperationException) { }
                    assert(calls == 1, "Unsafe redirect never requested");
                    assert(!Directory.Exists(folder), "No file created");
                }
                finally { Cleanup(folder); }
            }
        });
        Async("Updater rejects corrupt, short and oversized streams", async () =>
        {
            foreach (var body in new[] { bytes.Select(x => (byte)(x ^ 1)).ToArray(), bytes[..^1], bytes.Concat(new byte[] { 1 }).ToArray() })
            {
                var folder = TempFolder();
                using var service = new UpdateService(new HttpClient(new Handler(_ =>
                    new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamingContent(body) })));
                try
                {
                    try { await service.DownloadAsync(Parse(Release()), folder, null, CancellationToken.None); throw new Exception("Expected download rejection"); }
                    catch (InvalidOperationException) { }
                    assert(Directory.Exists(folder) && Directory.GetFiles(folder).Length == 0, "Rejected stream deleted");
                }
                finally { Cleanup(folder); }
            }
        });
        Async("Updater cancels partial downloads and deletes file", async () =>
        {
            using var cancel = new CancellationTokenSource();
            var folder = TempFolder();
            using var service = new UpdateService(new HttpClient(new Handler(_ =>
                new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(new CancelStream(bytes, cancel)) })));
            try
            {
                try { await service.DownloadAsync(Parse(Release()), folder, null, cancel.Token); throw new Exception("Expected cancellation"); }
                catch (OperationCanceledException) { }
                assert(Directory.GetFiles(folder).Length == 0, "Partial download deleted");
            }
            finally { Cleanup(folder); }
        });
        Async("Updater bounds redirect loops", async () =>
        {
            int calls = 0;
            using var service = new UpdateService(new HttpClient(new Handler(_ =>
            {
                calls++;
                var response = new HttpResponseMessage(HttpStatusCode.Found);
                response.Headers.Location = new Uri(assetUrl);
                return response;
            })));
            try { await service.DownloadAsync(Parse(Release()), TempFolder(), null, CancellationToken.None); throw new Exception("Expected redirect failure"); }
            catch (InvalidOperationException) { }
            assert(calls == 5, "Redirect bound");
        });
    }

    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        { token.ThrowIfCancellationRequested(); return Task.FromResult(respond(request)); }
    }

    private sealed class StreamingContent(byte[] body) : HttpContent
    {
        protected override bool TryComputeLength(out long length) { length = 0; return false; }
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) => stream.WriteAsync(body.AsMemory()).AsTask();
        protected override Task<Stream> CreateContentReadStreamAsync() => Task.FromResult<Stream>(new MemoryStream(body));
    }

    private sealed class CancelStream(byte[] bytes, CancellationTokenSource cancel) : MemoryStream(bytes)
    {
        private bool _readOnce;
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken token = default)
        {
            if (_readOnce) { cancel.Cancel(); token.ThrowIfCancellationRequested(); }
            _readOnce = true;
            return base.ReadAsync(buffer[..Math.Min(buffer.Length, 4)], token);
        }
    }
}

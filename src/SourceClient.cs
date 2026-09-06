using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Emby.Plugin.Fernsehserien
{
    internal sealed class Page { public Uri Url; public string Html; }
    internal sealed class SourceClient : IDisposable
    {
        public static readonly Uri Origin = new Uri("https://www.fernsehserien.de/");
        public static readonly SourceClient Shared = new SourceClient();
        private readonly HttpClient http;
        private readonly SemaphoreSlim slots = new SemaphoreSlim(2);
        private readonly object sync = new object();
        private readonly Dictionary<string, (DateTimeOffset Until, Page Page)> cache = new Dictionary<string, (DateTimeOffset, Page)>();
        private readonly Dictionary<string, Task<Page>> pending = new Dictionary<string, Task<Page>>();
        public SourceClient(HttpMessageHandler handler = null)
        {
            http = new HttpClient(handler ?? new HttpClientHandler { AllowAutoRedirect = false, AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate });
            http.Timeout = Timeout.InfiniteTimeSpan;
            http.DefaultRequestHeaders.UserAgent.ParseAdd("Emby-Fernsehserien/0.1 (+https://github.com/dbiesecke/emby-plugin-fernsehserien)");
            http.DefaultRequestHeaders.AcceptLanguage.ParseAdd("de-DE,de;q=0.9");
        }
        public static Uri Validate(string value, bool image = false, Uri relativeTo = null)
        {
            if (string.IsNullOrWhiteSpace(value) || value.IndexOf('\\') >= 0) throw new ArgumentException("Invalid source URL");
            var u = new Uri(relativeTo ?? Origin, value);
            bool allowed = u.Host.Equals("www.fernsehserien.de", StringComparison.OrdinalIgnoreCase) || u.Host.Equals("fernsehserien.de", StringComparison.OrdinalIgnoreCase);
            if (image) allowed |= u.Host.Equals("bilder.fernsehserien.de", StringComparison.OrdinalIgnoreCase);
            if (!allowed || u.Scheme != "https" || !u.IsDefaultPort || u.UserInfo.Length != 0) throw new ArgumentException("Untrusted source host");
            return new UriBuilder(u) { Fragment = "" }.Uri;
        }
        public static string PathOf(string value, Uri relativeTo = null) => Validate(value, false, relativeTo).AbsolutePath.TrimEnd('/');
        public async Task<Page> Get(string value, bool search, CancellationToken ct)
        {
            var url = Validate(value); Task<Page> work;
            lock (sync)
            {
                if (cache.TryGetValue(url.AbsoluteUri, out var hit) && hit.Until > DateTimeOffset.UtcNow) return hit.Page;
                if (!pending.TryGetValue(url.AbsoluteUri, out work))
                {
                    work = FetchPage(url, search);
                    pending[url.AbsoluteUri] = work;
                }
            }
            // A cancelled caller stops waiting without cancelling another caller's shared fetch.
            using (var registrationCts = CancellationTokenSource.CreateLinkedTokenSource(ct))
            {
                var cancelled = Task.Delay(Timeout.Infinite, registrationCts.Token);
                try
                {
                    if (await Task.WhenAny(work, cancelled).ConfigureAwait(false) != work) ct.ThrowIfCancellationRequested();
                    return await work.ConfigureAwait(false);
                }
                finally { registrationCts.Cancel(); lock (sync) { if (work.IsCompleted) pending.Remove(url.AbsoluteUri); } }
            }
        }
        private async Task<Page> FetchPage(Uri url, bool search)
        {
            // Yield before registering the pending task, including handlers that complete synchronously.
            await Task.Yield();
            try
            {
                Page page;
                using (var response = await Request(url, false, CancellationToken.None).ConfigureAwait(false))
                {
                    if (response.StatusCode == HttpStatusCode.NotFound) page = null;
                    else
                    {
                        response.EnsureSuccessStatusCode();
                        var type = response.Content.Headers.ContentType?.MediaType ?? "";
                        if (type != "text/html" && type != "application/xhtml+xml") throw new InvalidDataException("Expected HTML");
                        var bytes = await ReadLimited(response.Content, 4 * 1024 * 1024, CancellationToken.None).ConfigureAwait(false);
                        var html = System.Text.Encoding.UTF8.GetString(bytes);
                        if (!Scraper.IsRecognized(html, search)) throw new InvalidDataException("Source layout not recognized (or challenge page)");
                        page = new Page { Url = response.RequestMessage.RequestUri, Html = html };
                    }
                }
                lock (sync)
                {
                    foreach (var key in cache.Where(x => x.Value.Until <= DateTimeOffset.UtcNow).Select(x => x.Key).ToArray()) cache.Remove(key);
                    // Bounded page count; at most 4 MiB per page.
                    if (cache.Count >= 64) cache.Remove(cache.OrderBy(x => x.Value.Until).First().Key);
                    cache[url.AbsoluteUri] = (DateTimeOffset.UtcNow.AddMinutes(page == null ? 5 : search ? 60 : 1440), page);
                }
                return page;
            }
            finally { lock (sync) pending.Remove(url.AbsoluteUri); }
        }
        private async Task<HttpResponseMessage> Request(Uri start, bool image, CancellationToken ct)
        {
            for (int attempt = 0; ; attempt++)
            {
                HttpResponseMessage response = null;
                await slots.WaitAsync(ct).ConfigureAwait(false);
                try
                {
                    using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct))
                    {
                        timeout.CancelAfter(TimeSpan.FromSeconds(20));
                        var url = start;
                        for (int redirect = 0; ; redirect++)
                        {
                            using (var request = new HttpRequestMessage(HttpMethod.Get, url))
                            {
                                request.Headers.Referrer = Origin;
                                response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false);
                                // Some test handlers omit RequestMessage; retain the validated final address.
                                response.RequestMessage = new HttpRequestMessage(HttpMethod.Get, url);
                            }
                            int status = (int)response.StatusCode;
                            if (status != 301 && status != 302 && status != 303 && status != 307 && status != 308)
                            {
                                var contentType = response.Content.Headers.ContentType;
                                var bytes = await ReadLimited(response.Content, image ? 15 * 1024 * 1024 : 4 * 1024 * 1024, timeout.Token).ConfigureAwait(false);
                                response.Content.Dispose();
                                response.Content = new ByteArrayContent(bytes);
                                response.Content.Headers.ContentType = contentType;
                                break;
                            }
                            var location = response.Headers.Location;
                            response.Dispose(); response = null;
                            if (redirect >= 5 || location == null) throw new HttpRequestException("Invalid redirect chain");
                            url = Validate(location.ToString(), image, url);
                        }
                    }
                }
                catch (HttpRequestException) when (attempt < 2) { response?.Dispose(); response = null; }
                catch (OperationCanceledException) when (!ct.IsCancellationRequested && attempt < 2) { response?.Dispose(); response = null; }
                catch { response?.Dispose(); throw; }
                finally { slots.Release(); }
                if (response != null)
                {
                    int status = (int)response.StatusCode;
                    if ((status != 429 && status < 500) || attempt >= 2) return response;
                }
                var retry = response?.Headers.RetryAfter;
                var delay = retry?.Delta ?? (retry?.Date.HasValue == true ? retry.Date.Value - DateTimeOffset.UtcNow : TimeSpan.FromSeconds(attempt + 1));
                response?.Dispose();
                // Do not hold an Emby scan for a long rate-limit window or retry before it expires.
                if (delay > TimeSpan.FromSeconds(30)) throw new HttpRequestException("Source rate limited; retry later");
                await Task.Delay(delay > TimeSpan.Zero ? delay : TimeSpan.Zero, ct).ConfigureAwait(false);
            }
        }
        internal static async Task<byte[]> ReadLimited(HttpContent content, int max, CancellationToken ct)
        {
            if (content.Headers.ContentLength > max) throw new InvalidDataException("Source response too large");
            using (var input = await content.ReadAsStreamAsync().ConfigureAwait(false))
            using (var output = new MemoryStream())
            {
                var buffer = new byte[8192]; int n;
                while ((n = await input.ReadAsync(buffer, 0, buffer.Length, ct).ConfigureAwait(false)) != 0)
                { if (output.Length + n > max) throw new InvalidDataException("Source response too large"); output.Write(buffer, 0, n); }
                return output.ToArray();
            }
        }
        public async Task<(byte[] Data, string Type)> Image(string value, CancellationToken ct)
        {
            using (var response = await Request(Validate(value, true), true, ct).ConfigureAwait(false))
            {
                response.EnsureSuccessStatusCode();
                var data = await ReadLimited(response.Content, 15 * 1024 * 1024, ct).ConfigureAwait(false);
                string type = null;
                if (data.Length >= 12)
                {
                    if (data[0] == 0xff && data[1] == 0xd8 && data[2] == 0xff) type = "image/jpeg";
                    else if (data.Take(8).SequenceEqual(new byte[] { 137,80,78,71,13,10,26,10 })) type = "image/png";
                    else if (System.Text.Encoding.ASCII.GetString(data, 0, 6) == "GIF89a" || System.Text.Encoding.ASCII.GetString(data, 0, 6) == "GIF87a") type = "image/gif";
                    else if (System.Text.Encoding.ASCII.GetString(data, 0, 4) == "RIFF" && System.Text.Encoding.ASCII.GetString(data, 8, 4) == "WEBP") type = "image/webp";
                }
                if (type == null || !(response.Content.Headers.ContentType?.MediaType ?? "").StartsWith("image/")) throw new InvalidDataException("Invalid image response");
                return (data, type);
            }
        }
        public void Dispose() { http.Dispose(); slots.Dispose(); }
    }
}

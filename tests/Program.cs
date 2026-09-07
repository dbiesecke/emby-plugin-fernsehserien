using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Emby.Plugin.Fernsehserien;

class Program
{
    static void Check(bool ok, string message) { if (!ok) throw new Exception(message); }
    static Page Page(string html, string path) => new Page { Html = html, Url = new Uri(SourceClient.Origin, path) };
    static async Task Main(string[] args)
    {
        if (args.Length == 1 && args[0] == "--live-search")
        {
            foreach (var query in new[] { ("Dark", 2017, MediaKind.Series), ("Inception", 2010, MediaKind.Movie) })
            {
                var hits = await Catalog.Search(query.Item1, query.Item3, CancellationToken.None);
                Check(Matching.Unique(hits, query.Item1, query.Item2, query.Item3) != null, "live title search " + query.Item1);
                Console.WriteLine("Live title search passed: " + query.Item1);
            }
            return;
        }
        if (args.Length > 0)
        {
            foreach (var spec in new[] { ("dark", MediaKind.Series, "/dark"), ("movie", MediaKind.Movie, "/filme/inception"), ("season", MediaKind.Season, "/dark/episodenguide/staffel-1/33342"), ("episode", MediaKind.Episode, "/dark/folgen/1x01-geheimnisse-1144654") })
            {
                var e = Scraper.Detail(Page(File.ReadAllText(Path.Combine(args[0], spec.Item1 + ".html")), spec.Item3), spec.Item2);
                Check(e != null, "live " + spec.Item1);
                Console.WriteLine($"{spec.Item1}: {e.Name}; year={e.Year}; S{e.Season}E{e.Episode}; premiere={e.Premiere:yyyy-MM-dd}; overview={e.Overview?.Length}; images={e.Pictures.Count}; cast={e.People.Count}");
                Check(!string.IsNullOrWhiteSpace(e.Overview), "live overview " + spec.Item1);
                if (spec.Item1 == "dark") Check(e.Year == 2017 && e.Genres.Count > 0 && e.People.Count > 0 && e.Pictures.Count > 0, "live series fields");
                if (spec.Item1 == "movie") Check(e.Year == 2010 && e.Minutes == 148, "live movie fields");
                if (spec.Item1 == "episode") Check(e.Episode == 1 && e.Season == 1 && e.Premiere?.Year == 2017, "live episode fields");
            }
            var liveGuide = Scraper.Guide(Page(File.ReadAllText(Path.Combine(args[0], "dark-guide.html")), "/dark/episodenguide"));
            Check(liveGuide.Count(e => e.Kind == MediaKind.Episode && e.Episode.HasValue) == 26, "live guide");
            Check(liveGuide.Count(e => e.Kind == MediaKind.Season) == 4, "live seasons");
            Check(liveGuide.Where(e => e.Kind == MediaKind.Episode && e.Season == 0).All(e => e.Episode == null), "specials must not receive invented numbers");
            Console.WriteLine("Live HTML checks passed."); return;
        }
        // Minimal source-shaped fragments. No full copyrighted pages in the repository.
        const string movie = "<article itemscope itemtype='http://schema.org/Movie'><header><h1 itemprop=name>Der Test</h1><div>USA 2010 (148 Min.)</div><div itemscope itemtype='http://schema.org/VideoObject' itemprop=trailer><meta itemprop=name content='WRONG'><meta itemprop=description content='TRAILER'></div></header><meta itemprop=alternateName content='The Test'><div class=episode-output-inhalt-inner>Film <script>advert()</script> Text</div><meta itemprop=genre content='Drama'><abbr itemprop=countryOfOrigin title='USA'>US</abbr><ea-angabe><ea-angabe-titel>Deutsche TV-Premiere</ea-angabe-titel><ea-angabe-datum>01.01.2020</ea-angabe-datum></ea-angabe><ea-angabe><ea-angabe-titel>Original-Kinostart</ea-angabe-titel><ea-angabe-datum>02.02.2010</ea-angabe-datum></ea-angabe></article>";
        var m = Scraper.Detail(Page(movie, "/filme/test"), MediaKind.Movie);
        Check(m.Name == "Der Test" && m.OriginalTitle == "The Test" && m.Year == 2010 && m.Minutes == 148 && m.Overview == "Film Text" && m.Premiere?.Year == 2010, "movie scoping/date");
        Check(Scraper.Detail(Page(movie.Replace("episode-output-inhalt-inner", "episode-output-inhalt").Replace("Film <script>advert()</script> Text", "<p>Film</p><werbung></werbung><p>Text</p>"), "/filme/test"), MediaKind.Movie).Overview == "Film Text", "movie paragraph overview");
        Check(Matching.Unique(new[] { m }, "The Test", 2010, MediaKind.Movie) == m, "original title match");
        Check(Matching.Unique(new[] { m }, "Der Test", 2011, MediaKind.Movie) == null, "year mismatch");
        var duplicate = new Entry { Name = m.Name, Year = m.Year, Kind = m.Kind, Path = "/filme/other" };
        Check(Matching.Unique(new[] { m, duplicate }, m.Name, m.Year, m.Kind) == null, "ambiguous match");
        Check(Scraper.Search(Page(movie, "/filme/test")).Count == 1, "search redirect");
        Check(Scraper.Search(Page("<div class=suchergebnisse>Keine Treffer</div>", "/suche/xxx")).Count == 0, "no slug invention");
        const string guide = "<section itemscope itemtype='http://schema.org/TVSeason'><h2 itemprop=name>Staffel <span itemprop=seasonNumber>2</span><a itemprop=url href='episodenguide/staffel-2/55'>Staffel</a></h2><a href='/test/folgen/2x01-a' itemscope itemtype='http://schema.org/TVEpisode'><span itemprop=name>A</span><meta itemprop=episodeNumber content=1></a></section><section itemscope itemtype='http://schema.org/TVSeason'><h2 itemprop=name>Specials</h2><a itemprop=url href='/test/episodenguide/0/55'>Specials</a><a href='/test/folgen/special' itemscope itemtype='http://schema.org/TVEpisode'><span itemprop=name>Special</span><meta itemprop=episodeNumber content=0></a></section>";
        var g = Scraper.Guide(Page(guide, "/test/episodenguide"));
        Check(g.Any(e => e.Path == "/test/episodenguide/staffel-2/55" && e.Season == 2), "relative season URL");
        Check(g.Any(e => e.Kind == MediaKind.Episode && e.Season == 2 && e.Episode == 1), "season/episode mapping");
        Check(g.Any(e => e.Kind == MediaKind.Episode && e.Season == 0 && e.Episode == null), "unnumbered special");
        const string season = "<section itemscope itemtype='http://schema.org/TVSeason'><span itemprop=name>Staffel <span itemprop=seasonNumber>2</span></span><section itemscope itemtype='http://schema.org/TVEpisode'><span itemprop=name>Episode</span><img itemprop=image src='https://bilder.fernsehserien.de/episode.jpg' width=800 height=450></section></section>";
        Check(Scraper.Detail(Page(season, "/test/episodenguide/staffel-2/55"), MediaKind.Season).Pictures.Count == 0, "episode image is not season image");
        var images = movie.Replace("</article>", "<picture><source srcset='/poster-400.jpg 400w, /poster-800.jpg 800w'><img itemprop=image src='/poster-200.jpg' width=200 height=300></picture><img itemprop=image src='https://evil.example/image.jpg' width=200 height=300></article>");
        var poster = Scraper.Detail(Page(images, "/filme/test"), MediaKind.Movie).Pictures.Single();
        Check(poster.Type == "Primary" && poster.Width == 800 && poster.Url.EndsWith("poster-800.jpg"), "largest poster and host filter");
        foreach (var bad in new[] { "http://www.fernsehserien.de/a", "https://fernsehserien.de.evil/a", "https://127.0.0.1/a", "https://user@www.fernsehserien.de/a", "https://www.fernsehserien.de:444/a" })
        { bool rejected = false; try { SourceClient.Validate(bad); } catch (ArgumentException) { rejected = true; } Check(rejected, "URL validation"); }
        var png = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+jRZkAAAAASUVORK5CYII=");
        Check(SourceClient.Dimensions(png) == (1, 1) && SourceClient.Dimensions(new byte[24]) == (0, 0), "image geometry");
        var handler = new Handler(movie);
        using (var client = new SourceClient(handler))
        {
            await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => client.Get("/filme/test", false, CancellationToken.None)));
            await client.Get("/filme/test", false, CancellationToken.None);
            Check(handler.Calls == 1, "coalescing/cache");
            await client.Get("/missing", false, CancellationToken.None); await client.Get("/missing", false, CancellationToken.None);
            Check(handler.Calls == 2, "404 negative cache");
            for (int i = 0; i < 2; i++) { try { await client.Get("/challenge", false, CancellationToken.None); } catch (InvalidDataException) { } }
            Check(handler.Calls == 4, "challenge not cached");
            bool cancelled = false;
            using (var cts = new CancellationTokenSource(5))
            { try { await client.Get("/slow", false, cts.Token); } catch (OperationCanceledException) { cancelled = true; } }
            Check(cancelled, "caller cancellation");
            await client.Get("/slow", false, CancellationToken.None);
            bool redirectRejected = false;
            try { await client.Get("/redirect", false, CancellationToken.None); } catch (ArgumentException) { redirectRejected = true; }
            Check(redirectRejected, "redirect host validation");
            bool imageRejected = false;
            try { await client.Image("https://bilder.fernsehserien.de/not-image", CancellationToken.None); } catch (InvalidDataException) { imageRejected = true; }
            Check(imageRejected, "HTML disguised as image");
            await client.Get("/retry", false, CancellationToken.None);
            Check(handler.Retries == 3, "bounded 429 retries");
            await Task.WhenAll(Enumerable.Range(0, 6).Select(i => client.Get("/parallel" + i, false, CancellationToken.None)));
            Check(handler.MaxActive <= 2, "maximum concurrency");
        }
        Console.WriteLine("Parser, matching, image, HTTP and cache checks passed.");
    }
    sealed class Handler : HttpMessageHandler
    {
        readonly string html; public int Calls, Retries, MaxActive; int active;
        public Handler(string html) { this.html = html; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Interlocked.Increment(ref Calls); var now = Interlocked.Increment(ref active); MaxActive = Math.Max(MaxActive, now);
            try
            {
                await Task.Delay(30, ct);
                var path = request.RequestUri.AbsolutePath;
                var response = new HttpResponseMessage(path == "/missing" ? HttpStatusCode.NotFound : HttpStatusCode.OK) { Content = new StringContent(path == "/challenge" ? "<html>challenge</html>" : html, System.Text.Encoding.UTF8, "text/html") };
                if (path == "/redirect") { response.StatusCode = HttpStatusCode.Found; response.Headers.Location = new Uri("https://evil.example/"); }
                if (path == "/retry" && ++Retries < 3) { response.StatusCode = (HttpStatusCode)429; response.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.Zero); }
                return response;
            }
            finally { Interlocked.Decrement(ref active); }
        }
    }
}

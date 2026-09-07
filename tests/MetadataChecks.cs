using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Emby.Plugin.Fernsehserien;

internal static class MetadataChecks
{
    static void Assert(bool ok, string reason) { if (!ok) throw new Exception(reason); }
    static Entry Parse(string html, MediaKind kind = MediaKind.Series) => Scraper.Detail(new Page { Html = html, Url = new Uri(SourceClient.Origin, kind == MediaKind.Series ? "/escaping-bolivia" : "/example/folgen/1x01-test") }, kind);
    static string Person(string name, string type, string role = "") => $"<li itemscope itemtype='https://schema.org/Person' itemprop='{type}'><dt itemprop=name>{name}</dt><dd>{role}</dd></li>";
    public static async Task Run()
    {
        var html = "<div itemscope itemtype='https://schema.org/TVSeries'><h1 itemprop=name>Escaping Bolivia</h1>" +
            "<div class=serie-produktionsjahre>N 2025 (<span lang=no>Flukten fra Bolivia</span>)</div>" +
            "<meta itemprop=genre content='Sci-Fi'><ul class=genrepillen><li>science fiction<li>Fantasy- &amp; Sci-Fi-Serien<li>Comedy<li>Doku<li>Crime<li>Anime<li>Animation<li>Neues Genre<li>neues genre</ul>" +
            "<span itemprop=productionCompany itemscope itemtype='https://schema.org/Organization'><span itemprop=name>TV 2 Play</span></span>" +
            "<ea-angabe><ea-angabe-titel>Original-Streaming-Premiere</ea-angabe-titel><ea-angabe-datum><time datetime='2025-12-25'>25.12.2025</time></ea-angabe-datum><ea-angabe-sender>tv 2 play</ea-angabe-sender></ea-angabe>" +
            "<ea-angabe><ea-angabe-titel>Deutsche Streaming-Premiere</ea-angabe-titel><ea-angabe-datum>04.09.2026</ea-angabe-datum><ea-angabe-sender>ARD Mediathek</ea-angabe-sender></ea-angabe>" +
            "<ea-angabe><ea-angabe-titel>Deutsche TV-Premiere</ea-angabe-titel><ea-angabe-datum>11.09.2026</ea-angabe-datum><ea-angabe-sender>One</ea-angabe-sender></ea-angabe>" +
            "<ea-angabe><ea-angabe-titel>Wiederholung</ea-angabe-titel><ea-angabe-datum>12.09.2028</ea-angabe-datum><ea-angabe-sender>Fremder Sender</ea-angabe-sender></ea-angabe>" +
            "<a data-event-category=sonstige-links href='https://thetvdb.com/?tab=series&amp;id=334824&amp;lid=14'>TVDB</a>" +
            "<a href='https://thetvdb.com/?tab=series&amp;id=999'>Empfehlung</a>" +
            "<div itemscope itemtype='https://schema.org/TVEpisode'><meta itemprop=genre content=FALSCH><meta itemprop=originalName content=FALSCH><a itemprop=sameAs href='https://thetvdb.com/?tab=series&amp;id=888'></a></div>" +
            "<ul class=cast-crew>" + string.Concat(Enumerable.Range(1, 12).Select(i => Person("Actor " + i, "actor", "Role " + i))) +
            Person("Actor 1", "actor", "Role 1") + Person("Alex", "director", "Regie") + Person("Alex", "author", "Drehbuch") +
            Person("Alex", "author", "Drehbuch") + Person("Pat", "producer", "Produktion") + Person("Creator", "creator", "Idee") + "</ul></div>";
        var e = Parse(html);
        Assert(e.OriginalTitle == "Flukten fra Bolivia", "original title in production span");
        Assert(e.Year == 2025 && e.Premiere?.Year == 2025 && e.PremiereYears.SetEquals(new[] { 2025, 2026 }), "separate canonical and local premiere years");
        Assert(Matching.Unique(new[] { e }, "Escaping Bolivia", 2026, MediaKind.Series) == e, "German premiere year matching");
        Assert(Matching.Unique(new[] { e }, "Flukten fra Bolivia", 2025, MediaKind.Series) == e, "original title/year matching");
        Assert(Matching.Unique(new[] { e }, "Escaping Bolivia", 2024, MediaKind.Series) == null, "no guessed year tolerance");
        var remake = new Entry { Name = e.Name, Year = 2026, Kind = MediaKind.Series, Path = "/remake" };
        Assert(Matching.Unique(new[] { e, remake }, e.Name, 2026, MediaKind.Series) == null, "remake ambiguity remains unresolved");
        Assert(e.Ids["Tvdb"] == "334824", "ordinary legacy TVDB link, excluding unrelated links");
        Assert(e.Studios.SequenceEqual(new[] { "TV 2 Play", "ARD Mediathek", "One" }), "premiere senders and studio deduplication");
        Assert(e.Genres.SequenceEqual(new[] { "Science-Fiction", "Fantasy", "Komödie", "Dokumentation", "Krimi", "Anime", "Animation", "Neues Genre" }), "German genres, combinations and unknown values");
        Assert(e.People.Count(p => p.Type == "Actor") == 10 && e.People.Count == 13, "principal cast and crew without duplicate duties");
        Assert(e.People.Count(p => p.Name == "Alex") == 2 && e.People.All(p => p.Name != "Creator"), "multiple credited duties, creator is not writer");
        var legacy = "https://thetvdb.com/?tab=series&amp;id=334824&amp;lid=14";
        Assert(Parse(html.Replace(legacy, "https://thetvdb.com/dereferrer/series/334824")).Ids["Tvdb"] == "334824", "numeric TVDB dereferrer");
        Assert(!Parse(html.Replace(legacy, "https://thetvdb.com/series/flukten-fra-bolivia")).Ids.ContainsKey("Tvdb"), "no TVDB lookup for slug-only link");
        Assert(!Parse(html.Replace(legacy, "https://thetvdb.com.evil.example/?tab=series&amp;id=334824")).Ids.ContainsKey("Tvdb"), "TVDB hostname validation");
        Assert(!Parse(html.Replace("</div>", "<a itemprop=sameAs href='https://thetvdb.com/dereferrer/series/777'></a></div>")).Ids.ContainsKey("Tvdb"), "conflicting explicit IDs are omitted");
        Assert(Parse(html.Replace("<meta itemprop=genre", "<div class=serie-infos-originaltitel>Originaltitel: Explicit Original</div><meta itemprop=genre")).OriginalTitle == "Explicit Original", "explicit original title precedence");
        var episode = "<section itemscope itemtype='https://schema.org/TVEpisode'><span itemprop=name>Test</span><meta itemprop=episodeNumber content=1></section>" +
            "<section><h2 id=Cast-Crew>Cast &amp; Crew</h2><ul class=cast-crew>" + Person("Director", "director") + Person("Writer", "author") + "</ul></section>" +
            "<aside><section><h2 id=Cast-Crew>Recommendations</h2>" + Person("Wrong", "actor") + "</section></aside>";
        Assert(Parse(episode, MediaKind.Episode).People.Count == 2, "page-level episode credits without sidebar cast");
        var handler = new OnePage(html);
        using (var client = new SourceClient(handler))
        {
            var page = await client.Get("/escaping-bolivia", false, CancellationToken.None);
            var enriched = Scraper.Detail(page, MediaKind.Series);
            Assert(enriched.Ids.ContainsKey("Tvdb") && enriched.Studios.Count == 3 && enriched.People.Count == 13, "enriched fields from one response");
            Scraper.Detail(await client.Get("/escaping-bolivia", false, CancellationToken.None), MediaKind.Series);
            Assert(handler.Calls == 1, "enrichment performs no additional HTTP requests");
        }
        var searchHandler = new OnePage(html);
        using (var client = new SourceClient(searchHandler))
        {
            var hits = await Catalog.Search("Escaping Bolivia", MediaKind.Series, CancellationToken.None, client);
            Assert(Matching.Unique(hits, "Escaping Bolivia", 2026, MediaKind.Series) != null, "redirect search with regional year");
            Assert(searchHandler.Calls == 2, "search redirect plus target only; no duplicate detail fetch");
        }
        Console.WriteLine("Extended metadata and premiere-year checks passed (one HTTP request, cached reuse).");
    }
    sealed class OnePage : HttpMessageHandler
    {
        readonly string html; public int Calls;
        public OnePage(string html) { this.html = html; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct)
        {
            Calls++;
            if (r.RequestUri.AbsolutePath.StartsWith("/suche/"))
            {
                var redirect = new HttpResponseMessage(HttpStatusCode.Found);
                redirect.Headers.Location = new Uri("https://www.fernsehserien.de/escaping-bolivia");
                return Task.FromResult(redirect);
            }
            if (r.RequestUri.AbsolutePath != "/escaping-bolivia") throw new Exception("Unexpected enrichment request");
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(html, System.Text.Encoding.UTF8, "text/html") });
        }
    }
}

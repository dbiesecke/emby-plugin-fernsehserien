using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using Emby.Plugin.Fernsehserien.HtmlParser;

namespace Emby.Plugin.Fernsehserien
{
    internal static class Scraper
    {
        internal static HtmlNode Dom(string html) { var doc = new HtmlDocument(); doc.LoadHtml(html); return doc.DocumentNode; }
        internal static bool Prop(HtmlNode n, string name) => (n.GetAttributeValue("itemprop", "")).Split(' ').Contains(name);
        internal static bool Class(HtmlNode n, string name) => n.GetAttributeValue("class", "").Split(' ').Contains(name);
        private static bool Schema(HtmlNode n, string name) => n.GetAttributeValue("itemtype", "").TrimEnd('/').EndsWith("/" + name, StringComparison.Ordinal);
        internal static string Text(HtmlNode n)
        {
            if (n == null) return null;
            var copy = n.CloneNode(true);
            foreach (var drop in copy.Descendants().Where(x => new[] { "script", "style", "ins", "werbung", "noscript" }.Contains(x.Name) || x.Attributes["data-werbemittel-container"] != null).ToArray()) drop.Remove();
            return Regex.Replace(HtmlEntity.DeEntitize(copy.InnerText), @"\s+", " ").Trim();
        }
        internal static string Value(HtmlNode n) => n == null ? null : HtmlEntity.DeEntitize(n.GetAttributeValue("content", n.GetAttributeValue("datetime", n.GetAttributeValue("title", Text(n)))));
        private static IEnumerable<HtmlNode> Own(HtmlNode scope) => scope.Descendants().Where(n => !n.Ancestors().TakeWhile(a => a != scope).Any(a => a.Attributes["itemscope"] != null));
        private static string Field(HtmlNode scope, string name) => Value(Own(scope).FirstOrDefault(n => Prop(n, name)));
        private static int? Number(string s) => int.TryParse(s, out var n) && n >= 0 ? (int?)n : null;
        private static int? Capture(string s, string regex) => Number(Regex.Match(s ?? "", regex).Groups[1].Value);
        internal static bool IsRecognized(string html, bool search)
        {
            var root = Dom(html);
            return root.Descendants().Any(n => Schema(n, "Movie") || Schema(n, "TVSeries") || Schema(n, "TVEpisode") ||
                search && (n.GetAttributeValue("class", "").Contains("suchergebnis") || n.GetAttributeValue("id", "").Contains("suchergebnis"))) ||
                search && Text(root).IndexOf("keine Treffer", StringComparison.OrdinalIgnoreCase) >= 0;
        }
        public static Entry Detail(Page page, MediaKind kind)
        {
            if (page == null) return null;
            var root = Dom(page.Html);
            var type = kind == MediaKind.Movie ? "Movie" : kind == MediaKind.Series ? "TVSeries" : kind == MediaKind.Season ? "TVSeason" : "TVEpisode";
            var scope = root.Descendants().FirstOrDefault(n => Schema(n, type));
            if (scope == null) return null;
            // Overview pages also contain episode/season schema: require a real detail URL.
            if (kind == MediaKind.Series && page.Url.AbsolutePath.Split('/').Length != 2 || kind == MediaKind.Movie && !page.Url.AbsolutePath.StartsWith("/filme/")) return null;
            if (kind == MediaKind.Episode && !page.Url.AbsolutePath.Contains("/folgen/") || kind == MediaKind.Season && !page.Url.AbsolutePath.Contains("/episodenguide/")) return null;
            var name = Field(scope, "name");
            if (string.IsNullOrWhiteSpace(name)) return null;
            var e = new Entry { Kind = kind, Path = page.Url.AbsolutePath.TrimEnd('/'), Name = name };
            var production = scope.Descendants().FirstOrDefault(n => Class(n, "serie-produktionsjahre")) ??
                (kind == MediaKind.Movie ? scope.Descendants("header").FirstOrDefault() : null);
            e.Year = Capture(Text(production), @"\b((?:19|20)\d{2})\b");
            e.OriginalTitle = Field(scope, "alternateName");
            if (e.OriginalTitle == null) e.OriginalTitle = Regex.Match(Text(production) ?? "", @"\(([^()]*\p{L}[^()]*)\)\s*$").Groups[1].Value;
            if (Regex.IsMatch(e.OriginalTitle ?? "", @"\d+\s*Min")) e.OriginalTitle = null;
            var description = Own(scope).FirstOrDefault(n => Prop(n, "description")) ?? Own(scope).FirstOrDefault(n => Class(n, "episode-output-inhalt-inner")) ??
                (kind == MediaKind.Season ? Own(scope).FirstOrDefault(n => Class(n, "staffelinfo")) : null);
            e.Overview = Text(description);
            if (e.Overview == null && kind == MediaKind.Movie)
            {
                var content = Own(scope).FirstOrDefault(n => Class(n, "episode-output-inhalt"));
                if (content != null) e.Overview = string.Join(" ", content.Elements("p").Select(Text));
            }
            var minuteText = kind == MediaKind.Movie ? Text(production) : Text(Own(scope).FirstOrDefault(n => Prop(n, "episodeNumber")));
            e.Minutes = Capture(minuteText, @"(\d+)\s*Min");
            e.Season = Number(Field(scope, "seasonNumber"));
            e.Episode = Number(Field(scope, "episodeNumber"));
            if (kind == MediaKind.Episode) e.Season = Capture(minuteText, @"Staffel\s+(\d+)");
            if (kind == MediaKind.Season && !e.Season.HasValue && name.Equals("Specials", StringComparison.OrdinalIgnoreCase)) e.Season = 0;
            foreach (var n in Own(scope).Where(n => Prop(n, "genre"))) Add(e.Genres, Value(n));
            foreach (var n in Own(scope).Where(n => Class(n, "genrepillen")).SelectMany(n => n.Descendants("li"))) Add(e.Genres, Text(n));
            foreach (var n in Own(scope).Where(n => Prop(n, "countryOfOrigin"))) Add(e.Countries, Value(n));
            foreach (var n in Own(scope).Where(n => Prop(n, "productionCompany"))) Add(e.Studios, Field(n, "name") ?? Text(n));
            foreach (var n in Own(scope).Where(n => Prop(n, "actor") || Prop(n, "director") || Prop(n, "creator")))
            {
                var personName = Field(n, "name");
                if (!string.IsNullOrEmpty(personName)) e.People.Add(new Person { Name = personName, Role = Text(n.Descendants("dd").FirstOrDefault()), Type = Prop(n, "actor") ? "Actor" : Prop(n, "director") ? "Director" : "Writer" });
            }
            var dates = new List<DateTimeOffset>();
            foreach (var n in Own(scope).Where(n => n.Name == "ea-angabe"))
            {
                var label = Text(n.Descendants("ea-angabe-titel").FirstOrDefault()) ?? "";
                if (!label.StartsWith("Original", StringComparison.OrdinalIgnoreCase)) continue;
                var raw = Value(n.Descendants("time").FirstOrDefault()) ?? Text(n.Descendants("ea-angabe-datum").FirstOrDefault());
                var date = Regex.Match(raw ?? "", @"\d{4}-\d{2}-\d{2}|\d{2}\.\d{2}\.\d{4}").Value;
                if (DateTimeOffset.TryParseExact(date, new[] { "yyyy-MM-dd", "dd.MM.yyyy" }, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed)) dates.Add(parsed);
            }
            e.Premiere = dates.Count == 0 ? (DateTimeOffset?)null : dates.Min();
            // Never derive external IDs from ads, recommendations or navigation.
            foreach (var n in Own(scope).Where(n => Prop(n, "sameAs")))
            {
                var link = n.GetAttributeValue("href", n.GetAttributeValue("content", ""));
                var imdb = Regex.Match(link, @"^https://(?:www\.)?imdb\.com/title/(tt\d+)(?:/|$)");
                if (imdb.Success) e.Ids["Imdb"] = imdb.Groups[1].Value;
                var tmdb = Regex.Match(link, @"^https://(?:www\.)?themoviedb\.org/(?:movie|tv)/(\d+)(?:[/-]|$)");
                if (tmdb.Success) e.Ids["Tmdb"] = tmdb.Groups[1].Value;
            }
            Images(root, scope, page.Url, e);
            return e;
        }
        private static void Add(List<string> list, string s) { if (!string.IsNullOrWhiteSpace(s) && !list.Contains(s)) list.Add(s); }
        public static List<Entry> Search(Page page)
        {
            if (page == null) return new List<Entry>();
            foreach (var kind in new[] { MediaKind.Movie, MediaKind.Series })
            { var direct = Detail(page, kind); if (direct != null) return new List<Entry> { direct }; }
            var root = Dom(page.Html); var result = new List<Entry>();
            foreach (var li in root.Descendants("li").Where(n => Class(n, "suchergebnisse-film") || Class(n, "suchergebnisse-sendung")))
            {
                var a = li.Descendants("a").FirstOrDefault(); if (a == null) continue;
                try
                {
                    var path = SourceClient.PathOf(a.GetAttributeValue("href", ""), page.Url);
                    var name = Text(li.Descendants("dt").FirstOrDefault()) ?? a.GetAttributeValue("title", "");
                    if (name.Length == 0) continue;
                    result.Add(new Entry { Path = path, Name = name, Year = Capture(Text(li.Descendants("dd").FirstOrDefault()), @"\b((?:19|20)\d{2})\b"), Kind = Class(li, "suchergebnisse-film") ? MediaKind.Movie : MediaKind.Series });
                }
                catch (ArgumentException) { }
            }
            return result.GroupBy(x => x.Path).Select(x => x.First()).Take(20).ToList();
        }
        public static List<Entry> Guide(Page page)
        {
            var result = new List<Entry>(); if (page == null) return result;
            var root = Dom(page.Html);
            foreach (var section in root.Descendants().Where(n => Schema(n, "TVSeason")))
            {
                var season = Number(Field(section, "seasonNumber"));
                if (!season.HasValue && (Field(section, "name") ?? "").Equals("Specials", StringComparison.OrdinalIgnoreCase)) season = 0;
                var link = Own(section).FirstOrDefault(n => Prop(n, "url"));
                if (link != null && season.HasValue)
                {
                    // This site uses document-relative links prefixed with episodenguide/.
                    var raw = link.GetAttributeValue("href", link.GetAttributeValue("content", ""));
                    var seriesBase = new Uri(SourceClient.Origin, page.Url.AbsolutePath.Split('/')[1] + "/");
                    var path = SourceClient.PathOf(raw, raw.StartsWith("episodenguide/") ? seriesBase : page.Url);
                    result.Add(new Entry { Kind = MediaKind.Season, Season = season, Name = Field(section, "name"), Path = path });
                }
                foreach (var row in section.Descendants().Where(n => Schema(n, "TVEpisode")))
                {
                    var a = row.Name == "a" ? row : row.Descendants("a").FirstOrDefault(n => Prop(n, "url"));
                    if (a == null) continue;
                    var path = SourceClient.PathOf(a.GetAttributeValue("href", ""), page.Url);
                    var number = Number(Field(row, "episodeNumber"));
                    // Zero denotes an unnumbered special on fernsehserien.de, not Emby's S00E00.
                    result.Add(new Entry { Kind = MediaKind.Episode, Season = season, Episode = number > 0 ? number : null, Name = Field(row, "name"), Path = path });
                }
            }
            return result.GroupBy(x => x.Path).Select(x => x.First()).ToList();
        }
        private static void Images(HtmlNode root, HtmlNode scope, Uri url, Entry e)
        {
            var candidates = Own(scope).Where(n => Prop(n, "image") || n.Name == "img" &&
                n.Ancestors().TakeWhile(a => a != scope).Any(a => Class(a, "episode-output-inhalt") || Class(a, "serienlogo"))).ToList();
            if (e.Kind == MediaKind.Series || e.Kind == MediaKind.Movie)
                candidates.AddRange(root.Descendants("meta").Where(n => n.GetAttributeValue("property", "") == "og:image"));
            foreach (var n in candidates)
            {
                var raw = n.GetAttributeValue("content", n.GetAttributeValue("src", n.GetAttributeValue("data-src", "")));
                int? width = Number(n.GetAttributeValue("width", n.GetAttributeValue("data-width", "")));
                int? height = Number(n.GetAttributeValue("height", n.GetAttributeValue("data-height", "")));
                if (n.GetAttributeValue("property", "") == "og:image")
                {
                    width = Number(root.Descendants("meta").FirstOrDefault(x => x.GetAttributeValue("property", "") == "og:image:width")?.GetAttributeValue("content", ""));
                    height = Number(root.Descendants("meta").FirstOrDefault(x => x.GetAttributeValue("property", "") == "og:image:height")?.GetAttributeValue("content", ""));
                }
                var picture = n.Ancestors("picture").FirstOrDefault();
                // Width descriptors are directly comparable; density descriptors use the media query width.
                double best = width ?? 0;
                foreach (var source in (picture == null ? new[] { n }.AsEnumerable() : picture.Descendants().Where(x => x.Name == "source" || x.Name == "img")))
                foreach (var part in source.GetAttributeValue("srcset", "").Split(','))
                {
                    var m = Regex.Match(part.Trim(), @"^(\S+)\s+([\d.]+)([wx])$");
                    if (!m.Success || !double.TryParse(m.Groups[2].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var size)) continue;
                    if (m.Groups[3].Value == "x") size *= Capture(source.GetAttributeValue("media", ""), @"max-width:\s*(\d+)") ?? width ?? 0;
                    if (size > best) { best = size; raw = m.Groups[1].Value; }
                }
                string address;
                try { address = SourceClient.Validate(raw, true, url).AbsoluteUri; } catch (ArgumentException) { continue; }
                var cls = n.GetAttributeValue("class", "");
                string type = e.Kind == MediaKind.Episode ? "Primary" : cls.Contains("clearlogo") ? "Logo" :
                    width.HasValue && height > 0 ? (width < height ? "Primary" : (double)width.Value / height.Value >= 3 ? "Banner" : "Backdrop") :
                    address.Contains("/sendung/") ? "Primary" : null;
                if (type == null) continue;
                if (best > 0 && width > 0 && height > 0) { height = (int)Math.Round(height.Value * best / width.Value); width = (int)best; }
                if (!e.Pictures.Any(x => x.Url == address)) e.Pictures.Add(new Picture { Url = address, Width = width, Height = height, Type = type });
            }
        }
    }
}

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using Emby.Plugin.Fernsehserien.HtmlParser;

namespace Emby.Plugin.Fernsehserien
{
    internal static partial class Scraper
    {
        private static readonly Dictionary<string, string> GenreNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Sci-Fi"] = "Science-Fiction", ["Science Fiction"] = "Science-Fiction", ["Science-Fiction"] = "Science-Fiction",
            ["Comedy"] = "Komödie", ["Komödie"] = "Komödie", ["Doku"] = "Dokumentation", ["Documentary"] = "Dokumentation",
            ["Dokumentation"] = "Dokumentation", ["Crime"] = "Krimi", ["Krimiserie"] = "Krimi", ["Krimi"] = "Krimi",
            ["Actionserie"] = "Action", ["Action-Serie"] = "Action", ["Action"] = "Action",
            ["Dramaserie"] = "Drama", ["Drama-Serie"] = "Drama", ["Drama"] = "Drama",
            ["Thriller-Serie"] = "Thriller", ["Thrillerserie"] = "Thriller", ["Thriller"] = "Thriller",
            ["Mystery"] = "Mystery", ["Fantasy"] = "Fantasy", ["Anime"] = "Anime", ["Animation"] = "Animation", ["True Crime"] = "True Crime"
        };
        private static void AddClean(List<string> values, string raw)
        {
            var value = Regex.Replace(HtmlEntity.DeEntitize(raw ?? ""), @"\s+", " ").Trim();
            if (value.Length > 0 && !values.Contains(value, StringComparer.OrdinalIgnoreCase)) values.Add(value);
        }
        private static void Genre(Entry e, string raw)
        {
            var value = Regex.Replace(raw ?? "", @"\s+", " ").Trim();
            if (Regex.IsMatch(value, @"^Fantasy-?\s*&\s*Sci-Fi(?:-Serien)?$", RegexOptions.IgnoreCase))
            { AddClean(e.Genres, "Fantasy"); AddClean(e.Genres, "Science-Fiction"); return; }
            var baseGenre = Regex.Replace(value, @"-(?:Serie|Serien)$", "", RegexOptions.IgnoreCase);
            AddClean(e.Genres, GenreNames.TryGetValue(baseGenre, out var canonical) ? canonical : value);
        }
        private static string Original(HtmlNode scope, HtmlNode production)
        {
            var explicitNode = Own(scope).FirstOrDefault(n => Prop(n, "originalName") || Class(n, "serie-infos-originaltitel"));
            var label = Own(scope).FirstOrDefault(n => n.Name == "dt" && Text(n).TrimEnd(':').Equals("Originaltitel", StringComparison.OrdinalIgnoreCase));
            var labelledValue = label?.NextSibling;
            while (labelledValue != null && labelledValue.NodeType != HtmlNodeType.Element) labelledValue = labelledValue.NextSibling;
            var candidate = Value(explicitNode) ?? (labelledValue?.Name == "dd" ? Text(labelledValue) : null) ??
                (production == null ? null : Value(Own(production).FirstOrDefault(n => n.Name == "span" && n.Attributes["lang"] != null))) ?? Field(scope, "alternateName");
            if (string.IsNullOrWhiteSpace(candidate)) candidate = Regex.Match(Text(production) ?? "", @"\(([^()]*\p{L}[^()]*)\)\s*$").Groups[1].Value;
            candidate = Regex.Replace(candidate ?? "", @"^Originaltitel\s*:\s*", "", RegexOptions.IgnoreCase);
            candidate = Regex.Replace(candidate, @",\s*\d+\s*Min\..*$", "").Trim();
            return candidate.Length == 0 || Regex.IsMatch(candidate, @"^\d+(?:\s*Min\.?)?$", RegexOptions.IgnoreCase) ? null : candidate;
        }
        private static void Enrich(HtmlNode root, HtmlNode scope, HtmlNode production, Entry e)
        {
            e.OriginalTitle = Original(scope, production);
            var own = Own(scope).ToArray();
            foreach (var n in own.Where(n => Prop(n, "genre"))) Genre(e, Value(n));
            foreach (var n in own.Where(n => Class(n, "genrepillen")).SelectMany(n => n.Descendants("li"))) Genre(e, Text(n));
            foreach (var n in own.Where(n => Prop(n, "countryOfOrigin"))) AddClean(e.Countries, Value(n));
            foreach (var n in own.Where(n => Prop(n, "productionCompany"))) AddClean(e.Studios, Field(n, "name") ?? Text(n));
            // Some episode pages place their own cast section after the TVEpisode scope.
            var castHeader = e.Kind == MediaKind.Season ? null : root.Descendants().FirstOrDefault(n =>
                n.GetAttributeValue("id", "").Equals("Cast-Crew", StringComparison.OrdinalIgnoreCase) &&
                !n.Ancestors().Any(a => a.Name == "aside" || a.Name == "nav" || Schema(a, "TVEpisode") && a != scope));
            var castSection = castHeader?.Ancestors("section").FirstOrDefault();
            var credits = own.Concat(castSection == null ? Enumerable.Empty<HtmlNode>() : Own(castSection)).Distinct().ToArray();
            foreach (var company in credits.Where(n => Prop(n, "productionCompany"))) AddClean(e.Studios, Field(company, "name") ?? Text(company));
            int actors = 0;
            foreach (var n in credits.Where(n => Schema(n, "Person")))
            {
                string type = Prop(n, "actor") ? "Actor" : Prop(n, "director") ? "Director" : Prop(n, "author") ? "Writer" : Prop(n, "producer") ? "Producer" : null;
                // A creator is not necessarily a writer; only an explicit writing credit qualifies.
                var credit = Text(n.Descendants("dd").FirstOrDefault());
                if (type == null && Prop(n, "creator") && Regex.IsMatch(credit ?? "", @"\b(Drehbuch|Screenplay|Writer)\b", RegexOptions.IgnoreCase)) type = "Writer";
                var name = Field(n, "name");
                if (type == null || string.IsNullOrWhiteSpace(name)) continue;
                var role = type == "Actor" ? credit : null;
                if (e.People.Any(p => p.Type == type && Matching.Normalize(p.Name) == Matching.Normalize(name) && Matching.Normalize(p.Role) == Matching.Normalize(role))) continue;
                if (type == "Actor" && actors++ >= 10) continue;
                e.People.Add(new Person { Name = name, Type = type, Role = role });
            }
            var originals = new List<DateTimeOffset>();
            foreach (var n in own.Where(n => n.Name == "ea-angabe"))
            {
                var label = Text(n.Descendants("ea-angabe-titel").FirstOrDefault()) ?? "";
                bool original = label.StartsWith("Original", StringComparison.OrdinalIgnoreCase);
                bool premiere = Regex.IsMatch(label, @"(?:Premiere|Kinostart)$", RegexOptions.IgnoreCase) && label.IndexOf("Wiederholung", StringComparison.OrdinalIgnoreCase) < 0;
                if (!premiere) continue;
                if (original || label.StartsWith("Deutsch", StringComparison.OrdinalIgnoreCase))
                    AddClean(e.Studios, Text(n.Descendants("ea-angabe-sender").FirstOrDefault()));
                var raw = Value(n.Descendants("time").FirstOrDefault()) ?? Text(n.Descendants("ea-angabe-datum").FirstOrDefault());
                var date = Regex.Match(raw ?? "", @"\d{4}-\d{2}-\d{2}|\d{2}\.\d{2}\.\d{4}").Value;
                if (DateTimeOffset.TryParseExact(date, new[] { "yyyy-MM-dd", "dd.MM.yyyy" }, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed))
                { e.PremiereYears.Add(parsed.Year); if (original) originals.Add(parsed); }
            }
            e.Premiere = originals.Count == 0 ? (DateTimeOffset?)null : originals.Min();
            var conflicting = new HashSet<string>();
            foreach (var n in own.Where(n => Prop(n, "sameAs") ||
                (e.Kind == MediaKind.Series || e.Kind == MediaKind.Movie) && n.Name == "a" && n.GetAttributeValue("data-event-category", "") == "sonstige-links"))
            {
                var raw = HtmlEntity.DeEntitize(n.GetAttributeValue("href", n.GetAttributeValue("content", "")));
                if (!Uri.TryCreate(raw, UriKind.Absolute, out var url) || (url.Scheme != "https" && url.Scheme != "http") || url.UserInfo.Length > 0) continue;
                var host = url.Host.ToLowerInvariant(); if (host.StartsWith("www.")) host = host.Substring(4);
                string key = null, id = null;
                if (host == "imdb.com") { key = "Imdb"; id = Regex.Match(url.AbsolutePath, @"^/title/(tt\d+)(?:/|$)").Groups[1].Value; }
                if (host == "themoviedb.org") { key = "Tmdb"; id = Regex.Match(url.AbsolutePath, @"^/(?:movie|tv)/(\d+)(?:[/-]|$)").Groups[1].Value; }
                if (host == "thetvdb.com")
                {
                    key = "Tvdb";
                    var entity = e.Kind == MediaKind.Series ? "series" : e.Kind == MediaKind.Movie ? "movie" : e.Kind == MediaKind.Episode ? "episode" : null;
                    if (entity == null) continue;
                    id = Regex.Match(url.AbsolutePath, "^/dereferrer/" + entity + @"/(\d+)/?$").Groups[1].Value;
                    if (id.Length == 0 && Regex.IsMatch(url.Query, @"(?:^\?|&)tab=" + entity + "(?:&|$)", RegexOptions.IgnoreCase))
                        id = Regex.Match(url.Query, @"(?:^\?|&)id=(\d+)(?:&|$)").Groups[1].Value;
                }
                if (key == null || string.IsNullOrWhiteSpace(id) || conflicting.Contains(key)) continue;
                if (e.Ids.TryGetValue(key, out var previous) && previous != id) { e.Ids.Remove(key); conflicting.Add(key); }
                else e.Ids[key] = id;
            }
        }
    }
}

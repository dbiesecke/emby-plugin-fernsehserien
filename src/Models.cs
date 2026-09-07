using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace Emby.Plugin.Fernsehserien
{
    internal enum MediaKind { Series, Movie, Season, Episode }
    internal sealed class Entry
    {
        public string Path, Name, OriginalTitle, Overview;
        public MediaKind Kind;
        public int? Year, Season, Episode, Minutes;
        public DateTimeOffset? Premiere;
        public readonly HashSet<int> PremiereYears = new HashSet<int>();
        public readonly List<string> Genres = new List<string>();
        public readonly List<string> Countries = new List<string>();
        public readonly List<string> Studios = new List<string>();
        public readonly List<Person> People = new List<Person>();
        public readonly Dictionary<string, string> Ids = new Dictionary<string, string>();
        public readonly List<Picture> Pictures = new List<Picture>();
    }
    internal sealed class Person { public string Name, Role, Type; }
    internal sealed class Picture { public string Url, Type; public int? Width, Height; }
    internal static class Matching
    {
        public static string Normalize(string s) => Regex.Replace((s ?? "").Normalize(NormalizationForm.FormKC).ToLowerInvariant(), @"[^\p{L}\p{N}]", "");
        public static Entry Unique(IEnumerable<Entry> entries, string title, int? year, MediaKind kind)
        {
            var name = Normalize(title);
            var matches = entries.Where(x => x.Kind == kind && name.Length > 0 &&
                (Normalize(x.Name) == name || Normalize(x.OriginalTitle) == name) &&
                (!year.HasValue || x.Year == year || kind == MediaKind.Series && x.PremiereYears.Contains(year.Value))).GroupBy(x => x.Path).Select(x => x.First()).Take(2).ToArray();
            return matches.Length == 1 ? matches[0] : null;
        }
    }
}

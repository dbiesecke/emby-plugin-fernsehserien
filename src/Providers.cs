using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Common.Net;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Configuration;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Logging;
using MediaBrowser.Model.Providers;

namespace Emby.Plugin.Fernsehserien
{
    public sealed class Plugin : BasePlugin
    {
        public const string ProviderKey = "Fernsehserien";
        public override string Name => "fernsehserien.de";
        public override string Description => "Deutsche Metadaten und Bilder für Serien, Filme, Staffeln und Episoden.";
        public override Guid Id => new Guid("514f0721-eeb0-4ce5-aeb7-bf71b94bf222");
    }
    public sealed class FernsehserienId : IExternalId
    {
        public string Name => "fernsehserien.de (Seitenpfad)";
        public string Key => Plugin.ProviderKey;
        public string UrlFormatString => "https://www.fernsehserien.de/{0}";
        public bool Supports(IHasProviderIds item) => item is Series || item is Movie || item is Season || item is Episode;
    }
    internal static class Catalog
    {
        public static string Id(IDictionary<string,string> ids) => ids != null && ids.TryGetValue(Plugin.ProviderKey, out var id) ? id : null;
        public static async Task<List<Entry>> Search(string name, MediaKind kind, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(name)) return new List<Entry>();
            var query = string.Join("+", name.Trim().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries).Select(Uri.EscapeDataString));
            var page = await SourceClient.Shared.Get("/suche/" + query, true, ct).ConfigureAwait(false);
            var hits = Scraper.Search(page).Where(e => e.Kind == kind).ToList();
            // Enrich all bounded candidates before automatic matching; never choose from a partial success set.
            return (await Task.WhenAll(hits.Select(async e => await Detail(e.Path, kind, ct).ConfigureAwait(false))).ConfigureAwait(false)).Where(e => e != null).ToList();
        }
        public static async Task<Entry> Detail(string id, MediaKind kind, CancellationToken ct) =>
            Scraper.Detail(await SourceClient.Shared.Get(id, false, ct).ConfigureAwait(false), kind);
        public static async Task<List<Entry>> Children(string seriesId, MediaKind kind, int? season, int? episode, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(seriesId) || !season.HasValue) return new List<Entry>();
            var series = await Detail(seriesId, MediaKind.Series, ct).ConfigureAwait(false);
            if (series == null) return new List<Entry>();
            var guide = await SourceClient.Shared.Get(series.Path + "/episodenguide", false, ct).ConfigureAwait(false);
            var entries = Scraper.Guide(guide).Where(e => e.Kind == kind && e.Season == season && (kind != MediaKind.Episode || e.Episode == episode && episode > 0)).ToList();
            if (entries.Count != 1) return new List<Entry>();
            var full = await Detail(entries[0].Path, kind, ct).ConfigureAwait(false);
            if (full == null) return new List<Entry>();
            if (full.Season.HasValue && full.Season != season || kind == MediaKind.Episode && full.Episode != episode) return new List<Entry>();
            full.Season = season;
            return new List<Entry> { full };
        }
        public static async Task<HttpResponseInfo> Image(string url, CancellationToken ct)
        {
            var result = await SourceClient.Shared.Image(url, ct).ConfigureAwait(false);
            return new HttpResponseInfo { Content = new MemoryStream(result.Data, false), ContentLength = result.Data.Length, ContentType = result.Type, ResponseUrl = url, StatusCode = HttpStatusCode.OK };
        }
    }
    public abstract class MetadataProvider<T, TInfo> : IRemoteMetadataProvider<T, TInfo>, IHasOrder where T : BaseItem, IHasLookupInfo<TInfo>, new() where TInfo : ItemLookupInfo, new()
    {
        private readonly ILogger logger;
        protected MetadataProvider(ILogManager logs) { logger = logs.GetLogger("Fernsehserien"); }
        internal abstract MediaKind Kind { get; }
        public string Name => "fernsehserien.de";
        public int Order => 1;
        private async Task<List<Entry>> Candidates(TInfo info, CancellationToken ct)
        {
            var id = Catalog.Id(info.ProviderIds);
            if (!string.IsNullOrWhiteSpace(id))
            {
                var found = await Catalog.Detail(id, Kind, ct).ConfigureAwait(false);
                return found == null ? new List<Entry>() : new List<Entry> { found };
            }
            if (info is SeasonInfo s && s.SeriesDisplayOrder != SeriesDisplayOrder.Aired || info is EpisodeInfo ep && ep.SeriesDisplayOrder != SeriesDisplayOrder.Aired) return new List<Entry>();
            if (info is SeasonInfo season) return await Catalog.Children(Catalog.Id(season.SeriesProviderIds), Kind, info.IndexNumber, null, ct).ConfigureAwait(false);
            if (info is EpisodeInfo episode) return await Catalog.Children(Catalog.Id(episode.SeriesProviderIds), Kind, info.ParentIndexNumber, info.IndexNumber, ct).ConfigureAwait(false);
            return await Catalog.Search(info.Name, Kind, ct).ConfigureAwait(false);
        }
        public async Task<IEnumerable<RemoteSearchResult>> GetSearchResults(TInfo info, CancellationToken ct)
        {
            try
            {
                return (await Candidates(info, ct).ConfigureAwait(false)).Select(e => new RemoteSearchResult
                {
                    Name = e.Name, OriginalTitle = e.OriginalTitle, ProductionYear = e.Year, Overview = e.Overview,
                    IndexNumber = e.Kind == MediaKind.Season ? e.Season : e.Episode, ParentIndexNumber = e.Kind == MediaKind.Episode ? e.Season : null,
                    ProviderIds = new ProviderIdDictionary { { Plugin.ProviderKey, e.Path.TrimStart('/') } }, SearchProviderName = Name,
                    ImageUrl = e.Pictures.FirstOrDefault(p => p.Type == "Primary")?.Url
                }).ToArray();
            }
            catch (Exception ex) when (Expected(ex)) { logger.Warn("fernsehserien.de search failed: {0}", ex.GetType().Name); return Array.Empty<RemoteSearchResult>(); }
        }
        public async Task<MetadataResult<T>> GetMetadata(TInfo info, CancellationToken ct)
        {
            try
            {
                var entries = await Candidates(info, ct).ConfigureAwait(false);
                bool byId = !string.IsNullOrWhiteSpace(Catalog.Id(info.ProviderIds));
                var e = byId || Kind == MediaKind.Season || Kind == MediaKind.Episode ? (entries.Count == 1 ? entries[0] : null) : Matching.Unique(entries, info.Name, info.Year, Kind);
                if (e == null) return new MetadataResult<T>();
                var item = new T { Name = e.Name, OriginalTitle = e.OriginalTitle, Overview = e.Overview, ProductionYear = e.Year, PremiereDate = e.Premiere,
                    Genres = e.Genres.ToArray(), ProductionLocations = e.Countries.ToArray(), Studios = e.Studios.ToArray(),
                    RunTimeTicks = e.Minutes.HasValue ? (long?)TimeSpan.FromMinutes(e.Minutes.Value).Ticks : null };
                // Explicitly identified unnumbered specials keep the library's existing numbering.
                if (Kind == MediaKind.Season) item.IndexNumber = e.Season ?? info.IndexNumber;
                if (Kind == MediaKind.Episode) { item.IndexNumber = e.Episode > 0 ? e.Episode : info.IndexNumber; item.ParentIndexNumber = e.Season ?? info.ParentIndexNumber; }
                item.SetProviderId(Plugin.ProviderKey, e.Path.TrimStart('/'));
                foreach (var id in e.Ids) item.SetProviderId(id.Key, id.Value);
                var result = new MetadataResult<T> { Item = item, HasMetadata = true, ResultLanguage = "de", QueriedById = byId };
                foreach (var p in e.People) result.AddPerson(new PersonInfo { Name = p.Name, Role = p.Role, Type = (PersonType)Enum.Parse(typeof(PersonType), p.Type) });
                return result;
            }
            catch (Exception ex) when (Expected(ex)) { logger.Warn("fernsehserien.de metadata failed: {0}", ex.GetType().Name); return new MetadataResult<T>(); }
        }
        internal static bool Expected(Exception ex) => ex is HttpRequestException || ex is InvalidDataException || ex is ArgumentException || ex is UriFormatException;
        public Task<HttpResponseInfo> GetImageResponse(string url, CancellationToken ct) => Catalog.Image(url, ct);
    }
    public sealed class SeriesProvider : MetadataProvider<Series, SeriesInfo> { public SeriesProvider(ILogManager logs) : base(logs) { } internal override MediaKind Kind => MediaKind.Series; }
    public sealed class MovieProvider : MetadataProvider<Movie, MovieInfo> { public MovieProvider(ILogManager logs) : base(logs) { } internal override MediaKind Kind => MediaKind.Movie; }
    public sealed class SeasonProvider : MetadataProvider<Season, SeasonInfo> { public SeasonProvider(ILogManager logs) : base(logs) { } internal override MediaKind Kind => MediaKind.Season; }
    public sealed class EpisodeProvider : MetadataProvider<Episode, EpisodeInfo> { public EpisodeProvider(ILogManager logs) : base(logs) { } internal override MediaKind Kind => MediaKind.Episode; }
    public sealed class ImageProvider : IRemoteImageProvider, IHasOrder
    {
        private readonly ILogger logger;
        public ImageProvider(ILogManager logs) { logger = logs.GetLogger("Fernsehserien"); }
        public string Name => "fernsehserien.de";
        public int Order => 1;
        public bool Supports(BaseItem item) => item is Series || item is Movie || item is Season || item is Episode;
        public IEnumerable<ImageType> GetSupportedImages(BaseItem item) => item is Episode ? new[] { ImageType.Primary } : new[] { ImageType.Primary, ImageType.Backdrop, ImageType.Banner, ImageType.Logo };
        public async Task<IEnumerable<RemoteImageInfo>> GetImages(BaseItem item, LibraryOptions options, CancellationToken ct)
        {
            var id = item.GetProviderId(Plugin.ProviderKey);
            if (string.IsNullOrWhiteSpace(id) || !Supports(item)) return Array.Empty<RemoteImageInfo>();
            try
            {
                var entry = await Catalog.Detail(id, item is Movie ? MediaKind.Movie : item is Season ? MediaKind.Season : item is Episode ? MediaKind.Episode : MediaKind.Series, ct).ConfigureAwait(false);
                return entry?.Pictures.Select(p => new RemoteImageInfo { ProviderName = Name, Url = p.Url, Type = (ImageType)Enum.Parse(typeof(ImageType), p.Type), Width = p.Width, Height = p.Height }).ToArray() ?? Array.Empty<RemoteImageInfo>();
            }
            catch (Exception ex) when (MetadataProvider<Movie, MovieInfo>.Expected(ex)) { logger.Warn("fernsehserien.de images failed: {0}", ex.GetType().Name); return Array.Empty<RemoteImageInfo>(); }
        }
        public Task<HttpResponseInfo> GetImageResponse(string url, CancellationToken ct) => Catalog.Image(url, ct);
    }
}

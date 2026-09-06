// Test-only assembly. Never included in release packages.
using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Plugins;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Configuration;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Logging;
using Emby.Plugin.Fernsehserien;

public sealed class RuntimeChecks : IServerEntryPoint
{
    readonly IProviderManager manager;
    readonly ILogManager logs;
    readonly CancellationTokenSource stop = new CancellationTokenSource();
    Task run;
    public RuntimeChecks(IProviderManager manager, ILogManager logs) { this.manager = manager; this.logs = logs; }
    public void Run()
    {
        if (Environment.GetEnvironmentVariable("FERNSEHSERIEN_CI") != "1") return;
        run = Task.Run(Check);
    }
    static void Assert(bool value, string why) { if (!value) throw new Exception(why); }
    async Task Check()
    {
        try
        {
            await Task.Delay(10000, stop.Token);
            var options = new LibraryOptions();
            var series = await Metadata<Series, SeriesInfo>(new Series(), new SeriesInfo { ProviderIds = Ids("dark") }, options);
            var movie = await Metadata<Movie, MovieInfo>(new Movie(), new MovieInfo { ProviderIds = Ids("filme/inception") }, options);
            var season = await Metadata<Season, SeasonInfo>(new Season(), new SeasonInfo { IndexNumber = 1, SeriesProviderIds = Ids("dark") }, options);
            var episode = await Metadata<Episode, EpisodeInfo>(new Episode(), new EpisodeInfo { IndexNumber = 1, ParentIndexNumber = 1, SeriesProviderIds = Ids("dark") }, options);
            Assert(series.Item.Name == "Dark" && series.Item.ProductionYear == 2017, "series mapping");
            Assert(movie.Item.Name == "Inception" && movie.Item.ProductionYear == 2010, "movie mapping");
            Assert(season.Item.IndexNumber == 1 && episode.Item.IndexNumber == 1 && episode.Item.ParentIndexNumber == 1, "episode mapping");
            Assert(!string.IsNullOrWhiteSpace(episode.Item.Overview), "episode overview");
            Assert(manager.GetRemoteImageProviderInfo(series.Item, options).Any(p => p.Name == "fernsehserien.de"), "image provider registration");
            var imageProvider = new ImageProvider(logs);
            var pictures = (await imageProvider.GetImages(series.Item, options, stop.Token)).ToArray();
            Assert(pictures.Length > 0, "image discovery");
            using (var image = await imageProvider.GetImageResponse(pictures[0].Url, stop.Token)) Assert(image.ContentLength > 12, "image download");
            File.WriteAllText("/config/fernsehserien-result.txt", "PASS: provider registration, identify, series/movie/season/episode metadata, image registration and download\n");
        }
        catch (Exception e) { File.WriteAllText("/config/fernsehserien-result.txt", "FAIL: " + e + "\n"); }
    }
    async Task<MetadataResult<T>> Metadata<T, TI>(T item, TI info, LibraryOptions options) where T : BaseItem, IHasLookupInfo<TI>, new() where TI : ItemLookupInfo, new()
    {
        var provider = manager.GetEnabledMetadataProviders(item, options).OfType<IRemoteMetadataProvider<T, TI>>().Single(p => p.Name == "fernsehserien.de");
        var identified = (await provider.GetSearchResults(info, stop.Token)).ToArray();
        Assert(identified.Length == 1, "identify " + typeof(T).Name);
        var result = await provider.GetMetadata(info, stop.Token);
        Assert(result.HasMetadata && result.Item.GetProviderId("Fernsehserien") != null, "metadata " + typeof(T).Name);
        return result;
    }
    static ProviderIdDictionary Ids(string path) => new ProviderIdDictionary { { "Fernsehserien", path } };
    public void Dispose() { stop.Cancel(); }
}

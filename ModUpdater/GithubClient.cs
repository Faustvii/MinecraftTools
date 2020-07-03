using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Web;
using LazyCache;
using Microsoft.Extensions.Hosting;
using Semver;
using Serilog;

namespace ModUpdater {
    public class GithubClient {
        private readonly HttpClient _client;
        private readonly IAppCache _cache;
        private readonly IHostEnvironment _hostEnvironment;
        private readonly Settings _settings;
        public GithubClient(HttpClient client, IAppCache cache, IHostEnvironment hostEnvironment, Settings settings) {
            _settings = settings;
            _hostEnvironment = hostEnvironment;
            _cache = cache;
            client.BaseAddress = new System.Uri("https://api.github.com/");
            // GitHub API versioning
            client.DefaultRequestHeaders.Add("Accept", "application/vnd.github.v3+json");
            // GitHub requires a user-agent
            client.DefaultRequestHeaders.Add("User-Agent", "GithubHttpClient");
            _client = client;
        }

        public async Task < (bool UpToDate, string Version, Asset Asset) > IsLatestRelease(string currentVersion, string minecraftVersion, GithubProvider provider) {
            // Cache the response to avoid as many requests as possible to keep from hitting Rate limit.
            var latestRelease = await _cache.GetOrAddAsync($"release{provider.Author}{provider.Repository}", () => GetLatestRelease(provider.Author, provider.Repository), DateTimeOffset.Now.AddMinutes(5));
            var latestVersion = latestRelease.TagName;
            var isLatestVersion = latestRelease.TagName == currentVersion;
            var latestAsset = latestRelease.Assets.Where(x => Regex.IsMatch(x.Name, provider.AssetRegex)).FirstOrDefault();

            if (!provider.UseReleaseTagAsVersion) {
                (isLatestVersion, latestVersion, latestAsset) = FindLatestVersionFromAssetNames(currentVersion, provider, latestRelease, minecraftVersion);
            }

            return (isLatestVersion, latestVersion, latestAsset);
        }

        private(bool UpToDate, string Version, Asset Asset) FindLatestVersionFromAssetNames(string currentVersion, GithubProvider provider, GithubRelease latestRelease, string minecraftVersion) {
            if (string.IsNullOrWhiteSpace(currentVersion))
                currentVersion = "0";

            var currentMcSemvar = SemVersion.Parse(minecraftVersion);
            var currentSemvar = SemVersion.Parse(currentVersion);
            var releases = new List<SemVersion>();
            var allAssets = latestRelease.Assets.Where(x => Regex.IsMatch(x.Name, provider.AssetRegex));
            var assetsMatchingMinecraftVersion = ExtractSemVersionFromAssetNames(allAssets, provider.MinecraftVersionRegex).Where(x => x.Value == currentMcSemvar);
            var assets = ExtractSemVersionFromAssetNames(assetsMatchingMinecraftVersion.Select(x => x.Key), provider.ModVersionRegex);
            var latestReleaseAsset = GetLatestRelease(assets);
            return (currentSemvar >= latestReleaseAsset.Version, latestReleaseAsset.Version.ToString(), latestReleaseAsset.Asset);
        }

        private IDictionary<Asset, SemVersion> ExtractSemVersionFromAssetNames(IEnumerable<Asset> assets, string regexPattern) {
            var dictionary = new Dictionary<Asset, SemVersion>();
            foreach (var asset in assets) {
                var regexMatch = Regex.Match(asset.Name, regexPattern);
                var version = regexMatch.Groups.Values.Last().Value;
                var parsed = SemVersion.TryParse(version, out var semVersion);
                if (parsed) {
                    dictionary.Add(asset, semVersion);
                }
            }
            return dictionary;
        }

        private(Asset Asset, SemVersion Version) GetLatestRelease(IDictionary<Asset, SemVersion> releases) {
            var latestRelease = releases.First();

            foreach (var release in releases)
                if (SemVersion.Compare(release.Value, latestRelease.Value) > 0)
                    latestRelease = release;

            return (latestRelease.Key, latestRelease.Value);
        }

        private Asset GetLatestReleaseAsset(GithubProvider provider, GithubRelease latestRelease) {
            var releases = new Dictionary<Asset, SemVersion>();
            var assets = latestRelease.Assets.Where(x => Regex.IsMatch(x.Name, provider.AssetRegex));

            foreach (var asset in assets) {
                var regexMatch = Regex.Match(asset.Name, provider.ModVersionRegex);
                var version = regexMatch.Groups.Values.Last().Value;
                var parsed = SemVersion.TryParse(version, out var semVersion);
                if (parsed) {
                    releases.Add(asset, semVersion);
                }
            }

            var latestSemVer = GetLatestRelease(releases);
            var latestAsset = releases.Single(x => x.Value == latestSemVer.Version).Key;
            return latestAsset;
        }

        public async Task < (bool Success, string FileName) > DownloadLatestRelease(Asset latestReleaseAsset) {
            var fileName = await GetReleaseAssetAsync(latestReleaseAsset.BrowserDownloadUrl.ToString());
            return (true, fileName);
        }

        private async Task<GithubRelease> GetLatestRelease(string author, string repository) {
            var uri = await ReleaseEndpointBuilder(author, repository);
            var response = await _client.GetAsync(uri);
            var contentJson = await response.Content.ReadAsStringAsync();
            VerifyGitHubAPIResponse(response.StatusCode, contentJson);
            var githubReleases = JsonSerializer.Deserialize<GithubRelease[]>(contentJson);
            var latestRelease = githubReleases.OrderByDescending(x => x.PublishedAt).FirstOrDefault();

            return latestRelease;
        }

        private Task<string> ReleaseEndpointBuilder(string author, string repository) {
            return Task.FromResult($"repos/{author}/{repository}/releases");
        }

        private async Task<string> GetReleaseAssetAsync(string assetUrl) {
            try {
                var fileName = HttpUtility.UrlDecode(Path.GetFileName(assetUrl));
                var path = Path.Combine("downloads", fileName);
                using(var client = new WebClient()) {
                    await client.DownloadFileTaskAsync(new Uri(assetUrl), path);
                    return fileName;
                }
            } catch (Exception ex) {
                Log.Error(ex, "{errorMessage}", ex.Message);
                throw new Exception("Release assets download failed.");
            }
        }

        private static void VerifyGitHubAPIResponse(HttpStatusCode statusCode, string content) {
            switch (statusCode) {
                case HttpStatusCode.Forbidden when content.Contains("API rate limit exceeded"):
                    throw new Exception("GitHub API rate limit exceeded.");
                case HttpStatusCode.NotFound when content.Contains("Not Found"):
                    throw new Exception("GitHub Repo not found.");
                default:
                    {
                        if (statusCode != HttpStatusCode.OK) throw new Exception("GitHub API call failed.");
                        break;
                    }
            }
        }

        private static string CleanVersion(string version) {
            var cleanedVersion = version;
            cleanedVersion = cleanedVersion.StartsWith("v") ? cleanedVersion.Substring(1) : cleanedVersion;
            var buildDelimiterIndex = cleanedVersion.LastIndexOf("+", StringComparison.Ordinal);
            cleanedVersion = buildDelimiterIndex > 0 ?
                cleanedVersion.Substring(0, buildDelimiterIndex) :
                cleanedVersion;
            return cleanedVersion;
        }
    }
}
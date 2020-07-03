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

namespace ModUpdater.Curse {
    public class CurseClient {
        private HttpClient _client;
        private Settings _settings;
        private IHostEnvironment _hostEnvironment;
        private IAppCache _cache;

        public CurseClient(HttpClient client, IAppCache cache, IHostEnvironment hostEnvironment, Settings settings) {
            _settings = settings;
            _hostEnvironment = hostEnvironment;
            _cache = cache;
            client.BaseAddress = new System.Uri("https://addons-ecs.forgesvc.net");
            client.DefaultRequestHeaders.Add("User-Agent", "CurseHttpClient");

            _client = client;
        }

        public async Task < (bool UpToDate, string Version, LatestFile Asset) > IsLatestVersion(string currentVersion, CurseForgeProvider provider, string minecraftVersion) {

            var response = await _client.GetAsync($"/api/v2/addon/{provider.ProjectId}");
            var addon = await response.Content.ReadAsAsync<Addon>();

            if (string.IsNullOrWhiteSpace(currentVersion))
                currentVersion = "0";

            var currentMcSemvar = SemVersion.Parse(minecraftVersion);
            var currentSemvar = SemVersion.Parse(currentVersion);
            var mcVersionFiles = ExtractSemVersionFromAssetNames(addon.LatestFiles, provider.MinecraftVersionRegex, provider.ExtractFromField);
            var filesMatchingMinecraftVersion = mcVersionFiles.Where(x => x.Value == currentMcSemvar);
            var latestVersionFiles = ExtractSemVersionFromAssetNames(filesMatchingMinecraftVersion.Select(x => x.Key), provider.ModVersionRegex, provider.ExtractFromField);

            var latestFile = GetLatestRelease(latestVersionFiles);
            return (currentSemvar >= latestFile.Version, latestFile.Version.ToString(), latestFile.Asset);
        }

        public async Task < (bool Success, string FileName) > DownloadLatestRelease(LatestFile latestReleaseAsset) {
            var fileName = await GetReleaseAssetAsync(latestReleaseAsset.DownloadUrl.ToString());
            return (true, fileName);
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

        private IDictionary<LatestFile, SemVersion> ExtractSemVersionFromAssetNames(IEnumerable<LatestFile> assets, string regexPattern, string extractField) {
            var dictionary = new Dictionary<LatestFile, SemVersion>();
            foreach (var asset in assets) {
                var fileName = extractField.ToLower() switch {
                    "filename" => asset.FileName,
                    "displayname" => asset.DisplayName,
                    _ => asset.FileName,
                };
                var regexMatch = Regex.Match(fileName, regexPattern);
                var version = regexMatch.Groups.Values.Last().Value;
                var parsed = SemVersion.TryParse(version, out var semVersion);
                if (parsed) {
                    dictionary.Add(asset, semVersion);
                }
            }
            return dictionary;
        }

        private(LatestFile Asset, SemVersion Version) GetLatestRelease(IDictionary<LatestFile, SemVersion> releases) {
            var latestRelease = releases.First();

            foreach (var release in releases)
                if (SemVersion.Compare(release.Value, latestRelease.Value) > 0)
                    latestRelease = release;

            return (latestRelease.Key, latestRelease.Value);
        }
    }
}
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Semver;
using Serilog;

namespace ModUpdater.Masady {
    public class MasadyClient {
        private readonly HttpClient _client;
        public MasadyClient(HttpClient client) {
            _client = client;
            client.BaseAddress = new System.Uri("https://masa.dy.fi/mcmods/client_mods/");
            client.DefaultRequestHeaders.Add("User-Agent", "MasadyHttpClient");
        }

        public async Task < (bool UpToDate, string Version, string AssetUrl) > IsLatestVersion(string currentVersion, MasadyProvider provider, string minecraftVersion) {
            if (string.IsNullOrWhiteSpace(currentVersion))
                currentVersion = "0";

            var result = await _client.GetAsync($"?mcver={minecraftVersion}&mod={provider.ModName}");
            var response = await result.Content.ReadAsStringAsync();
            var regexMatch = Regex.Matches(response, provider.HtmlExtractorRegex);
            var assets = regexMatch.ToDictionary(x => x.Groups.Values.Skip(1).Last().Value, x => SemVersion.Parse(x.Groups.Values.Skip(1).First().Value));
            var currentSemvar = SemVersion.Parse(currentVersion);

            var latestAsset = GetLatestRelease(assets);
            var latestVersion = latestAsset.Version;
            var isLatestVersion = latestVersion == currentSemvar;

            return (isLatestVersion, latestVersion?.ToString(), latestAsset.Asset);
        }

        public async Task < (bool Success, string FileName) > DownloadLatestRelease(string assetUrl) {
            var fileName = await GetReleaseAssetAsync(assetUrl);
            return (true, fileName);
        }
        private(string Asset, SemVersion Version) GetLatestRelease(IDictionary<string, SemVersion> releases) {
            var latestRelease = releases.FirstOrDefault();

            foreach (var release in releases)
                if (SemVersion.Compare(release.Value, latestRelease.Value) > 0)
                    latestRelease = release;

            return (latestRelease.Key, latestRelease.Value);
        }

        private async Task<string> GetReleaseAssetAsync(string assetUrl) {
            try {
                var fileName = Path.GetFileName(assetUrl);
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
    }
}
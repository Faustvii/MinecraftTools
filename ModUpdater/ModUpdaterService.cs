using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using ModUpdater.Curse;
using ModUpdater.Masady;
using Serilog;
using Serilog.Core;

namespace ModUpdater {
    public class ModUpdaterService : IHostedService {
        private readonly GithubClient _githubClient;
        private readonly Settings _settings;
        private readonly IHostEnvironment _hostEnvironment;
        private readonly CurseClient _curseClient;
        private readonly MasadyClient _masadyClient;
        private readonly IHostApplicationLifetime _hostAppLifetime;

        public ModUpdaterService(GithubClient githubClient, Settings settings, IHostEnvironment hostEnvironment, CurseClient curseClient, MasadyClient masadyClient, IHostApplicationLifetime hostApplicationLifetime) {
            _hostEnvironment = hostEnvironment;
            _curseClient = curseClient;
            _masadyClient = masadyClient;
            _hostAppLifetime = hostApplicationLifetime;
            _settings = settings;
            _githubClient = githubClient;
        }

        public async Task StartAsync(CancellationToken cancellationToken) {
            Log.Information("-----------------------------------");
            Log.Information("Starting the process to update mods");
            Log.Information("-----------------------------------");
            Directory.CreateDirectory("downloads");

            var startTime = Stopwatch.GetTimestamp();

            var modUpdated = false;
            var updatedMods = 0;
            var previousMods = _settings.InstalledMods.Select(x => new Mod {
                CurrentVersion = x.CurrentVersion,
                    Filename = x.Filename,
                    Name = x.Name,
                    OverrideMinecraftVersion = x.OverrideMinecraftVersion,
                    ProviderKey = x.ProviderKey
            }).ToList();
            foreach (var mod in _settings.InstalledMods) {
                var provider = _settings.Providers.FirstOrDefault(x => x.Key == mod.ProviderKey);
                Log.Information("Checking for updates on {ModName} for Minecraft: {MinecraftVersion}", mod.Name, _settings.Minecraft.Version);
                if (provider == null)
                    continue;
                switch (provider) {
                    case GithubProvider githubProvider:
                        var githubUpdate = await RunGithubJob(githubProvider, mod);
                        if (githubUpdate)
                            updatedMods++;

                        modUpdated |= githubUpdate;
                        break;
                    case CurseForgeProvider curseForgeProvider:
                        var curseUpdate = await RunCurseJob(curseForgeProvider, mod);
                        if (curseUpdate)
                            updatedMods++;

                        modUpdated |= curseUpdate;
                        break;
                    case MasadyProvider masadyProvider:
                        var masadyUpdate = await RunMasadyJob(masadyProvider, mod);
                        if (masadyUpdate)
                            updatedMods++;

                        modUpdated |= masadyUpdate;
                        break;
                    default:
                        break;
                }
            }

            //foreach(var mod in _settings.InstalledMods)
            //{
            //    if (!string.IsNullOrWhiteSpace(mod.Filename))
            //    {

            //    }
            //}

            foreach (var mod in previousMods) {
                if (!_settings.InstalledMods.Any(x => x.Filename == mod.Filename)) {
                    var updatedMod = _settings.InstalledMods.First(x => x.Name == mod.Name && x.ProviderKey == mod.ProviderKey);
                    if (!string.IsNullOrWhiteSpace(mod.Filename)) {
                        var originalFileName = Path.Combine(_settings.Minecraft.Path, _settings.Minecraft.ModFolderName, mod.Filename);
                        var newFileName = $"{Path.Combine(_settings.Minecraft.Path, _settings.Minecraft.ModFolderName, mod.Filename)}.disabled";
                        if (File.Exists(originalFileName)) {
                            File.Move(originalFileName, newFileName);
                        }
                    }

                    var latestStagingFile = Path.Combine("downloads", updatedMod.Filename);
                    var latestModFile = Path.Combine(_settings.Minecraft.Path, _settings.Minecraft.ModFolderName, updatedMod.Filename);
                    if (!File.Exists(latestModFile)) {
                        File.Move(latestStagingFile, latestModFile);
                    } else {
                        File.Delete(latestStagingFile);
                    }
                } else {
                    var updatedMod = _settings.InstalledMods.First(x => x.Name == mod.Name && x.ProviderKey == mod.ProviderKey);
                    if (string.IsNullOrWhiteSpace(updatedMod.Filename) || updatedMod.Filename == mod.Filename)
                        continue;

                    if (!string.IsNullOrWhiteSpace(mod.Filename)) {
                        var originalFileName = Path.Combine(_settings.Minecraft.Path, _settings.Minecraft.ModFolderName, mod.Filename);
                        var newFileName = $"{Path.Combine(_settings.Minecraft.Path, _settings.Minecraft.ModFolderName, mod.Filename)}.disabled";
                        if (File.Exists(originalFileName)) {
                            File.Move(originalFileName, newFileName);
                        }
                    }

                    var latestStagingFile = Path.Combine("downloads", updatedMod.Filename);
                    var latestModFile = Path.Combine(_settings.Minecraft.Path, _settings.Minecraft.ModFolderName, updatedMod.Filename);
                    if (!File.Exists(latestModFile) && File.Exists(latestStagingFile)) {
                        File.Move(latestStagingFile, latestModFile);
                    } else {
                        File.Delete(latestStagingFile);
                    }
                }
            }

            if (modUpdated) {
                await UpdateAppSettings();
            }

            Log.Information("-----------------------------------");
            Log.Information("Finished update process in {Elapsed:0.0000} ms", GetElapsedMilliseconds(startTime, Stopwatch.GetTimestamp()));
            Log.Information("{updatedMods} mod(s) were updated", updatedMods);
            Log.Information("-----------------------------------");
            _hostAppLifetime.StopApplication();
        }

        private static double GetElapsedMilliseconds(long start, long stop) {
            return (stop - start) * 1000 / (double) Stopwatch.Frequency;
        }

        private async Task<bool> RunMasadyJob(MasadyProvider provider, Mod mod) {
            var modCurrentVersion = mod.CurrentVersion;
            var mcVersion = string.IsNullOrWhiteSpace(mod.OverrideMinecraftVersion) ? _settings.Minecraft.Version : mod.OverrideMinecraftVersion;
            var latestRelease = await _masadyClient.IsLatestVersion(mod.CurrentVersion, provider, mcVersion);
            if (!latestRelease.UpToDate) {
                Log.Information("Downloading update for {ModName} {CurrentVersion} -> {LatestVersion}", mod.Name, mod.CurrentVersion, latestRelease.Version);
                var downloadResult = await _masadyClient.DownloadLatestRelease(latestRelease.AssetUrl);
                if (downloadResult.Success) {
                    mod.Filename = downloadResult.FileName;
                    mod.CurrentVersion = latestRelease.Version;
                }
            }
            return !latestRelease.UpToDate;
        }

        private async Task UpdateAppSettings() {
            var appsettingsFileInfo = _hostEnvironment.ContentRootFileProvider.GetFileInfo("appsettings.json");
            var json = JsonSerializer.Serialize(_settings, new JsonSerializerOptions {
                WriteIndented = true,
                    IgnoreNullValues = true,
            });
            await File.WriteAllTextAsync(appsettingsFileInfo.PhysicalPath, json, Encoding.UTF8);
        }

        private async Task<bool> RunCurseJob(CurseForgeProvider provider, Mod mod) {
            var mcVersion = string.IsNullOrWhiteSpace(mod.OverrideMinecraftVersion) ? _settings.Minecraft.Version : mod.OverrideMinecraftVersion;
            var latestRelease = await _curseClient.IsLatestVersion(mod.CurrentVersion, provider, mcVersion);
            if (!latestRelease.UpToDate) {
                Log.Information("Downloading update for {ModName} {CurrentVersion} -> {LatestVersion}", mod.Name, mod.CurrentVersion, latestRelease.Version);
                var downloadResult = await _curseClient.DownloadLatestRelease(latestRelease.Asset);
                if (downloadResult.Success) {
                    mod.Filename = downloadResult.FileName;
                    mod.CurrentVersion = latestRelease.Version;
                }
            }
            return !latestRelease.UpToDate;
        }

        private async Task<bool> RunGithubJob(GithubProvider provider, Mod mod) {
            var mcVersion = string.IsNullOrWhiteSpace(mod.OverrideMinecraftVersion) ? _settings.Minecraft.Version : mod.OverrideMinecraftVersion;
            var latestRelease = await _githubClient.IsLatestRelease(mod.CurrentVersion, mcVersion, provider);
            if (!latestRelease.UpToDate) {
                Log.Information("Downloading update for {ModName} {CurrentVersion} -> {LatestVersion}", mod.Name, mod.CurrentVersion, latestRelease.Version);
                var downloadResult = await _githubClient.DownloadLatestRelease(latestRelease.Asset);
                if (downloadResult.Success) {
                    mod.Filename = downloadResult.FileName;
                    mod.CurrentVersion = latestRelease.Version;
                }
            }
            return !latestRelease.UpToDate;
        }

        public Task StopAsync(CancellationToken cancellationToken) {
            return Task.CompletedTask;
        }
    }
}
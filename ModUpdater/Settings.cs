using System.Text.Json.Serialization;

namespace ModUpdater
{
    public class Settings {
        public Minecraft Minecraft { get; set; }
        public Mod[] InstalledMods { get; set; }
        public BaseProvider[] Providers { get; set; }
    }

    public class Mod {
        public string Name { get; set; }
        public string CurrentVersion { get; set; }
        public string Filename { get; set; }
        public string OverrideMinecraftVersion { get; set; }
        public string ProviderKey { get; set; }
    }

    public enum ProviderType {
        Unknown = 0,
        Masadyfi = 1,
        Github = 2,
        CurseForge = 3
    }

    [JsonConverter(typeof(ProviderConverter))]
    public abstract class BaseProvider {
        public ProviderType Type { get; set; }
        public string Key { get; set; }
    }

    public class MasadyProvider : BaseProvider {
        public string ModName { get; set; }
        public string HtmlExtractorRegex { get; set; }
    }

    public class GithubProvider : BaseProvider {
        public string Author { get; set; }
        public string Repository { get; set; }
        public string AssetRegex { get; set; }
        public string MinecraftVersionRegex { get; set; }
        public string ModVersionRegex { get; set; }
        public bool UseReleaseTagAsVersion { get; set; }
    }

    public class CurseForgeProvider : BaseProvider {
        public string MinecraftVersionRegex { get; set; }
        public string ModVersionRegex { get; set; }
        public string ExtractFromField { get; set; }
        public string ModVersionExtractFromField { get; set; }
        public long ProjectId { get; set; }
    }

    public class Minecraft {
        public string Version { get; set; }
        public string Path { get; set; }
        public string ModFolderName { get; set; }
    }
}
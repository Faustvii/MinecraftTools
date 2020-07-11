using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ModUpdater {
    public class ProviderConverter : JsonConverter<BaseProvider> {

        public override BaseProvider Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) {
            if (reader.TokenType != JsonTokenType.StartObject) {
                throw new JsonException();
            }

            if (reader.TokenType != JsonTokenType.StartObject) {
                throw new JsonException();
            }

            if (!reader.Read() ||
                reader.TokenType != JsonTokenType.PropertyName ||
                reader.GetString() != "Type") {
                throw new JsonException();
            }

            if (!reader.Read() || reader.TokenType != JsonTokenType.String) {
                throw new JsonException();
            }

            string discriminatorPropertyName = reader.GetString();

            // For performance, parse with ignoreCase:false first.
            if (!Enum.TryParse(discriminatorPropertyName, ignoreCase : false, out ProviderType discriminator) &&
                !Enum.TryParse(discriminatorPropertyName, ignoreCase : true, out discriminator)) {
                throw new JsonException(
                    $"Unable to convert \"{discriminatorPropertyName}\" to valid provider type.");
            }
            BaseProvider provider;

            if (!reader.Read() || reader.TokenType != JsonTokenType.PropertyName || reader.GetString() != "Key") {
                throw new JsonException();
            }

            if (!reader.Read() || reader.TokenType != JsonTokenType.String) {
                throw new JsonException();
            }

            string providerKey = reader.GetString();

            switch (discriminator) {
                case ProviderType.Masadyfi:
                    if (!reader.Read() || reader.GetString() != "Data") {
                        throw new JsonException();
                    }
                    if (!reader.Read() || reader.TokenType != JsonTokenType.StartObject) {
                        throw new JsonException();
                    }
                    provider = (MasadyProvider) JsonSerializer.Deserialize(ref reader, typeof(MasadyProvider));
                    break;
                case ProviderType.Github:
                    if (!reader.Read() || reader.GetString() != "Data") {
                        throw new JsonException();
                    }
                    if (!reader.Read() || reader.TokenType != JsonTokenType.StartObject) {
                        throw new JsonException();
                    }
                    provider = (GithubProvider) JsonSerializer.Deserialize(ref reader, typeof(GithubProvider));
                    break;
                case ProviderType.CurseForge:
                    if (!reader.Read() || reader.GetString() != "Data") {
                        throw new JsonException();
                    }
                    if (!reader.Read() || reader.TokenType != JsonTokenType.StartObject) {
                        throw new JsonException();
                    }
                    provider = (CurseForgeProvider) JsonSerializer.Deserialize(ref reader, typeof(CurseForgeProvider));
                    break;
                default:
                    throw new NotSupportedException();
            }

            if (!reader.Read() || reader.TokenType != JsonTokenType.EndObject) {
                throw new JsonException();
            }

            provider.Key = providerKey;
            provider.Type = discriminator;

            return provider;
        }

        public override void Write(Utf8JsonWriter writer, BaseProvider value, JsonSerializerOptions options) {
            writer.WriteStartObject();

            writer.WriteString(nameof(BaseProvider.Type), value.Type.ToString());
            writer.WriteString(nameof(BaseProvider.Key), value.Key);
            writer.WritePropertyName("Data");

            if (value is GithubProvider githubProvider) {
                writer.WriteStartObject();
                writer.WriteString(nameof(GithubProvider.Author), githubProvider.Author);
                writer.WriteString(nameof(GithubProvider.Repository), githubProvider.Repository);
                writer.WriteString(nameof(GithubProvider.AssetRegex), githubProvider.AssetRegex);
                writer.WriteString(nameof(GithubProvider.MinecraftVersionRegex), githubProvider.MinecraftVersionRegex);
                writer.WriteString(nameof(GithubProvider.ModVersionRegex), githubProvider.ModVersionRegex);
                writer.WriteBoolean(nameof(GithubProvider.UseReleaseTagAsVersion), githubProvider.UseReleaseTagAsVersion);
                writer.WriteEndObject();
                //JsonSerializer.Serialize(writer, githubProvider);
            } else if (value is CurseForgeProvider curseForgeProvider) {
                writer.WriteStartObject();
                writer.WriteString(nameof(CurseForgeProvider.MinecraftVersionRegex), curseForgeProvider.MinecraftVersionRegex);
                writer.WriteString(nameof(CurseForgeProvider.ModVersionRegex), curseForgeProvider.ModVersionRegex);
                writer.WriteString(nameof(CurseForgeProvider.ExtractFromField), curseForgeProvider.ExtractFromField);
                writer.WriteString(nameof(CurseForgeProvider.ModVersionExtractFromField), curseForgeProvider.ModVersionExtractFromField);
                writer.WriteNumber(nameof(CurseForgeProvider.ProjectId), curseForgeProvider.ProjectId);
                writer.WriteEndObject();

                // JsonSerializer.Serialize(writer, curseForgeProvider);
            } else if (value is MasadyProvider masadyProvider) {
                writer.WriteStartObject();
                writer.WriteString(nameof(MasadyProvider.HtmlExtractorRegex), masadyProvider.HtmlExtractorRegex);
                writer.WriteString(nameof(MasadyProvider.ModName), masadyProvider.ModName);
                writer.WriteEndObject();

                // JsonSerializer.Serialize(writer, masadyProvider);
            } else {
                throw new NotSupportedException();
            }

            writer.WriteEndObject();
        }

    }
}
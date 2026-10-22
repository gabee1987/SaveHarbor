using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace SaveHarbor.App.Infrastructure.Games.Windrose;

// The "WorldSettings" block of WorldDescription.json. Its keys are themselves JSON, e.g.
// "{\"TagName\": \"WDS.Parameter.MobHealthMultiplier\"}". The file comes from friends' saves, so it is untrusted:
// size, count and tag names are bounded, and anything that does not fit is skipped rather than shown.
public sealed partial record WindroseWorldSettings(
    IReadOnlyDictionary<string, bool> Flags,
    IReadOnlyDictionary<string, double> Multipliers,
    IReadOnlyDictionary<string, string> Choices)
{
    public const string TagPrefix = "WDS.Parameter.";
    private const long MaxFileBytes = 1024 * 1024;
    private const int MaxParameters = 200;

    public static WindroseWorldSettings Empty { get; } = new(
        new Dictionary<string, bool>(),
        new Dictionary<string, double>(),
        new Dictionary<string, string>());

    public static async Task<WindroseWorldSettings> ReadAsync(string descriptionPath, CancellationToken cancellationToken)
    {
        var file = new FileInfo(descriptionPath);
        if (!file.Exists || file.Length > MaxFileBytes)
        {
            return Empty;
        }

        try
        {
            await using var stream = file.OpenRead();
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            return document.RootElement.TryGetProperty("WorldDescription", out var description)
                && description.TryGetProperty("WorldSettings", out var settings)
                && settings.ValueKind == JsonValueKind.Object
                    ? Parse(settings)
                    : Empty;
        }
        catch (JsonException)
        {
            return Empty;
        }
    }

    private static WindroseWorldSettings Parse(JsonElement settings)
    {
        var flags = new Dictionary<string, bool>(StringComparer.Ordinal);
        var multipliers = new Dictionary<string, double>(StringComparer.Ordinal);
        var choices = new Dictionary<string, string>(StringComparer.Ordinal);
        var count = 0;

        foreach (var (group, values) in Groups(settings))
        {
            foreach (var parameter in values)
            {
                if (++count > MaxParameters || TagName(parameter.Name) is not { } tag)
                {
                    continue;
                }

                switch (group)
                {
                    case "BoolParameters" when parameter.Value.ValueKind is JsonValueKind.True or JsonValueKind.False:
                        flags[tag] = parameter.Value.GetBoolean();
                        break;
                    case "FloatParameters" when parameter.Value.ValueKind == JsonValueKind.Number
                        && parameter.Value.TryGetDouble(out var number) && double.IsFinite(number):
                        multipliers[tag] = number;
                        break;
                    case "TagParameters" when parameter.Value.ValueKind == JsonValueKind.Object
                        && parameter.Value.TryGetProperty("TagName", out var choice)
                        && choice.ValueKind == JsonValueKind.String
                        && IsTagName(choice.GetString()):
                        choices[tag] = choice.GetString()!;
                        break;
                }
            }
        }

        return new WindroseWorldSettings(flags, multipliers, choices);
    }

    private static IEnumerable<(string Group, JsonElement.ObjectEnumerator Values)> Groups(JsonElement settings)
    {
        foreach (var group in new[] { "BoolParameters", "FloatParameters", "TagParameters" })
        {
            if (settings.TryGetProperty(group, out var values) && values.ValueKind == JsonValueKind.Object)
            {
                yield return (group, values.EnumerateObject());
            }
        }
    }

    // Short name without the common prefix, e.g. "MobHealthMultiplier" or "Coop.SharedQuests".
    public static string ShortName(string tag) => tag.StartsWith(TagPrefix, StringComparison.Ordinal) ? tag[TagPrefix.Length..] : tag;

    private static string? TagName(string key)
    {
        var match = TagKeyPattern().Match(key);
        return match.Success && IsTagName(match.Groups[1].Value) ? match.Groups[1].Value : null;
    }

    private static bool IsTagName(string? value) => value is not null && TagNamePattern().IsMatch(value);

    [GeneratedRegex("\"TagName\"\\s*:\\s*\"([^\"]{1,160})\"")]
    private static partial Regex TagKeyPattern();

    [GeneratedRegex("^[A-Za-z0-9_.]{1,120}$")]
    private static partial Regex TagNamePattern();
}

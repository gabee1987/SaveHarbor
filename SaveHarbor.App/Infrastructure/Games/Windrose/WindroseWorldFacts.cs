using System.Globalization;
using SaveHarbor.App.Domain;

namespace SaveHarbor.App.Infrastructure.Games.Windrose;

// World details and rules for display. Only known settings get a friendly label; the inspector lists the rest.
public static class WindroseWorldFacts
{
    public static readonly IReadOnlyDictionary<string, string> FlagLabels = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["WDS.Parameter.Coop.SharedQuests"] = "Shared quests",
        ["WDS.Parameter.EasyExplore"] = "Easy exploration"
    };

    public static readonly IReadOnlyDictionary<string, string> MultiplierLabels = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["WDS.Parameter.MobHealthMultiplier"] = "Enemy health",
        ["WDS.Parameter.MobDamageMultiplier"] = "Enemy damage",
        ["WDS.Parameter.ShipsHealthMultiplier"] = "Enemy ship health",
        ["WDS.Parameter.ShipsDamageMultiplier"] = "Enemy ship damage",
        ["WDS.Parameter.BoardingDifficultyMultiplier"] = "Boarding difficulty",
        ["WDS.Parameter.Coop.StatsCorrectionModifier"] = "Co-op enemy scaling",
        ["WDS.Parameter.Coop.ShipStatsCorrectionModifier"] = "Co-op ship scaling"
    };

    public const string CombatDifficultyTag = "WDS.Parameter.CombatDifficulty";

    public static IReadOnlyList<WorldFact> From(GameWorld world, WindroseWorldSettings settings, string? dataVersion)
    {
        var facts = new List<WorldFact>();

        void Add(string label, string? value)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                facts.Add(new WorldFact(WorldFactGroup.Details, label, value));
            }
        }

        Add("Preset", world.Subtitle);
        Add("Combat", settings.Choices.TryGetValue(CombatDifficultyTag, out var combat) ? LastSegment(combat) : null);
        foreach (var (tag, label) in FlagLabels)
        {
            Add(label, settings.Flags.TryGetValue(tag, out var flag) ? (flag ? "On" : "Off") : null);
        }

        Add("Created", world.CreatedAt == DateTimeOffset.MinValue ? null : world.CreatedAt.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        Add("Last saved", world.LastModifiedAt.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture));
        Add("Save format", dataVersion);

        foreach (var (tag, label) in MultiplierLabels)
        {
            if (settings.Multipliers.TryGetValue(tag, out var value))
            {
                facts.Add(new WorldFact(WorldFactGroup.Rules, label, FormatMultiplier(value)));
            }
        }

        return facts;
    }

    public static string FormatMultiplier(double value) => $"×{value.ToString("0.##", CultureInfo.InvariantCulture)}";

    public static string LastSegment(string tag) => tag[(tag.LastIndexOf('.') + 1)..];
}

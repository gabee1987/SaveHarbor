using System.Globalization;
using SaveHarbor.App.Domain;

namespace SaveHarbor.App.Infrastructure.Games.Dragonwilds;

public static class DragonwildsWorldFacts
{
    // Only value 3 ("Custom") has been matched against the game's own world list; other presets are shown by number.
    private const int CustomDifficulty = 3;

    private static readonly IReadOnlyDictionary<string, string> RuleLabels = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["Difficulty.Progression.XPGainScale"] = "XP gain",
        ["Difficulty.Progression.CraftingCostScale"] = "Crafting cost",
        ["Difficulty.Progression.BuildingMaterialCostScale"] = "Building cost",
        ["Difficulty.Progression.ProcessingSpeedScale"] = "Processing speed",
        ["Difficulty.Player.InventoryCarryCapacityScale"] = "Carry capacity",
        ["Difficulty.Player.TeleportationCostScale"] = "Teleport cost"
    };

    public static IReadOnlyList<WorldFact> From(DragonwildsSaveInfo info)
    {
        var facts = new List<WorldFact>();

        void Add(string label, string? value)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                facts.Add(new WorldFact(WorldFactGroup.Details, label, value));
            }
        }

        Add("Created by", info.CreatedBy);
        Add("Difficulty", info.Difficulty switch
        {
            null => null,
            CustomDifficulty => "Custom",
            var preset => $"Preset {preset}"
        });
        Add("PvP", OnOff(info.FriendlyFire));
        Add("Crossplay", OnOff(info.Crossplay));
        Add("Access", info.PasswordProtected switch
        {
            null => null,
            true => "Password",
            false => "Open"
        });
        Add("Last saved", info.SavedAtUtc?.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture));
        Add("Game build", info.GameBuild);

        foreach (var key in DragonwildsSaveInfoReader.RuleKeys)
        {
            if (info.RuleScales.TryGetValue(key, out var scale))
            {
                facts.Add(new WorldFact(WorldFactGroup.Rules, RuleLabels[key], $"×{scale.ToString("0.##", CultureInfo.InvariantCulture)}"));
            }
        }

        return facts;
    }

    private static string? OnOff(bool? value) => value switch
    {
        null => null,
        true => "On",
        false => "Off"
    };
}

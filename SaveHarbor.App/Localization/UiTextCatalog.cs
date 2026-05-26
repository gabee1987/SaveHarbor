using System.IO;
using System.Text.Json;

namespace SaveHarbor.App.Localization;

public static class UiTextCatalog
{
    private static readonly Lazy<IReadOnlyDictionary<string, string>> Texts = new(LoadTexts);

    public static string Get(string key)
    {
        return Texts.Value.TryGetValue(key, out var value) ? value : key;
    }

    private static IReadOnlyDictionary<string, string> LoadTexts()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Resources", "Texts", "ui-text.en.json");
        if (!File.Exists(path))
        {
            return new Dictionary<string, string>();
        }

        using var stream = File.OpenRead(path);
        return JsonSerializer.Deserialize<Dictionary<string, string>>(stream) ?? new Dictionary<string, string>();
    }
}

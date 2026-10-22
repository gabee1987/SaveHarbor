using System.Windows.Markup;

namespace SaveHarbor.App.Localization;

// WPF has no letter-spacing; thin spaces between letters approximate the wide-tracked headings of the game's UI.
[MarkupExtensionReturnType(typeof(string))]
public sealed class SpacedTextExtension : MarkupExtension
{
    private const char ThinSpace = ' ';

    public SpacedTextExtension()
    {
    }

    public SpacedTextExtension(string key)
    {
        Key = key;
    }

    public string Key { get; set; } = string.Empty;

    public static string Apply(string text) => string.Join(ThinSpace, text.ToCharArray());

    public override object ProvideValue(IServiceProvider serviceProvider) => Apply(UiTextCatalog.Get(Key));
}

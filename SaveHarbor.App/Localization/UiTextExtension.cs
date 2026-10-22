using System.Windows.Markup;

namespace SaveHarbor.App.Localization;

[MarkupExtensionReturnType(typeof(string))]
public sealed class UiTextExtension : MarkupExtension
{
    public UiTextExtension()
    {
    }

    public UiTextExtension(string key)
    {
        Key = key;
    }

    public string Key { get; set; } = string.Empty;

    public override object ProvideValue(IServiceProvider serviceProvider)
    {
        return UiTextCatalog.Get(Key);
    }
}

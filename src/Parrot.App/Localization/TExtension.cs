using System.Windows.Markup;

namespace Parrot.App.Localization;

/// <summary>
/// <c>Text="{l:T Nav.Dictionary}"</c> — UI text in XAML. The lookup happens when the element is
/// created, so a language change applies to windows opened after it.
/// </summary>
[MarkupExtensionReturnType(typeof(string))]
public sealed class TExtension(string key) : MarkupExtension
{
    public string Key { get; } = key;

    public override object ProvideValue(IServiceProvider serviceProvider) => L.T(Key);
}

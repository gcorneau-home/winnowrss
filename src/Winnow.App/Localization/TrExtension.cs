using System.Windows.Data;
using System.Windows.Markup;

namespace Winnow.App.Localization;

/// <summary>XAML shorthand for a live-updating translated string: <c>Text="{loc:Tr Toolbar_Refresh}"</c>.</summary>
[MarkupExtensionReturnType(typeof(object))]
public sealed class TrExtension : MarkupExtension
{
    public TrExtension() { }

    public TrExtension(string key) => Key = key;

    [ConstructorArgument("key")]
    public string Key { get; set; } = "";

    public override object ProvideValue(IServiceProvider serviceProvider) =>
        new Binding($"[{Key}]") { Source = Localizer.Instance, Mode = BindingMode.OneWay }.ProvideValue(serviceProvider);
}

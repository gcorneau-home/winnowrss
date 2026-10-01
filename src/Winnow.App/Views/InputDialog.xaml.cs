using System.Windows;
using System.Windows.Input;

namespace Winnow.App.Views;

public partial class InputDialog : Window
{
    private readonly List<Field> _fields;

    public InputDialog(string title, IReadOnlyList<(string Label, string InitialValue)> fields)
    {
        InitializeComponent();
        Title = title;
        _fields = fields.Select(f => new Field { Label = f.Label, Value = f.InitialValue }).ToList();
        FieldList.ItemsSource = _fields;
        Loaded += (_, _) => MoveFocus(new TraversalRequest(FocusNavigationDirection.First));
    }

    public string[] Values => _fields.Select(f => f.Value).ToArray();

    private void Ok_Click(object sender, RoutedEventArgs e) => DialogResult = true;

    private sealed class Field
    {
        public string Label { get; init; } = "";
        public string Value { get; set; } = "";
    }
}

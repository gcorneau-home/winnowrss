using System.Windows;
using Winnow.App.ViewModels;

namespace Winnow.App.Views;

public partial class FilterSettingsWindow : Window
{
    public FilterSettingsWindow(FilterSettingsViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        viewModel.CloseRequested += saved => DialogResult = saved;
    }
}

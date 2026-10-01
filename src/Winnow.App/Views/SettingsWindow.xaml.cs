using System.Windows;
using Winnow.App.ViewModels;

namespace Winnow.App.Views;

public partial class SettingsWindow : Window
{
    public SettingsWindow(SettingsViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        viewModel.CloseRequested += saved => DialogResult = saved;
    }
}

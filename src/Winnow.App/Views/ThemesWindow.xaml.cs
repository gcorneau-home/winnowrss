using System.Windows;
using Winnow.App.ViewModels;

namespace Winnow.App.Views;

public partial class ThemesWindow : Window
{
    public ThemesWindow(ThemesViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }
}

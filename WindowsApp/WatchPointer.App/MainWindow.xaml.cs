using System.Windows;
using WatchPointer.App.Services;
using WatchPointer.App.ViewModels;
using WatchPointer.Core.Settings;

namespace WatchPointer.App;

public partial class MainWindow : Window
{
    private readonly ISettingsStore _settings;
    private bool _loading = true;

    public MainWindow(MainViewModel viewModel, ISettingsStore settings)
    {
        InitializeComponent();
        _settings = settings;
        DataContext = viewModel;
        Icon = TrayIcon.CreateWindowIcon();
        ThemePicker.SelectedIndex = (int)settings.Current.Ui.Theme;
        TrayCheck.IsChecked = settings.Current.Ui.MinimizeToTray;
        StartMinimizedCheck.IsChecked = settings.Current.Ui.StartMinimized;
        _loading = false;
    }

    private void OnThemeChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (_loading) return;
        var theme = (ThemePreference)ThemePicker.SelectedIndex;
        _settings.Update(s => s with { Ui = s.Ui with { Theme = theme } });
    }

    private void OnTrayChanged(object sender, RoutedEventArgs e)
    {
        var tray = TrayCheck.IsChecked == true;
        var startMinimized = StartMinimizedCheck.IsChecked == true;
        _settings.Update(s => s with { Ui = s.Ui with { MinimizeToTray = tray, StartMinimized = startMinimized } });
    }
}

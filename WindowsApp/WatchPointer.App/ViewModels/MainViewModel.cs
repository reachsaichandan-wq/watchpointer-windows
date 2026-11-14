using System.Collections.ObjectModel;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;

namespace WatchPointer.App.ViewModels;

/// <summary>Base for navigation pages. <see cref="Refresh"/> runs 4× a second while the page is visible.</summary>
public abstract partial class PageViewModel : ObservableObject
{
    public abstract string Title { get; }
    /// <summary>Segoe Fluent Icons / MDL2 glyph.</summary>
    public abstract string Glyph { get; }
    public virtual void Refresh() { }
    public virtual void OnShown() => Refresh();
}

public sealed partial class MainViewModel : ObservableObject
{
    private readonly DispatcherTimer _timer;

    public MainViewModel(DashboardViewModel dashboard, PairingViewModel pairing, PointerViewModel pointer,
        CalibrationViewModel calibration, GesturesViewModel gestures, ProfilesViewModel profiles,
        ShortcutsViewModel shortcuts, DiagnosticsViewModel diagnostics, AboutViewModel about)
    {
        Dashboard = dashboard;
        Pages = [dashboard, pairing, pointer, calibration, gestures, profiles, shortcuts, diagnostics, about];
        _selectedPage = dashboard;
        _timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(250) };
        _timer.Tick += (_, _) =>
        {
            Dashboard.Refresh();
            if (!ReferenceEquals(SelectedPage, Dashboard)) SelectedPage.Refresh();
        };
        _timer.Start();
    }

    public DashboardViewModel Dashboard { get; }
    public ObservableCollection<PageViewModel> Pages { get; }

    [ObservableProperty] private PageViewModel _selectedPage;

    partial void OnSelectedPageChanged(PageViewModel value) => value?.OnShown();
}

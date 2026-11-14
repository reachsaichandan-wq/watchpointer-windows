using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WatchPointer.App.Services;
using WatchPointer.Core.Profiles;
using WatchPointer.Core.Settings;

namespace WatchPointer.App.ViewModels;

public sealed record ProfileItem(string Name, ProfileKind Kind, bool Active, string Summary, bool LaserPointer, bool MovePointer, CrownMode Crown);

public sealed partial class ProfilesViewModel : PageViewModel
{
    private readonly ISettingsStore _settings;
    private readonly ReceiverHost _host;

    public ProfilesViewModel(ISettingsStore settings, ReceiverHost host)
    {
        _settings = settings;
        _host = host;
        settings.Changed += s => App.OnUi(() => Load(s));
        Load(settings.Current);
    }

    public override string Title => "Profiles";
    public override string Glyph => "";

    public ObservableCollection<ProfileItem> Profiles { get; } = [];

    [ObservableProperty] private ProfileItem? _selected;
    [ObservableProperty] private string _newName = "";
    [ObservableProperty] private string _message = "";
    [ObservableProperty] private bool _laserPointer;

    private void Load(AppSettings s)
    {
        var keep = Selected?.Name ?? s.ActiveProfile;
        Profiles.Clear();
        foreach (var p in s.Profiles)
        {
            var summary = string.Join(" · ", new[] { WatchInput.PadTap, WatchInput.SwipeLeft, WatchInput.ButtonA }
                .Select(i => $"{MappingRow.Describe(i).Label.Replace("Tap the pad", "Tap")}: {p.Resolve(i)}"));
            Profiles.Add(new ProfileItem(p.Name, p.Kind, p.Name == s.ActiveProfile, $"Crown: {p.Crown} · {summary}", p.LaserPointer, p.MovePointer, p.Crown));
        }
        Selected = Profiles.FirstOrDefault(p => p.Name == keep) ?? Profiles.FirstOrDefault();
    }

    partial void OnSelectedChanged(ProfileItem? value)
    {
        if (value is null) return;
        _laserPointerLoading = true;
        LaserPointer = value.LaserPointer;
        _laserPointerLoading = false;
    }

    private bool _laserPointerLoading;

    partial void OnLaserPointerChanged(bool value)
    {
        if (_laserPointerLoading || Selected is null) return;
        var name = Selected.Name;
        _settings.Update(s => s with { Profiles = s.Profiles.Select(p => p.Name == name ? p with { LaserPointer = value } : p).ToList() });
        if (name == _host.ActiveProfile.Name) _host.Laser = value;
    }

    [RelayCommand]
    private void Activate()
    {
        if (Selected is { } p) _host.SwitchProfile(p.Name);
    }

    [RelayCommand]
    private void Duplicate()
    {
        if (Selected is null) return;
        var name = string.IsNullOrWhiteSpace(NewName) ? $"{Selected.Name} copy" : NewName.Trim();
        if (_settings.Current.Profiles.Any(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)))
        {
            Message = $"A profile called \"{name}\" already exists.";
            return;
        }
        var source = _settings.Current.Profiles.First(p => p.Name == Selected.Name);
        var copy = source with { Name = name, Kind = ProfileKind.Custom, Mappings = new Dictionary<WatchInput, ActionSpec>(source.Mappings) };
        _settings.Update(s => s with { Profiles = [.. s.Profiles, copy] });
        NewName = "";
        Message = $"Created \"{name}\". Edit its gestures on the Gestures page.";
    }

    [RelayCommand]
    private void Rename()
    {
        if (Selected is null || string.IsNullOrWhiteSpace(NewName)) { Message = "Type a new name first."; return; }
        var old = Selected.Name;
        var name = NewName.Trim();
        if (_settings.Current.Profiles.Any(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase))) { Message = "That name is taken."; return; }
        if (IsBuiltIn(old)) { Message = "Built-in profiles keep their names; duplicate it instead."; return; }
        _settings.Update(s => s with
        {
            Profiles = s.Profiles.Select(p => p.Name == old ? p with { Name = name } : p).ToList(),
            ActiveProfile = s.ActiveProfile == old ? name : s.ActiveProfile,
        });
        NewName = "";
    }

    [RelayCommand]
    private void Delete()
    {
        if (Selected is null) return;
        if (IsBuiltIn(Selected.Name)) { Message = "Built-in profiles can be reset but not deleted."; return; }
        var name = Selected.Name;
        _settings.Update(s => s with
        {
            Profiles = s.Profiles.Where(p => p.Name != name).ToList(),
            ActiveProfile = s.ActiveProfile == name ? DefaultProfiles.Desktop : s.ActiveProfile,
        });
        Message = $"Deleted \"{name}\".";
    }

    [RelayCommand]
    private void ResetBuiltIn()
    {
        if (Selected is null || !IsBuiltIn(Selected.Name)) { Message = "Only built-in profiles can be reset."; return; }
        var fresh = DefaultProfiles.Create().First(p => p.Name == Selected.Name);
        _settings.Update(s => s with { Profiles = s.Profiles.Select(p => p.Name == fresh.Name ? fresh : p).ToList() });
        Message = $"\"{fresh.Name}\" restored to defaults.";
    }

    private static bool IsBuiltIn(string name) => DefaultProfiles.Create().Any(p => p.Name == name);
}

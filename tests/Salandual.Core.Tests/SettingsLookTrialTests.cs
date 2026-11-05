// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : tests/Salandual.Core.Tests/SettingsLookTrialTests.cs

using Salandual.Core.Settings;

namespace Salandual.Core.Tests;

public sealed class SettingsLookTrialTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("salandual-look-").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private static AppearanceSettings Dark(AppearanceSettings a) => a with { Schedule = a.Schedule with { Mode = ScheduleMode.None, Active = ThemeCatalog.SalandualDarkId } };

    private AppSettings Saved() => SettingsStore.InDirectory(_dir).Load().Settings;

    [Fact]
    public void TriedLook_IsShown_ButTheFileKeepsTheOldOne_UntilKeep()
    {
        using var service = SettingsService.Open(_dir, TimeSpan.FromHours(1));
        var trials = 0;
        service.LookTrialChanged += (_, _) => trials++;

        service.TryLook(Dark);
        Assert.Equal(ThemeCatalog.SalandualDarkId, service.Current.Appearance.Schedule.Active);
        Assert.NotNull(service.KeptLook);
        Assert.Equal(1, trials);

        service.Flush();
        Assert.Equal(ScheduleMode.Windows, Saved().Appearance.Schedule.Mode);

        service.KeepLook();
        Assert.Null(service.KeptLook);
        Assert.Equal(2, trials);
        service.Flush();
        Assert.Equal(ThemeCatalog.SalandualDarkId, Saved().Appearance.Schedule.Active);
    }

    [Fact]
    public void Revert_PutsBackTheLookFromBefore_WithWhatWasSavedAtOnceSince()
    {
        using var service = SettingsService.Open(_dir, TimeSpan.FromHours(1));
        service.TryLook(Dark);
        service.TryLook(a => a with { PanelFont = a.PanelFont with { Size = 14 } });

        service.Update(s => s with { Layout = s.Layout with { RowHeight = 30 } });
        var mine = new ColorTheme { Id = "mine-1", Name = "Mine" };
        service.Update(s => s with { Appearance = ThemeEditing.Add(s.Appearance, mine) });
        service.Flush();
        var saved = Saved();
        Assert.Equal(30, saved.Layout.RowHeight);
        Assert.Contains(saved.Appearance.Themes, t => t.Id == "mine-1");
        Assert.Equal(FontSetting.DefaultPanelSize, saved.Appearance.PanelFont.Size);

        var changed = 0;
        service.Changed += (_, _) => changed++;
        service.RevertLook();
        Assert.Equal(1, changed);
        Assert.Null(service.KeptLook);
        Assert.Equal(ScheduleMode.Windows, service.Current.Appearance.Schedule.Mode);
        Assert.Equal(FontSetting.DefaultPanelSize, service.Current.Appearance.PanelFont.Size);
        Assert.Equal(30, service.Current.Layout.RowHeight);
        Assert.Contains(service.Current.Appearance.Themes, t => t.Id == "mine-1");
    }

    [Fact]
    public void ChangedBackByHand_TheTrialEnds()
    {
        using var service = SettingsService.Open(_dir, TimeSpan.FromHours(1));
        var trials = 0;
        service.LookTrialChanged += (_, _) => trials++;
        service.TryLook(a => a with { PanelFont = a.PanelFont with { Size = 14 } });
        service.TryLook(a => a with { PanelFont = a.PanelFont with { Size = FontSetting.DefaultPanelSize } });
        Assert.Null(service.KeptLook);
        Assert.Equal(2, trials);
    }

    [Fact]
    public void KeepAndRevert_WithoutATrial_DoNothing()
    {
        using var service = SettingsService.Open(_dir, TimeSpan.FromHours(1));
        var events = 0;
        service.Changed += (_, _) => events++;
        service.LookTrialChanged += (_, _) => events++;
        service.KeepLook();
        service.RevertLook();
        Assert.Equal(0, events);
    }

    [Fact]
    public void KeptThemes_AreSaved_AndCleanedUp()
    {
        SettingsStore.InDirectory(_dir).Save(new AppSettings { Appearance = new() { KeptThemes = ["dracula", "", "dracula"] } });
        Assert.Equal(["dracula"], Saved().Appearance.KeptThemes);
    }
}

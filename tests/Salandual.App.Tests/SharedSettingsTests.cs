// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : tests/Salandual.App.Tests/SharedSettingsTests.cs

using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Salandual.App.Dialogs;
using Salandual.App.Options;
using Salandual.App.Sharing;
using Salandual.Core.Settings;

namespace Salandual.App.Tests;

public sealed class SharedSettingsTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("salandual-sharing-").FullName;

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private string Folder => Path.Combine(_root, "cloud", "Salandual");

    private (SettingsService Settings, SharedSettingsService Sharing) Pc(string name)
    {
        var settings = SettingsService.Open(Path.Combine(_root, name), TimeSpan.FromHours(1));
        return (settings, new SharedSettingsService(settings, Path.Combine(_root, name)));
    }

    private static void Changed(SharedSettingsService sharing)
    {
        Dispatcher.UIThread.RunJobs();
        sharing.Flush();
    }

    private static AppSettings Font(AppSettings s, double size) => s with { Appearance = s.Appearance with { PanelFont = s.Appearance.PanelFont with { Size = size } } };

    private static void Capture(Control c, string name)
    {
        if (Environment.GetEnvironmentVariable("SALANDUAL_CAPTURE_DIR") is { Length: > 0 } dir && c is TopLevel top)
        {
            Directory.CreateDirectory(dir);
            top.CaptureRenderedFrame()!.Save(Path.Combine(dir, name), PngBitmapEncoderOptions.Default);
        }
    }

    [AvaloniaFact]
    public void AChangeOnOnePc_IsTakenOnTheOther_SaidWithUndo()
    {
        var (desk, deskSharing) = Pc("desk");
        var (laptop, laptopSharing) = Pc("laptop");
        using (desk)
        using (laptop)
        using (deskSharing)
        using (laptopSharing)
        {
            deskSharing.TurnOn(new SharingSettings { Folder = Folder }, null);
            Assert.True(File.Exists(Path.Combine(Folder, SettingsSharing.FileName)));

            var peek = SharedSettingsService.Peek(Folder, laptop.Current, new SharingSettings().Parts.ToHashSet())!.Value;
            Assert.Empty(peek.Import.Changes);
            var read = peek.Copy;
            laptopSharing.TurnOn(new SharingSettings { Folder = Folder }, read);

            SharingTaken? said = null;
            laptopSharing.Taken += (_, t) => said = t;
            desk.Update(s => Font(s, 14) with { Windows = s.Windows.With(WindowSettings.Main, new WindowPlace { Width = 1500, Height = 900 }) });
            Changed(deskSharing);
            laptopSharing.Check();

            Assert.Equal(14, laptop.Current.Appearance.PanelFont.Size);
            Assert.Null(laptop.Current.Windows.For(WindowSettings.Main));
            Assert.NotNull(said);
            Assert.Equal(["appearance.panelFont.size"], said!.Merge.Paths);

            laptopSharing.Undo(said);
            Changed(laptopSharing);
            Assert.Equal(FontSetting.DefaultPanelSize, laptop.Current.Appearance.PanelFont.Size);
            deskSharing.Check();
            Assert.Equal(FontSetting.DefaultPanelSize, desk.Current.Appearance.PanelFont.Size);
        }
    }

    [AvaloniaFact]
    public void AskMeFirst_WaitsForTakeThem()
    {
        var (desk, deskSharing) = Pc("desk");
        var (laptop, laptopSharing) = Pc("laptop");
        using (desk)
        using (laptop)
        using (deskSharing)
        using (laptopSharing)
        {
            deskSharing.TurnOn(new SharingSettings { Folder = Folder }, null);
            laptopSharing.TurnOn(new SharingSettings { Folder = Folder, AskFirst = true }, SharedSettingsService.Peek(Folder, laptop.Current, new SharingSettings().Parts.ToHashSet())!.Value.Copy);
            var asked = 0;
            laptopSharing.Asking += (_, _) => asked++;

            desk.Update(s => Font(s, 15));
            Changed(deskSharing);
            laptopSharing.Check();
            laptopSharing.Check();
            Assert.Equal(1, asked);
            Assert.Equal(FontSetting.DefaultPanelSize, laptop.Current.Appearance.PanelFont.Size);

            laptop.Update(s => s with { Panels = s.Panels with { SizeUnits = SizeUnits.Binary } });
            Changed(laptopSharing);
            deskSharing.Check();
            Assert.Equal(SizeUnits.Binary, desk.Current.Panels.SizeUnits);
            Assert.Equal(15, desk.Current.Appearance.PanelFont.Size);

            laptopSharing.Check();
            Assert.NotNull(laptopSharing.TakePending());
            Assert.Equal(15, laptop.Current.Appearance.PanelFont.Size);
            Assert.Null(laptopSharing.Pending);
        }
    }

    [AvaloniaFact]
    public void TheNotice_SaysWhatCame_InWords()
    {
        using var services = AppServices.CreateIsolated();
        var main = new MainWindow(services);
        var before = new AppSettings { Appearance = AppearanceSettings.Fixed(ThemeCatalog.SalandualLightId) };
        var after = before with
        {
            Appearance = AppearanceSettings.Fixed("nord"),
            HotPaths = new HotPathSettings { Paths = [new HotPath(1, "Photos", @"E:\Photos"), new HotPath(2, "Music", @"E:\Music")] },
            Panels = before.Panels with { SizeUnits = SizeUnits.Binary },
        };
        var copy = SettingsSharing.Read(SettingsSharing.Write(SettingsSharing.Only(after, new SharingSettings().Parts.ToHashSet()), "LAPTOP", DateTime.UtcNow, "x"));
        var merge = SettingsSharing.Merge(before, null, copy, new SharingSettings().Parts.ToHashSet(), null);
        Assert.Equal("Settings from LAPTOP: Nord instead of Salandual Light, 2 hot paths, 1 setting in Panels.", main.TakenText(new SharingTaken("LAPTOP", merge, before)));
        Assert.Equal("LAPTOP changed 4 settings: Nord instead of Salandual Light, 2 hot paths, 1 setting in Panels.",
            main.AskingText(new SharingPending("LAPTOP", merge, copy, before)));
        main.Close();
    }

    [AvaloniaFact]
    public void TheDialog_OffersTheCloudsFound_AndSharesOnlyAFullPath()
    {
        var clouds = new[] { new CloudFolder("OneDrive", @"C:\Users\you\OneDrive"), new CloudFolder("Dropbox", @"D:\Dropbox") };
        var dialog = new ShareSettingsDialog(new SharingSettings(), clouds);
        dialog.Show();
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(@"C:\Users\you\OneDrive\Salandual", dialog.Folder.Text);
        Assert.Contains("accent", dialog.Chips[0].Chip.Classes);
        Assert.True(dialog.AtOnce.IsChecked);

        Assert.Equal([SettingsPart.History, SettingsPart.Programs], dialog.Ticks.Where(t => t.Box.IsChecked != true).SelectMany(t => t.Parts));
        Capture(dialog, "share-dialog.png");

        dialog.Chips[1].Chip.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Assert.Equal(@"D:\Dropbox\Salandual", dialog.Folder.Text);
        dialog.AskFirst.IsChecked = true;
        Assert.Equal((@"D:\Dropbox\Salandual", true), (dialog.Result!.Folder, dialog.Result.AskFirst));
        Assert.Equal(new SharingSettings().Parts.Order(), dialog.Result!.Parts.Order());

        dialog.Folder.Text = "relative";
        Dispatcher.UIThread.RunJobs();
        Assert.False(dialog.Primary.IsEnabled);
        dialog.Folder.Text = @"\\server\share\Salandual";
        foreach (var (box, _) in dialog.Ticks)
        {
            box.IsChecked = false;
        }

        Dispatcher.UIThread.RunJobs();
        Assert.False(dialog.Primary.IsEnabled);
        dialog.Close();
    }

    [AvaloniaFact]
    public void Advanced_SaysWhetherAndHowItIsShared()
    {
        using var services = AppServices.CreateIsolated();
        var w = new OptionsWindow(services) { Width = 1100, Height = 820 };
        w.Show();
        w.ShowCategory("advanced");
        Dispatcher.UIThread.RunJobs();
        var texts = w.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text).ToList();
        Assert.Contains("Share with your other PCs", texts);
        Assert.Contains("Share…", w.GetVisualDescendants().OfType<Button>().Select(b => b.Content as string));
        w.Close();

        services.Settings.Update(s => s with { Sharing = new SharingSettings { Folder = @"C:\Users\you\OneDrive\Salandual", AskFirst = true } });
        var on = new OptionsWindow(services) { Width = 1100, Height = 820 };
        on.Show();
        on.ShowCategory("advanced");
        Dispatcher.UIThread.RunJobs();
        texts = on.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text).ToList();
        Assert.Contains("Shared with your other PCs", texts);
        Assert.Contains(@"Through C:\Users\you\OneDrive\Salandual. Everything except typing history and programs; another PC's changes wait until you take them.", texts);
        Assert.Contains("Stop sharing", on.GetVisualDescendants().OfType<Button>().Select(b => b.Content as string));
        Capture(on, "share-options-on.png");
        services.Settings.Update(s => s with { Sharing = new SharingSettings() });
        on.Close();
    }
}

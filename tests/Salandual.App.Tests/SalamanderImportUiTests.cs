// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : tests/Salandual.App.Tests/SalamanderImportUiTests.cs

using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Salandual.App.Dialogs;
using Salandual.App.Options;
using Salandual.App.Wizard;
using Salandual.Core.Settings;
using Salandual.Core.Settings.Salamander;

namespace Salandual.App.Tests;

public sealed class SalamanderImportUiTests : IDisposable
{
    private static readonly SalamanderInstall _four = new("Altap Salamander 4.0", @"Software\Altap\Altap Salamander 4.0");

    private static readonly string _reg = """
        REGEDIT4

        [HKEY_CURRENT_USER\Software\Altap\Altap Salamander 4.0\Configuration]
        "Sort Detects Numbers"=dword:00000000
        "Use Recycle Bin"=dword:00000002
        "Use Recycle Bin For"="*.txt;*.doc"

        [HKEY_CURRENT_USER\Software\Altap\Altap Salamander 4.0\Configuration\Copy History]
        "1"="D:\\Made up"

        [HKEY_CURRENT_USER\Software\Altap\Altap Salamander 4.0\Hot Paths\1]
        "Name"="Downloads"
        "Path"="C:\\Downloads"

        [HKEY_CURRENT_USER\Software\Altap\Altap Salamander 4.0\User Menu\1]
        "Item Name"="Play"
        "Command"="C:\\Player\\player.exe"
        "Type"=dword:00000000

        [HKEY_CURRENT_USER\Software\Altap\Altap Salamander 4.0\Plugins Configuration\FTP\Bookmarks\1]
        "Name"="Example site"
        "Address"="ftp.example.com"
        "User"="someone"

        [HKEY_CURRENT_USER\Software\Altap\Altap Salamander 4.0\Plugins\1]
        "Name"="PictView"
        "DLL"="pictview\\pictview.spl"
        """;

    public void Dispose()
    {
        var shared = AppServices.CreateIsolated().DataDirectory;
        foreach (var kept in Directory.Exists(shared) ? Directory.GetFiles(shared, "settings.before-*.json") : [])
        {
            File.Delete(kept);
        }
    }

    private static AppServices WithSalamander()
    {
        var services = AppServices.CreateIsolated();
        ((MemorySalamanderSettings)services.Salamander).Installs.Add((_four, RegFile.Parse(_reg).Root));
        return services;
    }

    private static void Click(Control c)
    {
        c.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();
    }

    private static List<string> Texts(Visual root)
        => [.. root.GetLogicalDescendants().OfType<TextBlock>().Where(t => t.IsEffectivelyVisible).Select(t => t.Text ?? string.Concat(t.Inlines?.OfType<Avalonia.Controls.Documents.Run>().Select(r => r.Text) ?? []))];

    [AvaloniaFact]
    public void WhichSettings_OffersThoseOnThisPc_ThenAFile()
    {
        var dialog = SettingsTransferDialog.ForSalamanderSource([_four, new SalamanderInstall("Altap Salamander 3.08", @"Software\Altap\Altap Salamander 3.08")]);
        Assert.Equal("Import from Altap Salamander", dialog.Title);
        Assert.Equal([(object)_four, new SalamanderInstall("Altap Salamander 3.08", @"Software\Altap\Altap Salamander 3.08"), SettingsTransferDialog.RegFileChoice], dialog.Picks.Select(p => p.Value));
        Assert.Equal([true, false, false], dialog.Picks.Select(p => p.Button.IsChecked == true));
        Assert.Equal("Next", dialog.Primary.Content);
        dialog.Show();
        Dispatcher.UIThread.RunJobs();
        Assert.Contains("Altap Salamander 4.0 on this PC", Texts(dialog));
        Assert.Contains("Passwords are never read: FTP passwords and the Password Manager are skipped.", Texts(dialog));
        dialog.Close();

        var none = SettingsTransferDialog.ForSalamanderSource([]);
        Assert.Equal(SettingsTransferDialog.RegFileChoice, Assert.Single(none.Picks).Value);
        Assert.True(none.Picks[0].Button.IsChecked);
    }

    [AvaloniaFact]
    public void WhatItWouldChange_ProgramsHistoryAndTheRecycleBinRule_NotTicked_ThenTheNoticeWithUndo()
    {
        var services = WithSalamander();
        (string Text, Action? Undo) said = default;
        var files = new SettingsFiles(services, new Window(), (text, undo) => said = (text, undo));
        var import = files.ReadSalamander(_four)!;
        var dialog = SettingsTransferDialog.ForImport(import);
        Assert.Equal("Import from Altap Salamander", dialog.Title);
        var ticks = dialog.Ticks.ToDictionary(t => (SettingsPart)t.Value, t => t.Box.IsChecked == true);
        Assert.Equal(new Dictionary<SettingsPart, bool>
        {
            [SettingsPart.Panels] = true,
            [SettingsPart.HotPaths] = true,
            [SettingsPart.History] = false,
            [SettingsPart.Programs] = false,
            [SettingsPart.Unusual] = false,
        }, ticks);
        dialog.Show();
        Dispatcher.UIThread.RunJobs();
        var texts = Texts(dialog);
        Assert.Contains("Altap Salamander 4.0 on this PC. Tick what to take:", texts);
        Assert.Contains("Settings that would surprise you", texts);
        Assert.Contains("Left out", texts);
        Assert.Contains(texts, t => t.Contains("never read", StringComparison.Ordinal));
        dialog.Close();

        var parts = ticks.Where(t => t.Value).Select(t => t.Key).ToHashSet();
        files.Import(import, parts);
        Assert.False(services.Settings.Current.Panels.NumericSort);
        Assert.True(services.Settings.Current.Operations.RecycleByDefault);
        Assert.Equal("Example site", Assert.Single(services.Settings.Current.Servers.Bookmarks).Name);
        Assert.Equal(
            "Imported 3 changes from Altap Salamander 4.0 on this PC. Not taken: Typing history, Programs Salandual would start, Settings that would surprise you. The servers ask for their passwords the first time.",
            said.Text);
        Assert.NotEmpty(Directory.GetFiles(services.DataDirectory, "settings.before-salamander-import-*.json"));
        said.Undo!();
        Assert.True(services.Settings.Current.Panels.NumericSort);
        Assert.Empty(services.Settings.Current.Servers.Bookmarks);
    }

    [AvaloniaFact]
    public void AFileThatIsntSalamanders_IsSaid()
    {
        var dir = Directory.CreateTempSubdirectory("salandual-salimp-ui-").FullName;
        try
        {
            var file = Path.Combine(dir, "other.reg");
            File.WriteAllText(file, "REGEDIT4\r\n[HKEY_CURRENT_USER\\Software\\Other]\r\n");
            string? said = null;
            var files = new SettingsFiles(AppServices.CreateIsolated(), new Window(), (text, _) => said = text);
            Assert.Null(files.ReadSalamander(file));
            Assert.Equal("Couldn't import other.reg: The file has no settings of Altap Salamander or Open Salamander.", said);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [AvaloniaFact]
    public void Options_HasIt_UnderYourSettings_NotUnderLater()
    {
        var w = new OptionsWindow(AppServices.CreateIsolated());
        w.Show();
        Assert.Contains(w.Items, i => i.Category == "advanced" && i.Section == "Your settings" && i.Title == "Import from Altap Salamander" && i.Badge is null);
        Assert.Contains(w.Find("salamander reg"), i => i.Title == "Import from Altap Salamander");
        Assert.DoesNotContain(w.Items, i => i.Category == "general" && i.Title == "Import from Altap Salamander");
        w.Close();
    }

    [AvaloniaFact]
    public async Task FirstSteps_ShowsItsPage_OnlyWhenSalamandersSettingsAreFound()
    {
        var plain = new FirstStepsWindow(AppServices.CreateIsolated(), false);
        Assert.DoesNotContain(FirstStepsWindow.Step.Salamander, plain.Steps);

        var services = WithSalamander();
        var w = new FirstStepsWindow(services, false);
        w.Show();
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(FirstStepsWindow.Step.Salamander, w.Steps[1]);
        Click(w.NextButton);
        Assert.Equal(FirstStepsWindow.Step.Salamander, w.Current);
        var texts = Texts(w.PageContent!);
        Assert.Contains("Coming from Altap Salamander?", texts);
        Assert.Contains(texts, t => t.StartsWith("Altap Salamander 4.0's settings are on this PC.", StringComparison.Ordinal));

        var import = new SettingsFiles(services, w, (_, _) => { }).ReadSalamander(_four)!;
        IReadOnlySet<SettingsPart> parts = new HashSet<SettingsPart> { SettingsPart.HotPaths };
        SalamanderInstall? asked = null;
        w.PickSalamander = install =>
        {
            asked = install;
            return Task.FromResult<(SettingsImport, IReadOnlySet<SettingsPart>)?>((import, parts));
        };
        Click(w.PageContent!.GetLogicalDescendants().OfType<Button>().First(b => Equals(b.Tag, "salamander")));
        await Task.Yield();
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(_four, asked);
        Assert.NotNull(w.Imported);
        Click(w.NextButton);
        Assert.Equal(FirstStepsWindow.Step.Ready, w.Current);
        w.Close();
    }

    [AvaloniaFact]
    public void Pictures()
    {
        if (Environment.GetEnvironmentVariable("SALANDUAL_CAPTURE_DIR") is not { Length: > 0 } dir)
        {
            return;
        }

        var services = WithSalamander();
        var files = new SettingsFiles(services, new Window(), (_, _) => { });
        foreach (var (window, name) in new (Window, string)[]
        {
            (SettingsTransferDialog.ForSalamanderSource(services.Salamander.Find()), "salimp-source.png"),
            (SettingsTransferDialog.ForImport(files.ReadSalamander(_four)!), "salimp-preview.png"),
        })
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            Capture(window, dir, name);
            window.Close();
        }

        var w = new FirstStepsWindow(services, false);
        w.Show();
        w.Show(FirstStepsWindow.Step.Salamander);
        Dispatcher.UIThread.RunJobs();
        Capture(w, dir, "salimp-wizard.png");
        w.Show(FirstStepsWindow.Step.Welcome);
        Dispatcher.UIThread.RunJobs();
        Capture(w, dir, "salimp-welcome.png");
        w.Close();

        var options = new OptionsWindow(services) { Width = 1000, Height = 740 };
        options.Show();
        options.ShowCategory("advanced", options.Items.First(i => i.Title == "Import from Altap Salamander"));
        Dispatcher.UIThread.RunJobs();
        Capture(options, dir, "salimp-options.png");
        options.Close();
    }

    private static void Capture(Window w, string dir, string name)
    {
        Directory.CreateDirectory(dir);
        w.CaptureRenderedFrame()!.Save(Path.Combine(dir, name), Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
    }
}

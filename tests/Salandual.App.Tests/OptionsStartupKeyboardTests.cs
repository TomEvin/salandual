// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : tests/Salandual.App.Tests/OptionsStartupKeyboardTests.cs

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Salandual.App.Options;
using Salandual.Core;
using Salandual.Core.Commands;
using Salandual.Core.Settings;
using Salandual.Platform.Windows.Shell;

namespace Salandual.App.Tests;

public sealed class OptionsStartupKeyboardTests
{
    private static OptionsWindow Open(AppServices services, string category)
    {
        var w = new OptionsWindow(services) { Width = 1000, Height = 740 };
        w.Show();
        w.ShowCategory(category);
        Dispatcher.UIThread.RunJobs();
        return w;
    }

    private static IEnumerable<T> Find<T>(Control root) => root.GetLogicalDescendants().OfType<T>();

    private static void Click(Button b)
    {
        b.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();
    }

    private static Button ButtonOf(Control root, string text) => Find<Button>(root).First(b => b.Content as string == text);

    private static TextBox Named(Control root, string name) => Find<TextBox>(root).First(t => Avalonia.Automation.AutomationProperties.GetName(t) == name);

    [AvaloniaFact]
    public void StartupAndGeneral_IsFirst_WithTheMockupsRows()
    {
        var w = Open(AppServices.CreateIsolated(), "general");
        Assert.Equal(["One Salandual at a time", "Folders at start", "Start with Windows", "Language", "Keep running in the background", "Import from Altap Salamander", "Updates"],
            w.Items.Where(i => i.Category == "general").Select(i => i.Title));
        Assert.Equal(["Later", "Phase 3", "Phase 3"], w.Items.Where(i => i.Badge is not null).Select(i => i.Badge));
        Assert.Contains(w.Find("autostart"), i => i.Title == "Start with Windows");
        Assert.Contains(w.Find("magyar"), i => i.Title == "Language");
        w.Close();
    }

    [AvaloniaFact]
    public void FoldersAtStart_TheseFolders_StartFromThePanels_AndAreSaved()
    {
        var services = AppServices.CreateIsolated();
        var w = Open(services, "general");
        w.PanelFolders = () => (@"D:\Projects", @"C:\Users\you\Downloads");
        Assert.False(Named(w.Page, "Left").IsEffectivelyVisible);

        Click(Find<ToggleButton>(w.Page).First(b => b.Content as string == "These folders"));
        var general = services.Settings.Current.General;
        Assert.Equal((StartFolders.These, @"D:\Projects", @"C:\Users\you\Downloads"), (general.StartFolders, general.StartLeft, general.StartRight));
        var left = Named(w.Page, "Left");
        Assert.True(left.IsEffectivelyVisible);
        Assert.Equal(@"D:\Projects", left.Text);

        left.Text = @"E:\Work";
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(@"E:\Work", services.Settings.Current.General.StartLeft);

        Click(Find<ToggleButton>(w.Page).First(b => b.Content as string == "Your user folder"));
        Assert.Equal(StartFolders.Home, services.Settings.Current.General.StartFolders);
        Assert.False(Named(w.Page, "Left").IsEffectivelyVisible);
        Assert.Equal(@"E:\Work", services.Settings.Current.General.StartLeft);
        w.Close();
    }

    [AvaloniaFact]
    public void StartWithWindows_RegistersThisProgram_AndNamesAnotherCopy()
    {
        var services = AppServices.CreateIsolated();
        var start = (MemoryStartWithWindows)services.StartWithWindows;
        start.Registered = @"E:\Old place\salandual.exe";
        var w = Open(services, "general");
        Assert.False(w.StartWithWindowsToggle!.IsChecked);
        Assert.Equal(@"Another copy starts with Windows now: E:\Old place\salandual.exe. Turning this on starts this one instead.", w.StartWithWindowsNote!.Text);

        w.StartWithWindowsToggle.IsChecked = true;
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(services.ProgramPath, start.Registered);
        Assert.False(w.StartWithWindowsNote.IsVisible);

        w.StartWithWindowsToggle.IsChecked = false;
        Dispatcher.UIThread.RunJobs();
        Assert.Null(start.Registered);
        w.Close();
    }

    [AvaloniaFact]
    public void Keyboard_WhichCommandHasAKey_SaysTheCommand_OrFree()
    {
        var services = AppServices.CreateIsolated();
        var w = Open(services, "keyboard");
        var compare = services.Commands["Compare.Files"];
        w.KeyBox!.Focus();
        w.KeyPress(Key.C, RawInputModifiers.Control | RawInputModifiers.Shift, PhysicalKey.None, null);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal($"→ {compare.Title} ({CommandText.Category(compare.Category)})", w.KeyAnswer!.Text);
        Assert.Equal(["Ctrl", "Shift", "C"], Find<TextBlock>(w.KeyBox).Select(t => t.Text));
        Assert.Equal(220, w.KeyBox.Bounds.Width);
        var boxAt = w.KeyBox.TranslatePoint(default, w);
        w.KeyPress(Key.F5, RawInputModifiers.None, PhysicalKey.None, null);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(boxAt, w.KeyBox.TranslatePoint(default, w));
        Assert.True(w.KeyAnswer.Bounds.Top > 0);

        w.KeyPress(Key.F, RawInputModifiers.Control, PhysicalKey.None, null);
        Dispatcher.UIThread.RunJobs();
        Assert.False(w.SearchBox.IsFocused);

        var free = KeyChord.Parse("Ctrl+Alt+Shift+F12");
        Assert.Null(services.Commands.Resolve(free));
        Assert.Equal("→ free: no command has these keys", w.Answer(free));
        var later = services.Commands["UserMenu.Open"];
        Assert.Equal($"→ {later.Title} ({CommandText.Category(later.Category)}), not yet in Salandual", w.Answer(KeyChord.Parse("F9")));
        w.Close();
    }

    [AvaloniaFact]
    public void Keyboard_GroupsTheCommands_AndGreysTheOnesNotBuilt()
    {
        var services = AppServices.CreateIsolated();
        var w = Open(services, "keyboard");
        var texts = Find<TextBlock>(w.Page).Select(t => t.Text).ToList();
        var count = services.Commands.Commands.Count;
        Assert.Contains($"{count} commands", texts);
        Assert.Contains(CommandText.Category("Commands"), texts);
        Assert.Contains("not yet in Salandual · Phase 3", texts);
        Assert.True(MainWindow.IsBuilt("File.OccupiedSpace"));

        var filter = Find<TextBox>(w.Page).First(t => t.PlaceholderText?.StartsWith("Filter", StringComparison.Ordinal) == true);
        Assert.Contains("clearButton", filter.Classes);

        filter.Text = "F5";
        Dispatcher.UIThread.RunJobs();
        Assert.Contains(Find<TextBlock>(w.Page), t => t.Text?.EndsWith($" of {count} commands", StringComparison.Ordinal) == true);
        w.Close();
    }

    [AvaloniaFact]
    public void CheatSheet_Copies_AndOpensInTheViewer()
    {
        var services = AppServices.CreateIsolated();

        var bare = Open(services, "keyboard");
        Assert.False(ButtonOf(bare.Page, "Copy").IsEnabled);
        Assert.False(ButtonOf(bare.Page, "Open in the viewer").IsEnabled);
        bare.Close();

        var clipboard = new TextClipboard();
        string? viewed = null;
        var w = new OptionsWindow(services) { TextClipboard = clipboard, ViewFile = file => viewed = file };
        w.Show();
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("keyboard", w.CurrentCategory);
        Assert.True(ButtonOf(w.Page, "Copy").IsEnabled);
        Assert.True(ButtonOf(w.Page, "Open in the viewer").IsEnabled);

        Click(ButtonOf(w.Page, "Copy"));
        Assert.Contains(services.Commands["File.Copy"].Title, clipboard.Text);
        Assert.Contains("F5", clipboard.Text);
        Assert.Contains("not yet in Salandual", clipboard.Text);
        Assert.Equal("Copied.", w.CheatSheetNote!.Text);

        Click(ButtonOf(w.Page, "Open in the viewer"));
        Assert.Equal("Salandual keys.txt", Path.GetFileName(viewed));
        Assert.Equal(w.CheatSheet(), File.ReadAllText(viewed!));
        Assert.StartsWith(services.Temp.Own, viewed, StringComparison.OrdinalIgnoreCase);
        w.Close();
    }

    [AvaloniaFact]
    public void Search_WaitsForAPauseInTheTyping()
    {
        var w = Open(AppServices.CreateIsolated(), "panels");
        w.SearchBox.Text = "nord";
        Dispatcher.UIThread.RunJobs();
        Assert.True(w.IsSearching);
        Assert.Null(w.NavCount("themes"));
        w.SearchNow();
        Assert.Equal("2", w.NavCount("themes"));

        w.SearchBox.Focus();
        Dispatcher.UIThread.RunJobs();
        Assert.IsAssignableFrom<Button>(w.SearchBox.InnerRightContent);

        w.SearchBox.Text = "";
        Dispatcher.UIThread.RunJobs();
        Assert.IsType<TextBlock>(w.SearchBox.InnerRightContent);
        Assert.Equal("panels", w.CurrentCategory);

        Assert.Null(w.NavCount("themes"));
        w.Close();
    }

    [AvaloniaFact]
    public void Search_FindsThemes_AndHotPaths_WithACountPerPage()
    {
        var services = AppServices.CreateIsolated();
        services.Settings.Update(s => s with { HotPaths = new HotPathSettings { Paths = [new HotPath(3, "Timesheets", @"D:\Work\Timesheets"), new HotPath(12, "", @"D:\Old\times")] } });
        var w = Open(services, "general");

        w.SearchBox.Text = "nord";
        w.SearchNow();
        var nord = w.Find("nord").Where(i => i.Category == "themes").Select(i => i.Title).ToList();
        Assert.Equal(["Nord", "Nord Light"], nord);
        Assert.Equal("2", w.NavCount("themes"));
        Assert.Null(w.NavCount("panels"));
        Click(ButtonOf(w.Page, "Show"));
        Assert.Equal("themes", w.CurrentCategory);
        Assert.False(w.IsSearching);
        Assert.Contains(Find<TextBlock>(w.Page), t => t.Text == "Nord");
        Assert.Null(w.NavCount("themes"));

        w.SearchBox.Text = "times";
        w.SearchNow();
        var hot = w.Find("times").Where(i => i.Category == "hotpaths").ToList();
        Assert.Equal(["Timesheets", "times"], hot.Select(i => i.Title));
        Assert.Equal(@"D:\Work\Timesheets · your hot path Ctrl+3", hot[0].Description);
        Assert.Equal(@"D:\Old\times · your hot path 12 (Shift+F9)", hot[1].Description);
        Assert.Equal("2", w.NavCount("hotpaths"));

        w.ShowHotPath(3);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("hotpaths", w.CurrentCategory);
        Assert.Equal(2, w.Page.GetLogicalDescendants().OfType<HotPathTable>().Single().List.SelectedIndex);
        w.Close();
    }

    [AvaloniaFact]
    public void Pictures()
    {
        var services = AppServices.CreateIsolated();
        services.Settings.Update(s => s with { General = s.General with { StartFolders = StartFolders.These, StartLeft = @"D:\Projects", StartRight = @"D:\Downloads" } });
        var w = Open(services, "general");
        Capture(w, "general.png");
        w.ShowCategory("keyboard");
        w.KeyBox!.Focus();
        w.KeyPress(Key.C, RawInputModifiers.Control | RawInputModifiers.Shift, PhysicalKey.None, null);
        Dispatcher.UIThread.RunJobs();
        Capture(w, "keyboard.png");
        w.SearchBox.Text = "time";
        w.SearchNow();
        Capture(w, "search.png");
        w.Close();
    }

    private static void Capture(Window w, string name)
    {
        if (Environment.GetEnvironmentVariable("SALANDUAL_CAPTURE_DIR") is { Length: > 0 } dir)
        {
            Directory.CreateDirectory(dir);
            w.CaptureRenderedFrame()!.Save(Path.Combine(dir, name), Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
        }
    }

    private sealed class TextClipboard : IClipboardAccess
    {
        public string Text { get; private set; } = "";

        public uint SequenceNumber => 0;

        public bool SetFiles(IReadOnlyList<string> paths, bool cut) => false;

        public bool SetText(string text)
        {
            Text = text;
            return true;
        }

        public void Clear() => Text = "";

        public ClipboardContent? Read() => null;
    }
}

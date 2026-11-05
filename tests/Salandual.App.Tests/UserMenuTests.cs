// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : tests/Salandual.App.Tests/UserMenuTests.cs

using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Salandual.App.Resources;
using Salandual.Core.Commands;
using Salandual.Core.Operations;
using Salandual.Core.UserMenu;

namespace Salandual.App.Tests;

public sealed class UserMenuTests : IDisposable
{
    private const string Player = @"C:\Program Files\MPC-BE\mpc-be64.exe";
    private const string Ffmpeg = @"C:\Tools\ffmpeg.exe";
    private readonly string _dir = Directory.CreateTempSubdirectory("salandual-usermenu-").FullName;
    private readonly string _left;
    private readonly string _right;

    public UserMenuTests()
    {
        _left = Directory.CreateDirectory(Path.Join(_dir, "Lake 2024")).FullName;
        _right = Directory.CreateDirectory(Path.Join(_dir, "Edits")).FullName;
        Directory.CreateDirectory(Path.Join(_left, "Stills"));
        File.WriteAllText(Path.Join(_left, "day1-boat.mp4"), "1");
        File.WriteAllText(Path.Join(_left, "day2-swim.mp4"), "2");
        File.WriteAllText(Path.Join(_left, "cut-list.txt"), "c");
        File.WriteAllText(Path.Join(_right, "day2-swim.mp4"), "x");
    }

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private static readonly List<UserMenuItem> Menu =
    [
        new() { Name = "&Play in MPC-BE", Program = "mpc-be64.exe", Arguments = "\"$(FullName)\"", FileKind = UserMenuFileKind.Video, InContextMenu = UserMenuContextPlace.Top },
        new() { Name = "Compare in WinMerge", Program = "mpc-be64.exe", Arguments = "\"$(FileToCompareLeft)\" \"$(FileToCompareRight)\"", Count = UserMenuCount.Two },
        new() { Kind = UserMenuItemKind.Separator },
        new() { Name = "SHA-256 of the files", Kind = UserMenuItemKind.Script, Runs = UserMenuRuns.Once, Script = "Get-FileHash -LiteralPath $Files" },
        new()
        {
            Name = "Video tools", Kind = UserMenuItemKind.Submenu,
            Items = [new() { Name = "Remux to MKV", Program = "ffmpeg.exe", Arguments = "-i \"$(FullName)\"", FileKind = UserMenuFileKind.Video, KeepWindowOpen = false }],
        },
    ];

    private async Task<MainWindow> Open(List<UserMenuItem>? items = null, UserMenuUnfit unfit = UserMenuUnfit.Hidden)
    {
        var w = new MainWindow { Width = 1100, Height = 700 };
        w.Services.Settings.Update(s => s with { UserMenu = new UserMenuSettings { Items = items ?? Menu, Unfit = unfit } });
        var starter = Starter(w);
        starter.Programs["mpc-be64.exe"] = Player;
        starter.Programs["ffmpeg.exe"] = Ffmpeg;
        starter.Programs["pwsh.exe"] = @"C:\Program Files\PowerShell\7\pwsh.exe";
        w.Show();
        await w.LeftPanel.Panel.NavigateAsync(_left);
        await w.RightPanel.Panel.NavigateAsync(_right);
        w.LeftPanel.View.Focus();
        Dispatcher.UIThread.RunJobs();
        return w;
    }

    private static MemoryProgramStarter Starter(MainWindow w) => (MemoryProgramStarter)w.Services.Starter;

    private static int Row(MainWindow w, string name) => w.LeftPanel.Panel.Items.ToList().FindIndex(e => e.Name == name);

    private static void Focus(MainWindow w, string name) => w.LeftPanel.Panel.SetFocus(Row(w, name));

    private static void Press(TopLevel w, Key key, RawInputModifiers mods = RawInputModifiers.None)
    {
        w.KeyPress(key, mods, PhysicalKey.None, null);
        w.KeyRelease(key, mods, PhysicalKey.None, null);
        Dispatcher.UIThread.RunJobs();
    }

    private static async Task Until(Func<bool> condition, int tries = 500)
    {
        for (var i = 0; i < tries && !condition(); i++)
        {
            await Task.Delay(10);
            Dispatcher.UIThread.RunJobs();
        }

        Assert.True(condition());
    }

    private static string Text(object? header) => header switch
    {
        string s => s.Replace("__", "\u0001", StringComparison.Ordinal).Replace("_", "", StringComparison.Ordinal).Replace('\u0001', '_'),
        TextBlock t => t.Text ?? "",
        _ => "",
    };

    private static List<string> Lines(ItemCollection items) => [.. items.Select(i => i switch
    {
        Separator => "—",
        MenuItem { Header: TextBlock head } => head.Text ?? "",
        MenuItem m => Text(m.Header) + (m.IsEnabled ? "" : " (greyed)"),
        _ => "?",
    })];

    private static MenuItem Item(ItemCollection items, string text) => items.OfType<MenuItem>().First(m => Text(m.Header) == text);

    private static void Click(MenuItem item)
    {
        item.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        Dispatcher.UIThread.RunJobs();
    }

    [AvaloniaFact]
    public async Task F9_WithNoItems_SaysSoAndOffersToMakeOne()
    {
        var w = await Open([]);
        Press(w, Key.F9);
        Assert.Null(w.UserMenuPopup);
        Assert.Equal("The user menu is empty: its items start your own programs with the selected files.", w.NoticeMessage);
        Assert.Equal("Make an item", (string?)w.NoticeButtons[0].Content);
        w.CloseNow();
    }

    [AvaloniaFact]
    public async Task F9_ShowsOnlyWhatFits_ThenEditUserMenu()
    {
        var w = await Open();
        Focus(w, "day2-swim.mp4");
        Press(w, Key.F9);
        var menu = Assert.IsType<ContextMenu>(w.UserMenuPopup);
        Assert.Equal(["User menu · for day2-swim.mp4", "—", "Play in MPC-BE", "—", "SHA-256 of the files", "Video tools", "—", "Edit user menu…"], Lines(menu.Items));
        Assert.Equal(["Remux to MKV"], Lines(Item(menu.Items, "Video tools").Items));
        menu.Close();

        Focus(w, "cut-list.txt");
        Press(w, Key.F9);
        Assert.Equal(["User menu · for cut-list.txt", "—", "SHA-256 of the files", "—", "Edit user menu…"], Lines(w.UserMenuPopup!.Items));
        w.UserMenuPopup.Close();
        w.CloseNow();
    }

    [AvaloniaFact]
    public async Task Greyed_ShowsWhyNot()
    {
        var w = await Open(unfit: UserMenuUnfit.Greyed);
        Focus(w, "cut-list.txt");
        Press(w, Key.F9);
        var items = w.UserMenuPopup!.Items;
        Assert.Equal(["User menu · for cut-list.txt", "—", "Play in MPC-BE (greyed)", "Compare in WinMerge (greyed)", "—", "SHA-256 of the files", "Video tools", "—", "Edit user menu…"], Lines(items));
        Assert.Equal("For video only.", ToolTip.GetTip(Item(items, "Play in MPC-BE")));
        Assert.Equal("For exactly two items.", ToolTip.GetTip(Item(items, "Compare in WinMerge")));
        w.UserMenuPopup.Close();
        w.CloseNow();
    }

    [AvaloniaFact]
    public async Task AnItem_StartsItsProgram_WithTheFileAsOneArgument()
    {
        var w = await Open();
        Focus(w, "day2-swim.mp4");
        Press(w, Key.F9);
        Click(Item(w.UserMenuPopup!.Items, "Play in MPC-BE"));
        var started = Assert.Single(Starter(w).Started);
        Assert.Equal(Player, started.Program);
        Assert.Equal([Path.Join(_left, "day2-swim.mp4")], started.Arguments);
        Assert.Equal(_left + "\\", started.Folder);
        Assert.Empty(w.Operations.Jobs);
        w.CloseNow();
    }

    [AvaloniaFact]
    public async Task SeveralFiles_OneAfterAnother_AsAJob()
    {
        var w = await Open();
        var panel = w.LeftPanel.Panel;
        panel.Toggle(Row(w, "day1-boat.mp4"));
        panel.Toggle(Row(w, "day2-swim.mp4"));
        await w.RunUserMenuItemAsync(w.LeftPanel, Menu[4].Items[0]);
        var job = Assert.Single(w.Operations.Jobs);
        Assert.Equal(OperationKind.Run, job.Kind);
        Assert.Equal("Remux to MKV", job.Title);
        await job.Completion;
        Assert.Equal(JobState.Done, job.State);
        Assert.Equal([["-i", Path.Join(_left, "day1-boat.mp4")], ["-i", Path.Join(_left, "day2-swim.mp4")]], Starter(w).Started.Select(s => s.Arguments.ToList()));
        w.CloseNow();
    }

    [AvaloniaFact]
    public async Task AScript_GetsTheSelectedFiles_InAListFile()
    {
        var w = await Open();
        var panel = w.LeftPanel.Panel;
        panel.Toggle(Row(w, "day1-boat.mp4"));
        panel.Toggle(Row(w, "cut-list.txt"));
        await w.RunUserMenuItemAsync(w.LeftPanel, Menu[3]);
        var started = Assert.Single(Starter(w).Started);
        Assert.EndsWith("pwsh.exe", started.Program, StringComparison.Ordinal);
        var list = started.Environment![UserMenuRunner.ListVariable];
        Assert.StartsWith(w.Services.Temp.Root, list, StringComparison.OrdinalIgnoreCase);
        Assert.Equal([Path.Join(_left, "cut-list.txt"), Path.Join(_left, "day1-boat.mp4")], File.ReadAllLines(list).Order());
        w.CloseNow();
    }

    [AvaloniaFact]
    public async Task AskFirst_ShowsTheArguments_ChangedForOnce()
    {
        List<UserMenuItem> items = [Menu[0] with { AskFirst = true }];
        var w = await Open(items);
        Focus(w, "day2-swim.mp4");
        var running = w.RunUserMenuItemAsync(w.LeftPanel, w.Services.Settings.Current.UserMenu.Items[0]);
        await Until(() => w.OpenUserMenuAsk is not null);
        var ask = w.OpenUserMenuAsk!;
        Assert.Equal([Player, Path.Join(_left, "day2-swim.mp4"), "Started in " + _left + "\\"], ask.ShownLines);
        ask.Arguments!.Text = "/play \"$(FullName)\"";
        Dispatcher.UIThread.RunJobs();
        Assert.Equal([Player, "/play", Path.Join(_left, "day2-swim.mp4"), "Started in " + _left + "\\"], ask.ShownLines);
        ask.DontAskBox.IsChecked = true;
        ask.Answer();
        await running;
        Assert.Equal(["/play", Path.Join(_left, "day2-swim.mp4")], Assert.Single(Starter(w).Started).Arguments);
        Assert.False(w.Services.Settings.Current.UserMenu.Items[0].AskFirst);
        Assert.Equal("\"$(FullName)\"", w.Services.Settings.Current.UserMenu.Items[0].Arguments);
        w.CloseNow();
    }

    [AvaloniaFact]
    public async Task AskFirst_Cancel_StartsNothing()
    {
        var w = await Open([Menu[0] with { AskFirst = true }]);
        Focus(w, "day2-swim.mp4");
        w.UserMenuAskAnswer = (_, _) => null;
        await w.RunUserMenuItemAsync(w.LeftPanel, w.Services.Settings.Current.UserMenu.Items[0]);
        Assert.Empty(Starter(w).Started);
        w.CloseNow();
    }

    [AvaloniaFact]
    public async Task WhatCantRun_IsSaid()
    {
        var w = await Open([new UserMenuItem { Name = "Gone", Program = "nothere.exe", Arguments = "$(FullName)" }]);
        Focus(w, "day2-swim.mp4");
        await w.RunUserMenuItemAsync(w.LeftPanel, w.Services.Settings.Current.UserMenu.Items[0]);
        Assert.Equal("Gone can't run: nothere.exe wasn't found. Give its full path in Options › User menu.", w.NoticeMessage);
        Assert.Equal("Options", (string?)w.NoticeButtons[0].Content);
        Assert.Empty(Starter(w).Started);
        w.CloseNow();
    }

    [AvaloniaFact]
    public async Task TwoToCompare_FromBothPanels()
    {
        var w = await Open([new UserMenuItem { Name = "Compare", Program = "mpc-be64.exe", Arguments = "\"$(FileToCompareLeft)\" \"$(FileToCompareRight)\"" }]);
        Focus(w, "day2-swim.mp4");
        w.RightPanel.Panel.SetFocus(w.RightPanel.Panel.Items.ToList().FindIndex(e => e.Name == "day2-swim.mp4"));
        await w.RunUserMenuItemAsync(w.LeftPanel, w.Services.Settings.Current.UserMenu.Items[0]);
        Assert.Equal([Path.Join(_left, "day2-swim.mp4"), Path.Join(_right, "day2-swim.mp4")], Assert.Single(Starter(w).Started).Arguments);
        w.CloseNow();
    }

    [AvaloniaFact]
    public async Task OwnKey_RunsTheItem_WhereNoCommandHasIt()
    {
        var w = await Open([Menu[0] with { Key = "Ctrl+Alt+P" }]);
        Focus(w, "day2-swim.mp4");
        Press(w, Key.P, RawInputModifiers.Control | RawInputModifiers.Alt);
        Assert.Single(Starter(w).Started);
        w.CloseNow();
    }

    [AvaloniaFact]
    public async Task RightClick_TopItems_AndUserMenuSubmenu()
    {
        var w = await Open();
        Focus(w, "day2-swim.mp4");
        w.ShowMenuFromKeyboard(w.LeftPanel, background: false);
        var menu = w.QuickMenu!;
        var lines = Lines(menu.Items);
        Assert.Equal(lines.IndexOf("Open with…") + 1, lines.IndexOf("Play in MPC-BE"));
        var sub = Item(menu.Items, "User menu");
        Assert.Equal(["SHA-256 of the files", "Video tools"], Lines(sub.Items));
        Assert.DoesNotContain("Play in MPC-BE", Lines(sub.Items));
        Click(Item(menu.Items, "Play in MPC-BE"));
        Assert.Single(Starter(w).Started);
        menu.Close();
        w.CloseNow();
    }

    [AvaloniaFact]
    public async Task Options_TheEditor_MakesMovesAndPreviews()
    {
        var w = await Open([]);
        Focus(w, "day2-swim.mp4");
        _ = w.OpenOptionsAsync(MainWindow.UserMenuPage);
        await Until(() => w.OpenOptions?.UserMenuEditor is not null);
        var editor = w.OpenOptions!.UserMenuEditor!;
        UserMenuSettings Menu() => w.Services.Settings.Current.UserMenu;

        editor.NewItem.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.Equal("New item", Assert.Single(Menu().Items).Name);
        var program = Assert.IsType<DockPanel>(editor.Fields["Program"]).Children.OfType<TextBox>().Single();
        program.Text = "mpc-be64.exe";
        program.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter });
        Assert.Equal("mpc-be64.exe", Menu().Items[0].Program);
        Assert.Contains(Player, editor.PreviewText, StringComparison.Ordinal);
        Assert.Contains("1   " + Path.Join(_left, "day2-swim.mp4"), editor.PreviewText, StringComparison.Ordinal);

        editor.NewSubmenu.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        editor.Select([0]);
        editor.Move(+1);
        Assert.Equal(["Submenu"], Menu().Items.Select(i => i.Name));
        Assert.Equal(["New item"], Menu().Items[0].Items.Select(i => i.Name));
        editor.Move(-1);
        Assert.Equal(["New item", "Submenu"], Menu().Items.Select(i => i.Name));
        Assert.Empty(Menu().Items[1].Items);

        Assert.Equal("Ctrl+C is the key of Copy to clipboard: choose another.", editor.WhyNotKey(KeyChord.Parse("Ctrl+C")));
        Assert.Equal("A key of an item needs Ctrl or Alt (or is an F key): letters start quick search.", editor.WhyNotKey(KeyChord.Parse("P")));
        Assert.Null(editor.WhyNotKey(KeyChord.Parse("Ctrl+Alt+P")));

        editor.Select([0]);
        editor.StartRename();
        var box = Assert.IsType<TextBox>(editor.RenameBox);
        box.Text = "&Play";
        box.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter });
        Assert.Null(editor.RenameBox);
        Assert.Equal("&Play", Menu().Items[0].Name);
        editor.StartRename();
        editor.RenameBox!.Text = "Other";
        editor.RenameBox.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Escape });
        Assert.Equal("&Play", Menu().Items[0].Name);

        Button[] Tools(int at) => [.. ((ListBoxItem)editor.Tree.Items[at]!).GetLogicalDescendants().OfType<Button>()];
        Assert.Equal([AppStrings.Opt_UserMenu_RenameTip, AppStrings.Opt_UserMenu_UpTip, AppStrings.Opt_UserMenu_DownTip, AppStrings.Opt_UserMenu_RemoveTip],
            Tools(0).Select(b => ToolTip.GetTip(b) as string));
        Assert.False(Tools(0)[1].IsEnabled);
        Assert.True(Tools(0)[2].IsEnabled);
        Assert.False(Tools(1)[2].IsEnabled);
        Tools(0)[2].RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.Equal(["&Play"], Menu().Items[0].Items.Select(i => i.Name));
        editor.Select([0, 0]);
        editor.Move(-1);
        Assert.Equal(["&Play", "Submenu"], Menu().Items.Select(i => i.Name));

        Assert.Equal(AppStrings.Opt_UserMenu_NewSeparator, ToolTip.GetTip(editor.NewSeparator));
        Assert.IsNotType<string>(editor.NewItem.Content);
        Assert.Equal(1, Grid.GetColumn(editor.Fields[AppStrings.Opt_UserMenu_Key].GetLogicalAncestors().OfType<Control>().First(c => c.Parent == editor)));
        Assert.Equal(1, Grid.GetRow(editor.Form));
        Assert.Equal(2, Grid.GetColumnSpan(editor.Form));

        editor.Select([0]);
        Assert.True(editor.IconButton.IsEnabled);
        var iconMenu = Assert.IsType<MenuFlyout>(editor.IconButton.Flyout);
        Assert.Equal([AppStrings.Opt_UserMenu_IconOwn, AppStrings.Opt_UserMenu_ChooseIcon, AppStrings.Opt_UserMenu_IconReset],
            iconMenu.Items.OfType<MenuItem>().Select(i => i.Header as string));
        Assert.False(editor.Fields.ContainsKey(AppStrings.Opt_UserMenu_Icon));
        editor.Select([1]);
        Assert.False(editor.IconButton.IsEnabled);

        editor.Select([0]);
        var variables = Assert.IsType<Button>(editor.Fields["Program variables"]);
        Assert.Equal(AppStrings.Opt_UserMenu_InsertVariable, ToolTip.GetTip(variables));
        var lines = ((MenuFlyout)variables.Flyout!).Items.Cast<object>().ToList();
        Assert.DoesNotContain(lines.Zip(lines.Skip(1)), p => p.First is Separator && p.Second is Separator);
        Assert.Contains(lines.OfType<MenuItem>(), i => i.Tag as string == "$(WinDir)" && i.Header is DockPanel d && d.Children.OfType<TextBlock>().Any(t => t.Text == "Windows folder"));
        Assert.Equal(["$(FullName)", "$(WinDir)", "$(SysDir)", "$(SalDir)", "$[]"], Options.UserMenuEditor.VariablesOf("Program"));
        Assert.Equal(["$(FullPath)", "$(FullPathLeft)", "$(FullPathRight)", "$(FullPathInactive)", "$(WinDir)", "$(SysDir)", "$(SalDir)", "$[]"], Options.UserMenuEditor.VariablesOf("StartIn"));
        Assert.Equal(UserMenuVariables.Groups.SelectMany(g => g).Select(v => v == "$[NAME]" ? "$[]" : v).Order(), Options.UserMenuEditor.VariablesOf("Arguments").Order());

        Assert.IsType<StackPanel>(editor.Tree.ItemsPanelRoot);

        editor.Select([1]);
        Tools(1)[3].RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.Equal(["&Play"], Menu().Items.Select(i => i.Name));
        w.OpenOptions.Close();
        w.CloseNow();
    }
}

// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : tests/Salandual.App.Tests/CommandLineTests.cs

using System.Text;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Salandual.App.Resources;
using Salandual.Core.CommandLine;

namespace Salandual.App.Tests;

public sealed class CommandLineTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("salandual-cmdline-").FullName;
    private readonly string _left;
    private readonly string _right;

    public CommandLineTests()
    {
        _left = Directory.CreateDirectory(Path.Join(_dir, "Lake 2024")).FullName;
        _right = Directory.CreateDirectory(Path.Join(_dir, "Edits")).FullName;
        Directory.CreateDirectory(Path.Join(_left, "Stills"));
        File.WriteAllText(Path.Join(_left, "day2 swim.mp4"), "2");
        File.WriteAllText(Path.Join(_left, "100%.txt"), "p");
        File.WriteAllText(Path.Join(_left, "notes.txt"), "n");
    }

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private async Task<MainWindow> Open(Func<CommandLineSettings, CommandLineSettings>? settings = null, bool shown = true)
    {
        var w = new MainWindow { Width = 1100, Height = 700 };
        w.Services.Settings.Update(s => s with
        {
            CommandLine = settings?.Invoke(new CommandLineSettings()) ?? new CommandLineSettings(),
            Layout = s.Layout with { Bars = s.Layout.Bars with { CommandLine = shown } },
            History = s.History with { Commands = [] },
        });
        w.Show();
        await w.LeftPanel.Panel.NavigateAsync(_left);
        await w.RightPanel.Panel.NavigateAsync(_right);
        w.LeftPanel.View.Focus();
        Dispatcher.UIThread.RunJobs();
        return w;
    }

    private static MemoryCommandShells Shells(MainWindow w) => (MemoryCommandShells)w.Services.Shells;

    private static void Press(TopLevel w, Key key, RawInputModifiers mods = RawInputModifiers.None)
    {
        w.KeyPress(key, mods, PhysicalKey.None, null);
        w.KeyRelease(key, mods, PhysicalKey.None, null);
        Dispatcher.UIThread.RunJobs();
    }

    private static void Focus(MainWindow w, string name) => w.LeftPanel.Panel.SetFocus(w.LeftPanel.Panel.Items.ToList().FindIndex(e => e.Name == name));

    private static string Decode(ShellStart start)
    {
        var at = start.Arguments.ToList().IndexOf("-EncodedCommand");
        return Encoding.Unicode.GetString(Convert.FromBase64String(start.Arguments[at + 1]));
    }

    private static TextBox Box(MainWindow w) => w.CommandLineBox.Box;

    private static void Toggle(MainWindow w)
    {
        w.Run("CommandLine.Toggle", Core.Commands.PanelTarget.Active);
        Dispatcher.UIThread.RunJobs();
    }

    [AvaloniaFact]
    public async Task TheCommandLine_ShowsThePanelsFolderAndTheShell_AndTheCommandGoesToItAndBack()
    {
        var w = await Open();
        Assert.True(w.CommandLineBox.IsVisible);
        Assert.Equal(_left + ">", w.CommandLineBox.Prompt);
        Assert.Equal(AppStrings.Shell_PowerShell7, w.CommandLineBox.ShellButton.Content);

        Toggle(w);
        Assert.True(Box(w).IsKeyboardFocusWithin);
        Toggle(w);
        Assert.True(w.LeftPanel.View.IsFocused);

        Press(w, Key.Tab);
        Assert.Equal(_right + ">", w.CommandLineBox.Prompt);
        w.CloseNow();
    }

    [AvaloniaFact]
    public async Task Enter_RunsWhatYouTypedInTheShell_InThePanelsFolder_AndRemembersIt()
    {
        var w = await Open();
        Toggle(w);
        w.KeyTextInput("ffprobe -hide_banner 'day2 swim.mp4' | sort");
        Press(w, Key.Enter);

        var start = Assert.Single(Shells(w).Started);
        Assert.Equal(Shells(w).Found.PowerShell7, start.Program);
        Assert.Equal(_left, start.Folder);
        Assert.Contains("-NoExit", start.Arguments);
        Assert.Equal("ffprobe -hide_banner 'day2 swim.mp4' | sort", Decode(start));
        Assert.Equal("", Box(w).Text);
        Assert.Equal(new CommandHistoryEntry("ffprobe -hide_banner 'day2 swim.mp4' | sort", _left), Assert.Single(w.Services.Settings.Current.History.Commands));

        w.KeyTextInput("dir");
        Press(w, Key.Enter, RawInputModifiers.Alt);
        Assert.DoesNotContain("-NoExit", Shells(w).Started[^1].Arguments);

        Focus(w, "Stills");
        Press(w, Key.Enter);
        await Until(() => w.LeftPanel.Panel.Path == Path.Join(_left, "Stills"));
        Assert.Equal(2, Shells(w).Started.Count);
        w.CloseNow();
    }

    [AvaloniaFact]
    public async Task CommandPrompt_GetsTheTextAsTyped()
    {
        var w = await Open(c => c with { RunIn = CommandLineShell.CommandPrompt });
        Assert.Equal(AppStrings.Shell_CommandPrompt, w.CommandLineBox.ShellButton.Content);
        Toggle(w);
        w.KeyTextInput("dir /b \"day2 swim.mp4\" & echo %PATH%");
        Press(w, Key.Enter);
        var start = Assert.Single(Shells(w).Started);
        Assert.Equal(Shells(w).Found.CommandPrompt, start.Program);
        Assert.Equal("/s /k \"dir /b \"day2 swim.mp4\" & echo %PATH%\"", start.RawArguments);
        w.CloseNow();
    }

    [AvaloniaFact]
    public async Task Cd_MovesThePanel_AndStartsNothing()
    {
        var w = await Open();
        Toggle(w);
        w.KeyTextInput("cd Stills");
        Press(w, Key.Enter);
        await Until(() => w.LeftPanel.Panel.Path == Path.Join(_left, "Stills"));
        Assert.Equal(Path.Join(_left, "Stills") + ">", w.CommandLineBox.Prompt);

        w.KeyTextInput("cd ..");
        Press(w, Key.Enter);
        await Until(() => w.LeftPanel.Panel.Path == _left);
        Assert.Empty(Shells(w).Started);
        Assert.Equal(["cd ..", "cd Stills"], w.Services.Settings.Current.History.Commands.Select(c => c.Text));
        w.CloseNow();
    }

    [AvaloniaFact]
    public async Task CtrlEnterAndCtrlSpace_PutInNamesQuotedForTheShell_WhileThePanelKeepsTheKeys()
    {
        var w = await Open();
        Focus(w, "day2 swim.mp4");
        Press(w, Key.Enter, RawInputModifiers.Control);
        Assert.Equal("'day2 swim.mp4' ", Box(w).Text);
        Assert.True(w.LeftPanel.View.IsFocused);

        Press(w, Key.Space, RawInputModifiers.Control);
        Assert.Equal("'day2 swim.mp4' '" + _left + "' ", Box(w).Text);
        Press(w, Key.OemCloseBrackets, RawInputModifiers.Control);
        Assert.EndsWith(" " + _right + " ", Box(w).Text, StringComparison.Ordinal);
        Assert.Null(w.CommandLineBox.Warning);
        w.CloseNow();
    }

    [AvaloniaFact]
    public async Task ANameCommandPromptWouldStillRead_MarksTheLineRed_UntilItIsChanged()
    {
        var w = await Open(c => c with { RunIn = CommandLineShell.CommandPrompt });
        Focus(w, "100%.txt");
        Press(w, Key.Enter, RawInputModifiers.Control);
        Assert.Equal("100%.txt ", Box(w).Text);
        Assert.Equal(AppStrings.CommandLine_RiskyCmd, w.CommandLineBox.Warning);
        Assert.Contains("risky", Box(w).Classes);

        Box(w).Text = "type 100.txt ";
        Dispatcher.UIThread.RunJobs();
        Assert.Null(w.CommandLineBox.Warning);
        Assert.DoesNotContain("risky", Box(w).Classes);
        w.CloseNow();
    }

    [AvaloniaFact]
    public async Task CtrlUp_GoesThroughTheEarlierCommands_DelForgetsOne_EscGoesBackToWhatYouTyped()
    {
        var w = await Open();
        w.Services.Settings.Update(s => s with { History = s.History with { Commands = [new("git status", _left), new("winget upgrade --all", @"C:\Users\me")] } });
        Toggle(w);
        w.KeyTextInput("half");

        Press(w, Key.Up, RawInputModifiers.Control);
        Assert.True(w.CommandLineBox.IsHistoryOpen);
        Assert.Equal("git status", Box(w).Text);
        Press(w, Key.Up);
        Assert.Equal("winget upgrade --all", Box(w).Text);
        Assert.Equal(1, w.CommandLineBox.HistorySelected);

        Press(w, Key.Delete);
        Assert.Equal(["git status"], w.Services.Settings.Current.History.Commands.Select(c => c.Text));
        Assert.Equal("git status", Box(w).Text);

        Press(w, Key.Escape);
        Assert.False(w.CommandLineBox.IsHistoryOpen);
        Assert.Equal("half", Box(w).Text);

        Press(w, Key.Down, RawInputModifiers.Alt);
        Assert.True(w.CommandLineBox.IsHistoryOpen);
        Press(w, Key.Enter);
        Assert.Equal("git status", Decode(Assert.Single(Shells(w).Started)));
        w.CloseNow();
    }

    [AvaloniaFact]
    public async Task KeysThatDontEditTheLine_WorkAsInThePanel_EscClearsAndGoesBack()
    {
        var w = await Open();
        w.LeftPanel.Panel.SetFocus(0);
        Toggle(w);
        w.KeyTextInput("abc");
        Press(w, Key.Down);
        Assert.Equal(1, w.LeftPanel.Panel.Focus);
        Assert.True(Box(w).IsKeyboardFocusWithin);
        Press(w, Key.Left);
        Assert.Equal(2, Box(w).CaretIndex);

        Press(w, Key.Escape);
        Assert.Equal("", Box(w).Text);
        Assert.True(w.LeftPanel.View.IsFocused);
        w.CloseNow();
    }

    [AvaloniaFact]
    public async Task Hidden_TheCommandStillBringsIt_WhileItIsUsed()
    {
        var w = await Open(shown: false);
        Assert.False(w.CommandLineBox.IsVisible);
        Toggle(w);
        Assert.True(w.CommandLineBox.IsVisible);
        Press(w, Key.Escape);
        Assert.False(w.CommandLineBox.IsVisible);

        Focus(w, "notes.txt");
        Press(w, Key.Enter, RawInputModifiers.Control);
        Assert.True(w.CommandLineBox.IsVisible);
        Toggle(w);
        Press(w, Key.Home);
        w.KeyTextInput("code ");
        Press(w, Key.Enter);
        Assert.Equal("code notes.txt", Decode(Assert.Single(Shells(w).Started)));
        Assert.False(w.CommandLineBox.IsVisible);
        Assert.True(w.LeftPanel.View.IsFocused);
        w.CloseNow();
    }

    [AvaloniaFact]
    public async Task LettersInAPanel_GoToTheCommandLine_WhenOptionsSendsThem()
    {
        var w = await Open(c => c with { Typing = PanelTyping.CommandLine });
        QuickSearchTypingTests.Type(w, "dir");
        Assert.Null(w.LeftPanel.Panel.QuickSearchText);
        Assert.Equal("dir", Box(w).Text);
        Assert.True(w.LeftPanel.View.IsFocused);

        w.KeyPress(Key.Space, RawInputModifiers.None, PhysicalKey.None, " ");
        w.KeyRelease(Key.Space, RawInputModifiers.None, PhysicalKey.None, " ");
        QuickSearchTypingTests.Type(w, "x");
        Press(w, Key.Back);
        Assert.Equal("dir ", Box(w).Text);
        Press(w, Key.Enter);
        Assert.Equal("dir", Decode(Assert.Single(Shells(w).Started)));
        w.CloseNow();
    }

    [AvaloniaFact]
    public async Task NumSlash_OpensTheShellInThePanelsFolder_ShiftNumSlashAsAdministrator()
    {
        var w = await Open();
        Press(w, Key.Divide);
        var start = Assert.Single(Shells(w).Started);
        Assert.Equal(Shells(w).Found.Terminal, start.Program);
        Assert.Equal(_left, start.Folder);
        Assert.Equal(["-d", "."], start.Arguments);
        Assert.False(start.Elevated);

        Press(w, Key.Divide, RawInputModifiers.Shift);
        Assert.True(Shells(w).Started[^1].Elevated);
        Assert.Equal(["-d", _left], Shells(w).Started[^1].Arguments);
        w.CloseNow();
    }

    [AvaloniaFact]
    public async Task OptionsPage_OffersOnlyWhatIsOnThisPC()
    {
        var w = await Open();
        Shells(w).Found = Shells(w).Found with { Terminal = null };
        var options = new Options.OptionsWindow(w.Services);
        options.Show();
        options.ShowCategory(MainWindow.CommandLinePage);
        Dispatcher.UIThread.RunJobs();
        var combos = options.GetVisualDescendants().OfType<ComboBox>().ToList();
        var shell = combos.First(c => Avalonia.Automation.AutomationProperties.GetName(c) == AppStrings.Opt_ShellOpens);
        var labels = shell.Items.Cast<ComboBoxItem>().Select(i => i.Content as string).ToList();
        Assert.Equal(Core.Resources.Loc.Format(AppStrings.Opt_ShellFound, AppStrings.Shell_PowerShell7), labels[0]);
        Assert.DoesNotContain(AppStrings.Shell_Terminal, labels);
        Assert.Contains(AppStrings.Opt_ShellChoose, labels);
        Assert.False(combos.First(c => Avalonia.Automation.AutomationProperties.GetName(c) == AppStrings.Opt_TerminalProfile).IsEffectivelyEnabled);
        options.Close();
        w.CloseNow();
    }

    [AvaloniaFact]
    public async Task Pictures()
    {
        if (Environment.GetEnvironmentVariable("SALANDUAL_CAPTURE_DIR") is not { Length: > 0 } dir)
        {
            return;
        }

        Directory.CreateDirectory(dir);
        var w = await Open(c => c with { RunIn = CommandLineShell.CommandPrompt });
        Toggle(w);
        w.KeyTextInput("ffprobe -hide_banner ");
        Focus(w, "day2 swim.mp4");
        Press(w, Key.Enter, RawInputModifiers.Control);
        Dispatcher.UIThread.RunJobs();
        w.CaptureRenderedFrame()!.Save(Path.Combine(dir, "commandline.png"), Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
        Focus(w, "100%.txt");
        Press(w, Key.Enter, RawInputModifiers.Control);
        Dispatcher.UIThread.RunJobs();
        w.CaptureRenderedFrame()!.Save(Path.Combine(dir, "commandline-risky.png"), Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
        w.CloseNow();
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
}

// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : tests/Salandual.App.Tests/ContextMenuTests.cs

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Salandual.Core.Settings;
using Salandual.Platform.Windows.Shell;

namespace Salandual.App.Tests;

public sealed class ContextMenuTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("salandual-menu-").FullName;
    private readonly string _left;
    private readonly string _right;
    private readonly FakeShell _shell = new();
    private readonly FakeClipboard _clip = new();

    public ContextMenuTests()
    {
        _left = Directory.CreateDirectory(Path.Join(_dir, "Left")).FullName;
        _right = Directory.CreateDirectory(Path.Join(_dir, "Right")).FullName;
        Directory.CreateDirectory(Path.Join(_left, "Edited"));
        File.WriteAllText(Path.Join(_left, "Edited", "inside.txt"), "12345");
        File.WriteAllText(Path.Join(_left, "a.txt"), "aaa");
        File.WriteAllText(Path.Join(_left, "b.txt"), "bb");
        File.WriteAllText(Path.Join(_left, "notes.txt"), "n");
    }

    public void Dispose()
    {
        foreach (var file in Directory.EnumerateFiles(_dir, "*", SearchOption.AllDirectories))
        {
            File.SetAttributes(file, FileAttributes.Normal);
        }

        Directory.Delete(_dir, recursive: true);
    }

    private async Task<MainWindow> Open(Func<MouseSettings, MouseSettings>? mouse = null)
    {
        var w = new MainWindow { Width = 1100, Height = 700 };
        w.ShellAccess = _shell;
        w.FileClipboardAccess = _clip;
        if (mouse is not null)
        {
            w.Services.Settings.Update(s => s with { Mouse = mouse(new MouseSettings()) });
        }
        else
        {
            w.Services.Settings.Update(s => s with { Mouse = new MouseSettings() });
        }

        w.Show();
        await w.LeftPanel.Panel.NavigateAsync(_left);
        await w.RightPanel.Panel.NavigateAsync(_right);
        w.LeftPanel.View.Focus();
        Dispatcher.UIThread.RunJobs();
        return w;
    }

    private static int Row(MainWindow w, string name) => w.LeftPanel.Panel.Items.ToList().FindIndex(e => e.Name == name);

    private static void RightClick(MainWindow w, string? name, RawInputModifiers mods = RawInputModifiers.None)
    {
        var view = w.LeftPanel.View;
        Point local;
        if (name is null)
        {
            local = new Point(60, view.Bounds.Height - 10);
        }
        else
        {
            var r = view.NameBounds(Row(w, name))!.Value;
            local = new Point(r.X + 20, r.Y + (r.Height / 2));
        }

        var p = view.TranslatePoint(local, w)!.Value;
        w.MouseDown(p, MouseButton.Right, mods);
        w.MouseUp(p, MouseButton.Right, mods);
        Dispatcher.UIThread.RunJobs();
    }

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
        string s => s.Replace("_", "", StringComparison.Ordinal),
        TextBlock t => t.Text ?? "",
        Panel p => string.Join(" ", p.Children.Select(c => c switch { AccessText a => a.Text?.Replace("_", "", StringComparison.Ordinal), TextBlock t => t.Text, _ => null }).Where(x => x is not null)),
        _ => "",
    };

    private static List<string> Headers(ItemsControl menu)
        => [.. menu.Items.OfType<MenuItem>().Where(m => m.IsVisible).Select(m => Text(m.Header))];

    private static MenuItem Item(ItemsControl menu, string startsWith)
        => menu.Items.OfType<MenuItem>().First(m => Text(m.Header).StartsWith(startsWith, StringComparison.Ordinal));

    private static void Click(MenuItem item)
    {
        item.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        Dispatcher.UIThread.RunJobs();
    }

    [AvaloniaFact]
    public async Task RightClick_OnAFile_OpensTheQuickMenu_WithWindowsCommands()
    {
        var w = await Open();
        RightClick(w, "a.txt");

        var menu = w.QuickMenu;
        Assert.NotNull(menu);
        var headers = Headers(menu);
        Assert.Equal("Open", headers[0]);
        Assert.Contains("Open with…", headers);
        Assert.Contains("Copy to other panel…", headers);
        Assert.Contains("Windows commands", headers);
        Assert.Equal("Properties", headers[^1]);
        Assert.Equal(Row(w, "a.txt"), w.LeftPanel.Panel.Focus);

        var windows = w.WindowsCommandsItem!;
        await Until(() => Headers(windows).Contains("Properties (Windows)"));
        var shellHeaders = Headers(windows);
        Assert.Contains("Edit with Notepad", shellHeaders);
        Assert.Contains("7-Zip", shellHeaders);
        Assert.DoesNotContain("Cut", shellHeaders);
        Assert.DoesNotContain("Delete", shellHeaders);
        Assert.Equal("Properties (Windows)", shellHeaders[^1]);
        Assert.Equal(["Add to archive..."], Headers(Item(windows, "7-Zip")));

        Click(Item(windows, "Edit with Notepad"));
        await Until(() => _shell.Invoked.Count == 1);
        Assert.Equal(11, _shell.Invoked[0]);
        Assert.Equal([Path.Join(_left, "a.txt")], _shell.LastBuild.Select(n => Path.Join(_left, n)));
        w.CloseNow();
    }

    [AvaloniaFact]
    public async Task RightClick_OnAnUnselectedItem_KeepsTheSelection_AndWorksOnThatItemOnly()
    {
        var w = await Open();
        var panel = w.LeftPanel.Panel;
        panel.Toggle(Row(w, "a.txt"));
        panel.Toggle(Row(w, "b.txt"));
        RightClick(w, "notes.txt");

        Assert.Equal("notes.txt only · the 2 selected are not included", Headers(w.QuickMenu!)[0]);
        Assert.True(panel.IsSelected(Row(w, "a.txt")) && panel.IsSelected(Row(w, "b.txt")));
        Assert.False(panel.IsSelected(Row(w, "notes.txt")));
        Assert.Equal(Row(w, "notes.txt"), panel.Focus);

        Click(Item(w.QuickMenu!, "Copy"));
        Assert.Equal([Path.Join(_left, "notes.txt")], _clip.Content.Files);
        Assert.Equal(2, panel.GetSelectionSummary().Count);
        w.CloseNow();
    }

    [AvaloniaFact]
    public async Task RightClick_OnASelectedItem_IsForTheWholeSelection()
    {
        var w = await Open();
        var panel = w.LeftPanel.Panel;
        panel.Toggle(Row(w, "a.txt"));
        panel.Toggle(Row(w, "b.txt"));
        RightClick(w, "b.txt");

        var headers = Headers(w.QuickMenu!);
        Assert.StartsWith("2 items selected", headers[0]);
        Assert.Contains("Open all 2", headers);
        Assert.Contains("Copy 2 items to other panel…", headers);
        Click(Item(w.QuickMenu!, "Cut"));
        Assert.Equal([Path.Join(_left, "a.txt"), Path.Join(_left, "b.txt")], _clip.Content.Files);
        Assert.True(_clip.Content.Cut);
        w.CloseNow();
    }

    [AvaloniaFact]
    public async Task SelectOnlyItem_Setting_ReplacesTheSelection()
    {
        var w = await Open(m => m with { RightClickUnselected = RightClickUnselected.SelectOnlyItem });
        var panel = w.LeftPanel.Panel;
        panel.Toggle(Row(w, "a.txt"));
        RightClick(w, "notes.txt");

        Assert.False(panel.IsSelected(Row(w, "a.txt")));
        Assert.True(panel.IsSelected(Row(w, "notes.txt")));
        Assert.Equal("Open", Headers(w.QuickMenu!)[0]);
        w.CloseNow();
    }

    [AvaloniaFact]
    public async Task RightButtonSelects_Setting_SelectsAndHoldingOpensTheMenu()
    {
        var w = await Open(m => m with { RightClickUnselected = RightClickUnselected.RightButtonSelects });
        var panel = w.LeftPanel.Panel;
        RightClick(w, "a.txt");
        Assert.True(panel.IsSelected(Row(w, "a.txt")));
        Assert.Null(w.QuickMenu);

        var view = w.LeftPanel.View;
        var r = view.NameBounds(Row(w, "b.txt"))!.Value;
        var p = view.TranslatePoint(new Point(r.X + 20, r.Y + 5), w)!.Value;
        w.MouseDown(p, MouseButton.Right);
        Dispatcher.UIThread.RunJobs();
        Assert.True(panel.IsSelected(Row(w, "b.txt")));
        view.ElapseRightHold();
        Dispatcher.UIThread.RunJobs();
        Assert.StartsWith("2 items selected", Headers(w.QuickMenu!)[0]);
        w.MouseUp(p, MouseButton.Right);
        w.CloseNow();
    }

    [AvaloniaFact]
    public async Task RightClick_BelowTheRows_IsTheFolderMenu_AndShiftF10UsesTheFocus()
    {
        var w = await Open();
        RightClick(w, null);
        var headers = Headers(w.QuickMenu!);
        Assert.Equal("Left", headers[0]);
        Assert.Contains("New folder…", headers);
        Assert.Contains("Sort by", headers);
        Assert.Equal("Properties of this folder", headers[^1]);
        Assert.Empty(_shell.LastBuild);
        w.QuickMenu!.Close();
        Dispatcher.UIThread.RunJobs();

        w.LeftPanel.Panel.SetFocus(Row(w, "Edited"));
        Press(w, Key.F10, RawInputModifiers.Shift);
        Assert.Contains("Open in other panel", Headers(w.QuickMenu!));
        Assert.Contains("Paste into this folder", Headers(w.QuickMenu!));
        w.CloseNow();
    }

    [AvaloniaFact]
    public async Task ShiftRightClick_ShowsWindowsFullMenu()
    {
        var w = await Open();
        RightClick(w, "a.txt", RawInputModifiers.Shift);
        await Until(() => _shell.NativeShown == 1);
        Assert.True(_shell.LastExtended);
        Assert.Null(w.QuickMenu);
        w.CloseNow();
    }

    [AvaloniaFact]
    public async Task Pinning_AWindowsCommand_PutsItInTheQuickMenu()
    {
        var w = await Open();
        RightClick(w, "a.txt");
        var windows = w.WindowsCommandsItem!;
        await Until(() => Headers(windows).Contains("Edit with Notepad"));
        var row = (DockPanel)Item(windows, "Edit with Notepad").Header!;
        var pin = row.Children.OfType<Button>().Single();
        pin.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();

        var pinned = Assert.Single(w.Services.Settings.Current.Mouse.Pinned);
        Assert.Equal("notepad", pinned.Verb);
        Assert.Equal(["Edit with Notepad"], pinned.Path);

        RightClick(w, "b.txt");
        await Until(() => w.WindowsCommandsItem is { } wc && Headers(wc).Count > 1);
        var quick = Headers(w.QuickMenu!);
        Assert.Equal("Edit with Notepad", quick[quick.IndexOf("Windows commands") - 1]);
        Click(Item(w.QuickMenu!, "Edit with Notepad"));
        await Until(() => _shell.Invoked.Contains(11));
        w.CloseNow();
    }

    [AvaloniaFact]
    public async Task Enter_OnAFile_OpensItWithItsProgram()
    {
        var w = await Open();
        w.LeftPanel.Panel.SetFocus(Row(w, "a.txt"));
        Press(w, Key.Enter);
        await Until(() => _shell.Opened.Count == 1);
        Assert.Equal(Path.Join(_left, "a.txt"), _shell.Opened[0]);
        w.CloseNow();
    }

    [AvaloniaFact]
    public async Task AltEnter_OpensProperties_WithLiveFolderCount_AndAltShiftEnterWindows()
    {
        var w = await Open();
        w.LeftPanel.Panel.SetFocus(Row(w, "Edited"));
        Press(w, Key.Enter, RawInputModifiers.Alt);
        var dialog = w.OpenPropertiesDialog;
        Assert.NotNull(dialog);
        await dialog.Loading;
        await Until(() => dialog.CountDone);
        Assert.Equal(["General", "Security", "Previous versions"], dialog.TabNames);
        Assert.StartsWith("1 file, 0 folders", dialog.Shown.Contains);
        Assert.Equal("5 B", dialog.Shown.Size);
        dialog.Close();
        Dispatcher.UIThread.RunJobs();

        Press(w, Key.Enter, RawInputModifiers.Alt | RawInputModifiers.Shift);
        await Until(() => _shell.WindowsProperties.Count == 1);
        Assert.Equal(_left, _shell.WindowsProperties[0].Folder);
        Assert.Equal(["Edited"], _shell.WindowsProperties[0].Names);
        w.CloseNow();
    }

    [AvaloniaFact]
    public async Task Properties_Attributes_ApplyAndUndo()
    {
        var w = await Open();
        w.LeftPanel.Panel.SetFocus(Row(w, "notes.txt"));
        Press(w, Key.Enter, RawInputModifiers.Alt);
        var dialog = w.OpenPropertiesDialog!;
        await dialog.Loading;
        var readOnly = dialog.AttributeBoxes[0];
        readOnly.IsChecked = true;
        Assert.True(dialog.ApplyButton.IsEnabled);
        dialog.ApplyAttributes();
        var file = Path.Join(_left, "notes.txt");
        Assert.True(File.GetAttributes(file).HasFlag(FileAttributes.ReadOnly));
        Assert.Equal("Attributes of notes.txt changed.", w.NoticeMessage);

        w.NoticeButtons[0].RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();
        Assert.False(File.GetAttributes(file).HasFlag(FileAttributes.ReadOnly));
        dialog.Close();
        w.CloseNow();
    }

    [AvaloniaFact]
    public async Task Properties_OfAProgram_HasTheSignaturePage()
    {
        File.WriteAllText(Path.Join(_left, "setup.exe"), "not a program");
        var w = await Open();
        await w.LeftPanel.Panel.RefreshAsync();
        w.LeftPanel.Panel.SetFocus(Row(w, "setup.exe"));
        Press(w, Key.Enter, RawInputModifiers.Alt);
        var dialog = w.OpenPropertiesDialog!;
        Assert.Equal(["General", "Signature", "Security", "Previous versions"], dialog.TabNames);
        dialog.ShowTab("Signature");
        await dialog.PageLoading!;
        dialog.ShowTab("Security");
        await dialog.PageLoading!;
        Assert.Equal("Security", dialog.CurrentTab);
        dialog.Close();
        w.CloseNow();
    }

    private static void Save(TopLevel? t, string name)
    {
        Dispatcher.UIThread.RunJobs();
        Assert.NotNull(t);
        var frame = t.CaptureRenderedFrame();
        Assert.NotNull(frame);
        if (Environment.GetEnvironmentVariable("SALANDUAL_CAPTURE_DIR") is { Length: > 0 } dir)
        {
            Directory.CreateDirectory(dir);
            frame!.Save(Path.Combine(dir, name), Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
        }
    }

    [AvaloniaFact]
    public async Task Capture_QuickMenuAndProperties()
    {
        File.WriteAllText(Path.Join(_left, "setup.exe"), "not a program");
        var w = await Open();
        await w.LeftPanel.Panel.RefreshAsync();
        w.LeftPanel.Panel.Toggle(Row(w, "a.txt"));
        w.LeftPanel.Panel.Toggle(Row(w, "b.txt"));
        RightClick(w, "notes.txt");
        var menu = w.QuickMenu!;
        Save(TopLevel.GetTopLevel(menu), "m35-quickmenu-only-this.png");
        menu.Close();
        RightClick(w, "a.txt");
        Save(TopLevel.GetTopLevel(w.QuickMenu!), "m35-quickmenu-selection.png");
        w.QuickMenu!.Close();
        w.LeftPanel.Panel.SelectWhere(_ => true, select: false);
        RightClick(w, "notes.txt");
        var windows = w.WindowsCommandsItem!;
        await Until(() => Headers(windows).Count > 2);
        windows.IsSubMenuOpen = true;
        Dispatcher.UIThread.RunJobs();
        Save(TopLevel.GetTopLevel(w.QuickMenu!), "m35-quickmenu-file.png");
        Save(TopLevel.GetTopLevel(Item(windows, "Edit with Notepad")), "m35-windows-commands.png");
        w.QuickMenu!.Close();
        RightClick(w, null);
        Save(TopLevel.GetTopLevel(w.QuickMenu!), "m35-quickmenu-folder.png");
        w.QuickMenu!.Close();

        w.LeftPanel.Panel.SetFocus(Row(w, "setup.exe"));
        File.WriteAllText(Path.Join(_left, "setup.exe") + ":Zone.Identifier", "[ZoneTransfer]\r\nZoneId=3\r\n");
        Press(w, Key.Enter, RawInputModifiers.Alt);
        var dialog = w.OpenPropertiesDialog!;
        await dialog.Loading;
        Save(dialog, "m35-props-general.png");
        dialog.ShowTab("Signature");
        await dialog.PageLoading!;
        Save(dialog, "m35-props-signature.png");
        dialog.ShowTab("Security");
        await dialog.PageLoading!;
        Save(dialog, "m35-props-security.png");
        dialog.ShowTab("Previous versions");
        await dialog.PageLoading!;
        Save(dialog, "m35-props-versions.png");
        dialog.Close();
        Dispatcher.UIThread.RunJobs();

        w.LeftPanel.Panel.SetFocus(Row(w, "Edited"));
        Press(w, Key.Enter, RawInputModifiers.Alt);
        dialog = w.OpenPropertiesDialog!;
        await dialog.Loading;
        await Until(() => dialog.CountDone);
        Save(dialog, "m35-props-folder.png");
        dialog.Close();
        w.CloseNow();
    }

    [Theory]
    [InlineData("&Open", "_Open")]
    [InlineData("Save && close", "Save & close")]
    [InlineData("a_b &c", "a__b _c")]
    public void AccessKeys_FromWindowsToAvalonia(string windows, string avalonia) => Assert.Equal(avalonia, MainWindow.AccessKeyText(windows));

    private sealed class FakeShell : IShellAccess
    {
        public List<int> Invoked { get; } = [];

        public List<string> Opened { get; } = [];

        public List<(string Folder, IReadOnlyList<string> Names)> WindowsProperties { get; } = [];

        public IReadOnlyList<string> LastBuild { get; private set; } = [];

        public bool LastExtended { get; private set; }

        public int NativeShown { get; private set; }

        public Task<IShellMenuHandle> BuildMenuAsync(string folder, IReadOnlyList<string> names, bool extended)
        {
            LastBuild = [.. names];
            LastExtended = extended;
            return Task.FromResult<IShellMenuHandle>(new Menu(this, folder, [.. names], extended));
        }

        public Task<int> OpenAsync(nint owner, string path)
        {
            Opened.Add(path);
            return Task.FromResult(0);
        }

        public void OpenWith(nint owner, string path)
        {
        }

        public Task ShowWindowsPropertiesAsync(nint owner, string folder, IReadOnlyList<string> names)
        {
            WindowsProperties.Add((folder, names));
            return Task.CompletedTask;
        }

        public Task ShowSecurityPageAsync(nint owner, string path) => Task.CompletedTask;

        public void ViewCertificate(nint owner, byte[] certificate)
        {
        }

        public Salandual.Core.Operations.DropEffects DragOut(nint owner, string folder, IReadOnlyList<string> names, bool plainCopies, DragPicture? picture) => Salandual.Core.Operations.DropEffects.None;

        public IconPixels? LoadIcon(string path, int sizePx) => null;

        public IDisposable? RegisterDropTarget(nint hwnd, IDropHandler handler) => null;

        public Task<string> CreateShortcutAsync(string target, string folder) => Task.FromResult("");

        private static ShellMenuItem Cmd(int id, string text, string? verb) => new(id, text, verb, false, true, false, false, null, []);

        private static readonly ShellMenuItem Separator = new(0, "", null, true, false, false, false, null, []);

        private sealed class Menu(FakeShell shell, string folder, string[] names, bool extended) : IShellMenuHandle
        {
            public IReadOnlyList<ShellMenuItem> Items { get; } =
            [
                Cmd(10, "&Open", "open"),
                Cmd(11, "Edit with &Notepad", "notepad"),
                new(0, "7-Zip", null, false, true, false, false, null, [Cmd(12, "Add to archive...", "SevenZipCompress")]),
                Separator,
                Cmd(13, "Cu&t", "cut"),
                Cmd(14, "&Copy", "copy"),
                Separator,
                Cmd(15, "&Delete", "delete"),
                Cmd(16, "P&roperties", "properties"),
            ];

            public bool IsFor(string f, IReadOnlyList<string> n, bool e) => e == extended && f == folder && n.SequenceEqual(names);

            public Task InvokeAsync(int id, nint owner, int screenX, int screenY)
            {
                shell.Invoked.Add(id);
                return Task.CompletedTask;
            }

            public Task<string?> ShowNativeAsync(nint owner, int screenX, int screenY)
            {
                shell.NativeShown++;
                return Task.FromResult<string?>(null);
            }

            public void Dispose()
            {
            }
        }
    }

    private sealed class FakeClipboard : IClipboardAccess
    {
        public ClipboardContent Content { get; private set; } = new();

        public uint SequenceNumber { get; private set; } = 1;

        public bool SetFiles(IReadOnlyList<string> paths, bool cut)
        {
            Content = new ClipboardContent { Files = [.. paths], Cut = cut };
            SequenceNumber++;
            return true;
        }

        public bool SetText(string text)
        {
            Content = new ClipboardContent { Text = text };
            SequenceNumber++;
            return true;
        }

        public void Clear()
        {
            Content = new ClipboardContent();
            SequenceNumber++;
        }

        public ClipboardContent? Read() => Content;
    }
}

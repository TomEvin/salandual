// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.App/MainWindow.ContextMenu.cs

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Primitives.PopupPositioning;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Salandual.App.Input;
using Salandual.App.Options;
using Salandual.App.Panels;
using Salandual.App.Resources;
using Salandual.App.Theme;
using Salandual.Core.Commands;
using Salandual.Core.Resources;
using Salandual.Core.Settings;
using Salandual.Platform.Windows.FileSystem;
using Salandual.Platform.Windows.Shell;

namespace Salandual.App;

internal interface IShellAccess
{
    Task<IShellMenuHandle> BuildMenuAsync(string folder, IReadOnlyList<string> names, bool extended);

    Task<int> OpenAsync(nint owner, string path);

    void OpenWith(nint owner, string path);

    Task ShowWindowsPropertiesAsync(nint owner, string folder, IReadOnlyList<string> names);

    Task ShowSecurityPageAsync(nint owner, string path);

    void ViewCertificate(nint owner, byte[] certificate);

    Salandual.Core.Operations.DropEffects DragOut(nint owner, string folder, IReadOnlyList<string> names, bool plainCopies, DragPicture? picture);

    IconPixels? LoadIcon(string path, int sizePx);

    IDisposable? RegisterDropTarget(nint hwnd, IDropHandler handler);

    Task<string> CreateShortcutAsync(string target, string folder);
}

internal interface IShellMenuHandle : IDisposable
{
    IReadOnlyList<ShellMenuItem> Items { get; }

    bool IsFor(string folder, IReadOnlyList<string> names, bool extended);

    Task InvokeAsync(int id, nint owner, int screenX, int screenY);

    Task<string?> ShowNativeAsync(nint owner, int screenX, int screenY);
}

internal sealed class WindowsShellAccess : IShellAccess
{
    public async Task<IShellMenuHandle> BuildMenuAsync(string folder, IReadOnlyList<string> names, bool extended)
        => new Handle(await ShellMenu.BuildAsync(folder, names, extended));

    public Task<int> OpenAsync(nint owner, string path) => ShellWindows.OpenAsync(owner, path);

    public void OpenWith(nint owner, string path) => ShellWindows.OpenWith(owner, path);

    public Task ShowWindowsPropertiesAsync(nint owner, string folder, IReadOnlyList<string> names) => ShellWindows.ShowPropertiesAsync(owner, folder, names);

    public Task ShowSecurityPageAsync(nint owner, string path) => ShellWindows.ShowPropertiesAsync(owner, path, ShellWindows.SecurityPage);

    public void ViewCertificate(nint owner, byte[] certificate) => FileSignature.ViewCertificate(owner, certificate);

    public Salandual.Core.Operations.DropEffects DragOut(nint owner, string folder, IReadOnlyList<string> names, bool plainCopies, DragPicture? picture) => ShellDrag.DragOut(owner, folder, names, plainCopies, picture);

    public IconPixels? LoadIcon(string path, int sizePx) => ShellIconLoader.Load(path, sizePx);

    public IDisposable? RegisterDropTarget(nint hwnd, IDropHandler handler) => ShellDropTarget.Register(hwnd, handler);

    public Task<string> CreateShortcutAsync(string target, string folder) => ShellLinks.CreateAsync(target, folder);

    private sealed class Handle(ShellMenu menu) : IShellMenuHandle
    {
        public IReadOnlyList<ShellMenuItem> Items => menu.Items;

        public bool IsFor(string folder, IReadOnlyList<string> names, bool extended) => menu.IsFor(folder, names, extended);

        public Task InvokeAsync(int id, nint owner, int screenX, int screenY) => menu.InvokeAsync(id, owner, screenX, screenY);

        public Task<string?> ShowNativeAsync(nint owner, int screenX, int screenY) => menu.ShowNativeAsync(owner, screenX, screenY);

        public void Dispose() => menu.Dispose();
    }
}

internal sealed class NoShellAccess : IShellAccess
{
    public Task<IShellMenuHandle> BuildMenuAsync(string folder, IReadOnlyList<string> names, bool extended) => Task.FromResult<IShellMenuHandle>(new Empty(folder, names, extended));

    public List<string> Opened { get; } = [];

    public Task<int> OpenAsync(nint owner, string path)
    {
        Opened.Add(path);
        return Task.FromResult(0);
    }

    public void OpenWith(nint owner, string path)
    {
    }

    public Task ShowWindowsPropertiesAsync(nint owner, string folder, IReadOnlyList<string> names) => Task.CompletedTask;

    public Task ShowSecurityPageAsync(nint owner, string path) => Task.CompletedTask;

    public void ViewCertificate(nint owner, byte[] certificate)
    {
    }

    public Salandual.Core.Operations.DropEffects DragOut(nint owner, string folder, IReadOnlyList<string> names, bool plainCopies, DragPicture? picture) => Salandual.Core.Operations.DropEffects.None;

    public IconPixels? LoadIcon(string path, int sizePx) => null;

    public IDisposable? RegisterDropTarget(nint hwnd, IDropHandler handler) => null;

    public Task<string> CreateShortcutAsync(string target, string folder) => Task.FromException<string>(new NotSupportedException());

    private sealed record Empty(string Folder, IReadOnlyList<string> Names, bool Extended) : IShellMenuHandle
    {
        public IReadOnlyList<ShellMenuItem> Items => [];

        public bool IsFor(string folder, IReadOnlyList<string> names, bool extended)
            => extended == Extended && string.Equals(folder, Folder, StringComparison.OrdinalIgnoreCase) && names.SequenceEqual(Names, StringComparer.OrdinalIgnoreCase);

        public Task InvokeAsync(int id, nint owner, int screenX, int screenY) => Task.CompletedTask;

        public Task<string?> ShowNativeAsync(nint owner, int screenX, int screenY) => Task.FromResult<string?>(null);

        public void Dispose()
        {
        }
    }
}

internal sealed record MenuTarget(string Folder, IReadOnlyList<string> Names, bool OverridesSelection)
{
    public bool IsBackground => Names.Count == 0;

    public static MenuTarget For(string folder, IReadOnlyList<string> names, bool overridesSelection)
    {
        if (names.Count > 0 && names.All(n => n.Contains('\\')))
        {
            var parts = names.Select(Salandual.Core.Panels.FilePanel.SplitResult).ToList();
            if (parts.Select(p => p.Folder).Distinct(StringComparer.OrdinalIgnoreCase).Count() == 1)
            {
                return new MenuTarget(Salandual.Platform.Windows.FileSystem.WindowsPath.Join(folder, parts[0].Folder), [.. parts.Select(p => p.Name)], overridesSelection);
            }
        }

        return new MenuTarget(folder, names, overridesSelection);
    }
}

public partial class MainWindow
{
    private static readonly HashSet<string> _hiddenShellVerbs = new(StringComparer.OrdinalIgnoreCase)
    {
        "cut", "copy", "paste", "delete", "rename", "open", "opennewwindow", "opennewtab", "opennewprocess", "properties",
    };

    private IShellAccess? _shell;
    private IReadOnlyList<string>? _namesOverride;
    private DispatcherTimer? _menuTimer;
    private (MenuTarget Target, Task<IShellMenuHandle?> Build)? _shellMenu;
    private IShellMenuHandle? _menuInUse;
    private IShellMenuHandle? _disposeAfterMenu;

    internal IShellAccess ShellAccess
    {
        get => _shell ??= _services.IsIsolated ? new NoShellAccess() : new WindowsShellAccess();
        set => _shell = value;
    }

    internal ContextMenu? QuickMenu { get; private set; }

    internal MenuItem? WindowsCommandsItem { get; private set; }

    private static MenuTarget DefaultTarget(PanelHost host)
    {
        var panel = host.Panel;
        var noSelection = panel.GetSelectionSummary().Count == 0;
        if (noSelection && (panel.FocusedEntry is null || panel.IsUp(panel.Focus)))
        {
            return MenuTarget.For(panel.Path, [], false);
        }

        return MenuTarget.For(panel.Path, panel.GetOperationNames(), false);
    }

    private void ScheduleShellMenu()
    {
        if (_menuTimer is null)
        {
            _menuTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(150) };
            _menuTimer.Tick += (_, _) =>
            {
                _menuTimer.Stop();
                if (IsActive && ActivePanel.Panel.Path.Length > 0 && !_services.IsIsolated)
                {
                    _ = ShellMenuFor(DefaultTarget(ActivePanel));
                }
            };
        }

        _menuTimer.Stop();
        _menuTimer.Start();
    }

    private Task<IShellMenuHandle?> ShellMenuFor(MenuTarget target)
    {
        if (_shellMenu is { } current && SameTarget(current.Target, target) && !current.Build.IsFaulted)
        {
            return current.Build;
        }

        if (_shellMenu is { } old)
        {
            _ = old.Build.ContinueWith(t =>
            {
                if (t.IsCompletedSuccessfully && t.Result is { } handle)
                {
                    Dispatcher.UIThread.Post(() =>
                    {
                        if (ReferenceEquals(handle, _menuInUse))
                        {
                            _disposeAfterMenu = handle;
                        }
                        else
                        {
                            handle.Dispose();
                        }
                    });
                }
            }, TaskScheduler.Default);
        }

        var build = BuildShellMenuAsync(target);
        _shellMenu = (target, build);
        return build;
    }

    private async Task<IShellMenuHandle?> BuildShellMenuAsync(MenuTarget target)
    {
        try
        {
            return await ShellAccess.BuildMenuAsync(target.Folder, target.Names, extended: false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Runtime.InteropServices.COMException or System.Runtime.InteropServices.ExternalException or ArgumentException)
        {
            return null;
        }
    }

    private static bool SameTarget(MenuTarget a, MenuTarget b)
        => string.Equals(a.Folder, b.Folder, StringComparison.OrdinalIgnoreCase) && a.Names.SequenceEqual(b.Names, StringComparer.OrdinalIgnoreCase);

    private void OnPanelMenuRequested(PanelHost host, PanelMenuRequest request)
    {
        SetActive(Array.IndexOf(_panels, host), focus: true);
        var panel = host.Panel;
        if (panel.Path.Length == 0)
        {
            return;
        }

        MenuTarget target;
        if (request.ForSelection || request.Row < 0 || panel.IsUp(request.Row) || request.Row >= panel.Count)
        {
            target = request.ForSelection ? DefaultTarget(host) : MenuTarget.For(panel.Path, [], false);
        }
        else if (panel.IsSelected(request.Row))
        {
            panel.SetFocus(request.Row);
            target = MenuTarget.For(panel.Path, panel.GetOperationNames(), false);
        }
        else
        {
            var name = panel.Items[request.Row].Name;
            panel.SetFocus(request.Row);
            var hasSelection = panel.GetSelectionSummary().Count > 0;
            if (hasSelection && _services.Settings.Current.Mouse.RightClickUnselected == RightClickUnselected.SelectOnlyItem)
            {
                panel.SelectWhere(_ => true, select: false);
                panel.Toggle(request.Row);
                target = MenuTarget.For(panel.Path, [name], false);
            }
            else
            {
                target = MenuTarget.For(panel.Path, [name], hasSelection);
            }
        }

        var native = request.Shift || _services.Settings.Current.Mouse.RightClick == RightClickMenu.Windows;
        if (native)
        {
            _ = ShowWindowsMenuAsync(host, target, request.Position);
        }
        else
        {
            ShowQuickMenu(host, target, request.Position);
        }
    }

    internal void ShowMenuFromKeyboard(PanelHost host, bool background)
    {
        var panel = host.Panel;
        if (panel.Path.Length == 0)
        {
            return;
        }

        var target = background ? MenuTarget.For(panel.Path, [], false) : DefaultTarget(host);
        var at = host.View.NameBounds(panel.Focus) is { } r && !background ? new Point(r.X + 24, r.Bottom) : new Point(40, 40);
        if (_services.Settings.Current.Mouse.RightClick == RightClickMenu.Windows)
        {
            _ = ShowWindowsMenuAsync(host, target, at);
        }
        else
        {
            ShowQuickMenu(host, target, at);
        }
    }

    private async Task ShowWindowsMenuAsync(PanelHost host, MenuTarget target, Point at)
    {
        var screen = host.View.PointToScreen(at);
        try
        {
            using var menu = await ShellAccess.BuildMenuAsync(target.Folder, target.Names, extended: true);
            var verb = await menu.ShowNativeAsync(_hwnd, screen.X, screen.Y);
            Activate();
            AfterShellCommand(host, verb);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Runtime.InteropServices.ExternalException or ArgumentException)
        {
            ShowNotice(Loc.Format(AppStrings.Notice_WindowsMenuFailed, ex.Message));
        }
    }

    private void AfterShellCommand(PanelHost host, string? verb)
    {
        if (verb is null)
        {
            return;
        }

        _ = host.Panel.RefreshAsync();
    }

    private ContextMenu ShowQuickMenu(PanelHost host, MenuTarget target, Point at)
    {
        QuickMenu?.Close();
        var panel = host.Panel;
        var menu = new ContextMenu
        {
            PlacementTarget = host.View,
            Placement = PlacementMode.AnchorAndGravity,
            PlacementRect = new Rect(at, new Size(1, 1)),
            PlacementAnchor = PopupAnchor.TopLeft,
            PlacementGravity = PopupGravity.BottomRight,
        };
        var items = menu.Items;

        MenuItem Cmd(string header, string? iconId, string? commandId, Action run, bool enabled = true)
        {
            var mi = new MenuItem { Header = header, IsEnabled = enabled, Icon = iconId is null ? null : AppIcons.Create(iconId) };
            if (commandId is not null && _services.Commands.FirstChord(commandId) is { } chord && KeyMapping.ToGesture(chord) is { } gesture)
            {
                mi.InputGesture = gesture;
            }

            mi.Click += (_, _) => run();
            items.Add(mi);
            return mi;
        }

        MenuItem Later(string header, string iconId, string commandId)
        {
            _services.Commands.TryGet(commandId, out var definition);
            var key = _services.Commands.FirstChord(commandId)?.ToString();
            var later = CommandText.Milestone(definition?.PlannedFor ?? "");
            var note = key is null ? Loc.Format(AppStrings.QuickMenu_ArrivesIn, later) : Loc.Format(AppStrings.QuickMenu_KeyArrivesIn, key, later);
            var label = new DockPanel();
            var noteText = new TextBlock { Text = note, FontSize = 11, Margin = new Thickness(24, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center }.Res(TextBlock.ForegroundProperty, "Brush.TertiaryText");
            DockPanel.SetDock(noteText, Dock.Right);
            label.Children.Add(noteText);
            label.Children.Add(new AccessText { Text = header, VerticalAlignment = VerticalAlignment.Center });
            var mi = new MenuItem { Header = label, IsEnabled = false, Icon = AppIcons.Create(iconId, "Brush.TertiaryText"), Tag = note };
            items.Add(mi);
            return mi;
        }

        void AddSeparator()
        {
            if (items.Count > 0 && items[^1] is not Separator)
            {
                items.Add(new Separator());
            }
        }

        void JumpToFile()
        {
            if (panel.Results is not null)
            {
                Cmd(AppStrings.QuickMenu_JumpToFile, "Icon.Path", "Navigation.GoToFile", () => _ = panel.GoToResultAsync());
            }
        }

        void RunFor(string commandId)
        {
            _namesOverride = target.OverridesSelection ? target.Names : null;
            try
            {
                Run(commandId, PanelTarget.Active);
            }
            finally
            {
                _namesOverride = null;
            }
        }

        var entries = target.Names.Select(n => EntryNamed(panel, n)).ToList();
        var single = target.Names.Count == 1 ? entries[0] : null;
        var hasClipboard = FileClipboardAccess.Read() is { } clip && (clip.Files.Count > 0 || clip.Text is not null || clip.Image is not null);

        if (target.IsBackground)
        {
            Head(items, WindowsPath.GetName(panel.Path).TrimEnd('\\') is { Length: > 0 } n ? n : panel.Path);
            Cmd(AppStrings.QuickMenu_Paste, "Clipboard.Paste", "Clipboard.Paste", () => RunFor("Clipboard.Paste"), hasClipboard);
            AddSeparator();
            Cmd(AppStrings.QuickMenu_NewFolder, "File.CreateDirectory", "File.CreateDirectory", () => RunFor("File.CreateDirectory"));
            Cmd(AppStrings.QuickMenu_NewTextFile, "File.Edit", "File.EditNew", () => RunFor("File.EditNew"));
            AddSeparator();
            var sort = Cmd(AppStrings.QuickMenu_SortBy, "Icon.Sort", null, () => { });
            foreach (var (header, id) in new[] { (AppStrings.QuickMenu_SortName, "Panel.SortByName"), (AppStrings.QuickMenu_SortExtension, "Panel.SortByExtension"), (AppStrings.QuickMenu_SortTime, "Panel.SortByTime"), (AppStrings.QuickMenu_SortSize, "Panel.SortBySize") })
            {
                var mi = new MenuItem { Header = header };
                if (KeyMapping.ToGesture(_services.Commands.ChordsFor(id).FirstOrDefault()) is { } g)
                {
                    mi.InputGesture = g;
                }

                mi.Click += (_, _) => RunFor(id);
                sort.Items.Add(mi);
            }

            Cmd(AppStrings.QuickMenu_Refresh, "Panel.Refresh", "Panel.Refresh", () => RunFor("Panel.Refresh"));
            AddSeparator();
            Cmd(AppStrings.QuickMenu_CopyFolderPath, "Icon.Path", "Clipboard.CopyPath", () => RunFor("Clipboard.CopyPath"));
        }
        else
        {
            var many = target.Names.Count > 1;
            if (many)
            {
                var files = entries.Count(e => e is { IsDirectory: false });
                var bytes = entries.Sum(e => e is { IsDirectory: false } f ? f.Size : 0);
                Head(items, Loc.Format(AppStrings.QuickMenu_SelectedHead, Loc.Plural(target.Names.Count, AppStrings.Count_Items_One, AppStrings.Count_Items_Other), CurrentFormat().Size(bytes)));
                Cmd(Loc.Format(AppStrings.QuickMenu_OpenAll, target.Names.Count), "Navigation.Execute", null, () => _ = OpenAllAsync(host, target), files > 0);
                JumpToFile();
            }
            else
            {
                if (target.OverridesSelection)
                {
                    var selected = panel.GetSelectionSummary().Count;
                    Head(items, Loc.Format(selected == 1 ? AppStrings.QuickMenu_OnlyThis_One : AppStrings.QuickMenu_OnlyThis_Other, target.Names[0], selected));
                }

                Cmd(AppStrings.QuickMenu_Open, "Navigation.Execute", "Navigation.Execute", () => _ = panel.OpenFocusedAsync()).FontWeight = FontWeight.SemiBold;
                if (single is { IsDirectory: true })
                {
                    Cmd(AppStrings.QuickMenu_OpenInOther, "Navigation.OpenInOtherPanel", "Navigation.OpenInOtherPanel", () => RunFor("Navigation.OpenInOtherPanel"));
                }
                else
                {
                    Cmd(AppStrings.QuickMenu_OpenWith, "Icon.OpenWith", null, () => ShellAccess.OpenWith(_hwnd, WindowsPath.Join(panel.Path, target.Names[0])));
                }

                JumpToFile();
            }

            Cmd(AppStrings.QuickMenu_View, "File.View", "File.View", () => RunFor("File.View"), single is { IsDirectory: false });
            if (!many && single is { IsDirectory: false })
            {
                Cmd(AppStrings.QuickMenu_Edit, "File.Edit", "File.Edit", () => RunFor("File.Edit"));
            }

            AddSeparator();
            Cmd(AppStrings.QuickMenu_Cut, "Clipboard.Cut", "Clipboard.Cut", () => RunFor("Clipboard.Cut"));
            Cmd(AppStrings.QuickMenu_Copy, "Clipboard.Copy", "Clipboard.Copy", () => RunFor("Clipboard.Copy"));
            if (single is { IsDirectory: true })
            {
                Cmd(AppStrings.QuickMenu_PasteInto, "Clipboard.Paste", "Clipboard.PasteIntoFocused", () => RunFor("Clipboard.PasteIntoFocused"), hasClipboard);
            }
            else if (!many)
            {
                Cmd(AppStrings.QuickMenu_Paste, "Clipboard.Paste", "Clipboard.Paste", () => RunFor("Clipboard.Paste"), hasClipboard);
            }

            AddSeparator();
            var what = Loc.Plural(target.Names.Count, AppStrings.Count_Items_One, AppStrings.Count_Items_Other);
            Cmd(many ? Loc.Format(AppStrings.QuickMenu_CopyItemsToOther, what) : AppStrings.QuickMenu_CopyToOther, "File.Copy", "File.Copy", () => RunFor("File.Copy"));
            Cmd(many ? Loc.Format(AppStrings.QuickMenu_MoveItemsToOther, what) : AppStrings.QuickMenu_MoveToOther, "File.Move", "File.Move", () => RunFor("File.Move"));
            Cmd(AppStrings.QuickMenu_Duplicate, "File.Duplicate", "File.Duplicate", () => RunFor("File.Duplicate"));
            if (!many)
            {
                Cmd(AppStrings.QuickMenu_Rename, "File.QuickRename", "File.QuickRename", () => RunFor("File.QuickRename"));
            }

            Cmd(many ? Loc.Format(AppStrings.QuickMenu_DeleteItems, what) : AppStrings.QuickMenu_Delete, "File.Delete", "File.Delete", () => RunFor("File.Delete"));
            AddSeparator();
            var copyPath = Cmd(many ? AppStrings.QuickMenu_CopyPaths : AppStrings.QuickMenu_CopyPath, "Icon.Path", null, () => { });
            foreach (var (header, id) in new[] { (AppStrings.QuickMenu_FullName, "Clipboard.CopyFullName"), (AppStrings.QuickMenu_Name, "Clipboard.CopyName"), (AppStrings.QuickMenu_FolderPath, "Clipboard.CopyPath"), (AppStrings.QuickMenu_UncName, "Clipboard.CopyUncName") })
            {
                var mi = new MenuItem { Header = header };
                if (KeyMapping.ToGesture(_services.Commands.ChordsFor(id).FirstOrDefault()) is { } g)
                {
                    mi.InputGesture = g;
                }

                mi.Click += (_, _) => RunFor(id);
                copyPath.Items.Add(mi);
            }

            Later(AppStrings.QuickMenu_Pack, "Archive.Pack", "Archive.Pack");
        }

        AddSeparator();
        AddPinned(host, menu, target);
        WindowsCommandsItem = AddWindowsCommands(host, menu, target);
        var props = Cmd(target.IsBackground ? AppStrings.QuickMenu_FolderProperties : AppStrings.QuickMenu_Properties, "File.Properties", "File.Properties",
            () => _ = ShowPropertiesAsync(host, target, windows: _services.Settings.Current.Mouse.AltEnter == PropertiesWindow.Windows));
        _ = props;

        QuickMenu = menu;
        menu.Closed += (_, _) =>
        {
            if (ReferenceEquals(QuickMenu, menu))
            {
                QuickMenu = null;
                WindowsCommandsItem = null;
            }

            _menuInUse = null;
            if (_disposeAfterMenu is { } stale)
            {
                _disposeAfterMenu = null;
                stale.Dispose();
            }

            host.View.Focus();
        };
        menu.Open(host.View);
        return menu;
    }

    private static Core.Vfs.FileEntry? EntryNamed(Core.Panels.FilePanel panel, string name)
    {
        foreach (var e in panel.Items)
        {
            if (string.Equals(e.Name, name, StringComparison.Ordinal))
            {
                return e;
            }
        }

        return null;
    }

    private static void Head(ItemCollection items, string text)
    {
        items.Add(new MenuItem { Header = new TextBlock { Text = text, FontSize = 12 }.Res(TextBlock.ForegroundProperty, "Brush.SecondaryText"), IsEnabled = false });
        items.Add(new Separator());
    }

    private async Task OpenAllAsync(PanelHost host, MenuTarget target)
    {
        foreach (var name in target.Names)
        {
            if (EntryNamed(host.Panel, name) is { IsDirectory: false })
            {
                await OpenFileAsync(WindowsPath.Join(target.Folder, name));
            }
        }
    }

    internal async Task OpenFileAsync(string path)
    {
        var error = await ShellAccess.OpenAsync(_hwnd, path);
        var name = WindowsPath.GetName(path);
        switch (error)
        {
            case 0 or 1223:
                break;
            case 1155:
                ShowNotice(Loc.Format(AppStrings.Notice_NoProgram, name), (AppStrings.QuickMenu_OpenWithAction, () => ShellAccess.OpenWith(_hwnd, path)));
                break;
            default:
                ShowNotice(Loc.Format(AppStrings.Notice_OpenFailed, name, new System.ComponentModel.Win32Exception(error).Message));
                break;
        }
    }

    private MenuItem AddWindowsCommands(PanelHost host, ContextMenu menu, MenuTarget target)
    {
        var item = new MenuItem { Header = AppStrings.QuickMenu_WindowsCommands, Icon = AppIcons.Create("Icon.Windows") };
        var build = ShellMenuFor(target);
        if (build.IsCompletedSuccessfully)
        {
            Fill(build.Result);
        }
        else
        {
            item.Items.Add(new MenuItem { Header = AppStrings.QuickMenu_Loading, IsEnabled = false });
            _ = build.ContinueWith(t => Dispatcher.UIThread.Post(() => Fill(t.IsCompletedSuccessfully ? t.Result : null)), TaskScheduler.Default);
        }

        menu.Items.Add(item);
        return item;

        void Fill(IShellMenuHandle? handle)
        {
            item.Items.Clear();
            _menuInUse = handle;
            var shellItems = handle?.Items ?? [];
            var added = AddShellItems(s => InvokeShell(host, handle, s), item.Items, shellItems, [], top: true, pins: true);
            if (handle is not null && Find(shellItems, i => string.Equals(i.Verb, "properties", StringComparison.OrdinalIgnoreCase)) is { } props)
            {
                if (added > 0)
                {
                    item.Items.Add(new Separator());
                }

                var mi = new MenuItem { Header = AppStrings.QuickMenu_WindowsProperties, Icon = AppIcons.Create("File.Properties") };
                if (KeyMapping.ToGesture(_services.Commands.ChordsFor("File.WindowsProperties").FirstOrDefault()) is { } g)
                {
                    mi.InputGesture = g;
                }

                mi.Click += (_, _) => InvokeShell(host, handle, props);
                item.Items.Add(mi);
                added++;
            }

            if (added == 0)
            {
                item.Items.Add(new MenuItem { Header = AppStrings.QuickMenu_NoWindowsCommands, IsEnabled = false });
            }

            UpdatePinned(menu, shellItems);
        }
    }

    private int AddShellItems(Action<ShellMenuItem> invoke, ItemCollection into, IReadOnlyList<ShellMenuItem> source, IReadOnlyList<string> path, bool top, bool pins)
    {
        var added = 0;
        foreach (var s in source)
        {
            if (s.IsSeparator)
            {
                if (into.Count > 0 && into[^1] is not Separator)
                {
                    into.Add(new Separator());
                }

                continue;
            }

            if (top && s.Verb is { } verb && _hiddenShellVerbs.Contains(verb))
            {
                continue;
            }

            var itemPath = path.Append(s.PlainText).ToList();
            var mi = new MenuItem { IsEnabled = s.IsEnabled, Icon = ShellIcon(s.Icon) };
            if (s.HasChildren)
            {
                mi.Header = AccessKeyText(s.Text);
                if (AddShellItems(invoke, mi.Items, s.Children, itemPath, top: false, pins) == 0)
                {
                    continue;
                }
            }
            else
            {
                mi.Header = pins ? PinnableHeader(s, itemPath) : AccessKeyText(s.Text);
                mi.Click += (_, _) => invoke(s);
            }

            into.Add(mi);
            added++;
        }

        while (into.Count > 0 && into[^1] is Separator)
        {
            into.RemoveAt(into.Count - 1);
        }

        return added;
    }

    private Control PinnableHeader(ShellMenuItem item, IReadOnlyList<string> path)
    {
        var pinned = IsPinned(item.Verb, path);
        var pin = new Button
        {
            Content = AppIcons.Create("Icon.Pin", pinned ? "Brush.Accent" : "Brush.TertiaryText"),
            Padding = new Thickness(2),
            Background = Brushes.Transparent,
            BorderThickness = default,
            Focusable = false,
            Opacity = pinned ? 1 : 0,
            Margin = new Thickness(16, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
        };
        var tip = pinned ? AppStrings.QuickMenu_Unpin : AppStrings.QuickMenu_Pin;
        ToolTip.SetTip(pin, tip);
        Avalonia.Automation.AutomationProperties.SetName(pin, tip);
        pin.Click += (_, e) =>
        {
            e.Handled = true;
            TogglePin(item.Verb, path);
            QuickMenu?.Close();
        };
        var text = new AccessText { Text = AccessKeyText(item.Text), VerticalAlignment = VerticalAlignment.Center };
        var row = new DockPanel { Background = Brushes.Transparent, LastChildFill = true };
        DockPanel.SetDock(pin, Dock.Right);
        row.Children.Add(pin);
        row.Children.Add(text);
        row.PointerEntered += (_, _) => pin.Opacity = 1;
        row.PointerExited += (_, _) => pin.Opacity = IsPinned(item.Verb, path) ? 1 : 0;
        row.Tag = item.PlainText;
        return row;
    }

    private void AddPinned(PanelHost host, ContextMenu menu, MenuTarget target)
    {
        foreach (var pin in _services.Settings.Current.Mouse.Pinned)
        {
            var mi = new MenuItem { Header = AccessKeyText(pin.Title), Tag = pin };
            mi.Click += async (_, _) =>
            {
                var handle = await ShellMenuFor(target);
                if (handle is not null && Find(handle.Items, i => !i.HasChildren && pin.Matches(i.Verb, PathOf(handle.Items, i))) is { } found)
                {
                    InvokeShell(host, handle, found);
                }
                else
                {
                    ShowNotice(Loc.Format(AppStrings.Notice_PinnedMissing, pin.Title));
                }
            };
            menu.Items.Add(mi);
        }
    }

    private static void UpdatePinned(ContextMenu menu, IReadOnlyList<ShellMenuItem> shellItems)
    {
        foreach (var mi in menu.Items.OfType<MenuItem>())
        {
            if (mi.Tag is PinnedShellCommand pin)
            {
                var found = Find(shellItems, i => !i.HasChildren && pin.Matches(i.Verb, PathOf(shellItems, i)));
                mi.IsVisible = found is not null;
                if (found?.Icon is { } icon)
                {
                    mi.Icon = ShellIcon(icon);
                }
            }
        }
    }

    private bool IsPinned(string? verb, IReadOnlyList<string> path) => _services.Settings.Current.Mouse.Pinned.Any(p => p.Matches(verb, path));

    private void TogglePin(string? verb, IReadOnlyList<string> path)
    {
        var title = path.Count > 0 ? path[^1] : verb ?? "";
        if (IsPinned(verb, path))
        {
            _services.Settings.Update(s => s with { Mouse = s.Mouse with { Pinned = [.. s.Mouse.Pinned.Where(p => !p.Matches(verb, path))] } });
            ShowNotice(Loc.Format(AppStrings.Notice_Unpinned, title));
        }
        else
        {
            var pin = new PinnedShellCommand { Verb = string.IsNullOrEmpty(verb) ? null : verb, Path = [.. path] };
            _services.Settings.Update(s => s with { Mouse = s.Mouse with { Pinned = [.. s.Mouse.Pinned, pin] } });
            ShowNotice(Loc.Format(AppStrings.Notice_Pinned, title));
        }
    }

    private void InvokeShell(PanelHost host, IShellMenuHandle? handle, ShellMenuItem item)
    {
        if (handle is null)
        {
            return;
        }

        var screen = host.View.PointToScreen(new Point(40, 40));
        _ = InvokeShellAsync();

        async Task InvokeShellAsync()
        {
            try
            {
                await handle.InvokeAsync(item.Id, _hwnd, screen.X, screen.Y);
                AfterShellCommand(host, item.Verb ?? item.PlainText);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Runtime.InteropServices.ExternalException or ObjectDisposedException)
            {
                ShowNotice(Loc.Format(AppStrings.Notice_WindowsMenuFailed, ex.Message));
            }
        }
    }

    private static ShellMenuItem? Find(IEnumerable<ShellMenuItem> items, Func<ShellMenuItem, bool> match)
    {
        foreach (var i in items)
        {
            if (!i.IsSeparator && match(i))
            {
                return i;
            }

            if (Find(i.Children, match) is { } inner)
            {
                return inner;
            }
        }

        return null;
    }

    private static List<string> PathOf(IReadOnlyList<ShellMenuItem> items, ShellMenuItem target)
    {
        foreach (var i in items)
        {
            if (ReferenceEquals(i, target))
            {
                return [i.PlainText];
            }

            if (i.HasChildren && PathOf(i.Children, target) is { Count: > 0 } inner)
            {
                return [i.PlainText, .. inner];
            }
        }

        return [];
    }

    internal static string AccessKeyText(string text)
    {
        var tab = text.IndexOf('\t');
        if (tab >= 0)
        {
            text = text[..tab];
        }

        return text.Trim().Replace("_", "__", StringComparison.Ordinal).Replace("&&", "\u0001", StringComparison.Ordinal).Replace('&', '_').Replace("\u0001", "&", StringComparison.Ordinal);
    }

    private static Image? ShellIcon(IconPixels? icon)
        => icon is null ? null : new Image { Source = IconCache.ToBitmap(icon), Width = 16, Height = 16 };
}

public partial class MainWindow
{
    private Dialogs.PropertiesDialog? _propertiesDialog;

    internal Dialogs.PropertiesDialog? OpenPropertiesDialog => _propertiesDialog;

    internal async Task ShowPropertiesAsync(PanelHost host, MenuTarget target, bool windows, Window? owner = null)
    {
        if (target.Folder.Length == 0)
        {
            return;
        }

        if (windows)
        {
            await ShellAccess.ShowWindowsPropertiesAsync(owner?.TryGetPlatformHandle()?.Handle ?? _hwnd, target.Folder, target.Names);
            return;
        }

        _propertiesDialog?.Close();
        var actions = new Dialogs.PropertiesActions(
            OpenInOtherPanel: (folder, focus) => _ = OtherPanel(host).Panel.NavigateAsync(folder, focus),
            Restore: (version, folder) =>
            {
                var fs = Operations.Files;
                var source = new Core.Operations.OperationSource(version.Path, Directory.Exists(version.Path));
                var options = _services.Settings.Current.TransferOptions() with { IfExists = Core.Operations.ExistingFilePolicy.Ask };
                Operations.Add(Core.Operations.FileOperations.Copy(fs, [source], new Core.Operations.TransferTarget(folder), options), toQueue: false);
            },
            AttributesChanged: changed =>
            {
                _ = host.Panel.RefreshAsync();
                var text = changed.Count == 1
                    ? Loc.Format(AppStrings.Notice_AttributesChanged_One, WindowsPath.GetName(changed[0].Path))
                    : Loc.Format(AppStrings.Notice_AttributesChanged_Other, changed.Count);
                ShowNotice(text, (AppStrings.Common_Undo, () =>
                {
                    foreach (var (path, old) in changed)
                    {
                        try
                        {
                            FileDetailsReader.SetSimpleAttributes(path, old);
                        }
                        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                        {
                            ShowNotice(ex.Message);
                        }
                    }

                    _ = host.Panel.RefreshAsync();
                }));
            },
            CopyText: text =>
            {
                ShowNotice(FileClipboardAccess.SetText(text) ? Loc.Format(AppStrings.Notice_CopiedText, text) : AppStrings.Notice_ClipboardBusy);
                return Task.CompletedTask;
            },
            CompareFiles: (left, right) => OpenFileCompare(left, right));

        var dialog = new Dialogs.PropertiesDialog(target.Folder, target.Names, CurrentFormat(), ShellAccess, actions, null);
        _propertiesDialog = dialog;
        ShellIconLoader? icons = null;
        if (!_services.IsIsolated)
        {
            var path = target.Names.Count == 1 ? WindowsPath.Join(target.Folder, target.Names[0]) : target.Names.Count == 0 ? target.Folder : null;
            if (path is not null)
            {
                icons = new ShellIconLoader();
                icons.Loaded += (_, _, pixels) =>
                {
                    if (pixels is not null)
                    {
                        Dispatcher.UIThread.Post(() => dialog.SetIcon(pixels));
                    }
                };
                icons.Request(path, path, (int)Math.Round(40 * RenderScaling));
            }
        }

        try
        {
            await dialog.ShowDialog(owner ?? this);
        }
        finally
        {
            icons?.Dispose();
            if (ReferenceEquals(_propertiesDialog, dialog))
            {
                _propertiesDialog = null;
            }

            if (owner is null)
            {
                host.View.Focus();
            }
        }
    }
}

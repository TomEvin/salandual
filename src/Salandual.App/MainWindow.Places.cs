// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.App/MainWindow.Places.cs

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Primitives.PopupPositioning;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Salandual.App.Dialogs;
using Salandual.App.Input;
using Salandual.App.Options;
using Salandual.App.Panels;
using Salandual.App.Resources;
using Salandual.App.Theme;
using Salandual.Core.Commands;
using Salandual.Core.Panels;
using Salandual.Core.Resources;
using Salandual.Core.Settings;
using Salandual.Core.Vfs;
using Salandual.Platform.Windows.FileSystem;
using Salandual.Platform.Windows.Shell;

namespace Salandual.App;

public partial class MainWindow
{
    private IReadOnlyList<ShellNewItem>? _newItems;

    internal ContextMenu? PlaceItemsMenu { get; private set; }

    internal ContextMenu? NewMenu { get; private set; }

    internal Func<string, Task<bool>>? PlaceQuestion { get; set; }

    private PlacesFileSystem PlaceFiles => _services.PanelFiles;

    private static bool InPlace(PanelHost host) => (ShellPlace.IsPlace(host.Panel.Path) || ShellPlace.IsServer(host.Panel.Path)) && host.Panel.Results is null;

    private static bool InBin(PanelHost host) => InPlace(host) && ShellPlace.Is(host.Panel.Path, ShellPlace.RecycleBin);

    private static bool NeedsFolder(string id)
        => id.StartsWith("File.", StringComparison.Ordinal) || id.StartsWith("Clipboard.", StringComparison.Ordinal) || id.StartsWith("Archive.", StringComparison.Ordinal)
           || id.StartsWith("Find.", StringComparison.Ordinal) || id.StartsWith("Compare.", StringComparison.Ordinal) || id.StartsWith("UserMenu.", StringComparison.Ordinal)
           || id.StartsWith("Shell.", StringComparison.Ordinal) || id.StartsWith("CommandLine.Insert", StringComparison.Ordinal)
           || id is "Panel.CalculateDirectorySizes" or "Panel.FlatView" or "Drive.Info" or "Navigation.ShortcutTarget";

    private bool RunInPlace(string id, PanelHost host)
    {
        if (id == "Navigation.Parent" && GoUpToThisPc(host))
        {
            return true;
        }

        if (!InPlace(host))
        {
            return false;
        }

        var bin = InBin(host);
        switch (id)
        {
            case "File.ContextMenu" or "Panel.DirectoryContextMenu":
                var at = id == "File.ContextMenu" && host.View.NameBounds(host.Panel.Focus) is { } r ? new Point(r.X + 24, r.Bottom) : new Point(40, 40);
                ShowPlaceMenu(host, at, background: id == "Panel.DirectoryContextMenu");
                return true;
            case "File.Copy" or "File.Move" when bin:
                _ = CopyOutOfBinAsync(host, move: id == "File.Move");
                return true;
            case "File.Delete" or "File.DeleteInverted" when bin:
                _ = DeleteForGoodAsync(host);
                return true;
        }

        if (NeedsFolder(id))
        {
            ShowNotice(bin ? AppStrings.Place_BinCommands : Loc.Format(AppStrings.Place_NotHere, ShellPlace.Label(host.Panel.Path)));
            return true;
        }

        return false;
    }

    private void UpdatePlaceState(PanelHost host)
    {
        var path = host.Panel.Path;
        host.View.Place = InPlace(host) ? PlaceColumnsFor(path) : null;
        UpdateNetworkState(host);
    }

    private PlaceColumns PlaceColumnsFor(string path)
    {
        var format = CurrentFormat();
        PlaceItem? Item(FileEntry e) => PlaceFiles.Item(path, e.Name);
        string? Icon(FileEntry e) => Item(e)?.Target is { } t && System.IO.Path.IsPathFullyQualified(t) ? t : null;
        if (ShellPlace.IsServer(path))
        {
            return new PlaceColumns(AppStrings.Net_CommentHead, e => Item(e)?.Comment) { Size = _ => "" };
        }

        return ShellPlace.RootOf(path) switch
        {
            ShellPlace.ThisPc => new PlaceColumns(AppStrings.Place_FreeHead, e => Item(e) is { IsDrive: true, Total: > 0 } d ? Loc.Format(AppStrings.Dir_Free, format.Size(d.Free)) : null)
            {
                Fill = e => Item(e) is { IsDrive: true, Total: > 0 } d ? 1 - ((double)d.Free / d.Total) : null,
                Size = e => Item(e) is { IsDrive: true, Total: > 0 } d ? format.ColumnSize(d.Total) : "",
                IconPath = Icon,
            },
            ShellPlace.RecycleBin => new PlaceColumns(AppStrings.Place_FromHead, e => Item(e)?.From) { DateHead = AppStrings.Place_DeletedHead, IconPath = Icon },
            ShellPlace.Network => new PlaceColumns(AppStrings.Net_CommentHead, e => Item(e)?.Comment)
            {
                IconPath = e => Item(e) is { IsComputer: false } i ? Icon(e) : null,
                Glyph = e => Item(e) is { IsComputer: true } ? "Icon.MenuNetwork" : null,
                Size = _ => "",
            },
            ShellPlace.Shares => new PlaceColumns(AppStrings.Panel_Col_Folder, e => Item(e)?.From) { IconPath = Icon },
            _ => new PlaceColumns(AppStrings.Panel_Col_Folder, e => Item(e)?.From) { IconPath = Icon },
        };
    }

    private bool GoUpToThisPc(PanelHost host)
    {
        var panel = host.Panel;
        var place = DriveMenu.Place(DriveMenuSettings.ThisPc);
        if (panel.Results is not null || panel.IsFlat || ShellPlace.IsPlace(panel.Path) || !(place.InMenu || place.InStrip)
            || WindowsPath.GetRoot(panel.Path) is not { } root || root.StartsWith(@"\\", StringComparison.Ordinal)
            || !string.Equals(root.TrimEnd('\\'), panel.Path.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        _ = GoToThisPcAsync(panel, root);
        return true;
    }

    private async Task GoToThisPcAsync(FilePanel panel, string root)
    {
        if (await panel.NavigateAsync(ShellPlace.ThisPc) && PlaceFiles.NameOf(ShellPlace.ThisPc, root) is { } name)
        {
            for (var i = 0; i < panel.Count; i++)
            {
                if (panel.Items[i].Name == name)
                {
                    panel.SetFocus(i);
                    break;
                }
            }
        }
    }

    private void ShowPlaceMenu(PanelHost host, Point at, bool background)
    {
        PlaceItemsMenu?.Close();
        var panel = host.Panel;
        var names = background ? [] : panel.GetOperationNames().Where(n => n != "..").ToList();
        var menu = PlaceItemsMenu = new ContextMenu
        {
            PlacementTarget = host.View,
            Placement = PlacementMode.AnchorAndGravity,
            PlacementRect = new Rect(at, new Size(1, 1)),
            PlacementAnchor = PopupAnchor.TopLeft,
            PlacementGravity = PopupGravity.BottomRight,
        };

        MenuItem Add(string header, string icon, string? commandId, Action run, bool enabled = true)
        {
            var mi = new MenuItem { Header = header, IsEnabled = enabled, Icon = AppIcons.ForPanel(AppIcons.TryGet(icon, out _) ? icon : "Icon.Path", ReferenceEquals(host, _panels[1])) };
            if (commandId is not null && _services.Commands.FirstChord(commandId) is { } chord && KeyMapping.ToGesture(chord) is { } gesture)
            {
                mi.InputGesture = gesture;
            }

            mi.Click += (_, _) => run();
            menu.Items.Add(mi);
            return mi;
        }

        if (InBin(host))
        {
            if (names.Count > 0)
            {
                Add(AppStrings.Place_Restore, "Icon.Undo", null, () => _ = RestoreAsync(host)).FontWeight = FontWeight.SemiBold;
                Add(AppStrings.Place_DeleteForGood, "File.Delete", "File.Delete", () => _ = DeleteForGoodAsync(host));
                menu.Items.Add(new Separator());
                Add(AppStrings.Place_CopyOut, "File.Copy", "File.Copy", () => _ = CopyOutOfBinAsync(host, move: false));
                Add(AppStrings.Place_MoveOut, "File.Move", "File.Move", () => _ = CopyOutOfBinAsync(host, move: true));
                menu.Items.Add(new Separator());
            }

            Add(AppStrings.Place_Empty, "File.Delete", null, () => _ = EmptyBinAsync(host), panel.Count > 0);
        }
        else if (names.Count == 1 && panel.FocusedEntry is { } focused)
        {
            Add(AppStrings.QuickMenu_Open, "Navigation.Execute", "Navigation.Execute", () => _ = panel.OpenFocusedAsync()).FontWeight = FontWeight.SemiBold;
            if (focused.IsDirectory)
            {
                Add(AppStrings.QuickMenu_OpenInOther, "Navigation.OpenInOtherPanel", "Navigation.OpenInOtherPanel", () => Run("Navigation.OpenInOtherPanel", PanelTarget.Active));
            }

            if (ShellPlace.IsServer(panel.Path) || PlaceFiles.Item(panel.Path, focused.Name) is { IsComputer: true })
            {
                Add(AppStrings.Net_ConnectThis, "Icon.MenuNetwork", "Network.ConnectDrive", () => _ = ConnectDriveAsync(host));
            }
        }

        if (menu.Items.Count > 0 && menu.Items[^1] is not Separator)
        {
            menu.Items.Add(new Separator());
        }

        Add(CommandText.Title("Panel.OpenInExplorer"), "Icon.Path", "Panel.OpenInExplorer", () => _ = OpenInExplorerAsync(host));
        Add(AppStrings.QuickMenu_Refresh, "Panel.Refresh", "Panel.Refresh", () => _ = panel.RefreshAsync());
        menu.Open(host.View);
    }

    private List<string> BinTargets(PanelHost host)
        => [.. host.Panel.GetOperationNames().Select(n => PlaceFiles.Item(host.Panel.Path, n)?.Target).OfType<string>()];

    private async Task<bool> AskAsync(string title, string message, string yes, ConfirmGroup group = ConfirmGroup.Delete)
    {
        if (PlaceQuestion is { } question)
        {
            return await question(title);
        }

        var dialog = new ConfirmDialog(title, message, yes, AppStrings.Common_Cancel, group: group);
        return await dialog.ShowDialog<bool?>(this) == true;
    }

    private string Items(int count) => Loc.Plural(count, AppStrings.Count_Items_One, AppStrings.Count_Items_Other);

    private async Task RestoreAsync(PanelHost host)
    {
        var targets = BinTargets(host);
        if (targets.Count > 0 && await BinActionAsync(host, () => PlaceFiles.Places.RestoreAsync(targets, _hwnd)))
        {
            ShowNotice(Loc.Format(AppStrings.Place_Restored, Items(targets.Count)));
        }
    }

    private async Task DeleteForGoodAsync(PanelHost host)
    {
        var targets = BinTargets(host);
        if (targets.Count > 0 && await AskAsync(AppStrings.Place_DeleteForGoodTitle, Loc.Format(AppStrings.Place_DeleteForGoodText, Items(targets.Count)), AppStrings.Place_DeleteForGood))
        {
            await BinActionAsync(host, () => PlaceFiles.Places.DeleteForGoodAsync(targets, _hwnd));
        }
    }

    private async Task EmptyBinAsync(PanelHost host)
    {
        if (await AskAsync(AppStrings.Place_EmptyTitle, Loc.Format(AppStrings.Place_EmptyText, Items(host.Panel.Count)), AppStrings.Place_EmptyButton))
        {
            await BinActionAsync(host, () => PlaceFiles.Places.EmptyAsync(_hwnd));
        }
    }

    private async Task CopyOutOfBinAsync(PanelHost host, bool move)
    {
        var other = OtherPanel(host);
        var folder = other.Panel.Path;
        if (ShellPlace.IsPlace(folder) || ShellPlace.IsServer(folder) || other.Panel.Results is not null || ArchiveAt(other) is not null || !System.IO.Path.IsPathFullyQualified(folder))
        {
            ShowNotice(AppStrings.Place_CopyOutWhere);
            return;
        }

        var targets = BinTargets(host);
        var title = Loc.Format(move ? AppStrings.Place_MoveOutTitle : AppStrings.Place_CopyOutTitle, Items(targets.Count));
        if (targets.Count > 0 && await AskAsync(title, Loc.Format(AppStrings.Place_IntoFolder, folder), move ? AppStrings.Place_MoveButton : AppStrings.Place_CopyButton, ConfirmGroup.Create))
        {
            if (await BinActionAsync(host, () => PlaceFiles.Places.CopyOutAsync(targets, folder, move, _hwnd)))
            {
                _ = other.Panel.RefreshAsync();
            }
        }
    }

    private async Task<bool> BinActionAsync(PanelHost host, Func<Task> action)
    {
        try
        {
            await action();
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Runtime.InteropServices.ExternalException or ArgumentException)
        {
            ShowNotice(Loc.Format(AppStrings.Place_Failed, ex.Message));
            return false;
        }
        finally
        {
            _ = host.Panel.RefreshAsync();
        }
    }

    internal async Task OpenInExplorerAsync(PanelHost host)
    {
        var panel = host.Panel;
        string folder;
        IReadOnlyList<string> names;
        if (ShellPlace.Is(panel.Path, ShellPlace.Shares))
        {
            folder = @"\\" + Environment.MachineName;
            names = [];
        }
        else if (ShellPlace.IsPlace(panel.Path))
        {
            folder = ShellPlace.RootOf(panel.Path)!;
            names = [];
        }
        else if (ArchiveAt(host) is { } location)
        {
            var archive = location.ArchivePath;
            while (WindowsPath.GetParent(archive) is { } up && _services.Archives.LocateCached(up) is not null)
            {
                archive = up;
            }

            folder = WindowsPath.GetParent(archive) ?? archive;
            names = [WindowsPath.GetName(archive)];
        }
        else
        {
            var target = MenuTarget.For(panel.Path, panel.GetOperationNames().Where(n => n != "..").ToList(), false);
            if (target.Names.Any(n => n.Contains('\\')))
            {
                var (inFolder, name) = FilePanel.SplitResult(panel.FocusedEntry?.Name ?? target.Names[0]);
                target = new MenuTarget(WindowsPath.Join(panel.Path, inFolder), [name], false);
            }

            folder = target.Folder;
            names = target.Names;
        }

        try
        {
            await ShellAccess.OpenInExplorerAsync(folder, names);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Runtime.InteropServices.ExternalException or ArgumentException)
        {
            ShowNotice(Loc.Format(AppStrings.Place_Failed, ex.Message));
        }
    }

    internal async Task GoToShortcutTargetAsync(PanelHost host)
    {
        var panel = host.Panel;
        if (panel.FocusedEntry is not { IsDirectory: false } entry || panel.IsUp(panel.Focus) || !entry.Name.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase))
        {
            ShowNotice(AppStrings.Place_NotAShortcut);
            return;
        }

        string? target = null;
        try
        {
            target = await ShellAccess.ShortcutTargetAsync(panel.ChildPath(panel.Path, entry.Name));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Runtime.InteropServices.ExternalException)
        {
        }

        if (target is null)
        {
            ShowNotice(Loc.Format(AppStrings.Place_TargetGone, entry.Name));
        }
        else if (WindowsPath.GetParent(target) is { } parent)
        {
            await panel.NavigateAsync(parent, WindowsPath.GetName(target));
        }
        else
        {
            await panel.NavigateAsync(target);
        }
    }

    internal void ShowNewMenu(PanelHost host, Point? at = null)
    {
        var panel = host.Panel;
        if (InPlace(host) || panel.Results is not null || ArchiveAt(host) is not null || !System.IO.Path.IsPathFullyQualified(panel.Path))
        {
            ShowNotice(AppStrings.New_NotHere);
            return;
        }

        QuickMenu?.Close();
        NewMenu?.Close();
        var point = at ?? (host.View.NameBounds(panel.Focus) is { } r ? new Point(r.X + 24, r.Bottom) : new Point(40, 40));
        var menu = NewMenu = new ContextMenu
        {
            PlacementTarget = host.View,
            Placement = PlacementMode.AnchorAndGravity,
            PlacementRect = new Rect(point, new Size(1, 1)),
            PlacementAnchor = PopupAnchor.TopLeft,
            PlacementGravity = PopupGravity.BottomRight,
        };

        MenuItem Add(string header, string icon, string? commandId, Action run, bool enabled = true)
        {
            var mi = new MenuItem { Header = header, IsEnabled = enabled, Icon = AppIcons.Create(AppIcons.TryGet(icon, out _) ? icon : "File.Edit") };
            if (commandId is not null && _services.Commands.FirstChord(commandId) is { } chord && KeyMapping.ToGesture(chord) is { } gesture)
            {
                mi.InputGesture = gesture;
            }

            mi.Click += (_, _) => run();
            menu.Items.Add(mi);
            return mi;
        }

        var templates = Templates();
        if (templates.Count > 0)
        {
            menu.Items.Add(new MenuItem { Header = new TextBlock { Text = AppStrings.New_YourTemplates, FontSize = 11 }.Res(TextBlock.ForegroundProperty, "Brush.TertiaryText"), IsEnabled = false });
            foreach (var template in templates)
            {
                Add(System.IO.Path.GetFileName(template), "File.Edit", null, () => _ = CreateNewAsync(host, System.IO.Path.GetFileName(template), template, null));
            }

            menu.Items.Add(new Separator());
        }

        Add(AppStrings.New_Folder, "File.CreateDirectory", "File.CreateDirectory", () => Run("File.CreateDirectory", PanelTarget.Active));
        var shortcutTo = ShortcutTarget(OtherPanel(host));
        Add(shortcutTo is null ? AppStrings.New_Shortcut : Loc.Format(AppStrings.New_ShortcutTo, WindowsPath.GetName(shortcutTo).TrimEnd('\\')), "Icon.Path", null,
            () => _ = CreateShortcutHereAsync(host, shortcutTo!), shortcutTo is not null);
        menu.Items.Add(new Separator());
        foreach (var item in _newItems ??= ShellAccess.NewItems())
        {
            Add(item.Name, "File.Edit", null, () => _ = CreateNewAsync(host, Loc.Format(AppStrings.New_FileName, item.Name) + item.Extension, item.Template, item.Data ?? []));
        }

        menu.Items.Add(new Separator());
        Add(AppStrings.New_TemplatesFolder, "Icon.Path", null, () => _ = ChooseTemplatesFolderAsync());
        menu.Open(host.View);
    }

    private List<string> Templates()
    {
        var folder = _services.Settings.Current.Operations.TemplatesFolder;
        if (folder.Length == 0 || !Directory.Exists(folder))
        {
            return [];
        }

        try
        {
            return [.. new DirectoryInfo(folder).EnumerateFiles().Where(f => (f.Attributes & (FileAttributes.Hidden | FileAttributes.System)) == 0)
                .Select(f => f.FullName).Order(StringComparer.CurrentCultureIgnoreCase).Take(40)];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    private string? ShortcutTarget(PanelHost other)
    {
        var panel = other.Panel;
        if (ShellPlace.IsPlace(panel.Path) || panel.Results is not null || ArchiveAt(other) is not null || !System.IO.Path.IsPathFullyQualified(panel.Path))
        {
            return null;
        }

        return panel.FocusedEntry is { } e && !panel.IsUp(panel.Focus) ? WindowsPath.Join(panel.Path, e.Name) : panel.Path;
    }

    internal async Task CreateNewAsync(PanelHost host, string name, string? template, byte[]? data)
    {
        var folder = host.Panel.Path;
        string? made = null;
        try
        {
            made = await Task.Run(() =>
            {
                var path = FreeName(folder, name);
                if (template is not null)
                {
                    File.Copy(template, WindowsPath.ToExtended(path), overwrite: false);
                }
                else
                {
                    using var stream = new FileStream(WindowsPath.ToExtended(path), FileMode.CreateNew, FileAccess.Write);
                    stream.Write(data ?? []);
                }

                return path;
            });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ShowNotice(Loc.Format(AppStrings.EditNew_CantCreate, name, ex.Message));
            return;
        }

        await FocusAndRenameAsync(host, WindowsPath.GetName(made));
    }

    private async Task CreateShortcutHereAsync(PanelHost host, string target)
    {
        try
        {
            var path = await ShellAccess.CreateShortcutAsync(target, host.Panel.Path);
            await FocusAndRenameAsync(host, WindowsPath.GetName(path));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Runtime.InteropServices.ExternalException or NotSupportedException)
        {
            ShowNotice(Loc.Format(AppStrings.EditNew_CantCreate, WindowsPath.GetName(target), ex.Message));
        }
    }

    private async Task FocusAndRenameAsync(PanelHost host, string name)
    {
        await host.Panel.RefreshAsync(name, []);
        if (host.Panel.FocusedEntry?.Name == name)
        {
            host.BeginRename(Operations.Files);
        }
    }

    private static string FreeName(string folder, string name)
    {
        var path = WindowsPath.Join(folder, name);
        var stem = System.IO.Path.GetFileNameWithoutExtension(name);
        var ext = System.IO.Path.GetExtension(name);
        for (var n = 2; File.Exists(WindowsPath.ToExtended(path)) || Directory.Exists(WindowsPath.ToExtended(path)); n++)
        {
            path = WindowsPath.Join(folder, stem + " (" + n.ToString(System.Globalization.CultureInfo.InvariantCulture) + ")" + ext);
        }

        return path;
    }

    private async Task ChooseTemplatesFolderAsync()
    {
        var current = _services.Settings.Current.Operations.TemplatesFolder;
        var start = current.Length > 0 ? await StorageProvider.TryGetFolderFromPathAsync(current) : null;
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = AppStrings.New_TemplatesFolderTitle, SuggestedStartLocation = start });
        if (folders.Count > 0 && folders[0].TryGetLocalPath() is { } path)
        {
            _services.Settings.Update(s => s with { Operations = s.Operations with { TemplatesFolder = path } });
        }
    }
}

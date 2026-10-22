// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.App/MainWindow.DragDrop.cs

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives.PopupPositioning;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Salandual.App.Panels;
using Salandual.App.Resources;
using Salandual.Core.Operations;
using Salandual.Core.Resources;
using Salandual.Core.Settings;
using Salandual.Platform.Windows.FileSystem;
using Salandual.Platform.Windows.Shell;

namespace Salandual.App;

internal enum DropSpotKind
{
    None,

    Panel,

    FolderRow,

    Chip,

    Line,
}

internal sealed record DropSpot(DropSpotKind Kind, PanelHost? Host = null, string Folder = "", string Name = "", int Row = FilePanelView.NoDrop, DriveStrip? Strip = null, StripItem? Item = null)
{
    public static DropSpot Nowhere { get; } = new(DropSpotKind.None);
}

public partial class MainWindow
{
    private IDisposable? _dropTarget;
    private DropData _dragData = DropData.Empty;
    private (string Folder, HashSet<string> Names)? _dragged;

    private bool IsDragged(string folder, string name)
        => _dragged is { } d && d.Names.Contains(name) && string.Equals(d.Folder, folder, StringComparison.OrdinalIgnoreCase);
    private bool _dragRight;
    private DropSpot _dropSpot = DropSpot.Nowhere;

    internal ContextMenu? DropMenu { get; private set; }

    private MouseSettings Mouse => _services.Settings.Current.Mouse;

    private void RegisterDropTarget()
    {
        if (_hwnd == 0 || _dropTarget is not null)
        {
            return;
        }

        try
        {
            _dropTarget = ShellAccess.RegisterDropTarget(_hwnd, new WindowDropHandler(this));
        }
        catch (Exception)
        {
        }
    }

    private void UnregisterDropTarget()
    {
        _dropTarget?.Dispose();
        _dropTarget = null;
    }

    internal void StartDrag(PanelHost host, PanelDragRequest request)
    {
        var panel = host.Panel;
        if (panel.Path.Length == 0 || request.Row < 0 || request.Row >= panel.Items.Count || panel.IsUp(request.Row))
        {
            return;
        }

        IReadOnlyList<string> names = panel.IsSelected(request.Row) ? panel.GetOperationNames() : [panel.Items[request.Row].Name];
        if (names.Count == 0)
        {
            return;
        }

        _dragged = (panel.Path, new HashSet<string>(names, StringComparer.OrdinalIgnoreCase));
        host.View.InvalidateRows();
        try
        {
            ShellAccess.DragOut(_hwnd, panel.Path, names, plainCopies: Mouse.PlainDrag == PlainDrag.AlwaysCopies, DragPictureFor(panel.Path, names));
        }
        catch (Exception ex)
        {
            ShowNotice(ex.Message);
        }
        finally
        {
            _dragged = null;
            host.View.InvalidateRows();
        }

        _ = panel.RefreshAsync();
    }

    internal DragPicture? DragPictureFor(string folder, IReadOnlyList<string> names)
    {
        try
        {
            var scale = RenderScaling;
            var icons = names.Take(3).Select(name =>
            {
                var path = WindowsPath.Join(folder, name);
                var pixels = ShellAccess.LoadIcon(path, (int)Math.Round(DragPictures.IconSize * scale));
                return (pixels is null ? null : (Avalonia.Media.Imaging.Bitmap)IconCache.ToBitmap(pixels), Directory.Exists(path));
            }).ToList();
            return DragPictures.Draw(icons, names.Count, scale, Brush("Brush.Accent"), Brush("Brush.OnAccent"), Brush("Brush.SecondaryText"));
        }
        catch (Exception)
        {
            return null;
        }

        IBrush Brush(string key) => this.TryFindResource(key, ActualThemeVariant, out var v) && v is IBrush b ? b : Brushes.Gray;
    }

    internal DropFeedback DragEnterAt(DropData data, DragKeys keys, Point at, DropEffects allowed)
    {
        _dragData = data;
        _dragRight = (keys & DragKeys.RightButton) != 0;
        return DragOverAt(keys, at, allowed);
    }

    internal DropFeedback DragOverAt(DragKeys keys, Point at, DropEffects allowed)
    {
        var spot = SpotAt(at);
        Highlight(spot);
        return Feedback(spot, keys, allowed);
    }

    internal void DragLeft()
    {
        Highlight(DropSpot.Nowhere);
        _dragData = DropData.Empty;
    }

    internal DropEffects DropAt(DragKeys keys, Point at, DropEffects allowed)
    {
        var right = _dragRight;
        var data = _dragData;
        var spot = SpotAt(at);
        var feedback = Feedback(spot, keys, allowed);
        DragLeft();
        if (spot.Kind == DropSpotKind.Line)
        {
            if (DroppedPath(data) is { } path)
            {
                Dispatcher.UIThread.Post(() => spot.Host!.DirectoryLine.Drop(path));
                return DropEffects.Link;
            }

            return DropEffects.None;
        }

        if (spot.Kind == DropSpotKind.None || data.Files.Count == 0 || DropRules.Check(data.Files, spot.Folder) != DropProblem.None)
        {
            return DropEffects.None;
        }

        if (right || _services.Settings.Current.Confirmations.DragDropMenu)
        {
            Dispatcher.UIThread.Post(() => ShowDropMenu(spot, data.Files, allowed, at));
            return DropEffects.None;
        }

        if (feedback.Effect != DropEffects.None)
        {
            Dispatcher.UIThread.Post(() => _ = DropFilesAsync(spot, data.Files, feedback.Effect, withOptions: false));
        }

        return feedback.Effect;
    }

    private static string? DroppedPath(DropData data)
        => data.Files.Count > 0 ? data.Files[0] : data.Text is { } text && text.Trim().Length > 0 ? text : null;

    internal DropSpot SpotAt(Point at)
    {
        for (var v = this.InputHitTest(at) as Visual; v is not null; v = v.GetVisualParent())
        {
            switch (v)
            {
                case FilePanelView view when _panels.FirstOrDefault(h => h.View == view) is { } host:
                    return PanelSpot(host, view.RowAt(this.TranslatePoint(at, view) ?? default));
                case DriveStrip strip:
                    return ChipSpot(strip, strip.ItemAt(this.TranslatePoint(at, strip) ?? default));
                case DirectoryLine line when _panels.FirstOrDefault(h => h.DirectoryLine == line) is { } host:
                    return new DropSpot(DropSpotKind.Line, host);
            }
        }

        return DropSpot.Nowhere;
    }

    private static DropSpot PanelSpot(PanelHost host, int row)
    {
        var panel = host.Panel;
        if (panel.Path.Length == 0)
        {
            return DropSpot.Nowhere;
        }

        if (row >= 0 && row < panel.Items.Count && !panel.IsUp(row) && panel.Items[row] is { IsDirectory: true } dir)
        {
            return new DropSpot(DropSpotKind.FolderRow, host, WindowsPath.Join(panel.Path, dir.Name), dir.Name, row);
        }

        return new DropSpot(DropSpotKind.Panel, host, panel.Path, FolderName(panel.Path), FilePanelView.DropOnPanel);
    }

    private DropSpot ChipSpot(DriveStrip strip, StripItem? item)
    {
        var host = _panels.FirstOrDefault(h => h.Strip == strip) ?? ActivePanel;
        switch (item?.Kind)
        {
            case StripItemKind.Drive:
                var folder = DriveDropFolder(host, item.Target);
                return new DropSpot(DropSpotKind.Chip, host, folder, folder, Strip: strip, Item: item);
            case StripItemKind.Place:
                return new DropSpot(DropSpotKind.Chip, host, item.Target, FolderName(item.Target), Strip: strip, Item: item);
            case StripItemKind.HotPath when HotPaths[int.Parse(item.Target, System.Globalization.CultureInfo.InvariantCulture)] is { } hp && hp.Path.Length > 0:
                return new DropSpot(DropSpotKind.Chip, host, hp.Path, Loc.Format(AppStrings.Drop_HotPath, FolderName(hp.Path), hp.Slot), Strip: strip, Item: item);
            default:
                return DropSpot.Nowhere;
        }
    }

    private string DriveDropFolder(PanelHost host, string root)
    {
        if (Mouse.DriveChipDrop == DriveChipDrop.DriveRoot)
        {
            return root;
        }

        var sameDrive = string.Equals(WindowsPath.GetRoot(host.Panel.Path), root, StringComparison.OrdinalIgnoreCase);
        return !sameDrive && _lastFolder.TryGetValue(root, out var last) && Directory.Exists(last) ? last : root;
    }

    private static string FolderName(string path)
        => Path.GetFileName(path.TrimEnd('\\')) is { Length: > 0 } name ? name : path;

    private DropFeedback Feedback(DropSpot spot, DragKeys keys, DropEffects allowed)
    {
        var files = _dragData.Files;
        if (spot.Kind == DropSpotKind.Line)
        {
            return DroppedPath(_dragData) is { } path
                ? new DropFeedback(DropEffects.Link, new DropNote(DropImage.Link, AppStrings.Drop_GoTo, FolderName(path.Split('\n', 2)[0].Trim().Trim('"'))))
                : DropFeedback.Nothing;
        }

        if (spot.Kind == DropSpotKind.None || files.Count == 0)
        {
            return DropFeedback.Nothing;
        }

        switch (DropRules.Check(files, spot.Folder))
        {
            case DropProblem.AlreadyThere:
                return new DropFeedback(DropEffects.None, new DropNote(DropImage.None, AppStrings.Drop_AlreadyIn, spot.Name));
            case DropProblem.IntoItself:
                return new DropFeedback(DropEffects.None, new DropNote(DropImage.None, AppStrings.Drop_IntoItself, spot.Name));
        }

        var effect = DropRules.Choose((keys & DragKeys.Control) != 0, (keys & DragKeys.Shift) != 0, (keys & DragKeys.Alt) != 0, allowed,
            DropRules.SameDrive(files[0], spot.Folder), Mouse.PlainDrag);
        return effect switch
        {
            DropEffects.Copy => new DropFeedback(effect, new DropNote(DropImage.Copy, AppStrings.Drop_CopyTo, spot.Name)),
            DropEffects.Move => new DropFeedback(effect, new DropNote(DropImage.Move, AppStrings.Drop_MoveTo, spot.Name)),
            DropEffects.Link => new DropFeedback(effect, new DropNote(DropImage.Link, AppStrings.Drop_LinkIn, spot.Name)),
            _ => DropFeedback.Nothing,
        };
    }

    private void Highlight(DropSpot spot)
    {
        var old = _dropSpot;
        _dropSpot = spot;
        if (old.Host is { } oldHost && !ReferenceEquals(old.Host, spot.Host))
        {
            oldHost.View.DropRow = FilePanelView.NoDrop;
            oldHost.DirectoryLine.Dropping = false;
        }

        if (old.Strip is { } oldStrip && !ReferenceEquals(oldStrip, spot.Strip))
        {
            oldStrip.DropItem = null;
        }

        if (spot.Host is { } host)
        {
            host.View.DropRow = spot.Kind is DropSpotKind.Panel or DropSpotKind.FolderRow ? spot.Row : FilePanelView.NoDrop;
            host.DirectoryLine.Dropping = spot.Kind == DropSpotKind.Line;
        }

        if (spot.Strip is { } strip)
        {
            strip.DropItem = spot.Item;
        }
    }

    internal async Task DropFilesAsync(DropSpot spot, IReadOnlyList<string> files, DropEffects effect, bool withOptions)
    {
        var host = spot.Host ?? ActivePanel;
        if (effect == DropEffects.Link)
        {
            await MakeShortcutsAsync(host, files, spot.Folder);
            return;
        }

        var kind = effect == DropEffects.Move ? OperationKind.Move : OperationKind.Copy;
        var fs = Operations.Files;
        var sources = files.Select(f => new OperationSource(f, fs.Stat(f) is { IsDirectory: true })).ToList();
        var showsTarget = string.Equals(host.Panel.Path, spot.Folder, StringComparison.OrdinalIgnoreCase);
        if (showsTarget)
        {
            SetActive(Array.IndexOf(_panels, host), focus: true);
        }

        OperationJob? job;
        if (withOptions || Mouse.AfterDrop == AfterDrop.ShowDialog)
        {
            var known = files.Sum(f => fs.Stat(f) is { IsDirectory: false } e ? e.Size : 0);
            job = await CopyMoveDialogAsync(host, kind, sources, WindowsPath.GetParent(files[0]) ?? "", known, spot.Folder);
        }
        else
        {
            var options = _services.Settings.Current.TransferOptions();
            job = kind == OperationKind.Copy
                ? FileOperations.Copy(fs, sources, new TransferTarget(spot.Folder), options)
                : FileOperations.Move(fs, sources, new TransferTarget(spot.Folder), options);
            Operations.Add(job, toQueue: false);
        }

        if (job is not null && showsTarget)
        {
            var names = files.Select(f => Path.GetFileName(f.TrimEnd('\\'))).ToList();
            _ = job.Completion.ContinueWith(_ => Dispatcher.UIThread.Post(() =>
            {
                if (string.Equals(host.Panel.Path, spot.Folder, StringComparison.OrdinalIgnoreCase))
                {
                    _ = host.Panel.RefreshAsync(names[0], select: names);
                }
            }), TaskScheduler.Default);
        }
    }

    private async Task MakeShortcutsAsync(PanelHost host, IReadOnlyList<string> files, string folder)
    {
        foreach (var file in files)
        {
            try
            {
                await ShellAccess.CreateShortcutAsync(file, folder);
            }
            catch (Exception ex)
            {
                ShowNotice(Loc.Format(AppStrings.Notice_ShortcutFailed, Path.GetFileName(file.TrimEnd('\\')), ex.Message));
                break;
            }
        }

        foreach (var panel in _panels.Where(h => string.Equals(h.Panel.Path, folder, StringComparison.OrdinalIgnoreCase)))
        {
            await panel.Panel.RefreshAsync();
        }
    }

    private ContextMenu ShowDropMenu(DropSpot spot, IReadOnlyList<string> files, DropEffects allowed, Point at)
    {
        DropMenu?.Close();
        var host = spot.Host ?? ActivePanel;
        Control anchor = spot.Strip is { } s ? s : host.View;
        var menu = new ContextMenu
        {
            PlacementTarget = anchor,
            Placement = PlacementMode.AnchorAndGravity,
            PlacementRect = new Rect(this.TranslatePoint(at, anchor) ?? default, new Size(1, 1)),
            PlacementAnchor = PopupAnchor.TopLeft,
            PlacementGravity = PopupGravity.BottomRight,
        };
        var plain = DropRules.PlainDefault(DropRules.SameDrive(files[0], spot.Folder), Mouse.PlainDrag);
        var count = Loc.Plural(files.Count, AppStrings.Count_Items_One, AppStrings.Count_Items_Other);
        var where = WindowsPath.GetRoot(spot.Folder) is { } root && !string.Equals(root, spot.Folder, StringComparison.OrdinalIgnoreCase)
            ? Loc.Format(AppStrings.DropMenu_Where, FolderName(spot.Folder), root.TrimEnd('\\'))
            : spot.Folder;
        menu.Items.Add(new MenuItem { Header = Loc.Format(AppStrings.DropMenu_Head, count, where), IsEnabled = false });
        menu.Items.Add(new Separator());

        void Add(string header, DropEffects effect, bool withOptions)
        {
            var mi = new MenuItem
            {
                Header = header,
                IsEnabled = (allowed & effect) != 0,
                FontWeight = effect == plain && !withOptions ? FontWeight.Bold : FontWeight.Normal,
                Icon = Salandual.App.Theme.AppIcons.Create(effect switch { DropEffects.Move => "File.Move", DropEffects.Link => "Icon.Path", _ => "File.Copy" }),
            };
            mi.Click += (_, _) => _ = DropFilesAsync(spot, files, effect, withOptions);
            menu.Items.Add(mi);
        }

        Add(AppStrings.DropMenu_CopyHere, DropEffects.Copy, withOptions: false);
        Add(AppStrings.DropMenu_MoveHere, DropEffects.Move, withOptions: false);
        Add(AppStrings.DropMenu_LinkHere, DropEffects.Link, withOptions: false);
        menu.Items.Add(new Separator());
        Add(AppStrings.DropMenu_CopyWithOptions, DropEffects.Copy, withOptions: true);
        Add(AppStrings.DropMenu_MoveWithOptions, DropEffects.Move, withOptions: true);
        menu.Items.Add(new Separator());
        menu.Items.Add(new MenuItem { Header = AppStrings.Common_Cancel, InputGesture = new KeyGesture(Key.Escape) });
        menu.Closed += (_, _) =>
        {
            if (ReferenceEquals(DropMenu, menu))
            {
                DropMenu = null;
            }
        };
        DropMenu = menu;
        menu.Open(anchor);
        return menu;
    }

    private sealed class WindowDropHandler(MainWindow window) : IDropHandler
    {
        public DropFeedback Enter(DropData data, DragKeys keys, int x, int y, DropEffects allowed)
            => window.DragEnterAt(data, keys, At(x, y), allowed);

        public DropFeedback Over(DragKeys keys, int x, int y, DropEffects allowed)
            => window.DragOverAt(keys, At(x, y), allowed);

        public void Leave() => window.DragLeft();

        public DropEffects Drop(DragKeys keys, int x, int y, DropEffects allowed)
            => window.DropAt(keys, At(x, y), allowed);

        private Point At(int x, int y) => window.PointToClient(new PixelPoint(x, y));
    }
}

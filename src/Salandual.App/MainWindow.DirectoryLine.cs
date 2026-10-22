// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.App/MainWindow.DirectoryLine.cs

using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Salandual.App.Dialogs;
using Salandual.App.Panels;
using Salandual.App.Resources;
using Salandual.Core.Listing;
using Salandual.Core.Panels;
using Salandual.Core.Resources;
using Salandual.Core.Settings;
using Salandual.Platform.Windows.FileSystem;
using static Salandual.App.Options.OptionsUi;

namespace Salandual.App;

public partial class MainWindow
{
    private readonly WorkingDirectories _working = new();
    private MenuFlyout? _workMenu;
    private PanelHost? _workMenuHost;
    private ChangeDirectoryDialog? _cdDialog;
    private DriveInfoDialog? _driveInfo;

    internal WorkingDirectories Working => _working;

    internal MenuFlyout? WorkingDirectoriesMenu => _workMenu;

    internal ChangeDirectoryDialog? OpenChangeDirectoryDialog => _cdDialog;

    internal DriveInfoDialog? OpenDriveInfo => _driveInfo;

    private DirectoryLineSettings DirLine => _services.Settings.Current.Layout.DirectoryLine;

    private bool SharedWorkingDirectories => DirLine.WorkingDirectories == WorkingDirectoriesScope.Shared;

    private void WireDirectoryLine(PanelHost host, int index)
    {
        var line = host.DirectoryLine;
        line.EditRequested += (_, _) => ChangeDirectory(host);
        line.HistoryRequested += (_, _) => ToggleWorkingDirectories(host);
        line.DriveInfoRequested += (_, _) => _ = ShowDriveInfoAsync(host);
        line.MaximizeRequested += (_, _) => ToggleMaximize(index);
        line.PathDropped += (_, path) => _ = GoDroppedAsync(host, path);

        host.PathChanged += (_, path) => _working.Remember(path, index);
    }

    internal void ChangeDirectory(PanelHost host)
    {
        SetActive(Array.IndexOf(_panels, host), focus: false);
        if (host.IsEditingPath)
        {
            host.ChangeDirectory.Input.Focus();
            return;
        }

        var context = ChangeDirectoryContextFor(host);
        if (DirLine.ChangeDirectory == ChangeDirectoryStyle.Dialog)
        {
            _ = ChangeDirectoryDialogAsync(host, context);
        }
        else
        {
            host.BeginChangeDirectory(context);
        }
    }

    private ChangeDirectoryContext ChangeDirectoryContextFor(PanelHost host)
    {
        var side = Array.IndexOf(_panels, host);
        var shared = SharedWorkingDirectories;
        return new ChangeDirectoryContext(
            host.Panel.Path,
            [.. _working.For(side, shared).Select(w => w.Path)],
            shared,
            _panels[0].Panel.Path,
            _panels[1].Panel.Path,
            HotPaths.Paths,
            _services.Settings.Current.Panels.ShowHiddenFiles);
    }

    private async Task ChangeDirectoryDialogAsync(PanelHost host, ChangeDirectoryContext context)
    {
        if (_cdDialog is not null)
        {
            _cdDialog.Activate();
            return;
        }

        _cdDialog = new ChangeDirectoryDialog(context);
        try
        {
            if (await _cdDialog.ShowDialog<ChangeDirectoryTarget?>(this) is { } target)
            {
                _ = host.Panel.NavigateAsync(target.Folder, target.FocusName);
            }
        }
        finally
        {
            _cdDialog = null;
            host.View.Focus();
        }
    }

    internal void ToggleWorkingDirectories(PanelHost host)
    {
        if (_workMenu is { IsOpen: true })
        {
            var same = ReferenceEquals(_workMenuHost, host);
            _workMenu.Hide();
            if (same)
            {
                return;
            }
        }

        _driveMenu?.Hide();
        var side = Array.IndexOf(_panels, host);
        SetActive(side, focus: false);
        _workMenuHost = host;
        var shared = SharedWorkingDirectories;
        var line = host.DirectoryLine;
        var flyout = _workMenu = new MenuFlyout { Placement = PlacementMode.Bottom };
        if (line.IsVisible && line.Bounds.Width > 0)
        {
            flyout.HorizontalOffset = line.HistoryButtonBounds.Center.X - (line.Bounds.Width / 2);
        }

        var header = Text((shared ? "Working directories" : "Working directories of this panel").ToUpperInvariant(), 11, FontWeight.SemiBold, "Brush.TertiaryText");
        flyout.Items.Add(new MenuItem { Header = header, IsEnabled = false, Focusable = false, Padding = new Thickness(10, 6, 10, 0), MinHeight = 0 });
        var list = _working.For(side, shared);
        for (var i = 0; i < list.Count; i++)
        {
            var w = list[i];
            var digit = i < 10 ? (char)('0' + ((i + 1) % 10)) : (char?)null;
            var isCurrent = string.Equals(w.Path, host.Panel.Path, StringComparison.OrdinalIgnoreCase);
            var number = Text(digit?.ToString() ?? "", 13, brush: "Brush.TertiaryText");
            number.MinWidth = 16;
            number.TextAlignment = TextAlignment.Right;
            var path = new TextBlock { Text = w.Path, FontSize = 12, MaxWidth = shared ? 320 : 370, TextTrimming = TextTrimming.PrefixCharacterEllipsis, FontWeight = isCurrent ? FontWeight.Bold : FontWeight.Normal, VerticalAlignment = VerticalAlignment.Center };
            path.Res(TextBlock.FontFamilyProperty, "Font.Mono");
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), ColumnSpacing = 10 };
            row.Children.Add(number);
            Grid.SetColumn(path, 1);
            row.Children.Add(path);
            if (shared)
            {
                var tag = Text(w.Side == 0 ? "left" : "right", 11, brush: "Brush.TertiaryText");
                tag.Margin = new Thickness(20, 0, 0, 0);
                tag.VerticalAlignment = VerticalAlignment.Center;
                Grid.SetColumn(tag, 2);
                row.Children.Add(tag);
            }

            var item = new MenuItem { Header = row, Tag = digit };
            var target = w.Path;
            item.Click += (_, _) => _ = host.Panel.NavigateAsync(target);
            item.AddHandler(KeyDownEvent, OnDriveMenuKey, Avalonia.Interactivity.RoutingStrategies.Tunnel);
            flyout.Items.Add(item);
        }

        flyout.Items.Add(new Separator());
        var clear = new MenuItem { Header = AppStrings.Dir_ClearList };
        clear.Click += (_, _) =>
        {
            _working.Clear(side, shared, _panels[0].Panel.Path, _panels[1].Panel.Path);
            ShowNotice(shared ? AppStrings.Dir_Cleared : AppStrings.Dir_ClearedHere);
        };
        flyout.Items.Add(clear);

        flyout.Closed += (_, _) =>
        {
            line.IsHistoryOpen = false;
            if (ReferenceEquals(_workMenu, flyout))
            {
                _workMenu = null;
            }

            if (!IsActive || FocusManager?.GetFocusedElement() is null or MenuItem)
            {
                host.View.Focus();
            }
        };
        line.IsHistoryOpen = true;
        HookDriveMenuKeys();
        flyout.ShowAt(line.IsVisible ? line : host);
        Dispatcher.UIThread.Post(() => flyout.Items.OfType<MenuItem>().FirstOrDefault(i => i.IsEnabled && i.Focusable)?.Focus(NavigationMethod.Directional), DispatcherPriority.Loaded);
    }

    internal async Task ShowDriveInfoAsync(PanelHost host)
    {
        SetActive(Array.IndexOf(_panels, host), focus: false);
        if (_driveInfo is not null)
        {
            _driveInfo.Activate();
            return;
        }

        if (host.Panel.Path.Length == 0)
        {
            return;
        }

        _driveInfo = new DriveInfoDialog(host.Panel.Path, DisplayFormat.From(_services.Settings.Current.Panels, CultureInfo.CurrentCulture));
        try
        {
            await _driveInfo.ShowDialog(this);
        }
        finally
        {
            _driveInfo = null;
            host.View.Focus();
            _ = LoadDrivesAsync();
        }
    }

    internal async Task GoDroppedAsync(PanelHost host, string text)
    {
        SetActive(Array.IndexOf(_panels, host), focus: true);
        if (PathInput.Resolve(text, host.Panel.Path) is not { } path)
        {
            return;
        }

        var (kind, real) = await Task.Run(() => PathInput.Probe(path));
        switch (kind)
        {
            case PathInput.Kind.Folder:
                await host.Panel.NavigateAsync(real);
                break;
            case PathInput.Kind.File when WindowsPath.GetParent(real) is { } folder:
                await host.Panel.NavigateAsync(folder, WindowsPath.GetName(real));
                break;
            default:
                ShowNotice(Loc.Format(AppStrings.Cd_NotFound, text));
                break;
        }
    }

    private void UpdateMaximizeButtons()
    {
        for (var i = 0; i < _panels.Length; i++)
        {
            _panels[i].DirectoryLine.IsMaximized = _maximized == i;
        }
    }
}

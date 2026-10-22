// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.App/Viewer/ViewerWindow.Limits.cs

using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Salandual.App.Options;
using Salandual.App.Resources;
using Salandual.Core.Resources;
using Salandual.Core.Listing;
using Salandual.Core.Settings;
using Salandual.Platform.Windows.FileSystem;

using static Salandual.App.Viewer.ToolbarParts;

namespace Salandual.App.Viewer;

internal sealed partial class ViewerWindow
{
    private readonly TextBlock _limitText = new() { FontSize = 13, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
    private Border _limitBar = null!;
    private Button _limitAnyway = null!;
    private StackPanel _limitButtons = null!;
    private Button _limitChange = null!;
    private Action? _barAction;
    private string? _anywayPath;
    private ViewerKind _limitKind;

    internal bool IsOverLimit => _limitBar.IsVisible;

    private Border BuildLimitBar()
    {
        _limitText.Res(TextBlock.ForegroundProperty, "Brush.Text");
        var anyway = _limitAnyway = new Button { Content = AppStrings.Viewer_OpenAnyway, Padding = new Thickness(12, 3), VerticalAlignment = VerticalAlignment.Center };
        anyway.Click += (_, _) => (_barAction ?? OpenAnyway)();
        var change = _limitChange = new Button { Content = AppStrings.Viewer_ChangeLimit, Padding = new Thickness(8, 3), Background = Brushes.Transparent, VerticalAlignment = VerticalAlignment.Center, IsVisible = _request.OpenOptions is not null };
        change.Res(Button.ForegroundProperty, "Brush.Accent");
        change.Click += (_, _) => _request.OpenOptions?.Invoke("viewers");
        var buttons = _limitButtons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Children = { anyway, change } };
        DockPanel.SetDock(buttons, Dock.Right);
        _limitBar = new Border
        {
            Padding = new Thickness(12, 6),
            BorderThickness = new Thickness(0, 0, 0, 1),
            IsVisible = false,
            Child = new DockPanel { Children = { buttons, _limitText } },
        }.Res(Border.BackgroundProperty, "Brush.InfoBackground").Res(Border.BorderBrushProperty, "Brush.Border");
        Avalonia.Automation.AutomationProperties.SetName(_limitBar, AppStrings.Viewer_OpenAnyway);
        return _limitBar;
    }

    private async Task<ViewerKind?> OverLimit(string path, ViewerKind kind)
    {
        if (string.Equals(_anywayPath, path, StringComparison.Ordinal))
        {
            return null;
        }

        var limit = Options.LargestBytes(WindowsPath.GetName(path), kind);
        if (limit <= 0)
        {
            return null;
        }

        long size;
        try
        {
            size = await Task.Run(() => new FileInfo(WindowsPath.ToExtended(path)).Length);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return null;
        }

        if (size <= limit)
        {
            return null;
        }

        var instead = kind == ViewerKind.Image ? ViewerKind.Hex : ViewerKind.Text;
        var format = DisplayFormat.From(_settings.Current.Panels, CultureInfo.CurrentCulture);
        var what = kind switch
        {
            ViewerKind.Image => AppStrings.Viewer_LimitImage,
            ViewerKind.Notebook => AppStrings.Viewer_LimitNotebook,
            ViewerKind.Tree => AppStrings.Viewer_LimitTree,
            ViewerKind.Html => AppStrings.Viewer_LimitHtml,
            _ => AppStrings.Viewer_LimitMarkdown,
        };
        _limitText.Text = Loc.Format(AppStrings.Viewer_OverLimit, instead == ViewerKind.Hex ? AppStrings.ViewerKind_Hex : AppStrings.ViewerKind_Text, format.Size(size), what, format.Size(limit));
        _limitAnyway.Content = kind == ViewerKind.Tree ? AppStrings.Viewer_OpenTreeAnyway : AppStrings.Viewer_OpenAnyway;
        _limitButtons.IsVisible = true;
        _limitChange.IsVisible = _request.OpenOptions is not null;
        _barAction = null;
        _limitKind = kind;
        _limitBar.IsVisible = true;
        return instead;
    }

    private void ShowNote(string text)
    {
        _limitText.Text = text;
        _limitButtons.IsVisible = false;
        _limitBar.IsVisible = true;
    }

    private void ShowBar(string text, string button, Action action)
    {
        _limitText.Text = text;
        _limitAnyway.Content = button;
        _limitChange.IsVisible = false;
        _barAction = action;
        _limitButtons.IsVisible = true;
        _limitBar.IsVisible = true;
    }

    internal string LimitText => _limitText.Text ?? "";

    private void HideLimitBar()
    {
        _limitBar.IsVisible = false;
        _barAction = null;
    }

    private void OpenAnyway()
    {
        _anywayPath = _path;
        HideLimitBar();
        _ = LoadAsync(_path, _limitKind);
    }
}

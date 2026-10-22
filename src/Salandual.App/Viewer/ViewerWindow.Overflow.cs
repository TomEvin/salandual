// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.App/Viewer/ViewerWindow.Overflow.cs

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Threading;
using Salandual.App.Controls;
using Salandual.App.Options;
using Salandual.App.Resources;
using Salandual.Core.Resources;
using Salandual.Core.Settings;
using Salandual.Core.Viewer;
using Salandual.Imaging;

using static Salandual.App.Viewer.ToolbarParts;

namespace Salandual.App.Viewer;

internal sealed partial class ViewerWindow
{
    private Border _modes = null!;
    private Ellipse _moreDot = null!;

    internal bool MoreHoldsTools => _moreDot.IsVisible;

    private Control MoreContent()
    {
        _moreDot = new Ellipse { Width = 6, Height = 6, IsVisible = false, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, -3, -4, 0) };
        _moreDot.Res(Shape.FillProperty, "Brush.Accent");
        return new Panel { Children = { Glyph("Icon.More"), _moreDot } };
    }

    private void WatchOverflow()
    {
        foreach (var row in new[] { ToolbarLeft, _textTools, _hexTools, _imageTools, _pageTools, _treeTools })
        {
            row.HiddenChanged += (_, _) => Dispatcher.UIThread.Post(UpdateMoreDot);
        }
    }

    private void UpdateMoreDot()
    {
        var holds = OverflowItems().Count > 0;
        _moreDot.IsVisible = holds;
        ToolTip.SetTip(_more, holds ? AppStrings.Viewer_MoreWithTools : AppStrings.Viewer_MoreTip);
    }

    internal bool OnToolbar(Control tool) => tool.IsVisible && !ToolbarLeft.Hidden.Contains(tool) && !ToolbarRight.Hidden.Contains(tool);

    internal List<Control> OverflowItems()
    {
        var items = new List<Control>();
        foreach (var tool in ToolbarLeft.Hidden)
        {
            items.AddRange(ItemsFor(tool));
        }

        return items;
    }

    private IEnumerable<Control> ItemsFor(Control tool)
    {
        MenuItem Item(string header, string? key, Action click, bool enabled = true)
        {
            var item = new MenuItem { Header = header, InputGesture = key is null ? null : KeyGesture.Parse(key), IsEnabled = enabled };
            item.Click += (_, _) => click();
            return item;
        }

        MenuItem Check(string header, string? key, bool on, Action click)
        {
            var item = Item(header, key, click);
            item.ToggleType = MenuItemToggleType.CheckBox;
            item.IsChecked = on;
            return item;
        }

        MenuItem Radio(string header, string? key, bool on, Action click)
        {
            var item = Item(header, key, click);
            item.ToggleType = MenuItemToggleType.Radio;
            item.IsChecked = on;
            return item;
        }

        MenuItem Choices(string header, int[] choices, int current, Func<int, string> label, Action<int> choose)
        {
            var item = new MenuItem { Header = header };
            foreach (var n in choices)
            {
                var choice = new MenuItem { Header = label(n), ToggleType = MenuItemToggleType.Radio, IsChecked = n == current };
                choice.Click += (_, _) => choose(n);
                item.Items.Add(choice);
            }

            return item;
        }

        void Later(Action action) => Dispatcher.UIThread.Post(action);

        MenuItem ZoomMenu(double current, Action<double> choose)
        {
            var item = new MenuItem { Header = Loc.Format(AppStrings.Viewer_ZoomItem, ZoomBox.Format(current)) };
            foreach (var preset in ZoomBox.PresetItems(current, choose))
            {
                item.Items.Add(preset);
            }

            return item;
        }

        if (tool == _previousFile)
        {
            return [Item(AppStrings.Viewer_PreviousFile, "Back", () => _ = StepFileAsync(-1), _previousFile.IsEnabled)];
        }

        if (tool == _nextFile)
        {
            return [Item(AppStrings.Viewer_NextFile, "Space", () => _ = StepFileAsync(1), _nextFile.IsEnabled)];
        }

        if (tool == _modes)
        {
            var modes = new List<Control>();
            if (_pageModeButton.IsVisible)
            {
                modes.Add(Radio(AppStrings.Viewer_Rendered, "F6", _pageModeButton.IsChecked == true, ShowPage));
            }

            if (_treeModeButton.IsVisible)
            {
                modes.Add(Radio(AppStrings.Viewer_Tree, "F6", _treeModeButton.IsChecked == true, ShowTree));
            }

            if (_textMode.IsVisible)
            {
                modes.Add(Radio(AppStrings.Viewer_Text, "F5", _textMode.IsChecked == true, () => SetMode(false)));
            }

            if (_imageModeButton.IsVisible)
            {
                modes.Add(Radio(AppStrings.Viewer_Image, null, _imageModeButton.IsChecked == true, ShowImage));
            }

            modes.Add(Radio(AppStrings.Viewer_Hex, "F4", _hexModeButton.IsChecked == true, () => _ = ShowHexAsync()));
            return modes;
        }

        if (tool == _encoding)
        {
            return [Item(Loc.Format(AppStrings.Viewer_EncodingItem, _document?.Encoding.Name ?? TextEncodings.Default.Name), "F8", () => Later(() => ShowEncodingMenu(_more, characters: false)))];
        }

        if (tool == _wrap)
        {
            return [Check(AppStrings.Viewer_Wrap, "F2", Options.Wrap, () => UpdateViewer(v => v with { Wrap = !v.Wrap }))];
        }

        if (tool == _numbers)
        {
            return [Check(AppStrings.Viewer_LineNumbers, null, Options.LineNumbers, () => UpdateViewer(v => v with { LineNumbers = !v.LineNumbers }))];
        }

        if (tool == _characters)
        {
            return [Item(Loc.Format(AppStrings.Viewer_CharactersItem, _hex.CharactersName), null, () => Later(() => ShowEncodingMenu(_more, characters: true)))];
        }

        if (tool == _bytesPerRow)
        {
            return [Choices(AppStrings.Viewer_BytesTip, ViewerSettings.BytesPerRowChoices, Options.HexBytesPerRow, BytesLabel, n => UpdateViewer(v => v with { HexBytesPerRow = n }))];
        }

        if (tool == _expand)
        {
            var expand = new MenuItem { Header = AppStrings.Viewer_Expand };
            foreach (var item in ExpandItems())
            {
                expand.Items.Add(item);
            }

            return [expand];
        }

        if (tool == _collapse)
        {
            return [MainWindow.EditWithMenuItem(new EditWithItem(AppStrings.Viewer_CollapseAll, "Ctrl+K Ctrl+0", _treeView.CollapseAll, Tree is not null)).Also(m => m.ToggleType = MenuItemToggleType.None)];
        }

        if (tool == _groups)
        {
            return [Choices(AppStrings.Viewer_GroupsTip, ViewerSettings.GroupChoices, Options.HexGroup, GroupLabel, n => UpdateViewer(v => v with { HexGroup = n }))];
        }

        var mode = _imageView.Mode;
        return tool switch
        {
            _ when tool == _fit => [Radio(AppStrings.Viewer_Fit, "F5", mode == ZoomMode.Fit, () => _imageView.SetMode(ZoomMode.Fit))],
            _ when tool == _width => [Radio(AppStrings.Viewer_Width, "F6", mode == ZoomMode.Width, () => _imageView.SetMode(ZoomMode.Width))],
            _ when tool == _actual => [Radio(AppStrings.Viewer_Actual, "F7", mode == ZoomMode.Actual, () => _imageView.SetMode(ZoomMode.Actual))],
            _ when tool == _zoomOut => [Item(AppStrings.Viewer_ZoomOut, "Subtract", () => _imageView.ZoomBy(0.8))],
            _ when tool == _zoomIn => [Item(AppStrings.Viewer_ZoomIn, "Add", () => _imageView.ZoomBy(1.25))],
            _ when tool == _rotateLeft => [Item(AppStrings.Viewer_RotateLeft, "L", () => _imageView.Rotate(-1))],
            _ when tool == _rotateRight => [Item(AppStrings.Viewer_RotateRight, "R", () => _imageView.Rotate(1))],
            _ when tool == _fullScreen => [Item(AppStrings.Viewer_FullScreen, "F11", ToggleFullScreen)],
            _ when tool == _pageZoomOut => [Item(AppStrings.Viewer_ZoomOut, "Ctrl+OemMinus", () => ZoomPage(-1))],
            _ when tool == _pageZoomIn => [Item(AppStrings.Viewer_ZoomIn, "Ctrl+OemPlus", () => ZoomPage(1))],
            _ when tool == _pageZoomBox => [ZoomMenu(_pageZoom, SetPageZoom)],
            _ when tool == _zoomBox => [ZoomMenu(_imageView.Zoom * 100, percent => _imageView.ZoomTo(percent / 100))],
            _ => [],
        };
    }
}

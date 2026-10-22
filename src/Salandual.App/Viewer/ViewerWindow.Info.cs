// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.App/Viewer/ViewerWindow.Info.cs

using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Salandual.App.Controls;
using Salandual.App.Options;
using Salandual.App.Resources;
using Salandual.Core.Listing;
using Salandual.Core.Resources;
using Salandual.Core.Settings;
using Salandual.Core.Viewer;
using Salandual.Imaging;
using Salandual.Platform.Windows.FileSystem;
using AppIcons = Salandual.App.Theme.AppIcons;

using static Salandual.App.Viewer.ToolbarParts;

namespace Salandual.App.Viewer;

internal sealed partial class ViewerWindow
{
    private const double InfoNameWidth = 112;

    private readonly Grid _imageHost = new() { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
    private readonly Border _infoPanel = new() { Width = 300, BorderThickness = new Thickness(1, 0, 0, 0), IsVisible = false };
    private ToggleButton _infoButton = null!;
    private Window? _infoWindow;
    private (string Path, PhotoInfo Info)? _info;
    private string? _infoReading;
    private InfoGroup _infoTab = InfoGroup.Summary;
    private string _infoShown = "";

    internal Task InfoLoading { get; private set; } = Task.CompletedTask;

    internal Control? InfoContent => _infoPanel.IsVisible ? _infoPanel.Child : _infoWindow?.IsVisible == true ? InfoBody(_infoWindow) : null;

    internal Window? InfoWindow => _infoWindow;

    internal bool InfoPanelVisible => _infoPanel.IsVisible;

    internal InfoGroup InfoTab => _infoTab;

    private void BuildInfo()
    {
        Grid.SetColumn(_infoPanel, 1);
        _imageHost.Children.Add(_infoPanel);
        _infoPanel.Res(Border.BackgroundProperty, "Brush.WindowBackground").Res(Border.BorderBrushProperty, "Brush.Border");

        _infoButton = Toggle(Label("Icon.Info", AppStrings.Viewer_Info, "E"), AppStrings.Viewer_InfoTip);
        _infoButton.Click += (_, _) => ToggleInfo();
    }

    internal void ToggleInfo() => UpdateViewer(v => v with { PhotoInfo = !v.PhotoInfo });

    private void UpdateInfo()
    {
        if (_infoButton is null)
        {
            return;
        }

        var o = Options;
        var want = _imageMode && _isImage && o.PhotoInfo && WindowState != WindowState.FullScreen;
        var inWindow = o.PhotoInfoPlace == InfoPlace.Window;
        _infoButton.IsVisible = _imageMode;
        _infoButton.IsChecked = o.PhotoInfo;
        _infoPanel.IsVisible = want && !inWindow;
        if (!(want && inWindow))
        {
            CloseInfoWindow();
        }

        if (!want)
        {
            return;
        }

        if (_info?.Path != _path && _infoReading != _path)
        {
            InfoLoading = ReadInfo(_path);
        }

        var place = _info is { } i && i.Path == _path ? i.Info.Place : null;
        var key = string.Join("|", _path, _info?.Path == _path, _infoTab, inWindow, _imageView.Image is null, _imageView.Size,
            o.MapSite, o.MapCustomName, o.MapCustomUrl, place, ActualThemeVariant);
        if (key == _infoShown && (!inWindow || _infoWindow is not null))
        {
            return;
        }

        _infoShown = key;
        if (inWindow)
        {
            ShowInfoWindow();
        }
        else
        {
            _infoPanel.Child = BuildInfoView(inWindow: false);
        }
    }

    private async Task ReadInfo(string path)
    {
        _infoReading = path;
        PhotoInfo info;
        try
        {
            var extended = WindowsPath.ToExtended(path);
            info = await Task.Run(() => PhotoInfo.Read(extended), _closing.Token);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        finally
        {
            if (_infoReading == path)
            {
                _infoReading = null;
            }
        }

        if (string.Equals(path, _path, StringComparison.Ordinal))
        {
            _info = (path, info);
            if (!info.Groups.ContainsKey(_infoTab))
            {
                _infoTab = InfoGroup.Summary;
            }

            UpdateInfo();
        }
    }

    private Control BuildInfoView(bool inWindow)
    {
        var info = _info is { } i && i.Path == _path ? i.Info : null;
        var groups = Groups(info);
        if (!groups.ContainsKey(_infoTab))
        {
            _infoTab = InfoGroup.Summary;
        }

        var root = new DockPanel { Margin = new Thickness(14, 10, 6, 10) };

        var header = new DockPanel { Margin = new Thickness(0, 0, 8, 8) };
        var close = Tool(AppIcons.Glyph("Icon.Cross", 12, 12, 1.3, "Brush.SecondaryText"), AppStrings.Viewer_InfoClose);
        close.Click += (_, _) => ToggleInfo();
        var copy = Tool(AppIcons.Glyph("Icon.CopyText", 14, 14, 1.3, "Brush.SecondaryText"), AppStrings.Viewer_InfoCopy);
        copy.Click += (_, _) => CopyInfo();
        var placeSwitch = Tool(AppIcons.Glyph(inWindow ? "Icon.InfoToPanel" : "Icon.InfoToWindow", 14, 14, 1.3, "Brush.SecondaryText"), inWindow ? AppStrings.Viewer_InfoToPanel : AppStrings.Viewer_InfoToWindow);
        placeSwitch.Click += (_, _) => UpdateViewer(v => v with { PhotoInfoPlace = inWindow ? InfoPlace.Panel : InfoPlace.Window });
        foreach (var b in inWindow ? new[] { placeSwitch, copy } : new[] { close, placeSwitch, copy })
        {
            b.Padding = new Thickness(6);
            DockPanel.SetDock(b, Dock.Right);
            header.Children.Add(b);
        }

        if (!inWindow)
        {
            header.Children.Add(new TextBlock { Text = AppStrings.Viewer_InfoTitle, FontSize = 13, FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center }
                .Res(TextBlock.ForegroundProperty, "Brush.Text"));
        }

        if (!inWindow)
        {
            DockPanel.SetDock(header, Dock.Top);
            root.Children.Add(header);
        }

        var tabs = new WrapPanel { Margin = new Thickness(0, 0, 8, 8), ItemSpacing = 4, LineSpacing = 4 };
        foreach (var group in groups.Keys)
        {
            var tab = new ToggleButton { Content = TabName(group), IsChecked = group == _infoTab, Focusable = false, Tag = group };
            tab.Classes.Add("vpill");
            tab.Click += (_, _) =>
            {
                _infoTab = group;
                UpdateInfo();
            };
            tabs.Children.Add(tab);
        }

        Control tabRow = tabs;
        if (inWindow)
        {
            header.Children.Clear();
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, -4, 8, 0), Children = { placeSwitch, copy } };
            DockPanel.SetDock(buttons, Dock.Right);
            tabs.Margin = new Thickness(0, 0, 0, 8);
            tabRow = new DockPanel { Children = { buttons, tabs } };
        }

        DockPanel.SetDock(tabRow, Dock.Top);
        root.Children.Add(tabRow);

        var list = new StackPanel { Spacing = 5, Margin = new Thickness(0, 0, 8, 0) };
        var rows = groups[_infoTab];
        var stacked = _infoTab is InfoGroup.All or InfoGroup.Xmp;
        foreach (var row in rows)
        {
            list.Children.Add(Row(row.Name, row.Value, stacked));
        }

        if (info is null && _imageView.Image is not null)
        {
            list.Children.Add(Note(AppStrings.Viewer_InfoReading));
        }
        else if (info is { IsEmpty: true } && _infoTab == InfoGroup.Summary)
        {
            list.Children.Add(Note(AppStrings.Viewer_InfoNone));
        }

        if (_infoTab is InfoGroup.Summary or InfoGroup.Gps && info?.Place is { } place && MapLinks.For(Options, place.Latitude, place.Longitude) is { } uri)
        {
            var link = new Button { Content = Loc.Format(AppStrings.Viewer_ShowOnMap, MapName()), Focusable = false, Cursor = new Cursor(StandardCursorType.Hand), Margin = new Thickness(InfoNameWidth + 8, 2, 0, 0) };
            link.Classes.Add("vlink");
            ToolTip.SetTip(link, uri.Host);
            link.Click += (_, _) => OpenMap(uri);
            list.Children.Add(link);
        }

        if (_infoTab == InfoGroup.All)
        {
            list.Children.Add(Note(AppStrings.Viewer_InfoAllNote));
        }

        root.Children.Add(new ScrollViewer { Content = list, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled });
        return root;

        static Control Row(string name, string value, bool stacked)
        {
            var label = new TextBlock { Text = name, FontSize = 12, TextWrapping = TextWrapping.Wrap }.Res(TextBlock.ForegroundProperty, "Brush.SecondaryText");
            var text = new SelectableTextBlock { Text = value, FontSize = 12, TextWrapping = TextWrapping.Wrap }.Res(TextBlock.ForegroundProperty, "Brush.Text");
            if (stacked)
            {
                return new StackPanel { Spacing = 1, Children = { label, text } };
            }

            label.Width = InfoNameWidth;
            Grid.SetColumn(text, 1);
            return new Grid { ColumnDefinitions = new ColumnDefinitions($"{InfoNameWidth.ToString(CultureInfo.InvariantCulture)},*"), ColumnSpacing = 8, Children = { label, text } };
        }

        static TextBlock Note(string text)
            => new TextBlock { Text = text, FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0) }.Res(TextBlock.ForegroundProperty, "Brush.SecondaryText");
    }

    private Dictionary<InfoGroup, IReadOnlyList<InfoRow>> Groups(PhotoInfo? info)
    {
        var groups = info?.Groups.ToDictionary(g => g.Key, g => g.Value) ?? [];
        var summary = groups.TryGetValue(InfoGroup.Summary, out var rows) ? rows.ToList() : [];

        var cameraBlock = new HashSet<string> { Strings.Info_Taken, Strings.Info_Camera, Strings.Info_Lens, Strings.Info_Exposure, Strings.Info_FocalLength };
        var at = summary.TakeWhile(r => cameraBlock.Contains(r.Name)).Count();
        var mine = new List<InfoRow>();
        if (_imageView.Image is not null)
        {
            var size = _imageView.Size;
            var megapixels = size.Width * (double)size.Height / 1e6;
            var text = Loc.Format(AppStrings.Viewer_ImageSize, size.Width, size.Height);
            mine.Add(new InfoRow(AppStrings.Viewer_InfoSize, megapixels >= 1 ? Loc.Format(AppStrings.Viewer_InfoSizeMp, text, megapixels.ToString("0.#", CultureInfo.CurrentCulture)) : text));
        }

        mine.Add(new InfoRow(AppStrings.Viewer_InfoFile, Loc.Format(AppStrings.Viewer_InfoFileValue, DisplayFormat.From(_settings.Current.Panels, CultureInfo.CurrentCulture).Size(_imageLength), FormatName())));
        summary.InsertRange(at, mine);
        groups[InfoGroup.Summary] = summary;
        return groups.OrderBy(g => g.Key).ToDictionary(g => g.Key, g => g.Value);
    }

    private string FormatName() => ImageFormats.Name(_path) ?? Loc.Format(AppStrings.Viewer_CameraRaw, Path.GetExtension(_path).TrimStart('.').ToUpperInvariant());

    internal static string TabName(InfoGroup group) => group switch
    {
        InfoGroup.Exif => AppStrings.Viewer_InfoTab_Exif,
        InfoGroup.Iptc => AppStrings.Viewer_InfoTab_Iptc,
        InfoGroup.Xmp => AppStrings.Viewer_InfoTab_Xmp,
        InfoGroup.Gps => AppStrings.Viewer_InfoTab_Gps,
        InfoGroup.Colour => AppStrings.Viewer_InfoTab_Colour,
        InfoGroup.All => AppStrings.Viewer_InfoTab_All,
        _ => AppStrings.Viewer_InfoTab_Summary,
    };

    private string MapName() => MapLinks.Effective(Options) switch
    {
        MapSite.GoogleMaps => AppStrings.Map_GoogleMaps,
        MapSite.Custom => string.IsNullOrWhiteSpace(Options.MapCustomName) ? AppStrings.Map_YourSite : Options.MapCustomName,
        _ => AppStrings.Map_OpenStreetMap,
    };

    private void OpenMap(Uri uri)
    {
        if (_request.OpenWeb?.Invoke(uri) != true)
        {
            ShowStatusNote(AppStrings.Viewer_MapFailed);
        }
    }

    internal string InfoText()
    {
        var info = _info is { } i && i.Path == _path ? i.Info : null;
        return Groups(info).TryGetValue(_infoTab, out var rows) ? string.Join("\r\n", rows.Select(r => $"{r.Name}\t{r.Value}")) : "";
    }

    private void CopyInfo()
    {
        if (_request.Clipboard is { } clipboard && InfoText() is { Length: > 0 } text)
        {
            ShowStatusNote(clipboard.SetText(text) ? AppStrings.Viewer_InfoCopied : AppStrings.Viewer_ClipboardBusy);
        }
    }

    internal void SelectInfoTab(InfoGroup group)
    {
        _infoTab = group;
        UpdateInfo();
    }

    private void ShowInfoWindow()
    {
        if (_infoWindow is null)
        {
            var window = new Window
            {
                Width = 380,
                Height = 540,
                MinWidth = 280,
                MinHeight = 220,
                ShowInTaskbar = false,
                WindowStartupLocation = WindowStartupLocation.Manual,
            };
            window.Res(BackgroundProperty, "Brush.WindowBackground");
            window.Styles.Add(ViewerStyles.Create());
            var title = new TextBlock { FontSize = 12, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(12, 0, 0, 0) }
                .Res(TextBlock.ForegroundProperty, "Brush.Text");
            window.Content = TitleBarParts.ForWindow(window, title, new ContentControl());
            window.Tag = title;
            window.KeyDown += (_, e) =>
            {
                if (e.KeyModifiers == KeyModifiers.None && e.Key is Key.E or Key.Escape)
                {
                    e.Handled = true;
                    ToggleInfo();
                }
            };
            window.Closed += (_, _) =>
            {
                if (ReferenceEquals(_infoWindow, window))
                {
                    _infoWindow = null;
                    if (Options.PhotoInfo)
                    {
                        UpdateViewer(v => v with { PhotoInfo = false });
                    }
                }
            };

            var scale = DesktopScaling;
            var x = Position.X + (int)((FrameSize?.Width ?? Width) * scale) - (int)(window.Width * scale) - (int)(24 * scale);
            var y = Position.Y + (int)(80 * scale);
            if (Screens.ScreenFromWindow(this)?.WorkingArea is { } area)
            {
                x = Math.Clamp(x, area.X, Math.Max(area.X, area.Right - (int)(window.Width * scale)));
                y = Math.Clamp(y, area.Y, Math.Max(area.Y, area.Bottom - (int)(window.Height * scale)));
            }

            window.Position = new PixelPoint(x, y);
            _infoWindow = window;
            window.Show(this);
        }

        if (_infoWindow.Tag is TextBlock caption)
        {
            caption.Text = Loc.Format(AppStrings.Viewer_InfoWindowTitle, WindowsPath.GetName(_path));
            _infoWindow.Title = caption.Text;
        }

        if (InfoHolder(_infoWindow) is { } holder)
        {
            holder.Content = BuildInfoView(inWindow: true);
        }
    }

    private void CloseInfoWindow()
    {
        if (_infoWindow is { } window)
        {
            _infoWindow = null;
            window.Close();
        }
    }

    private static ContentControl? InfoHolder(Window window)
        => window.Content is Panel panel ? panel.Children.OfType<ContentControl>().FirstOrDefault() : null;

    private static Control? InfoBody(Window window) => InfoHolder(window)?.Content as Control;
}

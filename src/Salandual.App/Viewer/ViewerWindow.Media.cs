// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.App/Viewer/ViewerWindow.Media.cs

using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Salandual.App.Controls;
using Salandual.App.Options;
using Salandual.App.Resources;
using Salandual.Core.Listing;
using Salandual.Core.Resources;
using Salandual.Core.Settings;
using Salandual.Imaging;
using Salandual.Platform.Windows.FileSystem;
using AppIcons = Salandual.App.Theme.AppIcons;

using static Salandual.App.Viewer.ToolbarParts;

namespace Salandual.App.Viewer;

internal sealed partial class ViewerWindow
{
    private const double CoverSide = 180;

    private const int VideoFrameSide = 640;

    private static readonly IBrush CoverInk = new SolidColorBrush(Color.FromRgb(0x9F, 0xC1, 0xE0));

    private readonly ScrollViewer _mediaHost = new() { HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Focusable = true };
    private ToggleButton _mediaModeButton = null!;
    private (string Path, MediaReport Report)? _media;
    private Bitmap? _cover;
    private bool _coverIsFrame;
    private string? _frameLoading;
    private bool _isMedia;
    private bool _mediaMode;
    private long _mediaLength;
    private Action<double>? _placeMedia;

    internal bool IsMedia => _mediaMode;

    internal MediaReport? Media => _media is { } m && m.Path == _path ? m.Report : null;

    internal ScrollViewer MediaHost => _mediaHost;

    internal CardGrid? CardsGrid { get; private set; }

    private ToggleButton BuildMediaModeButton()
    {
        _mediaHost.SizeChanged += (_, e) => _placeMedia?.Invoke(e.NewSize.Width);
        _mediaModeButton = Mode(AppStrings.Viewer_Info, null);
        _mediaModeButton.Click += (_, _) => ShowMedia();
        return _mediaModeButton;
    }

    private async Task LoadMedia(string path)
    {
        CancelImage();
        var cancel = CancellationTokenSource.CreateLinkedTokenSource(_closing.Token);
        _imageCancel = cancel;
        var token = cancel.Token;
        DropDocument();
        _message.IsVisible = false;
        _content.IsVisible = true;
        _mediaHost.Content = Placeholder(AppStrings.Viewer_MediaReading);
        ShowMediaMode();

        MediaReport report;
        try
        {
            var extended = WindowsPath.ToExtended(path);
            _mediaLength = new FileInfo(extended).Length;
            report = await Task.Run(() => MediaReport.Read(extended), token);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (MediaInfoUnavailableException e)
        {
            MediaFailed(Loc.Format(AppStrings.Viewer_MediaUnavailable, e.Message));
            return;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            MediaFailed(Loc.Format(AppStrings.Viewer_CantOpen, WindowsPath.GetName(path), e.Message));
            return;
        }

        if (token.IsCancellationRequested || !string.Equals(path, _path, StringComparison.Ordinal))
        {
            return;
        }

        if (report.IsEmpty)
        {
            MediaFailed(Loc.Format(AppStrings.Viewer_MediaFailed, WindowsPath.GetName(path)));
            return;
        }

        _cover?.Dispose();
        _cover = null;
        if (report.Cover is { } bytes)
        {
            try
            {
                using var stream = new MemoryStream(bytes);
                _cover = await Task.Run(() => Bitmap.DecodeToWidth(stream, (int)(CoverSide * 2)), token);
            }
            catch (Exception e) when (e is not OperationCanceledException and not OutOfMemoryException)
            {
            }
        }

        if (token.IsCancellationRequested || !string.Equals(path, _path, StringComparison.Ordinal))
        {
            return;
        }

        _coverIsFrame = false;
        _media = (path, report);
        _mediaHost.Content = BuildMediaView(report);
        UpdateToolbar();
        UpdateStatus();
        UpdateVideoFrame();

        void MediaFailed(string text)
        {
            if (string.Equals(path, _path, StringComparison.Ordinal))
            {
                ShowMessage(text);
                UpdateToolbar();
            }
        }
    }

    private void UpdateVideoFrame()
    {
        if (Media is not { HasVideo: true } report || (_cover is not null && !_coverIsFrame))
        {
            return;
        }

        if (!Options.VideoFrame)
        {
            if (_coverIsFrame)
            {
                _cover?.Dispose();
                _cover = null;
                _coverIsFrame = false;
                RebuildMediaView(report);
            }

            return;
        }

        if (_cover is null && _frameLoading != _path && _request.Thumbnail is { } thumbnail)
        {
            VideoFrameLoading = LoadVideoFrame(_path, report, thumbnail);
        }
    }

    internal Task VideoFrameLoading { get; private set; } = Task.CompletedTask;

    private async Task LoadVideoFrame(string path, MediaReport report, Func<string, int, Task<Salandual.Platform.Windows.Shell.IconPixels?>> thumbnail)
    {
        _frameLoading = path;
        RebuildMediaView(report);
        Salandual.Platform.Windows.Shell.IconPixels? frame;
        try
        {
            frame = await thumbnail(WindowsPath.ToExtended(path), VideoFrameSide);
        }
        finally
        {
            if (_frameLoading == path)
            {
                _frameLoading = null;
            }
        }

        if (!ReferenceEquals(Media, report))
        {
            return;
        }

        if (frame is not null && Options.VideoFrame && _cover is null)
        {
            _cover = Panels.IconCache.ToBitmap(frame);
            _coverIsFrame = true;
        }

        RebuildMediaView(report);
    }

    private void RebuildMediaView(MediaReport report)
    {
        var offset = _mediaHost.Offset;
        _mediaHost.Content = BuildMediaView(report);
        _mediaHost.Offset = offset;
    }

    private void ShowMediaMode()
    {
        if (_findBar.IsVisible)
        {
            CloseFind();
        }

        _mediaMode = true;
        _imageMode = false;
        _treeMode = false;
        _hexMode = false;
        _content.Content = _mediaHost;
        _vertical.IsVisible = false;
        _horizontal.IsVisible = false;
        UpdateToolbar();
        UpdateStatus();
        Dispatcher.UIThread.Post(() => _mediaHost.Focus(), DispatcherPriority.Input);
    }

    internal void ShowMedia()
    {
        if (!_isMedia)
        {
            return;
        }

        if (Media is null)
        {
            _ = LoadAsync(_path, ViewerKind.Media);
            return;
        }

        _message.IsVisible = false;
        _content.IsVisible = true;
        ShowMediaMode();
    }

    private Control BuildMediaView(MediaReport report)
    {
        var cover = new Border
        {
            Width = CoverSide,
            Height = CoverSide,
            CornerRadius = new CornerRadius(6),
            BorderThickness = new Thickness(1),
            ClipToBounds = true,
            VerticalAlignment = VerticalAlignment.Top,
            HorizontalAlignment = HorizontalAlignment.Left,
            Background = new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
                EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
                GradientStops = { new GradientStop(Color.FromRgb(0x29, 0x42, 0x5C), 0), new GradientStop(Color.FromRgb(0x11, 0x20, 0x2F), 1) },
            },
            Child = _cover is { } bitmap ? new Image { Source = bitmap, Stretch = _coverIsFrame ? Stretch.UniformToFill : Stretch.Uniform }
                : _frameLoading == _path && report.HasVideo ? new BusyRing { Width = 32, Height = 32, Brush = CoverInk }
                : AppIcons.Large(report.HasVideo ? "Icon.MediaVideo" : "Icon.MediaAudio", 56, 1.4, CoverInk),
        }.Res(Border.BorderBrushProperty, "Brush.Border");
        if (_cover is null && cover.Child is Control glyph)
        {
            glyph.HorizontalAlignment = HorizontalAlignment.Center;
            glyph.VerticalAlignment = VerticalAlignment.Center;
        }

        var title = new SelectableTextBlock { Text = report.Title ?? Path.GetFileNameWithoutExtension(WindowsPath.GetName(_path)), FontSize = 20, LineHeight = 28, FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap }
            .Res(TextBlock.ForegroundProperty, "Brush.Text");
        var subtitle = new SelectableTextBlock { Text = MediaSubtitle(report), FontSize = 13, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 14) }
            .Res(TextBlock.ForegroundProperty, "Brush.SecondaryText");

        var open = ActionButton(AppStrings.Viewer_MediaOpen, Options.MediaEnter == MediaEnter.OpenInDefaultApp ? "Enter" : null, AppStrings.Viewer_MediaOpenTip);
        open.Classes.Add("accent");
        open.IsEnabled = _request.OpenFile is not null;
        open.Click += (_, _) => _ = OpenInProgramAsync();
        var copy = ActionButton(AppStrings.Viewer_MediaCopy, "Ctrl+C", AppStrings.Viewer_MediaCopyTip);
        copy.Click += (_, _) => CopyMediaText();
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(0, 0, 0, 14), Children = { open, copy } };

        var cards = new CardGrid();
        var lanes = report.Cards.Any(c => c.Kind == MediaCardKind.Video);
        foreach (var card in report.Cards)
        {
            var control = Card(card);
            CardGrid.SetLane(control, !lanes ? 0 : card.Kind switch { MediaCardKind.Video => 0, MediaCardKind.Audio => 1, _ => 2 });
            cards.Children.Add(control);
        }

        CardsGrid = cards;

        var details = new StackPanel { Children = { title, subtitle, actions, cards } };
        var layout = new Grid { Margin = new Thickness(24, 20), ColumnSpacing = 24, RowSpacing = 16 };
        layout.Children.Add(cover);
        layout.Children.Add(details);

        void Place(double width)
        {
            var beside = width >= CoverSide + 24 + 2 * 240 + 12 + 48;
            layout.ColumnDefinitions = beside ? new ColumnDefinitions($"{CoverSide.ToString(CultureInfo.InvariantCulture)},*") : new ColumnDefinitions("*");
            layout.RowDefinitions = beside ? new RowDefinitions("Auto") : new RowDefinitions("Auto,Auto");
            Grid.SetColumn(details, beside ? 1 : 0);
            Grid.SetRow(details, beside ? 0 : 1);
        }

        Place(_mediaHost.Bounds.Width > 0 ? _mediaHost.Bounds.Width : ClientSize.Width);
        _placeMedia = Place;
        return layout;
    }

    private static Button ActionButton(string text, string? key, string tip)
    {
        var content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center } } };
        if (key is not null)
        {
            content.Children.Add(new TextBlock { Text = key, FontSize = 11, Opacity = 0.75, VerticalAlignment = VerticalAlignment.Center });
        }

        var button = new Button { Content = content, MinHeight = 30, Padding = new Thickness(12, 4), FontSize = 13 };
        ToolTip.SetTip(button, tip);
        return button;
    }

    private static Control Card(MediaCard card)
    {
        var header = new WrapPanel { ItemSpacing = 8, LineSpacing = 4, Margin = new Thickness(0, 0, 0, 6) };
        header.Children.Add(new TextBlock { Text = card.Title, FontSize = 13, FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center }.Res(TextBlock.ForegroundProperty, "Brush.Text"));
        foreach (var tag in card.Tags)
        {
            header.Children.Add(new Border
            {
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(999),
                Padding = new Thickness(8, 0),
                VerticalAlignment = VerticalAlignment.Center,
                Child = new TextBlock { Text = tag, FontSize = 11 }.Res(TextBlock.ForegroundProperty, "Brush.SecondaryText"),
            }.Res(Border.BorderBrushProperty, "Brush.BorderStrong"));
        }

        var rows = new Grid { ColumnDefinitions = new ColumnDefinitions("90,*"), ColumnSpacing = 8, RowSpacing = 3 };
        for (var i = 0; i < card.Rows.Count; i++)
        {
            rows.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            var name = new TextBlock { Text = card.Rows[i].Name, FontSize = 12, TextWrapping = TextWrapping.Wrap }.Res(TextBlock.ForegroundProperty, "Brush.SecondaryText");
            var value = new SelectableTextBlock { Text = card.Rows[i].Value, FontSize = 12, TextWrapping = TextWrapping.Wrap }.Res(TextBlock.ForegroundProperty, "Brush.Text");
            Grid.SetRow(name, i);
            Grid.SetRow(value, i);
            Grid.SetColumn(value, 1);
            rows.Children.Add(name);
            rows.Children.Add(value);
        }

        return new Border
        {
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(12, 10),
            Child = new StackPanel { Children = { header, rows } },
        }.Res(Border.BorderBrushProperty, "Brush.Border").Res(Border.BackgroundProperty, "Brush.PanelBackground");
    }

    private static TextBlock Placeholder(string text)
        => new TextBlock { Text = text, Margin = new Thickness(24, 20), FontSize = 13 }.Res(TextBlock.ForegroundProperty, "Brush.SecondaryText");

    private string MediaSubtitle(MediaReport report)
    {
        var parts = new List<string> { report.Format };
        if (report.Duration is { } duration)
        {
            parts.Add(MediaReport.LongDuration(duration));
        }

        parts.Add(DisplayFormat.From(_settings.Current.Panels, CultureInfo.CurrentCulture).Size(_mediaLength));
        if (report.BitRate is { } rate)
        {
            parts.Add(MediaReport.BitRateText(rate));
        }

        return string.Join(" · ", parts.Where(p => p.Length > 0));
    }

    private void UpdateMediaStatus()
    {
        var left = new List<Border>();
        var right = new List<Border>();
        if (Media is { } report)
        {
            Status(left, string.Join(" · ", new[] { report.Format }.Concat(report.Codecs).Where(p => p.Length > 0)), strong: true);
            if (report.Duration is { } duration)
            {
                Status(left, MediaReport.ShortDuration(duration));
            }

            Status(right, AppStrings.Viewer_MediaReadBy);
            Status(right, DisplayFormat.From(_settings.Current.Panels, CultureInfo.CurrentCulture).Size(_mediaLength));
        }

        ShowStatus(left, right);
    }

    private bool MediaKey(Key key, KeyModifiers mods)
    {
        var plain = mods == KeyModifiers.None;
        var step = 40.0;
        var page = Math.Max(step, _mediaHost.Viewport.Height - step);
        switch (key)
        {
            case Key.Enter when plain:
                if (Options.MediaEnter == MediaEnter.OpenInDefaultApp)
                {
                    _ = OpenInProgramAsync();
                }

                return true;
            case Key.C when mods == KeyModifiers.Control: CopyMediaText(); return true;
            case Key.Up when plain: ScrollMedia(-step); return true;
            case Key.Down when plain: ScrollMedia(step); return true;
            case Key.PageUp when plain: ScrollMedia(-page); return true;
            case Key.PageDown when plain: ScrollMedia(page); return true;
            case Key.Home when plain || mods == KeyModifiers.Control: _mediaHost.Offset = default; return true;
            case Key.End when plain || mods == KeyModifiers.Control: _mediaHost.Offset = new Vector(0, double.MaxValue); return true;
            case Key.F or Key.G or Key.A or Key.S when mods == KeyModifiers.Control: return true;
            case Key.F2 or Key.F3 or Key.F7 or Key.F8 when plain || mods == KeyModifiers.Shift: return true;
            default: return false;
        }
    }

    private void ScrollMedia(double by) => _mediaHost.Offset = new Vector(0, Math.Max(0, _mediaHost.Offset.Y + by));

    internal async Task OpenInProgramAsync()
    {
        if (_request.OpenFile is { } open)
        {
            await open(_path);
        }
    }

    internal void CopyMediaText()
    {
        if (_request.Clipboard is { } clipboard && Media is { } report)
        {
            ShowStatusNote(clipboard.SetText(report.Text.Replace("\n", "\r\n", StringComparison.Ordinal)) ? AppStrings.Viewer_MediaCopied : AppStrings.Viewer_ClipboardBusy);
        }
    }
}

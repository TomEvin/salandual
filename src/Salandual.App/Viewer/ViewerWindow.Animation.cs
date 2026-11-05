// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.App/Viewer/ViewerWindow.Animation.cs

using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using Salandual.App.Resources;
using Salandual.Core.Resources;
using Salandual.Core.Settings;
using Salandual.Imaging;
using Salandual.Platform.Windows.FileSystem;

using static Salandual.App.Viewer.ToolbarParts;

namespace Salandual.App.Viewer;

internal sealed partial class ViewerWindow
{
    private readonly DispatcherTimer _frameTimer = new();
    private AnimatedImage? _animation;
    private WriteableBitmap? _frameBitmap;
    private int _frame;
    private int _round;
    private bool _playing;
    private long _frameStatusAt;
    private Border _animationSeparator = null!;
    private Button _play = null!;
    private Button _previousFrame = null!;
    private Button _nextFrame = null!;

    internal static readonly Checkerboard CheckerLight = new(Color.FromRgb(0xFF, 0xFF, 0xFF), Color.FromRgb(0xCC, 0xCC, 0xCC));
    internal static readonly Checkerboard CheckerDark = new(Color.FromRgb(0x2A, 0x2A, 0x2A), Color.FromRgb(0x3A, 0x3A, 0x3A));

    private static ImageBrush? _swatchLight;
    private static ImageBrush? _swatchDark;

    internal AnimatedImage? Animated => _animation;

    internal int FrameIndex => _frame;

    internal bool IsPlaying => _playing;

    internal Task AnimationLoading { get; private set; } = Task.CompletedTask;

    internal Button PlayButton => _play;

    private Control[] BuildAnimationTools()
    {
        _animationSeparator = Separator();
        _play = Tool(Glyph("Icon.Pause"), AppStrings.Viewer_PauseTip);
        _play.Click += (_, _) => TogglePlay();
        _previousFrame = Tool(Glyph("Icon.FramePrevious"), AppStrings.Viewer_PreviousFrameTip);
        _previousFrame.Click += (_, _) => StepFrame(-1);
        _nextFrame = Tool(Glyph("Icon.FrameNext"), AppStrings.Viewer_NextFrameTip);
        _nextFrame.Click += (_, _) => StepFrame(1);
        _frameTimer.Tick += OnFrameTimer;
        return [_animationSeparator, _play, _previousFrame, _nextFrame];
    }

    private void UpdateAnimationTools()
    {
        var shown = _imageMode && _animation is not null;
        _animationSeparator.IsVisible = _play.IsVisible = _previousFrame.IsVisible = _nextFrame.IsVisible = shown;
        _play.Content = Glyph(_playing ? "Icon.Pause" : "Icon.Play");
        ToolTip.SetTip(_play, _playing ? AppStrings.Viewer_PauseTip : AppStrings.Viewer_PlayTip);
    }

    private async Task LoadAnimation(string path, DecodedImage still, CancellationToken token)
    {
        if (!Animation.MayMove(path))
        {
            return;
        }

        var target = DecodeTarget();
        var extended = WindowsPath.ToExtended(path);
        AnimatedImage? animation;
        try
        {
            animation = await Task.Run(() => Animation.TryDecode(extended, target.Width, target.Height, token), token);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        if (animation is null || token.IsCancellationRequested || !ReferenceEquals(_imageView.Image, still))
        {
            return;
        }

        var first = animation.Frames[0];
        _animation = animation;
        (_frame, _round) = (0, 0);
        _frameBitmap = new WriteableBitmap(new PixelSize(first.Width, first.Height), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Premul);
        Draw(first);
        _imageView.Replace(first, _frameBitmap);
        _playing = true;
        Schedule();
        UpdateToolbar();
        UpdateStatus();
    }

    private void StopAnimation()
    {
        _frameTimer.Stop();
        (_animation, _frameBitmap, _playing) = (null, null, false);
    }

    private void Draw(DecodedImage frame)
    {
        if (_frameBitmap is not { } bitmap)
        {
            return;
        }

        using var buffer = bitmap.Lock();
        var row = frame.Width * 4;
        for (var y = 0; y < frame.Height; y++)
        {
            Marshal.Copy(frame.Pixels, y * row, buffer.Address + (y * buffer.RowBytes), row);
        }
    }

    private void Schedule()
    {
        _frameTimer.Stop();
        if (_playing && _animation is { } animation)
        {
            _frameTimer.Interval = TimeSpan.FromMilliseconds(animation.Delays[_frame]);
            _frameTimer.Start();
        }
    }

    private void OnFrameTimer(object? sender, EventArgs e)
    {
        _frameTimer.Stop();
        if (_animation is not { } animation || !_playing)
        {
            return;
        }

        var next = _frame + 1;
        if (next >= animation.Frames.Count)
        {
            _round++;
            if (animation.Plays > 0 && _round >= animation.Plays)
            {
                _playing = false;
                UpdateAnimationTools();
                UpdateStatus();
                return;
            }

            next = 0;
        }

        ShowFrameAt(next);
        Schedule();
        if (Environment.TickCount64 - _frameStatusAt >= 200)
        {
            UpdateStatus();
        }
    }

    private void ShowFrameAt(int index)
    {
        if (_animation is not { } animation)
        {
            return;
        }

        _frame = index;
        if (_imageMode)
        {
            Draw(animation.Frames[index]);
            _imageView.ShowFrame(animation.Frames[index]);
        }
    }

    internal void TogglePlay()
    {
        if (_animation is not { } animation)
        {
            return;
        }

        _playing = !_playing;
        if (_playing && animation.Plays > 0 && _round >= animation.Plays)
        {
            _round = 0;
            ShowFrameAt(0);
        }

        Schedule();
        UpdateAnimationTools();
        UpdateStatus();
    }

    internal void StepFrame(int by)
    {
        if (_animation is not { } animation)
        {
            return;
        }

        _playing = false;
        _frameTimer.Stop();
        var count = animation.Frames.Count;
        ShowFrameAt((((_frame + by) % count) + count) % count);
        UpdateAnimationTools();
        UpdateStatus();
    }

    private void AnimationStatus(List<Border> left)
    {
        if (_animation is { } animation)
        {
            _frameStatusAt = Environment.TickCount64;
            Status(left, Loc.Format(AppStrings.Viewer_FrameStatus, _frame + 1, animation.Frames.Count, animation.Delays[_frame]));
        }
    }

    private IBrush? TransparentBrush()
    {
        var o = Options;
        return o.ImageTransparent switch
        {
            TransparentParts.White => Brushes.White,
            TransparentParts.Black => Brushes.Black,
            TransparentParts.Custom when ColorMath.TryParseHex(o.TransparentCustom, out var c) => new SolidColorBrush(Color.FromRgb(c.R, c.G, c.B)),
            TransparentParts.Custom => Brushes.White,
            _ => null,
        };
    }

    private Checkerboard? TransparentChecker() => Options.ImageTransparent switch
    {
        TransparentParts.CheckerLight => CheckerLight,
        TransparentParts.CheckerDark => CheckerDark,
        _ => null,
    };

    internal static ImageBrush CheckerSwatch(bool dark)
    {
        ref var brush = ref dark ? ref _swatchDark : ref _swatchLight;
        if (brush is null)
        {
            var colours = dark ? CheckerDark : CheckerLight;
            var (a, b) = ((int)colours.Light.ToUInt32(), (int)colours.Dark.ToUInt32());
            const int Side = 40, Square = 10;
            var bitmap = new WriteableBitmap(new PixelSize(Side, Side), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Premul);
            using (var buffer = bitmap.Lock())
            {
                var row = new int[Side];
                for (var y = 0; y < Side; y++)
                {
                    for (var x = 0; x < Side; x++)
                    {
                        row[x] = (((x / Square) + (y / Square)) & 1) == 1 ? b : a;
                    }

                    Marshal.Copy(row, 0, buffer.Address + (y * buffer.RowBytes), Side);
                }
            }

            brush = new ImageBrush(bitmap) { Stretch = Stretch.Fill };
        }

        return brush;
    }

    internal static string TransparentLabel(TransparentParts parts) => parts switch
    {
        TransparentParts.CheckerDark => AppStrings.Viewer_Transparent_CheckerDark,
        TransparentParts.Canvas => AppStrings.Viewer_Transparent_Canvas,
        TransparentParts.White => AppStrings.Viewer_Transparent_White,
        TransparentParts.Black => AppStrings.Viewer_Transparent_Black,
        TransparentParts.Custom => AppStrings.Common_Custom,
        _ => AppStrings.Viewer_Transparent_CheckerLight,
    };
}

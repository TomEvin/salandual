// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.App/Theme/BackgroundImageService.cs

using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Salandual.App.Resources;
using Salandual.Core.Settings;

namespace Salandual.App.Theme;

public sealed class BackgroundImageService
{
    private const int MaxWidth = 3840;

    private int _version;
    private Bitmap? _tile;

    public BackgroundImageSettings Settings { get; private set; } = new();

    public Bitmap? Image { get; private set; }

    public string? Error { get; private set; }

    public bool IsLoading { get; private set; }

    public event EventHandler? Changed;

    public void Apply(BackgroundImageSettings settings)
    {
        var reload = !string.Equals(settings.Path, Settings.Path, StringComparison.OrdinalIgnoreCase) || settings.Blur != Settings.Blur;
        Settings = settings;
        if (reload)
        {
            Load(settings.Path, settings.Blur);
        }
        else
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    private void Load(string? path, bool blur)
    {
        var version = ++_version;
        SetImage(null, null);
        if (path is null)
        {
            IsLoading = false;
            Changed?.Invoke(this, EventArgs.Empty);
            return;
        }

        IsLoading = true;
        Changed?.Invoke(this, EventArgs.Empty);
        _ = Task.Run(() =>
        {
            Bitmap? image = null;
            string? error = null;
            try
            {
                image = Decode(path, blur);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException or InvalidOperationException)
            {
                error = ex is FileNotFoundException or DirectoryNotFoundException
                    ? AppStrings.Bg_NotFound
                    : AppStrings.Bg_Unsupported;
            }

            Dispatcher.UIThread.Post(() =>
            {
                if (version != _version)
                {
                    image?.Dispose();
                    return;
                }

                IsLoading = false;
                SetImage(image, error);
                Changed?.Invoke(this, EventArgs.Empty);
            });
        });
    }

    public static Bitmap Decode(string path, bool blur)
    {
        using var stream = File.OpenRead(Platform.Windows.FileSystem.WindowsPath.ToExtended(path));
        var full = new Bitmap(stream);
        if (full.PixelSize.Width > MaxWidth)
        {
            var original = full;
            var height = Math.Max(1, (int)Math.Round(full.PixelSize.Height * (double)MaxWidth / full.PixelSize.Width));
            full = original.CreateScaledBitmap(new PixelSize(MaxWidth, height), BitmapInterpolationMode.HighQuality);
            original.Dispose();
        }

        if (!blur)
        {
            return full;
        }

        using (full)
        {
            return Blurred(full);
        }
    }

    private static Bitmap Blurred(Bitmap full)
    {
        var size = full.PixelSize;
        PixelSize Part(int divisor) => new(Math.Max(1, size.Width / divisor), Math.Max(1, size.Height / divisor));
        using var small = full.CreateScaledBitmap(Part(24), BitmapInterpolationMode.HighQuality);
        using var middle = small.CreateScaledBitmap(Part(6), BitmapInterpolationMode.HighQuality);
        using var large = middle.CreateScaledBitmap(Part(2), BitmapInterpolationMode.HighQuality);
        return large.CreateScaledBitmap(size, BitmapInterpolationMode.HighQuality);
    }

    private void SetImage(Bitmap? image, string? error)
    {
        if (_tile is not null && !ReferenceEquals(_tile, Image))
        {
            _tile.Dispose();
        }

        Image?.Dispose();
        _tile = null;
        Image = image;
        Error = error;
    }

    public void Draw(DrawingContext context, Rect area, Rect clip)
    {
        if (Image is not { } image || area.Width <= 0 || area.Height <= 0 || clip.Width <= 0 || clip.Height <= 0)
        {
            return;
        }

        using (context.PushClip(clip))
        using (context.PushOpacity(Settings.Opacity))
        {
            if (Settings.Fit == BackgroundFit.Tile)
            {
                DrawTiles(context, _tile ??= Tile(image), area, clip);
            }
            else
            {
                context.DrawImage(image, new Rect(image.Size), Place(image.Size, area, Settings.Fit, Settings.Position));
            }
        }
    }

    private static void DrawTiles(DrawingContext context, Bitmap tile, Rect area, Rect clip)
    {
        var (w, h) = (tile.Size.Width, tile.Size.Height);
        if (w < 1 || h < 1)
        {
            return;
        }

        var left = area.X + (Math.Floor((clip.X - area.X) / w) * w);
        var top = area.Y + (Math.Floor((clip.Y - area.Y) / h) * h);
        var source = new Rect(tile.Size);
        for (var y = top; y < clip.Bottom; y += h)
        {
            for (var x = left; x < clip.Right; x += w)
            {
                context.DrawImage(tile, source, new Rect(x, y, w, h));
            }
        }
    }

    private static Bitmap Tile(Bitmap image)
    {
        const int Least = 256;
        var size = image.PixelSize;
        if (size.Width >= Least && size.Height >= Least)
        {
            return image;
        }

        var (across, down) = ((int)Math.Ceiling(Least / (double)size.Width), (int)Math.Ceiling(Least / (double)size.Height));
        var tile = new RenderTargetBitmap(new PixelSize(size.Width * across, size.Height * down));
        using (var context = tile.CreateDrawingContext())
        {
            var source = new Rect(image.Size);
            for (var y = 0; y < down; y++)
            {
                for (var x = 0; x < across; x++)
                {
                    context.DrawImage(image, source, new Rect(x * size.Width, y * size.Height, size.Width, size.Height));
                }
            }
        }

        return tile;
    }

    public static Rect Place(Size image, Rect area, BackgroundFit fit, BackgroundPosition position)
    {
        var size = fit switch
        {
            BackgroundFit.Stretch => area.Size,
            BackgroundFit.Fill => image * Math.Max(area.Width / image.Width, area.Height / image.Height),
            BackgroundFit.Fit => image * Math.Min(area.Width / image.Width, area.Height / image.Height),
            _ => image,
        };

        var (fx, fy) = position switch
        {
            BackgroundPosition.TopLeft => (0.0, 0.0),
            BackgroundPosition.TopRight => (1.0, 0.0),
            BackgroundPosition.BottomLeft => (0.0, 1.0),
            BackgroundPosition.BottomRight => (1.0, 1.0),
            _ => (0.5, 0.5),
        };

        return new Rect(area.X + ((area.Width - size.Width) * fx), area.Y + ((area.Height - size.Height) * fy), size.Width, size.Height);
    }
}

// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.App/Panels/FolderLooks.cs

using System.Globalization;
using System.Security.Cryptography;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Media.Immutable;
using Salandual.App.Theme;
using Salandual.Core.Panels;
using Salandual.Core.Settings;

namespace Salandual.App.Panels;

public sealed class FolderLooks(string dataDirectory)
{
    private const int ImagePixels = 64;

    private readonly Dictionary<string, Bitmap?> _images = new(StringComparer.OrdinalIgnoreCase);
    private IReadOnlyDictionary<string, FolderLook> _folders = new Dictionary<string, FolderLook>();

    public string ImageFolder { get; } = Path.Combine(dataDirectory, FolderMarks.ImageFolder);

    public bool IsEmpty => _folders.Count == 0;

    public void Set(IReadOnlyDictionary<string, FolderLook> folders) => _folders = folders;

    public FolderLook? Of(string path) => _folders.Count == 0 ? null : _folders.GetValueOrDefault(HighlightSettings.Key(path));

    public Bitmap? Image(string name)
    {
        if (_images.TryGetValue(name, out var image))
        {
            return image;
        }

        image = null;
        try
        {
            var file = Path.Combine(ImageFolder, name);
            if (FolderMarks.IsImageName(name) && new FileInfo(file) is { Exists: true, Length: > 0 and <= FolderMarks.MaxImageBytes })
            {
                using var stream = File.OpenRead(file);
                image = Bitmap.DecodeToWidth(stream, ImagePixels);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException or NotSupportedException)
        {
            image = null;
        }

        _images[name] = image;
        return image;
    }

    public string? CopyIn(string file)
    {
        var ext = Path.GetExtension(file).ToLowerInvariant();
        if (!FolderMarks.ImageExtensions.Contains(ext))
        {
            return null;
        }

        try
        {
            var info = new FileInfo(file);
            if (!info.Exists || info.Length is 0 or > FolderMarks.MaxImageBytes)
            {
                return null;
            }

            var bytes = File.ReadAllBytes(file);
            using (var check = new MemoryStream(bytes))
            {
                using var _ = Bitmap.DecodeToWidth(check, ImagePixels);
            }

            var name = Convert.ToHexString(SHA256.HashData(bytes).AsSpan(0, 12)).ToLowerInvariant() + ext;
            Directory.CreateDirectory(ImageFolder);
            var target = Path.Combine(ImageFolder, name);
            if (!File.Exists(target))
            {
                File.WriteAllBytes(target, bytes);
            }

            _images.Remove(name);
            return name;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException or NotSupportedException)
        {
            return null;
        }
    }

    public static ISolidColorBrush? ThemeBrush(string token)
        => Avalonia.Application.Current is { } app && app.TryGetResource("Brush." + token, app.ActualThemeVariant, out var v) ? v as ISolidColorBrush : null;

    public static bool IsDarkTheme => ThemeBrush("PanelBackground") is { Color: var c } && ColorMath.Luminance(new(c.R, c.G, c.B)) < 0.2;

    public static IBrush? FolderBrush(NameColour colour, bool dark)
        => Highlighter.Shades(colour) is var (light, darkShade) ? new ImmutableSolidColorBrush(Fill(dark ? darkShade : light, dark)) : null;

    private static Color Fill(string hex, bool dark)
    {
        var c = ColorMath.ParseHex(hex);
        var f = dark ? c : ColorMath.Mix(c, ColorMath.White, 0.3);
        return Color.FromRgb(f.R, f.G, f.B);
    }

    public static bool DrawFolder(DrawingContext context, FolderLook look, Rect at, bool dark, IPen pen)
    {
        if (FolderBrush(look.Colour, dark) is not { } fill)
        {
            return false;
        }

        AppIcons.Draw(context, "Icon.FolderLoading", at.TopLeft, pen, fill, at.Width / 16);
        return true;
    }

    public void DrawBadge(DrawingContext context, FolderLook look, Rect at, Typeface face, IBrush background, IPen border, IBrush ink)
    {
        var side = Math.Max(10, Math.Round(at.Width * 0.66));
        if (look.Image is { } name && Image(name) is { } image)
        {
            var box = new Rect(at.Right - side + 3, at.Bottom - side + 3, side, side);
            context.FillRectangle(background, box, 2);
            context.DrawImage(image, box.Deflate(1));
            return;
        }

        if (look.Mark is not { } mark)
        {
            return;
        }

        var text = new FormattedText(mark, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, face, side * 0.82, ink);
        var width = Math.Max(side, Math.Ceiling(text.Width) + 3);
        var rect = new Rect(at.Right - width + 3, at.Bottom - side + 3, width, side);
        context.DrawRectangle(background, border, rect.Deflate(0.5), 2, 2);
        context.DrawText(text, new Point(rect.Center.X - (text.Width / 2), rect.Center.Y - (text.Height / 2)));
    }
}

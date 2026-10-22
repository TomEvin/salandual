// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.App/Panels/DragPictures.cs

using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Salandual.Platform.Windows.Shell;

namespace Salandual.App.Panels;

internal static class DragPictures
{
    public const double IconSize = 48;

    public const double PointerGap = 24;

    private const double Step = 6;
    private const double BadgeHeight = 20;

    public static DragPicture Draw(IReadOnlyList<(Bitmap? Icon, bool IsFolder)> icons, int count, double scale, IBrush accent, IBrush onAccent, IBrush glyph)
    {
        var view = new View(icons.Take(3).ToList(), count, accent, onAccent, glyph);
        var size = view.Size;
        var pixels = new PixelSize((int)Math.Ceiling(size.Width * scale), (int)Math.Ceiling(size.Height * scale));
        view.Measure(size);
        view.Arrange(new Rect(size));
        using var target = new RenderTargetBitmap(pixels, new Vector(96 * scale, 96 * scale));
        target.Render(view);
        var bgra = new byte[pixels.Width * pixels.Height * 4];
        unsafe
        {
            fixed (byte* p = bgra)
            {
                target.CopyPixels(new PixelRect(pixels), (nint)p, bgra.Length, pixels.Width * 4);
            }
        }

        return new DragPicture(pixels.Width, pixels.Height, bgra, (int)Math.Round(view.Pointer.X * scale), 0);
    }

    private sealed class View(List<(Bitmap? Icon, bool IsFolder)> icons, int count, IBrush accent, IBrush onAccent, IBrush glyph) : Control
    {
        private readonly FormattedText? _badge = count > 1
            ? new FormattedText(count.ToString(CultureInfo.CurrentCulture), CultureInfo.CurrentCulture, FlowDirection.LeftToRight, new Typeface(FontFamily.Default, weight: FontWeight.SemiBold), 11, onAccent)
            : null;

        private double FrontX => (icons.Count - 1) * Step;

        private double BadgeWidth => _badge is null ? 0 : Math.Max(BadgeHeight, _badge.Width + 12);

        public Size Size => new(FrontX + IconSize + (BadgeWidth / 2) + 2, PointerGap + IconSize + ((icons.Count - 1) * Step) + 1);

        public Point Pointer => new(FrontX + 10, 0);

        public override void Render(DrawingContext context)
        {
            for (var i = icons.Count - 1; i >= 0; i--)
            {
                var at = new Point(FrontX - (i * Step), PointerGap + (i * Step));
                using (context.PushOpacity(i == 0 ? 0.9 : 0.55))
                {
                    if (icons[i].Icon is { } bitmap)
                    {
                        context.DrawImage(bitmap, new Rect(at, new Size(IconSize, IconSize)));
                    }
                    else
                    {
                        Salandual.App.Theme.AppIcons.Draw(context, icons[i].IsFolder ? "Icon.Folder" : "Icon.File", at, new Pen(glyph, 1.2), null, IconSize / 16);
                    }
                }
            }

            if (_badge is not null)
            {
                var rect = new Rect(FrontX + IconSize - (BadgeWidth / 2), PointerGap - (BadgeHeight / 3), BadgeWidth, BadgeHeight);
                context.DrawRectangle(accent, null, rect, BadgeHeight / 2, BadgeHeight / 2);
                context.DrawText(_badge, new Point(rect.X + ((rect.Width - _badge.Width) / 2), rect.Y + ((rect.Height - _badge.Height) / 2)));
            }
        }
    }
}

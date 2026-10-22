// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.App/Options/ThemePreview.cs

using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Salandual.App.Resources;
using Salandual.App.Theme;
using Salandual.Core.Settings;

namespace Salandual.App.Options;

public enum ThemePreviewView { Window, Viewer, Dialog }

public sealed class ThemePreview : Control
{
    private const double DesignWidth = 720;
    private const double DesignHeight = 410;

    private static readonly Typeface _ui = new("Segoe UI Variable Text, Segoe UI");
    private static readonly Typeface _uiBold = new("Segoe UI Variable Text, Segoe UI", FontStyle.Normal, FontWeight.SemiBold);
    private static readonly Typeface _mono = new("Cascadia Mono, Consolas");

    private IReadOnlyDictionary<string, Rgb>? _colors;
    private ThemePreviewView _view;

    public IReadOnlyDictionary<string, Rgb>? Colors
    {
        get => _colors;
        set
        {
            _colors = value;
            InvalidateVisual();
        }
    }

    public ThemePreviewView View
    {
        get => _view;
        set
        {
            _view = value;
            InvalidateVisual();
        }
    }

    public bool Compact { get; init; }

    public BackgroundImageService? Image
    {
        get => _image;
        set
        {
            if (_image is not null)
            {
                _image.Changed -= OnImageChanged;
            }

            _image = value;
            if (_image is not null)
            {
                _image.Changed += OnImageChanged;
            }

            InvalidateVisual();
        }
    }

    private BackgroundImageService? _image;

    private void OnImageChanged(object? sender, EventArgs e) => InvalidateVisual();

    protected override Size MeasureOverride(Size availableSize)
    {
        var width = double.IsFinite(availableSize.Width) ? availableSize.Width : DesignWidth;
        return new Size(width, width * DesignHeight / DesignWidth);
    }

    public override void Render(DrawingContext context)
    {
        if (_colors is null || Bounds.Width <= 0)
        {
            return;
        }

        var scale = Bounds.Width / DesignWidth;
        using (context.PushClip(new Rect(Bounds.Size)))
        using (context.PushTransform(Matrix.CreateScale(scale, scale)))
        {
            new Painter(context, _colors, Compact, _image).Draw(_view);
        }
    }

    private readonly struct Painter(DrawingContext ctx, IReadOnlyDictionary<string, Rgb> c, bool compact, BackgroundImageService? image)
    {
        private IBrush B(string token) => new SolidColorBrush(ToColor(c[token]));

        private static Color ToColor(Rgb rgb) => Color.FromRgb(rgb.R, rgb.G, rgb.B);

        private static string[] Parts(string text) => text.Split('|');

        public void Draw(ThemePreviewView view)
        {
            ctx.FillRectangle(B(ColorTokens.WindowBackground), new Rect(0, 0, DesignWidth, DesignHeight));
            TitleBar();
            if (view == ThemePreviewView.Viewer)
            {
                Viewer();
                return;
            }

            Menu(Parts(AppStrings.ThemePreview_Menus), open: view == ThemePreviewView.Window && !compact ? 2 : -1);
            Tools();
            Panels();
            Bottom();
            if (view == ThemePreviewView.Window && !compact)
            {
                OpenMenu();
            }
            else if (view == ThemePreviewView.Dialog)
            {
                Dialog();
            }
        }

        private void TitleBar()
        {
            ctx.DrawRectangle(B(ColorTokens.Accent), null, new Rect(10, 7, 14, 14), 3, 3);
            Text(AppStrings.ThemePreview_Title, 32, 6, 12, ColorTokens.Text);
            var x = DesignWidth - 120;
            foreach (var glyph in (ReadOnlySpan<string>)["—", "☐", "✕"])
            {
                Text(glyph, x + 14, 6, 12, ColorTokens.SecondaryText);
                x += 40;
            }
        }

        private void Menu(string[] names, int open)
        {
            var x = 6.0;
            for (var i = 0; i < names.Length; i++)
            {
                var width = Width(names[i], 12) + 16;
                if (i == open)
                {
                    ctx.DrawRectangle(B(ColorTokens.Hover), null, new Rect(x, 30, width, 20), 3, 3);
                }

                Text(names[i], x + 8, 32, 12, ColorTokens.Text);
                x += width + 2;
            }
        }

        private void Tools()
        {
            var x = 8.0;
            for (var i = 0; i < 10; i++)
            {
                if (i is 3 or 7)
                {
                    ctx.FillRectangle(B(ColorTokens.Border), new Rect(x + 3, 57, 1, 20));
                    x += 8;
                }

                ctx.DrawRectangle(B(ColorTokens.Hover), null, new Rect(x, 57, 20, 20), 3, 3);
                x += 24;
            }

            ctx.FillRectangle(B(ColorTokens.Border), new Rect(0, 81, DesignWidth, 1));
        }

        private void Panels()
        {
            var left = new (string Name, string Size, string Kind)[]
            {
                ("..", "", "dir"), (AppStrings.Sample_Folder, "<DIR>", "dir"), ("IMG_0412.HEIC", "2.4 MB", "sel"), ("IMG_0413.HEIC", "2.1 MB", "sel"),
                (AppStrings.Sample_Panorama, "1.2 MB", ""), (AppStrings.Sample_OldPhotos, "18 MB", "zip"), (AppStrings.Sample_Passport, "412 KB", "cur"),
                (AppStrings.Sample_Prices, "2 KB", "enc"),
            };
            var right = new (string Name, string Size, string Kind)[]
            {
                ("..", "", "dir"), (AppStrings.Sample_Folder, "<DIR>", "dir"), (AppStrings.Sample_Prices, "2 KB", "hover"),
                (AppStrings.Sample_OldPhotos, "18 MB", "zip"), (AppStrings.Sample_Panorama, "1.2 MB", ""),
            };
            Pane(new Rect(4, 86, 354, 246), @"C:\", left, active: true);
            Pane(new Rect(362, 86, 354, 246), @"D:\", right, active: false);
        }

        private void Pane(Rect r, string path, (string Name, string Size, string Kind)[] rows, bool active)
        {
            ctx.DrawRectangle(B(ColorTokens.PanelBackground), new Pen(B(ColorTokens.Border)), r, 4, 4);
            ctx.FillRectangle(B(ColorTokens.PanelBackground), new Rect(r.X + 1, r.Y + 1, r.Width - 2, 24));
            Text(path, r.X + 8, r.Y + 5, 11.5, ColorTokens.Text, bold: true);
            if (active)
            {
                ctx.FillRectangle(B(ColorTokens.Accent), new Rect(r.X + 1, r.Y + 23, r.Width - 2, 2));
            }
            else
            {
                ctx.FillRectangle(B(ColorTokens.Border), new Rect(r.X + 1, r.Y + 24, r.Width - 2, 1));
            }

            var columns = Parts(AppStrings.ThemePreview_Columns);
            var header = new Rect(r.X + 1, r.Y + 25, r.Width - 2, 22);
            ctx.FillRectangle(B(ColorTokens.ColumnHeader), header);
            Text(columns[0], r.X + 8, header.Y + 4, 11, ColorTokens.SecondaryText);
            Text(columns[1], r.Right - 104, header.Y + 4, 11, ColorTokens.SecondaryText, rightEdge: true);
            Text(columns[2], r.Right - 8, header.Y + 4, 11, ColorTokens.SecondaryText, rightEdge: true);
            ctx.FillRectangle(B(ColorTokens.Border), new Rect(r.X + 1, header.Bottom, r.Width - 2, 1));
            if (image is { Image: not null })
            {
                var whole = image.Settings.Placement == BackgroundPlacement.Window ? new Rect(0, 0, DesignWidth, DesignHeight) : r;
                image.Draw(ctx, whole, new Rect(r.X + 1, header.Bottom + 1, r.Width - 2, r.Bottom - header.Bottom - 2));
            }


            var y = header.Bottom + 2;
            foreach (var (name, size, kind) in rows)
            {
                var row = new Rect(r.X + 2, y, r.Width - 4, 20);
                if (kind == "sel")
                {
                    ctx.FillRectangle(B(ColorTokens.SelectionBackground), row);
                }
                else if (kind == "hover")
                {
                    ctx.FillRectangle(B(ColorTokens.Hover), row);
                }

                if (kind == "cur")
                {
                    ctx.DrawRectangle(null, new Pen(B(ColorTokens.FocusOutline)), row.Deflate(0.5));
                }

                var ink = kind switch { "sel" => ColorTokens.SelectedText, "zip" => ColorTokens.CompressedFiles, "enc" => ColorTokens.EncryptedFiles, _ => ColorTokens.Text };
                var quiet = kind == "sel" ? ColorTokens.SelectedText : ColorTokens.SecondaryText;
                Text(name, r.X + 8, y + 3, 11.5, ink);
                if (size.Length > 0)
                {
                    Text(size, r.Right - 104, y + 3, 11.5, quiet, rightEdge: true);
                    Text("14.08.2026", r.Right - 8, y + 3, 11.5, quiet, rightEdge: true);
                }

                y += 20;
            }
        }

        private void Bottom()
        {
            var info = new Rect(0, 336, DesignWidth, 22);
            ctx.FillRectangle(B(ColorTokens.InformationLine), info);
            Text(AppStrings.Sample_Passport + "    412 KB    14.08.2026 18:23", 10, info.Y + 4, 11, ColorTokens.InformationLineText);
            Text(AppStrings.ThemePreview_Selected, DesignWidth - 10, info.Y + 4, 11, ColorTokens.InformationLineText, rightEdge: true);

            Text(@"C:\>", 8, 364, 11.5, ColorTokens.Text);
            ctx.DrawRectangle(B(ColorTokens.PanelBackground), new Pen(B(ColorTokens.Border)), new Rect(40, 361, DesignWidth - 48, 20), 3, 3);

            var keys = Parts(AppStrings.ThemePreview_Keys);
            var width = (DesignWidth - 8 - (3 * 7)) / 8;
            for (var i = 0; i < 8; i++)
            {
                var r = new Rect(4 + (i * (width + 3)), 384, width, 22);
                ctx.DrawRectangle(B(ColorTokens.PanelBackground), new Pen(B(ColorTokens.Border)), r, 3, 3);
                var label = "F" + (i + 3).ToString(CultureInfo.InvariantCulture);
                var total = Width(label, 11, bold: true) + 4 + Width(keys[i], 11);
                var x = r.X + ((r.Width - total) / 2);
                Text(label, x, r.Y + 4, 11, ColorTokens.SecondaryText, bold: true);
                Text(keys[i], x + Width(label, 11, bold: true) + 4, r.Y + 4, 11, ColorTokens.Text);
            }
        }

        private void OpenMenu()
        {
            var items = Parts(AppStrings.ThemePreview_MenuItems);
            var keys = new[] { "Ctrl+1", "Ctrl+2", "Ctrl+3", "Ctrl+F3", "Ctrl+F6", "Ctrl+H" };
            var box = new Rect(140, 52, 190, 8 + (items.Length * 22) + 14);
            ctx.DrawRectangle(new SolidColorBrush(Color.FromArgb(40, 0, 0, 0)), null, box.Inflate(2), 8, 8);
            ctx.DrawRectangle(B(ColorTokens.PanelBackground), new Pen(B(ColorTokens.Border)), box, 6, 6);
            var y = box.Y + 4;
            for (var i = 0; i < items.Length; i++)
            {
                if (i is 3 or 5)
                {
                    ctx.FillRectangle(B(ColorTokens.Border), new Rect(box.X + 6, y + 3, box.Width - 12, 1));
                    y += 7;
                }

                if (i == 1)
                {
                    ctx.DrawRectangle(B(ColorTokens.Hover), null, new Rect(box.X + 4, y, box.Width - 8, 22), 3, 3);
                }

                Text(items[i], box.X + 12, y + 4, 11.5, ColorTokens.Text);
                Text(keys[i], box.Right - 12, y + 5, 10.5, ColorTokens.SecondaryText, rightEdge: true);
                y += 22;
            }
        }

        private void Dialog()
        {
            ctx.FillRectangle(new SolidColorBrush(Color.FromArgb(72, 0, 0, 0)), new Rect(0, 0, DesignWidth, DesignHeight));
            var p = Parts(AppStrings.ThemePreview_Dialog);
            var box = new Rect(160, 100, 400, 200);
            ctx.DrawRectangle(new SolidColorBrush(Color.FromArgb(40, 0, 0, 0)), null, box.Inflate(3), 11, 11);
            ctx.DrawRectangle(B(ColorTokens.PanelBackground), new Pen(B(ColorTokens.Border)), box, 8, 8);
            Text(p[0], box.X + 16, box.Y + 14, 14, ColorTokens.Text, bold: true);
            Text(p[1], box.X + 16, box.Y + 44, 11.5, ColorTokens.SecondaryText);
            var field = new Rect(box.X + 16, box.Y + 62, box.Width - 32, 26);
            ctx.DrawRectangle(B(ColorTokens.WindowBackground), new Pen(B(ColorTokens.Border)), field, 4, 4);
            ctx.FillRectangle(B(ColorTokens.Accent), new Rect(field.X + 1, field.Bottom - 2, field.Width - 2, 2));
            var target = @"D:\" + AppStrings.Sample_Folder;
            Text(target, field.X + 8, field.Y + 5, 12, ColorTokens.Text);
            Check(box.X + 16, box.Y + 100, p[2], on: true);
            Check(box.X + 16, box.Y + 124, p[3], on: false);
            var cancel = new Rect(box.Right - 196, box.Bottom - 40, 86, 28);
            var ok = new Rect(box.Right - 102, box.Bottom - 40, 86, 28);
            ctx.DrawRectangle(B(ColorTokens.WindowBackground), new Pen(B(ColorTokens.Border)), cancel, 4, 4);
            ctx.DrawRectangle(B(ColorTokens.Accent), null, ok, 4, 4);
            Text(p[4], cancel.Center.X, cancel.Y + 6, 12, ColorTokens.Text, centre: true);
            Text(p[5], ok.Center.X, ok.Y + 6, 12, ColorTokens.OnAccent, centre: true);
        }

        private void Check(double x, double y, string label, bool on)
        {
            var r = new Rect(x, y, 14, 14);
            if (on)
            {
                ctx.DrawRectangle(B(ColorTokens.Accent), null, r, 3, 3);
                var pen = new Pen(B(ColorTokens.OnAccent), 1.6);
                ctx.DrawLine(pen, new Point(x + 3, y + 7), new Point(x + 6, y + 10));
                ctx.DrawLine(pen, new Point(x + 6, y + 10), new Point(x + 11, y + 4));
            }
            else
            {
                ctx.DrawRectangle(null, new Pen(B(ColorTokens.BorderStrong)), r.Deflate(0.5), 3, 3);
            }

            Text(label, x + 22, y - 1, 12, ColorTokens.Text);
        }

        private void Viewer()
        {
            Menu(Parts(AppStrings.ThemePreview_ViewerMenus), open: -1);
            var bar = new Rect(0, 52, DesignWidth, 30);
            var p = Parts(AppStrings.ThemePreview_Find);
            Text(p[0], 8, bar.Y + 8, 11.5, ColorTokens.Text);
            var box = new Rect(8 + Width(p[0], 11.5) + 8, bar.Y + 4, 200, 22);
            ctx.DrawRectangle(B(ColorTokens.PanelBackground), new Pen(B(ColorTokens.FocusOutline)), box, 3, 3);
            Text(AppStrings.ThemePreview_FindText, box.X + 6, box.Y + 4, 11.5, ColorTokens.Text);
            Text(p[1], box.Right + 8, bar.Y + 8, 11.5, ColorTokens.SecondaryText);
            ctx.FillRectangle(B(ColorTokens.Border), new Rect(0, bar.Bottom - 1, DesignWidth, 1));

            var area = new Rect(4, 86, DesignWidth - 8, 292);
            ctx.DrawRectangle(B(ColorTokens.SyntaxBackground), new Pen(B(ColorTokens.Border)), area, 4, 4);
            var lines = Code();
            var y = area.Y + 5;
            for (var i = 0; i < lines.Length; i++)
            {
                if (i == 6)
                {
                    ctx.FillRectangle(B(ColorTokens.Hover), new Rect(area.X + 1, y, area.Width - 2, 19));
                }

                Text((i + 1).ToString(CultureInfo.InvariantCulture), area.X + 30, y + 2, 12, ColorTokens.ViewerLineNumbers, mono: true, rightEdge: true);
                var x = area.X + 44;
                foreach (var (token, piece) in lines[i])
                {
                    var w = Width(piece, 12, mono: true);
                    if (i == 3 && token == ColorTokens.SyntaxKey)
                    {
                        ctx.FillRectangle(B(ColorTokens.SelectionBackground), new Rect(x, y, w, 19));
                    }
                    else if (i == 10 && token == ColorTokens.SyntaxString)
                    {
                        ctx.DrawRectangle(B(ColorTokens.ViewerMatch), new Pen(B(ColorTokens.FocusOutline)), new Rect(x, y + 1, w, 17));
                    }

                    Text(piece, x, y + 2, 12, token, mono: true);
                    x += w;
                }

                y += 19;
            }

            var info = new Rect(0, 386, DesignWidth, 24);
            ctx.FillRectangle(B(ColorTokens.InformationLine), info);
            var parts = Parts(AppStrings.ThemePreview_ViewerInfo);
            Text(parts[0], 10, info.Y + 4, 11, ColorTokens.InformationLineText);
            Text(parts[1], DesignWidth - 10, info.Y + 4, 11, ColorTokens.InformationLineText, rightEdge: true);
        }

        private static (string Token, string Text)[][] Code()
        {
            const string k = ColorTokens.SyntaxKey, s = ColorTokens.SyntaxString, n = ColorTokens.SyntaxNumber, w = ColorTokens.SyntaxKeyword,
                p = ColorTokens.SyntaxPunctuation, m = ColorTokens.SyntaxComment, t = ColorTokens.SyntaxText;
            return
            [
                [(p, "{")],
                [(t, "  "), (k, "\"name\""), (p, ": "), (s, "\"salandual\""), (p, ",")],
                [(t, "  "), (k, "\"version\""), (p, ": "), (s, "\"0.5.0\""), (p, ",")],
                [(t, "  "), (k, "\"panels\""), (p, ": "), (n, "2"), (p, ",")],
                [(t, "  "), (k, "\"portable\""), (p, ": "), (w, "true"), (p, ",")],
                [(t, "  "), (k, "\"viewers\""), (p, ": {")],
                [(t, "    "), (k, "\"markdown\""), (p, ": { "), (k, "\"largestMB\""), (p, ": "), (n, "10"), (p, " },")],
                [(t, "    "), (k, "\"notebook\""), (p, ": "), (n, "200")],
                [(p, "  },")],
                [(t, "  "), (m, "// " + AppStrings.ThemePreview_Comment)],
                [(t, "  "), (k, "\"theme\""), (p, ": "), (s, "\"" + AppStrings.ThemePreview_FindText + "\"")],
                [(p, "}")],
            ];
        }

        private void Text(string text, double x, double y, double size, string token, bool bold = false, bool mono = false, bool rightEdge = false, bool centre = false)
        {
            if (compact)
            {
                var width = Width(text, size, bold, mono);
                var left = rightEdge ? x - width : centre ? x - (width / 2) : x;
                var ink = c[token];
                ctx.FillRectangle(new SolidColorBrush(Color.FromArgb(170, ink.R, ink.G, ink.B)), new Rect(left, y + (size * 0.3), width, size * 0.6));
                return;
            }

            var formatted = Format(text, size, bold, mono, B(token));
            var at = rightEdge ? x - formatted.Width : centre ? x - (formatted.Width / 2) : x;
            ctx.DrawText(formatted, new Point(at, y));
        }

        private double Width(string text, double size, bool bold = false, bool mono = false)
            => mono || compact ? text.Length * size * (mono ? 0.6 : 0.52) : Format(text, size, bold, mono, Brushes.Black).Width;

        private static FormattedText Format(string text, double size, bool bold, bool mono, IBrush brush)
            => new(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, mono ? _mono : bold ? _uiBold : _ui, size, brush);
    }
}

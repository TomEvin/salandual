// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.App/Panels/InformationLineView.cs

using Avalonia;
using Avalonia.Media;
using Salandual.App.Controls;

namespace Salandual.App.Panels;

public sealed class InformationLineView : ThemedControl
{
    private string _left = "";
    private string _right = "";
    private IBrush _bg = null!, _text = null!, _dim = null!, _border = null!;

    internal string LeftText => _left;

    internal string RightText => _right;

    public void Set(string left, string right)
    {
        if (left != _left || right != _right)
        {
            _left = left;
            _right = right;
            InvalidateVisual();
        }
    }

    protected override void LoadTheme()
    {
        _bg = Brush("InformationLine");
        _text = Brush("InformationLineText");
        _dim = Brush("InformationLineDimText");
        _border = Brush("Border");
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        EnsureTheme();
        return new Size(0, Math.Round(FontSize * 2.1));
    }

    public override void Render(DrawingContext context)
    {
        EnsureTheme();
        var size = Bounds.Size;
        var small = Math.Max(9, FontSize - 1);
        context.FillRectangle(_bg, new Rect(size));
        DrawBackdrop(context, new Rect(size));
        context.FillRectangle(_border, new Rect(0, 0, size.Width, 1));

        var right = Text(_right, _dim, size: small);
        context.DrawText(right, new Point(size.Width - 12 - right.Width, (size.Height - right.Height) / 2));

        var colon = _left.IndexOf(':', StringComparison.Ordinal);
        var name = Text(colon > 0 ? _left[..colon] : _left, _text, bold: true, size: small);
        var rest = Text(colon > 0 ? _left[colon..] : "", _dim, size: small);
        var max = Math.Max(10, size.Width - right.Width - 40);
        name.MaxTextWidth = max;
        name.Trimming = TextTrimming.CharacterEllipsis;
        name.MaxLineCount = 1;
        var y = (size.Height - name.Height) / 2;
        context.DrawText(name, new Point(12, y));
        if (name.Width < max)
        {
            rest.MaxTextWidth = Math.Max(1, max - name.Width);
            rest.Trimming = TextTrimming.CharacterEllipsis;
            rest.MaxLineCount = 1;
            context.DrawText(rest, new Point(12 + name.WidthIncludingTrailingWhitespace, y));
        }
    }
}

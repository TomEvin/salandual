// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.App/Controls/FunctionKeyBar.cs

using Avalonia;
using Avalonia.Input;
using Avalonia.Media;
using Salandual.App.Resources;
using Salandual.Core.Commands;
using CoreModifiers = Salandual.Core.Commands.KeyModifiers;

namespace Salandual.App.Controls;

public sealed class FunctionKeyBar : ThemedControl
{
    private readonly List<(Rect Bounds, string Id)> _hits = [];
    private CommandRegistry? _commands;
    private CoreModifiers _modifiers;
    private Func<string, bool> _canExecute = _ => false;
    private int _hover = -1;
    private IBrush _bg = null!, _text = null!, _dim = null!, _faint = null!, _keyBg = null!, _hoverBg = null!, _border = null!;

    public event EventHandler<string>? CommandInvoked;

    public void Configure(CommandRegistry commands, Func<string, bool> canExecute)
    {
        _commands = commands;
        _canExecute = canExecute;
        InvalidateVisual();
    }

    public CoreModifiers Modifiers
    {
        get => _modifiers;
        set
        {
            if (value != _modifiers)
            {
                _modifiers = value;
                InvalidateVisual();
            }
        }
    }

    protected override void LoadTheme()
    {
        _bg = Brush("WindowBackground");
        _text = Brush("Text");
        _dim = Brush("SecondaryText");
        _faint = Brush("TertiaryText");
        _keyBg = Brush("Hover");
        _hoverBg = Brush("Pressed");
        _border = Brush("Border");
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        EnsureTheme();
        return new Size(0, Math.Round(FontSize * 2.3));
    }

    public override void Render(DrawingContext context)
    {
        EnsureTheme();
        var size = Bounds.Size;
        context.FillRectangle(_bg, new Rect(size));
        DrawBackdrop(context, new Rect(size));
        _hits.Clear();
        if (_commands is null)
        {
            return;
        }

        var small = Math.Max(9, FontSize - 1);
        var prefix = _modifiers == CoreModifiers.None ? "" : new KeyChord(_modifiers, "F1").ToString()[..^2];
        var x = 8.0;
        for (var n = 1; n <= 10; n++)
        {
            var key = "F" + n.ToString(System.Globalization.CultureInfo.InvariantCulture);
            var id = _commands.Resolve(new KeyChord(_modifiers, key));
            var definition = id is null ? null : _commands[id];
            var enabled = id is not null && _canExecute(id);
            var keyText = Text(key, enabled ? _dim : _faint, size: small);
            var label = Text(definition?.ShortLabel ?? definition?.Title ?? "", enabled ? _text : _faint, size: small);
            var w = keyText.Width + 8 + label.Width + 14;
            if (x + w > size.Width - 160)
            {
                break;
            }

            var cell = new Rect(x - 4, 3, w, size.Height - 6);
            if (id is not null && _hits.Count == _hover && enabled)
            {
                context.FillRectangle(_hoverBg, cell, 4);
            }

            var y = (size.Height - keyText.Height) / 2;
            context.DrawText(keyText, new Point(x, y));
            context.DrawText(label, new Point(x + keyText.Width + 8, y));
            _hits.Add((cell, enabled ? id! : ""));
            x += w + 4;
        }

        var hint = Text(prefix.Length > 0 ? prefix + "…" : AppStrings.Fkey_HoldHint, _faint, size: small);
        if (x + hint.Width < size.Width - 8)
        {
            context.DrawText(hint, new Point(x + 8, (size.Height - hint.Height) / 2));
        }
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        var p = e.GetPosition(this);
        var hover = _hits.FindIndex(h => h.Bounds.Contains(p));
        if (hover != _hover)
        {
            _hover = hover;
            InvalidateVisual();
        }
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        _hover = -1;
        InvalidateVisual();
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        var p = e.GetPosition(this);
        var hit = _hits.FindIndex(h => h.Bounds.Contains(p));
        if (hit >= 0 && _hits[hit].Id.Length > 0)
        {
            CommandInvoked?.Invoke(this, _hits[hit].Id);
            e.Handled = true;
        }
    }
}

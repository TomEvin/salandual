// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.App/Editor/SyntaxColorizer.cs

using Avalonia.Controls;
using Avalonia.Media;
using AvaloniaEdit.Document;
using AvaloniaEdit.Rendering;
using Salandual.Core.Settings;
using Salandual.Core.Viewer;

namespace Salandual.App.Editor;

internal sealed class SyntaxColorizer : DocumentColorizingTransformer
{
    private readonly Control _owner;
    private readonly SyntaxLanguage _language;
    private readonly List<int> _states = [0];
    private readonly List<SyntaxSpan> _spans = [];
    private readonly Dictionary<SyntaxKind, (IBrush Brush, FontWeight Weight, FontStyle Style)> _looks = [];

    public SyntaxColorizer(Control owner, TextDocument document, SyntaxLanguage language)
    {
        _owner = owner;
        _language = language;
        document.Changing += (_, e) => Forget(document.GetLineByOffset(Math.Min(e.Offset, document.TextLength)).LineNumber);
    }

    public SyntaxLanguage Language => _language;

    public void ForgetLooks() => _looks.Clear();

    private void Forget(int line)
    {
        if (_states.Count > line)
        {
            _states.RemoveRange(line, _states.Count - line);
        }
    }

    private int StateAt(TextDocument document, int lineNumber)
    {
        while (_states.Count < lineNumber)
        {
            var n = _states.Count;
            var line = document.GetLineByNumber(n);
            _spans.Clear();
            _states.Add(SyntaxRules.Tokenize(_language, document.GetText(line), _states[n - 1], _spans));
        }

        return _states[lineNumber - 1];
    }

    protected override void ColorizeLine(DocumentLine line)
    {
        var document = CurrentContext.Document;
        var state = StateAt(document, line.LineNumber);
        var text = document.GetText(line);
        _spans.Clear();
        var end = SyntaxRules.Tokenize(_language, text, state, _spans);
        if (_states.Count == line.LineNumber)
        {
            _states.Add(end);
        }

        foreach (var span in _spans)
        {
            if (span.Length <= 0 || span.Start >= text.Length)
            {
                continue;
            }

            var (brush, weight, style) = Look(span.Kind);
            var from = line.Offset + span.Start;
            var to = line.Offset + Math.Min(text.Length, span.Start + span.Length);
            ChangeLinePart(from, to, element =>
            {
                element.TextRunProperties.SetForegroundBrush(brush);
                if (weight != FontWeight.Normal || style != FontStyle.Normal)
                {
                    var face = element.TextRunProperties.Typeface;
                    element.TextRunProperties.SetTypeface(new Typeface(face.FontFamily, style, weight));
                }
            });
        }
    }

    private (IBrush, FontWeight, FontStyle) Look(SyntaxKind kind)
    {
        if (_looks.TryGetValue(kind, out var look))
        {
            return look;
        }

        var token = ColorTokens.TokenFor(kind);
        var key = token == ColorTokens.SyntaxText ? "Brush.CodeInk" : "Brush." + token;
        var brush = _owner.TryFindResource(key, _owner.ActualThemeVariant, out var b) && b is IBrush found ? found : Brushes.Gray;
        var weight = kind switch { SyntaxKind.Keyword => FontWeight.SemiBold, SyntaxKind.Heading or SyntaxKind.Strong => FontWeight.Bold, _ => FontWeight.Normal };
        var style = kind is SyntaxKind.Comment or SyntaxKind.Emphasis ? FontStyle.Italic : FontStyle.Normal;
        look = (brush, weight, style);
        _looks[kind] = look;
        return look;
    }
}

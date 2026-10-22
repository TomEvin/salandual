// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.App/Editor/EditorWindow.Tools.cs

using Avalonia.Controls;
using Salandual.App.Options;
using Salandual.App.Resources;
using Salandual.Core.Editing;
using Salandual.Core.Resources;
using Salandual.Core.Settings;
using Salandual.Core.Viewer;
using Salandual.Platform.Windows.FileSystem;

namespace Salandual.App.Editor;

internal sealed partial class EditorWindow
{
    private readonly Border _noteBar = new() { IsVisible = false };
    private int _editVersion;

    internal Border NoteBar => _noteBar;

    internal Task Formatting { get; private set; } = Task.CompletedTask;

    private bool CanFormat => _language is SyntaxLanguage.Json or SyntaxLanguage.Yaml or SyntaxLanguage.Xml;

    private IndentStyle FormatStyle => Options.FormatIndent switch
    {
        FormatIndent.Spaces2 => IndentStyle.Spaces2,
        FormatIndent.Spaces4 => IndentStyle.Spaces4,
        FormatIndent.Tabs => IndentStyle.Tabs,
        _ => _indent,
    };

    private string NewLine => _lineEnding == LineEnding.Lf ? "\n" : "\r\n";

    private void ShowNote(string text)
    {
        Notice(_noteBar, "Brush.InfoBackground", "i", new TextBlock { Text = text, TextWrapping = Avalonia.Media.TextWrapping.Wrap, FontSize = 12.5, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center }.Res(TextBlock.ForegroundProperty, "Brush.Text"),
            LinkButton(AppStrings.Common_Close, () => _noteBar.IsVisible = false));
        _noteBar.IsVisible = true;
    }

    private string MistakeNote() => _problems.Count > 0
        ? Loc.Format(AppStrings.Editor_FixFirstAt, _problems[0].Line)
        : AppStrings.Editor_FixFirst;

    internal Task FormatAsync() => Formatting = RunTool(Tool.Format);

    internal Task MinifyAsync() => Formatting = RunTool(Tool.Minify);

    internal Task SortKeysAsync() => Formatting = RunTool(Tool.SortKeys);

    private enum Tool { Format, Minify, SortKeys }

    private async Task RunTool(Tool tool)
    {
        var language = _language;
        if (!CanFormat || (tool != Tool.Format && language != SyntaxLanguage.Json))
        {
            return;
        }

        _noteBar.IsVisible = false;
        var document = _editor.Document;
        var version = _editVersion;
        var selection = _editor.SelectionLength > 0 ? (Start: _editor.SelectionStart, Length: _editor.SelectionLength) : ((int Start, int Length)?)null;
        var snapshot = document.CreateSnapshot();
        var style = FormatStyle;
        var unit = Indentation.Unit(style);
        var newline = NewLine;
        var comments = JsonText.AllowsComments(WindowsPath.GetName(_path));
        string prefix = "";
        if (selection is { } s)
        {
            var line = document.GetText(document.GetLineByOffset(s.Start));
            prefix = line[..(line.Length - line.TrimStart(' ', '\t').Length)];
        }

        var refusal = JsonText.SortRefusal.None;
        var result = await Task.Run(() =>
        {
            var all = snapshot.Text;
            var part = selection is { } sel ? all.Substring(sel.Start, sel.Length) : all;
            return (tool, language) switch
            {
                (Tool.Minify, _) => JsonText.Minify(part, comments),
                (Tool.SortKeys, _) => JsonText.SortKeys(part, unit, newline, prefix, out refusal),
                (_, SyntaxLanguage.Json) => JsonText.Format(part, unit, newline, comments, prefix),
                (_, SyntaxLanguage.Yaml) => YamlText.Format(all, style == IndentStyle.Spaces4 ? 4 : 2),
                _ => XmlMarkup.Format(all, unit),
            };
        });
        if (version != _editVersion)
        {
            return;
        }

        if (result is null)
        {
            ShowNote(tool == Tool.SortKeys ? refusal switch
            {
                JsonText.SortRefusal.Comments => AppStrings.Editor_SortComments,
                JsonText.SortRefusal.NotObject => AppStrings.Editor_SortNotObject,
                JsonText.SortRefusal.TooDeep => AppStrings.Editor_SortTooDeep,
                _ => MistakeNote(),
            } : language == SyntaxLanguage.Yaml && _problems.Count == 0 ? AppStrings.Editor_FormatYamlKept
            : selection is not null && language == SyntaxLanguage.Json ? AppStrings.Editor_FormatSelection : MistakeNote());
            return;
        }

        if (selection is { } range && language == SyntaxLanguage.Json)
        {
            document.Replace(range.Start, range.Length, result);
            _editor.Select(range.Start, result.Length);
        }
        else if (selection is { } lines)
        {
            var first = document.GetLineByOffset(lines.Start);
            var last = document.GetLineByOffset(lines.Start + lines.Length);
            var newLines = SplitKeepingBreaks(result);
            if (newLines.Count == document.LineCount)
            {
                var replacement = string.Concat(newLines.Skip(first.LineNumber - 1).Take(last.LineNumber - first.LineNumber + 1));
                var end = last.EndOffset + last.DelimiterLength;
                document.Replace(first.Offset, end - first.Offset, replacement);
                _editor.Select(first.Offset, Math.Max(0, replacement.Length - (last.DelimiterLength > 0 ? last.DelimiterLength : 0)));
            }
        }
        else
        {
            ReplaceChanged(result);
        }
    }

    private static List<string> SplitKeepingBreaks(string text)
    {
        var lines = new List<string>();
        var at = 0;
        while (true)
        {
            var end = at;
            while (end < text.Length && text[end] is not ('\r' or '\n'))
            {
                end++;
            }

            var breakEnd = end < text.Length && text[end] == '\r' && end + 1 < text.Length && text[end + 1] == '\n' ? end + 2 : Math.Min(text.Length, end + 1);
            lines.Add(text[at..breakEnd]);
            if (breakEnd == end)
            {
                return lines;
            }

            at = breakEnd;
        }
    }

    private void ReplaceChanged(string text)
    {
        var document = _editor.Document;
        var old = document.Text;
        var prefix = 0;
        var max = Math.Min(old.Length, text.Length);
        while (prefix < max && old[prefix] == text[prefix])
        {
            prefix++;
        }

        var suffix = 0;
        while (suffix < max - prefix && old[old.Length - 1 - suffix] == text[text.Length - 1 - suffix])
        {
            suffix++;
        }

        if (prefix == old.Length && prefix == text.Length)
        {
            return;
        }

        document.Replace(prefix, old.Length - prefix - suffix, text.Substring(prefix, text.Length - prefix - suffix));
    }

    internal void CommentLine()
    {
        if (CommentStyle.For(_language, WindowsPath.GetName(_path)) is not { } style)
        {
            ShowNote(AppStrings.Editor_NoComments);
            return;
        }

        var document = _editor.Document;
        var first = document.GetLineByOffset(_editor.SelectionStart);
        var end = _editor.SelectionStart + _editor.SelectionLength;
        var last = document.GetLineByOffset(end);
        if (_editor.SelectionLength > 0 && last.Offset == end && last.LineNumber > first.LineNumber)
        {
            last = last.PreviousLine;
        }

        var block = document.GetText(first.Offset, last.EndOffset - first.Offset);
        var toggled = style.Toggle(block);
        document.Replace(first.Offset, block.Length, toggled);
        if (_editor.SelectionLength > 0 || first != last)
        {
            _editor.Select(first.Offset, toggled.Length);
        }
    }

    private CommentStyle? CommentStyleNow => CommentStyle.For(_language, WindowsPath.GetName(_path));

    internal async Task EditWithAsync(Action open)
    {
        if (IsModified)
        {
            var answer = await Ask(AppStrings.Editor_SaveFirst_Title, [AppStrings.Editor_SaveFirst_Text],
                [new("save", AppStrings.Editor_SaveFirst_Save, "Enter", Primary: true), new("open", AppStrings.Editor_SaveFirst_Open), new("cancel", AppStrings.Common_Cancel, "Esc")]);
            if (answer == "cancel" || answer is null || (answer == "save" && !await SaveAsync()))
            {
                return;
            }
        }

        open();
    }

    private void ShowEditWith()
    {
        if (_request.EditWith is null)
        {
            return;
        }

        var menu = new MenuFlyout { Placement = Avalonia.Controls.PlacementMode.BottomEdgeAlignedRight };
        menu.Items.Add(new MenuItem { Header = Loc.Format(AppStrings.EditWith_Title, WindowsPath.GetName(_path)), IsEnabled = false });
        foreach (var item in _request.EditWith(_path))
        {
            menu.Items.Add(MainWindow.EditWithMenuItem(item with { Run = () => _ = EditWithAsync(item.Run) }));
        }

        menu.ShowAt(_more);
    }

    private bool ToolKey(Avalonia.Input.Key key, Avalonia.Input.KeyModifiers mods)
    {
        const Avalonia.Input.KeyModifiers ctrl = Avalonia.Input.KeyModifiers.Control;
        if (_chordK)
        {
            _chordK = false;
            if (mods == ctrl && key is Avalonia.Input.Key.D0 or Avalonia.Input.Key.NumPad0)
            {
                FoldAll(true);
                return true;
            }

            if (mods == ctrl && key == Avalonia.Input.Key.J)
            {
                FoldAll(false);
                return true;
            }
        }

        switch (key)
        {
            case Avalonia.Input.Key.K when mods == ctrl:
                _chordK = true;
                return true;
            case Avalonia.Input.Key.F when mods == (Avalonia.Input.KeyModifiers.Shift | Avalonia.Input.KeyModifiers.Alt):
                _ = FormatAsync();
                return true;
            case Avalonia.Input.Key.OemQuestion or Avalonia.Input.Key.Divide when mods == ctrl:
                CommentLine();
                return true;
            case Avalonia.Input.Key.F6 when mods == Avalonia.Input.KeyModifiers.None:
                ShowPreview(!_previewOn);
                return true;
            case Avalonia.Input.Key.F4 when mods == (ctrl | Avalonia.Input.KeyModifiers.Shift):
                ShowEditWith();
                return true;
            default:
                return false;
        }
    }

    private bool _chordK;

    internal void ConvertIndentation(IndentStyle to)
    {
        ReplaceChanged(Indentation.Convert(_editor.Document.Text, _indent, to));
        SetIndent(to);
    }
}

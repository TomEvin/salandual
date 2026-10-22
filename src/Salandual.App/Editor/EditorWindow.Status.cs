// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.App/Editor/EditorWindow.Status.cs

using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using AvaloniaEdit.Rendering;
using Salandual.App.Options;
using Salandual.App.Resources;
using Salandual.Core.Resources;
using Salandual.Core.Settings;
using Salandual.Core.Viewer;

namespace Salandual.App.Editor;

internal sealed partial class EditorWindow
{
    private readonly StackPanel _statusLeft = new() { Orientation = Orientation.Horizontal };
    private readonly StackPanel _statusRight = new() { Orientation = Orientation.Horizontal };
    private readonly MatchMarks _marks = new();
    private EditorStrip _strip = null!;

    internal IEnumerable<TextBlock> StatusTexts => _statusLeft.Children.Concat(_statusRight.Children).OfType<Border>().Select(b => b.Child).OfType<TextBlock>();

    internal Border StatusCell(string text) => _statusLeft.Children.Concat(_statusRight.Children).OfType<Border>().First(b => (b.Child as TextBlock)?.Text == text);

    private Control BuildStatus()
    {
        _editor.TextArea.TextView.BackgroundRenderers.Add(_marks);
        var status = new Border
        {
            Height = 26,
            BorderThickness = new Thickness(0, 1, 0, 0),
            Child = new DockPanel { Children = { _statusRight, _statusLeft } },
        }.Res(Border.BackgroundProperty, "Brush.InformationLine").Res(Border.BorderBrushProperty, "Brush.Border");
        DockPanel.SetDock(_statusRight, Dock.Right);
        return status;
    }

    private Control BuildStrip()
    {
        _strip = new EditorStrip(this) { Width = 12 };
        return _strip;
    }

    private void UpdateStatus()
    {
        if (_statusLeft is null || _editor is null)
        {
            return;
        }

        _statusLeft.Children.Clear();
        _statusRight.Children.Clear();
        var caret = _editor.TextArea.Caret;
        Cell(_statusLeft, Loc.Format(AppStrings.Editor_LineCol, caret.Line.ToString("N0", CultureInfo.CurrentCulture), caret.Column), tip: AppStrings.Editor_GoToTip, click: _ => OpenGoTo());
        if (_editor.SelectionLength > 0)
        {
            Cell(_statusLeft, Loc.Format(AppStrings.Editor_Selected, _editor.SelectionLength.ToString("N0", CultureInfo.CurrentCulture)));
        }

        if (_problems.Count > 0)
        {
            Cell(_statusLeft, "● " + Loc.Plural(_problems.Count, AppStrings.Editor_Problems_One, AppStrings.Editor_Problems_Other), tip: _problems[0].Message, click: _ => GoToProblem(), brush: "Brush.InformationLineDanger");
        }

        if (IsModified)
        {
            Cell(_statusLeft, AppStrings.Editor_Modified, accent: true);
        }

        Cell(_statusRight, LanguageName(_language), tip: AppStrings.Editor_KindTip, click: ShowKindMenu);
        Cell(_statusRight, IndentName(_indent), tip: AppStrings.Editor_IndentTip, click: ShowIndentMenu);
        Cell(_statusRight, _file.EncodingName, tip: AppStrings.Editor_EncodingTip, click: ShowEncodingMenu);
        Cell(_statusRight, _lineEnding switch { LineEnding.CrLf => "CRLF", LineEnding.Lf => "LF", _ => AppStrings.Editor_Mixed }, tip: AppStrings.Editor_EolTip, click: ShowEolMenu);
        Cell(_statusRight, Size(_file.Size));
        _strip?.InvalidateVisual();
    }

    private static void Cell(Panel row, string text, bool accent = false, string? tip = null, Action<Control>? click = null, string? brush = null)
    {
        var block = new TextBlock { Text = text, FontSize = 12, VerticalAlignment = VerticalAlignment.Center }
            .Res(TextBlock.ForegroundProperty, brush ?? (accent ? "Brush.InformationLineAccent" : "Brush.InformationLineDimText"));
        var cell = new Border { Padding = new Thickness(12, 0), BorderThickness = new Thickness(0, 0, 1, 0), Child = block }
            .Res(Border.BorderBrushProperty, "Brush.Border");
        if (click is not null)
        {
            cell.Cursor = new Cursor(StandardCursorType.Hand);
            cell.Background = Brushes.Transparent;
            cell.PointerEntered += (_, _) => cell.Res(Border.BackgroundProperty, "Brush.InformationLineHover");
            cell.PointerExited += (_, _) => cell.Background = Brushes.Transparent;
            cell.PointerReleased += (_, e) =>
            {
                if (e.InitialPressMouseButton == MouseButton.Left)
                {
                    click(cell);
                }
            };
            cell.Tag = click;
        }

        if (tip is not null)
        {
            ToolTip.SetTip(cell, tip);
        }

        row.Children.Add(cell);
    }

    internal static string LanguageName(SyntaxLanguage language) => language switch
    {
        SyntaxLanguage.Json => "JSON",
        SyntaxLanguage.Yaml => "YAML",
        SyntaxLanguage.Xml => "XML",
        SyntaxLanguage.Html => "HTML",
        SyntaxLanguage.Markdown => "Markdown",
        _ => AppStrings.Editor_PlainKind,
    };

    private static string IndentName(IndentStyle indent) => indent switch
    {
        IndentStyle.Spaces2 => Loc.Format(AppStrings.Editor_Spaces, 2),
        IndentStyle.Spaces4 => Loc.Format(AppStrings.Editor_Spaces, 4),
        _ => AppStrings.Editor_Tabs,
    };

    private static MenuFlyout Menu(string heading)
    {
        var menu = new MenuFlyout { Placement = PlacementMode.TopEdgeAlignedRight };
        menu.Items.Add(new MenuItem { Header = heading, IsEnabled = false, FontWeight = FontWeight.SemiBold });
        return menu;
    }

    private static MenuItem Radio(string header, bool on, Action click, string? note = null)
    {
        var item = new MenuItem { Header = header, ToggleType = MenuItemToggleType.Radio, IsChecked = on };
        if (note is not null)
        {
            item.InputGesture = null;
            ToolTip.SetTip(item, note);
        }

        item.Click += (_, _) => click();
        return item;
    }

    private void ShowKindMenu(Control at)
    {
        var menu = Menu(AppStrings.Editor_KindTip);
        foreach (var language in EditorSettings.ColorChoices)
        {
            menu.Items.Add(Radio(LanguageName(language), language == _language, () => SetLanguage(language)));
        }

        menu.ShowAt(at);
    }

    private void ShowIndentMenu(Control at)
    {
        var menu = Menu(AppStrings.Editor_IndentTip);
        foreach (var indent in new[] { IndentStyle.Spaces2, IndentStyle.Spaces4, IndentStyle.Tabs })
        {
            menu.Items.Add(Radio(IndentName(indent), indent == _indent, () => SetIndent(indent)));
        }

        menu.Items.Add(new Separator());
        var convert = new MenuItem { Header = AppStrings.Editor_ConvertTo };
        ToolTip.SetTip(convert, AppStrings.Editor_ConvertToTip);
        foreach (var indent in new[] { IndentStyle.Spaces2, IndentStyle.Spaces4, IndentStyle.Tabs })
        {
            var item = new MenuItem { Header = IndentName(indent), IsEnabled = indent != _indent };
            item.Click += (_, _) => ConvertIndentation(indent);
            convert.Items.Add(item);
        }

        menu.Items.Add(convert);

        menu.ShowAt(at);
    }

    private void ShowEncodingMenu(Control at)
    {
        var menu = Menu(AppStrings.Editor_SaveWithEncoding);
        void Choose(TextEncoding encoding, bool bom)
        {
            _file = _file with { Encoding = encoding, Bom = bom };
            Changed();
        }

        var utf8 = TextEncodings.Default;
        menu.Items.Add(Radio(utf8.Name, _file.Encoding.IsUtf8 && !_file.Bom, () => Choose(utf8, false)));
        menu.Items.Add(Radio(Salandual.Core.Resources.Strings.Editor_Utf8Bom, _file.Encoding.IsUtf8 && _file.Bom, () => Choose(utf8, true)));
        var more = new MenuItem { Header = AppStrings.Editor_OtherEncodings };
        foreach (var e in TextEncodings.All.Skip(1))
        {
            var bom = e.Unit == 2;
            var item = Radio(e.Name, e.Id == _file.Encoding.Id, () => Choose(e, bom));
            if (e.Id == TextEncodings.Ansi.Id || e.Unit == 2)
            {
                menu.Items.Add(item);
            }
            else
            {
                more.Items.Add(item);
            }
        }

        menu.Items.Add(more);
        menu.Items.Add(new Separator());
        var reopen = new MenuItem { Header = AppStrings.Editor_ReopenWithEncoding, IsEnabled = _file.WriteTimeUtc != default };
        foreach (var e in TextEncodings.All)
        {
            var item = new MenuItem { Header = e.Name };
            item.Click += (_, _) => _ = ReopenWithAsync(e);
            reopen.Items.Add(item);
        }

        menu.Items.Add(reopen);
        menu.ShowAt(at);
    }

    private void ShowEolMenu(Control at)
    {
        var menu = Menu(AppStrings.Editor_EolTip);
        menu.Items.Add(Radio("CRLF", _lineEnding == LineEnding.CrLf, () => SetLineEnding(LineEnding.CrLf), AppStrings.Editor_CrLfNote));
        menu.Items.Add(Radio("LF", _lineEnding == LineEnding.Lf, () => SetLineEnding(LineEnding.Lf), AppStrings.Editor_LfNote));
        menu.Items.Add(new Separator());
        menu.Items.Add(new MenuItem { Header = AppStrings.Editor_MixedNote, IsEnabled = false });
        menu.ShowAt(at);
    }

    internal void SetLineEnding(LineEnding lineEnding)
    {
        _lineEnding = lineEnding;
        Changed();
    }

    private void Changed()
    {
        _editor.Document.UndoStack.DiscardOriginalFileMarker();
        UpdateModified();
    }

    private async Task ReopenWithAsync(TextEncoding encoding)
    {
        if (IsModified)
        {
            var answer = await Ask(AppStrings.Editor_Reopen_Title, [AppStrings.Editor_Reopen_Text],
                [new("reopen", AppStrings.Editor_Reopen_Button, Primary: true), new("cancel", AppStrings.Common_Cancel, "Esc")]);
            if (answer != "reopen")
            {
                return;
            }
        }

        await ReloadAsync(encoding.Id);
    }

    private sealed class MatchMarks : IBackgroundRenderer
    {
        public IReadOnlyList<(int Start, int Length)> Matches { get; set; } = [];

        public KnownLayer Layer => KnownLayer.Selection;

        public void Draw(TextView textView, DrawingContext drawingContext)
        {
            if (Matches.Count == 0 || textView.VisualLines.Count == 0)
            {
                return;
            }

            var first = textView.VisualLines[0].FirstDocumentLine.Offset;
            var last = textView.VisualLines[^1].LastDocumentLine.EndOffset;
            var match = textView.TryFindResource("Brush.ViewerMatch", textView.ActualThemeVariant, out var m) && m is IBrush b ? b : Brushes.Yellow;
            var index = FirstAtOrAfter(first);
            for (var i = index; i < Matches.Count && Matches[i].Start <= last; i++)
            {
                var segment = new AvaloniaEdit.Document.TextSegment { StartOffset = Matches[i].Start, Length = Matches[i].Length };
                foreach (var rect in BackgroundGeometryBuilder.GetRectsForSegment(textView, segment))
                {
                    drawingContext.FillRectangle(match, rect);
                }
            }
        }

        private int FirstAtOrAfter(int offset)
        {
            int lo = 0, hi = Matches.Count;
            while (lo < hi)
            {
                var mid = (lo + hi) / 2;
                if (Matches[mid].Start + Matches[mid].Length < offset)
                {
                    lo = mid + 1;
                }
                else
                {
                    hi = mid;
                }
            }

            return lo;
        }
    }

    private sealed class EditorStrip : Control
    {
        private readonly EditorWindow _owner;

        public EditorStrip(EditorWindow owner)
        {
            _owner = owner;
            Cursor = new Cursor(StandardCursorType.Hand);
        }

        public override void Render(DrawingContext context)
        {
            var bounds = new Rect(Bounds.Size);
            context.FillRectangle(Brush("Brush.CodeSurface"), bounds);
            context.FillRectangle(Brush("Brush.Border"), new Rect(0, 0, 1, bounds.Height));
            var editor = _owner._editor;
            var lines = Math.Max(1, editor.Document.LineCount);
            var view = editor.TextArea.TextView;
            if (view.VisualLinesValid && view.VisualLines.Count > 0)
            {
                var top = view.VisualLines[0].FirstDocumentLine.LineNumber - 1;
                var bottom = view.VisualLines[^1].LastDocumentLine.LineNumber;
                context.FillRectangle(Brush("Brush.Hover"), new Rect(1, top * bounds.Height / lines, bounds.Width - 1, Math.Max(4, (bottom - top) * bounds.Height / lines)));
            }

            var match = Brush("Brush.ViewerCurrentMatch");
            var document = editor.Document;
            var lastY = -10.0;
            foreach (var (start, _) in _owner._matches)
            {
                var y = (document.GetLineByOffset(Math.Min(start, document.TextLength)).LineNumber - 1) * bounds.Height / lines;
                if (y - lastY >= 2)
                {
                    context.FillRectangle(match, new Rect(3, y, bounds.Width - 5, 2));
                    lastY = y;
                }
            }

            var danger = Brush("Brush.Danger");
            foreach (var problem in _owner._problems)
            {
                context.FillRectangle(danger, new Rect(1, (problem.Line - 1) * bounds.Height / lines, bounds.Width - 1, 3));
            }

            var caret = (editor.TextArea.Caret.Line - 1) * bounds.Height / lines;
            context.FillRectangle(Brush("Brush.Text"), new Rect(2, caret, bounds.Width - 3, 2));
        }

        protected override void OnPointerPressed(PointerPressedEventArgs e)
        {
            base.OnPointerPressed(e);
            var editor = _owner._editor;
            var line = Math.Clamp((int)(e.GetPosition(this).Y / Math.Max(1, Bounds.Height) * editor.Document.LineCount) + 1, 1, editor.Document.LineCount);
            editor.ScrollTo(line, 1);
            e.Handled = true;
        }

        private IBrush Brush(string key) => this.TryFindResource(key, ActualThemeVariant, out var b) && b is IBrush brush ? brush : Brushes.Gray;
    }
}

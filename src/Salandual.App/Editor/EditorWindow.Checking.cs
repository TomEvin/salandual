// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.App/Editor/EditorWindow.Checking.cs

using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using AvaloniaEdit.Editing;
using AvaloniaEdit.Folding;
using AvaloniaEdit.Rendering;
using Salandual.App.Options;
using Salandual.App.Resources;
using Salandual.Core.Editing;
using Salandual.Core.Resources;
using Salandual.Core.Viewer;
using Salandual.Platform.Windows.FileSystem;

namespace Salandual.App.Editor;

internal sealed partial class EditorWindow
{
    private readonly DispatcherTimer _checkDelay = new();
    private readonly ProblemMarks _problemMarks = new();
    private readonly Border _problemsLine = new() { IsVisible = false, BorderThickness = new Thickness(0, 1, 0, 0) };
    private readonly TextBlock _problemsText = new() { FontSize = 12, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
    private ProblemMargin? _problemMargin;
    private FoldingManager? _folding;
    private IReadOnlyList<TextProblem> _problems = [];
    private CheckKind _checkKind;
    private int _checkVersion;
    private bool _checked;

    private enum CheckKind { None, Json, Yaml, Xml }

    internal IReadOnlyList<TextProblem> Problems => _problems;

    internal Task Checking { get; private set; } = Task.CompletedTask;

    internal int FoldCount => _folding?.AllFoldings.Count() ?? 0;

    internal FoldingManager? Folding => _folding;

    internal Border ProblemsLine => _problemsLine;

    private Control BuildProblemsLine()
    {
        var dot = new TextBlock { Text = "●", FontSize = 12, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) }.Res(TextBlock.ForegroundProperty, "Brush.Danger");
        _problemsText.Res(TextBlock.ForegroundProperty, "Brush.Text");
        var go = LinkButton(AppStrings.Viewer_GoTo, GoToProblem);
        go.Padding = new Thickness(10, 1);
        var dock = new DockPanel { Margin = new Thickness(10, 3) };
        DockPanel.SetDock(dot, Dock.Left);
        DockPanel.SetDock(go, Dock.Right);
        dock.Children.Add(dot);
        dock.Children.Add(go);
        dock.Children.Add(_problemsText);
        _problemsLine.Child = dock;
        _problemsLine.Res(Border.BackgroundProperty, "Brush.DangerBackground").Res(Border.BorderBrushProperty, "Brush.Border");
        _editor.TextArea.TextView.BackgroundRenderers.Add(_problemMarks);
        _checkDelay.Tick += (_, _) =>
        {
            _checkDelay.Stop();
            Checking = CheckAsync();
        };
        return _problemsLine;
    }

    private CheckKind KindToCheck() => !Options.Check && !Options.Folding ? CheckKind.None : _language switch
    {
        SyntaxLanguage.Json => CheckKind.Json,
        SyntaxLanguage.Yaml => CheckKind.Yaml,
        SyntaxLanguage.Xml => CheckKind.Xml,
        _ => CheckKind.None,
    };

    private void RestartChecking()
    {
        _checkKind = KindToCheck();
        var folding = _checkKind != CheckKind.None && Options.Folding;
        if (folding && _folding is null)
        {
            _folding = FoldingManager.Install(_editor.TextArea);
            FoldingElementGenerator.TextBrush = Brush("Brush.TertiaryText");
            StyleFoldingMargin();
        }
        else if (!folding && _folding is not null)
        {
            FoldingManager.Uninstall(_folding);
            _folding = null;
        }

        if (_checkKind != CheckKind.None && Options.Check)
        {
            if (_problemMargin is null)
            {
                _problemMargin = new ProblemMargin(this);
                _editor.TextArea.LeftMargins.Insert(0, _problemMargin);
            }
        }
        else if (_problemMargin is not null)
        {
            _editor.TextArea.LeftMargins.Remove(_problemMargin);
            _problemMargin = null;
        }

        _checked = false;
        ShowProblems([]);
        if (_checkKind != CheckKind.None)
        {
            Checking = CheckAsync();
        }
    }

    private void StyleFoldingMargin()
    {
        foreach (var margin in _editor.TextArea.LeftMargins.OfType<FoldingMargin>())
        {
            margin.Res(FoldingMargin.FoldingMarkerBrushProperty, "Brush.TertiaryText");
            margin.Res(FoldingMargin.FoldingMarkerBackgroundBrushProperty, "Brush.CodeSurface");
            margin.Res(FoldingMargin.SelectedFoldingMarkerBrushProperty, "Brush.Accent");
            margin.Res(FoldingMargin.SelectedFoldingMarkerBackgroundBrushProperty, "Brush.CodeSurface");
            margin.Margin = new Thickness(0, 0, 4, 0);
        }
    }

    private void CheckLater()
    {
        if (_checkKind == CheckKind.None)
        {
            return;
        }

        _checkVersion++;
        _checkDelay.Stop();
        _checkDelay.Interval = TimeSpan.FromMilliseconds(_checkKind == CheckKind.Yaml ? 1000 : 300);
        _checkDelay.Start();
    }

    internal async Task CheckAsync()
    {
        var kind = _checkKind;
        if (kind == CheckKind.None)
        {
            return;
        }

        var version = ++_checkVersion;
        var snapshot = _editor.Document.CreateSnapshot();
        var comments = JsonText.AllowsComments(WindowsPath.GetName(_path));
        var result = await Task.Run(() =>
        {
            var text = snapshot.Text;
            return kind switch
            {
                CheckKind.Json => JsonText.Check(text, comments),
                CheckKind.Yaml => YamlText.Check(text),
                _ => XmlMarkup.Check(text),
            };
        });
        if (version != _checkVersion || kind != _checkKind)
        {
            return;
        }

        _checked = true;
        ShowProblems(Options.Check ? result.Problems : []);
        if (_folding is not null)
        {
            var length = _editor.Document.TextLength;
            var folds = result.Folds
                .Where(f => f.Start < f.End && f.End <= length)
                .Select(f => new NewFolding(f.Start, f.End) { Name = Loc.Plural(Math.Max(1, f.Lines), AppStrings.Editor_FoldedLines_One, AppStrings.Editor_FoldedLines_Other) })
                .OrderBy(f => f.StartOffset)
                .ToList();
            _folding.UpdateFoldings(folds, result.Problems.Count > 0 ? result.Problems[0].Offset : -1);
        }
    }

    private void ShowProblems(IReadOnlyList<TextProblem> problems)
    {
        _problems = problems;
        _problemMarks.Problems = problems;
        _problemMargin?.InvalidateVisual();
        _editor.TextArea.TextView.InvalidateLayer(KnownLayer.Selection);
        _strip?.InvalidateVisual();
        if (problems.Count > 0)
        {
            var first = problems[0];
            _problemsText.Inlines!.Clear();
            _problemsText.Inlines.Add(new Avalonia.Controls.Documents.Run(Loc.Format(AppStrings.Editor_ProblemAt, first.Line, first.Column) + " ") { FontWeight = FontWeight.SemiBold });
            _problemsText.Inlines.Add(new Avalonia.Controls.Documents.Run(first.Message));
            ToolTip.SetTip(_problemsText, first.Message);
        }

        _problemsLine.IsVisible = problems.Count > 0;
        UpdateToolbar();
        UpdateStatus();
    }

    internal string? CheckText => _checkKind == CheckKind.None || !Options.Check ? null
        : _problems.Count > 0 ? Loc.Plural(_problems.Count, AppStrings.Editor_Problems_One, AppStrings.Editor_Problems_Other)
        : !_checked ? AppStrings.Editor_Checking
        : _checkKind switch { CheckKind.Json => AppStrings.Editor_ValidJson, CheckKind.Yaml => AppStrings.Editor_ValidYaml, _ => AppStrings.Editor_WellFormedXml };

    internal void GoToProblem()
    {
        if (_problems.Count == 0)
        {
            return;
        }

        var p = _problems[0];
        var offset = Math.Min(p.Offset, _editor.Document.TextLength);
        _editor.Select(offset, Math.Min(p.Length, _editor.Document.TextLength - offset));
        var location = _editor.Document.GetLocation(offset);
        _editor.ScrollTo(location.Line, location.Column);
        _editor.Focus();
    }

    internal void FoldAll(bool fold)
    {
        if (_folding is null)
        {
            return;
        }

        foreach (var section in _folding.AllFoldings)
        {
            section.IsFolded = fold;
        }

        _editor.TextArea.TextView.Redraw();
    }

    private IBrush Brush(string key) => this.TryFindResource(key, ActualThemeVariant, out var b) && b is IBrush brush ? brush : Brushes.Gray;

    private sealed class ProblemMarks : IBackgroundRenderer
    {
        public IReadOnlyList<TextProblem> Problems { get; set; } = [];

        public KnownLayer Layer => KnownLayer.Selection;

        public void Draw(TextView textView, DrawingContext drawingContext)
        {
            if (Problems.Count == 0 || !textView.VisualLinesValid || textView.VisualLines.Count == 0)
            {
                return;
            }

            var brush = textView.TryFindResource("Brush.Danger", textView.ActualThemeVariant, out var b) && b is IBrush danger ? danger : Brushes.Red;
            var pen = new Pen(brush, 1.2);
            var length = textView.Document.TextLength;
            foreach (var problem in Problems)
            {
                var start = Math.Min(problem.Offset, length);
                var segment = new AvaloniaEdit.Document.TextSegment { StartOffset = start, Length = Math.Max(0, Math.Min(Math.Max(1, problem.Length), length - start)) };
                var rects = BackgroundGeometryBuilder.GetRectsForSegment(textView, segment).ToList();
                if (rects.Count == 0 && textView.GetVisualLine(textView.Document.GetLineByOffset(start).LineNumber) is { } line)
                {
                    var at = line.GetVisualPosition(line.GetVisualColumn(start - line.FirstDocumentLine.Offset), VisualYPosition.LineBottom);
                    rects.Add(new Rect(at.X - textView.ScrollOffset.X, at.Y - textView.ScrollOffset.Y - 2, 8, 2));
                }

                foreach (var rect in rects)
                {
                    var y = rect.Bottom - 1.5;
                    var geometry = new StreamGeometry();
                    using (var g = geometry.Open())
                    {
                        g.BeginFigure(new Point(rect.Left, y), false);
                        for (var x = rect.Left; x < Math.Max(rect.Right, rect.Left + 6); x += 3)
                        {
                            g.LineTo(new Point(x + 1.5, y - 2));
                            g.LineTo(new Point(x + 3, y));
                        }

                        g.EndFigure(false);
                    }

                    drawingContext.DrawGeometry(null, pen, geometry);
                }
            }
        }
    }

    private sealed class ProblemMargin : AbstractMargin
    {
        private readonly EditorWindow _owner;

        public ProblemMargin(EditorWindow owner)
        {
            _owner = owner;
            Cursor = new Cursor(StandardCursorType.Hand);
        }

        protected override Size MeasureOverride(Size availableSize) => new(12, 0);

        protected override void OnTextViewChanged(TextView? oldTextView, TextView? newTextView)
        {
            if (oldTextView is not null)
            {
                oldTextView.VisualLinesChanged -= Changed;
            }

            base.OnTextViewChanged(oldTextView, newTextView);
            if (newTextView is not null)
            {
                newTextView.VisualLinesChanged += Changed;
            }
        }

        private void Changed(object? sender, EventArgs e) => InvalidateVisual();

        public override void Render(DrawingContext context)
        {
            context.FillRectangle(Brushes.Transparent, new Rect(Bounds.Size));
            if (TextView is not { VisualLinesValid: true } view || _owner._problems.Count == 0)
            {
                return;
            }

            var lines = _owner._problems.Select(p => p.Line).ToHashSet();
            var brush = _owner.Brush("Brush.Danger");
            foreach (var visual in view.VisualLines)
            {
                if (lines.Contains(visual.FirstDocumentLine.LineNumber))
                {
                    var y = visual.VisualTop - view.VerticalOffset + (visual.Height / 2);
                    context.DrawEllipse(brush, null, new Point(7, y), 3.5, 3.5);
                }
            }
        }

        protected override void OnPointerPressed(PointerPressedEventArgs e)
        {
            base.OnPointerPressed(e);
            _owner.GoToProblem();
            e.Handled = true;
        }
    }
}

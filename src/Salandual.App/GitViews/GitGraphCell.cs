// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.App/GitViews/GitGraphCell.cs

using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Salandual.Core.Git;

namespace Salandual.App.GitViews;

internal sealed class GitGraphCell : Control
{
    public const double LaneWidth = 14;

    private static readonly string[] _colors = ["Brush.Accent", "Brush.Success", "Brush.Warning", "Brush.Danger", "Brush.Folder", "Brush.SecondaryText"];

    public GitGraphCell(GitGraphRow row, IReadOnlyList<GitGraphLine> above, bool isHead, int lanes)
    {
        Row = row;
        Above = above;
        IsHead = isHead;
        Width = 8 + (Math.Max(1, lanes) * LaneWidth);
    }

    public GitGraphRow Row { get; }

    public IReadOnlyList<GitGraphLine> Above { get; }

    public bool IsHead { get; }

    private static double X(int lane) => 8 + (lane * LaneWidth);

    private IBrush Brush(int color)
        => this.TryFindResource(_colors[color % _colors.Length], ActualThemeVariant, out var found) && found is IBrush b ? b : Brushes.Gray;

    public override void Render(DrawingContext context)
    {
        var h = Bounds.Height;
        var cy = h / 2;
        foreach (var line in Above)
        {
            var to = X(line.To);
            var mid = (X(line.From) + to) / 2;
            Draw(context, line.Color, new Point(mid, 0), new Point(to, cy), bendFirst: false);
        }

        foreach (var line in Row.Lines)
        {
            var from = X(line.From);
            var mid = (from + X(line.To)) / 2;
            Draw(context, line.Color, new Point(from, cy), new Point(mid, h), bendFirst: true);
        }

        var brush = Brush(Row.Color);
        var background = this.TryFindResource("Brush.PanelBackground", ActualThemeVariant, out var bg) && bg is IBrush b ? b : Brushes.White;
        context.DrawEllipse(IsHead ? brush : background, new Pen(brush, 2), new Point(X(Row.Lane), cy), 4, 4);
    }

    private void Draw(DrawingContext context, int color, Point a, Point b, bool bendFirst)
    {
        var pen = new Pen(Brush(color), 2, lineCap: PenLineCap.Round);
        if (Math.Abs(a.X - b.X) < 0.5)
        {
            context.DrawLine(pen, a, b);
            return;
        }

        var curve = new StreamGeometry();
        using (var g = curve.Open())
        {
            g.BeginFigure(a, false);
            g.QuadraticBezierTo(bendFirst ? new Point(a.X, b.Y) : new Point(b.X, a.Y), b);
            g.EndFigure(false);
        }

        context.DrawGeometry(null, pen, curve);
    }
}

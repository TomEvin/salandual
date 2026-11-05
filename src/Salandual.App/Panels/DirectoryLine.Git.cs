// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.App/Panels/DirectoryLine.Git.cs

using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Salandual.App.Resources;
using Salandual.Core.Resources;

namespace Salandual.App.Panels;

public sealed record GitFacts(string Label, int Ahead, int Behind, string? Upstream, bool Detached);

public sealed partial class DirectoryLine
{
    private GitFacts? _git;

    public GitFacts? Git
    {
        get => _git;
        set
        {
            if (_git != value)
            {
                _git = value;
                InvalidateVisual();
            }
        }
    }

    public event EventHandler<Point>? BranchMenuRequested;

    internal Rect BranchBounds { get; private set; }

    internal static string AheadBehind(GitFacts git)
        => string.Join(" ", new[] { git.Ahead > 0 ? "↑" + git.Ahead.ToString(System.Globalization.CultureInfo.CurrentCulture) : "", git.Behind > 0 ? "↓" + git.Behind.ToString(System.Globalization.CultureInfo.CurrentCulture) : "" }.Where(s => s.Length > 0));

    internal string GitTip()
    {
        if (_git is not { } git)
        {
            return "";
        }

        var where = git.Detached ? Loc.Format(AppStrings.Git_Tip_Detached, git.Label.Trim('(', ')'))
            : git.Upstream is null ? Loc.Format(AppStrings.Git_Tip_NoUpstream, git.Label)
            : Loc.Format(AppStrings.Git_Tip_Branch, git.Label, Loc.Plural(git.Ahead, AppStrings.Git_ToPush_One, AppStrings.Git_ToPush_Other), Loc.Plural(git.Behind, AppStrings.Git_ToPull_One, AppStrings.Git_ToPull_Other), git.Upstream);
        return where + Environment.NewLine + AppStrings.Git_Tip_Click;
    }

    private double DrawGit(DrawingContext context, Size size, double right, double buttonTop)
    {
        var git = _git!;
        var small = Math.Max(9, FontSize - 1);
        var hover = _hoverZone == Zone.Branch;
        var name = Text(git.Label, _text, size: small);
        var counts = AheadBehind(git);
        var numbers = counts.Length > 0 ? Text(counts, _dim, size: small) : null;
        const double icon = 12;
        var width = 8 + icon + 6 + name.Width + (numbers is null ? 0 : 6 + numbers.Width) + 8;
        var box = new Rect(right - width, buttonTop + 1, width, ButtonSize - 2);
        if (hover)
        {
            context.FillRectangle(_hoverBg, box, (float)Salandual.App.Theme.Corners.ChipRadius);
        }

        context.DrawRectangle(null, new ImmutablePen(_track.ToImmutable(), 1), box.Deflate(0.5), Salandual.App.Theme.Corners.ChipRadius, Salandual.App.Theme.Corners.ChipRadius);
        Salandual.App.Theme.AppIcons.Draw(context, "Icon.GitBranch", new Point(box.X + 8, box.Center.Y - (icon / 2)), new ImmutablePen(_dim.ToImmutable(), 1.2, lineCap: PenLineCap.Round), scale: icon / 16);
        var textX = box.X + 8 + icon + 6;
        context.DrawText(name, new Point(textX, (size.Height - name.Height) / 2));
        if (numbers is not null)
        {
            context.DrawText(numbers, new Point(textX + name.Width + 6, (size.Height - numbers.Height) / 2));
        }

        BranchBounds = box;
        return box.X - 6;
    }


    internal void ClickBranch() => BranchMenuRequested?.Invoke(this, BranchBounds.BottomLeft);
}

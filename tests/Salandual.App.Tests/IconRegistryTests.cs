// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : tests/Salandual.App.Tests/IconRegistryTests.cs

using System.Text.RegularExpressions;
using Avalonia.Headless.XUnit;
using Salandual.App.Theme;

namespace Salandual.App.Tests;

public sealed partial class IconRegistryTests
{
    private static readonly HashSet<string> _drawsCharts = ["UsageRing.cs", "SpeedGraph.cs", "BusyRing.cs", "EditorWindow.Checking.cs", "SpaceChart.cs"];

    [Fact]
    public void Outlines_live_only_in_the_icon_table()
    {
        var found = new List<string>();
        foreach (var file in AppSourceFiles().Where(f => Path.GetFileName(f) != "AppIcons.cs"))
        {
            var lines = File.ReadAllLines(file);
            for (var i = 0; i < lines.Length; i++)
            {
                if (PathData().IsMatch(lines[i]) || GeometryParse().IsMatch(lines[i])
                    || (Drawing().IsMatch(lines[i]) && !_drawsCharts.Contains(Path.GetFileName(file))))
                {
                    found.Add($"{Path.GetFileName(file)}:{i + 1}: {lines[i].Trim()}");
                }
            }
        }

        Assert.True(found.Count == 0, "Icon outline outside Theme/AppIcons.cs:\n" + string.Join("\n", found));
    }

    [AvaloniaFact]
    public void Every_icon_name_in_the_code_is_in_the_table()
    {
        var names = AppIcons.Names.ToHashSet(StringComparer.Ordinal);
        var missing = AppSourceFiles()
            .SelectMany(f => IconName().Matches(File.ReadAllText(f)).Select(m => m.Groups[1].Value))
            .Where(n => !names.Contains(n))
            .Distinct()
            .ToList();
        Assert.True(missing.Count == 0, "Not in AppIcons: " + string.Join(", ", missing));
    }

    [AvaloniaFact]
    public void Every_icon_fits_its_box()
    {
        foreach (var name in AppIcons.Names)
        {
            foreach (var part in AppIcons.Get(name).Parts)
            {
                var bounds = part.Geometry.Bounds;
                Assert.True(bounds.Width > 0 || bounds.Height > 0, name);
                Assert.True(bounds.X >= 0 && bounds.Y >= 0 && bounds.Right <= 26 && bounds.Bottom <= 26, $"{name}: {bounds}");
            }
        }
    }

    [GeneratedRegex(@"""(?:F[01] )?[Mm] ?-?\d+(?:\.\d+)?[ ,]")]
    private static partial Regex PathData();

    [GeneratedRegex(@"\bGeometry\.Parse\(")]
    private static partial Regex GeometryParse();

    [GeneratedRegex(@"\.BeginFigure\(|\.DrawEllipse\(|new StreamGeometry\(")]
    private static partial Regex Drawing();

    [GeneratedRegex(@"""(Icon\.[A-Za-z]+)""")]
    private static partial Regex IconName();

    private static IEnumerable<string> AppSourceFiles()
        => Directory.EnumerateFiles(Path.Combine(RepoRoot(), "src", "Salandual.App"), "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal));

    private static string RepoRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "Salandual.slnx")))
            {
                return dir.FullName;
            }
        }

        throw new InvalidOperationException("Repository root (Salandual.slnx) not found.");
    }
}

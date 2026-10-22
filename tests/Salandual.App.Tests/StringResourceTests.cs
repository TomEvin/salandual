// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : tests/Salandual.App.Tests/StringResourceTests.cs

using System.Globalization;
using System.Resources;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Salandual.App.Resources;
using Salandual.Core.Commands;
using Salandual.Core.Resources;

namespace Salandual.App.Tests;

public sealed partial class StringResourceTests
{
    [Fact]
    public void Every_command_has_a_title_and_a_category_name()
    {
        foreach (var command in DefaultCommands.All())
        {
            Assert.NotEqual(command.Id, command.Title);
            Assert.NotNull(Strings.ResourceManager.GetString("CommandCategory_" + command.Category, CultureInfo.InvariantCulture));
        }
    }

    [Fact]
    public void Every_menu_item_has_a_header()
    {
        foreach (var node in MainMenu.Flatten(MainMenu.Create()))
        {
            Assert.False(string.IsNullOrWhiteSpace(node.Header), node.CommandId);
        }
    }

    [Theory]
    [InlineData("src/Salandual.Core/Resources/Strings.resx")]
    [InlineData("src/Salandual.App/Resources/AppStrings.resx")]
    public void Placeholders_are_numbered_from_zero_without_gaps(string resx)
    {
        foreach (var (key, value) in Entries(resx))
        {
            var used = Placeholder().Matches(value).Select(m => int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture)).Distinct().Order().ToList();
            if (used.Count == 0)
            {
                continue;
            }

            var expected = Enumerable.Range(used[0], used.Count).ToList();
            Assert.True(used.SequenceEqual(expected) && (used[0] == 0 || key.EndsWith("_One", StringComparison.Ordinal)), $"{key}: {value}");
        }
    }

    [Fact]
    public void Every_resource_text_is_used()
    {
        var code = string.Join('\n', SourceFiles().Select(File.ReadAllText));
        var unused = new List<string>();
        foreach (var resx in new[] { "src/Salandual.Core/Resources/Strings.resx", "src/Salandual.App/Resources/AppStrings.resx" })
        {
            foreach (var (key, _) in Entries(resx))
            {
                if (key.StartsWith("Command", StringComparison.Ordinal) || key.StartsWith("Menu", StringComparison.Ordinal) || key.StartsWith("Place_", StringComparison.Ordinal))
                {
                    continue;
                }

                if (!code.Contains("Strings." + key, StringComparison.Ordinal))
                {
                    unused.Add(key);
                }
            }
        }

        Assert.True(unused.Count == 0, "Not used: " + string.Join(", ", unused));
    }

    [Fact]
    public void App_code_shows_no_literal_text()
    {
        var found = new List<string>();
        foreach (var file in SourceFiles().Where(f => f.Contains("Salandual.App", StringComparison.Ordinal)))
        {
            var lines = File.ReadAllLines(file);
            for (var i = 0; i < lines.Length; i++)
            {
                foreach (Match m in UiSink().Matches(lines[i]))
                {
                    var text = m.Groups["text"].Value;
                    var words = Hole().Replace(text, "");
                    if (Letters().IsMatch(words) && !_allowed.Contains(text))
                    {
                        found.Add($"{Path.GetFileName(file)}:{i + 1}: {text}");
                    }
                }
            }
        }

        Assert.True(found.Count == 0, "Literal text:\n" + string.Join("\n", found));
    }

    private static readonly HashSet<string> _allowed = ["Ctrl+F", "MB/s", "KiB, MiB", "SSD", "HDD"];

    [Fact]
    public void App_code_writes_no_sample_names()
    {
        var found = new List<string>();
        foreach (var file in SourceFiles().Where(f => f.Contains("Salandual.App", StringComparison.Ordinal)))
        {
            var lines = File.ReadAllLines(file);
            for (var i = 0; i < lines.Length; i++)
            {
                var line = lines[i].TrimStart();
                if (line.StartsWith("//", StringComparison.Ordinal))
                {
                    continue;
                }

                foreach (Match m in SampleSink().Matches(line))
                {
                    if (_cameraNames.Contains(m.Groups["text"].Value))
                    {
                        continue;
                    }

                    found.Add($"{Path.GetFileName(file)}:{i + 1}: {m.Groups["text"].Value}");
                }

                foreach (Match m in Literal().Matches(line))
                {
                    if (Accented().IsMatch(m.Value) && !_accentedAllowed.Contains(m.Value))
                    {
                        found.Add($"{Path.GetFileName(file)}:{i + 1}: {m.Value}");
                    }
                }
            }
        }

        Assert.True(found.Count == 0, "Sample names or accented text in the code (put them in AppStrings.resx):\n" + string.Join("\n", found));
    }

    private static readonly HashSet<string> _accentedAllowed = ["\"Nyaralás 2026 · 3 KB\""];

    private static readonly HashSet<string> _cameraNames = ["IMG_0412.HEIC", "IMG_0413.HEIC"];

    [GeneratedRegex(@"(?:\b(?:Folder|File)\(|\b[Ss]ample\w*\s*=\s*)\$?@?""(?<text>(?:[^""\\]|\\.)*)""")]
    private static partial Regex SampleSink();

    [GeneratedRegex(@"@""(?:[^""]|"""")*""|""(?:[^""\\]|\\.)*""")]
    private static partial Regex Literal();

    [GeneratedRegex("[À-ÖØ-öø-ž]")]
    private static partial Regex Accented();

    [GeneratedRegex(@"\{[^}]*\}")]
    private static partial Regex Hole();

    [GeneratedRegex(""""(?:\b(?:Text|Content|Title|Header|PlaceholderText|CenterText)\s*=\s*|new Run\(|ShowNotice\(|SetTip\([^,]+,\s*|SetName\([^,]+,\s*|\bText\(|Secondary\(|Link\(|ActionButton\()\$?@?"(?<text>(?:[^"\\]|\\.)*)"""")]
    private static partial Regex UiSink();

    [GeneratedRegex("[A-Za-zÀ-ž]{2,}")]
    private static partial Regex Letters();

    [GeneratedRegex(@"\{(\d+)(?::[^}]*)?\}")]
    private static partial Regex Placeholder();

    private static IEnumerable<(string Key, string Value)> Entries(string relative)
        => XDocument.Load(Path.Combine(RepoRoot(), relative)).Root!.Elements("data")
            .Select(d => ((string)d.Attribute("name")!, (string?)d.Element("value") ?? ""));

    private static IEnumerable<string> SourceFiles()
        => Directory.EnumerateFiles(Path.Combine(RepoRoot(), "src"), "*.cs", SearchOption.AllDirectories)
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

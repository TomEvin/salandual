// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.Core/Settings/SessionStore.cs

using System.Text;
using Salandual.Core.Listing;
using Salandual.Core.Panels;

namespace Salandual.Core.Settings;

public sealed record SavedTab(int Side, string Path, PanelView? View = null, SortSpec? Sort = null, bool Locked = false, NameColour Colour = NameColour.Normal, string? Filter = null, bool FilterFolders = false);

public sealed record SessionState(string? Left, string? Right, int Active, IReadOnlyDictionary<string, string> LastFolderPerDrive, IReadOnlyList<WorkingDirectory>? WorkingDirectories = null, PanelView? LeftView = null, PanelView? RightView = null)
{
    public IReadOnlyList<SavedTab> Tabs { get; init; } = [];

    public int LeftTab { get; init; }

    public int RightTab { get; init; }

    public static SessionState Empty { get; } = new(null, null, 0, new Dictionary<string, string>());
}

public sealed class SessionStore(string filePath)
{
    public const string FileName = "session.txt";

    public string FilePath { get; } = filePath;

    public static SessionStore InDirectory(string dataDirectory) => new(Path.Combine(dataDirectory, FileName));

    public SessionState Load()
    {
        string[] lines;
        try
        {
            lines = File.ReadAllLines(FilePath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return SessionState.Empty;
        }

        string? left = null, right = null;
        var active = 0;
        PanelView? leftView = null, rightView = null;
        var drives = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var working = new List<WorkingDirectory>();
        var tabs = new List<SavedTab>();
        int leftTab = 0, rightTab = 0;
        foreach (var line in lines)
        {
            var tab = line.IndexOf('\t');
            if (tab <= 0 || tab == line.Length - 1)
            {
                continue;
            }

            var key = line[..tab];
            var value = line[(tab + 1)..];
            switch (key)
            {
                case "left": left = value; break;
                case "right": right = value; break;
                case "active": active = value == "1" ? 1 : 0; break;
                case "view:0": leftView = ParseView(value); break;
                case "view:1": rightView = ParseView(value); break;
                case "work:0": working.Add(new WorkingDirectory(value, 0)); break;
                case "work:1": working.Add(new WorkingDirectory(value, 1)); break;
                case "tab:0" or "tab:1" when ParseTab(key[^1] - '0', value) is { } saved: tabs.Add(saved); break;
                case "tabon:0": leftTab = ParseIndex(value); break;
                case "tabon:1": rightTab = ParseIndex(value); break;
                default:
                    if (key.StartsWith("drive:", StringComparison.Ordinal) && key.Length > "drive:".Length)
                    {
                        drives[key["drive:".Length..]] = value;
                    }

                    break;
            }
        }

        return new SessionState(left, right, active, drives, working, leftView, rightView) { Tabs = tabs, LeftTab = leftTab, RightTab = rightTab };
    }

    private static int ParseIndex(string value)
        => int.TryParse(value, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var n) && n < 1000 ? n : 0;

    private static SavedTab? ParseTab(int side, string value)
    {
        var parts = value.Split('|', 8);
        if (parts.Length < 7 || parts[6].Length == 0)
        {
            return null;
        }

        SortSpec? sort = Enum.TryParse<SortColumn>(parts[1], ignoreCase: true, out var column) && Enum.IsDefined(column) && !int.TryParse(parts[1], out _)
            ? new SortSpec(column, parts[2] == "1")
            : null;
        var colour = Enum.TryParse<NameColour>(parts[4], ignoreCase: true, out var c) && Enum.IsDefined(c) && !int.TryParse(parts[4], out _) ? c : NameColour.Normal;
        var mask = parts.Length > 7 && parts[7].Length > 0 ? parts[7] : null;
        return new SavedTab(side, parts[6], ParseView(parts[0]), sort, parts[3] == "1", colour, mask, parts[5] == "1");
    }

    private static string TabLine(SavedTab tab)
        => string.Join('|', tab.View?.ToString() ?? "", tab.Sort?.Column.ToString() ?? "", tab.Sort is { Descending: true } ? "1" : "0", tab.Locked ? "1" : "0",
            tab.Colour == NameColour.Normal ? "" : tab.Colour.ToString(), tab.FilterFolders ? "1" : "0", tab.Path, tab.Filter ?? "");

    private static PanelView? ParseView(string value)
        => Enum.TryParse<PanelView>(value, ignoreCase: true, out var view) && Enum.IsDefined(view) && !int.TryParse(value, out _) ? view : null;

    public void Save(SessionState state)
    {
        var sb = new StringBuilder();
        void Line(string key, string? value)
        {
            if (!string.IsNullOrEmpty(value) && !value.Contains('\n') && !value.Contains('\t'))
            {
                sb.Append(key).Append('\t').Append(value).Append('\n');
            }
        }

        Line("left", state.Left);
        Line("right", state.Right);
        Line("active", state.Active == 1 ? "1" : "0");
        Line("view:0", state.LeftView?.ToString());
        Line("view:1", state.RightView?.ToString());
        foreach (var (drive, folder) in state.LastFolderPerDrive)
        {
            Line("drive:" + drive, folder);
        }

        foreach (var w in state.WorkingDirectories ?? [])
        {
            Line(w.Side == 1 ? "work:1" : "work:0", w.Path);
        }

        foreach (var tab in state.Tabs.Where(t => !t.Path.Contains('|')))
        {
            Line(tab.Side == 1 ? "tab:1" : "tab:0", TabLine(tab));
        }

        Line("tabon:0", state.LeftTab.ToString(System.Globalization.CultureInfo.InvariantCulture));
        Line("tabon:1", state.RightTab.ToString(System.Globalization.CultureInfo.InvariantCulture));

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            var temp = FilePath + ".tmp";
            File.WriteAllText(temp, sb.ToString());
            File.Move(temp, FilePath, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }
}

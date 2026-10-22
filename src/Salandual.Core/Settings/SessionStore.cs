// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.Core/Settings/SessionStore.cs

using System.Text;
using Salandual.Core.Panels;

namespace Salandual.Core.Settings;

public sealed record SessionState(string? Left, string? Right, int Active, IReadOnlyDictionary<string, string> LastFolderPerDrive, IReadOnlyList<WorkingDirectory>? WorkingDirectories = null)
{
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
        var drives = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var working = new List<WorkingDirectory>();
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
                case "work:0": working.Add(new WorkingDirectory(value, 0)); break;
                case "work:1": working.Add(new WorkingDirectory(value, 1)); break;
                default:
                    if (key.StartsWith("drive:", StringComparison.Ordinal) && key.Length > "drive:".Length)
                    {
                        drives[key["drive:".Length..]] = value;
                    }

                    break;
            }
        }

        return new SessionState(left, right, active, drives, working);
    }

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
        foreach (var (drive, folder) in state.LastFolderPerDrive)
        {
            Line("drive:" + drive, folder);
        }

        foreach (var w in state.WorkingDirectories ?? [])
        {
            Line(w.Side == 1 ? "work:1" : "work:0", w.Path);
        }

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

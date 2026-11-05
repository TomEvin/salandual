// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.Core/Diagnostics/AppLog.cs

using System.Globalization;

namespace Salandual.Core.Diagnostics;

public enum LogLevel
{
    Info,
    Warning,
    Error,
}

public sealed class AppLog(string? folder, TimeProvider? clock = null)
{
    public const int KeepDays = 30;

    public const int RecentCount = 20;

    private const string FilePrefix = "salandual-";
    private const string FileExtension = ".log";

    private readonly Lock _gate = new();
    private readonly Queue<string> _recent = new();
    private readonly TimeProvider _clock = clock ?? TimeProvider.System;

    public string? Folder { get; } = folder;

    public static AppLog Current { get; set; } = new(null);

    public static void Note(string text) => Current.Write(LogLevel.Info, text);

    public static void Warn(string text) => Current.Write(LogLevel.Warning, text);

    public static void Fail(string text) => Current.Write(LogLevel.Error, text);

    public DateTime Now => _clock.GetLocalNow().DateTime;

    public IReadOnlyList<string> Recent
    {
        get
        {
            lock (_gate)
            {
                return [.. _recent];
            }
        }
    }

    public static string FileName(DateTime day) => FilePrefix + day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + FileExtension;

    public static DateTime? DayOf(string fileName)
        => fileName.StartsWith(FilePrefix, StringComparison.OrdinalIgnoreCase) && fileName.EndsWith(FileExtension, StringComparison.OrdinalIgnoreCase)
           && DateTime.TryParseExact(fileName[FilePrefix.Length..^FileExtension.Length], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day)
            ? day
            : null;

    public static string Header(DateTime day) => Resources.Loc.Format(Resources.Strings.Log_Header, day.ToString("dddd d MMMM yyyy", Resources.Loc.TextCulture));

    public static string Word(LogLevel level) => level switch
    {
        LogLevel.Warning => "warning",
        LogLevel.Error => "error",
        _ => "info",
    };

    public static string Format(DateTime time, LogLevel level, string text)
        => time.ToString("HH:mm:ss", CultureInfo.InvariantCulture) + "  " + Word(level).PadRight(9) + text.ReplaceLineEndings(" ");

    public static LogLevel? LevelOf(string line)
    {
        if (line.Length < 11 || line[2] != ':' || line[5] != ':')
        {
            return null;
        }

        var word = line[10..].Split(' ', 2)[0];
        return word switch
        {
            "info" => LogLevel.Info,
            "warning" => LogLevel.Warning,
            "error" => LogLevel.Error,
            _ => null,
        };
    }

    public void Write(LogLevel level, string text)
    {
        var now = Now;
        var line = Format(now, level, text);
        lock (_gate)
        {
            _recent.Enqueue(line);
            while (_recent.Count > RecentCount)
            {
                _recent.Dequeue();
            }

            if (Folder is null)
            {
                return;
            }

            try
            {
                Directory.CreateDirectory(Folder);
                var file = Path.Combine(Folder, FileName(now));
                if (!File.Exists(file))
                {
                    File.AppendAllText(file, Header(now) + Environment.NewLine);
                }

                File.AppendAllText(file, line + Environment.NewLine);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
            }
        }
    }

    public int DeleteOld()
    {
        if (Folder is null || !Directory.Exists(Folder))
        {
            return 0;
        }

        var before = Now.Date.AddDays(-KeepDays);
        var deleted = 0;
        foreach (var file in OwnFiles(Folder))
        {
            try
            {
                var day = DayOf(Path.GetFileName(file)) ?? File.GetLastWriteTime(file).Date;
                if (day < before)
                {
                    File.Delete(file);
                    deleted++;
                }
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
            }
        }

        return deleted;
    }

    public static IEnumerable<string> OwnFiles(string folder)
        => Directory.Exists(folder)
            ? Directory.EnumerateFiles(folder).Where(f => DayOf(Path.GetFileName(f)) is not null || ProblemReports.IsReportName(Path.GetFileName(f)) || Path.GetFileName(f).Equals(ProblemReports.UnseenName, StringComparison.OrdinalIgnoreCase))
            : [];
}

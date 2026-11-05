// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.Core/Diagnostics/ProblemReports.cs

using System.Globalization;
using System.Text;
using Salandual.Core.Resources;

namespace Salandual.Core.Diagnostics;

public enum ProblemKind
{
    Crash,

    WentOn,
}

public sealed record ProblemReport(string Path, ProblemKind Kind, DateTime Time, string ExceptionType);

public static class ProblemReports
{
    public const string UnseenName = "unseen-crash.txt";

    private const string CrashPrefix = "crash-";
    private const string WentOnPrefix = "problem-";
    private const string StampFormat = "yyyy-MM-dd-HHmmss";

    public static bool IsReportName(string name) => Parse(WithoutNumber(name)) is not null;

    private static string WithoutNumber(string name) => Numbered(name) ? name[..^6] + ".txt" : name;

    private static bool Numbered(string name) => name.Length > 6 && name[^6] == '-' && char.IsDigit(name[^5]);

    private static (ProblemKind Kind, DateTime Time)? Parse(string name)
    {
        var kind = name.StartsWith(CrashPrefix, StringComparison.OrdinalIgnoreCase) ? ProblemKind.Crash
            : name.StartsWith(WentOnPrefix, StringComparison.OrdinalIgnoreCase) ? ProblemKind.WentOn
            : (ProblemKind?)null;
        if (kind is null || !name.EndsWith(".txt", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var stamp = name[(kind == ProblemKind.Crash ? CrashPrefix : WentOnPrefix).Length..^4];
        return DateTime.TryParseExact(stamp, StampFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var time) ? (kind.Value, time) : null;
    }

    public static string When(DateTime time)
        => Loc.Format(Strings.Report_When, time.ToString("d MMMM yyyy", Loc.TextCulture), time.ToString("HH:mm:ss", CultureInfo.InvariantCulture));

    public static string Duration(TimeSpan running)
        => running.TotalHours >= 1 ? Loc.Format(Strings.Report_Hours, (int)running.TotalHours, running.Minutes)
            : running.TotalMinutes >= 1 ? Loc.Format(Strings.Report_Minutes, (int)running.TotalMinutes)
            : Loc.Format(Strings.Report_Seconds, Math.Max(0, (int)running.TotalSeconds));

    public static string Build(ProblemKind kind, Exception problem, string facts, DateTime time, TimeSpan running, IReadOnlyList<string> open, IReadOnlyList<string> lastLines)
    {
        var text = new StringBuilder();
        text.AppendLine(facts);
        text.Append(kind == ProblemKind.Crash ? Strings.Report_Crash : Strings.Report_WentOn)
            .Append(" · ").Append(When(time))
            .Append(" · ").AppendLine(Loc.Format(Strings.Report_Running, Duration(running)));
        text.AppendLine();
        text.AppendLine(problem.ToString().ReplaceLineEndings(Environment.NewLine));
        if (open.Count > 0)
        {
            text.AppendLine();
            text.AppendLine(Loc.Format(Strings.Report_Open, string.Join(", ", open)));
        }

        if (lastLines.Count > 0)
        {
            text.AppendLine();
            text.AppendLine(Strings.Report_LastLines);
            foreach (var line in lastLines)
            {
                text.AppendLine(line);
            }
        }

        return text.ToString();
    }

    public static string? Write(string folder, ProblemKind kind, DateTime time, string text)
    {
        try
        {
            Directory.CreateDirectory(folder);
            var name = (kind == ProblemKind.Crash ? CrashPrefix : WentOnPrefix) + time.ToString(StampFormat, CultureInfo.InvariantCulture);
            var path = Path.Combine(folder, name + ".txt");
            for (var n = 2; File.Exists(path); n++)
            {
                path = Path.Combine(folder, name + "-" + n.ToString(CultureInfo.InvariantCulture) + ".txt");
            }

            File.WriteAllText(path, text);
            if (kind == ProblemKind.Crash)
            {
                File.WriteAllText(Path.Combine(folder, UnseenName), Path.GetFileName(path));
            }

            return path;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    public static IReadOnlyList<ProblemReport> List(string folder)
    {
        if (!Directory.Exists(folder))
        {
            return [];
        }

        var reports = new List<(ProblemReport Report, int Number)>();
        foreach (var path in Directory.EnumerateFiles(folder, "*.txt"))
        {
            var name = Path.GetFileName(path);
            if (Parse(WithoutNumber(name)) is { } parsed)
            {
                reports.Add((new ProblemReport(path, parsed.Kind, parsed.Time, ExceptionTypeOf(path)), Numbered(name) ? name[^5] - '0' : 1));
            }
        }

        return [.. reports.OrderByDescending(r => r.Report.Time).ThenByDescending(r => r.Number).Select(r => r.Report)];
    }

    private static string ExceptionTypeOf(string path)
    {
        try
        {
            foreach (var line in File.ReadLines(path).Skip(3).Take(3))
            {
                var colon = line.IndexOf(':', StringComparison.Ordinal);
                var type = colon > 0 ? line[..colon] : line;
                if (type.Length > 0 && !type.Contains(' ', StringComparison.Ordinal))
                {
                    return type[(type.LastIndexOf('.') + 1)..];
                }
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
        }

        return "";
    }

    public static ProblemReport? TakeUnseen(string folder)
    {
        var mark = Path.Combine(folder, UnseenName);
        try
        {
            if (!File.Exists(mark))
            {
                return null;
            }

            var name = File.ReadAllText(mark).Trim();
            File.Delete(mark);
            return List(folder).FirstOrDefault(r => Path.GetFileName(r.Path).Equals(name, StringComparison.OrdinalIgnoreCase));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}

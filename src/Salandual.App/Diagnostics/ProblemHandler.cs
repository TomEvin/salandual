// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.App/Diagnostics/ProblemHandler.cs

using Salandual.App.Resources;
using Salandual.Core.Diagnostics;
using Salandual.Core.Resources;

namespace Salandual.App.Diagnostics;

internal sealed class ProblemHandler(AppLog log, Func<string> facts, Func<IReadOnlyList<string>> open, DateTime started)
{
    public static readonly TimeSpan SameProblemWithin = TimeSpan.FromMinutes(1);

    private readonly Lock _gate = new();
    private (string Signature, DateTime Time)? _last;

    public string? LastReport { get; private set; }

    public bool Crashed { get; private set; }

    public ProblemKind Handle(Exception problem)
    {
        lock (_gate)
        {
            var now = log.Now;
            var signature = Signature(problem);
            var kind = _last is { } last && last.Signature == signature && now - last.Time < SameProblemWithin ? ProblemKind.Crash : ProblemKind.WentOn;
            _last = (signature, now);
            log.Write(LogLevel.Error, Loc.Format(AppStrings.Log_Problem, problem.GetType().Name, problem.Message));
            if (kind == ProblemKind.Crash)
            {
                log.Write(LogLevel.Error, Loc.Format(AppStrings.Log_Crash, problem.GetType().Name));
                Crashed = true;
            }

            Write(kind, problem, now);
            return kind;
        }
    }

    public void HandleFatal(Exception problem)
    {
        lock (_gate)
        {
            if (Crashed)
            {
                return;
            }

            Crashed = true;
            log.Write(LogLevel.Error, Loc.Format(AppStrings.Log_Crash, problem.GetType().Name));
            Write(ProblemKind.Crash, problem, log.Now);
        }
    }

    public void HandleUnobserved(Exception problem)
    {
        var inner = problem is AggregateException { InnerExceptions: [var one] } ? one : problem;
        log.Write(LogLevel.Warning, Loc.Format(AppStrings.Log_InBackground, inner.GetType().Name, inner.Message));
    }

    private void Write(ProblemKind kind, Exception problem, DateTime now)
    {
        IReadOnlyList<string> windows;
        try
        {
            windows = open();
        }
        catch (InvalidOperationException)
        {
            windows = [];
        }

        var text = ProblemReports.Build(kind, problem, facts(), now, now - started, windows, log.Recent);
        LastReport = log.Folder is { } folder ? ProblemReports.Write(folder, kind, now, text) : null;
    }

    internal static string Signature(Exception problem)
        => problem.GetType().FullName + "|" + (problem.StackTrace?.Split('\n', 2)[0].Trim() ?? problem.Message);

    public static string Where(Exception problem)
    {
        foreach (var frame in new System.Diagnostics.StackTrace(problem).GetFrames())
        {
            if (System.Diagnostics.DiagnosticMethodInfo.Create(frame) is { DeclaringTypeName: { } type } method && type.StartsWith("Salandual.", StringComparison.Ordinal))
            {
                var plus = type.IndexOf('+', StringComparison.Ordinal);
                var outer = plus > 0 ? type[..plus] : type;
                return outer[(outer.LastIndexOf('.') + 1)..] + "." + method.Name.TrimStart('<').Split('>')[0];
            }
        }

        return "";
    }
}

// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.App/Editor/ProgramLauncher.cs

using Salandual.Platform.Windows.Shell;

namespace Salandual.App.Editor;

internal interface IProgramLauncher
{
    StartProblem Start(string program, IReadOnlyList<string> arguments, string? folder, out string? detail);

    bool CanEdit(string file);

    StartProblem Edit(string file, out string? detail);
}

internal sealed class WindowsProgramLauncher : IProgramLauncher
{
    public static WindowsProgramLauncher Instance { get; } = new();

    public StartProblem Start(string program, IReadOnlyList<string> arguments, string? folder, out string? detail) => Programs.Start(program, arguments, folder, out detail);

    public bool CanEdit(string file) => Programs.CanEdit(file);

    public StartProblem Edit(string file, out string? detail) => Programs.Edit(file, out detail);
}

internal sealed class RecordingProgramLauncher : IProgramLauncher
{
    public List<(string Program, IReadOnlyList<string> Arguments)> Started { get; } = [];

    public List<string> Edited { get; } = [];

    public StartProblem Answer { get; set; } = StartProblem.None;

    public StartProblem Start(string program, IReadOnlyList<string> arguments, string? folder, out string? detail)
    {
        detail = null;
        Started.Add((program, arguments));
        return Answer;
    }

    public bool CanEdit(string file) => true;

    public StartProblem Edit(string file, out string? detail)
    {
        detail = null;
        Edited.Add(file);
        return Answer;
    }
}

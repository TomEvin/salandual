// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.Core/StartWithWindows.cs

namespace Salandual.Core;

public interface IStartWithWindows
{
    string? Registered { get; }

    void Register(string program);

    void Unregister();
}

public static class StartWithWindowsState
{
    public static bool IsOn(IStartWithWindows start, string program)
        => start.Registered is { } registered && SamePath(registered, program);

    public static string? Other(IStartWithWindows start, string program)
        => start.Registered is { Length: > 0 } registered && !SamePath(registered, program) ? registered : null;

    private static bool SamePath(string a, string b)
        => string.Equals(Path.GetFullPath(a).TrimEnd('\\'), Path.GetFullPath(b).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase);
}

public sealed class MemoryStartWithWindows : IStartWithWindows
{
    public string? Registered { get; set; }

    public void Register(string program) => Registered = program;

    public void Unregister() => Registered = null;
}

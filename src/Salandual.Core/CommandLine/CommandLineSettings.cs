// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.Core/CommandLine/CommandLineSettings.cs

namespace Salandual.Core.CommandLine;

public enum ShellKind { Terminal, PowerShell7, WindowsPowerShell, CommandPrompt, Program }

public enum CommandLineShell { PowerShell7, WindowsPowerShell, CommandPrompt }

public enum PanelTyping { QuickSearch, CommandLine }

public sealed record CommandLineSettings
{
    public const int MaxRemembered = 100;

    public ShellKind? Shell { get; set; }

    public string? TerminalProfile { get; set; }

    public string Program { get; set; } = "";

    public CommandLineShell? RunIn { get; set; }

    public bool CloseWhenDone { get; set; }

    public int Remembered { get; set; } = 30;

    public PanelTyping Typing { get; set; } = PanelTyping.QuickSearch;

    internal CommandLineSettings Normalize() => this with
    {
        Shell = Shell is { } shell && !Enum.IsDefined(shell) ? null : Shell,
        TerminalProfile = string.IsNullOrWhiteSpace(TerminalProfile) ? null : TerminalProfile.Trim(),
        Program = Program?.Trim() ?? "",
        RunIn = RunIn is { } runIn && !Enum.IsDefined(runIn) ? null : RunIn,
        Remembered = Math.Clamp(Remembered, 0, MaxRemembered),
        Typing = Enum.IsDefined(Typing) ? Typing : PanelTyping.QuickSearch,
    };
}

public sealed record CommandHistoryEntry(string Text, string Folder);

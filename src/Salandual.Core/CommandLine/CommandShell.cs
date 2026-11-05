// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.Core/CommandLine/CommandShell.cs

using System.Text;

namespace Salandual.Core.CommandLine;

public sealed record ShellStart(string Program, string? Folder)
{
    public IReadOnlyList<string> Arguments { get; init; } = [];

    public string? RawArguments { get; init; }

    public bool Elevated { get; init; }
}

public sealed record ShellsFound(string? Terminal, string? PowerShell7, string WindowsPowerShell, string CommandPrompt);

public sealed record TerminalProfile(string Id, string Name);

public enum ShellNote
{
    None,

    OwnFolder,
}

public interface ICommandShells
{
    ShellsFound Found { get; }

    (IReadOnlyList<TerminalProfile> Profiles, string? Default) TerminalProfiles();

    bool Start(ShellStart start, out string? detail);
}

public sealed class MemoryCommandShells : ICommandShells
{
    private readonly List<ShellStart> _started = [];

    public ShellsFound Found { get; set; } = new(
        @"C:\Users\me\AppData\Local\Microsoft\WindowsApps\wt.exe",
        @"C:\Program Files\PowerShell\7\pwsh.exe",
        @"C:\Windows\System32\WindowsPowerShell\v1.0\powershell.exe",
        @"C:\Windows\System32\cmd.exe");

    public List<TerminalProfile> Profiles { get; } =
    [
        new("{61c54bbd-c2c6-5271-96e7-009a87ff44bf}", "Windows PowerShell"),
        new("{0caa0dad-35be-5f56-a8ff-afceeeaa6101}", "Command Prompt"),
    ];

    public string? DefaultProfile { get; set; } = "{61c54bbd-c2c6-5271-96e7-009a87ff44bf}";

    public IReadOnlyList<ShellStart> Started => _started;

    public (IReadOnlyList<TerminalProfile> Profiles, string? Default) TerminalProfiles() => (Profiles, DefaultProfile);

    public bool Start(ShellStart start, out string? detail)
    {
        detail = null;
        _started.Add(start);
        return true;
    }
}

public static class CommandShell
{
    public static ShellKind Effective(CommandLineSettings settings, ShellsFound found)
    {
        var chosen = settings.Shell;
        if (chosen is { } kind && kind switch
            {
                ShellKind.Terminal => found.Terminal is not null,
                ShellKind.PowerShell7 => found.PowerShell7 is not null,
                ShellKind.Program => settings.Program.Length > 0,
                _ => true,
            })
        {
            return kind;
        }

        return found.Terminal is not null ? ShellKind.Terminal : found.PowerShell7 is not null ? ShellKind.PowerShell7 : ShellKind.WindowsPowerShell;
    }

    public static CommandLineShell RunsIn(CommandLineSettings settings, ShellsFound found)
    {
        var shell = settings.RunIn ?? Effective(settings, found) switch
        {
            ShellKind.WindowsPowerShell => CommandLineShell.WindowsPowerShell,
            ShellKind.CommandPrompt => CommandLineShell.CommandPrompt,
            _ => CommandLineShell.PowerShell7,
        };
        return shell == CommandLineShell.PowerShell7 && found.PowerShell7 is null ? CommandLineShell.WindowsPowerShell : shell;
    }

    public static ShellStart Open(CommandLineSettings settings, ShellsFound found, string folder, bool elevated, out ShellNote note)
    {
        note = ShellNote.None;
        switch (Effective(settings, found))
        {
            case ShellKind.Terminal:
                List<string> args = settings.TerminalProfile is { } profile ? ["-p", profile] : [];

                args.AddRange(["-d", elevated ? folder.Replace(";", @"\;", StringComparison.Ordinal) : "."]);
                return new ShellStart(found.Terminal!, folder) { Arguments = args, Elevated = elevated };
            case ShellKind.PowerShell7:
                return new ShellStart(found.PowerShell7!, folder) { Arguments = elevated ? ["-NoExit", "-WorkingDirectory", folder] : [], Elevated = elevated };
            case ShellKind.WindowsPowerShell:
                return new ShellStart(found.WindowsPowerShell, folder)
                {
                    Arguments = elevated ? ["-NoExit", "-EncodedCommand", Encode("Set-Location -LiteralPath " + QuotePowerShell(folder))] : [],
                    Elevated = elevated,
                };
            case ShellKind.CommandPrompt:
                if (elevated && folder.Contains('%', StringComparison.Ordinal))
                {
                    note = ShellNote.OwnFolder;
                    return new ShellStart(found.CommandPrompt, folder) { Elevated = true };
                }

                return new ShellStart(found.CommandPrompt, folder) { RawArguments = elevated ? "/k cd /d \"" + folder + "\"" : null, Elevated = elevated };
            default:
                note = elevated ? ShellNote.OwnFolder : ShellNote.None;
                return new ShellStart(Environment.ExpandEnvironmentVariables(settings.Program), folder) { Elevated = elevated };
        }
    }

    public static ShellStart Run(CommandLineShell shell, ShellsFound found, string text, string folder, bool keepOpen)
    {
        if (shell == CommandLineShell.CommandPrompt)
        {
            return new ShellStart(found.CommandPrompt, folder) { RawArguments = (keepOpen ? "/s /k \"" : "/s /c \"") + text + "\"" };
        }

        var program = shell == CommandLineShell.PowerShell7 && found.PowerShell7 is { } pwsh ? pwsh : found.WindowsPowerShell;
        List<string> args = ["-NoLogo"];
        if (keepOpen)
        {
            args.Add("-NoExit");
        }

        args.AddRange(["-EncodedCommand", Encode(text)]);
        return new ShellStart(program, folder) { Arguments = args };
    }

    public static string Encode(string text) => Convert.ToBase64String(Encoding.Unicode.GetBytes(text));

    private const string CmdSpecial = " \t&()[]{}^=;!'+,`~";

    public static string Quote(string name, CommandLineShell shell)
    {
        if (shell == CommandLineShell.CommandPrompt)
        {
            return name.Length > 0 && name.IndexOfAny(CmdSpecial.ToCharArray()) < 0 ? name : "\"" + name + "\"";
        }

        return name.Length > 0 && name[0] != '-' && name.All(c => char.IsLetterOrDigit(c) || "._-:\\/+=~".Contains(c, StringComparison.Ordinal)) ? name : QuotePowerShell(name);
    }

    public static string QuotePowerShell(string text)
    {
        var result = new StringBuilder(text.Length + 2).Append('\'');
        foreach (var c in text)
        {
            result.Append(c);
            if (c is '\'' or '\u2018' or '\u2019' or '\u201A' or '\u201B')
            {
                result.Append(c);
            }
        }

        return result.Append('\'').ToString();
    }

    public static bool IsRisky(string name, CommandLineShell shell)
        => shell == CommandLineShell.CommandPrompt ? name.Contains('%', StringComparison.Ordinal) : name.IndexOfAny(['[', ']']) >= 0;

    public static bool TryChangeDirectory(string text, string folder, out string target, out bool drive)
    {
        target = "";
        drive = false;
        var t = text.Trim();
        if (t.Length == 2 && char.IsAsciiLetter(t[0]) && t[1] == ':')
        {
            target = char.ToUpperInvariant(t[0]) + @":\";
            drive = true;
            return true;
        }

        string rest;
        if (t.StartsWith("chdir", StringComparison.OrdinalIgnoreCase) && t.Length > 5 && IsCdEnd(t[5]))
        {
            rest = t[5..];
        }
        else if (t.StartsWith("cd", StringComparison.OrdinalIgnoreCase) && t.Length > 2 && IsCdEnd(t[2]))
        {
            rest = t[2..];
        }
        else
        {
            return false;
        }

        rest = rest.Trim();
        if (rest.StartsWith("/d", StringComparison.OrdinalIgnoreCase) && (rest.Length == 2 || rest[2] == ' '))
        {
            rest = rest[2..].Trim();
        }

        if (rest.Length >= 2 && rest[0] == '"' && rest[^1] == '"')
        {
            rest = rest[1..^1];
        }
        else if (rest.Length >= 2 && rest[0] == '\'' && rest[^1] == '\'' && rest.Count(c => c == '\'') == 2)
        {
            rest = rest[1..^1];
        }

        if (rest.Length == 0 || rest.IndexOfAny(['"', '%', '$', '&', '|', '<', '>', ';', '*', '?', '`']) >= 0)
        {
            return false;
        }

        try
        {
            target = Path.GetFullPath(rest.Replace('/', '\\'), folder);
        }
        catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }

        return true;
    }

    private static bool IsCdEnd(char c) => c is ' ' or '.' or '\\' or '/';
}

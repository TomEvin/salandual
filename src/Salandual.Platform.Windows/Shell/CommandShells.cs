// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.Platform.Windows/Shell/CommandShells.cs

using System.ComponentModel;
using System.Diagnostics;
using Salandual.Core.CommandLine;

namespace Salandual.Platform.Windows.Shell;

public sealed class CommandShells : ICommandShells
{
    private ShellsFound? _found;

    public ShellsFound Found => _found ??= new ShellsFound(
        FileIfThere(Path.Combine(LocalAppData, "Microsoft", "WindowsApps", "wt.exe")),
        Programs.Resolve("pwsh.exe", out _),
        Path.Combine(Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe"),
        Path.Combine(Environment.SystemDirectory, "cmd.exe"));

    private static string LocalAppData => Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

    public (IReadOnlyList<TerminalProfile> Profiles, string? Default) TerminalProfiles()
    {
        string[] places =
        [
            Path.Combine(LocalAppData, "Packages", "Microsoft.WindowsTerminal_8wekyb3d8bbwe", "LocalState", "settings.json"),
            Path.Combine(LocalAppData, "Packages", "Microsoft.WindowsTerminalPreview_8wekyb3d8bbwe", "LocalState", "settings.json"),
            Path.Combine(LocalAppData, "Microsoft", "Windows Terminal", "settings.json"),
        ];
        foreach (var place in places)
        {
            try
            {
                var file = new FileInfo(place);
                if (file.Exists && file.Length <= TerminalSettings.MaxFileSize)
                {
                    return TerminalSettings.Parse(File.ReadAllText(place));
                }
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
            }
        }

        return ([], null);
    }

    public bool Start(ShellStart start, out string? detail)
    {
        detail = null;
        if (!Path.IsPathFullyQualified(start.Program) || !File.Exists(start.Program)
            || Path.GetExtension(start.Program).ToLowerInvariant() is not (".exe" or ".com"))
        {
            detail = start.Program;
            return false;
        }

        var info = new ProcessStartInfo(start.Program) { UseShellExecute = start.Elevated };
        if (start.Elevated)
        {
            info.Verb = "runas";
        }

        if (start.RawArguments is { } raw)
        {
            info.Arguments = raw;
        }
        else
        {
            foreach (var argument in start.Arguments)
            {
                info.ArgumentList.Add(argument);
            }
        }

        if (start.Folder is { Length: > 0 } folder && Directory.Exists(folder))
        {
            info.WorkingDirectory = folder;
        }

        try
        {
            using var process = Process.Start(info);
            return true;
        }
        catch (Win32Exception e) when (e.NativeErrorCode == 1223)
        {
            detail = null;
            return true;
        }
        catch (Exception e) when (e is Win32Exception or InvalidOperationException or IOException)
        {
            detail = e.Message;
            return false;
        }
    }

    private static string? FileIfThere(string path) => File.Exists(path) ? path : null;
}

// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.Platform.Windows/Shell/Programs.cs

using System.ComponentModel;
using System.Diagnostics;
using Microsoft.Win32;

namespace Salandual.Platform.Windows.Shell;

public enum StartProblem { None, NotFound, NotAProgram, NoEditor, Failed }

public static class Programs
{
    private static readonly HashSet<string> Runnable = new(StringComparer.OrdinalIgnoreCase)
    {
        ".exe", ".com", ".bat", ".cmd", ".vbs", ".vbe", ".js", ".jse", ".wsf", ".wsh", ".msc", ".ps1", ".psm1", ".msi", ".msp",
        ".scr", ".cpl", ".hta", ".lnk", ".url", ".pif", ".reg", ".application", ".appref-ms", ".jar", ".appx", ".msix", ".library-ms", ".settingcontent-ms",
    };

    public static string? Resolve(string program, out StartProblem problem)
    {
        problem = StartProblem.NotFound;
        var name = Environment.ExpandEnvironmentVariables(program.Trim().Trim('"'));
        if (name.Length == 0)
        {
            return null;
        }

        string? found = null;
        if (Path.IsPathFullyQualified(name))
        {
            found = File.Exists(name) ? name : null;
        }
        else if (name.IndexOfAny(['\\', '/']) < 0)
        {
            var withExe = Path.HasExtension(name) ? name : name + ".exe";
            found = AppPath(withExe) ?? OnPath(withExe);
        }

        if (found is null)
        {
            return null;
        }

        if (Path.GetExtension(found).ToLowerInvariant() is not (".exe" or ".com"))
        {
            problem = StartProblem.NotAProgram;
            return null;
        }

        problem = StartProblem.None;
        return found;
    }

    public static StartProblem Start(string program, IReadOnlyList<string> arguments, string? folder, out string? detail)
    {
        detail = null;
        if (Resolve(program, out var problem) is not { } path)
        {
            return problem;
        }

        var info = new ProcessStartInfo(path) { UseShellExecute = false };
        foreach (var argument in arguments)
        {
            info.ArgumentList.Add(argument);
        }

        if (folder is { Length: > 0 } && Directory.Exists(folder))
        {
            info.WorkingDirectory = folder;
        }

        try
        {
            using var process = Process.Start(info);
            return StartProblem.None;
        }
        catch (Exception e) when (e is Win32Exception or InvalidOperationException or IOException)
        {
            detail = e.Message;
            return StartProblem.Failed;
        }
    }

    public static bool CanEdit(string file) => Verb(file) is not null;

    public static StartProblem Edit(string file, out string? detail)
    {
        detail = null;
        if (Verb(file) is not { } verb)
        {
            return StartProblem.NoEditor;
        }

        try
        {
            using var process = Process.Start(new ProcessStartInfo(file) { UseShellExecute = true, Verb = verb, WorkingDirectory = Path.GetDirectoryName(file) ?? "" });
            return StartProblem.None;
        }
        catch (Exception e) when (e is Win32Exception or InvalidOperationException or IOException)
        {
            detail = e.Message;
            return StartProblem.Failed;
        }
    }

    private static string? Verb(string file)
    {
        try
        {
            var verbs = new ProcessStartInfo(file).Verbs;
            if (verbs.Contains("edit", StringComparer.OrdinalIgnoreCase))
            {
                return "edit";
            }

            return !Runnable.Contains(Path.GetExtension(file)) && verbs.Contains("open", StringComparer.OrdinalIgnoreCase) ? "open" : null;
        }
        catch (Exception e) when (e is ArgumentException or IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            return null;
        }
    }

    private static string? AppPath(string name)
    {
        foreach (var root in new[] { Registry.CurrentUser, Registry.LocalMachine })
        {
            try
            {
                using var key = root.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\App Paths\" + name);
                if (key?.GetValue(null) is string value && Environment.ExpandEnvironmentVariables(value.Trim().Trim('"')) is { Length: > 0 } path
                    && Path.IsPathFullyQualified(path) && File.Exists(path))
                {
                    return path;
                }
            }
            catch (Exception e) when (e is System.Security.SecurityException or UnauthorizedAccessException or IOException)
            {
            }
        }

        return null;
    }

    private static string? OnPath(string name)
    {
        foreach (var folder in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var expanded = Environment.ExpandEnvironmentVariables(folder.Trim('"'));
            if (!Path.IsPathFullyQualified(expanded))
            {
                continue;
            }

            var candidate = Path.Combine(expanded, name);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }
}

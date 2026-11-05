// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.Platform.Windows/Shell/ShellNewItems.cs

using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace Salandual.Platform.Windows.Shell;

public sealed record ShellNewItem(string Extension, string Name, byte[]? Data = null, string? Template = null);

public static partial class ShellNewItems
{
    public static IReadOnlyList<ShellNewItem> List()
    {
        var items = new List<ShellNewItem>();
        foreach (var ext in Classes())
        {
            if (ext.Length < 2 || ext[0] != '.' || ext.Equals(".lnk", StringComparison.OrdinalIgnoreCase) || items.Any(i => i.Extension.Equals(ext, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            try
            {
                if (Read(ext) is { } item)
                {
                    items.Add(item);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
            {
            }
        }

        return [.. items.OrderBy(i => i.Name, StringComparer.CurrentCultureIgnoreCase)];
    }

    private static string[] Classes()
    {
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\Discardable\PostSetup\ShellNew");
        return key?.GetValue("Classes") is string[] { Length: > 0 } classes ? classes : [".txt"];
    }

    private static ShellNewItem? Read(string ext)
    {
        using var extKey = Registry.ClassesRoot.OpenSubKey(ext);
        if (extKey is null)
        {
            return null;
        }

        var progId = extKey.GetValue(null) as string;
        using var progShellNew = progId is { Length: > 0 } ? extKey.OpenSubKey(progId + @"\ShellNew") : null;
        using var plainShellNew = progShellNew is null ? extKey.OpenSubKey("ShellNew") : null;
        var shellNew = progShellNew ?? plainShellNew;
        if (shellNew is null)
        {
            return null;
        }

        var name = TypeName(shellNew, progId) ?? ext[1..].ToUpperInvariant();
        if (shellNew.GetValue("NullFile") is not null)
        {
            return new ShellNewItem(ext, name, []);
        }

        if (shellNew.GetValue("Data") is { } data)
        {
            return new ShellNewItem(ext, name, data switch
            {
                byte[] bytes => bytes,
                string text => System.Text.Encoding.Default.GetBytes(text),
                _ => [],
            });
        }

        if (shellNew.GetValue("FileName") is string file && TemplatePath(file) is { } template)
        {
            return new ShellNewItem(ext, name, Template: template);
        }

        return null;
    }

    private static string? TypeName(RegistryKey shellNew, string? progId)
    {
        if (shellNew.GetValue("ItemName") is string itemName && Indirect(itemName) is { Length: > 0 } fromItem)
        {
            return fromItem;
        }

        if (progId is not { Length: > 0 })
        {
            return null;
        }

        using var prog = Registry.ClassesRoot.OpenSubKey(progId);
        if (prog?.GetValue("FriendlyTypeName") is string friendly && Indirect(friendly) is { Length: > 0 } fromFriendly)
        {
            return fromFriendly;
        }

        return prog?.GetValue(null) as string is { Length: > 0 } plain ? plain : null;
    }

    private static string? TemplatePath(string file)
    {
        file = Environment.ExpandEnvironmentVariables(file);
        if (Path.IsPathFullyQualified(file))
        {
            return File.Exists(file) ? file : null;
        }

        foreach (var folder in new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.Templates),
            Environment.GetFolderPath(Environment.SpecialFolder.CommonTemplates),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "ShellNew"),
        })
        {
            if (folder.Length > 0 && Path.Combine(folder, Path.GetFileName(file)) is var path && File.Exists(path))
            {
                return path;
            }
        }

        return null;
    }

    private static unsafe string? Indirect(string value)
    {
        if (!value.StartsWith('@'))
        {
            return value;
        }

        var buffer = stackalloc char[512];
        return SHLoadIndirectString(value, buffer, 512, 0) == 0 ? new string(buffer) : null;
    }

    [LibraryImport("shlwapi.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static unsafe partial int SHLoadIndirectString(string source, char* output, uint size, nint reserved);
}

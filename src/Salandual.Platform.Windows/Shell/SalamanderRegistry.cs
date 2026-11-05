// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.Platform.Windows/Shell/SalamanderRegistry.cs

using Microsoft.Win32;
using Salandual.Core.Settings.Salamander;

namespace Salandual.Platform.Windows.Shell;

public sealed class SalamanderRegistry : ISalamanderSettings
{
    private const int MaxKeys = 20_000, MaxDepth = 12;

    public IReadOnlyList<SalamanderInstall> Find()
    {
        var found = new List<SalamanderInstall>();
        foreach (var (key, name) in SalamanderRoots.Known)
        {
            try
            {
                using var root = Registry.CurrentUser.OpenSubKey(key, writable: false);
                if (root?.OpenSubKey("Configuration", writable: false) is { } configuration)
                {
                    configuration.Dispose();
                    found.Add(new SalamanderInstall(name, key));
                }
            }
            catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
            {
            }
        }

        return found;
    }

    public RegKey? Read(SalamanderInstall install)
    {
        try
        {
            using var root = Registry.CurrentUser.OpenSubKey(install.Key, writable: false);
            if (root is null)
            {
                return null;
            }

            var keys = 0;
            return Copy(root, 0, ref keys);
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            return null;
        }
    }

    private static RegKey Copy(RegistryKey key, int depth, ref int keys)
    {
        var copy = new RegKey();
        foreach (var name in key.GetValueNames())
        {
            if (RegKey.IsSecret(name))
            {
                continue;
            }

            switch (key.GetValueKind(name))
            {
                case RegistryValueKind.String or RegistryValueKind.ExpandString:
                    if (key.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames) is string text)
                    {
                        copy.Values[name] = new RegValue(text, null);
                    }

                    break;
                case RegistryValueKind.DWord:
                    if (key.GetValue(name) is int number)
                    {
                        copy.Values[name] = new RegValue(null, unchecked((uint)number));
                    }

                    break;
            }
        }

        if (depth >= MaxDepth)
        {
            return copy;
        }

        foreach (var name in key.GetSubKeyNames())
        {
            if (RegKey.IsSecret(name) || ++keys > MaxKeys)
            {
                continue;
            }

            using var sub = key.OpenSubKey(name, writable: false);
            if (sub is not null)
            {
                copy.Keys[name] = Copy(sub, depth + 1, ref keys);
            }
        }

        return copy;
    }
}

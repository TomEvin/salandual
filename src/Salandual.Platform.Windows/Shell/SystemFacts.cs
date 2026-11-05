// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.Platform.Windows/Shell/SystemFacts.cs

using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace Salandual.Platform.Windows.Shell;

public static partial class SystemFacts
{
    private const string VersionKey = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion";
    private const uint SpiGetClientAreaAnimation = 0x1042;

    public static string WindowsName()
    {
        var build = Environment.OSVersion.Version;
        var number = $"{build.Major}.{build.Minor}.{build.Build}";
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(VersionKey);
            var product = key?.GetValue("ProductName") as string ?? "Windows";
            var display = key?.GetValue("DisplayVersion") as string;
            return Name(product, display, build.Build) + $" ({number})";
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or IOException or UnauthorizedAccessException)
        {
            return $"Windows ({number})";
        }
    }

    public static string Name(string product, string? displayVersion, int build)
    {
        if (build >= 22000 && product.StartsWith("Windows 10", StringComparison.Ordinal))
        {
            product = "Windows 11" + product["Windows 10".Length..];
        }

        return string.IsNullOrEmpty(displayVersion) ? product : product + " " + displayVersion;
    }

    public static bool AnimationEffects()
        => !SystemParametersInfo(SpiGetClientAreaAnimation, 0, out var on, 0) || on != 0;

    [LibraryImport("user32.dll", EntryPoint = "SystemParametersInfoW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SystemParametersInfo(uint action, uint param, out int value, uint winIni);
}

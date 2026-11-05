// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.Platform.Windows/Shell/TextScale.cs

using Microsoft.Win32;

namespace Salandual.Platform.Windows.Shell;

public static class TextScale
{
    private const string Key = @"Software\Microsoft\Accessibility";

    public static double Read()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(Key);
            return key?.GetValue("TextScaleFactor") is int percent ? Math.Clamp(percent, 100, 225) / 100.0 : 1.0;
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or IOException or UnauthorizedAccessException)
        {
            return 1.0;
        }
    }
}

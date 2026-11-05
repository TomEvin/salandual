// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.App/Installer.cs

using Salandual.Core;
using Salandual.Platform.Windows.Shell;
using Velopack;

namespace Salandual.App;

internal static class Installer
{
    public static void Uninstalling() => Uninstalling(new RunAtSignIn(), Environment.ProcessPath);

    internal static void Uninstalling(IStartWithWindows start, string? program)
    {
        try
        {
            if (program is not null && StartWithWindowsState.IsOn(start, program))
            {
                start.Unregister();
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
        }
    }
}

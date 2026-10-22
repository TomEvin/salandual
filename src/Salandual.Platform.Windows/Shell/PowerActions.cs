// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.Platform.Windows/Shell/PowerActions.cs

using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Salandual.Platform.Windows.Shell;

public enum WhenDone { Nothing, CloseApp, Lock, Sleep, Hibernate, ShutDown }

public static partial class PowerActions
{
    public static bool CanHibernate
    {
        get
        {
            try
            {
                return IsPwrHibernateAllowed() != 0;
            }
            catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
            {
                return false;
            }
        }
    }

    public static bool Run(WhenDone action)
    {
        try
        {
            switch (action)
            {
                case WhenDone.Lock:
                    return LockWorkStation() != 0;
                case WhenDone.Sleep:
                    return SetSuspendState(0, 0, 0) != 0;
                case WhenDone.Hibernate:
                    return SetSuspendState(1, 0, 0) != 0;
                case WhenDone.ShutDown:
                    using (Process.Start(new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "shutdown.exe"), "/s /t 0") { UseShellExecute = false, CreateNoWindow = true }))
                    {
                        return true;
                    }

                default:
                    return false;
            }
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or System.ComponentModel.Win32Exception)
        {
            return false;
        }
    }

    [LibraryImport("user32.dll")]
    private static partial int LockWorkStation();

    [LibraryImport("powrprof.dll")]
    private static partial int SetSuspendState(int hibernate, int force, int wakeupEventsDisabled);

    [LibraryImport("powrprof.dll")]
    private static partial int IsPwrHibernateAllowed();
}

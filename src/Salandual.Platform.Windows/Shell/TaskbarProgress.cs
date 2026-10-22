// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.Platform.Windows/Shell/TaskbarProgress.cs

using System.Runtime.InteropServices;

namespace Salandual.Platform.Windows.Shell;

public enum TaskbarState
{
    None = 0,
    Indeterminate = 0x1,
    Normal = 0x2,
    Error = 0x4,
    Paused = 0x8,
}

public sealed unsafe partial class TaskbarProgress : IDisposable
{
    private void* _list;
    private bool _failed;

    public void Set(nint window, TaskbarState state, double fraction = 0)
    {
        if (window == 0 || !Ensure())
        {
            return;
        }

        var setState = (delegate* unmanaged[Stdcall]<void*, nint, int, int>)(*(void***)_list)[10];
        setState(_list, window, (int)state);
        if (state is TaskbarState.Normal or TaskbarState.Error or TaskbarState.Paused)
        {
            var setValue = (delegate* unmanaged[Stdcall]<void*, nint, ulong, ulong, int>)(*(void***)_list)[9];
            setValue(_list, window, (ulong)Math.Round(Math.Clamp(fraction, 0, 1) * 1000), 1000);
        }
    }

    private bool Ensure()
    {
        if (_list is not null)
        {
            return true;
        }

        if (_failed)
        {
            return false;
        }

        var clsid = new Guid("56FDF344-FD6D-11d0-958A-006097C9A090");
        var iid = new Guid("ea1afb91-9e28-4b86-90e9-9e9f8a5eefaf");
        void* list;
        if (CoCreateInstance(&clsid, null, 1 , &iid, &list) < 0 || list is null)
        {
            _failed = true;
            return false;
        }

        var init = (delegate* unmanaged[Stdcall]<void*, int>)(*(void***)list)[3];
        if (init(list) < 0)
        {
            ((delegate* unmanaged[Stdcall]<void*, uint>)(*(void***)list)[2])(list);
            _failed = true;
            return false;
        }

        _list = list;
        return true;
    }

    public void Dispose()
    {
        if (_list is not null)
        {
            ((delegate* unmanaged[Stdcall]<void*, uint>)(*(void***)_list)[2])(_list);
            _list = null;
        }
    }

    [LibraryImport("ole32.dll")]
    private static partial int CoCreateInstance(Guid* clsid, void* outer, uint context, Guid* iid, void** result);
}

public static partial class KeepAwake
{
    private const uint Continuous = 0x80000000;
    private const uint SystemRequired = 0x00000001;

    public static void Set(bool on) => SetThreadExecutionState(on ? Continuous | SystemRequired : Continuous);

    [LibraryImport("kernel32.dll")]
    private static partial uint SetThreadExecutionState(uint flags);
}

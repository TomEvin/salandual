// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.Platform.Windows/FileSystem/RecycleBin.cs

using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Salandual.Core.Operations;
using Salandual.Core.Resources;

namespace Salandual.Platform.Windows.FileSystem;

internal static unsafe partial class RecycleBin
{
    private static readonly BlockingCollection<Request> _requests = [];
    private static readonly Lock _gate = new();
    private static Thread? _thread;
    private static void** _sinkVtable;

    private sealed record Request(string Path, nint Owner, TaskCompletionSource<RecycleResult> Done);

    public static RecycleResult Recycle(string path, nint owner)
    {
        lock (_gate)
        {
            if (_thread is null)
            {
                _thread = new Thread(Loop) { IsBackground = true, Name = "Salandual Recycle Bin" };
                _thread.SetApartmentState(ApartmentState.STA);
                _thread.Start();
            }
        }

        var request = new Request(path, owner, new TaskCompletionSource<RecycleResult>(TaskCreationOptions.RunContinuationsAsynchronously));
        _requests.Add(request);
        return request.Done.Task.GetAwaiter().GetResult();
    }

    private static void Loop()
    {
        foreach (var request in _requests.GetConsumingEnumerable())
        {
            try
            {
                request.Done.SetResult(RecycleOne(request.Path, request.Owner));
            }
            catch (Exception ex)
            {
                request.Done.SetException(ex);
            }
        }
    }

    private static RecycleResult RecycleOne(string path, nint owner)
    {
        void* operation = null;
        void* item = null;
        var sink = (Sink*)NativeMemory.AllocZeroed((nuint)sizeof(Sink));
        sink->Vtable = SinkVtable();
        try
        {
            var clsid = ClsidFileOperation;
            var iid = IidFileOperation;
            Check(CoCreateInstance(&clsid, null, ClsctxAll, &iid, &operation));
            Check(Call<uint>(operation, 5, FofAllowUndo | FofNoConfirmation | FofSilent | FofNoErrorUi | FofWantNukeWarning | FofxRecycleOnDelete | FofxEarlyFailure));
            if (owner != 0)
            {
                Call<nint>(operation, 9, owner);
            }

            var iidItem = IidShellItem;
            if (SHCreateItemFromParsingName(path, null, &iidItem, &item) < 0 || item is null)
            {
                throw new IOException(Strings.Recycle_UnreadableName);
            }

            var deleteItem = (delegate* unmanaged[Stdcall]<void*, void*, void*, int>)(*(void***)operation)[18];
            Check(deleteItem(operation, item, sink));
            var perform = (delegate* unmanaged[Stdcall]<void*, int>)(*(void***)operation)[21];
            var hr = perform(operation);
            var getAborted = (delegate* unmanaged[Stdcall]<void*, int*, int>)(*(void***)operation)[22];
            int aborted;
            getAborted(operation, &aborted);

            if (sink->Called != 0)
            {
                if (sink->Result < 0)
                {
                    return aborted != 0 || sink->Result == CopyEngineUserCancelled ? RecycleResult.Kept : throw Win32.FromHResult(sink->Result);
                }

                return sink->Recycled != 0 ? RecycleResult.Recycled : RecycleResult.DeletedPermanently;
            }

            if (aborted != 0 || hr == CopyEngineUserCancelled)
            {
                return RecycleResult.Kept;
            }

            if (hr < 0)
            {
                throw Win32.FromHResult(hr);
            }

            return File.Exists(WindowsPath.ToExtended(path)) || Directory.Exists(WindowsPath.ToExtended(path)) ? RecycleResult.Kept : RecycleResult.DeletedPermanently;
        }
        finally
        {
            Release(item);
            Release(operation);
            NativeMemory.Free(sink);
        }
    }

    private static void Check(int hr)
    {
        if (hr < 0)
        {
            throw Win32.FromHResult(hr);
        }
    }

    private static int Call<T>(void* com, int slot, T argument) where T : unmanaged
        => ((delegate* unmanaged[Stdcall]<void*, T, int>)(*(void***)com)[slot])(com, argument);

    private static void Release(void* com)
    {
        if (com is not null)
        {
            ((delegate* unmanaged[Stdcall]<void*, uint>)(*(void***)com)[2])(com);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Sink
    {
        public void** Vtable;
        public int Called;
        public int Result;
        public int Recycled;
    }

    private static void** SinkVtable()
    {
        if (_sinkVtable is not null)
        {
            return _sinkVtable;
        }

        var table = (void**)NativeMemory.Alloc(19, (nuint)sizeof(void*));
        table[0] = (delegate* unmanaged[Stdcall]<Sink*, Guid*, void**, int>)&QueryInterface;
        table[1] = (delegate* unmanaged[Stdcall]<Sink*, uint>)&AddRefOrRelease;
        table[2] = (delegate* unmanaged[Stdcall]<Sink*, uint>)&AddRefOrRelease;
        for (var i = 3; i < 19; i++)
        {
            table[i] = (delegate* unmanaged[Stdcall]<Sink*, int>)&Ok;
        }

        table[12] = (delegate* unmanaged[Stdcall]<Sink*, uint, void*, int, void*, int>)&PostDeleteItem;
        _sinkVtable = table;
        return table;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static int QueryInterface(Sink* self, Guid* iid, void** result)
    {
        if (*iid == IidUnknown || *iid == IidProgressSink)
        {
            *result = self;
            return 0;
        }

        *result = null;
        return unchecked((int)0x80004002);
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static uint AddRefOrRelease(Sink* self) => 1;

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static int Ok(Sink* self) => 0;

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static int PostDeleteItem(Sink* self, uint flags, void* item, int result, void* newlyCreated)
    {
        self->Called = 1;
        self->Result = result;
        self->Recycled = newlyCreated is not null ? 1 : 0;
        return 0;
    }

    private const uint ClsctxAll = 0x17;
    private const uint FofSilent = 0x4;
    private const uint FofNoConfirmation = 0x10;
    private const uint FofAllowUndo = 0x40;
    private const uint FofNoErrorUi = 0x400;
    private const uint FofWantNukeWarning = 0x4000;
    private const uint FofxRecycleOnDelete = 0x00080000;
    private const uint FofxEarlyFailure = 0x00100000;
    private const int CopyEngineUserCancelled = unchecked((int)0x80270000);

    private static readonly Guid ClsidFileOperation = new("3ad05575-8857-4850-9277-11b85bdb8e09");
    private static readonly Guid IidFileOperation = new("947aab5f-0a5c-4c13-b4d6-4bf7836fc9f8");
    private static readonly Guid IidShellItem = new("43826d1e-e718-42ee-bc55-a1e261c37bfe");
    private static readonly Guid IidProgressSink = new("04b0f1a7-9490-44bc-96e1-4296a31252e2");
    private static readonly Guid IidUnknown = new("00000000-0000-0000-c000-000000000046");

    [LibraryImport("ole32.dll")]
    private static partial int CoCreateInstance(Guid* clsid, void* outer, uint context, Guid* iid, void** result);

    [LibraryImport("shell32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int SHCreateItemFromParsingName(string path, void* bindContext, Guid* iid, void** result);
}

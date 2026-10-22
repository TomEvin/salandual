// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.Platform.Windows/Shell/ShellData.cs

using System.Runtime.InteropServices;
using Salandual.Core.Operations;

namespace Salandual.Platform.Windows.Shell;

public sealed record DropData(IReadOnlyList<string> Files, string? Text)
{
    public static DropData Empty { get; } = new([], null);
}

public enum DropImage
{
    Invalid = -1,

    None = 0,
    Copy = 1,
    Move = 2,
    Link = 4,
    Label = 6,
    Warning = 7,
}

public sealed record DropNote(DropImage Image, string Message, string Insert)
{
    public static DropNote Clear { get; } = new(DropImage.Invalid, "", "");
}

internal static unsafe partial class ShellData
{
    private const ushort CfUnicodeText = 13;
    private const ushort CfHdrop = 15;
    private const uint TymedHGlobal = 1;
    private const uint GmemMoveable = 0x2;

    private static readonly ushort _dropDescription = (ushort)RegisterClipboardFormatW("DropDescription");
    private static readonly ushort _preferredDropEffect = (ushort)RegisterClipboardFormatW("Preferred DropEffect");
    private static readonly ushort _performedDropEffect = (ushort)RegisterClipboardFormatW("Performed DropEffect");
    private static readonly ushort _logicalPerformedDropEffect = (ushort)RegisterClipboardFormatW("Logical Performed DropEffect");

    public static DropData Read(void* data)
    {
        var files = new List<string>();
        if (Get(data, CfHdrop, out var medium))
        {
            try
            {
                var count = DragQueryFileW(medium.Handle, 0xFFFFFFFF, null, 0);
                for (var i = 0u; i < count && i < 100_000; i++)
                {
                    var length = DragQueryFileW(medium.Handle, i, null, 0);
                    var buffer = new char[length + 1];
                    fixed (char* b = buffer)
                    {
                        DragQueryFileW(medium.Handle, i, b, length + 1);
                    }

                    files.Add(new string(buffer, 0, (int)length));
                }
            }
            finally
            {
                ReleaseStgMedium(&medium);
            }
        }

        string? text = null;
        if (files.Count == 0 && Get(data, CfUnicodeText, out var t))
        {
            try
            {
                if (GlobalLock(t.Handle) is var p and not null)
                {
                    var max = (int)Math.Min(GlobalSize(t.Handle) / 2, 1 << 20);
                    text = new string((char*)p, 0, max).Split('\0', 2)[0];
                    GlobalUnlock(t.Handle);
                }
            }
            finally
            {
                ReleaseStgMedium(&t);
            }
        }

        return new DropData(files, text);
    }

    public static void SetPreferredEffect(void* data, DropEffects effect) => SetDword(data, _preferredDropEffect, (uint)effect);

    public static void SetPerformed(void* data, DropEffects performed, DropEffects logical)
    {
        SetDword(data, _performedDropEffect, (uint)performed);
        SetDword(data, _logicalPerformedDropEffect, (uint)logical);
    }

    public static uint? GetDword(void* data, string format)
    {
        if (!Get(data, (ushort)RegisterClipboardFormatW(format), out var medium))
        {
            return null;
        }

        try
        {
            return GlobalSize(medium.Handle) >= 4 && GlobalLock(medium.Handle) is var p and not null ? Unlock(medium.Handle, *(uint*)p) : null;
        }
        finally
        {
            ReleaseStgMedium(&medium);
        }
    }

    public static byte[]? GetBytes(void* data, string format)
    {
        if (!Get(data, (ushort)RegisterClipboardFormatW(format), out var medium))
        {
            return null;
        }

        try
        {
            var p = GlobalLock(medium.Handle);
            if (p is null)
            {
                return null;
            }

            var bytes = new ReadOnlySpan<byte>(p, (int)GlobalSize(medium.Handle)).ToArray();
            GlobalUnlock(medium.Handle);
            return bytes;
        }
        finally
        {
            ReleaseStgMedium(&medium);
        }
    }

    public static DropNote? GetNote(void* data)
    {
        if (!Get(data, _dropDescription, out var medium))
        {
            return null;
        }

        try
        {
            var d = (DropDescription*)GlobalLock(medium.Handle);
            if (d is null)
            {
                return null;
            }

            var note = new DropNote((DropImage)d->Type, new string(d->Message), new string(d->Insert));
            GlobalUnlock(medium.Handle);
            return note;
        }
        finally
        {
            ReleaseStgMedium(&medium);
        }
    }

    public static void SetNote(void* data, DropNote note)
    {
        var memory = GlobalAlloc(GmemMoveable, (nuint)sizeof(DropDescription));
        if (memory == 0)
        {
            return;
        }

        var d = (DropDescription*)GlobalLock(memory);
        *d = default;
        d->Type = (int)note.Image;
        Copy(note.Message, d->Message);
        Copy(note.Insert, d->Insert);
        GlobalUnlock(memory);
        Set(data, _dropDescription, memory);

        static void Copy(string text, char* to)
        {
            var span = text.AsSpan(0, Math.Min(text.Length, 259));
            span.CopyTo(new Span<char>(to, 260));
            to[span.Length] = '\0';
        }
    }

    private static uint Unlock(nint memory, uint value)
    {
        GlobalUnlock(memory);
        return value;
    }

    private static void SetDword(void* data, ushort format, uint value)
    {
        var memory = GlobalAlloc(GmemMoveable, 4);
        if (memory == 0)
        {
            return;
        }

        *(uint*)GlobalLock(memory) = value;
        GlobalUnlock(memory);
        Set(data, format, memory);
    }

    private static void Set(void* data, ushort format, nint memory)
    {
        var f = new FormatEtc { Format = format, Aspect = 1, Index = -1, Tymed = TymedHGlobal };
        var m = new StgMedium { Tymed = TymedHGlobal, Handle = memory };
        if (((delegate* unmanaged[Stdcall]<void*, FormatEtc*, StgMedium*, int, int>)(*(void***)data)[7])(data, &f, &m, 1) < 0)
        {
            GlobalFree(memory);
        }
    }

    private static bool Get(void* data, ushort format, out StgMedium medium)
    {
        var f = new FormatEtc { Format = format, Aspect = 1, Index = -1, Tymed = TymedHGlobal };
        StgMedium m;
        var ok = ((delegate* unmanaged[Stdcall]<void*, FormatEtc*, StgMedium*, int>)(*(void***)data)[3])(data, &f, &m) >= 0;
        if (ok && (m.Tymed != TymedHGlobal || m.Handle == 0))
        {
            ReleaseStgMedium(&m);
            ok = false;
        }

        medium = ok ? m : default;
        return ok;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct FormatEtc
    {
        public ushort Format;
        public nint Device;
        public uint Aspect;
        public int Index;
        public uint Tymed;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct StgMedium
    {
        public uint Tymed;
        public nint Handle;
        public nint UnkForRelease;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DropDescription
    {
        public int Type;
        public fixed char Message[260];
        public fixed char Insert[260];
    }

    [LibraryImport("user32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial uint RegisterClipboardFormatW(string name);

    [LibraryImport("shell32.dll")]
    private static partial uint DragQueryFileW(nint drop, uint index, char* file, uint length);

    [LibraryImport("ole32.dll")]
    private static partial void ReleaseStgMedium(StgMedium* medium);

    [LibraryImport("kernel32.dll")]
    private static partial nint GlobalAlloc(uint flags, nuint bytes);

    [LibraryImport("kernel32.dll")]
    private static partial nint GlobalFree(nint memory);

    [LibraryImport("kernel32.dll")]
    private static partial void* GlobalLock(nint memory);

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GlobalUnlock(nint memory);

    [LibraryImport("kernel32.dll")]
    private static partial nuint GlobalSize(nint memory);
}

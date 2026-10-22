// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.Platform.Windows/Shell/FileClipboard.cs

using System.Runtime.InteropServices;
using System.Text;

namespace Salandual.Platform.Windows.Shell;

public sealed record ClipboardContent
{
    public IReadOnlyList<string> Files { get; init; } = [];

    public bool Cut { get; init; }

    public string? Text { get; init; }

    public byte[]? Image { get; init; }

    public bool ImageIsPng { get; init; }
}

public static unsafe partial class FileClipboard
{
    private const uint CfUnicodeText = 13;
    private const uint CfHdrop = 15;
    private const uint CfDib = 8;
    private const uint DropEffectCopy = 1, DropEffectMove = 2, DropEffectLink = 4;
    private const uint GmemMoveable = 0x0002, GmemZeroInit = 0x0040;

    private static readonly uint _preferredDropEffect = RegisterClipboardFormatW("Preferred DropEffect");
    private static readonly uint _png = RegisterClipboardFormatW("PNG");

    public static uint SequenceNumber => GetClipboardSequenceNumber();

    public static bool SetFiles(nint owner, IReadOnlyList<string> paths, bool cut)
    {
        if (paths.Count == 0 || !Open(owner))
        {
            return false;
        }

        try
        {
            EmptyClipboard();

            var list = string.Concat(paths.Select(p => p + "\0")) + "\0";
            var bytes = Encoding.Unicode.GetBytes(list);
            var drop = GlobalAlloc(GmemMoveable | GmemZeroInit, (nuint)(20 + bytes.Length));
            var p = (byte*)GlobalLock(drop);
            *(uint*)p = 20;
            *(uint*)(p + 16) = 1;
            bytes.CopyTo(new Span<byte>(p + 20, bytes.Length));
            GlobalUnlock(drop);
            if (SetClipboardData(CfHdrop, drop) == 0)
            {
                GlobalFree(drop);
                return false;
            }

            var effect = GlobalAlloc(GmemMoveable, 4);
            *(uint*)GlobalLock(effect) = cut ? DropEffectMove : DropEffectCopy | DropEffectLink;
            GlobalUnlock(effect);
            if (SetClipboardData(_preferredDropEffect, effect) == 0)
            {
                GlobalFree(effect);
            }

            return true;
        }
        finally
        {
            CloseClipboard();
        }
    }

    public static bool SetText(nint owner, string text)
    {
        if (!Open(owner))
        {
            return false;
        }

        try
        {
            EmptyClipboard();
            var bytes = Encoding.Unicode.GetBytes(text + "\0");
            var memory = GlobalAlloc(GmemMoveable, (nuint)bytes.Length);
            bytes.CopyTo(new Span<byte>(GlobalLock(memory), bytes.Length));
            GlobalUnlock(memory);
            if (SetClipboardData(CfUnicodeText, memory) == 0)
            {
                GlobalFree(memory);
                return false;
            }

            return true;
        }
        finally
        {
            CloseClipboard();
        }
    }

    public static bool SetImage(nint owner, byte[] dib, byte[]? png)
    {
        if (!Open(owner))
        {
            return false;
        }

        try
        {
            EmptyClipboard();
            if (!Put(CfDib, dib))
            {
                return false;
            }

            if (png is not null)
            {
                Put(_png, png);
            }

            return true;
        }
        finally
        {
            CloseClipboard();
        }

        static bool Put(uint format, byte[] bytes)
        {
            var memory = GlobalAlloc(GmemMoveable, (nuint)bytes.Length);
            bytes.CopyTo(new Span<byte>(GlobalLock(memory), bytes.Length));
            GlobalUnlock(memory);
            if (SetClipboardData(format, memory) == 0)
            {
                GlobalFree(memory);
                return false;
            }

            return true;
        }
    }

    public static void Clear(nint owner)
    {
        if (Open(owner))
        {
            EmptyClipboard();
            CloseClipboard();
        }
    }

    public static ClipboardContent? Read(nint owner)
    {
        if (!Open(owner))
        {
            return null;
        }

        try
        {
            var files = new List<string>();
            var cut = false;
            if (IsClipboardFormatAvailable(CfHdrop) && GetClipboardData(CfHdrop) is var drop and not 0)
            {
                var count = DragQueryFileW(drop, 0xFFFFFFFF, null, 0);
                for (uint i = 0; i < count; i++)
                {
                    var length = DragQueryFileW(drop, i, null, 0);
                    var buffer = new char[length + 1];
                    fixed (char* b = buffer)
                    {
                        DragQueryFileW(drop, i, b, length + 1);
                    }

                    files.Add(new string(buffer, 0, (int)length));
                }

                if (IsClipboardFormatAvailable(_preferredDropEffect) && GetClipboardData(_preferredDropEffect) is var effect and not 0
                    && GlobalSize(effect) >= 4 && GlobalLock(effect) is var e and not null)
                {
                    cut = (*(uint*)e & DropEffectMove) != 0 && (*(uint*)e & DropEffectCopy) == 0;
                    GlobalUnlock(effect);
                }
            }

            string? text = null;
            if (IsClipboardFormatAvailable(CfUnicodeText) && GetClipboardData(CfUnicodeText) is var t and not 0 && GlobalLock(t) is var tp and not null)
            {
                var max = (int)(GlobalSize(t) / 2);
                var span = new ReadOnlySpan<char>(tp, max);
                var end = span.IndexOf('\0');
                text = new string(end >= 0 ? span[..end] : span);
                GlobalUnlock(t);
            }

            byte[]? image = null;
            var isPng = false;
            if (_png != 0 && IsClipboardFormatAvailable(_png) && Bytes(_png) is { } png)
            {
                image = png;
                isPng = true;
            }
            else if (IsClipboardFormatAvailable(CfDib) && Bytes(CfDib) is { } dib)
            {
                image = BmpFromDib(dib);
            }

            return new ClipboardContent { Files = files, Cut = cut, Text = text, Image = image, ImageIsPng = isPng };
        }
        finally
        {
            CloseClipboard();
        }
    }

    private static byte[]? Bytes(uint format)
    {
        var handle = GetClipboardData(format);
        if (handle == 0)
        {
            return null;
        }

        var size = (int)GlobalSize(handle);
        var p = GlobalLock(handle);
        if (p is null || size <= 0)
        {
            return null;
        }

        try
        {
            return new ReadOnlySpan<byte>(p, size).ToArray();
        }
        finally
        {
            GlobalUnlock(handle);
        }
    }

    public static byte[]? BmpFromDib(byte[] dib)
    {
        if (dib.Length < 40)
        {
            return null;
        }

        var headerSize = BitConverter.ToInt32(dib, 0);
        var bitCount = BitConverter.ToInt16(dib, 14);
        var compression = BitConverter.ToInt32(dib, 16);
        var colorsUsed = BitConverter.ToInt32(dib, 32);
        var masks = compression == 3 && headerSize == 40 ? 12 : 0;
        var palette = bitCount <= 8 ? (colorsUsed > 0 ? colorsUsed : 1 << bitCount) * 4 : colorsUsed * 4;
        var bmp = new byte[14 + dib.Length];
        bmp[0] = (byte)'B';
        bmp[1] = (byte)'M';
        BitConverter.TryWriteBytes(bmp.AsSpan(2), bmp.Length);
        BitConverter.TryWriteBytes(bmp.AsSpan(10), 14 + headerSize + masks + palette);
        dib.CopyTo(bmp, 14);
        return bmp;
    }

    private static bool Open(nint owner)
    {
        for (var i = 0; i < 10; i++)
        {
            if (OpenClipboard(owner) != 0)
            {
                return true;
            }

            Thread.Sleep(15);
        }

        return false;
    }

    [LibraryImport("user32.dll")]
    private static partial int OpenClipboard(nint owner);

    [LibraryImport("user32.dll")]
    private static partial int CloseClipboard();

    [LibraryImport("user32.dll")]
    private static partial int EmptyClipboard();

    [LibraryImport("user32.dll")]
    private static partial nint SetClipboardData(uint format, nint data);

    [LibraryImport("user32.dll")]
    private static partial nint GetClipboardData(uint format);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool IsClipboardFormatAvailable(uint format);

    [LibraryImport("user32.dll")]
    private static partial uint GetClipboardSequenceNumber();

    [LibraryImport("user32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial uint RegisterClipboardFormatW(string name);

    [LibraryImport("shell32.dll")]
    private static partial uint DragQueryFileW(nint drop, uint index, char* file, uint length);

    [LibraryImport("kernel32.dll")]
    private static partial nint GlobalAlloc(uint flags, nuint bytes);

    [LibraryImport("kernel32.dll")]
    private static partial void* GlobalLock(nint memory);

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GlobalUnlock(nint memory);

    [LibraryImport("kernel32.dll")]
    private static partial nuint GlobalSize(nint memory);

    [LibraryImport("kernel32.dll")]
    private static partial nint GlobalFree(nint memory);
}

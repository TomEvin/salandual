// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.Archives/SevenZip/Formats.cs

using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace Salandual.Archives.SevenZip;

internal sealed record SevenZipFormat(string Name, Guid ClassId, string[] Extensions, byte[][] Signatures, uint SignatureOffset, uint Flags)
{
    public bool CanFindSignature => (Flags & SevenZipIds.FlagFindSignature) != 0;

    public bool MatchesExtension(string extension) => Extensions.Contains(extension, StringComparer.OrdinalIgnoreCase);

    public bool MatchesStart(ReadOnlySpan<byte> start)
    {
        foreach (var signature in Signatures)
        {
            if (signature.Length > 0 && start.Length >= SignatureOffset + signature.Length
                && start.Slice((int)SignatureOffset, signature.Length).SequenceEqual(signature))
            {
                return true;
            }
        }

        return false;
    }
}

internal static unsafe class SevenZipLibrary
{
    private static readonly Lazy<SevenZipFormat[]> _formats = new(ReadFormats);
    private static readonly StrategyBasedComWrappers _wrappers = new();

    public static IReadOnlyList<SevenZipFormat> Formats => _formats.Value;

    public static IInArchive CreateInArchive(SevenZipFormat format)
    {
        var clsid = format.ClassId;
        var iid = typeof(IInArchive).GUID;
        nint pointer;
        Marshal.ThrowExceptionForHR(Native.CreateObject(&clsid, &iid, &pointer));
        try
        {
            return (IInArchive)_wrappers.GetOrCreateObjectForComInstance(pointer, CreateObjectFlags.UniqueInstance);
        }
        finally
        {
            Marshal.Release(pointer);
        }
    }

    public static IOutArchive CreateOutArchive(SevenZipFormat format)
    {
        var clsid = format.ClassId;
        var iid = typeof(IOutArchive).GUID;
        nint pointer;
        Marshal.ThrowExceptionForHR(Native.CreateObject(&clsid, &iid, &pointer));
        try
        {
            return (IOutArchive)_wrappers.GetOrCreateObjectForComInstance(pointer, CreateObjectFlags.UniqueInstance);
        }
        finally
        {
            Marshal.Release(pointer);
        }
    }

    public static int SetProperties(object handler, IReadOnlyList<(string Name, object Value)> properties)
    {
        if (properties.Count == 0)
        {
            return SevenZipIds.SOk;
        }

        if (handler is not ISetProperties setter)
        {
            return SevenZipIds.ENotImpl;
        }

        var names = new nint[properties.Count];
        var values = new PropVariant[properties.Count];
        try
        {
            for (var i = 0; i < properties.Count; i++)
            {
                names[i] = Marshal.StringToHGlobalUni(properties[i].Name);
                switch (properties[i].Value)
                {
                    case uint number:
                        values[i].Type = PropVariant.UInt32;
                        values[i].UInt32Value = number;
                        break;
                    case bool flag:
                        values[i].Type = PropVariant.Bool;
                        values[i].BoolValue = (short)(flag ? -1 : 0);
                        break;
                    case string text:
                        values[i].Type = PropVariant.Bstr;
                        values[i].Pointer = Marshal.StringToBSTR(text);
                        break;
                }
            }

            fixed (nint* n = names)
            fixed (PropVariant* v = values)
            {
                return setter.SetProperties(n, v, (uint)properties.Count);
            }
        }
        finally
        {
            for (var i = 0; i < properties.Count; i++)
            {
                Marshal.FreeHGlobal(names[i]);
                fixed (PropVariant* v = &values[i])
                {
                    Native.PropVariantClear(v);
                }
            }
        }
    }

    public static void Release(object handler)
    {
        if (handler is ComObject com)
        {
            com.FinalRelease();
        }
    }

    public static nint ToPointer<T>(T value) where T : class => (nint)ComInterfaceMarshaller<T>.ConvertToUnmanaged(value);

    private static SevenZipFormat[] ReadFormats()
    {
        uint count;
        Marshal.ThrowExceptionForHR(Native.GetNumberOfFormats(&count));
        var formats = new List<SevenZipFormat>((int)count);
        for (uint i = 0; i < count; i++)
        {
            var name = Read(i, SevenZipIds.HandlerName, v => v.AsString) ?? "";
            var classId = Read(i, SevenZipIds.HandlerClassId, v => v.AsBytes);
            if (classId is not { Length: 16 })
            {
                continue;
            }

            var extensions = (Read(i, SevenZipIds.HandlerExtension, v => v.AsString) ?? "")
                .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var signatures = new List<byte[]>();
            if (Read(i, SevenZipIds.HandlerSignature, v => v.AsBytes) is { Length: > 0 } single)
            {
                signatures.Add(single);
            }

            if (Read(i, SevenZipIds.HandlerMultiSignature, v => v.AsBytes) is { Length: > 0 } multi)
            {
                for (var at = 0; at < multi.Length;)
                {
                    var length = multi[at++];
                    if (at + length > multi.Length)
                    {
                        break;
                    }

                    signatures.Add(multi[at..(at + length)]);
                    at += length;
                }
            }

            var offset = (uint)(Read(i, SevenZipIds.HandlerSignatureOffset, v => v.AsUInt64) ?? 0);
            var flags = (uint)(Read(i, SevenZipIds.HandlerFlags, v => v.AsUInt64) ?? 0);
            formats.Add(new SevenZipFormat(name, new Guid(classId), extensions, [.. signatures], offset, flags));
        }

        return [.. formats];
    }

    private static T? Read<T>(uint format, uint propId, Func<PropVariant, T?> get)
    {
        var value = default(PropVariant);
        try
        {
            return Native.GetHandlerProperty2(format, propId, &value) == SevenZipIds.SOk ? get(value) : default;
        }
        finally
        {
            Native.PropVariantClear(&value);
        }
    }
}

// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.Archives/SevenZip/Interop.cs

using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace Salandual.Archives.SevenZip;

[GeneratedComInterface]
[Guid("23170F69-40C1-278A-0000-000300010000")]
internal unsafe partial interface ISequentialInStream
{
    [PreserveSig]
    int Read(byte* data, uint size, uint* processedSize);
}

[GeneratedComInterface]
[Guid("23170F69-40C1-278A-0000-000300030000")]
internal unsafe partial interface IInStream : ISequentialInStream
{
    [PreserveSig]
    int Seek(long offset, uint seekOrigin, ulong* newPosition);
}

[GeneratedComInterface]
[Guid("23170F69-40C1-278A-0000-000300020000")]
internal unsafe partial interface ISequentialOutStream
{
    [PreserveSig]
    int Write(byte* data, uint size, uint* processedSize);
}

[GeneratedComInterface]
[Guid("23170F69-40C1-278A-0000-000300040000")]
internal unsafe partial interface IOutStream : ISequentialOutStream
{
    [PreserveSig]
    int Seek(long offset, uint seekOrigin, ulong* newPosition);

    [PreserveSig]
    int SetSize(ulong newSize);
}

[GeneratedComInterface]
[Guid("23170F69-40C1-278A-0000-000000050000")]
internal unsafe partial interface IProgress
{
    [PreserveSig]
    int SetTotal(ulong total);

    [PreserveSig]
    int SetCompleted(ulong* completeValue);
}

[GeneratedComInterface]
[Guid("23170F69-40C1-278A-0000-000500100000")]
internal unsafe partial interface ICryptoGetTextPassword
{
    [PreserveSig]
    int CryptoGetTextPassword(nint* password);
}

[GeneratedComInterface]
[Guid("23170F69-40C1-278A-0000-000500110000")]
internal unsafe partial interface ICryptoGetTextPassword2
{
    [PreserveSig]
    int CryptoGetTextPassword2(int* passwordIsDefined, nint* password);
}

[GeneratedComInterface]
[Guid("23170F69-40C1-278A-0000-000600100000")]
internal unsafe partial interface IArchiveOpenCallback
{
    [PreserveSig]
    int SetTotal(ulong* files, ulong* bytes);

    [PreserveSig]
    int SetCompleted(ulong* files, ulong* bytes);
}

[GeneratedComInterface]
[Guid("23170F69-40C1-278A-0000-000600200000")]
internal unsafe partial interface IArchiveExtractCallback : IProgress
{
    [PreserveSig]
    int GetStream(uint index, nint* outStream, int askExtractMode);

    [PreserveSig]
    int PrepareOperation(int askExtractMode);

    [PreserveSig]
    int SetOperationResult(int opRes);
}

[GeneratedComInterface]
[Guid("23170F69-40C1-278A-0000-000600300000")]
internal unsafe partial interface IArchiveOpenVolumeCallback
{
    [PreserveSig]
    int GetProperty(uint propId, PropVariant* value);

    [PreserveSig]
    int GetStream(char* name, nint* inStream);
}

[GeneratedComInterface]
[Guid("23170F69-40C1-278A-0000-000600600000")]
internal unsafe partial interface IInArchive
{
    [PreserveSig]
    int Open(IInStream stream, ulong* maxCheckStartPosition, IArchiveOpenCallback openCallback);

    [PreserveSig]
    int Close();

    [PreserveSig]
    int GetNumberOfItems(uint* numItems);

    [PreserveSig]
    int GetProperty(uint index, uint propId, PropVariant* value);

    [PreserveSig]
    int Extract(uint* indices, uint numItems, int testMode, IArchiveExtractCallback extractCallback);

    [PreserveSig]
    int GetArchiveProperty(uint propId, PropVariant* value);

    [PreserveSig]
    int GetNumberOfProperties(uint* numProps);

    [PreserveSig]
    int GetPropertyInfo(uint index, nint* name, uint* propId, ushort* varType);

    [PreserveSig]
    int GetNumberOfArchiveProperties(uint* numProps);

    [PreserveSig]
    int GetArchivePropertyInfo(uint index, nint* name, uint* propId, ushort* varType);
}

[GeneratedComInterface]
[Guid("23170F69-40C1-278A-0000-000600800000")]
internal unsafe partial interface IArchiveUpdateCallback : IProgress
{
    [PreserveSig]
    int GetUpdateItemInfo(uint index, int* newData, int* newProps, uint* indexInArchive);

    [PreserveSig]
    int GetProperty(uint index, uint propId, PropVariant* value);

    [PreserveSig]
    int GetStream(uint index, nint* inStream);

    [PreserveSig]
    int SetOperationResult(int operationResult);
}

[GeneratedComInterface]
[Guid("23170F69-40C1-278A-0000-000600A00000")]
internal unsafe partial interface IOutArchive
{
    [PreserveSig]
    int UpdateItems(ISequentialOutStream outStream, uint numItems, IArchiveUpdateCallback updateCallback);

    [PreserveSig]
    int GetFileTimeType(uint* type);
}

[GeneratedComInterface]
[Guid("23170F69-40C1-278A-0000-000600030000")]
internal unsafe partial interface ISetProperties
{
    [PreserveSig]
    int SetProperties(nint* names, PropVariant* values, uint numProps);
}

[StructLayout(LayoutKind.Explicit, Size = 24)]
internal struct PropVariant
{
    public const ushort Empty = 0, Bstr = 8, Bool = 11, UInt32 = 19, UInt64 = 21, FileTime = 64;

    public const ushort TimePrecisionDos = 2;

    [FieldOffset(0)]
    public ushort Type;

    [FieldOffset(2)]
    public ushort TimePrecision;

    [FieldOffset(8)]
    public long Int64;

    [FieldOffset(8)]
    public nint Pointer;

    [FieldOffset(8)]
    public uint UInt32Value;

    [FieldOffset(8)]
    public short BoolValue;

    public readonly bool IsTrue => Type == Bool && BoolValue != 0;

    public readonly ulong? AsUInt64 => Type switch
    {
        UInt64 => (ulong)Int64,
        UInt32 => UInt32Value,
        _ => null,
    };

    public readonly string? AsString => Type == Bstr && Pointer != 0 ? Marshal.PtrToStringBSTR(Pointer) : null;

    public readonly byte[]? AsBytes
    {
        get
        {
            if (Type != Bstr || Pointer == 0)
            {
                return null;
            }

            var length = Marshal.ReadInt32(Pointer - 4);
            var bytes = new byte[length];
            Marshal.Copy(Pointer, bytes, 0, length);
            return bytes;
        }
    }

    public readonly DateTime? AsFileTimeUtc => Type == FileTime && Int64 > 0 ? DateTime.FromFileTimeUtc(Int64) : null;
}

internal static class SevenZipIds
{
    public const uint KpidPath = 3, KpidIsDir = 6, KpidSize = 7, KpidPackSize = 8, KpidAttrib = 9, KpidCTime = 10, KpidMTime = 12,
        KpidEncrypted = 15, KpidName = 4, KpidIsAnti = 21, KpidMethod = 22, KpidPosixAttrib = 53, KpidSymLink = 54,
        KpidIsAltStream = 63, KpidIsAux = 64, KpidIsDeleted = 65, KpidHardLink = 90;

    public const uint HandlerName = 0, HandlerClassId = 1, HandlerExtension = 2, HandlerAddExtension = 3,
        HandlerSignature = 6, HandlerMultiSignature = 7, HandlerSignatureOffset = 8, HandlerFlags = 11;

    public const uint FlagFindSignature = 1 << 3;

    public const int AskExtract = 0, AskTest = 1, AskSkip = 2;

    public const int ResultOk = 0, ResultUnsupportedMethod = 1, ResultDataError = 2, ResultCrcError = 3,
        ResultUnavailable = 4, ResultUnexpectedEnd = 5, ResultDataAfterEnd = 6, ResultIsNotArc = 7,
        ResultHeadersError = 8, ResultWrongPassword = 9;

    public const int SOk = 0, SFalse = 1;
    public const int EAbort = unchecked((int)0x80004004), EFail = unchecked((int)0x80004005);
    public const int ENotImpl = unchecked((int)0x80004001);
}

internal static unsafe partial class Native
{
    private const string Dll = "7z.dll";

    [DefaultDllImportSearchPaths(DllImportSearchPath.AssemblyDirectory)]
    [LibraryImport(Dll)]
    public static partial int CreateObject(Guid* clsid, Guid* iid, nint* outObject);

    [DefaultDllImportSearchPaths(DllImportSearchPath.AssemblyDirectory)]
    [LibraryImport(Dll)]
    public static partial int GetNumberOfFormats(uint* numFormats);

    [DefaultDllImportSearchPaths(DllImportSearchPath.AssemblyDirectory)]
    [LibraryImport(Dll)]
    public static partial int GetHandlerProperty2(uint formatIndex, uint propId, PropVariant* value);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("ole32.dll")]
    public static partial int PropVariantClear(PropVariant* value);
}

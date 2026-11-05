// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.Archives/SevenZip/UpdateCallbacks.cs

using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace Salandual.Archives.SevenZip;

[GeneratedComClass]
internal sealed unsafe partial class ArchiveOutStream(Stream stream) : IOutStream
{
    public int Write(byte* data, uint size, uint* processedSize)
    {
        try
        {
            stream.Write(new ReadOnlySpan<byte>(data, (int)Math.Min(size, int.MaxValue)));
            if (processedSize != null)
            {
                *processedSize = size;
            }

            return SevenZipIds.SOk;
        }
        catch (OperationCanceledException)
        {
            return SevenZipIds.EAbort;
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException or UnauthorizedAccessException)
        {
            return ex.HResult != 0 ? ex.HResult : SevenZipIds.EFail;
        }
    }

    public int Seek(long offset, uint seekOrigin, ulong* newPosition)
    {
        try
        {
            var position = stream.Seek(offset, (SeekOrigin)seekOrigin);
            if (newPosition != null)
            {
                *newPosition = (ulong)position;
            }

            return SevenZipIds.SOk;
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException or ArgumentException or NotSupportedException)
        {
            return SevenZipIds.EFail;
        }
    }

    public int SetSize(ulong newSize)
    {
        try
        {
            stream.SetLength((long)newSize);
            return SevenZipIds.SOk;
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException or ArgumentException or NotSupportedException)
        {
            return SevenZipIds.EFail;
        }
    }
}

[GeneratedComClass]
internal sealed unsafe partial class SequentialInStream(Stream stream) : ISequentialInStream
{
    public int Read(byte* data, uint size, uint* processedSize)
    {
        try
        {
            var read = stream.Read(new Span<byte>(data, (int)Math.Min(size, int.MaxValue)));
            if (processedSize != null)
            {
                *processedSize = (uint)read;
            }

            return SevenZipIds.SOk;
        }
        catch (OperationCanceledException)
        {
            return SevenZipIds.EAbort;
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException)
        {
            return ex.HResult != 0 ? ex.HResult : SevenZipIds.EFail;
        }
    }
}

internal sealed record UpdateItem(int OldIndex, string? NewPath, bool IsDirectory, long Size, DateTime? ModifiedUtc, DateTime? CreatedUtc, FileAttributes Attributes, Func<Stream>? Open)
{
    public bool IsNew => OldIndex < 0;
}

[GeneratedComClass]
internal sealed unsafe partial class UpdateCallback(IReadOnlyList<UpdateItem> items, IInArchive? old, bool tar, string? password, Func<string?>? oldPassword,
    Action<long, long>? progress, Func<UpdateItem, Exception, bool> openFailed, CancellationToken cancellation)
    : IArchiveUpdateCallback, ICryptoGetTextPassword2, ICryptoGetTextPassword, IDisposable
{
    private readonly List<Stream> _open = [];
    private long _total;

    public bool PasswordCancelled { get; private set; }

    public int SetTotal(ulong total)
    {
        _total = (long)total;
        return SevenZipIds.SOk;
    }

    public int SetCompleted(ulong* completeValue)
    {
        if (completeValue != null)
        {
            try
            {
                progress?.Invoke((long)*completeValue, _total);
            }
            catch (OperationCanceledException)
            {
                return SevenZipIds.EAbort;
            }
        }

        return cancellation.IsCancellationRequested ? SevenZipIds.EAbort : SevenZipIds.SOk;
    }

    public int GetUpdateItemInfo(uint index, int* newData, int* newProps, uint* indexInArchive)
    {
        var item = items[(int)index];
        if (newData != null)
        {
            *newData = item.IsNew ? 1 : 0;
        }

        if (newProps != null)
        {
            *newProps = item.IsNew || item.NewPath is not null ? 1 : 0;
        }

        if (indexInArchive != null)
        {
            *indexInArchive = item.IsNew ? uint.MaxValue : (uint)item.OldIndex;
        }

        return SevenZipIds.SOk;
    }

    public int GetProperty(uint index, uint propId, PropVariant* value)
    {
        *value = default;
        var item = items[(int)index];
        if (!item.IsNew && propId != SevenZipIds.KpidPath && old is not null)
        {
            return old.GetProperty((uint)item.OldIndex, propId, value);
        }

        switch (propId)
        {
            case SevenZipIds.KpidPath when item.NewPath is { } path:
                value->Type = PropVariant.Bstr;
                value->Pointer = Marshal.StringToBSTR(path);
                break;
            case SevenZipIds.KpidIsDir:
                value->Type = PropVariant.Bool;
                value->BoolValue = (short)(item.IsDirectory ? -1 : 0);
                break;
            case SevenZipIds.KpidIsAnti:
                value->Type = PropVariant.Bool;
                break;
            case SevenZipIds.KpidSize:
                value->Type = PropVariant.UInt64;
                value->Int64 = item.IsDirectory ? 0 : item.Size;
                break;
            case SevenZipIds.KpidAttrib when !tar:
                value->Type = PropVariant.UInt32;
                value->UInt32Value = (uint)(item.Attributes & (FileAttributes.ReadOnly | FileAttributes.Hidden | FileAttributes.System | FileAttributes.Archive | FileAttributes.Directory));
                break;
            case SevenZipIds.KpidPosixAttrib when tar:
                value->Type = PropVariant.UInt32;
                value->UInt32Value = item.IsDirectory ? 0x41EDu : 0x81A4u;
                break;
            case SevenZipIds.KpidMTime when item.ModifiedUtc is { } modified:
                value->Type = PropVariant.FileTime;
                value->Int64 = modified.ToFileTimeUtc();
                break;
            case SevenZipIds.KpidCTime when item.CreatedUtc is { } created && !tar:
                value->Type = PropVariant.FileTime;
                value->Int64 = created.ToFileTimeUtc();
                break;
        }

        return SevenZipIds.SOk;
    }

    public int GetStream(uint index, nint* inStream)
    {
        *inStream = 0;
        var item = items[(int)index];
        if (item.IsDirectory || item.Open is null)
        {
            return SevenZipIds.SOk;
        }

        while (true)
        {
            if (cancellation.IsCancellationRequested)
            {
                return SevenZipIds.EAbort;
            }

            try
            {
                var stream = item.Open();
                _open.Add(stream);
                *inStream = stream.CanSeek
                    ? SevenZipLibrary.ToPointer<ISequentialInStream>(new InStream(stream))
                    : SevenZipLibrary.ToPointer<ISequentialInStream>(new SequentialInStream(stream));
                return SevenZipIds.SOk;
            }
            catch (OperationCanceledException)
            {
                return SevenZipIds.EAbort;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                bool skip;
                try
                {
                    skip = openFailed(item, ex);
                }
                catch (OperationCanceledException)
                {
                    return SevenZipIds.EAbort;
                }

                if (skip)
                {
                    return SevenZipIds.SFalse;
                }
            }
        }
    }

    public int SetOperationResult(int operationResult) => SevenZipIds.SOk;

    public int CryptoGetTextPassword2(int* passwordIsDefined, nint* value)
    {
        *passwordIsDefined = password is null ? 0 : 1;
        *value = Marshal.StringToBSTR(password ?? "");
        return SevenZipIds.SOk;
    }

    public int CryptoGetTextPassword(nint* value)
    {
        if (oldPassword?.Invoke() is not { } given)
        {
            PasswordCancelled = true;
            return SevenZipIds.EAbort;
        }

        *value = Marshal.StringToBSTR(given);
        return SevenZipIds.SOk;
    }

    public void Dispose()
    {
        foreach (var stream in _open)
        {
            stream.Dispose();
        }

        _open.Clear();
    }
}

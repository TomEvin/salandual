// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.Archives/SevenZip/Callbacks.cs

using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace Salandual.Archives.SevenZip;

[GeneratedComClass]
internal sealed unsafe partial class InStream(Stream stream) : IInStream
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
        catch (Exception ex) when (ex is IOException or ObjectDisposedException or ArgumentException)
        {
            return SevenZipIds.EFail;
        }
    }
}

[GeneratedComClass]
internal sealed unsafe partial class OutStream(Stream stream, Action<long>? written = null) : ISequentialOutStream
{
    public int Write(byte* data, uint size, uint* processedSize)
    {
        try
        {
            stream.Write(new ReadOnlySpan<byte>(data, (int)Math.Min(size, int.MaxValue)));
            written?.Invoke(size);
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
}

internal delegate string? PasswordQuestion();

[GeneratedComClass]
internal sealed unsafe partial class OpenCallback(string firstVolume, PasswordQuestion? askPassword, CancellationToken cancellation, Action<long, long>? progress = null)
    : IArchiveOpenCallback, ICryptoGetTextPassword, IArchiveOpenVolumeCallback, IDisposable
{
    private readonly string _folder = Path.GetDirectoryName(firstVolume) ?? "";
    private readonly List<Stream> _volumes = [];
    private string _current = Path.GetFileName(firstVolume);

    public string? Password { get; private set; }

    public bool PasswordAsked { get; private set; }

    public bool PasswordCancelled { get; private set; }

    private long _total;

    public int SetTotal(ulong* files, ulong* bytes)
    {
        if (bytes != null)
        {
            _total = (long)*bytes;
        }

        return SevenZipIds.SOk;
    }

    public int SetCompleted(ulong* files, ulong* bytes)
    {
        if (bytes != null)
        {
            progress?.Invoke((long)*bytes, _total);
        }

        return cancellation.IsCancellationRequested ? SevenZipIds.EAbort : SevenZipIds.SOk;
    }

    public int CryptoGetTextPassword(nint* password)
    {
        PasswordAsked = true;
        Password ??= askPassword?.Invoke();
        if (Password is null)
        {
            PasswordCancelled = true;
            return SevenZipIds.EAbort;
        }

        *password = Marshal.StringToBSTR(Password);
        return SevenZipIds.SOk;
    }

    public int GetProperty(uint propId, PropVariant* value)
    {
        *value = default;
        if (propId == SevenZipIds.KpidName)
        {
            value->Type = PropVariant.Bstr;
            value->Pointer = Marshal.StringToBSTR(_current);
        }

        return SevenZipIds.SOk;
    }

    public int GetStream(char* name, nint* inStream)
    {
        *inStream = 0;
        try
        {
            var volume = new string(name);
            if (volume.Length == 0 || volume.IndexOfAny(['\\', '/', ':']) >= 0 || volume is "." or "..")
            {
                return SevenZipIds.SFalse;
            }

            var path = Path.Combine(_folder, volume);
            if (!File.Exists(path))
            {
                return SevenZipIds.SFalse;
            }

            var stream = new FileStream(path, FileMode.Open, System.IO.FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 1 << 16);
            _volumes.Add(stream);
            _current = volume;
            *inStream = SevenZipLibrary.ToPointer<IInStream>(new InStream(stream));
            return SevenZipIds.SOk;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return SevenZipIds.SFalse;
        }
    }

    public void Dispose()
    {
        foreach (var volume in _volumes)
        {
            volume.Dispose();
        }

        _volumes.Clear();
    }
}

[GeneratedComClass]
internal sealed unsafe partial class ExtractCallback(Func<uint, Stream?> target, Func<string?> password, Action<long>? progress, CancellationToken cancellation, Action<uint, bool>? done = null)
    : IArchiveExtractCallback, ICryptoGetTextPassword
{
    private uint _index;
    private bool _writing, _asked;

    public Dictionary<uint, int> Results { get; } = [];

    public bool PasswordCancelled { get; private set; }

    public int SetTotal(ulong total) => SevenZipIds.SOk;

    public int SetCompleted(ulong* completeValue)
    {
        if (completeValue != null)
        {
            progress?.Invoke((long)*completeValue);
        }

        return cancellation.IsCancellationRequested ? SevenZipIds.EAbort : SevenZipIds.SOk;
    }

    public int GetStream(uint index, nint* outStream, int askExtractMode)
    {
        *outStream = 0;
        _index = index;
        _writing = false;
        _asked = askExtractMode is SevenZipIds.AskExtract or SevenZipIds.AskTest;
        if (askExtractMode != SevenZipIds.AskExtract)
        {
            return SevenZipIds.SOk;
        }

        try
        {
            if (target(index) is { } stream)
            {
                _writing = true;
                *outStream = SevenZipLibrary.ToPointer<ISequentialOutStream>(new OutStream(stream, _ => cancellation.ThrowIfCancellationRequested()));
            }

            return SevenZipIds.SOk;
        }
        catch (OperationCanceledException)
        {
            return SevenZipIds.EAbort;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return ex.HResult != 0 ? ex.HResult : SevenZipIds.EFail;
        }
    }

    public int PrepareOperation(int askExtractMode) => SevenZipIds.SOk;

    public int SetOperationResult(int opRes)
    {
        if (_writing || opRes != SevenZipIds.ResultOk)
        {
            Results[_index] = opRes;
        }

        if (_asked)
        {
            done?.Invoke(_index, opRes == SevenZipIds.ResultOk);
        }

        _writing = _asked = false;
        return SevenZipIds.SOk;
    }

    public int CryptoGetTextPassword(nint* value)
    {
        if (password() is not { } given)
        {
            PasswordCancelled = true;
            return SevenZipIds.EAbort;
        }

        *value = Marshal.StringToBSTR(given);
        return SevenZipIds.SOk;
    }
}

// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.Archives/ArchiveWriter.cs

using Salandual.Archives.SevenZip;
using Salandual.Core.Operations;

namespace Salandual.Archives;

public sealed record PackInput(string SourcePath, string PathInArchive, bool IsDirectory, long Size, DateTime ModifiedUtc, DateTime CreatedUtc, FileAttributes Attributes);

public sealed record PackSettings(PackFormat Format, PackLevel Level = PackLevel.Normal, string? Password = null, bool HideNames = false, long VolumeSize = 0, bool StoreCompressed = true)
{
    public bool CanEncrypt => Format is PackFormat.Zip or PackFormat.SevenZip;
}

public sealed record ArchiveEdit(IReadOnlySet<int> Remove, IReadOnlyDictionary<int, string> Rename, IReadOnlyList<PackInput> Add)
{
    public static ArchiveEdit Adding(IReadOnlyList<PackInput> add) => new(new HashSet<int>(), new Dictionary<int, string>(), add);
}

public sealed record WriteCallbacks(Action<long, long>? Progress, CancellationToken Cancellation, Func<PackInput, Exception, bool>? OpenFailed = null, Action<int>? Read = null);

public static unsafe class ArchiveWriter
{
    private static readonly HashSet<string> _compressed = new(StringComparer.OrdinalIgnoreCase)
    {
        "jpg", "jpeg", "jfif", "png", "gif", "webp", "heic", "heif", "avif", "jxl", "jp2",
        "mp3", "m4a", "aac", "ogg", "oga", "opus", "flac", "wma", "ape",
        "mp4", "m4v", "mkv", "mov", "avi", "wmv", "webm", "3gp", "mpg", "mpeg", "flv",
        "zip", "7z", "rar", "gz", "tgz", "bz2", "tbz2", "xz", "txz", "zst", "lz", "lzma", "cab", "jar", "apk", "msi", "dmg",
        "docx", "xlsx", "pptx", "odt", "ods", "odp", "epub", "nupkg", "whl",
    };

    public static string Extension(PackFormat format) => format switch
    {
        PackFormat.SevenZip => ".7z",
        PackFormat.Tar => ".tar",
        PackFormat.TarGz => ".tar.gz",
        PackFormat.TarXz => ".tar.xz",
        _ => ".zip",
    };

    public static bool CanWrite(string formatName) => formatName is "zip" or "7z" or "tar";

    public static PackFormat? FormatOf(string formatName) => formatName switch
    {
        "zip" => PackFormat.Zip,
        "7z" => PackFormat.SevenZip,
        "tar" => PackFormat.Tar,
        _ => null,
    };

    public static bool IsCompressedAlready(string name)
    {
        var dot = name.LastIndexOf('.');
        return dot >= 0 && _compressed.Contains(name[(dot + 1)..]);
    }

    public static IReadOnlyList<string> Create(string target, IReadOnlyList<PackInput> inputs, PackSettings settings, WriteCallbacks callbacks, string? finalName = null)
        => Library(() => CreateCore(target, inputs, settings, callbacks, finalName ?? Path.GetFileName(target)), Path.GetFileName(target));

    public static IReadOnlyList<string> Update(ArchiveContents contents, string target, ArchiveEdit edit, PackSettings settings, WriteCallbacks callbacks, ArchivePasswordQuestion? askPassword, string name)
        => Library(() => UpdateStoring(contents, target, edit, settings, callbacks, askPassword, name), name);

    private static IReadOnlyList<string> UpdateStoring(ArchiveContents contents, string target, ArchiveEdit edit, PackSettings settings, WriteCallbacks callbacks, ArchivePasswordQuestion? askPassword, string name)
    {
        var stored = edit.Add.Where(i => !i.IsDirectory && IsCompressedAlready(i.PathInArchive)).ToList();
        var packed = edit.Add.Count(i => !i.IsDirectory) - stored.Count;
        if (!settings.StoreCompressed || settings.Level == PackLevel.Store || settings.Format == PackFormat.Tar || stored.Count == 0)
        {
            return UpdateCore(contents, target, edit, settings, callbacks, askPassword, name);
        }

        if (packed == 0 && edit.Remove.Count == 0)
        {
            return UpdateCore(contents, target, edit, settings with { Level = PackLevel.Store }, callbacks, askPassword, name);
        }

        var between = target + ".pass1";
        try
        {
            UpdateCore(contents, between, edit with { Add = [.. edit.Add.Except(stored)] }, settings, callbacks, askPassword, name);
            var password = settings.Password ?? contents.Password;
            var middle = ArchiveReader.Open(between, name, password is { } p ? (_, _) => p : askPassword, searchInside: false, callbacks.Cancellation);
            middle.Password = password;
            return UpdateCore(middle, target, ArchiveEdit.Adding(stored), settings with { Level = PackLevel.Store }, callbacks, askPassword, name);
        }
        finally
        {
            File.Delete(between);
        }
    }

    private static IReadOnlyList<string> CreateCore(string target, IReadOnlyList<PackInput> inputs, PackSettings settings, WriteCallbacks callbacks, string finalName)
    {
        if (settings.Format is PackFormat.TarGz or PackFormat.TarXz)
        {
            return WriteCompressedTar(target, inputs, settings, callbacks, finalName);
        }

        if (settings.StoreCompressed && settings.Level != PackLevel.Store && settings.Format != PackFormat.Tar)
        {
            var stored = inputs.Where(i => !i.IsDirectory && IsCompressedAlready(i.PathInArchive)).ToList();
            var packed = inputs.Where(i => i.IsDirectory || !IsCompressedAlready(i.PathInArchive)).ToList();
            var storedBytes = stored.Sum(i => i.Size);
            var packedBytes = packed.Sum(i => i.Size);
            if (stored.Count > 0 && packed.Count(i => !i.IsDirectory) > 0)
            {
                var storeFirst = storedBytes <= packedBytes;
                var (first, second) = storeFirst ? (stored, packed) : (packed, stored);
                var firstLevel = storeFirst ? PackLevel.Store : settings.Level;
                var total = storedBytes + packedBytes + (storeFirst ? storedBytes : packedBytes);
                var firstBytes = first.Sum(i => i.Size);
                var between = target + ".pass1";
                try
                {
                    WriteNew(between, first, settings with { Level = firstLevel, VolumeSize = 0 }, callbacks with { Progress = (done, _) => callbacks.Progress?.Invoke(done, total) });
                    var contents = ArchiveReader.Open(between, Path.GetFileName(target), settings.Password is { } p ? (_, _) => p : null, searchInside: false, callbacks.Cancellation);
                    contents.Password = settings.Password;
                    return UpdateCore(contents, target, ArchiveEdit.Adding(second), settings with { Level = storeFirst ? settings.Level : PackLevel.Store },
                        callbacks with { Progress = (done, _) => callbacks.Progress?.Invoke(firstBytes + done, total) }, null, Path.GetFileName(target));
                }
                finally
                {
                    File.Delete(between);
                }
            }

            if (stored.Count > 0 && packed.All(i => i.IsDirectory))
            {
                settings = settings with { Level = PackLevel.Store };
            }
        }

        return WriteNew(target, inputs, settings, callbacks);
    }

    private static IReadOnlyList<string> WriteNew(string target, IReadOnlyList<PackInput> inputs, PackSettings settings, WriteCallbacks callbacks)
    {
        var format = Format(settings.Format == PackFormat.SevenZip ? "7z" : settings.Format == PackFormat.Tar ? "tar" : "zip");
        var archive = SevenZipLibrary.CreateOutArchive(format);
        try
        {
            Check(SevenZipLibrary.SetProperties(archive, Properties(settings)));
            return Write(archive, null, target, Items(inputs, callbacks), settings, callbacks, null);
        }
        finally
        {
            SevenZipLibrary.Release(archive);
        }
    }

    private static IReadOnlyList<string> UpdateCore(ArchiveContents contents, string target, ArchiveEdit edit, PackSettings settings, WriteCallbacks callbacks, ArchivePasswordQuestion? askPassword, string name)
    {
        var format = SevenZipLibrary.Formats.First(f => f.ClassId == contents.FormatId);
        if (!CanWrite(format.Name))
        {
            throw new ArchiveException(ArchiveProblem.CantChange, name);
        }

        using var file = new FileStream(contents.ArchivePath, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16);
        if (file.Length != contents.FileLength)
        {
            throw new ArchiveException(ArchiveProblem.Changed, name);
        }

        string? Password()
        {
            contents.Password ??= askPassword?.Invoke(name, false);
            return contents.Password;
        }

        var archive = SevenZipLibrary.CreateInArchive(format);
        using var open = new OpenCallback(contents.ArchivePath, Password, callbacks.Cancellation);
        try
        {
            ulong max = 0;
            if (archive.Open(new InStream(file), &max, open) != SevenZipIds.SOk)
            {
                callbacks.Cancellation.ThrowIfCancellationRequested();
                throw new ArchiveException(open.PasswordCancelled ? ArchiveProblem.PasswordCancelled : ArchiveProblem.Damaged, name);
            }

            uint count;
            _ = archive.GetNumberOfItems(&count);
            var items = new List<UpdateItem>((int)count + edit.Add.Count);
            for (var i = 0; i < count; i++)
            {
                if (edit.Remove.Contains(i))
                {
                    continue;
                }

                items.Add(new UpdateItem(i, edit.Rename.GetValueOrDefault(i), false, 0, null, null, 0, null));
            }

            items.AddRange(Items(edit.Add, callbacks));
            var output = (IOutArchive)archive;
            Check(SevenZipLibrary.SetProperties(output, Properties(settings)));
            var written = Write(output, archive, target, items, settings, callbacks, Password);
            archive.Close();
            return written;
        }
        finally
        {
            SevenZipLibrary.Release(archive);
        }
    }

    private static IReadOnlyList<string> Write(IOutArchive archive, IInArchive? old, string target, List<UpdateItem> items, PackSettings settings, WriteCallbacks callbacks, Func<string?>? oldPassword)
    {
        Stream output = settings.VolumeSize > 0
            ? new VolumeWriter(n => target + "." + n.ToString("000", System.Globalization.CultureInfo.InvariantCulture), settings.VolumeSize)
            : new FileStream(target, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None, 1 << 16);
        using var callback = new UpdateCallback(items, old, settings.Format == PackFormat.Tar, settings.CanEncrypt ? settings.Password : null, oldPassword,
            callbacks.Progress, (item, ex) => callbacks.OpenFailed?.Invoke(Input(item), ex) ?? throw ex, callbacks.Cancellation);
        var ok = false;
        IReadOnlyList<string> files = [];
        try
        {
            var hr = archive.UpdateItems(new ArchiveOutStream(output), (uint)items.Count, callback);
            callbacks.Cancellation.ThrowIfCancellationRequested();
            if (callback.PasswordCancelled)
            {
                throw new ArchiveException(ArchiveProblem.PasswordCancelled, Path.GetFileName(target));
            }

            if (hr != SevenZipIds.SOk)
            {
                throw new IOException(Marshal(hr));
            }

            output.Flush();
            ok = true;
        }
        finally
        {
            files = output is VolumeWriter volumes ? volumes.Files : [target];
            output.Dispose();
            if (!ok)
            {
                foreach (var written in files)
                {
                    try
                    {
                        File.Delete(written);
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                    {
                    }
                }
            }
        }

        return files;
    }

    private static IReadOnlyList<string> WriteCompressedTar(string target, IReadOnlyList<PackInput> inputs, PackSettings settings, WriteCallbacks callbacks, string finalName)
    {
        var pipe = new Pipe();
        var tarSize = inputs.Sum(i => 512 + (i.IsDirectory ? 0 : (i.Size + 511) / 512 * 512)) + 1024 + 10240;
        var tar = Task.Run(() =>
        {
            var writer = pipe.Writer;
            var handler = SevenZipLibrary.CreateOutArchive(Format("tar"));
            try
            {
                var items = Items(inputs, callbacks);
                using var callback = new UpdateCallback(items, null, tar: true, null, null, callbacks.Progress,
                    (item, ex) => callbacks.OpenFailed?.Invoke(Input(item), ex) ?? throw ex, callbacks.Cancellation);
                var hr = handler.UpdateItems(new OutStream(writer), (uint)items.Count, callback);
                if (hr != SevenZipIds.SOk)
                {
                    pipe.Fail();
                    callbacks.Cancellation.ThrowIfCancellationRequested();
                    throw new IOException(Marshal(hr));
                }

                pipe.Complete();
            }
            catch
            {
                pipe.Fail();
                throw;
            }
            finally
            {
                SevenZipLibrary.Release(handler);
            }
        });

        var compressor = SevenZipLibrary.CreateOutArchive(Format(settings.Format == PackFormat.TarGz ? "gzip" : "xz"));
        IReadOnlyList<string> files = [];
        try
        {
            Check(SevenZipLibrary.SetProperties(compressor, [("x", LevelNumber(settings.Level))]));
            var tarName = finalName;
            tarName = tarName.EndsWith(".gz", StringComparison.OrdinalIgnoreCase) || tarName.EndsWith(".xz", StringComparison.OrdinalIgnoreCase) ? tarName[..^3] : tarName + ".tar";
            var reader = pipe.Reader;
            var one = new UpdateItem(-1, tarName, false, tarSize, DateTime.UtcNow, null, FileAttributes.Normal, () => reader);
            files = Write(compressor, null, target, [one], settings with { Format = PackFormat.Tar, Password = null }, callbacks with { Progress = null }, null);
        }
        catch (Exception ex)
        {
            pipe.Fail();
            WaitQuietly(tar);
            if (tar.Exception?.InnerException is { } first && first is not Pipe.PipeStoppedException && ex is not OperationCanceledException)
            {
                System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw(first);
            }

            throw;
        }
        finally
        {
            SevenZipLibrary.Release(compressor);
        }

        tar.GetAwaiter().GetResult();
        return files;
    }

    private static void WaitQuietly(Task task)
    {
        try
        {
            task.Wait();
        }
        catch (AggregateException)
        {
        }
    }

    private static List<UpdateItem> Items(IReadOnlyList<PackInput> inputs, WriteCallbacks callbacks)
        => [.. inputs.Select(i => new UpdateItem(-1, i.PathInArchive, i.IsDirectory, i.Size, i.ModifiedUtc, i.CreatedUtc, i.Attributes,
            i.IsDirectory ? null : () => new PackFileStream(i.SourcePath, i.Size, callbacks.Read)))];

    private static PackInput Input(UpdateItem item)
        => new("", item.NewPath ?? "", item.IsDirectory, item.Size, item.ModifiedUtc ?? default, item.CreatedUtc ?? default, item.Attributes);

    private static List<(string, object)> Properties(PackSettings settings)
    {
        var properties = new List<(string, object)>();
        if (settings.Format == PackFormat.Tar)
        {
            return properties;
        }

        properties.Add(("x", LevelNumber(settings.Level)));
        if (settings.Format == PackFormat.Zip && settings.Password is not null)
        {
            properties.Add(("em", "AES256"));
        }

        if (settings.Format == PackFormat.SevenZip && settings.Password is not null && settings.HideNames)
        {
            properties.Add(("he", true));
        }

        return properties;
    }

    private static uint LevelNumber(PackLevel level) => level switch
    {
        PackLevel.Store => 0u,
        PackLevel.Fast => 1u,
        PackLevel.Maximum => 9u,
        _ => 5u,
    };

    private static SevenZipFormat Format(string name) => SevenZipLibrary.Formats.First(f => string.Equals(f.Name, name, StringComparison.OrdinalIgnoreCase));

    private static void Check(int hr)
    {
        if (hr != SevenZipIds.SOk)
        {
            throw new IOException(Marshal(hr));
        }
    }

    private static string Marshal(int hr) => System.Runtime.InteropServices.Marshal.GetExceptionForHR(hr)?.Message ?? hr.ToString("X8", System.Globalization.CultureInfo.InvariantCulture);

    private static T Library<T>(Func<T> work, string name)
    {
        try
        {
            return work();
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException)
        {
            throw new ArchiveException(ArchiveProblem.EngineMissing, name);
        }
    }
}

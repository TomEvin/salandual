// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.Archives/ArchiveReader.cs

using Salandual.Archives.SevenZip;
using Salandual.Core.Resources;

namespace Salandual.Archives;

public enum ArchiveProblem
{
    EngineMissing,
    NotAnArchive,
    Damaged,
    WrongPassword,
    PasswordCancelled,
    UnsupportedMethod,
    CantChange,
    Changed,
}

public sealed class ArchiveException(ArchiveProblem problem, string name)
    : IOException(Loc.Format(problem switch
    {
        ArchiveProblem.EngineMissing => Strings.Archive_EngineMissing,
        ArchiveProblem.NotAnArchive => Strings.Archive_NotAnArchive,
        ArchiveProblem.Damaged => Strings.Archive_Damaged,
        ArchiveProblem.WrongPassword => Strings.Archive_WrongPassword,
        ArchiveProblem.PasswordCancelled => Strings.Archive_PasswordCancelled,
        ArchiveProblem.CantChange => Strings.Archive_CantChange,
        ArchiveProblem.Changed => Strings.Archive_Changed,
        _ => Strings.Archive_UnsupportedMethod,
    }, name))
{
    public ArchiveProblem Problem { get; } = problem;
}

public delegate string? ArchivePasswordQuestion(string archiveName, bool wrongBefore);

public enum ExtractResult
{
    Ok,
    Damaged,
    ChecksumError,
    Truncated,
    WrongPassword,
    UnsupportedMethod,
    Skipped,
}

public static unsafe class ArchiveReader
{
    private const ulong SearchDistance = 8 << 20;

    private static readonly HashSet<string> _programFormats = new(StringComparer.OrdinalIgnoreCase) { "PE", "TE", "ELF", "MachO", "MUB", "COFF" };

    public static bool IsAvailable
    {
        get
        {
            try
            {
                return SevenZipLibrary.Formats.Count > 0;
            }
            catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException)
            {
                return false;
            }
        }
    }

    public static IReadOnlySet<string> KnownExtensions => Library(() => SevenZipLibrary.Formats.SelectMany(f => f.Extensions).ToHashSet(StringComparer.OrdinalIgnoreCase), "");

    public static ArchiveContents Open(string path, string name, ArchivePasswordQuestion? askPassword, bool searchInside = true, CancellationToken cancellation = default, Action<long, long>? progress = null)
        => Library(() => OpenCore(path, name, askPassword, searchInside, cancellation, progress), name);

    private static ArchiveContents OpenCore(string path, string name, ArchivePasswordQuestion? askPassword, bool searchInside, CancellationToken cancellation, Action<long, long>? progress)
    {
        var info = new FileInfo(path);
        using var file = VolumeStream.Open(path);
        var start = new byte[(int)Math.Min(file.Length, 1 << 16)];
        file.ReadExactly(start);

        var extension = Path.GetExtension(VolumeStream.ArchiveName(path)).TrimStart('.');
        var formats = SevenZipLibrary.Formats;
        var byExtension = formats.Where(f => extension.Length > 0 && f.MatchesExtension(extension));
        var bySignature = formats.Where(f => f.MatchesStart(start));
        var first = byExtension.Concat(bySignature).Distinct().OrderBy(f => _programFormats.Contains(f.Name)).ToList();

        var wrong = false;
        string? password = null;
        while (true)
        {
            cancellation.ThrowIfCancellationRequested();
            var asked = false;
            foreach (var (format, distance) in first.Select(f => (f, 0UL))
                .Concat(searchInside ? formats.Where(f => f.CanFindSignature && !first.Contains(f)).Select(f => (f, SearchDistance)) : []))
            {
                var given = password;
                var result = TryOpen(file, path, format, distance, () => given ??= askPassword?.Invoke(name, wrong), cancellation, progress, out var contents, info, out var passwordAsked);
                asked |= passwordAsked;
                if (result == OpenResult.Opened)
                {
                    contents!.Password = given;
                    contents.NamesHidden = passwordAsked;
                    return contents;
                }

                if (result == OpenResult.Cancelled)
                {
                    cancellation.ThrowIfCancellationRequested();
                    throw new ArchiveException(ArchiveProblem.PasswordCancelled, name);
                }

                if (passwordAsked)
                {
                    break;
                }
            }

            if (!asked)
            {
                throw new ArchiveException(first.Count > 0 && !first.All(f => _programFormats.Contains(f.Name)) ? ArchiveProblem.Damaged : ArchiveProblem.NotAnArchive, name);
            }

            wrong = true;
            password = null;
        }
    }

    private enum OpenResult { Opened, NotThisFormat, Cancelled }

    private static OpenResult TryOpen(Stream file, string path, SevenZipFormat format, ulong distance, Func<string?> password,
        CancellationToken cancellation, Action<long, long>? progress, out ArchiveContents? contents, FileInfo info, out bool passwordAsked)
    {
        contents = null;
        file.Position = 0;
        var archive = SevenZipLibrary.CreateInArchive(format);
        using var callback = new OpenCallback(path, () => password(), cancellation, progress);
        try
        {
            var max = distance;
            var hr = archive.Open(new InStream(file), &max, callback);
            passwordAsked = callback.PasswordAsked;
            if (callback.PasswordCancelled || (hr == SevenZipIds.EAbort && cancellation.IsCancellationRequested))
            {
                return OpenResult.Cancelled;
            }

            if (hr != SevenZipIds.SOk)
            {
                return OpenResult.NotThisFormat;
            }

            try
            {
                contents = new ArchiveContents(path, format.Name, format.ClassId, ReadItems(archive, path, cancellation), file.Length, info.LastWriteTimeUtc, null);
                return OpenResult.Opened;
            }
            finally
            {
                archive.Close();
            }
        }
        finally
        {
            SevenZipLibrary.Release(archive);
        }
    }

    private static List<ArchiveItem> ReadItems(IInArchive archive, string path, CancellationToken cancellation)
    {
        uint count;
        _ = archive.GetNumberOfItems(&count);
        var items = new List<ArchiveItem>((int)Math.Min(count, 1 << 20));
        for (uint i = 0; i < count; i++)
        {
            if ((i & 1023) == 0)
            {
                cancellation.ThrowIfCancellationRequested();
            }

            if (Bool(archive, i, SevenZipIds.KpidIsAltStream) || Bool(archive, i, SevenZipIds.KpidIsAux) || Bool(archive, i, SevenZipIds.KpidIsDeleted))
            {
                continue;
            }

            var raw = String(archive, i, SevenZipIds.KpidPath) ?? "";
            var isDir = Bool(archive, i, SevenZipIds.KpidIsDir);
            var shown = ArchivePaths.Shown(raw);
            if (shown.Length == 0)
            {
                if (isDir)
                {
                    continue;
                }

                shown = ArchivePaths.SingleName(VolumeStream.ArchiveName(path));
            }

            var attrib = (uint)(UInt64(archive, i, SevenZipIds.KpidAttrib) ?? 0);
            var attributes = (FileAttributes)(attrib & (uint)(FileAttributes.ReadOnly | FileAttributes.Hidden | FileAttributes.System | FileAttributes.Archive));
            var encrypted = Bool(archive, i, SevenZipIds.KpidEncrypted);
            if (encrypted)
            {
                attributes |= FileAttributes.Encrypted;
            }

            if (isDir)
            {
                attributes |= FileAttributes.Directory;
            }

            var hardLink = String(archive, i, SevenZipIds.KpidHardLink) is { Length: > 0 } hard ? hard : null;
            var linkTarget = hardLink ?? (String(archive, i, SevenZipIds.KpidSymLink) is { Length: > 0 } sym ? sym : null);
            items.Add(new ArchiveItem((int)i, shown, raw, isDir, isDir ? 0 : (long)(UInt64(archive, i, SevenZipIds.KpidSize) ?? 0),
                (long)(UInt64(archive, i, SevenZipIds.KpidPackSize) ?? 0), FileTime(archive, i, SevenZipIds.KpidMTime), attributes, encrypted, linkTarget is not null, linkTarget, hardLink is not null));
        }

        return items;
    }

    public static IReadOnlyDictionary<int, ExtractResult> Extract(ArchiveContents archive, IReadOnlyList<int> indices, Func<int, Stream?> target,
        ArchivePasswordQuestion? askPassword, string name, Action<long>? progress = null, CancellationToken cancellation = default,
        Action<int, bool>? entryDone = null, bool test = false)
        => Library(() => ExtractCore(archive, indices, target, askPassword, name, progress, cancellation, entryDone, test), name);

    private static Dictionary<int, ExtractResult> ExtractCore(ArchiveContents contents, IReadOnlyList<int> indices, Func<int, Stream?> target,
        ArchivePasswordQuestion? askPassword, string name, Action<long>? progress, CancellationToken cancellation, Action<int, bool>? entryDone, bool test)
    {
        var results = new Dictionary<int, ExtractResult>();
        var todo = indices.Distinct().Order().ToList();
        var wrong = false;
        var format = SevenZipLibrary.Formats.First(f => f.ClassId == contents.FormatId);
        while (todo.Count > 0)
        {
            using var file = VolumeStream.Open(contents.ArchivePath);
            if (file.Length != contents.FileLength)
            {
                throw new ArchiveException(ArchiveProblem.Damaged, name);
            }

            var archive = SevenZipLibrary.CreateInArchive(format);
            var askedNow = wrong;
            string? Password()
            {
                if (contents.Password is null || askedNow)
                {
                    askedNow = false;
                    contents.Password = askPassword?.Invoke(name, wrong);
                }

                return contents.Password;
            }

            using var open = new OpenCallback(contents.ArchivePath, Password, cancellation);
            var callback = new ExtractCallback(i => target((int)i), Password, progress, cancellation, entryDone is null ? null : (i, ok) => entryDone((int)i, ok));
            try
            {
                var max = SearchDistance;
                if (archive.Open(new InStream(file), &max, open) != SevenZipIds.SOk)
                {
                    cancellation.ThrowIfCancellationRequested();
                    throw new ArchiveException(open.PasswordCancelled ? ArchiveProblem.PasswordCancelled : ArchiveProblem.Damaged, name);
                }

                var ids = todo.Select(i => (uint)i).ToArray();
                int hr;
                fixed (uint* p = ids)
                {
                    hr = archive.Extract(p, (uint)ids.Length, test ? 1 : 0, callback);
                }

                archive.Close();
                cancellation.ThrowIfCancellationRequested();
                if (callback.PasswordCancelled)
                {
                    throw new ArchiveException(ArchiveProblem.PasswordCancelled, name);
                }

                if (hr != SevenZipIds.SOk && callback.Results.Count == 0)
                {
                    throw new ArchiveException(ArchiveProblem.Damaged, name);
                }
            }
            finally
            {
                SevenZipLibrary.Release(archive);
            }

            var again = new List<int>();
            foreach (var index in todo)
            {
                var result = callback.Results.TryGetValue((uint)index, out var r) ? r : SevenZipIds.ResultOk;
                var item = contents.ItemAt(index);
                if (item is { Encrypted: true } && result is SevenZipIds.ResultWrongPassword or SevenZipIds.ResultDataError or SevenZipIds.ResultCrcError)
                {
                    again.Add(index);
                    continue;
                }

                results[index] = result switch
                {
                    SevenZipIds.ResultOk => ExtractResult.Ok,
                    SevenZipIds.ResultUnsupportedMethod => ExtractResult.UnsupportedMethod,
                    SevenZipIds.ResultWrongPassword => ExtractResult.WrongPassword,
                    SevenZipIds.ResultCrcError => ExtractResult.ChecksumError,
                    SevenZipIds.ResultUnexpectedEnd => ExtractResult.Truncated,
                    _ => ExtractResult.Damaged,
                };
            }

            if (again.Count > 0 && askPassword is null)
            {
                foreach (var index in again)
                {
                    results[index] = ExtractResult.WrongPassword;
                }

                break;
            }

            todo = again;
            wrong = again.Count > 0;
            if (wrong)
            {
                contents.Password = null;
            }
        }

        return results;
    }

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

    private static bool Bool(IInArchive archive, uint index, uint propId) => Property(archive, index, propId, v => v.IsTrue);

    private static string? String(IInArchive archive, uint index, uint propId) => Property(archive, index, propId, v => v.AsString);

    private static ulong? UInt64(IInArchive archive, uint index, uint propId) => Property(archive, index, propId, v => v.AsUInt64);

    private static DateTime? FileTime(IInArchive archive, uint index, uint propId)
        => Property(archive, index, propId, v => v.TimePrecision == PropVariant.TimePrecisionDos ? FromDosTime(v.AsFileTimeUtc, TimeZoneInfo.Local, DateTime.UtcNow) : v.AsFileTimeUtc);

    internal static DateTime? FromDosTime(DateTime? utc, TimeZoneInfo zone, DateTime now)
    {
        if (utc is not { } time)
        {
            return null;
        }

        var wall = DateTime.SpecifyKind(time + zone.GetUtcOffset(now), DateTimeKind.Unspecified);
        return DateTime.SpecifyKind(wall - zone.GetUtcOffset(wall), DateTimeKind.Utc);
    }

    private static T Property<T>(IInArchive archive, uint index, uint propId, Func<PropVariant, T> get)
    {
        var value = default(PropVariant);
        try
        {
            return archive.GetProperty(index, propId, &value) == SevenZipIds.SOk ? get(value) : get(default);
        }
        finally
        {
            Native.PropVariantClear(&value);
        }
    }
}

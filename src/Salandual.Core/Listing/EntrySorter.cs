// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.Core/Listing/EntrySorter.cs

using System.Globalization;
using Salandual.Core.Vfs;

namespace Salandual.Core.Listing;

public enum SortColumn { Name, Extension, Time, Size }

public readonly record struct SortSpec(SortColumn Column, bool Descending = false)
{
    public static SortSpec Default => new(SortColumn.Name);

    public SortSpec Toggle(SortColumn column) => column == Column ? this with { Descending = !Descending } : new SortSpec(column);
}

public sealed class EntrySorter(CultureInfo culture, bool numericSort)
{
    public const int SortKeyThreshold = 2000;

    private readonly CompareInfo _compare = culture.CompareInfo;
    private readonly CompareOptions _options = CompareOptions.IgnoreCase | (numericSort ? CompareOptions.NumericOrdering : CompareOptions.None);

    public bool NumericSort { get; } = numericSort;

    public static EntrySorter ForCurrentCulture(bool numericSort) => new(CultureInfo.CurrentCulture, numericSort);

    public void Sort(FileEntry[] entries, SortSpec spec, int sortKeyThreshold = SortKeyThreshold)
    {
        if (entries.Length < 2)
        {
            return;
        }

        if (entries.Length >= sortKeyThreshold)
        {
            SortWithKeys(entries, spec);
        }
        else
        {
            Array.Sort(entries, (a, b) => CompareDirect(a, b, spec));
        }
    }

    private int CompareDirect(in FileEntry a, in FileEntry b, SortSpec spec)
    {
        if (a.IsDirectory != b.IsDirectory)
        {
            return a.IsDirectory ? -1 : 1;
        }

        var c = CompareColumn(a, b, spec.Column);
        if (c == 0 && spec.Column == SortColumn.Extension && !a.IsDirectory)
        {
            c = _compare.Compare(a.Extension, b.Extension, _options);
        }

        if (c == 0)
        {
            c = _compare.Compare(a.Name, b.Name, _options);
        }

        if (c == 0)
        {
            c = string.CompareOrdinal(a.Name, b.Name);
        }

        return spec.Descending ? -c : c;
    }

    private static int CompareColumn(in FileEntry a, in FileEntry b, SortColumn column) => column switch
    {
        SortColumn.Time => a.LastWriteUtcTicks.CompareTo(b.LastWriteUtcTicks),
        SortColumn.Size when !a.IsDirectory => a.Size.CompareTo(b.Size),
        _ => 0,
    };

    private void SortWithKeys(FileEntry[] entries, SortSpec spec)
    {
        var source = (FileEntry[])entries.Clone();
        var nameKeys = new byte[source.Length][];
        var extKeys = spec.Column == SortColumn.Extension ? new byte[source.Length][] : null;
        Parallel.For(0, source.Length, i =>
        {
            nameKeys[i] = SortKey(source[i].Name);
            if (extKeys is not null)
            {
                extKeys[i] = SortKey(source[i].Extension);
            }
        });

        var order = new int[source.Length];
        for (var i = 0; i < order.Length; i++)
        {
            order[i] = i;
        }

        Array.Sort(order, (x, y) =>
        {
            ref readonly var a = ref source[x];
            ref readonly var b = ref source[y];
            if (a.IsDirectory != b.IsDirectory)
            {
                return a.IsDirectory ? -1 : 1;
            }

            var c = CompareColumn(a, b, spec.Column);
            if (c == 0 && extKeys is not null && !a.IsDirectory)
            {
                c = extKeys[x].AsSpan().SequenceCompareTo(extKeys[y]);
            }

            if (c == 0)
            {
                c = nameKeys[x].AsSpan().SequenceCompareTo(nameKeys[y]);
            }

            if (c == 0)
            {
                c = string.CompareOrdinal(a.Name, b.Name);
            }

            return spec.Descending ? -c : c;
        });

        for (var i = 0; i < order.Length; i++)
        {
            entries[i] = source[order[i]];
        }
    }

    private byte[] SortKey(ReadOnlySpan<char> text)
    {
        var key = new byte[_compare.GetSortKeyLength(text, _options)];
        var written = _compare.GetSortKey(text, key, _options);
        return written == key.Length ? key : key[..written];
    }
}

public static class EntryFilter
{
    public static FileEntry[] Apply(FileEntry[] entries, bool showHidden)
        => showHidden ? entries : Array.FindAll(entries, e => !e.IsHidden);
}

// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.Core/Panels/InformationLine.cs

using System.Globalization;
using System.Text;
using Salandual.Core.Listing;
using Salandual.Core.Vfs;

using Salandual.Core.Resources;

namespace Salandual.Core.Panels;

public static class InformationLine
{
    public static string Expand(string template, in FileEntry entry, bool isUp, DisplayFormat format, long? directorySize = null)
    {
        if (isUp)
        {
            return Strings.InfoLine_Parent;
        }

        var local = entry.LastWriteUtc.ToLocalTime();
        var sb = new StringBuilder(template.Length + 32);
        var i = 0;
        while (i < template.Length)
        {
            var start = template.IndexOf("$(", i, StringComparison.Ordinal);
            var end = start < 0 ? -1 : template.IndexOf(')', start + 2);
            if (start < 0 || end < 0)
            {
                sb.Append(template, i, template.Length - i);
                break;
            }

            sb.Append(template, i, start - i);
            var name = template.AsSpan(start + 2, end - start - 2);
            string? value = name switch
            {
                "FileName" => entry.Name,
                "FileNameOnly" => entry.BaseName.ToString(),
                "FileExt" => entry.Extension.ToString(),
                "FileSize" => !entry.IsDirectory ? format.Size(entry.Size) : directorySize is { } d ? format.Size(d) : Strings.InfoLine_Folder,
                "FileDate" => entry.LastWriteUtcTicks > 0 ? format.Date(local) : "",
                "FileTime" => entry.LastWriteUtcTicks > 0 ? format.Time(local) : "",
                "FileAttributes" => DisplayFormat.Attributes(entry.Attributes),
                _ => null,
            };
            sb.Append(value ?? template.Substring(start, end - start + 1));
            i = end + 1;
        }

        if (entry.LastWriteUtcTicks > 0)
        {
            return sb.ToString();
        }

        var text = sb.ToString();
        while (text.Contains(", ,", StringComparison.Ordinal))
        {
            text = text.Replace(", ,", ",", StringComparison.Ordinal);
        }

        return text.TrimEnd(',', ' ');
    }

    public static string Summary(FilePanel panel, DisplayFormat format, CultureInfo culture)
    {
        var total = panel.Count - (panel.HasUpEntry ? 1 : 0);
        var selection = panel.GetSelectionSummary();
        if (selection.Count > 0)
        {
            return string.Format(culture, Strings.InfoLine_Selected, selection.Count, total, format.Size(selection.Bytes));
        }

        if (FilterSummary(panel, culture) is { } filter)
        {
            return filter;
        }

        long bytes = 0;
        foreach (var e in panel.Items)
        {
            if (!e.IsDirectory)
            {
                bytes += e.Size;
            }
        }

        return string.Format(culture, total == 1 ? Strings.InfoLine_Items_One : Strings.InfoLine_Items_Other, total, format.Size(bytes));
    }

    public static string? FilterSummary(FilePanel panel, CultureInfo culture)
    {
        if (panel.Results is not null)
        {
            return null;
        }

        var (shown, all, names) = (panel.ShownCount, panel.ListedCount, panel.HiddenNameCount);
        var hidden = names == 0 ? null : string.Format(culture, names == 1 ? Strings.InfoLine_NamesHidden_One : Strings.InfoLine_NamesHidden_Other, names);
        if (panel.IsQuickFiltering)
        {
            return string.Format(culture, Strings.InfoLine_QuickFilter, shown, all);
        }

        if (panel.IsFiltered)
        {
            return hidden is null
                ? string.Format(culture, Strings.InfoLine_Filter, shown, all, all - shown)
                : string.Format(culture, Strings.InfoLine_FilterAndNames, shown, all, hidden);
        }

        return hidden;
    }
}

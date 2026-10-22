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
                "FileDate" => format.Date(local),
                "FileTime" => format.Time(local),
                "FileAttributes" => DisplayFormat.Attributes(entry.Attributes),
                _ => null,
            };
            sb.Append(value ?? template.Substring(start, end - start + 1));
            i = end + 1;
        }

        return sb.ToString();
    }

    public static string Summary(FilePanel panel, DisplayFormat format, CultureInfo culture)
    {
        var total = panel.Count - (panel.HasUpEntry ? 1 : 0);
        var selection = panel.GetSelectionSummary();
        if (selection.Count > 0)
        {
            return string.Format(culture, Strings.InfoLine_Selected, selection.Count, total, format.Size(selection.Bytes));
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
}

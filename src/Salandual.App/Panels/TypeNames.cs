// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.App/Panels/TypeNames.cs

using System.Collections.Concurrent;
using Salandual.App.Resources;
using Salandual.Core.Panels;
using Salandual.Core.Vfs;
using Salandual.Platform.Windows.FileSystem;

namespace Salandual.App.Panels;

internal static class TypeNames
{
    private static readonly ConcurrentDictionary<string, string> _names = new(StringComparer.OrdinalIgnoreCase);

    public static string Of(in FileEntry e)
        => _names.GetOrAdd(e.IsDirectory ? "/" : e.Extension.ToString(), static key =>
        {
            var name = FileDetailsReader.TypeName(key == "/" ? "folder" : "file." + key, key == "/" ? FileAttributes.Directory : FileAttributes.Normal);
            return name.Length > 0 ? name : key.ToUpperInvariant();
        });
}

internal static class ColumnNames
{
    public static string Of(PanelColumn column) => column switch
    {
        PanelColumn.Name => AppStrings.Columns_Name,
        PanelColumn.Extension => AppStrings.Columns_Extension,
        PanelColumn.Type => AppStrings.Columns_Type,
        PanelColumn.Size => AppStrings.Columns_Size,
        PanelColumn.Items => AppStrings.Columns_Items,
        PanelColumn.Modified => AppStrings.Columns_Modified,
        PanelColumn.Created => AppStrings.Columns_Created,
        PanelColumn.Accessed => AppStrings.Columns_Accessed,
        PanelColumn.Attributes => AppStrings.Columns_Attributes,
        PanelColumn.Git => AppStrings.Columns_Git,
        _ => AppStrings.Columns_DosName,
    };
}

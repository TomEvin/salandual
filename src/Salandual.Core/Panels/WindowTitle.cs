// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.Core/Panels/WindowTitle.cs

using System.Text;

using Salandual.Core.Resources;

namespace Salandual.Core.Panels;

public static class WindowTitle
{
    public const string Default = "$(Folder) — $(App)";

    public static IReadOnlyList<(string Name, string Template)> Presets { get; } =
    [
        (Strings.TitlePreset_FolderApp, Default),
        (Strings.TitlePreset_PathApp, "$(Path) — $(App)"),
        (Strings.TitlePreset_AppPath, "$(App) — $(Path)"),
        (Strings.TitlePreset_Folder, "$(Folder)"),
    ];

    public static string Expand(string template, string path, string app = ProductInfo.ShortName)
    {
        if (path.Length == 0)
        {
            return app;
        }

        var root = Path.GetPathRoot(path) ?? "";
        var trimmed = path.TrimEnd('\\', '/');
        var folder = trimmed.Length <= root.TrimEnd('\\', '/').Length ? root.TrimEnd('\\', '/') : Path.GetFileName(trimmed);
        var sb = new StringBuilder(template.Length + path.Length);
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
            var value = template.AsSpan(start + 2, end - start - 2) switch
            {
                "Folder" => folder,
                "Path" => path,
                "Drive" => root.TrimEnd('\\', '/'),
                "App" => app,
                _ => template.Substring(start, end - start + 1),
            };
            sb.Append(value);
            i = end + 1;
        }

        var text = sb.ToString().Trim();
        return text.Length > 0 ? text : app;
    }
}

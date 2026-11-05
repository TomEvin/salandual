// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.App/Diagnostics/ThirdParty.cs

using Salandual.App.Resources;
using Salandual.Core;
using Salandual.Core.Resources;

namespace Salandual.App.Diagnostics;

internal sealed record Component(string Name, string What, string Version, string Licence, string Holder, params string[] Licences)
{
    public string Text => string.Join("\n\n", Licences.Select(file => ThirdParty.Read(file).Replace("{0}", Holder, StringComparison.Ordinal)));
}

internal static class ThirdParty
{
    public static IReadOnlyList<Component> All { get; } =
    [
        new(ProductInfo.DisplayName, AppStrings.TP_Salandual, ProductInfo.DisplayVersion, "GPL-3.0-or-later", "", "GPL-3.0.txt"),
        new("Avalonia", AppStrings.TP_Avalonia, "12.1.3", "MIT", "the Avalonia authors", "MIT.txt"),
        new(".NET", AppStrings.TP_DotNet, "10.0", "MIT", ".NET Foundation and Contributors", "MIT.txt"),
        new("Magick.NET, ImageMagick", AppStrings.TP_Magick, "14.17.1", "Apache-2.0, ImageMagick", "", "ImageMagick.txt", "Apache-2.0.txt"),
        new("MediaInfo", AppStrings.TP_MediaInfo, "26.01", "BSD-2-Clause", "", "MediaInfo.txt"),
        new("AvaloniaEdit", AppStrings.TP_AvaloniaEdit, "12.0.0", "MIT", "the AvaloniaEdit and AvalonEdit authors", "MIT.txt"),
        new("YamlDotNet", AppStrings.TP_Yaml, "18.1.0", "MIT", "Antoine Aubry and contributors", "MIT.txt"),
        new("Markdig", AppStrings.TP_Markdig, "1.4.0", "BSD-2-Clause", "Alexandre Mutel", "BSD-2-Clause.txt"),
        new("MetadataExtractor", AppStrings.TP_Metadata, "2.9.3", "Apache-2.0", "", "Apache-2.0.txt"),
        new("XmpCore", AppStrings.TP_Xmp, "6.1.10.1", "BSD-3-Clause", "Adobe Systems Incorporated", "BSD-3-Clause.txt"),
        new("BCnEncoder.NET", AppStrings.TP_BCn, "2.3.0", "MIT OR Unlicense", "the BCnEncoder.NET authors", "MIT.txt"),
        new("CommunityToolkit.HighPerformance", AppStrings.TP_UsedByBCn, "8.4.0", "MIT", ".NET Foundation and Contributors", "MIT.txt"),
        new("Microsoft.Bcl.Numerics", AppStrings.TP_UsedByBCn, "10.0.3", "MIT", ".NET Foundation and Contributors", "MIT.txt"),
        new("Microsoft.Windows.CsWin32", AppStrings.TP_CsWin32, "0.3.335", "MIT", "Microsoft Corporation", "MIT.txt"),
        new("7-Zip", AppStrings.TP_SevenZip, "26.04", "LGPL-2.1-or-later with the unRAR restriction, BSD", "", "7-Zip.txt"),
        new("FluentFTP", AppStrings.TP_FluentFtp, "55.1.0", "MIT", "2015 Robin Rodricks and FluentFTP Contributors", "MIT.txt"),
        new("SSH.NET", AppStrings.TP_SshNet, "2026.0.0", "MIT", "Renci 2010-2026", "MIT.txt"),
        new("BouncyCastle.Cryptography", AppStrings.TP_UsedBySshNet, "2.7.0", "MIT", "2000-2026 The Legion of the Bouncy Castle Inc.", "MIT.txt"),
        new("Microsoft.Extensions.Logging.Abstractions, Microsoft.Extensions.DependencyInjection.Abstractions", AppStrings.TP_UsedByFtp, "8.0.3, 8.0.2", "MIT", ".NET Foundation and Contributors", "MIT.txt"),
        new("LibGit2Sharp", AppStrings.TP_LibGit2Sharp, "0.31.0", "MIT", "LibGit2Sharp contributors", "MIT.txt"),
        new("libgit2", AppStrings.TP_LibGit2, "3f4182d", "GPL-2.0 with the linking exception", "", "libgit2.txt"),
        new("Velopack", AppStrings.TP_Velopack, "1.2.161", "MIT", "Velopack Ltd", "MIT.txt"),
        new(AppStrings.TP_Themes, AppStrings.TP_Themes_What, "", "MIT", "Microsoft Corporation, and the authors of each theme", "MIT.txt"),
        new("Open Salamander, Samandarin", AppStrings.TP_Salamander, "", "GPL-2.0-or-later", "", "GPL-3.0.txt"),
    ];

    public static string Read(string file)
    {
        using var stream = typeof(ThirdParty).Assembly.GetManifestResourceStream("Licences/" + file);
        if (stream is null)
        {
            return "";
        }

        using var reader = new StreamReader(stream);
        return reader.ReadToEnd().ReplaceLineEndings("\n").TrimEnd();
    }

    public static string Note(Component c) => c.Licence.StartsWith("GPL-2", StringComparison.Ordinal) && c.Licence.Contains("or-later", StringComparison.Ordinal) ? Loc.Format(AppStrings.TP_UsedUnderGpl3, c.Licence) : "";
}

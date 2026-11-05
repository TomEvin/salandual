// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.App/Diagnostics/AppFacts.cs

using System.Runtime.InteropServices;
using Salandual.App.Resources;
using Salandual.Core;
using Salandual.Core.Diagnostics;
using Salandual.Core.Resources;
using Salandual.Core.Settings;
using Salandual.Platform.Windows.Shell;

namespace Salandual.App.Diagnostics;

internal static class AppFacts
{
    private static string? _windows;

    public static string Build => ProductInfo.Commit.Length > 0 ? ProductInfo.Version + "+" + ProductInfo.Commit : ProductInfo.Version;

    public static string Architecture => RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant();

    public static string Version => Loc.Format(AppStrings.About_VersionFact, ProductInfo.DisplayVersion, Build, Architecture);

    public static string Windows => _windows ??= SystemFacts.WindowsName();

    public static string DotNet => Environment.Version.ToString();

    public static string Rendering(RenderingMode mode) => mode == RenderingMode.Gpu ? AppStrings.About_Rendering_Gpu : AppStrings.About_Rendering_Software;

    public static string VersionLine()
    {
        var day = ProductInfo.BuildDate?.ToString("d MMMM yyyy", Loc.TextCulture) ?? "";
        return ProductInfo.Commit.Length > 0
            ? Loc.Format(AppStrings.About_VersionLine, ProductInfo.DisplayVersion, day, ProductInfo.Commit)
            : Loc.Format(AppStrings.About_VersionLineNoBuild, ProductInfo.DisplayVersion, day);
    }

    public static string Line(RenderingMode mode)
        => Loc.Format(AppStrings.About_Facts, ProductInfo.DisplayName, $"{ProductInfo.DisplayVersion} ({Build}) {Architecture}", Windows, DotNet, mode == RenderingMode.Gpu ? "GPU" : AppStrings.About_Rendering_Software);

    public static string VersionInfo(RenderingMode mode, string settingsFolder)
        => string.Join(Environment.NewLine,
            ProductInfo.DisplayName + " " + Version,
            Loc.Format(AppStrings.About_InfoLine, AppStrings.About_Fact_Windows, Windows),
            Loc.Format(AppStrings.About_InfoLine, AppStrings.About_Fact_Net, DotNet),
            Loc.Format(AppStrings.About_InfoLine, AppStrings.About_Fact_Rendering, Rendering(mode)),
            Loc.Format(AppStrings.About_InfoLine, AppStrings.About_Fact_Settings, PersonalPaths.Hide(settingsFolder, UserFolder)));

    public static string UserFolder => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
}

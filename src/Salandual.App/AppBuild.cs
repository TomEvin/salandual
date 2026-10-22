// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.App/AppBuild.cs

using Salandual.App.Resources;
using Salandual.Core;
using Salandual.Core.Resources;

namespace Salandual.App;

internal static class AppBuild
{
#if DEBUG
    public const bool IsDev = true;
#else
    public const bool IsDev = false;
#endif

    public static string Name => IsDev ? Loc.Format(AppStrings.App_DevName, ProductInfo.ShortName) : ProductInfo.ShortName;

    public static string TitleName(bool withVersion) => withVersion ? Name + " " + ProductInfo.DisplayVersion : Name;

    public static string IconUri => IsDev ? "avares://salandual/Assets/salandual-dev.ico" : "avares://salandual/Assets/salandual.ico";

    public static string LogoUri => IsDev ? "avares://salandual/Assets/salandual-dev-192.png" : "avares://salandual/Assets/salandual-192.png";
}

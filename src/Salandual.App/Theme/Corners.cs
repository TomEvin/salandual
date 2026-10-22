// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.App/Theme/Corners.cs

using Avalonia;

namespace Salandual.App.Theme;

internal static class Corners
{
    public const double ChipRadius = 4;

    public static readonly CornerRadius Chip = new(ChipRadius);

    public static readonly CornerRadius Inner = new(2);
}

// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.Core/Resources/Loc.cs

using System.Globalization;

namespace Salandual.Core.Resources;

public static class Loc
{
    public static string Format(string format, params object?[] args) => string.Format(CultureInfo.CurrentCulture, format, args);

    public static string Plural(long count, string one, string other, params object?[] more)
        => string.Format(CultureInfo.CurrentCulture, count == 1 ? one : other, [count, .. more]);
}

// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.Core/Commands/CommandText.cs

using Salandual.Core.Resources;

namespace Salandual.Core.Commands;

public static class CommandText
{
    public static string Title(string id) => Strings.ResourceManager.GetString("Command_" + Key(id), Strings.Culture) ?? id;

    public static string? ShortLabel(string id) => Strings.ResourceManager.GetString("CommandShort_" + Key(id), Strings.Culture);

    public static string Category(string category) => Strings.ResourceManager.GetString("CommandCategory_" + category, Strings.Culture) ?? category;

    public static string Milestone(string plannedFor)
        => plannedFor.StartsWith("Phase ", StringComparison.Ordinal) ? Loc.Format(Strings.Milestone_Phase, plannedFor["Phase ".Length..]) : plannedFor;

    private static string Key(string id) => id.Replace('.', '_');
}

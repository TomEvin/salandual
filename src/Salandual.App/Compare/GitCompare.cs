// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.App/Compare/GitCompare.cs

using Salandual.Core.Commands;

namespace Salandual.App.Compare;

internal sealed record GitCompare(string Title, string LeftLabel, string? RightLabel, bool RightReadOnly, string Keys, KeyChord? StageChord, Func<Task>? Stage);

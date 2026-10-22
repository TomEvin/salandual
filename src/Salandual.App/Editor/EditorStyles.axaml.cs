// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.App/Editor/EditorStyles.axaml.cs

using Avalonia.Markup.Xaml;
using Avalonia.Styling;

namespace Salandual.App.Editor;

internal sealed class EditorStyles : Styles
{
    public EditorStyles() => AvaloniaXamlLoader.Load(this);
}

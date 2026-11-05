// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.App/Controls/DriveMenuFlyout.cs

using Avalonia.Controls;
using Avalonia.Controls.Primitives;

namespace Salandual.App.Controls;

internal sealed class DriveMenuFlyout : MenuFlyout
{
    public double MaxMenuHeight { get; set; } = double.PositiveInfinity;

    protected override Control CreatePresenter()
    {
        var presenter = base.CreatePresenter();
        presenter.MaxHeight = MaxMenuHeight;
        ScrollViewer.SetVerticalScrollBarVisibility(presenter, ScrollBarVisibility.Auto);
        return presenter;
    }
}

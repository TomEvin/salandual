// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.App/Options/OptionsWindow.UserMenu.cs

using Avalonia.Controls;
using Salandual.App.Resources;
using Salandual.Core.UserMenu;
using static Salandual.App.Options.OptionsUi;

namespace Salandual.App.Options;

public sealed partial class OptionsWindow
{
    internal Func<UserMenuContext?>? UserMenuPreview { get; set; }

    internal Func<UserMenuItem, Avalonia.Media.IImage?>? UserMenuIcons { get; set; }

    internal UserMenuEditor? UserMenuEditor { get; private set; }

    private void AddUserMenu()
    {
        const string page = MainWindow.UserMenuPage;
        var kw = AppStrings.Opt_UserMenu_Kw;
        Add(page, "", null, null, () => UserMenuEditor = new UserMenuEditor(Settings, _services.Starter, _services.Commands, () => UserMenuPreview?.Invoke(), UserMenuIcons), wide: true, keywords: kw);
        Add(page, AppStrings.OptSec_UserMenuShow, AppStrings.Opt_UserMenu_Unfit, AppStrings.Opt_UserMenu_Unfit_Desc, () => Segmented(
            [(UserMenuUnfit.Hidden, AppStrings.Opt_UserMenu_Hidden), (UserMenuUnfit.Greyed, AppStrings.Opt_UserMenu_Greyed)],
            Current.UserMenu.Unfit, v => Settings.Update(s => s with { UserMenu = s.UserMenu with { Unfit = v } })), keywords: kw);

        var bar = AppStrings.Bar_UserMenuBar;
        Add(page, bar, AppStrings.Opt_UserMenuBarShown, AppStrings.Opt_UserMenuBarShown_Desc,
            () => Toggle(Current.Layout.Bars.UserMenuBar, on => UpdateLayout(l => l with { Bars = l.Bars with { UserMenuBar = on } })), keywords: AppStrings.Opt_ShowHide_Kw + " " + AppStrings.Bar_UserMenuBar_Kw);
        Add(page, bar, AppStrings.Opt_BarButtons, AppStrings.Opt_BarButtons_Desc, () => Segmented(
            [(false, AppStrings.Opt_BarNames_Names), (true, AppStrings.Opt_BarNames_IconsOnly)],
            Current.Layout.Blocks.IconsOnly, iconsOnly => UpdateLayout(l => l with { Blocks = l.Blocks with { IconsOnly = iconsOnly } })), keywords: AppStrings.Bar_UserMenuBar_Kw);
        Add(page, bar, AppStrings.Opt_LockBars, AppStrings.Opt_LockBars_Desc,
            () => Toggle(Current.Layout.Blocks.Locked, on => UpdateLayout(l => l with { Blocks = l.Blocks with { Locked = on } })), keywords: AppStrings.Opt_BarPlaces_Kw);
    }
}

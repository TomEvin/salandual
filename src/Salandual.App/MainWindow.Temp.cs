// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.App/MainWindow.Temp.cs

using Salandual.App.Resources;
using Salandual.Core.Resources;
using Salandual.Core.Settings;

namespace Salandual.App;

public partial class MainWindow
{
    internal async Task CheckLeftoverTempAsync()
    {
        var temp = _services.Temp;
        var leftovers = await Task.Run(() =>
        {
            temp.DeleteClosed();
            return temp.FindLeftovers();
        });
        if (leftovers.Files == 0 && leftovers.Entries.Count == 0)
        {
            return;
        }

        if (!_services.Settings.Current.Confirmations.DeleteLeftoverTemp)
        {
            await Task.Run(() => temp.Delete(leftovers));
            return;
        }

        var format = CurrentFormat();
        ShowNoticeInTurn(
            Loc.Format(AppStrings.Temp_Leftovers, Files(leftovers.Files), format.Size(leftovers.Bytes)),
            30,
            (AppStrings.Temp_Delete, () => _ = DeleteLeftoverTempAsync(leftovers)),
            (AppStrings.Temp_Show, () => _ = ActivePanel.Panel.NavigateAsync(temp.Root)),
            (AppStrings.Temp_NotNow, () => { }));
    }

    private async Task DeleteLeftoverTempAsync(TempLeftovers leftovers)
    {
        var stay = await Task.Run(() => _services.Temp.Delete(leftovers));
        ShowNotice(stay > 0
            ? Loc.Format(AppStrings.Temp_InUse, Files(stay))
            : Loc.Format(AppStrings.Temp_Deleted, Files(leftovers.Files), CurrentFormat().Size(leftovers.Bytes)));
        foreach (var host in _panels)
        {
            if (host.Panel.Path.StartsWith(_services.Temp.Root, StringComparison.OrdinalIgnoreCase))
            {
                _ = host.Panel.RefreshAsync();
            }
        }
    }

    private static string Files(long count) => Loc.Plural(count, AppStrings.Temp_Files_One, AppStrings.Temp_Files_Other);
}

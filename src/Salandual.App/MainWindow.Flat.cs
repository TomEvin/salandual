// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.App/MainWindow.Flat.cs

using Salandual.App.Dialogs;
using Salandual.App.Panels;
using Salandual.App.Resources;
using Salandual.Core.Panels;
using Salandual.Core.Resources;

namespace Salandual.App;

public partial class MainWindow
{
    internal ConfirmDialog? FlatQuestion { get; private set; }

    private void WireFlat(PanelHost host) => host.Panel.AskToReadOn = files => AskToReadOnAsync(host, files);

    internal void ToggleFlat(PanelHost host, IReadOnlyList<string>? folders = null)
    {
        var panel = host.Panel;
        if (panel.IsFlat)
        {
            _ = panel.LeaveResultsAsync();
        }
        else if (panel.Results is not null)
        {
            ShowNotice(AppStrings.Flat_NotInResults);
        }
        else if (panel.Path.Length == 0 || ArchiveAt(host) is not null)
        {
            ShowNotice(AppStrings.Flat_NotHere);
        }
        else
        {
            _ = panel.ShowFlatAsync(folders ?? SelectedFolders(panel));
        }

        host.View.Focus();
    }

    private static List<string> SelectedFolders(FilePanel panel)
    {
        var folders = new List<string>();
        for (var i = 0; i < panel.Count; i++)
        {
            if (panel.IsSelected(i) && panel.Items[i].IsDirectory && !panel.IsUp(i))
            {
                folders.Add(panel.Items[i].Name);
            }
        }

        return folders;
    }

    private async Task<bool> AskToReadOnAsync(PanelHost host, int files)
    {
        var folder = host.Panel.Results?.ReturnPath ?? host.Panel.Path;
        FlatQuestion = new ConfirmDialog(AppStrings.Flat_AskTitle, Loc.Format(AppStrings.Flat_AskText, files, folder), AppStrings.Flat_ReadOn, AppStrings.Flat_StopHere);
        try
        {
            return await FlatQuestion.ShowDialog<bool?>(this) == true;
        }
        finally
        {
            FlatQuestion = null;
            host.View.Focus();
        }
    }
}

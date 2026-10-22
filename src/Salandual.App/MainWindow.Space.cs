// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.App/MainWindow.Space.cs

using Salandual.App.Dialogs;
using Salandual.App.Panels;
using Salandual.App.Resources;
using Salandual.Platform.Windows.FileSystem;

namespace Salandual.App;

public partial class MainWindow
{
    internal OccupiedSpaceDialog? SpaceDialog { get; private set; }

    internal async Task OccupiedSpaceAsync(PanelHost host)
    {
        if (SpaceDialog is not null)
        {
            SpaceDialog.Activate();
            return;
        }

        var panel = host.Panel;
        if (OperationItems(panel) is not var (items, what))
        {
            ShowNotice(AppStrings.Space_Nothing);
            return;
        }

        var folder = panel.Path;
        var other = OtherPanel(host).Panel.Path;
        var sources = new OccupiedSpaceSources(
            () => VolumeInfo.Read(folder) is { } v && !DiskSpace.IsMadeUp(v.Total) ? new SpaceVolume(v.Root.TrimEnd('\\'), v.Total, v.Free, v.ClusterSize) : null,
            () => other.Length > 0 ? VolumeInfo.Read(other)?.ClusterSize ?? 0 : 0,
            FileDetailsReader.Allocated,
            FileClipboardAccess.SetText);
        var dialog = SpaceDialog = new OccupiedSpaceDialog(what, items, CurrentFormat(), sources);
        try
        {
            await dialog.ShowDialog(this);
        }
        finally
        {
            SpaceDialog = null;
            host.View.Focus();
        }
    }
}

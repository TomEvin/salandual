// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.App/MainWindow.FileList.cs

using System.Text;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Salandual.App.Viewer;
using Salandual.App.Dialogs;
using Salandual.App.Panels;
using Salandual.App.Resources;
using Salandual.Core.Listing;
using Salandual.Core.Panels;
using Salandual.Core.Resources;
using Salandual.Core.Settings;
using Salandual.Platform.Windows.FileSystem;

namespace Salandual.App;

public partial class MainWindow
{
    internal MakeFileListDialog? FileListDialog { get; private set; }

    internal async Task MakeFileListAsync(PanelHost host)
    {
        if (FileListDialog is not null)
        {
            FileListDialog.Activate();
            return;
        }

        var panel = host.Panel;
        if (OperationItems(panel) is not var (items, what))
        {
            ShowNotice(AppStrings.FileList_Nothing);
            return;
        }

        var settings = _services.Settings.Current.FileList;
        var format = CurrentFormat();
        var dialog = FileListDialog = new MakeFileListDialog(settings, panel.Path, what, items, panel.ShowHidden, format,
            mine => _services.Settings.Update(s => s with { FileList = s.FileList with { Mine = mine } }),
            () => _services.Settings.Current.Confirmations.RemoveFileListLine, ShortNames.Of);
        MakeFileListChoice? choice;
        try
        {
            choice = await dialog.ShowDialog<MakeFileListChoice?>(this);
        }
        finally
        {
            FileListDialog = null;
            host.View.Focus();
        }

        if (choice is null)
        {
            return;
        }

        _services.Settings.Update(s => s with { FileList = choice.Settings });
        var count = Loc.Plural(choice.Count, AppStrings.Count_Items_One, AppStrings.Count_Items_Other);
        switch (choice.Target)
        {
            case FileListTarget.Clipboard:
                ShowNotice(FileClipboardAccess.SetText(choice.Text) ? Loc.Format(AppStrings.FileList_OnClipboard, count) : AppStrings.Notice_ClipboardBusy);
                break;
            case FileListTarget.Viewer:
                ViewFileList(host, choice.Text);
                break;
            case FileListTarget.File:
                await WriteFileListAsync(choice, count);
                break;
        }
    }

    private static (List<FileListItem> Items, string What)? OperationItems(FilePanel panel)
    {
        var names = panel.GetOperationNames();
        if (panel.Path.Length == 0 || names.Count == 0)
        {
            return null;
        }

        var byName = panel.Items.Where(e => names.Contains(e.Name)).ToDictionary(e => e.Name, StringComparer.Ordinal);
        var items = names.Where(byName.ContainsKey).Select(n => FileListItem.From(panel.Path, byName[n])).ToList();
        if (items.Count == 0)
        {
            return null;
        }

        var selected = panel.GetSelectionSummary().Count > 0
            ? Loc.Plural(items.Count, AppStrings.FileList_Selected_One, AppStrings.FileList_Selected_Other)
            : WindowsPath.GetName(items[0].FullName);
        var folders = items.Count(i => i.IsDirectory);
        var what = folders > 0 && items.Count > 1
            ? Loc.Format(AppStrings.FileList_WhatFolders, selected, panel.Path, Loc.Plural(folders, AppStrings.FileList_Folders_One, AppStrings.FileList_Folders_Other))
            : Loc.Format(AppStrings.FileList_What, selected, panel.Path);
        return (items, what);
    }

    private void ViewFileList(PanelHost host, string text)
    {
        string path;
        try
        {
            path = Path.Combine(_services.Temp.NewFolder("list"), AppStrings.FileList_ViewerName + ".txt");
            File.WriteAllText(path, text, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            ShowNotice(Loc.Format(AppStrings.FileList_CantWrite, _services.Temp.Root, e.Message));
            return;
        }

        var viewer = OpenViewer(host, path, ViewerKind.Text, siblings: [path]);
        LastFileListView = path;
        _unsavedLists.Add(viewer);
        viewer.Closing += (_, e) =>
        {
            if (_unsavedLists.Contains(viewer) && !_closingAll && e.CloseReason != WindowCloseReason.OSShutdown && _services.Settings.Current.Confirmations.CloseUnsavedFileList)
            {
                e.Cancel = true;
                _ = CloseListAskingAsync(viewer, host.Panel.Path);
            }
        };
        viewer.Closed += (_, _) =>
        {
            _unsavedLists.Remove(viewer);
            try
            {
                File.Delete(path);
                Directory.Delete(Path.GetDirectoryName(path)!);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
            }
        };
    }

    private readonly HashSet<ViewerWindow> _unsavedLists = [];

    private bool _closingAll;

    private async Task<bool> CloseListsAskingAsync()
    {
        foreach (var viewer in _unsavedLists.ToList())
        {
            if (!await CloseListAskingAsync(viewer, ActivePanel.Panel.Path))
            {
                return false;
            }
        }

        return true;
    }

    internal ChoiceDialog? ListCloseQuestion { get; private set; }

    internal Func<string?>? ListSavePath { get; set; }

    private async Task<bool> CloseListAskingAsync(ViewerWindow viewer, string folder)
    {
        if (ListCloseQuestion is not null)
        {
            return false;
        }

        viewer.Activate();
        var dialog = ListCloseQuestion = new ChoiceDialog(AppStrings.FileList_CloseTitle, [AppStrings.FileList_CloseText],
            [new("save", AppStrings.FileList_SaveAs, "Enter", Primary: true), new("discard", AppStrings.Editor_DontSave, "N", Avalonia.Input.Key.N), new("cancel", AppStrings.Common_Cancel, "Esc")], cancel: "cancel");
        var answer = await dialog.ShowDialog<string?>(viewer);
        ListCloseQuestion = null;
        if (answer == "save")
        {
            var target = ListSavePath is { } test ? test() : await PickListFileAsync(viewer, folder);
            if (target is null)
            {
                return false;
            }

            try
            {
                File.Copy(viewer.FilePath, target, overwrite: true);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                ShowNotice(Loc.Format(AppStrings.FileList_CantWrite, target, e.Message));
                return false;
            }
        }
        else if (answer != "discard")
        {
            return false;
        }

        _unsavedLists.Remove(viewer);
        viewer.Close();
        return true;
    }

    private async Task<string?> PickListFileAsync(Window owner, string folder)
    {
        var start = Directory.Exists(folder) ? await owner.StorageProvider.TryGetFolderFromPathAsync(folder) : null;
        var file = await owner.StorageProvider.SaveFilePickerAsync(new Avalonia.Platform.Storage.FilePickerSaveOptions
        {
            Title = AppStrings.FileList_ChooseFile,
            SuggestedStartLocation = start,
            SuggestedFileName = _services.Settings.Current.FileList.FileName,
            ShowOverwritePrompt = true,
        });
        return file is null ? null : Avalonia.Platform.Storage.StorageProviderExtensions.TryGetLocalPath(file);
    }

    internal string? LastFileListView { get; private set; }

    private async Task WriteFileListAsync(MakeFileListChoice choice, string count)
    {
        try
        {
            if (choice.Append && File.Exists(choice.FilePath) && new FileInfo(choice.FilePath).Length > 0)
            {
                await File.AppendAllTextAsync(choice.FilePath, choice.Text, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            }
            else
            {
                await File.WriteAllTextAsync(choice.FilePath, choice.Text, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            ShowNotice(Loc.Format(AppStrings.FileList_CantWrite, choice.FilePath, e.Message));
            return;
        }

        ShowNotice(Loc.Format(AppStrings.FileList_InFile, count, WindowsPath.GetName(choice.FilePath)));
    }
}

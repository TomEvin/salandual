// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.App/MainWindow.Pack.cs

using Avalonia.Threading;
using Salandual.App.Dialogs;
using Salandual.App.Panels;
using Salandual.App.Resources;
using Salandual.Archives;
using Salandual.Core.Operations;
using Salandual.Core.Resources;
using Salandual.Platform.Windows.FileSystem;

namespace Salandual.App;

public partial class MainWindow
{
    private static readonly TimeSpan PackMeasureTime = TimeSpan.FromSeconds(1.5);

    internal PackDialog? OpenPackDialog { get; private set; }

    internal PackExistsDialog? OpenPackExists { get; private set; }

    internal OperationJob? LastPackJob { get; private set; }

    internal async Task PackAsync(PanelHost host)
    {
        if (_operationDialog is not null)
        {
            _operationDialog.Activate();
            return;
        }

        var panel = host.Panel;
        var names = ArchiveNames(host);
        if (names.Count == 0 || panel.Results is not null || panel.Path.Length == 0)
        {
            ShowNotice(panel.Results is not null ? AppStrings.Pack_NotInResults : AppStrings.Pack_NothingToPack);
            return;
        }

        var fs = Operations.Files;
        var folder = panel.Path;
        var s = _services.Settings.Current.Operations;
        var other = OtherPanel(host);
        var otherUsable = ArchiveAt(other) is null && other.Panel.Results is null && other.Panel.Path.Length > 0;
        var into = s.PackWhere == PackWhere.OtherPanel && otherUsable ? other.Panel.Path : folder;
        var byName = panel.Items.ToDictionary(e => e.Name, e => e.IsDirectory, StringComparer.OrdinalIgnoreCase);
        var single = names.Count == 1 ? names[0] : null;
        var stem = single is null
            ? WindowsPath.GetName(folder).TrimEnd('\\', ':') is { Length: > 0 } f ? f : AppStrings.Pack_DefaultName
            : byName.GetValueOrDefault(single) ? single : Path.GetFileNameWithoutExtension(single) is { Length: > 0 } n ? n : single;
        var target = WindowsPath.Join(into, stem + ArchiveWriter.Extension(s.FirstPackFormat()));

        long size;
        using (var cts = new CancellationTokenSource(PackMeasureTime))
        {
            try
            {
                size = await Task.Run(() => ArchivePacker.Collect(fs, folder, names, _ => false, (_, _) => { }, cts.Token).Sum(i => i.Size), cts.Token);
            }
            catch (OperationCanceledException)
            {
                size = -1;
            }
        }

        var format = CurrentFormat();
        var what = FileOperations.Count([.. names.Select(n => new OperationSource(n, byName.GetValueOrDefault(n)))]);
        var items = Loc.Plural(names.Count, Strings.Pack_Items_One, Strings.Pack_Items_Other);
        var from = WindowsPath.GetName(folder).TrimEnd('\\') is { Length: > 0 } name ? name : folder;
        var heading = size >= 0 ? Loc.Format(AppStrings.Pack_Heading, items, format.Size(size), from) : Loc.Format(AppStrings.Pack_HeadingUnmeasured, items, from);
        var whereNote = (single is not null && byName.GetValueOrDefault(single), into == folder) switch
        {
            (true, true) => AppStrings.Pack_Where_FolderBeside,
            (true, false) => AppStrings.Pack_Where_FolderOther,
            (false, true) => single is null ? AppStrings.Pack_Where_ParentBeside : AppStrings.Pack_Where_FileBeside,
            _ => single is null ? AppStrings.Pack_Where_ParentOther : AppStrings.Pack_Where_FileOther,
        };

        var dialog = OpenPackDialog = new PackDialog(new PackContext(heading, what + ".", target, whereNote, size, s), fs, format,
            defaults => _services.Settings.Update(x => x with { Operations = defaults }));
        _operationDialog = dialog;
        PackDialogResult? result;
        try
        {
            result = await dialog.ShowDialog<PackDialogResult?>(this);
        }
        finally
        {
            _operationDialog = null;
            host.View.Focus();
        }

        if (result is null)
        {
            return;
        }

        _services.Settings.Update(x => x with { Operations = x.Operations with { PackLastFormat = result.Settings.Format } });
        if (await WhereToPackAsync(result) is not { } request)
        {
            return;
        }

        var job = LastPackJob = ArchivePacker.Pack(fs, request with { Folder = folder, Names = names }, _services.Archives.AskPassword);
        Operations.Add(job, result.ToQueue);
        panel.SelectWhere(_ => true, select: false);
        _ = job.Completion.ContinueWith(_ => Dispatcher.UIThread.Post(() => _ = AfterPackAsync(job, request)), TaskScheduler.Default);
        if (result.ToQueue)
        {
            ShowNotice(Loc.Format(AppStrings.Notice_AddedToQueue, job.Title));
        }
    }

    private async Task<PackRequest?> WhereToPackAsync(PackDialogResult result)
    {
        var fs = Operations.Files;
        var target = result.Target;
        var volumes = result.Settings.VolumeSize > 0;
        var there = fs.Stat(volumes ? target + ".001" : target);
        var request = new PackRequest("", [], target, result.Settings, PackExisting.Replace, result.DeleteAfter);
        if (there is null)
        {
            return request;
        }

        if (there.Value.IsDirectory)
        {
            ShowNotice(Loc.Format(AppStrings.Pack_FolderThere, WindowsPath.GetName(target)), 8);
            return null;
        }

        var canAdd = !volumes && result.Settings.Format is PackFormat.Zip or PackFormat.SevenZip or PackFormat.Tar;
        var rule = _services.Settings.Current.Operations.PackExisting;
        if (rule == PackExisting.Ask || rule == PackExisting.Add && !canAdd)
        {
            var format = CurrentFormat();
            var facts = Loc.Format(AppStrings.PackExists_Facts, Path.GetDirectoryName(target) ?? "", format.Size(there.Value.Size), format.Date(there.Value.LastWriteUtc.ToLocalTime()));
            var dialog = OpenPackExists = new PackExistsDialog(WindowsPath.GetName(target), facts, WindowsPath.GetName(ArchivePacker.NewName(fs, target)), canAdd);
            try
            {
                if (await dialog.ShowDialog<PackExisting?>(this) is not { } chosen)
                {
                    return null;
                }

                rule = chosen;
            }
            finally
            {
                OpenPackExists = null;
            }
        }

        return rule switch
        {
            PackExisting.NewName => request with { Target = ArchivePacker.NewName(fs, target) },
            PackExisting.Add => request with { IfThere = PackExisting.Add },
            _ => request,
        };
    }

    private async Task AfterPackAsync(OperationJob job, PackRequest request)
    {
        foreach (var host in _panels)
        {
            _ = host.Panel.RefreshAsync();
        }

        var name = WindowsPath.GetName(request.Target);
        switch (job.State)
        {
            case JobState.Failed:
                ShowNotice(job.Error?.Message ?? Loc.Format(AppStrings.Notify_Failed, job.Title), 12, (AppStrings.Common_OK, () => { }));
                return;
            case JobState.Done:
                ShowNotice(Loc.Format(request.DeleteAfter ? AppStrings.Pack_DoneDeleted : AppStrings.Pack_Done, name, CurrentFormat().Size(job.Progress.TotalBytes)), 8);
                return;
            case JobState.DoneWithIssues:
                break;
            default:
                return;
        }

        var issues = job.Issues;
        var dialog = new UnpackReportDialog(AppStrings.Pack_Title, Loc.Plural(issues.Count, AppStrings.PackReport_Heading_One, AppStrings.PackReport_Heading_Other, name),
            AppStrings.PackReport_Text, AppStrings.UnpackReport_Why, issues, null, CopyListAsync, AppStrings.PackReport_File);
        OpenUnpackReport = dialog;
        await dialog.ShowDialog(this);
        ActivePanel.View.Focus();

        Task CopyListAsync(string text)
        {
            ShowNotice(FileClipboardAccess.SetText(text) ? AppStrings.UnpackReport_Copied : AppStrings.Notice_ClipboardBusy);
            return Task.CompletedTask;
        }
    }
}

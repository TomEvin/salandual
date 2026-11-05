// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.App/MainWindow.Unpack.cs

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
    private const long DragOutLimit = 64L << 20;

    internal UnpackDialog? OpenUnpackDialog { get; private set; }

    internal UnpackReportDialog? OpenUnpackReport { get; private set; }

    internal UnpackLimitDialog? OpenUnpackLimit { get; private set; }

    internal Func<UnpackSuspicion, bool>? UnpackLimitAnswer { get; set; }

    private List<string> ArchiveNames(PanelHost host)
        => [.. (_namesOverride ?? host.Panel.GetOperationNames()).Where(n => n != "..")];

    internal async Task CopyOutOfArchiveAsync(PanelHost host, bool unpack, bool move = false)
    {
        if (_operationDialog is not null)
        {
            _operationDialog.Activate();
            return;
        }

        if (ArchiveAt(host) is not { } location)
        {
            return;
        }

        var names = ArchiveNames(host);
        if (names.Count == 0)
        {
            ShowNotice(AppStrings.Notice_NothingToCopy);
            return;
        }

        var other = OtherPanel(host);
        if (ArchiveAt(other) is not null)
        {
            ShowNotice(AppStrings.Archive_IntoOtherArchive, 8, (AppStrings.Common_OK, () => { }));
            return;
        }

        var source = new UnpackSource(location.Contents, location.ArchivePath, location.FileOnDisk, location.Inner, names);
        var size = ArchiveUnpacker.Plan(source, new UnpackOptions()).TotalSize;
        var byName = host.Panel.Items.ToDictionary(e => e.Name, e => e.IsDirectory, StringComparer.OrdinalIgnoreCase);
        var what = FileOperations.Count([.. names.Select(n => new OperationSource(n, byName.GetValueOrDefault(n)))]);
        var archive = WindowsPath.GetName(location.ArchivePath);
        var format = CurrentFormat();
        var heading = unpack
            ? Loc.Format(AppStrings.Unpack_HeadingSome, what, archive)
            : Loc.Format(move ? AppStrings.UnpackMove_Heading : AppStrings.UnpackCopy_Heading, what, format.Size(size), archive);
        var request = await UnpackDialogAsync(host, new UnpackContext(!unpack, [source], heading, Loc.Format(AppStrings.Unpack_FactsSome, format.Size(size)), other.Panel.Path, _services.Settings.Current.Operations, move));
        if (request is not null)
        {
            var jobs = await StartUnpackAsync([source], request);
            if (move && jobs.Count == 1)
            {
                TakeOutAfterUnpack(jobs[0], location, names);
            }

            host.Panel.SelectWhere(_ => true, select: false);
        }
    }

    internal async Task UnpackAsync(PanelHost host)
    {
        if (ArchiveAt(host) is not null)
        {
            await CopyOutOfArchiveAsync(host, unpack: true);
            return;
        }

        if (_operationDialog is not null)
        {
            _operationDialog.Activate();
            return;
        }

        var sources = await ArchivesInFolderAsync(host);
        if (sources.Count == 0)
        {
            return;
        }

        var format = CurrentFormat();
        string heading, facts;
        if (sources.Count == 1)
        {
            var c = sources[0].Contents;
            heading = Loc.Format(AppStrings.Unpack_Heading, sources[0].ArchiveName);
            facts = Loc.Format(AppStrings.Unpack_Facts, FormatLabel(c.FormatName), Loc.Plural(c.FileCount, AppStrings.Archive_Files_One, AppStrings.Archive_Files_Other),
                Loc.Plural(c.FolderCount, AppStrings.ArchiveEntry_Folders_One, AppStrings.ArchiveEntry_Folders_Other), format.Size(c.TotalSize));
        }
        else
        {
            heading = Loc.Format(AppStrings.Unpack_HeadingMany, sources.Count);
            facts = Loc.Format(AppStrings.Unpack_FactsSome, format.Size(sources.Sum(s => s.Contents.TotalSize)));
        }

        var request = await UnpackDialogAsync(host, new UnpackContext(false, sources, heading, facts, OtherPanel(host).Panel.Path, _services.Settings.Current.Operations));
        if (request is not null)
        {
            await StartUnpackAsync(sources, request);
            host.Panel.SelectWhere(_ => true, select: false);
        }
    }

    private async Task<List<UnpackSource>> ArchivesInFolderAsync(PanelHost host)
    {
        var panel = host.Panel;
        var files = ArchiveNames(host).Where(n => panel.Items.FirstOrDefault(e => e.Name == n) is { IsDirectory: false }).ToList();
        if (files.Count == 0)
        {
            ShowNotice(AppStrings.Unpack_NothingToUnpack);
            return [];
        }

        var sources = new List<UnpackSource>();
        foreach (var name in files)
        {
            var path = WindowsPath.Join(panel.Path, name);
            try
            {
                var location = await _services.Archives.LocateAsync(path);
                sources.Add(new UnpackSource(location.Contents, path, location.FileOnDisk, "", []));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                ShowNotice(ex.Message);
            }
            catch (OperationCanceledException)
            {
                return [];
            }
        }

        return sources;
    }

    private async Task<UnpackRequest?> UnpackDialogAsync(PanelHost host, UnpackContext context)
    {
        var dialog = OpenUnpackDialog = new UnpackDialog(context, Operations.Files, CurrentFormat(), defaults => _services.Settings.Update(x => x with { Operations = defaults }));
        _operationDialog = dialog;
        try
        {
            return await dialog.ShowDialog<UnpackRequest?>(this);
        }
        finally
        {
            _operationDialog = null;
            host.View.Focus();
        }
    }

    private async Task<List<OperationJob>> StartUnpackAsync(IReadOnlyList<UnpackSource> sources, UnpackRequest request)
    {
        var jobs = new List<OperationJob>();
        var s = _services.Settings.Current.Operations;
        var started = 0;
        foreach (var source in sources)
        {
            var plan = ArchiveUnpacker.Plan(source, request.Options);
            var free = Operations.Files.Describe(request.Target).FreeBytes;
            if (ArchiveUnpacker.Suspicion(plan, source.Contents.FileLength, s.UnpackAskRatio, s.UnpackAskMb, free) is { } suspicion && !await AskSuspiciousAsync(source, suspicion, request.Target, free))
            {
                continue;
            }

            var root = ArchiveUnpacker.FolderFor(plan, request.Options.Folder, source.ArchiveName) is { } sub ? WindowsPath.Join(request.Target, sub) : request.Target;
            var job = ArchiveUnpacker.Unpack(Operations.Files, source, request.Target, request.Options, _services.Archives.AskPassword);
            Operations.Add(job, request.ToQueue);
            jobs.Add(job);
            started++;
            var whole = source.Folder.Length == 0 && source.Names.Count == 0 && string.Equals(source.ArchivePath, source.FileOnDisk, StringComparison.OrdinalIgnoreCase);
            WhenArchiveJobEnds(job, source, root, deleteArchive: request.DeleteArchive && whole);
        }

        if (request.ToQueue && started > 0)
        {
            ShowNotice(Loc.Format(AppStrings.Notice_AddedToQueue, Loc.Format(AppStrings.Unpack_Queued, started)));
        }

        return jobs;
    }

    private async Task<bool> AskSuspiciousAsync(UnpackSource source, UnpackSuspicion suspicion, string target, long? free)
    {
        if (UnpackLimitAnswer is { } answer)
        {
            return answer(suspicion);
        }

        var drive = (WindowsPath.GetRoot(target) ?? target).TrimEnd('\\');
        var dialog = OpenUnpackLimit = new UnpackLimitDialog(source.ArchiveName, suspicion, drive, free, CurrentFormat());
        try
        {
            return await dialog.ShowDialog<bool>(this);
        }
        finally
        {
            OpenUnpackLimit = null;
        }
    }

    internal async Task TestArchivesAsync(PanelHost host)
    {
        var sources = ArchiveAt(host) is { } location
            ? [new UnpackSource(location.Contents, location.ArchivePath, location.FileOnDisk, "", [])]
            : await ArchivesInFolderAsync(host);
        foreach (var source in sources)
        {
            var job = ArchiveUnpacker.Test(Operations.Files, source.Contents, source.ArchiveName, source.FileOnDisk, _services.Archives.AskPassword);
            Operations.Add(job, toQueue: false);
            WhenArchiveJobEnds(job, source, null, deleteArchive: false);
        }
    }

    private void WhenArchiveJobEnds(OperationJob job, UnpackSource source, string? root, bool deleteArchive)
        => job.Completion.ContinueWith(_ => Dispatcher.UIThread.Post(() => _ = AfterArchiveJobAsync(job, source, root, deleteArchive)), TaskScheduler.Default);

    private async Task AfterArchiveJobAsync(OperationJob job, UnpackSource source, string? root, bool deleteArchive)
    {
        var name = source.ArchiveName;
        var format = CurrentFormat();
        switch (job.State)
        {
            case JobState.Failed:
                ShowNotice(job.Error?.Message ?? Loc.Format(AppStrings.Notify_Failed, job.Title), 12, (AppStrings.Common_OK, () => { }));
                return;
            case JobState.Done when job.Kind == OperationKind.Test:
                ShowNotice(Loc.Format(AppStrings.Test_AllRight, name, Loc.Plural(job.Progress.TotalItems, AppStrings.Archive_Files_One, AppStrings.Archive_Files_Other), format.Size(job.Progress.TotalBytes)), 8);
                return;
            case JobState.Done when deleteArchive:
                DeleteUnpackedArchive(source.ArchivePath);
                return;
            case JobState.DoneWithIssues:
                break;
            default:
                return;
        }

        var issues = job.Issues;
        UnpackReportDialog dialog;
        if (job.Kind == OperationKind.Test)
        {
            var total = job.Progress.TotalItems;
            dialog = new UnpackReportDialog(Core.Commands.CommandText.Title("Archive.Test"), Loc.Plural(issues.Count, AppStrings.Test_Damaged_One, AppStrings.Test_Damaged_Other, name),
                Loc.Format(AppStrings.Test_Text, Loc.Plural(total, AppStrings.Archive_Files_One, AppStrings.Archive_Files_Other), format.Size(job.Progress.TotalBytes), Math.Max(0, total - issues.Count)),
                AppStrings.UnpackReport_Problem, issues, null, CopyListAsync);
        }
        else
        {
            var done = job.Progress.DoneItems;
            dialog = new UnpackReportDialog(AppStrings.Unpack_Unpack, Loc.Plural(issues.Count, AppStrings.UnpackReport_Heading_One, AppStrings.UnpackReport_Heading_Other, name),
                Loc.Format(AppStrings.UnpackReport_Text, Loc.Plural(done, AppStrings.Archive_Files_One, AppStrings.Archive_Files_Other), root ?? ""),
                AppStrings.UnpackReport_Why, issues, AppStrings.UnpackReport_Logged, CopyListAsync);
        }

        OpenUnpackReport = dialog;
        await dialog.ShowDialog(this);
        ActivePanel.View.Focus();

        Task CopyListAsync(string text)
        {
            ShowNotice(FileClipboardAccess.SetText(text) ? AppStrings.UnpackReport_Copied : AppStrings.Notice_ClipboardBusy);
            return Task.CompletedTask;
        }
    }

    private void DeleteUnpackedArchive(string path)
    {
        var fs = Operations.Files;
        var failed = false;
        foreach (var part in ArchiveParts(path))
        {
            try
            {
                failed |= fs.Recycle(part) != RecycleResult.Recycled;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                failed = true;
            }
        }

        ShowNotice(Loc.Format(failed ? AppStrings.Unpack_NotRecycled : AppStrings.Unpack_Recycled, WindowsPath.GetName(path)));
        foreach (var host in _panels)
        {
            _ = host.Panel.RefreshAsync();
        }
    }

    internal static List<string> ArchiveParts(string path)
    {
        var folder = Path.GetDirectoryName(path) ?? "";
        var name = Path.GetFileName(path);
        var extension = Path.GetExtension(name).TrimStart('.');
        IEnumerable<string> siblings = [];
        if (extension.Length >= 3 && extension.All(char.IsAsciiDigit))
        {
            var stem = Path.GetFileNameWithoutExtension(name);
            siblings = Directory.EnumerateFiles(folder, stem + ".*").Where(f => Path.GetExtension(f).TrimStart('.') is { Length: >= 3 } e && e.All(char.IsAsciiDigit));
        }
        else if (System.Text.RegularExpressions.Regex.Match(name, @"^(.*)\.part\d+\.rar$", System.Text.RegularExpressions.RegexOptions.IgnoreCase) is { Success: true } m)
        {
            var stem = m.Groups[1].Value;
            siblings = Directory.EnumerateFiles(folder, stem + ".part*.rar")
                .Where(f => System.Text.RegularExpressions.Regex.IsMatch(Path.GetFileName(f), "^" + System.Text.RegularExpressions.Regex.Escape(stem) + @"\.part\d+\.rar$", System.Text.RegularExpressions.RegexOptions.IgnoreCase));
        }

        var parts = siblings.Order(StringComparer.OrdinalIgnoreCase).ToList();
        return parts.Count > 0 ? parts : [path];
    }

    internal async Task CopyArchiveToClipboardAsync(PanelHost host)
    {
        if (ArchiveAt(host) is not { } location)
        {
            return;
        }

        var names = ArchiveNames(host);
        if (names.Count == 0)
        {
            ShowNotice(AppStrings.Notice_NothingToCopy);
            return;
        }

        var folder = _services.Temp.NewFolder("clipboard");
        var source = new UnpackSource(location.Contents, location.ArchivePath, location.FileOnDisk, location.Inner, names);
        var job = ArchiveUnpacker.Unpack(Operations.Files, source, folder, new UnpackOptions { IfExists = ExistingFilePolicy.Overwrite }, _services.Archives.AskPassword);
        Operations.Add(job, toQueue: false);
        ShowNotice(AppStrings.Archive_ClipboardUnpacking);
        await job.Completion;
        if (job.State is not (JobState.Done or JobState.DoneWithIssues))
        {
            return;
        }

        var paths = names.Select(n => Path.Combine(folder, ArchivePaths.SafeFileName(n))).Where(Path.Exists).ToList();
        if (paths.Count == 0)
        {
            return;
        }

        if (!FileClipboardAccess.SetFiles(paths, cut: false))
        {
            ShowNotice(AppStrings.Notice_ClipboardBusy);
            return;
        }

        SetCut([]);
        var what = paths.Count == 1 ? WindowsPath.GetName(paths[0]) : Loc.Plural(paths.Count, AppStrings.Count_Items_One, AppStrings.Count_Items_Other);
        ShowNotice(Loc.Format(AppStrings.Notice_CopiedToClipboard, what));
    }

    private (string Folder, List<string> Names)? UnpackForDrag(ArchiveLocation location, IReadOnlyList<string> names)
    {
        var source = new UnpackSource(location.Contents, location.ArchivePath, location.FileOnDisk, location.Inner, names);
        var plan = ArchiveUnpacker.Plan(source, new UnpackOptions());
        if (plan.TotalSize > DragOutLimit || plan.Entries.Any(e => e.Item.Encrypted))
        {
            ShowNotice(Loc.Format(AppStrings.Archive_DragTooLarge, CurrentFormat().Size(DragOutLimit)), 8);
            return null;
        }

        var folder = _services.Temp.NewFolder("drag");
        var job = ArchiveUnpacker.Unpack(Operations.Files, source, folder, new UnpackOptions { IfExists = ExistingFilePolicy.Overwrite, AskOnErrors = false }, askPassword: null);
        Task.Run(job.RunHere).Wait();
        var shown = names.Where(n => Path.Exists(Path.Combine(folder, ArchivePaths.SafeFileName(n)))).Select(ArchivePaths.SafeFileName).ToList();
        if (job.State == JobState.Failed || shown.Count == 0)
        {
            ShowNotice(job.Error?.Message ?? AppStrings.Archive_DragFailed);
            return null;
        }

        return (folder, shown);
    }
}

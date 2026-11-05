// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.App/MainWindow.ArchiveChanges.cs

using Avalonia.Input;
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
    internal CopyIntoArchiveDialog? OpenCopyIntoArchive { get; private set; }

    internal ChoiceDialog? OpenArchiveDelete { get; private set; }

    internal PutBackDialog? OpenPutBack { get; private set; }

    internal OperationJob? LastArchiveChange { get; private set; }

    private bool CanChange(ArchiveLocation location, bool say = true)
    {
        if (ArchivePacker.WhyNotChangeable(location) is not { } why)
        {
            return true;
        }

        if (say)
        {
            ShowNotice(why, 8, (AppStrings.Common_OK, () => { }));
        }

        return false;
    }

    private ArchiveTarget TargetOf(ArchiveLocation location)
    {
        var archives = _services.Archives;
        archives.KeepPassword(location);
        return new ArchiveTarget(location.FileOnDisk, location.Inner, cancellation =>
        {
            var current = archives.LocateAsync(location.ArchivePath, cancellation).GetAwaiter().GetResult();
            return current.Contents;
        });
    }

    private ArchivePasswordQuestion AskFor(ArchiveLocation location)
        => location.Contents.KnownPasswordFirst(_services.Archives.AskPassword);

    private OperationJob StartChange(ArchiveLocation location, string title, Func<ArchiveContents, ArchiveEdit> edit, string? focus = null)
    {
        var job = LastArchiveChange = ArchivePacker.Change(Operations.Files, TargetOf(location), title, edit, AskFor(location));
        Operations.Add(job, toQueue: false);
        _ = job.Completion.ContinueWith(_ => Dispatcher.UIThread.Post(() => AfterChange(job, focus)), TaskScheduler.Default);
        return job;
    }

    private void AfterChange(OperationJob job, string? focus)
    {
        foreach (var host in _panels)
        {
            if (ArchiveAt(host) is not null)
            {
                _ = ReferenceEquals(host, ActivePanel) && focus is not null ? host.Panel.RefreshAsync(focus) : host.Panel.RefreshAsync();
            }
        }

        if (job.State == JobState.Failed)
        {
            ShowNotice(job.Error?.Message ?? Loc.Format(AppStrings.Notify_Failed, job.Title), 12, (AppStrings.Common_OK, () => { }));
        }
    }

    internal async Task DeleteInArchiveAsync(PanelHost host, ArchiveLocation location)
    {
        var names = ArchiveNames(host);
        if (names.Count == 0 || !CanChange(location))
        {
            return;
        }

        var archive = WindowsPath.GetName(location.ArchivePath);
        var byName = host.Panel.Items.ToDictionary(e => e.Name, e => e.IsDirectory, StringComparer.OrdinalIgnoreCase);
        var what = names.Count == 1 ? names[0] : Loc.Plural(names.Count, Strings.Pack_Items_One, Strings.Pack_Items_Other);
        var shown = string.Join(", ", names.Take(3).Select(n => byName.GetValueOrDefault(n)
            ? Loc.Format(AppStrings.ArchiveDelete_Folder, n, Loc.Plural(location.Contents.FilesBelow(location.Inner.Length == 0 ? n : location.Inner + "\\" + n).Count(), AppStrings.Archive_Files_One, AppStrings.Archive_Files_Other))
            : n)) + (names.Count > 3 ? " …" : "");
        var dialog = OpenArchiveDelete = new ChoiceDialog(Loc.Format(AppStrings.ArchiveDelete_Title, what, archive),
            [Loc.Format(AppStrings.ArchiveDelete_Text, shown, archive), Loc.Format(AppStrings.ArchiveDelete_Replaced, archive)],
            [new("cancel", AppStrings.Common_Cancel, "Esc", Key.Escape), new("delete", AppStrings.ArchiveDelete_Delete, "Enter", Key.Enter, Primary: true)],
            cancel: "cancel", width: 500, group: Core.Settings.ConfirmGroup.Delete);
        string? answer;
        try
        {
            answer = await dialog.ShowDialog<string?>(this);
        }
        finally
        {
            OpenArchiveDelete = null;
            host.View.Focus();
        }

        if (answer != "delete")
        {
            return;
        }

        var inner = location.Inner;
        StartChange(location, Loc.Format(Strings.Job_DeletingIn, what, archive),
            contents => new ArchiveEdit(ArchivePacker.EntriesOf(contents, inner, names), new Dictionary<int, string>(), []));
        host.Panel.SelectWhere(_ => true, select: false);
    }

    internal async Task NewFolderInArchiveAsync(PanelHost host, ArchiveLocation location)
    {
        if (!CanChange(location))
        {
            return;
        }

        var files = new ArchiveFiles(Operations.Files, location);
        var dialog = new NewFolderDialog(files, host.Panel.Path, 0, false);
        _operationDialog = dialog;
        NewFolderRequest? request;
        try
        {
            request = await dialog.ShowDialog<NewFolderRequest?>(this);
        }
        finally
        {
            _operationDialog = null;
            host.View.Focus();
        }

        if (request is null || files.Created.Count == 0)
        {
            return;
        }

        var archive = WindowsPath.GetName(location.ArchivePath);
        var made = files.Created.Select(p => ArchivePaths.Inside(p, location.ArchivePath) ?? "").Where(p => p.Length > 0).ToList();
        var first = WindowsPath.GetName(request.Plan.First);
        StartChange(location, Loc.Format(Strings.Job_NewFolderIn, first, archive),
            _ => ArchiveEdit.Adding([.. made.SelectMany(p => ArchivePacker.NewFolder("", p).Add)]), focus: first);
    }

    private void QuickRenameInArchive(PanelHost host, ArchiveLocation location)
    {
        if (host.Panel.FocusedEntry is null || host.Panel.IsUp(host.Panel.Focus) || !CanChange(location))
        {
            return;
        }

        host.BeginRename(new ArchiveFiles(Operations.Files, location, (from, to) =>
        {
            var inner = location.Inner;
            var job = Dispatcher.UIThread.Invoke(() => StartChange(location, Loc.Format(Strings.Job_RenamingIn, from, WindowsPath.GetName(location.ArchivePath)),
                contents => ArchivePacker.Renaming(contents, inner, from, to), focus: to));
            job.Completion.GetAwaiter().GetResult();
            if (job.State != JobState.Done)
            {
                throw new IOException(job.Error?.Message ?? Loc.Format(AppStrings.Notify_Failed, job.Title));
            }
        }));
    }

    internal async Task CopyIntoArchiveAsync(PanelHost host, ArchiveLocation into, bool move)
    {
        if (_operationDialog is not null)
        {
            _operationDialog.Activate();
            return;
        }

        var names = ArchiveNames(host);
        if (names.Count == 0 || host.Panel.Path.Length == 0 || host.Panel.Results is not null)
        {
            ShowNotice(move ? AppStrings.Notice_NothingToMove : AppStrings.Notice_NothingToCopy);
            return;
        }

        if (!CanChange(into))
        {
            return;
        }

        var fs = Operations.Files;
        var folder = host.Panel.Path;
        var size = await Task.Run(() => ArchivePacker.Collect(fs, folder, names, _ => false, (_, _) => { }, CancellationToken.None).Sum(i => i.Size));
        var format = CurrentFormat();
        var items = Loc.Plural(names.Count, Strings.Pack_Items_One, Strings.Pack_Items_Other);
        var from = WindowsPath.GetName(folder).TrimEnd('\\') is { Length: > 0 } n ? n : folder;
        var dialog = OpenCopyIntoArchive = new CopyIntoArchiveDialog(new CopyIntoContext(move, Loc.Format(move ? AppStrings.CopyInto_HeadingMove : AppStrings.CopyInto_HeadingCopy, items, format.Size(size), from),
            WindowsPath.Join(into.ArchivePath + (into.Inner.Length > 0 ? "\\" + into.Inner : ""), ""), into.Contents, size, _services.Settings.Current.Operations), fs, format,
            defaults => _services.Settings.Update(x => x with { Operations = defaults }));
        _operationDialog = dialog;
        CopyIntoResult? result;
        try
        {
            result = await dialog.ShowDialog<CopyIntoResult?>(this);
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

        StartCopyInto(folder, names, into, result.Level, result.StoreCompressed, result.IfExists, move, result.ToQueue);
        host.Panel.SelectWhere(_ => true, select: false);
    }

    private OperationJob StartCopyInto(string folder, IReadOnlyList<string> names, ArchiveLocation into, PackLevel level, bool storeCompressed, ExistingFilePolicy ifExists, bool move, bool toQueue)
    {
        var target = TargetOf(into);
        var request = new PackRequest(folder, names, into.FileOnDisk, ArchivePacker.SettingsFor(into.Contents, level, storeCompressed) with { Password = null },
            PackExisting.Add, move, target, ifExists);
        var job = LastArchiveChange = ArchivePacker.Pack(Operations.Files, request, AskFor(into));
        Operations.Add(job, toQueue);
        _ = job.Completion.ContinueWith(_ => Dispatcher.UIThread.Post(() =>
        {
            AfterChange(job, names.Count == 1 ? names[0] : null);
            if (job.State == JobState.DoneWithIssues)
            {
                _ = AfterPackAsync(job, request);
            }
        }), TaskScheduler.Default);
        return job;
    }

    private void DropIntoArchive(ArchiveLocation into, IReadOnlyList<string> files, bool move)
    {
        if (!CanChange(into))
        {
            return;
        }

        var level = ArchivePacker.LevelOf(into.Contents);
        foreach (var group in files.GroupBy(f => WindowsPath.GetParent(f) ?? "", StringComparer.OrdinalIgnoreCase))
        {
            StartCopyInto(group.Key, [.. group.Select(WindowsPath.GetName)], into, level, true, _services.Settings.Current.Operations.IfExists, move, toQueue: false);
        }
    }

    private void TakeOutAfterUnpack(OperationJob unpack, ArchiveLocation location, IReadOnlyList<string> names)
    {
        var inner = location.Inner;
        var archive = WindowsPath.GetName(location.ArchivePath);
        _ = unpack.Completion.ContinueWith(_ => Dispatcher.UIThread.Post(() =>
        {
            if (unpack.State == JobState.Done)
            {
                var what = names.Count == 1 ? names[0] : Loc.Plural(names.Count, Strings.Pack_Items_One, Strings.Pack_Items_Other);
                StartChange(location, Loc.Format(Strings.Job_DeletingIn, what, archive),
                    contents => new ArchiveEdit(ArchivePacker.EntriesOf(contents, inner, names), new Dictionary<int, string>(), []));
            }
            else if (unpack.State == JobState.DoneWithIssues)
            {
                ShowNotice(Loc.Format(AppStrings.ArchiveMove_Kept, archive), 10);
            }
        }), TaskScheduler.Default);
    }

    private async Task PutBackAsync(ArchiveLocation location, string copy, string entryName)
    {
        var s = _services.Settings.Current.Operations;
        var archive = WindowsPath.GetName(location.ArchivePath);
        if (s.ArchiveEditSave == ArchiveEditSave.Ask)
        {
            var dialog = OpenPutBack = new PutBackDialog(entryName, archive);
            PutBackAnswer? answer;
            try
            {
                answer = await dialog.ShowDialog<PutBackAnswer?>(this);
            }
            finally
            {
                OpenPutBack = null;
            }

            if (answer is null || !answer.PutBack)
            {
                return;
            }

            if (answer.Always)
            {
                _services.Settings.Update(x => x with { Operations = x.Operations with { ArchiveEditSave = ArchiveEditSave.PutBack } });
            }
        }

        StartCopyInto(Path.GetDirectoryName(copy)!, [WindowsPath.GetName(copy)], location, ArchivePacker.LevelOf(location.Contents), true, ExistingFilePolicy.Overwrite, move: false, toQueue: false);
        ShowNotice(Loc.Format(AppStrings.PutBack_Started, entryName, archive));
    }

    private sealed class ArchiveFiles(IOperationFileSystem local, ArchiveLocation location, Action<string, string>? rename = null) : IOperationFileSystem
    {
        public List<string> Created { get; } = [];

        public string Combine(string directory, string name) => local.Combine(directory, name);

        public string GetName(string path) => local.GetName(path);

        public string? GetParent(string path) => local.GetParent(path);

        public string VolumeOf(string path) => local.VolumeOf(location.FileOnDisk);

        public ItemInfo? Stat(string path)
        {
            if (ArchivePaths.Inside(path, location.ArchivePath) is not { } inner)
            {
                return local.Stat(path);
            }

            if (Created.Contains(path, StringComparer.OrdinalIgnoreCase) || location.Contents.IsFolder(inner))
            {
                return new ItemInfo(WindowsPath.GetName(path), FileAttributes.Directory, 0, default, default);
            }

            return location.Contents.Find(inner) is { } item
                ? new ItemInfo(item.Name, item.Attributes, item.Size, item.ModifiedUtc ?? default, item.ModifiedUtc ?? default)
                : null;
        }

        public VolumeFacts Describe(string path) => local.Describe(location.FileOnDisk);

        public IReadOnlyList<ItemInfo> List(string directory, CancellationToken cancellationToken) => throw new NotSupportedException();

        public void CreateDirectory(string path) => Created.Add(path);

        public void CopyFile(string source, string target, bool overwrite, Action<long> progress) => throw new NotSupportedException();

        public void Move(string source, string target, bool replace)
        {
            if (rename is null)
            {
                throw new NotSupportedException();
            }

            rename(local.GetName(source), local.GetName(target));
        }

        public void DeleteFile(string path) => throw new NotSupportedException();

        public void DeleteDirectory(string path) => throw new NotSupportedException();

        public void SetAttributes(string path, FileAttributes attributes) => throw new NotSupportedException();

        public void SetTimes(string path, DateTime createdUtc, DateTime lastWriteUtc) => throw new NotSupportedException();

        public void RemoveStreams(string path) => throw new NotSupportedException();

        public bool ContentEquals(string a, string b, CancellationToken cancellationToken) => throw new NotSupportedException();

        public bool CanRecycle(string path, out string reason) => throw new NotSupportedException();

        public RecycleResult Recycle(string path) => throw new NotSupportedException();
    }
}

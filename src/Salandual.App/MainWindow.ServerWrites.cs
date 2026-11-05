// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.App/MainWindow.ServerWrites.cs

using Avalonia.Input;
using Avalonia.Threading;
using Salandual.App.Dialogs;
using Salandual.App.Panels;
using Salandual.App.Resources;
using Salandual.Core.Operations;
using Salandual.Core.Resources;
using Salandual.Core.Settings;
using Salandual.Core.Vfs;
using Salandual.Remote;

namespace Salandual.App;

public partial class MainWindow
{
    internal ServerCopyDialog? OpenServerCopy { get; private set; }

    internal ChoiceDialog? OpenServerDelete { get; private set; }

    internal PermissionsDialog? OpenPermissions { get; private set; }

    internal ChoiceDialog? OpenUploadEdited { get; private set; }

    internal OperationJob? LastServerJob { get; private set; }

    private List<(string Name, bool IsDirectory)> ChosenItems(PanelHost host)
    {
        var byName = new Dictionary<string, bool>(StringComparer.Ordinal);
        foreach (var e in host.Panel.Items)
        {
            byName.TryAdd(e.Name, e.IsDirectory);
        }

        return [.. (_namesOverride ?? host.Panel.GetOperationNames()).Where(n => n != "..").Select(n => (n, byName.GetValueOrDefault(n)))];
    }

    private static string What(IReadOnlyList<(string Name, bool IsDirectory)> items) => FileOperations.Count([.. items.Select(i => new OperationSource(i.Name, i.IsDirectory))]);

    private void StartServerJob(OperationJob job, ServerAddress server, bool toQueue = false, string? focus = null, Action? done = null)
    {
        LastServerJob = job;
        Operations.Add(job, toQueue);
        if (toQueue)
        {
            ShowNotice(Loc.Format(AppStrings.Notice_AddedToQueue, job.Title));
        }

        _ = job.Completion.ContinueWith(_ => Dispatcher.UIThread.Post(() =>
        {
            foreach (var host in _panels)
            {
                if (OnServer(host) && ServerAddress.Parse(host.Panel.Path) is { } here && here.SameServer(server))
                {
                    _ = focus is not null && ReferenceEquals(host, ActivePanel) ? host.Panel.RefreshAsync(focus) : host.Panel.RefreshAsync();
                }
            }

            if (job.State == JobState.Failed)
            {
                ShowNotice(job.Error?.Message ?? Loc.Format(AppStrings.Notify_Failed, job.Title), 12, (AppStrings.Server_ShowLog, () => ShowServerLog(server, null)));
            }

            done?.Invoke();
        }), TaskScheduler.Default);
    }

    private ServerCopyDialog NewServerCopyDialog(string heading, string target, bool toServer, bool move)
        => OpenServerCopy = new ServerCopyDialog(heading, target, toServer, move, _services.Settings.Current.Operations,
            defaults => _services.Settings.Update(x => x with { Operations = defaults }),
            on => _services.Settings.Update(x => x with { Operations = x.Operations with { CloseWhenDone = on } }));

    internal async Task CopyFromServerAsync(PanelHost host, bool move)
    {
        if (_operationDialog is not null)
        {
            _operationDialog.Activate();
            return;
        }

        if (ServerAddress.Parse(host.Panel.Path) is not { } folder)
        {
            return;
        }

        var items = ChosenItems(host);
        if (items.Count == 0)
        {
            ShowNotice(AppStrings.Notice_NothingToCopy);
            return;
        }

        var other = OtherPanel(host);
        var otherServer = OnServer(other) ? ServerAddress.Parse(other.Panel.Path) : null;
        if (otherServer is not null && !(move && otherServer.SameServer(folder)))
        {
            ShowNotice(AppStrings.Server_BetweenServers, 8, (AppStrings.Common_OK, () => { }));
            return;
        }

        var within = otherServer is not null;
        var target = within ? otherServer!.ToString()
            : Path.IsPathFullyQualified(other.Panel.Path) && !ShellPlace.IsPlace(other.Panel.Path) && ArchiveAt(other) is null ? other.Panel.Path : "";
        var dialog = NewServerCopyDialog(Loc.Format(move ? AppStrings.Server_MoveHeading : AppStrings.Server_CopyHeading, What(items), folder.Host), target, within, move);
        _operationDialog = dialog;
        ServerCopyChoice? choice;
        try
        {
            choice = await dialog.ShowDialog<ServerCopyChoice?>(this);
        }
        finally
        {
            _operationDialog = null;
            host.View.Focus();
        }

        if (choice is null)
        {
            return;
        }

        if (ServerAddress.Parse(choice.Target) is { } to)
        {
            if (!to.SameServer(folder))
            {
                ShowNotice(AppStrings.Server_BetweenServers, 8, (AppStrings.Common_OK, () => { }));
                return;
            }

            StartServerJob(RemoteChanges.MoveWithin(Servers, folder, items, to, choice.Settings.IfExists), folder, choice.ToQueue);
        }
        else
        {
            StartServerJob(RemoteDownload.Start(Operations.Files, Servers, new DownloadRequest(folder, items, choice.Target, choice.Settings)), folder, choice.ToQueue);
        }

        host.Panel.SelectWhere(_ => true, select: false);
    }

    internal async Task CopyToServerAsync(PanelHost host, bool move)
    {
        if (_operationDialog is not null)
        {
            _operationDialog.Activate();
            return;
        }

        if (ServerAddress.Parse(OtherPanel(host).Panel.Path) is not { } into)
        {
            return;
        }

        var items = ChosenItems(host);
        if (items.Count == 0)
        {
            ShowNotice(AppStrings.Notice_NothingToCopy);
            return;
        }

        var folder = host.Panel.Path;
        var dialog = NewServerCopyDialog(Loc.Format(move ? AppStrings.Server_MoveToHeading : AppStrings.Server_CopyToHeading, What(items), into.Host), into.ToString(), toServer: true, move);
        _operationDialog = dialog;
        ServerCopyChoice? choice;
        try
        {
            choice = await dialog.ShowDialog<ServerCopyChoice?>(this);
        }
        finally
        {
            _operationDialog = null;
            host.View.Focus();
        }

        if (choice is null || ServerAddress.Parse(choice.Target) is not { } target)
        {
            return;
        }

        StartServerJob(RemoteDownload.Upload(Operations.Files, Servers, new UploadRequest(folder, items, target, choice.Settings)), target, choice.ToQueue, focus: items[0].Name);
        host.Panel.SelectWhere(_ => true, select: false);
    }

    internal async Task DeleteOnServerAsync(PanelHost host)
    {
        if (ServerAddress.Parse(host.Panel.Path) is not { } folder)
        {
            return;
        }

        var items = ChosenItems(host);
        if (items.Count == 0)
        {
            return;
        }

        var shown = string.Join(", ", items.Take(3).Select(i => i.IsDirectory ? Loc.Format(AppStrings.Server_DeleteFolder, i.Name) : i.Name)) + (items.Count > 3 ? " …" : "");
        var dialog = OpenServerDelete = new ChoiceDialog(Loc.Format(AppStrings.Server_DeleteTitle, What(items), folder.Host),
            [Loc.Format(AppStrings.Server_DeleteText, shown, folder.Host), AppStrings.Server_NoBin],
            [new("cancel", AppStrings.Common_Cancel, "Esc", Key.Escape), new("delete", AppStrings.Server_Delete, "Enter", Key.Enter, Primary: true)],
            cancel: "cancel", width: 500, group: ConfirmGroup.Delete);
        string? answer;
        try
        {
            answer = await dialog.ShowDialog<string?>(this);
        }
        finally
        {
            host.View.Focus();
        }

        if (answer != "delete")
        {
            return;
        }

        StartServerJob(RemoteChanges.Delete(Servers, folder, items), folder);
        host.Panel.SelectWhere(_ => true, select: false);
    }

    internal async Task NewFolderOnServerAsync(PanelHost host)
    {
        if (ServerAddress.Parse(host.Panel.Path) is not { } folder)
        {
            return;
        }

        var files = new ServerFiles(Operations.Files, host.Panel.Path, host.Panel.Items);
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

        try
        {
            foreach (var made in files.Created)
            {
                await Servers.CreateFolderAsync(ServerFiles.Address(made)!, CancellationToken.None);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ShowNotice(ex.Message, 10, (AppStrings.Server_ShowLog, () => ShowServerLog(folder, null)));
        }

        await host.Panel.RefreshAsync(ServerFiles.Address(files.Created[0])!.Name);
    }

    private void QuickRenameOnServer(PanelHost host)
    {
        if (host.Panel.FocusedEntry is null || host.Panel.IsUp(host.Panel.Focus))
        {
            return;
        }

        host.BeginRename(new ServerFiles(Operations.Files, host.Panel.Path, host.Panel.Items,
            (from, to) => Servers.RenameAsync(from, to, CancellationToken.None).GetAwaiter().GetResult()));
    }

    internal async Task PermissionsOnServerAsync(PanelHost host)
    {
        if (ServerAddress.Parse(host.Panel.Path) is not { } folder)
        {
            return;
        }

        var items = ChosenItems(host);
        if (items.Count == 0)
        {
            return;
        }

        var dialog = OpenPermissions = new PermissionsDialog([.. items.Select(i => i.Name)], [.. items.Select(i => Servers.RightsOf(host.Panel.Path, i.Name) ?? "")],
            items.Any(i => i.IsDirectory), folder.User);
        PermissionsChoice? choice;
        try
        {
            choice = await dialog.ShowDialog<PermissionsChoice?>(this);
        }
        finally
        {
            host.View.Focus();
        }

        if (choice is null)
        {
            return;
        }

        StartServerJob(RemoteChanges.SetPermissions(Servers, folder, items, choice.Mode, choice.Scope), folder, focus: host.Panel.FocusedEntry?.Name);
    }

    private sealed class ServerEdit(ServerAddress file, long size, DateTime time, bool isNew)
    {
        public ServerAddress File => file;

        public long Size { get; set; } = size;

        public DateTime Time { get; set; } = time;

        public bool IsNew { get; set; } = isNew;

        public bool Always { get; set; }
    }

    internal async Task<Editor.EditorWindow?> EditOnServerAsync(PanelHost host)
    {
        if (FocusedOnServer(host, filesOnly: true) is not { } path || ServerAddress.Parse(path) is not { } file || host.Panel.FocusedEntry is not { } entry
            || await FetchAsync(file, entry.Size) is not { } copy)
        {
            return null;
        }

        return await EditServerCopyAsync(host, new ServerEdit(file, entry.Size, entry.LastWriteUtc, isNew: false), copy);
    }

    internal async Task<Editor.EditorWindow?> EditNewOnServerAsync(PanelHost host)
    {
        if (ServerAddress.Parse(host.Panel.Path) is not { } folder)
        {
            return null;
        }

        var dialog = EditNewDialog = new EditNewFileDialog(folder.ToString(), AppStrings.EditNew_Suggested);
        string? name;
        try
        {
            name = await dialog.ShowDialog<string?>(this);
        }
        finally
        {
            EditNewDialog = null;
        }

        if (name is null)
        {
            return null;
        }

        if (RemoteDownload.SafeLocalName(name) is null || name.Contains('/'))
        {
            ShowNotice(Core.Resources.Strings.Server_BadName);
            return null;
        }

        var file = folder.Child(name);
        if (host.Panel.Items.FirstOrDefault(e => e.Name == name) is { } there)
        {
            if (there.IsDirectory)
            {
                ShowNotice(Loc.Format(Core.Resources.Strings.Name_FolderExists, name));
                return null;
            }

            return await FetchAsync(file, there.Size) is { } existing ? await EditServerCopyAsync(host, new ServerEdit(file, there.Size, there.LastWriteUtc, isNew: false), existing) : null;
        }

        var copy = Path.Combine(_services.Temp.NewFolder("server"), name);
        await File.WriteAllBytesAsync(copy, []);
        return await EditServerCopyAsync(host, new ServerEdit(file, 0, default, isNew: true), copy);
    }

    private Task<Editor.EditorWindow?> EditServerCopyAsync(PanelHost host, ServerEdit edit, string copy)
        => OpenEditorAsync(host, copy, internalEditor: true, newFile: edit.IsNew, copyNote: Loc.Format(AppStrings.Server_EditNote, edit.File.Name, edit.File.Host),
            saved: saved => UploadEditedAsync(edit, saved));

    private async Task UploadEditedAsync(ServerEdit edit, string saved)
    {
        var name = edit.File.Name;
        if (!edit.Always)
        {
            var dialog = OpenUploadEdited = new ChoiceDialog(Loc.Format(AppStrings.Server_UploadTitle, name),
                [Loc.Format(AppStrings.Server_UploadText, name, edit.File.Host, edit.File.Parent()?.Path ?? "/")],
                [new("later", AppStrings.Server_UploadNotNow, "Esc", Key.Escape), new("always", AppStrings.Server_UploadAlways), new("upload", AppStrings.Server_Upload, "Enter", Key.Enter, Primary: true)],
                cancel: "later", width: 500);
            var answer = await dialog.ShowDialog<string?>(this);
            if (answer is not ("upload" or "always"))
            {
                ShowNotice(Loc.Format(AppStrings.Server_NotUploaded, name));
                return;
            }

            edit.Always = answer == "always";
        }

        RemoteItem? now;
        try
        {
            now = await Servers.StatAsync(edit.File, CancellationToken.None);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ShowNotice(ex.Message, 10, (AppStrings.Server_ShowLog, () => ShowServerLog(edit.File, null)));
            return;
        }

        var changed = now is not null && (edit.IsNew || now.Entry.Size != edit.Size || now.Entry.LastWriteUtc != edit.Time);
        if (changed)
        {
            var dialog = OpenUploadEdited = new ChoiceDialog(Loc.Format(AppStrings.Server_ChangedTitle, name), [Loc.Format(AppStrings.Server_ChangedText, name)],
                [new("later", AppStrings.Server_UploadNotNow, "Esc", Key.Escape, Primary: true), new("upload", AppStrings.Server_UploadAnyway)],
                cancel: "later", width: 500);
            if (await dialog.ShowDialog<string?>(this) != "upload")
            {
                ShowNotice(Loc.Format(AppStrings.Server_NotUploaded, name));
                return;
            }
        }

        var job = RemoteDownload.Upload(Operations.Files, Servers, new UploadRequest(Path.GetDirectoryName(saved)!, [(Path.GetFileName(saved), false)], edit.File.Parent()!,
            new TransferSettings(ExistingFilePolicy.Overwrite, KeepTimes: false)));
        ShowNotice(Loc.Format(AppStrings.Server_Uploading, name, edit.File.Host));
        StartServerJob(job, edit.File, focus: name, done: async () =>
        {
            if (job.State != JobState.Done)
            {
                return;
            }

            try
            {
                if (await Servers.StatAsync(edit.File, CancellationToken.None) is { } after)
                {
                    (edit.Size, edit.Time, edit.IsNew) = (after.Entry.Size, after.Entry.LastWriteUtc, false);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
            }
        });
    }

    internal async Task OpenFromServerAsync(PanelHost host)
    {
        if (FocusedOnServer(host, filesOnly: true) is { } path && ServerAddress.Parse(path) is { } file && host.Panel.FocusedEntry is { } entry
            && await FetchAsync(file, entry.Size) is { } copy)
        {
            await OpenFileAsync(copy);
        }
    }

    private sealed class ServerFiles(IOperationFileSystem local, string folder, IReadOnlyList<FileEntry> items, Action<ServerAddress, ServerAddress>? rename = null) : IOperationFileSystem
    {
        private readonly HashSet<string> _names = [.. items.Select(e => e.Name)];
        private readonly HashSet<string> _folders = [.. items.Where(e => e.IsDirectory).Select(e => e.Name)];

        public List<string> Created { get; } = [];

        public static ServerAddress? Address(string path) => ServerAddress.Parse(path.Replace('\\', '/'));

        public string Combine(string directory, string name) => Address(directory) is { } a ? a.Child(name).ToString() : local.Combine(directory, name);

        public string GetName(string path) => Address(path)?.Name ?? local.GetName(path);

        public string? GetParent(string path) => Address(path) is { } a ? a.Parent()?.ToString() : local.GetParent(path);

        public string VolumeOf(string path) => Address(path)?.Root ?? local.VolumeOf(path);

        public ItemInfo? Stat(string path)
        {
            if (Address(path) is not { } a)
            {
                return null;
            }

            if (Created.Contains(a.ToString()) || a.Parent()?.ToString() == Address(folder)?.ToString() && _folders.Contains(a.Name))
            {
                return new ItemInfo(a.Name, FileAttributes.Directory, 0, default, default);
            }

            return a.Parent()?.ToString() == Address(folder)?.ToString() && _names.Contains(a.Name) ? new ItemInfo(a.Name, FileAttributes.Normal, 0, default, default) : null;
        }

        public VolumeFacts Describe(string path) => VolumeFacts.Unknown;

        public IReadOnlyList<ItemInfo> List(string directory, CancellationToken cancellationToken) => [];

        public void CreateDirectory(string path) => Created.Add(Address(path)!.ToString());

        public void CopyFile(string source, string target, bool overwrite, Action<long> progress) => throw new NotSupportedException();

        public void Move(string source, string target, bool replace)
        {
            if (rename is null || Address(source) is not { } from || Address(target) is not { } to)
            {
                throw new NotSupportedException();
            }

            rename(from, to);
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

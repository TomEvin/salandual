// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.App/MainWindow.Archives.cs

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Primitives.PopupPositioning;
using Avalonia.Layout;
using Avalonia.Threading;
using Salandual.App.Dialogs;
using Salandual.App.Input;
using Salandual.App.Options;
using Salandual.App.Panels;
using Salandual.App.Resources;
using Salandual.App.Theme;
using Salandual.App.Viewer;
using Salandual.Archives;
using Salandual.Core.Commands;
using Salandual.Core.Resources;
using Salandual.Platform.Windows.FileSystem;

namespace Salandual.App;

public partial class MainWindow
{
    private static readonly HashSet<string> _notInArchives =
    [
        "File.Duplicate", "File.ChangeAttributes", "File.ChangeCase", "File.ViewWith", "File.EditWith", "File.EditNew", "File.OccupiedSpace", "File.MakeList",
        "Clipboard.Cut", "Clipboard.Paste", "Clipboard.PasteIntoFocused", "Clipboard.CopyUncName",
        "Find.Files", "Find.Duplicates", "Compare.Files", "Archive.Pack", "File.NewMenu", "Navigation.ShortcutTarget",
    ];

    private CancellationTokenSource? _archiveCopy;
    private string? _progressName, _progressText;
    private long _progressStart, _progressShownAt;
    private DispatcherTimer? _progressEnd;

    internal Func<string, bool, Task<string?>>? ArchivePasswordQuestion { get; set; }

    internal ArchivePasswordDialog? OpenPasswordDialog { get; private set; }

    internal ArchiveEntryDialog? OpenArchiveEntry { get; private set; }

    internal ContextMenu? ArchiveMenu { get; private set; }

    internal ArchiveLocation? ArchiveAt(PanelHost host) => host.Panel.Path.Length > 0 && host.Panel.Results is null ? _services.Archives.LocateCached(host.Panel.Path) : null;

    private void WireArchives()
    {
        var archives = _services.Archives;
        archives.AskPassword = (name, wrong) => Dispatcher.UIThread.InvokeAsync(() => AskArchivePasswordAsync(name, wrong)).GetAwaiter().GetResult();
        archives.Progress += progress => Dispatcher.UIThread.Post(() => ShowArchiveProgress(progress));
    }

    private Task<string?> AskArchivePasswordAsync(string name, bool wrong)
    {
        if (ArchivePasswordQuestion is { } question)
        {
            return question(name, wrong);
        }

        var dialog = OpenPasswordDialog = new ArchivePasswordDialog(name, wrong);
        return dialog.ShowDialog<string?>(this);
    }

    private void UpdateArchiveState(PanelHost host)
    {
        if (_panels is null)
        {
            return;
        }

        UpdatePlaceState(host);
        UpdateServerState(host);
        UpdateGitState(host);
        var location = ArchiveAt(host);
        if (location is null)
        {
            host.DirectoryLine.Archive = null;
            host.View.PackedSize = null;
            host.InfoExtra = null;
        }
        else
        {
            var contents = location.Contents;
            var format = CurrentFormat();
            host.DirectoryLine.Archive = new ArchiveFacts(location.ArchivePath, ArchivePacker.WhyNotChangeable(location) is null ? FormatLabel(contents.FormatName) : Loc.Format(AppStrings.Archive_ReadOnlyChip, FormatLabel(contents.FormatName)),
                Loc.Format(AppStrings.Archive_Holds, Loc.Plural(contents.FileCount, AppStrings.Archive_Files_One, AppStrings.Archive_Files_Other), format.Size(contents.TotalSize)));
            string Inner(string name) => location.Inner.Length == 0 ? name : location.Inner + "\\" + name;
            host.View.PackedSize = entry => contents.Find(Inner(entry.Name)) is { IsDirectory: false } item ? item.PackedSize : null;
            host.InfoExtra = entry => !entry.IsDirectory && contents.Find(Inner(entry.Name)) is { } item
                ? item.Size > 0
                    ? Loc.Format(AppStrings.Archive_InfoPacked, format.Size(item.PackedSize), ArchiveEntryDialog.Percent(item.PackedSize, item.Size))
                    : Loc.Format(AppStrings.Archive_InfoPackedOnly, format.Size(item.PackedSize))
                : null;
        }

        _services.Archives.Leave([.. _tabs.SelectMany(t => t).Select(t => t.Panel.Path)]);
        FunctionKeys.InvalidateVisual();
    }

    private string SessionPath(PanelHost host)
        => ArchiveAt(host) is { } location && Path.GetDirectoryName(location.FileOnDisk) is { Length: > 0 } folder ? folder
            : Remote.ServerAddress.IsServerPath(host.Panel.Path) ? LocalFolderBefore(host)
            : host.Panel.Path;

    internal static string FormatLabel(string format) => format switch
    {
        "7z" => "7z",
        "Rar" or "Rar5" => "RAR",
        "gzip" => "GZ",
        "bzip2" => "BZ2",
        "zstd" => "ZST",
        _ => format.ToUpperInvariant(),
    };

    private bool GreyedInArchive(string id)
        => _panels is not null && ArchiveAt(ActivePanel) is { } location
           && (_notInArchives.Contains(id) || _changesArchive.Contains(id) && ArchivePacker.WhyNotChangeable(location) is not null);

    private static readonly HashSet<string> _changesArchive = ["File.Move", "File.CreateDirectory", "File.QuickRename", "File.Delete", "File.DeleteInverted"];

    private bool BlockedByArchive(string id, PanelHost host)
    {
        if (_notInArchives.Contains(id) && ArchiveAt(host) is { } inside)
        {
            ShowReadOnlyNotice(inside);
            return true;
        }

        if (id is "File.Copy" or "File.Move" && ArchiveAt(host) is null && ArchiveAt(OtherPanel(host)) is { } into)
        {
            _ = CopyIntoArchiveAsync(host, into, move: id == "File.Move");
            return true;
        }

        return false;
    }

    private void ShowReadOnlyNotice(ArchiveLocation location)
        => ShowNotice(Loc.Format(AppStrings.Archive_ReadOnly, WindowsPath.GetName(location.ArchivePath)), 7, (AppStrings.Common_OK, () => { }));

    private bool RunInArchive(string id, PanelHost host)
    {
        if (ArchiveAt(host) is not { } location)
        {
            return false;
        }

        switch (id)
        {
            case "File.Copy":
                _ = CopyOutOfArchiveAsync(host, unpack: false);
                return true;
            case "File.Move":
                if (CanChange(location))
                {
                    _ = CopyOutOfArchiveAsync(host, unpack: false, move: true);
                }

                return true;
            case "File.Delete" or "File.DeleteInverted":
                _ = DeleteInArchiveAsync(host, location);
                return true;
            case "File.CreateDirectory":
                _ = NewFolderInArchiveAsync(host, location);
                return true;
            case "File.QuickRename":
                QuickRenameInArchive(host, location);
                return true;
            case "Clipboard.Copy":
                _ = CopyArchiveToClipboardAsync(host);
                return true;
            case "File.View" or "File.ViewAlternate":
                _ = ViewInArchiveAsync(host, alternate: id == "File.ViewAlternate");
                return true;
            case "File.Edit":
                _ = EditInArchiveAsync(host);
                return true;
            case "File.Properties" or "File.WindowsProperties":
                _ = ShowArchiveEntryAsync(host, location);
                return true;
            case "File.ContextMenu" or "Panel.DirectoryContextMenu":
                ShowArchiveMenu(host, host.View.NameBounds(host.Panel.Focus) is { } r && id == "File.ContextMenu" ? new Point(r.X + 24, r.Bottom) : new Point(40, 40));
                return true;
            default:
                return false;
        }
    }

    private void OnExecuteRequested(PanelHost host, string path)
    {
        RememberFile(path);
        if (Remote.ServerAddress.IsServerPath(path))
        {
            _ = OpenFromServerAsync(host);
        }
        else if (Core.Vfs.ShellPlace.IsPlace(path))
        {
            ShowNotice(Core.Resources.Strings.Place_RestoreFirst);
        }
        else if (ArchivePaths.IsArchiveName(WindowsPath.GetName(path)))
        {
            _ = host.Panel.NavigateAsync(path);
        }
        else if (ArchiveAt(host) is not null)
        {
            _ = OpenCopyAsync(host, path);
        }
        else if (path.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase))
        {
            _ = OpenShortcutAsync(host, path);
        }
        else
        {
            _ = OpenFileAsync(path);
        }
    }

    private async Task OpenShortcutAsync(PanelHost host, string path)
    {
        var panel = host.Panel;
        string? folder = null;
        try
        {
            folder = await Platform.Windows.Shell.ShellLinks.FolderTargetAsync(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Runtime.InteropServices.COMException)
        {
        }

        if (folder is not null)
        {
            _ = panel.NavigateAsync(folder);
        }
        else
        {
            await OpenFileAsync(path);
        }
    }

    private async Task OpenCopyAsync(PanelHost host, string path)
    {
        if (await CopyOutAsync(path) is { } copy)
        {
            await OpenFileAsync(copy);
        }
    }

    internal async Task<ViewerWindow?> ViewInArchiveAsync(PanelHost host, bool alternate)
    {
        if (FocusedFile(host) is not { } path || await CopyOutAsync(path) is not { } copy)
        {
            return null;
        }

        var kind = _services.Settings.Current.Viewers.KindFor(WindowsPath.GetName(copy), alternate);
        return OpenViewer(host, copy, kind, siblings: [copy]);
    }

    internal async Task<Editor.EditorWindow?> EditInArchiveAsync(PanelHost host)
        => FocusedFile(host) is { } path && ArchiveAt(host) is { } location ? await EditArchiveCopyAsync(host, path, location) : null;

    internal async Task<ViewerWindow?> ViewFoundInArchiveAsync(string path, Salandual.Core.Viewer.FindQuery? text, Window? comeBackTo)
    {
        if (await CopyOutAsync(path) is not { } copy)
        {
            return null;
        }

        var kind = _services.Settings.Current.Viewers.KindFor(WindowsPath.GetName(copy), alternate: false);
        return OpenViewer(ActivePanel, copy, kind, siblings: [copy], comeBackTo: comeBackTo, findText: text);
    }

    internal async Task<Editor.EditorWindow?> EditFoundInArchiveAsync(string path)
    {
        try
        {
            var location = await _services.Archives.LocateAsync(WindowsPath.GetParent(path) ?? path);
            return await EditArchiveCopyAsync(ActivePanel, path, location);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ShowNotice(ex.Message);
            return null;
        }
    }

    private async Task<Editor.EditorWindow?> EditArchiveCopyAsync(PanelHost host, string path, ArchiveLocation location)
    {
        if (await CopyOutAsync(path) is not { } copy)
        {
            return null;
        }

        var name = WindowsPath.GetName(path);
        var archive = WindowsPath.GetName(location.ArchivePath);
        var putBack = _services.Settings.Current.Operations.ArchiveEditSave != Core.Operations.ArchiveEditSave.ReadOnly
            && ArchivePacker.WhyNotChangeable(location) is null && WindowsPath.GetName(copy) == name;
        return putBack
            ? await OpenEditorAsync(host, copy, internalEditor: true, copyNote: Loc.Format(AppStrings.Archive_EditPutBack, name, archive), saved: saved => PutBackAsync(location, saved, name))
            : await OpenEditorAsync(host, copy, internalEditor: true, copyNote: Loc.Format(AppStrings.Archive_EditCopy, archive));
    }

    private string GetArchiveName(PanelHost host) => ArchiveAt(host)?.ArchivePath ?? host.Panel.Path;

    private async Task<string?> CopyOutAsync(string path)
    {
        _archiveCopy?.Cancel();
        var cts = _archiveCopy = new CancellationTokenSource();
        try
        {
            return await _services.Archives.CopyOutAsync(path, _services.Temp.NewFolder("archive"), cancellationToken: cts.Token);
        }
        catch (OperationCanceledException)
        {
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ShowNotice(ex.Message);
            return null;
        }
        finally
        {
            if (ReferenceEquals(_archiveCopy, cts))
            {
                _archiveCopy = null;
            }
        }
    }

    private async Task ShowArchiveEntryAsync(PanelHost host, ArchiveLocation location)
    {
        if (host.Panel.FocusedEntry is not { } entry || host.Panel.IsUp(host.Panel.Focus))
        {
            return;
        }

        var dialog = OpenArchiveEntry = new ArchiveEntryDialog(location, entry.Name, CurrentFormat(), FormatLabel(location.Contents.FormatName));
        await dialog.ShowDialog(this);
        OpenArchiveEntry = null;
        host.View.Focus();
    }

    private void ShowArchiveMenu(PanelHost host, Point at)
    {
        ArchiveMenu?.Close();
        var panel = host.Panel;
        var file = panel.FocusedEntry is { IsDirectory: false } && !panel.IsUp(panel.Focus);
        var menu = ArchiveMenu = new ContextMenu
        {
            PlacementTarget = host.View,
            Placement = PlacementMode.AnchorAndGravity,
            PlacementRect = new Rect(at, new Size(1, 1)),
            PlacementAnchor = PopupAnchor.TopLeft,
            PlacementGravity = PopupGravity.BottomRight,
        };

        void Cmd(string header, string commandId, bool enabled = true)
        {
            var mi = new MenuItem { Header = header, IsEnabled = enabled, Icon = AppIcons.ForPanel(AppIcons.TryGet(commandId, out _) ? commandId : "Icon.Path", ReferenceEquals(host, _panels[1])) };
            if (_services.Commands.FirstChord(commandId) is { } chord && KeyMapping.ToGesture(chord) is { } gesture)
            {
                mi.InputGesture = gesture;
            }

            mi.Click += (_, _) => Run(commandId, PanelTarget.Active);
            menu.Items.Add(mi);
        }

        var any = !panel.IsUp(panel.Focus) || panel.GetSelectionSummary().Count > 0;
        Cmd(AppStrings.QuickMenu_View, "File.View", file);
        Cmd(AppStrings.QuickMenu_Edit, "File.Edit", file);
        menu.Items.Add(new Separator());
        Cmd(AppStrings.QuickMenu_Copy, "Clipboard.Copy", any);
        Cmd(AppStrings.QuickMenu_CopyToOther, "File.Copy", any);
        Cmd(AppStrings.QuickMenu_Unpack, "Archive.Unpack", any);
        Cmd(AppStrings.QuickMenu_TestArchive, "Archive.Test");
        menu.Items.Add(new Separator());
        if (ArchiveAt(host) is { } location && ArchivePacker.WhyNotChangeable(location) is null)
        {
            Cmd(AppStrings.QuickMenu_MoveToOther, "File.Move", any);
            Cmd(AppStrings.QuickMenu_Rename, "File.QuickRename", !panel.IsUp(panel.Focus));
            Cmd(AppStrings.QuickMenu_NewFolder, "File.CreateDirectory");
            Cmd(AppStrings.QuickMenu_Delete, "File.Delete", any);
            menu.Items.Add(new Separator());
        }

        Cmd(AppStrings.QuickMenu_Name, "Clipboard.CopyName", !panel.IsUp(panel.Focus));
        Cmd(AppStrings.QuickMenu_FullName, "Clipboard.CopyFullName", !panel.IsUp(panel.Focus));
        menu.Items.Add(new Separator());
        if (ArchiveAt(host) is { } readOnly && ArchivePacker.WhyNotChangeable(readOnly) is not null)
        {
            var note = new TextBlock { Text = AppStrings.Archive_MenuReadOnly, FontSize = 11, TextWrapping = Avalonia.Media.TextWrapping.Wrap, MaxWidth = 260 }
                .Res(TextBlock.ForegroundProperty, "Brush.TertiaryText");
            menu.Items.Add(new MenuItem { Header = note, IsEnabled = false });
            menu.Items.Add(new Separator());
        }

        Cmd(AppStrings.QuickMenu_Properties, "File.Properties", !panel.IsUp(panel.Focus));
        menu.Open(host.View);
    }

    internal async Task SmokeArchiveAsync()
    {
        if (!Diagnostics.StartupTrace.IsEnabled || Environment.GetEnvironmentVariable("SALANDUAL_SMOKE_ARCHIVE") is not { Length: > 0 } list)
        {
            return;
        }

        var host = _panels[0];
        SetActive(0, focus: true);
        foreach (var archive in list.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            var opened = await host.Panel.NavigateAsync(archive);
            Dispatcher.UIThread.RunJobs();
            var location = ArchiveAt(host);
            var names = string.Join(",", host.Panel.Items.Where((_, i) => !host.Panel.IsUp(i)).Select(e => e.IsDirectory ? e.Name + "\\" : e.Name));
            Diagnostics.StartupTrace.Append($"archive {WindowsPath.GetName(archive)}: opened={opened} format={location?.Contents.FormatName} facts=\"{host.DirectoryLine.Archive?.Format} | {host.DirectoryLine.Archive?.Contents}\" items=[{names}]\n");
            if (host.Panel.Items.FirstOrDefault(e => !e.IsDirectory && e.Name != "..") is { Name.Length: > 0 } file)
            {
                var copy = await CopyOutAsync(WindowsPath.Join(host.Panel.Path, file.Name));
                Diagnostics.StartupTrace.Append($"archive copy {file.Name}: {(copy is null ? "failed" : new FileInfo(copy).Length + " bytes")}\n");
            }

            if (location is not null)
            {
                var test = ArchiveUnpacker.Test(Operations.Files, location.Contents, WindowsPath.GetName(location.ArchivePath), location.FileOnDisk, null);
                await Task.Run(test.RunHere);
                var into = _services.Temp.NewFolder("smoke");
                var unpack = ArchiveUnpacker.Unpack(Operations.Files, new UnpackSource(location.Contents, location.ArchivePath, location.FileOnDisk, location.Inner, []), into,
                    new UnpackOptions { IfExists = Core.Operations.ExistingFilePolicy.Overwrite, AskOnErrors = false }, null);
                await Task.Run(unpack.RunHere);
                Diagnostics.StartupTrace.Append($"archive test {WindowsPath.GetName(archive)}: {test.State} issues={test.Issues.Count}; unpack: {unpack.State} files={Directory.GetFiles(into, "*", SearchOption.AllDirectories).Length} issues={unpack.Issues.Count}\n");
            }

            if (Environment.GetEnvironmentVariable("SALANDUAL_SMOKE_CAPTURE") is { Length: > 0 } folder && TryGetPlatformHandle()?.Handle is { } hwnd)
            {
                await Task.Delay(300);
                Diagnostics.WindowCapture.Save(hwnd, Path.Combine(Directory.CreateDirectory(folder).FullName, "archive-" + WindowsPath.GetName(archive) + ".png"));
            }
        }

        var source = _services.Temp.NewFolder("smoke-pack");
        Directory.CreateDirectory(Path.Combine(source, "Summer"));
        await File.WriteAllTextAsync(Path.Combine(source, "Summer", "notes.txt"), string.Concat(Enumerable.Repeat("Hello from the lake. ", 2000)));
        await File.WriteAllBytesAsync(Path.Combine(source, "Summer", "photo.jpg"), [.. Enumerable.Range(0, 300_000).Select(i => (byte)(i * 7919 >> 3))]);
        var packed = _services.Temp.NewFolder("smoke-packed");
        foreach (var (format, password, volume) in new (Core.Operations.PackFormat, string?, long)[]
        {
            (Core.Operations.PackFormat.Zip, null, 0), (Core.Operations.PackFormat.SevenZip, "smoke", 0), (Core.Operations.PackFormat.Tar, null, 0),
            (Core.Operations.PackFormat.TarGz, null, 0), (Core.Operations.PackFormat.TarXz, null, 0), (Core.Operations.PackFormat.SevenZip, null, 100_000),
        })
        {
            var target = Path.Combine(packed, (volume > 0 ? "parts" : "Summer") + ArchiveWriter.Extension(format));
            var job = ArchivePacker.Pack(Operations.Files, new PackRequest(source, ["Summer"], target, new PackSettings(format, Password: password, HideNames: password is not null, VolumeSize: volume)), (_, _) => password);
            await Task.Run(job.RunHere);
            var first = ArchivePacker.FirstFile(new PackRequest(source, ["Summer"], target, new PackSettings(format, VolumeSize: volume)));
            var items = job.State == Core.Operations.JobState.Done ? ArchiveReader.Open(first, WindowsPath.GetName(target), (_, _) => password).Items.Count : -1;
            Diagnostics.StartupTrace.Append($"pack {WindowsPath.GetName(target)}: {job.State} items={items} parts={Directory.GetFiles(packed, Path.GetFileName(target) + "*").Length} error={job.Error?.Message}\n");
        }

        foreach (var name in new[] { "Summer.zip", "Summer.7z" })
        {
            var archive = Path.Combine(packed, name);
            var target = new ArchiveTarget(archive, "Summer", ct => ArchiveReader.Open(archive, name, (_, _) => "smoke", cancellation: ct));
            var steps = new Func<ArchiveContents, ArchiveEdit>[]
            {
                _ => ArchivePacker.NewFolder("Summer", "Winter"),
                c => ArchivePacker.Renaming(c, "Summer", "notes.txt", "diary.txt"),
                c => new ArchiveEdit(ArchivePacker.EntriesOf(c, "Summer", ["photo.jpg"]), new Dictionary<int, string>(), []),
            };
            var states = new List<string>();
            foreach (var step in steps)
            {
                var job = ArchivePacker.Change(Operations.Files, target, "smoke", step, (_, _) => "smoke");
                await Task.Run(job.RunHere);
                states.Add(job.State + (job.Error is { } e ? "(" + e.Message + ")" : ""));
            }

            var after = ArchiveReader.Open(archive, name, (_, _) => "smoke").Items.Select(i => i.Path).Order(StringComparer.Ordinal);
            Diagnostics.StartupTrace.Append($"change {name}: {string.Join(",", states)} items=[{string.Join(",", after)}]\n");
        }

        var search = new Core.Search.FileSearch(_services.Files, path => File.OpenRead(path), new ArchiveSearch());
        foreach (var criteria in new[] { new Core.Search.SearchCriteria { Named = "*.txt" }, new Core.Search.SearchCriteria { Texts = [new("Hello from the lake")] } })
        {
            var found = new List<string>();
            var log = new List<Core.Search.SearchLogEntry>();
            await search.RunAsync(criteria with { LookIn = [packed], InsideZip = true }, new Core.Search.SearchCallbacks { Found = h => found.Add(h.FullPath[(packed.Length + 1)..]), Log = log.Add });
            Diagnostics.StartupTrace.Append($"find {(criteria.HasTexts ? "contents" : "names")}: [{string.Join(",", found.Order(StringComparer.Ordinal))}] not looked into={log.Count(e => e.Kind == Core.Search.SearchLogKind.Error)}\n");
        }

        var starter = _services.Starter;
        var note = new Core.UserMenu.UserMenuEntry(Path.Combine(source, "Summer", "notes.txt"), false);
        var context = new Core.UserMenu.UserMenuContext { ActivePath = source, OtherPath = packed, Selected = [note], Focused = note, ShortPath = ShortNames.Of };
        var program = Core.UserMenu.UserMenuRunner.Plan(new Core.UserMenu.UserMenuItem { Program = "notepad.exe", Arguments = "\"$(FullName)\" $(ExtPart)" }, context, starter, out var programError);
        var script = Core.UserMenu.UserMenuRunner.Plan(new Core.UserMenu.UserMenuItem { Kind = Core.UserMenu.UserMenuItemKind.Script, Script = "$Files" }, context, starter, out var scriptError);
        var cmd = starter.Find("cmd.exe", out _);
        Diagnostics.StartupTrace.Append($"usermenu program: {program?.Steps[0].Start.Program} args=[{string.Join("|", program?.Steps[0].Start.Arguments ?? [])}] error={programError}\n"
            + $"usermenu script: {Path.GetFileName(script?.Steps[0].Start.Program)} files={script?.Steps[0].ListLines?.Count} error={scriptError}\n"
            + $"usermenu console: cmd={cmd is not null && starter.IsConsoleProgram(cmd)} notepad={program is not null && starter.IsConsoleProgram(program.Steps[0].Start.Program)}\n");

        _services.Settings.Update(s => s with
        {
            UserMenu = s.UserMenu with { Items = [new Core.UserMenu.UserMenuItem { Name = "Notepad", Program = "notepad.exe", Arguments = "\"$(FullName)\"", InBar = true }] },
            HotPaths = s.HotPaths.With(new Core.Settings.HotPath(1, "Summer", source)),
        });
        string Rows() => string.Join(" / ", BarRowStrips.Select(r => string.Join("+", r.Select(s => s == UserMenuBar ? "user" : s == HotPathBar ? "hot" : s == BackForwardBar ? "nav" : "drives"))));
        await Dispatcher.UIThread.InvokeAsync(UpdateLayout, DispatcherPriority.Background);
        var own = Rows();
        UpdateBlocks(b => b.Move(Core.Settings.BarBlock.UserMenu, new Core.Settings.BlockMove(Core.Settings.BlockPlace.WithOther, Core.Settings.BarBlock.HotPaths, Before: false)));
        await Dispatcher.UIThread.InvokeAsync(UpdateLayout, DispatcherPriority.Background);
        var auto = UserMenuBar.Bounds.Width;

        UpdateBlocks(b => b.WithLength(Core.Settings.BarBlock.HotPaths, 300));
        await Dispatcher.UIThread.InvokeAsync(UpdateLayout, DispatcherPriority.Background);
        Diagnostics.StartupTrace.Append($"usermenubar: {own} -> {Rows()} icon={UserMenuBar.Items.FirstOrDefault()?.Image is not null} width={auto:0}/{(UserMenuBar.Parent as Control)?.Bounds.Width:0} grip={UserMenuBar.Grip} end={HotPathBar.EndHandle}/{UserMenuBar.EndHandle} hot={HotPathBar.Bounds.Width:0} user={UserMenuBar.Bounds.Width:0}\n");
        if (Environment.GetEnvironmentVariable("SALANDUAL_SMOKE_CAPTURE") is { Length: > 0 } captures && TryGetPlatformHandle()?.Handle is { } window)
        {
            await Task.Delay(300);
            Diagnostics.WindowCapture.Save(window, Path.Combine(Directory.CreateDirectory(captures).FullName, "usermenubar.png"));
        }
    }

    private void ShowArchiveProgress(ArchiveProgress progress)
    {
        var now = Environment.TickCount64;
        if (_progressName != progress.Name)
        {
            _progressName = progress.Name;
            _progressStart = now;
        }

        _progressEnd ??= new DispatcherTimer(TimeSpan.FromMilliseconds(700), DispatcherPriority.Background, (_, _) => EndArchiveProgress());
        _progressEnd.Stop();
        _progressEnd.Start();
        if (now - _progressStart < 500 || now - _progressShownAt < 200)
        {
            return;
        }

        _progressShownAt = now;
        var format = CurrentFormat();
        var text = progress.Total > 0
            ? Loc.Format(AppStrings.Archive_Reading, progress.Name, format.Size(Math.Min(progress.Done, progress.Total)), format.Size(progress.Total))
            : Loc.Format(AppStrings.Archive_ReadingSoFar, progress.Name, format.Size(progress.Done));
        ShowNotice(text, 60, (AppStrings.Archive_Stop, StopArchiveWork));
        _progressText = text;
        if (progress.Total > 0)
        {
            NoticeActions.Children.Insert(0, new ProgressBar
            {
                Minimum = 0,
                Maximum = 1,
                Value = Math.Clamp((double)progress.Done / progress.Total, 0, 1),
                Width = 120,
                MinWidth = 120,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 8, 0),
            });
        }
    }

    private void EndArchiveProgress()
    {
        _progressEnd?.Stop();
        _progressName = null;
        if (_progressText is not null && NoticeMessage == _progressText)
        {
            Notice.IsVisible = false;
        }

        _progressText = null;
    }

    private void StopArchiveWork()
    {
        foreach (var host in _panels)
        {
            host.Panel.CancelLoading();
        }

        _archiveCopy?.Cancel();
        EndArchiveProgress();
    }
}

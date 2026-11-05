// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.App/MainWindow.QuickOpen.cs

using System.Globalization;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Salandual.App.Controls;
using Salandual.App.Panels;
using Salandual.App.Resources;
using Salandual.Core.Commands;
using Salandual.Core.QuickOpen;
using Salandual.Core.Resources;
using Salandual.Core.Settings;
using Salandual.Core.Vfs;
using Salandual.Platform.Windows.FileSystem;

namespace Salandual.App;

public partial class MainWindow
{
    private const int PerGroup = 5, PerScope = 50;

    private QuickOpenBox? _quick;
    private PanelHost? _quickHost;
    private CancellationTokenSource? _walkCancel;
    private Task<WalkResult>? _walk;
    private string? _walkRoot;
    private int _quickVersion;
    private IReadOnlyList<(string Category, string Where, string Title, string Words)>? _settingsIndex;
    private static HashSet<string>? _menuCommands;

    internal QuickOpenBox? QuickOpen => _quick;

    internal Task<WalkResult>? QuickWalk => _walk;

    private QuickOpenSettings QuickSettings => _services.Settings.Current.QuickOpen;

    internal void OpenQuickOpen()
    {
        if (_quick is null)
        {
            _quick = new QuickOpenBox { Margin = new Avalonia.Thickness(0, 4, 0, 0), ZIndex = 30 };
            Grid.SetColumnSpan(_quick, 3);
            PanelsGrid.Children.Add(_quick);
            _quick.QueryChanged += (_, query) => RefreshQuickOpen(query);
            _quick.Chosen += (_, chosen) => _ = ChooseQuickAsync(chosen.Item, chosen.Action);
            _quick.CloseRequested += (_, _) => CloseQuickOpen();
            _quick.CompleteRequested += (_, _) => CompleteQuickPath();
            _quick.Input.LostFocus += (_, _) => Dispatcher.UIThread.Post(() =>
            {
                var focused = FocusManager?.GetFocusedElement() as Avalonia.Visual;
                if (_quick.IsVisible && (focused is null || !_quick.IsVisualAncestorOf(focused)))
                {
                    CloseQuickOpen(focusPanel: false);
                }
            });
        }

        _quickHost = ActivePanel;
        _settingsIndex = null;
        _quick.FileOpens = QuickSettings.FileEnter == QuickOpenFileEnter.Open;
        _quick.KeyText = _services.Commands.FirstChord("App.CommandSearch")?.ToString() ?? "";
        _quick.Reset();
        _quick.IsVisible = true;
        _quick.Input.Focus();
        StartDriveSearch();
        RefreshQuickOpen(_quick.Query);
    }

    internal void CloseQuickOpen(bool focusPanel = true)
    {
        if (_quick is null || !_quick.IsVisible)
        {
            return;
        }

        _quick.IsVisible = false;
        _walkCancel?.Cancel();
        _walkCancel = null;
        _walk = null;
        _walkRoot = null;
        _settingsIndex = null;
        StopDriveSearch();
        if (focusPanel)
        {
            (_quickHost ?? ActivePanel).View.Focus();
        }
    }

    private void RefreshQuickOpen(QuickOpenQuery query)
    {
        if (_quick is null)
        {
            return;
        }

        var version = ++_quickVersion;
        var now = DateTime.UtcNow;
        UpdateQuickNote();
        if (query.Scope == QuickScope.Path)
        {
            _ = QuickPathAsync(query.Text, version);
            return;
        }

        if (query.Text.Length == 0)
        {
            _quick.Groups = query.Scope switch
            {
                QuickScope.All => BeforeTyping(now),
                QuickScope.Commands => [new(AppStrings.Quick_GroupCommands, [.. QuickCommandItems().Where(c => c.Enabled).OrderByDescending(c => CommandBoost(c, now)).ThenBy(c => c.Name, StringComparer.CurrentCultureIgnoreCase).Take(PerScope)])],
                QuickScope.Tabs => [new(AppStrings.Quick_GroupTabs, [.. QuickTabItems()])],
                QuickScope.Files => [new(AppStrings.Quick_GroupFilesLately, [.. Usage.Latest(_services.Settings.Current.History.QuickFiles).Take(PerScope).Select(e => FileItem(e.Item, When(e.LastUtc, now)))])],
                _ => [new(AppStrings.Quick_GroupSettings, [.. QuickSettingItems().Take(PerScope)])],
            };
            UpdateQuickNote();
            return;
        }

        var groups = new List<(QuickGroup Group, double Best)>();
        void Add(string title, IReadOnlyList<QuickItem> items)
        {
            if (items.Count > 0)
            {
                groups.Add((new QuickGroup(title, items), items[0].Score));
            }
        }

        var max = query.Scope == QuickScope.All ? PerGroup : PerScope;
        if (query.Scope is QuickScope.All or QuickScope.Commands)
        {
            Add(AppStrings.Quick_GroupCommands, QuickOpenSearch.Rank(query.Text, QuickCommandItems(), max, c => CommandBoost(c, now) + (c.Enabled ? 0 : -6)));
        }

        if (query.Scope == QuickScope.All)
        {
            Add(AppStrings.Quick_GroupFolders, QuickOpenSearch.Rank(query.Text, QuickFolderItems(now), max, f => FolderBoost(f, now)));
        }

        if (query.Scope is QuickScope.All or QuickScope.Files)
        {
            var walked = WalkBelow();
            var below = QuickOpenSearch.Rank(query.Text, QuickFileItems(now, walked), query.Scope == QuickScope.Files ? FilesOnly : max, f => FileBoost(f, now));
            Add(_walkRoot is { } root ? Loc.Format(AppStrings.Quick_GroupFilesIn, root) : AppStrings.Quick_GroupFiles, below);
            var shown = below.Select(i => i.Target).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var drives = DriveItems(query, shown, out var found);
            var more = DriveMoreRows(query, drives.Count, found).ToList();
            if (drives.Count > 0 || more.Count > 0)
            {
                var title = _driveTask is { IsCompleted: false } ? Loc.Format(AppStrings.Quick_StillSearching, AppStrings.Quick_GroupFilesAll) : AppStrings.Quick_GroupFilesAll;
                groups.Add((new QuickGroup(title, [.. drives, .. more]), drives.Count > 0 ? drives[0].Score : double.MinValue));
            }
        }

        if (query.Scope is QuickScope.All or QuickScope.Tabs)
        {
            Add(AppStrings.Quick_GroupTabs, QuickOpenSearch.Rank(query.Text, QuickTabItems(), max));
        }

        if (query.Scope is QuickScope.All or QuickScope.Settings)
        {
            Add(AppStrings.Quick_GroupSettings, RankSettings(query.Text, max));
        }

        _quick.Groups = [.. groups.OrderByDescending(g => g.Best).Select(g => g.Group)];
    }

    private List<QuickGroup> BeforeTyping(DateTime now)
    {
        var history = _services.Settings.Current.History;
        var commands = QuickCommandItems().ToDictionary(c => c.Target);
        var most = Usage.Most(history.QuickCommands, now).Select(e => commands.GetValueOrDefault(e.Item)).OfType<QuickItem>().Where(c => c.Enabled).Take(PerGroup).ToList();
        var folders = Usage.Latest(history.QuickFolders).Take(PerGroup).Select(e => FolderItem(e.Item, When(e.LastUtc, now))).ToList();
        if (folders.Count == 0)
        {
            folders = [.. _working.All.Select(w => w.Path).Where(p => !ShellPlace.IsPlace(p)).Distinct(StringComparer.OrdinalIgnoreCase).Take(PerGroup).Select(p => FolderItem(p, ""))];
        }

        var files = Usage.Latest(history.QuickFiles).Take(PerGroup).Select(e => FileItem(e.Item, When(e.LastUtc, now))).ToList();
        return
        [
            new(AppStrings.Quick_GroupMost, most),
            new(AppStrings.Quick_GroupFoldersLately, folders),
            new(AppStrings.Quick_GroupFilesLately, files),
            new(AppStrings.Quick_GroupTabs, [.. QuickTabItems().Take(8)]),
        ];
    }

    private static string When(DateTime utc, DateTime now)
    {
        var day = utc.ToLocalTime().Date;
        var today = now.ToLocalTime().Date;
        return day == today ? AppStrings.Quick_Today
            : day == today.AddDays(-1) ? AppStrings.Quick_Yesterday
            : day.ToString(day.Year == today.Year ? "d MMMM" : "d MMMM yyyy", Loc.TextCulture);
    }

    private IEnumerable<QuickItem> QuickCommandItems()
    {
        var commands = _services.Commands;
        foreach (var c in commands.Commands)
        {
            if (c.Id == "App.CommandSearch")
            {
                continue;
            }

            var built = IsBuilt(c.Id);
            yield return new QuickItem(QuickKind.Command, c.Title, built ? "" : Loc.Format(AppStrings.Quick_ArrivesIn, CommandText.Milestone(c.PlannedFor)), c.Id,
                commands.FirstChord(c.Id)?.ToString() ?? "", built);
        }
    }

    private static QuickItem FolderItem(string path, string key)
    {
        var name = WindowsPath.GetName(path);
        return name.Length == 0
            ? new QuickItem(QuickKind.Folder, path.TrimEnd('\\'), "", path, key)
            : new QuickItem(QuickKind.Folder, name, WindowsPath.GetParent(path) ?? "", path, key);
    }

    private static QuickItem FileItem(string path, string key)
        => new(QuickKind.File, WindowsPath.GetName(path), WindowsPath.GetParent(path) ?? "", path, key);

    private IEnumerable<QuickItem> QuickFolderItems(DateTime now)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var hp in HotPaths.Paths.Where(p => p.Path.Length > 0 && !ShellPlace.IsPlace(p.Path)))
        {
            if (seen.Add(hp.Path))
            {
                var item = FolderItem(hp.Path, Loc.Format(AppStrings.Quick_HotPath, hp.Slot));
                yield return hp.Name.Length > 0 && hp.Name != item.Name ? item with { Name = hp.Name, Detail = hp.Path } : item;
            }
        }

        foreach (var e in _services.Settings.Current.History.QuickFolders)
        {
            if (seen.Add(e.Item))
            {
                yield return FolderItem(e.Item, "");
            }
        }

        foreach (var w in _working.All.Where(w => !ShellPlace.IsPlace(w.Path)))
        {
            if (seen.Add(w.Path))
            {
                yield return FolderItem(w.Path, "");
            }
        }
    }

    private IEnumerable<QuickItem> QuickFileItems(DateTime now, WalkResult? walked)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var e in _services.Settings.Current.History.QuickFiles)
        {
            if (seen.Add(e.Item))
            {
                yield return FileItem(e.Item, "");
            }
        }

        if (walked is null || _walkRoot is not { } root)
        {
            yield break;
        }

        foreach (var entry in walked.Entries)
        {
            var full = WindowsPath.Join(entry.Folder, entry.Name);
            if (seen.Add(full))
            {
                var relative = entry.Folder.Length > root.Length ? entry.Folder[(root.TrimEnd('\\').Length + 1)..] : "";
                yield return new QuickItem(entry.IsFolder ? QuickKind.Folder : QuickKind.File, entry.Name, relative, full);
            }
        }
    }

    private IEnumerable<QuickItem> QuickTabItems()
    {
        for (var side = 0; side < 2; side++)
        {
            var where = side == 0 ? AppStrings.Quick_Left : AppStrings.Quick_Right;
            var tabs = _tabs[side];
            for (var i = 0; i < tabs.Count; i++)
            {
                yield return new QuickItem(QuickKind.Tab, Look(tabs[i]).Title, ShellPlace.Display(tabs[i].Panel.Path), string.Create(CultureInfo.InvariantCulture, $"{side}:{i}"), where);
            }
        }
    }

    private IReadOnlyList<(string Category, string Where, string Title, string Words)> SettingsIndex()
    {
        if (_settingsIndex is null)
        {
            var options = new Options.OptionsWindow(_services);
            var commands = _services.Commands.Commands.Select(c => c.Title).ToHashSet(StringComparer.CurrentCulture);
            _settingsIndex = [.. options.Items
                .Where(i => !string.IsNullOrWhiteSpace(i.Title) && !(i.Category == "keyboard" && commands.Contains(i.Title!)))
                .Select(i => (i.Category, options.CategoryTitle(i.Category) + (i.Section.Length > 0 ? " › " + i.Section : ""), i.Title!, $"{i.Keywords} {i.Description} {i.Section}"))
                .DistinctBy(i => (i.Category, i.Item3))];
        }

        return _settingsIndex;
    }

    private IEnumerable<QuickItem> QuickSettingItems()
        => SettingsIndex().Select((s, i) => new QuickItem(QuickKind.Setting, s.Title, s.Where, i.ToString(CultureInfo.InvariantCulture)));

    private IReadOnlyList<QuickItem> RankSettings(string typed, int max)
    {
        var index = SettingsIndex();
        var byTitle = QuickOpenSearch.Rank(typed, QuickSettingItems(), max);
        var words = typed.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var byWords = QuickSettingItems()
            .Where(s => !byTitle.Any(t => t.Target == s.Target)
                        && words.All(w => index[int.Parse(s.Target, CultureInfo.InvariantCulture)].Words.Contains(w, StringComparison.CurrentCultureIgnoreCase)))
            .Take(Math.Max(0, max - byTitle.Count))
            .Select(s => s with { Score = -20 });
        return [.. byTitle.Concat(byWords)];
    }

    private double CommandBoost(QuickItem item, DateTime now)
        => QuickOpenSearch.Boost(_services.Settings.Current.History.QuickCommands.Find(e => e.Item == item.Target) is { } e ? Usage.Frecency(e, now) : 0);

    private double FolderBoost(QuickItem item, DateTime now)
        => QuickOpenSearch.Boost(_services.Settings.Current.History.QuickFolders.Find(e => string.Equals(e.Item, item.Target, StringComparison.OrdinalIgnoreCase)) is { } e ? Usage.Frecency(e, now) : 0)
           + (item.Key.Length > 0 ? 2 : 0);

    private double FileBoost(QuickItem item, DateTime now)
        => QuickOpenSearch.Boost(_services.Settings.Current.History.QuickFiles.Find(e => string.Equals(e.Item, item.Target, StringComparison.OrdinalIgnoreCase)) is { } e ? Usage.Frecency(e, now) : 0);

    private WalkResult? WalkBelow()
    {
        if (_walk is null)
        {
            var root = (_quickHost ?? ActivePanel).Panel.Path;
            if (!CanWalk(root))
            {
                _belowNote = root.StartsWith(@"\\", StringComparison.Ordinal) || IsNetworkDrive(root) ? AppStrings.Quick_NotOnNetwork : "";
                UpdateQuickNote();
                return null;
            }

            _walkRoot = root;
            _walkCancel = new CancellationTokenSource();
            var cancel = _walkCancel.Token;
            var showHidden = (_quickHost ?? ActivePanel).Panel.ShowHidden;
            var filter = QuickSettings.Filter(forDrives: false);
            _belowNote = AppStrings.Quick_Reading;
            UpdateQuickNote();
            _walk = Task.Run(() => QuickOpenSearch.Walk(root, showHidden, cancel, filter: filter), cancel);
            _walk.ContinueWith(t =>
            {
                if (cancel.IsCancellationRequested || !t.IsCompletedSuccessfully || _quick is not { IsVisible: true } box)
                {
                    return;
                }

                var r = t.Result;
                var count = r.Entries.Count.ToString("N0", CultureInfo.CurrentCulture);
                _belowNote = r.CutShort ? Loc.Format(AppStrings.Quick_ReadCut, count) : Loc.Format(AppStrings.Quick_Read, count);
                RefreshQuickOpen(box.Query);
            }, TaskScheduler.FromCurrentSynchronizationContext());
            return null;
        }

        return _walk.IsCompletedSuccessfully ? _walk.Result : null;
    }

    private static bool CanWalk(string path)
        => path.Length >= 3 && char.IsAsciiLetter(path[0]) && path[1] == ':' && !ShellPlace.IsPlace(path) && !IsNetworkDrive(path) && Directory.Exists(path);

    private static bool IsNetworkDrive(string path)
    {
        try
        {
            return path.Length >= 2 && path[1] == ':' && new DriveInfo(path[..1]).DriveType == DriveType.Network;
        }
        catch (Exception e) when (e is ArgumentException or IOException)
        {
            return false;
        }
    }

    private async Task QuickPathAsync(string text, int version)
    {
        var host = _quickHost ?? ActivePanel;
        var basePath = host.Panel.Path;
        var showHidden = host.Panel.ShowHidden;
        var items = await Task.Run(() =>
        {
            var found = new List<QuickItem>();
            if (PathInput.Resolve(text, basePath) is { } full && Directory.Exists(full))
            {
                found.Add(new QuickItem(QuickKind.Path, full, "", full));
            }

            if (PathInput.ForCompletion(text, basePath) is { } split)
            {
                foreach (var folder in PathInput.Complete(split.Folder, split.Prefix, showHidden, PerScope))
                {
                    if (!found.Any(f => string.Equals(f.Target, folder, StringComparison.OrdinalIgnoreCase)))
                    {
                        var name = WindowsPath.GetName(folder);
                        var at = folder.Length - name.Length;
                        found.Add(new QuickItem(QuickKind.Path, folder, "", folder) { Hits = [.. Enumerable.Range(at, Math.Min(split.Prefix.Length, name.Length))] });
                    }
                }
            }

            return found;
        });
        if (version == _quickVersion && _quick is { IsVisible: true })
        {
            _quick.Groups = [new QuickGroup(AppStrings.Quick_GroupPath, items)];
            _quick.Note = "";
        }
    }

    private void CompleteQuickPath()
    {
        if (_quick?.SelectedItem is { Kind: QuickKind.Path } item)
        {
            _quick.Input.Text = item.Target.TrimEnd('\\') + "\\";
            _quick.Input.CaretIndex = _quick.Input.Text.Length;
        }
    }

    private async Task ChooseQuickAsync(QuickItem item, QuickAction action)
    {
        if (item.Kind == QuickKind.More)
        {
            ChooseQuickMore(item);
            return;
        }

        var host = _quickHost ?? ActivePanel;
        var setting = item.Kind == QuickKind.Setting ? SettingsIndex()[int.Parse(item.Target, CultureInfo.InvariantCulture)] : default;
        CloseQuickOpen();
        var target = action == QuickAction.OtherPanel ? OtherPanel(host) : host;
        switch (item.Kind)
        {
            case QuickKind.Command when action == QuickAction.ChangeKey:
                await OpenOptionsAsync("keyboard", keyCommand: item.Target);
                break;
            case QuickKind.Command:
                SetActive(Array.IndexOf(_panels, host), focus: true);
                Run(item.Target, PanelTarget.Active);
                break;
            case QuickKind.Folder or QuickKind.Path:
                await OpenQuickFolderAsync(target, item.Target, action);
                break;
            case QuickKind.File:
                await OpenQuickFileAsync(target, item.Target, action);
                break;
            case QuickKind.Tab:
                var parts = item.Target.Split(':');
                var side = int.Parse(parts[0], CultureInfo.InvariantCulture);
                var index = int.Parse(parts[1], CultureInfo.InvariantCulture);
                if (index < _tabs[side].Count)
                {
                    SetActive(side, focus: true);
                    ShowTab(side, index);
                }

                break;
            case QuickKind.Setting:
                _ = OpenOptionsAsync(setting.Category);
                _options?.ShowSetting(setting.Category, setting.Title);
                break;
        }
    }

    private async Task OpenQuickFolderAsync(PanelHost host, string path, QuickAction action)
    {
        SetActive(Array.IndexOf(_panels, host), focus: true);
        if (action == QuickAction.NewTab)
        {
            NewTab(host, path);
        }
        else
        {
            await host.Panel.NavigateAsync(path);
        }
    }

    private async Task OpenQuickFileAsync(PanelHost host, string path, QuickAction action)
    {
        if (!File.Exists(path))
        {
            ShowNotice(Loc.Format(AppStrings.Quick_Gone, WindowsPath.GetName(path)));
            return;
        }

        var folder = WindowsPath.GetParent(path) ?? path;
        var name = WindowsPath.GetName(path);
        SetActive(Array.IndexOf(_panels, host), focus: true);
        switch (action)
        {
            case QuickAction.View:
                RememberFile(path);
                OpenViewer(host, path, _services.Settings.Current.Viewers.KindFor(name, alternate: false));
                break;
            case QuickAction.Edit:
                RememberFile(path);
                await OpenEditorAsync(host, path);
                break;
            case QuickAction.NewTab:
                NewTab(host, folder, name);
                break;
            case QuickAction.Open when QuickSettings.FileEnter == QuickOpenFileEnter.Open:
                OnExecuteRequested(host, path);
                break;
            default:
                await host.Panel.NavigateAsync(folder, name);
                break;
        }
    }

    private void RememberFolder(string path)
    {
        if (QuickSettings.Remember && path.Length > 0 && !ShellPlace.IsPlace(path))
        {
            _services.Settings.Update(s => s with { History = s.History with { QuickFolders = Usage.Used(s.History.QuickFolders, path, DateTime.UtcNow, Usage.MaxFolders) } });
        }
    }

    internal void RememberFile(string path)
    {
        if (QuickSettings.Remember && path.Length > 0 && !ShellPlace.IsPlace(path))
        {
            _services.Settings.Update(s => s with { History = s.History with { QuickFiles = Usage.Used(s.History.QuickFiles, path, DateTime.UtcNow, Usage.MaxFiles) } });
        }
    }

    internal void RememberCommand(string id)
    {
        _menuCommands ??= [.. MainMenu.Flatten(MainMenu.Create()).Select(n => n.CommandId).OfType<string>()];
        if (QuickSettings.Remember && _menuCommands.Contains(id) && IsBuilt(id))
        {
            _services.Settings.Update(s => s with { History = s.History with { QuickCommands = Usage.Used(s.History.QuickCommands, id, DateTime.UtcNow, Usage.MaxCommands) } });
        }
    }

    private void WireQuickOpen(PanelHost host)
        => host.PathChanged += (_, path) => RememberFolder(path);
}

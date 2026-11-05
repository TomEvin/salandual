// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.App/MainWindow.QuickOpenDrives.cs

using System.Globalization;
using Avalonia.Threading;
using Salandual.App.Controls;
using Salandual.App.Resources;
using Salandual.Core.QuickOpen;
using Salandual.Core.Resources;
using Salandual.Core.Settings;

namespace Salandual.App;

public partial class MainWindow
{
    private const int DrivesInAll = 8, FilesOnly = 200, IndexAsked = 500;
    private const double DrivesBelow = 5;
    private static readonly TimeSpan WalkKept = TimeSpan.FromMinutes(10);

    private DriveWalk? _driveWalk;
    private DriveWalk? _driveOld;
    private string _driveKey = "";
    private Task<bool>? _driveTask;
    private Task? _driveStart;
    private CancellationTokenSource? _driveCancel;
    private bool _driveCut;
    private IFileNameIndex? _driveIndex;
    private string? _indexText;
    private IReadOnlyList<WalkedEntry> _indexFound = [];
    private CancellationTokenSource? _indexCancel;
    private (string Text, QuickScope Scope, IReadOnlyList<QuickItem> Items, int Found)? _drivesRanked;
    private (string Text, QuickScope Scope)? _drivesRanking;
    private CancellationTokenSource? _rankCancel;
    private string _belowNote = "";
    private bool _drivesOn;

    internal Task? QuickDriveStart => _driveStart;

    internal Task<bool>? QuickDriveWalk => _driveTask;

    internal int? QuickWalkLimit { get; set; }

    internal DriveWalk? QuickDrives => _driveWalk;

    internal IFileNameIndex? QuickIndex => _driveIndex;

    internal Task? QuickIndexAsk { get; private set; }

    internal Task? QuickDrivesRank { get; private set; }

    private void StartDriveSearch()
    {
        _driveIndex = null;
        _indexText = null;
        _indexFound = [];
        _drivesRanked = null;
        _drivesRanking = null;
        _drivesOn = false;
        var settings = QuickSettings;
        if (settings.FilesFrom == QuickOpenFilesFrom.Off)
        {
            return;
        }

        var sources = _services.FileNames;
        var cancel = (_driveCancel = new CancellationTokenSource()).Token;
        var running = _driveTask ?? Task.FromResult(false);
        _driveStart = Task.Run(async () =>
        {
            IFileNameIndex? index = null;
            IReadOnlyList<string> covered = [];
            if (settings.FilesFrom == QuickOpenFilesFrom.Best && sources.Everything is { } everything && everything.IsAvailable())
            {
                index = everything;
            }
            else if (settings.FilesFrom is QuickOpenFilesFrom.Best or QuickOpenFilesFrom.NoEverything && sources.WindowsIndex is { } windows && windows.IsAvailable())
            {
                index = windows;
                covered = windows.CoveredFolders();
            }

            var drives = index?.Kind == FileIndexKind.Everything ? []
                : sources.LocalDrives().Where(d => d.Length >= 2 && !settings.SkipDrives.Contains(d[..2].ToUpperInvariant())).ToList();
            try
            {
                await running.ConfigureAwait(false);
            }
            catch (Exception e) when (e is OperationCanceledException)
            {
            }

            return (index, covered, drives);
        }, cancel).ContinueWith(t =>
        {
            if (cancel.IsCancellationRequested || !t.IsCompletedSuccessfully || _quick is not { IsVisible: true })
            {
                return;
            }

            var (index, covered, drives) = t.Result;
            _driveIndex = index;
            var filter = settings.Filter(forDrives: true);
            var key = string.Join('|', drives) + "#" + string.Join('|', covered) + "#" + string.Join('|', filter.SkipFolders) + $"#{filter.SkipHidden}{filter.SkipSystem}{filter.SkipWindowsFolders}";
            var fresh = _driveWalk is { } kept && key == _driveKey && DateTime.UtcNow - kept.StartedUtc < WalkKept;
            if (drives.Count == 0)
            {
                _driveWalk = _driveOld = null;
            }
            else if (fresh && !_driveWalk!.Finished)
            {
                _driveOld = null;
            }
            else
            {
                _driveOld = fresh ? _driveWalk : null;
                _driveWalk = new DriveWalk(drives, filter, covered);
            }

            _driveKey = key;
            _drivesOn = true;
            if (_driveWalk is not null)
            {
                RunDriveWalk();
            }

            UpdateQuickNote();
            RefreshQuickOpen(_quick.Query);
        }, TaskScheduler.FromCurrentSynchronizationContext());
    }

    private void StopDriveSearch()
    {
        _driveCancel?.Cancel();
        _driveCancel = null;
        _indexCancel?.Cancel();
        _indexCancel = null;
        _rankCancel?.Cancel();
        _rankCancel = null;
        _driveIndex = null;
        _drivesOn = false;
        _belowNote = "";
    }

    private void RunDriveWalk()
    {
        if (_driveWalk is not { } walk || _driveCancel is null)
        {
            return;
        }

        var cancel = _driveCancel.Token;
        var max = QuickWalkLimit ?? QuickSettings.WalkEntries;
        var time = TimeSpan.FromSeconds(QuickSettings.WalkSeconds);
        _driveCut = false;
        _driveTask = Task.Run(() => walk.Run(max, time, cancel, _ => Dispatcher.UIThread.Post(() =>
        {
            if (!cancel.IsCancellationRequested && _quick is { IsVisible: true } box)
            {
                UpdateQuickNote();
                DrivesChanged(box);
            }
        })), cancel);
        _driveTask.ContinueWith(t =>
        {
            if (!t.IsCompletedSuccessfully || cancel.IsCancellationRequested || _quick is not { IsVisible: true } box)
            {
                return;
            }

            _driveCut = t.Result;
            if (walk.Finished)
            {
                _driveOld = null;
            }

            UpdateQuickNote();
            DrivesChanged(box);
        }, TaskScheduler.FromCurrentSynchronizationContext());
    }

    private void DrivesChanged(QuickOpenBox box)
    {
        _drivesRanked = null;
        if (box.Query.Text.Length > 0 && box.Query.Scope is QuickScope.All or QuickScope.Files)
        {
            RefreshQuickOpen(box.Query);
        }
    }

    private void UpdateQuickNote()
    {
        if (_quick is null)
        {
            return;
        }

        var source = _driveIndex?.Kind switch
        {
            FileIndexKind.Everything => AppStrings.Quick_SourceEverything,
            FileIndexKind.WindowsIndex => AppStrings.Quick_SourceIndex,
            _ => "",
        };
        var walk = _drivesOn ? _driveWalk : null;
        var note = walk is null ? source
            : _driveTask is { IsCompleted: false } ? Loc.Format(AppStrings.Quick_Walking, Letters(walk.Unfinished.Take(1)), walk.Count.ToString("N0", CultureInfo.CurrentCulture))
            : _driveCut && !walk.Finished ? Loc.Format(AppStrings.Quick_StoppedAt, Letters(walk.Unfinished))
            : source.Length > 0 ? Loc.Format(AppStrings.Quick_WalkedWith, source, Letters(walk.Roots))
            : Loc.Format(AppStrings.Quick_Walked, Letters(walk.Roots));
        _quick.Note = note.Length > 0 ? note : _belowNote;
    }

    private static string Letters(IEnumerable<string> roots)
        => string.Join(' ', roots.Select(r => r.Length >= 2 && r[1] == ':' ? r[..2].ToUpperInvariant() : r));

    private List<QuickItem> DriveItems(QuickOpenQuery query, ISet<string> shown, out int found)
    {
        found = 0;
        if (!_drivesOn || (_driveIndex is null && _driveWalk is null))
        {
            return [];
        }

        AskIndex(query.Text);
        var max = query.Scope == QuickScope.All ? DrivesInAll : FilesOnly;
        IReadOnlyList<QuickItem> items;
        if (_drivesRanked is { } ranked && ranked.Text == query.Text && ranked.Scope == query.Scope)
        {
            items = ranked.Items;
            found = ranked.Found;
        }
        else
        {
            RankDrives(query);
            items = _drivesRanked is { } old && old.Scope == query.Scope && query.Text.StartsWith(old.Text, StringComparison.CurrentCultureIgnoreCase)
                ? [.. old.Items.Where(i => QuickMatch.Match(query.Text, i.Name) is not null)]
                : [];
        }

        return [.. items.Where(i => !shown.Contains(i.Target)).Take(max)];
    }

    private void AskIndex(string text)
    {
        if (_driveIndex is not { } index || text == _indexText || text.Length == 0)
        {
            return;
        }

        _indexText = text;
        _indexCancel?.Cancel();
        var asking = new CancellationTokenSource();
        _indexCancel = asking;
        var cancel = asking.Token;
        var ask = Task.Run(async () =>
        {
            await Task.Delay(120, cancel).ConfigureAwait(false);
            return index.Find(text, IndexAsked, cancel);
        }, cancel);
        QuickIndexAsk = ask.ContinueWith(t =>
        {
            if (!t.IsCompletedSuccessfully || cancel.IsCancellationRequested || _quick is not { IsVisible: true } box)
            {
                return;
            }

            _indexFound = t.Result;
            if (box.Query.Text == text)
            {
                DrivesChanged(box);
            }
        }, TaskScheduler.FromCurrentSynchronizationContext());
    }

    private void RankDrives(QuickOpenQuery query)
    {
        if (_drivesRanking is { } now && now.Text == query.Text && now.Scope == query.Scope)
        {
            return;
        }

        if (_drivesRanking is not null && _rankCancel is not null)
        {
            _rankCancel.Cancel();
        }

        _drivesRanking = (query.Text, query.Scope);
        var ranking = new CancellationTokenSource();
        _rankCancel = ranking;
        var cancel = ranking.Token;
        var typed = query.Text;
        var max = (query.Scope == QuickScope.All ? DrivesInAll : FilesOnly) + PerGroup;
        var indexed = _indexText == typed ? _indexFound : [];
        var walks = new[] { _driveWalk, _driveOld }.OfType<DriveWalk>().ToList();
        var used = _services.Settings.Current.History.QuickFiles.ToDictionary(e => e.Item, e => QuickOpenSearch.Boost(Usage.Frecency(e, DateTime.UtcNow)), StringComparer.OrdinalIgnoreCase);
        var onlineText = AppStrings.Quick_OnlineOnly;
        var rank = Task.Run(() =>
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var found = new List<QuickItem>();
            var count = 0;
            foreach (var entry in indexed.Concat(walks.SelectMany(w => w.Entries)))
            {
                if ((count & 0x3FFF) == 0 && cancel.IsCancellationRequested)
                {
                    return default;
                }

                if (QuickMatch.Match(typed, entry.Name) is not { } m)
                {
                    continue;
                }

                var full = Path.Join(entry.Folder, entry.Name);
                if (!seen.Add(full))
                {
                    continue;
                }

                count++;
                found.Add(new QuickItem(entry.IsFolder ? QuickKind.Folder : QuickKind.File, entry.Name, entry.Folder, full, entry.OnlineOnly ? onlineText : "")
                {
                    Hits = m.Positions,
                    Score = m.Score - DrivesBelow + used.GetValueOrDefault(full),
                    OnlineOnly = entry.OnlineOnly,
                });
            }

            return (Items: (IReadOnlyList<QuickItem>)[.. found.OrderByDescending(i => i.Score).ThenBy(i => i.Name.Length).ThenBy(i => i.Name, StringComparer.CurrentCultureIgnoreCase).Take(max)], Found: count);
        }, cancel);
        QuickDrivesRank = rank.ContinueWith(t =>
        {
            if (ReferenceEquals(_rankCancel, ranking))
            {
                _drivesRanking = null;
            }

            if (!t.IsCompletedSuccessfully || cancel.IsCancellationRequested || t.Result.Items is null || _quick is not { IsVisible: true } box)
            {
                return;
            }

            _drivesRanked = (typed, query.Scope, t.Result.Items, t.Result.Found);
            if (box.Query.Text == typed && box.Query.Scope == query.Scope)
            {
                RefreshQuickOpen(box.Query);
            }
        }, TaskScheduler.FromCurrentSynchronizationContext());
    }

    private IEnumerable<QuickItem> DriveMoreRows(QuickOpenQuery query, int shownCount, int found)
    {
        if (query.Scope == QuickScope.All && found > shownCount)
        {
            yield return new QuickItem(QuickKind.More, AppStrings.Quick_AllFiles, Loc.Format(AppStrings.Quick_Found, found.ToString("N0", CultureInfo.CurrentCulture)), MoreAll, "/ " + query.Text) { Score = double.MinValue };
        }

        if (_drivesOn && _driveCut && _driveWalk is { Finished: false } walk && _driveTask is { IsCompleted: true })
        {
            yield return new QuickItem(QuickKind.More, AppStrings.Quick_KeepSearching, Loc.Format(AppStrings.Quick_NotFinished, Letters(walk.Unfinished)), MoreKeep) { Score = double.MinValue };
        }
    }

    private const string MoreAll = "all", MoreKeep = "keep";

    private void ChooseQuickMore(QuickItem item)
    {
        if (_quick is null)
        {
            return;
        }

        if (item.Target == MoreAll)
        {
            _quick.ChooseScope(QuickScope.Files);
        }
        else if (item.Target == MoreKeep)
        {
            RunDriveWalk();
            UpdateQuickNote();
            RefreshQuickOpen(_quick.Query);
        }
    }
}

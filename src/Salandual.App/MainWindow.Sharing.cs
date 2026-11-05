// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.App/MainWindow.Sharing.cs

using Salandual.App.Dialogs;
using Salandual.App.Resources;
using Salandual.App.Sharing;
using Salandual.Core.Resources;
using Salandual.Core.Settings;

namespace Salandual.App;

public partial class MainWindow
{
    private bool _sharingStarted;

    internal void StartSharing()
    {
        if (_sharingStarted)
        {
            return;
        }

        _sharingStarted = true;
        var sharing = _services.Sharing;
        EventHandler<SharingTaken> taken = (_, t) => ShowNoticeInTurn(TakenText(t), 10,
            (AppStrings.Common_Show, () => _ = ShowTakenAsync(t)), (AppStrings.Common_Undo, () => sharing.Undo(t)), (AppStrings.Common_OK, () => { }));
        EventHandler<SharingPending> asking = (_, p) => ShowNoticeInTurn(AskingText(p), 30,
            (AppStrings.Share_ShowMore, () => _ = ShowPendingAsync()), (AppStrings.Share_TakeThem, () => sharing.TakePending()), (AppStrings.Share_NotNow, () => { }));
        EventHandler<string> problem = (_, message) => ShowNoticeInTurn(Loc.Format(AppStrings.Share_Problem, _services.Settings.Current.Sharing.Folder, message), 10, (AppStrings.Common_OK, () => { }));
        sharing.Taken += taken;
        sharing.Asking += asking;
        sharing.Problem += problem;
        Activated += OnActivatedForSharing;
        Closed += (_, _) =>
        {
            sharing.Taken -= taken;
            sharing.Asking -= asking;
            sharing.Problem -= problem;
        };
        sharing.Start();
    }

    private void OnActivatedForSharing(object? sender, EventArgs e) => _services.Sharing.CheckSoon();

    internal string TakenText(SharingTaken t)
    {
        var text = Loc.Format(AppStrings.Share_Taken, PcName(t.Pc), Summary(t.Before, t.Merge.Local, t.Merge.Changes, out _));
        var lost = t.Merge.BothChanged - t.Merge.KeptMine;
        return lost > 0 ? text + Loc.Plural(lost, AppStrings.Share_Both_One, AppStrings.Share_Both_Other) : text;
    }

    internal string AskingText(SharingPending p)
    {
        var what = Summary(p.Mine, p.Merge.Local, p.Merge.Changes, out var count);
        return Loc.Plural(count, AppStrings.Share_Asking_One, AppStrings.Share_Asking_Other, PcName(p.Pc), what);
    }

    private static string PcName(string pc) => pc.Length > 0 ? pc : AppStrings.Share_AnotherPc;

    internal string Summary(AppSettings before, AppSettings after, IReadOnlyList<SettingChange> changes, out int count)
    {
        count = 0;
        var dark = (Avalonia.Application.Current as App)?.Theme.WindowsDark ?? false;
        var minute = (DateTime.Now.Hour * 60) + DateTime.Now.Minute;
        var (was, now) = (ThemeCatalog.Find(before.Appearance, before.Appearance.Schedule.ActiveId(dark, minute)), ThemeCatalog.Find(after.Appearance, after.Appearance.Schedule.ActiveId(dark, minute)));
        var parts = new List<string>();
        var rest = changes.AsEnumerable();
        if (was.Id != now.Id)
        {
            parts.Add(Loc.Format(AppStrings.Look_Instead, now.Name, was.Name));
            count++;
            rest = rest.Where(c => !SettingsSharing.IsSchedule(c));
        }

        var hotPaths = after.HotPaths.Paths.Count(p => !before.HotPaths.Paths.Contains(p)) + before.HotPaths.Paths.Count(p => !after.HotPaths.Paths.Any(a => a.Slot == p.Slot));
        if (hotPaths > 0)
        {
            parts.Add(Loc.Plural(hotPaths, AppStrings.Share_HotPaths_One, AppStrings.Share_HotPaths_Other));
            count += hotPaths;
            rest = rest.Where(c => !SettingsSharing.IsHotPathList(c));
        }

        foreach (var group in rest.GroupBy(c => c.Part).OrderBy(g => g.Key))
        {
            parts.Add(Loc.Plural(group.Count(), AppStrings.Share_InPart_One, AppStrings.Share_InPart_Other, SettingsTransferDialog.PartTitle(group.Key)));
            count += group.Count();
        }

        var shown = parts.Take(3).ToList();
        if (parts.Count > 3)
        {
            shown.Add(Loc.Plural(parts.Count - 3, AppStrings.Look_More_One, AppStrings.Look_More_Other));
        }

        return string.Join(", ", shown);
    }

    private async Task ShowTakenAsync(SharingTaken t)
    {
        var lines = t.Merge.Changes.Take(14).Select(c => Loc.Format(AppStrings.Share_Change_Line, c.Name, c.Old, c.New)).ToList();
        if (t.Merge.Changes.Count > 14)
        {
            lines.Add(Loc.Plural(t.Merge.Changes.Count - 14, AppStrings.Look_More_One, AppStrings.Look_More_Other));
        }

        lines.Add("");
        lines.Add(AppStrings.Share_Show_Undo_Note);
        var dialog = new ConfirmDialog(Loc.Format(AppStrings.Share_Show_Title, PcName(t.Pc)), string.Join(Environment.NewLine, lines), AppStrings.Common_Undo, AppStrings.Common_OK, group: Core.Settings.ConfirmGroup.Save, actionIsYes: false);
        if (await dialog.ShowDialog<bool?>(this) == true)
        {
            _services.Sharing.Undo(t);
        }
    }

    private async Task ShowPendingAsync()
    {
        if (_services.Sharing.Pending is not { } pending || _services.Sharing.FilePath is not { } file)
        {
            return;
        }

        var mine = _services.Settings.Current;
        var import = SettingsSharing.AsImport(mine, SettingsSharing.Merge(mine, SettingsSharing.LoadBase(_services.DataDirectory), pending.Copy, mine.Sharing.Parts.ToHashSet(), null), file, pending.Copy);
        if (await SettingsTransferDialog.ForImport(import).ShowDialog<object?>(this) is IReadOnlySet<SettingsPart> parts)
        {
            _services.Sharing.TakePending(import.Apply(parts));
        }
    }
}

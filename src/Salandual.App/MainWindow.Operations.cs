// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.App/MainWindow.Operations.cs

using System.Globalization;
using Avalonia.Controls;
using Avalonia.Threading;
using Salandual.App.Dialogs;
using Salandual.App.Operations;
using Salandual.App.Panels;
using Salandual.App.Resources;
using Salandual.Core.Listing;
using Salandual.Core.Operations;
using Salandual.Core.Resources;
using Salandual.Core.Settings;
using Salandual.Platform.Windows.FileSystem;
using Salandual.Platform.Windows.Shell;

namespace Salandual.App;

public partial class MainWindow
{
    private OperationsService? _operations;
    private OperationsChip? _chip;
    private OperationsPanel? _opsPanel;
    private Window? _conflictDialog;
    private Window? _operationDialog;
    private nint _hwnd;
    private bool _closingConfirmed;

    internal OperationsService Operations
    {
        get
        {
            if (_operations is not null)
            {
                return _operations;
            }

            Dispatcher.UIThread.VerifyAccess();
            var ops = _operations = new OperationsService { Window = () => _hwnd };
            ops.Files.OwnerWindow = () => _hwnd;
            ops.Changed += (_, _) => OnOperationsChanged();
            ops.DialogQuestion += (_, job) => ShowConflictDialog(job);
            ops.Finished += (_, job) => OnJobFinished(job);
            ops.QueueDone += (_, choice) => _ = OnQueueDoneAsync(choice);
            _chip = new OperationsChip();
            Grid.SetColumn(_chip, 1);
            BottomRow.Children.Add(_chip);
            _chip.PointerPressed += (_, e) =>
            {
                OnChipClicked();
                e.Handled = true;
            };
            _opsPanel = new OperationsPanel(ops, CurrentFormat) { ZIndex = 20 };
            Grid.SetColumnSpan(_opsPanel, 3);
            PanelsGrid.Children.Add(_opsPanel);
            return ops;
        }
    }

    internal OperationsChip? Chip => _chip;

    internal OperationsPanel? OpsPanel => _opsPanel;

    internal ConflictDialog? OpenConflictDialog => _conflictDialog as ConflictDialog;

    internal CheckDialog? OpenCheckDialog => _conflictDialog as CheckDialog;

    internal Window? OpenOperationDialog => _operationDialog;

    private DisplayFormat CurrentFormat() => DisplayFormat.From(_services.Settings.Current.Panels, CultureInfo.CurrentCulture);

    private List<OperationSource> OperationSources(PanelHost host)
    {
        var panel = host.Panel;
        var names = _namesOverride ?? panel.GetOperationNames();
        var byName = panel.Items.Where(e => names.Contains(e.Name)).ToDictionary(e => e.Name, StringComparer.Ordinal);
        return [.. names.Select(n => new OperationSource(WindowsPath.Join(panel.Path, n), byName.TryGetValue(n, out var e) && e.IsDirectory))];
    }

    internal async Task CopyOrMoveAsync(PanelHost host, OperationKind kind)
    {
        if (_operationDialog is not null)
        {
            _operationDialog.Activate();
            return;
        }

        var sources = OperationSources(host);
        if (sources.Count == 0 || host.Panel.Path.Length == 0)
        {
            ShowNotice(kind == OperationKind.Copy ? AppStrings.Notice_NothingToCopy : AppStrings.Notice_NothingToMove);
            return;
        }

        var summary = host.Panel.GetSelectionSummary();
        var known = _namesOverride is not null
            ? host.Panel.Items.Where(e => !e.IsDirectory && _namesOverride.Contains(e.Name)).Sum(e => e.Size)
            : summary.Count > 0 ? summary.Bytes : host.Panel.FocusedEntry is { IsDirectory: false } f ? f.Size : 0;
        if (await CopyMoveDialogAsync(host, kind, sources, host.Panel.Path, known, OtherPanel(host).Panel.Path) is not null)
        {
            host.Panel.SelectWhere(_ => true, select: false);
        }
    }

    private async Task<OperationJob?> CopyMoveDialogAsync(PanelHost host, OperationKind kind, List<OperationSource> sources, string sourceFolder, long known, string target, Window? owner = null)
    {
        var s = _services.Settings.Current;
        var context = new CopyMoveContext(kind, sources, sourceFolder, known, target,
            s.HotPaths.Paths, s.History.CopyTargets, s.Operations, host.Panel.ShowHidden, s.Confirmations.CreateTargetFolder);
        var dialog = new CopyMoveDialog(context, Operations.Files, CurrentFormat(),
            defaults => _services.Settings.Update(x => x with { Operations = defaults }));
        _operationDialog = dialog;
        CopyMoveRequest? request;
        try
        {
            request = await dialog.ShowDialog<CopyMoveRequest?>(owner ?? this);
        }
        finally
        {
            _operationDialog = null;
            if (owner is null)
            {
                host.View.Focus();
            }
        }

        if (request is null)
        {
            return null;
        }

        _services.Settings.Update(x => x with
        {
            History = x.History with { CopyTargets = HistorySettings.Push(x.History.CopyTargets, request.TypedTarget) },
            Operations = x.Operations with { MaskInSubfolders = request.MaskInSubfolders },
        });
        var job = kind == OperationKind.Copy
            ? FileOperations.Copy(Operations.Files, sources, request.Target, _services.Settings.Current.Confirmations.Apply(request.Options))
            : FileOperations.Move(Operations.Files, sources, request.Target, _services.Settings.Current.Confirmations.Apply(request.Options));
        job.ExpectedConflicts = request.ExpectedConflicts;
        Operations.Add(job, request.ToQueue);
        if (request.ToQueue)
        {
            ShowNotice(Loc.Format(AppStrings.Notice_AddedToQueue, job.Title));
        }

        return job;
    }

    internal async Task DeleteAsync(PanelHost host, bool inverted)
    {
        if (_operationDialog is not null)
        {
            _operationDialog.Activate();
            return;
        }

        var sources = OperationSources(host);
        if (sources.Count == 0 || host.Panel.Path.Length == 0)
        {
            ShowNotice(AppStrings.Notice_NothingToDelete);
            return;
        }

        var sizes = host.Panel.Items.Where(e => !e.IsDirectory).ToDictionary(e => WindowsPath.Join(host.Panel.Path, e.Name), e => e.Size, StringComparer.OrdinalIgnoreCase);
        OperationJob? job;
        try
        {
            job = await DeleteSourcesAsync(sources, sizes, host.Panel.Path, inverted, this);
        }
        finally
        {
            host.View.Focus();
        }

        if (job is not null)
        {
            host.Panel.SelectWhere(_ => true, select: false);
        }
    }

    private async Task<OperationJob?> DeleteSourcesAsync(List<OperationSource> sources, IReadOnlyDictionary<string, long> sizes, string folder, bool inverted, Window owner)
    {
        var s = _services.Settings.Current.Operations;
        var permanent = inverted ? s.RecycleByDefault : !s.RecycleByDefault;
        var confirm = _services.Settings.Current.Confirmations;
        if (!permanent && !confirm.Delete)
        {
            var quick = FileOperations.Delete(Operations.Files, sources, recycle: true, refusedPermanently: null, confirm.DeleteChecks);
            Operations.Add(quick, toQueue: false);
            return quick;
        }

        var dialog = new DeleteDialog(sources, sizes, folder, permanent, Operations.Files, CurrentFormat(), s);
        _operationDialog = dialog;
        DeleteRequest? request;
        try
        {
            request = await dialog.ShowDialog<DeleteRequest?>(owner);
        }
        finally
        {
            _operationDialog = null;
        }

        if (request is null)
        {
            return null;
        }

        var job = FileOperations.Delete(Operations.Files, sources, request.Recycle, request.RefusedPermanently, _services.Settings.Current.Confirmations.DeleteChecks);
        Operations.Add(job, request.ToQueue);
        if (request.ToQueue)
        {
            ShowNotice(Loc.Format(AppStrings.Notice_AddedToQueue, job.Title));
        }

        return job;
    }

    internal async Task NewFolderAsync(PanelHost host)
    {
        if (_operationDialog is not null)
        {
            _operationDialog.Activate();
            return;
        }

        var panel = host.Panel;
        if (panel.Path.Length == 0)
        {
            return;
        }

        var selected = panel.GetSelectionSummary().Count;
        var sources = selected > 0 ? OperationSources(host) : [];
        var dialog = new NewFolderDialog(Operations.Files, panel.Path, selected, _services.Settings.Current.Operations.MoveIntoNewFolder, _services.Settings.Current.Confirmations.CreateTargetFolder);
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

        if (dialog.MoveBox is { } box && box.IsChecked is { } move && move != _services.Settings.Current.Operations.MoveIntoNewFolder)
        {
            _services.Settings.Update(x => x with { Operations = x.Operations with { MoveIntoNewFolder = move } });
        }

        if (request is null)
        {
            return;
        }

        var first = request.Plan.First;
        if (string.Equals(WindowsPath.GetParent(first), panel.Path, StringComparison.OrdinalIgnoreCase))
        {
            _ = panel.RefreshAsync(WindowsPath.GetName(first), select: []);
        }

        if (request.MoveSelection && sources.Count > 0)
        {
            var job = FileOperations.Move(Operations.Files, sources, new TransferTarget(request.Plan.Deepest), _services.Settings.Current.TransferOptions());
            Operations.Add(job, toQueue: false);
        }
    }

    internal void Duplicate(PanelHost host)
    {
        var sources = OperationSources(host);
        if (sources.Count == 0 || host.Panel.Path.Length == 0)
        {
            ShowNotice(AppStrings.Notice_NothingToDuplicate);
            return;
        }

        var panel = host.Panel;
        var folder = panel.Path;
        var job = FileOperations.Duplicate(Operations.Files, sources, folder, _services.Settings.Current.TransferOptions() with { Filter = null });
        Operations.Add(job, toQueue: false);
        panel.SelectWhere(_ => true, select: false);
        job.Completion.ContinueWith(_ => Dispatcher.UIThread.Post(() =>
        {
            var names = job.Targets.Select(WindowsPath.GetName).ToList();
            if (names.Count > 0 && panel.Path == folder)
            {
                _ = panel.RefreshAsync(names[0], names.Count > 1 ? names : []);
            }
        }), TaskScheduler.Default);
    }

    internal void QuickRename(PanelHost host)
    {
        if (host.Panel.FocusedEntry is null || host.Panel.IsUp(host.Panel.Focus))
        {
            return;
        }

        host.BeginRename(Operations.Files);
    }

    private void OnOperationsChanged()
    {
        var jobs = _operations!.Jobs;
        _chip!.Update(jobs);
        if (_opsPanel!.IsVisible)
        {
            _opsPanel.Refresh();
        }
    }

    private void OnChipClicked()
    {
        if (_chip?.Asking is { Question: ConflictQuestion or CheckQuestion, QuestionsInPanel: true } asking && _chip.Look == "ask")
        {
            _operations!.BringToFront(asking);
            return;
        }

        ToggleOperationsPanel();
    }

    internal void ToggleOperationsPanel()
    {
        _ = Operations;
        _opsPanel!.IsVisible = !_opsPanel.IsVisible;
        if (_opsPanel.IsVisible)
        {
            _opsPanel.Refresh();
        }
        else
        {
            ActivePanel.View.Focus();
        }
    }

    private async void ShowConflictDialog(OperationJob job)
    {
        if (_conflictDialog is not null || job.Question is not { } question)
        {
            return;
        }

        Window dialog;
        if (question is ConflictQuestion conflict)
        {
            dialog = new ConflictDialog(job, conflict, CurrentFormat());
        }
        else if (question is CheckQuestion check)
        {
            dialog = new CheckDialog(job, check);
        }
        else
        {
            return;
        }

        _conflictDialog = dialog;
        ConflictChoice? choice;
        try
        {
            choice = await dialog.ShowDialog<ConflictChoice?>(this);
        }
        finally
        {
            _conflictDialog = null;
        }

        if (!ReferenceEquals(job.Question, question))
        {
            return;
        }

        if (choice?.Answer is { } answer)
        {
            job.Answer(answer);
        }
        else
        {
            _operations!.Minimize(job);
        }

        ActivePanel.View.Focus();

        if (_operations!.Jobs.FirstOrDefault(j => j != job && j.Question is ConflictQuestion or CheckQuestion && !j.QuestionsInPanel) is { } next)
        {
            Dispatcher.UIThread.Post(() => ShowConflictDialog(next));
        }
    }

    private void OnJobFinished(OperationJob job)
    {
        foreach (var host in _panels)
        {
            _ = host.Panel.RefreshAsync();
        }

        NotifyIfAway(job);
        if (job.State == JobState.DoneWithIssues && _opsPanel is { IsVisible: false })
        {
            ShowNotice(Loc.Format(AppStrings.Notice_NotDone, job.Title, job.Issues.Count), (AppStrings.Common_Show, () => ToggleOperationsPanel()));
        }
    }

    internal Func<WhenDone, bool> RunPowerAction { get; set; } = PowerActions.Run;

    internal int CountdownSeconds { get; set; } = 60;

    internal CountdownDialog? OpenCountdown { get; private set; }

    internal List<(string Title, string Text)> SentNotifications { get; } = [];

    private void NotifyIfAway(OperationJob job)
    {
        if (IsActive || job.Elapsed < TimeSpan.FromSeconds(10) || !_services.Settings.Current.Operations.NotifyWhenDone || job.State == JobState.Cancelled)
        {
            return;
        }

        var verb = job.Kind switch { OperationKind.Copy => AppStrings.Notify_Copy, OperationKind.Move => AppStrings.Notify_Move, _ => AppStrings.Notify_Delete };
        var title = job.State switch
        {
            JobState.DoneWithIssues => Loc.Format(AppStrings.Notify_FinishedWithIssues, verb, Ui.Items(job.Issues.Count)),
            JobState.Failed => Loc.Format(AppStrings.Notify_Failed, verb),
            _ => Loc.Format(AppStrings.Notify_Finished, verb),
        };
        var p = job.Progress;
        var parts = new List<string> { job.Subject ?? job.Title };
        if (p.TotalBytes > 0)
        {
            parts.Add(CurrentFormat().Size(p.TotalBytes));
        }

        parts.Add(job.Route);
        parts.Add(Ui.Duration(job.Elapsed));
        var text = string.Join(" · ", parts);
        SentNotifications.Add((title, text));
        if (_hwnd != 0 && TrayNotification.Show(_hwnd, title, text))
        {
            HookNotificationClicks();
        }
    }

    private bool _notifyHooked;

    private void HookNotificationClicks()
    {
        if (_notifyHooked)
        {
            return;
        }

        _notifyHooked = true;
        Win32Properties.AddWndProcHookCallback(this, (nint hwnd, uint msg, nint wParam, nint lParam, ref bool handled) =>
        {
            if (msg != TrayNotification.CallbackMessage)
            {
                return 0;
            }

            handled = true;
            switch ((int)(lParam & 0xFFFF))
            {
                case TrayNotification.BalloonClicked:
                    TrayNotification.Remove(_hwnd);
                    Dispatcher.UIThread.Post(() =>
                    {
                        if (WindowState == WindowState.Minimized)
                        {
                            WindowState = WindowState.Normal;
                        }

                        Activate();
                        if (_opsPanel is { IsVisible: false })
                        {
                            ToggleOperationsPanel();
                        }
                    });
                    break;
                case TrayNotification.BalloonGone or TrayNotification.BalloonHidden:
                    TrayNotification.Remove(_hwnd);
                    break;
                case 0x0201 or 0x0203:
                    TrayNotification.Remove(_hwnd);
                    Dispatcher.UIThread.Post(Activate);
                    break;
            }

            return 0;
        });
    }

    private async Task OnQueueDoneAsync(WhenDone choice)
    {
        _opsPanel?.Refresh();
        if (choice == WhenDone.CloseApp)
        {
            Close();
            return;
        }

        if (choice == WhenDone.Lock)
        {
            RunPowerAction(choice);
            return;
        }

        var (title, text, now) = choice switch
        {
            WhenDone.Sleep => (AppStrings.Countdown_Sleep, AppStrings.Countdown_Sleep_Text, AppStrings.Countdown_Sleep_Now),
            WhenDone.Hibernate => (AppStrings.Countdown_Hibernate, AppStrings.Countdown_Hibernate_Text, AppStrings.Countdown_Hibernate_Now),
            _ => (AppStrings.Countdown_ShutDown, AppStrings.Countdown_ShutDown_Text, AppStrings.Countdown_ShutDown_Now),
        };
        var dialog = OpenCountdown = new CountdownDialog(title, s => Loc.Format(text, s), now, CountdownSeconds);
        bool go;
        try
        {
            go = await dialog.ShowDialog<bool>(this);
        }
        finally
        {
            OpenCountdown = null;
        }

        if (!go)
        {
            ShowNotice(AppStrings.Notice_StaysOn);
            return;
        }

        if (!RunPowerAction(choice))
        {
            ShowNotice(AppStrings.Notice_NotAllowed);
        }
    }

    private async void OnClosingWithJobs(object? sender, WindowClosingEventArgs e)
    {
        if (e.Cancel)
        {
            return;
        }

        var confirm = _services.Settings.Current.Confirmations;
        var running = _operations?.Jobs.Count(j => !j.IsFinished) ?? 0;
        var askJobs = running > 0 && confirm.CloseWithJobs;
        var others = OpenChildWindows();
        if (_closingConfirmed || e.CloseReason == WindowCloseReason.OSShutdown || (!askJobs && !confirm.CloseApp && others.Count == 0))
        {
            _operations?.Dispose();
            return;
        }

        e.Cancel = true;
        var message = running > 0 ? Loc.Plural(running, AppStrings.Close_Running_One, AppStrings.Close_Running_Other) : others.Count > 0 ? "" : AppStrings.Close_Plain;
        if (others.Count > 0)
        {
            var lines = new List<string> { Loc.Plural(others.Count, AppStrings.Close_Windows_One, AppStrings.Close_Windows_Other) };
            lines.AddRange(others.Take(6).Select(t => Loc.Format(AppStrings.Close_WindowLine, t)));
            if (others.Count > 6)
            {
                lines.Add(Loc.Format(AppStrings.Close_WindowsMore, others.Count - 6));
            }

            message = message.Length > 0 ? message + Environment.NewLine + Environment.NewLine + string.Join(Environment.NewLine, lines) : string.Join(Environment.NewLine, lines);
        }

        var dialog = askJobs
            ? new ConfirmDialog(AppStrings.Close_Title, message, AppStrings.Close_CancelAndClose, AppStrings.Close_KeepRunning)
            : new ConfirmDialog(AppStrings.Close_Title, message, AppStrings.Common_Close, AppStrings.Common_Cancel);
        OpenCloseQuestion = dialog;
        bool close;
        try
        {
            close = await dialog.ShowDialog<bool>(this);
        }
        finally
        {
            OpenCloseQuestion = null;
        }

        if (close)
        {
            _closingConfirmed = true;
            _operations?.Dispose();
            Close();
        }
    }

    internal List<string> OpenChildWindows() =>
        ChildWindows()
            .Select(w => string.IsNullOrWhiteSpace(w.Title) ? AppBuild.Name : w.Title.Split(" — ")[0])
            .ToList();

    private List<Window> ChildWindows() =>
        ((Avalonia.Application.Current?.ApplicationLifetime as Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime)?.Windows ?? [])
            .Concat(_viewers).Concat(_editors).Concat(_finds).Concat(OwnedWindows)
            .Distinct()
            .Where(w => w != this && w.IsVisible && w is not ConfirmDialog)
            .ToList();

    private void CloseChildWindows()
    {
        _closingAll = true;
        foreach (var window in ChildWindows())
        {
            if (window is Find.FindWindow find)
            {
                find.CloseWithSalandual();
            }
            else if (window is Compare.FileCompareWindow compare)
            {
                compare.CloseWithSalandual();
            }
            else
            {
                window.Close();
            }
        }
    }

    internal void CloseNow()
    {
        _closingConfirmed = true;
        Close();
    }

    internal ConfirmDialog? OpenCloseQuestion { get; private set; }
}

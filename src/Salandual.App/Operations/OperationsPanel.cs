// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.App/Operations/OperationsPanel.cs

using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Salandual.App.Options;
using Salandual.App.Resources;
using Salandual.Core.Listing;
using Salandual.Core.Operations;
using Salandual.Core.Resources;
using Salandual.Platform.Windows.Shell;

namespace Salandual.App.Operations;

public sealed class OperationsPanel : Border
{
    private readonly OperationsService _service;
    private readonly Func<DisplayFormat> _format;
    private readonly StackPanel _list = new() { Spacing = 0 };
    private readonly Button _pauseQueue;
    private readonly ComboBox _whenDone;
    private readonly List<WhenDone> _whenDoneChoices = [];
    private readonly Dictionary<OperationJob, JobView> _views = [];
    private readonly HashSet<OperationJob> _showIssues = [];
    private string _shape = "";

    public OperationsPanel(OperationsService service, Func<DisplayFormat> format)
    {
        _service = service;
        _format = format;
        Width = 460;
        MaxHeight = 560;
        Padding = new Thickness(14, 10, 14, 12);
        CornerRadius = new CornerRadius(8);
        BorderThickness = new Thickness(1);
        HorizontalAlignment = HorizontalAlignment.Right;
        VerticalAlignment = VerticalAlignment.Bottom;
        Margin = new Thickness(0, 0, 0, 8);
        BoxShadow = BoxShadows.Parse("0 8 32 0 #40000000");
        this.Res(BackgroundProperty, "Brush.PanelBackground").Res(BorderBrushProperty, "Brush.Border");

        var title = OptionsUi.Text(AppStrings.Ops_Title, 14, FontWeight.SemiBold);
        _pauseQueue = Ui.Link(AppStrings.Ops_PauseQueue, () => _service.Queue.SetQueuePaused(!_service.Queue.IsQueuePaused));
        var close = Ui.Link(AppStrings.Common_Close, () => IsVisible = false);
        var head = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };
        var links = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 14, VerticalAlignment = VerticalAlignment.Center, Children = { _pauseQueue, close } };
        DockPanel.SetDock(links, Dock.Right);
        head.Children.Add(links);
        head.Children.Add(title);

        _whenDone = new ComboBox { MinHeight = 26, Padding = new Thickness(8, 2), FontSize = 12, VerticalAlignment = VerticalAlignment.Center };
        Avalonia.Automation.AutomationProperties.SetName(_whenDone, AppStrings.Ops_WhenDone);
        foreach (var (choice, label) in new[] { (WhenDone.Nothing, AppStrings.Ops_WhenDone_Nothing), (WhenDone.CloseApp, AppStrings.Ops_WhenDone_Close), (WhenDone.Lock, AppStrings.Ops_WhenDone_Lock), (WhenDone.Sleep, AppStrings.Ops_WhenDone_Sleep), (WhenDone.Hibernate, AppStrings.Ops_WhenDone_Hibernate), (WhenDone.ShutDown, AppStrings.Ops_WhenDone_ShutDown) })
        {
            if (choice == WhenDone.Hibernate && !PowerActions.CanHibernate)
            {
                continue;
            }

            _whenDoneChoices.Add(choice);
            _whenDone.Items.Add(label);
        }

        _whenDone.SelectedIndex = 0;
        _whenDone.SelectionChanged += (_, _) =>
        {
            if (_whenDone.SelectedIndex >= 0)
            {
                _service.WhenDone = _whenDoneChoices[_whenDone.SelectedIndex];
            }
        };
        var awake = OptionsUi.Text(AppStrings.Ops_Awake, 12, brush: "Brush.TertiaryText", wrap: true);
        awake.VerticalAlignment = VerticalAlignment.Center;
        var footer = new StackPanel { Spacing = 4, Margin = new Thickness(0, 8, 0, 0) };
        var choose = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { OptionsUi.Text(AppStrings.Ops_WhenDone, 12, brush: "Brush.SecondaryText").Also(t => t.VerticalAlignment = VerticalAlignment.Center), _whenDone } };
        footer.Children.Add(choose);
        footer.Children.Add(awake);
        var root = new DockPanel();
        DockPanel.SetDock(head, Dock.Top);
        DockPanel.SetDock(footer, Dock.Bottom);
        root.Children.Add(head);
        root.Children.Add(footer);
        root.Children.Add(new ScrollViewer { Content = _list, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled });
        Child = root;
        IsVisible = false;
        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                IsVisible = false;
                e.Handled = true;
            }
        };
    }

    internal IReadOnlyDictionary<OperationJob, JobView> Views => _views;

    internal ComboBox WhenDoneBox => _whenDone;

    internal Button PauseQueueLink => _pauseQueue;

    public void Refresh()
    {
        var whenDone = _whenDoneChoices.IndexOf(_service.WhenDone);
        if (whenDone >= 0 && _whenDone.SelectedIndex != whenDone)
        {
            _whenDone.SelectedIndex = whenDone;
        }

        _pauseQueue.Content = _service.Queue.IsQueuePaused ? AppStrings.Ops_ResumeQueue : AppStrings.Ops_PauseQueue;
        var jobs = _service.Jobs;
        var queued = jobs.Where(j => j.State == JobState.Queued).ToList();
        var finished = jobs.Where(j => j.IsFinished).ToList();
        var active = jobs.Except(queued).Except(finished).ToList();

        _pauseQueue.IsVisible = active.Count + queued.Count > 0 || _service.Queue.IsQueuePaused;
        var shape = string.Join(",", active.Select(j => "a" + j.Id)) + "|" + string.Join(",", queued.Select(j => "q" + j.Id)) + "|"
            + string.Join(",", finished.Select(j => "f" + j.Id + (_showIssues.Contains(j) ? "+" : ""))) + "|" + _service.Queue.IsQueuePaused;
        if (shape != _shape)
        {
            _shape = shape;
            Rebuild(active, queued, finished);
        }

        foreach (var view in _views.Values)
        {
            view.Update(_format());
        }
    }

    private void Rebuild(List<OperationJob> active, List<OperationJob> queued, List<OperationJob> finished)
    {
        _list.Children.Clear();
        foreach (var gone in _views.Keys.Except(active).ToList())
        {
            _views.Remove(gone);
        }

        if (active.Count == 0)
        {
            _list.Children.Add(OptionsUi.Text(AppStrings.Ops_NothingRunning, 12, brush: "Brush.TertiaryText"));
        }

        foreach (var job in active)
        {
            if (!_views.TryGetValue(job, out var view))
            {
                view = new JobView(job, _service, expanded: _views.Count == 0);
                _views[job] = view;
            }

            Add(view);
        }

        _list.Children.Add(Section(queued.Count == 0 ? AppStrings.Ops_Queue : Loc.Format(_service.Queue.IsQueuePaused ? AppStrings.Ops_QueueWaitingPaused : AppStrings.Ops_QueueWaiting, queued.Count)));
        if (queued.Count == 0)
        {
            _list.Children.Add(OptionsUi.Secondary(AppStrings.Ops_QueueHint));
        }

        for (var i = 0; i < queued.Count; i++)
        {
            Add(QueuedRow(queued[i], i, queued.Count));
        }

        if (finished.Count > 0)
        {
            var clear = Ui.Link(AppStrings.Ops_ClearFinished, () =>
            {
                _service.Queue.ClearFinished();
                Refresh();
            });
            _list.Children.Add(Section(AppStrings.Ops_Finished, clear));
            foreach (var job in finished)
            {
                Add(FinishedRow(job));
            }
        }
    }

    private void Add(Control row)
    {
        if (_list.Children.Count > 0 && _list.Children[^1] is not TextBlock { Tag: "section" })
        {
            row.Margin = new Thickness(0, 2, 0, 0);
            _list.Children.Add(new Border { Height = 1, Margin = new Thickness(0, 6, 0, 6) }.Res(Border.BackgroundProperty, "Brush.Border"));
        }

        _list.Children.Add(row);
    }

    private static Control Section(string text, Control? action = null)
    {
        var label = OptionsUi.Text(text.ToUpperInvariant(), 11, FontWeight.SemiBold, "Brush.TertiaryText");
        label.Tag = "section";
        if (action is null)
        {
            label.Margin = new Thickness(0, 12, 0, 4);
            return label;
        }

        var row = new DockPanel { Margin = new Thickness(0, 12, 0, 4) };
        DockPanel.SetDock(action, Dock.Right);
        row.Children.Add(action);
        row.Children.Add(label);
        return row;
    }

    private Control QueuedRow(OperationJob job, int index, int count)
    {
        var number = new Border
        {
            Width = 18,
            Height = 18,
            CornerRadius = new CornerRadius(9),
            Child = OptionsUi.Text((index + 1).ToString(CultureInfo.CurrentCulture), 11, FontWeight.SemiBold).Also(t => { t.HorizontalAlignment = HorizontalAlignment.Center; t.VerticalAlignment = VerticalAlignment.Center; }),
        }.Res(Border.BackgroundProperty, "Brush.Pressed");
        var top = Ui.TitleRow(job.Title, job.Route, number);
        var waits = index == 0
            ? _service.Queue.IsQueuePaused ? AppStrings.Ops_WaitsForResume : AppStrings.Ops_StartsAfterPrevious
            : Loc.Format(AppStrings.Ops_WaitsForNumber, index);
        var links = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 12,
            Children =
            {
                Ui.Link(AppStrings.Ops_StartNow, () => _service.Queue.StartNow(job)),
                Ui.Link("↑", () => _service.Queue.MoveUp(job), enabled: index > 0),
                Ui.Link("↓", () => _service.Queue.MoveDown(job), enabled: index < count - 1),
                Ui.Link(AppStrings.Common_Remove, () => _service.Queue.Remove(job)),
            },
        };
        return new StackPanel { Spacing = 4, Children = { top, Ui.SubRow(OptionsUi.Text(waits, 12, brush: "Brush.SecondaryText"), links) } };
    }

    private Control FinishedRow(OperationJob job)
    {
        var top = Ui.TitleRow(job.Title, job.Route);
        var issues = job.Issues;
        Control status = job.State switch
        {
            JobState.Done => OptionsUi.Text(Loc.Format(AppStrings.Ops_DoneSummary, Summary(job)), 12, brush: "Brush.Success"),
            JobState.DoneWithIssues => OptionsUi.Text(Loc.Format(AppStrings.Ops_DoneWithIssues, issues.Count), 12, brush: "Brush.Danger"),
            JobState.Cancelled => OptionsUi.Text(AppStrings.Ops_Cancelled, 12, brush: "Brush.Danger"),
            _ => OptionsUi.Text(Loc.Format(AppStrings.Ops_Stopped, job.Error?.Message ?? AppStrings.Ops_AnError), 12, brush: "Brush.Danger", wrap: true),
        };
        var stack = new StackPanel { Spacing = 4, Children = { top } };
        if (issues.Count > 0)
        {
            var show = _showIssues.Contains(job);
            stack.Children.Add(Ui.SubRow(status, Ui.Link(show ? AppStrings.Common_Hide : AppStrings.Common_Show, () =>
            {
                if (!_showIssues.Remove(job))
                {
                    _showIssues.Add(job);
                }

                Refresh();
            })));
            if (show)
            {
                var list = new StackPanel { Spacing = 2 };
                foreach (var issue in issues.Take(50))
                {
                    list.Children.Add(OptionsUi.Text(Loc.Format(AppStrings.Ops_IssueLine, Path.GetFileName(issue.Path), issue.Message), 12, brush: "Brush.SecondaryText", wrap: true));
                }

                if (issues.Count > 50)
                {
                    list.Children.Add(OptionsUi.Text(Loc.Format(AppStrings.Common_AndMore, issues.Count - 50), 12, brush: "Brush.TertiaryText"));
                }

                stack.Children.Add(new Border { Padding = new Thickness(10, 6), CornerRadius = new CornerRadius(4), Child = list }.Res(Border.BackgroundProperty, "Brush.Hover"));
            }
        }
        else
        {
            stack.Children.Add(status);
        }

        return stack;
    }

    private string Summary(OperationJob job)
    {
        var p = job.Progress;
        var seconds = Math.Max(1, (int)Math.Round(job.Elapsed.TotalSeconds));
        var what = job.Kind == OperationKind.Delete || p.TotalBytes == 0 ? Ui.Items(p.TotalItems) : _format().Size(p.TotalBytes);
        return Loc.Format(AppStrings.Ops_SummaryIn, what, Ui.Duration(TimeSpan.FromSeconds(seconds)));
    }
}

internal sealed class JobView : StackPanel
{
    private static readonly (string Label, long Bytes)[] _limits =
        [(AppStrings.Ops_NoLimit, 0), ("100 MB/s", 100L << 20), ("20 MB/s", 20L << 20), ("5 MB/s", 5L << 20), ("1 MB/s", 1L << 20)];

    private readonly OperationJob _job;
    private readonly OperationsService _service;
    private readonly ProgressBar _bar = Ui.Bar();
    private readonly SpeedGraph _graph = new();
    private readonly TextBlock _line = OptionsUi.Text("", 12, brush: "Brush.SecondaryText", wrap: true);
    private readonly Grid _stats = new() { ColumnDefinitions = new ColumnDefinitions("1.7*,*,*,0.8*"), ColumnSpacing = 10 };
    private readonly TextBlock[] _statValues = new TextBlock[4];
    private readonly StackPanel _current = new() { Spacing = 2 };
    private readonly TextBlock _currentText = OptionsUi.Text("", 12, brush: "Brush.SecondaryText");
    private readonly ProgressBar _currentBar = Ui.Bar(3);
    private readonly Button _pause, _expand;
    private readonly ComboBox _limit;
    private readonly ContentControl _question = new();
    private OperationQuestion? _shownQuestion;
    private bool _questionInPanel;
    private bool _expanded;
    private DisplayFormat _format = DisplayFormat.Default;

    public JobView(OperationJob job, OperationsService service, bool expanded)
    {
        _job = job;
        _service = service;
        _expanded = expanded;
        Spacing = 4;
        _expand = Ui.Link(expanded ? "▴" : "▾", ToggleExpanded);
        ToolTip.SetTip(_expand, AppStrings.Ops_SpeedGraph);
        Children.Add(Ui.TitleRow(job.Title, job.Route, trailing: _expand));
        Children.Add(_graph);
        string[] labels = [AppStrings.Ops_Stat_Done, AppStrings.Ops_Stat_Now, AppStrings.Ops_Stat_Peak, AppStrings.Ops_Stat_Left];
        for (var i = 0; i < 4; i++)
        {
            _statValues[i] = OptionsUi.Text("", 13, FontWeight.SemiBold);
            var cell = new StackPanel { Children = { OptionsUi.Text(labels[i], 11, brush: "Brush.SecondaryText"), _statValues[i] } };
            Grid.SetColumn(cell, i);
            _stats.Children.Add(cell);
        }

        Children.Add(_stats);
        _current.Children.Add(_currentText);
        _current.Children.Add(_currentBar);
        Children.Add(_current);
        Children.Add(_bar);

        _limit = new ComboBox { MinWidth = 104, Height = 26, FontSize = 12, Padding = new Thickness(8, 0) };
        foreach (var (label, _) in _limits)
        {
            _limit.Items.Add(label);
        }

        var known = Array.FindIndex(_limits, l => l.Bytes == job.SpeedLimit);
        if (known < 0)
        {
            _limit.Items.Add($"{job.SpeedLimit / (1024.0 * 1024):0.#} MB/s");
            known = _limits.Length;
        }

        _limit.SelectedIndex = known;
        _limit.SelectionChanged += (_, _) =>
        {
            if (_limit.SelectedIndex is >= 0 and var i && i < _limits.Length)
            {
                _job.SpeedLimit = _limits[i].Bytes;
            }
        };
        Avalonia.Automation.AutomationProperties.SetName(_limit, AppStrings.Ops_SpeedLimit);

        _pause = Ui.Link(AppStrings.Common_Pause, () =>
        {
            if (_job.State == JobState.Paused)
            {
                _job.Resume();
            }
            else
            {
                _job.Pause();
            }
        });
        var actions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 12,
            VerticalAlignment = VerticalAlignment.Center,
            Children = { _pause, Ui.Link(AppStrings.Common_Cancel, () => _job.Cancel()) },
        };
        var limitRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Children = { OptionsUi.Text(AppStrings.Ops_SpeedLimit, 12, brush: "Brush.SecondaryText").Also(t => t.VerticalAlignment = VerticalAlignment.Center), _limit } };
        Children.Add(_line);
        Children.Add(Ui.SubRow(job.Kind == OperationKind.Delete ? new Control() : limitRow, actions));
        Children.Add(_question);
    }

    internal OperationJob Job => _job;

    internal bool IsExpanded => _expanded;

    internal SpeedGraph Graph => _graph;

    internal ComboBox LimitBox => _limit;

    internal Control? QuestionCard => _question.Content as Control;

    private void ToggleExpanded()
    {
        _expanded = !_expanded;
        _expand.Content = _expanded ? "▴" : "▾";
        Update(_format);
    }

    public void Update(DisplayFormat format)
    {
        _format = format;
        var p = _job.Progress;
        var state = _job.State;
        var paused = state == JobState.Paused;
        var bytesJob = !p.CountsFiles && _job.Kind != OperationKind.Delete;
        var graph = _expanded && state != JobState.Waiting && _job.Kind != OperationKind.Delete;
        _graph.IsVisible = graph;
        _stats.IsVisible = graph;
        _bar.IsVisible = !graph;
        _bar.Value = p.Fraction * 100;
        _expand.IsVisible = _job.Kind != OperationKind.Delete;
        _pause.Content = paused ? AppStrings.Common_Resume : AppStrings.Common_Pause;
        _pause.IsEnabled = state is JobState.Running or JobState.Paused;

        var now = bytesJob ? Ui.Speed(format, p.BytesPerSecond) : Ui.FilesPerSecond(p.ItemsPerSecond);
        var left = p.Remaining is { } r ? Ui.Left(r) : "–";
        if (graph)
        {
            _graph.Update(_job.SpeedHistory, p.Fraction, !bytesJob, paused, now,
                Loc.Format(AppStrings.Ops_Average, bytesJob ? Ui.Speed(format, p.AverageBytesPerSecond) : Ui.FilesPerSecond(p.AverageItemsPerSecond)));
            _statValues[0].Text = Loc.Format(AppStrings.Common_XOfY, bytesJob ? format.Size(p.DoneBytes) : p.DoneItems.ToString("N0", CultureInfo.CurrentCulture), bytesJob ? format.Size(p.TotalBytes) : p.TotalItems.ToString("N0", CultureInfo.CurrentCulture));
            _statValues[1].Text = now;
            _statValues[2].Text = bytesJob ? Ui.Speed(format, p.PeakBytesPerSecond) : "";
            _statValues[3].Text = left;
        }

        var bigFile = p.CurrentTotal >= 64L << 20 && state is JobState.Running or JobState.Paused;
        _current.IsVisible = bigFile;
        if (bigFile)
        {
            _currentText.Text = Loc.Format(AppStrings.Ops_CurrentFile, Path.GetFileName(p.Current), format.Size(p.CurrentDone), format.Size(p.CurrentTotal));
            _currentBar.Value = p.CurrentTotal > 0 ? 100.0 * p.CurrentDone / p.CurrentTotal : 0;
        }

        _line.Text = state switch
        {
            JobState.Waiting => Loc.Format(AppStrings.Ops_WaitsForJob, Blocker()),
            _ when p.Phase == OperationPhase.Scanning => Loc.Format(AppStrings.Ops_Scanning, Ui.Items(p.TotalItems)),
            _ when _job.Kind == OperationKind.Delete => Loc.Format(AppStrings.Ops_DeleteProgress, p.DoneItems, Ui.Items(p.TotalItems)),
            _ when graph => "",
            _ => Loc.Format(paused ? AppStrings.Ops_LinePaused : _job.Question is not null ? AppStrings.Ops_LineAsking : AppStrings.Ops_Line,
                (int)(p.Fraction * 100),
                bytesJob ? Loc.Format(AppStrings.Common_XOfY, format.Size(p.DoneBytes), format.Size(p.TotalBytes)) : Loc.Format(AppStrings.Ops_FilesOf, p.DoneItems, p.TotalItems),
                now,
                left),
        };
        _line.IsVisible = _line.Text.Length > 0;

        var question = _job.Question;
        var inPanel = _job.QuestionsInPanel;
        if (!ReferenceEquals(question, _shownQuestion) || inPanel != _questionInPanel)
        {
            _shownQuestion = question;
            _questionInPanel = inPanel;
            _question.Content = question is null ? null : QuestionCards.For(_job, question, _service, format);
        }
    }

    private string Blocker()
        => _service.Jobs.FirstOrDefault(j => j != _job && j.IsActive && j.Volumes.Intersect(_job.Volumes, StringComparer.OrdinalIgnoreCase).Any())?.Title ?? AppStrings.Ops_AnotherJob;
}

internal static class QuestionCards
{
    public static Control For(OperationJob job, OperationQuestion question, OperationsService service, DisplayFormat format) => question switch
    {
        ErrorQuestion e => Card(true,
            new[] { Ui.Bold(Path.GetFileName(e.Path)), Ui.Plain(": " + e.Message) },
            (AppStrings.Ops_Retry, () => job.Answer(new OperationAnswer(AnswerKind.Retry)), true),
            (AppStrings.Ops_Skip, () => job.Answer(new OperationAnswer(AnswerKind.Skip)), false),
            (AppStrings.Ops_SkipAll, () => job.Answer(new OperationAnswer(AnswerKind.Skip, ForAll: true)), false),
            (AppStrings.Common_Cancel, () => job.Answer(new OperationAnswer(AnswerKind.Cancel)), false)),
        PermanentDeleteQuestion d => Card(true,
            Rich.Build(d.Items.Count == 1 ? AppStrings.Ops_CantRecycle_One : AppStrings.Ops_CantRecycle_Other, null, Ui.Bold(d.Items.Count == 1 ? Path.GetFileName(d.Items[0].Path) : Loc.Plural(d.Items.Count, AppStrings.Count_Items_One, AppStrings.Count_Items_Other)), Ui.Plain(d.Items[0].Reason)),
            (AppStrings.Ops_DeletePermanently, () => job.Answer(new OperationAnswer(AnswerKind.DeletePermanently)), false),
            (d.Items.Count == 1 ? AppStrings.Ops_LeaveIt : AppStrings.Ops_LeaveThem, () => job.Answer(new OperationAnswer(AnswerKind.Skip)), true),
            (AppStrings.Common_Cancel, () => job.Answer(new OperationAnswer(AnswerKind.Cancel)), false)),
        CheckQuestion q when job.QuestionsInPanel => Card(false, Dialogs.CheckTexts.For(job, q).Message,
            (Dialogs.CheckTexts.For(job, q).Yes, () => job.Answer(new OperationAnswer(AnswerKind.Yes)), true),
            (AppStrings.Ops_Skip, () => job.Answer(new OperationAnswer(AnswerKind.Skip)), false),
            (AppStrings.Ops_Answer, () => service.BringToFront(job), false)),
        CheckQuestion q => Card(false,
            Rich.Build(AppStrings.Check_Waiting, null, Ui.Bold(Path.GetFileName(q.Path))),
            (AppStrings.Ops_Answer, () => service.BringToFront(job), true)),
        ConflictQuestion c when job.QuestionsInPanel => Conflict(job, c, service, format),
        ConflictQuestion c => Card(false,
            Rich.Build(AppStrings.Ops_WaitingForAnswer, null, Ui.Bold(Path.GetFileName(c.Target))),
            (AppStrings.Ops_Answer, () => service.BringToFront(job), true)),
        _ => new Control(),
    };

    private static Control Conflict(OperationJob job, ConflictQuestion c, OperationsService service, DisplayFormat format)
    {
        var forAll = new CheckBox { Content = AppStrings.Ops_ForTheRest, FontSize = 12, VerticalAlignment = VerticalAlignment.Center };
        var newer = c.SourceInfo.LastWriteUtc > c.TargetInfo.LastWriteUtc;
        var where = Path.GetFileName(Path.GetDirectoryName(c.Target)) ?? c.Target;
        var rows = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), RowDefinitions = new RowDefinitions("Auto,Auto"), Margin = new Thickness(0, 4, 0, 6) };
        void Row(int r, string label, ItemInfo info, string? tag)
        {
            var l = OptionsUi.Text(label, 12, brush: "Brush.SecondaryText");
            l.Margin = new Thickness(0, 0, 10, 0);
            var v = OptionsUi.Text($"{format.Kilobytes(info.Size)} · {format.DateAndTime(info.LastWriteUtc.ToLocalTime())}", 12);
            Grid.SetRow(l, r);
            Grid.SetRow(v, r);
            Grid.SetColumn(v, 1);
            rows.Children.Add(l);
            rows.Children.Add(v);
            if (tag is not null)
            {
                var t = Ui.Tag(tag);
                Grid.SetRow(t, r);
                Grid.SetColumn(t, 2);
                rows.Children.Add(t);
            }
        }

        Row(0, job.Kind == OperationKind.Move ? AppStrings.Ops_Conflict_Moving : AppStrings.Ops_Conflict_Copying, c.SourceInfo, Tag(c.SourceInfo, c.TargetInfo));
        Row(1, AppStrings.Ops_Conflict_There, c.TargetInfo, Tag(c.TargetInfo, c.SourceInfo));

        var text = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 12 };
        Rich.Add(text.Inlines!, AppStrings.Ops_AlreadyIn, null, Ui.Bold(Path.GetFileName(c.Target)), Ui.Plain(where));
        var buttons = new WrapPanel { ItemSpacing = 6, LineSpacing = 6 };
        foreach (var (label, kind, primary) in new[] { (AppStrings.Conflict_Overwrite, AnswerKind.Overwrite, newer), (AppStrings.Ops_Skip, AnswerKind.Skip, !newer), (AppStrings.Conflict_KeepBoth, AnswerKind.KeepBoth, false), (AppStrings.Conflict_IfNewer, AnswerKind.OverwriteOlder, false) })
        {
            buttons.Children.Add(Ui.Mini(label, () => job.Answer(new OperationAnswer(kind, forAll.IsChecked == true)), primary));
        }

        buttons.Children.Add(forAll);
        buttons.Children.Add(Ui.Mini(AppStrings.Ops_Answer, () => service.BringToFront(job), false));
        return new Border
        {
            Padding = new Thickness(10, 8),
            CornerRadius = new CornerRadius(4),
            Margin = new Thickness(0, 4, 0, 0),
            Child = new StackPanel { Children = { text, rows, buttons } },
        }.Res(Border.BackgroundProperty, "Brush.WarningBackground");
    }

    private static string? Tag(ItemInfo a, ItemInfo b)
        => a.LastWriteUtc > b.LastWriteUtc ? AppStrings.Conflict_Newer : a.Size > b.Size ? AppStrings.Conflict_Larger : null;

    private static Control Card(bool error, Avalonia.Controls.Documents.Inline[] message, params (string Label, Action Run, bool Primary)[] actions)
    {
        var text = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 12, Margin = new Thickness(0, 0, 0, 6) };
        foreach (var inline in message)
        {
            text.Inlines!.Add(inline);
        }

        var buttons = new WrapPanel { ItemSpacing = 6, LineSpacing = 6 };
        foreach (var (label, run, primary) in actions)
        {
            buttons.Children.Add(Ui.Mini(label, run, primary));
        }

        return new Border
        {
            Padding = new Thickness(10, 8),
            CornerRadius = new CornerRadius(4),
            Margin = new Thickness(0, 4, 0, 0),
            Child = new StackPanel { Children = { text, buttons } },
        }.Res(Border.BackgroundProperty, error ? "Brush.DangerBackground" : "Brush.WarningBackground");
    }
}

internal static class Ui
{
    public static Button Link(string text, Action click, bool enabled = true)
    {
        var button = new Button
        {
            Content = text,
            Padding = new Thickness(0),
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            FontSize = 12,
            IsEnabled = enabled,
            Cursor = new Cursor(StandardCursorType.Hand),
            VerticalAlignment = VerticalAlignment.Center,
        };
        button.Classes.Add("link");
        button.Res(Button.ForegroundProperty, enabled ? "Brush.Accent" : "Brush.TertiaryText");
        button.Click += (_, _) => click();
        return button;
    }

    public static Button Mini(string text, Action click, bool primary)
    {
        var button = new Button { Content = text, FontSize = 12, Padding = new Thickness(10, 3), MinHeight = 26 };
        if (primary)
        {
            button.Classes.Add("accent");
        }

        button.Click += (_, _) => click();
        return button;
    }

    public static ProgressBar Bar(double height = 4) => new() { Minimum = 0, Maximum = 100, Height = height, MinHeight = height, Margin = new Thickness(0, 2, 0, 2) };

    public static Control TitleRow(string title, string route, Control? leading = null, Control? trailing = null)
    {
        var row = new DockPanel();
        var right = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, VerticalAlignment = VerticalAlignment.Center };
        right.Children.Add(OptionsUi.Text(route, 12, brush: "Brush.SecondaryText"));
        if (trailing is not null)
        {
            right.Children.Add(trailing);
        }

        DockPanel.SetDock(right, Dock.Right);
        row.Children.Add(right);
        if (leading is not null)
        {
            leading.Margin = new Thickness(0, 0, 8, 0);
            DockPanel.SetDock(leading, Dock.Left);
            row.Children.Add(leading);
        }

        var t = OptionsUi.Text(title, 13, FontWeight.SemiBold);
        t.TextTrimming = TextTrimming.CharacterEllipsis;
        row.Children.Add(t);
        return row;
    }

    public static Control SubRow(Control left, Control right)
    {
        var row = new DockPanel();
        right.VerticalAlignment = VerticalAlignment.Center;
        DockPanel.SetDock(right, Dock.Right);
        row.Children.Add(right);
        left.VerticalAlignment = VerticalAlignment.Center;
        row.Children.Add(left);
        return row;
    }

    public static Border Tag(string text) => new Border
    {
        Padding = new Thickness(7, 0),
        CornerRadius = new CornerRadius(8),
        Margin = new Thickness(8, 0, 0, 0),
        VerticalAlignment = VerticalAlignment.Center,
        Child = OptionsUi.Text(text, 11, FontWeight.SemiBold, "Brush.SelectedText"),
    }.Res(Border.BackgroundProperty, "Brush.SelectionBackground");

    public static Avalonia.Controls.Documents.Run Bold(string text) => new(text) { FontWeight = FontWeight.SemiBold };

    public static Avalonia.Controls.Documents.Run Plain(string text) => new(text);

    public static string Speed(DisplayFormat format, double bytesPerSecond) => format.Size((long)Math.Max(0, bytesPerSecond)) + "/s";

    public static string FilesPerSecond(double perSecond) => Loc.Format(AppStrings.Ops_FilesPerSecond, perSecond);

    public static string Items(int n) => Loc.Plural(n, AppStrings.Count_Items_One, AppStrings.Count_Items_Other);

    public static string Duration(TimeSpan t) => t.TotalSeconds < 60 ? Loc.Format(AppStrings.Time_Seconds, (int)Math.Max(1, t.TotalSeconds))
        : t.TotalMinutes < 60 ? Loc.Format(AppStrings.Time_MinutesSeconds, (int)t.TotalMinutes, t.Seconds)
        : Loc.Format(AppStrings.Time_HoursMinutes, (int)t.TotalHours, t.Minutes);

    public static string Left(TimeSpan t) => t.TotalSeconds < 60 ? Loc.Format(AppStrings.Time_Seconds, (int)Math.Max(1, Math.Ceiling(t.TotalSeconds)))
        : t.TotalMinutes < 60 ? Loc.Format(AppStrings.Time_AboutMinutes, (int)Math.Round(t.TotalMinutes))
        : Loc.Format(AppStrings.Time_AboutHoursMinutes, (int)t.TotalHours, t.Minutes);
}

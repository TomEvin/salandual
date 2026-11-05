// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.App/Operations/ProgressWindow.cs

using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Salandual.App.Controls;
using Salandual.App.Options;
using Salandual.App.Resources;
using Salandual.App.Theme;
using Salandual.Core.Listing;
using Salandual.Core.Operations;
using Salandual.Core.Resources;
using Salandual.Platform.Windows.FileSystem;

namespace Salandual.App.Operations;

public sealed class ProgressWindow : Window
{
    private const string MinimizeGlyph = "Icon.Minimize";
    private static readonly TimeSpan StuckAfter = TimeSpan.FromSeconds(3);

    private readonly OperationJob _job;
    private readonly Func<DisplayFormat> _format;
    private readonly JobView _view;
    private bool _stuck;
    private string? _stuckText;
    private string? _endText;
    private readonly CheckBox _closeWhenDone;
    private readonly Button _background, _pause, _cancel, _close;
    private readonly DispatcherTimer _timer;
    private (int Done, string? Current) _last;
    private DateTime _lastChange;
    private string? _holderOf;
    private string? _holder;
    private bool _ended;

    public ProgressWindow(OperationJob job, OperationsService service, Func<DisplayFormat> format, bool closeWhenDone, Action<bool> closeWhenDoneChanged)
    {
        _job = job;
        _format = format;
        Title = job.Title;
        Width = 540;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        this.Res(BackgroundProperty, "Brush.PanelBackground");

        _view = new JobView(job, service, expanded: true, inWindow: true);
        var body = new StackPanel { Spacing = 8, Margin = new Thickness(20, 6, 20, 16), Children = { _view } };

        _closeWhenDone = new CheckBox { Content = AppStrings.Progress_CloseWhenDone, IsChecked = closeWhenDone, VerticalAlignment = VerticalAlignment.Center };
        _closeWhenDone.IsCheckedChanged += (_, _) => closeWhenDoneChanged(_closeWhenDone.IsChecked == true);
        _background = new Button { Content = AppStrings.Progress_Background, Padding = new Thickness(14, 5) };
        ToolTip.SetTip(_background, AppStrings.Progress_BackgroundTip);
        _background.Click += (_, _) => Close();
        _pause = new Button { Content = AppStrings.Common_Pause, MinWidth = 84, Padding = new Thickness(14, 5), HorizontalContentAlignment = HorizontalAlignment.Center };
        _pause.Click += (_, _) => TogglePause();
        _cancel = new Button { Content = AppStrings.Common_Cancel, MinWidth = 84, Padding = new Thickness(14, 5), HorizontalContentAlignment = HorizontalAlignment.Center };
        _cancel.Click += (_, _) => _job.Cancel();
        _close = new Button { Content = AppStrings.Common_Close, MinWidth = 84, Padding = new Thickness(14, 5), HorizontalContentAlignment = HorizontalAlignment.Center, IsVisible = false };
        _close.Classes.Add("accent");
        _close.Click += (_, _) => Close();

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { _background, _pause, _cancel, _close } };
        var footerRow = new DockPanel { LastChildFill = false };
        DockPanel.SetDock(buttons, Dock.Right);
        footerRow.Children.Add(buttons);
        footerRow.Children.Add(_closeWhenDone);
        var footer = new Border { Padding = new Thickness(20, 12), BorderThickness = new Thickness(0, 1, 0, 0), Child = footerRow }
            .Res(Border.BackgroundProperty, "Brush.WindowBackground").Res(Border.BorderBrushProperty, "Brush.Border");

        var root = new DockPanel();
        DockPanel.SetDock(footer, Dock.Bottom);
        root.Children.Add(footer);
        root.Children.Add(body);
        var titleMinimize = TitleBarParts.Button(AppIcons.Glyph(MinimizeGlyph, 16, 16), AppStrings.Progress_BackgroundTip, AppStrings.Progress_Background);
        titleMinimize.Width = 40;
        titleMinimize.Click += (_, _) => Close();
        Content = TitleBarParts.ForDialog(this, root, titleMinimize);

        _lastChange = Now();
        _view.Update(format());
        _timer = new DispatcherTimer(TimeSpan.FromMilliseconds(250), DispatcherPriority.Background, (_, _) => Refresh());
        Opened += (_, _) =>
        {
            Refresh();
            _timer.Start();
            _background.Focus();
        };
        Closed += (_, _) => _timer.Stop();
    }

    internal OperationJob Job => _job;

    internal Func<DateTime> Now { get; set; } = () => DateTime.UtcNow;

    internal bool HasEnded => _ended;

    internal string? StuckText => _stuck ? _stuckText : null;

    internal string EndText => _ended ? _endText ?? "" : "";

    internal JobView View => _view;

    internal CheckBox CloseWhenDoneBox => _closeWhenDone;

    internal Button PauseButton => _pause;

    internal Button CancelButton => _cancel;

    internal Button CloseButton => _close;

    internal Button BackgroundButton => _background;

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key == Key.Escape && !e.Handled)
        {
            Close();
            e.Handled = true;
        }
    }

    private void TogglePause()
    {
        if (_job.State == JobState.Paused)
        {
            _job.Resume();
        }
        else
        {
            _job.Pause();
        }

        Refresh();
    }

    internal void Refresh()
    {
        _view.Update(_format());
        var state = _job.State;
        if (!_job.IsFinished)
        {
            Title = Loc.Format(AppStrings.Progress_Title, Ui.Percent(_job.Progress.Fraction), _job.Title);
        }

        _pause.Content = state == JobState.Paused ? AppStrings.Common_Resume : AppStrings.Common_Pause;
        _pause.IsEnabled = state is JobState.Running or JobState.Paused;
        ShowStuck();
        if (_job.IsFinished && !_ended)
        {
            End();
        }
    }

    private void ShowStuck()
    {
        var p = _job.Progress;
        var now = Now();
        var key = (p.DoneItems, p.Current);
        if (key != _last || _job.State != JobState.Running || _job.Kind != OperationKind.Delete || p.Phase != OperationPhase.Working)
        {
            _last = key;
            _lastChange = now;
            if (_stuck)
            {
                _stuck = false;
                _view.SetStatus(null);
            }

            return;
        }

        var waited = now - _lastChange;
        if (waited < StuckAfter || p.Current is not { Length: > 0 } current)
        {
            return;
        }

        if (_holderOf != current)
        {
            _holderOf = current;
            _holder = null;
            _ = Task.Run(() => LockingPrograms.Describe(current)).ContinueWith(
                t => Dispatcher.UIThread.Post(() =>
                {
                    if (_holderOf == current)
                    {
                        _holder = t.IsCompletedSuccessfully ? t.Result : null;
                    }
                }),
                TaskScheduler.Default);
        }

        var text = Loc.Format(AppStrings.Progress_StillOn, Path.GetFileName(current), Ui.Duration(waited));
        _stuckText = _holder is { } holder ? text + " " + holder : text;
        _view.SetStatus(_stuckText);
        _stuck = true;
    }

    private void End()
    {
        _ended = true;
        _timer.Stop();
        _stuck = false;
        if (_closeWhenDone.IsChecked == true)
        {
            Close();
            return;
        }

        var issues = _job.Issues;
        var (text, brush) = _job.State switch
        {
            JobState.Done => (Loc.Format(AppStrings.Ops_DoneSummary, Ui.Summary(_job, _format())), "Brush.Success"),
            JobState.DoneWithIssues => (Loc.Format(AppStrings.Ops_DoneWithIssues, issues.Count), "Brush.Danger"),
            JobState.Cancelled => (AppStrings.Ops_Cancelled, "Brush.Danger"),
            _ => (Loc.Format(AppStrings.Ops_Stopped, _job.Error?.Message ?? AppStrings.Ops_AnError), "Brush.Danger"),
        };
        string? detail = null;
        if (issues.Count > 0)
        {
            detail = Loc.Format(AppStrings.Ops_IssueLine, Path.GetFileName(issues[0].Path), issues[0].Message);
            if (issues.Count > 1)
            {
                detail += " " + Loc.Format(AppStrings.Common_AndMore, issues.Count - 1);
            }
        }

        _endText = detail is null ? text : text + "\n" + detail;
        Title = _job.Title;
        _view.SetStatus(text, brush, detail);
        _background.IsVisible = false;
        _pause.IsVisible = false;
        _cancel.IsVisible = false;
        _closeWhenDone.Opacity = 0;
        _closeWhenDone.IsHitTestVisible = false;
        _closeWhenDone.Focusable = false;
        _close.IsVisible = true;
        _close.Focus();
    }
}

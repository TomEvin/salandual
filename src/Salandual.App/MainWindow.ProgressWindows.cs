// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.App/MainWindow.ProgressWindows.cs

using Avalonia;
using Avalonia.Threading;
using Salandual.App.Operations;
using Salandual.Core.Operations;
using Salandual.Core.Settings;

namespace Salandual.App;

public partial class MainWindow
{
    private const int ProgressOffset = 28;
    private readonly Dictionary<OperationJob, ProgressWindow> _progressWindows = [];

    internal IReadOnlyDictionary<OperationJob, ProgressWindow> ProgressWindows => _progressWindows;

    internal TimeSpan ProgressDelay { get; set; } = TimeSpan.FromMilliseconds(500);

    private void OnJobStarted(OperationJob job)
    {
        if (_services.Settings.Current.Operations.Progress != ProgressShow.Window || job.Kind == OperationKind.Git)
        {
            return;
        }

        if (ProgressDelay <= TimeSpan.Zero)
        {
            OpenProgressWhileRunning(job);
            return;
        }

        DispatcherTimer.RunOnce(() => OpenProgressWhileRunning(job), ProgressDelay);
    }

    private void OpenProgressWhileRunning(OperationJob job)
    {
        if (!job.IsFinished && _services.Settings.Current.Operations.Progress == ProgressShow.Window)
        {
            ShowProgressWindow(job);
        }
    }

    internal void ShowProgressWindow(OperationJob job)
    {
        if (_progressWindows.TryGetValue(job, out var open))
        {
            open.Activate();
            return;
        }

        if (job.IsFinished)
        {
            return;
        }

        var settings = _services.Settings.Current.Operations;
        var window = new ProgressWindow(job, Operations, CurrentFormat, settings.CloseWhenDone,
            on => _services.Settings.Update(s => s with { Operations = s.Operations with { CloseWhenDone = on } }));
        var offset = _progressWindows.Count * ProgressOffset;
        _progressWindows[job] = window;
        if (offset > 0)
        {
            window.Opened += (_, _) => window.Position = new PixelPoint(window.Position.X + offset, window.Position.Y + offset);
        }

        window.Closed += (_, _) =>
        {
            _progressWindows.Remove(job);
            if (_progressWindows.Count == 0 && _conflictDialog is null)
            {
                ActivePanel.View.Focus();
            }
        };

        if (settings.ProgressBlocks)
        {
            _ = window.ShowDialog(this);
        }
        else
        {
            window.Show(this);
        }
    }
}

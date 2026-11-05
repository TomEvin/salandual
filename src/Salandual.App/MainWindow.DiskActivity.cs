// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.App/MainWindow.DiskActivity.cs

using Avalonia.Controls;
using Salandual.App.Panels;
using Salandual.Core;

namespace Salandual.App;

public partial class MainWindow
{
    internal const int ActivitySeconds = 30;

    private readonly List<string> _activityExtra = [];
    private DiskActivityMonitor? _activity;

    internal DiskActivityMonitor Activity => _activity ??= CreateActivity();

    private DiskActivityMonitor CreateActivity()
    {
        var monitor = new DiskActivityMonitor(_services.DiskActivitySource);
        monitor.Updated += (_, _) => ShowActivity();
        monitor.Sampled += (_, _) =>
        {
            if (_services.Settings.Current.DriveMenu.ActivityLook != ActivityLook.Bar)
            {
                ShowActivity();
            }
        };
        if (!_services.IsIsolated)
        {
            monitor.IsMinimized = () => WindowState == WindowState.Minimized;
            monitor.InFront = Viewer.NativeFocus.IsSalandualInFront;
        }

        Activated += (_, _) => monitor.CheckInFront();
        PropertyChanged += (_, e) =>
        {
            if (e.Property == WindowStateProperty)
            {
                monitor.CheckInFront();
            }
        };
        foreach (var host in _panels)
        {
            host.PathChanged += (_, _) => UpdateActivity();
        }

        Closed += (_, _) => monitor.Dispose();
        return monitor;
    }

    internal void StartActivity()
    {
        _ = Activity;
        UpdateActivity();
        Activity.CheckInFront();
        if (!_services.IsIsolated)
        {
            Activity.Start();
        }
    }

    internal void SampleActivityOf(string? drive, bool on)
    {
        if (drive is null)
        {
            return;
        }

        if (on)
        {
            _activityExtra.Add(drive);
        }
        else
        {
            _activityExtra.Remove(drive);
        }

        _ = Activity;
        UpdateActivity();
    }

    internal void UpdateActivity()
    {
        if (_activity is null)
        {
            return;
        }

        var menu = _services.Settings.Current.DriveMenu;
        var bars = _services.Settings.Current.Layout.Bars;
        var wanted = new List<string>(_activityExtra);
        if (menu.ActivityInLine && bars.DirectoryLine)
        {
            wanted.AddRange(_panels.Select(h => DiskActivity.DriveOf(h.Panel.Path)).OfType<string>());
        }

        if (menu.ActivityInStrip && bars.DriveStrip)
        {
            wanted.AddRange(DriveStripItems().Where(i => i.Kind == StripItemKind.Drive && !i.IsNetwork).Select(i => DiskActivity.DriveOf(i.Target)).OfType<string>());
        }

        Activity.SetWanted(wanted);
        ShowActivity();
    }

    private void ShowActivity()
    {
        var menu = _services.Settings.Current.DriveMenu;
        var monitor = Activity;
        var now = monitor.Now();
        foreach (var host in _panels)
        {
            var drive = DiskActivity.DriveOf(host.Panel.Path);
            var line = host.DirectoryLine;
            line.ActivityLook = menu.ActivityLook;
            line.ActivityRecent = menu.ActivityInLine && menu.ActivityLook != ActivityLook.Bar && drive is not null && !monitor.IsPaused
                ? monitor.History.LastSeconds(drive, now, ActivitySeconds)
                : null;
            line.Activity = menu.ActivityInLine ? monitor.Get(drive) : null;
            host.Strip.Activity = menu.ActivityInStrip ? monitor.Values : null;
        }

        SharedStrip.Activity = menu.ActivityInStrip ? monitor.Values : null;
    }
}

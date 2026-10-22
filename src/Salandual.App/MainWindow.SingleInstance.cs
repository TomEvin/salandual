// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.App/MainWindow.SingleInstance.cs

using Avalonia.Controls;
using Avalonia.Threading;
using Salandual.Core;
using Salandual.Core.Settings;
using Salandual.Platform.Windows.Shell;

namespace Salandual.App;

public partial class MainWindow
{
    private IDisposable? _singleInstance;
    private WindowState _shownState = WindowState.Normal;

    private void InitSingleInstance()
    {
        _shownState = WindowState == WindowState.Minimized ? WindowState.Normal : WindowState;
        PropertyChanged += (_, e) =>
        {
            if (e.Property == WindowStateProperty && WindowState != WindowState.Minimized)
            {
                _shownState = WindowState;
            }
        };

        if (_services.IsIsolated)
        {
            return;
        }

        ApplySingleInstance(_services.Settings.Current.General);
        _services.Settings.Changed += (_, e) =>
        {
            if (e.Old.General != e.New.General)
            {
                Dispatcher.UIThread.Post(() => ApplySingleInstance(e.New.General));
            }
        };
        Closed += (_, _) =>
        {
            _singleInstance?.Dispose();
            _singleInstance = null;
        };
    }

    private void ApplySingleInstance(GeneralSettings general)
    {
        if (general.SingleInstance && _singleInstance is null)
        {
            _singleInstance = SingleInstance.Listen(SingleInstance.NameFor(_services.DataDirectory),
                (folder, args) => Dispatcher.UIThread.Post(() => ReceiveStart(StartOptions.Parse(args, folder))));
        }
        else if (!general.SingleInstance && _singleInstance is not null)
        {
            _singleInstance.Dispose();
            _singleInstance = null;
        }
    }

    internal void ReceiveStart(StartOptions start)
    {
        if (WindowState == WindowState.Minimized)
        {
            WindowState = _shownState;
        }

        Activate();
        if (start.Left is { } left)
        {
            _ = _panels[0].Panel.NavigateAsync(FolderOf(left));
        }

        if (start.Right is { } right)
        {
            _ = _panels[1].Panel.NavigateAsync(FolderOf(right));
        }

        var index = start.Panel != 0 ? start.ActiveIndex : _active;
        if (start.Active is { } active)
        {
            _ = _panels[index].Panel.NavigateAsync(FolderOf(active));
        }

        if (index != _active)
        {
            SetActive(index, focus: true);
        }
    }

    private static string FolderOf(string path) => File.Exists(path) ? Path.GetDirectoryName(path) ?? path : path;
}

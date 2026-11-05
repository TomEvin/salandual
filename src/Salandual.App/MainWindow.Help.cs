// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.App/MainWindow.Help.cs

using System.Globalization;
using Avalonia.Threading;
using Salandual.App.Diagnostics;
using Salandual.App.Dialogs;
using Salandual.App.Resources;
using Salandual.Core.Diagnostics;
using Salandual.Core.Resources;
using Salandual.Core.Settings;
using Salandual.Platform.Windows.Shell;

namespace Salandual.App;

public partial class MainWindow
{
    private LogsWindow? _logs;

    internal LogsWindow? Logs => _logs;

    internal AboutWindow? About { get; private set; }

    internal Func<Uri, bool> OpenWebPage { get; set; } = WebLinks.Open;

    private bool CopyText(string text) => FileClipboardAccess.SetText(text);

    private string LogsFolder => _services.Log.Folder ?? Path.Combine(_services.DataDirectory, AppServices.LogsFolderName);

    internal async Task ShowAboutAsync()
    {
        if (About is not null)
        {
            About.Activate();
            return;
        }

        var dark = (Avalonia.Application.Current as App)?.Theme?.ActiveTheme.Dark == true;
        var about = new AboutWindow(_services.InitialRendering, _services.DataDirectory, new AboutActions(CopyText, OpenWebPage, ShowInPanel),
            animate: _services.IsIsolated || SystemFacts.AnimationEffects(), dark: dark);
        About = about;
        try
        {
            await about.ShowDialog(this);
        }
        finally
        {
            About = null;
        }

        ActivePanel.View.Focus();
    }

    internal void ShowLogs(string? select = null)
    {
        if (_logs is null)
        {
            _logs = new LogsWindow(LogsFolder, AppFacts.UserFolder, CopyText, OpenWebPage, ShowInPanel);
            _logs.Closed += (_, _) =>
            {
                _logs = null;
                ActivePanel.View.Focus();
            };
            _logs.Show(this);
        }
        else
        {
            _logs.Reload();
            _logs.Activate();
        }

        if (select is not null)
        {
            _logs.Select(select);
        }
    }

    private void ShowInPanel(string folder)
    {
        Directory.CreateDirectory(folder);
        _ = ActivePanel.Panel.NavigateAsync(folder, null);
        Activate();
    }

    private void StartLog()
    {
        if (_services.IsIsolated)
        {
            return;
        }

        var log = _services.Log;
        var load = _services.Settings.LoadResult;
        _ = Task.Run(() =>
        {
            log.DeleteOld();
            log.Write(LogLevel.Info, Loc.Format(AppStrings.Log_Started, Core.ProductInfo.DisplayVersion, AppFacts.Rendering(_services.InitialRendering)));
            if (load.Status is SettingsLoadStatus.Corrupt or SettingsLoadStatus.Unreadable)
            {
                log.Write(LogLevel.Warning, Loc.Format(AppStrings.Log_SettingsBroken, load.Error?.Message ?? load.Status.ToString()));
            }

            if (log.Folder is { } folder && ProblemReports.TakeUnseen(folder) is { } crash)
            {
                Dispatcher.UIThread.Post(() => ShowNoticeInTurn(Loc.Format(AppStrings.Notice_ClosedUnexpectedly, CrashWhen(crash.Time, log.Now)), 20,
                    (AppStrings.Problem_ShowReport, () => ShowLogs(crash.Path)), (AppStrings.Common_OK, () => { })));
            }
        });
    }

    internal static string CrashWhen(DateTime time, DateTime now)
    {
        var clock = time.ToString("HH:mm", CultureInfo.CurrentCulture);
        return time.Date == now.Date ? Loc.Format(AppStrings.Notice_When_Today, clock)
            : time.Date == now.Date.AddDays(-1) ? Loc.Format(AppStrings.Notice_When_Yesterday, clock)
            : Loc.Format(AppStrings.Notice_When_Day, time.ToString("d MMMM", Loc.TextCulture), clock);
    }

    internal async Task AskAfterProblemAsync(Exception problem, string? report)
    {
        var choice = await new ProblemDialog(problem).ShowDialog<ProblemChoice>(this);
        switch (choice)
        {
            case ProblemChoice.Restart:
                Program.RestartAfterClose = true;
                Close();
                break;
            case ProblemChoice.ShowReport:
                ShowLogs(report);
                break;
        }
    }
}

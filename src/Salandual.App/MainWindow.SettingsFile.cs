// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.App/MainWindow.SettingsFile.cs

using Salandual.App.Resources;
using Salandual.Core.Resources;
using Salandual.Core.Settings;

namespace Salandual.App;

public partial class MainWindow
{
    private bool _saveFailureShown;

    internal void ShowSettingsFileProblem()
    {
        var settings = _services.Settings;
        if (SettingsFileMessages.ForLoad(settings.LoadResult, settings.FilePath) is { } text)
        {
            ShowNoticeInTurn(text, 30, SettingsFileActions(settings.FilePath));
        }
    }

    internal void ShowSettingsSaveFailed(Exception error)
    {
        if (_saveFailureShown)
        {
            return;
        }

        _saveFailureShown = true;
        var file = _services.Settings.FilePath;
        ShowNoticeInTurn(SettingsFileMessages.ForSave(error, file), 30, SettingsFileActions(file));
    }

    private void OnSettingsSaveFailed(object? sender, EventArgs e) => Avalonia.Threading.Dispatcher.UIThread.Post(() =>
    {
        if (_services.Settings.LastSaveError is { } error)
        {
            ShowSettingsSaveFailed(error);
        }
    });

    internal Options.SettingsFiles SettingsFiles => new(_services, this, (text, undo) =>
    {
        if (undo is null)
        {
            ShowNotice(text);
        }
        else
        {
            ShowNotice(text, 30, (AppStrings.Common_Undo, undo), (AppStrings.Common_OK, () => { }));
        }
    });

    private (string Label, Action Run)[] SettingsFileActions(string file) =>

    [
        (AppStrings.Temp_Show, () => _ = ActivePanel.Panel.NavigateAsync(Path.GetDirectoryName(file)!)),
        (AppStrings.Common_OK, () => { }),
    ];
}

internal static class SettingsFileMessages
{
    public static string? ForLoad(SettingsLoadResult load, string file) => load.Status switch
    {
        SettingsLoadStatus.Corrupt when load.BackupPath is { } backup => Loc.Format(AppStrings.Settings_Broken, file, Reason(load.Error), Path.GetFileName(backup)),
        SettingsLoadStatus.Corrupt => Loc.Format(AppStrings.Settings_BrokenKept, file, Reason(load.Error)),
        SettingsLoadStatus.Unreadable => Loc.Format(AppStrings.Settings_Unreadable, file, Reason(load.Error)),
        SettingsLoadStatus.NewerVersion => Loc.Format(AppStrings.Settings_Newer, file),
        _ => null,
    };

    public static string ForSave(Exception error, string file) => Loc.Format(AppStrings.Settings_SaveFailed, file, Reason(error));

    private static string Reason(Exception? error) => error switch
    {
        System.Text.Json.JsonException { LineNumber: { } line } => Loc.Format(AppStrings.Settings_Reason_Line, line + 1),
        UnauthorizedAccessException => AppStrings.Settings_Reason_NoAccess,
        IOException io when (io.HResult & 0xFFFF) is 32 or 33 => AppStrings.Settings_Reason_InUse,
        null => AppStrings.Settings_Reason_Unknown,
        _ => error.Message.TrimEnd('.', ' ', '\r', '\n'),
    };
}

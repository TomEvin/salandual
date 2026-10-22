// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.App/MainWindow.FirstSteps.cs

using Salandual.App.Resources;
using Salandual.App.Wizard;
using Salandual.Core;
using Salandual.Core.Resources;
using Salandual.Core.Settings;

namespace Salandual.App;

public partial class MainWindow
{
    private FirstStepsWindow? _firstSteps;

    internal static bool OfferFirstSteps(SettingsLoadStatus status, bool isolated, bool measuring)
        => status == SettingsLoadStatus.NotFound && !isolated && !measuring;

    internal FirstStepsWindow? FirstSteps => _firstSteps;

    internal async Task ShowFirstStepsAsync(bool fromHelp)
    {
        if (_firstSteps is not null)
        {
            _firstSteps.Activate();
            return;
        }

        var window = new FirstStepsWindow(_services, fromHelp)
        {
            Portable = !_services.IsIsolated && File.Exists(Path.Combine(AppContext.BaseDirectory, ProductInfo.PortableFlagFileName)),
            PanelFolders = () => (_panels[0].Panel.Path, _panels[1].Panel.Path),
        };
        _firstSteps = window;
        FirstStepsResult? result;
        try
        {
            result = await window.ShowDialog<FirstStepsResult?>(this);
        }
        finally
        {
            _firstSteps = null;
        }

        if (!fromHelp)
        {
            _services.Settings.SaveSoon();
        }

        if (result is not null)
        {
            ApplyFirstSteps(result, fromHelp);
        }

        ActivePanel.View.Focus();
    }

    internal void ApplyFirstSteps(FirstStepsResult result, bool fromHelp)
    {
        var before = _services.Settings.Current;
        if (result.Import is { } import && result.Parts is { } parts)
        {
            SettingsFiles.Import(import, parts);
        }
        else
        {
            _services.Settings.Update(s => result.Choices.ApplyTo(s, DateTime.Now));
            if (fromHelp)
            {
                ShowNotice(AppStrings.Wizard_Applied, 30, (AppStrings.Common_Undo, () => _services.Settings.Update(_ => before)), (AppStrings.Common_OK, () => { }));
            }
        }

        (Avalonia.Application.Current as App)?.Theme.Apply(_services.Settings.Current.Appearance);
        StartWithWindows(result.Choices.StartWithWindows);
    }

    private void StartWithWindows(bool on)
    {
        var start = _services.StartWithWindows;
        var program = _services.ProgramPath;
        try
        {
            if (on && !StartWithWindowsState.IsOn(start, program))
            {
                start.Register(program);
            }
            else if (!on && StartWithWindowsState.IsOn(start, program))
            {
                start.Unregister();
            }
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or System.Security.SecurityException)
        {
            ShowNotice(Loc.Format(AppStrings.Opt_StartWithWindows_Failed, ex.Message));
        }
    }
}

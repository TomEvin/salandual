// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.App/Options/OptionsWindow.General.cs

using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Salandual.App.Resources;
using Salandual.Core;
using Salandual.Core.Commands;
using Salandual.Core.Resources;
using Salandual.Core.Settings;
using static Salandual.App.Options.OptionsUi;

namespace Salandual.App.Options;

public partial class OptionsWindow
{
    internal Func<(string Left, string Right)>? PanelFolders { get; set; }

    internal ToggleSwitch? StartWithWindowsToggle { get; private set; }

    internal TextBlock? StartWithWindowsNote { get; private set; }

    private void UpdateGeneral(Func<GeneralSettings, GeneralSettings> change) => Settings.Update(s => s with { General = change(s.General) });

    private void AddGeneral()
    {
        const string page = "general";
        var kw = AppStrings.Opt_General_Kw;
        var startup = AppStrings.OptSec_Startup;
        Add(page, startup, AppStrings.Opt_SingleInstance, AppStrings.Opt_SingleInstance_Desc,
            () => Toggle(Current.General.SingleInstance, on => UpdateGeneral(g => g with { SingleInstance = on })), keywords: kw);
        Add(page, startup, AppStrings.Opt_StartFolders, AppStrings.Opt_StartFolders_Desc, StartFoldersPicker, keywords: AppStrings.Opt_StartFolders_Kw, below: StartFoldersFields);
        Add(page, startup, AppStrings.Opt_StartWithWindows, AppStrings.Opt_StartWithWindows_Desc, StartWithWindowsSwitch, keywords: AppStrings.Opt_StartWithWindows_Kw);

        Add(page, AppStrings.OptSec_Language, AppStrings.Opt_Language, AppStrings.Opt_Language_Desc,
            () => Combo([("en", AppStrings.Opt_Language_English)], "en", _ => { }, minWidth: 160), keywords: AppStrings.Opt_Language_Kw);

        var later = AppStrings.OptSec_Later;
        Add(page, later, AppStrings.Opt_KeepRunning, AppStrings.Opt_KeepRunning_Desc, () => new Panel(), keywords: AppStrings.Opt_KeepRunning_Kw, badge: AppStrings.Opt_Badge_Later);
        Add(page, later, AppStrings.Opt_ImportSalamander, AppStrings.Opt_ImportSalamander_Desc, () => new Panel(), keywords: AppStrings.Opt_ImportSalamander_Kw, badge: CommandText.Milestone("Phase 3"));
        Add(page, later, AppStrings.Opt_Updates, AppStrings.Opt_Updates_Desc, () => new Panel(), keywords: AppStrings.Opt_Updates_Kw, badge: CommandText.Milestone("Phase 3"));
    }

    private Control StartFoldersPicker() => Segmented(
        [(StartFolders.WhereYouLeft, AppStrings.Opt_StartFolders_WhereYouLeft), (StartFolders.Home, AppStrings.Opt_StartFolders_Home), (StartFolders.These, AppStrings.Opt_StartFolders_These)],
        Current.General.StartFolders, choice =>
        {
            UpdateGeneral(g =>
            {
                if (choice == StartFolders.These && g.StartLeft.Length == 0 && g.StartRight.Length == 0 && PanelFolders?.Invoke() is var (left, right))
                {
                    g = g with { StartLeft = left, StartRight = right };
                }

                return g with { StartFolders = choice };
            });
            RebuildLater();
        });

    private Control StartFoldersFields()
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), RowDefinitions = new RowDefinitions("Auto,Auto"), ColumnSpacing = 10, RowSpacing = 6 };
        grid.IsVisible = Current.General.StartFolders == StartFolders.These;
        void Field(int row, string label, string value, Func<GeneralSettings, string, GeneralSettings> set)
        {
            var name = Text(label, 13, brush: "Brush.SecondaryText");
            name.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetRow(name, row);
            var box = new TextBox { Text = value, PlaceholderText = AppStrings.Opt_StartFolders_Empty };
            Avalonia.Automation.AutomationProperties.SetName(box, label);
            box.TextChanged += (_, _) => UpdateGeneral(g => set(g, (box.Text ?? "").Trim()));
            Grid.SetRow(box, row);
            Grid.SetColumn(box, 1);
            var browse = ActionButton(AppStrings.Opt_StartFolders_Choose, () => _ = ChooseAsync());
            Grid.SetRow(browse, row);
            Grid.SetColumn(browse, 2);
            grid.Children.Add(name);
            grid.Children.Add(box);
            grid.Children.Add(browse);

            async Task ChooseAsync()
            {
                var text = (box.Text ?? "").Trim();
                var start = Directory.Exists(text) ? await StorageProvider.TryGetFolderFromPathAsync(text) : null;
                var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = label, SuggestedStartLocation = start });
                if (folders.Count > 0 && folders[0].TryGetLocalPath() is { } path)
                {
                    box.Text = path;
                }
            }
        }

        Field(0, AppStrings.Opt_StartFolders_LeftPanel, Current.General.StartLeft, (g, v) => g with { StartLeft = v });
        Field(1, AppStrings.Opt_StartFolders_RightPanel, Current.General.StartRight, (g, v) => g with { StartRight = v });
        return grid;
    }

    private Control StartWithWindowsSwitch()
    {
        var start = _services.StartWithWindows;
        var program = _services.ProgramPath;
        var note = Text("", 12, brush: "Brush.SecondaryText", wrap: true);
        note.MaxWidth = 300;
        note.TextAlignment = TextAlignment.Right;
        void ShowOther()
        {
            note.Text = StartWithWindowsState.Other(start, program) is { } other ? Loc.Format(AppStrings.Opt_StartWithWindows_Other, other) : "";
            note.IsVisible = note.Text.Length > 0;
        }

        var reverting = false;
        ToggleSwitch? toggle = null;
        toggle = Toggle(StartWithWindowsState.IsOn(start, program), on =>
        {
            if (reverting)
            {
                return;
            }

            try
            {
                if (on)
                {
                    start.Register(program);
                }
                else if (StartWithWindowsState.IsOn(start, program))
                {
                    start.Unregister();
                }

                ShowOther();
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or System.Security.SecurityException)
            {
                reverting = true;
                toggle!.IsChecked = StartWithWindowsState.IsOn(start, program);
                reverting = false;
                note.Text = Loc.Format(AppStrings.Opt_StartWithWindows_Failed, ex.Message);
                note.IsVisible = true;
            }
        });
        ShowOther();
        StartWithWindowsToggle = toggle;
        StartWithWindowsNote = note;
        toggle.HorizontalAlignment = HorizontalAlignment.Right;
        return new StackPanel { Spacing = 4, HorizontalAlignment = HorizontalAlignment.Right, Children = { toggle, note } };
    }
}

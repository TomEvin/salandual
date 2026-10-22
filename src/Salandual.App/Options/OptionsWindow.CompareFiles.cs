// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.App/Options/OptionsWindow.CompareFiles.cs

using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Salandual.App.Resources;
using Salandual.Core.Compare;
using Salandual.Core.Resources;
using Salandual.Core.Settings;
using static Salandual.App.Options.OptionsUi;

namespace Salandual.App.Options;

public sealed partial class OptionsWindow
{
    private IReadOnlyList<(CompareProgram Program, bool Found)>? _comparePrograms;

    private IReadOnlyList<(CompareProgram Program, bool Found)> ComparePrograms => _comparePrograms ??= CompareProgram.Known(
        File.Exists,
        Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
        Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));

    private void AddCompareFiles()
    {
        const string page = "compare";
        var kw = AppStrings.Opt_CompareFiles_Kw;
        var with = AppStrings.OptSec_CompareFiles;
        Add(page, with, AppStrings.Opt_CompareWith, AppStrings.Opt_CompareWith_Desc, () => Segmented<bool>(
            [(false, AppStrings.Opt_CompareWithSalandual), (true, AppStrings.Opt_CompareWithProgram)], Current.Compare.Files.UseProgram, on =>
            {
                UpdateFiles(f => f with
                {
                    UseProgram = on,

                    Program = on && f.Program.Length == 0 && ComparePrograms.FirstOrDefault(p => p.Found) is { Program: { } found } ? found.Path : f.Program,
                    ProgramArguments = on && f.Program.Length == 0 && ComparePrograms.FirstOrDefault(p => p.Found) is { Program: { } first } ? first.Arguments : f.ProgramArguments,
                });
                RebuildLater();
            }), keywords: kw);
        Add(page, with, AppStrings.Opt_CompareProgram, AppStrings.Opt_CompareProgram_Desc, () => EnabledWhile(CompareProgramPicker(), s => s.Compare.Files.UseProgram), keywords: kw);
        Add(page, with, AppStrings.Opt_CompareArguments, AppStrings.Opt_CompareArguments_Desc, () => EnabledWhile(CompareArgumentsEditor(), s => s.Compare.Files.UseProgram), keywords: kw);

        var files = AppStrings.OptSec_FileComparator;
        Add(page, files, AppStrings.FileCompare_IgnoreSpaces, AppStrings.FileCompare_IgnoreSpacesTip,
            () => Toggle(Current.Compare.Files.IgnoreWhiteSpace, on => UpdateFiles(f => f with { IgnoreWhiteSpace = on })), keywords: kw);
        Add(page, files, AppStrings.FileCompare_IgnoreCase, AppStrings.FileCompare_IgnoreCaseTip,
            () => Toggle(Current.Compare.Files.IgnoreCase, on => UpdateFiles(f => f with { IgnoreCase = on })), keywords: kw);
        Add(page, files, AppStrings.Opt_CompareCodeColors, AppStrings.Opt_CompareCodeColors_Desc,
            () => Toggle(Current.Compare.Files.CodeColors, on => UpdateFiles(f => f with { CodeColors = on })), keywords: kw);
        Add(page, files, AppStrings.Opt_CompareOnly, AppStrings.Opt_CompareOnly_Desc, CompareOnlyEditor, keywords: kw);
        Add(page, files, AppStrings.Opt_CompareColors, AppStrings.Opt_CompareColors_Desc, CompareColorsRow, keywords: kw);
    }

    private void UpdateFiles(Func<FileCompareSettings, FileCompareSettings> change) => UpdateCompare(c => c with { Files = change(c.Files) });

    private Control CompareProgramPicker()
    {
        var current = Current.Compare.Files.Program;
        var items = ComparePrograms.Select(p => (p.Program.Path, Label: p.Found ? Loc.Format(AppStrings.Opt_CompareProgramFound, p.Program.Name) : Loc.Format(AppStrings.Opt_CompareProgramNotFound, p.Program.Name))).ToList();
        if (current.Length > 0 && !items.Any(i => string.Equals(i.Path, current, StringComparison.OrdinalIgnoreCase)))
        {
            items.Add((current, current));
        }

        const string choose = "\u0001choose";
        items.Add((choose, AppStrings.Opt_CompareChoose));
        var combo = Combo(items.Select(i => (i.Path, i.Label)).ToList(), current.Length > 0 ? items.First(i => string.Equals(i.Path, current, StringComparison.OrdinalIgnoreCase)).Path : "", async path =>
        {
            if (path == choose)
            {
                await ChooseCompareProgramAsync();
                return;
            }

            var known = ComparePrograms.FirstOrDefault(p => p.Program.Path == path).Program;
            UpdateFiles(f => f with { Program = path, ProgramArguments = known?.Arguments ?? f.ProgramArguments });
            RebuildLater();
        }, minWidth: 300);
        Avalonia.Automation.AutomationProperties.SetName(combo, AppStrings.Opt_CompareProgram);
        ToolTip.SetTip(combo, current.Length > 0 ? current : null);
        return combo;
    }

    private async Task ChooseCompareProgramAsync()
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = AppStrings.Opt_CompareChoose,
            AllowMultiple = false,
            FileTypeFilter = [new FilePickerFileType(AppStrings.Opt_ComparePrograms) { Patterns = ["*.exe"] }],
        });
        if (files.Count > 0 && files[0].TryGetLocalPath() is { } path && !CompareProgram.IsShell(path))
        {
            UpdateFiles(f => f with { Program = path, ProgramArguments = CompareProgram.DefaultArguments });
        }

        RebuildLater();
    }

    private Control CompareArgumentsEditor()
    {
        var box = new TextBox { Text = Current.Compare.Files.ProgramArguments, Width = 360, FontSize = 12.5 };
        box.Res(TextBox.FontFamilyProperty, "Font.Mono");
        Avalonia.Automation.AutomationProperties.SetName(box, AppStrings.Opt_CompareArguments);
        box.TextChanged += (_, _) => UpdateFiles(f => f with { ProgramArguments = box.Text ?? "" });
        var variables = new Button { Content = "$( ) ▾", Padding = new Thickness(8, 4) };
        ToolTip.SetTip(variables, AppStrings.Opt_CompareVariables);
        var menu = new MenuFlyout();
        foreach (var (variable, label) in CompareProgram.Variables.Zip([AppStrings.Opt_CompareVarLeft, AppStrings.Opt_CompareVarRight, AppStrings.Opt_CompareVarLeftName, AppStrings.Opt_CompareVarRightName]))
        {
            var item = new MenuItem { Header = variable + "   " + label };
            item.Click += (_, _) =>
            {
                var text = box.Text ?? "";
                var at = Math.Clamp(box.CaretIndex, 0, text.Length);
                box.Text = text.Insert(at, "\"" + variable + "\"");
                box.CaretIndex = at + variable.Length + 2;
                box.Focus();
            };
            menu.Items.Add(item);
        }

        variables.Flyout = menu;
        return new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { box, variables } };
    }

    private Control CompareOnlyEditor()
    {
        var on = Toggle(Current.Compare.Files.OnlyDifferences, value => UpdateFiles(f => f with { OnlyDifferences = value }));
        var box = new TextBox { Text = Current.Compare.Files.Context.ToString(CultureInfo.CurrentCulture), Width = 56, HorizontalContentAlignment = HorizontalAlignment.Right };
        Avalonia.Automation.AutomationProperties.SetName(box, AppStrings.Opt_CompareContext);
        box.LostFocus += (_, _) =>
        {
            if (int.TryParse(box.Text, NumberStyles.Integer, CultureInfo.CurrentCulture, out var lines) && lines >= 0)
            {
                UpdateFiles(f => f with { Context = Math.Min(lines, FileCompareSettings.MaxContext) });
            }

            box.Text = Current.Compare.Files.Context.ToString(CultureInfo.CurrentCulture);
        };
        var label = Text(AppStrings.Opt_CompareContext, 13).Also(t => t.VerticalAlignment = VerticalAlignment.Center);
        return new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, Children = { on, label, box } };
    }

    private Control CompareColorsRow()
    {
        var colors = ResolveTheme(OnScreenTheme);
        var row = new WrapPanel { Orientation = Orientation.Horizontal, ItemSpacing = 14, LineSpacing = 6, VerticalAlignment = VerticalAlignment.Center };
        foreach (var (token, label) in ColorTokens.Compare)
        {
            if (colors.TryGetValue(token, out var color))
            {
                var text = label.Contains(':', StringComparison.Ordinal) ? label[(label.IndexOf(':', StringComparison.Ordinal) + 1)..].Trim() : label;
                text = text.Length > 0 ? char.ToUpper(text[0], CultureInfo.CurrentCulture) + text[1..] : text;
                row.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Children = { Swatch(color, 16), Text(text, 12.5).Also(t => t.VerticalAlignment = VerticalAlignment.Center) } });
            }
        }

        var change = new Button { Content = AppStrings.Opt_CompareColorsChange, Padding = new Thickness(0), Background = Brushes.Transparent, BorderThickness = default, Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand) };
        change.Res(Button.ForegroundProperty, "Brush.Accent");
        change.Click += (_, _) =>
        {
            _themesTab = "editor";
            ShowCategory(Themes);
        };
        return new StackPanel { Spacing = 8, Children = { row, change } };
    }
}

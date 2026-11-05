// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.App/Options/LanguageProblemsWindow.cs

using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Salandual.App.Controls;
using Salandual.App.Resources;
using Salandual.Core.Resources;

namespace Salandual.App.Options;

public sealed class LanguageProblemsWindow : Window
{
    private readonly LanguageChoice _choice;
    private readonly IClipboardAccess? _clipboard;
    private readonly ListBox _list;
    private readonly IReadOnlyList<LanguageProblem> _problems;

    internal LanguageProblemsWindow(LanguageChoice choice, IClipboardAccess? clipboard)
    {
        _choice = choice;
        _clipboard = clipboard;
        var missing = choice.Problems.Count > 0 && choice.Problems.All(p => p.Kind == LanguageProblemKind.NotTranslated);
        Title = Loc.Format(missing ? AppStrings.LangProblems_MissingTitle : AppStrings.LangProblems_Title, choice.FileName);
        Width = 900;
        Height = 480;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        this.Res(BackgroundProperty, "Brush.PanelBackground");

        _problems = [.. choice.Problems.OrderBy(p => p.Kind == LanguageProblemKind.Unknown).ThenBy(p => p.Key, StringComparer.Ordinal)];
        Rows = [.. _problems.Select(p => (p.Key, What(p), ShownInstead(p), p.Kind is LanguageProblemKind.Unknown or LanguageProblemKind.NotTranslated))];
        _list = new ListBox { ItemsSource = Rows.Select(Row).ToList(), Background = Brushes.Transparent };
        if (Rows.Count > 0)
        {
            _list.SelectedIndex = 0;
        }

        var head = Line(AppStrings.LangProblems_TextHead, AppStrings.LangProblems_WhatHead, AppStrings.LangProblems_InsteadHead, "Brush.TertiaryText", "Brush.TertiaryText");
        head.Margin = new Thickness(12, 6, 12, 2);
        var unknown = choice.Problems.Count(p => p.Kind == LanguageProblemKind.Unknown);
        var summary = OptionsUi.Text(missing ? Loc.Plural(choice.Problems.Count, AppStrings.Language_NotTranslated_One, AppStrings.Language_NotTranslated_Other) : Loc.Format(AppStrings.LangProblems_Summary, choice.Problems.Count, choice.Broken, unknown), 12, brush: "Brush.SecondaryText");
        var keys = OptionsUi.Text(missing ? AppStrings.LangProblems_KeysMissing : AppStrings.LangProblems_Keys, 11, brush: "Brush.TertiaryText", wrap: true);
        var foot = new StackPanel { Spacing = 2, Margin = new Thickness(12, 6), Children = { summary, keys } };
        var root = new DockPanel();
        DockPanel.SetDock(head, Dock.Top);
        DockPanel.SetDock(foot, Dock.Bottom);
        root.Children.Add(head);
        root.Children.Add(foot);
        root.Children.Add(_list);
        Content = TitleBarParts.ForDialog(this, root);
        Opened += (_, _) => _list.Focus();
    }

    internal Action<string, int>? EditFileAt { get; set; }

    internal IReadOnlyList<(string Key, string What, string Instead, bool Warning)> Rows { get; }

    internal ListBox List => _list;

    internal static string What(LanguageProblem p) => p.Kind switch
    {
        LanguageProblemKind.Missing => Loc.Format(AppStrings.LangProblem_Missing, p.Placeholders, Placeholders(p.English)),
        LanguageProblemKind.NotInEnglish => Loc.Format(AppStrings.LangProblem_NotInEnglish, p.Placeholders, Placeholders(p.English)),
        LanguageProblemKind.Empty => AppStrings.LangProblem_Empty,
        LanguageProblemKind.Braces => AppStrings.LangProblem_Braces,
        LanguageProblemKind.NotText => AppStrings.LangProblem_NotText,
        LanguageProblemKind.NotTranslated => AppStrings.LangProblem_NotTranslated,
        _ => AppStrings.LangProblem_Unknown,
    };

    private static string Placeholders(string? english)
    {
        var numbers = english is null ? [] : LanguageFiles.Placeholders(english).Order().ToList();
        return numbers.Count == 0 ? AppStrings.LangProblem_NoPlaceholders : string.Join(", ", numbers.Select(n => "{" + n.ToString(CultureInfo.InvariantCulture) + "}"));
    }

    private string ShownInstead(LanguageProblem p)
    {
        if (p.Kind == LanguageProblemKind.Unknown)
        {
            return AppStrings.LangProblems_Nothing;
        }

        return _choice.Texts.TryGetValue(p.Key, out var text) ? text : p.English ?? "";
    }

    private static Control Row((string Key, string What, string Instead, bool Warning) r)
        => Line(r.Key, r.What, r.Instead, "Brush.Text", r.Warning ? "Brush.Warning" : "Brush.Danger");

    private static Grid Line(string key, string what, string instead, string keyBrush, string whatBrush)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("220,*,*"), ColumnSpacing = 12 };
        var k = OptionsUi.Text(key, 12, brush: keyBrush);
        k.FontFamily = new FontFamily("Cascadia Mono, Consolas");
        k.TextTrimming = TextTrimming.CharacterEllipsis;
        var w = OptionsUi.Text(what, 12, brush: whatBrush, wrap: true);
        var i = OptionsUi.Text(instead, 12, wrap: true);
        Grid.SetColumn(w, 1);
        Grid.SetColumn(i, 2);
        grid.Children.Add(k);
        grid.Children.Add(w);
        grid.Children.Add(i);
        return grid;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Close();
            e.Handled = true;
        }
        else if (e.Key == Key.Enter && _list.SelectedIndex >= 0 && _problems[_list.SelectedIndex].Path is { } path && EditFileAt is { } edit)
        {
            edit(path, LineOf(path, Rows[_list.SelectedIndex].Key));
            Close();
            e.Handled = true;
        }
        else if (e.Key == Key.C && e.KeyModifiers == KeyModifiers.Control && _clipboard is not null)
        {
            _clipboard.SetText(CopyText());
            e.Handled = true;
        }

        base.OnKeyDown(e);
    }

    internal string CopyText() => string.Join(Environment.NewLine, Rows.Select(r => r.Key + "\t" + r.What + "\t" + r.Instead));

    internal static int LineOf(string path, string key)
    {
        try
        {
            var quoted = "\"" + key + "\"";
            var n = 0;
            foreach (var line in File.ReadLines(path))
            {
                n++;
                if (line.Contains(quoted, StringComparison.Ordinal))
                {
                    return n;
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }

        return 1;
    }
}

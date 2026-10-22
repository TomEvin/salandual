// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.App/Find/FindWindow.Saved.cs

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Salandual.App.Dialogs;
using Salandual.App.Options;
using Salandual.App.Resources;
using Salandual.App.Theme;
using Salandual.Core.Resources;
using Salandual.Core.Settings;

namespace Salandual.App.Find;

internal sealed partial class FindWindow
{
    private readonly TextBlock _savedText = new() { Text = AppStrings.Find_Saved, VerticalAlignment = VerticalAlignment.Center };

    internal Button? SavedButton { get; private set; }

    internal SaveSearchDialog? SaveDialog { get; private set; }

    private Button BuildSavedButton()
    {
        var caret = new TextBlock { Text = "▾", FontSize = 10, VerticalAlignment = VerticalAlignment.Center };
        caret.Res(TextBlock.ForegroundProperty, "Brush.TertiaryText");
        var button = new Button
        {
            Content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Children = { AppIcons.Create("Icon.Bookmark")!, _savedText, caret } },
            Padding = new Thickness(10, 5),
            VerticalAlignment = VerticalAlignment.Stretch,
        };
        ToolTip.SetTip(button, AppStrings.Find_SavedTip);
        Avalonia.Automation.AutomationProperties.SetName(button, AppStrings.Find_SavedTip);
        var flyout = new MenuFlyout { Placement = PlacementMode.BottomEdgeAlignedRight, FlyoutPresenterClasses = { "wide" } };
        FillSavedMenu(flyout);
        flyout.Opening += (_, _) => FillSavedMenu(flyout);
        button.Flyout = flyout;

        SizeChanged += (_, e) => _savedText.IsVisible = e.NewSize.Width >= 640;
        return SavedButton = button;
    }

    private void FillSavedMenu(MenuFlyout flyout)
    {
        flyout.Items.Clear();
        flyout.Items.Add(new MenuItem { Header = AppStrings.Find_SavedHead, IsEnabled = false });
        var saved = _settings.Current.Find.Saved;
        if (saved.Count == 0)
        {
            flyout.Items.Add(new MenuItem { Header = new TextBlock { Text = AppStrings.Find_SavedNone, FontStyle = FontStyle.Italic }, IsEnabled = false });
        }

        for (var i = 0; i < saved.Count; i++)
        {
            flyout.Items.Add(SavedItem(flyout, saved[i], i));
        }

        flyout.Items.Add(new Separator());
        var save = new MenuItem { Header = AppStrings.Find_SaveThis, InputGesture = new KeyGesture(Key.D, KeyModifiers.Control) };
        save.Click += (_, _) => _ = SaveSearchAsync();
        flyout.Items.Add(save);
        var manage = new MenuItem { Header = AppStrings.Find_SavedManage };
        manage.Click += (_, _) => _request.OpenOptions("compare");
        flyout.Items.Add(manage);
    }

    private MenuItem SavedItem(MenuFlyout flyout, SavedSearch search, int index)
    {
        var name = new TextBlock { Text = search.Name, VerticalAlignment = VerticalAlignment.Center };
        var what = new TextBlock { Text = SavedText(search.Search), FontSize = 11.5, MaxWidth = 320, TextTrimming = TextTrimming.CharacterEllipsis };
        ToolTip.SetTip(what, what.Text);
        what.Res(TextBlock.ForegroundProperty, "Brush.TertiaryText");
        var rename = Small("✎", AppStrings.Find_SavedRename);
        rename.Click += (_, _) =>
        {
            flyout.Hide();
            _ = RenameSavedAsync(search.Name);
        };
        var remove = Small("✕", AppStrings.Find_SavedRemove);
        remove.Click += (_, _) =>
        {
            flyout.Hide();
            RemoveSaved(search.Name);
        };
        var header = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto"), ColumnSpacing = 2, MinWidth = 280 };
        header.Children.Add(new StackPanel { Children = { name, what } });
        Grid.SetColumn(rename, 1);
        Grid.SetColumn(remove, 2);
        header.Children.Add(rename);
        header.Children.Add(remove);
        var item = new MenuItem { Header = header, Tag = search.Name };
        if (index < 9)
        {
            item.InputGesture = new KeyGesture(Key.D1 + index, KeyModifiers.Control);
        }

        item.Click += (_, _) => ApplySaved(search.Name);
        return item;

        static Button Small(string text, string tip)
        {
            var button = new Button { Content = text, Padding = new Thickness(6, 2), FontSize = 12, VerticalAlignment = VerticalAlignment.Center, Focusable = false };
            button.Classes.Add("ftool");
            button.Classes.Add("saved-ed");
            ToolTip.SetTip(button, tip);
            Avalonia.Automation.AutomationProperties.SetName(button, tip);
            return button;
        }
    }

    internal static string SavedText(SearchDefaults d) => OptionsWindow.SearchDefaultsText(d, brief: true);

    internal bool ApplySaved(string name)
    {
        if (IsSearching || _settings.Current.Find.Saved.FirstOrDefault(s => string.Equals(s.Name, name, StringComparison.CurrentCultureIgnoreCase)) is not { } saved)
        {
            return false;
        }

        ApplyDefaults(saved.Search);
        _named.Focus();
        return true;
    }

    private bool ApplySaved(int index)
        => index < _settings.Current.Find.Saved.Count && ApplySaved(_settings.Current.Find.Saved[index].Name);

    internal async Task SaveSearchAsync()
    {
        if (SaveDialog is not null || (AdvancedProblem() ?? TextsProblemNow()) is { } problem && ShowProblem(problem))
        {
            return;
        }

        var search = ReadDefaults();
        var first = search.Texts.FirstOrDefault(t => t.Contains)?.Text;
        var named = search.Named.Length > 0 ? search.Named : "*.*";
        var suggested = first is { Length: > 0 } ? Loc.Format(AppStrings.Find_SavedNameWith, named, first) : named;
        var dialog = SaveDialog = new SaveSearchDialog(suggested, search.LookIn, SavedText(search with { LookIn = search.LookIn }));
        string? name;
        try
        {
            name = await dialog.ShowDialog<string?>(this);
        }
        finally
        {
            SaveDialog = null;
        }

        if (name is null || !await MayReplaceAsync(name, except: null))
        {
            return;
        }

        var kept = dialog.KeepsFolders ? search : search with { LookIn = "" };
        _settings.Update(s => s with
        {
            Find = s.Find with
            {
                Saved = Replace(s.Find.Saved, name, new SavedSearch(name, kept)),
            },
        });
    }

    private async Task RenameSavedAsync(string old)
    {
        if (SaveDialog is not null)
        {
            return;
        }

        var dialog = SaveDialog = new SaveSearchDialog(old);
        string? name;
        try
        {
            name = await dialog.ShowDialog<string?>(this);
        }
        finally
        {
            SaveDialog = null;
        }

        if (name is null || name == old || !await MayReplaceAsync(name, except: old))
        {
            return;
        }

        RenameSaved(_settings, old, name);
    }

    private async Task<bool> MayReplaceAsync(string name, string? except)
    {
        var taken = _settings.Current.Find.Saved.Any(s => string.Equals(s.Name, name, StringComparison.CurrentCultureIgnoreCase)
            && !string.Equals(s.Name, except, StringComparison.CurrentCultureIgnoreCase));
        if (!taken)
        {
            return true;
        }

        var ask = new ConfirmDialog(Loc.Format(AppStrings.Find_SavedReplaceTitle, name), AppStrings.Find_SavedReplaceText, AppStrings.Find_SavedReplace, AppStrings.Common_Cancel);
        return await ask.ShowDialog<bool>(this);
    }

    private void RemoveSaved(string name) => RemoveSaved(_settings, name);

    internal static List<SavedSearch> Replace(IReadOnlyList<SavedSearch> list, string name, SavedSearch search)
    {
        var result = list.ToList();
        var i = result.FindIndex(s => string.Equals(s.Name, name, StringComparison.CurrentCultureIgnoreCase));
        if (i >= 0)
        {
            result[i] = search;
        }
        else
        {
            result.Add(search);
        }

        return result;
    }

    internal static void RenameSaved(SettingsService settings, string old, string name)
        => settings.Update(s => s with
        {
            Find = s.Find with
            {
                Saved = [.. s.Find.Saved
                    .Where(x => !string.Equals(x.Name, name, StringComparison.CurrentCultureIgnoreCase) || string.Equals(x.Name, old, StringComparison.CurrentCultureIgnoreCase))
                    .Select(x => string.Equals(x.Name, old, StringComparison.CurrentCultureIgnoreCase) ? x with { Name = name } : x)],
            },
        });

    internal static void RemoveSaved(SettingsService settings, string name)
        => settings.Update(s => s with { Find = s.Find with { Saved = [.. s.Find.Saved.Where(x => !string.Equals(x.Name, name, StringComparison.CurrentCultureIgnoreCase))] } });

    private string? TextsProblemNow()
        => TextsProblem(new Salandual.Core.Search.SearchCriteria { Texts = [.. _rows.Select(r => r.Read())], MatchCase = _matchCase.IsChecked == true, WholeWords = _wholeWords.IsChecked == true, Encodings = _encodings });

    private bool ShowProblem(string problem)
    {
        _problem.Text = problem;
        _problem.IsVisible = true;
        return true;
    }
}

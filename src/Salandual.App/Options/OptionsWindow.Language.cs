// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.App/Options/OptionsWindow.Language.cs

using System.Globalization;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Salandual.App.Resources;
using Salandual.App.Theme;
using Salandual.Core;
using Salandual.Core.Listing;
using Salandual.Core.Resources;
using Salandual.Core.Settings;
using static Salandual.App.Options.OptionsUi;

namespace Salandual.App.Options;

public partial class OptionsWindow
{
    internal Action? RestartNow { get; set; }

    internal Action<string>? OpenFolderInPanel { get; set; }

    internal Action<string, int>? EditFileAt { get; set; }

    internal Action<Uri>? OpenWeb { get; set; }

    internal StackPanel? LanguageHeaderBlock { get; private set; }

    internal TextBlock? LanguageMissingLine { get; private set; }

    internal Border? LanguageRestartBar { get; private set; }

    internal TextBlock? LanguageProblemsLine { get; private set; }

    private string? _exported;

    private bool _languageLater;

    internal static IReadOnlyList<(string Id, string Label)> LanguageChoices(IReadOnlyList<string> folders, string current)
    {
        var (files, odd) = LanguageFiles.List(folders);
        var choices = new List<(string, string)> { (GeneralSettings.English, AppStrings.Opt_Language_English) };
        static string Id(LanguageFile f) => f.NewerFormat is null ? f.Id : Greyed + f.Id;
        string Changes(LanguageFile f, string language) => f.NewerFormat is not null ? Loc.Format(AppStrings.Opt_Language_Newer, f.FileName)
            : f.Unreadable is null ? Loc.Format(AppStrings.Opt_Language_Changes, language, f.Changes) : Loc.Format(AppStrings.Opt_Language_Unreadable, f.FileName);
        choices.AddRange(files.Where(f => f.Changes is not null && f.Code == GeneralSettings.English).Select(f => (Id(f), Changes(f, AppStrings.Opt_Language_English))));
        foreach (var language in files.Where(f => f.Changes is null))
        {
            choices.Add((Id(language), language.NewerFormat is not null ? Loc.Format(AppStrings.Opt_Language_Newer, language.FileName)
                : language.Unreadable is null ? language.Name : Loc.Format(AppStrings.Opt_Language_Unreadable, language.FileName)));
            choices.AddRange(files.Where(f => f.Changes is not null && f.Code == language.Code).Select(f => (Id(f), Changes(f, language.Name))));
        }

        foreach (var orphan in files.Where(f => f.Changes is not null && f.Code != GeneralSettings.English && !files.Any(l => l.Changes is null && l.Code == f.Code)))
        {
            choices.Add((Greyed + orphan.Id, Loc.Format(AppStrings.Opt_Language_NeedsBase, orphan.FileName, orphan.Code + ".json")));
        }

        choices.AddRange(odd.Select(name => (Greyed + name, Loc.Format(AppStrings.Opt_Language_BadName, name))));
        if (!choices.Any(c => c.Item1 == current))
        {
            choices.Add((current, Loc.Format(AppStrings.Opt_Language_Gone, current.EndsWith(".json", StringComparison.OrdinalIgnoreCase) ? current : current + ".json")));
        }

        return choices;
    }

    private const string Greyed = "?";

    internal static ComboBox LanguageCombo(IReadOnlyList<string> folders, string current, Action<string> changed)
        => Combo(LanguageChoices(folders, current), current, changed, minWidth: 220, enabled: id => !id.StartsWith(Greyed, StringComparison.Ordinal));

    internal static string? LanguageTrouble(LanguageChoice c, bool warnings)
    {
        var language = c.Language?.Name ?? AppStrings.Opt_Language_English;
        var changesMissing = c.Missing is { } m && m == c.Id + ".json" && c.Id.Contains('.', StringComparison.Ordinal) && LanguageFiles.TryParseId(c.Id, out _, out _);
        return c.Id == GeneralSettings.English ? null
            : c.Missing is { } missing && !changesMissing ? Loc.Format(AppStrings.Opt_Language_NotFound, missing)
            : c.Language?.Unreadable is { } why ? Loc.Format(AppStrings.Opt_Language_CantRead, c.Language.FileName, why)
            : changesMissing ? Loc.Format(AppStrings.Opt_Language_ChangesNotFound, c.Missing, language)
            : c.Changes?.Unreadable is { } why2 ? Loc.Format(AppStrings.Opt_Language_ChangesCantRead, c.Changes.FileName, why2, language)
            : c.Broken > 0 || (warnings && c.Problems.Count > 0) ? ProblemsSummary(c)
            : null;
    }

    private string LanguageLabel(string id) => LanguageChoices(_services.LanguageFolders, id).FirstOrDefault(c => c.Id == id).Label ?? id;

    private void AddLanguage(string page)
    {
        var section = AppStrings.OptSec_Language;
        Add(page, section, AppStrings.Opt_Language, AppStrings.Opt_Language_Desc,
            () => LanguageCombo(_services.LanguageFolders, Current.General.Language, id =>
            {
                UpdateGeneral(g => g with { Language = id });
                _languageLater = false;
                RebuildLater();
            }), keywords: AppStrings.Opt_Language_Kw, below: LanguageBelow);
        Add(page, section, AppStrings.Opt_LanguageFiles, AppStrings.Opt_LanguageFiles_Desc, LanguageFileButtons, keywords: AppStrings.Opt_Language_Kw, below: LanguageFilesBelow);
    }

    private Control LanguageBelow()
    {
        var stack = new StackPanel { Spacing = 6 };
        var chosen = Current.General.Language;
        var running = _services.Language;
        var bar = new Border
        {
            Background = Brush("Brush.InfoBackground"),
            BorderBrush = Brush("Brush.Accent"),
            BorderThickness = new Avalonia.Thickness(1),
            CornerRadius = Corners.Chip,
            Padding = new Avalonia.Thickness(10, 6),
            IsVisible = chosen != running.Id && !_languageLater,
        };
        var row = new DockPanel { LastChildFill = true };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        if (RestartNow is { } restart)
        {
            var now = ActionButton(AppStrings.Opt_Language_RestartNow, restart);
            now.Classes.Add("accent");
            buttons.Children.Add(now);
        }

        buttons.Children.Add(ActionButton(AppStrings.Opt_Language_Later, () =>
        {
            _languageLater = true;
            bar.IsVisible = false;
        }));
        DockPanel.SetDock(buttons, Dock.Right);
        row.Children.Add(buttons);
        var say = Text(Loc.Format(AppStrings.Opt_Language_Restart, LanguageLabel(chosen)), 13, wrap: true);
        say.VerticalAlignment = VerticalAlignment.Center;
        row.Children.Add(say);
        bar.Child = row;
        LanguageRestartBar = bar;
        stack.Children.Add(bar);
        LanguageHeaderBlock = LanguageHeader(chosen);
        if (LanguageHeaderBlock is not null)
        {
            stack.Children.Add(LanguageHeaderBlock);
        }

        LanguageProblemsLine = null;
        if (chosen == running.Id)
        {
            if (LanguageTrouble(running, warnings: true) is { } text)
            {
                var line = Text(text, 12, brush: "Brush.Warning", wrap: true);
                LanguageProblemsLine = line;
                var problems = new DockPanel();
                if (running.Problems.Count > 0)
                {
                    var show = ActionButton(AppStrings.Opt_Language_Show, () => ShowLanguageProblems(running));
                    DockPanel.SetDock(show, Dock.Right);
                    problems.Children.Add(show);
                }

                problems.Children.Add(line);
                stack.Children.Add(problems);
            }
        }

        return stack;
    }

    private StackPanel? LanguageHeader(string chosen)
    {
        LanguageMissingLine = null;
        if (chosen == GeneralSettings.English || chosen.StartsWith(Greyed, StringComparison.Ordinal))
        {
            return null;
        }

        var choice = chosen == _services.Language.Id ? _services.Language : TextLanguage.Choose(chosen, _services.LanguageFolders, AppServices.Originals);
        var files = new[] { choice.Language, choice.Changes }.OfType<LanguageFile>()
            .Where(f => f.Unreadable is null && f.Header is { } h && new[] { h.Author, h.ForVersion, h.Updated, h.Contact, h.Copyright, h.License }.Any(v => v.Length > 0)).ToList();
        var block = new StackPanel { Spacing = 2 };
        var dates = DisplayFormat.From(Current.Panels, CultureInfo.CurrentCulture);
        foreach (var file in files)
        {
            var h = file.Header!;
            var first = new WrapPanel { ItemSpacing = 16 };
            if (files.Count > 1)
            {
                first.Children.Add(Text(file.FileName, 12, FontWeight.SemiBold, brush: "Brush.SecondaryText"));
            }

            Part(first, AppStrings.Opt_Language_Author, h.Author);
            Part(first, AppStrings.Opt_Language_Version, h.ForVersion);
            Part(first, AppStrings.Opt_Language_Updated, Core.Resources.LanguageHeader.ReadDate(h.Updated) is { } day ? dates.Date(day) : h.Updated);
            if (first.Children.Count > (files.Count > 1 ? 1 : 0) || files.Count > 1)
            {
                block.Children.Add(first);
            }

            var second = new WrapPanel { ItemSpacing = 6 };
            if (h.Contact.Length > 0)
            {
                second.Children.Add(Text(AppStrings.Opt_Language_Contact, 12, brush: "Brush.SecondaryText"));
                second.Children.Add(Contact(h.Contact));
            }

            foreach (var value in new[] { h.Copyright, h.License }.Where(v => v.Length > 0))
            {
                if (second.Children.Count > 0)
                {
                    second.Children.Add(Text("·", 12, brush: "Brush.TertiaryText"));
                }

                second.Children.Add(Text(value, 12, brush: "Brush.SecondaryText"));
            }

            if (second.Children.Count > 0)
            {
                block.Children.Add(second);
            }
        }

        if (choice.Language is { } language)
        {
            var missing = LanguageExportWindow.Entries()
                .Where(e => !language.Texts.ContainsKey(e.Key) && choice.Changes?.Texts.ContainsKey(e.Key) != true)
                .Select(e => new LanguageProblem(e.Key, LanguageProblemKind.NotTranslated, English: e.English))
                .ToList();
            if (missing.Count > 0)
            {
                var line = Text(Loc.Plural(missing.Count, AppStrings.Language_NotTranslated_One, AppStrings.Language_NotTranslated_Other), 12, brush: "Brush.Warning", wrap: true);
                line.VerticalAlignment = VerticalAlignment.Center;
                LanguageMissingLine = line;
                var row = new DockPanel { Margin = new Avalonia.Thickness(0, 4, 0, 0) };
                var show = ActionButton(AppStrings.Opt_Language_Show, () => ShowLanguageProblems(new LanguageChoice(choice.Id, new Dictionary<string, string>(), language, null, missing)));
                DockPanel.SetDock(show, Dock.Right);
                row.Children.Add(show);
                row.Children.Add(line);
                block.Children.Add(row);
            }
        }

        return block.Children.Count > 0 ? block : null;
    }

    private static void Part(WrapPanel row, string format, string value)
    {
        if (value.Length > 0)
        {
            row.Children.Add(Text(Loc.Format(format, value), 12, brush: "Brush.SecondaryText"));
        }
    }

    private Control Contact(string contact)
    {
        if (Uri.TryCreate(contact, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp))
        {
            var link = Operations.Ui.Link(contact, () => OpenWeb?.Invoke(uri));
            ToolTip.SetTip(link, AppStrings.Torrent_LinkTip);
            return link;
        }

        var text = Text(contact, 12, brush: "Brush.SecondaryText");
        if (!EmailLike().IsMatch(contact) || TextClipboard is not { } clipboard)
        {
            return text;
        }

        var copy = Operations.Ui.Link(AppStrings.Common_Copy, () => clipboard.SetText(contact));
        return new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Children = { text, copy } };
    }

    [System.Text.RegularExpressions.GeneratedRegex(@"^[^@\s:/]+@[^@\s:/]+\.[^@\s:/]+$")]
    private static partial System.Text.RegularExpressions.Regex EmailLike();

    internal static string ProblemsSummary(LanguageChoice choice)
    {
        var name = choice.FileName;
        return choice.Broken > 0 ? Loc.Plural(choice.Broken, AppStrings.Language_Broken_One, AppStrings.Language_Broken_Other, name)
            : Loc.Plural(choice.Problems.Count, AppStrings.Language_Unknown_One, AppStrings.Language_Unknown_Other, name);
    }

    private void ShowLanguageProblems(LanguageChoice choice)
    {
        var window = new LanguageProblemsWindow(choice, TextClipboard) { EditFileAt = EditFileAt };
        _ = window.ShowDialog(this);
    }

    private Control LanguageFileButtons()
    {
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right };
        buttons.Children.Add(ActionButton(AppStrings.Opt_LanguageFiles_Export, () => _ = ExportLanguageAsync()));
        buttons.Children.Add(ActionButton(AppStrings.Opt_LanguageFiles_Open, OpenLanguagesFolder));
        return buttons;
    }

    private Control LanguageFilesBelow()
    {
        var said = Secondary(_exported is null ? "" : Loc.Format(AppStrings.Opt_LanguageFiles_Written, Path.GetFileName(_exported)));
        said.IsVisible = _exported is not null;
        return said;
    }

    private void OpenLanguagesFolder()
    {
        var folder = _services.OwnLanguageFolder;
        Directory.CreateDirectory(folder);
        if (OpenFolderInPanel is { } open)
        {
            open(folder);
            Close();
        }
    }

    internal LanguageExportWindow? OpenExport { get; private set; }

    private async Task ExportLanguageAsync()
    {
        var dialog = new LanguageExportWindow(_services.OwnLanguageFolder, _services.LanguageFolders);
        OpenExport = dialog;
        try
        {
            if (await dialog.ShowDialog<string?>(this) is { } written)
            {
                _exported = written;
                RebuildLater();
            }
        }
        finally
        {
            OpenExport = null;
        }
    }

    private static IBrush? Brush(string key) => Avalonia.Application.Current?.FindResource(key) as IBrush;
}

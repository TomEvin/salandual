// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : tests/Salandual.App.Tests/LanguageTests.cs

using System.Globalization;
using System.Text.Json;
using System.Xml.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Salandual.App.Options;
using Salandual.App.Resources;
using Salandual.Core.Commands;
using Salandual.Core.Listing;
using Salandual.Core.Resources;
using Salandual.Core.Settings;

namespace Salandual.App.Tests;

public sealed class LanguageTests : IDisposable
{
    private static App CurrentApp => (App)Application.Current!;

    private static string Folder => CurrentApp.Services.OwnLanguageFolder;

    private static IReadOnlyList<string> Folders => CurrentApp.Services.LanguageFolders;

    public LanguageTests() => CurrentApp.Services.Settings.Update(_ => new AppSettings());

    public void Dispose()
    {
        AppServices.SwitchLanguage(LanguageChoice.English);
        CurrentApp.Services.Settings.Update(_ => new AppSettings());
        if (Directory.Exists(Folder) && Folders.Count > 1)
        {
            foreach (var file in Directory.GetFiles(Folder, "*.json").Where(f => Path.GetFileName(f).Contains("test", StringComparison.Ordinal) || Path.GetFileName(f) is "fi.json" or "hu.json"))
            {
                File.Delete(file);
            }
        }
    }

    private static string WriteFile(string name, object content)
    {
        Directory.CreateDirectory(Folder);
        var path = Path.Combine(Folder, name);
        File.WriteAllText(path, JsonSerializer.Serialize(content));
        return path;
    }

    [AvaloniaFact]
    public void Magyar_is_hu_json_beside_the_program_and_English_takes_it_away_again()
    {
        Assert.True(File.Exists(Path.Combine(AppContext.BaseDirectory, LanguageFiles.FolderName, "hu.json")));
        var hu = TextLanguage.Choose("hu", Folders, AppServices.Originals);
        Assert.Equal("Magyar", hu.Language?.Name);
        AppServices.SwitchLanguage(hu);
        Assert.Equal("Másolás", Strings.Command_File_Copy);
        Assert.Equal("hu-HU", Loc.TextCulture.Name);

        AppServices.SwitchLanguage(LanguageChoice.English);
        Assert.Equal("Copy", Strings.Command_File_Copy);
        Assert.Equal("en-GB", Loc.TextCulture.Name);
    }

    [AvaloniaFact]
    public void English_stays_English_on_a_Hungarian_Windows()
    {
        var before = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("hu-HU");
            AppServices.SwitchLanguage(LanguageChoice.English);
            Assert.Equal("Copy", Strings.Command_File_Copy);
            Assert.Equal("Copy", Strings.ResourceManager.GetString(nameof(Strings.Command_File_Copy), CultureInfo.GetCultureInfo("hu-HU")));
        }
        finally
        {
            CultureInfo.CurrentUICulture = before;
        }
    }

    [AvaloniaFact]
    public void The_naming_rule_a_language_code_then_your_changes_name()
    {
        Assert.True(LanguageFiles.TryParseId("hu", out var code, out var changes));
        Assert.Equal(("hu", null), (code, changes));
        Assert.True(LanguageFiles.TryParseId("hu.my-words", out code, out changes));
        Assert.Equal(("hu", "my-words"), (code, changes));
        Assert.True(LanguageFiles.TryParseId("pt-BR", out code, out _));
        Assert.Equal("pt-BR", code);
        Assert.True(LanguageFiles.TryParseId("en.my-words", out _, out _));
        Assert.False(LanguageFiles.TryParseId("en", out _, out _));
        Assert.False(LanguageFiles.TryParseId("my-words", out _, out _));
        Assert.False(LanguageFiles.TryParseId("hu.my words", out _, out _));
        Assert.False(LanguageFiles.TryParseId("hu.a.b", out _, out _));
        Assert.False(LanguageFiles.TryParseId("hu." + new string('a', 41), out _, out _));

        Assert.True(LanguageFiles.IsLanguageFileName("hu.json"));
        Assert.True(LanguageFiles.IsLanguageFileName("hu.office.json"));
        Assert.False(LanguageFiles.IsLanguageFileName(@"..\settings.json"));
        Assert.False(LanguageFiles.IsLanguageFileName("C:/x.json"));
        Assert.False(LanguageFiles.IsLanguageFileName("hu.txt"));
        Assert.Null(TextLanguage.Choose(@"..\settings", Folders, AppServices.Originals).Language);

        var data = Path.Combine(Path.GetTempPath(), "data");
        var program = Path.Combine(Path.GetTempPath(), "program");
        Assert.Equal([Path.Combine(program, "languages"), Path.Combine(data, "languages")], LanguageFiles.Folders(data, program));
    }

    [AvaloniaFact]
    public void Your_changes_go_in_front_of_Magyar_and_their_broken_texts_are_Magyars()
    {
        WriteFile("hu.test-words.json", new
        {
            texts = new Dictionary<string, object>
            {
                ["Command_HotPaths_Menu"] = "Kedvenc mappák",
                ["Command_HotPaths_Go"] = "Ugrás ide: {0}",
                ["Command_HotPaths_GoOther"] = "Ugrás a másik panelen",
                ["Job_Copying"] = "{0} másolása {1}",
                ["Command_File_Move"] = "",
                ["Command_Fiel_Copy"] = "Másolás",
                ["Command_File_Delete"] = new { text = "Törlés" },
            },
        });

        var choice = TextLanguage.Choose("hu.test-words", Folders, AppServices.Originals);
        AppServices.SwitchLanguage(choice);

        Assert.Equal("Kedvenc mappák", Strings.Command_HotPaths_Menu);
        Assert.Equal("Ugrás ide: {0}", Strings.Command_HotPaths_Go);
        Assert.Equal("Másolás", Strings.Command_File_Copy);
        Assert.Equal("Ugrás a(z) {0}. gyorsmappára a másik panelen", Strings.Command_HotPaths_GoOther);
        Assert.Equal("Áthelyezés", Strings.Command_File_Move);
        Assert.Equal("hu-HU", Loc.TextCulture.Name);
        Assert.Equal(4, choice.Broken);
        Assert.Equal("hu.test-words.json", choice.FileName);
        var kinds = choice.Problems.ToDictionary(p => p.Key, p => p.Kind);
        Assert.Equal(LanguageProblemKind.Missing, kinds["Command_HotPaths_GoOther"]);
        Assert.Equal(LanguageProblemKind.NotInEnglish, kinds["Job_Copying"]);
        Assert.Equal(LanguageProblemKind.Empty, kinds["Command_File_Move"]);
        Assert.Equal(LanguageProblemKind.Unknown, kinds["Command_Fiel_Copy"]);
        Assert.Equal(LanguageProblemKind.NotText, kinds["Command_File_Delete"]);
        Assert.All(choice.Problems, p => Assert.Equal(Path.Combine(Folder, "hu.test-words.json"), p.Path));

        var list = new LanguageProblemsWindow(choice, null);
        Assert.Equal(5, list.Rows.Count);
        Assert.True(list.Rows[^1].Warning);
        Assert.Contains(list.Rows, r => r.Key == "Command_HotPaths_GoOther" && r.What.Contains("{0}", StringComparison.Ordinal) && r.Instead.StartsWith("Ugrás a(z)", StringComparison.Ordinal));
        Assert.Equal(1, LanguageProblemsWindow.LineOf(Path.Combine(Folder, "hu.test-words.json"), "Command_HotPaths_Menu"));
    }

    [AvaloniaFact]
    public void Changes_on_English_and_changes_whose_file_is_gone()
    {
        WriteFile("en.test-mine.json", new { texts = new Dictionary<string, object> { ["Command_File_Copy"] = "Duplicate here" } });
        var mine = TextLanguage.Choose("en.test-mine", Folders, AppServices.Originals);
        AppServices.SwitchLanguage(mine);
        Assert.Equal("Duplicate here", Strings.Command_File_Copy);
        Assert.Equal("Move", Strings.Command_File_Move);
        Assert.Equal("en-GB", Loc.TextCulture.Name);
        Assert.Null(OptionsWindow.LanguageTrouble(mine, warnings: true));

        var gone = TextLanguage.Choose("hu.test-gone", Folders, AppServices.Originals);
        Assert.Equal("hu.test-gone.json", gone.Missing);
        Assert.Equal("Másolás", gone.Texts["Command_File_Copy"]);
        Assert.Equal(Loc.Format(AppStrings.Opt_Language_ChangesNotFound, "hu.test-gone.json", "Magyar"), OptionsWindow.LanguageTrouble(gone, warnings: false));

        var noLanguage = TextLanguage.Choose("fi.test-orphan", Folders, AppServices.Originals);
        Assert.Equal("fi.json", noLanguage.Missing);
        Assert.Empty(noLanguage.Texts);
        Assert.Equal(Loc.Format(AppStrings.Opt_Language_NotFound, "fi.json"), OptionsWindow.LanguageTrouble(noLanguage, warnings: false));
    }

    [AvaloniaFact]
    public void A_language_beside_the_settings_does_not_hide_the_programs()
    {
        WriteFile("hu.json", new { texts = new Dictionary<string, object> { ["Command_File_Copy"] = "Régi" } });
        Assert.Equal("Másolás", TextLanguage.Choose("hu", Folders, AppServices.Originals).Texts["Command_File_Copy"]);
    }

    [AvaloniaFact]
    public void A_file_that_is_not_JSON_is_never_used()
    {
        Directory.CreateDirectory(Folder);
        File.WriteAllText(Path.Combine(Folder, "fi.json"), "{ \"texts\": { \"Command_File_Copy\": ");
        var broken = TextLanguage.Choose("fi", Folders, AppServices.Originals);
        Assert.NotNull(broken.Unreadable);
        Assert.Empty(broken.Texts);
        Assert.StartsWith("fi.json", OptionsWindow.LanguageTrouble(broken, warnings: false)!.Replace("A(z) ", "", StringComparison.Ordinal), StringComparison.Ordinal);

        File.WriteAllText(Path.Combine(Folder, "hu.test-broken.json"), "not json");
        var changes = TextLanguage.Choose("hu.test-broken", Folders, AppServices.Originals);
        Assert.Same(changes.Changes, changes.Unreadable);
        Assert.Equal("Másolás", changes.Texts["Command_File_Copy"]);
    }

    [AvaloniaFact]
    public void Options_lists_each_language_with_your_changes_under_it_and_greys_what_cant_be_chosen()
    {
        WriteFile("hu.test-words.json", new { texts = new Dictionary<string, object> { ["Command_File_Copy"] = "Másol" } });
        WriteFile("en.test-mine.json", new { texts = new Dictionary<string, object> { ["Command_File_Copy"] = "Duplicate" } });
        WriteFile("fi.test-orphan.json", new { texts = new Dictionary<string, object>() });
        WriteFile("test words.json", new { texts = new Dictionary<string, object>() });

        var choices = OptionsWindow.LanguageChoices(Folders, GeneralSettings.English);
        var ids = choices.Select(c => c.Id).ToList();
        Assert.Equal(GeneralSettings.English, ids[0]);
        Assert.Equal("en.test-mine", ids[1]);
        Assert.Equal(Loc.Format(AppStrings.Opt_Language_Changes, AppStrings.Opt_Language_English, "test-mine"), choices[1].Label);
        var magyar = ids.IndexOf("hu");
        Assert.Equal("Magyar", choices[magyar].Label);
        Assert.Equal("hu.test-words", ids[magyar + 1]);
        Assert.Equal("Magyar + test-words", choices[magyar + 1].Label);
        Assert.Contains(choices, c => c.Id == "?fi.test-orphan" && c.Label == Loc.Format(AppStrings.Opt_Language_NeedsBase, "fi.test-orphan.json", "fi.json"));
        Assert.Contains(choices, c => c.Id == "?test words.json" && c.Label == Loc.Format(AppStrings.Opt_Language_BadName, "test words.json"));

        var combo = OptionsWindow.LanguageCombo(Folders, GeneralSettings.English, _ => { });
        var items = combo.Items.OfType<ComboBoxItem>().ToList();
        Assert.True(items[magyar].IsEnabled);
        Assert.False(items[ids.IndexOf("?fi.test-orphan")].IsEnabled);
        Assert.Contains(OptionsWindow.LanguageChoices(Folders, "de.gone"), c => c.Id == "de.gone" && c.Label == Loc.Format(AppStrings.Opt_Language_Gone, "de.gone.json"));
    }

    [AvaloniaFact]
    public void Export_writes_every_English_text_as_a_new_language_in_the_form_of_hu_json()
    {
        var export = new LanguageExportWindow(Folder, Folders);
        export.NameBox.Text = "";
        Assert.False(export.ExportButton.IsEnabled);
        Assert.False(export.ErrorText.IsVisible);
        foreach (var (name, error) in new[]
        {
            ("hu", Loc.Format(AppStrings.LangExport_Exists, "hu.json")),
            ("hu.my-words", AppStrings.LangExport_BadName),
            ("en", AppStrings.LangExport_BadName),
            ("my-words", AppStrings.LangExport_BadName),
        })
        {
            export.NameBox.Text = name;
            export.Accept();
            Assert.True(export.ErrorText.IsVisible);
            Assert.Equal(error, export.ErrorText.Text);
        }

        export.NameBox.Text = "fi";
        export.Accept();
        var path = Path.Combine(Folder, "fi.json");
        Assert.True(File.Exists(path));

        var written = File.ReadAllText(path);
        using (var doc = JsonDocument.Parse(written))
        using (var hu = JsonDocument.Parse(File.ReadAllText(Path.Combine(Folders[0], "hu.json"))))
        {
            var root = doc.RootElement;
            Assert.Equal(hu.RootElement.EnumerateObject().Select(p => p.Name), root.EnumerateObject().Select(p => p.Name));
            Assert.Equal("Salandual language file 1", root.GetProperty("format").GetString());
            Assert.Equal("Suomi", root.GetProperty("name").GetString());
            Assert.Equal("", root.GetProperty("author").GetString());
            Assert.Equal("GPL-3.0-or-later", root.GetProperty("license").GetString());
            Assert.Equal(typeof(LanguageExportWindow).Assembly.GetName().Version!.ToString(3), root.GetProperty("forVersion").GetString());
            Assert.Equal(DateTime.Today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), root.GetProperty("updated").GetString());
            Assert.Equal("YYYY-MM-DD", root.GetProperty("updatedFormat").GetString());
            Assert.Equal(DateTime.Today, LanguageHeader.ReadDate(root.GetProperty("updated").GetString()));
            Assert.Equal("", root.GetProperty("contact").GetString());
            var texts = root.GetProperty("texts");
            Assert.Equal("Copy", texts.GetProperty("Command_File_Copy").GetString());
            Assert.All(texts.EnumerateObject(), t => Assert.Equal(JsonValueKind.String, t.Value.ValueKind));
            Assert.Equal(LanguageExportWindow.Entries().Count, texts.EnumerateObject().Count());
            Assert.Equal(texts.EnumerateObject().Select(t => t.Name).Order(StringComparer.Ordinal), texts.EnumerateObject().Select(t => t.Name));
        }

        Assert.DoesNotContain("\r", written, StringComparison.Ordinal);
        Assert.StartsWith("{\n  \"format\": \"Salandual language file 1\",\n  \"name\": \"Suomi\"", written, StringComparison.Ordinal);
        var fi = TextLanguage.Choose("fi", Folders, AppServices.Originals);
        Assert.Empty(fi.Problems);
        Assert.Equal("Suomi", fi.Language!.Name);
        Assert.Contains(OptionsWindow.LanguageChoices(Folders, GeneralSettings.English), c => c.Id == "fi");

        var again = new LanguageExportWindow(Folder, Folders);
        again.NameBox.Text = "fi.json";
        again.Accept();
        Assert.Equal(Loc.Format(AppStrings.LangExport_Exists, "fi.json"), again.ErrorText.Text);
    }

    [AvaloniaFact]
    public void Options_says_a_restart_brings_the_language_chosen()
    {
        var w = new OptionsWindow();
        w.Show();
        w.ShowCategory("general");
        Dispatcher.UIThread.RunJobs();
        Assert.False(w.LanguageRestartBar!.IsVisible);

        var combo = w.GetLogicalDescendants().OfType<ComboBox>().First(c => c.Items.OfType<ComboBoxItem>().Any(i => i.Content as string == "Magyar"));
        combo.SelectedIndex = combo.Items.OfType<ComboBoxItem>().ToList().FindIndex(i => i.Content as string == "Magyar");
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("hu", CurrentApp.Services.Settings.Current.General.Language);
        Assert.True(w.LanguageRestartBar!.IsVisible);
        Assert.Equal("Copy", Strings.Command_File_Copy);
        w.Close();
    }

    [AvaloniaFact]
    public void Options_shows_what_the_chosen_language_file_says_about_itself()
    {
        WriteFile("fi.json", new Dictionary<string, object>
        {
            ["format"] = LanguageFiles.Format,
            ["name"] = "Suomi",
            ["author"] = "Test Kääntäjä",
            ["contact"] = "https://example.org/fi",
            ["copyright"] = "© 2026 Test Kääntäjä",
            ["license"] = "GPL-3.0-or-later",
            ["forVersion"] = "0.7.0",
            ["updated"] = "2026-09-14",
            ["updatedFormat"] = "YYYY-MM-DD",
            ["texts"] = new Dictionary<string, string> { ["Command_File_Copy"] = "Kopioi", ["Command_File_Move"] = "Siirrä" },
        });
        var opened = new List<Uri>();
        var w = new OptionsWindow { OpenWeb = opened.Add };
        CurrentApp.Services.Settings.Update(s => s with { General = s.General with { Language = "fi" } });
        w.Show();
        w.ShowCategory("general");
        Dispatcher.UIThread.RunJobs();

        var texts = w.LanguageHeaderBlock!.GetLogicalDescendants().OfType<TextBlock>().Select(t => t.Text).ToList();
        var date = DisplayFormat.From(CurrentApp.Services.Settings.Current.Panels, CultureInfo.CurrentCulture).Date(new DateTime(2026, 9, 14));
        Assert.Contains(Loc.Format(AppStrings.Opt_Language_Author, "Test Kääntäjä"), texts);
        Assert.Contains(Loc.Format(AppStrings.Opt_Language_Version, "0.7.0"), texts);
        Assert.Contains(Loc.Format(AppStrings.Opt_Language_Updated, date), texts);
        Assert.Contains("© 2026 Test Kääntäjä", texts);
        Assert.Contains("GPL-3.0-or-later", texts);

        var link = w.LanguageHeaderBlock!.GetLogicalDescendants().OfType<Button>().Single(b => b.Content as string == "https://example.org/fi");
        link.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Assert.Equal([new Uri("https://example.org/fi")], opened);

        var lacks = LanguageExportWindow.Entries().Count - 2;
        Assert.Equal(Loc.Plural(lacks, AppStrings.Language_NotTranslated_One, AppStrings.Language_NotTranslated_Other), w.LanguageMissingLine!.Text);
        var list = new LanguageProblemsWindow(new LanguageChoice("fi", new Dictionary<string, string>(), TextLanguage.Choose("fi", Folders, AppServices.Originals).Language, null,
            [new LanguageProblem("Command_File_Delete", LanguageProblemKind.NotTranslated, English: "Delete")]), null);
        Assert.Equal(Loc.Format(AppStrings.LangProblems_MissingTitle, "fi.json"), list.Title);
        Assert.Equal(("Command_File_Delete", AppStrings.LangProblem_NotTranslated, "Delete", true), list.Rows.Single());
        Assert.Equal(0, TextLanguage.Choose("fi", Folders, AppServices.Originals).Broken);
        w.Close();

        CurrentApp.Services.Settings.Update(s => s with { General = s.General with { Language = "hu" } });
        var hu = new OptionsWindow();
        hu.Show();
        hu.ShowCategory("general");
        Dispatcher.UIThread.RunJobs();
        Assert.Null(hu.LanguageMissingLine);
        Assert.Contains(Loc.Format(AppStrings.Opt_Language_Author, "Salandual"), hu.LanguageHeaderBlock!.GetLogicalDescendants().OfType<TextBlock>().Select(t => t.Text));
        Assert.DoesNotContain(hu.LanguageHeaderBlock!.GetLogicalDescendants().OfType<TextBlock>(), t => t.Text == AppStrings.Opt_Language_Contact);
        hu.Close();

        CurrentApp.Services.Settings.Update(s => s with { General = s.General with { Language = GeneralSettings.English } });
        var en = new OptionsWindow();
        en.Show();
        en.ShowCategory("general");
        Dispatcher.UIThread.RunJobs();
        Assert.Null(en.LanguageHeaderBlock);
        en.Close();
    }

    [AvaloniaFact]
    public void A_files_format_says_what_it_is_and_a_newer_one_is_listed_greyed()
    {
        WriteFile("fi.json", new Dictionary<string, object> { ["format"] = "Salandual language file 2", ["texts"] = new Dictionary<string, string> { ["Command_File_Copy"] = "Kopioi" } });
        WriteFile("hu.test-notours.json", new Dictionary<string, object> { ["format"] = "language file 1", ["texts"] = new Dictionary<string, string> { ["Command_File_Copy"] = "Másol" } });
        WriteFile("hu.test-noformat.json", new Dictionary<string, object> { ["texts"] = new Dictionary<string, string> { ["Command_File_Copy"] = "Másol" } });

        var newer = LanguageFiles.Read(Path.Combine(Folder, "fi.json"));
        Assert.Equal(2, newer.NewerFormat);
        Assert.Equal(Loc.Format(Strings.Language_Newer, 2), newer.Unreadable);
        Assert.Empty(newer.Texts);
        Assert.Equal(Strings.Language_NotOurs, LanguageFiles.Read(Path.Combine(Folder, "hu.test-notours.json")).Unreadable);
        Assert.Null(LanguageFiles.Read(Path.Combine(Folder, "hu.test-noformat.json")).Unreadable);
        Assert.Null(LanguageFiles.Read(Path.Combine(Folders[0], "hu.json")).Unreadable);

        var choices = OptionsWindow.LanguageChoices(Folders, GeneralSettings.English);
        Assert.Contains(choices, c => c.Id == "?fi" && c.Label == Loc.Format(AppStrings.Opt_Language_Newer, "fi.json"));
        var combo = OptionsWindow.LanguageCombo(Folders, GeneralSettings.English, _ => { });
        Assert.False(combo.Items.OfType<ComboBoxItem>().Single(i => i.Content as string == Loc.Format(AppStrings.Opt_Language_Newer, "fi.json")).IsEnabled);
        Assert.Contains(choices, c => c.Id == "hu.test-notours" && c.Label == Loc.Format(AppStrings.Opt_Language_Unreadable, "hu.test-notours.json"));
        Assert.Equal("Másol", TextLanguage.Choose("hu.test-noformat", Folders, AppServices.Originals).Texts["Command_File_Copy"]);
    }

    [AvaloniaFact]
    public void The_language_is_read_before_the_settings_load()
    {
        var dir = Path.Combine(Path.GetTempPath(), "salandual-peek-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = SettingsStore.InDirectory(dir);
            Assert.Equal(GeneralSettings.English, store.Peek().Language);
            store.Save(new AppSettings { General = new GeneralSettings { Language = "hu.my-words" } });
            Assert.Equal("hu.my-words", store.Peek().Language);
        }
        finally
        {
            if (Directory.Exists(dir))
            {
                Directory.Delete(dir, recursive: true);
            }
        }
    }

    [AvaloniaFact(Explicit = true)]
    public void Hungarian_is_in_step_with_the_English()
    {
        var core = Entries("src/Salandual.Core/Resources/Strings.resx");
        var en = new Dictionary<string, string>(core);
        foreach (var (key, text) in Entries("src/Salandual.App/Resources/AppStrings.resx"))
        {
            en.TryAdd(key, text);
        }

        var hu = LanguageFiles.Read(Path.Combine(RepoRoot(), "src/Salandual.App/languages/hu.json"));
        Assert.Null(hu.Unreadable);
        var problems = new List<string>();
        using (var doc = JsonDocument.Parse(File.ReadAllText(hu.Path)))
        {
            var version = typeof(LanguageExportWindow).Assembly.GetName().Version!.ToString(3);
            if (doc.RootElement.GetProperty("forVersion").GetString() != version)
            {
                problems.Add("forVersion: not " + version + " (and updated: today's date, as YYYY-MM-DD)");
            }

            if (LanguageHeader.ReadDate(doc.RootElement.GetProperty("updated").GetString()) is null)
            {
                problems.Add("updated: not a date written as YYYY-MM-DD");
            }
        }

        foreach (var (key, text) in hu.Texts)
        {
            if (!en.TryGetValue(key, out var source))
            {
                problems.Add(key + ": no such English text");
            }
            else if (LanguageFiles.CheckText(key, text, source) is { } problem)
            {
                problems.Add($"{key}: {problem.Kind} {problem.Placeholders}");
            }
        }

        problems.AddRange(en.Keys.Except(hu.Texts.Keys).Order(StringComparer.Ordinal).Select(k => k + ": no Hungarian"));
        Assert.True(problems.Count == 0, string.Join("\n", problems));
    }

    [AvaloniaFact]
    public void A_menus_access_keys_differ_in_both_languages()
    {
        foreach (var id in new[] { GeneralSettings.English, "hu" })
        {
            AppServices.SwitchLanguage(TextLanguage.Choose(id, Folders, AppServices.Originals));
            var twice = new List<string>();
            void Check(IReadOnlyList<MenuNode> items)
            {
                var keys = items.Where(i => i.Header is not null).Select(i => (Header: i.Header!, Key: AccessKey(i.Header!))).Where(k => k.Key is not null).ToList();
                twice.AddRange(keys.GroupBy(k => k.Key).Where(g => g.Count() > 1).Select(g => id + ": " + string.Join(" / ", g.Select(k => k.Header))));
                foreach (var item in items.Where(i => i.Children is not null))
                {
                    Check(item.Children!);
                }
            }

            Check(MainMenu.Create());
            Assert.True(twice.Count == 0, string.Join("\n", twice));
        }
    }

    [AvaloniaFact]
    public async Task Pictures_in_Hungarian()
    {
        if (Environment.GetEnvironmentVariable("SALANDUAL_CAPTURE_DIR") is not { Length: > 0 } dir)
        {
            return;
        }

        var sample = Path.Combine(Path.GetTempPath(), "salandual-hu-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(sample, "tavasz"));
        File.WriteAllText(Path.Combine(sample, "katalogus.csv"), "név;fajta");
        File.WriteAllText(Path.Combine(sample, "ültetési-naptár.md"), "# Naptár");
        void Save(Window w, string name)
        {
            for (var k = 0; k < 10; k++)
            {
                Dispatcher.UIThread.RunJobs();
            }

            w.CaptureRenderedFrame()!.Save(Path.Combine(dir, name), Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
        }

        try
        {
            WriteFile("hu.test-pictures.json", new { texts = new Dictionary<string, object> { ["Job_Copying"] = "{0} másolása {1}", ["Command_Fiel_Copy"] = "x" } });
            WriteFile("fi.test-orphan.json", new { texts = new Dictionary<string, object>() });
            AppServices.SwitchLanguage(TextLanguage.Choose("hu", Folders, AppServices.Originals));
            var main = new MainWindow { Width = 1200, Height = 700 };
            main.Show();
            await main.LeftPanel.Panel.NavigateAsync(sample);
            await main.RightPanel.Panel.NavigateAsync(Path.Combine(sample, "tavasz"));
            main.ActivePanel.View.Focus();
            Save(main, "hu-main.png");

            var panel = main.ActivePanel.Panel;
            panel.SetFocus(panel.Items.ToList().FindIndex(e => e.Name == "katalogus.csv"));
            async Task<Window> Opened(Func<Window?> open)
            {
                for (var k = 0; k < 1500 && open() is not { IsVisible: true }; k++)
                {
                    Dispatcher.UIThread.RunJobs();
                    await Task.Delay(2, TestContext.Current.CancellationToken);
                }

                return open()!;
            }

            foreach (var (command, name) in new[] { ("File.Copy", "hu-copy.png"), ("File.Delete", "hu-delete.png") })
            {
                Assert.True(main.Run(command, PanelTarget.Active));
                var dialog = await Opened(() => main.OpenOperationDialog);
                Save(dialog, name);
                dialog.Close();
                Dispatcher.UIThread.RunJobs();
            }

            Assert.True(main.Run("Archive.Pack", PanelTarget.Active));
            var pack = await Opened(() => main.OpenPackDialog);
            Save(pack, "hu-pack.png");
            pack.Close();
            var find = main.OpenFind(main.ActivePanel);
            find.Width = 1000;
            find.Height = 640;
            Save(find, "hu-find.png");
            find.Close();

            var options = new OptionsWindow();
            options.Show();
            options.ShowCategory("general");
            CurrentApp.Services.Settings.Update(s => s with { General = s.General with { Language = "hu.test-pictures" } });
            options.Rebuild();
            Save(options, "hu-options.png");
            options.LanguageHeaderBlock!.BringIntoView();
            Dispatcher.UIThread.RunJobs();
            Save(options, "hu-options-header.png");
            var combo = options.GetLogicalDescendants().OfType<ComboBox>().First(c => c.Items.OfType<ComboBoxItem>().Any(i => i.Content as string == "Magyar"));
            combo.IsDropDownOpen = true;
            Save(options, "hu-options-list.png");
            combo.IsDropDownOpen = false;
            var export = new LanguageExportWindow(Folder, Folders);
            export.Show();
            Save(export, "hu-export.png");
            export.Close();
            options.Close();

            var problems = new LanguageProblemsWindow(TextLanguage.Choose("hu.test-pictures", Folders, AppServices.Originals), null);
            problems.Show();
            Save(problems, "hu-problems.png");
            problems.Close();

            var wizard = new Wizard.FirstStepsWindow(CurrentApp.Services, fromHelp: false);
            wizard.Show();
            Save(wizard, "hu-wizard.png");
            wizard.Close();
            main.Close();
        }
        finally
        {
            Directory.Delete(sample, recursive: true);
        }
    }

    [AvaloniaTheory]
    [InlineData("en")]
    [InlineData("hu")]
    public void FirstSteps_StepNames_Wrap_NotCut(string language)
    {
        AppServices.SwitchLanguage(language == "en" ? LanguageChoice.English : TextLanguage.Choose(language, Folders, AppServices.Originals));
        var wizard = new Wizard.FirstStepsWindow(CurrentApp.Services, fromHelp: false);
        wizard.Show();
        try
        {
            Dispatcher.UIThread.RunJobs();
            var names = wizard.StepsBar.GetLogicalDescendants().OfType<TextBlock>().Where(t => t.Text is { Length: > 1 }).ToList();
            Assert.Equal(wizard.Steps.Count, names.Count);
            var cut = names.Where(t => t.TextLayout.TextLines.Any(l => l.HasCollapsed)).Select(t => t.Text).ToList();
            Assert.True(cut.Count == 0, "cut: " + string.Join(", ", cut));
            Assert.All(names, t => Assert.True(t.TextLayout.TextLines.Count <= 2, t.Text));
        }
        finally
        {
            wizard.Close();
        }
    }

    private static char? AccessKey(string header)
    {
        var i = header.IndexOf('_', StringComparison.Ordinal);
        return i >= 0 && i + 1 < header.Length ? char.ToUpperInvariant(header[i + 1]) : null;
    }

    private static Dictionary<string, string> Entries(string relative)
        => XDocument.Load(Path.Combine(RepoRoot(), relative)).Root!.Elements("data")
            .ToDictionary(d => (string)d.Attribute("name")!, d => (string?)d.Element("value") ?? "");

    private static string RepoRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "Salandual.slnx")))
            {
                return dir.FullName;
            }
        }

        throw new InvalidOperationException("Repository root (Salandual.slnx) not found.");
    }
}

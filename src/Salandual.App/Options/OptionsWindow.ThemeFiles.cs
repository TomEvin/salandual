// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.App/Options/OptionsWindow.ThemeFiles.cs

using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Salandual.App.Resources;
using Salandual.App.Theme;
using Salandual.Core.Resources;
using Salandual.Core.Settings;
using static Salandual.App.Options.OptionsUi;

namespace Salandual.App.Options;

public sealed partial class OptionsWindow
{
    private IReadOnlyList<VsCodeTheme>? _vsCodeInstalled;
    private Dictionary<string, ColorTheme>? _vsCodeRead;
    private string? _themeNotice;

    internal static Func<IReadOnlyList<VsCodeTheme>> VsCodeSource { get; set; } = () => VsCodeThemes.Installed();

    internal TextBlock? ThemeNoticeText { get; private set; }

    internal MenuFlyout? ThemeImportMenu { get; private set; }

    private IReadOnlyList<VsCodeTheme> VsCodeInstalled => _vsCodeInstalled ??= VsCodeSource();

    private List<ColorTheme> VsCodeGroup()
    {
        var copies = Current.Appearance.Themes.Where(t => t.Origin == ThemeOrigin.VsCode).ToList();
        if (_vsCodeRead is null)
        {
            _vsCodeRead = new(StringComparer.Ordinal);
            foreach (var vs in VsCodeInstalled)
            {
                if (VsCodeThemes.ToTheme(vs.Path, out _, vs.UiTheme, vs.Label) is { } theme)
                {
                    _vsCodeRead.TryAdd(vs.Id, theme with { Id = vs.Id, Origin = ThemeOrigin.VsCode, SourcePath = vs.Path, CopiedOn = null });
                }
            }
        }

        var copied = copies.Select(t => t.Id).ToHashSet(StringComparer.Ordinal);
        return [.. copies, .. _vsCodeRead.Values.Where(t => !copied.Contains(t.Id))];
    }

    private ColorTheme FindTheme(string? id)
        => ThemeCatalog.TryFind(Current.Appearance, id)
           ?? (id is not null && _vsCodeRead?.TryGetValue(id, out var vs) == true ? vs : ThemeCatalog.Find(Current.Appearance, id));

    private IEnumerable<ColorTheme> AllThemes()
    {
        var all = ThemeCatalog.All(Current.Appearance).ToList();
        var ids = all.Select(t => t.Id).ToHashSet(StringComparer.Ordinal);
        return [.. all, .. (_vsCodeRead?.Values ?? Enumerable.Empty<ColorTheme>()).Where(t => !ids.Contains(t.Id))];
    }

    private bool IsUncopiedVsCode(ColorTheme theme)
        => theme.Origin == ThemeOrigin.VsCode && ThemeCatalog.TryFind(Current.Appearance, theme.Id) is null;

    internal void UseNow(ColorTheme theme)
    {
        if (IsUncopiedVsCode(theme) && VsCodeInstalled.FirstOrDefault(v => v.Id == theme.Id) is { } vs)
        {
            try
            {
                var copy = ThemePackage.CopyFromVsCode(vs, DateTime.Now);
                UpdateAppearance(a => ThemeEditing.Add(a, copy));
                theme = copy;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
            {
                _themeNotice = Loc.Format(AppStrings.Themes_ImportFailed, vs.Label, ex.Message);
                RebuildLater();
                return;
            }
        }

        var used = theme;
        UpdateAppearance(x => x with { Schedule = UseTheme(x.Schedule, used) });
        RebuildLater();
    }

    internal void UpdateFromVsCode(string id)
    {
        if (ThemeCatalog.TryFind(Current.Appearance, id) is not { Origin: ThemeOrigin.VsCode } copy)
        {
            return;
        }

        try
        {
            var updated = ThemePackage.UpdateFromVsCode(copy, DateTime.Now);
            UpdateAppearance(a => a with { Themes = [.. a.Themes.Select(t => t.Id == id ? updated : t)] });
            _vsCodeRead = null;
            _themeNotice = Loc.Format(AppStrings.Themes_Vs_Updated, copy.Name);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            _themeNotice = ex.Message;
        }

        RebuildLater();
    }

    private Border VsCodeBanner(out Action<ColorTheme> show)
    {
        var text = Text("", 12.5, wrap: true).Also(t => t.VerticalAlignment = VerticalAlignment.Center);
        var update = ActionButton(AppStrings.Themes_Vs_Update, () => UpdateFromVsCode(_pickedTheme ?? ""));
        Grid.SetColumn(update, 1);
        var box = new Border
        {
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(12, 6),
            Margin = new Thickness(0, 8, 0, 0),
            Child = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 10, Children = { text, update } },
        };
        show = t =>
        {
            box.IsVisible = t.Origin == ThemeOrigin.VsCode;
            var uncopied = IsUncopiedVsCode(t);
            text.Text = uncopied ? AppStrings.Themes_Vs_NotCopied : Loc.Format(AppStrings.Themes_Vs_Copied, t.CopiedOn?.ToString("d", CultureInfo.CurrentCulture) ?? "");
            update.IsVisible = !uncopied;
            box.Res(Border.BackgroundProperty, uncopied ? "Brush.WindowBackground" : "Brush.TextSelection");
        };
        return box;
    }

    private async Task ExportThemeAsync(ColorTheme theme)
    {
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = AppStrings.Themes_Export_Title,
            SuggestedFileName = ThemePackage.FileName(theme),
            DefaultExtension = ThemePackage.Extension,
            FileTypeChoices = [new FilePickerFileType(AppStrings.Themes_Picker_Theme) { Patterns = ["*" + ThemePackage.Extension] }],
        });
        if (file?.TryGetLocalPath() is { } path)
        {
            ExportTheme(theme, path);
        }
    }

    internal void ExportTheme(ColorTheme theme, string path)
    {
        try
        {
            ThemePackage.Export(theme, ThemeFiles.ImagePath(DataDirectory, theme), path);
            _themeNotice = Loc.Format(AppStrings.Themes_Exported, Path.GetFileName(path));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _themeNotice = Loc.Format(AppStrings.Themes_ExportFailed, ex.Message);
        }

        RebuildLater();
    }

    private MenuFlyout ImportMenu()
    {
        var menu = new MenuFlyout { Placement = PlacementMode.BottomEdgeAlignedLeft };
        MenuItem Item(string header, string note, Action click)
        {
            var item = new MenuItem { Header = header, InputGesture = null };
            item.Click += (_, _) => click();
            ToolTip.SetTip(item, note);
            return item;
        }

        menu.Items.Add(Item(AppStrings.Themes_Import_Theme, ThemePackage.Extension, () => _ = ImportThemeAsync(AppStrings.Themes_Picker_Theme, "*" + ThemePackage.Extension)));
        menu.Items.Add(Item(AppStrings.Themes_Import_VsFile, ".json", () => _ = ImportThemeAsync(AppStrings.Themes_Picker_VsCode, "*.json")));
        menu.Items.Add(Item(AppStrings.Themes_Import_ColorFile, ".json", () => _ = ImportThemeAsync(AppStrings.Themes_Picker_ColorFile, "*.json")));
        menu.Items.Add(new Separator());
        menu.Items.Add(new MenuItem { Header = AppStrings.Themes_Group_VsCode, IsEnabled = false, FontWeight = FontWeight.SemiBold });
        if (VsCodeInstalled.Count == 0)
        {
            menu.Items.Add(new MenuItem { Header = AppStrings.Themes_Import_NoVsCode, IsEnabled = false });
        }

        foreach (var vs in VsCodeInstalled)
        {
            var item = Item(vs.Label, vs.Dark ? AppStrings.Themes_Filter_Dark : AppStrings.Themes_Filter_Light, () => ImportFromVsCode(vs));
            item.Tag = vs.Id;
            menu.Items.Add(item);
        }

        ThemeImportMenu = menu;
        return menu;
    }

    private async Task ImportThemeAsync(string kind, string pattern)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = AppStrings.Themes_Import_Title,
            FileTypeFilter = [new FilePickerFileType(kind) { Patterns = [pattern] }, FilePickerFileTypes.All],
        });
        if (files is [var file] && file.TryGetLocalPath() is { } path)
        {
            ImportThemeFile(path);
        }
    }

    internal void ImportThemeFile(string path)
    {
        try
        {
            var themes = ThemePackage.Import(path, Current.Appearance, DataDirectory, DateTime.Now);
            UpdateAppearance(a => themes.Aggregate(a, ThemeEditing.Add));
            Imported(themes);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            _themeNotice = Loc.Format(AppStrings.Themes_ImportFailed, Path.GetFileName(path), ex.Message);
            RebuildLater();
        }
    }

    internal void ImportFromVsCode(VsCodeTheme vs)
    {
        try
        {
            var theme = ThemePackage.FromVsCode(vs, Current.Appearance, DateTime.Now);
            UpdateAppearance(a => ThemeEditing.Add(a, theme));
            Imported([theme]);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            _themeNotice = Loc.Format(AppStrings.Themes_ImportFailed, vs.Label, ex.Message);
            RebuildLater();
        }
    }

    private void Imported(IReadOnlyList<ColorTheme> themes)
    {
        _pickedTheme = themes[0].Id;
        _themeSearch = "";
        _themeFilter = "all";
        var names = themes.Count == 2 ? Loc.Format(AppStrings.Themes_ImportedTwo, themes[0].Name, themes[1].Name) : themes[0].Name;
        _themeNotice = Loc.Format(AppStrings.Themes_Imported, names);
        _themesTab = "library";
        RebuildLater();
    }

    private Control? ThemeNotice()
    {
        ThemeNoticeText = null;
        if (_themeNotice is not { } notice)
        {
            return null;
        }

        var text = Text(notice, 12.5, wrap: true).Also(t => t.VerticalAlignment = VerticalAlignment.Center);
        ThemeNoticeText = text;
        var close = new Button
        {
            Content = AppIcons.Glyph("Icon.Cross", 10, 10, 1.4),
            Width = 24,
            Height = 24,
            Padding = default,
            Background = Brushes.Transparent,
            BorderThickness = default,
        };
        ToolTip.SetTip(close, AppStrings.Common_Close);
        Avalonia.Automation.AutomationProperties.SetName(close, AppStrings.Common_Close);
        close.Click += (_, _) =>
        {
            _themeNotice = null;
            RebuildLater();
        };
        Grid.SetColumn(close, 1);
        var box = new Border
        {
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(12, 4, 4, 4),
            Child = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 8, Children = { text, close } },
        };
        box.Res(Border.BackgroundProperty, "Brush.InfoBackground");
        return box;
    }
}

// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.App/Wizard/FirstStepsWindow.Pages.cs

using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Salandual.App.Dialogs;
using Salandual.App.Options;
using Salandual.App.Resources;
using Salandual.App.Theme;
using Salandual.Core;
using Salandual.Core.Commands;
using Salandual.Core.Listing;
using Salandual.Core.Resources;
using Salandual.Core.Settings;
using static Salandual.App.Options.OptionsUi;

namespace Salandual.App.Wizard;

public sealed partial class FirstStepsWindow
{
    internal bool Portable { get; set; }

    private string? _importProblem;

    private Control Welcome()
    {
        var logo = new Image { Width = 88, Height = 88, VerticalAlignment = VerticalAlignment.Top };
        try
        {
            logo.Source = new Bitmap(AssetLoader.Open(new Uri(AppBuild.LogoUri)));
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or FileNotFoundException)
        {
            logo.IsVisible = false;
        }

        var hello = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), ColumnSpacing = 20 };
        var about = new StackPanel
        {
            Spacing = 6,
            VerticalAlignment = VerticalAlignment.Center,
            Children = { Text(FromHelp ? AppStrings.Wizard_Welcome_TitleHelp : AppStrings.Wizard_Welcome_Title, 24, FontWeight.SemiBold, wrap: true), Secondary(AppStrings.Wizard_Welcome_About, 13) },
        };
        Grid.SetColumn(about, 1);
        hello.Children.Add(logo);
        hello.Children.Add(about);

        var says = new Border { BorderThickness = new Thickness(3, 0, 0, 0), Padding = new Thickness(12, 6), Child = Rich(FromHelp ? AppStrings.Wizard_Welcome_SaysHelp : AppStrings.Wizard_Welcome_Says) }
            .Res(Border.BorderBrushProperty, "Brush.Accent");

        var language = new StackPanel
        {
            Spacing = 6,
            Children =
            {
                Text(AppStrings.Wizard_Language, 13, FontWeight.SemiBold),
                Secondary(AppStrings.Wizard_Language_Desc, 12),
                new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { Combo([("en", AppStrings.Opt_Language_English)], "en", _ => { }, minWidth: 180), Badge(AppStrings.Wizard_MoreLanguages) } },
            },
        };

        var importButton = ActionButton(AppStrings.Wizard_Import_Button, () => _ = ImportAsync());
        importButton.Tag = "import";
        var salamander = Later(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { ActionButton(AppStrings.Wizard_ImportSalamander, () => { }, enabled: false), Badge(AppStrings.Wizard_ComesLater) } });
        var import = new StackPanel
        {
            Spacing = 6,
            Children =
            {
                Text(AppStrings.Wizard_Import_Title, 13, FontWeight.SemiBold),
                Secondary(AppStrings.Wizard_Import_Desc, 12),
                new WrapPanel { Children = { importButton.Also(b => b.Margin = new Thickness(0, 4, 8, 4)), salamander.Also(s => s.Margin = new Thickness(0, 4, 0, 4)) } },
            },
        };
        if (Imported is var (picked, parts))
        {
            import.Children.Add(Rich(Loc.Format(AppStrings.Wizard_Imported, Path.GetFileName(picked.File), PartNames(parts)), 12.5));
        }
        else if (_importProblem is not null)
        {
            import.Children.Add(Text(_importProblem, 12.5, brush: "Brush.Danger", wrap: true));
        }

        var page = new StackPanel { Spacing = 18, Children = { hello, says, Columns(Box(language), Box(import)) } };
        if (Portable)
        {
            page.Children.Add(Rich(AppStrings.Wizard_Portable, 12.5));
        }

        return page;
    }

    private static string PartNames(IReadOnlySet<SettingsPart> parts) => string.Join(", ", parts.Order().Select(SettingsTransferDialog.PartTitle));

    private async Task ImportAsync()
    {
        _importProblem = null;
        (SettingsImport, IReadOnlySet<SettingsPart>)? picked;
        if (PickImport is not null)
        {
            picked = await PickImport();
        }
        else
        {
            var files = new SettingsFiles(_services, this, (text, _) => _importProblem = text);
            picked = await files.PickImportAsync();
        }

        if (picked is var (import, parts))
        {
            Imported = (import, parts);
        }

        Show(Step.Welcome, "import");
    }

    private Control ThemePage()
    {
        var c = Choices;
        var follow = Toggle(c.FollowWindows, on => Dispatcher.UIThread.Post(() => Change(x => x with
        {
            ThemeChanged = true,
            FollowWindows = on,
            One = on ? x.One : WindowsDark ? x.Dark : x.Light,
        }, "follow")));
        follow.Tag = "follow";
        follow.VerticalAlignment = VerticalAlignment.Center;
        var followText = new StackPanel
        {
            Spacing = 1,
            VerticalAlignment = VerticalAlignment.Center,
            Children =
            {
                Text(AppStrings.Wizard_Theme_Follow, 13, FontWeight.SemiBold),
                Secondary(c.FollowWindows ? WindowsDark ? AppStrings.Wizard_Theme_FollowOnDark : AppStrings.Wizard_Theme_FollowOnLight : AppStrings.Wizard_Theme_FollowOff, 12),
            },
        };
        var followRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12, Children = { follow, followText } };

        return Page(AppStrings.Wizard_Theme_Title, AppStrings.Wizard_Theme_Sub,
            followRow,
            ThemeGroup(AppStrings.Themes_Filter_Light, FirstStepsChoices.LightThemes, dark: false),
            ThemeGroup(AppStrings.Themes_Filter_Dark, FirstStepsChoices.DarkThemes, dark: true),
            AccentField(),
            Later(Field(AppStrings.Wizard_Icons, IconSets(), badge: AppStrings.Wizard_ComesLater)));
    }

    private Control ThemeGroup(string title, IReadOnlyList<string> offered, bool dark)
    {
        var appearance = _services.Settings.Current.Appearance;
        var ids = Choices.ThemeIds.Where(id => ThemeCatalog.Find(appearance, id).Dark == dark && !offered.Contains(id)).Distinct().Concat(offered);
        var cards = new WrapPanel();
        foreach (var id in ids)
        {
            cards.Children.Add(ThemeCard(ThemeCatalog.Find(appearance, id)));
        }

        return new StackPanel { Spacing = 6, Children = { Text(title, 12, FontWeight.SemiBold, "Brush.SecondaryText"), cards } };
    }

    private Button ThemeCard(ColorTheme theme)
    {
        var c = Choices;
        var picked = c.FollowWindows ? (theme.Dark ? c.Dark : c.Light) == theme.Id : c.One == theme.Id;
        var tag = !picked ? null : !c.FollowWindows ? AppStrings.Wizard_Theme_InUse : theme.Dark ? AppStrings.Wizard_Theme_DarkMode : AppStrings.Wizard_Theme_LightMode;
        var picture = new ThemePreview { Colors = Resolve(WithAccent(theme)), Compact = true, Width = 160, IsHitTestVisible = false };
        var frame = new Border { CornerRadius = Corners.Chip, ClipToBounds = true, BorderThickness = new Thickness(1), Child = picture }.Res(Border.BorderBrushProperty, "Brush.Border");
        var top = new Grid { Children = { frame } };
        if (tag is not null)
        {
            top.Children.Add(new Border
            {
                CornerRadius = Corners.Inner,
                Padding = new Thickness(7, 0),
                Margin = new Thickness(4),
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Top,
                Child = Text(tag, 10.5, FontWeight.SemiBold, "Brush.OnAccent"),
            }.Res(Border.BackgroundProperty, "Brush.Accent"));
        }

        var name = Text(theme.Name, 12.5, picked ? FontWeight.SemiBold : FontWeight.Normal);
        name.Margin = new Thickness(2, 6, 2, 0);
        name.TextTrimming = TextTrimming.CharacterEllipsis;
        var card = new Button
        {
            Tag = "theme:" + theme.Id,
            Width = 180,
            Margin = new Thickness(0, 0, 10, 10),
            Padding = new Thickness(8),
            BorderThickness = new Thickness(2),
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Content = new StackPanel { Children = { top, name } },
        }.Res(Button.BackgroundProperty, "Brush.WindowBackground").Res(Button.BorderBrushProperty, picked ? "Brush.Accent" : "Brush.Border");
        Avalonia.Automation.AutomationProperties.SetName(card, theme.Name);
        card.Click += (_, _) => Change(x => x with
        {
            ThemeChanged = true,
            Light = x.FollowWindows && !theme.Dark ? theme.Id : x.Light,
            Dark = x.FollowWindows && theme.Dark ? theme.Id : x.Dark,
            One = x.FollowWindows ? x.One : theme.Id,
        }, card.Tag);
        return card;
    }

    private ColorTheme WithAccent(ColorTheme theme) => Choices.Accent switch
    {
        AccentChoice.Windows => theme with { Accent = ThemeAccent.Windows },
        AccentChoice.Colour => theme with { Accent = ThemeAccent.Custom, CustomAccent = ColorMath.ToHex(Choices.Colour) },
        _ => theme with { Accent = ThemeAccent.Theme },
    };

    private IReadOnlyDictionary<string, Rgb> Resolve(ColorTheme theme) => _theme?.Resolve(theme) ?? ColorTokens.Resolve(theme);

    private Control AccentField()
    {
        var c = Choices;
        var appearance = _services.Settings.Current.Appearance;
        var onScreen = ThemeCatalog.Find(appearance, c.FollowWindows ? WindowsDark ? c.Dark : c.Light : c.One);
        var row = new WrapPanel();

        ToggleButton Option(object content, bool on, string tag, Func<FirstStepsChoices, FirstStepsChoices> change, string name)
        {
            var button = new ToggleButton { Content = content, IsChecked = on, Tag = tag, Padding = new Thickness(10, 5), Margin = new Thickness(0, 0, 6, 6), CornerRadius = Corners.Chip };
            Avalonia.Automation.AutomationProperties.SetName(button, name);
            button.Click += (_, _) => Change(x => change(x) with { ThemeChanged = true }, tag);
            return button;
        }

        Control Dot(Rgb colour, string label) => new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 7,
            Children = { Swatch(colour, 12).Also(s => s.CornerRadius = Corners.Inner), new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center } },
        };

        var themeAccent = Resolve(onScreen with { Accent = ThemeAccent.Theme })[ColorTokens.Accent];
        var windowsAccent = Resolve(onScreen with { Accent = ThemeAccent.Windows })[ColorTokens.Accent];
        row.Children.Add(Option(Dot(themeAccent, AppStrings.Wizard_Accent_Theme), c.Accent == AccentChoice.Theme, "accent:theme", x => x with { Accent = AccentChoice.Theme }, AppStrings.Wizard_Accent_Theme));
        row.Children.Add(Option(Dot(windowsAccent, AppStrings.Wizard_Accent_Windows), c.Accent == AccentChoice.Windows, "accent:windows", x => x with { Accent = AccentChoice.Windows }, AppStrings.Wizard_Accent_Windows));
        row.Children.Add(new Border { Width = 1, Height = 22, Margin = new Thickness(6, 0, 12, 6) }.Res(Border.BackgroundProperty, "Brush.Border"));
        foreach (var colour in FirstStepsChoices.Colours)
        {
            var hex = ColorMath.ToHex(colour);
            var on = c.Accent == AccentChoice.Colour && c.Colour == colour;
            var swatch = Option(Swatch(colour, 18).Also(s => s.CornerRadius = Corners.Inner), on, "accent:" + hex, x => x with { Accent = AccentChoice.Colour, Colour = colour }, hex);
            swatch.Padding = new Thickness(4);
            row.Children.Add(swatch);
        }

        var any = ColorButton(() => c.Accent == AccentChoice.Colour ? c.Colour : themeAccent,
            colour => Change(x => x with { ThemeChanged = true, Accent = AccentChoice.Colour, Colour = colour }, "accent:any"), AppStrings.Wizard_Accent_Any, 18);
        any.Tag = "accent:any";
        ToolTip.SetTip(any, AppStrings.Wizard_Accent_Any);
        row.Children.Add(any.Also(a => a.Margin = new Thickness(0, 0, 0, 6)));
        return Field(AppStrings.Wizard_Accent, row, description: AppStrings.Wizard_Accent_Desc);
    }

    private Control IconSets()
    {
        Border Set(string name, Control picture, bool on) => new Border
        {
            CornerRadius = Corners.Chip,
            BorderThickness = new Thickness(1),
            Padding = new Thickness(10, 5),
            Margin = new Thickness(0, 0, 6, 0),
            Child = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Children = { picture, Text(name, 12.5) } },
        }.Res(Border.BorderBrushProperty, on ? "Brush.Accent" : "Brush.Border");

        Border Folder(bool filled) => new Border { Width = 14, Height = 11, CornerRadius = new CornerRadius(2), BorderThickness = new Thickness(filled ? 0 : 1.3), VerticalAlignment = VerticalAlignment.Center }
            .Res(Border.BorderBrushProperty, "Brush.Text").Res(Border.BackgroundProperty, filled ? "Brush.Text" : "Brush.WindowBackground");
        var classic = new Border { Width = 14, Height = 11, CornerRadius = new CornerRadius(1), VerticalAlignment = VerticalAlignment.Center }.Res(Border.BackgroundProperty, "Brush.Warning");

        var row = new WrapPanel { IsEnabled = false };
        row.Children.Add(Set(AppStrings.Wizard_Icons_Outline, Folder(filled: false), on: true));
        row.Children.Add(Set(AppStrings.Wizard_Icons_Filled, Folder(filled: true), on: false));
        row.Children.Add(Set(AppStrings.Wizard_Icons_Classic, classic, on: false));
        row.Children.Add(Tick(AppStrings.Wizard_Seasonal, AppStrings.Wizard_Seasonal_Desc, on: false, null, "seasonal", enabled: false).Also(t => t.Margin = new Thickness(12, 0, 0, 0)));
        return row;
    }

    private static readonly string[] _cardCommands = ["File.QuickRename", "Panel.Refresh", "HotPaths.Menu", "Compare.Directories", "File.OccupiedSpace", "Selection.UnselectAll"];

    private Control KeyboardPage()
    {
        var c = Choices;
        var cards = new UniformGrid { Columns = 3, ColumnSpacing = 10 };
        cards.Children.Add(KeyMapCard(KeyMapName.Salamander));
        cards.Children.Add(KeyMapCard(KeyMapName.TotalCommander));
        cards.Children.Add(VimCard());

        Control? goes = null;
        if (c.Keys == KeyMapName.TotalCommander)
        {
            var now = _services.Settings.Current;
            var (quick, right) = SettingsTransferDialog.GoesWith(KeyMapName.TotalCommander, now);
            var ticks = new StackPanel { Spacing = 10 };
            if (now.Panels.QuickSearch != quick)
            {
                ticks.Children.Add(Tick(Loc.Format(AppStrings.KeyMapDlg_QuickSearch, SettingsTransferDialog.QuickSearchName(quick)),
                    AppStrings.Wizard_Keys_QuickDesc + " " + Loc.Format(AppStrings.Wizard_Now, SettingsTransferDialog.QuickSearchName(now.Panels.QuickSearch)),
                    c.TcQuickSearch, on => Choices = Choices with { TcQuickSearch = on }, "tc:quick"));
            }

            if (now.Mouse.RightClickUnselected != right)
            {
                ticks.Children.Add(Tick(Loc.Format(AppStrings.KeyMapDlg_RightClick, SettingsTransferDialog.RightClickName(right)),
                    AppStrings.Wizard_Keys_RightDesc + " " + Loc.Format(AppStrings.Wizard_Now, SettingsTransferDialog.RightClickName(now.Mouse.RightClickUnselected)),
                    c.TcRightButton, on => Choices = Choices with { TcRightButton = on }, "tc:right"));
            }

            var notYet = _services.Commands.KeysOf(KeyMapName.TotalCommander).NotYet;
            var examples = string.Join(", ", notYet.Take(3).Select(k => k.Title + " (" + k.Chord + ")"));
            var body = new StackPanel { Spacing = 10 };
            if (ticks.Children.Count > 0)
            {
                body.Children.Add(Text(Loc.Format(AppStrings.KeyMapDlg_GoesWith, KeyMaps.Title(KeyMapName.TotalCommander)), 13, FontWeight.SemiBold));
                body.Children.Add(ticks);
            }

            if (notYet.Count > 0)
            {
                body.Children.Add(Secondary(Loc.Format(AppStrings.KeyMapDlg_NotYet_Desc, notYet.Count, KeyMaps.Title(KeyMapName.TotalCommander), examples), 12));
            }

            goes = Box(body);
        }

        return Page(AppStrings.Wizard_Keys_Title, AppStrings.Wizard_Keys_Sub, cards, goes, Secondary(AppStrings.Wizard_Keys_Later, 12));
    }

    private Button KeyMapCard(KeyMapName name)
    {
        var on = Choices.Keys == name;
        var commands = _services.Commands;
        var dot = new Border { Width = 14, Height = 14, CornerRadius = new CornerRadius(7), BorderThickness = new Thickness(on ? 4 : 1), VerticalAlignment = VerticalAlignment.Center }
            .Res(Border.BorderBrushProperty, on ? "Brush.Accent" : "Brush.SecondaryText");
        var head = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { dot, Text(KeyMaps.Title(name), 14, FontWeight.SemiBold) } };
        var description = name == KeyMapName.Salamander
            ? AppStrings.Wizard_Keys_Salamander
            : Loc.Format(AppStrings.Wizard_Keys_TotalCommander, commands.Differences(KeyMapName.Salamander, name).Count);

        var card = new Button
        {
            Tag = "keys:" + name,
            Padding = new Thickness(on ? 13 : 14, on ? 11 : 12),
            BorderThickness = new Thickness(on ? 2 : 1),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            VerticalContentAlignment = VerticalAlignment.Top,
            Content = new StackPanel { Spacing = 4, Children = { head, Secondary(description, 12), KeyList(MainKeys(name)).Also(k => k.Margin = new Thickness(0, 8, 0, 0)) } },
        }.Res(Button.BackgroundProperty, "Brush.WindowBackground").Res(Button.BorderBrushProperty, on ? "Brush.Accent" : "Brush.BorderStrong");
        Avalonia.Automation.AutomationProperties.SetName(card, KeyMaps.Title(name));
        card.Click += (_, _) => Change(x => x with { Keys = name }, card.Tag);
        return card;
    }

    internal IReadOnlyList<(string Key, string Label, bool Differs)> MainKeys(KeyMapName name)
    {
        var commands = _services.Commands;
        var map = commands.KeysOf(name).Map;
        var salamander = commands.KeysOf(KeyMapName.Salamander).Map;
        var keys = new List<(string, string, bool)>();
        foreach (var id in _cardCommands)
        {
            var chords = map.Where(kv => kv.Value == id).Select(kv => kv.Key).ToList();
            if (chords.Count == 0)
            {
                continue;
            }

            var differ = chords.Where(k => !salamander.TryGetValue(k, out var s) || s != id).ToList();
            var order = commands[id].DefaultChords.ToList();
            var chord = (differ.Count > 0 ? differ : chords).OrderBy(k => order.IndexOf(k) is var n and >= 0 ? n : int.MaxValue).First();
            keys.Add((chord.ToString(), commands[id].Title, differ.Count > 0));
        }

        var tc = name == KeyMapName.TotalCommander;
        keys.Add((tc ? AppStrings.Wizard_Keys_QuickCtrlAlt : AppStrings.Wizard_Keys_QuickLetters, AppStrings.Wizard_Keys_Quick, tc));
        return keys;
    }

    private static Grid KeyList(IEnumerable<(string Key, string Label, bool Differs)> keys)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), ColumnSpacing = 10, RowSpacing = 5 };
        var row = 0;
        foreach (var (key, label, differs) in keys)
        {
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            var chord = new Border
            {
                BorderThickness = new Thickness(differs ? 3 : 0, 0, 0, 0),
                Padding = new Thickness(differs ? 4 : 0, 0, 0, 0),
                Child = new TextBlock { Text = key, FontSize = 12 }.Res(TextBlock.FontFamilyProperty, "Font.Mono").Res(TextBlock.ForegroundProperty, "Brush.Text"),
            }.Res(Border.BorderBrushProperty, "Brush.Accent");
            Grid.SetRow(chord, row);
            var text = Text(label, 12, brush: "Brush.SecondaryText");
            text.TextTrimming = TextTrimming.CharacterEllipsis;
            Grid.SetRow(text, row);
            Grid.SetColumn(text, 1);
            grid.Children.Add(chord);
            grid.Children.Add(text);
            row++;
        }

        return grid;
    }

    private static Control VimCard()
    {
        var parts = AppStrings.Wizard_Keys_Vim.Split('|');
        var keys = new List<(string, string, bool)>();
        for (var i = 0; i + 1 < parts.Length; i += 2)
        {
            keys.Add((parts[i], parts[i + 1], false));
        }

        var dot = new Border { Width = 14, Height = 14, CornerRadius = new CornerRadius(7), BorderThickness = new Thickness(1), VerticalAlignment = VerticalAlignment.Center }.Res(Border.BorderBrushProperty, "Brush.SecondaryText");
        var content = new StackPanel
        {
            Spacing = 4,
            Children =
            {
                new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { dot, Text(AppStrings.Wizard_Keys_VimName, 14, FontWeight.SemiBold) } },
                Badge(AppStrings.Wizard_ComesLater).Also(b => b.HorizontalAlignment = HorizontalAlignment.Left),
                KeyList(keys).Also(k => k.Margin = new Thickness(0, 8, 0, 0)),
            },
        };
        return Later(new Border { CornerRadius = Corners.Chip, BorderThickness = new Thickness(1), Padding = new Thickness(14, 12), Child = content }
            .Res(Border.BorderBrushProperty, "Brush.Border").Res(Border.BackgroundProperty, "Brush.WindowBackground"));
    }

    private Control BarsPage()
    {
        var c = Choices;
        var preview = new BarsPreview(() => _services.Settings.Current.Layout with { Bars = Choices.Bars }) { Width = 250, VerticalAlignment = VerticalAlignment.Top };
        var ticks = new StackPanel { Spacing = 10 };
        void Bar(string title, string description, bool on, Func<BarVisibility, bool, BarVisibility> set, string tag)
            => ticks.Children.Add(Tick(title, description, on, value =>
            {
                Choices = Choices with { Bars = set(Choices.Bars, value) };
                preview.Refresh();
            }, tag));

        var b = c.Bars;
        Bar(AppStrings.Wizard_Bar_Menu, AppStrings.Wizard_Bar_Menu_Desc, b.MenuBar, (v, on) => v with { MenuBar = on }, "bar:menu");
        Bar(AppStrings.Wizard_Bar_Drives, AppStrings.Wizard_Bar_Drives_Desc, b.DriveStrip, (v, on) => v with { DriveStrip = on }, "bar:drives");
        Bar(AppStrings.Wizard_Bar_HotPaths, AppStrings.Wizard_Bar_HotPaths_Desc, b.HotPathBar, (v, on) => v with { HotPathBar = on }, "bar:hot");
        Bar(AppStrings.Wizard_Bar_Middle, AppStrings.Wizard_Bar_Middle_Desc, b.MiddleToolbar, (v, on) => v with { MiddleToolbar = on }, "bar:middle");
        Bar(AppStrings.Wizard_Bar_FKeys, Loc.Format(AppStrings.Wizard_Bar_FKeys_Desc, KeyMaps.Title(c.Keys)), b.FunctionKeyBar, (v, on) => v with { FunctionKeyBar = on }, "bar:fkeys");
        var bars = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), ColumnSpacing = 24 };
        Grid.SetColumn(ticks, 1);
        bars.Children.Add(preview);
        bars.Children.Add(ticks);

        var stops = LayoutSettings.RowHeightStops;
        var description = c.RowHeight switch
        {
            <= 22 => AppStrings.Wizard_RowHeight_Compact,
            >= 28 => AppStrings.Wizard_RowHeight_Comfortable,
            _ => AppStrings.Wizard_RowHeight_Default,
        };
        var rowHeight = Field(AppStrings.Wizard_RowHeight,
            Pills([.. stops.Select(s => (s.Height, s.Name))], c.RowHeight, (x, h) => x with { RowHeight = h }, "row"),
            Loc.Format(AppStrings.Wizard_RowHeight_Px, c.RowHeight),
            description + " " + Loc.Format(AppStrings.Wizard_RowHeight_More, LayoutSettings.MinRowHeight, LayoutSettings.MaxRowHeight));
        var rows = Columns(rowHeight, RowsPreview(), 24);

        return Page(AppStrings.Wizard_Bars_Title, AppStrings.Wizard_Bars_Sub, bars, new Border { Height = 1, Margin = new Thickness(0, 4) }.Res(Border.BackgroundProperty, "Brush.Border"), rows);
    }

    private Control RowsPreview()
    {
        var c = Choices;
        var format = new DisplayFormat(c.Units, c.DateFormat, CultureInfo.CurrentCulture, c.Time);
        var names = AppStrings.Wizard_Sample_Files.Split('|');
        long?[] sizes = [null, null, 1_288_490_188, 4_096, 655_360];
        DateTime?[] dates = [null, new(2026, 10, 2, 9, 14, 0), new(2026, 8, 14, 18, 25, 0), new(2026, 10, 20, 21, 3, 0), new(2026, 9, 30, 11, 47, 0)];
        var list = new StackPanel();

        Grid Line(string name, string size, string date, bool header, bool current)
        {
            var line = new Grid { ColumnDefinitions = new ColumnDefinitions("*,80,128"), Height = header ? 22 : c.RowHeight };
            var brush = header ? "Brush.SecondaryText" : current ? "Brush.SelectedText" : "Brush.Text";
            var cells = new[] { name, size, date };
            for (var i = 0; i < cells.Length; i++)
            {
                var cell = new TextBlock { Text = cells[i], FontSize = header ? 11.5 : 12, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0), TextTrimming = TextTrimming.CharacterEllipsis, TextAlignment = i == 1 ? TextAlignment.Right : TextAlignment.Left }
                    .Res(TextBlock.ForegroundProperty, brush);
                if (!header)
                {
                    cell.Res(TextBlock.FontFamilyProperty, "Font.Panel");
                }

                Grid.SetColumn(cell, i);
                line.Children.Add(cell);
            }

            if (current)
            {
                line.Res(Panel.BackgroundProperty, "Brush.SelectionBackground");
            }

            return line;
        }

        list.Children.Add(Line(AppStrings.Panel_Col_Name, AppStrings.Panel_Col_Size, AppStrings.Panel_Col_Date, header: true, current: false).Res(Panel.BackgroundProperty, "Brush.ColumnHeader"));
        for (var i = 0; i < names.Length && i < sizes.Length; i++)
        {
            var size = i == 0 ? AppStrings.Wizard_Sample_Up : sizes[i] is { } bytes ? format.Size(bytes) : AppStrings.Wizard_Sample_Folder;
            list.Children.Add(Line(names[i], size, dates[i] is { } d ? format.DateAndTime(d) : "", header: false, current: i == 2));
        }

        var box = new Border { CornerRadius = Corners.Chip, BorderThickness = new Thickness(1), ClipToBounds = true, Child = list, VerticalAlignment = VerticalAlignment.Top }
            .Res(Border.BorderBrushProperty, "Brush.Border").Res(Border.BackgroundProperty, "Brush.PanelBackground");
        Avalonia.Automation.AutomationProperties.SetName(box, Loc.Format(AppStrings.Wizard_RowHeight_Px, c.RowHeight));
        return box;
    }

    private Control PanelsPage()
    {
        var c = Choices;
        var format = new DisplayFormat(c.Units, c.DateFormat, CultureInfo.CurrentCulture, c.Time);
        var sample = new DateTime(2026, 8, 14, 18, 25, 0);

        var sizes = Field(AppStrings.Wizard_Sizes,
            Pills([(SizeUnits.Windows, AppStrings.Opt_SizeUnits_Windows), (SizeUnits.Bytes, AppStrings.Opt_SizeUnits_Bytes), (SizeUnits.Decimal, AppStrings.Opt_SizeUnits_Decimal)], c.Units, (x, u) => x with { Units = u }, "units"),
            format.Size(25_690_112) + " · " + format.Size(2_048),
            c.Units switch
            {
                SizeUnits.Bytes => AppStrings.Wizard_Sizes_Bytes,
                SizeUnits.Decimal => AppStrings.Wizard_Sizes_Decimal,
                _ => AppStrings.Wizard_Sizes_Windows,
            });
        var dates = Field(AppStrings.Wizard_Dates,
            Pills([(PanelSettings.DateFormatIso, AppStrings.Wizard_Dates_Iso), (PanelSettings.DateFormatSystem, AppStrings.Wizard_Dates_Windows)], c.DateFormat, (x, d) => x with { DateFormat = d }, "dates"),
            format.DateAndTime(sample),
            c.DateFormat == PanelSettings.DateFormatIso ? AppStrings.Wizard_Dates_Iso_Desc : AppStrings.Wizard_Dates_Windows_Desc);
        var time = Field(AppStrings.Wizard_Time,
            Pills([(TimeFormat.Windows, AppStrings.Opt_Time_Windows), (TimeFormat.Hours24, AppStrings.Opt_Time_24), (TimeFormat.Hours12, AppStrings.Opt_Time_12)], c.Time, (x, t) => x with { Time = t }, "time"),
            description: c.Time == TimeFormat.Windows ? AppStrings.Wizard_Time_Windows_Desc : AppStrings.Wizard_Time_Desc);

        var start = new StackPanel { Spacing = 10 };
        void Radio(StartFolders value, string title, string description, Control? below = null)
        {
            var text = new StackPanel { Spacing = 1, Children = { Text(title, 13, FontWeight.SemiBold), Secondary(description, 12) } };
            var radio = new RadioButton { GroupName = "start", Content = text, IsChecked = c.Start == value, Tag = "start:" + value, VerticalContentAlignment = VerticalAlignment.Top };
            Avalonia.Automation.AutomationProperties.SetName(radio, title);
            radio.IsCheckedChanged += (_, _) =>
            {
                if (radio.IsChecked == true && Choices.Start != value)
                {
                    Dispatcher.UIThread.Post(() => Change(x => x with
                    {
                        Start = value,
                        StartLeft = value == StartFolders.These && x.StartLeft.Length == 0 && x.StartRight.Length == 0 ? PanelFolders?.Invoke().Left ?? "" : x.StartLeft,
                        StartRight = value == StartFolders.These && x.StartLeft.Length == 0 && x.StartRight.Length == 0 ? PanelFolders?.Invoke().Right ?? "" : x.StartRight,
                    }, radio.Tag));
                }
            };
            start.Children.Add(radio);
            if (below is not null)
            {
                start.Children.Add(below);
            }
        }

        Radio(StartFolders.WhereYouLeft, AppStrings.Opt_StartFolders_WhereYouLeft, FromHelp ? AppStrings.Wizard_Start_Left_Desc : AppStrings.Wizard_Start_Left_Desc + " " + AppStrings.Wizard_Start_Left_First);
        Radio(StartFolders.Home, AppStrings.Opt_StartFolders_Home, AppStrings.Wizard_Start_Home_Desc);
        Radio(StartFolders.These, AppStrings.Opt_StartFolders_These, AppStrings.Wizard_Start_These_Desc, c.Start == StartFolders.These ? FolderFields() : null);

        return Page(AppStrings.Wizard_Panels_Title, AppStrings.Wizard_Panels_Sub, sizes, Columns(dates, time, 24), Field(AppStrings.Opt_StartFolders, start));
    }

    internal Func<(string Left, string Right)>? PanelFolders { get; set; }

    private Control FolderFields()
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), RowDefinitions = new RowDefinitions("Auto,Auto"), ColumnSpacing = 10, RowSpacing = 6, Margin = new Thickness(28, 0, 0, 0) };
        void Field(int row, string label, string value, Func<FirstStepsChoices, string, FirstStepsChoices> set)
        {
            var name = Text(label, 13, brush: "Brush.SecondaryText");
            Grid.SetRow(name, row);
            var box = new TextBox { Text = value, PlaceholderText = AppStrings.Opt_StartFolders_Empty, Tag = "folder:" + row };
            Avalonia.Automation.AutomationProperties.SetName(box, label);
            box.TextChanged += (_, _) => Choices = set(Choices, box.Text ?? "");
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
                var from = Directory.Exists(text) ? await StorageProvider.TryGetFolderFromPathAsync(text) : null;
                var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = label, SuggestedStartLocation = from });
                if (folders.Count > 0 && folders[0].TryGetLocalPath() is { } path)
                {
                    box.Text = path;
                }
            }
        }

        Field(0, AppStrings.Opt_StartFolders_LeftPanel, Choices.StartLeft, (x, v) => x with { StartLeft = v });
        Field(1, AppStrings.Opt_StartFolders_RightPanel, Choices.StartRight, (x, v) => x with { StartRight = v });
        return grid;
    }

    private Control FileManagerPage()
    {
        var toggle = new ToggleSwitch { IsChecked = false, IsEnabled = false, OnContent = AppStrings.Common_On, OffContent = AppStrings.Common_Off, VerticalAlignment = VerticalAlignment.Center };
        Avalonia.Automation.AutomationProperties.SetName(toggle, AppStrings.Wizard_Fm_Box);
        var text = new StackPanel { Spacing = 4, Children = { Text(AppStrings.Wizard_Fm_Box, 14, FontWeight.SemiBold), Rich(AppStrings.Wizard_Fm_Box_Desc, 12.5) } };
        var line = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 16 };
        Grid.SetColumn(toggle, 1);
        line.Children.Add(text);
        line.Children.Add(toggle);

        var title = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, Children = { Text(AppStrings.Wizard_Fm_Title, 20, FontWeight.SemiBold), Badge(AppStrings.Wizard_ComesLater) } };
        return new StackPanel
        {
            Spacing = 14,
            Children =
            {
                new StackPanel { Spacing = 4, Children = { title, Secondary(AppStrings.Wizard_Fm_Sub, 13) } },
                Later(Box(line)),
                Secondary("• " + AppStrings.Wizard_Fm_Off, 12.5),
                Secondary("• " + AppStrings.Wizard_Fm_OnlyYou, 12.5),
            },
        };
    }

    private Control ReadyPage()
    {
        var c = Choices;
        var startWithWindows = Tick(AppStrings.Wizard_Ready_StartWithWindows, AppStrings.Wizard_Ready_StartWithWindows_Desc, c.StartWithWindows,
            on => Choices = Choices with { StartWithWindows = on }, "startWithWindows");
        var updates = Later(Tick(AppStrings.Wizard_Ready_Updates, AppStrings.Wizard_Ready_Updates_Desc, on: false, null, "updates", enabled: false, badge: AppStrings.Wizard_ComesLater));
        var ticks = new StackPanel { Spacing = 10, Children = { startWithWindows, updates } };
        var title = FromHelp ? AppStrings.Wizard_Ready_TitleHelp : AppStrings.Wizard_Ready_Title;

        if (Imported is var (import, parts))
        {
            var kept = Summary([
                (AppStrings.Wizard_Sum_From, Path.GetFileName(import.File)),
                (AppStrings.Wizard_Sum_Parts, PartNames(parts)),
                (AppStrings.Wizard_Sum_Kept, AppStrings.Wizard_Sum_Kept_Value)]);
            return Page(title, FromHelp ? AppStrings.Wizard_Ready_SubHelp : AppStrings.Wizard_Ready_SubImported, kept, ticks, Rich(AppStrings.Wizard_Ready_AgainImported, 12.5));
        }

        var summary = Summary(SummaryLines());
        return Page(title, FromHelp ? AppStrings.Wizard_Ready_SubHelp : AppStrings.Wizard_Ready_Sub, summary, ticks, FromHelp ? null : Rich(AppStrings.Wizard_Ready_Again, 12.5));
    }

    internal IReadOnlyList<(string Label, string Value)> SummaryLines()
    {
        var c = Choices;
        var appearance = _services.Settings.Current.Appearance;
        string Name(string id) => ThemeCatalog.Find(appearance, id).Name;
        var theme = c.FollowWindows ? Loc.Format(AppStrings.Wizard_Sum_Follow, Name(c.Light), Name(c.Dark)) : Name(c.One);
        var accent = c.Accent switch
        {
            AccentChoice.Windows => AppStrings.Wizard_Accent_Windows,
            AccentChoice.Colour => ColorMath.ToHex(c.Colour),
            _ => AppStrings.Wizard_Accent_Theme,
        };

        string keys;
        if (c.Keys == KeyMapName.TotalCommander)
        {
            var with = new List<string>();
            var now = _services.Settings.Current;
            var (quick, right) = SettingsTransferDialog.GoesWith(KeyMapName.TotalCommander, now);
            if (c.TcQuickSearch && now.Panels.QuickSearch != quick)
            {
                with.Add(AppStrings.Wizard_Sum_TcQuick);
            }

            if (c.TcRightButton && now.Mouse.RightClickUnselected != right)
            {
                with.Add(AppStrings.Wizard_Sum_TcRight);
            }

            keys = with.Count == 0 ? AppStrings.Wizard_Sum_Tc : Loc.Format(AppStrings.Wizard_Sum_TcWith, string.Join(" " + AppStrings.Opt_KeyMap_And + " ", with));
        }
        else
        {
            keys = AppStrings.Wizard_Sum_Salamander;
        }

        var b = c.Bars;
        var bars = new List<string>();
        foreach (var (on, name) in new[] { (b.MenuBar, AppStrings.Wizard_Sum_Bar_Menu), (b.DriveStrip, AppStrings.Wizard_Sum_Bar_Drives), (b.HotPathBar, AppStrings.Wizard_Sum_Bar_HotPaths), (b.MiddleToolbar, AppStrings.Wizard_Sum_Bar_Middle), (b.FunctionKeyBar, AppStrings.Wizard_Sum_Bar_FKeys) })
        {
            if (on)
            {
                bars.Add(name);
            }
        }

        var row = LayoutSettings.RowHeightStops.FirstOrDefault(s => s.Height == c.RowHeight).Name ?? Loc.Format(AppStrings.Wizard_RowHeight_Px, c.RowHeight);
        var units = c.Units switch
        {
            SizeUnits.Bytes => AppStrings.Opt_SizeUnits_Bytes,
            SizeUnits.Decimal => AppStrings.Opt_SizeUnits_Decimal,
            SizeUnits.Binary => AppStrings.Wizard_Sum_Binary,
            _ => AppStrings.Opt_SizeUnits_Windows,
        };
        var date = c.DateFormat switch
        {
            PanelSettings.DateFormatIso => AppStrings.Wizard_Dates_Iso,
            PanelSettings.DateFormatSystem => AppStrings.Wizard_Dates_Windows,
            var culture => culture,
        };
        var time = c.Time switch
        {
            TimeFormat.Hours24 => AppStrings.Wizard_Sum_24,
            TimeFormat.Hours12 => AppStrings.Wizard_Sum_12,
            _ => AppStrings.Opt_Time_Windows,
        };
        var start = c.Start switch
        {
            StartFolders.Home => AppStrings.Opt_StartFolders_Home,
            StartFolders.These => (c.StartLeft.Trim() is { Length: > 0 } l ? l : AppStrings.Opt_StartFolders_Empty) + " · " + (c.StartRight.Trim() is { Length: > 0 } r ? r : AppStrings.Opt_StartFolders_Empty),
            _ => AppStrings.Opt_StartFolders_WhereYouLeft,
        };

        return
        [
            (AppStrings.Wizard_Sum_Theme, theme),
            (AppStrings.Wizard_Accent, accent),
            (AppStrings.Wizard_Sum_Keys, keys),
            (AppStrings.Wizard_Sum_Bars, bars.Count == 0 ? AppStrings.Wizard_Sum_None : string.Join(", ", bars)),
            (AppStrings.Wizard_RowHeight, row),
            (AppStrings.Wizard_Sizes, units),
            (AppStrings.Wizard_Sum_DatesTime, date + " · " + time),
            (AppStrings.Opt_StartFolders, start),
        ];
    }

    private static Control Summary(IReadOnlyList<(string Label, string Value)> lines)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), ColumnSpacing = 24, RowSpacing = 8 };
        for (var i = 0; i < lines.Count; i++)
        {
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            var label = Text(lines[i].Label, 13, brush: "Brush.SecondaryText");
            label.VerticalAlignment = VerticalAlignment.Top;
            var value = Text(lines[i].Value, 13, FontWeight.SemiBold, wrap: true);
            Grid.SetRow(label, i);
            Grid.SetRow(value, i);
            Grid.SetColumn(value, 1);
            grid.Children.Add(label);
            grid.Children.Add(value);
        }

        return Box(grid, 16);
    }
}

// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.App/Options/OptionsWindow.Themes.cs

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Salandual.App.Resources;
using Salandual.App.Theme;
using Salandual.Core.Listing;
using Salandual.Core.Resources;
using Salandual.Core.Settings;
using static Salandual.App.Options.OptionsUi;

namespace Salandual.App.Options;

public sealed partial class OptionsWindow
{
    private const string Themes = "themes";

    private string _themesTab = "library";
    private string? _pickedTheme;
    private ThemePreviewView _previewView = ThemePreviewView.Window;
    private string _themeFilter = "all";
    private string _themeSearch = "";

    internal ThemePreview? ThemeBigPreview { get; private set; }

    internal Button? ThemeSearchClear { get; private set; }

    internal Border? ThemeLibraryBar { get; private set; }

    internal List<Button> ThemeCards { get; } = [];

    private void AddThemes()
    {
        Add(Themes, "", null, null, ThemesPage, wide: true, keywords: AppStrings.Themes_Kw);
        Add(Themes, "", AppStrings.Themes_Windows, AppStrings.Themes_Windows_Desc, () => new Control(), keywords: AppStrings.Themes_Schedule_Kw, searchOnly: true);
        Add(Themes, "", AppStrings.OptSec_Background, AppStrings.Themes_Bg_Search_Desc, () => new Control(), keywords: AppStrings.Opt_Bg_Image_Kw, searchOnly: true);
    }

    private IReadOnlyDictionary<string, Rgb> ResolveTheme(ColorTheme theme)
        => AppInstance?.Theme.Resolve(theme) ?? ColorTokens.Resolve(theme);

    private ColorTheme OnScreenTheme => AppInstance?.Theme.ActiveTheme ?? ThemeCatalog.Find(Current.Appearance, Current.Appearance.Schedule.ActiveId(false, 0));

    private Control ThemesPage()
    {
        var panel = new StackPanel { Spacing = 12 };
        var tabs = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
        foreach (var (id, label) in new[] { ("library", AppStrings.Themes_Tab_Library), ("editor", AppStrings.Themes_Tab_Editor), ("schedule", AppStrings.Themes_Tab_Schedule) })
        {
            var on = _themesTab == id;
            var tab = new Button
            {
                Content = Text(label, 14, on ? FontWeight.SemiBold : FontWeight.Normal, on ? "Brush.Text" : "Brush.SecondaryText"),
                Background = Brushes.Transparent,
                Padding = new Thickness(12, 6),
                BorderThickness = new Thickness(0, 0, 0, 3),
            };
            if (on)
            {
                tab.Res(Button.BorderBrushProperty, "Brush.Accent");
            }
            else
            {
                tab.BorderBrush = Brushes.Transparent;
            }

            tab.Click += (_, _) => ShowThemesTab(id);
            tabs.Children.Add(tab);
        }

        panel.Children.Add(tabs);
        panel.Children.Add(_themesTab switch { "schedule" => ScheduleTab(), "editor" => EditorTab(), _ => LibraryTab() });
        return panel;
    }

    private void ShowThemesTab(string id)
    {
        _themesTab = id;
        RebuildLater();
    }

    private Control LibraryTab()
    {
        var a = Current.Appearance;
        var onScreen = OnScreenTheme;
        _pickedTheme ??= onScreen.Id;
        var vsCode = VsCodeGroup();
        var picked = FindTheme(_pickedTheme);
        var panel = new StackPanel { Spacing = 12 };

        var now = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,Auto,*,Auto"), ColumnSpacing = 6 };
        now.Children.Add(Text(AppStrings.Themes_OnScreen, 13));
        now.Children.Add(Text(onScreen.Name, 13, FontWeight.SemiBold).Also(t => Grid.SetColumn(t, 1)));
        now.Children.Add(Text("— " + Why(a.Schedule), 13, brush: "Brush.SecondaryText").Also(t => Grid.SetColumn(t, 2)));
        now.Children.Add(ActionButton(AppStrings.Themes_ScheduleButton, () => ShowThemesTab("schedule")).Also(b => Grid.SetColumn(b, 3)));
        panel.Children.Add(Boxed(now));

        var head = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), ColumnSpacing = 8 };
        var title = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        var bigName = Text(picked.Name, 14, FontWeight.SemiBold);
        var bigKind = Text(Kind(picked), 12, brush: "Brush.TertiaryText");
        title.Children.Add(bigName);
        title.Children.Add(bigKind);
        head.Children.Add(title);
        var preview = new ThemePreview { Colors = ResolveTheme(picked), View = _previewView, Margin = new Thickness(0, 8) };
        ThemeBigPreview = preview;
        head.Children.Add(Segmented(
            [(ThemePreviewView.Window, AppStrings.Themes_View_Window), (ThemePreviewView.Viewer, AppStrings.Themes_View_Viewer), (ThemePreviewView.Dialog, AppStrings.Themes_View_Dialog)],
            _previewView, v =>
            {
                _previewView = v;
                preview.View = v;
            }).Also(s => Grid.SetColumn(s, 2)));
        var actionSets = new List<(Button Use, Button Edit, Button Delete)>();
        StackPanel Actions()
        {
            var use = new Button { Padding = new Thickness(16, 6) };
            use.Classes.Add("accent");
            use.Click += (_, _) => UseNow(FindTheme(_pickedTheme));
            var duplicate = ActionButton(AppStrings.Themes_Duplicate, () =>
            {
                _pickedTheme = DuplicateTheme(FindTheme(_pickedTheme)).Id;
                RebuildLater();
            });
            var edit = ActionButton(AppStrings.Themes_Edit, () => EditTheme(_pickedTheme));
            var export = ActionButton(AppStrings.Themes_Export, () => _ = ExportThemeAsync(FindTheme(_pickedTheme)));
            var delete = ActionButton(AppStrings.Themes_Delete, () => _ = ConfirmDeleteAsync(_pickedTheme));
            delete.Res(Button.ForegroundProperty, "Brush.Danger");
            actionSets.Add((use, edit, delete));
            return new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { use, duplicate, edit, export, delete } };
        }

        void UpdateActions(ColorTheme t)
        {
            var (label, inUse) = UseState(Current.Appearance.Schedule, t);
            foreach (var (use, edit, delete) in actionSets)
            {
                use.Content = inUse ? "✓ " + label : label;
                use.IsEnabled = !inUse;
                edit.Content = t.ReadOnly ? AppStrings.Themes_OpenInEditor : AppStrings.Themes_Edit;
                delete.IsVisible = !t.ReadOnly;
            }
        }

        var actions = Actions();
        ThemeDeleteButton = actionSets[0].Delete;
        preview.Image = PreviewImage(picked);
        preview.DetachedFromVisualTree += (_, _) => preview.Image?.Apply(new BackgroundImageSettings());

        var vsBanner = VsCodeBanner(out var showVsCode);
        showVsCode(picked);
        var big = new StackPanel();
        big.Children.Add(head);
        big.Children.Add(vsBanner);
        big.Children.Add(preview);
        big.Children.Add(actions);
        var bigBox = Boxed(big);
        panel.Children.Add(bigBox);

        if (ThemeNotice() is { } notice)
        {
            panel.Children.Add(notice);
        }

        var tools = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), ColumnSpacing = 8 };
        var import = ActionButton(AppStrings.Themes_Import + " ▾", () => { });
        import.Flyout = ImportMenu();
        tools.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { ActionButton("＋ " + AppStrings.Themes_New, NewTheme), import } });
        var search = new TextBox { PlaceholderText = AppStrings.Themes_Search, Width = 220, HorizontalAlignment = HorizontalAlignment.Right, Text = _themeSearch };
        Grid.SetColumn(search, 1);
        var clear = new Button
        {
            Content = AppIcons.Glyph("Icon.Cross", 10, 10, 1.4).Also(g => g.HorizontalAlignment = HorizontalAlignment.Center),
            Width = 24,
            Height = 24,
            Padding = default,
            Margin = new Thickness(0, 0, 4, 0),
            Background = Brushes.Transparent,
            BorderThickness = default,
            VerticalAlignment = VerticalAlignment.Center,
            Focusable = false,
            IsVisible = _themeSearch.Length > 0,
        };
        ToolTip.SetTip(clear, AppStrings.Themes_ClearSearch);
        Avalonia.Automation.AutomationProperties.SetName(clear, AppStrings.Themes_ClearSearch);
        clear.Click += (_, _) =>
        {
            search.Text = "";
            search.Focus();
        };
        search.InnerRightContent = clear;
        ThemeSearchClear = clear;
        tools.Children.Add(search);
        tools.Children.Add(Segmented([("all", AppStrings.Themes_Filter_All), ("light", AppStrings.Themes_Filter_Light), ("dark", AppStrings.Themes_Filter_Dark)], _themeFilter, f =>
        {
            _themeFilter = f;
            FilterCards();
        }).Also(s => Grid.SetColumn(s, 2)));
        panel.Children.Add(tools);

        ThemeCards.Clear();
        var cards = new StackPanel { Spacing = 12 };
        var barName = Text(picked.Name, 13, FontWeight.SemiBold).Also(t =>
        {
            t.VerticalAlignment = VerticalAlignment.Center;
            t.TextTrimming = TextTrimming.CharacterEllipsis;
        });
        void Pick(ColorTheme t)
        {
            _pickedTheme = t.Id;
            bigName.Text = t.Name;
            barName.Text = t.Name;
            bigKind.Text = Kind(t);
            preview.Colors = ResolveTheme(t);
            preview.Image?.Apply(t.Background with { Path = ThemeFiles.ImagePath(DataDirectory, t) });
            showVsCode(t);
            UpdateActions(t);
            foreach (var card in ThemeCards)
            {
                card.Classes.Set("picked", (string?)card.Tag == t.Id);
            }
        }

        foreach (var (heading, note, list) in new[]
        {
            (AppStrings.Themes_Group_BuiltIn, AppStrings.Themes_ReadOnly, ThemeCatalog.BuiltIn),
            (AppStrings.Themes_Group_Shipped, AppStrings.Themes_Group_Shipped_Note, ThemeCatalog.Shipped),
            (AppStrings.Themes_Group_Mine, AppStrings.Themes_Group_Mine_Note, (IReadOnlyList<ColorTheme>)a.Themes.Where(t => t.Origin == ThemeOrigin.Mine).ToList()),
            (AppStrings.Themes_Group_VsCode, AppStrings.Themes_Group_VsCode_Note, vsCode),
        })
        {
            var mine = heading == AppStrings.Themes_Group_Mine;
            if (list.Count == 0 && !mine)
            {
                continue;
            }

            var group = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(0, 6, 0, 0) };
            group.Children.Add(Text(heading, 13, FontWeight.SemiBold, "Brush.SecondaryText"));
            group.Children.Add(Text(note, 12, brush: "Brush.TertiaryText"));
            cards.Children.Add(group);
            var wrap = new WrapPanel { ItemSpacing = 8, LineSpacing = 8 };
            foreach (var theme in list)
            {
                wrap.Children.Add(ThemeCard(theme, a, onScreen, Pick));
            }

            if (mine)
            {
                wrap.Children.Add(NewThemeCard());
            }

            cards.Children.Add(wrap);
        }

        var barRow = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto"), ColumnSpacing = 8 };
        barRow.Children.Add(barName);
        barRow.Children.Add(Actions().Also(s => Grid.SetColumn(s, 1)));
        var back = ActionButton("↑", () => bigBox.BringIntoView());
        ToolTip.SetTip(back, AppStrings.Themes_BackToPreview);
        Avalonia.Automation.AutomationProperties.SetName(back, AppStrings.Themes_BackToPreview);
        Grid.SetColumn(back, 2);
        barRow.Children.Add(back);
        var bar = new Border { CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(1), Padding = new Thickness(12, 6), Child = barRow, IsVisible = false };
        bar.Res(Border.BackgroundProperty, "Brush.PanelBackground");
        bar.Res(Border.BorderBrushProperty, "Brush.Accent");
        ThemeLibraryBar = bar;
        UpdateActions(picked);
        var area = new Grid { Children = { cards, new StickyPanel { Children = { bar } } } };
        panel.Children.Add(area);
        void ShowBar(object? sender, ScrollChangedEventArgs e)
            => bar.IsVisible = actions.TranslatePoint(new Point(0, actions.Bounds.Height), PageScroll) is { Y: < 0 };
        area.AttachedToVisualTree += (_, _) => PageScroll.ScrollChanged += ShowBar;
        area.DetachedFromVisualTree += (_, _) => PageScroll.ScrollChanged -= ShowBar;

        search.TextChanged += (_, _) =>
        {
            _themeSearch = search.Text ?? "";
            clear.IsVisible = _themeSearch.Length > 0;
            FilterCards();
        };
        FilterCards();
        return panel;
    }

    private void FilterCards()
    {
        var all = AllThemes().ToDictionary(t => t.Id);
        foreach (var card in ThemeCards)
        {
            if ((string?)card.Tag is { } id && all.TryGetValue(id, out var t))
            {
                card.IsVisible = (_themeFilter == "all" || (t.Dark ? "dark" : "light") == _themeFilter)
                                 && t.Name.Contains(_themeSearch.Trim(), StringComparison.CurrentCultureIgnoreCase);
            }
        }
    }

    private Button ThemeCard(ColorTheme theme, AppearanceSettings a, ColorTheme onScreen, Action<ColorTheme> pick)
    {
        var picture = new ThemePreview { Colors = ResolveTheme(theme), Compact = true, Width = 148, IsHitTestVisible = false };
        var frame = new Border { CornerRadius = new CornerRadius(4), ClipToBounds = true, BorderThickness = new Thickness(1), Child = picture };
        frame.Res(Border.BorderBrushProperty, "Brush.Border");

        var badges = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(4) };
        foreach (var (label, strong) in Badges(a.Schedule, theme, onScreen))
        {
            var badge = new Border { CornerRadius = new CornerRadius(999), Padding = new Thickness(6, 0), Child = Text(label, 10.5, FontWeight.SemiBold, strong ? "Brush.OnAccent" : "Brush.Text") };
            badge.Res(Border.BackgroundProperty, strong ? "Brush.Accent" : "Brush.PanelBackground");
            badges.Children.Add(badge);
        }

        var top = new Grid { Children = { frame, badges } };
        var name = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Margin = new Thickness(2, 6, 2, 0) };
        name.Children.Add(Text(theme.Name, 12.5, FontWeight.SemiBold).Also(t => t.TextTrimming = TextTrimming.CharacterEllipsis));
        name.Children.Add(Text(theme.Dark ? AppStrings.Themes_Filter_Dark : AppStrings.Themes_Filter_Light, 11, brush: "Brush.TertiaryText").Also(t => Grid.SetColumn(t, 1)));

        var card = new Button
        {
            Tag = theme.Id,
            Width = 166,
            Padding = new Thickness(8),
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Content = new StackPanel { Children = { top, name } },
            BorderThickness = new Thickness(2),
        };
        card.Classes.Add("themeCard");
        card.Classes.Set("picked", theme.Id == _pickedTheme);
        card.Click += (_, _) => pick(theme);
        card.DoubleTapped += (_, _) => UseNow(theme);
        card.Res(Button.BackgroundProperty, "Brush.WindowBackground");
        card.Classes.CollectionChanged += (_, _) => PaintCard(card);
        PaintCard(card);
        ThemeCards.Add(card);
        return card;
    }

    private static void PaintCard(Button card)
        => card.Res(Button.BorderBrushProperty, card.Classes.Contains("picked") ? "Brush.Accent" : "Brush.Border");

    private static Border Boxed(Control child)
    {
        var box = new Border { CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(1), Padding = new Thickness(12, 10), Child = child };
        box.Res(Border.BorderBrushProperty, "Brush.Border");
        box.Res(Border.BackgroundProperty, "Brush.PanelBackground");
        return box;
    }

    private static string Kind(ColorTheme t)
        => (t.Dark ? AppStrings.Themes_Filter_Dark : AppStrings.Themes_Filter_Light) + " · " + t.Origin switch
        {
            ThemeOrigin.BuiltIn => AppStrings.Themes_Origin_BuiltIn,
            ThemeOrigin.Shipped => AppStrings.Themes_Origin_Shipped,
            ThemeOrigin.VsCode => AppStrings.Themes_Origin_VsCode,
            _ => AppStrings.Themes_Origin_Mine,
        };

    private string Why(ThemeSchedule s) => s.Mode switch
    {
        ScheduleMode.None => AppStrings.Themes_Why_Use,
        ScheduleMode.Windows => AppInstance?.Theme.WindowsDark == true ? AppStrings.Themes_Why_WindowsDark : AppStrings.Themes_Why_WindowsLight,
        _ => AppStrings.Themes_Why_Times,
    };

    private static (string Label, bool InUse) UseState(ThemeSchedule s, ColorTheme t) => s.Mode switch
    {
        ScheduleMode.None => s.Active == t.Id ? (AppStrings.Themes_InUse, true) : (AppStrings.Themes_Use, false),
        ScheduleMode.Windows when t.Dark => s.Dark == t.Id ? (AppStrings.Themes_InUseDark, true) : (AppStrings.Themes_UseDark, false),
        ScheduleMode.Windows => s.Light == t.Id ? (AppStrings.Themes_InUseLight, true) : (AppStrings.Themes_UseLight, false),
        _ => (AppStrings.Themes_UseTimes, false),
    };

    internal static ThemeSchedule UseTheme(ThemeSchedule s, ColorTheme t) => s.Mode switch
    {
        ScheduleMode.None => s with { Active = t.Id },
        ScheduleMode.Windows when t.Dark => s with { Dark = t.Id },
        ScheduleMode.Windows => s with { Light = t.Id },
        _ => s with { Changes = [.. s.Changes.Where(c => c.Minute != 12 * 60), new ThemeChange { Minute = 12 * 60, Theme = t.Id }] },
    };

    private IEnumerable<(string Label, bool Strong)> Badges(ThemeSchedule s, ColorTheme t, ColorTheme onScreen)
    {
        if (onScreen.Id == t.Id)
        {
            yield return (AppStrings.Themes_InUse, true);
        }

        if (s.Mode == ScheduleMode.Windows)
        {
            if (s.Light == t.Id)
            {
                yield return (AppStrings.Themes_Badge_LightMode, false);
            }

            if (s.Dark == t.Id)
            {
                yield return (AppStrings.Themes_Badge_DarkMode, false);
            }
        }
        else if (s.Mode == ScheduleMode.Custom)
        {
            foreach (var c in s.Sorted().Where(c => c.Theme == t.Id))
            {
                yield return (Time(c.Minute), false);
            }
        }
    }

    private string Time(int minute) => DisplayFormat.From(Current.Panels, System.Globalization.CultureInfo.CurrentCulture).TimeOfDay(minute);

    private Control ScheduleTab()
    {
        var s = Current.Appearance.Schedule;
        var a = Current.Appearance;
        var panel = new StackPanel { Spacing = 8 };
        var names = ThemeCatalog.All(a).Select(t => (t.Id, t.Name)).ToList();

        Control ModeCard(ScheduleMode mode, string title, string description, Control? inner)
        {
            var on = s.Mode == mode;
            var radio = new RadioButton { GroupName = "themeSchedule", IsChecked = on, VerticalAlignment = VerticalAlignment.Top };
            radio.IsCheckedChanged += (_, _) =>
            {
                if (radio.IsChecked == true && Current.Appearance.Schedule.Mode != mode)
                {
                    UpdateAppearance(x => x with { Schedule = x.Schedule with { Mode = mode } });
                    RebuildLater();
                }
            };
            var body = new StackPanel { Spacing = 4 };
            body.Children.Add(Text(title, 14, FontWeight.SemiBold));
            body.Children.Add(Secondary(description, 12.5));
            if (inner is not null)
            {
                inner.Margin = new Thickness(0, 6, 0, 0);
                inner.IsEnabled = on;
                inner.Opacity = on ? 1 : 0.5;
                body.Children.Add(inner);
            }

            var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), ColumnSpacing = 6, Children = { radio, body.Also(b => Grid.SetColumn(b, 1)) } };
            var box = Boxed(grid);
            box.Res(Border.BorderBrushProperty, on ? "Brush.Accent" : "Brush.Border");
            return box;
        }

        var none = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        none.Children.Add(Text(Loc.Format(AppStrings.Themes_Now, ThemeCatalog.Find(a, s.Active).Name), 13));
        none.Children.Add(ActionButton(AppStrings.Themes_LibraryButton, () => ShowThemesTab("library")));
        panel.Children.Add(ModeCard(ScheduleMode.None, AppStrings.Themes_None, AppStrings.Themes_None_Desc, none));

        var windows = new WrapPanel { ItemSpacing = 16, LineSpacing = 8 };
        windows.Children.Add(Labeled(AppStrings.Themes_WhenLight, Combo(names, s.Light, id => UpdateAppearance(x => x with { Schedule = x.Schedule with { Light = id } }), 220)));
        windows.Children.Add(Labeled(AppStrings.Themes_WhenDark, Combo(names, s.Dark, id => UpdateAppearance(x => x with { Schedule = x.Schedule with { Dark = id } }), 220)));
        panel.Children.Add(ModeCard(ScheduleMode.Windows, AppStrings.Themes_Windows, AppStrings.Themes_Windows_Desc, windows));

        var times = new StackPanel { Spacing = 6 };
        var timeline = new ScheduleTimeline
        {
            Changes = s.Sorted(),
            Paint = id => ResolveTheme(ThemeCatalog.Find(Current.Appearance, id)),
            NameOf = id => ThemeCatalog.Find(Current.Appearance, id).Name,
            TimeOf = Time,
            Height = 46,
            Margin = new Thickness(0, 4, 0, 18),
        };
        timeline.Moved += (index, minute) =>
        {
            var sorted = Current.Appearance.Schedule.Sorted().ToList();
            sorted[index] = sorted[index] with { Minute = minute };
            UpdateAppearance(x => x with { Schedule = x.Schedule with { Changes = sorted } });
        };
        timeline.Released += RebuildLater;
        times.Children.Add(timeline);
        var sortedChanges = s.Sorted();
        for (var i = 0; i < sortedChanges.Count; i++)
        {
            var index = i;
            var change = sortedChanges[i];
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            var twelve = DisplayFormat.From(Current.Panels, System.Globalization.CultureInfo.CurrentCulture).TwelveHour;
            var time = new TimePicker { ClockIdentifier = twelve ? "12HourClock" : "24HourClock", MinuteIncrement = 5, SelectedTime = TimeSpan.FromMinutes(change.Minute), MinWidth = 0, Width = 140 };
            time.SelectedTimeChanged += (_, e) =>
            {
                if (e.NewTime is { } t)
                {
                    SetChange(index, c => c with { Minute = (int)t.TotalMinutes });
                }
            };
            row.Children.Add(time);
            row.Children.Add(Text("→", 13, brush: "Brush.SecondaryText"));
            row.Children.Add(Combo(names, change.Theme, id => SetChange(index, c => c with { Theme = id }), 220));
            row.Children.Add(ActionButton(AppStrings.Themes_RemoveChange, () =>
            {
                UpdateAppearance(x => x with { Schedule = x.Schedule with { Changes = [.. x.Schedule.Sorted().Where((_, j) => j != index)] } });
                RebuildLater();
            }, enabled: sortedChanges.Count > 1));
            times.Children.Add(row);
        }

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        buttons.Children.Add(ActionButton("＋ " + AppStrings.Themes_AddChange, () =>
        {
            var free = Enumerable.Range(0, 24).Select(h => h * 60).FirstOrDefault(m => Current.Appearance.Schedule.Changes.All(c => c.Minute != m), 0);
            UpdateAppearance(x => x with { Schedule = x.Schedule with { Changes = [.. x.Schedule.Changes, new ThemeChange { Minute = free, Theme = OnScreenTheme.Id }] } });
            RebuildLater();
        }));
        var defaults = new ThemeSchedule().Changes;
        buttons.Children.Add(ActionButton(AppStrings.Themes_DefaultTimes, () =>
        {
            UpdateAppearance(x => x with { Schedule = x.Schedule with { Changes = new ThemeSchedule().Changes } });
            RebuildLater();
        }, enabled: !sortedChanges.SequenceEqual(defaults)));
        times.Children.Add(buttons);
        panel.Children.Add(ModeCard(ScheduleMode.Custom, AppStrings.Themes_Times, AppStrings.Themes_Times_Desc, times));
        panel.Children.Add(Secondary(AppStrings.Themes_Schedule_Note, 12.5));
        return panel;
    }

    private void SetChange(int index, Func<ThemeChange, ThemeChange> change)
    {
        var sorted = Current.Appearance.Schedule.Sorted().ToList();
        if (index < sorted.Count)
        {
            sorted[index] = change(sorted[index]);
            UpdateAppearance(x => x with { Schedule = x.Schedule with { Changes = sorted } });
            RebuildLater();
        }
    }

    private static Control Labeled(string label, Control control)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        row.Children.Add(Text(label, 13));
        row.Children.Add(control);
        return row;
    }
}

internal sealed class ScheduleTimeline : Control
{
    private int _dragging = -1;

    public IReadOnlyList<ThemeChange> Changes { get; set; } = [];

    public Func<string, IReadOnlyDictionary<string, Rgb>> Paint { get; set; } = _ => ColorTokens.Resolve(ThemeCatalog.BuiltIn[0]);

    public Func<string, string> NameOf { get; set; } = id => id;

    public Func<int, string> TimeOf { get; set; } = minute => $"{minute / 60:00}:{minute % 60:00}";

    public event Action<int, int>? Moved;

    public event Action? Released;

    public ScheduleTimeline()
    {
        Cursor = new Cursor(StandardCursorType.SizeWestEast);
    }

    public override void Render(DrawingContext context)
    {
        var w = Bounds.Width;
        var h = Bounds.Height;
        if (Changes.Count == 0 || w <= 0)
        {
            return;
        }

        var stretches = new List<(int From, int To, string Theme)>();
        var from = 0;
        var theme = Changes[^1].Theme;
        foreach (var c in Changes)
        {
            if (c.Minute > from)
            {
                stretches.Add((from, c.Minute, theme));
            }

            from = c.Minute;
            theme = c.Theme;
        }

        stretches.Add((from, 1440, theme));
        foreach (var (start, end, id) in stretches)
        {
            var colors = Paint(id);
            var rect = new Rect(start * w / 1440, 0, (end - start) * w / 1440, h);
            context.FillRectangle(Brush(colors[ColorTokens.PanelBackground]), rect);
            var label = new FormattedText(NameOf(id), System.Globalization.CultureInfo.CurrentCulture, FlowDirection.LeftToRight, new Typeface("Segoe UI"), 11.5, Brush(colors[ColorTokens.Text]));
            if (label.Width + 12 < rect.Width)
            {
                context.DrawText(label, new Point(rect.Center.X - (label.Width / 2), (h - label.Height) / 2));
            }
        }

        var ink = Brush(Paint(Changes[0].Theme)[ColorTokens.Text]);
        context.DrawRectangle(null, new Pen(new SolidColorBrush(Color.FromArgb(0x60, 0x80, 0x80, 0x80))), new Rect(0.5, 0.5, w - 1, h - 1));
        foreach (var c in Changes)
        {
            var x = c.Minute * w / 1440;
            context.FillRectangle(Brushes.White, new Rect(x - 2.5, -3, 5, h + 6));
            context.FillRectangle(Brushes.Black, new Rect(x - 1.5, -3, 3, h + 6));
            var time = new FormattedText(TimeOf(c.Minute), System.Globalization.CultureInfo.CurrentCulture, FlowDirection.LeftToRight, new Typeface("Cascadia Mono, Consolas"), 11, ink);
            context.DrawText(time, new Point(Math.Clamp(x - (time.Width / 2), 0, w - time.Width), h + 3));
        }
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        var x = e.GetPosition(this).X;
        var best = -1;
        var bestDistance = 8.0;
        for (var i = 0; i < Changes.Count; i++)
        {
            var d = Math.Abs((Changes[i].Minute * Bounds.Width / 1440) - x);
            if (d < bestDistance)
            {
                (best, bestDistance) = (i, d);
            }
        }

        if (best >= 0)
        {
            _dragging = best;
            e.Pointer.Capture(this);
            e.Handled = true;
        }
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (_dragging < 0 || Bounds.Width <= 0)
        {
            return;
        }

        var low = _dragging > 0 ? Changes[_dragging - 1].Minute + 15 : 0;
        var high = _dragging < Changes.Count - 1 ? Changes[_dragging + 1].Minute - 15 : 1425;
        var minute = (int)Math.Round(e.GetPosition(this).X / Bounds.Width * 1440 / 15) * 15;
        minute = Math.Clamp(minute, low, Math.Max(low, high));
        if (Changes[_dragging].Minute == minute)
        {
            return;
        }

        Changes = [.. Changes.Select((c, i) => i == _dragging ? c with { Minute = minute } : c)];
        InvalidateVisual();
        Moved?.Invoke(_dragging, minute);
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (_dragging >= 0)
        {
            _dragging = -1;
            e.Pointer.Capture(null);
            Released?.Invoke();
        }
    }

    private static SolidColorBrush Brush(Rgb c) => new(Color.FromRgb(c.R, c.G, c.B));
}

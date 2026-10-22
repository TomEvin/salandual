// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.App/Options/OptionsWindow.ThemeEditor.cs

using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Salandual.App.Resources;
using Salandual.App.Theme;
using Salandual.Core.Resources;
using Salandual.Core.Settings;
using static Salandual.App.Options.OptionsUi;

namespace Salandual.App.Options;

public sealed partial class OptionsWindow
{
    private string? _editedTheme;
    private string _editorTab = "colors";
    private string? _backgroundError;

    internal ThemePreview? ThemeLivePreview { get; private set; }

    internal Dictionary<string, (Button Swatch, TextBox Hex, Button Reset)> ThemeColorRows { get; } = new(StringComparer.Ordinal);

    internal Button? ThemeDeleteButton { get; private set; }

    internal TextBox? ThemeNameBox { get; private set; }

    internal StickyPanel? ThemeLiveSticky { get; private set; }

    internal Dialogs.ConfirmDialog? ThemeDeleteQuestion { get; private set; }

    private const double NarrowEditor = 720;

    private string DataDirectory => _services.DataDirectory;

    internal void EditTheme(string? id)
    {
        _editedTheme = id;
        ShowThemesTab("editor");
    }

    internal void NewTheme()
    {
        var theme = ThemeEditing.New(Current.Appearance, DateTime.Now);
        UpdateAppearance(a => ThemeEditing.Add(a, theme));
        _pickedTheme = theme.Id;
        _editorTab = "colors";
        EditTheme(theme.Id);
    }

    internal ColorTheme DuplicateTheme(ColorTheme source)
    {
        var copy = ThemeEditing.Duplicate(source, Current.Appearance, DateTime.Now);
        if (copy.Background.Path is not null && !ThemeFiles.CopyImageTo(DataDirectory, source, copy.Id))
        {
            copy = copy with { Background = copy.Background with { Path = null } };
        }

        UpdateAppearance(a => ThemeEditing.Add(a, copy));
        return copy;
    }

    internal void DeleteTheme(string? id)
    {
        if (id is null || ThemeCatalog.TryFind(Current.Appearance, id) is not { ReadOnly: false })
        {
            return;
        }

        UpdateAppearance(a => ThemeEditing.Remove(a, id));
        ThemeFiles.DeleteFolder(DataDirectory, id);
        if (_editedTheme == id)
        {
            _editedTheme = null;
        }
    }

    internal async Task ConfirmDeleteAsync(string? id)
    {
        if (id is null || ThemeCatalog.TryFind(Current.Appearance, id) is not { ReadOnly: false } theme)
        {
            return;
        }

        var message = Loc.Format(theme.Background.Path is null ? AppStrings.Themes_DeleteAsk_Text : AppStrings.Themes_DeleteAsk_TextImage, theme.Name);
        if (Current.Appearance.Schedule.Uses(id))
        {
            message += " " + AppStrings.Themes_DeleteAsk_InUse;
        }

        var ask = ThemeDeleteQuestion = new Dialogs.ConfirmDialog(AppStrings.Themes_DeleteAsk_Title, message, AppStrings.Themes_Delete, AppStrings.Common_Cancel);
        ask.Yes.Res(Button.ForegroundProperty, "Brush.Danger");
        var yes = await ask.ShowDialog<bool>(this);
        ThemeDeleteQuestion = null;
        if (yes)
        {
            DeleteTheme(id);
            _pickedTheme = OnScreenTheme.Id;
            RebuildLater();
        }
    }

    private BackgroundImageService PreviewImage(ColorTheme theme)
    {
        var image = new BackgroundImageService();
        image.Apply(theme.Background with { Path = ThemeFiles.ImagePath(DataDirectory, theme) });
        return image;
    }

    private void ChangeTheme(string id, Func<ColorTheme, ColorTheme> change)
        => UpdateAppearance(a => ThemeEditing.Change(a, id, change));

    private Button NewThemeCard()
    {
        var picture = new Border
        {
            Height = 148 * 410 / 720.0,
            CornerRadius = new CornerRadius(4),
            BorderThickness = new Thickness(1),
            Child = Text("＋ " + AppStrings.Themes_New, 13, brush: "Brush.SecondaryText").Also(t =>
            {
                t.HorizontalAlignment = HorizontalAlignment.Center;
                t.VerticalAlignment = VerticalAlignment.Center;
            }),
        };
        picture.Res(Border.BorderBrushProperty, "Brush.BorderStrong");
        picture.Res(Border.BackgroundProperty, "Brush.PanelBackground");
        var name = Text(AppStrings.Themes_NewCard_Desc, 12, brush: "Brush.SecondaryText").Also(t =>
        {
            t.Margin = new Thickness(2, 6, 2, 0);
            t.TextTrimming = TextTrimming.CharacterEllipsis;
        });
        var card = new Button
        {
            Width = 166,
            Padding = new Thickness(8),
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Content = new StackPanel { Children = { picture, name } },
            BorderThickness = new Thickness(2),
        };
        card.Res(Button.BackgroundProperty, "Brush.WindowBackground");
        card.Res(Button.BorderBrushProperty, "Brush.Border");
        Avalonia.Automation.AutomationProperties.SetName(card, AppStrings.Themes_New);
        card.Click += (_, _) => NewTheme();
        return card;
    }

    private Control EditorTab()
    {
        var a = Current.Appearance;
        var theme = FindTheme(_editedTheme ?? _pickedTheme ?? OnScreenTheme.Id);
        _editedTheme = theme.Id;
        var readOnly = theme.ReadOnly;
        var panel = new StackPanel { Spacing = 12 };
        ThemeColorRows.Clear();

        var head = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), RowDefinitions = new RowDefinitions("Auto,Auto"), ColumnSpacing = 12, RowSpacing = 8 };
        head.Children.Add(Text(AppStrings.Themes_Ed_Theme, 12.5, brush: "Brush.SecondaryText").Also(t => t.VerticalAlignment = VerticalAlignment.Center));
        var choose = Combo(AllThemes().Select(t => (t.Id, t.Name)).ToList(), theme.Id, id =>
        {
            _editedTheme = id;
            RebuildLater();
        }, 260);
        Grid.SetColumn(choose, 1);
        head.Children.Add(choose);
        var commands = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        commands.Children.Add(ActionButton(AppStrings.Themes_Duplicate, () => EditTheme(DuplicateTheme(FindTheme(_editedTheme)).Id)));
        commands.Children.Add(ActionButton(AppStrings.Themes_Export, () => _ = ExportThemeAsync(FindTheme(_editedTheme))));
        Grid.SetColumn(commands, 2);
        head.Children.Add(commands);
        head.Children.Add(Text(AppStrings.Themes_Ed_Name, 12.5, brush: "Brush.SecondaryText").Also(t =>
        {
            t.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetRow(t, 1);
        }));
        var name = new TextBox { Text = theme.Name, IsEnabled = !readOnly, MaxWidth = 360, HorizontalAlignment = HorizontalAlignment.Left, MinWidth = 260 };
        ThemeNameBox = name;
        void Rename()
        {
            var current = ThemeCatalog.Find(Current.Appearance, theme.Id);
            var renamed = ThemeEditing.Rename(current, name.Text ?? "", Current.Appearance);
            if (renamed.Name != current.Name)
            {
                ChangeTheme(theme.Id, _ => renamed);
            }

            name.Text = renamed.Name;
        }

        name.LostFocus += (_, _) => Rename();
        name.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter)
            {
                Rename();
                e.Handled = true;
            }
        };
        Grid.SetRow(name, 1);
        Grid.SetColumn(name, 1);
        head.Children.Add(name);
        panel.Children.Add(head);

        var banner = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 10 };
        banner.Children.Add(Text(readOnly ? Loc.Format(theme.Origin switch
        {
            ThemeOrigin.Shipped => AppStrings.Themes_Ed_ReadOnly_Shipped,
            ThemeOrigin.VsCode => AppStrings.Themes_Ed_ReadOnly_VsCode,
            _ => AppStrings.Themes_Ed_ReadOnly_BuiltIn,
        }, theme.Name) : AppStrings.Themes_Ed_Mine, 13, wrap: true).Also(t => t.VerticalAlignment = VerticalAlignment.Center));
        if (readOnly)
        {
            var duplicate = new Button { Content = AppStrings.Themes_Ed_DuplicateToChange, Padding = new Thickness(12, 5) };
            duplicate.Classes.Add("accent");
            duplicate.Click += (_, _) => EditTheme(DuplicateTheme(FindTheme(_editedTheme)).Id);
            Grid.SetColumn(duplicate, 1);
            banner.Children.Add(duplicate);
        }

        var bannerBox = new Border { CornerRadius = new CornerRadius(6), Padding = new Thickness(12, 8), Child = banner };
        bannerBox.Res(Border.BackgroundProperty, readOnly ? "Brush.TextSelection" : "Brush.PanelBackground");
        panel.Children.Add(bannerBox);

        var pills = new WrapPanel { ItemSpacing = 4, LineSpacing = 4 };
        foreach (var (id, label) in new[] { ("colors", AppStrings.Themes_Ed_Colors), ("background", AppStrings.Themes_Ed_Background), ("icons", AppStrings.Themes_Ed_Icons), ("settings", AppStrings.Themes_Ed_Settings) })
        {
            var on = _editorTab == id;
            var pill = new Button { Content = Text(label, 12.5, brush: on ? "Brush.Text" : "Brush.SecondaryText"), CornerRadius = new CornerRadius(999), Padding = new Thickness(12, 3), BorderThickness = new Thickness(1) };
            pill.Res(Button.BackgroundProperty, on ? "Brush.TextSelection" : "Brush.PanelBackground");
            pill.Res(Button.BorderBrushProperty, on ? "Brush.Accent" : "Brush.Border");
            pill.Click += (_, _) =>
            {
                _editorTab = id;
                RebuildLater();
            };
            pills.Children.Add(pill);
        }

        panel.Children.Add(pills);

        var live = new ThemePreview { Colors = ResolveTheme(theme), View = _previewView, Image = PreviewImage(theme) };
        live.DetachedFromVisualTree += (_, _) => live.Image?.Apply(new BackgroundImageSettings());
        ThemeLivePreview = live;
        void Refresh()
        {
            var current = FindTheme(theme.Id);
            live.Colors = ResolveTheme(current);
            live.Image?.Apply(current.Background with { Path = ThemeFiles.ImagePath(DataDirectory, current) });
        }

        Control body = _editorTab switch
        {
            "background" => BackgroundEditor(theme, Refresh),
            "icons" => IconsEditor(),
            "settings" => SettingsEditor(theme, Refresh),
            _ => ColorsEditor(theme, Refresh),
        };
        if (readOnly)
        {
            body.IsEnabled = false;
            body.Opacity = 0.55;
        }

        var liveHead = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Margin = new Thickness(0, 0, 0, 6) };
        liveHead.Children.Add(Text(AppStrings.Themes_Ed_Live, 12, brush: "Brush.SecondaryText").Also(t => t.VerticalAlignment = VerticalAlignment.Center));
        liveHead.Children.Add(Segmented(
            [(ThemePreviewView.Window, AppStrings.Themes_View_Window), (ThemePreviewView.Viewer, AppStrings.Themes_View_Viewer), (ThemePreviewView.Dialog, AppStrings.Themes_View_Dialog)],
            _previewView, v =>
            {
                _previewView = v;
                live.View = v;
            }).Also(s => Grid.SetColumn(s, 1)));
        var liveFrame = new Border { CornerRadius = new CornerRadius(4), ClipToBounds = true, Child = live };
        var liveBox = Boxed(new StackPanel { Children = { liveHead, liveFrame } });
        liveBox.Width = 330;
        liveBox.HorizontalAlignment = HorizontalAlignment.Left;

        var sticky = new StickyPanel { Children = { liveBox } };
        ThemeLiveSticky = sticky;
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), RowDefinitions = new RowDefinitions("Auto,Auto") };
        grid.Children.Add(body);
        grid.Children.Add(sticky);
        void Arrange(double width)
        {
            var narrow = width < NarrowEditor;
            Grid.SetColumn(sticky, narrow ? 0 : 1);
            Grid.SetRow(body, narrow ? 1 : 0);
            grid.ColumnSpacing = narrow ? 0 : 16;
            grid.RowSpacing = narrow ? 12 : 0;
        }

        Arrange(double.PositiveInfinity);
        grid.SizeChanged += (_, e) => Arrange(e.NewSize.Width);
        panel.Children.Add(grid);
        return panel;
    }

    private Control ColorsEditor(ColorTheme theme, Action refresh)
    {
        var panel = new StackPanel { Spacing = 6 };
        var id = theme.Id;
        IReadOnlyDictionary<string, Rgb> Colors() => ResolveTheme(ThemeCatalog.Find(Current.Appearance, id));
        var mainSwatches = new List<(string Token, Button Swatch, TextBlock Hex)>();

        void Update()
        {
            var t = ThemeCatalog.Find(Current.Appearance, id);
            var colors = ResolveTheme(t);
            foreach (var (token, swatch, hex) in mainSwatches)
            {
                swatch.Content = Swatch(colors[token], 36).Also(s => s.Width = double.NaN);
                hex.Text = ColorMath.ToHex(colors[token]);
            }

            foreach (var (token, (swatch, hex, reset)) in ThemeColorRows)
            {
                swatch.Content = Swatch(colors[token], 18);
                if (!hex.IsFocused)
                {
                    hex.Text = ColorMath.ToHex(colors[token]);
                }

                reset.IsEnabled = !t.ReadOnly && ThemeEditing.IsChanged(t, token);
            }

            refresh();
        }

        void Set(string token, Rgb color)
        {
            ChangeTheme(id, t => ThemeEditing.SetColor(t, token, color));
            Update();
        }

        var main = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*,*,*,*"), ColumnSpacing = 8 };
        var colors = Colors();
        for (var i = 0; i < ThemeEditing.Main.Count; i++)
        {
            var (token, label) = ThemeEditing.Main[i];
            var hex = new TextBlock { Text = ColorMath.ToHex(colors[token]), FontSize = 11.5, FontFamily = new FontFamily("Cascadia Mono, Consolas") }.Res(TextBlock.ForegroundProperty, "Brush.Text");
            var swatch = ColorButton(() => Colors()[token], c => Set(token, c), label, 36);
            swatch.HorizontalAlignment = HorizontalAlignment.Stretch;
            swatch.HorizontalContentAlignment = HorizontalAlignment.Stretch;
            swatch.Content = Swatch(colors[token], 36).Also(s => s.Width = double.NaN);
            mainSwatches.Add((token, swatch, hex));
            var cell = new StackPanel { Spacing = 4, Children = { Text(label, 12, brush: "Brush.SecondaryText"), swatch, hex } };
            Grid.SetColumn(cell, i);
            main.Children.Add(cell);
        }

        panel.Children.Add(main);
        var fill = ActionButton(AppStrings.Themes_Ed_Fill, () =>
        {
            ChangeTheme(id, t => ThemeEditing.FillFromMain(t, ResolveTheme(t)));
            Update();
        });
        fill.HorizontalAlignment = HorizontalAlignment.Left;
        fill.Margin = new Thickness(0, 4, 0, 0);
        panel.Children.Add(fill);
        panel.Children.Add(Secondary(AppStrings.Themes_Ed_Fill_Note, 12));

        foreach (var (title, tokens, open) in new[]
        {
            (AppStrings.Themes_Ed_Group_Window, ColorTokens.Window, true),
            (AppStrings.Themes_Ed_Group_Viewers, ColorTokens.Viewer, false),
            (AppStrings.Themes_Ed_Group_Code, ColorTokens.Syntax, false),
            (AppStrings.Themes_Ed_Group_Compare, ColorTokens.Compare, false),
        })
        {
            var rows = new StackPanel { Spacing = 2 };
            foreach (var (token, label) in tokens)
            {
                rows.Children.Add(ColorRow(theme, token, label, colors[token], Set, () =>
                {
                    ChangeTheme(id, t => ThemeEditing.ResetColor(t, token));
                    Update();
                }));
            }

            var header = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
            header.Children.Add(Text(title, 13, FontWeight.SemiBold, "Brush.SecondaryText"));
            header.Children.Add(Text(Loc.Format(AppStrings.Themes_Ed_Count, tokens.Count), 12, brush: "Brush.TertiaryText").Also(t => t.VerticalAlignment = VerticalAlignment.Center));
            panel.Children.Add(new Expander
            {
                Header = header,
                Content = rows,
                IsExpanded = open,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Margin = new Thickness(0, 6, 0, 0),
            });
        }

        return panel;
    }

    private Control ColorRow(ColorTheme theme, string token, string label, Rgb color, Action<string, Rgb> set, Action reset)
    {
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto,Auto"), ColumnSpacing = 6 };
        row.Children.Add(Text(label, 13).Also(t =>
        {
            t.VerticalAlignment = VerticalAlignment.Center;
            t.TextTrimming = TextTrimming.CharacterEllipsis;
        }));
        var swatch = ColorButton(() => ResolveTheme(ThemeCatalog.Find(Current.Appearance, theme.Id))[token], c => set(token, c), label, 18);
        Grid.SetColumn(swatch, 1);
        row.Children.Add(swatch);
        var hex = new TextBox { Text = ColorMath.ToHex(color), Width = 86, MinWidth = 0, FontFamily = new FontFamily("Cascadia Mono, Consolas"), FontSize = 12 };
        Avalonia.Automation.AutomationProperties.SetName(hex, label);
        void Commit()
        {
            if (ColorMath.TryParseHex(hex.Text, out var c))
            {
                if (c != ResolveTheme(ThemeCatalog.Find(Current.Appearance, theme.Id))[token])
                {
                    set(token, c);
                }
            }
            else
            {
                hex.Text = ColorMath.ToHex(ResolveTheme(ThemeCatalog.Find(Current.Appearance, theme.Id))[token]);
            }
        }

        hex.LostFocus += (_, _) => Commit();
        hex.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter)
            {
                Commit();
                e.Handled = true;
            }
        };
        Grid.SetColumn(hex, 2);
        row.Children.Add(hex);
        var undo = new Button
        {
            Content = Text("↺", 13, brush: "Brush.SecondaryText"),
            Background = Brushes.Transparent,
            BorderThickness = default,
            Padding = new Thickness(6, 2),
            IsEnabled = !theme.ReadOnly && ThemeEditing.IsChanged(theme, token),
        };
        ToolTip.SetTip(undo, AppStrings.Themes_Ed_Reset);
        Avalonia.Automation.AutomationProperties.SetName(undo, AppStrings.Themes_Ed_Reset);
        undo.Click += (_, _) => reset();
        Grid.SetColumn(undo, 3);
        row.Children.Add(undo);
        ThemeColorRows[token] = (swatch, hex, undo);
        return row;
    }

    private Control BackgroundEditor(ColorTheme theme, Action refresh)
    {
        var panel = new StackPanel { Spacing = 10 };
        var b = theme.Background;
        var has = b.Path is not null;
        var path = ThemeFiles.ImagePath(DataDirectory, theme);

        var picture = new Border { Height = 150, CornerRadius = new CornerRadius(6), BorderThickness = new Thickness(1), ClipToBounds = true };
        picture.Res(Border.BorderBrushProperty, "Brush.BorderStrong");
        picture.Res(Border.BackgroundProperty, "Brush.PanelBackground");
        if (has && path is not null)
        {
            var image = new Image { Stretch = Stretch.UniformToFill };
            var caption = new Border { CornerRadius = new CornerRadius(999), Padding = new Thickness(8, 2), Margin = new Thickness(8), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Bottom, Background = new SolidColorBrush(Color.FromArgb(0x73, 0, 0, 0)) };
            caption.Child = new TextBlock { Text = b.Path, FontSize = 12, Foreground = Brushes.White };
            picture.Child = new Grid { Children = { image, caption } };
            _ = Task.Run(() =>
            {
                try
                {
                    using var stream = File.OpenRead(path);
                    var bitmap = Bitmap.DecodeToWidth(stream, 640);
                    Dispatcher.UIThread.Post(() => image.Source = bitmap);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException or InvalidOperationException)
                {
                    Dispatcher.UIThread.Post(() => caption.Child = new TextBlock { Text = AppStrings.Bg_Unsupported, FontSize = 12, Foreground = Brushes.White });
                }
            });
        }
        else
        {
            picture.Child = Secondary(AppStrings.Themes_Bg_None, 13).Also(t =>
            {
                t.HorizontalAlignment = HorizontalAlignment.Center;
                t.VerticalAlignment = VerticalAlignment.Center;
                t.TextAlignment = TextAlignment.Center;
                t.Margin = new Thickness(12);
            });
        }

        DragDrop.SetAllowDrop(picture, true);
        picture.AddHandler(DragDrop.DragOverEvent, (_, e) =>
        {
            e.DragEffects = e.DataTransfer.Contains(Avalonia.Input.DataFormat.File) ? DragDropEffects.Copy : DragDropEffects.None;
            e.Handled = true;
        });
        picture.AddHandler(DragDrop.DropEvent, (_, e) =>
        {
            if (e.DataTransfer.TryGetFiles() is { Length: > 0 } files && files[0].TryGetLocalPath() is { } dropped)
            {
                SetBackground(theme.Id, dropped);
            }

            e.Handled = true;
        });
        panel.Children.Add(picture);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        buttons.Children.Add(ActionButton(AppStrings.Opt_Bg_Choose, () => _ = ChooseBackgroundAsync(theme.Id)));
        buttons.Children.Add(ActionButton(AppStrings.Common_Remove, () =>
        {
            ChangeTheme(theme.Id, t => t with { Background = t.Background with { Path = null } });
            ThemeFiles.DeleteImagesExcept(DataDirectory, theme.Id, null);
            RebuildLater();
        }, has));
        panel.Children.Add(buttons);
        if (_backgroundError is { } error)
        {
            panel.Children.Add(Text(error, 12.5, brush: "Brush.Warning", wrap: true));
            _backgroundError = null;
        }

        void Background(Func<BackgroundImageSettings, BackgroundImageSettings> change)
        {
            ChangeTheme(theme.Id, t => t with { Background = change(t.Background) });
            refresh();
        }

        var options = new StackPanel { Spacing = 10, IsEnabled = has };
        options.Children.Add(EditorRow(AppStrings.Opt_Bg_ShowBehind, AppStrings.Opt_Bg_ShowBehind_Desc, Segmented(
            [(BackgroundPlacement.Panels, AppStrings.Opt_Bg_Panels), (BackgroundPlacement.Window, AppStrings.Opt_Bg_Window)], b.Placement, p => Background(x => x with { Placement = p }))));
        options.Children.Add(EditorRow(AppStrings.Opt_Bg_Fit, null, Segmented(
            [(BackgroundFit.Fill, AppStrings.Opt_Bg_Fill), (BackgroundFit.Fit, AppStrings.Opt_Bg_Fit), (BackgroundFit.Stretch, AppStrings.Opt_Bg_Stretch), (BackgroundFit.Tile, AppStrings.Opt_Bg_Tile), (BackgroundFit.Center, AppStrings.Opt_Bg_Center)],
            b.Fit, f => Background(x => x with { Fit = f }))));
        options.Children.Add(EditorRow(AppStrings.Opt_Bg_Position, null, Combo(
            [(BackgroundPosition.Center, AppStrings.Opt_Bg_Center), (BackgroundPosition.TopLeft, AppStrings.Opt_Bg_TopLeft), (BackgroundPosition.TopRight, AppStrings.Opt_Bg_TopRight),
             (BackgroundPosition.BottomLeft, AppStrings.Opt_Bg_BottomLeft), (BackgroundPosition.BottomRight, AppStrings.Opt_Bg_BottomRight)],
            b.Position, p => Background(x => x with { Position = p }), 160)));
        var label = Text(Percent(b.Opacity), 13);
        label.MinWidth = 44;
        label.VerticalAlignment = VerticalAlignment.Center;
        var slider = new Slider
        {
            Minimum = BackgroundImageSettings.MinOpacity * 100,
            Maximum = BackgroundImageSettings.MaxOpacity * 100,
            Value = Math.Round(b.Opacity * 100),
            TickFrequency = 1,
            IsSnapToTickEnabled = true,
            Width = 160,
        };
        slider.ValueChanged += (_, e) =>
        {
            var opacity = Math.Round(e.NewValue) / 100;
            label.Text = Percent(opacity);
            Background(x => x with { Opacity = opacity });
        };
        options.Children.Add(EditorRow(AppStrings.Opt_Bg_Blur, AppStrings.Opt_Bg_Blur_Desc, Toggle(b.Blur, on => Background(x => x with { Blur = on }))));
        options.Children.Add(EditorRow(AppStrings.Opt_Bg_Opacity, AppStrings.Opt_Bg_Opacity_Desc, new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { slider, label } }));
        panel.Children.Add(options);
        panel.Children.Add(Secondary(AppStrings.Themes_Bg_Copied, 12));
        return panel;
    }

    private async Task ChooseBackgroundAsync(string themeId)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = AppStrings.OptSec_Background,
            FileTypeFilter = [new FilePickerFileType(AppStrings.Opt_Bg_PickerType) { Patterns = [.. ThemeFiles.ImagePatterns] }, FilePickerFileTypes.All],
        });
        if (files is [var file] && file.TryGetLocalPath() is { } path)
        {
            SetBackground(themeId, path);
        }
    }

    internal void SetBackground(string themeId, string source)
    {
        if (ThemeCatalog.TryFind(Current.Appearance, themeId) is not { ReadOnly: false })
        {
            return;
        }

        try
        {
            var name = ThemeFiles.CopyImage(DataDirectory, themeId, source);
            ChangeTheme(themeId, t => t with { Background = t.Background with { Path = name } });
            ThemeFiles.DeleteImagesExcept(DataDirectory, themeId, name);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            _backgroundError = ex is FileNotFoundException ? AppStrings.Bg_NotFound : ex.Message;
        }

        RebuildLater();
    }

    private Control IconsEditor()
    {
        var panel = new StackPanel { Spacing = 6 };
        var note = new Border { CornerRadius = new CornerRadius(6), BorderThickness = new Thickness(1), Padding = new Thickness(12, 8), Child = Secondary(AppStrings.Themes_Icons_Later, 13) };
        note.Res(Border.BorderBrushProperty, "Brush.BorderStrong");
        panel.Children.Add(note);
        var commands = _services.Commands;
        foreach (var id in (ReadOnlySpan<string>)["File.Copy", "File.Move", "File.CreateDirectory", "File.Delete", "File.View", "File.Edit", "Find.Files", "Panel.Swap"])
        {
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("28,*,Auto"), ColumnSpacing = 10, Margin = new Thickness(0, 2) };
            row.Children.Add(AppIcons.Glyph(id, 18, 18, 1.3, "Brush.Text"));
            row.Children.Add(Text(commands.TryGet(id, out var command) ? command.Title : id, 13).Also(t =>
            {
                t.VerticalAlignment = VerticalAlignment.Center;
                Grid.SetColumn(t, 1);
            }));
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
            buttons.Children.Add(ActionButton(AppStrings.Themes_Icons_BuiltIn, () => { }, enabled: false));
            buttons.Children.Add(ActionButton(AppStrings.Themes_Icons_Change, () => { }, enabled: false));
            Grid.SetColumn(buttons, 2);
            row.Children.Add(buttons);
            panel.Children.Add(row);
        }

        panel.Children.Add(Secondary(AppStrings.Themes_Icons_Pack, 12).Also(t => t.Margin = new Thickness(0, 6, 0, 0)));
        return panel;
    }

    private Control SettingsEditor(ColorTheme theme, Action refresh)
    {
        var panel = new StackPanel { Spacing = 12 };
        panel.Children.Add(EditorRow(AppStrings.Themes_Set_Kind, AppStrings.Themes_Set_Kind_Desc, Segmented(
            [(false, AppStrings.Themes_Filter_Light), (true, AppStrings.Themes_Filter_Dark)], theme.Dark, dark =>
            {
                ChangeTheme(theme.Id, t => t with { Dark = dark });
                refresh();
            })));

        var custom = ColorButton(
            () => ColorMath.TryParseHex(ThemeCatalog.Find(Current.Appearance, theme.Id).CustomAccent, out var c) ? c : ResolveTheme(ThemeCatalog.Find(Current.Appearance, theme.Id))[ColorTokens.Accent],
            c =>
            {
                ChangeTheme(theme.Id, t => t with { Accent = ThemeAccent.Custom, CustomAccent = ColorMath.ToHex(c) });
                refresh();
            }, AppStrings.Themes_Set_Accent_Custom, 18);
        custom.IsVisible = theme.Accent == ThemeAccent.Custom;
        var accent = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        accent.Children.Add(Segmented(
            [(ThemeAccent.Theme, AppStrings.Themes_Set_Accent_Theme), (ThemeAccent.Windows, AppStrings.Themes_Set_Accent_Windows), (ThemeAccent.Custom, AppStrings.Themes_Set_Accent_Custom)], theme.Accent, mode =>
            {
                ChangeTheme(theme.Id, t => t with
                {
                    Accent = mode,
                    CustomAccent = mode == ThemeAccent.Custom ? t.CustomAccent ?? ColorMath.ToHex(ResolveTheme(t)[ColorTokens.Accent]) : t.CustomAccent,
                });
                custom.IsVisible = mode == ThemeAccent.Custom;
                refresh();
            }));
        accent.Children.Add(custom);
        panel.Children.Add(EditorRow(AppStrings.Themes_Set_Accent, AppStrings.Themes_Set_Accent_Desc, accent, below: true));

        panel.Children.Add(EditorRow(AppStrings.Themes_Set_CodeBg, AppStrings.Themes_Set_CodeBg_Desc, Segmented(
            [(CodeBackground.Panels, AppStrings.Themes_Set_CodeBg_Panels), (CodeBackground.Own, AppStrings.Themes_Set_CodeBg_Own)], theme.CodeBackground, bg =>
            {
                ChangeTheme(theme.Id, t => t with { CodeBackground = bg });
                refresh();
            }), below: true));

        var origin = theme.Origin switch
        {
            ThemeOrigin.BuiltIn => AppStrings.Themes_Set_From_BuiltIn,
            ThemeOrigin.Shipped => AppStrings.Themes_Set_From_Shipped,
            ThemeOrigin.VsCode => Loc.Format(AppStrings.Themes_Set_From_VsCode, theme.CopiedFrom ?? theme.Name, theme.CopiedOn?.ToString("d", CultureInfo.CurrentCulture) ?? ""),
            _ when theme.ImportedFrom is { } file => Loc.Format(AppStrings.Themes_Set_From_Import, file, theme.CopiedOn?.ToString("d", CultureInfo.CurrentCulture) ?? ""),
            _ when theme.CopiedFrom is { } from => Loc.Format(AppStrings.Themes_Set_From_Copy, from, theme.CopiedOn?.ToString("d", CultureInfo.CurrentCulture) ?? ""),
            _ => AppStrings.Themes_Origin_Mine,
        };
        panel.Children.Add(EditorRow(AppStrings.Themes_Set_From, origin, null));
        return panel;
    }

    private static Control EditorRow(string title, string? description, Control? control, bool below = false)
    {
        if (below && control is not null)
        {
            var stacked = new StackPanel { Spacing = 2 };
            stacked.Children.Add(Text(title, 13.5));
            if (description is not null)
            {
                stacked.Children.Add(Secondary(description, 12));
            }

            control.HorizontalAlignment = HorizontalAlignment.Left;
            control.Margin = new Thickness(0, 6, 0, 0);
            stacked.Children.Add(control);
            return stacked;
        }

        var text = new StackPanel { Spacing = 2 };
        text.Children.Add(Text(title, 13.5));
        if (description is not null)
        {
            text.Children.Add(Secondary(description, 12));
        }

        return control is null ? text : new ResponsiveRow { Children = { text, control } };
    }
}

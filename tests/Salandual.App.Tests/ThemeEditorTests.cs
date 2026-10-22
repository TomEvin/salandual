// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : tests/Salandual.App.Tests/ThemeEditorTests.cs

using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Salandual.App.Options;
using Salandual.Core.Settings;

namespace Salandual.App.Tests;

public sealed class ThemeEditorTests : IDisposable
{
    private static App CurrentApp => (App)Application.Current!;

    private static SettingsService Settings => CurrentApp.Services.Settings;

    private static string Data => CurrentApp.Services.DataDirectory;

    private readonly string _dir = Directory.CreateTempSubdirectory("salandual-theme-editor-").FullName;

    public ThemeEditorTests() => Settings.Update(_ => new AppSettings());

    public void Dispose()
    {
        foreach (var t in Settings.Current.Appearance.Themes)
        {
            ThemeFiles.DeleteFolder(Data, t.Id);
        }

        Settings.Update(_ => new AppSettings());
        Dispatcher.UIThread.RunJobs();
        Directory.Delete(_dir, recursive: true);
    }

    private static OptionsWindow Open()
    {
        var w = new OptionsWindow { Width = 1100, Height = 900 };
        w.Show();
        w.ShowCategory("themes");
        Dispatcher.UIThread.RunJobs();
        return w;
    }

    private static IEnumerable<T> Find<T>(Control root) => root.GetLogicalDescendants().OfType<T>();

    private static void Click(Button b)
    {
        b.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();
    }

    private static Button ButtonNamed(Control root, string text)
        => Find<Button>(root).First(b => b.Content as string == text || (b.Content as TextBlock)?.Text == text);

    private static void Type(TextBox box, string text)
    {
        box.Text = text;
        box.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter });
        Dispatcher.UIThread.RunJobs();
    }

    private static void Capture(Window w, string name)
    {
        if (Environment.GetEnvironmentVariable("SALANDUAL_CAPTURE_DIR") is { Length: > 0 } dir)
        {
            Directory.CreateDirectory(dir);
            w.CaptureRenderedFrame()!.Save(Path.Combine(dir, name), PngBitmapEncoderOptions.Default);
        }
    }

    [AvaloniaFact]
    public void NewTheme_EditColours_ResetAndFill()
    {
        var w = Open();
        Click(ButtonNamed(w.Page, "＋ New theme"));
        var theme = Assert.Single(Settings.Current.Appearance.Themes);
        Assert.Equal("New theme", theme.Name);
        Assert.Equal(ThemeOrigin.Mine, theme.Origin);
        Assert.Equal("Salandual Light", theme.CopiedFrom);
        Assert.Equal("New theme", w.ThemeNameBox!.Text);
        Assert.True(w.ThemeNameBox.IsEnabled);
        Capture(w, "themes-editor-colours.png");

        var (_, hex, reset) = w.ThemeColorRows[ColorTokens.PanelBackground];
        Assert.False(reset.IsEnabled);
        Type(hex, "#FFF8E7");
        theme = Settings.Current.Appearance.Themes[0];
        Assert.Equal("#FFF8E7", theme.Colors[ColorTokens.PanelBackground]);
        Assert.Equal(ColorMath.ParseHex("#FFF8E7"), w.ThemeLivePreview!.Colors![ColorTokens.PanelBackground]);
        Assert.True(reset.IsEnabled);

        Type(hex, "beige");
        Assert.Equal("#FFF8E7", hex.Text);

        Click(reset);
        Assert.Equal("#FFFFFF", Settings.Current.Appearance.Themes[0].Colors[ColorTokens.PanelBackground]);
        Assert.False(reset.IsEnabled);

        var (_, selText, selReset) = w.ThemeColorRows[ColorTokens.SelectedText];
        Type(selText, "#AA0000");
        Assert.True(selReset.IsEnabled);
        Click(selReset);
        Assert.False(Settings.Current.Appearance.Themes[0].Colors.ContainsKey(ColorTokens.SelectedText));

        Type(w.ThemeColorRows[ColorTokens.Hover].Hex, "#123456");
        Click(ButtonNamed(w.Page, "Fill the other colours from these five"));
        theme = Settings.Current.Appearance.Themes[0];
        Assert.Equal([ColorTokens.Accent, ColorTokens.PanelBackground, ColorTokens.SelectionBackground, ColorTokens.Text, ColorTokens.WindowBackground], theme.Colors.Keys.Order(StringComparer.Ordinal));
        Assert.NotEqual(ColorMath.ParseHex("#123456"), w.ThemeLivePreview.Colors[ColorTokens.Hover]);

        Type(w.ThemeNameBox, "Nord");
        Assert.Equal("Nord 2", Settings.Current.Appearance.Themes[0].Name);
        w.Close();
    }

    [AvaloniaFact]
    public void BuiltIn_IsReadOnly_DuplicateToChange_AndTheThemeOnScreenFollows()
    {
        Settings.Update(s => s with { Appearance = AppearanceSettings.Fixed("nord") });
        Dispatcher.UIThread.RunJobs();
        var w = Open();
        Click(ButtonNamed(w.Page, "Open in the Editor"));
        Assert.False(w.ThemeNameBox!.IsEnabled);
        Assert.False(w.ThemeColorRows[ColorTokens.Text].Hex.IsEffectivelyEnabled);
        Assert.False(w.ThemeColorRows[ColorTokens.Text].Reset.IsEnabled);
        Capture(w, "themes-editor-readonly.png");

        Click(ButtonNamed(w.Page, "Duplicate to change"));
        var copy = Assert.Single(Settings.Current.Appearance.Themes);
        Assert.Equal("Nord (copy)", copy.Name);
        Assert.True(copy.Dark);
        Assert.Equal(ThemeCatalog.Find(Settings.Current.Appearance, "nord").Colors, copy.Colors);
        Assert.True(w.ThemeNameBox!.IsEnabled);

        Settings.Update(s => s with { Appearance = s.Appearance with { Schedule = s.Appearance.Schedule with { Active = copy.Id } } });
        Dispatcher.UIThread.RunJobs();
        Type(w.ThemeColorRows[ColorTokens.Text].Hex, "#FFD580");
        Dispatcher.UIThread.RunJobs();
        Assert.True(Application.Current!.TryGetResource("Brush.Text", Avalonia.Styling.ThemeVariant.Dark, out var brush));
        Assert.Equal(Color.Parse("#FFD580"), ((ISolidColorBrush)brush!).Color);

        Click(ButtonNamed(w.Page, "Settings"));
        Find<Avalonia.Controls.Primitives.ToggleButton>(w.Page).First(b => b.Content as string == "Light").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();
        Assert.False(Settings.Current.Appearance.Themes[0].Dark);
        Find<Avalonia.Controls.Primitives.ToggleButton>(w.Page).First(b => b.Content as string == "Custom").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(ThemeAccent.Custom, Settings.Current.Appearance.Themes[0].Accent);
        Assert.NotNull(Settings.Current.Appearance.Themes[0].CustomAccent);
        Capture(w, "themes-editor-settings.png");

        Click(ButtonNamed(w.Page, "Icons"));
        Assert.All(Find<Button>(w.Page).Where(b => b.Content as string is "Built in" or "Change…"), b => Assert.False(b.IsEnabled));
        Capture(w, "themes-editor-icons.png");
        w.Close();
    }

    [AvaloniaFact]
    public async Task BackgroundImage_BelongsToTheTheme()
    {
        var image = Path.Combine(_dir, "fjord.png");
        using (var bitmap = new RenderTargetBitmap(new PixelSize(400, 240)))
        {
            using (var ctx = bitmap.CreateDrawingContext())
            {
                ctx.FillRectangle(new LinearGradientBrush
                {
                    StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
                    EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
                    GradientStops = { new GradientStop(Color.Parse("#2E3440"), 0), new GradientStop(Color.Parse("#88C0D0"), 1) },
                }, new Rect(0, 0, 400, 240));
            }

            bitmap.Save(image, PngBitmapEncoderOptions.Default);
        }

        var w = Open();
        Click(ButtonNamed(w.Page, "＋ New theme"));
        var id = Settings.Current.Appearance.Themes[0].Id;
        Settings.Update(s => s with { Appearance = s.Appearance with { Schedule = AppearanceSettings.Fixed(id).Schedule } });
        Click(ButtonNamed(w.Page, "Background image"));
        w.SetBackground(id, image);
        Dispatcher.UIThread.RunJobs();

        var theme = Settings.Current.Appearance.Themes[0];
        Assert.Equal("fjord.png", theme.Background.Path);
        var copied = ThemeFiles.ImagePath(Data, theme)!;
        Assert.True(File.Exists(copied));
        Assert.StartsWith(Path.Combine(Data, "themes", id), copied, StringComparison.OrdinalIgnoreCase);
        for (var i = 0; i < 300 && CurrentApp.BackgroundImage.Image is null; i++)
        {
            await Task.Delay(10);
            Dispatcher.UIThread.RunJobs();
        }

        Assert.NotNull(CurrentApp.BackgroundImage.Image);
        for (var i = 0; i < 300 && w.ThemeLivePreview!.Image?.Image is null; i++)
        {
            await Task.Delay(10);
            Dispatcher.UIThread.RunJobs();
        }

        Assert.NotNull(w.ThemeLivePreview!.Image!.Image);

        var sharp = CurrentApp.BackgroundImage.Image;
        Find<ToggleSwitch>(w.Page).Single().IsChecked = true;
        Dispatcher.UIThread.RunJobs();
        Assert.True(Settings.Current.Appearance.Themes[0].Background.Blur);
        for (var i = 0; i < 300 && (CurrentApp.BackgroundImage.Image is null || ReferenceEquals(CurrentApp.BackgroundImage.Image, sharp)); i++)
        {
            await Task.Delay(10);
            Dispatcher.UIThread.RunJobs();
        }

        Assert.True(CurrentApp.BackgroundImage.Settings.Blur);
        Assert.NotNull(CurrentApp.BackgroundImage.Image);
        await Task.Delay(100);
        Dispatcher.UIThread.RunJobs();
        Capture(w, "themes-editor-background.png");

        w.SetBackground(id, image);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("fjord (2).png", Settings.Current.Appearance.Themes[0].Background.Path);
        Assert.False(File.Exists(copied));

        var copy = w.DuplicateTheme(Settings.Current.Appearance.Themes[0]);
        Assert.True(File.Exists(ThemeFiles.ImagePath(Data, copy)));

        Settings.Update(s => s with { Appearance = s.Appearance with { Schedule = AppearanceSettings.Fixed(ThemeCatalog.SalandualLightId).Schedule } });
        Dispatcher.UIThread.RunJobs();
        Assert.Null(CurrentApp.BackgroundImage.Settings.Path);

        Click(ButtonNamed(w.Page, "Remove"));
        Assert.Null(Settings.Current.Appearance.Themes[0].Background.Path);
        Assert.Empty(Directory.EnumerateFiles(Path.Combine(Data, "themes", id)));
        w.DeleteTheme(copy.Id);
        Assert.False(Directory.Exists(Path.Combine(Data, "themes", copy.Id)));
        Assert.Single(Settings.Current.Appearance.Themes);
        w.Close();
    }

    [AvaloniaFact]
    public void Library_Duplicate_Edit_Delete()
    {
        var w = Open();
        Click(w.ThemeCards.First(c => (string?)c.Tag == "dracula"));
        Assert.False(w.ThemeDeleteButton!.IsVisible);
        Click(ButtonNamed(w.Page, "Duplicate"));
        var copy = Assert.Single(Settings.Current.Appearance.Themes);
        Assert.Equal("Dracula (copy)", copy.Name);
        Assert.Contains(w.ThemeCards, c => (string?)c.Tag == copy.Id && c.Classes.Contains("picked"));
        Assert.True(w.ThemeDeleteButton!.IsVisible);
        Capture(w, "themes-library-mine.png");

        Settings.Update(s => s with { Appearance = s.Appearance with { Schedule = s.Appearance.Schedule with { Dark = copy.Id } } });
        Click(ButtonNamed(w.Page, "Edit"));
        Assert.Equal("Dracula (copy)", w.ThemeNameBox!.Text);

        Click(ButtonNamed(w.Page, "Library"));
        Click(w.ThemeDeleteButton!);
        var ask = w.ThemeDeleteQuestion!;
        Assert.Contains("Dracula (copy)", Find<TextBlock>(ask).Select(t => t.Text).First(t => t?.Contains("deleted", StringComparison.Ordinal) == true), StringComparison.Ordinal);
        Assert.Contains(Find<TextBlock>(ask), t => t.Text?.Contains("Salandual Light or Dark takes its place", StringComparison.Ordinal) == true);
        Capture(ask, "themes-delete-ask.png");
        Click(ask.No);
        Assert.Single(Settings.Current.Appearance.Themes);
        Click(w.ThemeDeleteButton!);
        Click(w.ThemeDeleteQuestion!.Yes);
        Assert.Empty(Settings.Current.Appearance.Themes);
        Assert.Equal(ThemeCatalog.SalandualDarkId, Settings.Current.Appearance.Schedule.Dark);
        w.Close();
    }

    [AvaloniaFact]
    public void Library_BarKeepsTheActions_WhenThePreviewScrollsAway()
    {
        var w = Open();
        Assert.False(w.ThemeLibraryBar!.IsVisible);
        Click(w.ThemeCards.First(c => (string?)c.Tag == "gruvbox-light"));
        w.PageScroll.Offset = new Vector(0, 900);
        Dispatcher.UIThread.RunJobs();
        Assert.True(w.ThemeLibraryBar.IsVisible);
        Assert.Contains(Find<TextBlock>(w.ThemeLibraryBar), t => t.Text == "Gruvbox Light");
        var barTop = w.ThemeLibraryBar.TranslatePoint(default, w.PageScroll)!.Value.Y;
        Assert.InRange(barTop, 0, 40);
        Capture(w, "themes-library-bar.png");

        Click(Find<Button>(w.ThemeLibraryBar).First(b => b.Content as string == "Duplicate"));
        Assert.Equal("Gruvbox Light (copy)", Assert.Single(Settings.Current.Appearance.Themes).Name);
        w.PageScroll.Offset = default;
        Dispatcher.UIThread.RunJobs();
        Assert.False(w.ThemeLibraryBar!.IsVisible);
        w.Close();
    }

    [AvaloniaFact]
    public void Editor_PreviewStaysInView_AndANarrowWindowStacks()
    {
        var w = Open();
        Click(ButtonNamed(w.Page, "＋ New theme"));
        foreach (var expander in Find<Expander>(w.Page))
        {
            expander.IsExpanded = true;
        }

        Dispatcher.UIThread.RunJobs();
        w.PageScroll.Offset = new Vector(0, 900);
        Dispatcher.UIThread.RunJobs();
        Assert.True(w.ThemeLiveSticky!.ChildOffset > 400);
        Capture(w, "themes-editor-sticky.png");

        w.Width = 760;
        w.PageScroll.Offset = default;
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(0, Grid.GetRow(w.ThemeLiveSticky));
        Assert.True(w.ThemeLivePreview!.TranslatePoint(default, w.Page)!.Value.Y < w.ThemeColorRows[ColorTokens.Text].Hex.TranslatePoint(default, w.Page)!.Value.Y);
        Capture(w, "themes-editor-narrow.png");

        Click(ButtonNamed(w.Page, "Settings"));
        foreach (var row in Find<ResponsiveRow>(w.Page))
        {
            var children = row.Children;
            var text = children[0].Bounds;
            var control = children[1].Bounds;
            Assert.False(text.Intersects(control), "text and control overlap");
        }

        Capture(w, "themes-editor-narrow-settings.png");
        w.Close();
    }

    [AvaloniaTheory]
    [InlineData(40)]
    [InlineData(300)]
    public async Task Tile_CoversTheVisiblePart(int size)
    {
        var png = Path.Combine(_dir, $"tile-{size}.png");
        using (var b = new RenderTargetBitmap(new PixelSize(size, size)))
        {
            using (var c = b.CreateDrawingContext())
            {
                c.FillRectangle(Brushes.Red, new Rect(0, 0, size, size));
            }

            b.Save(png, PngBitmapEncoderOptions.Default);
        }

        var service = new Theme.BackgroundImageService();
        service.Apply(new BackgroundImageSettings { Path = png, Fit = BackgroundFit.Tile, Opacity = 0.6 });
        for (var i = 0; i < 300 && service.Image is null; i++)
        {
            await Task.Delay(10);
            Dispatcher.UIThread.RunJobs();
        }

        using var target = new RenderTargetBitmap(new PixelSize(400, 300));
        using (var c = target.CreateDrawingContext())
        {
            c.FillRectangle(Brushes.White, new Rect(0, 0, 400, 300));
            service.Draw(c, new Rect(-37, -91, 1280, 720), new Rect(0, 30, 400, 270));
        }

        var pixels = new byte[400 * 300 * 4];
        var handle = System.Runtime.InteropServices.GCHandle.Alloc(pixels, System.Runtime.InteropServices.GCHandleType.Pinned);
        try
        {
            target.CopyPixels(new PixelRect(0, 0, 400, 300), handle.AddrOfPinnedObject(), pixels.Length, 400 * 4);
        }
        finally
        {
            handle.Free();
        }

        (byte B, byte G, byte R) At(int x, int y) => (pixels[((y * 400) + x) * 4], pixels[(((y * 400) + x) * 4) + 1], pixels[(((y * 400) + x) * 4) + 2]);
        Assert.Equal((255, 255, 255), At(200, 10));
        foreach (var (x, y) in new[] { (2, 32), (200, 150), (397, 297), (120, 260) })
        {
            var (blue, green, red) = At(x, y);
            Assert.True(red > 240 && green < 140 && blue < 140, $"no image at {x},{y}");
        }

        service.Apply(new BackgroundImageSettings());
    }

    [AvaloniaFact]
    public void ResponsiveRow_StacksWhenNarrow()
    {
        var row = new ResponsiveRow { Children = { new TextBlock { Text = "Accent", TextWrapping = TextWrapping.Wrap }, new Border { Width = 320, Height = 30 } } };
        row.Measure(new Size(700, double.PositiveInfinity));
        row.Arrange(new Rect(row.DesiredSize));
        Assert.False(row.Stacked);
        Assert.Equal(380, row.Children[1].Bounds.X);

        row.Measure(new Size(450, double.PositiveInfinity));
        row.Arrange(new Rect(row.DesiredSize));
        Assert.True(row.Stacked);
        Assert.Equal(0, row.Children[1].Bounds.X);
        Assert.True(row.Children[1].Bounds.Y >= row.Children[0].Bounds.Bottom);
    }
}

// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : tests/Salandual.App.Tests/TextSizeTests.cs

using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Salandual.App.Theme;
using Salandual.Core.Settings;

namespace Salandual.App.Tests;

public sealed class TextSizeTests : IDisposable
{
    private static App App => (App)Application.Current!;

    public void Dispose()
    {
        App.Theme.ReadWindowsTextSize = () => 1;
        App.Services.Settings.Update(_ => new AppSettings());
        Dispatcher.UIThread.RunJobs();
        App.Theme.CheckTextSize();
    }

    [AvaloniaFact]
    public void Capture_At125()
    {
        if (Environment.GetEnvironmentVariable("SALANDUAL_CAPTURE_DIR") is not { Length: > 0 } dir)
        {
            return;
        }

        App.Theme.ReadWindowsTextSize = () => 1.25;
        App.Theme.CheckTextSize();
        var options = new Salandual.App.Options.OptionsWindow { Width = 1100, Height = 820 };
        options.Show();
        options.ShowCategory("appearance");
        Dispatcher.UIThread.RunJobs();
        Directory.CreateDirectory(dir);
        options.CaptureRenderedFrame()!.Save(Path.Combine(dir, "textsize-options-125.png"), Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
        options.Close();
    }

    [AvaloniaFact]
    public void Text_GrowsWithWindowsTextSize_Once_AndBackWhenTurnedOff()
    {
        var plain = new TextBlock { Text = "Name", FontSize = 12 };
        var button = new Button { Content = "OK", FontSize = 12 };
        var box = new TextBox { Text = "typed", FontSize = 12 };
        var w = new Window { Content = new StackPanel { Children = { plain, button, box } } };
        w.Show();
        Dispatcher.UIThread.RunJobs();
        var inButton = button.GetVisualDescendants().OfType<TextBlock>().First();
        Assert.Equal(12, plain.FontSize);

        App.Theme.ReadWindowsTextSize = () => 1.25;
        App.Theme.CheckTextSize();
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(15, plain.FontSize);
        Assert.Equal(15, inButton.FontSize);
        Assert.Equal(15, box.GetVisualDescendants().OfType<Avalonia.Controls.Presenters.TextPresenter>().First().FontSize);
        Assert.Equal(12, button.FontSize);
        Assert.Equal(TextScale.Of(FontSetting.DefaultPanelSize), App.FindResource("Font.PanelSizeShown"));
        Assert.Equal(15.0, App.FindResource("Font.PanelSizeShown"));

        var later = new TextBlock { Text = "Later", FontSize = 10 };
        ((StackPanel)w.Content!).Children.Add(later);
        Assert.Equal(12.5, later.FontSize);

        App.Services.Settings.Update(s => s with { Appearance = s.Appearance with { FollowTextSize = false } });
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(12, plain.FontSize);
        Assert.Equal(12, inButton.FontSize);
        Assert.Equal(12.0, App.FindResource("Font.PanelSizeShown"));
        w.Close();
    }
}

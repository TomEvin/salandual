// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.App/Dialogs/CountdownDialog.cs

using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Salandual.App.Controls;
using Salandual.App.Options;
using Salandual.App.Resources;

namespace Salandual.App.Dialogs;

public sealed class CountdownDialog : Window
{
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly TextBlock _message;
    private readonly Func<int, string> _text;
    private int _left;

    public CountdownDialog(string title, Func<int, string> text, string actNow, int seconds = 60)
    {
        _text = text;
        _left = seconds;
        Title = title;
        Width = 420;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        Topmost = true;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        this.Res(BackgroundProperty, "Brush.PanelBackground");

        _message = OptionsUi.Text(text(seconds), 13, wrap: true);
        Now = new Button { Content = actNow, Padding = new Thickness(14, 5) };
        Now.Click += (_, _) => Close(true);
        Cancel = new Button { Content = AppStrings.Common_Cancel, Padding = new Thickness(14, 5), MinWidth = 84, HorizontalContentAlignment = HorizontalAlignment.Center, IsDefault = true };
        Cancel.Classes.Add("accent");
        Cancel.Click += (_, _) => Close(false);
        var footer = new Border
        {
            Padding = new Thickness(20, 12),
            BorderThickness = new Thickness(0, 1, 0, 0),
            Child = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right, Children = { Now, Cancel } },
        }.Res(Border.BackgroundProperty, "Brush.WindowBackground").Res(Border.BorderBrushProperty, "Brush.Border");
        var body = new StackPanel { Spacing = 8, Margin = new Thickness(20, 6, 20, 16), Children = { _message } };
        var root = new DockPanel();
        DockPanel.SetDock(footer, Dock.Bottom);
        root.Children.Add(footer);
        root.Children.Add(body);
        Content = TitleBarParts.ForDialog(this, root);

        _timer.Tick += (_, _) => Tick();
        Opened += (_, _) =>
        {
            Cancel.Focus();
            _timer.Start();
        };
        Closed += (_, _) => _timer.Stop();
    }

    internal void Tick()
    {
        _left--;
        if (_left <= 0)
        {
            _timer.Stop();
            Close(true);
            return;
        }

        _message.Text = _text(_left);
    }

    internal Button Now { get; }

    internal Button Cancel { get; }

    internal string Message => _message.Text ?? "";

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (!e.Handled && e.Key == Key.Escape)
        {
            Close(false);
            e.Handled = true;
        }
    }
}

// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.App/Dialogs/ServerLogWindow.cs

using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Salandual.App.Controls;
using Salandual.App.Options;
using Salandual.App.Resources;
using Salandual.Core.Resources;
using Salandual.Remote;

namespace Salandual.App.Dialogs;

internal sealed class ServerLogWindow : Window
{
    private readonly ConnectionLog _log;
    private readonly TextBox _text;

    public ServerLogWindow(string name, ConnectionLog log, Func<string, bool> copy)
    {
        _log = log;
        Title = Loc.Format(AppStrings.Server_LogTitle, name);
        Width = 720;
        Height = 440;
        MinWidth = 420;
        MinHeight = 240;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        this.Res(BackgroundProperty, "Brush.PanelBackground");

        _text = new TextBox
        {
            IsReadOnly = true,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.NoWrap,
            FontFamily = new FontFamily("Cascadia Mono, Consolas, monospace"),
            FontSize = 12,
            Margin = new Thickness(12),
        };
        Avalonia.Automation.AutomationProperties.SetName(_text, Title);
        ScrollViewer.SetHorizontalScrollBarVisibility(_text, Avalonia.Controls.Primitives.ScrollBarVisibility.Auto);

        var copyButton = new Button { Content = AppStrings.Server_LogCopy, Padding = new Thickness(14, 5) };
        copyButton.Click += (_, _) => copy(_text.Text ?? "");
        var clear = new Button { Content = AppStrings.Server_LogClear, Padding = new Thickness(14, 5) };
        clear.Click += (_, _) => _log.Clear();
        var close = new Button { Content = AppStrings.Common_Close, Padding = new Thickness(14, 5), IsDefault = true };
        close.Classes.Add("accent");
        close.Click += (_, _) => Close();
        var footer = new Border
        {
            Padding = new Thickness(20, 12),
            BorderThickness = new Thickness(0, 1, 0, 0),
            Child = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right, Children = { copyButton, clear, close } },
        }.Res(Border.BackgroundProperty, "Brush.WindowBackground").Res(Border.BorderBrushProperty, "Brush.Border");

        var root = new DockPanel();
        DockPanel.SetDock(footer, Dock.Bottom);
        root.Children.Add(footer);
        root.Children.Add(_text);
        Content = TitleBarParts.ForDialog(this, root);
        Refresh();
        _log.Changed += OnChanged;
        Closed += (_, _) => _log.Changed -= OnChanged;
    }

    internal string Text => _text.Text ?? "";

    private void OnChanged() => Dispatcher.UIThread.Post(Refresh);

    private void Refresh()
    {
        _text.Text = string.Join(Environment.NewLine, _log.Lines.Select(l => l.Time.ToString("HH:mm:ss", CultureInfo.InvariantCulture) + "  " + l.Text));
        _text.CaretIndex = _text.Text.Length;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (!e.Handled && e.Key == Key.Escape)
        {
            Close();
            e.Handled = true;
        }
    }
}

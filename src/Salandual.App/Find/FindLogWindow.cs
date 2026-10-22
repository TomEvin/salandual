// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.App/Find/FindLogWindow.cs

using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Salandual.App.Controls;
using Salandual.App.Options;
using Salandual.App.Resources;
using Salandual.Core.Search;

namespace Salandual.App.Find;

internal sealed class FindLogWindow : Window
{
    private readonly StackPanel _entries = new() { Spacing = 8, Margin = new Thickness(18, 14) };

    public FindLogWindow()
    {
        Title = AppStrings.Find_LogTitle;
        Width = 620;
        Height = 420;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        this.Res(BackgroundProperty, "Brush.PanelBackground");
        var close = new Button { Content = AppStrings.Common_Close, MinWidth = 96, Padding = new Thickness(14, 5), HorizontalContentAlignment = HorizontalAlignment.Center, IsDefault = true };
        close.Click += (_, _) => Close();
        Content = TitleBarParts.ForDialog(this, new ScrollViewer { Content = _entries }, close);
    }

    internal IEnumerable<string> Lines => _entries.Children.OfType<StackPanel>().Select(p => string.Join(" ", p.Children.OfType<TextBlock>().Select(t => t.Text)));

    public void Show(IReadOnlyList<SearchLogEntry> log)
    {
        _entries.Children.Clear();
        if (log.Count == 0)
        {
            _entries.Children.Add(OptionsUi.Secondary(AppStrings.Find_LogEmpty));
            return;
        }

        foreach (var entry in log)
        {
            var kind = OptionsUi.Text(entry.Kind switch
            {
                SearchLogKind.Error => AppStrings.Find_LogError,
                SearchLogKind.Skipped => AppStrings.Find_LogSkipped,
                _ => AppStrings.Find_LogNotRead,
            }, 12, FontWeight.SemiBold, entry.Kind == SearchLogKind.Error ? "Brush.Danger" : "Brush.SecondaryText");
            var path = OptionsUi.Text(entry.Path, 13, wrap: true);
            var message = OptionsUi.Secondary(entry.Message);
            _entries.Children.Add(new StackPanel { Spacing = 2, Children = { kind, path, message } });
        }
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

// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.App/Dialogs/CommitDialog.cs

using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Salandual.App.Controls;
using Salandual.App.Options;
using Salandual.App.Resources;
using Salandual.Core.Git;
using Salandual.Core.Resources;

namespace Salandual.App.Dialogs;

public sealed record CommitContents(IReadOnlyList<string> Staged, int NotStaged)
{
    public bool TakesAll => Staged.Count == 0;
}

public sealed class CommitDialog : Window
{
    private const int SummaryLetters = 72;
    private const int ListedNames = 3;

    private readonly GitHeadFacts _head;
    private readonly Func<string, bool, Task<GitRun>> _commit;
    private readonly TextBox _message;
    private readonly TextBlock _summary;
    private readonly TextBlock _error;
    private readonly Button _commitButton;
    private string? _typedBeforeAmend;
    private bool _busy;

    public CommitDialog(string branch, CommitContents contents, GitHeadFacts head, Func<string, bool, Task<GitRun>> commit, bool canPush = false)
    {
        _head = head;
        _commit = commit;
        Title = Loc.Format(AppStrings.Git_Commit_Title, branch);
        Width = 560;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        this.Res(BackgroundProperty, "Brush.PanelBackground");

        _message = new TextBox
        {
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            Height = 120,
            VerticalContentAlignment = VerticalAlignment.Top,
        };
        Avalonia.Automation.AutomationProperties.SetName(_message, AppStrings.Git_Commit_Message);
        _message.TextChanged += (_, _) => UpdateSummary();
        _message.AddHandler(KeyDownEvent, OnMessageKey, RoutingStrategies.Tunnel);
        _summary = OptionsUi.Text("", 12, brush: "Brush.SecondaryText", wrap: true);
        _error = OptionsUi.Text("", 12, brush: "Brush.Danger", wrap: true);
        _error.IsVisible = false;

        var goesIn = new TextBlock { FontSize = 12, TextWrapping = TextWrapping.Wrap };
        goesIn.Res(TextBlock.ForegroundProperty, "Brush.Text");
        goesIn.Inlines!.Add(new Avalonia.Controls.Documents.Run(contents.TakesAll ? "" : AppStrings.Git_Commit_GoesInHead + " ") { FontWeight = FontWeight.SemiBold });
        goesIn.Inlines.Add(new Avalonia.Controls.Documents.Run(GoesIn(contents)));
        GoesInText = goesIn;
        var card = new Border
        {
            Padding = new Thickness(12, 8),
            BorderThickness = new Thickness(1),
            CornerRadius = Salandual.App.Theme.Corners.Chip,
            Child = goesIn,
        }.Res(Border.BackgroundProperty, "Brush.WindowBackground").Res(Border.BorderBrushProperty, "Brush.Border");

        AmendBox = new CheckBox
        {
            Content = head.Sha is null ? "" : Loc.Format(head.Pushed ? AppStrings.Git_Commit_AmendPushed : AppStrings.Git_Commit_Amend, head.Sha),
            IsVisible = head.Sha is not null,
        };
        AmendBox.IsCheckedChanged += (_, _) => OnAmend();
        PushBox = new CheckBox { Content = AppStrings.Git_Commit_PushAfter, IsVisible = canPush };
        var who = OptionsUi.Text(head.Author is { } author ? Loc.Format(AppStrings.Git_Commit_As, author) : AppStrings.Git_Commit_NoAuthor, 12, brush: "Brush.SecondaryText", wrap: true);

        var body = new StackPanel { Spacing = 8, Margin = new Thickness(20, 6, 20, 16) };
        body.Children.Add(new StackPanel { Spacing = 5, Children = { OptionsUi.Text(AppStrings.Git_Commit_Message, 13, FontWeight.SemiBold), _message, _summary, _error } });
        body.Children.Add(card);
        body.Children.Add(AmendBox);
        body.Children.Add(PushBox);
        body.Children.Add(who);

        _commitButton = new Button { Content = AppStrings.Git_Commit_Commit, MinWidth = 96, Padding = new Thickness(14, 5), HorizontalContentAlignment = HorizontalAlignment.Center };
        _commitButton.Classes.Add("accent");
        ToolTip.SetTip(_commitButton, AppStrings.Git_Commit_CommitTip);
        _commitButton.Click += async (_, _) => await AcceptAsync();
        var cancel = new Button { Content = AppStrings.Common_Cancel, MinWidth = 84, Padding = new Thickness(14, 5), HorizontalContentAlignment = HorizontalAlignment.Center };
        cancel.Click += (_, _) => Close(false);
        var footer = new Border
        {
            Padding = new Thickness(20, 12),
            BorderThickness = new Thickness(0, 1, 0, 0),
            Child = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right, Children = { cancel, _commitButton } },
        }.Res(Border.BackgroundProperty, "Brush.WindowBackground").Res(Border.BorderBrushProperty, "Brush.Border");

        var root = new DockPanel();
        DockPanel.SetDock(footer, Dock.Bottom);
        root.Children.Add(footer);
        root.Children.Add(body);
        Content = TitleBarParts.ForDialog(this, root);
        UpdateSummary();
        Opened += (_, _) => _message.Focus();
    }

    internal TextBox MessageBox => _message;

    internal CheckBox AmendBox { get; }

    internal CheckBox PushBox { get; }

    internal TextBlock SummaryText => _summary;

    internal TextBlock ErrorText => _error;

    internal TextBlock GoesInText { get; }

    internal Button CommitButton => _commitButton;

    internal ContextMenu? RecentMenu { get; private set; }

    private static string GoesIn(CommitContents contents)
    {
        if (contents.TakesAll)
        {
            return Loc.Plural(contents.NotStaged, AppStrings.Git_Commit_All_One, AppStrings.Git_Commit_All_Other);
        }

        var names = string.Join(", ", contents.Staged.Take(ListedNames));
        if (contents.Staged.Count > ListedNames)
        {
            names = Loc.Format(AppStrings.Git_Commit_AndMore, names, contents.Staged.Count - ListedNames);
        }

        var staged = Loc.Format(AppStrings.Git_Commit_Staged, names);
        return contents.NotStaged == 0 ? staged
            : staged + " · " + Loc.Plural(contents.NotStaged, AppStrings.Git_Commit_Stay_One, AppStrings.Git_Commit_Stay_Other);
    }

    private void UpdateSummary()
    {
        var text = _message.Text ?? "";
        var first = text.Split('\n')[0].TrimEnd('\r').Trim();
        var parts = new List<string>
        {
            first.Length > SummaryLetters ? Loc.Format(AppStrings.Git_Commit_SummaryLong, first.Length, SummaryLetters) : Loc.Format(AppStrings.Git_Commit_Summary, first.Length, SummaryLetters),
        };
        if (_head.Recent.Count > 0)
        {
            parts.Add(AppStrings.Git_Commit_Recent);
        }

        _summary.Text = string.Join(" ", parts);
        _summary.Res(TextBlock.ForegroundProperty, first.Length > SummaryLetters ? "Brush.Warning" : "Brush.SecondaryText");
        if (text.Trim().Length > 0)
        {
            ShowError(null);
        }
    }

    private void OnAmend()
    {
        if (AmendBox.IsChecked == true)
        {
            _typedBeforeAmend = _message.Text;
            if (string.IsNullOrWhiteSpace(_message.Text) && _head.Message is { } last)
            {
                _message.Text = last;
                _message.CaretIndex = last.Length;
            }
        }
        else if (_typedBeforeAmend is not null && _message.Text == _head.Message)
        {
            _message.Text = _typedBeforeAmend;
            _typedBeforeAmend = null;
        }
    }

    private void OnMessageKey(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Down && e.KeyModifiers == KeyModifiers.Alt)
        {
            ShowRecent();
            e.Handled = true;
        }
        else if (e.Key == Key.Enter && e.KeyModifiers == KeyModifiers.Control)
        {
            _ = AcceptAsync();
            e.Handled = true;
        }
    }

    internal void ShowRecent()
    {
        if (_head.Recent.Count == 0)
        {
            return;
        }

        var menu = new ContextMenu { PlacementTarget = _message, Placement = PlacementMode.Bottom };
        foreach (var message in _head.Recent)
        {
            var first = message.Split('\n')[0].TrimEnd('\r');
            var item = new MenuItem { Header = new TextBlock { Text = first, MaxWidth = 480, TextTrimming = TextTrimming.CharacterEllipsis } };
            var chosen = message;
            item.Click += (_, _) =>
            {
                _message.Text = chosen;
                _message.CaretIndex = chosen.Length;
                _message.Focus();
            };
            menu.Items.Add(item);
        }

        RecentMenu = menu;
        menu.Open(_message);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (!e.Handled && e.Key == Key.Escape)
        {
            Close(false);
            e.Handled = true;
        }
    }

    private void ShowError(string? problem)
    {
        _error.Text = problem ?? "";
        _error.IsVisible = problem is not null;
    }

    internal async Task AcceptAsync()
    {
        if (_busy)
        {
            return;
        }

        var message = (_message.Text ?? "").Replace("\r\n", "\n", StringComparison.Ordinal).Trim();
        if (message.Length == 0)
        {
            ShowError(AppStrings.Git_Commit_Empty);
            _message.Focus();
            return;
        }

        _busy = true;
        _commitButton.IsEnabled = false;
        try
        {
            var run = await _commit(message, AmendBox.IsChecked == true);
            if (!run.Succeeded)
            {
                ShowError(Loc.Format(AppStrings.Git_Refused, run.Message));
                return;
            }

            Close(true);
        }
        finally
        {
            _busy = false;
            _commitButton.IsEnabled = true;
        }
    }
}

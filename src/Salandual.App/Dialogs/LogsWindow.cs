// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.App/Dialogs/LogsWindow.cs

using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Salandual.App.Controls;
using Salandual.App.Options;
using Salandual.App.Resources;
using Salandual.App.Theme;
using Salandual.Core;
using Salandual.Core.Diagnostics;
using Salandual.Core.Resources;

namespace Salandual.App.Dialogs;

internal sealed record LogItem(string Path, string Title, string Chip, string ChipBrush, string Sub, ProblemReport? Report);

internal sealed class LogsWindow : Window
{
    internal const int MaxLines = 2000;

    internal const int MaxIssueAddress = 7500;

    private static readonly FontFamily _mono = new("Cascadia Mono, Consolas");

    private readonly string _folder;
    private readonly string _userFolder;
    private readonly Func<string, bool> _copy;
    private readonly Func<Uri, bool> _openLink;
    private readonly Func<DateTime> _now;
    private readonly ListBox _list = new() { Background = Brushes.Transparent, Padding = new Thickness(6) };
    private readonly SelectableTextBlock _text = new() { FontFamily = _mono, FontSize = 12, LineHeight = 18, Padding = new Thickness(14, 10), TextWrapping = TextWrapping.NoWrap };
    private readonly ContentControl _right = new();
    private readonly Control _textPane;
    private readonly Control _guide;
    private readonly TextBlock _status = OptionsUi.Secondary("");
    private string _raw = "";

    public LogsWindow(string folder, string userFolder, Func<string, bool> copy, Func<Uri, bool> openLink, Action<string>? showFolder = null, Func<DateTime>? now = null)
    {
        _folder = folder;
        _userFolder = userFolder;
        _copy = copy;
        _openLink = openLink;
        _now = now ?? (() => DateTime.Now);
        Title = AppStrings.Logs_Title;
        Width = 960;
        Height = 620;
        MinWidth = 640;
        MinHeight = 360;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        this.Res(BackgroundProperty, "Brush.PanelBackground");

        HidePaths = new ToggleSwitch { IsChecked = true, OnContent = null, OffContent = null, MinWidth = 0, VerticalAlignment = VerticalAlignment.Center };
        HidePaths.IsCheckedChanged += (_, _) => ShowText();
        var hideLabel = OptionsUi.Text(AppStrings.Logs_Hide, 13);
        ToolTip.SetTip(hideLabel, AppStrings.Logs_HideTip);
        var bar = new DockPanel { Margin = new Thickness(12, 6) };
        var hide = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { hideLabel, HidePaths } };
        DockPanel.SetDock(hide, Dock.Right);
        bar.Children.Add(hide);
        bar.Children.Add(OptionsUi.Secondary(AppStrings.Logs_What).Also(t => t.VerticalAlignment = VerticalAlignment.Center));
        var barLine = new Border { BorderThickness = new Thickness(0, 0, 0, 1), Child = bar }.Res(Border.BorderBrushProperty, "Brush.Border");
        var textPane = new DockPanel();
        DockPanel.SetDock(barLine, Dock.Top);
        textPane.Children.Add(barLine);
        textPane.Children.Add(new ScrollViewer { HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto, Content = _text });
        _textPane = textPane;
        _guide = BuildGuide();
        _right.Content = _textPane;

        _list.SelectionChanged += (_, _) => ShowText();
        _list.Styles.AddRange(ListStyles());
        var listPane = new Border { BorderThickness = new Thickness(0, 0, 1, 0), Width = 290, Child = _list }.Res(Border.BorderBrushProperty, "Brush.Border");

        var open = new Button { Content = AppStrings.Logs_OpenFolder, Padding = new Thickness(12, 5), IsEnabled = showFolder is not null };
        open.Click += (_, _) => showFolder?.Invoke(_folder);
        DeleteAll = new Button { Content = AppStrings.Logs_DeleteAll, Padding = new Thickness(12, 5) };
        DeleteAll.Click += async (_, _) => await DeleteAllAsync();
        HowButton = new Button { Content = AppStrings.Logs_How, Padding = new Thickness(12, 5) };
        HowButton.Click += (_, _) => ShowGuide(_right.Content != _guide);
        CopyButton = new Button { Content = AppStrings.Common_Copy, Padding = new Thickness(14, 5) };
        CopyButton.Click += (_, _) => Copy();
        var close = new Button { Content = AppStrings.Common_Close, Padding = new Thickness(14, 5) };
        close.Classes.Add("accent");
        close.Click += (_, _) => Close();
        var left = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { open, DeleteAll, _status } };
        _status.VerticalAlignment = VerticalAlignment.Center;
        _status.Margin = new Thickness(6, 0, 0, 0);
        var footer = new DockPanel();
        DockPanel.SetDock(left, Dock.Left);
        footer.Children.Add(left);
        footer.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right, Children = { HowButton, CopyButton, close } });
        var foot = new Border { Padding = new Thickness(16, 11), BorderThickness = new Thickness(0, 1, 0, 0), Child = footer }
            .Res(Border.BackgroundProperty, "Brush.WindowBackground").Res(Border.BorderBrushProperty, "Brush.Border");

        var root = new DockPanel();
        DockPanel.SetDock(foot, Dock.Bottom);
        DockPanel.SetDock(listPane, Dock.Left);
        root.Children.Add(foot);
        root.Children.Add(listPane);
        root.Children.Add(_right);
        Content = TitleBarParts.ForDialog(this, root);
        Reload();
        Opened += (_, _) => _list.Focus();
    }

    internal ToggleSwitch HidePaths { get; }

    internal Button DeleteAll { get; }

    internal Button HowButton { get; }

    internal Button CopyButton { get; }

    internal Button? IssueButton { get; private set; }

    internal IReadOnlyList<LogItem> Items { get; private set; } = [];

    internal LogItem? Selected => (_list.SelectedItem as ListBoxItem)?.Tag as LogItem;

    internal string Shown { get; private set; } = "";

    internal bool ShowingGuide => _right.Content == _guide;

    internal string Status => _status.Text ?? "";

    internal void Select(string path)
    {
        foreach (var item in _list.Items.OfType<ListBoxItem>())
        {
            if (item.Tag is LogItem log && string.Equals(log.Path, path, StringComparison.OrdinalIgnoreCase))
            {
                _list.SelectedItem = item;
                return;
            }
        }
    }

    internal void Reload()
    {
        var today = _now().Date;
        var items = new List<LogItem>();
        foreach (var r in ProblemReports.List(_folder))
        {
            var crash = r.Kind == ProblemKind.Crash;
            items.Add(new LogItem(r.Path, crash ? Strings.Report_Crash : Strings.Report_WentOn, crash ? AppStrings.Logs_Chip_Crash : AppStrings.Logs_Chip_WentOn,
                crash ? "Brush.Danger" : "Brush.Warning", r.ExceptionType.Length > 0 ? Loc.Format(AppStrings.Logs_Sub, When(r.Time, today), r.ExceptionType) : When(r.Time, today), r));
        }

        var reports = items.Count;
        var logs = Directory.Exists(_folder) ? Directory.EnumerateFiles(_folder, "*.log").Select(p => (Path: p, Day: AppLog.DayOf(Path.GetFileName(p)))).Where(x => x.Day is not null).OrderByDescending(x => x.Day).ToList() : [];
        foreach (var (path, day) in logs)
        {
            var (warnings, errors) = Count(path);
            var (chip, brush) = errors > 0 ? (Loc.Plural(errors, AppStrings.Logs_Errors_One, AppStrings.Logs_Errors_Other), "Brush.Danger")
                : warnings > 0 ? (Loc.Plural(warnings, AppStrings.Logs_Warnings_One, AppStrings.Logs_Warnings_Other), "Brush.Warning")
                : ("", "");
            var d = day!.Value;
            var title = d == today ? AppStrings.Logs_Today : d == today.AddDays(-1) ? AppStrings.Logs_Yesterday : d.ToString("d MMMM", Loc.TextCulture);
            items.Add(new LogItem(path, title, chip, brush, Loc.Format(AppStrings.Logs_Sub, Path.GetFileName(path), Size(path)), null));
        }

        Items = items;
        var selected = Selected?.Path;
        _list.Items.Clear();
        if (reports > 0)
        {
            _list.Items.Add(Group(AppStrings.Logs_Reports));
        }

        for (var i = 0; i < items.Count; i++)
        {
            if (i == reports)
            {
                _list.Items.Add(Group(AppStrings.Logs_Logs));
            }

            _list.Items.Add(Row(items[i]));
        }

        _list.Items.Add(Group(Loc.Format(AppStrings.Logs_Kept, AppLog.KeepDays), bold: false));
        if (selected is not null)
        {
            Select(selected);
        }

        if (Selected is null && _list.Items.OfType<ListBoxItem>().FirstOrDefault(i => i.Tag is LogItem) is { } first)
        {
            _list.SelectedItem = first;
        }

        ShowText();
    }

    private static string When(DateTime time, DateTime today)
    {
        var clock = time.ToString("HH:mm", CultureInfo.CurrentCulture);
        return time.Date == today ? Loc.Format(AppStrings.Logs_TodayAt, clock)
            : time.Date == today.AddDays(-1) ? Loc.Format(AppStrings.Logs_YesterdayAt, clock)
            : time.ToString("d MMM", Loc.TextCulture) + " " + clock;
    }

    private static (int Warnings, int Errors) Count(string path)
    {
        var (warnings, errors) = (0, 0);
        try
        {
            foreach (var line in File.ReadLines(path))
            {
                switch (AppLog.LevelOf(line))
                {
                    case LogLevel.Warning:
                        warnings++;
                        break;
                    case LogLevel.Error:
                        errors++;
                        break;
                }
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
        }

        return (warnings, errors);
    }

    private static string Size(string path)
    {
        try
        {
            return Core.Listing.DisplayFormat.Default.Kilobytes(new FileInfo(path).Length);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return "";
        }
    }

    private static ListBoxItem Group(string text, bool bold = true) => new()
    {
        IsEnabled = false,
        Focusable = false,
        Padding = new Thickness(8, 8, 8, 4),
        MinHeight = 0,
        Content = OptionsUi.Text(text, 11.5, bold ? FontWeight.SemiBold : FontWeight.Normal, "Brush.TertiaryText", wrap: true),
    };

    private static ListBoxItem Row(LogItem item)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), RowDefinitions = new RowDefinitions("Auto,Auto") };
        grid.Children.Add(OptionsUi.Text(item.Title, 13));
        if (item.Chip.Length > 0)
        {
            var chip = new Border
            {
                BorderThickness = new Thickness(1),
                CornerRadius = Corners.Inner,
                Padding = new Thickness(6, 0),
                VerticalAlignment = VerticalAlignment.Center,
                Child = OptionsUi.Text(item.Chip, 11, brush: item.ChipBrush),
            }.Res(Border.BorderBrushProperty, item.ChipBrush);
            Grid.SetColumn(chip, 1);
            grid.Children.Add(chip);
        }

        var sub = OptionsUi.Text(item.Sub, 12, brush: "Brush.SecondaryText");
        sub.TextTrimming = TextTrimming.CharacterEllipsis;
        Grid.SetRow(sub, 1);
        Grid.SetColumnSpan(sub, 2);
        grid.Children.Add(sub);
        var bar = new Border { Width = 3, CornerRadius = Corners.Inner, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(-8, 2, 0, 2) }
            .Res(Border.BackgroundProperty, "Brush.Accent");
        bar.Classes.Add("bar");
        return new ListBoxItem { Tag = item, Content = new Panel { Children = { grid, bar } }, Padding = new Thickness(8, 6), CornerRadius = Corners.Chip };
    }

    private static IEnumerable<Avalonia.Styling.Style> ListStyles()
    {
        static Avalonia.Styling.Style Back(Func<Avalonia.Styling.Selector?, Avalonia.Styling.Selector> state, string brush) => new(x => state(x.OfType<ListBoxItem>()).Template().OfType<Avalonia.Controls.Presenters.ContentPresenter>().Name("PART_ContentPresenter"))
        {
            Setters = { new Avalonia.Styling.Setter(Avalonia.Controls.Presenters.ContentPresenter.BackgroundProperty, new Avalonia.Markup.Xaml.MarkupExtensions.DynamicResourceExtension(brush)) },
        };

        yield return Back(x => x!.Class(":pointerover"), "Brush.Hover");
        yield return Back(x => x!.Class(":selected"), "Brush.Pressed");
        yield return Back(x => x!.Class(":selected").Class(":pointerover"), "Brush.Pressed");
        yield return Back(x => x!.Class(":selected").Class(":focus"), "Brush.Pressed");
        yield return new Avalonia.Styling.Style(x => x.OfType<ListBoxItem>().Descendant().OfType<Border>().Class("bar"))
        {
            Setters = { new Avalonia.Styling.Setter(Visual.IsVisibleProperty, false) },
        };
        yield return new Avalonia.Styling.Style(x => x.OfType<ListBoxItem>().Class(":selected").Descendant().OfType<Border>().Class("bar"))
        {
            Setters = { new Avalonia.Styling.Setter(Visual.IsVisibleProperty, true) },
        };
    }

    private void ShowText()
    {
        _raw = Selected is { } item ? Read(item.Path) : AppStrings.Logs_Empty;
        var hide = HidePaths.IsChecked == true && Selected is not null;
        Shown = hide ? PersonalPaths.Hide(_raw, _userFolder) : _raw;
        CopyButton.IsEnabled = Selected is not null;
        _text.Inlines!.Clear();
        var lines = _raw.Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            if (i > 0)
            {
                _text.Inlines.Add(new LineBreak());
            }

            AddLine(lines[i].TrimEnd('\r'), i, Selected?.Report is not null, hide);
        }
    }

    private static string Read(string path)
    {
        try
        {
            var lines = File.ReadAllLines(path);
            var text = lines.Length > MaxLines
                ? Loc.Format(AppStrings.Logs_Cut, lines.Length - MaxLines) + "\n" + string.Join('\n', lines[^MaxLines..])
                : string.Join('\n', lines);
            return text.TrimEnd('\n');
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return e.Message;
        }
    }

    private void AddLine(string line, int index, bool report, bool hide)
    {
        if (AppLog.LevelOf(line) is { } level)
        {
            _text.Inlines!.Add(Colored(line[..10], "Brush.TertiaryText"));
            var word = line[10..].Split(' ', 2)[0];
            _text.Inlines.Add(level == LogLevel.Info ? new Run(word) : Colored(word, level == LogLevel.Error ? "Brush.Danger" : "Brush.Warning"));
            AddParts(line[(10 + word.Length)..], hide, null);
        }
        else
        {
            AddParts(line, hide, report && index == 3 ? "Brush.Danger" : null);
        }
    }

    private void AddParts(string text, bool hide, string? brush)
    {
        foreach (var part in hide ? PersonalPaths.Split(text, _userFolder) : [new TextPart(text, false)])
        {
            var run = brush is null ? new Run(part.Text) : Colored(part.Text, brush);
            if (part.Hidden)
            {
                run.Bind(Run.BackgroundProperty, _text.GetResourceObservable("Brush.TextSelection"));
            }

            _text.Inlines!.Add(run);
        }
    }

    private Run Colored(string text, string brush) => new Run(text).Also(r => r.Bind(Run.ForegroundProperty, _text.GetResourceObservable(brush)));

    internal void Copy()
    {
        if (Selected is not null)
        {
            _status.Text = _copy(Shown) ? AppStrings.Logs_Copied : AppStrings.Notice_ClipboardBusy;
        }
    }

    internal async Task DeleteAllAsync()
    {
        var files = AppLog.OwnFiles(_folder).ToList();
        if (files.Count == 0)
        {
            return;
        }

        var ask = new ConfirmDialog(AppStrings.Logs_DeleteTitle, Loc.Format(AppStrings.Logs_DeleteText, files.Count), AppStrings.Logs_Delete, AppStrings.Common_Cancel, group: Core.Settings.ConfirmGroup.Delete);
        if (await ask.ShowDialog<bool>(this))
        {
            DeleteFiles(files);
        }
    }

    internal void DeleteFiles(IEnumerable<string> files)
    {
        foreach (var file in files)
        {
            try
            {
                File.Delete(file);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
            }
        }

        Reload();
    }

    internal void ShowGuide(bool on)
    {
        _right.Content = on ? _guide : _textPane;
        HowButton.Content = on ? AppStrings.Logs_BackToText : AppStrings.Logs_How;
        HowButton.Classes.Set("accent", false);
    }

    private Control BuildGuide()
    {
        Control Step(int n, string title, string text, Control? action)
        {
            var body = new StackPanel { Spacing = 3, Children = { OptionsUi.Text(title, 14, FontWeight.SemiBold), OptionsUi.Secondary(text, 13) } };
            if (action is not null)
            {
                action.HorizontalAlignment = HorizontalAlignment.Left;
                action.Margin = new Thickness(0, 4, 0, 0);
                body.Children.Add(action);
            }

            var number = OptionsUi.Text(n.ToString(CultureInfo.CurrentCulture) + ".", 14, FontWeight.SemiBold);
            number.VerticalAlignment = VerticalAlignment.Top;
            number.Width = 22;
            var row = new DockPanel { Margin = new Thickness(0, 0, 0, 14) };
            DockPanel.SetDock(number, Dock.Left);
            row.Children.Add(number);
            row.Children.Add(body);
            return row;
        }

        var copy = new Button { Content = AppStrings.Common_Copy, Padding = new Thickness(14, 4) };
        copy.Click += (_, _) => Copy();
        IssueButton = new Button { Content = AppStrings.Logs_How2_Button, Padding = new Thickness(14, 4) };
        IssueButton.Click += (_, _) => OpenIssue();
        return new ScrollViewer
        {
            Content = new StackPanel
            {
                Margin = new Thickness(22, 18),
                Children =
                {
                    Step(1, AppStrings.Logs_How1, AppStrings.Logs_How1_Text, copy),
                    Step(2, AppStrings.Logs_How2, AppStrings.Logs_How2_Text, IssueButton),
                    Step(3, AppStrings.Logs_How3, AppStrings.Logs_How3_Text, null),
                    new Border { BorderThickness = new Thickness(1), CornerRadius = Corners.Chip, Padding = new Thickness(10, 8), Child = OptionsUi.Secondary(AppStrings.Logs_Never) }
                        .Res(Border.BorderBrushProperty, "Brush.Border").Res(Border.BackgroundProperty, "Brush.WindowBackground"),
                },
            },
        };
    }

    internal void OpenIssue()
    {
        var (uri, cut) = IssueAddress(Selected, Selected is null ? "" : Shown);
        if (cut)
        {
            _copy(Shown);
        }

        _openLink(uri);
    }

    internal static (Uri Address, bool Cut) IssueAddress(LogItem? item, string report)
    {
        var title = item?.Report is { } r && r.ExceptionType.Length > 0 ? Loc.Format(AppStrings.Logs_IssueTitle, item.Title, r.ExceptionType) : "";
        string Address(string text)
        {
            var body = AppStrings.Logs_IssueDid + "\n\n\n" + AppStrings.Logs_IssueExpected + "\n\n\n" + AppStrings.Logs_IssueReport + "\n\n```\n" + text + "\n```\n";
            return ProductInfo.RepositoryUrl + "/issues/new?title=" + Uri.EscapeDataString(title) + "&body=" + Uri.EscapeDataString(body);
        }

        var address = Address(report);
        var cut = false;
        var keep = report.Length;
        while (address.Length > MaxIssueAddress && keep > 0)
        {
            keep = Math.Max(0, keep - Math.Max(200, (address.Length - MaxIssueAddress) / 3));
            address = Address(report[..keep] + "\n" + AppStrings.Logs_IssueCut);
            cut = true;
        }

        return (new Uri(address), cut);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Handled)
        {
            return;
        }

        if (e.Key == Key.Escape)
        {
            Close();
            e.Handled = true;
        }
        else if (e.Key == Key.C && e.KeyModifiers == KeyModifiers.Control && _text.SelectedText is not { Length: > 0 })
        {
            Copy();
            e.Handled = true;
        }
    }
}

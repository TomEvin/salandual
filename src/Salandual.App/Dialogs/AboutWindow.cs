// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.App/Dialogs/AboutWindow.cs

using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Salandual.App.Controls;
using Salandual.App.Diagnostics;
using Salandual.App.Options;
using Salandual.App.Resources;
using Salandual.App.Theme;
using Salandual.Core;
using Salandual.Core.Resources;
using Salandual.Core.Settings;

namespace Salandual.App.Dialogs;

internal sealed record AboutActions(Func<string, bool> Copy, Func<Uri, bool> OpenLink, Action<string>? ShowFolder);

internal sealed class AboutWindow : Window
{
    public const int ClicksForSurprise = 7;
    public static readonly TimeSpan ClicksApart = TimeSpan.FromSeconds(1.5);
    public const double RunSeconds = 3;
    private const double PressSeconds = 0.14;
    private const double SpinSeconds = 0.7;
    private const double SurpriseSeconds = 4;

    private readonly AboutActions _actions;
    private readonly StackPanel _tabs = new() { Orientation = Orientation.Horizontal, Spacing = 4, Margin = new Thickness(16, 6, 16, 0) };
    private readonly ContentControl _page = new() { Height = 470 };
    private readonly Control _about;
    private readonly Control _third;
    private readonly Image _logo;
    private readonly ScaleTransform _logoScale = new();
    private readonly RotateTransform _logoTurn = new();
    private readonly Canvas _runway = new() { ClipToBounds = true, IsHitTestVisible = false, Margin = new Thickness(0, 0, 110, 0) };
    private readonly DispatcherTimer _frames = new() { Interval = TimeSpan.FromMilliseconds(16) };
    private readonly Stopwatch _since = new();
    private readonly SelectableTextBlock _licence = new() { FontFamily = Salandual.App.Options.OptionsUi.MonoFamily, FontSize = 11.5, LineHeight = 16, TextWrapping = TextWrapping.Wrap };
    private readonly List<(Border Row, Component Component)> _rows = [];
    private int _clicks;
    private DateTime _lastClick;
    private double? _pressAt;
    private double? _spinAt;
    private double? _runAt;
    private double? _lineAt;

    public AboutWindow(RenderingMode rendering, string settingsFolder, AboutActions actions, bool animate = true, bool dark = false)
    {
        _actions = actions;
        Animate = animate;
        Title = AppStrings.About_Title;
        Width = 620;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        this.Res(BackgroundProperty, "Brush.PanelBackground");

        _logo = new Image
        {
            Width = 72,
            Height = 72,
            Source = new Bitmap(Avalonia.Platform.AssetLoader.Open(new Uri(AppBuild.LogoUri))),
            RenderTransform = new TransformGroup { Children = { _logoScale, _logoTurn } },
            Cursor = new Cursor(StandardCursorType.Arrow),
        };
        _logo.PointerPressed += (_, e) =>
        {
            if (e.GetCurrentPoint(_logo).Properties.IsLeftButtonPressed)
            {
                ClickLogo();
            }
        };
        More = OptionsUi.Text("", 12, brush: "Brush.Accent");
        More.MinHeight = 16;
        _about = BuildAbout(rendering, settingsFolder);
        _third = BuildThird();
        AddTab(ProductInfo.DisplayName, _about);
        AddTab(AppStrings.About_TabThirdParty, _third);

        Runner = new Salamander { Dark = dark, IsVisible = false };
        _runway.Children.Add(Runner);
        var close = new Button { Content = AppStrings.Common_Close, Padding = new Thickness(16, 5), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center, IsDefault = true };
        close.Classes.Add("accent");
        close.Click += (_, _) => Close();
        var foot = new Border
        {
            Height = 54,
            Padding = new Thickness(16, 0),
            BorderThickness = new Thickness(0, 1, 0, 0),
            Child = new Grid { Children = { _runway, close } },
        }.Res(Border.BackgroundProperty, "Brush.WindowBackground").Res(Border.BorderBrushProperty, "Brush.Border");

        var tabLine = new Border { BorderThickness = new Thickness(0, 0, 0, 1), Child = _tabs }.Res(Border.BorderBrushProperty, "Brush.Border");
        var root = new DockPanel();
        DockPanel.SetDock(foot, Dock.Bottom);
        DockPanel.SetDock(tabLine, Dock.Top);
        root.Children.Add(foot);
        root.Children.Add(tabLine);
        root.Children.Add(_page);
        Content = TitleBarParts.ForDialog(this, root);
        ShowTab(_about);

        _frames.Tick += (_, _) => Frame(_since.Elapsed.TotalSeconds);
        Opened += (_, _) => close.Focus();
        Closed += (_, _) => _frames.Stop();
    }

    internal bool Animate { get; }

    internal Func<DateTime> Clock { get; set; } = () => DateTime.Now;

    internal TextBlock More { get; }

    internal Salamander Runner { get; }

    internal bool ShowingThirdParty => _page.Content == _third;

    internal IReadOnlyList<Component> Components => [.. _rows.Select(r => r.Component)];

    internal Component? SelectedComponent { get; private set; }

    internal string LicenceText => _licence.Text ?? "";

    internal Button CopyInfo { get; private set; } = null!;

    internal Button LicenceButton { get; private set; } = null!;

    internal Button SourceButton { get; private set; } = null!;

    internal Button OpenSettings { get; private set; } = null!;

    internal double LogoTurn => _logoTurn.Angle;

    private Control BuildAbout(RenderingMode rendering, string settingsFolder)
    {
        var name = OptionsUi.Text(ProductInfo.DisplayName, 24, FontWeight.SemiBold);
        var version = OptionsUi.Text(AppFacts.VersionLine(), 13, brush: "Brush.SecondaryText");
        var hero = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 18,
            Margin = new Thickness(0, 0, 0, 16),
            Children = { _logo, new StackPanel { VerticalAlignment = VerticalAlignment.Center, Children = { name, version, More } } },
        };

        var gpl = OptionsUi.Secondary("", 13);
        gpl.Text = null;
        Rich.Add(gpl.Inlines!, AppStrings.About_Gpl, null, Bold(AppStrings.About_GplName, gpl), Bold(AppStrings.About_NoWarranty, gpl));

        var facts = new Grid { ColumnDefinitions = new ColumnDefinitions("130,*"), RowSpacing = 4 };
        void Fact(string label, Control value)
        {
            var row = facts.RowDefinitions.Count;
            facts.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            var l = OptionsUi.Text(label, 13, brush: "Brush.SecondaryText");
            Grid.SetRow(l, row);
            Grid.SetRow(value, row);
            Grid.SetColumn(value, 1);
            facts.Children.Add(l);
            facts.Children.Add(value);
        }

        Fact(AppStrings.About_Fact_Version, OptionsUi.Text(AppFacts.Version, 13));
        Fact(AppStrings.About_Fact_Windows, OptionsUi.Text(AppFacts.Windows, 13));
        Fact(AppStrings.About_Fact_Net, OptionsUi.Text(AppFacts.DotNet, 13));
        Fact(AppStrings.About_Fact_Rendering, OptionsUi.Text(AppFacts.Rendering(rendering), 13));
        OpenSettings = new Button { Content = AppStrings.About_Open, Padding = new Thickness(8, 1), FontSize = 12, CornerRadius = Corners.Inner, IsEnabled = _actions.ShowFolder is not null, VerticalAlignment = VerticalAlignment.Center };
        ToolTip.SetTip(OpenSettings, AppStrings.About_OpenTip);
        OpenSettings.Click += (_, _) =>
        {
            _actions.ShowFolder?.Invoke(settingsFolder);
            Close();
        };
        var folder = OptionsUi.Text(settingsFolder, 12).Also(t =>
        {
            t.FontFamily = Salandual.App.Options.OptionsUi.MonoFamily;
            t.TextTrimming = TextTrimming.CharacterEllipsis;
        });
        ToolTip.SetTip(folder, settingsFolder);
        var settingsRow = new DockPanel();
        DockPanel.SetDock(OpenSettings, Dock.Right);
        OpenSettings.Margin = new Thickness(8, 0, 0, 0);
        settingsRow.Children.Add(OpenSettings);
        settingsRow.Children.Add(folder);
        Fact(AppStrings.About_Fact_Settings, settingsRow);
        var factsBox = new Border { BorderThickness = new Thickness(1), CornerRadius = Corners.Chip, Padding = new Thickness(14, 10), Margin = new Thickness(0, 4, 0, 14), Child = facts }
            .Res(Border.BorderBrushProperty, "Brush.Border").Res(Border.BackgroundProperty, "Brush.WindowBackground");

        LicenceButton = new Button { Content = AppStrings.About_Licence, Padding = new Thickness(12, 5) };
        LicenceButton.Click += (_, _) => ShowLicence(ThirdParty.All[0]);
        SourceButton = new Button { Content = AppStrings.About_Source, Padding = new Thickness(12, 5) };
        SourceButton.Click += (_, _) => _actions.OpenLink(new Uri(ProductInfo.RepositoryUrl));
        var releases = new Button { Content = AppStrings.About_Releases, Padding = new Thickness(12, 5) };
        releases.Click += (_, _) => _actions.OpenLink(new Uri(ProductInfo.RepositoryUrl + "/releases"));
        CopyInfo = new Button { Content = AppStrings.About_CopyInfo, Padding = new Thickness(12, 5) };
        CopyInfo.Click += (_, _) =>
        {
            if (_actions.Copy(AppFacts.VersionInfo(rendering, settingsFolder)))
            {
                CopyInfo.Content = AppStrings.About_Copied;
                DispatcherTimer.RunOnce(() => CopyInfo.Content = AppStrings.About_CopyInfo, TimeSpan.FromSeconds(2));
            }
        };
        var links = new WrapPanel { Orientation = Orientation.Horizontal, ItemSpacing = 8, LineSpacing = 8, Children = { LicenceButton, SourceButton, releases, CopyInfo } };

        return new ScrollViewer
        {
            Content = new StackPanel
            {
                Margin = new Thickness(28, 22, 28, 16),
                Spacing = 8,
                Children =
                {
                    hero,
                    OptionsUi.Secondary(AppStrings.About_Lead, 13),
                    gpl,
                    factsBox,
                    links,
                    OptionsUi.Secondary(AppStrings.About_Copyright).Also(t => t.Margin = new Thickness(0, 10, 0, 0)),
                },
            },
        };
    }

    private static Run Bold(string text, TextBlock owner)
        => new Run(text) { FontWeight = FontWeight.SemiBold }.Also(r => r.Bind(Run.ForegroundProperty, owner.GetResourceObservable("Brush.Text")));

    private Control BuildThird()
    {
        var table = new StackPanel();
        Control Cells(Control a, Control b, Control c)
        {
            var g = new Grid { ColumnDefinitions = new ColumnDefinitions("*,90,170"), ColumnSpacing = 10 };
            Grid.SetColumn(b, 1);
            Grid.SetColumn(c, 2);
            g.Children.Add(a);
            g.Children.Add(b);
            g.Children.Add(c);
            return g;
        }

        var head = new Border { Padding = new Thickness(8, 4), BorderThickness = new Thickness(0, 0, 0, 1), Child = Cells(Head(AppStrings.About_Col_Component), Head(AppStrings.About_Col_Version), Head(AppStrings.About_Col_Licence)) }
            .Res(Border.BorderBrushProperty, "Brush.Border");
        foreach (var c in ThirdParty.All)
        {
            var name = new StackPanel { Children = { OptionsUi.Text(c.Name, 12.5, wrap: true) } };
            if (c.What.Length > 0)
            {
                name.Children.Add(OptionsUi.Text(c.What, 11.5, brush: "Brush.SecondaryText", wrap: true));
            }

            var row = new Border { Padding = new Thickness(8, 5), CornerRadius = Corners.Inner, Background = Brushes.Transparent, Cursor = new Cursor(StandardCursorType.Hand), Child = Cells(name, OptionsUi.Text(c.Version, 12.5), OptionsUi.Text(c.Licence, 12.5, wrap: true)) };
            row.PointerPressed += (_, _) => ShowLicence(c);
            _rows.Add((row, c));
            table.Children.Add(row);
        }

        var licenceBox = new Border
        {
            BorderThickness = new Thickness(1),
            CornerRadius = Corners.Chip,
            Margin = new Thickness(0, 10, 0, 0),
            Height = 150,
            Child = new ScrollViewer { Padding = new Thickness(12, 10), Content = _licence },
        }.Res(Border.BorderBrushProperty, "Brush.Border").Res(Border.BackgroundProperty, "Brush.WindowBackground");
        _licence.Res(SelectableTextBlock.ForegroundProperty, "Brush.SecondaryText");

        var top = new StackPanel { Children = { OptionsUi.Secondary(AppStrings.About_ThirdLead, 13).Also(t => t.Margin = new Thickness(0, 0, 0, 8)), head } };
        var panel = new DockPanel { Margin = new Thickness(16, 12, 16, 12) };
        DockPanel.SetDock(top, Dock.Top);
        DockPanel.SetDock(licenceBox, Dock.Bottom);
        panel.Children.Add(top);
        panel.Children.Add(licenceBox);
        panel.Children.Add(new ScrollViewer { Content = table });
        SelectComponent(ThirdParty.All[1]);
        return panel;
    }

    private static TextBlock Head(string text) => OptionsUi.Text(text, 12, FontWeight.SemiBold, "Brush.SecondaryText");

    internal void ShowLicence(Component c)
    {
        ShowTab(_third);
        SelectComponent(c);
    }

    private void SelectComponent(Component c)
    {
        SelectedComponent = c;
        _licence.Text = ThirdParty.Note(c) is { Length: > 0 } note ? note + "\n\n" + c.Text : c.Text;
        foreach (var (row, component) in _rows)
        {
            if (component == c)
            {
                row.Res(Border.BackgroundProperty, "Brush.Pressed");
                row.BringIntoView();
            }
            else
            {
                row.ClearValue(Border.BackgroundProperty);
                row.Background = Brushes.Transparent;
            }
        }
    }

    private void AddTab(string name, Control page)
    {
        var b = new Button
        {
            Content = name,
            Tag = page,
            Padding = new Thickness(12, 6),
            Background = Brushes.Transparent,
            CornerRadius = default,
            Margin = new Thickness(0, 0, 0, -1),
        };
        b.Res(Button.BorderBrushProperty, "Brush.Accent");
        b.Click += (_, _) => ShowTab(page);
        _tabs.Children.Add(b);
    }

    internal void ShowThirdParty(bool on) => ShowTab(on ? _third : _about);

    private void ShowTab(Control page)
    {
        _page.Content = page;
        foreach (var b in _tabs.Children.OfType<Button>())
        {
            var on = b.Tag == page;
            b.FontWeight = on ? FontWeight.SemiBold : FontWeight.Normal;
            b.BorderThickness = new Thickness(0, 0, 0, on ? 2 : 0);
        }
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
        else if (e.Key == Key.Tab && (e.KeyModifiers & KeyModifiers.Control) != 0)
        {
            ShowThirdParty(!ShowingThirdParty);
            e.Handled = true;
        }
    }

    internal void ClickLogo()
    {
        var now = Clock();
        if (now - _lastClick > ClicksApart)
        {
            _clicks = 0;
        }

        _lastClick = now;
        _clicks++;
        if (_clicks < ClicksForSurprise)
        {
            More.Text = _clicks >= 4 ? Loc.Format(AppStrings.About_More, ClicksForSurprise - _clicks) : "";
            Start(ref _pressAt);
            return;
        }

        _clicks = 0;
        More.Text = AppStrings.About_Surprise;
        _pressAt = null;
        Start(ref _lineAt);
        if (Animate)
        {
            Start(ref _spinAt);
            Start(ref _runAt);
            Runner.IsVisible = true;
        }

        Frame(_since.Elapsed.TotalSeconds);
    }

    private void Start(ref double? at)
    {
        if (!_since.IsRunning)
        {
            _since.Start();
        }

        at = _since.Elapsed.TotalSeconds;
        if (Animate || _lineAt is not null)
        {
            _frames.Start();
        }
    }

    internal void Frame(double now)
    {
        var scale = 1.0;
        var turn = 0.0;
        if (Animate && _pressAt is { } press && now - press < PressSeconds)
        {
            var p = (now - press) / PressSeconds;
            scale = 1 - (0.08 * Math.Sin(p * Math.PI));
        }
        else
        {
            _pressAt = null;
        }

        if (_spinAt is { } spin && now - spin < SpinSeconds)
        {
            var p = (now - spin) / SpinSeconds;
            var q = p < 0.6 ? p / 0.6 : (p - 0.6) / 0.4;
            (turn, scale) = p < 0.6 ? (380 * (1 - Math.Pow(1 - q, 3)), 1 + (0.08 * q)) : (380 - (20 * q), 1.08 - (0.08 * q));
        }
        else
        {
            _spinAt = null;
        }

        _logoScale.ScaleX = _logoScale.ScaleY = scale;
        _logoTurn.Angle = turn;

        if (_runAt is { } run && now - run < RunSeconds)
        {
            var t = now - run;
            var (left, opacity) = Salamander.RunAt(t, RunSeconds, _runway.Bounds.Width);
            Runner.Time = t;
            Runner.Opacity = opacity;
            Canvas.SetLeft(Runner, left);
            Canvas.SetTop(Runner, Math.Max(0, (_runway.Bounds.Height - Runner.Height) / 2));
        }
        else if (_runAt is not null)
        {
            _runAt = null;
            Runner.IsVisible = false;
        }

        if (_lineAt is { } line && now - line >= SurpriseSeconds)
        {
            _lineAt = null;
            if (_clicks == 0)
            {
                More.Text = "";
            }
        }

        if (_pressAt is null && _spinAt is null && _runAt is null && _lineAt is null)
        {
            _frames.Stop();
        }
    }

    internal double Elapsed => _since.Elapsed.TotalSeconds;

    internal bool Running => _runAt is not null;
}

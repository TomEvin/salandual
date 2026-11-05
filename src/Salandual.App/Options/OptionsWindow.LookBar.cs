// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.App/Options/OptionsWindow.LookBar.cs

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Salandual.App.Dialogs;
using Salandual.App.Resources;
using Salandual.Core.Resources;
using Salandual.Core.Settings;
using static Salandual.App.Options.OptionsUi;

namespace Salandual.App.Options;

public sealed partial class OptionsWindow
{
    internal const int CountdownSeconds = 15;

    private Border? _lookBar;
    private TextBlock? _lookTitle;
    private TextBlock? _lookDetail;
    private Button? _keepButton;
    private Button? _revertButton;
    private readonly Dictionary<string, Ellipse> _navDots = [];
    private readonly DispatcherTimer _countdown = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly DispatcherTimer _keptShown = new() { Interval = TimeSpan.FromSeconds(3) };
    private int _secondsLeft;
    private AppearanceSettings? _countingLook;
    private bool _closeAnswered;

    internal Border LookBar => _lookBar!;

    internal string LookBarTitle => _lookTitle!.Text ?? "";

    internal string LookBarDetail => _lookDetail!.Text ?? "";

    internal Button KeepButton => _keepButton!;

    internal Button RevertButton => _revertButton!;

    internal TimeSpan KeptShownFor
    {
        get => _keptShown.Interval;
        set => _keptShown.Interval = value;
    }

    internal int? CountdownLeft => _countdown.IsEnabled ? _secondsLeft : null;

    internal bool HasLookDot(string page) => _navDots.TryGetValue(page, out var dot) && dot.IsVisible;

    internal ConfirmDialog? LookQuestion { get; private set; }

    private bool TryingLook => Settings.KeptLook is not null;

    private void TryAppearance(Func<AppearanceSettings, AppearanceSettings> change) => Settings.TryLook(change);

    private Ellipse NavDot(string id)
    {
        var dot = new Ellipse { Width = 7, Height = 7, Margin = new Thickness(6, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, IsVisible = false }
            .Res(Shape.FillProperty, "Brush.Accent");
        ToolTip.SetTip(dot, AppStrings.Look_Changed_Tip);
        _navDots[id] = dot;
        return dot;
    }

    private Border BuildLookBar()
    {
        _lookTitle = Text("", 13, FontWeight.SemiBold);
        _lookTitle.TextTrimming = TextTrimming.CharacterEllipsis;
        _lookDetail = Text("", 12, brush: "Brush.SecondaryText");
        _lookDetail.TextTrimming = TextTrimming.CharacterEllipsis;
        ToolTip.SetTip(_lookDetail, AppStrings.Look_Keys);
        _revertButton = new Button { Content = AppStrings.Look_Revert, Padding = new Thickness(14, 5) };
        _revertButton.Click += (_, _) => RevertLook();
        _keepButton = new Button { Content = AppStrings.Look_Keep, Padding = new Thickness(14, 5) };
        _keepButton.Classes.Add("accent");
        _keepButton.Click += (_, _) => KeepLook();

        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto"), ColumnSpacing = 8, Margin = new Thickness(16, 10) };
        grid.Children.Add(new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center, Children = { _lookTitle, _lookDetail } });
        grid.Children.Add(_revertButton.Also(b => Grid.SetColumn(b, 1)));
        grid.Children.Add(_keepButton.Also(b => Grid.SetColumn(b, 2)));

        var edge = new Border { Width = 3, HorizontalAlignment = HorizontalAlignment.Left }.Res(Border.BackgroundProperty, "Brush.Accent");
        var panel = new Panel { Children = { grid, edge } };
        _lookBar = new Border { BorderThickness = new Thickness(0, 1, 0, 0), Child = panel, IsVisible = false }
            .Res(Border.BorderBrushProperty, "Brush.Border").Res(Border.BackgroundProperty, "Brush.PanelBackground");

        _countdown.Tick += (_, _) => CountdownTick();
        _keptShown.Tick += (_, _) =>
        {
            _keptShown.Stop();
            ShowLookBar();
        };
        EventHandler trial = (_, _) => Dispatcher.UIThread.Post(ShowLookBar);
        EventHandler<SettingsChangedEventArgs> changed = (_, e) => Dispatcher.UIThread.Post(() => OnLookChanged(e.New.Appearance));
        Settings.LookTrialChanged += trial;
        Settings.Changed += changed;
        Closing += OnClosingWithLook;
        Closed += (_, _) =>
        {
            Settings.LookTrialChanged -= trial;
            Settings.Changed -= changed;
            _countdown.Stop();
            _keptShown.Stop();
            Settings.RevertLook();
        };
        return _lookBar;
    }

    private void OnLookChanged(AppearanceSettings now)
    {
        if (_countingLook is not null && !now.IsEquivalentTo(_countingLook))
        {
            StopCountdown();
        }

        ShowLookBar();
    }

    private void ShowLookBar()
    {
        if (_lookBar is null)
        {
            return;
        }

        var kept = Settings.KeptLook;
        var changes = kept is null ? null : LookChanges.Describe(kept, Current.Appearance, ActiveId);
        foreach (var (id, dot) in _navDots)
        {
            dot.IsVisible = changes?.Pages.Contains(id) == true;
        }

        if (changes is null)
        {
            StopCountdown();
            _lookBar.IsVisible = _keptShown.IsEnabled;
            _lookTitle!.Text = AppStrings.Look_Kept;
            _lookTitle.FontWeight = FontWeight.Normal;
            _lookDetail!.IsVisible = false;
            _keepButton!.IsVisible = _revertButton!.IsVisible = false;
            ((Panel)_lookBar.Child!).Children[1].IsVisible = false;
            return;
        }

        _keptShown.Stop();
        _lookBar.IsVisible = true;
        _lookTitle!.Text = changes.Title;
        _lookTitle.FontWeight = FontWeight.SemiBold;
        _lookDetail!.IsVisible = true;
        _lookDetail.Text = _countdown.IsEnabled
            ? Loc.Format(AppStrings.Look_Countdown_Desc, changes.Before.Name, _secondsLeft)
            : changes.Detail;
        _keepButton!.Content = _countdown.IsEnabled ? Loc.Format(AppStrings.Look_KeepIn, _secondsLeft) : AppStrings.Look_Keep;
        _keepButton.IsVisible = _revertButton!.IsVisible = true;
        ((Panel)_lookBar.Child!).Children[1].IsVisible = true;
    }

    private string ActiveId(ThemeSchedule schedule)
    {
        var now = DateTime.Now;
        return schedule.ActiveId(AppInstance?.Theme.WindowsDark ?? false, (now.Hour * 60) + now.Minute);
    }

    private void CountDownIfNew()
    {
        if (Settings.KeptLook is not { } kept)
        {
            return;
        }

        var shown = ActiveId(Current.Appearance.Schedule);
        if (shown == ActiveId(kept.Schedule) || kept.Schedule.Uses(shown) || kept.KeptThemes.Contains(shown))
        {
            return;
        }

        _secondsLeft = CountdownSeconds;
        _countingLook = Current.Appearance;
        _countdown.Start();
        ShowLookBar();
    }

    internal void CountdownTick()
    {
        if (!_countdown.IsEnabled)
        {
            return;
        }

        if (--_secondsLeft <= 0)
        {
            RevertLook();
            return;
        }

        ShowLookBar();
    }

    private void StopCountdown()
    {
        _countdown.Stop();
        _countingLook = null;
    }

    internal void KeepLook()
    {
        if (!TryingLook)
        {
            return;
        }

        StopCountdown();
        var shown = ActiveId(Current.Appearance.Schedule);
        Settings.KeepLook();
        Settings.Update(s => s.Appearance.KeptThemes.Contains(shown) ? s : s with { Appearance = s.Appearance with { KeptThemes = [.. s.Appearance.KeptThemes, shown] } });
        _keptShown.Stop();
        _keptShown.Start();
        ShowLookBar();
        RebuildLater();
    }

    internal void RevertLook()
    {
        StopCountdown();
        _keptShown.Stop();
        if (!TryingLook)
        {
            ShowLookBar();
            return;
        }

        Settings.RevertLook();
        ShowLookBar();
        RebuildLater();
    }

    internal void CloseAsking()
    {
        if (TryingLook)
        {
            _ = AskAndClose();
        }
        else
        {
            Close();
        }
    }

    private void OnClosingWithLook(object? sender, WindowClosingEventArgs e)
    {
        if (e.Cancel || _closeAnswered || !TryingLook || e.IsProgrammatic || e.CloseReason != WindowCloseReason.WindowClosing)
        {
            return;
        }

        e.Cancel = true;
        _ = AskAndClose();
    }

    private async Task AskAndClose()
    {
        if (LookQuestion is not null || Settings.KeptLook is not { } kept)
        {
            return;
        }

        var changes = LookChanges.Describe(kept, Current.Appearance, ActiveId);
        var message = changes.ThemeSwitched
            ? Loc.Format(AppStrings.Look_Ask_Theme, changes.Title, changes.Before.Name)
            : Loc.Format(AppStrings.Look_Ask_Other, changes.Title);
        var dialog = LookQuestion = new ConfirmDialog(AppStrings.Look_Ask_Title, message, AppStrings.Look_Revert, AppStrings.Look_Keep, AppStrings.Common_Cancel, Core.Settings.ConfirmGroup.Save, actionIsYes: false);
        bool? answer;
        try
        {
            answer = await dialog.ShowDialog<bool?>(this);
        }
        finally
        {
            LookQuestion = null;
        }

        if (answer is null)
        {
            return;
        }

        if (answer == true)
        {
            RevertLook();
        }
        else
        {
            KeepLook();
        }

        _closeAnswered = true;
        Close();
    }
}

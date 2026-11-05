// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.App/Dialogs/ServerCopyDialog.cs

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Salandual.App.Controls;
using Salandual.App.Options;
using Salandual.App.Resources;
using Salandual.Core.Operations;
using Salandual.Core.Resources;
using Salandual.Core.Settings;
using Salandual.Remote;

namespace Salandual.App.Dialogs;

internal sealed record ServerCopyChoice(string Target, TransferSettings Settings, bool ToQueue);

internal sealed class ServerCopyDialog : Window
{
    private static readonly ExistingFilePolicy[] _policies = [ExistingFilePolicy.Ask, ExistingFilePolicy.OverwriteOlder, ExistingFilePolicy.Overwrite, ExistingFilePolicy.Skip, ExistingFilePolicy.KeepBoth];
    private readonly bool _upload;
    private readonly bool _move;
    private readonly OperationSettings _settings;
    private readonly StackPanel _options;
    private readonly Button _disclose;
    private readonly CheckBox _times, _filterOn, _skipEmpty, _limitOn, _verify;
    private readonly TextBox _filter, _limit;

    public ServerCopyDialog(string heading, string target, bool upload, bool move, OperationSettings settings, Action<OperationSettings>? saveDefaults = null, Action<bool>? keepCloseWhenDone = null)
    {
        _upload = upload;
        _move = move;
        _settings = settings;
        Title = heading;
        Width = 600;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        this.Res(BackgroundProperty, "Brush.PanelBackground");

        To = new TextBox { Text = target, MinHeight = 30, VerticalContentAlignment = VerticalAlignment.Center };
        To.Res(TextBox.FontFamilyProperty, "Font.Mono");
        Avalonia.Automation.AutomationProperties.SetName(To, AppStrings.Server_CopyTo);
        IfExists = new ComboBox
        {
            ItemsSource = new[] { AppStrings.Server_IfAsk, AppStrings.Server_IfOlder, AppStrings.Server_IfOverwrite, AppStrings.Server_IfSkip, AppStrings.Server_IfKeepBoth },
            SelectedIndex = Math.Max(0, Array.IndexOf(_policies, settings.IfExists)),
            MinWidth = 240,
        };
        Error = OptionsUi.Text(upload ? AppStrings.Server_CopyToServer : AppStrings.Server_CopyToDisk, 12, brush: "Brush.Danger", wrap: true);
        Error.IsVisible = false;

        var fields = new Grid { ColumnDefinitions = new ColumnDefinitions("150,*"), RowDefinitions = new RowDefinitions("Auto,Auto"), RowSpacing = 10 };
        void Field(int row, string label, Control control)
        {
            var text = OptionsUi.Text(label, 13);
            text.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetRow(text, row);
            Grid.SetRow(control, row);
            Grid.SetColumn(control, 1);
            fields.Children.Add(text);
            fields.Children.Add(control);
        }

        Field(0, AppStrings.Server_CopyTo, To);
        Field(1, AppStrings.Server_IfThere, new StackPanel { Orientation = Orientation.Horizontal, Children = { IfExists } });

        CheckBox Check(string label, bool on) => new() { Content = label, IsChecked = on, FontSize = 13, MinHeight = 26 };
        _times = Check(AppStrings.Server_KeepDates, settings.KeepFolderTimes);
        _filterOn = Check(AppStrings.Copy_OnlyMatching, settings.FilterEnabled);
        _filter = new TextBox { Text = settings.Filter, Width = 110, MinHeight = 26, Padding = new Thickness(6, 2), FontSize = 12 };
        _filter.Res(TextBox.FontFamilyProperty, "Font.Mono");
        _skipEmpty = Check(AppStrings.Copy_SkipEmpty, settings.SkipEmptyFolders);
        _limitOn = Check(AppStrings.Copy_LimitTo, settings.LimitSpeed);
        _limit = new TextBox { Text = settings.SpeedLimitMb.ToString("0.#", System.Globalization.CultureInfo.CurrentCulture), Width = 56, MinHeight = 26, Padding = new Thickness(6, 2), FontSize = 12, HorizontalContentAlignment = HorizontalAlignment.Right };
        _verify = Check(AppStrings.Server_CheckSize, settings.Verify);
        _options = new StackPanel
        {
            Spacing = 4,
            IsVisible = false,
            Children =
            {
                new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Children = { _filterOn, _filter } },
                _skipEmpty,
                new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Children = { _limitOn, _limit, OptionsUi.Text("MB/s", 13).Also(t => t.VerticalAlignment = VerticalAlignment.Center) } },
                _verify,
                _times,
                OptionsUi.Text(move ? AppStrings.Server_MoveNote : AppStrings.Server_OptionsNote, 12, brush: "Brush.TertiaryText", wrap: true),
            },
        };
        _disclose = new Button { Padding = new Thickness(0, 2), Background = Brushes.Transparent, BorderThickness = new Thickness(0), HorizontalAlignment = HorizontalAlignment.Left };
        _disclose.Click += (_, _) =>
        {
            _options.IsVisible = !_options.IsVisible;
            UpdateDisclose();
        };
        foreach (var box in new[] { _times, _filterOn, _skipEmpty, _limitOn, _verify })
        {
            box.IsCheckedChanged += (_, _) => UpdateDisclose();
        }

        UpdateDisclose();

        var body = new StackPanel { Spacing = 10, Margin = new Thickness(20, 12, 20, 16) };
        body.Children.Add(fields);
        body.Children.Add(OptionsUi.Text(upload ? AppStrings.Server_UploadNote : AppStrings.Server_CopyNote, 12, brush: "Brush.TertiaryText", wrap: true));
        CloseWhenDone = new CheckBox { Content = AppStrings.Opt_CloseWhenDone, IsChecked = settings.CloseWhenDone, IsVisible = settings.Progress == ProgressShow.Window, FontSize = 13, MinHeight = 26 };
        CloseWhenDone.IsCheckedChanged += (_, _) => keepCloseWhenDone?.Invoke(CloseWhenDone.IsChecked == true);
        body.Children.Add(CloseWhenDone);
        body.Children.Add(_disclose);
        body.Children.Add(_options);
        body.Children.Add(Error);

        var queueFirst = settings.Enter == EnterAction.AddToQueue;
        var verb = move ? AppStrings.Copy_Move : AppStrings.Copy_Copy;
        Ok = new Button { Content = queueFirst ? AppStrings.Copy_AddToQueueEnter : Loc.Format(AppStrings.Copy_GoEnter, verb), Padding = new Thickness(14, 5), MinWidth = 96 };
        Ok.Classes.Add("accent");
        Ok.Click += (_, _) => Accept(toQueue: queueFirst);
        QueueButton = new Button { Content = queueFirst ? AppStrings.Copy_StartNowShift : AppStrings.Copy_AddToQueueShift, Padding = new Thickness(14, 5) };
        QueueButton.Click += (_, _) => Accept(toQueue: !queueFirst);
        var cancel = new Button { Content = AppStrings.Common_Cancel, Padding = new Thickness(14, 5) };
        cancel.Click += (_, _) => Close(null);
        var save = Operations.Ui.Link(AppStrings.Copy_SaveDefaults, () =>
        {
            saveDefaults?.Invoke(CurrentSettings());
            SavedDefaults = true;
        });
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { cancel, QueueButton, Ok } };
        var footerRow = new DockPanel();
        DockPanel.SetDock(buttons, Dock.Right);
        footerRow.Children.Add(buttons);
        footerRow.Children.Add(save);
        var footer = new Border { Padding = new Thickness(20, 12), BorderThickness = new Thickness(0, 1, 0, 0), Child = footerRow }
            .Res(Border.BackgroundProperty, "Brush.WindowBackground").Res(Border.BorderBrushProperty, "Brush.Border");

        var root = new DockPanel();
        DockPanel.SetDock(footer, Dock.Bottom);
        root.Children.Add(footer);
        root.Children.Add(body);
        Content = TitleBarParts.ForDialog(this, root);
        Opened += (_, _) =>
        {
            To.Focus();
            To.SelectAll();
        };
    }

    internal TextBox To { get; }

    internal ComboBox IfExists { get; }

    internal TextBlock Error { get; }

    internal Button Ok { get; }

    internal Button QueueButton { get; }

    internal CheckBox CloseWhenDone { get; }

    internal CheckBox LimitBox => _limitOn;

    internal TextBox LimitText => _limit;

    internal CheckBox FilterBox => _filterOn;

    internal TextBox FilterText => _filter;

    internal bool SavedDefaults { get; private set; }

    internal void ShowOptions()
    {
        _options.IsVisible = true;
        UpdateDisclose();
    }

    private void UpdateDisclose()
    {
        var current = CurrentSettings();
        var changed = (current.FilterEnabled ? 1 : 0) + (current.SkipEmptyFolders ? 1 : 0) + (current.LimitSpeed ? 1 : 0) + (current.Verify ? 1 : 0) + (current.KeepFolderTimes ? 0 : 1);
        var text = new TextBlock { FontSize = 13 };
        text.Res(TextBlock.ForegroundProperty, "Brush.Text");
        text.Inlines!.Add(new Run((_options.IsVisible ? "▾ " : "▸ ") + AppStrings.Copy_Options) { FontWeight = FontWeight.SemiBold });
        if (changed > 0)
        {
            text.Inlines.Add(new Run(" · " + Loc.Format(AppStrings.Copy_Changed, changed)));
        }

        _disclose.Content = text;
    }

    private OperationSettings CurrentSettings() => _settings with
    {
        IfExists = _policies[Math.Max(0, IfExists.SelectedIndex)],
        KeepFolderTimes = _times.IsChecked == true,
        FilterEnabled = _filterOn.IsChecked == true,
        Filter = string.IsNullOrWhiteSpace(_filter.Text) ? "*.*" : _filter.Text.Trim(),
        SkipEmptyFolders = _skipEmpty.IsChecked == true,
        LimitSpeed = _limitOn.IsChecked == true,
        SpeedLimitMb = double.TryParse(_limit.Text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.CurrentCulture, out var mb) && mb > 0 ? mb : _settings.SpeedLimitMb,
        Verify = _verify.IsChecked == true,
    };

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Handled)
        {
            return;
        }

        if (e.Key == Key.Escape)
        {
            Close(null);
            e.Handled = true;
        }
        else if (e.Key == Key.Enter)
        {
            var queueFirst = _settings.Enter == EnterAction.AddToQueue;
            Accept(toQueue: queueFirst != (e.KeyModifiers == KeyModifiers.Shift));
            e.Handled = true;
        }
    }

    internal void Accept(bool toQueue = false)
    {
        var target = (To.Text ?? "").Trim().Trim('"');
        var ok = _upload
            ? ServerAddress.Parse(target) is not null
            : target.Length > 0 && Path.IsPathFullyQualified(target) && !ServerAddress.IsServerPath(target) && !Core.Vfs.ShellPlace.IsPlace(target);
        if (!ok)
        {
            Error.IsVisible = true;
            return;
        }

        Close(new ServerCopyChoice(target, TransferSettings.From(CurrentSettings().ToOptions(), _move), toQueue));
    }
}

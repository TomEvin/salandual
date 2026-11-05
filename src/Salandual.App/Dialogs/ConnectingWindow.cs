// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.App/Dialogs/ConnectingWindow.cs

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

internal sealed class ConnectingWindow : Window
{
    public const int ShowAfterMs = 500;

    private readonly ConnectionLog _log;
    private readonly int _from;
    private readonly DateTime _since;
    private readonly TextBox _text;
    private readonly ProgressBar _bar = new() { IsIndeterminate = true, Height = 4, MinHeight = 4, CornerRadius = new CornerRadius(2) };
    private readonly TextBlock _note;
    private readonly TextBlock _error = OptionsUi.Text("", 13, brush: "Brush.Danger", wrap: true);
    private readonly Button _cancel, _retry, _close;
    private readonly Action _onCancel;

    public ConnectingWindow(string name, ServerAddress server, ConnectionLog log, Func<string, bool> copy, Action onCancel, Action onRetry, bool closeWhenConnected = false, Action<bool>? keepCloseWhenConnected = null)
    {
        _name = name;
        _log = log;
        _onCancel = onCancel;
        _since = DateTime.Now.AddSeconds(-1);
        _from = log.Lines.Count;
        Title = Loc.Format(AppStrings.Server_ConnectingTitle, name);
        Width = 600;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        this.Res(BackgroundProperty, "Brush.PanelBackground");

        var where = OptionsUi.Text(Loc.Format(AppStrings.Server_ConnectingWhere, server.Host, server.Port), 13);
        _text = new TextBox
        {
            IsReadOnly = true,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.NoWrap,
            FontFamily = new FontFamily("Cascadia Mono, Consolas, monospace"),
            FontSize = 12,
            Height = 150,
        };
        Avalonia.Automation.AutomationProperties.SetName(_text, Title);
        ScrollViewer.SetHorizontalScrollBarVisibility(_text, Avalonia.Controls.Primitives.ScrollBarVisibility.Auto);
        _note = OptionsUi.Text(AppStrings.Server_ConnectingNote, 12, brush: "Brush.TertiaryText", wrap: true);
        _error.IsVisible = false;

        var copyButton = new Button { Content = AppStrings.Server_ConnectingCopyLog, Padding = new Thickness(14, 5) };
        copyButton.Click += (_, _) => copy(_text.Text ?? "");
        _cancel = new Button { Content = AppStrings.Common_Cancel, Padding = new Thickness(14, 5), IsDefault = true };
        _cancel.Classes.Add("accent");
        _cancel.Click += (_, _) => CancelConnecting();
        _retry = new Button { Content = AppStrings.Server_TryAgain, Padding = new Thickness(14, 5), IsVisible = false };
        _retry.Classes.Add("accent");
        _retry.Click += (_, _) =>
        {
            Close();
            onRetry();
        };
        _close = new Button { Content = AppStrings.Common_Close, Padding = new Thickness(14, 5), IsVisible = false };
        _close.Click += (_, _) => Close();
        _auto = new CheckBox { Content = AppStrings.Server_CloseWhenConnected, IsChecked = closeWhenConnected, VerticalAlignment = VerticalAlignment.Center };
        _auto.IsCheckedChanged += (_, _) => keepCloseWhenConnected?.Invoke(_auto.IsChecked == true);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { copyButton, _retry, _cancel, _close } };
        var footerRow = new DockPanel();
        DockPanel.SetDock(buttons, Dock.Right);
        footerRow.Children.Add(buttons);
        footerRow.Children.Add(_auto);
        var footer = new Border
        {
            Padding = new Thickness(20, 12),
            BorderThickness = new Thickness(0, 1, 0, 0),
            Child = footerRow,
        }.Res(Border.BackgroundProperty, "Brush.WindowBackground").Res(Border.BorderBrushProperty, "Brush.Border");

        var body = new StackPanel { Spacing = 10, Margin = new Thickness(20, 12, 20, 16), Children = { where, _bar, _text, _status, _error, _note } };
        _status.IsVisible = false;
        var root = new DockPanel();
        DockPanel.SetDock(footer, Dock.Bottom);
        root.Children.Add(footer);
        root.Children.Add(body);
        Content = TitleBarParts.ForDialog(this, root);
        Refresh();
        _log.Changed += OnChanged;
        Closed += (_, _) =>
        {
            _log.Changed -= OnChanged;
            IsClosed = true;
            if (!_finished && !Failed)
            {
                onCancel();
            }
        };
    }

    private bool _finished;

    public bool IsClosed { get; private set; }

    private readonly string _name;
    private readonly CheckBox _auto;
    private readonly TextBlock _status = OptionsUi.Text("", 13, FontWeight.SemiBold);

    public void Done()
    {
        _finished = true;
        if (IsClosed)
        {
            return;
        }

        Refresh();
        if (_auto.IsChecked == true)
        {
            Close();
            return;
        }

        _bar.IsIndeterminate = false;
        _bar.IsVisible = false;
        _status.Text = Loc.Format(AppStrings.Server_Connected, _name);
        _status.IsVisible = true;
        _cancel.IsVisible = false;
        _close.IsVisible = true;
        _close.IsDefault = true;
        _close.Focus();
    }

    public void Abandon()
    {
        _finished = true;
        if (!IsClosed)
        {
            Close();
        }
    }

    internal bool Connected => _finished && !IsClosed;

    internal CheckBox CloseWhenConnected => _auto;

    internal Button CloseButton => _close;

    public bool Failed { get; private set; }

    internal string Text => _text.Text ?? "";

    internal TextBlock Error => _error;

    internal Button Cancel => _cancel;

    internal Button Retry => _retry;

    public void ShowFailed(string reason)
    {
        Failed = true;
        Refresh();
        _bar.IsIndeterminate = false;
        _bar.IsVisible = false;
        _note.IsVisible = false;
        _error.Text = reason;
        _error.IsVisible = true;
        _cancel.IsVisible = false;
        _retry.IsVisible = _close.IsVisible = true;
        _retry.IsDefault = true;
        _retry.Focus();
    }

    private void CancelConnecting()
    {
        _cancel.IsEnabled = false;
        _onCancel();
    }

    private void OnChanged() => Dispatcher.UIThread.Post(Refresh);

    private void Refresh()
    {
        var lines = _log.Lines;
        var mine = lines.Count >= _from && lines.Count < ConnectionLog.MaxLines ? lines.Skip(_from) : lines.Where(l => l.Time >= _since);
        _text.Text = string.Join(Environment.NewLine, mine.Select(l => l.Time.ToString("HH:mm:ss", CultureInfo.InvariantCulture) + "  " + l.Text));
        _text.CaretIndex = _text.Text.Length;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (!e.Handled && e.Key == Key.Escape)
        {
            if (Failed || _finished)
            {
                Close();
            }
            else
            {
                CancelConnecting();
            }

            e.Handled = true;
        }
    }
}

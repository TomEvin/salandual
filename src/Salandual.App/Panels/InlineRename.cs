// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.App/Panels/InlineRename.cs

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Salandual.App.Options;
using Salandual.App.Resources;
using Salandual.Core.Operations;
using Salandual.Core.Panels;
using Salandual.Core.Resources;
using Salandual.Platform.Windows.FileSystem;

namespace Salandual.App.Panels;

internal sealed class InlineRename
{
    private readonly PanelHost _host;
    private readonly Canvas _layer;
    private readonly TextBox _box;
    private readonly Border _bubble;
    private readonly TextBlock _bubbleText;
    private IOperationFileSystem? _fs;
    private string _folder = "";
    private string _oldName = "";
    private string _rowName = "";
    private string _panelPath = "";
    private bool _isDirectory;
    private bool _busy;
    private bool _ending;

    public InlineRename(PanelHost host, Canvas layer)
    {
        _host = host;
        _layer = layer;
        _box = new TextBox { IsVisible = false, Padding = new Thickness(4, 0), MinHeight = 0, VerticalContentAlignment = Avalonia.Layout.VerticalAlignment.Center, BorderThickness = new Thickness(1.5) };
        _box.Res(TextBox.FontFamilyProperty, "Font.Panel").Res(TextBox.FontSizeProperty, "Font.PanelSize").Res(TextBox.BorderBrushProperty, "Brush.Accent");
        Avalonia.Automation.AutomationProperties.SetName(_box, AppStrings.Rename_NewName);
        _box.AddHandler(InputElement.KeyDownEvent, OnKey, RoutingStrategies.Tunnel);
        _box.TextChanged += (_, _) => OnTyped();
        _box.LostFocus += (_, _) => Dispatcher.UIThread.Post(OnFocusLeft, DispatcherPriority.Background);

        _bubbleText = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 12, MaxWidth = 380 };
        _bubbleText.Res(TextBlock.ForegroundProperty, "Brush.Text");
        _bubble = new Border
        {
            IsVisible = false,
            Padding = new Thickness(10, 6),
            CornerRadius = new CornerRadius(6),
            BorderThickness = new Thickness(1),
            Child = _bubbleText,
            BoxShadow = BoxShadows.Parse("0 4 12 0 #26000000"),
        };
        layer.Children.Add(_box);
        layer.Children.Add(_bubble);
        host.View.Scrolled += (_, _) => Place();
        host.View.SizeChanged += (_, _) => Place();
    }

    public bool IsOpen => _box.IsVisible;

    internal TextBox Box => _box;

    internal string? Message => _bubble.IsVisible ? _bubbleText.Inlines?.Count > 0 ? string.Concat(_bubbleText.Inlines.OfType<Run>().Select(r => r.Text)) : _bubbleText.Text : null;

    internal Task Finished { get; private set; } = Task.CompletedTask;

    public void Begin(IOperationFileSystem fs)
    {
        var panel = _host.Panel;
        if (panel.FocusedEntry is not { } entry || panel.IsUp(panel.Focus) || (panel.Path.Length == 0 && panel.Results is null))
        {
            return;
        }

        var (inFolder, plain) = FilePanel.SplitResult(entry.Name);
        _fs = fs;
        _panelPath = panel.Path;
        _rowName = entry.Name;
        _folder = panel.Results is null || inFolder.Length == 0 ? panel.Path : WindowsPath.Join(panel.Path, inFolder);
        _oldName = panel.Results is null ? entry.Name : plain;
        _isDirectory = entry.IsDirectory;
        _host.View.EnsureFocusVisible();
        _box.Text = _oldName;
        HideBubble();
        _box.IsVisible = true;
        _box.Height = _host.View.RowHeight;
        panel.Changed += OnPanelChanged;
        Place();
        _box.Focus();
        _box.SelectionStart = 0;
        _box.SelectionEnd = QuickRename.StemLength(_oldName, _isDirectory);
    }

    public void Cancel() => End(focusList: true);

    private void End(bool focusList)
    {
        if (!_box.IsVisible)
        {
            return;
        }

        _ending = true;
        _host.Panel.Changed -= OnPanelChanged;
        _box.IsVisible = false;
        HideBubble();
        if (focusList)
        {
            _host.View.Focus();
        }

        _ending = false;
    }

    private void OnPanelChanged(object? sender, PanelChange change)
    {
        if (_busy)
        {
            return;
        }

        if ((change & PanelChange.Items) != 0 && (_host.Panel.Path != _panelPath || !_host.Panel.Items.Any(e => e.Name == _rowName)))
        {
            End(focusList: false);
            return;
        }

        Place();
    }

    private int Row()
    {
        var items = _host.Panel.Items;
        for (var i = 0; i < items.Count; i++)
        {
            if (items[i].Name == _rowName)
            {
                return i;
            }
        }

        return -1;
    }

    private void Place()
    {
        if (!_box.IsVisible)
        {
            return;
        }

        if (_host.View.NameBounds(Row()) is not { } r)
        {
            _box.Opacity = 0;
            _bubble.Opacity = 0;
            return;
        }

        _box.Opacity = 1;
        _bubble.Opacity = 1;
        Canvas.SetLeft(_box, r.X);
        Canvas.SetTop(_box, r.Y);
        _box.Width = r.Width;
        _box.Height = r.Height;
        PlaceBubble(r);
    }

    private void PlaceBubble(Rect row)
    {
        if (!_bubble.IsVisible)
        {
            return;
        }

        _bubble.Measure(Size.Infinity);
        var height = _bubble.DesiredSize.Height;
        var top = row.Bottom + 4;
        if (top + height > _layer.Bounds.Height && row.Y - height - 4 >= 0)
        {
            top = row.Y - height - 4;
        }

        Canvas.SetLeft(_bubble, row.X + 4);
        Canvas.SetTop(_bubble, top);
    }

    private void OnTyped()
    {
        if (!_box.IsVisible || _busy)
        {
            return;
        }

        var name = _box.Text ?? "";
        if (name.Length > 0 && FileNames.Problem(name) is { } problem && problem != Strings.Name_Empty)
        {
            ShowError(problem);
        }
        else if (QuickRename.ExtensionChange(_oldName, name, _isDirectory) is { } change)
        {
            ShowExtensionWarning(change.From, change.To);
        }
        else
        {
            HideBubble();
        }
    }

    private void OnKey(object? sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Enter:
                e.Handled = true;
                Finished = CommitAsync(focusList: true);
                break;
            case Key.Escape:
                e.Handled = true;
                if (QuickRename.ExtensionChange(_oldName, _box.Text ?? "", _isDirectory) is { } change)
                {
                    var name = _box.Text ?? "";
                    var dot = name.LastIndexOf('.');
                    var stem = dot > 0 && change.To.Length > 0 ? name[..dot] : name;
                    _box.Text = stem + change.From;
                    _box.SelectionStart = 0;
                    _box.SelectionEnd = stem.Length;
                }
                else
                {
                    Cancel();
                }

                break;
            case Key.Tab:
                e.Handled = true;
                Finished = CommitAsync(focusList: true);
                break;
        }
    }

    private void OnFocusLeft()
    {
        if (!_box.IsVisible || _box.IsFocused || _busy || _ending)
        {
            return;
        }

        var name = _box.Text ?? "";
        if (name != _oldName && FileNames.Problem(name) is null && QuickRename.ExtensionChange(_oldName, name, _isDirectory) is null)
        {
            Finished = CommitAsync(focusList: false);
        }
        else
        {
            End(focusList: false);
        }
    }

    private async Task CommitAsync(bool focusList)
    {
        if (_busy || !_box.IsVisible || _fs is not { } fs)
        {
            return;
        }

        var name = (_box.Text ?? "").TrimEnd(' ');
        if (name == _oldName)
        {
            End(focusList);
            return;
        }

        _busy = true;
        var folder = _folder;
        var oldName = _oldName;
        string? problem;
        try
        {
            problem = await Task.Run(() =>
            {
                if (QuickRename.Problem(fs, folder, oldName, name) is { } p)
                {
                    return p;
                }

                try
                {
                    fs.Move(WindowsPath.Join(folder, oldName), WindowsPath.Join(folder, name), replace: false);
                    return null;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    return Loc.Format(AppStrings.Drive_CantRenameError, ex.Message);
                }
            });
        }
        finally
        {
            _busy = false;
        }

        if (problem is not null)
        {
            if (!_box.IsVisible)
            {
                _host.ShowError(problem);
                return;
            }

            ShowError(problem);
            _box.Focus();
            return;
        }

        End(focusList);
        if (_host.Panel.Results is not null)
        {
            _host.Panel.RenameResult(_rowName, name);
            return;
        }

        await _host.Panel.RefreshAsync(name);
    }

    private void ShowError(string message)
    {
        _bubbleText.Inlines?.Clear();
        _bubbleText.Text = message;
        _bubble.Res(Border.BackgroundProperty, "Brush.DangerBackground").Res(Border.BorderBrushProperty, "Brush.Danger");
        _bubble.IsVisible = true;
        Place();
    }

    private void ShowExtensionWarning(string from, string to)
    {
        _bubbleText.Text = null;
        var inlines = _bubbleText.Inlines!;
        inlines.Clear();
        if (to.Length == 0)
        {
            Rich.Add(inlines, AppStrings.Rename_RemovingExt, null, new Run(from) { FontWeight = FontWeight.SemiBold });
        }
        else
        {
            Rich.Add(inlines, AppStrings.Rename_ChangingExt, null, new Run(from) { FontWeight = FontWeight.SemiBold }, new Run(to) { FontWeight = FontWeight.SemiBold });
        }

        var keys = new Run("\n" + AppStrings.Rename_Keys) { FontSize = 11 };
        keys.Bind(Run.ForegroundProperty, _bubbleText.GetResourceObservable("Brush.SecondaryText"));
        inlines.Add(keys);
        _bubble.Res(Border.BackgroundProperty, "Brush.WarningBackground").Res(Border.BorderBrushProperty, "Brush.Warning");
        _bubble.IsVisible = true;
        Place();
    }

    private void HideBubble() => _bubble.IsVisible = false;
}

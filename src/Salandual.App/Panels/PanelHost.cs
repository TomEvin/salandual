// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.App/Panels/PanelHost.cs

using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Salandual.Core.Commands;
using Salandual.Core.Listing;
using Salandual.App.Options;
using Salandual.App.Resources;
using Salandual.Core.Panels;
using Salandual.Core.Resources;
using Salandual.Core.Settings;
using Salandual.Platform.Windows.FileSystem;

namespace Salandual.App.Panels;

public sealed class PanelHost : Border
{
    private readonly Grid _grid;
    private ChangeDirectoryBox? _cd;
    private Border? _cdBar;
    private Border? _cdList;
    private string _infoTemplate = PanelSettings.DefaultInformationLine;
    private int _spaceVersion;
    private InlineRename? _rename;
    private Border? _resultsBar;
    private TextBlock? _resultsText;
    private Button? _resultsBack;
    private Button? _resultsJump;

    public PanelHost(FilePanel panel, CommandRegistry commands)
    {
        Panel = panel;
        Strip = new DriveStrip();
        DirectoryLine = new DirectoryLine();
        View = new FilePanelView { Panel = panel, Commands = commands };
        Info = new InformationLineView();

        var grid = _grid = new Grid { RowDefinitions = new RowDefinitions("Auto,Auto,*,Auto") };
        grid.Children.Add(Strip);
        Grid.SetRow(DirectoryLine, 1);
        grid.Children.Add(DirectoryLine);
        Grid.SetRow(View, 2);
        grid.Children.Add(View);
        Grid.SetRow(Info, 3);
        grid.Children.Add(Info);

        Child = grid;
        CornerRadius = new CornerRadius(8);
        BorderThickness = new Thickness(1);
        ClipToBounds = true;
        Bind(BorderBrushProperty, this.GetResourceObservable("Brush.Border"));
        Bind(BackgroundProperty, this.GetResourceObservable("Brush.PanelBackground"));

        panel.Changed += OnPanelChanged;
        DirectoryLine.NavigateRequested += (_, path) => GoTo(path);
    }

    public event EventHandler<string>? NoticeRequested;

    internal void ShowError(string message) => NoticeRequested?.Invoke(this, message);

    public bool IsRenaming => _rename?.IsOpen == true;

    internal InlineRename? Rename => _rename;

    public void BeginRename(Salandual.Core.Operations.IOperationFileSystem fs)
    {
        if (_rename is null)
        {
            var layer = new Canvas { ClipToBounds = true, ZIndex = 5 };
            Grid.SetRow(layer, 2);
            Grid.SetRowSpan(layer, 2);
            _grid.Children.Add(layer);
            _rename = new InlineRename(this, layer);
        }

        _rename.Begin(fs);
    }

    public void CancelRename() => _rename?.Cancel();

    private ChangeDirectoryBox EnsureChangeDirectory()
    {
        if (_cd is not null)
        {
            return _cd;
        }

        var cd = _cd = new ChangeDirectoryBox();
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 6 };
        row.Children.Add(cd.Input);
        Grid.SetColumn(cd.BrowseButton, 1);
        row.Children.Add(cd.BrowseButton);
        _cdBar = new Border { Child = row, IsVisible = false, Padding = new Thickness(0, 4, 8, 4), Margin = new Thickness(0, 0, 0, 1) };
        _cdBar.Bind(BackgroundProperty, _cdBar.GetResourceObservable("Brush.ColumnHeader"));
        Grid.SetRow(_cdBar, 1);
        _grid.Children.Add(_cdBar);

        _cdList = cd.List;
        _cdList.IsVisible = false;
        _cdList.VerticalAlignment = VerticalAlignment.Top;
        _cdList.CornerRadius = new CornerRadius(0, 0, 8, 8);
        _cdList.ZIndex = 10;
        Grid.SetRow(_cdList, 2);
        Grid.SetRowSpan(_cdList, 2);
        _grid.Children.Add(_cdList);

        cd.Accepted += (_, target) =>
        {
            EndChangeDirectory();
            _ = Panel.NavigateAsync(target.Folder, target.FocusName);
        };
        cd.Cancelled += (_, _) => EndChangeDirectory();
        cd.Input.LostFocus += (_, _) => Dispatcher.UIThread.Post(CloseIfFocusLeft, DispatcherPriority.Background);
        return cd;
    }

    public FilePanel Panel { get; private set; }

    public void Attach(FilePanel panel)
    {
        if (ReferenceEquals(panel, Panel))
        {
            return;
        }

        Panel.Changed -= OnPanelChanged;
        Panel = panel;
        panel.Changed += OnPanelChanged;
        View.Panel = panel;
        OnPanelChanged(panel, PanelChange.Items | PanelChange.Focus | PanelChange.Selection);
    }

    public event EventHandler<string>? PathChanged;

    public DriveStrip Strip { get; }

    public DirectoryLine DirectoryLine { get; }

    public FilePanelView View { get; }

    public InformationLineView Info { get; }

    public bool IsActive
    {
        get => View.IsActivePanel;
        set
        {
            View.IsActivePanel = value;
            DirectoryLine.IsActive = value;
        }
    }

    public bool IsEditingPath => _cdBar?.IsVisible == true;

    internal ChangeDirectoryBox ChangeDirectory => EnsureChangeDirectory();

    public void ApplySettings(AppSettings settings, DisplayFormat format)
    {
        var bars = settings.Layout.Bars;
        View.RowHeight = settings.Layout.RowHeight;
        View.ClickToRename = settings.Panels.ClickToRename;
        View.ExtensionColumn = settings.Panels.ExtensionColumn;
        View.RightButtonSelects = settings.Mouse.RightClickUnselected == RightClickUnselected.RightButtonSelects;
        View.QuickSearchWithCtrlAlt = settings.Panels.QuickSearch == QuickSearchStart.CtrlAltLetter;
        View.ShowHeader = bars.ColumnHeader;
        View.Format = format;
        DirectoryLine.Format = format;
        Strip.IsVisible = bars.DriveStrip && settings.Layout.Strip == StripMode.PerPanel;
        DirectoryLine.IsVisible = bars.DirectoryLine;
        DirectoryLine.ShowMaximizeButton = settings.Layout.DirectoryLine.MaximizeButton;
        Info.IsVisible = bars.InformationLine;
        _infoTemplate = settings.Panels.InformationLine;
        Panel.SelectFoldersWithNumPlus = settings.Panels.SelectFoldersWithNumPlus;
        UpdateInfo();
    }

    public void GoTo(string path)
    {
        _ = Panel.NavigateAsync(path);
        View.Focus();
    }

    public void BeginChangeDirectory(ChangeDirectoryContext context)
    {
        var cd = EnsureChangeDirectory();

        var left = DirectoryLine.IsVisible ? DirectoryLine.DriveButtonBounds.Right + 6 : 6;
        _cdBar!.Margin = new Thickness(left - 4, 0, 0, 1);
        _cdBar.Padding = new Thickness(4, 4, 8, 4);
        _cdBar.MinHeight = DirectoryLine.IsVisible ? 0 : 34;
        _cdList!.Margin = new Thickness(left, -1, 8, 0);
        _cdBar.IsVisible = true;
        _cdList.IsVisible = true;
        cd.Open(context);
        cd.Input.Focus();
    }

    private void EndChangeDirectory()
    {
        if (_cd is null || _cdBar is not { IsVisible: true } || _cdList is null)
        {
            return;
        }

        var hadFocus = _cd.Input.IsFocused;
        _cdBar.IsVisible = false;
        _cdList.IsVisible = false;
        if (hadFocus)
        {
            View.Focus();
        }
    }

    private void CloseIfFocusLeft()
    {
        if (_cd is null || _cdBar is not { IsVisible: true } || _cdList is null || _cd.IsBusyElsewhere || _cd.Input.IsFocused)
        {
            return;
        }

        if (TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement() is Visual focused && (_cdBar.IsVisualAncestorOf(focused) || _cdList.IsVisualAncestorOf(focused)))
        {
            return;
        }

        EndChangeDirectory();
    }

    internal Border? ResultsBar => _resultsBar;

    internal string ResultsText => _resultsText?.Text ?? "";

    internal Button? ResultsBack => _resultsBack;

    internal Button? ResultsJump => _resultsJump;

    private void UpdateResultsBar()
    {
        if (Panel.Results is not { } results)
        {
            if (_resultsBar is not null)
            {
                _resultsBar.IsVisible = false;
            }

            return;
        }

        if (_resultsBar is null)
        {
            _resultsText = new TextBlock { FontSize = 12.5, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
            _resultsText.Bind(TextBlock.ForegroundProperty, _resultsText.GetResourceObservable("Brush.Accent"));
            _resultsBack = new Button { Padding = new Thickness(8, 2), FontSize = 12, VerticalAlignment = VerticalAlignment.Center, Focusable = false };
            _resultsBack.Click += (_, _) =>
            {
                _ = Panel.LeaveResultsAsync();
                View.Focus();
            };
            _resultsJump = new Button { Padding = new Thickness(8, 2), FontSize = 12, VerticalAlignment = VerticalAlignment.Center, Focusable = false };
            _resultsJump.Click += (_, _) =>
            {
                _ = Panel.GoToResultAsync();
                View.Focus();
            };
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto,Auto"), ColumnSpacing = 8 };
            var icon = Salandual.App.Theme.AppIcons.Glyph("Icon.Find", 14, 14, 1.4, "Brush.Accent");
            icon.VerticalAlignment = VerticalAlignment.Center;
            row.Children.Add(icon);
            Grid.SetColumn(_resultsText, 1);
            row.Children.Add(_resultsText);
            Grid.SetColumn(_resultsJump, 2);
            row.Children.Add(_resultsJump);
            Grid.SetColumn(_resultsBack, 3);
            row.Children.Add(_resultsBack);
            _resultsBar = new Border { Child = row, Padding = new Thickness(10, 3, 6, 3), MinHeight = 30 };
            _resultsBar.Bind(BackgroundProperty, _resultsBar.GetResourceObservable("Brush.InfoBackground"));
            _resultsBar.PointerPressed += (_, _) => View.Focus();
            Grid.SetRow(_resultsBar, 1);
            _grid.Children.Add(_resultsBar);
        }

        var back = WindowsPath.GetName(results.ReturnPath) is { Length: > 0 } name ? name : results.ReturnPath;
        _resultsText!.Text = string.Join(" · ", new[] { AppStrings.Panel_ResultsHead, results.What, Loc.Format(AppStrings.Panel_ResultsCount, Panel.Count) }.Where(s => s.Length > 0));
        _resultsBack!.Content = "← " + (Bounds.Width is > 0 and < 420 ? AppStrings.Panel_ResultsBackShort : Loc.Format(AppStrings.Panel_ResultsBack, back));
        ToolTip.SetTip(_resultsBack, Loc.Format(AppStrings.Panel_ResultsBackTip, results.ReturnPath));
        _resultsJump!.Content = "↗ " + (Bounds.Width is > 0 and < 420 ? AppStrings.Panel_ResultsJumpShort : AppStrings.Panel_ResultsJump);
        ToolTip.SetTip(_resultsJump, AppStrings.Panel_ResultsJumpTip);
        _resultsJump.IsEnabled = Panel.Count > 0;
        _resultsBar.IsVisible = true;
    }

    private void OnPanelChanged(object? sender, PanelChange change)
    {
        if ((change & PanelChange.Items) != 0)
        {
            UpdateResultsBar();
        }

        if ((change & PanelChange.Items) != 0 && DirectoryLine.Path != Panel.Path && Panel.Results is null)
        {
            DirectoryLine.Path = Panel.Path;
            Strip.Current = WindowsPath.GetRoot(Panel.Path);
            ReadFreeSpace(Panel.Path);
            PathChanged?.Invoke(this, Panel.Path);
        }

        if ((change & (PanelChange.Items | PanelChange.Focus | PanelChange.Selection | PanelChange.Sizes)) != 0)
        {
            UpdateInfo();
        }
    }

    private void UpdateInfo()
    {
        var format = View.Format;
        var left = Panel.FocusedEntry is { } focused
            ? InformationLine.Expand(_infoTemplate, focused, Panel.IsUp(Panel.Focus), format, Panel.TryGetDirectorySize(focused, out var dirSize) ? dirSize : null)
            : "";
        Info.Set(left, Panel.Path.Length > 0 ? InformationLine.Summary(Panel, format, CultureInfo.CurrentCulture) : "");
    }

    private void ReadFreeSpace(string path)
    {
        var version = ++_spaceVersion;
        _ = Task.Run(() =>
        {
            if (DiskSpace.TryGet(path, out var free, out var total))
            {
                Dispatcher.UIThread.Post(() =>
                {
                    if (version == _spaceVersion)
                    {
                        DirectoryLine.SetSpace(path, free, total);
                    }
                });
            }
        });
    }
}

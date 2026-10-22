// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.App/MainWindow.TitleBar.cs

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Chrome;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Salandual.App.Controls;
using Salandual.App.Resources;
using Salandual.App.Theme;
using Salandual.Core.Commands;
using Salandual.Core.Settings;

namespace Salandual.App;

public partial class MainWindow
{
    private Grid? _titleBar;
    private Panel? _menuHost;
    private TextBlock? _titleText;
    private Button? _appMenuButton;
    private Button? _hamburgerButton;
    private Control? _plainLogo;
    private MenuFlyout? _appMenu;

    private bool _menuBarOnce;

    private bool _altTap;

    internal Grid? TitleBar => _titleBar;

    internal MenuFlyout? AppMenu => _appMenu;

    internal bool IsMenuBarShownOnce => _menuBarOnce;

    internal bool IsMenuInTitleBar => MainMenuBar.Parent == _menuHost;

    private void ApplyMenuBar(LayoutSettings layout)
    {
        EnsureTitleBar();
        var shown = layout.Bars.MenuBar;
        PlaceMenu(inTitleBar: shown && layout.MenuRow == MenuRow.InTitleBar);
        MainMenuBar.IsVisible = shown || _menuBarOnce;

        var behindIcon = !shown && layout.MenuButton == MenuButtonStyle.AppIcon;
        _appMenuButton!.IsVisible = behindIcon;
        _plainLogo!.IsVisible = !behindIcon;
        _hamburgerButton!.IsVisible = !shown && layout.MenuButton == MenuButtonStyle.Hamburger;
        _titleText!.Margin = new Thickness(IsMenuInTitleBar ? 12 : 8, 0, 12, 0);
    }

    private void PlaceMenu(bool inTitleBar)
    {
        Panel target = inTitleBar ? _menuHost! : RootDock;
        if (MainMenuBar.Parent == target)
        {
            return;
        }

        (MainMenuBar.Parent as Panel)?.Children.Remove(MainMenuBar);
        if (inTitleBar)
        {
            MainMenuBar.Padding = new Thickness(0);
            MainMenuBar.VerticalAlignment = VerticalAlignment.Center;
            _menuHost!.Children.Add(MainMenuBar);
        }
        else
        {
            MainMenuBar.Padding = new Thickness(4, 2);
            MainMenuBar.VerticalAlignment = VerticalAlignment.Stretch;
            DockPanel.SetDock(MainMenuBar, Dock.Top);
            RootDock.Children.Insert(RootDock.Children.IndexOf(_titleBar!) + 1, MainMenuBar);
        }
    }

    private void EnsureTitleBar()
    {
        if (_titleBar is not null)
        {
            return;
        }

        TitleBarParts.ExtendIntoTitleBar(this);
        _titleBar = new Grid
        {
            Height = TitleBarParts.Height,
            ColumnDefinitions = new ColumnDefinitions($"Auto,Auto,*,Auto,{3 * TitleBarParts.CaptionButtonWidth}"),
            Background = Brushes.Transparent,
        };
        WindowDecorationProperties.SetElementRole(_titleBar, WindowDecorationsElementRole.TitleBar);
        WindowDecorationProperties.SetElementRole(MainMenuBar, WindowDecorationsElementRole.User);

        _appMenuButton = TitleBarParts.Button(new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 5,
            Children = { TitleBarParts.Logo(), AppIcons.Glyph("Icon.TitleChevron", 10, 10) },
        }, AppStrings.Title_MenuTip, AppStrings.Title_Menu);
        _appMenuButton.Margin = new Thickness(6, 0, 0, 0);
        _plainLogo = TitleBarParts.Logo();
        _plainLogo.Margin = new Thickness(12, 0, 4, 0);
        _hamburgerButton = TitleBarParts.Button(AppIcons.Glyph("Icon.Hamburger", 16, 16), AppStrings.Title_MenuTip, AppStrings.Title_Menu);
        _hamburgerButton.Width = 46;
        _menuHost = new Panel { VerticalAlignment = VerticalAlignment.Center };
        _titleText = TitleBarParts.Title(this);
        TitleBarParts.DimWhenInactive(this, _plainLogo);

        Grid.SetColumn(_menuHost, 1);
        Grid.SetColumn(_titleText, 2);
        Grid.SetColumn(_hamburgerButton, 3);
        _titleBar.Children.Add(_appMenuButton);
        _titleBar.Children.Add(_plainLogo);
        _titleBar.Children.Add(_menuHost);
        _titleBar.Children.Add(_titleText);
        _titleBar.Children.Add(_hamburgerButton);

        _appMenu = new MenuFlyout();
        _appMenu.Opening += (_, _) => RefreshMenuStates();
        _appMenu.Closed += (_, _) =>
        {
            if (!ActivePanel.IsEditingPath)
            {
                ActivePanel.View.Focus();
            }
        };
        _appMenuButton.Click += (_, _) => OpenAppMenu(_appMenuButton, PlacementMode.BottomEdgeAlignedLeft);
        _titleBar.PointerReleased += (_, e) =>
        {
            if (e.InitialPressMouseButton == MouseButton.Right && e.Source is Visual source && source.FindAncestorOfType<Menu>(includeSelf: true) is null && source.FindAncestorOfType<Button>(includeSelf: true) is null)
            {
                ShowWindowMenu(e.GetPosition(this));
                e.Handled = true;
            }
        };
        _plainLogo.PointerPressed += (_, e) =>
        {
            if (e.GetCurrentPoint(_plainLogo).Properties.IsLeftButtonPressed)
            {
                e.Handled = true;
                ShowWindowMenu(new Point(4, TitleBarParts.Height));
            }
        };
        _hamburgerButton.Click += (_, _) => OpenAppMenu(_hamburgerButton, PlacementMode.BottomEdgeAlignedRight);

        DockPanel.SetDock(_titleBar, Dock.Top);
        RootDock.Children.Insert(0, _titleBar);
    }

    internal void ShowWindowMenu(Point point) => Dispatcher.UIThread.Post(() => SystemMenu.Show(this, point));

    internal void OpenAppMenu(Button? button = null, PlacementMode placement = PlacementMode.BottomEdgeAlignedLeft)
    {
        EnsureTitleBar();
        button ??= _appMenuButton!.IsVisible ? _appMenuButton : _hamburgerButton!;
        if (button == _hamburgerButton)
        {
            placement = PlacementMode.BottomEdgeAlignedRight;
        }

        FillAppMenu();
        _appMenu!.Placement = placement;
        _appMenu.ShowAt(button);
    }

    private void FillAppMenu()
    {
        if (_appMenu!.Items.Count > 0)
        {
            return;
        }

        foreach (var node in MainMenu.Create())
        {
            var item = (MenuItem)CreateMenuItem(node);
            item.SubmenuOpened += (_, _) => RefreshMenuStates();
            _appMenu.Items.Add(item);
        }

        _appMenu.Items.Add(new Separator());
        _appMenu.Items.Add(CreateMenuItem(new MenuNode(AppStrings.Title_ShowMenuBar, "View.ToggleMenuBar") { Checkable = true }));
        _appMenu.Items.Add(CreateMenuItem(new MenuNode(AppStrings.Title_Configuration, "App.Options")));
        _appMenu.Items.Add(new Separator());
        _appMenu.Items.Add(CreateMenuItem(new MenuNode(AppStrings.Title_Exit, "App.Exit")));
    }

    private void ShowMenuBarOnce(int? openIndex)
    {
        FillMenus();
        _menuBarOnce = true;
        MainMenuBar.IsVisible = true;
        Dispatcher.UIThread.Post(() =>
        {
            if (openIndex is { } i && MainMenuBar.Items[i] is MenuItem item)
            {
                item.Focus();
                item.Open();
            }
            else
            {
                MainMenuBar.Open();
            }
        }, DispatcherPriority.Loaded);
    }

    private void OnMenuBarClosed()
    {
        if (_menuBarOnce)
        {
            _menuBarOnce = false;
            MainMenuBar.IsVisible = _services.Settings.Current.Layout.Bars.MenuBar;
        }
    }

    private void OpenMenuBar()
    {
        FillMenus();
        if (MainMenuBar.IsVisible)
        {
            MainMenuBar.Open();
        }
        else
        {
            ShowMenuBarOnce(null);
        }
    }

    private void OnMenuKey(object? sender, KeyEventArgs e)
    {
        if (MainMenuBar.IsVisible || _services.Settings.Current.Layout.Bars.MenuBar)
        {
            _altTap = false;
            return;
        }

        var isAlt = e.Key is Key.LeftAlt or Key.RightAlt;
        if (e.RoutedEvent == KeyDownEvent)
        {
            if (isAlt)
            {
                _altTap = e.KeyModifiers == Avalonia.Input.KeyModifiers.Alt;
                return;
            }

            _altTap = false;
            if (e.KeyModifiers == Avalonia.Input.KeyModifiers.Alt && e.Key is >= Key.A and <= Key.Z && MenuIndexForAccessKey((char)('A' + (e.Key - Key.A))) is { } index)
            {
                ShowMenuBarOnce(index);
                e.Handled = true;
            }
        }
        else if (isAlt && _altTap)
        {
            _altTap = false;
            ShowMenuBarOnce(null);
            e.Handled = true;
        }
    }

    private int? MenuIndexForAccessKey(char letter)
    {
        for (var i = 0; i < MainMenuBar.Items.Count; i++)
        {
            if (MainMenuBar.Items[i] is MenuItem { Header: string header } && header.IndexOf('_') is var u and >= 0 && u + 1 < header.Length
                && char.ToUpperInvariant(header[u + 1]) == letter)
            {
                return i;
            }
        }

        return null;
    }
}

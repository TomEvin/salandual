// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.App/MainWindow.UserMenu.cs

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Primitives.PopupPositioning;
using Salandual.App.Input;
using Salandual.App.Panels;
using Salandual.App.Resources;
using Salandual.App.Theme;
using Salandual.Core.Commands;
using Salandual.Core.Resources;
using Salandual.Core.UserMenu;
using Salandual.Platform.Windows.FileSystem;

namespace Salandual.App;

public partial class MainWindow
{
    private readonly Dictionary<string, Image?> _userMenuIcons = new(StringComparer.OrdinalIgnoreCase);

    internal ContextMenu? UserMenuPopup { get; private set; }

    internal Dialogs.UserMenuAskDialog? OpenUserMenuAsk { get; private set; }

    private UserMenuSettings UserMenuSettings => _services.Settings.Current.UserMenu;

    internal UserMenuContext UserMenuContextFor(PanelHost host, IReadOnlyList<string>? names = null)
    {
        static (IReadOnlyList<UserMenuEntry> Items, bool Selection, UserMenuEntry? Focused) Read(PanelHost h)
        {
            var panel = h.Panel;
            UserMenuEntry Entry(string name) => new(WindowsPath.Join(panel.Path, name), EntryNamed(panel, name)?.IsDirectory ?? Directory.Exists(WindowsPath.Join(panel.Path, name)));
            var selection = panel.GetSelectionSummary().Count > 0;
            var focused = panel.FocusedEntry is { } f && !panel.IsUp(panel.Focus) ? Entry(f.Name) : null;
            var names = selection || focused is not null ? panel.GetOperationNames() : [];
            return ([.. names.Select(Entry)], selection, focused);
        }

        var other = OtherPanel(host);
        var (items, selection, focused) = Read(host);
        var (otherItems, otherSelection, otherFocused) = Read(other);
        if ((names ?? _namesOverride) is { } only)
        {
            items = [.. only.Select(n => Path.IsPathFullyQualified(n)
                ? new UserMenuEntry(n, Directory.Exists(n))
                : new UserMenuEntry(WindowsPath.Join(host.Panel.Path, n), EntryNamed(host.Panel, n)?.IsDirectory ?? false))];
            selection = only.Count > 1;
            focused = only.Count == 1 ? items[0] : focused;
        }

        return new UserMenuContext
        {
            ActivePath = host.Panel.Path,
            OtherPath = other.Panel.Path,
            ActiveIsLeft = host == _panels[0],
            Selected = items,
            HasSelection = selection,
            Focused = focused,
            OtherSelected = otherItems,
            OtherHasSelection = otherSelection,
            OtherFocused = otherFocused,
            OtherNamed = name => EntryNamed(other.Panel, name) is { } e ? new UserMenuEntry(WindowsPath.Join(other.Panel.Path, name), e.IsDirectory) : null,
            AppFolder = AppContext.BaseDirectory,
            ShortPath = _services.IsIsolated ? p => p : ShortNames.Of,
        };
    }

    internal void ShowUserMenu(PanelHost host)
    {
        if (UserMenuBlocked(host))
        {
            return;
        }

        var settings = UserMenuSettings;
        if (settings.Items.Count == 0)
        {
            ShowNotice(AppStrings.UserMenu_Empty, (AppStrings.UserMenu_MakeOne, () => _ = OpenOptionsAsync(UserMenuPage)));
            return;
        }

        UserMenuPopup?.Close();
        var context = UserMenuContextFor(host);
        var at = host.View.NameBounds(host.Panel.Focus) is { } r ? new Point(r.X + 24, r.Bottom) : new Point(40, 40);
        var menu = new ContextMenu
        {
            PlacementTarget = host.View,
            Placement = PlacementMode.AnchorAndGravity,
            PlacementRect = new Rect(at, new Size(1, 1)),
            PlacementAnchor = PopupAnchor.TopLeft,
            PlacementGravity = PopupGravity.BottomRight,
        };
        Head(menu.Items, UserMenuHead(context));
        AddUserMenuItems(menu.Items, settings.Items, host, context, settings.Unfit);
        if (menu.Items.Count > 0 && menu.Items[^1] is not Separator)
        {
            menu.Items.Add(new Separator());
        }

        var edit = new MenuItem { Header = AppStrings.UserMenu_Edit };
        edit.Click += (_, _) => _ = OpenOptionsAsync(UserMenuPage);
        menu.Items.Add(edit);
        UserMenuPopup = menu;
        menu.Closed += (_, _) =>
        {
            if (ReferenceEquals(UserMenuPopup, menu))
            {
                UserMenuPopup = null;
            }

            host.View.Focus();
        };
        menu.Open(host.View);
    }

    internal const string UserMenuPage = "usermenu";

    private static string UserMenuHead(UserMenuContext context) => context.Selected.Count switch
    {
        0 => Loc.Format(AppStrings.UserMenu_HeadFolder, WindowsPath.GetName(context.ActivePath).TrimEnd('\\') is { Length: > 0 } n ? n : context.ActivePath),
        1 => Loc.Format(AppStrings.UserMenu_HeadOne, context.Selected[0].Name),
        var count => Loc.Format(AppStrings.UserMenu_HeadMany, Loc.Plural(count, AppStrings.Count_Items_One, AppStrings.Count_Items_Other)),
    };

    private bool UserMenuBlocked(PanelHost host)
    {
        if (host.Panel.Path.Length == 0)
        {
            return true;
        }

        if (ArchiveAt(host) is not null)
        {
            ShowNotice(AppStrings.UserMenu_InArchive);
            return true;
        }

        return false;
    }

    private void AddUserMenuItems(ItemCollection items, IEnumerable<UserMenuItem> list, PanelHost host, UserMenuContext context, UserMenuUnfit unfit, Func<UserMenuItem, bool>? only = null, IReadOnlyList<string>? names = null)
    {
        foreach (var item in list)
        {
            switch (item.Kind)
            {
                case UserMenuItemKind.Separator:
                    if (items.Count > 0 && items[^1] is not Separator)
                    {
                        items.Add(new Separator());
                    }

                    break;
                case UserMenuItemKind.Submenu:
                    var sub = new MenuItem { Header = ContextMenuText(item.Name), Icon = AppIcons.Create("Icon.Folder") };
                    AddUserMenuItems(sub.Items, item.Items, host, context, unfit, only, names);
                    while (sub.Items.Count > 0 && sub.Items[^1] is Separator)
                    {
                        sub.Items.RemoveAt(sub.Items.Count - 1);
                    }

                    if (sub.Items.Count > 0)
                    {
                        items.Add(sub);
                    }

                    break;
                default:
                    if (only is not null && !only(item))
                    {
                        break;
                    }

                    var why = UserMenuFit.WhyNot(item, context.Selected);
                    if (why is not null && unfit == UserMenuUnfit.Hidden)
                    {
                        break;
                    }

                    items.Add(UserMenuMenuItem(item, host, why, names));
                    break;
            }
        }

        while (items.Count > 0 && items[^1] is Separator)
        {
            items.RemoveAt(items.Count - 1);
        }
    }

    private MenuItem UserMenuMenuItem(UserMenuItem item, PanelHost host, string? why, IReadOnlyList<string>? names)
    {
        var mi = new MenuItem { Header = ContextMenuText(item.Name), Icon = UserMenuIcon(item), IsEnabled = why is null };
        if (why is not null)
        {
            ToolTip.SetTip(mi, why);
            ToolTip.SetShowOnDisabled(mi, true);
        }

        if (item.Key is { } key && KeyChord.TryParse(key, out var chord) && KeyMapping.ToGesture(chord) is { } gesture)
        {
            mi.InputGesture = gesture;
        }

        mi.Click += (_, _) => _ = RunUserMenuItemAsync(host, item, names);
        return mi;
    }

    private static string ContextMenuText(string name) => AccessKeyText(name.Length > 0 ? name : "…");

    private Image? UserMenuIcon(UserMenuItem item)
    {
        var file = item.Icon
            ?? (item.Kind == UserMenuItemKind.Script
                ? _services.Starter.Find("pwsh.exe", out _) ?? _services.Starter.WindowsPowerShell
                : _services.Starter.Find(Environment.ExpandEnvironmentVariables(item.Program), out _));
        if (file is null)
        {
            return null;
        }

        if (!_userMenuIcons.TryGetValue(file, out var image))
        {
            image = ShellIcon(ShellAccess.LoadIcon(file, 16));
            _userMenuIcons[file] = image;
        }

        return image is null ? null : new Image { Source = image.Source, Width = 16, Height = 16 };
    }

    internal Func<UserMenuItem, string, string?>? UserMenuAskAnswer { get; set; }

    internal async Task RunUserMenuItemAsync(PanelHost host, UserMenuItem item, IReadOnlyList<string>? names = null)
    {
        if (UserMenuBlocked(host))
        {
            return;
        }

        var context = UserMenuContextFor(host, names);
        if (UserMenuFit.WhyNot(item, context.Selected) is { } why)
        {
            ShowNotice(Loc.Format(AppStrings.UserMenu_CantRun, item.PlainName, why));
            return;
        }

        var starter = _services.Starter;
        var plan = UserMenuRunner.Plan(item, context, starter, out var error);
        if (plan is null)
        {
            ShowUserMenuProblem(item, error);
            return;
        }

        if (item.AskFirst)
        {
            string? arguments;
            var dontAskAgain = false;
            if (UserMenuAskAnswer is { } answer)
            {
                arguments = answer(item, item.Arguments);
            }
            else
            {
                var dialog = OpenUserMenuAsk = new Dialogs.UserMenuAskDialog(item, plan, context, starter);
                try
                {
                    arguments = await dialog.ShowDialog<string?>(this);
                    dontAskAgain = dialog.DontAskAgain;
                }
                finally
                {
                    OpenUserMenuAsk = null;
                    host.View.Focus();
                }
            }

            if (arguments is null)
            {
                return;
            }

            if (dontAskAgain)
            {
                ChangeUserMenuItem(item, i => i with { AskFirst = false });
            }

            if (item.Kind == UserMenuItemKind.Program && arguments != item.Arguments)
            {
                plan = UserMenuRunner.Plan(item, context, starter, out error, arguments);
                if (plan is null)
                {
                    ShowUserMenuProblem(item, error);
                    return;
                }
            }
        }

        string NewListFile() => _services.Temp.NewFile("usermenu", ".txt");
        if (plan.Steps.Count == 1)
        {
            using var running = UserMenuRunner.Start(plan.Steps[0], starter, NewListFile, out var failure, out var detail);
            if (running is null)
            {
                ShowUserMenuProblem(item, Loc.Format(Core.Resources.Strings.UserMenu_DidNotStart, detail ?? failure.ToString()));
            }

            return;
        }

        Operations.Add(UserMenuRunner.Job(plan, starter, NewListFile), toQueue: false);
    }

    private void ShowUserMenuProblem(UserMenuItem item, string? error)
        => ShowNotice(Loc.Format(AppStrings.UserMenu_CantRun, item.PlainName, error ?? ""), (AppStrings.UserMenu_Options, () => _ = OpenOptionsAsync(UserMenuPage)));

    private void ChangeUserMenuItem(UserMenuItem item, Func<UserMenuItem, UserMenuItem> change)
    {
        static List<UserMenuItem> In(List<UserMenuItem> list, UserMenuItem item, Func<UserMenuItem, UserMenuItem> change)
            => [.. list.Select(i => ReferenceEquals(i, item) ? change(i) : i.Kind == UserMenuItemKind.Submenu ? i with { Items = In(i.Items, item, change) } : i)];
        _services.Settings.Update(s => s with { UserMenu = s.UserMenu with { Items = In(s.UserMenu.Items, item, change) } });
    }

    internal bool TryUserMenuKey(PanelHost host, KeyChord chord)
    {
        foreach (var item in UserMenuSettings.All())
        {
            if (item.IsRunnable && item.Key is { } key && KeyChord.TryParse(key, out var own) && own == chord)
            {
                _ = RunUserMenuItemAsync(host, item);
                return true;
            }
        }

        return false;
    }

    private void AddUserMenuTop(ItemCollection items, PanelHost host, MenuTarget target)
    {
        if (ArchiveAt(host) is not null || !UserMenuSettings.All().Any(i => i.IsRunnable && i.InContextMenu == UserMenuContextPlace.Top))
        {
            return;
        }

        var names = target.OverridesSelection ? target.Names : null;
        AddUserMenuItems(items, UserMenuSettings.Items.SelectMany(Runnable), host, UserMenuContextFor(host, names), UserMenuUnfit.Hidden, i => i.InContextMenu == UserMenuContextPlace.Top, names);

        static IEnumerable<UserMenuItem> Runnable(UserMenuItem item) => item.Kind == UserMenuItemKind.Submenu ? item.Items.SelectMany(Runnable) : item.IsRunnable ? [item] : [];
    }

    private MenuItem? UserMenuSubmenu(PanelHost host, MenuTarget target)
    {
        if (ArchiveAt(host) is not null || !UserMenuSettings.All().Any(i => i.IsRunnable && i.InContextMenu == UserMenuContextPlace.Submenu))
        {
            return null;
        }

        var names = target.OverridesSelection ? target.Names : null;
        var sub = new MenuItem { Header = AppStrings.UserMenu_Submenu, Icon = AppIcons.Create("UserMenu.Open") };
        AddUserMenuItems(sub.Items, UserMenuSettings.Items, host, UserMenuContextFor(host, names), UserMenuSettings.Unfit, i => i.InContextMenu == UserMenuContextPlace.Submenu, names);
        return sub.Items.Count > 0 ? sub : null;
    }
}

// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.App/Options/UserMenuEditor.cs

using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Styling;
using Avalonia.VisualTree;
using Salandual.App.Input;
using Salandual.App.Resources;
using Salandual.Core.Commands;
using Salandual.Core.Resources;
using Salandual.Core.Settings;
using Salandual.Core.UserMenu;
using static Salandual.App.Options.OptionsUi;

namespace Salandual.App.Options;

internal sealed class UserMenuEditor : Grid
{
    private readonly SettingsService _settings;
    private readonly IProgramStarter _starter;
    private readonly CommandRegistry _commands;
    private readonly Func<UserMenuContext?> _preview;
    private readonly Func<UserMenuItem, IImage?>? _icon;
    private readonly ListBox _tree = new()
    {
        MinHeight = 240, MaxHeight = 480, Background = Brushes.Transparent, BorderThickness = default,
        ItemsPanel = new Avalonia.Controls.Templates.FuncTemplate<Panel?>(() => new StackPanel()),
    };
    private readonly StackPanel _top = new() { Spacing = 10 };
    private readonly StackPanel _form = new() { Spacing = 8 };
    private List<int> _path = [];
    private bool _filling;

    public UserMenuEditor(SettingsService settings, IProgramStarter starter, CommandRegistry commands, Func<UserMenuContext?> preview, Func<UserMenuItem, IImage?>? icon = null)
    {
        _settings = settings;
        _starter = starter;
        _commands = commands;
        _preview = preview;
        _icon = icon;

        ColumnDefinitions = new ColumnDefinitions("3*,2*");
        RowDefinitions = new RowDefinitions("Auto,Auto");
        ColumnSpacing = 20;
        RowSpacing = 16;

        Styles.Add(new Style(x => x.OfType<Button>().Class("tool").Class(":disabled")) { Setters = { new Setter(OpacityProperty, 0.35) } });
        Styles.Add(new Style(x => x.OfType<Button>().Class("tool").Class(":disabled").Template().OfType<Avalonia.Controls.Presenters.ContentPresenter>().Name("PART_ContentPresenter"))
        {
            Setters = { new Setter(Avalonia.Controls.Presenters.ContentPresenter.BackgroundProperty, Brushes.Transparent), new Setter(Avalonia.Controls.Presenters.ContentPresenter.BorderBrushProperty, Brushes.Transparent) },
        });

        _tree.SelectionChanged += (_, _) =>
        {
            if (!_filling && _tree.SelectedItem is ListBoxItem { Tag: List<int> path })
            {
                _path = path;
                ShowForm();
            }
        };
        _tree.KeyDown += (_, e) =>
        {
            if (RenameBox is not null)
            {
                return;
            }

            if (e.Key == Key.F2 && e.KeyModifiers == Avalonia.Input.KeyModifiers.None)
            {
                StartRename();
                e.Handled = true;
            }
            else if (e.KeyModifiers == Avalonia.Input.KeyModifiers.Alt && e.Key is Key.Up or Key.Down)
            {
                Move(e.Key == Key.Up ? -1 : +1);
                e.Handled = true;
            }
            else if (e.Key == Key.Delete && e.KeyModifiers == Avalonia.Input.KeyModifiers.None)
            {
                Remove();
                e.Handled = true;
            }
        };
        _tree.DoubleTapped += (_, e) =>
        {
            if (e.Source is not Visual v || v.FindAncestorOfType<Button>(includeSelf: true) is null)
            {
                StartRename();
            }
        };
        Avalonia.Automation.AutomationProperties.SetName(_tree, AppStrings.Opt_UserMenu_Items);

        NewItem = Tool("Icon.NewItem", AppStrings.Opt_UserMenu_NewItem, () => Insert(new UserMenuItem { Name = AppStrings.Opt_UserMenu_NewItemName, Arguments = "\"$(FullName)\"" }));
        NewSubmenu = Tool("File.CreateDirectory", AppStrings.Opt_UserMenu_NewSubmenu, () => Insert(new UserMenuItem { Name = AppStrings.Opt_UserMenu_NewSubmenuName, Kind = UserMenuItemKind.Submenu }));
        NewSeparator = Tool("Icon.NewSeparator", AppStrings.Opt_UserMenu_NewSeparator, () => Insert(new UserMenuItem { Kind = UserMenuItemKind.Separator }));
        IconButton = Tool("Icon.Picture", AppStrings.Opt_UserMenu_Icon, () => { });
        IconButton.Margin = new Thickness(10, 0, 0, 0);
        var tools = new Border
        {
            BorderThickness = new Thickness(0, 0, 0, 1),
            Padding = new Thickness(4),
            Child = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2, Children = { NewItem, NewSubmenu, NewSeparator, IconButton } },
        }.Res(Border.BorderBrushProperty, "Brush.Border");
        DockPanel.SetDock(tools, Dock.Top);
        var block = new Border
        {
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            ClipToBounds = true,
            VerticalAlignment = VerticalAlignment.Top,
            Child = new DockPanel { Children = { tools, _tree } },
        }.Res(Border.BorderBrushProperty, "Brush.Border");
        Children.Add(block);
        SetColumn(_top, 1);
        Children.Add(_top);
        SetRow(_form, 1);
        SetColumnSpan(_form, 2);
        Children.Add(_form);

        FillTree();
        if (Items.Count > 0)
        {
            Select([0]);
        }
        else
        {
            ShowForm();
        }
    }

    internal ListBox Tree => _tree;

    internal Button NewItem { get; }

    internal Button NewSubmenu { get; }

    internal Button NewSeparator { get; }

    internal Button IconButton { get; }

    internal StackPanel Form => _form;

    internal Dictionary<string, Control> Fields { get; } = [];

    internal string PreviewText { get; private set; } = "";

    private List<UserMenuItem> Items => _settings.Current.UserMenu.Items;

    internal TextBox? RenameBox { get; private set; }

    private static Button Tool(string glyph, string tip, Action click, double size = 26)
    {
        var icon = Salandual.App.Theme.AppIcons.Glyph(glyph, 16, 16, 1.3);
        icon.HorizontalAlignment = HorizontalAlignment.Center;
        var b = new Button
        {
            Content = icon, Width = size, Height = size, Padding = default, Background = Brushes.Transparent, BorderThickness = default,
            HorizontalContentAlignment = HorizontalAlignment.Center, VerticalContentAlignment = VerticalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
        };
        ToolTip.SetTip(b, tip);
        Avalonia.Automation.AutomationProperties.SetName(b, tip);
        b.Classes.Add("tool");
        b.Click += (_, e) =>
        {
            e.Handled = true;
            click();
        };
        return b;
    }

    private static IEnumerable<(List<int> Path, UserMenuItem Item)> Flatten(List<UserMenuItem> items, List<int> parent)
    {
        for (var i = 0; i < items.Count; i++)
        {
            List<int> path = [.. parent, i];
            yield return (path, items[i]);
            foreach (var inner in Flatten(items[i].Items, path))
            {
                yield return inner;
            }
        }
    }

    private void FillTree()
    {
        _filling = true;
        _tree.Items.Clear();
        foreach (var (path, item) in Flatten(Items, []))
        {
            _tree.Items.Add(new ListBoxItem { Tag = path, Content = Row(item, path), Padding = new Thickness(6, 2, 2, 2) });
        }

        _filling = false;
    }

    private RowView Row(UserMenuItem item, List<int> path)
    {
        var row = new RowView { Margin = new Thickness((path.Count - 1) * 18, 0, 0, 0) };
        var tools = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(6, 0, 0, 0) };
        var rename = Tool("Icon.Rename", AppStrings.Opt_UserMenu_RenameTip, () => On(path, StartRename), 22);
        if (item.Kind == UserMenuItemKind.Separator)
        {
            rename.IsEnabled = false;
            rename.Opacity = 0;
        }

        var top = path.Count == 1;
        var up = Tool("Icon.ArrowUp", AppStrings.Opt_UserMenu_UpTip, () => On(path, () => Move(-1)), 22);
        up.IsEnabled = !(top && path[0] == 0);
        var down = Tool("Icon.ArrowDown", AppStrings.Opt_UserMenu_DownTip, () => On(path, () => Move(+1)), 22);
        down.IsEnabled = !(top && path[0] == Items.Count - 1);
        tools.Children.Add(rename);
        tools.Children.Add(up);
        tools.Children.Add(down);
        tools.Children.Add(Tool("Icon.Bin", AppStrings.Opt_UserMenu_RemoveTip, () => On(path, Remove), 22));
        DockPanel.SetDock(tools, Dock.Right);
        row.Children.Add(tools);

        var line = new DockPanel();
        if (item.Kind == UserMenuItemKind.Separator)
        {
            var label = Text(AppStrings.Opt_UserMenu_Separator, 12, brush: "Brush.TertiaryText").Also(t => t.VerticalAlignment = VerticalAlignment.Center);
            DockPanel.SetDock(label, Dock.Left);
            line.Children.Add(label);
            line.Children.Add(new Border { Height = 1, Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center }.Res(Border.BackgroundProperty, "Brush.Border"));
            row.Children.Add(line);
            return row;
        }

        Control icon = (item.Kind == UserMenuItemKind.Submenu ? null : _icon?.Invoke(item)) is { } image
            ? new Image { Source = image, Width = 16, Height = 16 }
            : Salandual.App.Theme.AppIcons.Glyph(item.Kind switch
            {
                UserMenuItemKind.Submenu => "Icon.Folder",
                UserMenuItemKind.Script => "Icon.Script",
                _ => "Navigation.Execute",
            }, 16, 16, 1.2);
        icon.VerticalAlignment = VerticalAlignment.Center;
        icon.Margin = new Thickness(0, 0, 8, 0);
        DockPanel.SetDock(icon, Dock.Left);
        line.Children.Add(icon);
        row.Label = Text(item.PlainName.Length > 0 ? item.PlainName : "…", 13).Also(t => { t.VerticalAlignment = VerticalAlignment.Center; t.TextTrimming = TextTrimming.CharacterEllipsis; });
        row.NameHost.Children.Add(row.Label);
        line.Children.Add(row.NameHost);
        row.Children.Add(line);
        return row;
    }

    private void On(List<int> path, Action act)
    {
        if (!_path.SequenceEqual(path))
        {
            Select(path);
        }

        act();
    }

    private sealed class RowView : DockPanel
    {
        public Grid NameHost { get; } = new();

        public TextBlock? Label { get; set; }
    }

    internal void StartRename()
    {
        if (Chosen is not { Kind: not UserMenuItemKind.Separator } item || _tree.SelectedItem is not ListBoxItem { Content: RowView row })
        {
            return;
        }

        var box = new TextBox { Text = item.Name, FontSize = 13, MinHeight = 0, Padding = new Thickness(4, 1), VerticalContentAlignment = VerticalAlignment.Center };
        Avalonia.Automation.AutomationProperties.SetName(box, AppStrings.Opt_UserMenu_Name);
        ToolTip.SetTip(box, AppStrings.Opt_UserMenu_RenameTip);
        row.NameHost.Children.Clear();
        row.NameHost.Children.Add(box);
        RenameBox = box;
        var done = false;
        void Finish(bool keep)
        {
            if (done)
            {
                return;
            }

            done = true;
            RenameBox = null;
            var text = (box.Text ?? "").Trim();
            if (keep && text.Length > 0 && text != item.Name)
            {
                Change(i => i with { Name = text });
            }
            else
            {
                row.NameHost.Children.Clear();
                row.NameHost.Children.Add(row.Label!);
            }

            _tree.Focus();
        }

        box.KeyDown += (_, e) =>
        {
            if (e.Key is Key.Enter or Key.Escape)
            {
                Finish(e.Key == Key.Enter);
            }

            e.Handled = true;
        };
        box.LostFocus += (_, _) => Finish(true);
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            box.Focus();
            box.SelectAll();
        });
    }

    internal void Select(IReadOnlyList<int> path)
    {
        _path = [.. path];
        _filling = true;
        _tree.SelectedItem = _tree.Items.OfType<ListBoxItem>().FirstOrDefault(i => i.Tag is List<int> p && p.SequenceEqual(path));
        _filling = false;
        ShowForm();
    }

    private UserMenuItem? Chosen => Find(Items, _path);

    private static UserMenuItem? Find(List<UserMenuItem> items, IReadOnlyList<int> path)
    {
        UserMenuItem? item = null;
        var list = items;
        foreach (var i in path)
        {
            if (i < 0 || i >= list.Count)
            {
                return null;
            }

            item = list[i];
            list = item.Items;
        }

        return item;
    }

    private static List<UserMenuItem> Copy(List<UserMenuItem> items) => [.. items.Select(i => i with { Items = Copy(i.Items) })];

    private static List<UserMenuItem> ListAt(List<UserMenuItem> root, IReadOnlyList<int> parent)
    {
        var list = root;
        foreach (var i in parent)
        {
            list = list[i].Items;
        }

        return list;
    }

    private void Structure(Func<List<UserMenuItem>, List<int>?> change)
    {
        var copy = Copy(Items);
        var select = change(copy);
        _settings.Update(s => s with { UserMenu = s.UserMenu with { Items = copy } });
        FillTree();
        if (select is not null && Find(Items, select) is not null)
        {
            Select(select);
        }
        else
        {
            _path = [];
            ShowForm();
        }
    }

    internal void Insert(UserMenuItem item) => Structure(root =>
    {
        if (_path.Count == 0 || Find(root, _path) is not { } chosen)
        {
            root.Add(item);
            return [root.Count - 1];
        }

        if (chosen.Kind == UserMenuItemKind.Submenu)
        {
            chosen.Items.Insert(0, item);
            return [.. _path, 0];
        }

        var parent = _path[..^1];
        ListAt(root, parent).Insert(_path[^1] + 1, item);
        return [.. parent, _path[^1] + 1];
    });

    internal void Move(int step) => Structure(root =>
    {
        if (_path.Count == 0 || Find(root, _path) is not { } item)
        {
            return null;
        }

        var parent = _path[..^1];
        var siblings = ListAt(root, parent);
        var i = _path[^1];
        var neighbour = i + step;
        if (neighbour >= 0 && neighbour < siblings.Count)
        {
            var next = siblings[neighbour];
            siblings.RemoveAt(i);
            if (next.Kind == UserMenuItemKind.Submenu)
            {
                var at = step < 0 ? next.Items.Count : 0;
                next.Items.Insert(at, item);
                return [.. parent, step < 0 ? neighbour : i, at];
            }

            siblings.Insert(neighbour, item);
            return [.. parent, neighbour];
        }

        if (parent.Count == 0)
        {
            return [.. _path];
        }

        siblings.RemoveAt(i);
        var grand = parent[..^1];
        var place = parent[^1] + (step < 0 ? 0 : 1);
        ListAt(root, grand).Insert(place, item);
        return [.. grand, place];
    });

    internal void Remove() => Structure(root =>
    {
        if (_path.Count == 0 || Find(root, _path) is null)
        {
            return null;
        }

        var parent = _path[..^1];
        var siblings = ListAt(root, parent);
        siblings.RemoveAt(_path[^1]);
        return siblings.Count == 0 ? parent.Count > 0 ? parent : null : [.. parent, Math.Min(_path[^1], siblings.Count - 1)];
    });

    private void Change(Func<UserMenuItem, UserMenuItem> change, bool rebuildForm = false)
    {
        if (Chosen is null)
        {
            return;
        }

        var copy = Copy(Items);
        var parent = ListAt(copy, _path[..^1]);
        parent[_path[^1]] = change(parent[_path[^1]]);
        _settings.Update(s => s with { UserMenu = s.UserMenu with { Items = copy } });
        if (_tree.SelectedItem is ListBoxItem selected && Chosen is { } now)
        {
            selected.Content = Row(now, _path);
        }

        if (rebuildForm)
        {
            ShowForm();
        }
        else
        {
            ShowPreview();
        }
    }

    private TextBlock? _previewBlock;
    private TextBlock? _problem;

    private void ShowForm()
    {
        _top.Children.Clear();
        _form.Children.Clear();
        Fields.Clear();
        _previewBlock = null;
        _problem = null;
        ShowIconButton(Chosen);
        if (Chosen is not { } item)
        {
            _top.Children.Add(Secondary(Items.Count == 0 ? AppStrings.Opt_UserMenu_Empty : AppStrings.Opt_UserMenu_ChooseOne, 13));
            return;
        }

        if (item.Kind == UserMenuItemKind.Separator)
        {
            _top.Children.Add(Secondary(AppStrings.Opt_UserMenu_SeparatorNote, 13));
            return;
        }

        if (item.Kind == UserMenuItemKind.Submenu)
        {
            _top.Children.Add(Check(AppStrings.Opt_UserMenu_InBar, AppStrings.Opt_UserMenu_InBarSubmenuNote, item.InBar, on => Change(i => i with { InBar = on }), beside: true));
            _top.Children.Add(Secondary(AppStrings.Opt_UserMenu_SubmenuNote, 12));
            return;
        }

        _top.Children.Add(TopField(AppStrings.Opt_UserMenu_Key, KeyField(item.Key)));
        _top.Children.Add(TopField(AppStrings.Opt_UserMenu_RightClick, Combo(
            [(UserMenuContextPlace.Submenu, AppStrings.Opt_UserMenu_InSubmenu), (UserMenuContextPlace.Top, AppStrings.Opt_UserMenu_AtTop), (UserMenuContextPlace.None, AppStrings.Opt_UserMenu_NotThere)],
            item.InContextMenu, v => Change(i => i with { InContextMenu = v }), 0)));
        _top.Children.Add(Check(AppStrings.Opt_UserMenu_InBar, AppStrings.Opt_UserMenu_InBarNote, item.InBar, on => Change(i => i with { InBar = on }), beside: true));

        _form.Children.Add(Field(AppStrings.Opt_UserMenu_Runs, Segmented(
            [(UserMenuItemKind.Program, AppStrings.Opt_UserMenu_AProgram), (UserMenuItemKind.Script, AppStrings.Opt_UserMenu_AScript)], item.Kind,
            v => Change(i => i with { Kind = v, Runs = v == UserMenuItemKind.Script ? UserMenuRuns.Once : UserMenuRuns.EachFile }, rebuildForm: true))));
        if (item.Kind == UserMenuItemKind.Program)
        {
            _form.Children.Add(Field(AppStrings.Opt_UserMenu_Program, WithButtons(TextField(item.Program, v => Change(i => i with { Program = v })), VariableField.Program,
                (AppStrings.Opt_UserMenu_BrowseProgram, ChooseProgramAsync))));
            _form.Children.Add(Field(AppStrings.UserMenu_Arguments, WithButtons(TextField(item.Arguments, v => Change(i => i with { Arguments = v })), VariableField.Arguments)));
        }
        else
        {
            var script = new TextBox { Text = item.Script, AcceptsReturn = true, AcceptsTab = true, TextWrapping = TextWrapping.NoWrap, MinHeight = 110, MaxHeight = 260 }.Res(TextBox.FontFamilyProperty, "Font.Mono");
            script.LostFocus += (_, _) =>
            {
                if (script.Text != Chosen?.Script)
                {
                    Change(i => i with { Script = script.Text ?? "" });
                }
            };
            _form.Children.Add(Field(AppStrings.Opt_UserMenu_Script, script));
            _form.Children.Add(Secondary(AppStrings.Opt_UserMenu_ScriptNote, 12).Also(t => t.Margin = new Thickness(LabelWidth, 0, 0, 0)));
            _form.Children.Add(Field(AppStrings.Opt_UserMenu_PowerShell, Combo(
                [(UserMenuPowerShell.Pwsh, AppStrings.Opt_UserMenu_Pwsh), (UserMenuPowerShell.Windows, AppStrings.Opt_UserMenu_WindowsPowerShell)], item.PowerShell,
                v => Change(i => i with { PowerShell = v }), 240)));
        }

        _form.Children.Add(Field(AppStrings.Opt_UserMenu_StartIn, WithButtons(TextField(item.StartIn, v => Change(i => i with { StartIn = v.Length == 0 ? UserMenuVariables.DefaultStartIn : v })), VariableField.StartIn,
            (AppStrings.Opt_UserMenu_BrowseFolder, ChooseFolderAsync))));
        _problem = Text("", 12, brush: "Brush.Danger", wrap: true).Also(t => t.Margin = new Thickness(LabelWidth, 0, 0, 0));
        _form.Children.Add(_problem);

        _previewBlock = Text("", 12, brush: "Brush.SecondaryText", wrap: true);
        _form.Children.Add(new Border { Padding = new Thickness(10, 8), CornerRadius = new CornerRadius(4), BorderThickness = new Thickness(1), Child = _previewBlock }
            .Res(Border.BackgroundProperty, "Brush.WindowBackground").Res(Border.BorderBrushProperty, "Brush.Border"));
        ShowPreview();

        _form.Children.Add(Heading(AppStrings.Opt_UserMenu_ShownFor));
        _form.Children.Add(Field(AppStrings.Opt_UserMenu_Items, Segmented(
            [(UserMenuShowsFor.Both, AppStrings.Opt_UserMenu_Both), (UserMenuShowsFor.Files, AppStrings.Opt_UserMenu_Files), (UserMenuShowsFor.Folders, AppStrings.Opt_UserMenu_Folders)],
            item.ShowsFor, v => Change(i => i with { ShowsFor = v }))));
        _form.Children.Add(Field(AppStrings.Opt_UserMenu_Named, TextField(item.Named, v => Change(i => i with { Named = v.Length == 0 ? "*" : v }))));
        _form.Children.Add(Field(AppStrings.Opt_UserMenu_Kind, Combo(
            [.. Enum.GetValues<UserMenuFileKind>().Select(k => (k, Capital(UserMenuFit.KindName(k))))], item.FileKind, v => Change(i => i with { FileKind = v }), 200)));
        _form.Children.Add(Field(AppStrings.Opt_UserMenu_HowMany, Combo(
            [(UserMenuCount.Any, AppStrings.Opt_UserMenu_AnyNumber), (UserMenuCount.One, AppStrings.Opt_UserMenu_ExactlyOne), (UserMenuCount.Two, AppStrings.Opt_UserMenu_ExactlyTwo), (UserMenuCount.AtLeastTwo, AppStrings.Opt_UserMenu_AtLeastTwo)],
            item.Count, v => Change(i => i with { Count = v }), 200)));

        _form.Children.Add(Heading(AppStrings.Opt_UserMenu_HowItRuns));
        _form.Children.Add(Field(AppStrings.Opt_UserMenu_RunsFor, Segmented(
            [(UserMenuRuns.EachFile, AppStrings.Opt_UserMenu_EachFile), (UserMenuRuns.Once, AppStrings.Opt_UserMenu_Once)], item.Runs,
            v => Change(i => i with { Runs = v }, rebuildForm: true))));
        if (item.Runs == UserMenuRuns.EachFile)
        {
            _form.Children.Add(Field(AppStrings.Opt_UserMenu_WhenOneFails, Segmented(
                [(UserMenuOnFailure.GoOn, AppStrings.Opt_UserMenu_GoOn), (UserMenuOnFailure.Stop, AppStrings.Opt_UserMenu_StopThere)], item.OnFailure,
                v => Change(i => i with { OnFailure = v }))));
        }

        _form.Children.Add(Secondary(AppStrings.Opt_UserMenu_RunsNote, 12).Also(t => t.Margin = new Thickness(LabelWidth, 0, 0, 0)));
        _form.Children.Add(Check(AppStrings.Opt_UserMenu_KeepOpen, AppStrings.Opt_UserMenu_KeepOpenNote, item.KeepWindowOpen, on => Change(i => i with { KeepWindowOpen = on })));
        _form.Children.Add(Check(AppStrings.Opt_UserMenu_AskFirst, AppStrings.Opt_UserMenu_AskFirstNote, item.AskFirst, on => Change(i => i with { AskFirst = on })));
    }

    private const double LabelWidth = 120;

    private Control TopField(string label, Control control)
    {
        Fields[label] = control;
        control.HorizontalAlignment = HorizontalAlignment.Stretch;
        return new StackPanel { Spacing = 4, Children = { Text(label, 12, brush: "Brush.SecondaryText"), control } };
    }

    private Control Field(string label, Control control)
    {
        Fields[label] = control;
        var row = new DockPanel();
        var text = Text(label, 13, brush: "Brush.SecondaryText").Also(t => { t.Width = LabelWidth; t.VerticalAlignment = control is TextBox { AcceptsReturn: true } ? VerticalAlignment.Top : VerticalAlignment.Center; t.Margin = new Thickness(0, 6, 0, 0); });
        DockPanel.SetDock(text, Dock.Left);
        row.Children.Add(text);
        control.HorizontalAlignment = control is TextBox or DockPanel ? HorizontalAlignment.Stretch : HorizontalAlignment.Left;
        row.Children.Add(control);
        return row;
    }

    private static TextBlock Heading(string text) => Text(text, 12, FontWeight.SemiBold, "Brush.SecondaryText").Also(t => t.Margin = new Thickness(0, 10, 0, 0));

    private static string Capital(string text) => text.Length == 0 ? text : char.ToUpper(text[0], CultureInfo.CurrentCulture) + text[1..];

    private Control Check(string title, string note, bool value, Action<bool> changed, bool beside = false)
    {
        var box = new CheckBox
        {
            IsChecked = value,
            Margin = new Thickness(beside ? 0 : LabelWidth, 0, 0, 0),
            Content = new StackPanel { Spacing = 1, Children = { Text(title, 13), Text(note, 12, brush: "Brush.TertiaryText", wrap: true) } },
        };
        box.IsCheckedChanged += (_, _) => changed(box.IsChecked == true);
        Fields[title] = box;
        return box;
    }

    private TextBox TextField(string value, Action<string> changed, bool mono = true)
    {
        var box = new TextBox { Text = value, VerticalContentAlignment = VerticalAlignment.Center };
        if (mono)
        {
            box.Res(TextBox.FontFamilyProperty, "Font.Mono");
        }

        void Commit()
        {
            var text = (box.Text ?? "").Trim();
            if (text != value)
            {
                value = text;
                changed(text);
            }
        }

        box.LostFocus += (_, _) => Commit();
        box.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter)
            {
                Commit();
                e.Handled = true;
            }
        };
        return box;
    }

    private enum VariableField { Program, Arguments, StartIn }

    private static List<(string Heading, List<(string Variable, string Name)> Items)> Variables(VariableField field)
    {
        var args = field == VariableField.Arguments;
        var groups = new List<(string, List<(string, string)>)>();
        var file = new List<(string, string)>();
        if (field != VariableField.StartIn)
        {
            file.Add(("$(FullName)", AppStrings.Opt_UserMenu_Var_FullName));
        }

        if (args)
        {
            file.Add(("$(Name)", AppStrings.Opt_UserMenu_Var_Name));
            file.Add(("$(NamePart)", AppStrings.Opt_UserMenu_Var_NamePart));
            file.Add(("$(ExtPart)", AppStrings.Opt_UserMenu_Var_ExtPart));
        }

        if (field != VariableField.Program)
        {
            file.Add(("$(FullPath)", AppStrings.Opt_UserMenu_Var_FullPath));
        }

        if (args)
        {
            file.Add(("$(Drive)", AppStrings.Opt_UserMenu_Var_Drive));
            file.Add(("$(Path)", AppStrings.Opt_UserMenu_Var_Path));
        }

        groups.Add((AppStrings.Opt_UserMenu_VarGroup_File, file));
        if (args)
        {
            groups.Add((AppStrings.Opt_UserMenu_VarGroup_Selected,
            [
                ("$(ListOfSelectedNames)", AppStrings.Opt_UserMenu_Var_ListOfSelectedNames),
                ("$(ListOfSelectedFullNames)", AppStrings.Opt_UserMenu_Var_ListOfSelectedFullNames),
            ]));
        }

        if (field != VariableField.Program)
        {
            groups.Add((AppStrings.Opt_UserMenu_VarGroup_Panels,
            [
                ("$(FullPathLeft)", AppStrings.Opt_UserMenu_Var_FullPathLeft),
                ("$(FullPathRight)", AppStrings.Opt_UserMenu_Var_FullPathRight),
                ("$(FullPathInactive)", AppStrings.Opt_UserMenu_Var_FullPathInactive),
            ]));
        }

        if (args)
        {
            var compare = new List<(string, string)>();
            foreach (var (prefix, what) in new[] { ("FileToCompare", AppStrings.Opt_UserMenu_Var_FileToCompare), ("DirToCompare", AppStrings.Opt_UserMenu_Var_DirToCompare), ("FileOrDirToCompare", AppStrings.Opt_UserMenu_Var_FileOrDirToCompare) })
            {
                foreach (var (side, where) in new[] { ("Left", AppStrings.Opt_UserMenu_Var_Left), ("Right", AppStrings.Opt_UserMenu_Var_Right), ("Active", AppStrings.Opt_UserMenu_Var_Active), ("Inactive", AppStrings.Opt_UserMenu_Var_Inactive) })
                {
                    compare.Add(($"$({prefix}{side})", Loc.Format(AppStrings.Opt_UserMenu_Var_Compare, what, where)));
                }
            }

            groups.Add((AppStrings.Opt_UserMenu_VarGroup_Compare, compare));
        }

        groups.Add((AppStrings.Opt_UserMenu_VarGroup_Windows,
        [
            ("$(WinDir)", AppStrings.Opt_UserMenu_Var_WinDir),
            ("$(SysDir)", AppStrings.Opt_UserMenu_Var_SysDir),
            ("$(SalDir)", AppStrings.Opt_UserMenu_Var_SalDir),
            ("$[]", AppStrings.Opt_UserMenu_Var_Environment),
        ]));
        if (args)
        {
            groups.Add((AppStrings.Opt_UserMenu_VarGroup_Short,
            [
                ("$(DOSFullName)", AppStrings.Opt_UserMenu_Var_FullName),
                ("$(DOSName)", AppStrings.Opt_UserMenu_Var_Name),
                ("$(DOSNamePart)", AppStrings.Opt_UserMenu_Var_NamePart),
                ("$(DOSExtPart)", AppStrings.Opt_UserMenu_Var_ExtPart),
                ("$(DOSFullPath)", AppStrings.Opt_UserMenu_Var_FullPath),
                ("$(DOSPath)", AppStrings.Opt_UserMenu_Var_Path),
                ("$(DOSWinDir)", AppStrings.Opt_UserMenu_Var_WinDir),
                ("$(DOSSysDir)", AppStrings.Opt_UserMenu_Var_SysDir),
            ]));
        }

        return groups;
    }

    internal static IReadOnlyList<string> VariablesOf(string field) => [.. Variables(Enum.Parse<VariableField>(field)).SelectMany(g => g.Items).Select(i => i.Variable)];

    private static Button FieldButton(string glyph, string tip)
    {
        var icon = Salandual.App.Theme.AppIcons.Glyph(glyph, 16, 16, 1.3);
        icon.HorizontalAlignment = HorizontalAlignment.Center;
        var button = new Button { Content = icon, Padding = new Thickness(8, 0), Margin = new Thickness(4, 0, 0, 0), VerticalAlignment = VerticalAlignment.Stretch, VerticalContentAlignment = VerticalAlignment.Center };
        ToolTip.SetTip(button, tip);
        Avalonia.Automation.AutomationProperties.SetName(button, tip);
        return button;
    }

    private Control WithButtons(TextBox box, VariableField field, (string Tip, Func<TextBox, Task> Click)? browse = null)
    {
        var row = new DockPanel();
        var insert = FieldButton("Icon.Variable", AppStrings.Opt_UserMenu_InsertVariable);
        var menu = new MenuFlyout();
        foreach (var (heading, items) in Variables(field))
        {
            if (menu.Items.Count > 0)
            {
                menu.Items.Add(new Separator());
            }

            menu.Items.Add(new MenuItem { Header = heading, IsEnabled = false });
            foreach (var (variable, name) in items)
            {
                var code = Text(variable, 11, brush: "Brush.TertiaryText").Res(TextBlock.FontFamilyProperty, "Font.Mono").Also(c => { c.VerticalAlignment = VerticalAlignment.Center; c.Margin = new Thickness(16, 0, 0, 0); });
                DockPanel.SetDock(code, Dock.Right);
                var item = new MenuItem { Header = new DockPanel { Children = { code, Text(name, 13).Also(n => n.VerticalAlignment = VerticalAlignment.Center) } }, Tag = variable };
                item.Click += (_, _) =>
                {
                    var at = Math.Clamp(box.CaretIndex, 0, (box.Text ?? "").Length);
                    box.Text = (box.Text ?? "").Insert(at, variable);
                    box.CaretIndex = at + variable.Length - (variable == "$[]" ? 1 : 0);
                    box.Focus();
                };
                menu.Items.Add(item);
            }
        }

        insert.Flyout = menu;
        Fields[field + " variables"] = insert;
        DockPanel.SetDock(insert, Dock.Right);
        row.Children.Add(insert);
        if (browse is { } b)
        {
            var button = FieldButton("Icon.Folder", b.Tip);
            button.Click += async (_, _) => await b.Click(box);
            DockPanel.SetDock(button, Dock.Right);
            row.Children.Add(button);
        }

        row.Children.Add(box);
        return row;
    }

    private async Task ChooseProgramAsync(TextBox box)
    {
        if (TopLevel.GetTopLevel(this)?.StorageProvider is not { } storage)
        {
            return;
        }

        var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = AppStrings.Opt_UserMenu_Program,
            AllowMultiple = false,
            FileTypeFilter = [new FilePickerFileType(AppStrings.Opt_ComparePrograms) { Patterns = ["*.exe", "*.com"] }],
        });
        if (files is [var file] && file.TryGetLocalPath() is { } path)
        {
            box.Text = path;
            Change(i => i with { Program = path, Name = i.Name == AppStrings.Opt_UserMenu_NewItemName ? Path.GetFileNameWithoutExtension(path) : i.Name }, rebuildForm: true);
        }
    }

    private async Task ChooseFolderAsync(TextBox box)
    {
        if (TopLevel.GetTopLevel(this)?.StorageProvider is not { } storage)
        {
            return;
        }

        var folders = await storage.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = AppStrings.Opt_UserMenu_StartIn });
        if (folders is [var folder] && folder.TryGetLocalPath() is { } path)
        {
            box.Text = path;
            Change(i => i with { StartIn = path });
        }
    }

    private Control KeyField(string? key)
    {
        var box = new TextBox { Text = key ?? "", PlaceholderText = AppStrings.Opt_UserMenu_PressKey, IsReadOnly = true };
        var note = Text("", 12, brush: "Brush.Danger", wrap: true).Also(t => t.IsVisible = false);
        box.AddHandler(KeyDownEvent, (_, e) =>
        {
            e.Handled = true;
            if (e.Key is Key.Back or Key.Delete && e.KeyModifiers == Avalonia.Input.KeyModifiers.None)
            {
                box.Text = "";
                note.Text = "";
                note.IsVisible = false;
                Change(i => i with { Key = null });
                return;
            }

            if (e.Key is Key.Tab or Key.Escape || !KeyMapping.TryGetChord(e.Key, e.KeyModifiers, out var chord))
            {
                e.Handled = e.Key is not (Key.Tab or Key.Escape);
                return;
            }

            note.Text = WhyNotKey(chord) ?? "";
            note.IsVisible = note.Text.Length > 0;
            if (note.Text.Length == 0)
            {
                box.Text = chord.ToString();
                Change(i => i with { Key = chord.ToString() });
            }
        }, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        Fields[AppStrings.Opt_UserMenu_PressKey] = box;
        return new StackPanel { Spacing = 2, Children = { box, note } };
    }

    internal string? WhyNotKey(KeyChord chord)
    {
        var fKey = chord.Key.Length > 1 && chord.Key[0] == 'F' && char.IsDigit(chord.Key[1]);
        if ((chord.Modifiers & (Core.Commands.KeyModifiers.Ctrl | Core.Commands.KeyModifiers.Alt)) == 0 && !fKey)
        {
            return AppStrings.Opt_UserMenu_KeyNeedsCtrl;
        }

        if (_commands.Resolve(chord) is { } id && _commands.TryGet(id, out var command))
        {
            return Loc.Format(AppStrings.Opt_UserMenu_KeyTaken, chord.ToString(), command.Title);
        }

        var other = _settings.Current.UserMenu.All().FirstOrDefault(i => !ReferenceEquals(i, Chosen) && i.Key is { } k && KeyChord.TryParse(k, out var c) && c == chord);
        return other is null ? null : Loc.Format(AppStrings.Opt_UserMenu_KeyTaken, chord.ToString(), other.PlainName);
    }

    private void ShowIconButton(UserMenuItem? item)
    {
        IconButton.IsEnabled = item is { IsRunnable: true };
        if (item is not { IsRunnable: true })
        {
            IconButton.Flyout = null;
            return;
        }

        var menu = new MenuFlyout();
        var now = _icon?.Invoke(item) is { } image
            ? new Image { Source = image, Width = 16, Height = 16 }
            : (Control)Salandual.App.Theme.AppIcons.Glyph(item.Kind == UserMenuItemKind.Script ? "Icon.Script" : "Navigation.Execute", 16, 16, 1.2);
        menu.Items.Add(new MenuItem { Header = item.Icon is null ? AppStrings.Opt_UserMenu_IconOwn : Path.GetFileName(item.Icon), Icon = now, IsEnabled = false });
        menu.Items.Add(new Separator());
        var choose = new MenuItem { Header = AppStrings.Opt_UserMenu_ChooseIcon };
        choose.Click += async (_, _) =>
        {
            if (TopLevel.GetTopLevel(this)?.StorageProvider is not { } storage)
            {
                return;
            }

            var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = AppStrings.Opt_UserMenu_Icon,
                AllowMultiple = false,
                FileTypeFilter = [new FilePickerFileType(AppStrings.Opt_UserMenu_IconFiles) { Patterns = ["*.ico", "*.exe", "*.dll"] }],
            });
            if (files is [var file] && file.TryGetLocalPath() is { } path)
            {
                Change(i => i with { Icon = path }, rebuildForm: true);
            }
        };
        var own = new MenuItem { Header = AppStrings.Opt_UserMenu_IconReset, IsEnabled = item.Icon is not null };
        own.Click += (_, _) => Change(i => i with { Icon = null }, rebuildForm: true);
        menu.Items.Add(choose);
        menu.Items.Add(own);
        IconButton.Flyout = menu;
    }

    private void ShowPreview()
    {
        if (Chosen is not { IsRunnable: true } item || _previewBlock is null || _problem is null)
        {
            return;
        }

        var check = item.Kind == UserMenuItemKind.Program
            ? UserMenuVariables.Check(item.Program, false) ?? UserMenuVariables.Check(item.Arguments, true) ?? UserMenuVariables.Check(item.StartIn, false)
            : UserMenuVariables.Check(item.StartIn, false);
        _problem.Text = check ?? "";
        _problem.IsVisible = check is not null;

        var lines = new List<string> { AppStrings.Opt_UserMenu_PreviewHead };
        if (_preview() is not { } context)
        {
            lines.Add(AppStrings.Opt_UserMenu_NoPreview);
        }
        else if (UserMenuRunner.Plan(item, context, _starter, out var error) is not { } plan)
        {
            lines.Add(error ?? "");
        }
        else
        {
            var shown = UserMenuRunner.Shown(plan.Steps[0]);
            lines.Add(shown[0]);
            for (var i = 1; i < shown.Count; i++)
            {
                lines.Add(i.ToString(CultureInfo.CurrentCulture) + "   " + shown[i]);
            }

            if (plan.Steps.Count > 1)
            {
                lines.Add(Loc.Format(AppStrings.UserMenu_Ask_Each, plan.Steps.Count));
            }
        }

        lines.Add(AppStrings.Opt_UserMenu_PreviewNote);
        PreviewText = string.Join("\n", lines);
        _previewBlock.Text = PreviewText;
    }
}

// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.App/Dialogs/MakeFileListDialog.cs

using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Styling;
using Avalonia.Threading;
using Salandual.App.Find;
using Salandual.App.Options;
using Salandual.App.Resources;
using Salandual.App.Theme;
using Salandual.Core.Listing;
using Salandual.Core.Resources;
using Salandual.Core.Settings;

namespace Salandual.App.Dialogs;

public sealed record MakeFileListChoice(string Text, int Count, FileListTarget Target, string FilePath, bool Append, FileListSettings Settings);

public sealed class MakeFileListDialog : Window
{
    private const int PreviewItems = 8;
    private const string Mono = "Cascadia Mono,Consolas,monospace";

    private readonly string _folder;
    private readonly IReadOnlyList<FileListItem> _selected;
    private readonly bool _showHidden;
    private readonly FileListContext _context;
    private readonly Action<List<FileListTemplate>> _saveMine;
    private readonly Func<bool> _confirmRemove;
    private readonly FileListSettings _settings;
    private readonly List<FileListTemplate> _mine;
    private readonly CheckBox _include;
    private readonly TextBox _mask;
    private readonly CheckBox _foldersToo;
    private readonly WrapPanel _builtInRow = new() { Orientation = Orientation.Horizontal };
    private readonly WrapPanel _mineRow = new() { Orientation = Orientation.Horizontal };
    private readonly Button _save;
    private readonly DockPanel _nameRow;
    private readonly TextBox _name;
    private readonly Button _nameOk;
    private readonly TextBlock _nameError;
    private readonly TextBox _first;
    private readonly TextBox _line;
    private readonly TextBlock _firstError;
    private readonly TextBlock _lineError;
    private readonly TextBlock _previewNote;
    private readonly SelectableTextBlock _preview;
    private readonly RadioButton _toClipboard;
    private readonly RadioButton _toViewer;
    private readonly RadioButton _toFile;
    private readonly StackPanel _fileRows;
    private readonly TextBox _fileBox;
    private readonly CheckBox _append;
    private readonly TextBlock _problem;
    private IReadOnlyList<FileListItem> _items;
    private IReadOnlyList<FileListItem> _listed;
    private CancellationTokenSource? _scan;
    private int _counted = -1;
    private string? _renaming;

    public MakeFileListDialog(FileListSettings settings, string folder, string what, IReadOnlyList<FileListItem> selected, bool showHidden, DisplayFormat format, Action<List<FileListTemplate>> saveMine, Func<bool> confirmRemove, Func<string, string>? shortPath = null)
    {
        _settings = settings;
        _folder = folder;
        _selected = selected;
        _items = _listed = selected;
        _showHidden = showHidden;
        _context = new FileListContext(format, folder, shortPath);
        _saveMine = saveMine;
        _confirmRemove = confirmRemove;
        _mine = [.. settings.Mine];
        Title = AppStrings.FileList_Title;
        Width = 660;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        this.Res(BackgroundProperty, "Brush.PanelBackground");
        Styles.Add(FindStyles.Create());
        Styles.Add(new Style(x => x.OfType<MenuFlyoutPresenter>().Class("vars")) { Setters = { new Setter(MaxHeightProperty, 420.0), new Setter(MaxWidthProperty, 640.0) } });

        _include = new CheckBox { Content = AppStrings.FileList_Include, IsChecked = settings.IncludeInside && selected.Any(i => i.IsDirectory), IsEnabled = selected.Any(i => i.IsDirectory) };
        _include.IsCheckedChanged += (_, _) => UpdateItems();

        _mask = new TextBox { Text = settings.Mask, PlaceholderText = AppStrings.FileList_NamedHint, MinHeight = 30, VerticalContentAlignment = VerticalAlignment.Center, FontFamily = new FontFamily(Mono), FontSize = 12.5 };
        Avalonia.Automation.AutomationProperties.SetName(_mask, AppStrings.FileList_Named);
        _foldersToo = new CheckBox { Content = AppStrings.FileList_FoldersToo, IsChecked = settings.FoldersToo, Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        _mask.PropertyChanged += (_, e) =>
        {
            if (e.Property == TextBox.TextProperty)
            {
                Refresh();
            }
        };
        _foldersToo.IsCheckedChanged += (_, _) => Refresh();
        var maskLabel = OptionsUi.Text(AppStrings.FileList_Named, 12.5, brush: "Brush.SecondaryText");
        maskLabel.VerticalAlignment = VerticalAlignment.Center;
        maskLabel.Margin = new Thickness(0, 0, 10, 0);
        var maskRow = new DockPanel { Children = { maskLabel, _foldersToo, _mask } };
        DockPanel.SetDock(maskLabel, Dock.Left);
        DockPanel.SetDock(_foldersToo, Dock.Right);
        var maskHelp = OptionsUi.Text(AppStrings.FileList_NamedHelp, 12, brush: "Brush.TertiaryText", wrap: true);

        _save = new Button { Content = AppStrings.FileList_SaveAsYours, FontSize = 12, MinHeight = 24, Padding = new Thickness(10, 2), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 6) };
        _save.Click += (_, _) => OpenNameRow(null);
        _name = new TextBox { PlaceholderText = AppStrings.FileList_NameHint, MinHeight = 28, VerticalContentAlignment = VerticalAlignment.Center };
        Avalonia.Automation.AutomationProperties.SetName(_name, AppStrings.Common_Name);
        _name.TextChanged += (_, _) => _nameError!.IsVisible = false;
        _name.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter)
            {
                SaveName();
                e.Handled = true;
            }
            else if (e.Key == Key.Escape)
            {
                CloseNameRow();
                e.Handled = true;
            }
        };
        _nameOk = new Button { Content = AppStrings.Common_Save, MinWidth = 72, Padding = new Thickness(12, 4), HorizontalContentAlignment = HorizontalAlignment.Center };
        _nameOk.Classes.Add("accent");
        _nameOk.Click += (_, _) => SaveName();
        var nameCancel = new Button { Content = AppStrings.Common_Cancel, MinWidth = 72, Padding = new Thickness(12, 4), HorizontalContentAlignment = HorizontalAlignment.Center };
        nameCancel.Click += (_, _) => CloseNameRow();
        var nameLabel = OptionsUi.Text(AppStrings.Common_Name, 12, brush: "Brush.SecondaryText");
        nameLabel.VerticalAlignment = VerticalAlignment.Center;
        nameLabel.Margin = new Thickness(0, 0, 8, 0);
        var nameButtons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Margin = new Thickness(6, 0, 0, 0), Children = { _nameOk, nameCancel } };
        _nameRow = new DockPanel { IsVisible = false, Children = { nameLabel, nameButtons, _name } };
        DockPanel.SetDock(nameLabel, Dock.Left);
        DockPanel.SetDock(nameButtons, Dock.Right);
        _nameError = OptionsUi.Text("", 12, brush: "Brush.Danger", wrap: true);
        _nameError.IsVisible = false;
        var ready = new StackPanel
        {
            Spacing = 2,
            Children =
            {
                Labelled(AppStrings.FileList_BuiltIn, _builtInRow),
                Labelled(AppStrings.FileList_Yours, _mineRow),
                _nameRow,
                _nameError,
                OptionsUi.Text(AppStrings.FileList_ReadyHelp, 12, brush: "Brush.TertiaryText", wrap: true),
            },
        };

        _first = NewLineBox(settings.First, AppStrings.FileList_First);
        _line = NewLineBox(settings.Line, AppStrings.FileList_Each);
        var insert = new Button { Content = AppStrings.FileList_InsertButton, FontFamily = new FontFamily(Mono), FontSize = 12, Padding = new Thickness(8, 2), Margin = new Thickness(0, 0, 3, 0), VerticalAlignment = VerticalAlignment.Center, Focusable = false };
        insert.Classes.Add("ftool");
        ToolTip.SetTip(insert, AppStrings.FileList_Insert);
        Avalonia.Automation.AutomationProperties.SetName(insert, AppStrings.FileList_Insert);
        insert.Flyout = VariablesMenu();
        InsertButton = insert;
        _line.InnerRightContent = insert;
        _firstError = OptionsUi.Text("", 12, brush: "Brush.Danger", wrap: true);
        _lineError = OptionsUi.Text("", 12, brush: "Brush.Danger", wrap: true);
        foreach (var box in (TextBox[])[_first, _line])
        {
            box.PropertyChanged += (_, e) =>
            {
                if (e.Property == TextBox.TextProperty)
                {
                    Refresh();
                }
            };
        }

        _previewNote = OptionsUi.Text("", 12, brush: "Brush.TertiaryText");
        _preview = new SelectableTextBlock { FontFamily = new FontFamily(Mono), FontSize = 12.5, TextWrapping = TextWrapping.NoWrap };
        var previewBox = new Border
        {
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(10, 8),
            Height = 150,
            Child = new ScrollViewer { HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto, Content = _preview },
        }.Res(Border.BackgroundProperty, "Brush.WindowBackground").Res(Border.BorderBrushProperty, "Brush.Border");

        _toClipboard = To(AppStrings.FileList_Clipboard, settings.Target == FileListTarget.Clipboard);
        _toViewer = To(AppStrings.FileList_Viewer, settings.Target == FileListTarget.Viewer);
        _toFile = To(AppStrings.FileList_File, settings.Target == FileListTarget.File);
        _fileBox = new TextBox { Text = settings.FileName, MinHeight = 30, VerticalContentAlignment = VerticalAlignment.Center };
        Avalonia.Automation.AutomationProperties.SetName(_fileBox, AppStrings.FileList_File);
        _fileBox.TextChanged += (_, _) => _problem!.IsVisible = false;
        var browse = new Button { Content = AppStrings.Common_Browse, Padding = new Thickness(12, 4), Margin = new Thickness(6, 0, 0, 0), VerticalAlignment = VerticalAlignment.Stretch, VerticalContentAlignment = VerticalAlignment.Center };
        browse.Click += async (_, _) => await BrowseAsync();
        var fileRow = new DockPanel { Children = { browse, _fileBox } };
        DockPanel.SetDock(browse, Dock.Right);
        _append = new CheckBox { Content = AppStrings.FileList_Append, IsChecked = settings.Append };
        _fileRows = new StackPanel { Spacing = 4, Children = { fileRow, _append } };
        _toFile.IsCheckedChanged += (_, _) => _fileRows.IsVisible = _toFile.IsChecked == true;
        _fileRows.IsVisible = _toFile.IsChecked == true;
        _problem = OptionsUi.Text("", 12, brush: "Brush.Danger", wrap: true);
        _problem.IsVisible = false;

        var body = new StackPanel { Spacing = 14, Margin = new Thickness(20, 16, 20, 18) };
        body.Children.Add(OptionsUi.Text(what, 12.5, brush: "Brush.SecondaryText", wrap: true));
        body.Children.Add(_include);
        body.Children.Add(new StackPanel { Spacing = 4, Children = { maskRow, maskHelp } });
        body.Children.Add(Section(AppStrings.FileList_ReadyMade, null, ready));
        body.Children.Add(Section(AppStrings.FileList_First, AppStrings.FileList_FirstHint, _first, _firstError));
        body.Children.Add(Section(AppStrings.FileList_Each, null, _line, _lineError, OptionsUi.Text(AppStrings.FileList_WidthHelp, 12, brush: "Brush.TertiaryText", wrap: true)));
        body.Children.Add(Section(AppStrings.FileList_Preview, _previewNote, previewBox));
        body.Children.Add(Section(AppStrings.FileList_To, null, new StackPanel { Orientation = Orientation.Horizontal, Spacing = 18, Children = { _toClipboard, _toViewer, _toFile } }, _fileRows, _problem));

        Make = new Button { Content = WithKey(AppStrings.FileList_Make, "Enter"), MinWidth = 96, Padding = new Thickness(14, 5) };
        Make.Classes.Add("accent");
        Make.Click += async (_, _) => await AcceptAsync();
        var cancel = new Button { Content = WithKey(AppStrings.Common_Cancel, "Esc"), MinWidth = 96, Padding = new Thickness(14, 5) };
        cancel.Click += (_, _) => Close(null);
        var footer = new Border
        {
            Padding = new Thickness(20, 12),
            BorderThickness = new Thickness(0, 1, 0, 0),
            Child = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right, Children = { cancel, Make } },
        }.Res(Border.BackgroundProperty, "Brush.WindowBackground").Res(Border.BorderBrushProperty, "Brush.Border");

        var root = new DockPanel();
        DockPanel.SetDock(footer, Dock.Bottom);
        root.Children.Add(footer);
        root.Children.Add(body);
        Content = Controls.TitleBarParts.ForDialog(this, root);
        Opened += (_, _) =>
        {
            _line.Focus();
            _line.CaretIndex = _line.Text?.Length ?? 0;
        };
        Closed += (_, _) => _scan?.Cancel();
        UpdateItems();
    }

    internal Button Make { get; }

    internal Button InsertButton { get; }

    internal CheckBox IncludeBox => _include;

    internal TextBox MaskBox => _mask;

    internal CheckBox FoldersTooBox => _foldersToo;

    internal TextBox FirstBox => _first;

    internal TextBox LineBox => _line;

    internal TextBox NameBox => _name;

    internal Button SaveButton => _save;

    internal TextBox FileBox => _fileBox;

    internal CheckBox AppendBox => _append;

    internal RadioButton ToClipboard => _toClipboard;

    internal RadioButton ToViewer => _toViewer;

    internal RadioButton ToFile => _toFile;

    internal string PreviewText => _preview.Inlines is { } inlines ? string.Concat(inlines.OfType<Run>().Select(r => r.Text)) : _preview.Text ?? "";

    internal string PreviewNote => _previewNote.Text ?? "";

    internal string LineError => _lineError.IsVisible ? _lineError.Text ?? "" : "";

    internal string FirstError => _firstError.IsVisible ? _firstError.Text ?? "" : "";

    internal string NameError => _nameError.IsVisible ? _nameError.Text ?? "" : "";

    internal string Problem => _problem.IsVisible ? _problem.Text ?? "" : "";

    internal IReadOnlyList<(string Name, bool Lit, bool Mine)> Chips =>
        [.. _builtInRow.Children.OfType<Button>().Select(b => ((string)b.Tag!, b.Classes.Contains("on"), false)),
         .. _mineRow.Children.OfType<Button>().Where(b => b.Tag is string).Select(b => ((string)b.Tag!, b.Classes.Contains("on"), true))];

    internal bool IsCounting => _counted >= 0;

    internal IReadOnlyList<FileListItem> Items => _items;

    internal IReadOnlyList<FileListItem> Listed => _listed;

    internal void Pick(string name)
    {
        if (FileListTemplate.BuiltIn.Concat(_mine).FirstOrDefault(t => t.Name == name) is { } t)
        {
            _first.Text = t.First;
            _line.Text = t.Line;
            CloseNameRow();
        }
    }

    internal void OpenNameRow(string? rename)
    {
        _renaming = rename;
        _name.Text = rename ?? Matching()?.Name ?? "";
        _nameOk.Content = rename is null ? AppStrings.Common_Save : AppStrings.FileList_Rename;
        _nameRow.IsVisible = true;
        _nameError.IsVisible = false;
        _name.Focus();
        _name.SelectAll();
    }

    private void CloseNameRow()
    {
        _nameRow.IsVisible = false;
        _nameError.IsVisible = false;
        _renaming = null;
    }

    internal void SaveName()
    {
        var name = (_name.Text ?? "").Trim();
        string? error = name.Length == 0 ? AppStrings.FileList_NameEmpty
            : FileListTemplate.BuiltIn.Any(b => string.Equals(b.Name, name, StringComparison.CurrentCultureIgnoreCase)) ? Loc.Format(AppStrings.FileList_NameBuiltIn, name)
            : _renaming is not null && !string.Equals(_renaming, name, StringComparison.CurrentCultureIgnoreCase) && _mine.Any(m => string.Equals(m.Name, name, StringComparison.CurrentCultureIgnoreCase)) ? Loc.Format(AppStrings.FileList_NameUsed, name)
            : _renaming is null && _mine.Count >= FileListSettings.MaxMine && !_mine.Any(m => string.Equals(m.Name, name, StringComparison.CurrentCultureIgnoreCase)) ? Loc.Format(AppStrings.FileList_TooMany, FileListSettings.MaxMine)
            : null;
        if (error is not null)
        {
            _nameError.Text = error;
            _nameError.IsVisible = true;
            return;
        }

        if (_renaming is { } old)
        {
            var at = _mine.FindIndex(m => m.Name == old);
            if (at >= 0)
            {
                _mine[at] = _mine[at] with { Name = name };
            }
        }
        else
        {
            var template = new FileListTemplate(name, _first.Text ?? "", _line.Text ?? "");
            var at = _mine.FindIndex(m => string.Equals(m.Name, name, StringComparison.CurrentCultureIgnoreCase));
            if (at >= 0)
            {
                _mine[at] = template;
            }
            else
            {
                _mine.Add(template);
            }
        }

        _saveMine([.. _mine]);
        CloseNameRow();
        Refresh();
    }

    internal async Task RemoveMineAsync(string name)
    {
        if (_confirmRemove())
        {
            var ask = RemoveQuestion = new ConfirmDialog(AppStrings.FileList_RemoveTitle, Loc.Format(AppStrings.FileList_RemoveText, name), AppStrings.Common_Remove, AppStrings.Common_Cancel);
            var yes = await ask.ShowDialog<bool>(this);
            RemoveQuestion = null;
            if (!yes)
            {
                return;
            }
        }

        if (_mine.RemoveAll(m => m.Name == name) > 0)
        {
            _saveMine([.. _mine]);
            CloseNameRow();
            Refresh();
        }
    }

    private FileListTemplate? Matching()
    {
        var (first, line) = (_first.Text ?? "", _line.Text ?? "");
        return FileListTemplate.BuiltIn.FirstOrDefault(t => t.SameLines(first, line)) ?? _mine.FirstOrDefault(t => t.SameLines(first, line));
    }

    private void BuildChips(FileListTemplate? lit)
    {
        _builtInRow.Children.Clear();
        foreach (var t in FileListTemplate.BuiltIn)
        {
            var chip = Chip(t, lit);
            ToolTip.SetTip(chip, AppStrings.FileList_BuiltInTip);
            _builtInRow.Children.Add(chip);
        }

        _mineRow.Children.Clear();
        if (_mine.Count == 0)
        {
            var none = OptionsUi.Text(AppStrings.FileList_NoneYet, 12, brush: "Brush.TertiaryText");
            none.VerticalAlignment = VerticalAlignment.Center;
            none.Margin = new Thickness(0, 0, 10, 6);
            _mineRow.Children.Add(none);
        }

        foreach (var t in _mine)
        {
            var chip = Chip(t, lit);
            var rename = Small("Icon.Rename", AppStrings.FileList_RenameTip);
            rename.Click += (_, e) =>
            {
                e.Handled = true;
                OpenNameRow(t.Name);
            };
            var remove = Small("Icon.Cross", AppStrings.FileList_RemoveTip);
            remove.Click += (_, e) =>
            {
                e.Handled = true;
                _ = RemoveMineAsync(t.Name);
            };
            chip.Content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2, Children = { new TextBlock { Text = t.Name, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 4, 0) }, rename, remove } };
            chip.Padding = new Thickness(10, 0, 3, 0);
            _mineRow.Children.Add(chip);
        }

        var mineLit = lit is not null && _mine.Contains(lit);
        _save.Content = mineLit ? AppStrings.FileList_Saved : AppStrings.FileList_SaveAsYours;
        _save.IsEnabled = lit is null;
        _mineRow.Children.Add(_save);
    }

    private Button Chip(FileListTemplate t, FileListTemplate? lit)
    {
        var chip = new Button { Content = t.Name, Tag = t.Name, Margin = new Thickness(0, 0, 6, 6), VerticalAlignment = VerticalAlignment.Center };
        chip.Classes.Add("chip");
        if (ReferenceEquals(t, lit))
        {
            chip.Classes.Add("on");
        }

        chip.Click += (_, _) => Pick(t.Name);
        return chip;
    }

    private static Button Small(string icon, string tip)
    {
        var button = new Button { Content = AppIcons.Glyph(icon, 11, 11, 1.4), Padding = new Thickness(4), MinHeight = 18, VerticalAlignment = VerticalAlignment.Center, Focusable = false };
        button.Classes.Add("ftool");
        ToolTip.SetTip(button, tip);
        Avalonia.Automation.AutomationProperties.SetName(button, tip);
        return button;
    }

    private MenuFlyout VariablesMenu()
    {
        var flyout = new MenuFlyout { Placement = Avalonia.Controls.PlacementMode.BottomEdgeAlignedRight };
        flyout.FlyoutPresenterClasses.Add("vars");
        string[] headings = [AppStrings.FileList_Group_Name, AppStrings.FileList_Group_Folder, AppStrings.FileList_Group_Size, AppStrings.FileList_Group_Date, AppStrings.FileList_Group_Other, AppStrings.FileList_Group_Breaks];
        for (var g = 0; g < FileListLine.Groups.Count; g++)
        {
            if (g > 0)
            {
                flyout.Items.Add(new Separator());
            }

            flyout.Items.Add(new MenuItem { Header = headings[g], IsEnabled = false });
            foreach (var name in FileListLine.Groups[g])
            {
                flyout.Items.Add(VariableItem("$(" + name + ")", Describe(name), "$(" + name + ")", 0));
            }
        }

        flyout.Items.Add(new Separator());
        flyout.Items.Add(new MenuItem { Header = AppStrings.FileList_Group_Environment, IsEnabled = false });
        flyout.Items.Add(VariableItem("$[ ]", AppStrings.FileList_Var_Environment, "$[]", 1));
        flyout.Items.Add(VariableItem("$[USERNAME]", AppStrings.FileList_Var_UserName, "$[USERNAME]", 0));
        flyout.Items.Add(VariableItem("$[COMPUTERNAME]", AppStrings.FileList_Var_ComputerName, "$[COMPUTERNAME]", 0));
        return flyout;
    }

    private static string Describe(string variable) => variable switch
    {
        "FileName" => AppStrings.FileList_Var_FileName,
        "FileNamePart" => AppStrings.FileList_Var_FileNamePart,
        "FileExtension" => AppStrings.FileList_Var_FileExtension,
        "FileDOSName" => AppStrings.FileList_Var_FileDOSName,
        "RelativeName" => AppStrings.FileList_Var_RelativeName,
        "FileSize" => AppStrings.FileList_Var_FileSize,
        "FileSizeShort" => AppStrings.FileList_Var_FileSizeShort,
        "FileDate" => AppStrings.FileList_Var_FileDate,
        "FileTime" => AppStrings.FileList_Var_FileTime,
        "FileDateTime" => AppStrings.FileList_Var_FileDateTime,
        "Created" => AppStrings.FileList_Var_Created,
        "FileAttributes" => AppStrings.FileList_Var_FileAttributes,
        "Type" => AppStrings.FileList_Var_Type,
        "Number" => AppStrings.FileList_Var_Number,
        "Count" => AppStrings.FileList_Var_Count,
        "FullName" => AppStrings.FileList_Var_FullName,
        "FullPath" => AppStrings.FileList_Var_FullPath,
        "FolderName" => AppStrings.FileList_Var_FolderName,
        "Drive" => AppStrings.FileList_Var_Drive,
        "Path" => AppStrings.FileList_Var_Path,
        "DOSPath" => AppStrings.FileList_Var_DOSPath,
        "CRLF" => AppStrings.FileList_Var_CRLF,
        "LF" => AppStrings.FileList_Var_LF,
        "CR" => AppStrings.FileList_Var_CR,
        "TAB" => AppStrings.FileList_Var_TAB,
        _ => variable,
    };

    private MenuItem VariableItem(string shown, string description, string text, int back)
    {
        var pattern = new TextBlock { Text = shown, FontFamily = new FontFamily(Mono), FontSize = 12, Width = 150, VerticalAlignment = VerticalAlignment.Center };
        pattern.Res(TextBlock.ForegroundProperty, "Brush.Accent");
        var header = new StackPanel { Orientation = Orientation.Horizontal, Children = { pattern, new TextBlock { Text = description, VerticalAlignment = VerticalAlignment.Center } } };
        var item = new MenuItem { Header = header, Tag = text };
        item.Click += (_, _) => Insert(text, back);
        return item;
    }

    internal void Insert(string text, int back = 0)
    {
        var line = _line.Text ?? "";
        var (start, end) = (Math.Min(_line.SelectionStart, _line.SelectionEnd), Math.Max(_line.SelectionStart, _line.SelectionEnd));
        if (start == end)
        {
            start = end = Math.Clamp(_line.CaretIndex, 0, line.Length);
        }

        _line.Text = line[..start] + text + line[end..];
        _line.Focus();
        _line.SelectionStart = _line.SelectionEnd = _line.CaretIndex = start + text.Length - back;
    }

    private void UpdateItems()
    {
        _scan?.Cancel();
        _scan = null;
        if (_include.IsChecked != true)
        {
            _items = _selected;
            _counted = -1;
            Refresh();
            return;
        }

        var scan = _scan = new CancellationTokenSource();
        _counted = _selected.Count;
        Refresh();
        _ = Task.Run(() => FileListScan.Inside(_selected, _showHidden, n => Dispatcher.UIThread.Post(() =>
        {
            if (_scan == scan)
            {
                _counted = n;
                ShowPreviewNote();
            }
        }), scan.Token), scan.Token).ContinueWith(t => Dispatcher.UIThread.Post(() =>
        {
            if (_scan == scan && t.IsCompletedSuccessfully)
            {
                _items = t.Result;
                _counted = -1;
                Refresh();
            }
        }), TaskScheduler.Default);
    }

    private void Refresh()
    {
        BuildChips(Matching());
        _foldersToo.IsEnabled = Mask() is not null;
        _listed = FileListScan.Named(_items, Mask(), _foldersToo.IsChecked == true);
        var first = ParseFirst(out var firstError);
        var line = Parse(_line.Text, out var lineError);
        _firstError.Text = firstError ?? "";
        _firstError.IsVisible = firstError is not null;
        _lineError.Text = lineError ?? "";
        _lineError.IsVisible = lineError is not null;
        Make.IsEnabled = first is not null && line is not null && !IsCounting && _listed.Count > 0;
        ShowPreviewNote();
        if (first is null || line is null)
        {
            ShowPreview("");
            return;
        }

        var shown = _listed.Take(PreviewItems).ToList();
        ShowPreview(Head(first, _listed.Count) + line.Expand(shown, _context, widthsOf: IsCounting ? shown : _listed));
    }

    private FileMask? Mask() => FileMask.Parse(_mask.Text);

    private void ShowPreviewNote() => _previewNote.Text = IsCounting ? Loc.Format(AppStrings.FileList_Counting, _counted)
        : _listed.Count == 0 ? AppStrings.FileList_NamedNone
        : _listed.Count > PreviewItems ? Loc.Format(AppStrings.FileList_PreviewFirst, PreviewItems, _listed.Count)
        : Loc.Format(AppStrings.FileList_PreviewAll, _listed.Count);

    private void ShowPreview(string text)
    {
        _preview.Inlines ??= [];
        _preview.Inlines.Clear();
        var column = 0;
        var run = new StringBuilder();
        foreach (var c in text.Replace("\r\n", "\n").Replace('\r', '\n'))
        {
            if (c == '\t')
            {
                Flush();
                var width = 8 - (column % 8);
                var arrow = new Run("→" + new string(' ', width - 1));
                arrow.Bind(TextElement.ForegroundProperty, _preview.GetResourceObservable("Brush.TertiaryText"));
                _preview.Inlines.Add(arrow);
                column += width;
                continue;
            }

            run.Append(c);
            column = c == '\n' ? 0 : column + 1;
        }

        Flush();

        void Flush()
        {
            if (run.Length > 0)
            {
                _preview.Inlines.Add(new Run(run.ToString()));
                run.Clear();
            }
        }
    }

    private static FileListLine? Parse(string? text, out string? error)
    {
        var line = FileListLine.Parse(text ?? "", out error, out _);
        return line;
    }

    private FileListLine? ParseFirst(out string? error)
    {
        var first = Parse(_first.Text, out error);
        if (first is { NamesAFile: true })
        {
            error = AppStrings.FileList_FirstNoFile;
            return null;
        }

        return first;
    }

    private static string Head(FileListLine first, int count)
    {
        var head = first.ExpandOnce(count);
        return head.Length > 0 && !first.EndsWithBreak ? head + "\r\n" : head;
    }

    private async Task BrowseAsync()
    {
        var path = FilePath();
        var start = Directory.Exists(Path.GetDirectoryName(path)) ? await StorageProvider.TryGetFolderFromPathAsync(Path.GetDirectoryName(path)!) : null;
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions { Title = AppStrings.FileList_ChooseFile, SuggestedStartLocation = start, SuggestedFileName = Path.GetFileName(path), ShowOverwritePrompt = false });
        if (file is not null && file.TryGetLocalPath() is { } chosen)
        {
            _fileBox.Text = chosen;
            _toFile.IsChecked = true;
        }
    }

    private string FilePath()
    {
        var text = (_fileBox.Text ?? "").Trim().Trim('"');
        return text.Length == 0 ? "" : Path.IsPathFullyQualified(text) ? text : Path.GetFullPath(Path.Combine(_folder, text));
    }

    private FileListTarget Target => _toFile.IsChecked == true ? FileListTarget.File : _toViewer.IsChecked == true ? FileListTarget.Viewer : FileListTarget.Clipboard;

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
            _ = AcceptAsync();
            e.Handled = true;
        }
    }

    internal async Task AcceptAsync()
    {
        var first = ParseFirst(out _);
        var line = Parse(_line.Text, out _);
        if (first is null || line is null || IsCounting || _listed.Count == 0)
        {
            return;
        }

        var path = Target == FileListTarget.File ? FilePath() : "";
        if (Target == FileListTarget.File)
        {
            var folder = path.Length == 0 ? null : Path.GetDirectoryName(path);
            string? problem = path.Length == 0 ? AppStrings.FileList_NoFileName
                : folder is null || !Directory.Exists(folder) ? Loc.Format(AppStrings.FileList_NoFolder, folder ?? path)
                : Directory.Exists(path) ? Loc.Format(AppStrings.FileList_IsAFolder, path)
                : null;
            if (problem is not null)
            {
                _problem.Text = problem;
                _problem.IsVisible = true;
                return;
            }

            if (_append.IsChecked != true && File.Exists(path))
            {
                var ask = ReplaceQuestion = new ConfirmDialog(AppStrings.FileList_ReplaceTitle, Loc.Format(AppStrings.FileList_ReplaceText, path), AppStrings.FileList_Replace, AppStrings.Common_Cancel);
                var yes = await ask.ShowDialog<bool>(this);
                ReplaceQuestion = null;
                if (!yes)
                {
                    return;
                }
            }
        }

        var text = Head(first, _listed.Count) + line.Expand(_listed, _context);
        var settings = _settings with
        {
            Mine = [.. _mine],
            First = _first.Text ?? "",
            Line = _line.Text ?? "",
            Target = Target,
            FileName = (_fileBox.Text ?? "").Trim().Length > 0 ? (_fileBox.Text ?? "").Trim() : _settings.FileName,
            Append = _append.IsChecked == true,
            IncludeInside = _include.IsEnabled ? _include.IsChecked == true : _settings.IncludeInside,
            Mask = (_mask.Text ?? "").Trim(),
            FoldersToo = _foldersToo.IsChecked == true,
        };
        Close(new MakeFileListChoice(text, _listed.Count, Target, path, _append.IsChecked == true, settings));
    }

    internal ConfirmDialog? RemoveQuestion { get; private set; }

    internal ConfirmDialog? ReplaceQuestion { get; private set; }

    private static TextBox NewLineBox(string text, string name)
    {
        var box = new TextBox { Text = text, MinHeight = 30, VerticalContentAlignment = VerticalAlignment.Center, FontFamily = new FontFamily(Mono), FontSize = 12.5 };
        Avalonia.Automation.AutomationProperties.SetName(box, name);
        return box;
    }

    private static RadioButton To(string text, bool on) => new() { Content = text, GroupName = "to", IsChecked = on };

    private static DockPanel Labelled(string label, Control row)
    {
        var text = OptionsUi.Text(label, 12, brush: "Brush.SecondaryText");
        text.Width = 64;
        text.VerticalAlignment = VerticalAlignment.Top;
        text.Margin = new Thickness(0, 4, 0, 0);
        var panel = new DockPanel { Children = { text, row } };
        DockPanel.SetDock(text, Dock.Left);
        return panel;
    }

    private static StackPanel Section(string heading, object? note, params Control[] rows)
    {
        var title = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Children = { OptionsUi.Text(heading, 13, FontWeight.SemiBold) } };
        if (note is string hint)
        {
            var h = OptionsUi.Text(hint, 12, brush: "Brush.TertiaryText");
            h.VerticalAlignment = VerticalAlignment.Center;
            title.Children.Add(h);
        }
        else if (note is TextBlock block)
        {
            block.VerticalAlignment = VerticalAlignment.Center;
            title.Children.Add(block);
        }

        var section = new StackPanel { Spacing = 4, Children = { title } };
        section.Children.AddRange(rows);
        return section;
    }

    private static StackPanel WithKey(string label, string key) => new()
    {
        Orientation = Orientation.Horizontal,
        Spacing = 6,
        Children = { new TextBlock { Text = label }, new TextBlock { Text = key, FontSize = 11, Opacity = 0.65, VerticalAlignment = VerticalAlignment.Center } },
    };
}

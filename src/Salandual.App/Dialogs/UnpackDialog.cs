// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.App/Dialogs/UnpackDialog.cs

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Salandual.App.Controls;
using Salandual.App.Options;
using Salandual.App.Resources;
using Salandual.Archives;
using Salandual.Core.Listing;
using Salandual.Core.Operations;
using Salandual.Core.Resources;
using Salandual.Core.Settings;
using Salandual.Platform.Windows.FileSystem;

namespace Salandual.App.Dialogs;

internal sealed record UnpackContext(bool CopyMode, IReadOnlyList<UnpackSource> Sources, string Heading, string Facts, string Target, OperationSettings Settings, bool Move = false);

internal sealed record UnpackRequest(string Target, UnpackOptions Options, bool ToQueue, bool DeleteArchive);

internal sealed class UnpackDialog : Window
{
    private readonly UnpackContext _context;
    private readonly IOperationFileSystem _fs;
    private readonly DisplayFormat _format;
    private readonly Action<OperationSettings> _saveDefaults;
    private readonly TextBox _to;
    private readonly TextBlock _where = OptionsUi.Text("", 12, brush: "Brush.SecondaryText", wrap: true);
    private readonly Border _whereBox;
    private readonly TextBlock _check = OptionsUi.Text("", 12, brush: "Brush.SecondaryText", wrap: true);
    private readonly Ellipse _checkDot = new() { Width = 8, Height = 8, Margin = new Thickness(0, 5, 0, 0), VerticalAlignment = VerticalAlignment.Top };
    private readonly ComboBox _ifExists;
    private readonly CheckBox _times, _attrs, _keepFolders, _delete;
    private readonly RadioButton _whenNeeded, _always, _never;
    private readonly TextBox _mask;
    private readonly StackPanel _advanced;
    private readonly TextBlock _advancedHead = new() { FontSize = 13 };
    private readonly TextBlock _summary = OptionsUi.Text("", 12, brush: "Brush.SecondaryText", wrap: true);
    private readonly Button _go, _queue;

    public UnpackDialog(UnpackContext context, IOperationFileSystem fs, DisplayFormat format, Action<OperationSettings> saveDefaults)
    {
        _context = context;
        _fs = fs;
        _format = format;
        _saveDefaults = saveDefaults;
        var verb = context.CopyMode ? context.Move ? AppStrings.Copy_Move : AppStrings.Copy_Copy : AppStrings.Unpack_Unpack;
        Title = context.Heading;
        Width = 580;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        this.Res(BackgroundProperty, "Brush.PanelBackground");
        var s = context.Settings;

        var body = new StackPanel { Spacing = 10, Margin = new Thickness(20, 8, 20, 16) };
        body.Children.Add(OptionsUi.Text(context.Move ? AppStrings.UnpackMove_Note : context.CopyMode ? AppStrings.UnpackCopy_Note : context.Facts, 13, brush: "Brush.SecondaryText", wrap: true));

        _to = new TextBox { MinHeight = 32, VerticalContentAlignment = VerticalAlignment.Center, Text = context.Target.Length > 0 ? WindowsPath.Join(context.Target, "") : "" };
        _to.Res(TextBox.FontFamilyProperty, "Font.Mono");
        Avalonia.Automation.AutomationProperties.SetName(_to, AppStrings.Copy_To);
        _to.TextChanged += (_, _) => Update();
        var browse = new Button { Content = AppStrings.Common_Browse, MinHeight = 32, Padding = new Thickness(12, 0), VerticalContentAlignment = VerticalAlignment.Center, Focusable = false };
        browse.Click += async (_, _) => await BrowseAsync();
        var toRow = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 6 };
        toRow.Children.Add(_to);
        Grid.SetColumn(browse, 1);
        toRow.Children.Add(browse);
        var toField = new StackPanel { Spacing = 5, Children = { OptionsUi.Text(AppStrings.Copy_To, 13, FontWeight.SemiBold), toRow } };
        if (context.CopyMode)
        {
            toField.Children.Add(OptionsUi.Secondary(AppStrings.Unpack_ToHint));
        }

        body.Children.Add(toField);
        _whereBox = new Border { Padding = new Thickness(10, 7), CornerRadius = new CornerRadius(4), BorderThickness = new Thickness(1), Child = _where, IsVisible = !context.CopyMode }
            .Res(Border.BackgroundProperty, "Brush.WindowBackground").Res(Border.BorderBrushProperty, "Brush.Border");
        body.Children.Add(_whereBox);

        _ifExists = new ComboBox { MinWidth = 260 };
        foreach (var label in new[] { AppStrings.Copy_IfExists_Ask, AppStrings.Copy_IfExists_OverwriteOlder, AppStrings.Copy_IfExists_OverwriteAll, AppStrings.Copy_IfExists_SkipAll, AppStrings.Copy_IfExists_KeepBoth })
        {
            _ifExists.Items.Add(label);
        }

        _ifExists.SelectedIndex = s.IfExists switch
        {
            ExistingFilePolicy.OverwriteOlder => 1,
            ExistingFilePolicy.Overwrite => 2,
            ExistingFilePolicy.Skip => 3,
            ExistingFilePolicy.KeepBoth => 4,
            _ => 0,
        };
        body.Children.Add(LabelRow(AppStrings.Copy_IfExists, _ifExists));
        if (!context.CopyMode)
        {
            body.Children.Add(LabelRow(AppStrings.Unpack_Password, OptionsUi.Text(AppStrings.Unpack_PasswordNote, 12, brush: "Brush.SecondaryText", wrap: true)));
        }

        _times = Check(AppStrings.UnpackCopy_KeepTimes, s.KeepFolderTimes);
        _attrs = Check(AppStrings.Copy_KeepAttributes, s.KeepAttributes);
        _whenNeeded = Radio(AppStrings.Unpack_Folder_WhenNeeded, AppStrings.Unpack_Folder_WhenNeededNote, s.UnpackFolder == UnpackFolderRule.WhenNeeded);
        _always = Radio(AppStrings.Unpack_Folder_Always, null, s.UnpackFolder == UnpackFolderRule.Always);
        _never = Radio(AppStrings.Unpack_Folder_Never, null, s.UnpackFolder == UnpackFolderRule.Never);
        _mask = new TextBox { Width = 170, MinHeight = 28, Padding = new Thickness(6, 3), FontSize = 12, PlaceholderText = AppStrings.Unpack_AllFiles };
        _mask.Res(TextBox.FontFamilyProperty, "Font.Mono");
        Avalonia.Automation.AutomationProperties.SetName(_mask, AppStrings.Unpack_Files);
        _mask.TextChanged += (_, _) => Update();
        _keepFolders = Check(AppStrings.Unpack_KeepFolders, s.UnpackKeepFolders, AppStrings.Unpack_KeepFoldersNote);
        _delete = Check(AppStrings.Unpack_DeleteArchive, false, AppStrings.Unpack_DeleteArchiveNote);
        foreach (var box in new[] { _times, _attrs, _keepFolders, _delete })
        {
            box.IsCheckedChanged += (_, _) => Update();
        }

        _advanced = new StackPanel { Spacing = 6, Margin = new Thickness(18, 4, 0, 0), IsVisible = false };
        if (context.CopyMode)
        {
            _advanced.Children.Add(Head(AppStrings.Copy_Keep));
            _advanced.Children.Add(_times);
            _advanced.Children.Add(_attrs);
        }
        else
        {
            _advanced.Children.Add(Head(AppStrings.Unpack_FolderHead));
            _advanced.Children.Add(_whenNeeded);
            _advanced.Children.Add(_always);
            _advanced.Children.Add(_never);
            _advanced.Children.Add(Head(AppStrings.Unpack_WhatHead).Also(h => h.Margin = new Thickness(0, 8, 0, 0)));
            _advanced.Children.Add(new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 10,
                Children = { OptionsUi.Text(AppStrings.Unpack_Files, 13).Also(t => { t.Width = 140; t.VerticalAlignment = VerticalAlignment.Center; }), _mask, OptionsUi.Text(AppStrings.Unpack_MaskHint, 12, brush: "Brush.TertiaryText").Also(t => t.VerticalAlignment = VerticalAlignment.Center) },
            });
            _advanced.Children.Add(_keepFolders);
            _advanced.Children.Add(Head(AppStrings.Unpack_AfterwardsHead).Also(h => h.Margin = new Thickness(0, 8, 0, 0)));
            _advanced.Children.Add(_delete);
        }

        var disclose = new Button { Padding = new Thickness(0, 2), Background = Brushes.Transparent, BorderThickness = new Thickness(0), HorizontalAlignment = HorizontalAlignment.Left, Content = _advancedHead };
        disclose.Click += (_, _) => ToggleAdvanced();
        _summary.Margin = new Thickness(18, 0, 0, 0);
        _summary.Cursor = new Cursor(StandardCursorType.Hand);
        _summary.PointerPressed += (_, e) =>
        {
            ToggleAdvanced();
            e.Handled = true;
        };
        _advancedHead.Res(TextBlock.ForegroundProperty, "Brush.Text");
        AdvancedButton = disclose;
        body.Children.Add(new Border { Height = 1, Margin = new Thickness(0, 4, 0, 0) }.Res(Border.BackgroundProperty, "Brush.Border"));
        body.Children.Add(new StackPanel { Spacing = 2, Children = { disclose, _summary, _advanced } });

        body.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { _checkDot, _check } }.Also(p => _check.MaxWidth = 500));

        var save = Operations.Ui.Link(AppStrings.Copy_SaveDefaults, () =>
        {
            _saveDefaults(CurrentSettings());
            SavedDefaults = true;
        });
        var cancel = new Button { Content = AppStrings.Common_Cancel, MinWidth = 84, Padding = new Thickness(14, 5), HorizontalContentAlignment = HorizontalAlignment.Center };
        cancel.Click += (_, _) => Close(null);
        var queueFirst = s.Enter == EnterAction.AddToQueue;
        _queue = new Button { Content = queueFirst ? AppStrings.Copy_StartNowShift : AppStrings.Copy_AddToQueueShift, Padding = new Thickness(14, 5) };
        _queue.Click += (_, _) => Accept(toQueue: !queueFirst);
        _go = new Button { Content = queueFirst ? AppStrings.Copy_AddToQueueEnter : Loc.Format(AppStrings.Copy_GoEnter, verb), Padding = new Thickness(14, 5), MinWidth = 96 };
        _go.Classes.Add("accent");
        _go.Click += (_, _) => Accept(toQueue: queueFirst);
        var right = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { cancel, _queue, _go } };
        var footerRow = new DockPanel();
        DockPanel.SetDock(right, Dock.Right);
        footerRow.Children.Add(right);
        footerRow.Children.Add(save);
        var footer = new Border { Padding = new Thickness(20, 12), BorderThickness = new Thickness(0, 1, 0, 0), Child = footerRow }
            .Res(Border.BackgroundProperty, "Brush.WindowBackground").Res(Border.BorderBrushProperty, "Brush.Border");

        var root = new DockPanel();
        DockPanel.SetDock(footer, Dock.Bottom);
        root.Children.Add(footer);
        root.Children.Add(body);
        Content = TitleBarParts.ForDialog(this, root);
        Update();
        Opened += (_, _) =>
        {
            _to.Focus();
            _to.CaretIndex = _to.Text?.Length ?? 0;
        };
    }

    internal TextBox To => _to;

    internal Button Go => _go;

    internal Button AdvancedButton { get; }

    internal bool AdvancedOpen => _advanced.IsVisible;

    internal string Summary => _summary.Text ?? "";

    internal string WhereText => _where.Text ?? "";

    internal string CheckText => _check.Text ?? "";

    internal CheckBox DeleteBox => _delete;

    internal CheckBox KeepFoldersBox => _keepFolders;

    internal RadioButton AlwaysOption => _always;

    internal TextBox MaskBox => _mask;

    internal bool SavedDefaults { get; private set; }

    private void ToggleAdvanced()
    {
        _advanced.IsVisible = !_advanced.IsVisible;
        Update();
    }

    private UnpackFolderRule Rule => _always.IsChecked == true ? UnpackFolderRule.Always : _never.IsChecked == true ? UnpackFolderRule.Never : UnpackFolderRule.WhenNeeded;

    private string? Mask => string.IsNullOrWhiteSpace(_mask.Text) ? null : _mask.Text.Trim();

    private ExistingFilePolicy IfExistsPolicy() => _ifExists.SelectedIndex switch
    {
        1 => ExistingFilePolicy.OverwriteOlder,
        2 => ExistingFilePolicy.Overwrite,
        3 => ExistingFilePolicy.Skip,
        4 => ExistingFilePolicy.KeepBoth,
        _ => ExistingFilePolicy.Ask,
    };

    private UnpackOptions Options() => new()
    {
        IfExists = IfExistsPolicy(),
        KeepTimes = _times.IsChecked == true,
        KeepAttributes = _attrs.IsChecked == true,
        KeepFolders = _context.CopyMode || _keepFolders.IsChecked == true,
        Mask = _context.CopyMode ? null : Mask,
        Folder = _context.CopyMode ? UnpackFolderRule.Never : Rule,
        UnknownSizeLimit = (long)_context.Settings.UnpackAskMb * 1024 * 1024,
    };

    private OperationSettings CurrentSettings() => _context.CopyMode
        ? _context.Settings with { IfExists = IfExistsPolicy(), KeepFolderTimes = _times.IsChecked == true, KeepAttributes = _attrs.IsChecked == true }
        : _context.Settings with { IfExists = IfExistsPolicy(), UnpackFolder = Rule, UnpackKeepFolders = _keepFolders.IsChecked == true };

    private string? FullTarget()
    {
        var text = _to.Text ?? "";
        return text.Trim().Length == 0 ? null : PathInput.Resolve(text, _context.Target);
    }

    private void Update()
    {
        _advancedHead.Inlines!.Clear();
        _advancedHead.Inlines.Add(new Run((_advanced.IsVisible ? "▾  " : "▸  ") + AppStrings.Unpack_Advanced) { FontWeight = FontWeight.SemiBold });
        _summary.IsVisible = !_advanced.IsVisible;
        _summary.Text = _context.CopyMode ? CopySummary() : UnpackSummary();

        var options = Options();
        var target = FullTarget();
        long size = 0;
        if (!_context.CopyMode)
        {
            if (_context.Sources.Count == 1)
            {
                var plan = ArchiveUnpacker.Plan(_context.Sources[0], options);
                var into = target is null ? "" : ArchiveUnpacker.FolderFor(plan, Rule, _context.Sources[0].ArchiveName) is { } sub ? WindowsPath.Join(target, sub) + "\\" : WindowsPath.Join(target, "");
                var folders = Loc.Plural(plan.TopFolders, AppStrings.Unpack_Top_Folders_One, AppStrings.Unpack_Top_Folders_Other);
                var files = Loc.Plural(plan.TopFiles, AppStrings.Unpack_Top_Files_One, AppStrings.Unpack_Top_Files_Other);
                var top = plan.TopFolders > 0 && plan.TopFiles > 0 ? Loc.Format(AppStrings.Unpack_Top, folders, files) : plan.TopFolders > 0 ? folders : files;
                _where.Text = Loc.Format(plan.NeedsFolder ? AppStrings.Unpack_GoesInto_Other : AppStrings.Unpack_GoesInto_One, top, into);
            }
            else
            {
                _where.Text = Rule switch
                {
                    UnpackFolderRule.Never => AppStrings.Unpack_EachStraightIn,
                    UnpackFolderRule.Always => AppStrings.Unpack_EachAlways,
                    _ => AppStrings.Unpack_EachWhenNeeded,
                };
            }
        }

        foreach (var source in _context.Sources)
        {
            size += ArchiveUnpacker.Plan(source, options).TotalSize;
        }

        var free = target is null ? null : _fs.Describe(target).FreeBytes;
        var root = target is null ? "" : (WindowsPath.GetRoot(target) ?? target).TrimEnd('\\');
        var fits = free is not { } f || size <= f;
        _checkDot.Res(Shape.FillProperty, target is null || !fits ? "Brush.Warning" : "Brush.Success");
        _check.Text = target is null
            ? AppStrings.Copy_TypeWhereCopy
            : free is { } room
                ? Loc.Format(fits ? AppStrings.Unpack_Fits : AppStrings.Unpack_DoesNotFit, _format.Size(size), root, _format.Size(room))
                : AppStrings.Unpack_Checked;
        _go.IsEnabled = _queue.IsEnabled = target is not null;
    }

    private string CopySummary() => (_times.IsChecked == true, _attrs.IsChecked == true) switch
    {
        (true, true) => AppStrings.UnpackCopy_Keeping_Both,
        (true, false) => AppStrings.UnpackCopy_Keeping_Times,
        (false, true) => AppStrings.UnpackCopy_Keeping_Attributes,
        _ => AppStrings.UnpackCopy_Keeping_Neither,
    };

    private string UnpackSummary() => string.Join(" · ",
        Rule switch
        {
            UnpackFolderRule.Always => AppStrings.Unpack_Sum_Always,
            UnpackFolderRule.Never => AppStrings.Unpack_Sum_Never,
            _ => AppStrings.Unpack_Sum_WhenNeeded,
        },
        Mask is { } mask ? Loc.Format(AppStrings.Unpack_Sum_Only, mask) : AppStrings.Unpack_Sum_AllFiles,
        _keepFolders.IsChecked == true ? AppStrings.Unpack_Sum_KeepFolders : AppStrings.Unpack_Sum_NoFolders,
        _delete.IsChecked == true ? AppStrings.Unpack_Sum_Delete : AppStrings.Unpack_Sum_Kept);

    private async Task BrowseAsync()
    {
        var start = FullTarget() ?? _context.Target;
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = _context.CopyMode ? AppStrings.Copy_BrowseTitleCopy : AppStrings.Unpack_BrowseTitle,
            AllowMultiple = false,
            SuggestedStartLocation = start.Length == 0 ? null : await StorageProvider.TryGetFolderFromPathAsync(start),
        });
        if (folders.Count > 0 && folders[0].TryGetLocalPath() is { } path)
        {
            _to.Text = WindowsPath.Join(path, "");
            _to.CaretIndex = _to.Text.Length;
        }
    }

    internal void Accept(bool toQueue)
    {
        if (!_go.IsEnabled || FullTarget() is not { } target)
        {
            return;
        }

        Close(new UnpackRequest(target, Options(), toQueue, !_context.CopyMode && _delete.IsChecked == true));
    }

    internal void OpenAdvanced()
    {
        if (!_advanced.IsVisible)
        {
            ToggleAdvanced();
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
            Close(null);
            e.Handled = true;
        }
        else if (e.Key == Key.Enter && e.Source is not Button)
        {
            var queueFirst = _context.Settings.Enter == EnterAction.AddToQueue;
            Accept(toQueue: e.KeyModifiers.HasFlag(KeyModifiers.Shift) ? !queueFirst : queueFirst);
            e.Handled = true;
        }
    }

    private static Control LabelRow(string label, Control control)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("160,*"), ColumnSpacing = 10 };
        var text = OptionsUi.Text(label, 13, brush: "Brush.SecondaryText");
        text.VerticalAlignment = VerticalAlignment.Center;
        grid.Children.Add(text);
        Grid.SetColumn(control, 1);
        control.HorizontalAlignment = HorizontalAlignment.Left;
        grid.Children.Add(control);
        return grid;
    }

    private static TextBlock Head(string text) => OptionsUi.Text(text, 12, FontWeight.SemiBold, "Brush.SecondaryText");

    private static CheckBox Check(string text, bool on, string? note = null)
        => new() { Content = Labelled(text, note), IsChecked = on, FontSize = 13, MinHeight = 26 };

    private RadioButton Radio(string text, string? note, bool on)
    {
        var radio = new RadioButton { Content = Labelled(text, note), IsChecked = on, GroupName = "UnpackFolder" + GetHashCode(), FontSize = 13, MinHeight = 26 };
        radio.IsCheckedChanged += (_, _) => Update();
        return radio;
    }

    private static object Labelled(string text, string? note)
        => note is null ? text : new StackPanel { Spacing = 1, Children = { OptionsUi.Text(text, 13), OptionsUi.Text(note, 12, brush: "Brush.TertiaryText", wrap: true).Also(t => t.MaxWidth = 440) } };
}

// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.App/Dialogs/CopyIntoArchiveDialog.cs

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
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

internal sealed record CopyIntoContext(bool Move, string Heading, string To, ArchiveContents Archive, long Size, OperationSettings Settings);

internal sealed record CopyIntoResult(PackLevel Level, bool StoreCompressed, ExistingFilePolicy IfExists, bool ToQueue);

internal sealed class CopyIntoArchiveDialog : Window
{
    private readonly CopyIntoContext _context;
    private readonly IOperationFileSystem _fs;
    private readonly DisplayFormat _format;
    private readonly Action<OperationSettings> _saveDefaults;
    private readonly PackLevel _archiveLevel;
    private readonly ComboBox _ifExists, _level;
    private readonly CheckBox _storeCompressed;
    private readonly StackPanel _advanced;
    private readonly TextBlock _advancedHead = new() { FontSize = 13 };
    private readonly TextBlock _summary = OptionsUi.Text("", 12, brush: "Brush.SecondaryText", wrap: true);
    private readonly TextBlock _check = OptionsUi.Text("", 12, brush: "Brush.SecondaryText", wrap: true);
    private readonly Ellipse _checkDot = new() { Width = 8, Height = 8, Margin = new Thickness(0, 5, 0, 0), VerticalAlignment = VerticalAlignment.Top };
    private readonly Button _go, _queue;

    public CopyIntoArchiveDialog(CopyIntoContext context, IOperationFileSystem fs, DisplayFormat format, Action<OperationSettings> saveDefaults)
    {
        _context = context;
        _fs = fs;
        _format = format;
        _saveDefaults = saveDefaults;
        _archiveLevel = ArchivePacker.LevelOf(context.Archive);
        var verb = context.Move ? AppStrings.Copy_Move : AppStrings.Copy_Copy;
        var s = context.Settings;
        Title = context.Heading;
        Width = 580;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        this.Res(BackgroundProperty, "Brush.PanelBackground");

        var body = new StackPanel { Spacing = 10, Margin = new Thickness(20, 8, 20, 16) };
        body.Children.Add(OptionsUi.Text(context.Move ? AppStrings.CopyInto_NoteMove : AppStrings.CopyInto_Note, 13, brush: "Brush.SecondaryText", wrap: true));

        var to = new TextBox { MinHeight = 32, VerticalContentAlignment = VerticalAlignment.Center, Text = context.To, IsReadOnly = true };
        to.Res(TextBox.FontFamilyProperty, "Font.Mono");
        Avalonia.Automation.AutomationProperties.SetName(to, AppStrings.Copy_To);
        body.Children.Add(new StackPanel { Spacing = 5, Children = { OptionsUi.Text(AppStrings.Copy_To, 13, FontWeight.SemiBold), to } });

        _ifExists = OptionsUi.Combo(
            [
                (ExistingFilePolicy.Ask, AppStrings.Copy_IfExists_Ask),
                (ExistingFilePolicy.OverwriteOlder, AppStrings.Copy_IfExists_OverwriteOlder),
                (ExistingFilePolicy.Overwrite, AppStrings.Copy_IfExists_OverwriteAll),
                (ExistingFilePolicy.Skip, AppStrings.Copy_IfExists_SkipAll),
                (ExistingFilePolicy.KeepBoth, AppStrings.Copy_IfExists_KeepBoth),
            ], s.IfExists, _ => { }, minWidth: 260);
        body.Children.Add(LabelRow(AppStrings.Copy_IfExists, _ifExists));

        _level = OptionsUi.Combo(
            [
                ((PackLevel?)null, Loc.Format(AppStrings.CopyInto_LevelAsArchive, LevelName(_archiveLevel))),
                (PackLevel.Store, AppStrings.Pack_Level_Store),
                (PackLevel.Fast, AppStrings.Pack_Level_Fast),
                (PackLevel.Normal, AppStrings.Pack_Level_Normal),
                (PackLevel.Maximum, AppStrings.Pack_Level_Maximum),
            ], (PackLevel?)null, _ => Update(), minWidth: 220);
        _storeCompressed = new CheckBox { Content = AppStrings.Pack_StoreCompressed, IsChecked = s.PackStoreCompressed, FontSize = 13, MinHeight = 26 };
        _storeCompressed.IsCheckedChanged += (_, _) => Update();
        _storeCompressed.IsEnabled = context.Archive.FormatName != "tar";
        _level.IsEnabled = context.Archive.FormatName != "tar";
        _advanced = new StackPanel { Spacing = 6, Margin = new Thickness(18, 4, 0, 0), IsVisible = false };
        _advanced.Children.Add(LabelRow(AppStrings.Pack_Compression, _level));
        _advanced.Children.Add(_storeCompressed);
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
        body.Children.Add(new Border { Height = 1, Margin = new Thickness(0, 4, 0, 0) }.Res(Border.BackgroundProperty, "Brush.Border"));
        body.Children.Add(new StackPanel { Spacing = 2, Children = { disclose, _summary, _advanced } });
        body.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { _checkDot, _check } }.Also(_ => _check.MaxWidth = 500));

        var save = Operations.Ui.Link(AppStrings.Copy_SaveDefaults, () => _saveDefaults(_context.Settings with { IfExists = IfExists, PackStoreCompressed = _storeCompressed.IsChecked == true }));
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
        Opened += (_, _) => _go.Focus();
    }


    internal string Summary => _summary.Text ?? "";

    internal string CheckText => _check.Text ?? "";

    internal ComboBox IfExistsBox => _ifExists;

    private ExistingFilePolicy IfExists => _ifExists.SelectedIndex switch
    {
        1 => ExistingFilePolicy.OverwriteOlder,
        2 => ExistingFilePolicy.Overwrite,
        3 => ExistingFilePolicy.Skip,
        4 => ExistingFilePolicy.KeepBoth,
        _ => ExistingFilePolicy.Ask,
    };

    private PackLevel Level => _level.SelectedIndex switch
    {
        1 => PackLevel.Store,
        2 => PackLevel.Fast,
        3 => PackLevel.Normal,
        4 => PackLevel.Maximum,
        _ => _archiveLevel,
    };

    private static string LevelName(PackLevel level) => level switch
    {
        PackLevel.Store => AppStrings.Pack_Level_Store,
        PackLevel.Fast => AppStrings.Pack_Level_Fast,
        PackLevel.Maximum => AppStrings.Pack_Level_Maximum,
        _ => AppStrings.Pack_Level_Normal,
    };

    private void ToggleAdvanced()
    {
        _advanced.IsVisible = !_advanced.IsVisible;
        Update();
    }

    internal void OpenAdvanced()
    {
        if (!_advanced.IsVisible)
        {
            ToggleAdvanced();
        }
    }

    private void Update()
    {
        _advancedHead.Inlines!.Clear();
        _advancedHead.Inlines.Add(new Run((_advanced.IsVisible ? "▾  " : "▸  ") + AppStrings.Unpack_Advanced) { FontWeight = FontWeight.SemiBold });
        _summary.IsVisible = !_advanced.IsVisible;
        var level = _level.SelectedIndex <= 0 ? Loc.Format(AppStrings.CopyInto_Sum_AsArchive, LevelName(_archiveLevel)) : Loc.Format(AppStrings.CopyInto_Sum_Level, LevelName(Level));
        _summary.Text = _context.Archive.FormatName == "tar" ? AppStrings.CopyInto_Sum_Tar
            : string.Join(" · ", level, _storeCompressed.IsChecked == true ? AppStrings.Pack_Sum_Stored : AppStrings.Pack_Sum_PackedToo);

        var archive = _context.Archive.ArchivePath;
        var need = _context.Archive.FileLength + _context.Size;
        var free = _fs.Describe(archive).FreeBytes;
        var drive = (WindowsPath.GetRoot(archive) ?? archive).TrimEnd('\\');
        var fits = free is not { } f || need <= f;
        _checkDot.Res(Shape.FillProperty, fits ? "Brush.Warning" : "Brush.Danger");
        _check.Text = free is { } room
            ? Loc.Format(fits ? AppStrings.CopyInto_Room : AppStrings.CopyInto_NoRoom, WindowsPath.GetName(archive), _format.Size(need), drive, _format.Size(room))
            : Loc.Format(AppStrings.CopyInto_RoomUnknown, WindowsPath.GetName(archive), _format.Size(need));
        _go.IsEnabled = _queue.IsEnabled = fits;
    }

    internal void Accept(bool toQueue)
    {
        if (!_go.IsEnabled)
        {
            return;
        }

        Close(new CopyIntoResult(Level, _storeCompressed.IsChecked == true, IfExists, toQueue));
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
}

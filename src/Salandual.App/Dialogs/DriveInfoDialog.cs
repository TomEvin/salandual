// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.App/Dialogs/DriveInfoDialog.cs

using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Layout;
using Avalonia.Media;
using Salandual.App.Controls;
using Salandual.App.Options;
using Salandual.App.Resources;
using Salandual.App.Theme;
using Salandual.Core.Listing;
using Salandual.Core.Resources;
using Salandual.Platform.Windows.FileSystem;

namespace Salandual.App.Dialogs;

public sealed class DriveInfoDialog : Window
{
    private static readonly char[] _fatForbidden = ['*', '?', '.', ',', ';', ':', '/', '\\', '|', '+', '=', '<', '>', '[', ']', '"'];
    private const string PencilGlyph = "Icon.Rename";
    private const string CheckGlyph = "Icon.Confirm";
    private const string CrossGlyph = "Icon.Cross";
    private const string WarningGlyph = "Icon.Warning";
    private const string ShieldGlyph = "Icon.Shield";
    private const string CopyGlyph = "Icon.CopyText";
    private const string ExternalGlyph = "Icon.External";

    private readonly string _path;
    private readonly DisplayFormat _format;
    private readonly StackPanel _body = new() { Spacing = 20, Margin = new Thickness(24, 6, 24, 20) };
    private readonly Border _header = new();
    private VolumeDetails? _details;
    private bool _editing;
    private bool _needsAdmin;
    private string? _error;
    private bool _renamed;
    private TextBox? _labelBox;

    public DriveInfoDialog(string path, DisplayFormat format)
    {
        _path = path;
        _format = format;
        var root = WindowsPath.GetRoot(path) ?? path;
        Title = Loc.Format(AppStrings.Drive_Title, root.TrimEnd('\\'));
        Width = 540;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        this.Res(BackgroundProperty, "Brush.PanelBackground");

        _body.Children.Add(OptionsUi.Text(AppStrings.Drive_Reading, 13, brush: "Brush.SecondaryText"));

        var properties = new Button { Content = WithIcon(ExternalGlyph, AppStrings.Drive_WindowsProperties), Padding = new Thickness(14, 5) };
        properties.Click += (_, _) => VolumeInfo.ShowProperties(TryGetPlatformHandle()?.Handle ?? IntPtr.Zero, root);
        var close = new Button { Content = AppStrings.Common_Close, IsDefault = true, MinWidth = 96, Padding = new Thickness(14, 5), HorizontalContentAlignment = HorizontalAlignment.Center };
        close.Classes.Add("accent");
        close.Click += (_, _) => Close();
        var footer = new Border
        {
            Padding = new Thickness(24, 12),
            BorderThickness = new Thickness(0, 1, 0, 0),
            Child = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right, Children = { properties, close } },
        }.Res(Border.BackgroundProperty, "Brush.WindowBackground").Res(Border.BorderBrushProperty, "Brush.Border");

        var dock = new DockPanel();
        DockPanel.SetDock(footer, Dock.Bottom);
        dock.Children.Add(footer);
        dock.Children.Add(_body);
        Content = TitleBarParts.ForDialog(this, dock);
        Reading = ReloadAsync();
        Opened += (_, _) => close.Focus();
    }

    internal Task Reading { get; private set; }

    internal VolumeDetails? Details => _details;

    internal TextBox? LabelBox => _editing ? _labelBox : null;

    internal bool IsRenaming => _editing;

    internal bool NeedsAdministrator => _needsAdmin;

    internal string? RenameError => _error;

    internal UsageRing? Ring { get; private set; }

    internal Control? StatusLine { get; private set; }

    internal Button? Elevate { get; private set; }

    internal bool CanRename => _details is { } d && d.Kind is not (DriveKind.Network or DriveKind.Optical) && !d.IsReadOnly;

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Handled)
        {
            return;
        }

        if (e.Key == Key.F2 && !_editing && CanRename)
        {
            StartRename();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            if (_editing)
            {
                CancelRename();
            }
            else
            {
                Close();
            }

            e.Handled = true;
        }
    }

    private async Task ReloadAsync()
    {
        var path = _path;
        _details = await Task.Run(() => VolumeInfo.Read(path));
        ShowDetails();
    }

    private void ShowDetails()
    {
        _body.Children.Clear();
        if (_details is not { } d)
        {
            _body.Children.Add(OptionsUi.Text(Loc.Format(AppStrings.Drive_CantRead, WindowsPath.GetRoot(_path) ?? _path), 13, brush: "Brush.SecondaryText", wrap: true));
            return;
        }

        UpdateHeader();
        _body.Children.Add(_header);
        _body.Children.Add(Usage(d));
        StatusLine = !DiskSpace.IsMadeUp(d.Total) && UsageRing.LevelOf(d.Free, d.Total) is not SpaceLevel.Normal and var level ? Status(d, level) : null;
        if (StatusLine is not null)
        {
            _body.Children.Add(StatusLine);
        }

        _body.Children.Add(Table(d));
    }

    private void UpdateHeader()
    {
        if (_details is not { } d)
        {
            return;
        }

        var icon = AppIcons.Glyph(d.Kind == DriveKind.Network ? "Icon.NetworkLarge" : "Icon.DriveLarge", 26, 26, 1.2);
        icon.HorizontalAlignment = HorizontalAlignment.Center;
        var tile = new Border { Width = 44, Height = 44, CornerRadius = new CornerRadius(10), BorderThickness = new Thickness(1), Child = icon, VerticalAlignment = VerticalAlignment.Top }
            .Res(Border.BackgroundProperty, "Brush.WindowBackground").Res(Border.BorderBrushProperty, "Brush.Border");

        var line = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, MinHeight = 34 };
        line.Children.Add(Centered(OptionsUi.Text(d.Root.TrimEnd('\\'), 20, FontWeight.SemiBold)));
        if (_editing)
        {
            var box = _labelBox = new TextBox { Text = _labelBox?.Text ?? d.Label ?? "", Width = 240, Height = 32, FontSize = 16, MaxLength = d.MaxLabelLength, VerticalContentAlignment = VerticalAlignment.Center };
            Avalonia.Automation.AutomationProperties.SetName(box, AppStrings.Drive_VolumeLabel);
            if (_error is not null || _needsAdmin)
            {
                box.Res(TextBox.BorderBrushProperty, "Brush.Danger");
            }

            box.KeyDown += async (_, e) =>
            {
                if (e.Key == Key.Enter)
                {
                    e.Handled = true;
                    await SaveRenameAsync();
                }
                else if (e.Key == Key.Escape)
                {
                    e.Handled = true;
                    CancelRename();
                }
            };
            box.TextChanged += (_, _) =>
            {
                if (_error is not null)
                {
                    _error = null;
                    box.ClearValue(TextBox.BorderBrushProperty);
                }
            };
            line.Children.Add(box);
            line.Children.Add(IconButton(CheckGlyph, AppStrings.Drive_SaveEnter, async () => await SaveRenameAsync(), accent: true));
            line.Children.Add(IconButton(CrossGlyph, AppStrings.Drive_CancelEsc, CancelRename));
        }
        else
        {
            var name = OptionsUi.Text(d.Label ?? (d.Kind == DriveKind.Network ? "" : AppStrings.Drive_NoLabel), 20, brush: d.Label is null ? "Brush.TertiaryText" : "Brush.Text");
            if (CanRename)
            {
                var hover = new Border { Child = name, Padding = new Thickness(4, 1), Margin = new Thickness(-4, 0, 0, 0), CornerRadius = new CornerRadius(4), Background = Brushes.Transparent, Cursor = new Cursor(StandardCursorType.Ibeam) };
                ToolTip.SetTip(hover, AppStrings.Drive_RenameF2);
                hover.PointerEntered += (_, _) => hover.Res(Border.BackgroundProperty, "Brush.Hover");
                hover.PointerExited += (_, _) => hover.Background = Brushes.Transparent;
                hover.PointerPressed += (_, e) =>
                {
                    e.Handled = true;
                    StartRename();
                };
                line.Children.Add(Centered(hover));
                line.Children.Add(IconButton(PencilGlyph, AppStrings.Drive_RenameF2, StartRename));
            }
            else
            {
                line.Children.Add(Centered(name));
            }

            if (_renamed)
            {
                line.Children.Add(Centered(OptionsUi.Text(AppStrings.Drive_Renamed, 12, brush: "Brush.Success")));
            }
        }

        var parts = new[] { KindText(d), MediaText(d), d.FileSystem, d.Share is { } s && s != d.Root ? s : null };
        var kind = OptionsUi.Text(string.Join("  ·  ", parts.Where(x => x is not null)), 12, brush: "Brush.SecondaryText");
        var column = new StackPanel { Children = { line, kind } };
        if (Note(d) is { } note)
        {
            note.Margin = new Thickness(0, 4, 0, 0);
            column.Children.Add(note);
        }

        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), ColumnSpacing = 14 };
        grid.Children.Add(tile);
        Grid.SetColumn(column, 1);
        grid.Children.Add(column);
        _header.Child = grid;
    }

    private Control? Note(VolumeDetails d)
    {
        Elevate = null;
        if (_editing && _needsAdmin)
        {
            var elevate = Elevate = new Button { Content = WithIcon(ShieldGlyph, AppStrings.Drive_RenameAsAdmin, "Brush.Accent"), Padding = new Thickness(0), Background = Brushes.Transparent, BorderThickness = default, Cursor = new Cursor(StandardCursorType.Hand) };
            elevate.Click += async (_, _) => await RenameElevatedAsync();
            return new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 10,
                Children = { Centered(OptionsUi.Text(Loc.Format(AppStrings.Drive_RenameNeedsAdmin, d.Root.TrimEnd('\\')), 12, brush: "Brush.Danger")), elevate },
            };
        }

        if (_editing && _error is not null)
        {
            return OptionsUi.Text(_error, 12, brush: "Brush.Danger", wrap: true);
        }

        if (_editing)
        {
            return OptionsUi.Text(Loc.Format(AppStrings.Drive_LabelHint, d.FileSystem, d.MaxLabelLength), 12, brush: "Brush.TertiaryText");
        }

        if (!CanRename)
        {
            return OptionsUi.Text(d.Kind == DriveKind.Network ? AppStrings.Drive_NetworkName : AppStrings.Drive_CantRename, 12, brush: "Brush.TertiaryText");
        }

        return null;
    }

    private static string KindText(VolumeDetails d) => d.Kind switch
    {
        DriveKind.Fixed => AppStrings.Drive_Kind_Fixed,
        DriveKind.Removable => AppStrings.Drive_Kind_Removable,
        DriveKind.Optical => AppStrings.Drive_Kind_Optical,
        DriveKind.Network => AppStrings.Drive_Kind_Network,
        _ => AppStrings.Drive_Kind_Other,
    };

    private static string? MediaText(VolumeDetails d) => d.SolidState switch { true => "SSD", false => "HDD", _ => null };

    private Grid Usage(VolumeDetails d)
    {
        if (DiskSpace.IsMadeUp(d.Total))
        {
            Ring = new UsageRing { Fraction = 0, CenterText = AppStrings.Ring_Cloud, Level = SpaceLevel.Normal, Width = 116, Height = 116 };
            var note = OptionsUi.Text(Loc.Format(AppStrings.Drive_CloudNote, _format.Size(d.Total)), 13, brush: "Brush.SecondaryText", wrap: true);
            note.VerticalAlignment = VerticalAlignment.Center;
            var cloud = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), ColumnSpacing = 24 };
            cloud.Children.Add(Ring);
            Grid.SetColumn(note, 1);
            cloud.Children.Add(note);
            return cloud;
        }

        var used = Math.Max(0, d.Total - d.Free);
        var level = UsageRing.LevelOf(d.Free, d.Total);
        Ring = new UsageRing { Fraction = d.Total > 0 ? (double)used / d.Total : 0, CenterText = _format.Size(d.Free), Level = level, Width = 116, Height = 116 };
        var amounts = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), RowDefinitions = new RowDefinitions("Auto,Auto,Auto"), ColumnSpacing = 12, RowSpacing = 8, VerticalAlignment = VerticalAlignment.Center };
        void Row(int row, string? swatch, string label, string value, string labelBrush = "Brush.Text")
        {
            if (swatch is not null)
            {
                var dot = new Border { Width = 10, Height = 10, CornerRadius = new CornerRadius(3), VerticalAlignment = VerticalAlignment.Center }.Res(Border.BackgroundProperty, swatch);
                Grid.SetRow(dot, row);
                amounts.Children.Add(dot);
            }

            var l = OptionsUi.Text(label, 13, brush: labelBrush);
            Grid.SetRow(l, row);
            Grid.SetColumn(l, 1);
            amounts.Children.Add(l);
            var v = OptionsUi.Text(value, 13, FontWeight.SemiBold);
            v.TextAlignment = TextAlignment.Right;
            Grid.SetRow(v, row);
            Grid.SetColumn(v, 2);
            amounts.Children.Add(v);
        }

        Row(0, UsageRing.BrushKey(level), AppStrings.Drive_Used, _format.Size(used));
        Row(1, "Brush.Pressed", AppStrings.Drive_Free, _format.Size(d.Free));
        Row(2, null, AppStrings.Drive_Capacity, _format.Size(d.Total), "Brush.SecondaryText");

        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), ColumnSpacing = 24 };
        grid.Children.Add(Ring);
        Grid.SetColumn(amounts, 1);
        grid.Children.Add(amounts);
        return grid;
    }

    private Panel Status(VolumeDetails d, SpaceLevel level)
    {
        var key = UsageRing.BrushKey(level);
        var pct = d.Total > 0 ? (int)Math.Round(100.0 * d.Free / d.Total) : 0;
        var text = OptionsUi.Text(
            Loc.Format(level == SpaceLevel.AlmostFull ? AppStrings.Drive_AlmostFull : AppStrings.Drive_GettingFull, _format.Size(d.Free), pct),
            12, brush: key, wrap: true);
        var icon = AppIcons.Glyph(WarningGlyph, 16, 16);
        icon.Res(Avalonia.Controls.Shapes.Shape.StrokeProperty, key);
        icon.VerticalAlignment = VerticalAlignment.Top;
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), ColumnSpacing = 8, Margin = new Thickness(12, 8) };
        row.Children.Add(icon);
        Grid.SetColumn(text, 1);
        row.Children.Add(text);
        var tint = new Border { CornerRadius = new CornerRadius(4), Opacity = 0.14 }.Res(Border.BackgroundProperty, key);
        return new Panel { Children = { tint, row } };
    }

    private StackPanel Table(VolumeDetails d)
    {
        var type = string.Join(", ", new[] { KindText(d), MediaText(d) }.Where(x => x is not null)) + (IsWindowsDrive(d.Root) ? AppStrings.Drive_WindowsHere : "");
        var rows = new List<(string Key, string Value)>
        {
            (AppStrings.Drive_Capacity, DiskSpace.IsMadeUp(d.Total) ? AppStrings.Drive_CloudCapacity : Loc.Format(AppStrings.Drive_Bytes, d.Total)),
            (AppStrings.Drive_FileSystem, d.FileSystem),
            (AppStrings.Drive_Type, type),
            (AppStrings.Drive_Serial, d.Serial),
            (AppStrings.Drive_ClusterSize, d.ClusterSize > 0 ? Loc.Format(AppStrings.Drive_Bytes, d.ClusterSize) : "—"),
            (AppStrings.Drive_LongestName, Loc.Format(AppStrings.Drive_Characters, d.MaxNameLength)),
        };

        var table = new StackPanel();
        static Border Line() => new Border { Height = 1 }.Res(Border.BackgroundProperty, "Brush.Border");
        table.Children.Add(Line());
        foreach (var (key, value) in rows)
        {
            table.Children.Add(TableRow(key, ValueWithCopy(value)));
            table.Children.Add(Line());
        }

        var chips = new WrapPanel { ItemSpacing = 4, LineSpacing = 4 };
        foreach (var feature in Features(d.Features))
        {
            chips.Children.Add(new Border
            {
                CornerRadius = new CornerRadius(9),
                Padding = new Thickness(8, 0),
                BorderThickness = new Thickness(1),
                Child = OptionsUi.Text(feature, 11, brush: "Brush.SecondaryText"),
            }.Res(Border.BackgroundProperty, "Brush.WindowBackground").Res(Border.BorderBrushProperty, "Brush.Border"));
        }

        table.Children.Add(TableRow(AppStrings.Drive_Features, chips.Children.Count > 0 ? chips : OptionsUi.Text("—", 13)));
        return table;
    }

    private static Grid TableRow(string key, Control value)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("150,*"), MinHeight = 32 };
        var k = OptionsUi.Text(key, 13, brush: "Brush.SecondaryText");
        k.VerticalAlignment = VerticalAlignment.Center;
        value.VerticalAlignment = VerticalAlignment.Center;
        value.Margin = new Thickness(0, 5);
        grid.Children.Add(k);
        Grid.SetColumn(value, 1);
        grid.Children.Add(value);
        return grid;
    }

    private StackPanel ValueWithCopy(string value)
    {
        var copy = IconButton(CopyGlyph, AppStrings.Common_Copy, async () =>
        {
            if (Clipboard is { } clipboard)
            {
                await clipboard.SetTextAsync(value);
            }
        }, size: 22);
        copy.Opacity = 0;
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Background = Brushes.Transparent, Children = { Centered(OptionsUi.Text(value, 13)), copy } };
        row.PointerEntered += (_, _) => copy.Opacity = 1;
        row.PointerExited += (_, _) => copy.Opacity = 0;
        return row;
    }

    private static bool IsWindowsDrive(string root)
        => string.Equals(Path.GetPathRoot(Environment.SystemDirectory), root, StringComparison.OrdinalIgnoreCase);

    internal void StartRename()
    {
        if (!CanRename || _editing)
        {
            return;
        }

        _editing = true;
        _needsAdmin = false;
        _error = null;
        _renamed = false;
        _labelBox = null;
        UpdateHeader();
        _labelBox?.Focus();
        _labelBox?.SelectAll();
    }

    internal void CancelRename()
    {
        _editing = _needsAdmin = false;
        _error = null;
        _labelBox = null;
        UpdateHeader();
    }

    internal async Task SaveRenameAsync()
    {
        if (_details is not { } d || _labelBox is null)
        {
            return;
        }

        var label = (_labelBox.Text ?? "").Trim();
        if (label == (d.Label ?? ""))
        {
            CancelRename();
            return;
        }

        var fat = d.FileSystem.StartsWith("FAT", StringComparison.OrdinalIgnoreCase);
        if (label.Any(char.IsControl) || label.Contains('"') || (fat && label.IndexOfAny(_fatForbidden) >= 0))
        {
            ShowRenameError(fat ? AppStrings.Drive_FatLabelChars : AppStrings.Drive_LabelChars);
            return;
        }

        var root = d.Root;
        try
        {
            await Task.Run(() => VolumeInfo.SetLabel(root, label));
        }
        catch (UnauthorizedAccessException)
        {
            _needsAdmin = true;
            _error = null;
            UpdateHeader();
            _labelBox?.Focus();
            return;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or IOException)
        {
            ShowRenameError(Loc.Format(AppStrings.Drive_CantRenameError, ex.Message));
            return;
        }

        await FinishRenameAsync();
    }

    private void ShowRenameError(string message)
    {
        _error = message;
        _needsAdmin = false;
        UpdateHeader();
        _labelBox?.Focus();
    }

    private async Task RenameElevatedAsync()
    {
        if (_details is not { } d || _labelBox is null)
        {
            return;
        }

        var label = (_labelBox.Text ?? "").Trim();
        if (!await VolumeInfo.SetLabelElevatedAsync(d.Root, label))
        {
            ShowRenameError(AppStrings.Drive_AdminDeclined);
            return;
        }

        await FinishRenameAsync();
    }

    private async Task FinishRenameAsync()
    {
        _editing = _needsAdmin = false;
        _error = null;
        _labelBox = null;
        await ReloadAsync();
        _renamed = true;
        UpdateHeader();
    }

    private static T Centered<T>(T control) where T : Control
    {
        control.VerticalAlignment = VerticalAlignment.Center;
        return control;
    }

    private static Button IconButton(string glyph, string tip, Action click, bool accent = false, double size = 28)
    {
        var icon = AppIcons.Glyph(glyph, 16, 16, 1.3);
        if (accent)
        {
            icon.Res(Avalonia.Controls.Shapes.Shape.StrokeProperty, "Brush.Accent");
        }

        icon.HorizontalAlignment = HorizontalAlignment.Center;
        var button = new Button { Content = icon, Width = size, Height = size, Padding = new Thickness(0), Background = Brushes.Transparent, BorderThickness = default, VerticalAlignment = VerticalAlignment.Center, HorizontalContentAlignment = HorizontalAlignment.Center, VerticalContentAlignment = VerticalAlignment.Center };
        ToolTip.SetTip(button, tip);
        Avalonia.Automation.AutomationProperties.SetName(button, tip);
        button.Click += (_, _) => click();
        return button;
    }

    private static StackPanel WithIcon(string glyph, string text, string brush = "Brush.Text")
    {
        var icon = AppIcons.Glyph(glyph, 16, 16, 1.3);
        icon.Res(Avalonia.Controls.Shapes.Shape.StrokeProperty, brush);
        var label = OptionsUi.Text(text, 13, brush: brush);
        label.VerticalAlignment = VerticalAlignment.Center;
        return new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Children = { icon, label } };
    }

    private static List<string> Features(VolumeFeatures f)
    {
        var list = new List<string>();
        void Add(VolumeFeatures flag, string text)
        {
            if ((f & flag) != 0)
            {
                list.Add(text);
            }
        }

        Add(VolumeFeatures.UnicodeNames, AppStrings.Drive_Feature_Unicode);
        Add(VolumeFeatures.Compression, AppStrings.Drive_Feature_Compression);
        Add(VolumeFeatures.Encryption, AppStrings.Drive_Feature_Encryption);
        Add(VolumeFeatures.HardLinks, AppStrings.Drive_Feature_HardLinks);
        Add(VolumeFeatures.NamedStreams, AppStrings.Drive_Feature_Streams);
        Add(VolumeFeatures.SparseFiles, AppStrings.Drive_Feature_Sparse);
        Add(VolumeFeatures.Quotas, AppStrings.Drive_Feature_Quotas);
        Add(VolumeFeatures.BlockCloning, AppStrings.Drive_Feature_BlockCloning);
        Add(VolumeFeatures.ReadOnly, AppStrings.Drive_Feature_ReadOnly);
        return list;
    }
}

// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.App/Dialogs/ArchiveEntryDialog.cs

using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Salandual.App.Controls;
using Salandual.App.Options;
using Salandual.App.Resources;
using Salandual.Archives;
using Salandual.Core.Listing;
using Salandual.Core.Resources;

namespace Salandual.App.Dialogs;

internal sealed class ArchiveEntryDialog : Window
{
    public ArchiveEntryDialog(ArchiveLocation location, string name, DisplayFormat format, string formatLabel)
    {
        Title = AppStrings.ArchiveEntry_Title;
        Width = 500;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        this.Res(BackgroundProperty, "Brush.PanelBackground");

        var contents = location.Contents;
        var inner = location.Inner.Length == 0 ? name : location.Inner + "\\" + name;
        var item = contents.Find(inner);
        var isFolder = item?.IsDirectory ?? contents.IsFolder(inner);
        var where = location.Inner.Length == 0
            ? Path.GetFileName(location.ArchivePath)
            : Path.GetFileName(location.ArchivePath) + " › " + location.Inner.Replace("\\", " › ", StringComparison.Ordinal);

        var rows = new Grid { ColumnDefinitions = new ColumnDefinitions("130,*"), RowSpacing = 6, ColumnSpacing = 12 };
        void Row(string label, string value)
        {
            var r = rows.RowDefinitions.Count;
            rows.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            var l = OptionsUi.Text(label, 13, brush: "Brush.SecondaryText");
            var v = OptionsUi.Text(value, 13, wrap: true);
            Grid.SetRow(l, r);
            Grid.SetRow(v, r);
            Grid.SetColumn(v, 1);
            rows.Children.Add(l);
            rows.Children.Add(v);
            Values.Add(value);
        }

        if (isFolder)
        {
            var files = contents.FilesBelow(inner).ToList();
            Row(AppStrings.ArchiveEntry_Contains, Loc.Format(AppStrings.ArchiveEntry_FilesAndSize,
                Loc.Plural(files.Count, AppStrings.Archive_Files_One, AppStrings.Archive_Files_Other), format.Size(files.Sum(f => f.Size))));
        }
        else if (item is not null)
        {
            Row(AppStrings.ArchiveEntry_Size, Loc.Format(AppStrings.ArchiveEntry_Bytes, format.Size(item.Size), item.Size.ToString("N0", CultureInfo.CurrentCulture)));
            Row(AppStrings.ArchiveEntry_Packed, item.Size > 0
                ? Loc.Format(AppStrings.ArchiveEntry_PackedOf, format.Size(item.PackedSize), Percent(item.PackedSize, item.Size))
                : format.Size(item.PackedSize));
        }

        if (item?.ModifiedUtc is { } time)
        {
            var local = time.ToLocalTime();
            Row(AppStrings.ArchiveEntry_Modified, format.Date(local) + " " + format.Time(local));
        }

        if (item is not null)
        {
            var attributes = DisplayFormat.Attributes(item.Attributes & ~(FileAttributes.Directory | FileAttributes.Encrypted));
            Row(AppStrings.ArchiveEntry_Attributes, attributes.Length > 0 ? attributes : AppStrings.ArchiveEntry_None);
            Row(AppStrings.ArchiveEntry_Encrypted, item.Encrypted ? AppStrings.ArchiveEntry_Yes : AppStrings.ArchiveEntry_No);
        }

        var packedAll = contents.Items.Sum(i => i.PackedSize);
        Row(AppStrings.ArchiveEntry_Archive, Loc.Format(AppStrings.ArchiveEntry_ArchiveFacts, formatLabel,
            Loc.Plural(contents.FileCount, AppStrings.Archive_Files_One, AppStrings.Archive_Files_Other),
            Loc.Plural(contents.FolderCount, AppStrings.ArchiveEntry_Folders_One, AppStrings.ArchiveEntry_Folders_Other),
            format.Size(contents.TotalSize), format.Size(packedAll > 0 ? packedAll : contents.FileLength)));

        var body = new StackPanel { Spacing = 4, Margin = new Thickness(20, 8, 20, 18) };
        body.Children.Add(OptionsUi.Text(name, 15, FontWeight.SemiBold, wrap: true));
        body.Children.Add(OptionsUi.Text(Loc.Format(AppStrings.ArchiveEntry_In, where), 13, brush: "Brush.SecondaryText", wrap: true));
        rows.Margin = new Thickness(0, 10, 0, 0);
        body.Children.Add(rows);

        var close = new Button { Content = AppStrings.Common_Close, Padding = new Thickness(14, 5) };
        close.Classes.Add("accent");
        close.Click += (_, _) => Close();
        var footer = new Border
        {
            Padding = new Thickness(20, 12),
            BorderThickness = new Thickness(0, 1, 0, 0),
            Child = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Children = { close } },
        }.Res(Border.BackgroundProperty, "Brush.WindowBackground").Res(Border.BorderBrushProperty, "Brush.Border");

        var root = new DockPanel();
        DockPanel.SetDock(footer, Dock.Bottom);
        root.Children.Add(footer);
        root.Children.Add(body);
        Content = TitleBarParts.ForDialog(this, root);
    }

    internal List<string> Values { get; } = [];

    internal static string Percent(long packed, long size) => Math.Round(100.0 * packed / size).ToString("0", CultureInfo.CurrentCulture);

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (!e.Handled && e.Key is Key.Escape or Key.Enter)
        {
            Close();
            e.Handled = true;
        }
    }
}

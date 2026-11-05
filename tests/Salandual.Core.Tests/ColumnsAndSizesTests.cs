// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : tests/Salandual.Core.Tests/ColumnsAndSizesTests.cs

using System.Globalization;
using Salandual.Core.Listing;
using Salandual.Core.Panels;
using Salandual.Core.Settings;
using Salandual.Core.Vfs;

namespace Salandual.Core.Tests;

public sealed class ColumnsAndSizesTests : IDisposable
{
    private static readonly CultureInfo _en = CultureInfo.GetCultureInfo("en-US");
    private readonly string _dir = Directory.CreateTempSubdirectory("salandual-columns-").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    [Theory]
    [InlineData(SizeStyle.Short, "1.2 MB")]
    [InlineData(SizeStyle.Kilobytes, "1,229 KB")]
    [InlineData(SizeStyle.Bytes, "1,258,291")]
    public void SizeStyle_IsTheColumns_AndTheInformationLineStaysShort(SizeStyle style, string column)
    {
        var f = new DisplayFormat(SizeUnits.Windows, PanelSettings.DateFormatIso, _en, style: style);
        Assert.Equal(column, f.ColumnSize(1_258_291));
        Assert.Equal("1.2 MB", f.Size(1_258_291));

        var asPanel = new DisplayFormat(SizeUnits.Windows, PanelSettings.DateFormatIso, _en, style: style, infoAsPanel: true);
        Assert.Equal(column, asPanel.Size(1_258_291));
    }

    [Theory]
    [InlineData(DigitGrouping.Windows, "1,234,567")]
    [InlineData(DigitGrouping.Space, "1 234 567")]
    [InlineData(DigitGrouping.None, "1234567")]
    public void DigitGrouping_GroupsTheThousands(DigitGrouping grouping, string expected)
    {
        var f = new DisplayFormat(SizeUnits.Windows, PanelSettings.DateFormatIso, _en, style: SizeStyle.Bytes, grouping: grouping);
        Assert.Equal(expected, f.ColumnSize(1_234_567));
        Assert.Equal(expected, f.Number(1_234_567));
    }

    [Fact]
    public void RelativeDates_SayHowLongAgo_ThenTheDate()
    {
        var f = new DisplayFormat(SizeUnits.Windows, PanelSettings.DateFormatRelative, _en, TimeFormat.Hours24);
        var now = new DateTime(2026, 10, 28, 12, 0, 0);
        Assert.True(f.IsRelative);
        Assert.Equal("just now", f.ColumnDate(now.AddSeconds(-20), now));
        Assert.Equal("1 minute ago", f.ColumnDate(now.AddMinutes(-1), now));
        Assert.Equal("5 minutes ago", f.ColumnDate(now.AddMinutes(-5), now));
        Assert.Equal("2 hours ago", f.ColumnDate(now.AddHours(-2), now));
        Assert.Equal("today 03:15", f.ColumnDate(new DateTime(2026, 10, 28, 3, 15, 0), now));
        Assert.Equal("yesterday 18:23", f.ColumnDate(new DateTime(2026, 10, 27, 18, 23, 0), now));
        Assert.Equal("Sat 14:48", f.ColumnDate(new DateTime(2026, 10, 24, 14, 48, 0), now));
        Assert.Equal("2026-10-20 14:48", f.ColumnDate(new DateTime(2026, 10, 20, 14, 48, 0), now));
        Assert.Equal("2026-10-28", f.Date(now));

        var iso = new DisplayFormat(SizeUnits.Windows, PanelSettings.DateFormatIso, _en, TimeFormat.Hours24);
        Assert.Equal("2026-10-28 11:58", iso.ColumnDate(now.AddMinutes(-2), now));
    }

    [Fact]
    public void Views_HaveTheirDefaultColumns_AndChangesStayInThatView()
    {
        var detailed = ColumnViews.For([], PanelView.Detailed);
        Assert.Equal([PanelColumn.Name, PanelColumn.Extension, PanelColumn.Size, PanelColumn.Modified, PanelColumn.Attributes], detailed.Columns.Where(c => c.Shown).Select(c => c.Column));
        Assert.Equal(ColumnViews.AllColumns.Count, detailed.Columns.Count);
        var types = ColumnViews.For([], PanelView.Types);
        Assert.Equal([PanelColumn.Name, PanelColumn.Type, PanelColumn.Size, PanelColumn.Modified], types.Columns.Where(c => c.Shown).Select(c => c.Column));

        var kept = ColumnViews.With([], ColumnViews.Toggle(detailed, PanelColumn.Items));
        Assert.Contains(ColumnViews.For(kept, PanelView.Detailed).Columns, c => c.Column == PanelColumn.Items && c.Shown);
        Assert.DoesNotContain(ColumnViews.For(kept, PanelView.Types).Columns, c => c.Column == PanelColumn.Items && c.Shown);

        Assert.Same(detailed, ColumnViews.Toggle(detailed, PanelColumn.Name));
        var named = ColumnViews.SetWidth(detailed, PanelColumn.Name, 300);
        Assert.False(named.SmartName);
        Assert.Equal(300, named.NameWidth);
        Assert.Equal(PanelColumn.Name, ColumnViews.Move(detailed, PanelColumn.Extension, -1).Columns[0].Column);
        Assert.Equal(PanelColumn.Extension, ColumnViews.Move(detailed, PanelColumn.Size, -1).Columns[2].Column);
        Assert.Equal(PanelColumn.Size, ColumnViews.Move(detailed, PanelColumn.Size, -1).Columns[1].Column);
        Assert.Equal(0, ColumnViews.SetWidth(detailed, PanelColumn.Size, 0).Columns.First(c => c.Column == PanelColumn.Size).Width);
    }

    [Fact]
    public void OwnViews_AreMadeByNamingThem_AndHaveTheirKeys()
    {
        Assert.False(ColumnViews.IsMade([], PanelView.Own1));
        var mine = ColumnViews.For([], PanelView.Own1) with { Name = "Photos" };
        var kept = ColumnViews.With([], mine);
        Assert.True(ColumnViews.IsMade(kept, PanelView.Own1));
        Assert.False(ColumnViews.IsMade(kept, PanelView.Own2));
        Assert.False(ColumnViews.IsMade(ColumnViews.Without(kept, PanelView.Own1), PanelView.Own1));

        Assert.Equal(PanelView.Own3, PanelViews.FromCommand("View.Mode0"));
        Assert.Equal(PanelView.Own1, PanelViews.FromCommand("View.Mode8"));
        Assert.Equal(PanelView.Types, PanelViews.FromCommand("View.Mode7"));
        Assert.Equal("View.Mode0", PanelViews.Command(PanelView.Own3));
        Assert.Equal(PanelView.Columns, PanelViews.FromCommand("View.Mode1"));
        Assert.Null(PanelViews.FromCommand("View.Mode"));
        Assert.True(PanelViews.HasColumns(PanelView.Types) && PanelViews.HasColumns(PanelView.Own2) && !PanelViews.HasColumns(PanelView.Tiles));
    }

    [Fact]
    public void OldSettings_CarryOver_BytesBecomesAStyle_AndTheExtensionSwitch()
    {
        File.WriteAllText(Path.Combine(_dir, SettingsStore.FileName),
            """{ "schemaVersion": 1, "panels": { "sizeUnits": "Bytes", "extensionColumn": false } }""");
        var panels = SettingsStore.InDirectory(_dir).Load().Settings.Panels;
        Assert.Equal(SizeStyle.Bytes, panels.SizeStyle);
        Assert.Equal(SizeUnits.Windows, panels.SizeUnits);
        Assert.DoesNotContain(ColumnViews.For(panels.ColumnViews, PanelView.Detailed).Columns, c => c.Column == PanelColumn.Extension && c.Shown);

        var fresh = new AppSettings();
        Assert.Equal(SizeStyle.Short, fresh.Panels.SizeStyle);
        Assert.Equal(FolderSizeText.Folder, fresh.Panels.FolderSize);
        Assert.True(fresh.Panels.DimHidden);
        Assert.False(fresh.Panels.RowStripes);
    }

    [Fact]
    public void ColumnViews_SurviveSavingAndLoading()
    {
        var store = SettingsStore.InDirectory(_dir);
        var mine = ColumnViews.SetWidth(ColumnViews.Toggle(ColumnViews.For([], PanelView.Own2), PanelColumn.Created), PanelColumn.Created, 128) with { Name = "Photos" };
        store.Save(new AppSettings { Panels = new PanelSettings { ColumnViews = ColumnViews.With([], mine), DateFormat = PanelSettings.DateFormatRelative } });
        var loaded = store.Load().Settings.Panels;
        var back = ColumnViews.For(loaded.ColumnViews, PanelView.Own2);
        Assert.Equal("Photos", back.Name);
        Assert.Equal(128, back.Columns.First(c => c.Column == PanelColumn.Created).Width);
        Assert.Equal(PanelSettings.DateFormatRelative, loaded.DateFormat);
    }

    [Fact]
    public void SortingByType_UsesWindowsTypeNames_FoldersFirst()
    {
        var entries = new[]
        {
            new FileEntry("b.txt", 1, 0, FileAttributes.Normal),
            new FileEntry("a.jpg", 1, 0, FileAttributes.Normal),
            new FileEntry("z", 0, 0, FileAttributes.Directory),
            new FileEntry("c.pdf", 1, 0, FileAttributes.Normal),
        };
        string Type(FileEntry e) => e.Extension.ToString() switch { "txt" => "Text Document", "jpg" => "JPEG File", _ => "PDF Document" };
        foreach (var threshold in new[] { 1, 1000 })
        {
            var sorted = (FileEntry[])entries.Clone();
            new EntrySorter(_en, numericSort: true) { TypeName = Type }.Sort(sorted, new SortSpec(SortColumn.Type), threshold);
            Assert.Equal(["z", "a.jpg", "c.pdf", "b.txt"], sorted.Select(e => e.Name));
        }

        var created = new[] { new FileEntry("new", 1, 0, FileAttributes.Normal, 20), new FileEntry("old", 1, 0, FileAttributes.Normal, 10) };
        new EntrySorter(_en, numericSort: true).Sort(created, new SortSpec(SortColumn.Created));
        Assert.Equal("old", created[0].Name);
    }
}

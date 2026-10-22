// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : tests/Salandual.Core.Tests/DisplayFormatTests.cs

using System.Globalization;
using Salandual.Core.Listing;
using Salandual.Core.Settings;

namespace Salandual.Core.Tests;

public sealed class DisplayFormatTests
{
    private static readonly CultureInfo _en = CultureInfo.GetCultureInfo("en-US");
    private static readonly DateTime _when = new(2026, 8, 14, 18, 25, 0, DateTimeKind.Local);

    private static DisplayFormat Iso(SizeUnits units) => new(units, PanelSettings.DateFormatIso, _en);

    [Theory]
    [InlineData(SizeUnits.Windows, 0, "0 B")]
    [InlineData(SizeUnits.Windows, 1023, "1,023 B")]
    [InlineData(SizeUnits.Windows, 1024, "1 KB")]
    [InlineData(SizeUnits.Windows, 1025, "2 KB")]
    [InlineData(SizeUnits.Windows, 880_640, "860 KB")]
    [InlineData(SizeUnits.Windows, 1_258_291, "1.2 MB")]
    [InlineData(SizeUnits.Windows, 25_690_112, "24.5 MB")]
    [InlineData(SizeUnits.Windows, 3_650_722_201, "3.4 GB")]
    [InlineData(SizeUnits.Binary, 25_690_112, "24.5 MiB")]
    [InlineData(SizeUnits.Binary, 2048, "2 KiB")]
    [InlineData(SizeUnits.Binary, 3_650_722_201, "3.4 GiB")]
    [InlineData(SizeUnits.Decimal, 999, "999 B")]
    [InlineData(SizeUnits.Decimal, 1000, "1 kB")]
    [InlineData(SizeUnits.Decimal, 24_500_000, "24.5 MB")]
    [InlineData(SizeUnits.Decimal, 3_400_000_000, "3.4 GB")]
    [InlineData(SizeUnits.Bytes, 1_234_567, "1,234,567")]
    public void Size(SizeUnits units, long bytes, string expected) => Assert.Equal(expected, Iso(units).Size(bytes));

    [Theory]
    [InlineData(SizeUnits.Windows, 0, "0 B")]
    [InlineData(SizeUnits.Windows, 1023, "1,023 B")]
    [InlineData(SizeUnits.Windows, 1024, "1 KB")]
    [InlineData(SizeUnits.Windows, 2_900_000, "2,833 KB")]
    [InlineData(SizeUnits.Windows, 5L * 1024 * 1024 * 1024, "5,242,880 KB")]
    [InlineData(SizeUnits.Binary, 2048, "2 KiB")]
    [InlineData(SizeUnits.Decimal, 2_900_000, "2,900 kB")]
    [InlineData(SizeUnits.Bytes, 2_900_000, "2,900,000")]
    public void Kilobytes(SizeUnits units, long bytes, string expected) => Assert.Equal(expected, Iso(units).Kilobytes(bytes));

    [Fact]
    public void Size_UsesTheRegionDecimalSign_WithARegionDateFormat()
    {
        var hu = new DisplayFormat(SizeUnits.Windows, "hu-HU", _en);
        Assert.Equal("24,5 MB", hu.Size(25_690_112));
    }

    [Theory]
    [InlineData("iso", "2026-08-14")]
    [InlineData("en-US", "8/14/2026")]
    [InlineData("en-GB", "14/08/2026")]
    [InlineData("hu-HU", "2026. 08. 14.")]
    [InlineData("de-DE", "14.08.2026")]
    public void Date_FollowsTheDateFormat(string format, string date)
        => Assert.Equal(date, new DisplayFormat(SizeUnits.Windows, format, _en).Date(_when));

    [Theory]
    [InlineData(TimeFormat.Windows, "en-US", "6:25 PM", "6:25:00 PM", true)]
    [InlineData(TimeFormat.Windows, "hu-HU", "18:25", "18:25:00", false)]
    [InlineData(TimeFormat.Hours24, "en-US", "18:25", "18:25:00", false)]
    [InlineData(TimeFormat.Hours12, "hu-HU", "6:25 PM", "6:25:00 PM", true)]
    public void Time_FollowsTheTimeSetting_WhateverTheDateFormat(TimeFormat time, string windows, string expected, string withSeconds, bool twelve)
    {
        var f = new DisplayFormat(SizeUnits.Windows, "de-DE", CultureInfo.GetCultureInfo(windows), time);
        Assert.Equal(expected, f.Time(_when).Replace(' ', ' '));
        Assert.Equal(withSeconds, f.TimeWithSeconds(_when).Replace(' ', ' '));
        Assert.Equal(twelve, f.TwelveHour);
        Assert.Equal("14.08.2026 " + f.Time(_when), f.DateAndTime(_when));
    }

    [Fact]
    public void TimeOfDay_ForTheThemeSchedule()
    {
        Assert.Equal("07:05", new DisplayFormat(SizeUnits.Windows, "iso", _en, TimeFormat.Hours24).TimeOfDay(7 * 60 + 5));
        Assert.Equal("7:05 PM", new DisplayFormat(SizeUnits.Windows, "iso", _en, TimeFormat.Hours12).TimeOfDay(19 * 60 + 5));
    }

    [Fact]
    public void System_FollowsTheGivenCurrentCulture()
    {
        var f = new DisplayFormat(SizeUnits.Windows, PanelSettings.DateFormatSystem, CultureInfo.GetCultureInfo("de-DE"));
        Assert.Equal("14.08.2026", f.Date(_when));
    }

    [Fact]
    public void Samples_AreAtLeastAsWideAsRealValues()
    {
        var f = new DisplayFormat(SizeUnits.Binary, "en-US", _en);
        var (size, date, time) = f.Samples;
        Assert.Equal("999.9 MiB", size);
        Assert.Equal("12/28/2026", date);
        Assert.StartsWith("10:58", time);
    }

    [Theory]
    [InlineData(FileAttributes.ReadOnly | FileAttributes.Archive, "RA")]
    [InlineData(FileAttributes.Hidden | FileAttributes.System | FileAttributes.Directory, "HS")]
    [InlineData(FileAttributes.Archive | FileAttributes.Compressed, "AC")]
    [InlineData(FileAttributes.Encrypted, "E")]
    [InlineData(FileAttributes.Normal, "")]
    public void Attributes(FileAttributes a, string expected) => Assert.Equal(expected, DisplayFormat.Attributes(a));

    [Theory]
    [InlineData(null, "iso")]
    [InlineData("ISO", "iso")]
    [InlineData(" System ", "system")]
    [InlineData("hu-hu", "hu-HU")]
    [InlineData("xx-NOPE", "iso")]
    public void Settings_NormalizeDateFormat(string? input, string expected)
        => Assert.Equal(expected, new AppSettings { Panels = new PanelSettings { DateFormat = input! } }.Normalize().Panels.DateFormat);
}

// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.Core/Listing/DisplayFormat.cs

using System.Globalization;
using Salandual.Core.Settings;

namespace Salandual.Core.Listing;

public sealed class DisplayFormat
{
    private static readonly string[] _windowsUnits = ["KB", "MB", "GB", "TB", "PB", "EB"];
    private static readonly string[] _binaryUnits = ["KiB", "MiB", "GiB", "TiB", "PiB", "EiB"];
    private static readonly string[] _decimalUnits = ["kB", "MB", "GB", "TB", "PB", "EB"];

    private readonly SizeUnits _units;
    private readonly CultureInfo _numbers;
    private readonly CultureInfo? _dates;
    private readonly TimeFormat _time;
    private readonly CultureInfo _current;

    public DisplayFormat(SizeUnits units, string dateFormat, CultureInfo current, TimeFormat time = TimeFormat.Windows)
    {
        _units = units;
        _time = time;
        _current = current;
        _dates = dateFormat switch
        {
            PanelSettings.DateFormatIso => null,
            PanelSettings.DateFormatSystem => current,
            _ => TryCulture(dateFormat) ?? null,
        };
        _numbers = _dates ?? current;
    }

    public static DisplayFormat Default { get; } = new(SizeUnits.Windows, PanelSettings.DateFormatIso, CultureInfo.InvariantCulture);

    public static DisplayFormat From(PanelSettings panels, CultureInfo current) => new(panels.SizeUnits, panels.DateFormat, current, panels.TimeFormat);

    public string Size(long bytes)
    {
        if (_units == SizeUnits.Bytes)
        {
            return bytes.ToString("N0", _numbers);
        }

        double k = _units == SizeUnits.Decimal ? 1000 : 1024;
        var units = _units switch
        {
            SizeUnits.Binary => _binaryUnits,
            SizeUnits.Decimal => _decimalUnits,
            _ => _windowsUnits,
        };

        if (bytes < k)
        {
            return bytes.ToString("N0", _numbers) + " B";
        }

        var value = bytes / k;
        if (value < 1000)
        {
            return Math.Ceiling(value).ToString("N0", _numbers) + " " + units[0];
        }

        var unit = 1;
        value /= k;
        while (value >= 1000 && unit < units.Length - 1)
        {
            value /= k;
            unit++;
        }

        return value.ToString("0.0", _numbers) + " " + units[unit];
    }

    public string Kilobytes(long bytes)
    {
        if (_units == SizeUnits.Bytes)
        {
            return bytes.ToString("N0", _numbers);
        }

        double k = _units == SizeUnits.Decimal ? 1000 : 1024;
        if (bytes < k)
        {
            return bytes.ToString("N0", _numbers) + " B";
        }

        var unit = _units switch
        {
            SizeUnits.Binary => _binaryUnits[0],
            SizeUnits.Decimal => _decimalUnits[0],
            _ => _windowsUnits[0],
        };
        return Math.Ceiling(bytes / k).ToString("N0", _numbers) + " " + unit;
    }

    public string Date(DateTime local) => _dates is null ? local.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : local.ToString("d", _dates);

    public string Time(DateTime local) => _time switch
    {
        TimeFormat.Hours24 => local.ToString("HH:mm", CultureInfo.InvariantCulture),
        TimeFormat.Hours12 => local.ToString("h:mm tt", CultureInfo.InvariantCulture),
        _ => local.ToString("t", _current),
    };

    public string TimeWithSeconds(DateTime local) => _time switch
    {
        TimeFormat.Hours24 => local.ToString("HH:mm:ss", CultureInfo.InvariantCulture),
        TimeFormat.Hours12 => local.ToString("h:mm:ss tt", CultureInfo.InvariantCulture),
        _ => local.ToString("T", _current),
    };

    public string DateAndTime(DateTime local) => Date(local) + " " + Time(local);

    public string TimeOfDay(int minute) => Time(new DateTime(2000, 1, 1).AddMinutes(Math.Clamp(minute, 0, 24 * 60 - 1)));

    public bool TwelveHour => _time switch
    {
        TimeFormat.Hours24 => false,
        TimeFormat.Hours12 => true,
        _ => _current.DateTimeFormat.ShortTimePattern.Contains('t'),
    };


    public (string Size, string Date, string Time) Samples
    {
        get
        {
            var wide = new DateTime(2026, 12, 28, 22, 58, 0, DateTimeKind.Local);
            double k = _units == SizeUnits.Decimal ? 1000 : 1024;
            var size = _units == SizeUnits.Bytes ? Size(99_999_999_999) : Size((long)(999.9 * k * k));
            return (size, Date(wide), Time(wide));
        }
    }

    public static string Attributes(FileAttributes a)
    {
        Span<char> s = stackalloc char[6];
        var n = 0;
        foreach (var (flag, letter) in _attributeLetters)
        {
            if ((a & flag) != 0)
            {
                s[n++] = letter;
            }
        }

        return new string(s[..n]);
    }

    private static readonly (FileAttributes Flag, char Letter)[] _attributeLetters =
    [
        (FileAttributes.ReadOnly, 'R'), (FileAttributes.Hidden, 'H'), (FileAttributes.System, 'S'),
        (FileAttributes.Archive, 'A'), (FileAttributes.Compressed, 'C'), (FileAttributes.Encrypted, 'E'),
    ];

    private static CultureInfo? TryCulture(string name)
    {
        try
        {
            return CultureInfo.GetCultureInfo(name, predefinedOnly: true);
        }
        catch (CultureNotFoundException)
        {
            return null;
        }
    }
}

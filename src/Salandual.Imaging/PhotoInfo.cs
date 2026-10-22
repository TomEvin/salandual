// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.Imaging/PhotoInfo.cs

using System.Globalization;
using MetadataExtractor;
using MetadataExtractor.Formats.Exif;
using MetadataExtractor.Formats.Icc;
using MetadataExtractor.Formats.Iptc;
using MetadataExtractor.Formats.Xmp;
using Salandual.Core.Resources;

namespace Salandual.Imaging;

public enum InfoGroup { Summary, Exif, Iptc, Xmp, Gps, Colour, All }

public readonly record struct InfoRow(string Name, string Value);

public sealed class PhotoInfo
{
    private const int MaxValue = 400;

    private PhotoInfo(IReadOnlyDictionary<InfoGroup, IReadOnlyList<InfoRow>> groups, (double Latitude, double Longitude)? place)
    {
        Groups = groups;
        Place = place;
    }

    public static PhotoInfo Empty { get; } = new(new Dictionary<InfoGroup, IReadOnlyList<InfoRow>>(), null);

    public IReadOnlyDictionary<InfoGroup, IReadOnlyList<InfoRow>> Groups { get; }

    public (double Latitude, double Longitude)? Place { get; }

    public bool IsEmpty => Groups.Count == 0;

    public static PhotoInfo Read(string path)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 64 * 1024);
            return From(ImageMetadataReader.ReadMetadata(stream));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ImageProcessingException or ArgumentException or NotSupportedException or FormatException or OverflowException or IndexOutOfRangeException)
        {
            return Empty;
        }
    }

    internal static PhotoInfo From(IReadOnlyList<MetadataExtractor.Directory> directories)
    {
        var exif = new Exif([.. directories.OfType<ExifIfd0Directory>(), .. directories.OfType<ExifSubIfdDirectory>()]);
        var gps = directories.OfType<GpsDirectory>().FirstOrDefault();
        var iptc = directories.OfType<IptcDirectory>().FirstOrDefault();
        var xmp = directories.OfType<XmpDirectory>().FirstOrDefault();
        var xmpProps = xmp?.GetXmpProperties() ?? new Dictionary<string, string>();

        (double, double)? place = null;
        if (gps?.GetGeoLocation() is { } geo && !geo.IsZero && double.IsFinite(geo.Latitude) && double.IsFinite(geo.Longitude))
        {
            place = (geo.Latitude, geo.Longitude);
        }

        var groups = new Dictionary<InfoGroup, IReadOnlyList<InfoRow>>();
        void Add(InfoGroup group, List<InfoRow> rows)
        {
            if (rows.Count > 0)
            {
                groups[group] = rows;
            }
        }

        Add(InfoGroup.Summary, Summary(exif, iptc, xmpProps, place, gps));
        Add(InfoGroup.Exif, [.. exif.Blocks.SelectMany(Tags).Distinct()]);
        Add(InfoGroup.Iptc, Tags(iptc));
        Add(InfoGroup.Xmp, [.. xmpProps.Where(p => !string.IsNullOrWhiteSpace(p.Value)).Select(p => new InfoRow(p.Key, Cut(p.Value)))]);
        Add(InfoGroup.Gps, Tags(gps));
        Add(InfoGroup.Colour, [.. directories.OfType<IccDirectory>().SelectMany(Tags)]);

        var all = new List<InfoRow>();
        foreach (var directory in directories)
        {
            if (directory is XmpDirectory)
            {
                all.AddRange(xmpProps.Where(p => !string.IsNullOrWhiteSpace(p.Value)).Select(p => new InfoRow($"XMP › {p.Key}", Cut(p.Value))));
                continue;
            }

            all.AddRange(Tags(directory).Select(r => r with { Name = $"{directory.Name} › {r.Name}" }));
        }

        Add(InfoGroup.All, all);
        return groups.Count == 0 ? Empty : new PhotoInfo(groups.OrderBy(g => g.Key).ToDictionary(g => g.Key, g => g.Value), place);
    }

    private static List<InfoRow> Summary(Exif exif, IptcDirectory? iptc, IDictionary<string, string> xmp, (double Lat, double Lon)? place, GpsDirectory? gps)
    {
        var rows = new List<InfoRow>();
        void Row(string name, string? value)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                rows.Add(new InfoRow(name, Cut(value.Trim())));
            }
        }

        if ((exif.Date(ExifDirectoryBase.TagDateTimeOriginal) ?? exif.Date(ExifDirectoryBase.TagDateTimeDigitized) ?? exif.Date(ExifDirectoryBase.TagDateTime)) is { } taken)
        {
            Row(Strings.Info_Taken, taken.ToString("G", CultureInfo.CurrentCulture));
        }

        Row(Strings.Info_Camera, Camera(exif.Text(ExifDirectoryBase.TagMake), exif.Text(ExifDirectoryBase.TagModel)));
        Row(Strings.Info_Lens, exif.Text(ExifDirectoryBase.TagLensModel));

        var exposure = new List<string>();
        if (exif.Value(ExifDirectoryBase.TagExposureTime) is { } time)
        {
            exposure.Add(Shutter(time));
        }

        if (exif.Value(ExifDirectoryBase.TagFNumber) is { } f)
        {
            exposure.Add(Loc.Format(Strings.Info_Aperture, f.ToString("0.#", CultureInfo.CurrentCulture)));
        }

        if (exif.Number(ExifDirectoryBase.TagIsoEquivalent) is { } iso)
        {
            exposure.Add(Loc.Format(Strings.Info_Iso, iso));
        }

        Row(Strings.Info_Exposure, string.Join(" · ", exposure));
        Row(Strings.Info_FocalLength, exif.Description(ExifDirectoryBase.TagFocalLength));

        if (xmp.TryGetValue("xmp:Rating", out var rating) && int.TryParse(rating, NumberStyles.Integer, CultureInfo.InvariantCulture, out var stars) && stars is > 0 and <= 5)
        {
            Row(Strings.Info_Rating, new string('★', stars) + new string('☆', 5 - stars));
        }

        var keywords = iptc?.GetStringArray(IptcDirectory.TagKeywords) is { Length: > 0 } words
            ? words
            : [.. xmp.Where(p => p.Key.StartsWith("dc:subject[", StringComparison.Ordinal)).Select(p => p.Value)];
        Row(Strings.Info_Keywords, string.Join(", ", keywords.Where(k => !string.IsNullOrWhiteSpace(k))));
        Row(Strings.Info_Caption, iptc?.GetString(IptcDirectory.TagCaption) ?? Xmp(xmp, "dc:description") ?? exif.Text(ExifDirectoryBase.TagImageDescription));
        Row(Strings.Info_Creator, exif.Text(ExifDirectoryBase.TagArtist) ?? iptc?.GetString(IptcDirectory.TagByLine) ?? Xmp(xmp, "dc:creator"));
        Row(Strings.Info_Copyright, exif.Text(ExifDirectoryBase.TagCopyright) ?? iptc?.GetString(IptcDirectory.TagCopyrightNotice) ?? Xmp(xmp, "dc:rights"));
        if (place is { } p)
        {
            Row(Strings.Info_Place, Degrees(p.Lat, p.Lon));
        }

        if (gps is not null && gps.TryGetRational(GpsDirectory.TagAltitude, out var altitude) && altitude.Denominator != 0)
        {
            var metres = altitude.ToDouble() * (gps.TryGetInt32(GpsDirectory.TagAltitudeRef, out var below) && below == 1 ? -1 : 1);
            Row(Strings.Info_Altitude, Loc.Format(Strings.Info_Metres, Math.Round(metres).ToString("N0", CultureInfo.CurrentCulture)));
        }

        return rows;
    }

    internal static string Shutter(double seconds)
        => seconds < 0.5 ? Loc.Format(Strings.Info_Seconds, "1/" + Math.Round(1 / seconds).ToString("0", CultureInfo.CurrentCulture))
            : Loc.Format(Strings.Info_Seconds, seconds.ToString("0.#", CultureInfo.CurrentCulture));

    internal static string? Camera(string? make, string? model)
    {
        make = make?.Trim();
        model = model?.Trim();
        if (string.IsNullOrEmpty(make))
        {
            return model;
        }

        if (string.IsNullOrEmpty(model))
        {
            return make;
        }

        var brand = make.Split(' ', StringSplitOptions.RemoveEmptyEntries)[0];
        return model.Contains(brand, StringComparison.OrdinalIgnoreCase) ? model : $"{make} {model}";
    }

    private sealed class Exif(IReadOnlyList<ExifDirectoryBase> blocks)
    {
        public IReadOnlyList<ExifDirectoryBase> Blocks => blocks;

        public string? Text(int tag) => blocks.Select(b => b.GetString(tag)?.Trim()).FirstOrDefault(s => !string.IsNullOrEmpty(s));

        public string? Description(int tag) => blocks.Where(b => b.ContainsTag(tag)).Select(b => b.GetDescription(tag)).FirstOrDefault(s => !string.IsNullOrEmpty(s));

        public double? Value(int tag) => blocks.FirstOrDefault(b => b.ContainsTag(tag)) is { } b && b.TryGetRational(tag, out var r) && r.Denominator != 0 && r.ToDouble() is > 0 and var v && double.IsFinite(v) ? v : null;

        public int? Number(int tag) => blocks.FirstOrDefault(b => b.ContainsTag(tag)) is { } b && b.TryGetInt32(tag, out var n) ? n : null;

        public DateTime? Date(int tag)
        {
            foreach (var b in blocks)
            {
                if (b.TryGetDateTime(tag, out var d))
                {
                    return d;
                }
            }

            return null;
        }
    }

    public static string Degrees(double latitude, double longitude)
        => string.Create(CultureInfo.InvariantCulture, $"{latitude:0.#####}, {longitude:0.#####}");

    private static string? Xmp(IDictionary<string, string> xmp, string name)
        => xmp.TryGetValue(name, out var v) || xmp.TryGetValue(name + "[1]", out v) ? v : null;

    private static List<InfoRow> Tags(MetadataExtractor.Directory? directory)
    {
        var rows = new List<InfoRow>();
        if (directory is null)
        {
            return rows;
        }

        foreach (var tag in directory.Tags)
        {
            string? value;
            try
            {
                value = tag.Description;
            }
            catch (Exception e) when (e is FormatException or ArgumentException or InvalidCastException or IndexOutOfRangeException or OverflowException)
            {
                continue;
            }

            if (!string.IsNullOrWhiteSpace(value))
            {
                rows.Add(new InfoRow(tag.Name, Cut(value.Trim())));
            }
        }

        return rows;
    }

    private static string Cut(string value) => value.Length <= MaxValue ? value : value[..MaxValue] + "…";
}

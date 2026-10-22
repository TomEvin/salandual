// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : tests/Salandual.Imaging.Tests/PhotoInfoTests.cs

namespace Salandual.Imaging.Tests;

public sealed class PhotoInfoTests
{
    private static string? Samples
    {
        get
        {
            for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
            {
                if (File.Exists(Path.Combine(dir.FullName, "Salandual.slnx")))
                {
                    var samples = Path.Combine(dir.FullName, "_samples", "images");
                    return Directory.Exists(samples) ? samples : null;
                }
            }

            return null;
        }
    }

    [Fact]
    public void CameraRaw_GivesTheSummary_FromWhicheverExifBlockHasIt()
    {
        if (Samples is not { } samples)
        {
            Assert.Skip("The local _samples folder is not here.");
            return;
        }

        var nef = PhotoInfo.Read(Path.Combine(samples, "raw", "Nikon_D750.NEF"));
        var summary = nef.Groups[InfoGroup.Summary].ToDictionary(r => r.Name, r => r.Value);
        Assert.Equal("NIKON D750", summary["Camera"]);
        Assert.Contains("1/30 s", summary["Exposure"], StringComparison.Ordinal);
        Assert.Contains("ISO 100", summary["Exposure"], StringComparison.Ordinal);
        Assert.Equal("35 mm", summary["Focal length"]);
        Assert.Contains(InfoGroup.Exif, nef.Groups.Keys);
        Assert.True(nef.Groups[InfoGroup.All].Count > 100);

        var dng = PhotoInfo.Read(Path.Combine(samples, "raw", "Leica_M10.DNG")).Groups[InfoGroup.Summary].ToDictionary(r => r.Name, r => r.Value);
        Assert.Equal("LEICA M10", dng["Camera"]);
        Assert.Equal("Summicron-M 1:2/35 ASPH.", dng["Lens"]);
    }

    [Fact]
    public void AFileWithoutMetadata_OrNoFileAtAll_GivesEmptyInformation()
    {
        Assert.True(PhotoInfo.Read(Path.Combine(Path.GetTempPath(), "salandual-no-such-photo.jpg")).IsEmpty);
        var path = Path.Combine(Path.GetTempPath(), $"salandual-{Guid.NewGuid():N}.jpg");
        File.WriteAllText(path, "not a photo");
        try
        {
            Assert.True(PhotoInfo.Read(path).IsEmpty);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void AGpsBlock_GivesThePlace_InDecimalDegrees_AndTheAltitude()
    {
        var gps = new MetadataExtractor.Formats.Exif.GpsDirectory();
        gps.Set(MetadataExtractor.Formats.Exif.GpsDirectory.TagLatitudeRef, "N");
        gps.Set(MetadataExtractor.Formats.Exif.GpsDirectory.TagLatitude, new[] { new MetadataExtractor.Rational(46, 1), new MetadataExtractor.Rational(54, 1), new MetadataExtractor.Rational(216, 10) });
        gps.Set(MetadataExtractor.Formats.Exif.GpsDirectory.TagLongitudeRef, "W");
        gps.Set(MetadataExtractor.Formats.Exif.GpsDirectory.TagLongitude, new[] { new MetadataExtractor.Rational(17, 1), new MetadataExtractor.Rational(53, 1), new MetadataExtractor.Rational(348, 10) });
        gps.Set(MetadataExtractor.Formats.Exif.GpsDirectory.TagAltitude, new MetadataExtractor.Rational(112, 1));

        var info = PhotoInfo.From([gps]);

        Assert.NotNull(info.Place);
        Assert.Equal(46.906, info.Place.Value.Latitude, 3);
        Assert.Equal(-17.893, info.Place.Value.Longitude, 3);
        var summary = info.Groups[InfoGroup.Summary].ToDictionary(r => r.Name, r => r.Value);
        Assert.Equal("46.906, -17.893", summary["Place"]);
        Assert.Equal("112 m", summary["Altitude"]);
        Assert.Contains(InfoGroup.Gps, info.Groups.Keys);
        Assert.DoesNotContain(InfoGroup.Iptc, info.Groups.Keys);
    }

    [Theory]
    [InlineData("NIKON CORPORATION", "NIKON D750", "NIKON D750")]
    [InlineData("Canon", "Canon EOS 40D", "Canon EOS 40D")]
    [InlineData("Leica Camera AG", "LEICA M10", "LEICA M10")]
    [InlineData("SONY", "ILCE-7M3", "SONY ILCE-7M3")]
    [InlineData(null, "X100V", "X100V")]
    [InlineData("FUJIFILM", null, "FUJIFILM")]
    public void TheCameraName_DoesNotRepeatTheMaker(string? make, string? model, string expected)
        => Assert.Equal(expected, PhotoInfo.Camera(make, model));

    [Fact]
    public void ShutterTimes_ReadAsCamerasShowThem()
    {
        Assert.Equal("1/250 s", PhotoInfo.Shutter(0.004));
        Assert.Equal("1/50 s", PhotoInfo.Shutter(0.02));
        Assert.Equal("1/3 s", PhotoInfo.Shutter(1.0 / 3));
        Assert.Equal(string.Create(System.Globalization.CultureInfo.CurrentCulture, $"{1.3:0.#} s"), PhotoInfo.Shutter(1.3));
    }
}

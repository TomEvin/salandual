// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : tests/Salandual.Core.Tests/MapLinksTests.cs

using Salandual.Core.Settings;
using Salandual.Core.Viewer;

namespace Salandual.Core.Tests;

public sealed class MapLinksTests
{
    [Fact]
    public void OpenStreetMap_IsTheDefault_WithThePlaceInIt()
    {
        var settings = new ViewerSettings();
        Assert.Equal(MapSite.OpenStreetMap, settings.MapSite);
        Assert.Equal("https://www.openstreetmap.org/?mlat=46.906&mlon=17.893#map=15/46.906/17.893", MapLinks.For(settings, 46.906, 17.893)!.ToString());
    }

    [Fact]
    public void GoogleMaps_AndYourOwnSite_GetThePlace_WithAPointInEveryLanguage()
    {
        var culture = Thread.CurrentThread.CurrentCulture;
        Thread.CurrentThread.CurrentCulture = new System.Globalization.CultureInfo("hu-HU");
        try
        {
            Assert.Equal("https://www.google.com/maps/search/?api=1&query=-33.8568,151.2153", MapLinks.For(new ViewerSettings { MapSite = MapSite.GoogleMaps }, -33.8568, 151.2153)!.ToString());
            var own = new ViewerSettings { MapSite = MapSite.Custom, MapCustomName = "Mapy", MapCustomUrl = "https://mapy.example/?x={lon}&y={lat}&z=16" };
            Assert.Equal("https://mapy.example/?x=19.0402&y=47.4979&z=16", MapLinks.For(own, 47.4979, 19.0402)!.ToString());
        }
        finally
        {
            Thread.CurrentThread.CurrentCulture = culture;
        }
    }

    [Theory]
    [InlineData("https://maps.example/{lat},{lon}", true)]
    [InlineData("http://maps.example/?q={LAT},{LON}", true)]
    [InlineData("https://maps.example/{lat}", false)]
    [InlineData("file:///C:/map.html?{lat},{lon}", false)]
    [InlineData("javascript:alert({lat},{lon})", false)]
    [InlineData("maps.example/{lat},{lon}", false)]
    [InlineData("", false)]
    public void YourOwnSite_MustBeAWebAddress_WithBothPlaces(string template, bool valid)
        => Assert.Equal(valid, MapLinks.IsValidTemplate(template));

    [Fact]
    public void AnInvalidOwnSite_UsesOpenStreetMap_APlaceNotOnEarth_GivesNoLink()
    {
        var bad = new ViewerSettings { MapSite = MapSite.Custom, MapCustomUrl = "file:///{lat}/{lon}" };
        Assert.Equal(MapSite.OpenStreetMap, MapLinks.Effective(bad));
        Assert.StartsWith("https://www.openstreetmap.org/", MapLinks.For(bad, 1, 2)!.ToString(), StringComparison.Ordinal);
        Assert.Equal(MapSite.OpenStreetMap, MapLinks.Effective(new ViewerSettings { MapSite = MapSite.Custom, MapCustomUrl = "https://maps.example/{lat},{lon}" }));
        Assert.Null(MapLinks.For(new ViewerSettings(), 91, 0));
        Assert.Null(MapLinks.For(new ViewerSettings(), double.NaN, 0));
    }
}

// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.Core/Viewer/MapLinks.cs

using System.Globalization;
using Salandual.Core.Settings;

namespace Salandual.Core.Viewer;

public static class MapLinks
{
    public const string OpenStreetMap = "https://www.openstreetmap.org/?mlat={lat}&mlon={lon}#map=15/{lat}/{lon}";

    public const string GoogleMaps = "https://www.google.com/maps/search/?api=1&query={lat},{lon}";

    public static bool IsValidTemplate(string? template)
        => !string.IsNullOrWhiteSpace(template)
           && template.Contains("{lat}", StringComparison.OrdinalIgnoreCase)
           && template.Contains("{lon}", StringComparison.OrdinalIgnoreCase)
           && Uri.TryCreate(Fill(template, 0, 0), UriKind.Absolute, out var uri)
           && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp);

    public static MapSite Effective(ViewerSettings settings)
        => settings.MapSite == MapSite.Custom && (string.IsNullOrWhiteSpace(settings.MapCustomName) || !IsValidTemplate(settings.MapCustomUrl)) ? MapSite.OpenStreetMap : settings.MapSite;

    public static Uri? For(ViewerSettings settings, double latitude, double longitude)
    {
        if (!double.IsFinite(latitude) || !double.IsFinite(longitude) || Math.Abs(latitude) > 90 || Math.Abs(longitude) > 180)
        {
            return null;
        }

        var template = Effective(settings) switch
        {
            MapSite.GoogleMaps => GoogleMaps,
            MapSite.Custom => settings.MapCustomUrl,
            _ => OpenStreetMap,
        };
        return Uri.TryCreate(Fill(template, latitude, longitude), UriKind.Absolute, out var uri) ? uri : null;
    }

    private static string Fill(string template, double latitude, double longitude)
        => template
            .Replace("{lat}", latitude.ToString("0.######", CultureInfo.InvariantCulture), StringComparison.OrdinalIgnoreCase)
            .Replace("{lon}", longitude.ToString("0.######", CultureInfo.InvariantCulture), StringComparison.OrdinalIgnoreCase);
}

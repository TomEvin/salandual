// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.Core/ProductInfo.cs

namespace Salandual.Core;

public static class ProductInfo
{
    public const string DisplayName = "Salandual";
    public const string ShortName = "Salandual";
    public const string Identifier = "Salandual";
    public const string ExecutableName = "salandual.exe";
    public const string PortableFlagFileName = "portable.flag";

    public static string FullVersion { get; } = System.Reflection.Assembly.GetExecutingAssembly()
        .GetCustomAttributes(typeof(System.Reflection.AssemblyInformationalVersionAttribute), false)
        .OfType<System.Reflection.AssemblyInformationalVersionAttribute>().FirstOrDefault()?.InformationalVersion ?? "0";

    public static string Version { get; } = FullVersion.Split('+')[0];

    public static string Commit { get; } = FullVersion.Split('+') is [_, var commit, ..] ? commit[..Math.Min(7, commit.Length)] : "";

    public static DateTime? BuildDate { get; } = System.Reflection.Assembly.GetExecutingAssembly()
        .GetCustomAttributes(typeof(System.Reflection.AssemblyMetadataAttribute), false)
        .OfType<System.Reflection.AssemblyMetadataAttribute>().FirstOrDefault(a => a.Key == "BuildDate")?.Value is { } day
        && DateTime.TryParseExact(day, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var date)
            ? date
            : null;

    public const string RepositoryUrl = "https://github.com/TomEvin/salandual";

    public static string DisplayVersion { get; } = FormatVersion(Version);

    public static string FormatVersion(string version)
    {
        var dash = version.IndexOf('-');
        var number = dash < 0 ? version : version[..dash];
        var suffix = dash < 0 ? "" : version[dash..];
        if (number.EndsWith(".0", StringComparison.Ordinal) && number.Count(c => c == '.') == 2)
        {
            number = number[..^2];
        }

        return "v" + number + suffix;
    }

    public static string GetDataDirectory(string appDirectory, string roamingAppDataDirectory)
    {
        ArgumentException.ThrowIfNullOrEmpty(appDirectory);
        ArgumentException.ThrowIfNullOrEmpty(roamingAppDataDirectory);

        return File.Exists(Path.Combine(appDirectory, PortableFlagFileName))
            ? Path.Combine(appDirectory, "Data")
            : Path.Combine(roamingAppDataDirectory, Identifier);
    }
}

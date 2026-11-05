// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.Core/Settings/CloudFolders.cs

using System.Text.Json;
using System.Text.Json.Nodes;
using Salandual.Core.Resources;

namespace Salandual.Core.Settings;

public sealed record CloudFolder(string Name, string Path, bool IsDrive = false);

public sealed record CloudDriveFacts(string Root, string? Share, string? Label);

public static class CloudFolders
{
    public static IReadOnlyList<CloudFolder> Find(Func<string, string?> env, Func<string, bool> exists, Func<string, string?> readText, IEnumerable<string> driveRoots,
        IEnumerable<(string Folder, bool Business)>? oneDriveAccounts = null, IEnumerable<CloudDriveFacts>? drives = null)
    {
        var found = new List<CloudFolder>();
        void Add(string name, string? path, bool isDrive = false)
        {
            if (!string.IsNullOrWhiteSpace(path) && (isDrive || exists(path)) && !found.Any(f => string.Equals(f.Path.TrimEnd('\\'), path.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase)))
            {
                found.Add(new CloudFolder(name, path, isDrive));
            }
        }

        string OneDriveName(string folder, bool business)
        {
            var name = System.IO.Path.GetFileName(folder.TrimEnd('\\'));
            return !business && string.Equals(name, "OneDrive", StringComparison.OrdinalIgnoreCase) ? Strings.Cloud_OneDrivePersonal
                : name.StartsWith("OneDrive - ", StringComparison.OrdinalIgnoreCase) ? "OneDrive – " + name["OneDrive - ".Length..]
                : business ? "OneDrive – " + name : Strings.Cloud_OneDrivePersonal;
        }

        var consumer = env("OneDriveConsumer");
        var commercial = env("OneDriveCommercial");
        if (consumer is not null)
        {
            Add(OneDriveName(consumer, false), consumer);
        }

        foreach (var (folder, business) in oneDriveAccounts ?? [])
        {
            Add(OneDriveName(folder, business), folder);
        }

        if (commercial is not null)
        {
            Add(OneDriveName(commercial, true), commercial);
        }

        if (env("OneDrive") is { } either)
        {
            Add(OneDriveName(either, string.Equals(either, commercial, StringComparison.OrdinalIgnoreCase)), either);
        }

        foreach (var root in new[] { env("APPDATA"), env("LOCALAPPDATA") })
        {
            if (root is not null && readText(System.IO.Path.Combine(root, "Dropbox", "info.json")) is { } text)
            {
                try
                {
                    if (JsonNode.Parse(text) is JsonObject info)
                    {
                        Add("Dropbox", info["personal"]?["path"]?.GetValue<string>());
                        if (info["business"]?["path"]?.GetValue<string>() is { } business)
                        {
                            var name = System.IO.Path.GetFileName(business.TrimEnd('\\'));
                            Add(string.IsNullOrEmpty(name) || string.Equals(name, "Dropbox", StringComparison.OrdinalIgnoreCase) ? Strings.Cloud_DropboxBusiness : name, business);
                        }
                    }
                }
                catch (Exception ex) when (ex is JsonException or InvalidOperationException or FormatException)
                {
                }
            }
        }

        foreach (var drive in driveRoots)
        {
            Add("Google Drive", System.IO.Path.Combine(drive, "My Drive"));
        }

        var profile = env("USERPROFILE");
        if (profile is not null)
        {
            Add("Google Drive", System.IO.Path.Combine(profile, "My Drive"));
            Add("iCloud Drive", System.IO.Path.Combine(profile, "iCloudDrive"));
            Add("Box", System.IO.Path.Combine(profile, "Box"));
            Add("Syncplicity", System.IO.Path.Combine(profile, "Syncplicity"));
            Add("MEGA", System.IO.Path.Combine(profile, "MEGA"));
            Add("Nextcloud", System.IO.Path.Combine(profile, "Nextcloud"));
        }

        foreach (var drive in drives ?? [])
        {
            if (drive.Share is { } share && share.StartsWith(@"\\Syncplicity\", StringComparison.OrdinalIgnoreCase))
            {
                Add("Syncplicity", drive.Root, isDrive: true);
            }
            else if (string.Equals(drive.Label, "pCloud Drive", StringComparison.OrdinalIgnoreCase))
            {
                Add("pCloud", drive.Root, isDrive: true);
            }
        }

        return found;
    }

    public static IReadOnlyList<CloudFolder> OnThisPc(IEnumerable<(string Folder, bool Business)>? oneDriveAccounts = null, IEnumerable<CloudDriveFacts>? drives = null) => Find(
        Environment.GetEnvironmentVariable,
        Directory.Exists,
        path =>
        {
            try
            {
                return File.Exists(path) && new FileInfo(path).Length < 64 * 1024 ? File.ReadAllText(path) : null;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return null;
            }
        },
        DriveInfo.GetDrives().Where(d => d.DriveType == DriveType.Fixed && SafeReady(d)).Select(d => d.RootDirectory.FullName),
        oneDriveAccounts,
        drives);

    private static bool SafeReady(DriveInfo d)
    {
        try
        {
            return d.IsReady;
        }
        catch (IOException)
        {
            return false;
        }
    }

    public static string SuggestedFolder(CloudFolder cloud) => System.IO.Path.Combine(cloud.Path, "Salandual");
}

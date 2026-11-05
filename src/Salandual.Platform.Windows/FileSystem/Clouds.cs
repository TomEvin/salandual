// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.Platform.Windows/FileSystem/Clouds.cs

using Microsoft.Win32;
using Salandual.Core.Settings;

namespace Salandual.Platform.Windows.FileSystem;

public static class Clouds
{
    public static IReadOnlyList<CloudFolder> OnThisPc(IEnumerable<DriveDetails> drives)
        => CloudFolders.OnThisPc(OneDriveAccounts(), [.. drives.Select(d => new CloudDriveFacts(d.Root, d.Share, d.Label))]);

    private static List<(string Folder, bool Business)> OneDriveAccounts()
    {
        var found = new List<(string, bool)>();
        try
        {
            using var accounts = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\OneDrive\Accounts", writable: false);
            foreach (var name in accounts?.GetSubKeyNames() ?? [])
            {
                using var account = accounts!.OpenSubKey(name, writable: false);
                if (account is not null && account.GetValueNames().Contains("UserFolder", StringComparer.OrdinalIgnoreCase)
                    && account.GetValueKind("UserFolder") == RegistryValueKind.String && account.GetValue("UserFolder") is string folder && folder.Length is > 3 and < 260)
                {
                    found.Add((folder, name.StartsWith("Business", StringComparison.OrdinalIgnoreCase)));
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
        }

        return found;
    }
}

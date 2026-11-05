// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : tests/Salandual.Platform.Windows.Tests/ShellPlacesTests.cs

using System.Buffers.Binary;
using System.Text;
using System.Xml.Linq;
using Salandual.Platform.Windows.Shell;

namespace Salandual.Platform.Windows.Tests;

public sealed class ShellPlacesTests
{
    private static readonly DateTime Deleted = new(2026, 10, 28, 9, 30, 0, DateTimeKind.Utc);

    private static byte[] Index(int version, string path, long size)
    {
        var name = Encoding.Unicode.GetBytes(path + "\0");
        var data = new byte[version == 1 ? 24 + 520 : 28 + name.Length];
        BinaryPrimitives.WriteInt64LittleEndian(data, version);
        BinaryPrimitives.WriteInt64LittleEndian(data.AsSpan(8), size);
        BinaryPrimitives.WriteInt64LittleEndian(data.AsSpan(16), Deleted.ToFileTimeUtc());
        if (version == 1)
        {
            name.CopyTo(data.AsSpan(24));
        }
        else
        {
            BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(24), name.Length / 2);
            name.CopyTo(data.AsSpan(28));
        }

        return data;
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void AnIndexFile_TellsWhereItCameFrom_ItsSize_AndWhen(int version)
    {
        var item = WindowsShellPlaces.ParseIndex(Index(version, @"D:\Work\Reports\old report.pdf", 4096));
        Assert.NotNull(item);
        Assert.Equal(@"D:\Work\Reports\old report.pdf", item.OriginalPath);
        Assert.Equal(4096, item.Size);
        Assert.Equal(Deleted, item.DeletedUtc);
    }

    [Fact]
    public void WhatIsntAnIndexFile_IsLeftOut()
    {
        Assert.Null(WindowsShellPlaces.ParseIndex([1, 2, 3]));
        Assert.Null(WindowsShellPlaces.ParseIndex(Index(3, @"C:\a.txt", 1)));
        var cut = Index(2, @"C:\a long name.txt", 1);
        Assert.Null(WindowsShellPlaces.ParseIndex(cut.AsSpan(0, 34)));
    }

    [Fact]
    public void ALibrary_ListsItsFolders_TheSaveLocationFirst()
    {
        var documents = new Guid("FDD39AD0-238F-46AF-ADB4-6C85480369C7");
        var xml = XDocument.Parse("""
            <libraryDescription xmlns="http://schemas.microsoft.com/windows/2009/library">
              <searchConnectorDescriptionList>
                <searchConnectorDescription><simpleLocation><url>E:\Shared docs</url></simpleLocation></searchConnectorDescription>
                <searchConnectorDescription><isDefaultSaveLocation>true</isDefaultSaveLocation><simpleLocation><url>knownfolder:{FDD39AD0-238F-46AF-ADB4-6C85480369C7}</url></simpleLocation></searchConnectorDescription>
                <searchConnectorDescription><simpleLocation><url>knownfolder:{00000000-0000-0000-0000-000000000001}</url></simpleLocation></searchConnectorDescription>
                <searchConnectorDescription><simpleLocation><url>relative\path</url></simpleLocation></searchConnectorDescription>
              </searchConnectorDescriptionList>
            </libraryDescription>
            """);
        var folders = WindowsShellPlaces.LibraryLocations(xml, id => id == documents ? @"C:\Users\Me\Documents" : null).ToList();
        Assert.Equal([@"C:\Users\Me\Documents", @"E:\Shared docs"], folders);
    }
}

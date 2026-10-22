// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : tests/Salandual.Platform.Windows.Tests/PathInputTests.cs

using Salandual.Platform.Windows.FileSystem;

namespace Salandual.Platform.Windows.Tests;

public sealed class PathInputTests : IDisposable
{
    private const string Base = @"C:\Users\user\Projects";
    private readonly string _dir = Directory.CreateTempSubdirectory("salandual-cd-").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    [Theory]
    [InlineData("..", @"C:\Users\user")]
    [InlineData(@"..\..\..\..\..", @"C:\")]
    [InlineData("salandual", @"C:\Users\user\Projects\salandual")]
    [InlineData(@"salandual\src\.", @"C:\Users\user\Projects\salandual\src")]
    [InlineData(@"\Windows", @"C:\Windows")]
    [InlineData("d:", @"D:\")]
    [InlineData("d:/Backup/2026/", @"D:\Backup\2026")]
    [InlineData(@"  ""C:\Program Files""  ", @"C:\Program Files")]
    [InlineData(@"C:\a\.\b\..\c", @"C:\a\c")]
    [InlineData(@"C:\name.", @"C:\name.")]
    [InlineData(@"\\nas\photos\..\..", @"\\nas\photos")]
    [InlineData(@"\\nas\photos\2026", @"\\nas\photos\2026")]
    [InlineData(@"\\nas", @"\\nas")]
    public void Resolve(string typed, string expected)
        => Assert.Equal(expected, PathInput.Resolve(typed, Base));

    [Fact]
    public void Resolve_ExpandsVariables_AndHome()
    {
        Environment.SetEnvironmentVariable("SALANDUAL_CD_TEST", @"D:\Media");
        Assert.Equal(@"D:\Media\Music", PathInput.Resolve(@"%SALANDUAL_CD_TEST%\Music", Base));
        Assert.Equal(@"D:\Media", PathInput.Resolve("$[SALANDUAL_CD_TEST]", Base));
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        Assert.Equal(Path.Join(home, "x"), PathInput.Resolve(@"~\x", Base));
        Assert.Null(PathInput.Resolve("   ", Base));
    }

    [Theory]
    [InlineData(@"C:\Us", @"C:\", "Us")]
    [InlineData(@"C:\Users\", @"C:\Users", "")]
    [InlineData("C:", @"C:\", "")]
    [InlineData("mark", @"C:\Users\user\Projects", "mark")]
    [InlineData("..", @"C:\Users\user", "")]
    public void ForCompletion(string typed, string folder, string prefix)
        => Assert.Equal((folder, prefix), PathInput.ForCompletion(typed, Base));

    [Fact]
    public void Complete_FoldersStartingWithTheTypedName_AnyCase_Sorted()
    {
        foreach (var name in new[] { "Alpha", "alpine", "Beta", "Álmos" })
        {
            Directory.CreateDirectory(Path.Join(_dir, name));
        }

        File.WriteAllText(Path.Join(_dir, "alp.txt"), "a file is not offered");
        var hidden = Directory.CreateDirectory(Path.Join(_dir, "alpHidden"));
        hidden.Attributes |= FileAttributes.Hidden;

        Assert.Equal([Path.Join(_dir, "Alpha"), Path.Join(_dir, "alpine")], PathInput.Complete(_dir, "ALP", showHidden: false, max: 8));
        Assert.Equal(3, PathInput.Complete(_dir, "alp", showHidden: true, max: 8).Count);
        Assert.Single(PathInput.Complete(_dir, "", showHidden: false, max: 1));
        Assert.Empty(PathInput.Complete(Path.Join(_dir, "missing"), "", showHidden: false, max: 8));
    }

    [Fact]
    public void Probe_FolderFileOrMissing_InTheCaseOnDisk()
    {
        var folder = Directory.CreateDirectory(Path.Join(_dir, "Nyaralás 2026")).FullName;
        var file = Path.Join(folder, "IMG_0412.HEIC");
        File.WriteAllText(file, "x");

        var typed = PathInput.Probe(Path.Join(_dir, "NYARALÁS 2026"));
        Assert.Equal(PathInput.Kind.Folder, typed.Kind);
        Assert.Equal("Nyaralás 2026", Path.GetFileName(typed.Path));
        Assert.Equal(PathInput.Kind.File, PathInput.Probe(file).Kind);
        Assert.Equal("IMG_0412.HEIC", Path.GetFileName(PathInput.Probe(Path.Join(folder, "img_0412.heic")).Path));
        Assert.Equal(PathInput.Kind.Missing, PathInput.Probe(Path.Join(_dir, "nope")).Kind);
    }
}

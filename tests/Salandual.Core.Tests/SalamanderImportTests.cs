// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : tests/Salandual.Core.Tests/SalamanderImportTests.cs

using System.Text;
using Salandual.Core.Settings;
using Salandual.Core.Settings.Salamander;
using Salandual.Core.UserMenu;

namespace Salandual.Core.Tests;

public sealed class SalamanderImportTests : IDisposable
{
    private const string Root = @"[HKEY_CURRENT_USER\Software\Altap\Altap Salamander 4.0";

    private readonly string _dir = Directory.CreateTempSubdirectory("salandual-salimp-").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private static string Reg(string body) => "REGEDIT4\r\n\r\n" + body.Replace("[S", Root, StringComparison.Ordinal);

    private static readonly string _sample = Reg("""
        [S]

        [S\Configuration]
        "Use Shift For GoTo HotPath"=dword:00000001
        "Sort Detects Numbers"=dword:00000000
        "Select/Deselect Directories"=dword:00000001
        "Full Row Highlight"=dword:00000000
        "Hot Paths Bar"=dword:00000000
        "Show Drive Bar"=dword:00000000
        "Show Drive Bar2"=dword:00000001
        "Use Recycle Bin"=dword:00000002
        "Use Recycle Bin For"="*.txt;*.doc"
        "Compare By Content"=dword:00000001
        "Only One Instance"=dword:00000000

        [S\Configuration\Confirmation]
        "Non-empty Dir Del"=dword:00000000
        "Drag and Drop"=dword:00000001

        [S\Configuration\Copy History]
        "1"="D:\\Made up\\Target"
        "2"="E:\\Another"

        [S\Hot Paths\1]
        "Name"="Downloads"
        "Path"="C:\\Users\\Someone\\Downloads"
        "Visible"=dword:00000001

        [S\Hot Paths\2]
        "Name"="Projects"
        "Path"="D:\\Projects"
        "Visible"=dword:00000000

        [S\Hot Paths\3]
        "Name"="Music again"
        "Path"="E:\\Music"

        [S\Colors\Panel Items Hilighting\1]
        "Masks"="*.*"
        "Attributes"=dword:00000800
        "Valid Attributes"=dword:00000800
        "Item Fg Normal"=dword:00ff0000

        [S\Colors\Panel Items Hilighting\2]
        "Masks"="*.bak;*.tmp"
        "Attributes"=dword:00000000
        "Valid Attributes"=dword:00000000
        "Item Fg Normal"=dword:00008000

        [S\Plugins\1]
        "Name"="PictView"
        "DLL"="pictview\\pictview.spl"

        [S\Plugins\2]
        "Name"="DB Viewer"
        "DLL"="dbviewer\\dbviewer.spl"

        [S\Viewers\1]
        "Masks"="*.cr2;*.jpg;*.example"
        "Type"=dword:ffffffff

        [S\Viewers\2]
        "Masks"="*.dbf"
        "Type"=dword:fffffffe

        [S\Viewers\3]
        "Masks"="*.log"
        "Command"="C:\\Tools\\viewer.exe"
        "Type"=dword:00000000

        [S\Editors\1]
        "Masks"="*.*"
        "Command"="C:\\Program Files\\Some Editor\\edit.exe"
        "Arguments"="\"$(FullName)\""

        [S\User Menu\1]
        "Item Name"="Play"
        "Command"="C:\\Player\\player.exe"
        "Arguments"="$(FullName)"
        "Execute Through Shell"=dword:00000001
        "Type"=dword:00000000

        [S\User Menu\2]
        "Item Name"="Tools"
        "Type"=dword:00000001

        [S\User Menu\3]
        "Item Name"="Count lines"
        "Command"="C:\\Tools\\wc.exe"
        "Type"=dword:00000000

        [S\User Menu\4]
        "Type"=dword:00000002

        [S\Plugins Configuration\FTP\Bookmarks\1]
        "Name"="Example site"
        "Address"="ftp.example.com"
        "User"="someone"
        "PasswordS"=hex:01,02,03,04
        "Save Password"=dword:00000001
        "Port"=dword:00000015

        [S\Plugins Configuration\FTP\Bookmarks\2]
        "Name"="Mirror"
        "Address"="mirror.example.org"
        "Anonymous"=dword:00000001
        "Port"=dword:00000801
        "Passive Mode"=dword:00000000
        "Initial Path"="/pub"

        [S\Plugins Configuration\FTP\Bookmarks\3]
        "Name"="Broken"
        "Address"="someone:secret@host.example"

        [S\Password Manager]
        "Master Password"="must never be read"

        [S\Packers & Unpackers]
        "Anything"=dword:00000001
        """);

    private static AppSettings Mine() => new()
    {
        HotPaths = new HotPathSettings { Paths = [new HotPath(3, "Music", @"F:\Music")] },
        History = new HistorySettings { CopyTargets = [@"C:\Mine"] },
    };

    private static SettingsImport Read(AppSettings? current = null)
    {
        var (name, root) = RegFile.Parse(_sample);
        return SalamanderImport.Read(root, name + " on this PC", current ?? Mine());
    }

    [Fact]
    public void Parse_FindsTheSalamander_TakesTextsAndNumbers_AndNeverPasswords()
    {
        var (name, root) = RegFile.Parse(_sample);
        Assert.Equal("Altap Salamander 4.0", name);
        Assert.Equal(@"D:\Made up\Target", root.Key(@"Configuration\Copy History")!.Text("1"));
        Assert.Equal(1u, root.Key("Configuration")!.Number("Use Shift For GoTo HotPath"));
        Assert.Equal(0xFFFFFFFFu, root.Key(@"Viewers\1")!.Number("Type"));
        Assert.Equal("\"$(FullName)\"", root.Key(@"Editors\1")!.Text("Arguments"));
        Assert.Null(root.Key("Password Manager"));
        Assert.False(root.Key(@"Plugins Configuration\FTP\Bookmarks\1")!.Values.ContainsKey("PasswordS"));
        Assert.Equal(1u, root.Key(@"Plugins Configuration\FTP\Bookmarks\1")!.Number("Save Password"));
    }

    [Fact]
    public void Read_TakesVersion5InUtf16_AndRefusesWhatIsntSalamanders()
    {
        var file = Path.Combine(_dir, "v5.reg");
        var text = "Windows Registry Editor Version 5.00\r\n\r\n" + Root + "\\Hot Paths\\1]\r\n\"Name\"=\"Ünnepek\"\r\n\"Path\"=\"C:\\\\Ünnepek\"\r\n";
        File.WriteAllBytes(file, [0xFF, 0xFE, .. Encoding.Unicode.GetBytes(text)]);
        var (_, root) = RegFile.Read(file);
        Assert.Equal(@"C:\Ünnepek", root.Key(@"Hot Paths\1")!.Text("Path"));

        Assert.Throws<InvalidDataException>(() => RegFile.Parse("not a reg file"));
        Assert.Throws<InvalidDataException>(() => RegFile.Parse("REGEDIT4\r\n[HKEY_CURRENT_USER\\Software\\Other]\r\n\"A\"=\"b\"\r\n"));
    }

    [Fact]
    public void Parse_JoinsContinuedLines_AndExpandStrings()
    {
        var (_, root) = RegFile.Parse(Reg("[S\\Hot Paths\\1]\r\n\"Path\"=hex(2):43,3a,5c,\\\r\n  41,00\r\n\"Name\"=\"A\"\r\n"));
        Assert.Equal(@"C:\A", root.Key(@"Hot Paths\1")!.Text("Path"));
        Assert.Equal("A", root.Key(@"Hot Paths\1")!.Text("Name"));
    }

    [Fact]
    public void HotPaths_GoIntoTheirSlots_ASlotOfYoursStaysYours()
    {
        var import = Read();
        var after = import.Apply(new HashSet<SettingsPart> { SettingsPart.HotPaths });
        Assert.Equal(@"C:\Users\Someone\Downloads", after.HotPaths[1]!.Path);
        Assert.False(after.HotPaths[2]!.InStrip);
        Assert.Equal(@"F:\Music", after.HotPaths[3]!.Path);
        Assert.Equal(@"E:\Music", after.HotPaths[4]!.Path);
        Assert.True(after.HotPaths.ShiftDigits);
        Assert.Contains(import.Changes, c => c.Part == SettingsPart.HotPaths && c.Name == "Ctrl+4" && c.New.Contains("Music again", StringComparison.Ordinal) && c.New.Contains("Music", StringComparison.Ordinal));
    }

    [Fact]
    public void Panels_Layout_Confirmations_Compare_General_AreTicked_ProgramsHistoryAndTheSurprisingOnesAreNot()
    {
        var import = Read();
        Assert.Equal("Altap Salamander 4.0 on this PC", import.Source);
        Assert.Contains(SettingsPart.Unusual, import.Parts);
        Assert.Equal(new HashSet<SettingsPart> { SettingsPart.Programs, SettingsPart.History, SettingsPart.Unusual }, import.OffAtFirst);

        var ticked = import.Parts.Where(p => !import.OffAtFirst.Contains(p)).ToHashSet();
        var after = import.Apply(ticked);
        Assert.False(after.Panels.NumericSort);
        Assert.True(after.Panels.SelectFoldersWithNumPlus);
        Assert.Equal(MarkLook.NameOnly, after.Panels.FocusLook);
        Assert.False(after.Layout.Bars.HotPathBar);
        Assert.Equal(StripMode.PerPanel, after.Layout.Strip);
        Assert.False(after.Confirmations.DeleteNonEmptyFolder);
        Assert.True(after.Confirmations.DragDropMenu);
        Assert.True(after.Compare.Directories.Content);
        Assert.False(after.General.SingleInstance);

        Assert.True(after.Operations.RecycleByDefault);
        Assert.Empty(after.UserMenu.Items);
        Assert.Null(after.Editors.Masks.Single(m => m.Mask == "*.*").Program);
        Assert.Equal([@"C:\Mine"], after.History.CopyTargets);
    }

    [Fact]
    public void RecycleBinOnlyForSomeNames_IsWarned_AndTakenOnlyWhenTicked()
    {
        var import = Read();
        var change = Assert.Single(import.Changes, c => c.Part == SettingsPart.Unusual);
        Assert.Contains("*.txt;*.doc", change.Name, StringComparison.Ordinal);
        Assert.False(import.Apply(new HashSet<SettingsPart> { SettingsPart.Unusual }).Operations.RecycleByDefault);
    }

    [Fact]
    public void Programs_EditorAndUserMenu_OnlyWhenTicked_NeverThroughAShell()
    {
        var import = Read();
        var programs = import.Changes.Where(c => c.Part == SettingsPart.Programs).ToList();
        Assert.Contains(programs, c => c.New == "\"C:\\Program Files\\Some Editor\\edit.exe\" \"$(FullName)\"");
        Assert.Contains(programs, c => c.Name.Contains("Play", StringComparison.Ordinal) && c.New == @"C:\Player\player.exe");

        var after = import.Apply(new HashSet<SettingsPart> { SettingsPart.Programs });
        Assert.Equal("\"C:\\Program Files\\Some Editor\\edit.exe\" \"$(FullName)\"", after.Editors.Masks.Single(m => m.Mask == "*.*").Program);
        var play = after.UserMenu.Items[0];
        Assert.Equal((UserMenuItemKind.Program, @"C:\Player\player.exe", "$(FullName)"), (play.Kind, play.Program, play.Arguments));
        var tools = after.UserMenu.Items[1];
        Assert.Equal(UserMenuItemKind.Submenu, tools.Kind);
        Assert.Equal("Count lines", Assert.Single(tools.Items).Name);
    }

    [Fact]
    public void FtpBookmarks_ComeWithoutPasswords_AnAddressWithOneIsLeftOut()
    {
        var import = Read();
        var after = import.Apply(new HashSet<SettingsPart> { SettingsPart.HotPaths });
        var site = after.Servers.Bookmarks.Single(b => b.Name == "Example site");
        Assert.Equal((ServerKind.Ftp, "ftp.example.com", 0, "someone"), (site.Kind, site.Host, site.Port, site.User));
        var mirror = after.Servers.Bookmarks.Single(b => b.Name == "Mirror");
        Assert.Equal(("anonymous", 2049, "/pub", DataConnection.Active), (mirror.User, mirror.Port, mirror.StartIn, mirror.Connection!.Data));
        Assert.DoesNotContain(after.Servers.Bookmarks, b => b.Name == "Broken");
        Assert.Contains(import.LeftOut, l => l.Contains("FTP bookmark", StringComparison.Ordinal));
        Assert.Contains(import.LeftOut, l => l.Contains("never read", StringComparison.Ordinal));
    }

    [Fact]
    public void Viewers_PictViewGoesToTheImageViewer_TheRestIsLeftOut()
    {
        var import = Read();
        var after = import.Apply(new HashSet<SettingsPart> { SettingsPart.Viewers });
        var row = after.Viewers.Masks[0];
        Assert.Equal(("*.example", ViewerKind.Image), (row.Mask, row.View));
        Assert.Contains(import.LeftOut, l => l.Contains("DB Viewer", StringComparison.Ordinal));
        Assert.Contains(import.LeftOut, l => l.Contains("1 viewer that is a program", StringComparison.Ordinal));
        Assert.Contains(import.LeftOut, l => l.Contains("plugins' own settings", StringComparison.Ordinal));
        Assert.Contains(import.LeftOut, l => l.Contains("packers", StringComparison.Ordinal));
    }

    [Fact]
    public void HighlightRules_OnlyThoseWithAColourOfTheirOwn_NotTwiceForCompressed()
    {
        var import = Read();
        var after = import.Apply(new HashSet<SettingsPart> { SettingsPart.Panels });
        var rules = after.Highlighting.Effective;
        Assert.Single(rules, r => r.MustHave == FileAttributes.Compressed);
        var bak = Assert.Single(rules, r => r.Masks == "*.bak;*.tmp");
        Assert.Equal((NameColour.Own, "#008000"), (bak.Colour, bak.OwnLight));
    }

    [Fact]
    public void History_IsMergedAfterYours_WhenTicked()
    {
        var after = Read().Apply(new HashSet<SettingsPart> { SettingsPart.History });
        Assert.Equal([@"C:\Mine", @"D:\Made up\Target", @"E:\Another"], after.History.CopyTargets);
    }

    [Fact]
    public void Read_TheSameSettingsTwice_ChangesNothing()
    {
        var import = Read();
        var after = import.Apply(import.Parts.ToHashSet());
        var again = SalamanderImport.Read(RegFile.Parse(_sample).Root, "again", after);
        Assert.DoesNotContain(again.Changes, c => c.Part is SettingsPart.HotPaths or SettingsPart.Viewers or SettingsPart.Programs);
    }
}

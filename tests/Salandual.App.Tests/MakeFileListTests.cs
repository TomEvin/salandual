// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : tests/Salandual.App.Tests/MakeFileListTests.cs

using System.Text;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Salandual.App.Dialogs;
using Salandual.Core.Settings;
using Salandual.Platform.Windows.Shell;

namespace Salandual.App.Tests;

public sealed class MakeFileListTests : IDisposable
{
    private static readonly DateTime Noon = new(2026, 10, 10, 12, 0, 0, DateTimeKind.Utc);
    private readonly string _dir = Directory.CreateTempSubdirectory("salandual-list-").FullName;

    public MakeFileListTests()
    {
        Write("README.md", "read me");
        Write("CHANGELOG.md", "changes, more of them");
        Directory.CreateDirectory(Path.Join(_dir, "assets"));
        Write(Path.Join("assets", "site.css"), "body{}");
        Write(Path.Join("assets", "logo.svg"), "<svg/>");
    }

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private void Write(string name, string text)
    {
        var path = Path.Join(_dir, name);
        File.WriteAllText(path, text);
        File.SetLastWriteTimeUtc(path, Noon);
    }

    private async Task<MainWindow> Open(params string[] select)
    {
        var w = new MainWindow { Width = 1100, Height = 700 };
        w.Show();
        w.Services.Settings.Update(s => s with { FileList = new() });
        w.FileClipboardAccess = new TextClipboard();
        await w.LeftPanel.Panel.NavigateAsync(_dir);
        w.LeftPanel.Panel.SelectWhere(e => select.Contains(e.Name), select: true);
        w.LeftPanel.View.Focus();
        Dispatcher.UIThread.RunJobs();
        return w;
    }

    private static async Task Until(Func<bool> condition)
    {
        for (var i = 0; i < 500 && !condition(); i++)
        {
            await Task.Delay(10);
            Dispatcher.UIThread.RunJobs();
        }

        Assert.True(condition());
    }

    private static async Task<MakeFileListDialog> Dialog(MainWindow w)
    {
        Assert.True(w.Run("File.MakeList", Salandual.Core.Commands.PanelTarget.Active));
        await Until(() => w.FileListDialog is not null);
        return w.FileListDialog!;
    }

    private static string[] Lit(MakeFileListDialog d) => [.. d.Chips.Where(c => c.Lit).Select(c => c.Name)];

    [AvaloniaFact]
    public async Task CtrlM_ListsTheSelection_AndTheNamesGoToTheClipboard()
    {
        var w = await Open("README.md", "assets");
        var d = await Dialog(w);
        Assert.Equal(["Names"], Lit(d));
        Assert.Equal("assets\nREADME.md\n", d.PreviewText);
        Assert.Equal("(all 2)", d.PreviewNote);
        Assert.True(d.ToClipboard.IsChecked);
        Assert.False(d.IncludeBox.IsChecked);
        Assert.True(d.IncludeBox.IsEnabled);

        await d.AcceptAsync();
        await Until(() => w.FileListDialog is null);
        Assert.Equal("assets\r\nREADME.md\r\n", ((TextClipboard)w.FileClipboardAccess).Text);
        Assert.Equal("The list of 2 items is on the clipboard.", w.NoticeMessage);
        w.CloseNow();
    }

    [AvaloniaFact]
    public async Task WhatIsInsideTheFolders_TheVariables_AndTheirWidths()
    {
        var w = await Open("assets", "CHANGELOG.md");
        var d = await Dialog(w);
        d.IncludeBox.IsChecked = true;
        await Until(() => !d.IsCounting);
        Assert.Equal(4, d.Items.Count);
        Assert.Equal("assets\nlogo.svg\nsite.css\nCHANGELOG.md\n", d.PreviewText);

        d.LineBox.Text = "$(FileName:max)|$(FileSize)|$(FileExtension)$(CRLF)";
        Assert.Equal(["assets      |Folder|", "logo.svg    |6|svg", "site.css    |6|css", "CHANGELOG.md|21|md", ""], d.PreviewText.Split('\n'));
        Assert.Empty(Lit(d));

        d.FirstBox.Text = "Name$(TAB)Size";
        Assert.StartsWith("Name→   Size\n", d.PreviewText, StringComparison.Ordinal);

        d.LineBox.Text = "$(FileName)";
        d.LineBox.CaretIndex = 0;
        d.Insert("$(TAB)");
        Assert.Equal("$(TAB)$(FileName)", d.LineBox.Text);
        Assert.Equal(6, d.LineBox.CaretIndex);
        d.Insert("$[]", back: 1);
        Assert.Equal("$(TAB)$[]$(FileName)", d.LineBox.Text);
        Assert.Equal(8, d.LineBox.CaretIndex);
        var menu = Assert.IsType<Avalonia.Controls.MenuFlyout>(d.InsertButton.Flyout);
        Assert.Equal("$(FileName)", menu.Items.OfType<Avalonia.Controls.MenuItem>().First(i => i.Tag is not null).Tag);
        d.Close();
        w.CloseNow();
    }

    [AvaloniaFact]
    public async Task FilesNamed_PicksTheFiles_FoldersOnlyWhenAsked()
    {
        var w = await Open("assets", "CHANGELOG.md", "README.md");
        var d = await Dialog(w);
        Assert.False(d.FoldersTooBox.IsEnabled);
        d.IncludeBox.IsChecked = true;
        await Until(() => !d.IsCounting);
        d.MaskBox.Text = "*.md;*.css";
        Assert.Equal("site.css\nCHANGELOG.md\nREADME.md\n", d.PreviewText);
        Assert.Equal("(all 3)", d.PreviewNote);
        Assert.True(d.FoldersTooBox.IsEnabled);
        d.FoldersTooBox.IsChecked = true;
        Assert.Equal("assets\nsite.css\nCHANGELOG.md\nREADME.md\n", d.PreviewText);

        d.FoldersTooBox.IsChecked = false;
        d.MaskBox.Text = "|*.md";
        Assert.Equal("logo.svg\nsite.css\n", d.PreviewText);

        d.MaskBox.Text = "*.txt";
        Assert.Equal("Nothing here is named so.", d.PreviewNote);
        Assert.False(d.Make.IsEnabled);

        d.MaskBox.Text = "*.md";
        d.FirstBox.Text = "$(Count) files$(CRLF)";
        d.LineBox.Text = "$(Number). $(RelativeName) $(Type)$(CRLF)";
        Assert.Equal("2 files\n1. CHANGELOG.md File\n2. README.md File\n", d.PreviewText);
        await d.AcceptAsync();
        await Until(() => w.FileListDialog is null);
        Assert.Equal("2 files\r\n1. CHANGELOG.md File\r\n2. README.md File\r\n", ((TextClipboard)w.FileClipboardAccess).Text);
        Assert.Equal("*.md", w.Services.Settings.Current.FileList.Mask);
        w.CloseNow();
    }

    [AvaloniaFact]
    public async Task TheVariablesMenu_OneListInGroups()
    {
        var w = await Open("README.md");
        var d = await Dialog(w);
        var menu = Assert.IsType<Avalonia.Controls.MenuFlyout>(d.InsertButton.Flyout);
        var items = menu.Items.OfType<Avalonia.Controls.MenuItem>().ToList();
        Assert.Equal(["Name", "Folder", "Size", "Date and time", "Other", "Line breaks and tabs", "Environment"], items.Where(i => !i.IsEnabled).Select(i => i.Header as string));
        Assert.All(items.Where(i => !i.IsEnabled), i => Assert.Null(i.Tag));
        var tags = items.Select(i => i.Tag as string).OfType<string>().ToList();
        foreach (var v in Salandual.Core.Listing.FileListLine.Variables)
        {
            Assert.Contains("$(" + v + ")", tags);
        }

        Assert.Contains("$[USERNAME]", tags);
        Assert.Contains("$[COMPUTERNAME]", tags);
        string Words(Avalonia.Controls.MenuItem i) => string.Join("|", ((Avalonia.Controls.StackPanel)i.Header!).Children.OfType<Avalonia.Controls.TextBlock>().Select(t => t.Text));
        Assert.Equal("$(RelativeName)|From the panel's folder: assets\\logo.svg", Words(items.First(i => (string?)i.Tag == "$(RelativeName)")));
        Assert.Contains("vars", menu.FlyoutPresenterClasses);

        d.LineBox.Text = "";
        d.Insert("$(FileDOSName)|$(FileSizeShort)|$(FolderName)");
        Assert.Matches(@"^README(\.md|~\d\.MD)\|7 B\|salandual-list-[\w.]+$", d.PreviewText);
        d.Close();
        w.CloseNow();
    }

    [AvaloniaFact]
    public async Task ALineThatCantBeRead_IsNamed_AndNothingIsMade()
    {
        var w = await Open("README.md");
        var d = await Dialog(w);
        d.LineBox.Text = "$(FileNme)$(CRLF)";
        Assert.Equal("There is no variable $(FileNme).", d.LineError);
        Assert.False(d.Make.IsEnabled);
        Assert.Equal("", d.PreviewText);
        d.LineBox.Text = "$(FileName)";
        Assert.True(d.Make.IsEnabled);

        d.FirstBox.Text = "$(FileName)";
        Assert.Equal("The first line can't name a file: only $(Count), $(TAB), $(CRLF), $(LF) and $(CR).", d.FirstError);
        Assert.False(d.Make.IsEnabled);
        d.Close();
        w.CloseNow();
    }

    [AvaloniaFact]
    public async Task TheReadyMadeLines_BuiltInAndYours()
    {
        var w = await Open("README.md");
        var d = await Dialog(w);
        Assert.Equal(["Names", "Full names", "Name, size, date", "CSV", "Markdown table"], d.Chips.Where(c => !c.Mine).Select(c => c.Name));
        d.Pick("CSV");
        Assert.Equal(["CSV"], Lit(d));
        Assert.Equal("Name;Size;Date;Folder", d.FirstBox.Text);
        Assert.StartsWith("Name;Size;Date;Folder\n\"README.md\";7;", d.PreviewText, StringComparison.Ordinal);
        Assert.False(d.SaveButton.IsEnabled);

        d.LineBox.Text = "\"$(FileName)\";$(FileSize)$(CRLF)";
        Assert.Empty(Lit(d));
        Assert.True(d.SaveButton.IsEnabled);
        d.OpenNameRow(null);
        d.NameBox.Text = "csv";
        d.SaveName();
        Assert.Equal("“csv” is the name of a built-in template.", d.NameError);
        d.NameBox.Text = "";
        d.SaveName();
        Assert.Equal("Type a name.", d.NameError);
        d.NameBox.Text = "Short CSV";
        d.SaveName();
        Assert.Equal(["Short CSV"], Lit(d));
        Assert.Equal("Saved", d.SaveButton.Content);
        Assert.False(d.SaveButton.IsEnabled);
        var mine = Assert.Single(w.Services.Settings.Current.FileList.Mine);
        Assert.Equal(new Core.Listing.FileListTemplate("Short CSV", "Name;Size;Date;Folder", "\"$(FileName)\";$(FileSize)$(CRLF)"), mine);

        d.LineBox.Text = "$(FileName);$(CRLF)";
        Assert.Empty(Lit(d));
        d.OpenNameRow(null);
        Assert.Equal("", d.NameBox.Text);
        d.NameBox.Text = "Short CSV";
        d.SaveName();
        Assert.Equal("$(FileName);$(CRLF)", Assert.Single(w.Services.Settings.Current.FileList.Mine).Line);

        d.OpenNameRow("Short CSV");
        Assert.Equal("Short CSV", d.NameBox.Text);
        d.NameBox.Text = "For the wiki";
        d.SaveName();
        Assert.Equal("For the wiki", Assert.Single(w.Services.Settings.Current.FileList.Mine).Name);
        Assert.Contains(d.Chips, c => c.Mine && c.Name == "For the wiki");
        var removing = d.RemoveMineAsync("For the wiki");
        await Until(() => d.RemoveQuestion is not null);
        Assert.Equal("Remove the template?", d.RemoveQuestion!.Title);
        Assert.StartsWith("Are you sure you want to remove this saved template: For the wiki?", d.RemoveQuestion.Message);
        d.RemoveQuestion.No.RaiseEvent(new RoutedEventArgs(Avalonia.Controls.Button.ClickEvent));
        await removing;
        Assert.Single(w.Services.Settings.Current.FileList.Mine);
        removing = d.RemoveMineAsync("For the wiki");
        await Until(() => d.RemoveQuestion is not null);
        d.RemoveQuestion!.Yes.RaiseEvent(new RoutedEventArgs(Avalonia.Controls.Button.ClickEvent));
        await removing;
        Assert.Empty(w.Services.Settings.Current.FileList.Mine);
        Assert.DoesNotContain(d.Chips, c => c.Mine);
        d.Close();
        w.CloseNow();
    }

    [AvaloniaFact]
    public async Task ToAFile_Replaced_OrAddedToItsEnd()
    {
        var w = await Open("README.md");
        var d = await Dialog(w);
        d.ToFile.IsChecked = true;
        d.FileBox.Text = "list.txt";
        await d.AcceptAsync();
        var path = Path.Join(_dir, "list.txt");
        await Until(() => w.FileListDialog is null && File.Exists(path));
        Assert.Equal([0xEF, 0xBB, 0xBF], File.ReadAllBytes(path)[..3]);
        Assert.Equal("README.md\r\n", File.ReadAllText(path));
        await Until(() => w.NoticeMessage == "The list of 1 item is in list.txt.");

        d = await Dialog(w);
        Assert.True(d.ToFile.IsChecked);
        Assert.Equal("list.txt", d.FileBox.Text);
        d.AppendBox.IsChecked = true;
        await d.AcceptAsync();
        await Until(() => w.FileListDialog is null);
        await Until(() => File.ReadAllText(path) == "README.md\r\nREADME.md\r\n");

        d = await Dialog(w);
        d.AppendBox.IsChecked = false;
        var accept = d.AcceptAsync();
        await Until(() => d.ReplaceQuestion is not null);
        d.ReplaceQuestion!.Yes.RaiseEvent(new RoutedEventArgs(Avalonia.Controls.Button.ClickEvent));
        await accept;
        await Until(() => w.FileListDialog is null);
        await Until(() => File.ReadAllText(path) == "README.md\r\n");

        d = await Dialog(w);
        d.FileBox.Text = Path.Join(_dir, "nope", "list.txt");
        await d.AcceptAsync();
        Assert.Equal($"The folder {Path.Join(_dir, "nope")} isn't there.", d.Problem);
        Assert.Same(d, w.FileListDialog);
        d.Close();
        w.CloseNow();
    }

    [AvaloniaFact]
    public async Task ToTheViewer_FromAFileThatGoesWithIt()
    {
        var w = await Open("README.md", "CHANGELOG.md");
        var d = await Dialog(w);
        d.ToViewer.IsChecked = true;
        await d.AcceptAsync();
        await Until(() => w.Viewers.Count == 1);
        var path = w.LastFileListView!;
        Assert.Equal("File list.txt", Path.GetFileName(path));
        Assert.Equal("CHANGELOG.md\r\nREADME.md\r\n", File.ReadAllText(path, Encoding.UTF8));

        var viewer = w.Viewers[0];
        viewer.Close();
        await Until(() => w.ListCloseQuestion is not null);
        Assert.Equal("Save the list?", w.ListCloseQuestion!.Title);
        w.ListCloseQuestion.Buttons.Single(b => (string?)b.Tag == "cancel").RaiseEvent(new RoutedEventArgs(Avalonia.Controls.Button.ClickEvent));
        await Until(() => w.ListCloseQuestion is null);
        Assert.True(viewer.IsVisible);

        var saved = Path.Join(_dir, "saved list.txt");
        w.ListSavePath = () => saved;
        viewer.Close();
        await Until(() => w.ListCloseQuestion is not null);
        w.ListCloseQuestion!.Buttons.Single(b => (string?)b.Tag == "save").RaiseEvent(new RoutedEventArgs(Avalonia.Controls.Button.ClickEvent));
        await Until(() => w.Viewers.Count == 0);
        Assert.Equal("CHANGELOG.md\r\nREADME.md\r\n", File.ReadAllText(saved, Encoding.UTF8));
        await Until(() => !File.Exists(path) && !Directory.Exists(Path.GetDirectoryName(path)));
        w.CloseNow();
    }

    [AvaloniaFact]
    public async Task TheQuestions_CanBeTurnedOff()
    {
        var w = await Open("README.md");
        w.Services.Settings.Update(s => s with { Confirmations = s.Confirmations with { RemoveFileListLine = false, CloseUnsavedFileList = false }, FileList = s.FileList with { Mine = [new("Mine", "", "$(FileName)")] } });
        var d = await Dialog(w);
        await d.RemoveMineAsync("Mine");
        Assert.Null(d.RemoveQuestion);
        Assert.Empty(w.Services.Settings.Current.FileList.Mine);

        d.ToViewer.IsChecked = true;
        await d.AcceptAsync();
        await Until(() => w.Viewers.Count == 1);
        w.Viewers[0].Close();
        await Until(() => w.Viewers.Count == 0);
        Assert.Null(w.ListCloseQuestion);
        w.Services.Settings.Update(s => s with { Confirmations = new() });
        w.CloseNow();
    }

    [AvaloniaFact]
    public async Task ClosingSalandual_AsksAboutAListInTheViewer()
    {
        var w = await Open("README.md");
        var d = await Dialog(w);
        d.ToViewer.IsChecked = true;
        await d.AcceptAsync();
        await Until(() => w.Viewers.Count == 1);
        w.Close();
        await Until(() => w.ListCloseQuestion is not null);
        w.ListCloseQuestion!.Buttons.Single(b => (string?)b.Tag == "discard").RaiseEvent(new RoutedEventArgs(Avalonia.Controls.Button.ClickEvent));
        await Until(() => !w.IsVisible);
        Assert.Empty(w.Viewers);
    }

    [AvaloniaFact]
    public async Task NothingToList_OnTheUpRow()
    {
        var w = await Open();
        w.LeftPanel.Panel.SetFocus(0);
        Assert.True(w.Run("File.MakeList", Salandual.Core.Commands.PanelTarget.Active));
        Assert.Null(w.FileListDialog);
        Assert.Equal("Nothing to list: select files or folders.", w.NoticeMessage);
        w.CloseNow();
    }

    [AvaloniaFact]
    public async Task Capture_TheDialog()
    {
        if (Environment.GetEnvironmentVariable("SALANDUAL_CAPTURE_DIR") is not { Length: > 0 } dir)
        {
            Assert.Skip("Only with SALANDUAL_CAPTURE_DIR.");
            return;
        }

        var w = await Open("README.md", "CHANGELOG.md", "assets");
        w.Services.Settings.Update(s => s with { FileList = s.FileList with { Mine = [new("For the wiki", "", "* $(FileName)$(CRLF)")] } });
        var d = await Dialog(w);
        d.IncludeBox.IsChecked = true;
        await Until(() => !d.IsCounting);
        d.Pick("Name, size, date");
        await Task.Delay(300);
        Dispatcher.UIThread.RunJobs();
        d.CaptureRenderedFrame()?.Save(Path.Join(dir, "filelist-1.png"), Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);

        d.LineBox.Text = "$(FileName:max)$(TAB)$(FileSize)$(CRLF)";
        d.ToFile.IsChecked = true;
        d.OpenNameRow(null);
        await Task.Delay(300);
        Dispatcher.UIThread.RunJobs();
        d.CaptureRenderedFrame()?.Save(Path.Join(dir, "filelist-2.png"), Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);

        d.ToClipboard.IsChecked = true;
        d.MaskBox.Text = "*.md;*.css";
        d.InsertButton.Flyout!.ShowAt(d.InsertButton);
        await Task.Delay(300);
        Dispatcher.UIThread.RunJobs();
        d.CaptureRenderedFrame()?.Save(Path.Join(dir, "filelist-3.png"), Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
        if (Avalonia.Controls.TopLevel.GetTopLevel(((Avalonia.Controls.MenuFlyout)d.InsertButton.Flyout).Items.OfType<Avalonia.Controls.MenuItem>().First()) is Avalonia.Controls.TopLevel popup && popup != d)
        {
            popup.CaptureRenderedFrame()?.Save(Path.Join(dir, "filelist-menu.png"), Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
        }

        d.InsertButton.Flyout.Hide();
        d.Close();
        w.CloseNow();
    }

    private sealed class TextClipboard : IClipboardAccess
    {
        public string? Text { get; private set; }

        public uint SequenceNumber { get; private set; } = 1;

        public bool SetFiles(IReadOnlyList<string> paths, bool cut) => false;

        public bool SetText(string text)
        {
            Text = text;
            SequenceNumber++;
            return true;
        }

        public void Clear() => Text = null;

        public ClipboardContent? Read() => new() { Text = Text };
    }
}

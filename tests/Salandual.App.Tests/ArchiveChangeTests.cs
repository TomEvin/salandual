// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : tests/Salandual.App.Tests/ArchiveChangeTests.cs

using System.IO.Compression;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Salandual.App.Resources;
using Salandual.Archives;
using Salandual.Core.Operations;

namespace Salandual.App.Tests;

public sealed class ArchiveChangeTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("salandual-change-").FullName;
    private readonly string _left, _right, _zip;

    public ArchiveChangeTests()
    {
        _left = Directory.CreateDirectory(Path.Join(_dir, "Photos")).FullName;
        _right = Directory.CreateDirectory(Path.Join(_dir, "Backup")).FullName;
        File.WriteAllText(Path.Join(_left, "route.gpx"), "<gpx/>");
        _zip = Path.Join(_right, "2024.zip");
        using var archive = ZipFile.Open(_zip, ZipArchiveMode.Create);
        Add(archive, "Balaton/beach.txt", "Sand");
        Add(archive, "Balaton/lake.txt", "Water");
        Add(archive, "readme.txt", "Hello");
    }

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private static void Add(ZipArchive archive, string name, string text)
    {
        using var s = archive.CreateEntry(name).Open();
        s.Write(System.Text.Encoding.UTF8.GetBytes(text));
    }

    private async Task<MainWindow> Open()
    {
        var w = new MainWindow { Width = 1100, Height = 700 };
        w.Show();
        await w.LeftPanel.Panel.NavigateAsync(_left);
        await w.RightPanel.Panel.NavigateAsync(_zip);
        w.LeftPanel.View.Focus();
        Dispatcher.UIThread.RunJobs();
        return w;
    }

    private static void Press(MainWindow w, Key key, RawInputModifiers mods = RawInputModifiers.None)
    {
        w.KeyPress(key, mods, PhysicalKey.None, null);
        w.KeyRelease(key, mods, PhysicalKey.None, null);
        Dispatcher.UIThread.RunJobs();
    }

    private static async Task Until(Func<bool> condition, int tries = 1500)
    {
        for (var i = 0; i < tries && !condition(); i++)
        {
            await Task.Delay(10);
            Dispatcher.UIThread.RunJobs();
        }

        Assert.True(condition());
    }

    private static void FocusOn(MainWindow w, string name)
    {
        var panel = w.ActivePanel.Panel;
        panel.SetFocus(panel.Items.ToList().FindIndex(e => e.Name == name));
    }

    private string[] Entries() => [.. ArchiveReader.Open(_zip, "2024.zip", null, cancellation: TestContext.Current.CancellationToken).Items.Where(i => !i.IsDirectory).Select(i => i.Path).Order(StringComparer.Ordinal)];

    private static async Task<OperationJob> ChangeDone(MainWindow w, OperationJob? before = null)
    {
        await Until(() => w.LastArchiveChange is { IsFinished: true } j && !ReferenceEquals(j, before));
        Dispatcher.UIThread.RunJobs();
        return w.LastArchiveChange!;
    }

    [AvaloniaFact]
    public async Task F5IntoAnArchive_WritesItAnewWithTheFile()
    {
        var w = await Open();
        FocusOn(w, "route.gpx");

        Press(w, Key.F5);
        await Until(() => w.OpenCopyIntoArchive is { IsVisible: true });
        var dialog = w.OpenCopyIntoArchive!;
        Assert.Equal("Copy 1 item (6 B) from Photos", dialog.Title);
        Assert.StartsWith(Core.Resources.Loc.Format(AppStrings.CopyInto_Sum_AsArchive, AppStrings.Pack_Level_Store), dialog.Summary, StringComparison.Ordinal);
        Assert.StartsWith("Writing 2024.zip anew needs up to ", dialog.CheckText, StringComparison.Ordinal);
        dialog.Accept(toQueue: false);
        var job = await ChangeDone(w);

        Assert.Equal(JobState.Done, job.State);
        Assert.Equal(["Balaton\\beach.txt", "Balaton\\lake.txt", "readme.txt", "route.gpx"], Entries());
        Assert.True(File.Exists(Path.Join(_left, "route.gpx")));
        await Until(() => w.RightPanel.Panel.Items.Any(e => e.Name == "route.gpx"));
        w.CloseNow();
    }

    [AvaloniaFact]
    public async Task F8InsideAnArchive_AlwaysAsks_ThenTheEntriesAreGone()
    {
        var w = await Open();
        w.Services.Settings.Update(s => s with { Confirmations = s.Confirmations with { Delete = false } });
        Press(w, Key.Tab);
        FocusOn(w, "Balaton");

        Press(w, Key.F8);
        await Until(() => w.OpenArchiveDelete is { IsVisible: true });
        var dialog = w.OpenArchiveDelete!;
        Assert.Equal("Delete Balaton from 2024.zip?", dialog.Title);
        dialog.Close("delete");
        var job = await ChangeDone(w);

        Assert.Equal(JobState.Done, job.State);
        Assert.Equal(["readme.txt"], Entries());
        w.CloseNow();
    }

    [AvaloniaFact]
    public async Task F6OutOfAnArchive_UnpacksThenTakesTheEntriesOut()
    {
        var w = await Open();
        Press(w, Key.Tab);
        FocusOn(w, "readme.txt");

        Press(w, Key.F6);
        await Until(() => w.OpenUnpackDialog is { IsVisible: true });
        Assert.StartsWith(AppStrings.Copy_Move + " ", w.OpenUnpackDialog!.Title, StringComparison.Ordinal);
        w.OpenUnpackDialog.Accept(toQueue: false);
        var job = await ChangeDone(w);

        Assert.Equal(JobState.Done, job.State);
        Assert.Equal("Hello", File.ReadAllText(Path.Join(_left, "readme.txt")));
        Assert.Equal(["Balaton\\beach.txt", "Balaton\\lake.txt"], Entries());
        w.CloseNow();
    }

    [AvaloniaFact]
    public async Task F4_SavingAsksToPutTheFileBack()
    {
        var w = await Open();
        Press(w, Key.Tab);
        FocusOn(w, "readme.txt");

        var editor = await w.EditInArchiveAsync(w.RightPanel);
        Assert.NotNull(editor);
        await Until(() => editor!.Editor.Document.TextLength > 0);
        Assert.False(editor!.Editor.IsReadOnly);
        editor.Editor.Document.Insert(0, "Edited. ");
        Assert.True(await editor.SaveAsync());
        await Until(() => w.OpenPutBack is { IsVisible: true });
        w.OpenPutBack!.Answer(putBack: true);
        var job = await ChangeDone(w);

        Assert.Equal(JobState.Done, job.State);
        var contents = ArchiveReader.Open(_zip, "2024.zip", null, cancellation: TestContext.Current.CancellationToken);
        using var text = new MemoryStream();
        ArchiveReader.Extract(contents, [contents.Find("readme.txt")!.Index], _ => text, null, "2024.zip", cancellation: TestContext.Current.CancellationToken);
        Assert.Equal("Edited. Hello", System.Text.Encoding.UTF8.GetString(text.ToArray()));
        editor.Close();
        w.CloseNow();
    }

    [AvaloniaFact]
    public async Task F4_StaysReadOnly_WhenTheSettingSaysSo()
    {
        var w = await Open();
        w.Services.Settings.Update(s => s with { Operations = s.Operations with { ArchiveEditSave = ArchiveEditSave.ReadOnly } });
        Press(w, Key.Tab);
        FocusOn(w, "readme.txt");

        var editor = await w.EditInArchiveAsync(w.RightPanel);
        await Until(() => editor!.Editor.Document.TextLength > 0);

        Assert.True(editor!.Editor.IsReadOnly);
        editor.Close();
        w.CloseNow();
    }

    [AvaloniaFact]
    public async Task AnArchiveInsideAnArchive_SaysItCantBeChanged()
    {
        var outer = Path.Join(_right, "outer.zip");
        using (var archive = ZipFile.Open(outer, ZipArchiveMode.Create))
        {
            archive.CreateEntryFromFile(_zip, "2024.zip");
        }

        var w = await Open();
        await w.RightPanel.Panel.NavigateAsync(Path.Join(outer, "2024.zip"));
        Press(w, Key.Tab);
        Assert.Equal("ZIP · read only", w.RightPanel.DirectoryLine.Archive!.Format);
        FocusOn(w, "readme.txt");

        Press(w, Key.F8);

        Assert.Null(w.OpenArchiveDelete);
        Assert.Contains("inside another archive", w.NoticeMessage, StringComparison.Ordinal);
        w.CloseNow();
    }

    [AvaloniaFact]
    public async Task AChangeableArchive_IsNotMarkedReadOnly()
    {
        var w = await Open();
        Assert.Equal("ZIP", w.RightPanel.DirectoryLine.Archive!.Format);
        w.CloseNow();
    }

    [AvaloniaFact]
    public void Pictures()
    {
        if (Environment.GetEnvironmentVariable("SALANDUAL_CAPTURE_DIR") is not { Length: > 0 } dir)
        {
            return;
        }

        Directory.CreateDirectory(dir);
        void Save(Avalonia.Controls.Window window, string name)
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            window.CaptureRenderedFrame()!.Save(Path.Combine(dir, name), Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
        }

        var settings = new Core.Settings.OperationSettings();
        var format = Core.Listing.DisplayFormat.From(new Core.Settings.PanelSettings(), System.Globalization.CultureInfo.CurrentCulture);
        var fs = new Platform.Windows.FileSystem.LocalOperations();
        var contents = ArchiveReader.Open(_zip, "2024.zip", null);
        var into = new Dialogs.CopyIntoArchiveDialog(new Dialogs.CopyIntoContext(false, "Copy 3 items (1.8 GB) from Photos", "E:\\Backup\\2024.zip\\", contents, 1_900_000_000, settings), fs, format, _ => { });
        Save(into, "into.png");
        into.OpenAdvanced();
        Save(into, "into-advanced.png");
        into.Close();

        var delete = new Dialogs.ChoiceDialog("Delete 2 items from 2024.zip?",
            [Core.Resources.Loc.Format(AppStrings.ArchiveDelete_Text, "Balaton (214 files), IMG_4021.jpg", "2024.zip"), Core.Resources.Loc.Format(AppStrings.ArchiveDelete_Replaced, "2024.zip")],
            [new("cancel", AppStrings.Common_Cancel, "Esc", Key.Escape), new("delete", AppStrings.ArchiveDelete_Delete, "Enter", Key.Enter, Primary: true)], "cancel", 500);
        Save(delete, "delete.png");
        delete.Close();

        var putBack = new Dialogs.PutBackDialog("readme.txt", "2024.zip");
        Save(putBack, "putback.png");
        putBack.Close();
    }
}

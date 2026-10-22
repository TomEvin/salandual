// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : tests/Salandual.App.Tests/DragDropTests.cs

using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Salandual.App.Panels;
using Salandual.App.Resources;
using Salandual.Core.Operations;
using Salandual.Core.Resources;
using Salandual.Core.Settings;
using Salandual.Platform.Windows.Shell;

namespace Salandual.App.Tests;

public sealed class DragDropTests : IDisposable
{
    private const DropEffects All = DropEffects.Copy | DropEffects.Move | DropEffects.Link;
    private readonly string _dir = Directory.CreateTempSubdirectory("salandual-dnd-").FullName;
    private readonly string _left;
    private readonly string _right;
    private readonly FakeShell _shell = new();

    public DragDropTests()
    {
        _left = Directory.CreateDirectory(Path.Join(_dir, "Nyaralás 2026")).FullName;
        _right = Directory.CreateDirectory(Path.Join(_dir, "Photos")).FullName;
        Directory.CreateDirectory(Path.Join(_left, "Edited"));
        File.WriteAllText(Path.Join(_left, "IMG_0412.HEIC"), "12");
        File.WriteAllText(Path.Join(_left, "IMG_0413.HEIC"), "345");
        File.WriteAllText(Path.Join(_left, "notes.txt"), "n");
        Directory.CreateDirectory(Path.Join(_right, "2025"));
    }

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private async Task<MainWindow> Open(Func<MouseSettings, MouseSettings>? mouse = null)
    {
        var w = new MainWindow { Width = 1100, Height = 700 };
        w.ShellAccess = _shell;
        w.Services.Settings.Update(s => s with { Mouse = (mouse ?? (m => m))(new MouseSettings()) });
        w.Show();
        await w.LeftPanel.Panel.NavigateAsync(_left);
        await w.RightPanel.Panel.NavigateAsync(_right);
        w.LeftPanel.View.Focus();
        Dispatcher.UIThread.RunJobs();
        w.CaptureRenderedFrame();
        return w;
    }

    private static int Row(PanelHost host, string name) => host.Panel.Items.ToList().FindIndex(e => e.Name == name);

    private static Point RowPoint(MainWindow w, PanelHost host, string name)
    {
        var r = host.View.NameBounds(Row(host, name))!.Value;
        return host.View.TranslatePoint(new Point(r.X + 20, r.Y + (r.Height / 2)), w)!.Value;
    }

    private static Point PanelPoint(MainWindow w, PanelHost host)
        => host.View.TranslatePoint(new Point(80, host.View.Bounds.Height - 20), w)!.Value;

    private static DropData Files(params string[] paths) => new(paths, null);

    private static async Task Until(Func<bool> condition, int tries = 500)
    {
        for (var i = 0; i < tries && !condition(); i++)
        {
            await Task.Delay(10);
            Dispatcher.UIThread.RunJobs();
        }

        Assert.True(condition());
    }

    [AvaloniaFact]
    public async Task LeftDrag_TakesTheSelection_OrTheRowUnderTheMouse()
    {
        var w = await Open();
        var left = w.LeftPanel;
        left.Panel.SelectWhere(e => e.Name.StartsWith("IMG", StringComparison.Ordinal), select: true);

        var from = RowPoint(w, left, "IMG_0412.HEIC");
        var dimmed = new List<string>();
        _shell.DuringDrag = () => dimmed.AddRange(left.Panel.Items.Select(e => e.Name).Where(n => left.View.IsCut!(n)));
        w.MouseDown(from, MouseButton.Left);
        w.MouseMove(from + new Point(2, 1), RawInputModifiers.LeftMouseButton);
        Assert.Empty(_shell.Drags);
        w.MouseMove(from + new Point(12, 6), RawInputModifiers.LeftMouseButton);
        w.MouseUp(from + new Point(12, 6), MouseButton.Left);
        Dispatcher.UIThread.RunJobs();

        var drag = Assert.Single(_shell.Drags);
        Assert.Equal(_left, drag.Folder);
        Assert.Equal(["IMG_0412.HEIC", "IMG_0413.HEIC"], drag.Names.Order());
        Assert.True(drag.PlainCopies);
        Assert.Equal(["IMG_0412.HEIC", "IMG_0413.HEIC"], dimmed.Order());

        var picture = _shell.Pictures[0]!;
        Assert.Equal(0, picture.PointerY);
        Assert.Equal(0, picture.Bgra[(picture.PointerX * 4) + 3]);
        Assert.True(picture.Height >= DragPictures.PointerGap + DragPictures.IconSize);
        Assert.Contains(Enumerable.Range(0, picture.Width * picture.Height), i => picture.Bgra[(i * 4) + 3] == 255);
        Assert.DoesNotContain(left.Panel.Items, e => left.View.IsCut!(e.Name));
        _shell.DuringDrag = null;

        var notes = RowPoint(w, left, "notes.txt");
        w.MouseDown(notes, MouseButton.Left);
        w.MouseMove(notes + new Point(0, 15), RawInputModifiers.LeftMouseButton);
        w.MouseUp(notes + new Point(0, 15), MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(["notes.txt"], _shell.Drags[1].Names);
        Assert.Equal(2, left.Panel.GetSelectionSummary().Count);
        w.CloseNow();
    }

    [AvaloniaFact]
    public async Task RightDrag_StartsADrag_UnlessTheRightButtonSelects()
    {
        var w = await Open(m => m with { PlainDrag = PlainDrag.WindowsRule });
        var from = RowPoint(w, w.LeftPanel, "notes.txt");
        w.MouseDown(from, MouseButton.Right);
        w.MouseMove(from + new Point(15, 0), RawInputModifiers.RightMouseButton);
        w.MouseUp(from + new Point(15, 0), MouseButton.Right);
        Dispatcher.UIThread.RunJobs();
        var drag = Assert.Single(_shell.Drags);
        Assert.Equal(["notes.txt"], drag.Names);
        Assert.False(drag.PlainCopies);
        Assert.Null(w.QuickMenu);
        w.CloseNow();

        var selecting = await Open(m => m with { RightClickUnselected = RightClickUnselected.RightButtonSelects });
        from = RowPoint(selecting, selecting.LeftPanel, "notes.txt");
        selecting.MouseDown(from, MouseButton.Right);
        selecting.MouseMove(from + new Point(15, 0), RawInputModifiers.RightMouseButton);
        selecting.MouseUp(from + new Point(15, 0), MouseButton.Right);
        Dispatcher.UIThread.RunJobs();
        Assert.Single(_shell.Drags);
        selecting.Close();
    }

    [AvaloniaFact]
    public async Task TheParentRow_IsNotDragged()
    {
        var w = await Open();
        var from = RowPoint(w, w.LeftPanel, "..");
        w.MouseDown(from, MouseButton.Left);
        w.MouseMove(from + new Point(20, 0), RawInputModifiers.LeftMouseButton);
        w.MouseUp(from + new Point(20, 0), MouseButton.Left);
        Assert.Empty(_shell.Drags);
        w.CloseNow();
    }

    [AvaloniaFact]
    public async Task DropOnTheOtherPanel_CopiesAtOnce_AndShowsThemSelected()
    {
        var w = await Open();
        var files = Files(Path.Join(_left, "IMG_0412.HEIC"), Path.Join(_left, "IMG_0413.HEIC"));
        var at = PanelPoint(w, w.RightPanel);

        var feedback = w.DragEnterAt(files, DragKeys.LeftButton, at, All);
        Assert.Equal(DropEffects.Copy, feedback.Effect);
        Assert.Equal(new DropNote(DropImage.Copy, AppStrings.Drop_CopyTo, "Photos"), feedback.Note);
        Assert.Equal(FilePanelView.DropOnPanel, w.RightPanel.View.DropRow);

        Assert.Equal(DropEffects.Copy, w.DropAt(DragKeys.None, at, All));
        Assert.Equal(FilePanelView.NoDrop, w.RightPanel.View.DropRow);
        await Until(() => File.Exists(Path.Join(_right, "IMG_0413.HEIC")));
        await Until(() => w.RightPanel.Panel.GetSelectionSummary().Count == 2);
        Assert.True(File.Exists(Path.Join(_left, "IMG_0412.HEIC")));
        Assert.True(w.RightPanel.IsActive);
        w.CloseNow();
    }

    [AvaloniaFact]
    public async Task Keys_ChangeTheNote_ShiftMoves_AltMakesShortcuts()
    {
        var w = await Open();
        var file = Path.Join(_left, "notes.txt");
        var at = PanelPoint(w, w.RightPanel);
        w.DragEnterAt(Files(file), DragKeys.LeftButton, at, All);

        var shift = w.DragOverAt(DragKeys.LeftButton | DragKeys.Shift, at, All);
        Assert.Equal(new DropNote(DropImage.Move, AppStrings.Drop_MoveTo, "Photos"), shift.Note);
        var alt = w.DragOverAt(DragKeys.LeftButton | DragKeys.Alt, at, All);
        Assert.Equal(new DropNote(DropImage.Link, AppStrings.Drop_LinkIn, "Photos"), alt.Note);

        Assert.Equal(DropEffects.Link, w.DropAt(DragKeys.Alt, at, All));
        await Until(() => _shell.Shortcuts.Count == 1);
        Assert.Equal((file, _right), _shell.Shortcuts[0]);

        w.DragEnterAt(Files(file), DragKeys.LeftButton | DragKeys.Shift, at, All);
        Assert.Equal(DropEffects.Move, w.DropAt(DragKeys.Shift, at, All));
        await Until(() => File.Exists(Path.Join(_right, "notes.txt")) && !File.Exists(file));
        w.CloseNow();
    }

    [AvaloniaFact]
    public async Task DropOnAFolderRow_GoesIntoIt()
    {
        var w = await Open();
        var at = RowPoint(w, w.RightPanel, "2025");
        var feedback = w.DragEnterAt(Files(Path.Join(_left, "notes.txt")), DragKeys.LeftButton, at, All);
        Assert.Equal("2025", feedback.Note!.Insert);
        Assert.Equal(Row(w.RightPanel, "2025"), w.RightPanel.View.DropRow);

        w.DropAt(DragKeys.None, at, All);
        await Until(() => File.Exists(Path.Join(_right, "2025", "notes.txt")));
        w.CloseNow();
    }

    [AvaloniaFact]
    public async Task OntoItsOwnFolder_OrIntoItself_NothingHappens()
    {
        var w = await Open();
        var own = w.DragEnterAt(Files(Path.Join(_left, "notes.txt")), DragKeys.LeftButton, PanelPoint(w, w.LeftPanel), All);
        Assert.Equal(DropEffects.None, own.Effect);
        Assert.Equal(new DropNote(DropImage.None, AppStrings.Drop_AlreadyIn, "Nyaralás 2026"), own.Note);
        Assert.Equal(DropEffects.None, w.DropAt(DragKeys.None, PanelPoint(w, w.LeftPanel), All));

        var edited = RowPoint(w, w.LeftPanel, "Edited");
        var into = w.DragEnterAt(Files(Path.Join(_left, "Edited")), DragKeys.LeftButton, edited, All);
        Assert.Equal(DropEffects.None, into.Effect);
        Assert.Equal(AppStrings.Drop_IntoItself, into.Note!.Message);
        w.DragLeft();
        Assert.Equal(FilePanelView.NoDrop, w.LeftPanel.View.DropRow);
        w.CloseNow();
    }

    [AvaloniaFact]
    public async Task DropOnAHotPath_GoesIntoItsFolder()
    {
        var w = await Open();
        var inbox = Directory.CreateDirectory(Path.Join(_dir, "Inbox")).FullName;
        w.Services.Settings.Update(s => s with { HotPaths = s.HotPaths.With(new HotPath(3, "Inbox", inbox)) });
        Dispatcher.UIThread.RunJobs();
        w.CaptureRenderedFrame();
        var strip = w.HotPathBar;
        var chip = strip.Shown.First(i => i.Kind == StripItemKind.HotPath && i.Target == "3");
        var at = strip.TranslatePoint(strip.ChipBounds(chip)!.Value.Center, w)!.Value;

        var feedback = w.DragEnterAt(Files(Path.Join(_left, "notes.txt")), DragKeys.LeftButton, at, All);
        Assert.Equal(DropEffects.Copy, feedback.Effect);
        Assert.Equal(Loc.Format(AppStrings.Drop_HotPath, "Inbox", 3), feedback.Note!.Insert);
        Assert.Equal(chip, strip.DropItem);

        w.DropAt(DragKeys.None, at, All);
        Assert.Null(strip.DropItem);
        await Until(() => File.Exists(Path.Join(inbox, "notes.txt")));
        w.CloseNow();
    }

    [AvaloniaFact]
    public async Task DriveChip_TheFolderItOpens_OrTheRoot()
    {
        var w = await Open(m => m with { DriveChipDrop = DriveChipDrop.DriveRoot });
        w.CaptureRenderedFrame();
        var strip = w.RightPanel.Strip;
        var root = Path.GetPathRoot(_right)!;
        var chip = strip.Shown.First(i => i.Kind == StripItemKind.Drive && string.Equals(i.Target, root, StringComparison.OrdinalIgnoreCase));
        var spot = w.SpotAt(strip.TranslatePoint(strip.ChipBounds(chip)!.Value.Center, w)!.Value);
        Assert.Equal(DropSpotKind.Chip, spot.Kind);
        Assert.Equal(root, spot.Folder);
        w.CloseNow();
    }

    [AvaloniaFact]
    public async Task RightDrag_AsksWhatToDo()
    {
        var w = await Open();
        var file = Path.Join(_left, "notes.txt");
        var at = PanelPoint(w, w.RightPanel);
        w.DragEnterAt(Files(file), DragKeys.RightButton, at, All);
        Assert.Equal(DropEffects.None, w.DropAt(DragKeys.None, at, All));
        Dispatcher.UIThread.RunJobs();

        var menu = w.DropMenu;
        Assert.NotNull(menu);
        var items = menu.Items.OfType<MenuItem>().ToList();
        Assert.Contains("1 item", items[0].Header as string, StringComparison.Ordinal);
        var copy = items.First(i => (string?)i.Header == AppStrings.DropMenu_CopyHere);
        var move = items.First(i => (string?)i.Header == AppStrings.DropMenu_MoveHere);
        Assert.Equal(FontWeight.Bold, copy.FontWeight);
        Assert.Equal(FontWeight.Normal, move.FontWeight);
        Assert.Contains(items, i => (string?)i.Header == AppStrings.DropMenu_LinkHere);
        Assert.Contains(items, i => (string?)i.Header == AppStrings.DropMenu_CopyWithOptions);

        move.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        await Until(() => File.Exists(Path.Join(_right, "notes.txt")) && !File.Exists(file));
        w.CloseNow();
    }

    [AvaloniaFact]
    public async Task TheDialogFirst_Setting_OpensCopyMove()
    {
        var w = await Open(m => m with { AfterDrop = AfterDrop.ShowDialog });
        var at = PanelPoint(w, w.RightPanel);
        w.DragEnterAt(Files(Path.Join(_left, "notes.txt")), DragKeys.LeftButton, at, All);
        w.DropAt(DragKeys.None, at, All);
        await Until(() => w.OpenOperationDialog is not null);
        Assert.False(File.Exists(Path.Join(_right, "notes.txt")));
        w.OpenOperationDialog!.Close();
        w.CloseNow();
    }

    [AvaloniaFact]
    public async Task DropOnTheDirectoryLine_GoesThere()
    {
        var w = await Open();
        var line = w.RightPanel.DirectoryLine;
        var at = line.TranslatePoint(new Point(line.Bounds.Width / 2, line.Bounds.Height / 2), w)!.Value;
        var edited = Path.Join(_left, "Edited");

        var feedback = w.DragEnterAt(Files(edited), DragKeys.LeftButton, at, All);
        Assert.Equal(DropEffects.Link, feedback.Effect);
        Assert.Equal(new DropNote(DropImage.Link, AppStrings.Drop_GoTo, "Edited"), feedback.Note);
        Assert.True(line.Dropping);

        w.DropAt(DragKeys.None, at, All);
        await Until(() => string.Equals(w.RightPanel.Panel.Path, edited, StringComparison.OrdinalIgnoreCase));
        Assert.False(line.Dropping);
        Assert.True(Directory.Exists(edited));
        w.CloseNow();
    }

    [AvaloniaFact]
    public async Task Capture_DragAndDrop()
    {
        var w = await Open();
        var inbox = Directory.CreateDirectory(Path.Join(_dir, "Inbox")).FullName;
        w.Services.Settings.Update(s => s with { HotPaths = s.HotPaths.With(new HotPath(3, "Inbox", inbox)) });
        Dispatcher.UIThread.RunJobs();
        w.CaptureRenderedFrame();
        var files = Files(Path.Join(_left, "IMG_0412.HEIC"), Path.Join(_left, "IMG_0413.HEIC"));
        w.LeftPanel.Panel.SelectWhere(e => e.Name.StartsWith("IMG", StringComparison.Ordinal), select: true);

        w.DragEnterAt(files, DragKeys.LeftButton, PanelPoint(w, w.RightPanel), All);
        Save(w, "m35b-drop-panel.png");
        w.DragOverAt(DragKeys.LeftButton, RowPoint(w, w.RightPanel, "2025"), All);
        Save(w, "m35b-drop-folder-row.png");
        var strip = w.HotPathBar;
        var chip = strip.Shown.First(i => i.Kind == StripItemKind.HotPath);
        w.DragOverAt(DragKeys.LeftButton, strip.TranslatePoint(strip.ChipBounds(chip)!.Value.Center, w)!.Value, All);
        Save(w, "m35b-drop-hot-path.png");
        w.DragLeft();

        w.DragEnterAt(files, DragKeys.RightButton, PanelPoint(w, w.RightPanel), All);
        w.DropAt(DragKeys.None, PanelPoint(w, w.RightPanel), All);
        Dispatcher.UIThread.RunJobs();
        Save(TopLevel.GetTopLevel(w.DropMenu!), "m35b-right-drag-menu.png");
        w.DropMenu!.Close();
        SavePicture(w.DragPictureFor(_left, ["notes.txt"]), "m35b-drag-picture-1.png");
        SavePicture(w.DragPictureFor(_left, ["IMG_0412.HEIC", "IMG_0413.HEIC", "Edited", "notes.txt"]), "m35b-drag-picture-4.png");
        w.CloseNow();

        var options = new Options.OptionsWindow { Width = 1000, Height = 760 };
        options.Show();
        options.ShowCategory("panels");
        Dispatcher.UIThread.RunJobs();
        options.Page.GetVisualDescendants().OfType<TextBlock>().First(t => t.Text == AppStrings.Opt_ChipDrop).BringIntoView();
        Save(options, "m35b-options-mouse.png");
        options.Close();
    }

    private static void SavePicture(DragPicture? picture, string name)
    {
        Assert.NotNull(picture);
        if (Environment.GetEnvironmentVariable("SALANDUAL_CAPTURE_DIR") is not { Length: > 0 } dir)
        {
            return;
        }

        var bgra = (byte[])picture.Bgra.Clone();
        for (var dy = 0; dy < 3; dy++)
        {
            for (var dx = 0; dx < 3; dx++)
            {
                var i = ((((picture.PointerY + dy) * picture.Width) + picture.PointerX + dx) * 4);
                bgra[i] = 0;
                bgra[i + 1] = 0;
                bgra[i + 2] = 255;
                bgra[i + 3] = 255;
            }
        }

        using var bitmap = new Avalonia.Media.Imaging.WriteableBitmap(new PixelSize(picture.Width, picture.Height), new Vector(96, 96), Avalonia.Platform.PixelFormat.Bgra8888, Avalonia.Platform.AlphaFormat.Premul);
        using (var fb = bitmap.Lock())
        {
            System.Runtime.InteropServices.Marshal.Copy(bgra, 0, fb.Address, bgra.Length);
        }

        Directory.CreateDirectory(dir);
        bitmap.Save(Path.Combine(dir, name), Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
    }

    private static void Save(TopLevel? t, string name)
    {
        Dispatcher.UIThread.RunJobs();
        Assert.NotNull(t);
        var frame = t.CaptureRenderedFrame();
        Assert.NotNull(frame);
        if (Environment.GetEnvironmentVariable("SALANDUAL_CAPTURE_DIR") is { Length: > 0 } dir)
        {
            Directory.CreateDirectory(dir);
            frame!.Save(Path.Combine(dir, name), Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
        }
    }

    private sealed class FakeShell : IShellAccess
    {
        public List<(string Folder, IReadOnlyList<string> Names, bool PlainCopies)> Drags { get; } = [];

        public List<DragPicture?> Pictures { get; } = [];

        public IconPixels? LoadIcon(string path, int sizePx) => null;

        public List<(string Target, string Folder)> Shortcuts { get; } = [];

        public Task<IShellMenuHandle> BuildMenuAsync(string folder, IReadOnlyList<string> names, bool extended) => new NoShellAccess().BuildMenuAsync(folder, names, extended);

        public Task<int> OpenAsync(nint owner, string path) => Task.FromResult(0);

        public void OpenWith(nint owner, string path)
        {
        }

        public Task ShowWindowsPropertiesAsync(nint owner, string folder, IReadOnlyList<string> names) => Task.CompletedTask;

        public Task ShowSecurityPageAsync(nint owner, string path) => Task.CompletedTask;

        public void ViewCertificate(nint owner, byte[] certificate)
        {
        }

        public Action? DuringDrag { get; set; }

        public DropEffects DragOut(nint owner, string folder, IReadOnlyList<string> names, bool plainCopies, DragPicture? picture)
        {
            Drags.Add((folder, [.. names], plainCopies));
            Pictures.Add(picture);
            DuringDrag?.Invoke();
            return DropEffects.None;
        }

        public IDisposable? RegisterDropTarget(nint hwnd, IDropHandler handler) => null;

        public Task<string> CreateShortcutAsync(string target, string folder)
        {
            Shortcuts.Add((target, folder));
            return Task.FromResult(Path.Join(folder, "link.lnk"));
        }
    }
}

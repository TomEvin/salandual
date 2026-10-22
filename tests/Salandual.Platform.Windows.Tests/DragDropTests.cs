// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : tests/Salandual.Platform.Windows.Tests/DragDropTests.cs

using Salandual.Core.Operations;
using Salandual.Platform.Windows.Shell;

namespace Salandual.Platform.Windows.Tests;

public sealed class DragDropTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("salandual-drag-").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    [Fact]
    public async Task DataObject_CarriesTheFiles_AndThePlainCopyMark()
    {
        File.WriteAllText(Path.Combine(_dir, "a.txt"), "a");
        Directory.CreateDirectory(Path.Combine(_dir, "Nyaralás 2026"));
        var (data, preferred) = await ShellThread.Run(() => ReadDataObject(_dir));

        Assert.Equal([Path.Combine(_dir, "a.txt"), Path.Combine(_dir, "Nyaralás 2026")], data.Files);
        Assert.Equal((uint)DropEffects.Copy, preferred);
    }

    [Fact]
    public async Task DragPicture_IsOurs_WithThePointerAboveIt()
    {
        File.WriteAllText(Path.Combine(_dir, "a.txt"), "a");
        var bgra = new byte[40 * 60 * 4];
        bgra.AsSpan(40 * 20 * 4).Fill(0xFF);
        var picture = new DragPicture(40, 60, bgra, 12, 0);

        var (ours, windows) = await ShellThread.Run(() => (ImageHeader(_dir, picture), ImageHeader(_dir, null)));

        Assert.Equal((40, 60, 12, 0), ours);
        Assert.NotEqual(0, windows.Item4);
    }

    private static unsafe (int, int, int, int) ImageHeader(string dir, DragPicture? picture)
    {
        var obj = ShellDrag.CreateDataObject(dir, ["a.txt"], plainCopies: true);
        try
        {
            ShellDrag.SetDragImage(obj, picture);
            var b = ShellData.GetBytes(obj, "DragImageBits")!;
            return (BitConverter.ToInt32(b, 0), BitConverter.ToInt32(b, 4), BitConverter.ToInt32(b, 8), BitConverter.ToInt32(b, 12));
        }
        finally
        {
            ShellItems.Release(obj);
        }
    }

    [Fact]
    public async Task DropTarget_TellsTheHandler_AndWritesTheNote()
    {
        File.WriteAllText(Path.Combine(_dir, "a.txt"), "a");
        var handler = new Handler
        {
            Answer = new DropFeedback(DropEffects.Copy, new DropNote(DropImage.Copy, "Copy to %1", "Photos")),
        };

        var (enterEffect, note, dropEffect, performed, logical, noteAfter) = await ShellThread.Run(() => EnterAndDrop(_dir, handler));

        Assert.Equal([Path.Combine(_dir, "a.txt")], handler.Entered!.Files);
        Assert.Equal(DragKeys.LeftButton | DragKeys.Shift, handler.EnterKeys);
        Assert.Equal((100, 200), handler.EnterPoint);
        Assert.Equal((uint)DropEffects.Copy, enterEffect);
        Assert.Equal(new DropNote(DropImage.Copy, "Copy to %1", "Photos"), note);

        Assert.Equal(0u, dropEffect);
        Assert.Equal(0u, performed);
        Assert.Equal((uint)DropEffects.Move, logical);
        Assert.Equal(DropImage.Invalid, noteAfter!.Image);
        Assert.True(handler.Dropped);
    }

    [Fact]
    public async Task DropTarget_OnlyAnswersWhatTheSourceAllows()
    {
        File.WriteAllText(Path.Combine(_dir, "a.txt"), "a");
        var handler = new Handler { Answer = new DropFeedback(DropEffects.Move) };
        var effect = await ShellThread.Run(() => EnterCopyOnlyAndLeave(_dir, handler));

        Assert.Equal(0u, effect);
        Assert.True(handler.Left);
    }

    [Fact]
    public async Task DropTarget_RegistersWithWindows_AndLetsGo()
    {
        var replaced = await ShellThread.Run(() =>
        {
            using (ShellDropTarget.Register(ShellThread.Window, new Handler()))
            {
            }

            using (ShellDropTarget.Register(ShellThread.Window, new Handler()))
            {
            }

            return true;
        });
        Assert.True(replaced);
    }

    [Fact]
    public async Task Shortcut_IsNamedTheWindowsWay_AndNumberedWhenTaken()
    {
        var photo = Path.Combine(_dir, "photo.jpg");
        File.WriteAllText(photo, "x");
        var into = Directory.CreateDirectory(Path.Combine(_dir, "links")).FullName;

        var first = await ShellLinks.CreateAsync(photo, into);
        var second = await ShellLinks.CreateAsync(photo, into);

        Assert.True(File.Exists(first));
        Assert.True(File.Exists(second));
        Assert.NotEqual(first, second);
        Assert.Equal(into, Path.GetDirectoryName(first));
        Assert.EndsWith(".lnk", first, StringComparison.OrdinalIgnoreCase);
        Assert.StartsWith("photo", Path.GetFileName(first), StringComparison.OrdinalIgnoreCase);
    }

    private static unsafe (DropData Data, uint? Preferred) ReadDataObject(string dir)
    {
        var obj = ShellDrag.CreateDataObject(dir, ["a.txt", "Nyaralás 2026"], plainCopies: true);
        try
        {
            return (ShellData.Read(obj), ShellData.GetDword(obj, "Preferred DropEffect"));
        }
        finally
        {
            ShellItems.Release(obj);
        }
    }

    private static unsafe (uint Enter, DropNote? Note, uint Drop, uint? Performed, uint? Logical, DropNote? NoteAfter) EnterAndDrop(string dir, Handler handler)
    {
        using var target = ShellDropTarget.CreateUnregistered(handler);
        var obj = ShellDrag.CreateDataObject(dir, ["a.txt"], plainCopies: true);
        try
        {
            var t = target.Pointer;
            var vtable = *(void***)t;
            var pt = new ShellDrag.Point32 { X = 100, Y = 200 };
            uint effect = 7;
            ((delegate* unmanaged[Stdcall]<void*, void*, uint, ShellDrag.Point32, uint*, int>)vtable[3])(t, obj, (uint)(DragKeys.LeftButton | DragKeys.Shift), pt, &effect);
            var enter = effect;
            var note = ShellData.GetNote(obj);

            handler.DropAnswer = DropEffects.Move;
            effect = 7;
            ((delegate* unmanaged[Stdcall]<void*, void*, uint, ShellDrag.Point32, uint*, int>)vtable[6])(t, obj, (uint)DragKeys.Shift, pt, &effect);
            return (enter, note, effect, ShellData.GetDword(obj, "Performed DropEffect"), ShellData.GetDword(obj, "Logical Performed DropEffect"), ShellData.GetNote(obj));
        }
        finally
        {
            ShellItems.Release(obj);
        }
    }

    private static unsafe uint EnterCopyOnlyAndLeave(string dir, Handler handler)
    {
        using var target = ShellDropTarget.CreateUnregistered(handler);
        var obj = ShellDrag.CreateDataObject(dir, ["a.txt"], plainCopies: false);
        try
        {
            var t = target.Pointer;
            uint e = (uint)DropEffects.Copy;
            ((delegate* unmanaged[Stdcall]<void*, void*, uint, ShellDrag.Point32, uint*, int>)(*(void***)t)[3])(t, obj, 1, default, &e);
            ((delegate* unmanaged[Stdcall]<void*, int>)(*(void***)t)[5])(t);
            return e;
        }
        finally
        {
            ShellItems.Release(obj);
        }
    }

    private sealed class Handler : IDropHandler
    {
        public DropFeedback Answer { get; set; } = DropFeedback.Nothing;

        public DropEffects DropAnswer { get; set; }

        public DropData? Entered { get; private set; }

        public DragKeys EnterKeys { get; private set; }

        public (int, int) EnterPoint { get; private set; }

        public bool Left { get; private set; }

        public bool Dropped { get; private set; }

        public DropFeedback Enter(DropData data, DragKeys keys, int x, int y, DropEffects allowed)
        {
            Entered = data;
            EnterKeys = keys;
            EnterPoint = (x, y);
            return Answer;
        }

        public DropFeedback Over(DragKeys keys, int x, int y, DropEffects allowed) => Answer;

        public void Leave() => Left = true;

        public DropEffects Drop(DragKeys keys, int x, int y, DropEffects allowed)
        {
            Dropped = true;
            return DropAnswer;
        }
    }
}

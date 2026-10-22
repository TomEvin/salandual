// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : tests/Salandual.App.Tests/PhotoInfoViewerTests.cs

using System.Buffers.Binary;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Salandual.App.Viewer;
using Salandual.Core.Settings;
using Salandual.Imaging;

namespace Salandual.App.Tests;

public sealed class PhotoInfoViewerTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("salandual-photoinfo-").FullName;

    public PhotoInfoViewerTests()
    {
        File.WriteAllBytes(Path.Join(_dir, "balaton.jpg"), JpegWithGps());
    }

    public void Dispose()
    {
        for (var i = 0; ; i++)
        {
            try
            {
                Directory.Delete(_dir, recursive: true);
                return;
            }
            catch (IOException) when (i < 50)
            {
                Thread.Sleep(20);
            }
        }
    }

    private static byte[] JpegWithGps()
    {
        using var image = new ImageMagick.MagickImage(ImageMagick.MagickColors.SeaGreen, 64, 48) { Format = ImageMagick.MagickFormat.Jpeg };
        var jpeg = image.ToByteArray();

        var tiff = new byte[128];
        "II*\0"u8.CopyTo(tiff);
        BinaryPrimitives.WriteInt32LittleEndian(tiff.AsSpan(4), 8);
        BinaryPrimitives.WriteInt16LittleEndian(tiff.AsSpan(8), 1);
        Entry(10, 0x8825, 4, 1, 26);
        BinaryPrimitives.WriteInt16LittleEndian(tiff.AsSpan(26), 4);
        Entry(28, 1, 2, 2, 'N');
        Entry(40, 2, 5, 3, 80);
        Entry(52, 3, 2, 2, 'E');
        Entry(64, 4, 5, 3, 104);
        var at = 80;
        foreach (var (n, d) in new[] { (46, 1), (54, 1), (216, 10), (17, 1), (53, 1), (348, 10) })
        {
            BinaryPrimitives.WriteInt32LittleEndian(tiff.AsSpan(at), n);
            BinaryPrimitives.WriteInt32LittleEndian(tiff.AsSpan(at + 4), d);
            at += 8;
        }

        var app1 = new List<byte> { 0xFF, 0xE1, 0, 0 };
        app1.AddRange("Exif\0\0"u8.ToArray());
        app1.AddRange(tiff);
        BinaryPrimitives.WriteUInt16BigEndian(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(app1)[2..], (ushort)(app1.Count - 2));
        return [.. jpeg[..2], .. app1, .. jpeg[2..]];

        void Entry(int offset, ushort tag, ushort type, int count, int value)
        {
            BinaryPrimitives.WriteUInt16LittleEndian(tiff.AsSpan(offset), tag);
            BinaryPrimitives.WriteUInt16LittleEndian(tiff.AsSpan(offset + 2), type);
            BinaryPrimitives.WriteInt32LittleEndian(tiff.AsSpan(offset + 4), count);
            BinaryPrimitives.WriteInt32LittleEndian(tiff.AsSpan(offset + 8), value);
        }
    }

    private async Task<MainWindow> Open()
    {
        var w = new MainWindow { Width = 1300, Height = 700 };
        w.Show();
        await w.LeftPanel.Panel.NavigateAsync(_dir, "balaton.jpg");
        await w.RightPanel.Panel.NavigateAsync(_dir);
        w.LeftPanel.View.Focus();
        Dispatcher.UIThread.RunJobs();
        return w;
    }

    private static void Press(TopLevel w, Key key, RawInputModifiers mods = RawInputModifiers.None)
    {
        w.KeyPress(key, mods, PhysicalKey.None, null);
        w.KeyRelease(key, mods, PhysicalKey.None, null);
        Dispatcher.UIThread.RunJobs();
    }

    private static async Task Until(Func<bool> condition)
    {
        for (var i = 0; i < 1000 && !condition(); i++)
        {
            await Task.Delay(10);
            Dispatcher.UIThread.RunJobs();
        }

        Assert.True(condition());
    }

    private static async Task<ViewerWindow> Viewer(MainWindow w)
    {
        Press(w, Key.F3);
        await Until(() => w.Viewers.Count > 0 && w.Viewers[^1].ImageView.Image is not null);
        var v = w.Viewers[^1];
        v.Width = 1100;
        v.Height = 700;
        Dispatcher.UIThread.RunJobs();
        return v;
    }

    private static List<string> Texts(Control? root) => root is null ? [] : [.. root.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text ?? "")];

    [AvaloniaFact]
    public async Task E_ShowsPhotoInformation_BesideThePicture_AndIsRemembered()
    {
        var w = await Open();
        var settings = w.Services.Settings;
        var before = settings.Current.Viewers;
        try
        {
            settings.Update(s => s with { Viewers = s.Viewers with { PhotoInfo = false, PhotoInfoPlace = InfoPlace.Panel } });
            var v = await Viewer(w);
            Assert.False(v.InfoPanelVisible);

            Press(v, Key.E);
            await Until(() => v.InfoPanelVisible && Texts(v.InfoContent).Contains("46.906, 17.893"));
            Assert.True(settings.Current.Viewers.PhotoInfo);
            var texts = Texts(v.InfoContent);
            Assert.Contains("Photo information", texts);
            Assert.Contains(v.InfoContent!.GetVisualDescendants().OfType<Button>(), b => ToolTip.GetTip(b) as string == "Close (E)");
            Assert.Contains("64 × 48", texts);
            Assert.Contains(texts, t => t.EndsWith("· JPEG", StringComparison.Ordinal));
            Assert.Contains("GPS", v.InfoContent!.GetVisualDescendants().OfType<ToggleButton>().Select(b => b.Content as string));
            Assert.DoesNotContain("IPTC", v.InfoContent!.GetVisualDescendants().OfType<ToggleButton>().Select(b => b.Content as string));

            v.SelectInfoTab(InfoGroup.Gps);
            await Until(() => Texts(v.InfoContent).Contains("GPS Latitude"));
            Assert.Contains("GPS Latitude\t", v.InfoText(), StringComparison.Ordinal);

            Button Tip(string tip) => v.InfoContent!.GetVisualDescendants().OfType<Button>().Single(b => ToolTip.GetTip(b) as string == tip);
            Tip("Show in its own window").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await Until(() => v.InfoWindow is not null && !v.InfoPanelVisible && v.InfoContent is not null);
            Assert.Equal(InfoPlace.Window, settings.Current.Viewers.PhotoInfoPlace);
            Tip("Show beside the picture").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await Until(() => v.InfoWindow is null && v.InfoPanelVisible);

            Press(v, Key.F4);
            await Until(() => v.IsHex);
            Assert.False(v.InfoPanelVisible);
            v.Close();

            var again = await Viewer(w);
            await Until(() => again.InfoPanelVisible);
            Press(again, Key.E);
            Assert.False(again.InfoPanelVisible);
            Assert.False(settings.Current.Viewers.PhotoInfo);
            again.Close();
        }
        finally
        {
            settings.Update(s => s with { Viewers = before });
            w.CloseNow();
        }
    }

    [AvaloniaFact]
    public async Task ShowOnAMap_OpensTheChosenSite_WithThePhotosPlace()
    {
        var w = await Open();
        var settings = w.Services.Settings;
        var before = settings.Current.Viewers;
        var opened = new List<Uri>();
        w.OpenWebLink = uri =>
        {
            opened.Add(uri);
            return true;
        };
        try
        {
            settings.Update(s => s with { Viewers = s.Viewers with { PhotoInfo = true, PhotoInfoPlace = InfoPlace.Panel, MapSite = MapSite.OpenStreetMap } });
            var v = await Viewer(w);
            Button Link() => v.InfoContent!.GetVisualDescendants().OfType<Button>().Single(b => b.Classes.Contains("vlink"));
            await Until(() => v.InfoContent?.GetVisualDescendants().OfType<Button>().Any(b => b.Classes.Contains("vlink")) == true);
            Assert.Equal("Show on OpenStreetMap", Link().Content);
            Link().RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal("https://www.openstreetmap.org/?mlat=46.906&mlon=17.893#map=15/46.906/17.893", Assert.Single(opened).ToString());

            settings.Update(s => s with { Viewers = s.Viewers with { MapSite = MapSite.Custom, MapCustomName = "", MapCustomUrl = "https://mapy.example/?x={lon}&y={lat}" } });
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("Show on OpenStreetMap", Link().Content);
            settings.Update(s => s with { Viewers = s.Viewers with { MapSite = MapSite.Custom, MapCustomName = "Mapy", MapCustomUrl = "https://mapy.example/?x={lon}&y={lat}" } });
            await Until(() => Link().Content as string == "Show on Mapy");
            Link().RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal("https://mapy.example/?x=17.893&y=46.906", opened[^1].ToString());
            v.Close();
        }
        finally
        {
            settings.Update(s => s with { Viewers = before });
            w.CloseNow();
        }
    }

    [AvaloniaFact]
    public async Task InItsOwnWindow_PhotoInformationFollowsTheViewer_AndItsCloseButtonTurnsItOff()
    {
        var w = await Open();
        var settings = w.Services.Settings;
        var before = settings.Current.Viewers;
        try
        {
            settings.Update(s => s with { Viewers = s.Viewers with { PhotoInfo = true, PhotoInfoPlace = InfoPlace.Window } });
            var v = await Viewer(w);
            await Until(() => v.InfoWindow is { IsVisible: true } && Texts(v.InfoContent).Contains("46.906, 17.893"));
            Assert.False(v.InfoPanelVisible);
            Assert.Equal("Photo information — balaton.jpg", v.InfoWindow!.Title);
            Assert.DoesNotContain(v.InfoContent!.GetVisualDescendants().OfType<Button>(), b => ToolTip.GetTip(b) as string == "Close (E)");

            v.InfoWindow.Close();
            await Until(() => v.InfoWindow is null);
            Assert.False(settings.Current.Viewers.PhotoInfo);

            settings.Update(s => s with { Viewers = s.Viewers with { PhotoInfo = true } });
            await Until(() => v.InfoWindow is not null);
            v.Close();
            Assert.True(settings.Current.Viewers.PhotoInfo);
        }
        finally
        {
            settings.Update(s => s with { Viewers = before });
            w.CloseNow();
        }
    }

    [AvaloniaFact]
    public async Task ANarrowViewer_PutsTheToolsThatDontFit_AtTheTopOfTheMoreMenu()
    {
        var w = await Open();
        var settings = w.Services.Settings;
        var before = settings.Current.Viewers;
        try
        {
            settings.Update(s => s with { Viewers = s.Viewers with { PhotoInfo = false } });
            var v = await Viewer(w);
            Assert.False(v.MoreHoldsTools);
            Assert.DoesNotContain("Not on the toolbar", v.BuildMoreMenu().Items.OfType<MenuItem>().Select(i => i.Header as string));

            v.Width = v.MinWidth;
            v.UpdateLayout();
            await Until(() => v.MoreHoldsTools);
            var headers = v.BuildMoreMenu().Items.Select(i => (i as MenuItem)?.Header as string).ToList();
            Assert.Equal("Not on the toolbar", headers[0]);
            var group = headers.Skip(1).TakeWhile(h => h is not null).ToList();
            Assert.Contains("Full screen", group);
            Assert.Equal(group.IndexOf("Rotate left") + 1, group.IndexOf("Rotate right"));
            Assert.True(group.IndexOf("Zoom in") < group.IndexOf("Full screen"));

            var full = v.BuildMoreMenu().Items.OfType<MenuItem>().First(i => i.Header as string == "Full screen");
            Assert.Equal("F11", full.InputGesture?.ToString());

            v.Width = 1400;
            v.UpdateLayout();
            await Until(() => !v.MoreHoldsTools);
            v.Close();
        }
        finally
        {
            settings.Update(s => s with { Viewers = before });
            w.CloseNow();
        }
    }

    [AvaloniaFact]
    public async Task TheZoomTools_GoAndComeBack_AllAtOnce()
    {
        var w = await Open();
        var settings = w.Services.Settings;
        var before = settings.Current.Viewers;
        try
        {
            settings.Update(s => s with { Viewers = s.Viewers with { PhotoInfo = false } });
            var v = await Viewer(w);
            var tools = v.ZoomTools.Children.ToList();
            Assert.Equal(3, tools.Count);
            var fit = ((Panel)v.ZoomTools.Parent!).Children[0];
            var fitAlone = false;
            var seen = new HashSet<int>();
            for (var width = 1400.0; width >= v.MinWidth; width -= 4)
            {
                v.Width = width;
                Dispatcher.UIThread.RunJobs();
                v.UpdateLayout();
                var room = v.ToolbarLeft.Bounds.Width;
                var shown = tools.Count(t => Avalonia.VisualExtensions.TranslatePoint(t, default, v.ToolbarLeft) is { } p && p.X + t.Bounds.Width <= room + 0.5);
                Assert.True(shown is 0 or 3, $"at {width} px {shown} of the 3 zoom tools are shown");
                Assert.Equal(shown == 0, v.ToolbarLeft.Hidden.Contains(tools[0]));
                Assert.Equal(shown == 0, v.ToolbarLeft.Hidden.Contains(tools[2]));
                fitAlone |= shown == 0 && !v.ToolbarLeft.Hidden.Contains(fit);
                seen.Add(shown);
            }

            Assert.True(seen.SetEquals([0, 3]), $"seen {string.Join(",", seen)}");
            Assert.True(fitAlone);
            var headers = v.BuildMoreMenu().Items.OfType<MenuItem>().Select(i => i.Header as string).ToList();
            Assert.Contains("Zoom in", headers);
                        v.Close();
        }
        finally
        {
            settings.Update(s => s with { Viewers = before });
            w.CloseNow();
        }
    }

    [AvaloniaFact]
    public async Task Capture_PhotoInformation()
    {
        if (Environment.GetEnvironmentVariable("SALANDUAL_CAPTURE_DIR") is not { Length: > 0 } dir)
        {
            return;
        }

        Directory.CreateDirectory(dir);
        File.Copy(Path.Join(_dir, "balaton.jpg"), Path.Combine(dir, "gps-sample.jpg"), overwrite: true);
        var w = await Open();
        var settings = w.Services.Settings;
        var before = settings.Current.Viewers;
        try
        {
            settings.Update(s => s with { Viewers = s.Viewers with { PhotoInfo = true, PhotoInfoPlace = InfoPlace.Panel, MapSite = MapSite.OpenStreetMap } });
            var v = await Viewer(w);
            await Until(() => Texts(v.InfoContent).Contains("46.906, 17.893"));
            v.CaptureRenderedFrame()!.Save(Path.Combine(dir, "info-summary.png"), Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
            v.SelectInfoTab(InfoGroup.Gps);
            await Until(() => Texts(v.InfoContent).Contains("GPS Latitude"));
            v.CaptureRenderedFrame()!.Save(Path.Combine(dir, "info-gps.png"), Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
            v.SelectInfoTab(InfoGroup.Summary);
            settings.Update(s => s with { Viewers = s.Viewers with { PhotoInfoPlace = InfoPlace.Window } });
            await Until(() => v.InfoWindow is not null && Texts(v.InfoContent).Contains("46.906, 17.893"));
            v.InfoWindow!.CaptureRenderedFrame()!.Save(Path.Combine(dir, "info-window.png"), Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
            settings.Update(s => s with { Viewers = s.Viewers with { PhotoInfoPlace = InfoPlace.Panel } });
            settings.Update(s => s with { Viewers = s.Viewers with { PhotoInfo = false } });
            v.Width = v.MinWidth;
            v.UpdateLayout();
            await Until(() => v.MoreHoldsTools);
            v.CaptureRenderedFrame()!.Save(Path.Combine(dir, "info-narrow.png"), Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
            v.Close();

            var appSettings = ((App)Avalonia.Application.Current!).Services.Settings;
            var appBefore = appSettings.Current.Viewers;
            appSettings.Update(s => s with { Viewers = s.Viewers with { MapSite = MapSite.Custom, MapCustomName = "Mapy", MapCustomUrl = "https://mapy.example/?x={lon}" } });
            var o = new Salandual.App.Options.OptionsWindow { Width = 1100, Height = 760 };
            o.Show();
            o.ShowCategory("viewers");
            Dispatcher.UIThread.RunJobs();
            o.Page.Children[^1].BringIntoView();
            Dispatcher.UIThread.RunJobs();
            o.CaptureRenderedFrame()!.Save(Path.Combine(dir, "info-options.png"), Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
            o.Close();
            appSettings.Update(s => s with { Viewers = appBefore });
        }
        finally
        {
            settings.Update(s => s with { Viewers = before });
            w.CloseNow();
        }
    }
}

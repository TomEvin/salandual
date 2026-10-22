// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.Core/Settings/ViewerSettings.cs

using Salandual.Core.Listing;

namespace Salandual.Core.Settings;

public enum ViewerKind
{
    Automatic,
    Text,
    Hex,

    Image,

    Media,

    Markdown,

    Notebook,

    Tree,

    Html,
}

public enum ViewerOpenAs { Automatic, Text, Hex }

public enum FindBarPosition { Top, Bottom }

public enum ViewerTitle { NameAndFolder, FullPath }

public enum SizeFormat { Hex, Decimal, Units }

public enum ByteColors { Two, ByKind }

public enum CanvasColor { Dark, Black, Grey, Magenta, Theme, Custom }

public enum ImageZoom { Fit, Width, Actual }

public enum InfoPlace { Panel, Window }

public enum MapSite { OpenStreetMap, GoogleMaps, Custom }

public enum MediaEnter { OpenInDefaultApp, Nothing }

public enum ViewButtonOrder { MostUsedFirst, KeyOrder }

public enum ViewerClose { EscOrAltF4, AltF4Only }

public enum TreeOpening { FirstLevel, AllFolded }

public sealed record ViewerMask
{
    public string Mask { get; set; } = "*.*";

    public ViewerKind View { get; set; } = ViewerKind.Automatic;

    public ViewerKind Alternate { get; set; } = ViewerKind.Hex;

    public long? LargestMB { get; set; }
}

public sealed record ViewerSettings
{
    public static readonly int[] BytesPerRowChoices = [16, 24, 32, 0];

    public static readonly int[] GroupChoices = [1, 2, 4, 8];

    public static readonly int[] TabWidthChoices = [2, 4, 8];

    public static List<ViewerMask> DefaultMasks() =>
    [
        new()
        {
            Mask = "*.jpg;*.jpeg;*.png;*.gif;*.bmp;*.tif;*.tiff;*.webp;*.avif;*.heic;*.heif;*.jxl;*.arw;*.cr2;*.cr3;*.dng;*.nef;*.orf;*.pef;*.raf;*.rw2;*.psd;*.tga;*.dds;*.svg;*.ico",
            View = ViewerKind.Image,
        },
        new() { Mask = "*.mp4;*.mkv;*.mov;*.avi;*.wmv;*.webm;*.m4v;*.mp3;*.flac;*.m4a;*.aac;*.ogg;*.opus;*.wav;*.wma", View = ViewerKind.Media },
        new() { Mask = "*.md;*.markdown", View = ViewerKind.Markdown, Alternate = ViewerKind.Text },
        NotebookMask(),
        .. TreeMasks(),
        HtmlMask(),
        new() { Mask = "*.*" },
    ];

    public static long DefaultLargestMB(ViewerKind kind, string? nameOrMask = null) => kind switch
    {
        ViewerKind.Image => 500,
        ViewerKind.Markdown => 10,
        ViewerKind.Notebook => 200,
        ViewerKind.Html => 5,
        ViewerKind.Tree => IsYaml(nameOrMask) ? 20 : 50,
        _ => 0,
    };

    private static bool IsYaml(string? nameOrMask) => nameOrMask is not null
        && (nameOrMask.Contains(".yaml", StringComparison.OrdinalIgnoreCase) || nameOrMask.Contains(".yml", StringComparison.OrdinalIgnoreCase));

    public long LargestBytes(string name, ViewerKind kind)
    {
        var row = RowFor(name);
        var mb = row >= 0 && Masks[row].View == kind && Masks[row].LargestMB is { } set ? set : DefaultLargestMB(kind, name);
        return mb <= 0 ? 0 : mb * 1024 * 1024;
    }

    public static ViewerMask NotebookMask() => new() { Mask = "*.ipynb", View = ViewerKind.Notebook, Alternate = ViewerKind.Text };

    public static ViewerMask[] TreeMasks() =>
    [
        new() { Mask = "*.json;*.jsonc;*.jsonl;*.geojson", View = ViewerKind.Tree, Alternate = ViewerKind.Text },
        new() { Mask = "*.yaml;*.yml", View = ViewerKind.Tree, Alternate = ViewerKind.Text },
        new() { Mask = "*.xml;*.xaml;*.csproj;*.slnx;*.props;*.config;*.resx", View = ViewerKind.Tree, Alternate = ViewerKind.Text },
    ];

    public static ViewerMask HtmlMask() => new() { Mask = "*.html;*.htm;*.xhtml;*.mht;*.mhtml", View = ViewerKind.Html, Alternate = ViewerKind.Text };

    public List<ViewerMask> Masks { get; set; } = DefaultMasks();

    public ViewerTitle Title { get; set; } = ViewerTitle.NameAndFolder;

    public FindBarPosition FindBar { get; set; } = FindBarPosition.Top;

    public ViewerClose Close { get; set; } = ViewerClose.EscOrAltF4;

    public ViewerOpenAs OpenAs { get; set; } = ViewerOpenAs.Automatic;

    public string? Encoding { get; set; }

    public bool Wrap { get; set; } = true;

    public bool LineNumbers { get; set; } = true;

    public int TabWidth { get; set; } = 8;

    public bool TextColors { get; set; } = true;

    public int ExactColorsMB { get; set; } = 200;

    public int HexBytesPerRow { get; set; } = 16;

    public int HexGroup { get; set; } = 8;

    public bool DecimalOffsets { get; set; }

    public SizeFormat SelectionFormat { get; set; } = SizeFormat.Hex;

    public SizeFormat SizeFormat { get; set; } = SizeFormat.Hex;

    public ByteColors HexByteColors { get; set; } = ByteColors.Two;

    public CanvasColor ImageCanvas { get; set; } = CanvasColor.Dark;

    public string CanvasCustom { get; set; } = "#1C1C1C";

    public ImageZoom ImageZoom { get; set; } = ImageZoom.Fit;

    public bool PhotoInfo { get; set; }

    public InfoPlace PhotoInfoPlace { get; set; } = InfoPlace.Panel;

    public MapSite MapSite { get; set; } = MapSite.OpenStreetMap;

    public string MapCustomName { get; set; } = "";

    public string MapCustomUrl { get; set; } = "";

    public MediaEnter MediaEnter { get; set; } = MediaEnter.OpenInDefaultApp;

    public bool VideoFrame { get; set; }

    public ViewButtonOrder ViewButtons { get; set; } = ViewButtonOrder.MostUsedFirst;

    public bool MarkdownFullWidth { get; set; }

    public bool MarkdownWebImages { get; set; } = true;

    public bool MarkdownContents { get; set; }

    public bool MarkdownContentsLeft { get; set; }

    public bool NotebookHideCode { get; set; }

    public bool NotebookFoldOutputs { get; set; } = true;

    public bool HtmlScripts { get; set; } = true;

    public bool HtmlWebContent { get; set; } = true;

    public TreeOpening TreeOpens { get; set; } = TreeOpening.FirstLevel;

    public Salandual.Core.Viewer.DataPathStyle TreePath { get; set; } = Salandual.Core.Viewer.DataPathStyle.Dollar;

    public double WindowWidth { get; set; } = 1100;

    public double WindowHeight { get; set; } = 760;

    public bool WindowMaximized { get; set; }

    public int RowFor(string name)
    {
        for (var i = 0; i < Masks.Count; i++)
        {
            if (FileMask.Parse(Masks[i].Mask)?.IsMatch(name) == true)
            {
                return i;
            }
        }

        return -1;
    }

    public IReadOnlyList<(string Mask, int Row)> TakenAbove(int row)
    {
        var taken = new List<(string, int)>();
        if (row <= 0 || row >= Masks.Count)
        {
            return taken;
        }

        var text = Masks[row].Mask;
        var bar = text.IndexOf('|');
        foreach (var part in (bar < 0 ? text : text[..bar]).Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (part is "*" or "*.*")
            {
                continue;
            }

            var sample = part.Replace('*', 'x').Replace('?', 'x');
            for (var above = 0; above < row; above++)
            {
                if (FileMask.Parse(Masks[above].Mask)?.IsMatch(sample) == true)
                {
                    taken.Add((part, above));
                    break;
                }
            }
        }

        return taken;
    }

    public ViewerKind KindFor(string name, bool alternate)
    {
        var row = RowFor(name);
        return row < 0 ? (alternate ? ViewerKind.Hex : ViewerKind.Automatic) : alternate ? Masks[row].Alternate : Masks[row].View;
    }

    internal ViewerSettings Normalize() => this with
    {
        Masks = Masks is { Count: > 0 } masks
            ? [.. masks.Where(m => m is not null && !string.IsNullOrWhiteSpace(m.Mask)).Select(m => m with
            {
                Mask = m.Mask.Trim(),
                View = Enum.IsDefined(m.View) ? m.View : ViewerKind.Automatic,
                Alternate = Enum.IsDefined(m.Alternate) ? m.Alternate : ViewerKind.Hex,
                LargestMB = m.LargestMB is { } mb ? Math.Clamp(mb, 0, 1_000_000) : null,
            })]
            : DefaultMasks(),
        Title = Enum.IsDefined(Title) ? Title : ViewerTitle.NameAndFolder,
        FindBar = Enum.IsDefined(FindBar) ? FindBar : FindBarPosition.Top,
        Close = Enum.IsDefined(Close) ? Close : ViewerClose.EscOrAltF4,
        OpenAs = Enum.IsDefined(OpenAs) ? OpenAs : ViewerOpenAs.Automatic,
        Encoding = string.IsNullOrWhiteSpace(Encoding) ? null : Encoding,
        TabWidth = TabWidthChoices.Contains(TabWidth) ? TabWidth : 8,
        ExactColorsMB = Math.Clamp(ExactColorsMB, 1, 100_000),
        HexBytesPerRow = BytesPerRowChoices.Contains(HexBytesPerRow) ? HexBytesPerRow : 16,
        HexGroup = GroupChoices.Contains(HexGroup) ? HexGroup : 8,
        SelectionFormat = Enum.IsDefined(SelectionFormat) ? SelectionFormat : SizeFormat.Hex,
        SizeFormat = Enum.IsDefined(SizeFormat) ? SizeFormat : SizeFormat.Hex,
        HexByteColors = Enum.IsDefined(HexByteColors) ? HexByteColors : ByteColors.Two,
        ImageCanvas = Enum.IsDefined(ImageCanvas) ? ImageCanvas : CanvasColor.Dark,
        PhotoInfoPlace = Enum.IsDefined(PhotoInfoPlace) ? PhotoInfoPlace : InfoPlace.Panel,
        MapSite = Enum.IsDefined(MapSite) ? MapSite : MapSite.OpenStreetMap,
        MapCustomName = (MapCustomName ?? "").Trim(),
        MapCustomUrl = (MapCustomUrl ?? "").Trim(),
        MediaEnter = Enum.IsDefined(MediaEnter) ? MediaEnter : MediaEnter.OpenInDefaultApp,
        ViewButtons = Enum.IsDefined(ViewButtons) ? ViewButtons : ViewButtonOrder.MostUsedFirst,
        TreeOpens = Enum.IsDefined(TreeOpens) ? TreeOpens : TreeOpening.FirstLevel,
        TreePath = Enum.IsDefined(TreePath) ? TreePath : Salandual.Core.Viewer.DataPathStyle.Dollar,
        CanvasCustom = ColorMath.TryParseHex(CanvasCustom, out var canvas) ? ColorMath.ToHex(canvas) : "#1C1C1C",
        ImageZoom = Enum.IsDefined(ImageZoom) ? ImageZoom : ImageZoom.Fit,
        WindowWidth = double.IsFinite(WindowWidth) ? Math.Clamp(WindowWidth, 400, 10000) : 1100,
        WindowHeight = double.IsFinite(WindowHeight) ? Math.Clamp(WindowHeight, 250, 10000) : 760,
    };
}

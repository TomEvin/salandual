// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.App/Theme/AppIcons.cs

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Salandual.App.Options;

namespace Salandual.App.Theme;

internal static class AppIcons
{
    public sealed record Part(Geometry Geometry, bool Dashed = false, bool Filled = false);

    public sealed record Icon(IReadOnlyList<Part> Parts);

    private static readonly Dictionary<string, Icon> _icons = new(StringComparer.Ordinal)
    {
        ["File.CreateDirectory"] = I("M8.5 13H3A1.5 1.5 0 0 1 1.5 11.5V4.5A1.5 1.5 0 0 1 3 3H6L7.5 4.5H13A1.5 1.5 0 0 1 14.5 6V8 M12.5 9.5V14.5 M10 12H15"),
        ["Find.Files"] = I("M2.5 7A4.5 4.5 0 1 0 11.5 7A4.5 4.5 0 1 0 2.5 7Z M10.4 10.4L14 14"),
        ["Panel.Swap"] = I("M2.5 5.5H13.5L11 3 M13.5 10.5H2.5L5 13"),
        ["File.Copy"] = I("M2.5 2.5H7.5A1 1 0 0 1 8.5 3.5V10.5A1 1 0 0 1 7.5 11.5H2.5A1 1 0 0 1 1.5 10.5V3.5A1 1 0 0 1 2.5 2.5Z M4 14H10.5 M8.5 7H14.5 M12.5 5L14.5 7L12.5 9"),
        ["File.Move"] = new([new(Geo("M2.5 2.5H7.5A1 1 0 0 1 8.5 3.5V10.5A1 1 0 0 1 7.5 11.5H2.5A1 1 0 0 1 1.5 10.5V3.5A1 1 0 0 1 2.5 2.5Z"), Dashed: true), new(Geo("M5 7H14.5 M12.5 5L14.5 7L12.5 9"))]),
        ["File.Delete"] = I("M2.5 4.5H13.5 M6 4.5V3A0.5 0.5 0 0 1 6.5 2.5H9.5A0.5 0.5 0 0 1 10 3V4.5 M4 4.5L4.8 13.5A1 1 0 0 0 5.8 14.4H10.2A1 1 0 0 0 11.2 13.5L12 4.5 M6.8 7V11.5 M9.2 7V11.5"),
        ["File.QuickRename"] = I("M9 5H2.5A1 1 0 0 0 1.5 6V10A1 1 0 0 0 2.5 11H9 M14 5H14.5V11H14 M11.5 2.5V13.5 M10 2.5H13 M10 13.5H13 M4 8H7"),
        ["File.ChangeAttributes"] = I("M2.3 2.5H4.7A0.8 0.8 0 0 1 5.5 3.3V5.7A0.8 0.8 0 0 1 4.7 6.5H2.3A0.8 0.8 0 0 1 1.5 5.7V3.3A0.8 0.8 0 0 1 2.3 2.5Z M2.5 4.6L3.5 5.6L5.1 3.6 M2.3 9.5H4.7A0.8 0.8 0 0 1 5.5 10.3V12.7A0.8 0.8 0 0 1 4.7 13.5H2.3A0.8 0.8 0 0 1 1.5 12.7V10.3A0.8 0.8 0 0 1 2.3 9.5Z M8 4.5H14.5 M8 11.5H14.5"),
        ["File.View"] = I("M1.5 8C1.5 8 4 3.5 8 3.5C12 3.5 14.5 8 14.5 8C14.5 8 12 12.5 8 12.5C4 12.5 1.5 8 1.5 8Z M6 8A2 2 0 1 0 10 8A2 2 0 1 0 6 8Z"),
        ["File.Edit"] = I("M10.6 2.4A1.4 1.4 0 0 1 12.6 2.4L13.6 3.4A1.4 1.4 0 0 1 13.6 5.4L6 13H3V10Z M9.5 3.5L12.5 6.5"),
        ["Clipboard.Cut"] = I("M2.5 12A2 2 0 1 0 6.5 12A2 2 0 1 0 2.5 12Z M9.5 12A2 2 0 1 0 13.5 12A2 2 0 1 0 9.5 12Z M5.8 10.5L11.5 2 M10.2 10.5L4.5 2"),
        ["Clipboard.Copy"] = I("M6.7 5.5H12.8A1.2 1.2 0 0 1 14 6.7V12.8A1.2 1.2 0 0 1 12.8 14H6.7A1.2 1.2 0 0 1 5.5 12.8V6.7A1.2 1.2 0 0 1 6.7 5.5Z M10.5 5.5V3A1 1 0 0 0 9.5 2H3A1 1 0 0 0 2 3V9.5A1 1 0 0 0 3 10.5H5.5"),
        ["Clipboard.Paste"] = I("M5 3H3.5A1 1 0 0 0 2.5 4V13.5A1 1 0 0 0 3.5 14.5H12.5A1 1 0 0 0 13.5 13.5V4A1 1 0 0 0 12.5 3H11 M5.8 1.5H10.2A0.8 0.8 0 0 1 11 2.3V3.7A0.8 0.8 0 0 1 10.2 4.5H5.8A0.8 0.8 0 0 1 5 3.7V2.3A0.8 0.8 0 0 1 5.8 1.5Z M5.5 8.5H10.5 M5.5 11H9"),
        ["Archive.Pack"] = I("M2 8.5H14V13.5A1 1 0 0 1 13 14.5H3A1 1 0 0 1 2 13.5Z M6.5 11H9.5 M8 1.5V6 M5.8 3.8L8 6L10.2 3.8"),
        ["Archive.Unpack"] = I("M2 8.5H14V13.5A1 1 0 0 1 13 14.5H3A1 1 0 0 1 2 13.5Z M6.5 11H9.5 M8 6.5V1.5 M5.8 3.7L8 1.5L10.2 3.7"),
        ["File.Email"] = I("M3 3.5H13A1.5 1.5 0 0 1 14.5 5V11A1.5 1.5 0 0 1 13 12.5H3A1.5 1.5 0 0 1 1.5 11V5A1.5 1.5 0 0 1 3 3.5Z M2 4.5L8 9L14 4.5"),
        ["Icon.More"] = F("M1.6 8A1.4 1.4 0 1 0 4.4 8A1.4 1.4 0 1 0 1.6 8Z M6.6 8A1.4 1.4 0 1 0 9.4 8A1.4 1.4 0 1 0 6.6 8Z M11.6 8A1.4 1.4 0 1 0 14.4 8A1.4 1.4 0 1 0 11.6 8Z"),

        ["Navigation.Execute"] = I("M1.5 12.5V4A1.5 1.5 0 0 1 3 2.5H6L7.5 4H12A1.5 1.5 0 0 1 13.5 5.5V7 M1.5 12.5L3.6 7.9A1.5 1.5 0 0 1 5 7H14.2L11.8 12.5H1.5Z"),
        ["Icon.OpenWith"] = I("M2.5 1.5H5.5A1 1 0 0 1 6.5 2.5V5.5A1 1 0 0 1 5.5 6.5H2.5A1 1 0 0 1 1.5 5.5V2.5A1 1 0 0 1 2.5 1.5Z M10.5 1.5H13.5A1 1 0 0 1 14.5 2.5V5.5A1 1 0 0 1 13.5 6.5H10.5A1 1 0 0 1 9.5 5.5V2.5A1 1 0 0 1 10.5 1.5Z M2.5 9.5H5.5A1 1 0 0 1 6.5 10.5V13.5A1 1 0 0 1 5.5 14.5H2.5A1 1 0 0 1 1.5 13.5V10.5A1 1 0 0 1 2.5 9.5Z M12 9.5V14.5 M9.5 12H14.5"),
        ["Navigation.OpenInOtherPanel"] = I("M3 2.5H13A1.5 1.5 0 0 1 14.5 4V12A1.5 1.5 0 0 1 13 13.5H3A1.5 1.5 0 0 1 1.5 12V4A1.5 1.5 0 0 1 3 2.5Z M8 2.5V13.5 M10 8H13 M11.8 6.5L13.3 8L11.8 9.5"),
        ["File.Duplicate"] = I("M2.7 1.5H8.3A1.2 1.2 0 0 1 9.5 2.7V8.3A1.2 1.2 0 0 1 8.3 9.5H2.7A1.2 1.2 0 0 1 1.5 8.3V2.7A1.2 1.2 0 0 1 2.7 1.5Z M7.7 6.5H13.3A1.2 1.2 0 0 1 14.5 7.7V13.3A1.2 1.2 0 0 1 13.3 14.5H7.7A1.2 1.2 0 0 1 6.5 13.3V7.7A1.2 1.2 0 0 1 7.7 6.5Z"),
        ["Icon.Path"] = I("M6.5 9.5A3 3 0 0 0 10.7 9.5L13 7.2A3 3 0 0 0 8.8 3L7.6 4.1 M9.5 6.5A3 3 0 0 0 5.3 6.5L3 8.8A3 3 0 0 0 7.2 13L8.4 11.9"),
        ["Icon.Windows"] = I("M2 2.5H7.5V8H2Z M8.5 2.5H14V8H8.5Z M2 9H7.5V14H2Z M8.5 9H14V14H8.5Z"),
        ["File.Properties"] = I("M3.5 1.5H12.5A1.5 1.5 0 0 1 14 3V13A1.5 1.5 0 0 1 12.5 14.5H3.5A1.5 1.5 0 0 1 2 13V3A1.5 1.5 0 0 1 3.5 1.5Z M5 5H11 M5 8H11 M5 11H8.5"),
        ["Panel.Refresh"] = I("M13.5 8A5.5 5.5 0 1 1 11.8 4 M12 1.5V4.5H9"),
        ["Icon.Sort"] = I("M4.5 2.5V13.5 M2.5 11.5L4.5 13.5L6.5 11.5 M9 4H14 M9 7.5H13 M9 11H11.5"),
        ["Icon.Pin"] = I("M9.5 1.5L14.5 6.5L12 7.5L9.5 10L9 13.5L2.5 7L6 6.5L8.5 4Z M5.5 10.5L1.5 14.5"),
        ["Icon.Check"] = I("M1.5 8A6.5 6.5 0 1 0 14.5 8A6.5 6.5 0 1 0 1.5 8Z M5.2 8.2L7.2 10.2L11 6.2"),
        ["Icon.Info"] = I("M1.5 8A6.5 6.5 0 1 0 14.5 8A6.5 6.5 0 1 0 1.5 8Z M8 7.5V11 M8 5V5.2"),
        ["Icon.Cross"] = I("M4 4L12 12 M12 4L4 12"),
        ["Icon.Tick"] = I("M3 8.5L6.5 12L13 4.5"),
        ["Icon.CopyText"] = I("M5.5 5.5H13.5V13.5H5.5Z M3.5 10.5H2.5V2.5H10.5V3.5"),

        ["Icon.ParentFolder"] = I("M8 3L3 8 M8 3L13 8 M8 3V14"),
        ["Icon.FolderLoading"] = F("F1 M1 2H6A1 1 0 0 1 7 3V4A1 1 0 0 1 6 5H1A1 1 0 0 1 0 4V3A1 1 0 0 1 1 2Z M2 4H14A2 2 0 0 1 16 6V12A2 2 0 0 1 14 14H2A2 2 0 0 1 0 12V6A2 2 0 0 1 2 4Z"),
        ["Icon.FileLoading"] = F("M4 1H12A2 2 0 0 1 14 3V13A2 2 0 0 1 12 15H4A2 2 0 0 1 2 13V3A2 2 0 0 1 4 1Z"),

        ["Icon.StripDrive"] = new([new(Geo("M4.5 5H11.5A2 2 0 0 1 13.5 7V9A2 2 0 0 1 11.5 11H4.5A2 2 0 0 1 2.5 9V7A2 2 0 0 1 4.5 5Z")), new(Geo("M10 7.25H11.5V8.75H10Z"), Filled: true)]),
        ["Icon.StripNetwork"] = I("M4 3.5H12A1 1 0 0 1 13 4.5V9.5A1 1 0 0 1 12 10.5H4A1 1 0 0 1 3 9.5V4.5A1 1 0 0 1 4 3.5Z M8 10.5V12.5 M5 12.5H11"),
        ["Icon.StripFolder"] = I("M2 4H6L7.5 5.5H14V12.5H2Z"),
        ["Icon.StripAdd"] = I("M8 2L9.53 5.9L13.71 6.15L10.47 8.8L11.53 12.85L8 10.6L4.47 12.85L5.53 8.8L2.29 6.15L6.47 5.9Z"),

        ["Icon.PanelMaximize"] = I("M9.5 2.5L13.5 2.5L13.5 6.5 M6.5 13.5L2.5 13.5L2.5 9.5 M13.5 2.5L9 7 M2.5 13.5L7 9"),
        ["Icon.PanelRestore"] = I("M13.5 6.5L9.5 6.5L9.5 2.5 M2.5 9.5L6.5 9.5L6.5 13.5 M9.5 6.5L13.5 2.5 M6.5 9.5L2.5 13.5"),
        ["Icon.ChevronDown"] = I("M4 6L8 10L12 6"),
        ["Icon.TitleChevron"] = I("M1 3L5 7L9 3"),
        ["Icon.Hamburger"] = I("M1 3H15 M1 8H15 M1 13H15"),
        ["Icon.CaptionMinimize"] = I("M0 5H10"),
        ["Icon.CaptionMaximize"] = I("M0.5 0.5H9.5V9.5H0.5Z"),
        ["Icon.CaptionClose"] = I("M0.5 0.5L9.5 9.5 M9.5 0.5L0.5 9.5"),

        ["Icon.MenuDrive"] = I("M1.5 5H14.5V11H1.5Z M11.5 8H12.5"),
        ["Icon.MenuNetwork"] = I("M2 3H14V10H2Z M8 10V13 M4.5 13H11.5"),
        ["Icon.Folder"] = I("M1.5 4H6L7.5 5.5H14.5V13.5H1.5Z"),
        ["Icon.Registry"] = I("M2 2H7V7H2Z M9 2H14V7H9Z M2 9H7V14H2Z M9 9H14V14H9Z"),
        ["Icon.History"] = I("M8 2A6 6 0 1 0 8.01 2Z M8 4.5V8L10.5 9.5"),

        ["Icon.Minimize"] = I("M3.5 8H12.5"),
        ["Icon.File"] = I("M4 1.5H9L12.5 5V14.5H4Z M9 1.5V5H12.5"),
        ["Icon.Bin"] = I("M2.5 4.5H13.5 M6 4.5V3H10V4.5 M4 4.5L4.8 13.5H11.2L12 4.5 M6.8 7V11.5 M9.2 7V11.5"),
        ["Icon.Rename"] = I("M10.5 2.5L13.5 5.5L5.5 13.5H2.5V10.5Z M9 4L12 7"),
        ["Icon.Confirm"] = I("M3 8.5L6 11.5L13 4.5"),
        ["Icon.Warning"] = I("M8 2L14.5 13.5H1.5Z M8 6.5V10 M8 12V12.5"),
        ["Icon.Shield"] = I("M8 1.5L13.5 3.5V7.5C13.5 11 11 13.5 8 14.5C5 13.5 2.5 11 2.5 7.5V3.5Z"),
        ["Icon.External"] = I("M9 2.5H13.5V7 M13.5 2.5L7 9 M11.5 9.5V13.5H2.5V4.5H6.5"),
        ["Icon.InfoToWindow"] = I("M9.5 2.5H13.5V6.5 M13.5 2.5L8.5 7.5 M12.5 9.5V13.5H2.5V3.5H6.5"),
        ["Icon.InfoToPanel"] = I("M2.5 3.5H13.5V12.5H2.5Z M9.5 3.5V12.5 M11 6H12 M11 8H12"),
        ["Icon.MediaAudio"] = I("M6 12.5A1.75 1.75 0 1 1 2.5 12.5A1.75 1.75 0 1 1 6 12.5Z M6 12.5V3L13.5 1.5V10.5 M13.5 10.5A1.75 1.75 0 1 1 10 10.5A1.75 1.75 0 1 1 13.5 10.5Z M6 5.5L13.5 4"),
        ["Icon.MediaVideo"] = I("M2.5 3.5H13.5V12.5H2.5Z M6.5 6L10 8L6.5 10Z"),
        ["Icon.DriveLarge"] = I("M2.5 8.5H23.5V17.5H2.5Z M18 13H20"),
        ["Icon.NetworkLarge"] = I("M3.5 4.5H22.5V16.5H3.5Z M13 16.5V20.5 M8 21.5H18"),

        ["Icon.ChevronLeft"] = I("M10 3.5L5.5 8L10 12.5"),
        ["Icon.ChevronRight"] = I("M6 3.5L10.5 8L6 12.5"),
        ["Icon.Find"] = I("M2.8 7A4.2 4.2 0 1 0 11.2 7A4.2 4.2 0 1 0 2.8 7Z M10.2 10.2L13.5 13.5"),
        ["Icon.Bookmark"] = I("M4.5 2.5L11.5 2.5L11.5 13.5L8 10.5L4.5 13.5Z"),
        ["Icon.Panels"] = I("M2 3L14 3L14 13L2 13Z M8 3L8 13"),
        ["Icon.Gear"] = I("M13.06 6.43L14.9 6.82L14.9 9.18L13.06 9.57L12.69 10.47L13.72 12.04L12.04 13.72L10.47 12.69L9.57 13.06L9.18 14.9L6.82 14.9L6.43 13.06L5.53 12.69L3.96 13.72L2.28 12.04L3.31 10.47L2.94 9.57L1.1 9.18L1.1 6.82L2.94 6.43L3.31 5.53L2.28 3.96L3.96 2.28L5.53 3.31L6.43 2.94L6.82 1.1L9.18 1.1L9.57 2.94L10.47 3.31L12.04 2.28L13.72 3.96L12.69 5.53Z M5.8 8A2.2 2.2 0 1 0 10.2 8A2.2 2.2 0 1 0 5.8 8Z"),
        ["Icon.Advanced"] = I("M1.5 4.5H4 M7.5 4.5H14.5 M4 4.5A1.75 1.75 0 1 0 7.5 4.5A1.75 1.75 0 1 0 4 4.5Z M1.5 11.5H8.5 M12 11.5H14.5 M8.5 11.5A1.75 1.75 0 1 0 12 11.5A1.75 1.75 0 1 0 8.5 11.5Z"),
        ["Icon.GoTo"] = I("M2.5 8H11.5 M8.5 4.5L12 8L8.5 11.5 M14 3V13"),

        ["Icon.Save"] = I("M3 2.5H11L13.5 5V13.5H3Z M5.5 2.5V5.5H9.5V2.5 M5.5 13.5V9.5H10.5V13.5"),
        ["Icon.Undo"] = I("M5.5 4L2.5 7L5.5 10 M2.5 7H9.5A3.5 3.5 0 0 1 9.5 14H7"),
        ["Icon.Redo"] = I("M10.5 4L13.5 7L10.5 10 M13.5 7H6.5A3.5 3.5 0 0 0 6.5 14H9"),
        ["Icon.Replace"] = I("M2.5 4.5H9.5 M2.5 8H6.5 M9 10.5L11 12.5L14 8.5 M11.5 2.5V6.5"),
        ["Icon.Wrap"] = I("M2.5 4H13.5 M2.5 8H11.5A2 2 0 0 1 11.5 12H8 M9.5 10.5L8 12L9.5 13.5 M2.5 12H5.5"),
        ["Icon.Format"] = I("M2.5 3.5H13.5 M5 6.5H13.5 M5 9.5H13.5 M2.5 12.5H13.5"),
        ["Icon.Minify"] = I("M2.5 8H13.5 M5 5L2.5 8L5 11 M11 5L13.5 8L11 11"),
        ["Icon.Preview"] = I("M2 3.5H14V12.5H2Z M8 3.5V12.5 M10 6.5H12.5 M10 9H12.5"),
        ["Icon.LineNumbers"] = I("M3 3.5H4V7.5 M2.5 7.5H4.5 M2.5 10H4.5L2.5 13H4.5 M7 4.5H13.5 M7 8H13.5 M7 11.5H13.5"),
        ["Icon.Contents"] = I("M2.5 3.5H3 M5.5 3.5H13.5 M4.5 8H5 M7.5 8H13.5 M4.5 12.5H5 M7.5 12.5H13.5"),
        ["Icon.SwapSides"] = I("M2.5 5.5H13.5 M11 3L13.5 5.5L11 8 M13.5 10.5H2.5 M5 8L2.5 10.5L5 13"),
        ["Icon.SwapUpDown"] = I("M5.5 13.5V2.5 M3 5L5.5 2.5L8 5 M10.5 2.5V13.5 M8 11L10.5 13.5L13 11"),
        ["Icon.ArrowUp"] = I("M8 13V3 M4 7L8 3L12 7"),
        ["Icon.ArrowDown"] = I("M8 3V13 M4 9L8 13L12 9"),

        ["Icon.ZoomOut"] = I("M3.5 8H12.5"),
        ["Icon.ZoomIn"] = I("M3.5 8H12.5 M8 3.5V12.5"),
        ["Icon.RotateLeft"] = I("M3.5 6.5A5 5 0 1 1 4 11 M3 3V6.5H6.5"),
        ["Icon.RotateRight"] = I("M12.5 6.5A5 5 0 1 0 12 11 M13 3V6.5H9.5"),
        ["Icon.FullScreen"] = I("M2.5 6V2.5H6 M10 2.5H13.5V6 M13.5 10V13.5H10 M6 13.5H2.5V10"),
        ["Icon.Copy"] = I("M6.7 5.5H12.3A1.2 1.2 0 0 1 13.5 6.7V12.3A1.2 1.2 0 0 1 12.3 13.5H6.7A1.2 1.2 0 0 1 5.5 12.3V6.7A1.2 1.2 0 0 1 6.7 5.5Z M10.5 3.5V3A1 1 0 0 0 9.5 2H3.5A1 1 0 0 0 2.5 3V9A1 1 0 0 0 3.5 10H4"),
    };

    private static readonly Dictionary<(Geometry, double), Geometry> _scaled = [];

    private static Icon I(string data) => new([new(Geo(data))]);

    private static Icon F(string data) => new([new(Geo(data), Filled: true)]);

    private static Geometry Geo(string data) => StreamGeometry.Parse(data);

    public static IEnumerable<string> Names => _icons.Keys;

    public static bool TryGet(string name, out Icon icon) => _icons.TryGetValue(name, out icon!);

    public static Icon Get(string name) => _icons[name];

    public static Control? Create(string name, string brush = "Brush.SecondaryText")
        => _icons.ContainsKey(name) ? Glyph(name, 16, 16, 1.25, brush) : null;

    public static Control Glyph(string name, double width, double height, double thickness = 1.4, string brush = "Brush.SecondaryText", bool round = true)
    {
        var box = BoxOf(name);
        var (dx, dy) = (Math.Min(0, (width - box) / 2), Math.Min(0, (height - box) / 2));
        var paths = Get(name).Parts.Select(part => Path(part, width, height, thickness, brush, round)).ToList();
        if (dx != 0 || dy != 0)
        {
            foreach (var path in paths)
            {
                path.RenderTransform = new TranslateTransform(dx, dy);
            }
        }
        if (paths.Count == 1)
        {
            paths[0].VerticalAlignment = VerticalAlignment.Center;
            return paths[0];
        }

        var panel = new Panel { Width = width, Height = height, VerticalAlignment = VerticalAlignment.Center };
        panel.Children.AddRange(paths);
        return panel;
    }

    public static Control Large(string name, double size, double thickness, IBrush brush)
    {
        var panel = new Panel { Width = size, Height = size };
        foreach (var part in Get(name).Parts)
        {
            var path = new Avalonia.Controls.Shapes.Path { Data = Scaled(part.Geometry, size / 16), Width = size, Height = size };
            if (part.Filled)
            {
                path.Fill = brush;
            }
            else
            {
                path.Stroke = brush;
                path.StrokeThickness = thickness;
                path.StrokeLineCap = PenLineCap.Round;
                path.StrokeJoin = PenLineJoin.Round;
            }

            panel.Children.Add(path);
        }

        return panel;
    }

    private static double BoxOf(string name) => name switch
    {
        "Icon.TitleChevron" or "Icon.CaptionMinimize" or "Icon.CaptionMaximize" or "Icon.CaptionClose" => 10,
        "Icon.DriveLarge" or "Icon.NetworkLarge" => 26,
        _ => 16,
    };

    private static Avalonia.Controls.Shapes.Path Path(Part part, double width, double height, double thickness, string brush, bool round)
    {
        var path = new Avalonia.Controls.Shapes.Path { Data = part.Geometry, Width = width, Height = height };
        if (part.Filled)
        {
            return path.Res(Shape.FillProperty, brush);
        }

        path.StrokeThickness = thickness;
        if (round)
        {
            path.StrokeLineCap = PenLineCap.Round;
            path.StrokeJoin = PenLineJoin.Round;
        }

        path.StrokeDashArray = part.Dashed ? [1.6, 1.3] : null;
        return path.Res(Shape.StrokeProperty, brush);
    }

    public static void Draw(DrawingContext context, string name, Point origin, IPen pen, IBrush? fill = null, double scale = 1)
    {
        using (context.PushTransform(Matrix.CreateTranslation(origin.X, origin.Y)))
        {
            foreach (var part in Get(name).Parts)
            {
                var geometry = scale == 1 ? part.Geometry : Scaled(part.Geometry, scale);
                if (part.Filled)
                {
                    context.DrawGeometry(fill ?? pen.Brush, null, geometry);
                }
                else
                {
                    context.DrawGeometry(null, pen, geometry);
                }
            }
        }
    }

    private static Geometry Scaled(Geometry geometry, double scale)
    {
        if (!_scaled.TryGetValue((geometry, scale), out var scaled))
        {
            scaled = geometry.Clone();
            scaled.Transform = new ScaleTransform(scale, scale);
            _scaled[(geometry, scale)] = scaled;
        }

        return scaled;
    }

    public static void DrawMark(DrawingContext context, double size, bool dev = AppBuild.IsDev)
    {
        var small = size <= 24;
        var scale = size / (small ? 16 : 256);
        using (context.PushTransform(Matrix.CreateScale(scale, scale)))
        {
            var box = small ? _markBoxSmall : _markBoxLarge;
            context.DrawGeometry(small ? (dev ? _devSmall : _blueSmall) : (dev ? _devLarge : _blueLarge), null, box);
            foreach (var (geometry, brush) in small ? _markSmall : _markLarge)
            {
                context.DrawGeometry(brush, null, geometry);
            }

            if (dev && small)
            {
                using (context.PushGeometryClip(box))
                {
                    context.DrawGeometry(_devPink, null, _devCorner);
                }
            }
            else if (dev)
            {
                context.DrawGeometry(_devPink, new ImmutablePen(Brushes.White, 8), _devBadge);
                context.DrawGeometry(null, new ImmutablePen(Brushes.White, 7, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round), _devLetters);
            }
        }
    }

    public static Control Mark(double size, bool dev = AppBuild.IsDev) => new MarkView { Dev = dev, Width = size, Height = size, VerticalAlignment = VerticalAlignment.Center };

    private static readonly Color _markBlue = Color.Parse("#0F6CBD");
    private static readonly IBrush _cursorGrey = new ImmutableSolidColorBrush(Color.Parse("#2F3237"));
    private static readonly IBrush _rowDim = new ImmutableSolidColorBrush(Colors.White, 0.55);
    private static readonly IBrush _devPink = new ImmutableSolidColorBrush(Color.Parse("#FF3D7F"));

    private static readonly Geometry _markBoxSmall = Box(0.5, 0.5, 15, 15, 3.5);
    private static readonly Geometry _markBoxLarge = Box(16, 16, 224, 224, 52);
    private static readonly IBrush _blueSmall = new ImmutableSolidColorBrush(_markBlue);
    private static readonly IBrush _devSmall = new ImmutableSolidColorBrush(Color.Parse("#2C1D5A"));
    private static readonly IBrush _blueLarge = Diagonal("#2B88D8", "#0B5A9E");
    private static readonly IBrush _devLarge = Diagonal("#3B2A6E", "#1E1440");
    private static readonly Geometry _devCorner = Geometry.Parse("M8.5 15.5L15.5 15.5L15.5 8.5Z");
    private static readonly Geometry _devBadge = Box(128, 176, 116, 64, 18);
    private static readonly Geometry _devLetters = Geometry.Parse("M148 195V221H156A12 13 0 0 0 156 195Z M196 195H176V221H196 M176 208H192 M204 195L214 221L224 195");

    private static readonly (Geometry, IBrush)[] _markSmall =
    [
        (Box(2.5, 3, 5, 2, 1), Brushes.White),
        (Box(2, 6.5, 6, 3, 1), _cursorGrey),
        (Box(2.5, 11, 4, 2, 1), Brushes.White),
        (Box(9, 3, 4.5, 2, 1), _rowDim),
        (Box(9, 7, 3.5, 2, 1), _rowDim),
        (Box(9, 11, 4, 2, 1), _rowDim),
    ];

    private static readonly (Geometry, IBrush)[] _markLarge =
    [
        (Box(48, 62, 72, 20, 10), Brushes.White),
        (Box(40, 96, 88, 30, 12), _cursorGrey),
        (Box(48, 140, 58, 20, 10), Brushes.White),
        (Box(48, 174, 66, 20, 10), Brushes.White),
        (Box(140, 62, 68, 20, 10), _rowDim),
        (Box(140, 100, 50, 20, 10), _rowDim),
        (Box(140, 140, 62, 20, 10), _rowDim),
        (Box(140, 174, 44, 20, 10), _rowDim),
    ];

    private static ImmutableLinearGradientBrush Diagonal(string from, string to)
        => new([new ImmutableGradientStop(0, Color.Parse(from)), new ImmutableGradientStop(1, Color.Parse(to))],
            startPoint: new RelativePoint(0, 0, RelativeUnit.Relative), endPoint: new RelativePoint(1, 1, RelativeUnit.Relative));

    private static RectangleGeometry Box(double x, double y, double width, double height, double radius)
        => new(new Rect(x, y, width, height), radius, radius);

    private sealed class MarkView : Control
    {
        public bool Dev { get; init; }

        public override void Render(DrawingContext context) => DrawMark(context, Math.Min(Bounds.Width, Bounds.Height), Dev);
    }
}

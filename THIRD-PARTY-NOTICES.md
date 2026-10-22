<!--
  Salandual - a keyboard-first, dual-pane file manager for Windows.
  Project : https://github.com/TomEvin/salandual
  Author  : TomEvin (https://github.com/TomEvin)
  Copyright (C) 2026 TomEvin
  License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
  SPDX-License-Identifier: GPL-3.0-or-later
  Source  : THIRD-PARTY-NOTICES.md
-->
# Third-party notices

Salandual is licensed under GPL-3.0-or-later. It uses the components below. This file is updated whenever a dependency is added.

## Runtime dependencies

| Component | Version | License | Source |
|---|---|---|---|
| Avalonia, Avalonia.Desktop, Avalonia.Themes.Fluent | 12.1.3 | MIT | https://github.com/AvaloniaUI/Avalonia |
| .NET runtime and base libraries | 10.0 | MIT | https://github.com/dotnet/runtime |
| Microsoft.Windows.CsWin32 (build-time source generator; generated interop code is compiled in) | 0.3.335 | MIT | https://github.com/microsoft/CsWin32 |
| Magick.NET-Q8-x64, Magick.NET.Core (image viewer: JPEG XL, PSD, SVG, TGA, camera RAW and the fallback decoder; loaded on the first image that needs it) | 14.17.1 | Apache-2.0 | https://github.com/dlemstra/Magick.NET |
| ImageMagick and the libraries built into Magick.Native (libheif, libde265, libjxl, libraw, librsvg, libwebp, libjpeg-turbo, libpng, libtiff, OpenJPEG, OpenEXR, lcms2, cairo, pango, freetype, zlib, …) | with Magick.NET 14.17.1 | ImageMagick License (Apache-2.0 style); the libraries under their own licenses (BSD, MIT, zlib, LGPL-2.1-or-later, …), all GPLv3-compatible | https://github.com/dlemstra/Magick.Native |
| BCnEncoder.NET (DDS block formats) | 2.3.0 | MIT OR Unlicense | https://github.com/Nominom/BCnEncoder.NET |
| CommunityToolkit.HighPerformance (used by BCnEncoder.NET) | 8.4.0 | MIT | https://github.com/CommunityToolkit/dotnet |
| Microsoft.Bcl.Numerics (used by BCnEncoder.NET) | 10.0.3 | MIT | https://github.com/dotnet/runtime |
| MetadataExtractor (photo information: EXIF, IPTC, XMP, GPS, ICC) | 2.9.3 | Apache-2.0 | https://github.com/drewnoakes/metadata-extractor-dotnet |
| XmpCore (XMP parsing, used by MetadataExtractor; a port of Adobe's XMP Toolkit) | 6.1.10.1 | BSD-3-Clause (Adobe XMP Toolkit) | https://github.com/drewnoakes/xmp-core-dotnet |
| MediaInfo (MediaInfo.dll: media information, streams, codecs, tags, cover art; MediaArea's own signed x64 build, taken from the MediaInfoDLL 26.1.0 package and checked at build time against the SHA-256 of MediaArea's download; loaded only when the first media file is opened) | 26.01 | BSD-2-Clause | https://github.com/MediaArea/MediaInfoLib |
| AvaloniaEdit (the editor, `F4`: the text area, undo, line numbers) | 12.0.0 | MIT | https://github.com/AvaloniaUI/AvaloniaEdit |
| YamlDotNet (the editor: checking YAML and keeping Format's meaning; only its parser) | 18.1.0 | MIT | https://github.com/aaubry/YamlDotNet |
| Markdig (Markdown viewer: Markdown to HTML, CommonMark and the GitHub extras) | 1.4.0 | BSD-2-Clause | https://github.com/xoofx/markdig |
| Avalonia.Controls.WebView (Markdown viewer: the page, shown by the WebView2 engine that comes with Windows; nothing of the engine is shipped) | 12.1.0 | MIT | https://github.com/AvaloniaUI/Avalonia |

## Colour themes

The shipped themes take their colours from each theme's published palette or theme files. No code of theirs is included.

| Theme | License | Source |
|---|---|---|
| VS Code's own themes (Dark Modern, Dark+, Light+, the 2026 and high-contrast themes, Abyss, Kimbie Dark, Monokai, Monokai Dimmed, Quiet Light, Red, Solarized Dark and Light, Tomorrow Night Blue), read from VS Code 1.141.2's theme files | MIT (Microsoft; Solarized by Ethan Schoonover, Tomorrow by Chris Kempson, Kimbie by Jan T. Sott, all MIT) | https://github.com/microsoft/vscode |
| Dracula | MIT | https://github.com/dracula/dracula-theme |
| Nord, Nord Light (from the Nord palette) | MIT | https://github.com/nordtheme/nord |
| Gruvbox Dark and Light | MIT | https://github.com/morhetz/gruvbox |
| Catppuccin Latte and Mocha | MIT | https://github.com/catppuccin/catppuccin |
| Tokyo Night | MIT | https://github.com/enkia/tokyo-night-vscode-theme |
| One Light (Atom) | MIT | https://github.com/atom/atom |
| GitHub Light and Dark (Primer) | MIT | https://github.com/primer/github-vscode-theme |

## Test-only dependencies (not shipped)

| Component | Version | License | Source |
|---|---|---|---|
| xunit.v3, xunit.runner.visualstudio | 3.2.2 / 3.1.5 | Apache-2.0 | https://github.com/xunit/xunit |
| Microsoft.NET.Test.Sdk | 18.10.1 | MIT | https://github.com/microsoft/vstest |
| Avalonia.Headless.XUnit | 12.1.3 | MIT | https://github.com/AvaloniaUI/Avalonia |

## Reference material

Contains menu and keyboard tables and help text converted from **Open Salamander** (https://github.com/OpenSalamander/salamander) and **Samandarin** (https://github.com/KRtkovo-eu-AI/salamander, a fork of Open Salamander). Both are licensed GPL-2.0-or-later and used here under GPL-3.0-or-later.

No code from those projects is included in `src/`. If any part ever is, it keeps its original copyright notice and is listed here.

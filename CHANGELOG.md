<!--
  Salandual - a keyboard-first, dual-pane file manager for Windows.
  Project : https://github.com/TomEvin/salandual
  Author  : TomEvin (https://github.com/TomEvin)
  Copyright (C) 2026 TomEvin
  License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
  SPDX-License-Identifier: GPL-3.0-or-later
  Source  : CHANGELOG.md
-->
# Changelog

All notable changes to Salandual are listed here, newest first. The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and the versions follow [Semantic Versioning](https://semver.org/). While the major version is 0, a minor version may still change how something works.

## [0.1.0] - 2026-10-22

The first release. Written **v0.1** in the app and on the download page.

### Added

**Panels and navigation**
- Two panels with a splitter, breadcrumb directory line, information line, drive strip and hot-path bar, and an F-key bar that follows `Shift`, `Ctrl` and `Alt`.
- Salamander's key map as the default, and a Total Commander key map; quick search, selection by mask, extension and name, folder sizes, hidden files.
- Drive menu with free space, Drive information, 30 hot paths, Change directory with completion and history, working directories, back and forward, session restore.

**File operations**
- Copy, Move, Delete, New folder, Quick rename and Duplicate, with checks before they start, a conflict dialog, a per-disk job queue and an operations panel (speed graph, pause, speed limit, error cards).
- Recycle Bin by default, long paths, alternate data streams, Explorer-compatible clipboard and drag and drop, the Windows context menu, Properties with Signature, Security and Previous versions.
- *When the queue is done* (close, lock, sleep, hibernate, shut down), taskbar progress and a notification.

**Viewers and editor**
- Streaming text and hex viewer for files of any size; colours for JSON, YAML, XML, HTML, CSS, JavaScript and Markdown; a Tree view for JSON, JSON Lines, YAML and XML.
- Rendered Markdown, Jupyter notebooks (never run) and HTML pages.
- Image viewer for the common formats plus AVIF, WebP, HEIC, JPEG XL, camera RAW, PSD, SVG, TGA and DDS, with photo information; media information for video and audio files.
- Editor for text, JSON, YAML, XML and Markdown with checking while you type, Format, Minify, folds, find and replace, a live preview, safe saving and external editors per extension.

**Search and compare**
- Search by name, several texts, regular expression, hex, size, date and attributes, also inside ZIP files; saved searches; Find duplicates.
- Compare directories with a list of differences, a file comparator with copy-across arrows, an external comparator option, Make file list and Occupied space.

**Look and settings**
- Themes (light, dark, Salamander Classic, high contrast, popular palettes, VS Code's themes), your own themes, a schedule that follows Windows' mode, accent colours, background pictures.
- Options with search, export, import and reset of settings, a first-start wizard, an optional *Version in the title*, a portable mode (`portable.flag`).

### Not in this release

Archives, tabs, user menu and command line, remote file systems (FTP, SFTP), plugins, other languages than English, import from Altap Salamander, an installer and automatic updates. See the [README](README.md#what-is-not-in-v01).

[0.1.0]: https://github.com/TomEvin/salandual/releases/tag/v0.1

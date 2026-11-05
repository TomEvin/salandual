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

## [Unreleased]

## [0.8.0] - 2026-11-05

The second release. Written **v0.8** in the app and on the download page.

### Added

**Git in the panels** (the Git view, step 1)
- In a folder inside a Git repository, the directory line shows the branch with ↑ commits to push and ↓ to pull, where the free space was. I compare with the upstream as Git last fetched it; nothing is asked online.
- A row is colored by its Git state, the name and its other columns alike (size, date, the Git letter, a tile's lines): changed, staged, new, deleted and conflicted in the theme's colors, ignored ones dimmed, and a folder colored when something inside it changed. The information line says the state in words. The new Git column appears by itself in a folder inside a repository and goes away elsewhere (Options › Panels › Git; off, the column chooser decides). It shows Git's letter on a tag of its state's color. A file changed in another program changes color by itself.
- A click on the branch, or Ctrl+Shift+G, opens the branch menu: *Switch to* another branch (or one of the remote's you don't have yet), and *New branch…*. Switching and making a branch use your installed Git, so your Git settings work as usual; without Git they are greyed, saying why.
- I read repositories with LibGit2Sharp, inside Salandual: a repository never makes me run a program, a hook or a filter. Git itself runs only for what you ask, by its full path, never through a shell. Only on this PC's own drives at first (Options › Panels › Git: also on network drives); cloud folders and archives get nothing.

**Git's changes** (the Git view, step 2)
- Ctrl+Shift+G, then C (or *Changes* in the branch menu) lists the repository's changed files in the panel, by their path from its top, under *Staged*, *Changed*, *New* and *Conflicted*, with the lines added and removed. Your keys, Insert and Enter work as in any panel, and the list follows changes made anywhere.
- Ctrl+Shift+S stages the selected files (or the focused one), Ctrl+Shift+U unstages them, here or in any folder of the repository. *Discard the Git changes* always asks, and each file as it is goes to the Recycle Bin first, so a wrong discard can be undone.
- F3 on a change compares it with the last commit in the file comparator: the commit on the left, read-only, your copy (or what is staged) on the right. F7 goes to the next change and Ctrl+Shift+S stages the file.
- Ctrl+Shift+G, then M commits: the message with its summary counted, the last messages on Alt+Down, what goes in (with nothing staged, all the changes), *Amend the last commit*, and who commits from your Git settings. Git commits and runs the repository's hooks, as it always does; when it refuses, its words stay in the dialog.
- Staging, discarding and committing run your installed Git by its full path; each file's name goes to it literally, never as a pattern or an option.

**Git's history** (the Git view, step 3)
- Ctrl+Shift+G, then H lists the repository's commits in the panel, newest first, with their authors and dates; with an upstream, the ones not pushed yet come first. Ctrl+Shift+G, then F shows only the commits that changed the focused file or folder.
- Enter on a commit lists the files it changed with their lines added and removed. F3 compares a file with the commit before, Alt+F3 shows it as it was, and Backspace goes back to History.
- Ctrl+Shift+G, then G opens the graph in a window of its own: each branch a line, each commit a dot, merges and branch points where they happen, the branch and tag names beside each summary, *Branches: All / This one* and *First parent only*. The commit you move to shows its message, author, parents and files, F3 shows a file's diff, and Enter opens the commit in the panel.
- It all only reads, inside Salandual; nothing is asked online.
- Git's colors can be set on their own, in Options › Themes › Editor › Git: changed, staged or added, new, deleted, conflicted and ignored. Until you set them they follow the theme's warning, success, accent and danger colors, as before; a VS Code theme you import brings its own Git colors.

**Fetch, pull, push and clone** (the Git view, step 4)
- Ctrl+Shift+G, then T fetches, L pulls and P pushes (also in the branch menu and in Commands). Each is a job in the operations queue, with Git's own progress on its line (*Receiving objects: 64%*). A notice when it's done gives Git's last words, such as *Already up to date.* Two jobs on one repository wait for each other.
- Pull does what your Git settings say (merge or rebase). When it stops on a conflict, the notice says so and *Show the changes* opens Changes with the conflicted files. When Git refuses (a password, a push that needs a pull first), the job stops with Git's words.
- A branch without an upstream is pushed to origin under its own name and gets it as its upstream. The Commit dialog has *Push afterwards*.
- Ctrl+Shift+G in a folder outside any repository, or Commands › Clone a Git Repository…, opens *Clone a repository*: the address, and the folder it goes into (named after the address, in the other panel's folder), and *Open it in this panel when done*. I take only https, http, ssh and git addresses, or user@host:path, and refuse one with a password in it. The folder must be new or empty.
- Your installed Git does it all, by its full path, never through a shell. Passwords and keys stay with Git's credential manager and SSH, which ask in their own windows: I never see them.
- While one runs, a notice says so (*Pull garden-planner…*) with Git's progress line and a bar, *Stop* and *Show*; the notice of how it ended replaces it, and a stopped one says it was stopped.

**An old version, whole** (the Git view, step 3b)
- A commit's files end with *Browse this version*: Enter on *The whole tree at a41f2c9* (or Ctrl+Shift+G, then B, on a commit in History; elsewhere it opens the last commit's) shows every file and folder as they were in that commit, read-only, one folder at a time. Backspace goes up, and from the top back to where you came from.
- F3 (or Enter) shows a file as it was and Alt+F3 opens it in the other viewer. The files the commit changed show its lines and Git's color.
- F5 copies the selected files and folders out, as they were, with the usual Copy dialog to the other panel (its conflicts and progress). Nothing can be changed, moved, deleted or copied into an old version; a press says so, and the F-key bar greys those keys. A name Windows can't hold (CON, a name with a colon) is left out and the notice says how many.
- Only reading, inside Salandual; nothing goes into the repository and nothing runs.

**FTP and SFTP** (Phase 3, step 1: browse and copy from a server)
- Connect to a server (Ctrl+Shift+F, or Commands › Connect to a Server…): SFTP, FTP that tries TLS first, FTPS, or plain FTP. Your servers are kept as bookmarks, without passwords. You can also type an address such as `sftp://user@server/path` in Change directory or the directory line.
- I check a server's key before anything is sent. A new key shows its fingerprint with *Trust and remember*, *Trust this once* or *Cancel*. A key that changed since you trusted it is always refused. Keys in your `.ssh\known_hosts` are trusted too, and I never write that file.
- I ask for the password when you connect. *Remember the password* keeps it in Windows Credential Manager, for your Windows account on this PC only, never in my settings, exports or logs. SFTP also signs in with a key file (OpenSSH or PuTTY .ppk), asking its passphrase only when it has one.
- FTP to a server without TLS asks before a password goes in plain text, once per bookmark, and the directory line then says *not encrypted* in orange.
- The server opens in the panel like a folder, with a lock and SFTP or FTPS in the directory line and a Rights column. F3 and Enter view a copy of a file. F5 copies files and folders to your disk in the progress window, marked as downloaded.
- The drive menu has *FTP and SFTP*: your bookmarks, the open servers, *Connect to a server…*, *Show the server's log* and *Disconnect*. F12 on a server disconnects. The log is in memory only and never shows a password.
- Total Commander's keys: Ctrl+F and Ctrl+N connect, Ctrl+Shift+F disconnects.

**Writing to a server** (FTP and SFTP, steps 2 and 3)
- F5 and F6 both ways, with the copy dialog's options and the queue; Resume for a file that stopped part way; F6 within a server, F7, F2, and F8 always asked; Permissions for a file on the server.
- F4 and Shift+F4 on a server's file upload it again after asking.
- Settings for each bookmark, or the defaults in Options: passive or active, keep alive, time-outs and retries, copies at once, a proxy (its password only in Credential Manager), TLS versions, ASCII mode, compression, the list command, encoding, time zone, hidden files, the listing cache, and signing in through the SSH agent or Pageant.
- Options lists the saved sign-ins and trusted keys, with Forget. Servers can be on the drives strip, in hot paths and in tabs.

**The drive menu**
- A Cloud group with the cloud folders found by themselves (OneDrive personal and work, Dropbox, Google Drive, iCloud Drive, Box, pCloud, MEGA, Nextcloud and others) and *Add a cloud folder*.
- The drives' space as Free, Free of total, Total or nothing; closer rows as an option; a menu taller than its room scrolls; a whole group hidden from its heading; the 3 servers used last.
- Bars in three heights (Lower, Normal, Taller), in Options and First steps.

**Import from Altap Salamander**
- Options › Import from Altap Salamander reads its settings from your account (read only) or from a `.reg` file, and shows them part by part before anything changes, as Import settings does.
- Hot paths go to their own slots (a slot of yours stays yours), with the panels, bars, confirmations, compare options, highlight rules and FTP bookmarks (without passwords); PictView and the other viewers map to mine. Programs, the user menu and history are offered but not ticked. Passwords are skipped unread.

**Torrent files** (the viewer)
- F3 on a `.torrent` shows the name, size and files, version 1, 2 or hybrid, the pieces, both info hashes, the trackers by tier and web seeds; Files as a tree with a filter; the bencode in the Tree. *Copy magnet link* and *Copy info hash*. Never online.
- *Check the files…* reads the downloaded files as a job in the queue and compares each piece with the torrent's hashes: complete, how much is right, missing or a different size.

**Languages**
- Options › Startup & general › Language: English, Magyar and your language files; a restart switches it (*Restart now* or *Later*). Every text has its Hungarian.
- Language files are JSON in a `languages` folder, beside the settings or beside the program. Each text is checked against the English placeholders; a broken one falls back to English and is listed, with a notice at start. Only words: nothing in a language file is run.
- *Export language file* writes every English text as a new language (`de.json`), in the same form as `hu.json`: a header (format, name, author, contact, copyright, licence, the version it is for, the date it was updated as YYYY-MM-DD), then the texts. Options shows the chosen file's header, and how many texts a file made for an older version lacks, with *Show*.

**Changing keys** (Phase 3, M5 step 3)
- Options › Keyboard: click a command (or Enter or F2 on it) to change its keys. Press a key in its box to hear who has it: a free key is assigned with Enter; a key another command or a user menu item has is reassigned only when you choose *Reassign to …*, and the other command's row says where its key went, with Undo.
- Keys a panel and its boxes need can't be given on their own (letters, digits, Space, arrows, Home, End, Page Up and Down, Enter, Esc, Tab, Backspace, Alt with a menu's letter); each says why.
- Your keys stay on top of either key map, Salamander's or Total Commander's. *Restore Salamander's keys* works for one command or, with Undo, for all; *Only your keys* lists them.
- In Quick open, Ctrl+K on a command opens its row; *Which command has a key?* answers with *Change…*.
- Vim keys moved to the end of the plan.

**Quick open: files on all drives** (Phase 3, M5 step 2)
- As you type, Quick open also finds files and folders on all your local drives, in a group *Files on all drives* after the panel's own files, its best 8 and *All files…* for the rest. A `/` first (or the new *Files* chip) lists only files, up to 200.
- The names come from Everything when it is running (never started by Salandual), else from the Windows index for the places it covers and from Salandual's own walk of the rest of your local drives. Options › Startup & general › Quick open chooses this and says what each source covers on your PC.
- The walk starts when the box opens, so names come in while you type; opened again within 10 minutes, the names found before show at once. It stops at 500,000 entries or 15 seconds (Options), and *Keep searching* goes on from where it stopped. The names stay in memory only.
- Options can leave out hidden files and folders, system files and folders, Windows' own folders, and folders you name, by name anywhere (node_modules) or by full path (D:\Backups); each drive can be switched off. Memory cards, sticks, CDs, network drives, archives and folder links are never walked, and an online-only file is found by its name without downloading it.

**Back and forward**
- ‹ and › go back and forward through the folders a panel has shown, in a bar of their own for the active panel. The bar works like the hot paths and user menu bars: drag it by its grip into another bar's row or the end of the drives' row, or use *Where the bar is* in its right-click menu; *Lock the bars* holds it in place. A button with nowhere to go is greyed. After them comes ↑ (up one folder, as Backspace).
- Options › Layout › Each panel › *Back and forward buttons*: a bar of their own (at first), in each panel's directory line (each for its own panel), both, or off. The View menu (*Options › Show › Back and Forward Bar*) switches the bar.
- The small ▾ beside ‹ and › lists the steps, nearest first, and closes again when clicked a second time; a click or a digit jumps there, and the folders passed over can be gone through again with ›. *The list of steps opens with* holding the button instead (half a second, as in a browser). *Steps listed* shows both panels' steps, marked left or right; one of the other panel's opens in the active one. *Clear the steps* forgets them.
- The mouse's side buttons go back and forward in the panel under the mouse (Options: *Mouse side buttons go back and forward*).
- *Steps back kept*: how many steps each panel keeps, 10 to 200 (50 at first).
- First steps › Bars and layout has the back and forward bar.

**Quick open** (Phase 3, M5 step 1)
- Ctrl+Shift+P opens one box at the top of the window that finds commands (with their keys), folders, files, tabs, settings and a typed path. Before you type it shows the commands you run most, the folders and files you opened lately and your tabs.
- The letters you type are matched in order, a letter at the start of a word counting most: *dwn* finds Downloads. A first character narrows it to one kind: `>` commands, `@` tabs, `,` settings, a drive or `\` a path; the chips over the list do the same, and Tab goes from one kind to the next.
- Files below the panel's folder are read by name as you type, with a limit (50,000 entries or 3 seconds); links and network drives aren't entered.
- Enter opens a folder (Ctrl+Enter in the other panel, Alt+Enter in a new tab) and takes the panel to a file with it focused (F3 views it, F4 edits it). Options › Startup & general › Quick open can make Enter open a file instead, turn off what it remembers, or forget it.
- The folders and files you open and the commands you run are remembered to put them first: paths and names only, kept with your other history on this PC.

**Network** (Phase 3, M4 step 3)
- Ctrl+, (or Network in the drive menu) opens Network in a panel: the computers Windows finds on your network, your network drives with the folder each is connected to, and the servers you opened before. Options › Hot paths & drives puts Network in the drive strip too.
- Enter on a computer lists its shares with their comments; the hidden ones (C$) show only while hidden files are shown. The directory line reads *Network › NAS-01 › Media*, and Backspace goes from a share's root up to its server and from a server up to Network, the one you came from focused.
- A share or a server that wants a password brings up Windows' own sign-in. The password goes to Windows, and Windows keeps it only when you tick *Remember my credentials* there; Salandual never sees or stores it.
- F11 connects a network drive: the first free letter, the folder (completed from your history, the computers found and a server's shares), *Reconnect at sign-in*, *Connect as another user* (Windows asks). It starts with the focused share or computer, and the panel opens the new drive.
- F12 lists your network drives to tick, the panel's own ticked, with how many files are open on one; a drive with files open is disconnected only after you confirm. A panel showing that drive goes to This PC first.
- Ctrl+Shift+F9 lists this PC's shares with the folder each one shares; Enter opens the folder.
- Right-click on the drives strip › *Places in the drives strip* switches each place on or off there (This PC, Home, Libraries, Recycle Bin, your folders, Network); a place's chip has *Hide from the bar*, and This PC, Home, the Recycle Bin and Network have their own icons. *Icons only* there (or in Options › Hot paths & drives) leaves the names to the tooltips.

**Windows' places** (Phase 3, M4 step 2)
- This PC, Home, Libraries and the Recycle Bin open in a panel, from the drive menu (Alt+F1) or This PC's chip in the drive strip; Options › Hot paths & drives switches each on or off in the menu and the strip.
- This PC lists your folders, then the drives with a bar of how full each is, the free space and the size; Enter opens one, and Backspace on a drive's root comes back to This PC with the drive focused.
- Home lists Windows' Quick access folders and Libraries each library's folders, with the folder each is in.
- The Recycle Bin shows each item's original folder and when it was deleted. The right-click menu restores the selected items, deletes them for good (F8, asking first), copies or moves them out into the other panel's folder (F5, F6) or empties the Recycle Bin (asking first).
- Files › New Item… (Ctrl+Shift+N, and *New* in the right-click menu of a folder's empty space): your own templates first (any folder you choose), then Folder, a shortcut to the other panel's item, and Windows' list of new files your programs added. The new item is focused with its name ready to type.
- Go to shortcut target, in a shortcut's right-click menu and in Commands: the panel opens the folder of what the shortcut points to, with it focused (shortcuts to files too).
- Shift+F3 opens Explorer on the panel's folder with the focused or selected items selected; in an archive, the archive's folder with the archive selected; in a place, that place.

**Tabs** (Phase 3, M4 step 1)
- Each panel has tabs, each tab its own folder, view, sort, filter, history and selection. Ctrl+T opens a new tab in the same folder, Ctrl+W closes it, Ctrl+Tab and Ctrl+Shift+Tab go from tab to tab, Ctrl+Shift+T opens the last closed tab again. Left/Right › Tabs has them all.
- The strip shows above a panel's drives and path from two tabs (or always): + opens a tab, a middle click closes one, a double click on the empty strip opens one, a tab dragged along the strip moves, dragged onto the other panel goes there, and files dropped on a tab go into its folder.
- A tab's menu: Duplicate, Lock (a locked tab can't be closed; as a setting it also stays in its folder), a color (a bar along its top, or the whole tab tinted), Move or Copy to the other panel, Close the others, Reopen closed tab.
- The tabs come back when Salandual starts, each with its folder, view, sort and filter; a folder that is gone opens its nearest parent. More tabs than fit shrink, then the strip scrolls (‹ › and ⌄ for the list), or they wrap onto more rows. Options › Panels › Tabs.
- Enter on a shortcut to a folder opens that folder in the panel.

**Archives** (Phase 3, steps 1 to 4)
- Archives open in the panel like folders, read only: Enter on a ZIP, 7z, RAR, TAR and its compressions, ISO, CAB, WIM, DMG and more; Ctrl+PgDn looks inside any file 7-Zip can read (a `.docx`, an installer). Archives inside archives and split archives open too.
- The directory line shows the archive's format and what it holds; a Packed column and the packed size on the information line.
- F3 views and F4 shows a copy of an entry; Properties of an entry; a password is asked for encrypted entries and hidden names, kept only while you are in that archive and never written anywhere.
- Reading a large archive shows its progress on the notice line, with Stop.
- 7-Zip's own `7z.dll` (26.04) reads the archives; its licence is in About and next to the exe.
- F5 copies out of an archive, and Unpack (Alt+F9) unpacks whole archives or their selected entries, as jobs in the operations panel: a folder for the archive only when it's needed, a mask, the archive's folders kept or not, the archive to the Recycle Bin afterwards if you want.
- Every name in an archive is checked before anything is written: names leading out of the target, full paths, device names and hidden streams are refused, links are made only inside the target, and what was left out is listed with its reason. An archive that would fill a disk asks first (the limits are in Options › Operations).
- Test archive (Alt+Shift+F9) reads an archive through and lists damaged files. Ctrl+C and dragging out of an archive work too.
- Pack (Alt+F5) packs files and folders into a ZIP, 7z, TAR, TAR.GZ or TAR.XZ archive as a job in the operations panel: four levels, a password with AES-256 (a 7z can hide its names too), volumes, files that are already compressed stored as they are, and the packed files to the Recycle Bin afterwards if you want. The archive is written under a temporary name and only then put in place; one with a password is tested with the password asked again. An archive that is already there asks whether to add to it, replace it or make a new one.
- ZIP, 7z and TAR archives can be changed in the panel: F5 and F6 (and dropping files) copy or move files into them, F6 moves out of them, F7 makes a folder, F2 renames, F8 deletes (always asking: there is no Recycle Bin inside an archive). F4 edits a copy, and saving asks to put it back. Each change writes the archive anew and replaces the old one only when it is complete. RAR, ISO and the like, archives inside archives and archives in volumes stay read only and say why.
- Options › Operations › Archives › Pack: the format Pack starts with (or the last one used), where the archive goes (beside the files or in the other panel's folder), and what happens when it is already there; Inside an archive: whether a file edited with F4 goes back when saved.
- Find (Alt+F7) looks inside every kind of archive with *Inside archives*, by name and by content (it was ZIP only); a found file opens, is viewed or edited from a copy, and Ctrl+J opens the archive in the panel at it. Encrypted files are found by name only, and nothing asks for a password. Find duplicates looks inside archives too.
- Compare directories works with an archive open in one of the panels, also by content.

**User menu** (Phase 3, M2 step 1)
- F9 opens your own menu of programs and PowerShell scripts, with submenus and separators; Options › User menu edits it, with a preview of what an item would start for the focused file.
- Salamander's variables (`$(FullName)`, `$(ListOfSelectedFullNames)`, `$(FileToCompareLeft)` and the rest, `$[NAME]` for an environment variable). The arguments are split before the variables are filled in, so a file name is always one argument and can never add a command. A script gets the files in `$Files`, never in its text.
- An item can say what it is for (files or folders, names, a kind of file, how many) and is left out (or greyed) where it doesn't fit; it runs once for each file, one after another in the operations panel, or once for all; *Ask first* shows what will run; it can have its own key and an icon, and be in the right-click menu (at the top, or under *User menu ›*).
- An imported or shared settings file brings its user menu only when its Programs are ticked.

**User menu bar** (Phase 3, M2 step 2)
- The items ticked *In the user menu bar* get buttons on a bar under the menus; buttons that don't fit go into ». A file dropped on a button runs that item with it; a program dropped on the bar becomes an item.
- The user menu bar and the hot paths bar are blocks: each has a row of its own, or they share one in either order, or they sit at the end of the drives' row when both panels share it. *Where the bar is ›* in their right-click menu, Options › Layout and dragging a bar's grip move them; *Lock the bars* hides the grips.
- A block's length is set by dragging its end line (double-click: as long as its buttons again); the bar after it fills the rest of the row. The length is kept in pixels and shrinks only in a window too narrow for it.
- Options › Layout follows the window from top to bottom: the title bar, each panel (the drives strip is one choice: Off, In each panel, One for both), and what is between and under the panels. The user menu bar's settings and *Lock the bars* are in Options › User menu.
- In a narrow Options window a setting's control goes under its text instead of past the edge of its card.
- Options › User menu: each line of the list shows the item's icon and ends with Change the name, Up, Down and Remove; the name is changed in the line (F2, double-click). New item, submenu and separator are icons at the top of the list, with the icon chooser beside them. Browse and the variables are icon buttons; the variables menu shows them by name (Windows folder, System folder…), only those that mean something in the field. The list no longer jumps when the page is scrolled away and back. The icon, key, right-click menu and the user menu bar are beside the list, everything else under both.
- The command line above the F-key bar: Ctrl+Tab goes to it and back; Enter runs what you typed in PowerShell or Command Prompt in the panel's folder (Alt+Enter with the other *When the command ends*, Shift+Enter is Enter in the panel), and `cd` with a folder moves the panel instead. Ctrl+Enter, Ctrl+Space, Ctrl+[ and Ctrl+] put in the focused name or a panel's folder, quoted for the shell (with Shift, the short names); a name the shell would still read in its own way turns the line red. Ctrl+↑ and Ctrl+↓ go through the earlier commands, Alt+↓ lists them. Other keys work as in the panel.
- Num / opens Windows Terminal, PowerShell 7, Windows PowerShell, Command Prompt or a program of your own in the panel's folder; Shift+Num / as administrator. Options › Command line & shell, and View › Show › Command Line.

**Views** (Phase 3, M3 step 1)
- Each panel can show Brief (Alt+2), Detailed (Alt+3), Icons (Alt+4), Thumbnails (Alt+5) or Tiles (Alt+6), as in Salamander; Alt+wheel goes through them. Every key and the mouse work in each view as in Detailed. Each panel keeps its view after a restart, and Ctrl+U swaps the panels with their views.
- Brief puts the names down a column, then on in the next, in columns as wide as the longest name, and scrolls sideways.
- Thumbnails are made by Windows in the background, as Explorer shows them (pictures, videos with their length, PDFs and documents), the ones in view first; Ctrl+wheel makes them bigger or smaller. An online-only file shows only a thumbnail Windows already has and is never downloaded for one. Nothing is written to the disk: Windows keeps them in its own cache.
- Tiles show a picture as tall as the name and the two lines beside it: size and date, or type and size.
- Left › View and Right › View, and a view button in the directory line that opens the same menu.
- Options › Panels › Views, Thumbnails and Tiles: the view of a new panel, Alt+wheel, the view button, the thumbnail size, thumbnails of everything Windows can show or of pictures and videos only, and the tiles' lines.

**Columns and sizes** (Phase 3, M3 step 2)
- A right-click on the column header chooses the columns: Extension, Type, Size, Items in a folder, Modified, Created, Accessed, Attributes and DOS name; Left › View › Columns has the same. Each view has its own columns, the same in both panels.
- A column's width is set by dragging its edge in the header, and a double-click fits it. Ctrl+N (smart name width) gives Name the rest of the panel; off, Name keeps its width and the panel scrolls sideways under it.
- Items in a folder are counted in the background on local drives; network drives, archives, cloud folders and folder links are never entered for it.
- The Types view (Alt+7): the name with its extension and the type Windows gives it; sorting by Type, Created and Accessed.
- Three views of your own on Alt+8, Alt+9 and Alt+0, made in the new Options › Views page, with the columns, order and widths you choose.
- Options › Panels: sizes Short, in kilobytes or in bytes (Bytes was a unit before and carries over), grouping digits, sizes in the information line always short or as the panel, what a folder shows in Size, relative dates (2 hours ago, yesterday 18:23), the alignment of the columns, long names cut in the middle, row stripes and hidden files dimmed, with a preview. Row stripes are in First steps too.

**The focus look** (Phase 3, M3 step 3)
- The focused item has a soft background, a colour of each theme set apart from the selection, over the whole row or the name only whatever the outline marks; Options › Panels › Focus and selection, the first section of the page, turns it off.
- The focus and the selection can each mark the whole row or only the icon and the name with its extension, as in Salamander, in every view; First steps offers the selection by name only.

**Disk activity** (Phase 3, M3 step 8)
- Each panel's directory line shows how busy its drive is right now, as Task Manager's Active time: a short bar and the percent before the free space; the tooltip has the reading and writing speeds, and a click opens Drive information. Network and cloud drives show nothing rather than 0 %.
- Options › Hot paths & drives turns it off, or on under every drive in the drive bar too (off at first); First steps' *Bars and layout* has it. It is read from Windows' performance counters once a second, only for the drives shown, starting after the window appears, and not while Salandual is minimized or another program is in front (its own dialogs don't count).
- **A progress window for each job you start** (your check of 2026-10-29: a long permanent delete went to the chip with nothing showing it run): Copy, Move, Paste, drops, Delete and the rest open a window after half a second with the job's graph, bars and numbers, *In the background* (Esc), Pause, Cancel and *Close when done*; a delete that stays on one item says so and names the program holding it. Queued jobs stay in the chip. Options › Operations › *While a job runs*: in a window or only in the chip, whether the window blocks Salandual (off at first), closing it when done. The operations panel's *Window* brings a window back.
- **The percent in the progress window's title** (*47 % · Copying 1 032 files*), thicker bars, the big file's own percent, and no empty lines; the operations panel shows the percent with its graph open. **The titles say it**: Copy, Move, Delete, Pack, Unpack, Copy into an archive and New folder show their heading in the title bar, not twice.
- **Two jobs on SSDs at once**: a job started now waits for another on the same disk only on a hard disk (Options › Operations: wait for each other, only on a hard disk, always at once), and a waiting job has *Start now* in the operations panel.
- **Drive information shows the drive's kind**: hard disk, SSD, NVMe, USB stick, external drive, SD card, card reader, CD/DVD or network drive, as a small picture and in words; Options can show the free space on a picture of the drive filling up instead of the ring, flat or in 3D, the used space growing from empty; the 3D drive moves while it works (electricity toward the connector, a spinning platter or disc, a blinking light), unless Windows' animation effects are off. The Change drive list can show each drive's kind too, as a flat or a 3D picture (Options › Hot paths & drives).
- **Enter in confirmations** (Options › Confirmations): OK or Cancel for each kind of question: creating, moving and copying; saving and editing; deleting; closing. To start with, Enter cancels deleting (the Delete dialog too) and presses the action in the others. Replacing while copying skips the file on Cancel. The safety questions (an archive that would fill the disk, the shutdown countdown) and the flat view's question keep Enter on the safe answer.
- **An archive Windows doesn't know** (no program opens it, no icon of its own, such as .lz, .dmg or .squashfs) shows the general compressed file's icon in the panels, a ZIP file's, when Salandual opens it.
- **Transparent parts of an image** show a light checkerboard in the image viewer (they showed the canvas colour, so the image's edge couldn't be seen); Options › Viewers › Images › *Transparent parts* and the viewer's ⋯ menu choose a light or a dark checkerboard, the canvas colour as before, white, black or any colour. The squares are drawn on whole screen pixels, so no dark lines show between them or around them.
- **Animated images play** in the image viewer: GIF, WebP, APNG and JPEG XL, each frame for as long as the file says and as many times as it says. While one plays the toolbar has Pause / Play and one frame back or on (P , .) and the status line the frame (*Frame 12 / 48 · 40 ms*); Ctrl+C copies the frame on screen.
- **The archive password** (asked for an archive, and in Pack) is shown and hidden with the eye at the end of the field; the *Show the password* checkbox is gone.
- **Drive information's Health tab**: the drive's health (SMART) without administrator rights: a summary (good, watch, replace soon), the life used, temperature, hours, data written, errors, and every SMART attribute of a SATA drive or hard disk; the connection (PCIe generation and lanes, SATA I to III), with a note when it runs slower than it can. Read again every 10 seconds while the tab is shown; only read, never the serial number.
- **Drive information's Activity tab**: *Busy now*, *Reading* and *Writing* in three columns of their own, so the numbers don't jump as they change.
- **One question for a delete**: the Delete dialog names folders that aren't empty and hidden or system items, so a permanent delete no longer asks a second time (your check of 2026-10-29). The progress window keeps one height whatever it shows.
- **The directory line's free space has its own switch**, beside the disk activity's (Options › Hot paths & drives › *Directory line*, a right-click on either, and First steps).
- **The last 30 seconds and Drive information's tabs** (your answers to mockup v2): Options › Hot paths & drives › *Disk activity looks like* chooses the bar (at first), columns or a line of the last 30 seconds, with the number. Drive information keeps the free space at the top, then two tabs: *Activity* (first) with the current reading and charts of the busy time and of the reading and writing speeds of the last 15 minutes, and *Details* with the table. The panels' drives are read also while another program is in front (nothing is redrawn then), so the 15 minutes stay complete; only minimized stops it. Kept in memory only.
- Fixed after the first check: the activity didn't show at all, then came and went; *Same answer for the rest* in the Confirmations dialog was squeezed beside four buttons and has its own line now.

**The Columns view and peeking into a folder** (Phase 3, M3 step 7)
- Alt+1 (or View › Columns): the folders above on the left, the folder you are in, and the focused folder's contents or file on the right (a picture, a text's first lines, its details). → and ← go in and back, the earlier columns slide out to the left and the scroll bar along the bottom brings them back; a click in another column goes there. One width for all columns, about 32 characters, set by dragging an edge or in Options › Views.
- Shift+Space on a folder, in any view, shows what it holds in a popup without going in; Enter goes in. It can open when the pointer rests on a folder too (Options, off at first).

**The flat view** (Phase 3, M3 step 6)
- Ctrl+B (or View › Flat view, or right-click folders › Flat view): every file under the folder, or under the selected folders, in one list to sort, select, view, copy and delete across subfolders; each file's folder in a column, as headings or in front of the name (Options › Panels › Flat view). It fills while it reads, Stop keeps what was found, it asks before reading on above 100 000 files, and it follows changes anywhere in the tree.
- The filter (Ctrl+F12) can look in the subfolders too, and the quick filter works over the flat view.

**Highlighting and folder colours** (Phase 3, M3 step 5)
- Options › Highlighting: rules that colour names by mask and attributes, as Salamander's file and folder colours, in bold or italic too; the first that matches wins. Encrypted, compressed and hidden names look as before; archives, programs and temporary files are ready to switch on.
- Right-click folders › Colour and icon: a colour, a mark or an image of your own for a folder, in every view, the directory line and the hot paths. Kept in Salandual's settings, never in the folder, and moved along when you rename or move the folder in Salandual.

**The filter** (Phase 3, M3 step 4)
- Ctrl+F12 shows only the names that match Salamander's masks, with the masks used before and quick picks by kind of file; it stays while you change folders, with its chip in the directory line (a click for its menu, ✕ to turn it off) and *Filter: 4 of 10 shown* in the information line.
- The quick search box switches to a quick filter with Ctrl+S or a click: what does not hold the text goes out of sight; Esc brings it all back, Enter keeps it until you leave the folder. Options can make typing filter first.
- Edit › Hide selected names, Hide unselected names and Show hidden names, as in Salamander, with a chip that brings them back.
- Dot files and folders (.git, .vscode) can be hidden: Options › Panels › Listing, the filter, and First steps.
- Options › Layout › Each panel › *Folders kept*: how many folders each panel's working directories (Alt+F12) keep, 5 to 100 (20 at first); a smaller number forgets the oldest.

### Changed

- The version is 0.8 (it was 0.1), as About and *Version in the title* show it.
- Button text is light in a dark theme and dark in a light one: the primary button, a checked toggle, a badge and the Delete and Stop buttons in a dark theme get light text on their color made deeper, so Nord's frost-blue buttons no longer have near-black text, and a theme's dim text (VS Code's Abyss) is made lighter on buttons.
- Options › Themes › Editor: a read-only theme's colors show at full strength and their groups open and close, so you can look at them before you duplicate it; the groups stay as you left them while Options is open.
- "Color" is spelled the American way everywhere in Salandual (menus, Options, notices); searching Options for "colour" still finds it.
- Ctrl+T, Ctrl+W and Ctrl+Tab are the tabs' keys, as in a browser: Restore selection (Edit) and the command line (Commands › Command Line) are in the menus without a key, and Go to shortcut target gives way to Enter on the shortcut.

### Fixed

- The working directories (Alt+F12) and Change directory's recent folders showed This PC and the other places as a long `::{…}` number instead of their names.
- The *This PC* chip in the drives strip, and a tab on one of Windows' places, showed a long `::{…}` number as its tooltip instead of the place's name.
- The question dialogs (Delete, Create the folder?, Save the changes?, the file that is already there, Put it back, the shutdown countdown…) open on the button Enter presses with its frame showing, and the arrow keys go from button to button. The Delete dialog's bin and the *already there* dialog's file icons sit in the middle of their boxes.
- The viewer's Text and Hex scroll bar: the thumb is as long as the share of the file on screen, from the first moment and after resizing, and reaches the bottom at the end of the file; it is hidden when the whole file fits. Home and End jump to the start and the end of the text, as in the Markdown and tree views.
- A panel that was slow to refresh (after a job, or when a folder changed) put the cursor back on the item focused when the refresh began, even if you had moved it meanwhile.
- About › Third-party didn't list FluentFTP, SSH.NET and the libraries they use, though they ship with me since FTP and SFTP came; they're there now with their licences.

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
- A *Keep* or *Revert* bar for a new look; every window remembers its size, the main window its place; settings shared between PCs through a folder that syncs; Windows' *Text size* followed.

**Help**
- About Salandual: the version, the GPL, the licences of the components.
- A log a day and a report when something goes wrong, kept 30 days on your PC and never sent; Logs and crash reports to read, copy with your user folder hidden, and report on GitHub.

**Install**
- An installer for your account only (no administrator rights), and a portable zip that keeps its settings beside itself.

### Not in this release

Archives, tabs, user menu and command line, remote file systems (FTP, SFTP), plugins, other languages than English, import from Altap Salamander, automatic updates. See the [README](README.md#what-is-not-in-v01).

[0.8.0]: https://github.com/TomEvin/salandual/releases/tag/v0.8
[0.1.0]: https://github.com/TomEvin/salandual/releases/tag/v0.1

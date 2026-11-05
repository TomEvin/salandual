// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.Core/Settings/Salamander/SalamanderImport.cs

using System.Globalization;
using System.Text.Json.Nodes;
using Salandual.Core.Resources;
using Salandual.Core.UserMenu;

namespace Salandual.Core.Settings.Salamander;

public static class SalamanderImport
{
    private const string HotPathsPath = "hotPaths.paths", RulesPath = "highlighting.rules", ServersPath = "servers.bookmarks",
        ViewersPath = "viewers.masks", EditorsPath = "editors.masks", UserMenuPath = "userMenu.items",
        RecyclePath = "operations.recycleByDefault", IfExistsPath = "operations.ifExists";

    public static SettingsImport Read(RegKey root, string source, AppSettings current)
    {
        var leftOut = new List<string>();
        var notes = new List<SettingChange>();
        var s = current;
        var config = root.Key("Configuration") ?? new RegKey();

        s = HotPaths(root, config, s, notes, leftOut);
        s = Panels(config, s);
        s = Layout(root, config, s);
        s = Confirmations(config, s);
        s = Compare(config, s);
        s = General(config, s);
        s = Unusual(config, s, notes);
        s = HighlightRules(root, s, notes);
        s = Servers(root, s, notes, leftOut);
        s = Viewers(root, s, notes, leftOut);
        s = Editors(root, s, notes);
        s = UserMenu(root, s, notes);
        s = History(config, s);

        var mine = SettingsTransfer.ToJson(current);
        var theirs = SettingsTransfer.ToJson(s.Normalize());
        var partOf = new Dictionary<string, SettingsPart>
        {
            [EditorsPath] = SettingsPart.Programs,
            [RecyclePath] = SettingsPart.Unusual,
            [IfExistsPath] = SettingsPart.Unusual,
        };
        var described = new HashSet<string> { HotPathsPath, RulesPath, ServersPath, ViewersPath, EditorsPath, UserMenuPath, RecyclePath, IfExistsPath };
        var changes = SettingsTransfer.Describe(mine, theirs, partOf).Where(c => c.Path is null || !described.Contains(c.Path)).ToList();
        foreach (var note in notes)
        {
            if (note.Path is { } path && !JsonNode.DeepEquals(SettingsTransfer.Get(mine, path), SettingsTransfer.Get(theirs, path)))
            {
                changes.Add(note);
            }
        }

        changes = [.. changes.OrderBy(c => c.Part)];
        leftOut.Add(Strings.SalImp_LeftOut_Passwords);
        leftOut.InsertRange(0, Plugins(root));
        return new SettingsImport(source, null, null, mine, theirs, changes, changes.Select(c => c.Part).ToHashSet(), partOf)
        {
            Source = source,
            OffAtFirst = new HashSet<SettingsPart> { SettingsPart.Programs, SettingsPart.History, SettingsPart.Unusual },
            LeftOut = leftOut,
        };
    }

    private static AppSettings HotPaths(RegKey root, RegKey config, AppSettings s, List<SettingChange> notes, List<string> leftOut)
    {
        var hot = s.HotPaths;
        if (config.Flag("Use Shift For GoTo HotPath") is { } shift)
        {
            hot = hot with { ShiftDigits = shift };
        }

        var theirs = new List<(int Slot, string Name, string Path, bool Visible)>();
        if (root.Key("Hot Paths") is { } key)
        {
            for (var i = 0; i <= HotPathSettings.SlotCount; i++)
            {
                if (key.Keys.TryGetValue(i.ToString(CultureInfo.InvariantCulture), out var item)
                    && item.Text("Path")?.Trim() is { Length: > 0 } path)
                {
                    var name = item.Text("Name")?.Trim() is { Length: > 0 } n ? n : HotPathSettings.DefaultName(path);
                    theirs.Add((i == 0 ? 10 : i, name, path, item.Flag("Visible") ?? true));
                }
            }
        }

        var moved = new List<(int From, string Name, string Path, bool Visible)>();
        foreach (var (slot, name, path, visible) in theirs.OrderBy(t => t.Slot))
        {
            if (hot.Paths.Any(p => SamePath(p.Path, path)))
            {
                continue;
            }

            if (hot[slot] is null)
            {
                hot = hot.With(new HotPath(slot, name, path, visible));
                notes.Add(new(SettingsPart.HotPaths, SlotName(slot), Strings.Transfer_None, name, HotPathsPath));
            }
            else
            {
                moved.Add((slot, name, path, visible));
            }
        }

        var full = 0;
        foreach (var (from, name, path, visible) in moved)
        {
            if (hot.NextFreeSlot() is not { } free)
            {
                full++;
                continue;
            }

            hot = hot.With(new HotPath(free, name, path, visible));
            notes.Add(new(SettingsPart.HotPaths, SlotName(free), Strings.Transfer_None, Loc.Format(Strings.SalImp_HotPathMoved, name, SlotName(from), hot[from]?.Name ?? ""), HotPathsPath));
        }

        if (full > 0)
        {
            leftOut.Add(Loc.Plural(full, Strings.SalImp_LeftOut_HotPaths_One, Strings.SalImp_LeftOut_HotPaths_Other));
        }

        return s with { HotPaths = hot };
    }

    private static string SlotName(int slot) => HotPathSettings.KeyOf(slot) ?? Loc.Format(Strings.SalImp_Slot, slot);

    private static bool SamePath(string a, string b) => string.Equals(a.TrimEnd('\\', '/'), b.TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase);

    private static AppSettings Panels(RegKey config, AppSettings s)
    {
        var p = s.Panels;
        if (config.Flag("Hide Hidden and System Files and Directories") is { } hide)
        {
            p = p with { ShowHiddenFiles = !hide };
        }

        if (config.Flag("Sort Detects Numbers") is { } numbers)
        {
            p = p with { NumericSort = numbers };
        }

        if (config.Flag("Select/Deselect Directories") is { } folders)
        {
            p = p with { SelectFoldersWithNumPlus = folders };
        }

        if (config.Flag("Click to Quick Rename") is { } rename)
        {
            p = p with { ClickToRename = rename };
        }

        if (config.Flag("Full Row Highlight") is { } fullRow)
        {
            var look = fullRow ? MarkLook.WholeRow : MarkLook.NameOnly;
            p = p with { FocusLook = look, FocusBackgroundLook = look };
        }

        if (config.Number("Thumbnail Size") is { } size and > 0)
        {
            p = p with { ThumbnailSize = Math.Clamp((int)Math.Min(size, int.MaxValue), PanelSettings.MinThumbnailSize, PanelSettings.MaxThumbnailSize) };
        }

        if (config.Text("Information Line Content")?.Trim() is { Length: > 0 } info)
        {
            p = p with { InformationLine = info };
        }

        return s with { Panels = p };
    }

    private static AppSettings Layout(RegKey root, RegKey config, AppSettings s)
    {
        var layout = s.Layout;
        var bars = layout.Bars;
        bars = Bar(config, "Show Top ToolBar", bars, (b, on) => b with { Toolbar = on });
        bars = Bar(config, "Show Middle ToolBar", bars, (b, on) => b with { MiddleToolbar = on });
        bars = Bar(config, "Show User Menu ToolBar", bars, (b, on) => b with { UserMenuBar = on });
        bars = Bar(config, "Hot Paths Bar", bars, (b, on) => b with { HotPathBar = on });
        bars = Bar(config, "Show Bottom ToolBar", bars, (b, on) => b with { FunctionKeyBar = on });
        bars = Bar(config, "Command Line", bars, (b, on) => b with { CommandLine = on });

        if (config.Flag("Show Drive Bar") is not null || config.Flag("Show Drive Bar2") is not null)
        {
            var (shared, perPanel) = (config.Flag("Show Drive Bar") == true, config.Flag("Show Drive Bar2") == true);
            bars = bars with { DriveStrip = shared || perPanel };
            if (shared || perPanel)
            {
                layout = layout with { Strip = perPanel ? StripMode.PerPanel : StripMode.Shared };
            }
        }

        var (left, right) = (root.Key("Left Panel"), root.Key("Right Panel"));
        bool? Either(string name)
        {
            var (l, r) = (left?.Flag(name), right?.Flag(name));
            return l is null && r is null ? null : l == true || r == true;
        }

        if (Either("Directory Line") is { } directory)
        {
            bars = bars with { DirectoryLine = directory };
        }

        if (Either("Header Line") is { } header)
        {
            bars = bars with { ColumnHeader = header };
        }

        if (Either("Status Line") is { } status)
        {
            bars = bars with { InformationLine = status };
        }

        return s with { Layout = layout with { Bars = bars } };
    }

    private static BarVisibility Bar(RegKey config, string name, BarVisibility bars, Func<BarVisibility, bool, BarVisibility> set)
        => config.Flag(name) is { } on ? set(bars, on) : bars;

    private static AppSettings Confirmations(RegKey config, AppSettings s)
    {
        if (config.Key("Confirmation") is not { } c)
        {
            return s;
        }

        var o = s.Confirmations;
        o = Ask(c, "Files or Dirs Del", o, (x, on) => x with { Delete = on });
        o = Ask(c, "Non-empty Dir Del", o, (x, on) => x with { DeleteNonEmptyFolder = on });
        if (c.Flag("SH File Del") is not null || c.Flag("SH Dir Del") is not null)
        {
            o = o with { DeleteHiddenOrSystem = c.Flag("SH File Del") == true || c.Flag("SH Dir Del") == true };
        }

        o = Ask(c, "SH File Overwrite", o, (x, on) => x with { ReplaceHiddenOrSystem = on });
        o = Ask(c, "Directory Overwrite", o, (x, on) => x with { MergeFolders = on });
        o = Ask(c, "Create Target Path", o, (x, on) => x with { CreateTargetFolder = on });
        o = Ask(c, "Drag and Drop", o, (x, on) => x with { DragDropMenu = on });
        o = Ask(c, "Close Salamander", o, (x, on) => x with { CloseApp = on });
        o = Ask(c, "Close Find", o, (x, on) => x with { CloseSearch = on });
        return s with { Confirmations = o };
    }

    private static ConfirmationSettings Ask(RegKey c, string name, ConfirmationSettings o, Func<ConfirmationSettings, bool, ConfirmationSettings> set)
        => c.Flag(name) is { } on ? set(o, on) : o;

    private static AppSettings Compare(RegKey config, AppSettings s)
    {
        var d = s.Compare.Directories;
        bool Take(string name, bool now) => config.Flag(name) ?? now;
        d = d with
        {
            Newer = Take("Compare By Time", d.Newer),
            Bigger = Take("Compare By Size", d.Bigger),
            Content = Take("Compare By Content", d.Content),
            Attributes = Take("Compare By Attr", d.Attributes),
            Subfolders = Take("Compare By Subdirs", d.Subfolders),
            SubfolderAttributes = Take("Compare By Subdirs Attr", d.SubfolderAttributes),
            FoldersOnOneSide = Take("Compare One Panel Dirs", d.FoldersOnOneSide),
            IgnoreOneHour = Take("Ignore DST Shifts", d.IgnoreOneHour),
            LeaveOutFiles = Take("Compare Ignore Files", d.LeaveOutFiles),
            LeaveOutFilesMask = config.Text("Compare Ignore Files Masks")?.Trim() is { Length: > 0 } files ? files : d.LeaveOutFilesMask,
            LeaveOutFolders = Take("Compare Ignore Dirs", d.LeaveOutFolders),
            LeaveOutFoldersMask = config.Text("Compare Ignore Dirs Masks")?.Trim() is { Length: > 0 } folders ? folders : d.LeaveOutFoldersMask,
        };
        return s with { Compare = s.Compare with { Directories = d } };
    }

    private static AppSettings General(RegKey config, AppSettings s)
        => config.Flag("Only One Instance") is { } one ? s with { General = s.General with { SingleInstance = one } } : s;

    private static AppSettings Unusual(RegKey config, AppSettings s, List<SettingChange> notes)
    {
        var o = s.Operations;
        switch (config.Number("Use Recycle Bin"))
        {
            case 0 when o.RecycleByDefault:
                o = o with { RecycleByDefault = false };
                notes.Add(new(SettingsPart.Unusual, Strings.SalImp_RecycleNever, Strings.Transfer_On, Strings.SalImp_DeletedForGood, RecyclePath));
                break;
            case 2 when o.RecycleByDefault:
                o = o with { RecycleByDefault = false };
                var masks = config.Text("Use Recycle Bin For")?.Trim() is { Length: > 0 } m ? m : "*.*";
                notes.Add(new(SettingsPart.Unusual, Loc.Format(Strings.SalImp_RecycleOnlyFor, masks), Strings.Transfer_On, Strings.SalImp_DeletedForGood, RecyclePath));
                break;
        }

        if (config.Key("Confirmation")?.Flag("File Overwrite") == false && o.IfExists == Operations.ExistingFilePolicy.Ask)
        {
            o = o with { IfExists = Operations.ExistingFilePolicy.Overwrite };
            notes.Add(new(SettingsPart.Unusual, Strings.SalImp_ReplaceWithoutAsking, Strings.Transfer_Off, Strings.Transfer_On, IfExistsPath));
        }

        return s with { Operations = o };
    }

    private static AppSettings HighlightRules(RegKey root, AppSettings s, List<SettingChange> notes)
    {
        if (root.Key(@"Colors\Panel Items Hilighting") is not { } key)
        {
            return s;
        }

        var rules = s.Highlighting.Effective.ToList();
        foreach (var item in key.Numbered())
        {
            var masks = item.Text("Masks")?.Trim() ?? "";
            var valid = (FileAttributes)(item.Number("Valid Attributes") ?? 0);
            var attr = (FileAttributes)(item.Number("Attributes") ?? 0) & valid;
            var colour = item.Number("Item Fg Normal") ?? 0x01000000;
            if (masks.Length == 0 || (colour >> 24 & 1) != 0)
            {
                continue;
            }

            var mustHave = attr;
            var mustNot = valid & ~attr;
            var anyMask = masks is "*.*" or "*";
            if (rules.Any(r => r.MustHave == mustHave && r.MustNotHave == mustNot && (anyMask ? r.Masks.Length == 0 || r.Masks is "*.*" or "*" : string.Equals(r.Masks, masks, StringComparison.OrdinalIgnoreCase))))
            {
                continue;
            }

            var hex = "#" + (colour & 0xFF).ToString("X2", CultureInfo.InvariantCulture) + (colour >> 8 & 0xFF).ToString("X2", CultureInfo.InvariantCulture) + (colour >> 16 & 0xFF).ToString("X2", CultureInfo.InvariantCulture);
            var name = anyMask && mustHave != 0 ? AttributeWords(mustHave) : masks;
            rules.Add(new HighlightRule { Name = name, Masks = anyMask ? "" : masks, MustHave = mustHave, MustNotHave = mustNot, Colour = NameColour.Own, OwnLight = hex, OwnDark = hex });
            notes.Add(new(SettingsPart.Panels, Loc.Format(Strings.SalImp_Rule, name), Strings.Transfer_None, hex, RulesPath));
        }

        return rules.Count == s.Highlighting.Effective.Count ? s : s with { Highlighting = s.Highlighting with { Rules = rules } };
    }

    private static string AttributeWords(FileAttributes attributes)
    {
        var words = new List<string>();
        foreach (var (a, word) in (ReadOnlySpan<(FileAttributes, string)>)
            [
                (FileAttributes.Compressed, Strings.Highlight_Compressed), (FileAttributes.Encrypted, Strings.Highlight_Encrypted),
                (FileAttributes.Hidden, Strings.SalImp_Attr_Hidden), (FileAttributes.System, Strings.SalImp_Attr_System),
                (FileAttributes.ReadOnly, Strings.SalImp_Attr_ReadOnly), (FileAttributes.Archive, Strings.SalImp_Attr_Archive),
            ])
        {
            if ((attributes & a) != 0)
            {
                words.Add(word);
            }
        }

        return words.Count > 0 ? string.Join(", ", words) : "*.*";
    }

    private static AppSettings Servers(RegKey root, AppSettings s, List<SettingChange> notes, List<string> leftOut)
    {
        if (root.Key(@"Plugins Configuration\FTP\Bookmarks") is not { } key)
        {
            return s;
        }

        var bookmarks = s.Servers.Bookmarks.ToList();
        var skipped = 0;
        foreach (var item in key.Keys.OrderBy(k => int.TryParse(k.Key, NumberStyles.None, CultureInfo.InvariantCulture, out var n) ? n : int.MaxValue).Select(k => k.Value))
        {
            var host = item.Text("Address")?.Trim() ?? "";
            if (!IsHostName(host))
            {
                skipped++;
                continue;
            }

            var port = item.Number("Port") is { } p and > 0 and <= 65535 ? (int)p : 0;
            var user = item.Flag("Anonymous") == true ? "anonymous" : item.Text("User")?.Trim() ?? "";
            var bookmark = new ServerBookmark
            {
                Name = item.Text("Name")?.Trim() is { Length: > 0 } n ? n : host,
                Kind = ServerKind.Ftp,
                Host = host,
                Port = port == ServerBookmark.DefaultPort(ServerKind.Ftp) ? 0 : port,
                User = user,
                StartIn = item.Text("Initial Path")?.Trim() ?? "",
                Connection = item.Flag("Passive Mode") == false ? ServerOptions.BuiltIn.Connection with { Data = DataConnection.Active } : null,
            };
            if (bookmarks.Any(b => string.Equals(b.Host, host, StringComparison.OrdinalIgnoreCase) && b.PortOrDefault == bookmark.PortOrDefault && string.Equals(b.User, user, StringComparison.Ordinal)))
            {
                continue;
            }

            bookmarks.Add(bookmark);
            var where = user.Length > 0 ? user + "@" + host : host;
            notes.Add(new(SettingsPart.HotPaths, Loc.Format(Strings.SalImp_Server, bookmark.Name), Strings.Transfer_None, port > 0 ? where + ":" + port.ToString(CultureInfo.InvariantCulture) : where, ServersPath));
        }

        if (skipped > 0)
        {
            leftOut.Add(Loc.Plural(skipped, Strings.SalImp_LeftOut_Servers_One, Strings.SalImp_LeftOut_Servers_Other));
        }

        return s with { Servers = s.Servers with { Bookmarks = bookmarks } };
    }

    private static bool IsHostName(string host)
        => host.Length is > 0 and <= 253 && host.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '-' or ':' or '_') && !host.StartsWith('-') && !host.StartsWith('.');

    private static AppSettings Viewers(RegKey root, AppSettings s, List<SettingChange> notes, List<string> leftOut)
    {
        if (root.Key("Viewers") is not { } key)
        {
            return s;
        }

        var rows = s.Viewers.Masks.ToList();
        var known = rows.SelectMany(r => Patterns(r.Mask)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var added = new Dictionary<ViewerKind, List<string>>();
        var programs = 0;
        var noCounterpart = new SortedSet<string>(StringComparer.CurrentCultureIgnoreCase);
        foreach (var item in key.Numbered())
        {
            var masks = item.Text("Masks") ?? "";
            var type = item.Number("Type") is { } t ? unchecked((int)t) : 1;
            if (type == 0)
            {
                programs++;
                continue;
            }

            if (type > 0)
            {
                continue;
            }

            var plugin = root.Key(@"Plugins\" + (-type).ToString(CultureInfo.InvariantCulture));
            var dll = Path.GetFileNameWithoutExtension(plugin?.Text("DLL") ?? "").ToLowerInvariant();
            foreach (var pattern in Patterns(masks))
            {
                ViewerKind? kind = dll switch
                {
                    "pictview" => ViewerKind.Image,
                    "mmviewer" => ViewerKind.Media,
                    "ieviewer" => pattern.EndsWith(".md", StringComparison.OrdinalIgnoreCase) || pattern.EndsWith(".markdown", StringComparison.OrdinalIgnoreCase) ? ViewerKind.Markdown : ViewerKind.Html,
                    _ => null,
                };
                if (kind is not { } k)
                {
                    noCounterpart.Add(plugin?.Text("Name")?.Trim() is { Length: > 0 } n ? n : dll.Length > 0 ? dll : "?");
                    continue;
                }

                if (pattern is "*.*" or "*" || !known.Add(pattern))
                {
                    continue;
                }

                (added.TryGetValue(k, out var list) ? list : added[k] = []).Add(pattern);
            }
        }

        var at = 0;
        foreach (var (kind, patterns) in added)
        {
            var mask = string.Join(';', patterns);
            rows.Insert(at++, new ViewerMask { Mask = mask, View = kind, Alternate = kind == ViewerKind.Markdown ? ViewerKind.Text : ViewerKind.Hex });
            notes.Add(new(SettingsPart.Viewers, mask, Strings.Transfer_None, ViewerWord(kind), ViewersPath));
        }

        if (programs > 0)
        {
            leftOut.Add(Loc.Plural(programs, Strings.SalImp_LeftOut_ViewerPrograms_One, Strings.SalImp_LeftOut_ViewerPrograms_Other));
        }

        if (noCounterpart.Count > 0)
        {
            leftOut.Add(Loc.Format(Strings.SalImp_LeftOut_Viewers, string.Join(", ", noCounterpart)));
        }

        return s with { Viewers = s.Viewers with { Masks = rows } };
    }

    private static IEnumerable<string> Patterns(string masks)
        => masks.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Where(p => p.Length > 0 && !p.StartsWith('|'));

    private static string ViewerWord(ViewerKind kind) => kind switch
    {
        ViewerKind.Image => Strings.SalImp_View_Image,
        ViewerKind.Media => Strings.SalImp_View_Media,
        ViewerKind.Markdown => Strings.SalImp_View_Markdown,
        _ => Strings.SalImp_View_Html,
    };

    private static AppSettings Editors(RegKey root, AppSettings s, List<SettingChange> notes)
    {
        if (root.Key("Editors") is not { } key)
        {
            return s;
        }

        var rows = s.Editors.Masks.ToList();
        foreach (var item in key.Numbered())
        {
            var masks = item.Text("Masks")?.Trim() ?? "";
            if (masks.Length == 0 || ProgramLine(item) is not { } program)
            {
                continue;
            }

            var i = rows.FindIndex(r => string.Equals(r.Mask, masks, StringComparison.OrdinalIgnoreCase));
            if (i >= 0 && rows[i].Program == program)
            {
                continue;
            }

            var before = i >= 0 ? rows[i].Program : null;
            if (i >= 0)
            {
                rows[i] = rows[i] with { Program = program };
            }
            else
            {
                var all = rows.FindIndex(r => r.Mask is "*.*" or "*");
                rows.Insert(all >= 0 ? all : rows.Count, new EditorMask { Mask = masks, Program = program });
            }

            notes.Add(new(SettingsPart.Programs, Loc.Format(Strings.Transfer_EditorFor, masks), before ?? Strings.Transfer_None, program, EditorsPath));
        }

        return s with { Editors = s.Editors with { Masks = rows } };
    }

    private static string? ProgramLine(RegKey item)
    {
        var command = item.Text("Command")?.Trim() ?? "";
        if (command.Length == 0)
        {
            return null;
        }

        var quoted = command.Contains(' ') && !command.StartsWith('"') ? "\"" + command + "\"" : command;
        return item.Text("Arguments")?.Trim() is { Length: > 0 } arguments ? quoted + " " + arguments : quoted;
    }

    private static AppSettings UserMenu(RegKey root, AppSettings s, List<SettingChange> notes)
    {
        if (root.Key("User Menu") is not { } key)
        {
            return s;
        }

        var top = new List<UserMenuItem>();
        var stack = new Stack<(UserMenuItem Submenu, List<UserMenuItem> Items, List<UserMenuItem> Parent)>();
        var into = top;
        foreach (var item in key.Numbered())
        {
            var name = item.Text("Item Name")?.Trim() ?? "";
            switch (item.Number("Type") ?? 0)
            {
                case 1:
                    var submenu = new UserMenuItem { Name = name, Kind = UserMenuItemKind.Submenu, InBar = item.Flag("Show In Toolbar") == true };
                    stack.Push((submenu, [], into));
                    into = stack.Peek().Items;
                    break;
                case 2 when stack.Count > 0:
                    var (done, items, parent) = stack.Pop();
                    parent.Add(done with { Items = items });
                    into = parent;
                    break;
                case 3:
                    into.Add(new UserMenuItem { Kind = UserMenuItemKind.Separator });
                    break;
                case 0 when item.Text("Command")?.Trim() is { Length: > 0 } command && name.Length > 0:
                    into.Add(new UserMenuItem
                    {
                        Name = name,
                        Program = command,
                        Arguments = item.Text("Arguments")?.Trim() ?? "",
                        StartIn = item.Text("Initial Directory")?.Trim() is { Length: > 0 } dir ? dir : UserMenuVariables.DefaultStartIn,
                        InBar = item.Flag("Show In Toolbar") == true,
                    });
                    break;
            }
        }

        while (stack.Count > 0)
        {
            var (open, items, parent) = stack.Pop();
            parent.Add(open with { Items = items });
        }

        var mine = s.UserMenu.Items;
        var news = top.Where(t => !mine.Any(m => m.Kind == t.Kind && m.Name == t.Name && m.Program == t.Program)).ToList();
        if (news.All(n => n.Kind == UserMenuItemKind.Separator))
        {
            return s;
        }

        foreach (var item in news.Where(n => n.Kind != UserMenuItemKind.Separator))
        {
            var what = item.Kind == UserMenuItemKind.Submenu
                ? Loc.Plural(item.Items.Count(i => i.Kind != UserMenuItemKind.Separator), Strings.SalImp_Submenu_One, Strings.SalImp_Submenu_Other)
                : item.Program;
            notes.Add(new(SettingsPart.Programs, Loc.Format(Strings.SalImp_UserMenuItem, item.PlainName), Strings.Transfer_None, what, UserMenuPath));
        }

        return s with { UserMenu = s.UserMenu with { Items = [.. mine, .. news] } };
    }

    private static AppSettings History(RegKey config, AppSettings s)
    {
        List<string> Merge(List<string> mine, string key)
        {
            var theirs = Entries(config.Key(key));
            return theirs.Count == 0 ? mine : [.. mine.Concat(theirs).Distinct(StringComparer.OrdinalIgnoreCase)];
        }

        var commands = Entries(config.Key("Command History"));
        var h = s.History with
        {
            CopyTargets = Merge(s.History.CopyTargets, "Copy History"),
            Commands = commands.Count == 0 ? s.History.Commands
                : [.. s.History.Commands, .. commands.Where(c => !s.History.Commands.Any(m => m.Text == c)).Select(c => new CommandLine.CommandHistoryEntry(c, ""))],
        };
        var f = s.Find with
        {
            Named = Merge(s.Find.Named, "Named History"),
            LookIn = Merge(s.Find.LookIn, "Look In History"),
            Texts = Merge(s.Find.Texts, "Grep History"),
        };
        return s with { History = h, Find = f };
    }

    private static List<string> Entries(RegKey? key)
    {
        var entries = new List<string>();
        for (var i = 1; key?.Text(i.ToString(CultureInfo.InvariantCulture)) is { } text; i++)
        {
            if (text.Trim().Length > 0)
            {
                entries.Add(text);
            }
        }

        return entries;
    }

    private static IEnumerable<string> Plugins(RegKey root)
    {
        var plugins = root.Key("Plugins")?.Numbered().Count() ?? 0;
        if (plugins > 0)
        {
            yield return Loc.Plural(plugins, Strings.SalImp_LeftOut_Plugins_One, Strings.SalImp_LeftOut_Plugins_Other);
        }

        if (root.Keys.Keys.Any(k => k.StartsWith("Packers", StringComparison.OrdinalIgnoreCase) || k.Contains("Unpackers", StringComparison.OrdinalIgnoreCase) || k.Contains("Packer", StringComparison.OrdinalIgnoreCase)))
        {
            yield return Strings.SalImp_LeftOut_Packers;
        }
    }
}

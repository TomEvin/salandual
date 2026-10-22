// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.Core/Commands/KeyMaps.cs

using Salandual.Core.Resources;

namespace Salandual.Core.Commands;

public enum KeyMapName
{
    Salamander,

    TotalCommander,
}

public sealed record NotYetKey(KeyChord Chord, string Title);

public static class KeyMaps
{
    public static string Title(KeyMapName name) => name == KeyMapName.TotalCommander ? Strings.KeyMap_TotalCommander : Strings.KeyMap_Salamander;


    internal static readonly (string Keys, string Target)[] TotalCommander =
    [
        ("F1", "Help.Contents"),
        ("F2 Ctrl+R", "Panel.Refresh"),
        ("F3 Shift+F3", "File.View"),
        ("Alt+F3", "File.ViewAlternate"),
        ("F4", "File.Edit"),
        ("Shift+F4", "File.EditNew"),
        ("F5", "File.Copy"),
        ("Shift+F5", "File.Duplicate"),
        ("F6", "File.Move"),
        ("Shift+F6", "File.QuickRename"),
        ("F7", "File.CreateDirectory"),
        ("F8 Delete", "File.Delete"),
        ("Shift+F8 Shift+Delete", "File.DeleteInverted"),
        ("F9 F10", "App.Menu"),
        ("Shift+F2", "Compare.Directories"),
        ("Shift+F10 Apps", "File.ContextMenu"),
        ("Alt+F1", "Drive.ChangeLeft"),
        ("Alt+F2", "Drive.ChangeRight"),
        ("Alt+F4", "App.Exit"),
        ("Alt+F5", "Archive.Pack"),
        ("Alt+F6 Alt+F9", "Archive.Unpack"),
        ("Alt+F7", "Find.Files"),
        ("Ctrl+F3", "Panel.SortByName"),
        ("Ctrl+F4", "Panel.SortByExtension"),
        ("Ctrl+F5", "Panel.SortByTime"),
        ("Ctrl+F6", "Panel.SortBySize"),
        ("Ctrl+F12", "Panel.Filter"),
        ("Ctrl+A Ctrl+Num+", "Selection.SelectAll"),
        ("Ctrl+Num-", "Selection.UnselectAll"),
        ("Num+", "Selection.SelectByMask"),
        ("Num-", "Selection.UnselectByMask"),
        ("Num*", "Selection.InvertFiles"),
        ("Num/", "Selection.Restore"),
        ("Alt+Num+", "Selection.SameExtension"),
        ("Alt+Num-", "Selection.UnselectSameExtension"),
        ("Insert", "Selection.ToggleAndNext"),
        ("Space", "Selection.ToggleAndSize"),
        ("Ctrl+C", "Clipboard.Copy"),
        ("Ctrl+X", "Clipboard.Cut"),
        ("Ctrl+V", "Clipboard.Paste"),
        ("Ctrl+D", "HotPaths.Menu"),
        ("Ctrl+L", "File.OccupiedSpace"),
        ("Ctrl+U", "Panel.Swap"),
        ("Ctrl+P", "CommandLine.InsertDirectory"),
        ("Ctrl+Enter", "CommandLine.InsertName"),
        ("Ctrl+Left Ctrl+Right", "Navigation.OpenInOtherPanel"),
        ("Alt+Left", "History.Back"),
        ("Alt+Right", "History.Forward"),
        ("Alt+Down", "Lists.WorkingDirectories"),
        ("Backspace Ctrl+PageUp", "Navigation.Parent"),
        ("Ctrl+\\ Ctrl+Backspace", "Navigation.Root"),
        ("Enter", "Navigation.Execute"),
        ("Alt+Enter", "File.Properties"),
        ("Shift+Escape", "App.Minimize"),
        ("Tab", "Panel.SwitchActive"),

        ("Ctrl+T", "~" + nameof(Strings.KeyMap_NotYet_NewTab)),
        ("Ctrl+W", "~" + nameof(Strings.KeyMap_NotYet_CloseTab)),
        ("Ctrl+Q", "~" + nameof(Strings.KeyMap_NotYet_QuickView)),
        ("Ctrl+M", "~" + nameof(Strings.KeyMap_NotYet_MultiRename)),
        ("Ctrl+F", "~" + nameof(Strings.KeyMap_NotYet_FtpConnect)),
        ("Shift+F1", "~" + nameof(Strings.KeyMap_NotYet_CustomColumns)),
        ("Ctrl+Shift+F5", "~" + nameof(Strings.KeyMap_NotYet_CreateShortcuts)),
        ("Alt+Shift+F5", "~" + nameof(Strings.KeyMap_NotYet_MoveToArchive)),
        ("Alt+Shift+F9", "~" + nameof(Strings.KeyMap_NotYet_TestArchives)),
        ("Alt+F8", "~" + nameof(Strings.KeyMap_NotYet_CommandHistory)),
        ("Alt+F10", "~" + nameof(Strings.KeyMap_NotYet_DirectoryTree)),
        ("Ctrl+F1", "~" + nameof(Strings.KeyMap_NotYet_BriefView)),
        ("Ctrl+F2", "~" + nameof(Strings.KeyMap_NotYet_FullView)),
        ("Ctrl+Shift+F1", "~" + nameof(Strings.KeyMap_NotYet_Thumbnails)),
        ("Ctrl+Shift+F2", "~" + nameof(Strings.KeyMap_NotYet_CommentsView)),
        ("Ctrl+F7", "~" + nameof(Strings.KeyMap_NotYet_Unsorted)),
        ("Ctrl+F8", "~" + nameof(Strings.KeyMap_NotYet_TreePanel)),
        ("Ctrl+F9", "~" + nameof(Strings.KeyMap_NotYet_Print)),
        ("Ctrl+F10", "~" + nameof(Strings.KeyMap_NotYet_AllFiles)),
        ("Ctrl+F11", "~" + nameof(Strings.KeyMap_NotYet_Programs)),
        ("Ctrl+S", "~" + nameof(Strings.KeyMap_NotYet_QuickFilter)),
        ("Ctrl+Shift+T", "~" + nameof(Strings.KeyMap_NotYet_NewTabBehind)),
        ("Ctrl+Shift+W", "~" + nameof(Strings.KeyMap_NotYet_CloseAllTabs)),
        ("Ctrl+Tab", "~" + nameof(Strings.KeyMap_NotYet_NextTab)),
        ("Ctrl+Shift+Tab", "~" + nameof(Strings.KeyMap_NotYet_PreviousTab)),
        ("Ctrl+Up", "~" + nameof(Strings.KeyMap_NotYet_OpenInNewTab)),
        ("Ctrl+Shift+Up", "~" + nameof(Strings.KeyMap_NotYet_OpenInNewTabOther)),
        ("Ctrl+Shift+U", "~" + nameof(Strings.KeyMap_NotYet_SwapWithTabs)),
        ("Ctrl+Z", "~" + nameof(Strings.KeyMap_NotYet_Comment)),
        ("Ctrl+Shift+F", "~" + nameof(Strings.KeyMap_NotYet_FtpDisconnect)),
        ("Ctrl+N", "~" + nameof(Strings.KeyMap_NotYet_FtpNew)),
        ("Ctrl+B", "~" + nameof(Strings.KeyMap_NotYet_BranchView)),
    ];

    internal static (Dictionary<KeyChord, string> Map, Dictionary<string, List<KeyChord>> Chords, List<NotYetKey> NotYet) Build(KeyMapName name, IEnumerable<CommandDefinition> commands)
    {
        var all = commands.ToList();
        var map = new Dictionary<KeyChord, string>();
        var chords = all.ToDictionary(c => c.Id, _ => new List<KeyChord>(), StringComparer.Ordinal);
        var notYet = new List<NotYetKey>();
        var own = new HashSet<string>(StringComparer.Ordinal);
        if (name == KeyMapName.TotalCommander)
        {
            foreach (var (keys, target) in TotalCommander)
            {
                foreach (var chord in keys.Split(' ').Select(KeyChord.Parse))
                {
                    if (target.StartsWith('~'))
                    {
                        notYet.Add(new NotYetKey(chord, Strings.ResourceManager.GetString(target[1..], Strings.Culture) ?? target[1..]));
                        map[chord] = "";
                    }
                    else if (chords.TryGetValue(target, out var list))
                    {
                        map[chord] = target;
                        list.Add(chord);
                        own.Add(target);
                    }
                }
            }
        }

        foreach (var command in all.Where(c => !own.Contains(c.Id)))
        {
            foreach (var chord in command.DefaultChords)
            {
                if (map.TryAdd(chord, command.Id))
                {
                    chords[command.Id].Add(chord);
                }
            }
        }

        foreach (var reserved in map.Where(p => p.Value.Length == 0).Select(p => p.Key).ToList())
        {
            map.Remove(reserved);
        }

        return (map, chords, notYet);
    }
}

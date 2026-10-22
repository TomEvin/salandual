// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.Core/Commands/DefaultCommands.cs

using System.Globalization;
using Salandual.Core.Resources;

namespace Salandual.Core.Commands;

public static class DefaultCommands
{
    public static IEnumerable<CommandDefinition> All()
    {
        yield return C("Help.Contents", "Help", "M7", "F1");
        yield return C("Help.WhatIsThis", "Help", "Phase 5", "Shift+F1");
        yield return C("File.QuickRename", "Files", "M3", "F2");
        yield return C("File.View", "Files", "M4", "F3");
        yield return C("File.ViewAlternate", "Files", "M4", "Alt+F3");
        yield return C("File.ViewWith", "Files", "M4", "Ctrl+Shift+F3");
        yield return C("File.Edit", "Files", "M5", "F4");
        yield return C("File.EditNew", "Files", "M5", "Shift+F4");
        yield return C("File.EditWith", "Files", "M5", "Ctrl+Shift+F4");
        yield return C("File.Copy", "Files", "M3", "F5");
        yield return C("File.CopyToEach", "Files", "M3", "Shift+F5");
        yield return C("File.Move", "Files", "M3", "F6");
        yield return C("File.CreateDirectory", "Files", "M3", "F7");
        yield return C("File.Delete", "Files", "M3", "F8", "Delete");
        yield return C("File.DeleteInverted", "Files", "M3", "Shift+F8", "Shift+Delete");
        yield return C("UserMenu.Open", "Commands", "Phase 3", "F9");
        yield return C("App.Menu", "Application", "M1", "F10");
        yield return C("Network.ConnectDrive", "Commands", "Phase 3", "F11");
        yield return C("Network.DisconnectDrive", "Commands", "Phase 3", "F12");
        yield return C("Drive.Info", "Commands", "M2", "Ctrl+F1", "Ctrl+L");
        yield return C("File.ChangeAttributes", "Files", "Phase 3", "Ctrl+F2");
        yield return C("Panel.SortByName", "Panel", "M1", "Ctrl+F3");
        yield return C("Panel.SortByExtension", "Panel", "M1", "Ctrl+F4");
        yield return C("Panel.SortByTime", "Panel", "M1", "Ctrl+F5");
        yield return C("Panel.SortBySize", "Panel", "M1", "Ctrl+F6");
        yield return C("File.ChangeCase", "Files", "Phase 3", "Ctrl+F7");
        yield return C("Panel.Refresh", "Panel", "M1", "Ctrl+F9", "Ctrl+R");
        yield return C("Compare.Directories", "Commands", "M6", "Ctrl+F10");
        yield return C("Compare.Files", "Commands", "M6", "Ctrl+Shift+C");
        yield return C("Panel.Maximize", "Panel", "M2", "Ctrl+F11");
        yield return C("Panel.Filter", "Panel", "Phase 3", "Ctrl+F12");
        yield return C("Drive.ChangeLeft", "Panel", "M2", "Alt+F1");
        yield return C("Drive.ChangeRight", "Panel", "M2", "Alt+F2");
        yield return C("App.Exit", "Application", "M1", "Alt+F4");
        yield return C("Archive.Pack", "Files", "Phase 3", "Alt+F5");
        yield return C("Archive.Unpack", "Files", "Phase 3", "Alt+F6", "Alt+F9");
        yield return C("Find.Files", "Commands", "M6", "Alt+F7", "Ctrl+F");
        yield return C("Find.Duplicates", "Commands", "M6", "Alt+Shift+F7");
        yield return C("File.OccupiedSpace", "Commands", "M6", "Alt+F10", "Ctrl+Q");
        yield return C("Lists.OpenedFiles", "Commands", "Phase 3", "Alt+F11");
        yield return C("Lists.WorkingDirectories", "Commands", "M2", "Alt+F12");
        yield return C("Panel.OpenInExplorer", "Panel", "Phase 3", "Shift+F3");
        yield return C("Panel.ChangeDirectory", "Panel", "M2", "Shift+F7", "Ctrl+G");
        yield return C("HotPaths.Menu", "Navigation", "M2", "Shift+F9");
        yield return C("File.ContextMenu", "Files", "M3", "Shift+F10", "Apps");
        yield return C("Selection.Save", "Edit", "Phase 3", "Ctrl+Shift+F5");
        yield return C("Selection.Load", "Edit", "Phase 3", "Ctrl+Shift+F6");
        yield return C("Network.SharedDirectories", "Commands", "Phase 3", "Ctrl+Shift+F9");
        yield return C("Panel.CalculateDirectorySizes", "Panel", "Phase 3", "Ctrl+Shift+F10");
        yield return C("App.MaximizeWindow", "Application", "M1", "Ctrl+Shift+F11");
        yield return C("Panel.DirectoryContextMenu", "Panel", "M3", "Alt+Shift+F10");

        yield return C("Focus.Previous", "Navigation", "M1", "Up");
        yield return C("Focus.Next", "Navigation", "M1", "Down");
        yield return C("Focus.Left", "Navigation", "M1", "Left");
        yield return C("Focus.Right", "Navigation", "M1", "Right");
        yield return C("Focus.PageUp", "Navigation", "M1", "PageUp");
        yield return C("Focus.PageDown", "Navigation", "M1", "PageDown");
        yield return C("Focus.First", "Navigation", "M1", "Home");
        yield return C("Focus.Last", "Navigation", "M1", "End");
        yield return C("Focus.FirstFile", "Navigation", "M1", "Ctrl+Home");
        yield return C("Focus.PreviousSelected", "Navigation", "M1", "Alt+Up");
        yield return C("Focus.NextSelected", "Navigation", "M1", "Alt+Down");
        yield return C("Focus.FirstSelected", "Navigation", "M1", "Alt+Home");
        yield return C("Focus.LastSelected", "Navigation", "M1", "Alt+End");
        yield return C("Panel.ScrollUp", "Navigation", "M1", "Ctrl+Up");
        yield return C("Panel.ScrollDown", "Navigation", "M1", "Ctrl+Down");
        yield return C("Panel.ScrollLeft", "Navigation", "M1", "Ctrl+Left");
        yield return C("Panel.ScrollRight", "Navigation", "M1", "Ctrl+Right");
        yield return C("Panel.SwitchActive", "Navigation", "M1", "Tab");

        yield return C("Navigation.Execute", "Navigation", "M1", "Enter");
        yield return C("Navigation.Parent", "Navigation", "M1", "Backspace", "Ctrl+PageUp");
        yield return C("Navigation.Root", "Navigation", "M1", "Ctrl+\\", "Ctrl+Backspace", "Shift+Backspace");
        yield return C("Navigation.OpenFocused", "Navigation", "M1", "Ctrl+PageDown");
        yield return C("Navigation.OpenInOtherPanel", "Navigation", "M2", "Ctrl+Shift+Left", "Ctrl+Shift+Right");
        yield return C("Navigation.GoToFile", "Navigation", "M6", "Ctrl+J");
        yield return C("History.Back", "Navigation", "M2", "Alt+Left");
        yield return C("History.Forward", "Navigation", "M2", "Alt+Right");
        yield return C("Navigation.ShortcutTarget", "Navigation", "Phase 3", "Ctrl+T");
        yield return C("Navigation.Documents", "Navigation", "M2", "Ctrl+;");
        yield return C("Navigation.OtherPanelDirectory", "Navigation", "M2", "Ctrl+.", "Ctrl+Shift+`");
        yield return C("Network.Open", "Navigation", "Phase 3", "Ctrl+,", "Ctrl+`");
        yield return C("Panel.Swap", "Panel", "M2", "Ctrl+U");
        yield return C("Panel.ToggleHidden", "Panel", "M1", "Ctrl+H");
        yield return C("Panel.SmartNameWidth", "Panel", "Phase 3", "Ctrl+N");

        yield return C("Selection.ToggleAndNext", "Edit", "M1", "Insert");
        yield return C("Selection.ToggleAndSize", "Edit", "M1", "Space");
        yield return C("Selection.ExtendUp", "Edit", "M1", "Shift+Up");
        yield return C("Selection.ExtendDown", "Edit", "M1", "Shift+Down");
        yield return C("Selection.ExtendLeft", "Edit", "M1", "Shift+Left");
        yield return C("Selection.ExtendRight", "Edit", "M1", "Shift+Right");
        yield return C("Selection.ExtendToFirst", "Edit", "M1", "Shift+Home");
        yield return C("Selection.ExtendToLast", "Edit", "M1", "Shift+End");
        yield return C("Selection.ExtendPageUp", "Edit", "M1", "Shift+PageUp");
        yield return C("Selection.ExtendPageDown", "Edit", "M1", "Shift+PageDown");
        yield return C("Selection.SelectByMask", "Edit", "M1", "Num+", "Ctrl+=");
        yield return C("Selection.UnselectByMask", "Edit", "M1", "Num-", "Ctrl+-");
        yield return C("Selection.SelectAll", "Edit", "M1", "Ctrl+A", "Ctrl+Num+");
        yield return C("Selection.UnselectAll", "Edit", "M1", "Ctrl+D", "Ctrl+Num-");
        yield return C("Selection.InvertFiles", "Edit", "M1", "Num*");
        yield return C("Selection.InvertAll", "Edit", "M1", "Ctrl+Num*");
        yield return C("Selection.SameExtension", "Edit", "M1", "Shift+Num+");
        yield return C("Selection.UnselectSameExtension", "Edit", "M1", "Shift+Num-");
        yield return C("Selection.SameName", "Edit", "M1", "Ctrl+Shift+Num+");
        yield return C("Selection.UnselectSameName", "Edit", "M1", "Ctrl+Shift+Num-");
        yield return C("Selection.Restore", "Edit", "M1", "Ctrl+W");

        yield return C("Clipboard.Copy", "Edit", "M3", "Ctrl+C", "Ctrl+Insert");
        yield return C("Clipboard.Cut", "Edit", "M3", "Ctrl+X");
        yield return C("Clipboard.Paste", "Edit", "M3", "Ctrl+V", "Shift+Insert");
        yield return C("Clipboard.PasteIntoFocused", "Edit", "M3");
        yield return C("Clipboard.PasteShortcut", "Edit", "Phase 3", "Ctrl+S");
        yield return C("Clipboard.CopyFullName", "Edit", "M3", "Alt+Insert");
        yield return C("Clipboard.CopyName", "Edit", "M3", "Alt+Shift+Insert");
        yield return C("Clipboard.CopyPath", "Edit", "M3", "Ctrl+Alt+Insert");
        yield return C("Clipboard.CopyUncName", "Edit", "M3", "Ctrl+Shift+Insert");

        yield return C("CommandLine.Toggle", "Commands", "Phase 3", "Ctrl+Tab");
        yield return C("CommandLine.InsertName", "Commands", "Phase 3", "Ctrl+Enter");
        yield return C("CommandLine.InsertDosName", "Commands", "Phase 3", "Ctrl+Shift+Enter");
        yield return C("CommandLine.InsertDirectory", "Commands", "Phase 3", "Ctrl+Space");
        yield return C("CommandLine.InsertDirectoryDos", "Commands", "Phase 3", "Ctrl+Shift+Space");
        yield return C("CommandLine.InsertLeftDirectory", "Commands", "Phase 3", "Ctrl+[");
        yield return C("CommandLine.InsertLeftDirectoryDos", "Commands", "Phase 3", "Ctrl+Shift+[");
        yield return C("CommandLine.InsertRightDirectory", "Commands", "Phase 3", "Ctrl+]");
        yield return C("CommandLine.InsertRightDirectoryDos", "Commands", "Phase 3", "Ctrl+Shift+]");
        yield return C("Shell.CommandPrompt", "Commands", "Phase 3", "Num/", "Ctrl+/");

        yield return C("File.Properties", "Files", "M3", "Alt+Enter");
        yield return C("File.WindowsProperties", "Files", "M3", "Alt+Shift+Enter");
        yield return C("File.MakeList", "Commands", "M6", "Ctrl+M");
        yield return C("File.ConvertText", "Files", "Phase 3", "Ctrl+K");
        yield return C("File.Email", "Files", "Phase 5", "Ctrl+E");
        yield return C("Plugins.RepeatLast", "Tools", "Phase 4", "Ctrl+I");
        yield return C("Security.Owner", "Files", "Phase 5", "Ctrl+O");
        yield return C("Security.Permissions", "Files", "Phase 5", "Ctrl+P");
        yield return C("App.WindowMenu", "Application", "M1", "Alt+Space");
        yield return C("App.Minimize", "Application", "M1", "Shift+Escape");

        for (var d = 0; d <= 9; d++)
        {
            var n = d.ToString(CultureInfo.InvariantCulture);
            yield return N($"View.Mode{n}", Strings.Command_View_Mode, n, "View", "Phase 3", $"Alt+{n}");
            yield return N($"HotPaths.Define{n}", Strings.Command_HotPaths_Define, n, "Navigation", "M2", $"Ctrl+Shift+{n}");
            yield return N($"HotPaths.Go{n}", Strings.Command_HotPaths_Go, n, "Navigation", "M2", $"Ctrl+{n}", $"Shift+{n}");
            yield return N($"HotPaths.GoOther{n}", Strings.Command_HotPaths_GoOther, n, "Navigation", "M2", $"Ctrl+Alt+{n}", $"Alt+Shift+{n}");
        }

        yield return C("HotPaths.AddCurrent", "Navigation", "M2", "Ctrl+Shift+=");

        for (var letter = 'A'; letter <= 'Z'; letter++)
        {
            yield return N($"Drive.Switch{letter}", Strings.Command_Drive_Switch, letter, "Navigation", "M2", $"Shift+{letter}");
        }

        yield return C("App.Options", "Application", "M1", "Ctrl+Shift+O");
        yield return C("File.Duplicate", "Files", "M3", "Alt+Shift+D");
        yield return C("App.CommandSearch", "Application", "Phase 3", "Ctrl+Shift+P");
        yield return C("App.About", "Help", "M7");
        yield return C("Help.FirstSteps", "Help", "M7");
        yield return C("Options.ExportSettings", "Options", "M7");
        yield return C("Options.ImportSettings", "Options", "M7");
        yield return C("Options.ResetAllSettings", "Options", "M7");
        yield return C("View.ToggleMenuBar", "Options", "M1");
        yield return C("View.ToggleDriveStrip", "Options", "M1");
        yield return C("View.ToggleHotPathBar", "Options", "M2");
        yield return C("View.ToggleDirectoryLine", "Options", "M1");
        yield return C("View.ToggleColumnHeader", "Options", "M1");
        yield return C("View.ToggleInformationLine", "Options", "M1");
        yield return C("View.ToggleFunctionKeyBar", "Options", "M1");
        yield return C("View.ToggleMiddleToolbar", "Options", "M3");
    }

    private static CommandDefinition C(string id, string category, string plannedFor, params string[] chords)
        => Make(id, CommandText.Title(id), category, plannedFor, chords);

    private static CommandDefinition N(string id, string titleFormat, object number, string category, string plannedFor, params string[] chords)
        => Make(id, Loc.Format(titleFormat, number), category, plannedFor, chords);

    private static CommandDefinition Make(string id, string title, string category, string plannedFor, string[] chords)
        => new(id, title, category, [.. chords.Select(KeyChord.Parse)], plannedFor) { ShortLabel = CommandText.ShortLabel(id) };
}

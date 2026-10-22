// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.Core/Commands/MainMenu.cs

using Salandual.Core.Resources;

namespace Salandual.Core.Commands;

public enum PanelTarget { Active, Left, Right }

public sealed record MenuNode(string? Header, string? CommandId = null, PanelTarget Target = PanelTarget.Active, IReadOnlyList<MenuNode>? Children = null)
{
    public static MenuNode Separator { get; } = new("-");

    public bool IsSeparator => Header == "-";

    public bool Checkable { get; init; }

    public string? Dynamic { get; init; }
}

public static class MainMenu
{
    public static IReadOnlyList<MenuNode> Create() =>
    [
        PanelMenu("Left", PanelTarget.Left, "Drive.ChangeLeft"),
        Sub("Files",
            Cmd("File.QuickRename"),
            Cmd("File.View"),
            Cmd("File.ViewAlternate"),
            Cmd("File.ViewWith"),
            Cmd("File.Edit"),
            Cmd("File.EditWith"),
            Cmd("File.EditNew"),
            Cmd("File.Copy"),
            Cmd("File.Move"),
            Cmd("File.Duplicate"),
            Cmd("File.Email"),
            Cmd("File.Delete"),
            Cmd("File.Properties"),
            Sub("Security", Cmd("Security.Owner"), Cmd("Security.Permissions")),
            MenuNode.Separator,
            Cmd("File.ChangeAttributes"),
            Cmd("File.ChangeCase"),
            Cmd("File.ConvertText"),
            Cmd("Archive.Pack"),
            Cmd("Archive.Unpack"),
            MenuNode.Separator,
            Cmd("App.Exit")),
        Sub("Edit",
            Cmd("Clipboard.Cut"),
            Cmd("Clipboard.Copy"),
            Cmd("Clipboard.Paste"),
            Cmd("Clipboard.PasteIntoFocused"),
            Cmd("Clipboard.PasteShortcut"),
            MenuNode.Separator,
            Cmd("Clipboard.CopyFullName"),
            Cmd("Clipboard.CopyName"),
            Cmd("Clipboard.CopyPath"),
            Cmd("Clipboard.CopyUncName"),
            MenuNode.Separator,
            Cmd("Selection.SelectByMask"),
            Cmd("Selection.UnselectByMask"),
            Cmd("Selection.InvertFiles"),
            Cmd("Selection.SelectAll"),
            Cmd("Selection.UnselectAll"),
            Cmd("Selection.Restore"),
            MenuNode.Separator,
            Sub("Advanced",
                Cmd("Selection.Save"),
                Cmd("Selection.Load"),
                MenuNode.Separator,
                Cmd("Selection.SameExtension"),
                Cmd("Selection.UnselectSameExtension"),
                MenuNode.Separator,
                Cmd("Selection.SameName"),
                Cmd("Selection.UnselectSameName"),
                MenuNode.Separator,
                Cmd("Focus.PreviousSelected"),
                Cmd("Focus.NextSelected"))),
        Sub("Commands",
            Cmd("File.CreateDirectory"),
            Cmd("Panel.ChangeDirectory"),
            Cmd("Compare.Directories"),
            Cmd("Compare.Files"),
            Cmd("File.OccupiedSpace"),
            Cmd("Panel.CalculateDirectorySizes"),
            Cmd("Drive.Info"),
            Cmd("Find.Files"),
            Cmd("Find.Duplicates"),
            new MenuNode(Header("MenuSub_SavedSearches"), Children: []) { Dynamic = "Find.Saved" },
            Cmd("File.MakeList"),
            Cmd("Navigation.ShortcutTarget"),
            MenuNode.Separator,
            Cmd("Lists.OpenedFiles"),
            Cmd("Lists.WorkingDirectories"),
            MenuNode.Separator,
            Cmd("Network.ConnectDrive"),
            Cmd("Network.DisconnectDrive"),
            Cmd("Network.SharedDirectories"),
            MenuNode.Separator,
            Cmd("Shell.CommandPrompt"),
            MenuNode.Separator,
            Cmd("UserMenu.Open"),
            Cmd("Panel.OpenInExplorer")),
        Sub("Tools",
            Cmd("Plugins.RepeatLast")),
        Sub("Options",
            Cmd("App.Options"),
            Cmd("Options.ExportSettings"),
            Cmd("Options.ImportSettings"),
            Cmd("Options.ResetAllSettings"),
            MenuNode.Separator,
            Sub("Show",
                Toggle("View.ToggleMenuBar"),
                Toggle("View.ToggleDriveStrip"),
                Toggle("View.ToggleHotPathBar"),
                Toggle("View.ToggleDirectoryLine"),
                Toggle("View.ToggleColumnHeader"),
                Toggle("View.ToggleInformationLine"),
                Toggle("View.ToggleFunctionKeyBar"),
                Toggle("View.ToggleMiddleToolbar")),
            Toggle("Panel.ToggleHidden")),
        PanelMenu("Right", PanelTarget.Right, "Drive.ChangeRight"),
        Sub("Help",
            Cmd("Help.Contents"),
            Cmd("Help.WhatIsThis"),
            Cmd("Help.FirstSteps"),
            MenuNode.Separator,
            Cmd("App.About")),
    ];

    public static IEnumerable<MenuNode> Flatten(IEnumerable<MenuNode> nodes)
    {
        foreach (var node in nodes)
        {
            if (node.CommandId is not null)
            {
                yield return node;
            }

            if (node.Children is not null)
            {
                foreach (var child in Flatten(node.Children))
                {
                    yield return child;
                }
            }
        }
    }

    private static MenuNode PanelMenu(string name, PanelTarget target, string changeDrive) => Sub(name,
        Cmd(changeDrive, target),
        Sub("GoTo",
            Cmd("History.Back", target),
            Cmd("History.Forward", target),
            Cmd("Navigation.Parent", target),
            Cmd("Navigation.Root", target),
            Cmd("Navigation.OtherPanelDirectory", target),
            Cmd("Navigation.GoToFile", target)),
        Sub("SortBy",
            Cmd("Panel.SortByName", target),
            Cmd("Panel.SortByExtension", target),
            Cmd("Panel.SortByTime", target),
            Cmd("Panel.SortBySize", target)),
        MenuNode.Separator,
        Cmd("Panel.Maximize", target),
        Cmd("Panel.Swap"),
        MenuNode.Separator,
        Cmd("Panel.Filter", target),
        Cmd("Panel.Refresh", target));

    private static MenuNode Sub(string name, params MenuNode[] children) => new(Header("MenuSub_" + name), Children: children);

    private static MenuNode Cmd(string id, PanelTarget target = PanelTarget.Active) => new(Header("Menu_" + id.Replace('.', '_')), id, target);

    private static MenuNode Toggle(string id) => new(Header("Menu_" + id.Replace('.', '_')), id) { Checkable = true };

    private static string Header(string key) => Strings.ResourceManager.GetString(key, Strings.Culture) ?? throw new InvalidOperationException("Missing string resource " + key);
}

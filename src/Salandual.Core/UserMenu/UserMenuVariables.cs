// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.Core/UserMenu/UserMenuVariables.cs

using System.Text.RegularExpressions;
using Salandual.Core.Editing;
using Salandual.Core.Listing;
using Salandual.Core.Resources;

namespace Salandual.Core.UserMenu;

public sealed record UserMenuEntry(string FullName, bool IsDirectory)
{
    public string Name => FullName.LastIndexOf('\\') is var cut and >= 0 && cut < FullName.Length - 1 ? FullName[(cut + 1)..] : FullName;
}

public sealed record UserMenuContext
{
    public string ActivePath { get; init; } = "";

    public string OtherPath { get; init; } = "";

    public bool ActiveIsLeft { get; init; } = true;

    public IReadOnlyList<UserMenuEntry> Selected { get; init; } = [];

    public bool HasSelection { get; init; }

    public UserMenuEntry? Focused { get; init; }

    public IReadOnlyList<UserMenuEntry> OtherSelected { get; init; } = [];

    public bool OtherHasSelection { get; init; }

    public UserMenuEntry? OtherFocused { get; init; }

    public Func<string, UserMenuEntry?> OtherNamed { get; init; } = _ => null;

    public string WindowsFolder { get; init; } = Environment.GetFolderPath(Environment.SpecialFolder.Windows);

    public string SystemFolder { get; init; } = Environment.SystemDirectory;

    public string AppFolder { get; init; } = AppContext.BaseDirectory;

    public Func<string, string?> EnvironmentVariable { get; init; } = Environment.GetEnvironmentVariable;

    public Func<string, string> ShortPath { get; init; } = p => p;

    public string LeftPath => ActiveIsLeft ? ActivePath : OtherPath;

    public string RightPath => ActiveIsLeft ? OtherPath : ActivePath;
}

internal sealed record ComparePair(UserMenuEntry First, bool FirstActive, UserMenuEntry Second, bool SecondActive, bool FirstLeft, bool SecondLeft);

public static partial class UserMenuVariables
{
    public const string DefaultStartIn = "$(FullPath)";

    public static readonly IReadOnlyList<IReadOnlyList<string>> Groups =
    [
        ["$(FullName)", "$(Name)", "$(NamePart)", "$(ExtPart)", "$(FullPath)", "$(Drive)", "$(Path)",
            "$(DOSFullName)", "$(DOSName)", "$(DOSNamePart)", "$(DOSExtPart)", "$(DOSFullPath)", "$(DOSPath)"],
        ["$(ListOfSelectedNames)", "$(ListOfSelectedFullNames)"],
        ["$(FullPathLeft)", "$(FullPathRight)", "$(FullPathInactive)"],
        ["$(FileToCompareLeft)", "$(FileToCompareRight)", "$(FileToCompareActive)", "$(FileToCompareInactive)",
            "$(DirToCompareLeft)", "$(DirToCompareRight)", "$(DirToCompareActive)", "$(DirToCompareInactive)",
            "$(FileOrDirToCompareLeft)", "$(FileOrDirToCompareRight)", "$(FileOrDirToCompareActive)", "$(FileOrDirToCompareInactive)"],
        ["$(WinDir)", "$(SysDir)", "$(SalDir)", "$(DOSWinDir)", "$(DOSSysDir)", "$[NAME]"],
    ];

    private static readonly HashSet<string> _file = new(StringComparer.OrdinalIgnoreCase)
    {
        "FullName", "Name", "NamePart", "ExtPart", "FullPath", "Drive", "Path", "DOSFullName", "DOSName", "DOSNamePart", "DOSExtPart", "DOSFullPath", "DOSPath",
    };

    private static readonly HashSet<string> _lists = new(StringComparer.OrdinalIgnoreCase) { "ListOfSelectedNames", "ListOfSelectedFullNames" };

    private static readonly HashSet<string> _others = new(StringComparer.OrdinalIgnoreCase)
    {
        "FullPathLeft", "FullPathRight", "FullPathInactive", "WinDir", "SysDir", "SalDir", "DOSWinDir", "DOSSysDir",
    };

    private static bool IsCompare(string name) => CompareParts(name) is not null;

    private static (bool? Directory, string Side)? CompareParts(string name)
    {
        foreach (var (prefix, directory) in new (string, bool?)[] { ("FileOrDirToCompare", null), ("FileToCompare", false), ("DirToCompare", true) })
        {
            if (name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) && name[prefix.Length..] is var side
                && (side.Equals("Left", StringComparison.OrdinalIgnoreCase) || side.Equals("Right", StringComparison.OrdinalIgnoreCase)
                    || side.Equals("Active", StringComparison.OrdinalIgnoreCase) || side.Equals("Inactive", StringComparison.OrdinalIgnoreCase)))
            {
                return (directory, side);
            }
        }

        return null;
    }

    [GeneratedRegex(@"\$\(([^)]*)\)|\$\[([^\]]*)\]|\$\(")]
    private static partial Regex VariablePattern();

    private static IEnumerable<string> Named(string text)
    {
        foreach (Match m in VariablePattern().Matches(text))
        {
            yield return m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Success ? "[" + m.Groups[2].Value + "]" : "(";
        }
    }

    public static bool NamesAFile(UserMenuItem item) => Texts(item).SelectMany(Named).Any(_file.Contains);

    public static bool TakesAll(UserMenuItem item) => Texts(item).SelectMany(Named).Any(n => _lists.Contains(n) || IsCompare(n));

    private static IEnumerable<string> Texts(UserMenuItem item) => item.Kind == UserMenuItemKind.Script ? [item.StartIn] : [item.Program, item.Arguments, item.StartIn];

    public static string? Check(string text, bool arguments)
    {
        foreach (var name in Named(text))
        {
            if (name == "(")
            {
                return Strings.UserMenu_Unclosed;
            }

            if (name.StartsWith('['))
            {
                continue;
            }

            if (_lists.Contains(name))
            {
                if (!arguments)
                {
                    return Loc.Format(Strings.UserMenu_ListOnlyInArguments, "$(" + name + ")");
                }

                continue;
            }

            if (!_file.Contains(name) && !_others.Contains(name) && !IsCompare(name))
            {
                return Loc.Format(Strings.UserMenu_UnknownVariable, "$(" + name + ")");
            }
        }

        if (arguments)
        {
            foreach (var part in ExternalCommand.Split(text))
            {
                if (ListIn(part) is { } list && !IsWholeList(part))
                {
                    return Loc.Format(Strings.UserMenu_ListAlone, "$(" + list + ")");
                }
            }
        }

        return null;
    }

    private static string? ListIn(string part) => Named(part).FirstOrDefault(_lists.Contains);

    private static bool IsWholeList(string part) => part.Length > 3 && part.StartsWith("$(", StringComparison.Ordinal) && part.EndsWith(')') && _lists.Contains(part[2..^1]);

    public static string? Expand(string text, UserMenuContext context, UserMenuEntry? file, out string? error)
    {
        error = Check(text, arguments: false);
        if (error is not null)
        {
            return null;
        }

        var filler = new Filler(context, file);
        var result = filler.Fill(text);
        error = filler.Error;
        return error is null ? result : null;
    }

    public static IReadOnlyList<string>? ExpandArguments(string arguments, UserMenuContext context, UserMenuEntry? file, out string? error)
    {
        error = Check(arguments, arguments: true);
        if (error is not null)
        {
            return null;
        }

        var result = new List<string>();
        var filler = new Filler(context, file);
        foreach (var part in ExternalCommand.Split(arguments))
        {
            if (IsWholeList(part))
            {
                var full = part[2..^1].Equals("ListOfSelectedFullNames", StringComparison.OrdinalIgnoreCase);
                result.AddRange(context.Selected.Select(e => full ? e.FullName : e.Name));
                continue;
            }

            result.Add(filler.Fill(part));
            if (filler.Error is not null)
            {
                error = filler.Error;
                return null;
            }
        }

        return result;
    }

    private sealed class Filler(UserMenuContext context, UserMenuEntry? file)
    {
        private ComparePair? _pair;
        private string? _pairError;

        public string? Error { get; private set; }

        public string Fill(string text) => VariablePattern().Replace(text, m =>
        {
            var value = Value(m, out var error);
            Error ??= error;
            return value ?? "";
        });

        private string? Value(Match m, out string? error)
        {
            error = null;
            if (m.Groups[2].Success)
            {
                var name = m.Groups[2].Value;
                var value = name.Length == 0 ? null : context.EnvironmentVariable(name);
                if (value is null)
                {
                    error = Loc.Format(Strings.UserMenu_NoEnvironment, name);
                }

                return value;
            }

            var variable = m.Groups[1].Value;
            if (_file.Contains(variable))
            {
                if (file is null)
                {
                    error = Loc.Format(Strings.UserMenu_NeedsAFile, "$(" + variable + ")");
                    return null;
                }

                return FileValue(variable, file, context);
            }

            if (CompareParts(variable) is { } parts)
            {
                if (_pair is null && _pairError is null)
                {
                    _pair = Pair(context, out _pairError);
                }

                if (_pair is not { } pair)
                {
                    error = _pairError;
                    return null;
                }

                if (parts.Directory is { } directory && (pair.First.IsDirectory != directory || pair.Second.IsDirectory != directory))
                {
                    error = directory ? Strings.UserMenu_NoFoldersToCompare : Strings.UserMenu_NoFilesToCompare;
                    return null;
                }

                return Side(pair, parts.Side).FullName;
            }

            return variable.ToLowerInvariant() switch
            {
                "fullpathleft" => WithSlash(context.LeftPath),
                "fullpathright" => WithSlash(context.RightPath),
                "fullpathinactive" => WithSlash(context.OtherPath),
                "windir" => WithSlash(context.WindowsFolder),
                "sysdir" => WithSlash(context.SystemFolder),
                "saldir" => WithSlash(context.AppFolder),
                "doswindir" => WithSlash(context.ShortPath(context.WindowsFolder)),
                "dossysdir" => WithSlash(context.ShortPath(context.SystemFolder)),
                _ => "",
            };
        }
    }

    private static UserMenuEntry Side(ComparePair p, string side) => side.ToLowerInvariant() switch
    {
        "left" => p.FirstLeft == p.SecondLeft || p.FirstLeft ? p.First : p.Second,
        "right" => p.FirstLeft == p.SecondLeft || p.FirstLeft ? p.Second : p.First,
        "active" => p.FirstActive == p.SecondActive || p.FirstActive ? p.First : p.Second,
        _ => p.FirstActive == p.SecondActive || p.FirstActive ? p.Second : p.First,
    };

    private static string FileValue(string variable, UserMenuEntry file, UserMenuContext context)
    {
        var folder = Folder(file.FullName);
        var name = file.Name;
        string Short() => context.ShortPath(file.FullName);
        return variable.ToLowerInvariant() switch
        {
            "fullname" => file.FullName,
            "name" => name,
            "namepart" => NamePart(name),
            "extpart" => ExtPart(name),
            "fullpath" => WithSlash(folder),
            "drive" => FileListLine.Drive(folder),
            "path" => FileListLine.PathPart(folder),
            "dosfullname" => Short(),
            "dosname" => new UserMenuEntry(Short(), file.IsDirectory).Name,
            "dosnamepart" => NamePart(new UserMenuEntry(Short(), file.IsDirectory).Name),
            "dosextpart" => ExtPart(new UserMenuEntry(Short(), file.IsDirectory).Name),
            "dosfullpath" => WithSlash(context.ShortPath(folder)),
            _ => FileListLine.PathPart(context.ShortPath(folder)),
        };
    }

    internal static ComparePair? Pair(UserMenuContext c, out string? error)
    {
        error = null;
        var activeLeft = c.ActiveIsLeft;
        if (c.HasSelection && c.Selected.Count == 2)
        {
            return new(c.Selected[0], true, c.Selected[1], true, activeLeft, activeLeft);
        }

        if (c.HasSelection && c.Selected.Count == 1 && c.Focused is { } focused && !SameItem(focused, c.Selected[0]))
        {
            return new(c.Selected[0], true, focused, true, activeLeft, activeLeft);
        }

        var here = c.HasSelection ? c.Selected.Count == 1 ? c.Selected[0] : null : c.Focused;
        var there = c.OtherHasSelection ? c.OtherSelected.Count == 1 ? c.OtherSelected[0] : null : c.OtherFocused;
        if (here is not null && there is not null)
        {
            return new(here, true, there, false, activeLeft, !activeLeft);
        }

        if (here is not null && c.OtherNamed(here.Name) is { } same)
        {
            return new(here, true, same, false, activeLeft, !activeLeft);
        }

        error = Strings.UserMenu_NoPair;
        return null;
    }

    private static bool SameItem(UserMenuEntry a, UserMenuEntry b) => string.Equals(a.FullName, b.FullName, StringComparison.OrdinalIgnoreCase);

    private static string Folder(string fullName)
    {
        var cut = fullName.TrimEnd('\\').LastIndexOf('\\');
        return cut < 0 ? fullName : fullName[..(cut + 1)];
    }

    private static string WithSlash(string folder) => folder.Length == 0 || folder.EndsWith('\\') ? folder : folder + "\\";

    private static string NamePart(string name) => name.LastIndexOf('.') is var dot and > 0 ? name[..dot] : name;

    private static string ExtPart(string name) => name.LastIndexOf('.') is var dot and > 0 ? name[(dot + 1)..] : "";
}

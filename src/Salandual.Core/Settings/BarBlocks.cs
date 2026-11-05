// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.Core/Settings/BarBlocks.cs

namespace Salandual.Core.Settings;

public enum BarBlock { HotPaths, UserMenu, BackForward }

public enum BlockPlace { OwnRow, WithOther, WithDrives }

public sealed record BarArrangement(IReadOnlyList<IReadOnlyList<BarBlock>> Rows, IReadOnlyList<BarBlock> WithDrives);

public readonly record struct BlockMove(BlockPlace Place, BarBlock? Beside, bool Before);

public sealed record BarBlockSettings
{
    public BarBlock[][] Rows { get; set; } = [[BarBlock.UserMenu], [BarBlock.BackForward], [BarBlock.HotPaths]];

    public BarBlock[] WithDrives { get; set; } = [];

    public bool Locked { get; set; }

    public bool IconsOnly { get; set; }

    public int? HotPathsLength { get; set; }

    public int? UserMenuLength { get; set; }

    public int? BackForwardLength { get; set; }

    public const int MinLength = 60, MaxLength = 4000;

    public static IReadOnlyList<BarBlock> All { get; } = [BarBlock.UserMenu, BarBlock.BackForward, BarBlock.HotPaths];

    public BlockPlace this[BarBlock block]
        => WithDrives.Contains(block) ? BlockPlace.WithDrives
            : Rows.FirstOrDefault(r => r.Contains(block)) is { Length: > 1 } ? BlockPlace.WithOther
            : BlockPlace.OwnRow;

    public int? Length(BarBlock block) => block switch
    {
        BarBlock.HotPaths => HotPathsLength,
        BarBlock.UserMenu => UserMenuLength,
        _ => BackForwardLength,
    };

    public BarBlockSettings WithLength(BarBlock block, int? px)
    {
        var length = px is { } p ? Math.Clamp(p, MinLength, MaxLength) : (int?)null;
        return block switch
        {
            BarBlock.HotPaths => this with { HotPathsLength = length },
            BarBlock.UserMenu => this with { UserMenuLength = length },
            _ => this with { BackForwardLength = length },
        };
    }

    public BarBlockSettings Move(BarBlock block, BlockMove to)
    {
        var rows = Rows.Select(r => r.Where(b => b != block).ToList()).ToList();
        var drives = WithDrives.Where(b => b != block).ToList();
        var beside = to.Beside is { } n && n != block ? n : (BarBlock?)null;
        var place = to.Place;
        if (place == BlockPlace.WithOther && beside is { } d && drives.Contains(d))
        {
            place = BlockPlace.WithDrives;
        }

        switch (place)
        {
            case BlockPlace.WithOther when beside is { } other && rows.FindIndex(r => r.Contains(other)) is var at and >= 0:
                var row = rows[at];
                row.Insert(row.IndexOf(other) + (to.Before ? 0 : 1), block);
                break;
            case BlockPlace.WithDrives:
                var i = beside is { } by && drives.IndexOf(by) is var j and >= 0 ? j + (to.Before ? 0 : 1) : to.Before ? 0 : drives.Count;
                drives.Insert(i, block);
                break;
            default:
                var index = beside is { } a && rows.FindIndex(r => r.Contains(a)) is var k and >= 0 ? k + (to.Before ? 0 : 1)
                    : to.Before && beside is null ? 0 : rows.Count;
                rows.Insert(Math.Clamp(index, 0, rows.Count), [block]);
                break;
        }

        return this with { Rows = [.. rows.Where(r => r.Count > 0).Select(r => r.ToArray())], WithDrives = [.. drives] };
    }

    public BlockPlace Effective(BarBlock block, Func<BarBlock, bool> shown, bool drivesShared)
    {
        var arrangement = Arrange(shown, drivesShared);
        return arrangement.WithDrives.Contains(block) ? BlockPlace.WithDrives
            : arrangement.Rows.FirstOrDefault(r => r.Contains(block)) is { Count: > 1 } ? BlockPlace.WithOther
            : BlockPlace.OwnRow;
    }

    public BarArrangement Arrange(Func<BarBlock, bool> shown, bool drivesShared)
    {
        var rows = Rows.Select(r => (IReadOnlyList<BarBlock>)[.. r.Where(shown)]).Where(r => r.Count > 0).ToList();
        IReadOnlyList<BarBlock> drives = [.. WithDrives.Where(shown)];
        if (!drivesShared)
        {
            if (drives.Count > 0)
            {
                rows.Add(drives);
            }

            drives = [];
        }

        return new BarArrangement(rows, drives);
    }

    internal BarBlockSettings Normalize()
    {
        var seen = new HashSet<BarBlock>();
        var rows = (Rows ?? []).Where(r => r is not null).Select(r => r.Where(b => Enum.IsDefined(b) && seen.Add(b)).ToList()).Where(r => r.Count > 0).ToList();
        var drives = (WithDrives ?? []).Where(b => Enum.IsDefined(b) && seen.Add(b)).ToList();
        foreach (var block in All.Where(b => !seen.Contains(b)))
        {
            var hot = rows.FindIndex(r => r.Contains(BarBlock.HotPaths));
            rows.Insert(block == BarBlock.BackForward && hot >= 0 ? hot : rows.Count, [block]);
        }

        static int? Clamp(int? px) => px is { } p ? Math.Clamp(p, MinLength, MaxLength) : null;
        return this with
        {
            Rows = [.. rows.Select(r => r.ToArray())],
            WithDrives = [.. drives],
            HotPathsLength = Clamp(HotPathsLength),
            UserMenuLength = Clamp(UserMenuLength),
            BackForwardLength = Clamp(BackForwardLength),
        };
    }
}

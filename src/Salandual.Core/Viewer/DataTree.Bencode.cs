// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.Core/Viewer/DataTree.Bencode.cs

using System.Globalization;
using Salandual.Core.Resources;

namespace Salandual.Core.Viewer;

public sealed partial class DataTree
{
    private const int HexUpTo = 64;

    public static DataTree FromBencode(BencodeValue root, Func<DateTimeOffset, string> date, CancellationToken token = default)
    {
        var tree = new DataTree("", DataFormat.Bencode);
        tree.Root = Node(root, null, 0, date, token);
        tree.Count();
        return tree;
    }

    private static DataNode Node(BencodeValue value, string? key, int index, Func<DateTimeOffset, string> date, CancellationToken token)
    {
        var node = new DataNode { Name = key is null ? null : TorrentNames.Shown(key), Index = index, Line = 1, Start = value.Start, End = -1 };
        switch (value.Kind)
        {
            case BencodeKind.Dictionary:
                node.Kind = DataKind.Object;
                var n = 0;
                foreach (var (k, v) in value.Dictionary)
                {
                    if ((++n & 0x3FF) == 0)
                    {
                        token.ThrowIfCancellationRequested();
                    }

                    node.Add(Node(v, k, 0, date, token));
                }

                break;
            case BencodeKind.List:
                node.Kind = DataKind.Array;
                for (var i = 0; i < value.List.Count; i++)
                {
                    if ((i & 0x3FF) == 0x3FF)
                    {
                        token.ThrowIfCancellationRequested();
                    }

                    node.Add(Node(value.List[i], null, i, date, token));
                }

                break;
            case BencodeKind.Integer:
                node.Kind = DataKind.Number;
                node.Own = value.Integer.ToString(CultureInfo.InvariantCulture);
                if (key is "creation date" && value.Integer is > 0 and < 253402300800)
                {
                    node.ShownText = node.Own + " · " + date(DateTimeOffset.FromUnixTimeSeconds(value.Integer));
                }

                break;
            default:
                var bytes = value.Bytes;
                if (value.IsText)
                {
                    node.Kind = DataKind.String;
                    node.Own = value.Text;
                    node.ShownText = "\"" + TorrentNames.Shown(node.Own!) + "\"";
                }
                else
                {
                    node.Kind = DataKind.Bytes;
                    node.Own = TorrentInfo.Hex(bytes);
                    var size = bytes.Length.ToString("N0", CultureInfo.CurrentCulture);
                    node.ShownText = key is "pieces" && bytes.Length % 20 == 0
                        ? Loc.Format(Strings.Torrent_Tree_Hashes, size, (bytes.Length / 20).ToString("N0", CultureInfo.CurrentCulture))
                        : bytes.Length <= HexUpTo ? node.Own : Loc.Format(Strings.Torrent_Tree_Bytes, size);
                }

                break;
        }

        return node;
    }
}

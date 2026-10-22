// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.Core/Compare/TextDiff.cs

using System.Diagnostics;

namespace Salandual.Core.Compare;

public enum LineChange : byte
{
    Same,
    Changed,

    SpacingOrCase,

    Added,

    Removed,
}

public readonly record struct DiffRow(int Left, int Right, LineChange Change);

public readonly record struct DiffBlock(int Row, int Count);

public sealed record TextDiffOptions(bool IgnoreWhiteSpace = false, bool IgnoreCase = false);

public readonly record struct InlinePiece(int Start, int Length, bool SpacingOrCase = false);

public sealed class TextDiff
{
    private const int MaxPairing = 250_000;

    private const double Alike = 0.45;

    private TextDiff(List<DiffRow> rows, bool finished)
    {
        Rows = rows;
        Finished = finished;
        var blocks = new List<DiffBlock>();
        for (var i = 0; i < rows.Count; i++)
        {
            if (rows[i].Change == LineChange.Same)
            {
                continue;
            }

            var start = i;
            while (i + 1 < rows.Count && rows[i + 1].Change != LineChange.Same)
            {
                i++;
            }

            blocks.Add(new DiffBlock(start, i - start + 1));
        }

        Blocks = blocks;
        foreach (var row in rows)
        {
            switch (row.Change)
            {
                case LineChange.Changed:
                    Changed++;
                    break;
                case LineChange.SpacingOrCase:
                    SpacingOrCase++;
                    break;
                case LineChange.Added:
                    Added++;
                    break;
                case LineChange.Removed:
                    Removed++;
                    break;
            }
        }
    }

    public IReadOnlyList<DiffRow> Rows { get; }

    public IReadOnlyList<DiffBlock> Blocks { get; }

    public int Changed { get; }

    public int SpacingOrCase { get; }

    public int Added { get; }

    public int Removed { get; }

    public bool Finished { get; }

    public int BlockOf(int row)
    {
        int lo = 0, hi = Blocks.Count - 1;
        while (lo <= hi)
        {
            var mid = (lo + hi) / 2;
            var b = Blocks[mid];
            if (row < b.Row)
            {
                hi = mid - 1;
            }
            else if (row >= b.Row + b.Count)
            {
                lo = mid + 1;
            }
            else
            {
                return mid;
            }
        }

        return -1;
    }

    public static TextDiff Compare(IReadOnlyList<string> left, IReadOnlyList<string> right, TextDiffOptions? options = null, TimeSpan? limit = null, CancellationToken cancel = default)
    {
        options ??= new();
        var keys = new Dictionary<string, int>(StringComparer.Ordinal);
        var a = new int[left.Count];
        var b = new int[right.Count];
        var texts = new List<string>();
        for (var i = 0; i < a.Length; i++)
        {
            a[i] = Id(left[i]);
        }

        for (var i = 0; i < b.Length; i++)
        {
            b[i] = Id(right[i]);
        }

        var myers = new Myers(a, b, Stopwatch.StartNew(), limit ?? TimeSpan.FromSeconds(4), cancel);
        var match = myers.Run();
        var rows = new List<DiffRow>(Math.Max(a.Length, b.Length));
        int x = 0, y = 0;
        while (x < a.Length || y < b.Length)
        {
            if (x < a.Length && y < b.Length && match[x] == y)
            {
                rows.Add(new DiffRow(x++, y++, LineChange.Same));
                continue;
            }

            var x0 = x;
            while (x < a.Length && match[x] < 0)
            {
                x++;
            }

            var y0 = y;
            var next = x < a.Length ? match[x] : b.Length;
            y = next;
            Pair(texts, a, b, x0, x, y0, y, rows, cancel);
        }

        if (!(options.IgnoreWhiteSpace && options.IgnoreCase))
        {
            for (var i = 0; i < rows.Count; i++)
            {
                if (rows[i].Change == LineChange.Changed && OnlySpacingOrCase(left[rows[i].Left], right[rows[i].Right]))
                {
                    rows[i] = rows[i] with { Change = LineChange.SpacingOrCase };
                }
            }
        }

        return new TextDiff(rows, myers.Finished);

        int Id(string line)
        {
            var key = Key(line, options);
            if (!keys.TryGetValue(key, out var id))
            {
                id = keys.Count;
                keys[key] = id;
                texts.Add(key);
            }

            return id;
        }
    }

    public static string Key(string line, TextDiffOptions options)
    {
        if (options.IgnoreWhiteSpace)
        {
            line = string.Concat(line.Where(c => !char.IsWhiteSpace(c)));
        }
        else
        {
            line = line.TrimEnd('\r', '\n');
        }

        return options.IgnoreCase ? line.ToUpperInvariant() : line;
    }

    private static readonly TextDiffOptions _ignoreBoth = new(IgnoreWhiteSpace: true, IgnoreCase: true);

    public static bool OnlySpacingOrCase(string left, string right)
        => string.Equals(Key(left, _ignoreBoth), Key(right, _ignoreBoth), StringComparison.Ordinal);

    private static void Pair(List<string> texts, int[] a, int[] b, int x0, int x1, int y0, int y1, List<DiffRow> rows, CancellationToken cancel)
    {
        int n = x1 - x0, m = y1 - y0;
        if (n == 0 || m == 0 || (long)n * m > MaxPairing)
        {
            var both = n == 0 || m == 0 ? 0 : Math.Min(n, m);
            for (var i = 0; i < both; i++)
            {
                rows.Add(new DiffRow(x0 + i, y0 + i, LineChange.Changed));
            }

            for (var i = both; i < n; i++)
            {
                rows.Add(new DiffRow(x0 + i, -1, LineChange.Removed));
            }

            for (var j = both; j < m; j++)
            {
                rows.Add(new DiffRow(-1, y0 + j, LineChange.Added));
            }

            return;
        }

        var score = new double[n + 1, m + 1];
        for (var i = 1; i <= n; i++)
        {
            cancel.ThrowIfCancellationRequested();
            for (var j = 1; j <= m; j++)
            {
                var best = Math.Max(score[i - 1, j], score[i, j - 1]);
                var like = Likeness(texts[a[x0 + i - 1]], texts[b[y0 + j - 1]]);
                if (like >= Alike)
                {
                    best = Math.Max(best, score[i - 1, j - 1] + like);
                }

                score[i, j] = best;
            }
        }

        var pairs = new List<(int I, int J)>();
        for (int i = n, j = m; i > 0 && j > 0;)
        {
            if (score[i, j] == score[i - 1, j])
            {
                i--;
            }
            else if (score[i, j] == score[i, j - 1])
            {
                j--;
            }
            else
            {
                pairs.Add((i - 1, j - 1));
                i--;
                j--;
            }
        }

        pairs.Reverse();
        int xi = 0, yj = 0;
        foreach (var (pi, pj) in pairs.Append((n, m)))
        {
            for (; xi < pi && yj < pj; xi++, yj++)
            {
                rows.Add(new DiffRow(x0 + xi, y0 + yj, LineChange.Changed));
            }

            for (; xi < pi; xi++)
            {
                rows.Add(new DiffRow(x0 + xi, -1, LineChange.Removed));
            }

            for (; yj < pj; yj++)
            {
                rows.Add(new DiffRow(-1, y0 + yj, LineChange.Added));
            }

            if (pi < n)
            {
                rows.Add(new DiffRow(x0 + xi++, y0 + yj++, LineChange.Changed));
            }
        }
    }

    internal static double Likeness(string a, string b)
    {
        a = a.Trim();
        b = b.Trim();
        if (a.Length == 0 || b.Length == 0)
        {
            return a.Length == b.Length ? 1 : 0;
        }

        if (a.Length < 2 || b.Length < 2)
        {
            return a == b ? 1 : 0;
        }

        var pairs = new Dictionary<int, int>();
        for (var i = 0; i + 1 < a.Length; i++)
        {
            var key = (a[i] << 16) | a[i + 1];
            pairs[key] = pairs.GetValueOrDefault(key) + 1;
        }

        var shared = 0;
        for (var i = 0; i + 1 < b.Length; i++)
        {
            var key = (b[i] << 16) | b[i + 1];
            if (pairs.TryGetValue(key, out var left) && left > 0)
            {
                pairs[key] = left - 1;
                shared++;
            }
        }

        return 2.0 * shared / (a.Length - 1 + b.Length - 1);
    }

    public static (List<InlinePiece> Left, List<InlinePiece> Right) Inline(string left, string right, TextDiffOptions? options = null)
    {
        options ??= new();
        var (l, r) = Pieces(left, right, options);
        if (OnlySpacingOrCase(left, right))
        {
            return ([.. l.Select(p => new InlinePiece(p.Start, p.Length, true))], [.. r.Select(p => new InlinePiece(p.Start, p.Length, true))]);
        }

        var (ml, mr) = options == _ignoreBoth ? (l, r) : Pieces(left, right, _ignoreBoth);
        return (Split(left, l, ml), Split(right, r, mr));

        static List<InlinePiece> Split(string text, List<(int Start, int Length)> pieces, List<(int Start, int Length)> real)
        {
            var result = new List<InlinePiece>();
            foreach (var (start, length) in pieces)
            {
                var parts = new List<InlinePiece>();
                for (var i = start; i < start + length;)
                {
                    var minor = !real.Any(p => i >= p.Start && i < p.Start + p.Length);
                    var from = i;
                    while (i < start + length && minor == !real.Any(p => i >= p.Start && i < p.Start + p.Length))
                    {
                        i++;
                    }

                    parts.Add(new InlinePiece(from, i - from, minor));
                }

                if (parts.Any(p => !p.SpacingOrCase))
                {
                    parts = [.. parts.Select(p => p.SpacingOrCase && string.IsNullOrWhiteSpace(text.Substring(p.Start, p.Length)) ? p with { SpacingOrCase = false } : p)];
                }

                foreach (var p in parts)
                {
                    if (result.Count > 0 && result[^1].SpacingOrCase == p.SpacingOrCase && result[^1].Start + result[^1].Length == p.Start)
                    {
                        result[^1] = result[^1] with { Length = result[^1].Length + p.Length };
                    }
                    else
                    {
                        result.Add(p);
                    }
                }
            }

            return result;
        }
    }

    private static (List<(int Start, int Length)> Left, List<(int Start, int Length)> Right) Pieces(string left, string right, TextDiffOptions options)
    {
        var lt = Tokens(left);
        var rt = Tokens(right);
        var ids = new Dictionary<string, int>(StringComparer.Ordinal);
        int[] Ids(string text, List<(int Start, int Length)> tokens)
            => tokens.Select(t =>
            {
                var piece = text.Substring(t.Start, t.Length);
                if (options.IgnoreWhiteSpace && piece.All(char.IsWhiteSpace))
                {
                    return -1;
                }

                var key = options.IgnoreCase ? piece.ToUpperInvariant() : piece;
                if (!ids.TryGetValue(key, out var id))
                {
                    ids[key] = id = ids.Count;
                }

                return id;
            }).ToArray();

        var a = Ids(left, lt);
        var b = Ids(right, rt);
        if (options.IgnoreWhiteSpace)
        {
            (a, lt) = Without(a, lt);
            (b, rt) = Without(b, rt);
        }

        var match = new Myers(a, b, Stopwatch.StartNew(), TimeSpan.FromMilliseconds(50), default).Run();
        var matchedRight = new bool[b.Length];
        foreach (var j in match)
        {
            if (j >= 0)
            {
                matchedRight[j] = true;
            }
        }

        return (Lit(lt, i => match[i] < 0), Lit(rt, j => !matchedRight[j]));

        static (int[], List<(int Start, int Length)>) Without(int[] ids, List<(int Start, int Length)> tokens)
        {
            var keep = Enumerable.Range(0, ids.Length).Where(i => ids[i] >= 0).ToList();
            return ([.. keep.Select(i => ids[i])], [.. keep.Select(i => tokens[i])]);
        }

        static List<(int Start, int Length)> Lit(List<(int Start, int Length)> tokens, Func<int, bool> differs)
        {
            var result = new List<(int Start, int Length)>();
            for (var i = 0; i < tokens.Count; i++)
            {
                if (!differs(i))
                {
                    continue;
                }

                var (start, length) = tokens[i];
                if (result.Count > 0 && result[^1].Start + result[^1].Length == start)
                {
                    result[^1] = (result[^1].Start, result[^1].Length + length);
                }
                else
                {
                    result.Add((start, length));
                }
            }

            return result;
        }
    }

    internal static List<(int Start, int Length)> Tokens(string text)
    {
        var tokens = new List<(int, int)>();
        var i = 0;
        while (i < text.Length)
        {
            var start = i;
            if (char.IsLetterOrDigit(text[i]) || text[i] == '_')
            {
                while (i < text.Length && (char.IsLetterOrDigit(text[i]) || text[i] == '_'))
                {
                    i++;
                }
            }
            else if (char.IsWhiteSpace(text[i]))
            {
                while (i < text.Length && char.IsWhiteSpace(text[i]))
                {
                    i++;
                }
            }
            else
            {
                i++;
            }

            tokens.Add((start, i - start));
        }

        return tokens;
    }

    private sealed class Myers(int[] a, int[] b, Stopwatch clock, TimeSpan limit, CancellationToken cancel)
    {
        private readonly int[] _match = Enumerable.Repeat(-1, a.Length).ToArray();

        public bool Finished { get; private set; } = true;

        public int[] Run()
        {
            Diff(0, a.Length, 0, b.Length);
            return _match;
        }

        private void Diff(int a0, int a1, int b0, int b1)
        {
            while (true)
            {
                while (a0 < a1 && b0 < b1 && a[a0] == b[b0])
                {
                    _match[a0++] = b0++;
                }

                while (a0 < a1 && b0 < b1 && a[a1 - 1] == b[b1 - 1])
                {
                    _match[--a1] = --b1;
                }

                if (a0 == a1 || b0 == b1)
                {
                    return;
                }

                if (MiddleSnake(a0, a1, b0, b1) is not var (x, y))
                {
                    return;
                }

                if (x - a0 + (y - b0) < a1 - x + (b1 - y))
                {
                    Diff(a0, x, b0, y);
                    (a0, b0) = (x, y);
                }
                else
                {
                    Diff(x, a1, y, b1);
                    (a1, b1) = (x, y);
                }
            }
        }

        private (int X, int Y)? MiddleSnake(int a0, int a1, int b0, int b1)
        {
            int n = a1 - a0, m = b1 - b0;
            var max = (n + m + 1) / 2;
            var offset = max + 1;
            var size = (2 * max) + 3;
            var v1 = new int[size];
            var v2 = new int[size];
            Array.Fill(v1, -1);
            Array.Fill(v2, -1);
            v1[offset + 1] = 0;
            v2[offset + 1] = 0;
            var delta = n - m;
            var front = (delta & 1) != 0;
            int k1Start = 0, k1End = 0, k2Start = 0, k2End = 0;
            for (var d = 0; d <= max; d++)
            {
                if ((d & 63) == 0)
                {
                    cancel.ThrowIfCancellationRequested();
                    if (clock.Elapsed > limit)
                    {
                        Finished = false;
                        return null;
                    }
                }

                for (var k1 = -d + k1Start; k1 <= d - k1End; k1 += 2)
                {
                    var i1 = offset + k1;
                    var x1 = k1 == -d || (k1 != d && v1[i1 - 1] < v1[i1 + 1]) ? v1[i1 + 1] : v1[i1 - 1] + 1;
                    var y1 = x1 - k1;
                    while (x1 < n && y1 < m && a[a0 + x1] == b[b0 + y1])
                    {
                        x1++;
                        y1++;
                    }

                    v1[i1] = x1;
                    if (x1 > n)
                    {
                        k1End += 2;
                    }
                    else if (y1 > m)
                    {
                        k1Start += 2;
                    }
                    else if (front)
                    {
                        var i2 = offset + delta - k1;
                        if (i2 >= 0 && i2 < size && v2[i2] != -1 && x1 >= n - v2[i2])
                        {
                            return (a0 + x1, b0 + y1);
                        }
                    }
                }

                for (var k2 = -d + k2Start; k2 <= d - k2End; k2 += 2)
                {
                    var i2 = offset + k2;
                    var x2 = k2 == -d || (k2 != d && v2[i2 - 1] < v2[i2 + 1]) ? v2[i2 + 1] : v2[i2 - 1] + 1;
                    var y2 = x2 - k2;
                    while (x2 < n && y2 < m && a[a1 - x2 - 1] == b[b1 - y2 - 1])
                    {
                        x2++;
                        y2++;
                    }

                    v2[i2] = x2;
                    if (x2 > n)
                    {
                        k2End += 2;
                    }
                    else if (y2 > m)
                    {
                        k2Start += 2;
                    }
                    else if (!front)
                    {
                        var i1 = offset + delta - k2;
                        if (i1 >= 0 && i1 < size && v1[i1] != -1)
                        {
                            var x1 = v1[i1];
                            var y1 = x1 - (i1 - offset);
                            if (x1 >= n - x2)
                            {
                                return (a0 + x1, b0 + y1);
                            }
                        }
                    }
                }
            }

            return null;
        }
    }
}

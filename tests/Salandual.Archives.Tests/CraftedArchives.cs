// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : tests/Salandual.Archives.Tests/CraftedArchives.cs

using System.Formats.Tar;
using System.IO.Compression;
using System.Text;

namespace Salandual.Archives.Tests;

internal static class CraftedArchives
{
    public static readonly string[] Names = ["../../evil.txt", "C:/Windows/win.ini", "Summer/CON.txt", "notes.txt:hidden", "Summer/ok.txt"];

    public static void Zip(string path, IEnumerable<(string Name, byte[] Data)> entries, CompressionLevel level = CompressionLevel.Optimal)
    {
        using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
        foreach (var (name, data) in entries)
        {
            using var stream = archive.CreateEntry(name, level).Open();
            stream.Write(data);
        }
    }

    public static void Tar(string path, IEnumerable<TarEntry> entries)
    {
        using var file = File.Create(path);
        using var writer = new TarWriter(file, TarEntryFormat.Pax);
        foreach (var entry in entries)
        {
            writer.WriteEntry(entry);
        }
    }

    public static PaxTarEntry TarFile(string name, string text)
        => new(TarEntryType.RegularFile, name) { DataStream = new MemoryStream(Encoding.UTF8.GetBytes(text)) };

    public static PaxTarEntry TarLink(string name, string target, bool hard = false)
        => new(hard ? TarEntryType.HardLink : TarEntryType.SymbolicLink, name) { LinkName = target };

    public static void Rar5(string path, IEnumerable<(string Name, byte[] Data)> entries)
    {
        using var file = File.Create(path);
        file.Write([0x52, 0x61, 0x72, 0x21, 0x1A, 0x07, 0x01, 0x00]);
        Block(file, [.. VInt(1), .. VInt(0), .. VInt(0)], []);
        foreach (var (name, data) in entries)
        {
            var nameBytes = Encoding.UTF8.GetBytes(name);
            var header = new List<byte>();
            header.AddRange(VInt(2));
            header.AddRange(VInt(0x0002));
            header.AddRange(VInt((ulong)data.Length));
            header.AddRange(VInt(0x0004));
            header.AddRange(VInt((ulong)data.Length));
            header.AddRange(VInt(0x20));
            header.AddRange(BitConverter.GetBytes(Crc32(data)));
            header.AddRange(VInt(0));
            header.AddRange(VInt(0));
            header.AddRange(VInt((ulong)nameBytes.Length));
            header.AddRange(nameBytes);
            Block(file, [.. header], data);
        }

        Block(file, [.. VInt(5), .. VInt(0), .. VInt(0)], []);
    }

    private static void Block(Stream file, byte[] header, byte[] data)
    {
        var withSize = VInt((ulong)header.Length).Concat(header).ToArray();
        file.Write(BitConverter.GetBytes(Crc32(withSize)));
        file.Write(withSize);
        file.Write(data);
    }

    private static byte[] VInt(ulong value)
    {
        var bytes = new List<byte>();
        do
        {
            var b = (byte)(value & 0x7F);
            value >>= 7;
            bytes.Add(value > 0 ? (byte)(b | 0x80) : b);
        }
        while (value > 0);
        return [.. bytes];
    }

    private static uint Crc32(byte[] data)
    {
        var crc = 0xFFFFFFFFu;
        foreach (var b in data)
        {
            crc ^= b;
            for (var i = 0; i < 8; i++)
            {
                crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320u : crc >> 1;
            }
        }

        return ~crc;
    }
}

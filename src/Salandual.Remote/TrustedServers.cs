// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.Remote/TrustedServers.cs

using System.Buffers.Binary;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Salandual.Remote;

public enum KeyTrust
{
    Unknown,

    Trusted,

    Changed,
}

public sealed record TrustedKey(string Server, string Kind, string Algorithm, string Fingerprint, DateTime Since);

public sealed class TrustedServers
{
    public const string FileName = "server-keys.txt";

    private readonly string? _file;
    private readonly Func<string?> _knownHosts;
    private readonly List<TrustedKey> _keys = [];
    private bool _loaded;

    public TrustedServers(string? file, Func<string?> knownHosts)
    {
        _file = file;
        _knownHosts = knownHosts;
    }

    public static TrustedServers InMemory() => new(null, () => null);

    public static string? UserKnownHosts()
        => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) is { Length: > 0 } home ? Path.Combine(home, ".ssh", "known_hosts") : null;

    public bool ReadKnownHosts { get; set; } = true;

    public IReadOnlyList<TrustedKey> Keys
    {
        get
        {
            lock (_keys)
            {
                Load();
                return [.. _keys];
            }
        }
    }

    public static string ServerName(string host, int port) => (host.Contains(':') ? "[" + host + "]" : host) + ":" + port.ToString(CultureInfo.InvariantCulture);

    public static string SshFingerprint(byte[] keyBlob) => Convert.ToBase64String(SHA256.HashData(keyBlob)).TrimEnd('=');

    public static string SshAlgorithm(byte[] keyBlob)
    {
        var name = BlobType(keyBlob) ?? "";
        return name switch
        {
            "ssh-ed25519" => "ED25519",
            "ssh-ed448" => "ED448",
            "ssh-rsa" => "RSA",
            "ssh-dss" => "DSA",
            _ when name.StartsWith("ecdsa-sha2-", StringComparison.Ordinal) => "ECDSA",
            _ => name.ToUpperInvariant(),
        };
    }

    private static string? BlobType(byte[] blob)
    {
        if (blob.Length < 4)
        {
            return null;
        }

        var length = BinaryPrimitives.ReadUInt32BigEndian(blob);
        return length is > 0 and < 64 && 4 + length <= blob.Length ? Encoding.ASCII.GetString(blob, 4, (int)length) : null;
    }

    public static string CertificateFingerprint(byte[] rawCertificate) => Convert.ToHexString(SHA256.HashData(rawCertificate)).Chunk(2).Select(p => new string(p)).Aggregate((a, b) => a + ":" + b);

    public KeyTrust CheckSsh(string host, int port, byte[] keyBlob)
    {
        var server = ServerName(host, port);
        var algorithm = SshAlgorithm(keyBlob);
        var fingerprint = SshFingerprint(keyBlob);
        lock (_keys)
        {
            Load();
            var mine = _keys.Where(k => k.Kind == "ssh" && string.Equals(k.Server, server, StringComparison.OrdinalIgnoreCase) && k.Algorithm == algorithm).ToList();
            if (mine.Any(k => k.Fingerprint == fingerprint))
            {
                return KeyTrust.Trusted;
            }

            if (mine.Count > 0)
            {
                return KeyTrust.Changed;
            }
        }

        return ReadKnownHosts ? CheckKnownHosts(host, port, keyBlob) : KeyTrust.Unknown;
    }

    public KeyTrust CheckCertificate(string host, int port, string fingerprint)
    {
        var server = ServerName(host, port);
        lock (_keys)
        {
            Load();
            var mine = _keys.Where(k => k.Kind == "tls" && string.Equals(k.Server, server, StringComparison.OrdinalIgnoreCase)).ToList();
            return mine.Any(k => k.Fingerprint == fingerprint) ? KeyTrust.Trusted : mine.Count > 0 ? KeyTrust.Changed : KeyTrust.Unknown;
        }
    }

    public void Remember(string host, int port, string kind, string algorithm, string fingerprint)
    {
        var server = ServerName(host, port);
        lock (_keys)
        {
            Load();
            _keys.RemoveAll(k => k.Kind == kind && string.Equals(k.Server, server, StringComparison.OrdinalIgnoreCase) && (kind == "tls" || k.Algorithm == algorithm));
            _keys.Add(new TrustedKey(server, kind, algorithm, fingerprint, DateTime.Now));
            Save();
        }
    }

    public bool Remove(TrustedKey key)
    {
        lock (_keys)
        {
            Load();
            var removed = _keys.Remove(key);
            if (removed)
            {
                Save();
            }

            return removed;
        }
    }

    internal KeyTrust CheckKnownHosts(string host, int port, byte[] keyBlob)
    {
        if (_knownHosts() is not { } path)
        {
            return KeyTrust.Unknown;
        }

        string[] lines;
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists || info.Length > 4 << 20)
            {
                return KeyTrust.Unknown;
            }

            lines = File.ReadAllLines(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return KeyTrust.Unknown;
        }

        return CheckKnownHostsLines(lines, host, port, keyBlob);
    }

    internal static KeyTrust CheckKnownHostsLines(IEnumerable<string> lines, string host, int port, byte[] keyBlob)
    {
        var name = port == 22 ? host : "[" + host + "]:" + port.ToString(CultureInfo.InvariantCulture);
        var family = SshAlgorithm(keyBlob);
        var sameFamilyOther = false;
        foreach (var raw in lines)
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#'))
            {
                continue;
            }

            var fields = line.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
            var marker = fields[0].StartsWith('@') ? fields[0] : null;
            if (marker == "@cert-authority")
            {
                continue;
            }

            var at = marker is null ? 0 : 1;
            if (fields.Length < at + 3 || !Matches(fields[at], name))
            {
                continue;
            }

            byte[] blob;
            try
            {
                blob = Convert.FromBase64String(fields[at + 2]);
            }
            catch (FormatException)
            {
                continue;
            }

            if (blob.AsSpan().SequenceEqual(keyBlob))
            {
                return marker == "@revoked" ? KeyTrust.Changed : KeyTrust.Trusted;
            }

            sameFamilyOther |= marker is null && SshAlgorithm(blob) == family;
        }

        return sameFamilyOther ? KeyTrust.Changed : KeyTrust.Unknown;
    }

    private static bool Matches(string patterns, string name)
    {
        if (patterns.StartsWith("|1|", StringComparison.Ordinal))
        {
            var parts = patterns.Split('|');
            if (parts.Length != 4)
            {
                return false;
            }

            try
            {
                var salt = Convert.FromBase64String(parts[2]);
                var hash = Convert.FromBase64String(parts[3]);
                return CryptographicOperations.FixedTimeEquals(HMACSHA1.HashData(salt, Encoding.UTF8.GetBytes(name)), hash);
            }
            catch (FormatException)
            {
                return false;
            }
        }

        return patterns.Split(',').Any(p => p.IndexOfAny(['*', '?', '!']) < 0 && string.Equals(p, name, StringComparison.OrdinalIgnoreCase));
    }

    private void Load()
    {
        if (_loaded)
        {
            return;
        }

        _loaded = true;
        if (_file is null || !File.Exists(_file))
        {
            return;
        }

        try
        {
            foreach (var line in File.ReadLines(_file))
            {
                var f = line.Split('\t');
                if (f.Length == 5 && f[1] is "ssh" or "tls"
                    && DateTime.TryParseExact(f[4], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var since))
                {
                    _keys.Add(new TrustedKey(f[0], f[1], f[2], f[3], since));
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    private void Save()
    {
        if (_file is null)
        {
            return;
        }

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_file)!);
            var text = new StringBuilder("# Salandual: server keys and certificates you trusted (host:port, ssh or tls, algorithm, fingerprint, since). No secrets.\n");
            foreach (var k in _keys)
            {
                text.Append(k.Server).Append('\t').Append(k.Kind).Append('\t').Append(k.Algorithm).Append('\t').Append(k.Fingerprint).Append('\t')
                    .Append(k.Since.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)).Append('\n');
            }

            var temp = _file + ".tmp";
            File.WriteAllText(temp, text.ToString());
            File.Move(temp, _file, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }
}

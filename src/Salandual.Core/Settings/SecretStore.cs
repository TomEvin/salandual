// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.Core/Settings/SecretStore.cs

namespace Salandual.Core.Settings;

public interface ISecretStore
{
    string? Read(string target);

    bool Write(string target, string user, string secret);

    bool Delete(string target);

    IReadOnlyList<string> List(string prefix);
}

public sealed class MemorySecretStore : ISecretStore
{
    private readonly Dictionary<string, string> _secrets = new(StringComparer.OrdinalIgnoreCase);

    public string? Read(string target)
    {
        lock (_secrets)
        {
            return _secrets.GetValueOrDefault(target);
        }
    }

    public bool Write(string target, string user, string secret)
    {
        lock (_secrets)
        {
            _secrets[target] = secret;
            return true;
        }
    }

    public bool Delete(string target)
    {
        lock (_secrets)
        {
            return _secrets.Remove(target);
        }
    }

    public IReadOnlyList<string> List(string prefix)
    {
        lock (_secrets)
        {
            return [.. _secrets.Keys.Where(k => k.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)).Order(StringComparer.OrdinalIgnoreCase)];
        }
    }
}

// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.Remote/SecretSource.cs

using Salandual.Core.Settings;

namespace Salandual.Remote;

public sealed class SecretSource(ServerAddress server, ISecretStore store, IServerQuestions questions)
{
    public const string Prefix = "Salandual:";

    private readonly Dictionary<SecretKind, SecretAnswer> _asked = [];
    private readonly Dictionary<SecretKind, string> _current = [];
    private readonly HashSet<SecretKind> _keptTried = [];

    public static string Target(ServerAddress server, SecretKind kind) => Prefix + server.Key + (kind == SecretKind.Passphrase ? "#passphrase" : "");

    public static bool IsKept(ISecretStore store, ServerAddress server, SecretKind kind)
        => store.List(Target(server, kind)).Contains(Target(server, kind), StringComparer.OrdinalIgnoreCase);

    public static bool ForgetKept(ISecretStore store, ServerAddress server)
        => store.Delete(Target(server, SecretKind.Password)) | store.Delete(Target(server, SecretKind.Passphrase));

    public async Task<string?> GetAsync(SecretKind kind, bool wrongBefore, CancellationToken cancellationToken)
    {
        if (!wrongBefore && _current.TryGetValue(kind, out var known))
        {
            return known;
        }

        var target = Target(server, kind);
        if (!wrongBefore && _keptTried.Add(kind) && store.Read(target) is { } kept)
        {
            _current[kind] = kept;
            return kept;
        }

        var answer = await questions.AskSecretAsync(server, kind, wrongBefore, keptBefore: _keptTried.Contains(kind) && store.Read(target) is not null, cancellationToken).ConfigureAwait(false);
        if (answer is null)
        {
            return null;
        }

        _asked[kind] = answer;
        _current[kind] = answer.Secret;
        return answer.Secret;
    }

    public void SignedIn()
    {
        foreach (var (kind, answer) in _asked)
        {
            var target = Target(server, kind);
            if (answer.Remember)
            {
                store.Write(target, server.User, answer.Secret);
            }
            else
            {
                store.Delete(target);
            }
        }

        _asked.Clear();
    }

    public void Forget()
    {
        _asked.Clear();
        _current.Clear();
        _keptTried.Clear();
    }
}

// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.Remote/RemoteSession.cs

using Salandual.Core.Settings;
using Salandual.Core.Vfs;

namespace Salandual.Remote;

public sealed record RemoteItem(FileEntry Entry, string Rights);

public interface IRemoteSession : IAsyncDisposable
{
    ServerAddress Server { get; }

    string Security { get; }

    bool Encrypted { get; }

    bool IsConnected { get; }

    string Home { get; }

    Task<IReadOnlyList<RemoteItem>> ListAsync(string path, CancellationToken cancellationToken);

    Task DownloadAsync(string path, Stream to, long offset, Action<long> progress, CancellationToken cancellationToken);

    Task UploadAsync(string path, Stream from, long offset, Action<long> progress, CancellationToken cancellationToken);

    Task CreateFolderAsync(string path, CancellationToken cancellationToken);

    Task DeleteFileAsync(string path, CancellationToken cancellationToken);

    Task DeleteFolderAsync(string path, CancellationToken cancellationToken);

    Task RenameAsync(string from, string to, CancellationToken cancellationToken);

    Task SetPermissionsAsync(string path, int mode, CancellationToken cancellationToken);

    Task<bool> SetTimeAsync(string path, DateTime utc, CancellationToken cancellationToken);

    Task KeepAliveAsync(CancellationToken cancellationToken);
}

public sealed record ServerTarget(ServerAddress Address, ServerSignIn SignIn = ServerSignIn.Password, string KeyFile = "", bool PlainAllowed = false, string? Bookmark = null, ServerOptions? Options = null)
{
    public ServerOptions Settings => Options ?? ServerOptions.BuiltIn;
}

public interface IServerConnector
{
    Task<IRemoteSession> ConnectAsync(ServerTarget target, ConnectContext context, CancellationToken cancellationToken);
}

public enum SecretKind { Password, Passphrase }

public sealed record SecretAnswer(string Secret, bool Remember);

public enum TrustAnswer { Cancel, Once, Remember }

public sealed record TrustQuestion(ServerAddress Server, string Algorithm, string Fingerprint, string? Subject = null, string? Problem = null, bool Changed = false);

public interface IServerQuestions
{
    Task<SecretAnswer?> AskSecretAsync(ServerAddress server, SecretKind kind, bool wrongBefore, bool keptBefore, CancellationToken cancellationToken);

    Task<TrustAnswer> AskTrustAsync(TrustQuestion question, CancellationToken cancellationToken);

    Task<bool> AskPlainAsync(ServerAddress server, CancellationToken cancellationToken);
}

public sealed class ConnectContext(IServerQuestions questions, SecretSource secrets, TrustedServers trusted, ConnectionLog log)
{
    public IServerQuestions Questions => questions;

    public SecretSource Secrets => secrets;

    public TrustedServers Trusted => trusted;

    public ConnectionLog Log => log;

    public string? ProxyPassword { get; init; }

    public bool SaidYesToPlain { get; set; }
}

public sealed class SignInRefusedException(string message, SecretKind kind) : IOException(message)
{
    public SecretKind Kind => kind;
}

public sealed class ServerKeyChangedException(string message, string algorithm, string fingerprint) : IOException(message)
{
    public string Algorithm => algorithm;

    public string Fingerprint => fingerprint;
}

public sealed class ConnectCancelledException() : OperationCanceledException("Cancelled");

// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.Remote/SshAgent.cs

using System.Buffers.Binary;
using System.IO.MemoryMappedFiles;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Text;
using Renci.SshNet;
using Renci.SshNet.Security;
using Salandual.Core.Resources;

namespace Salandual.Remote;

internal interface IAgentChannel
{
    byte[] Request(byte[] message);
}

internal sealed record AgentKey(byte[] Blob, string Algorithm, string Comment);

internal sealed partial class SshAgent(IAgentChannel channel)
{
    private const byte RequestIdentities = 11, IdentitiesAnswer = 12, SignRequest = 13, SignResponse = 14;

    internal const uint RsaSha256 = 2, RsaSha512 = 4;

    public static SshAgent? Find()
    {
        if (OpenSshPipe.TryOpen() is { } pipe)
        {
            return new SshAgent(pipe);
        }

        return OperatingSystem.IsWindows() && Pageant.IsRunning() ? new SshAgent(new Pageant()) : null;
    }

    public IReadOnlyList<AgentKey> Keys()
    {
        var answer = channel.Request([RequestIdentities]);
        if (answer.Length < 5 || answer[0] != IdentitiesAnswer)
        {
            throw new IOException(Strings.Server_AgentRefused);
        }

        var reader = new Reader(answer, 1);
        var count = reader.UInt32();
        var keys = new List<AgentKey>();
        for (var i = 0; i < count && i < 100; i++)
        {
            var blob = reader.Bytes();
            var comment = Encoding.UTF8.GetString(reader.Bytes());
            var algorithm = Encoding.ASCII.GetString(new Reader(blob, 0).Bytes());
            keys.Add(new AgentKey(blob, algorithm, comment));
        }

        return keys;
    }

    public byte[] Sign(AgentKey key, byte[] data, uint flags)
    {
        var message = new byte[1 + 4 + key.Blob.Length + 4 + data.Length + 4];
        message[0] = SignRequest;
        var at = Writer.Bytes(message, 1, key.Blob);
        at = Writer.Bytes(message, at, data);
        BinaryPrimitives.WriteUInt32BigEndian(message.AsSpan(at), flags);
        var answer = channel.Request(message);
        if (answer.Length < 5 || answer[0] != SignResponse)
        {
            throw new IOException(Strings.Server_AgentRefused);
        }

        return new Reader(answer, 1).Bytes();
    }

    public IPrivateKeySource KeySource() => new Source(this, Keys());

    private sealed class Source(SshAgent agent, IReadOnlyList<AgentKey> keys) : IPrivateKeySource
    {
        public IReadOnlyCollection<HostAlgorithm> HostKeyAlgorithms { get; } = [.. keys.SelectMany(k => Algorithms(agent, k))];

        private static IEnumerable<HostAlgorithm> Algorithms(SshAgent agent, AgentKey key)
        {
            if (key.Algorithm == "ssh-rsa")
            {
                yield return new AgentAlgorithm(agent, key, "rsa-sha2-512", RsaSha512);
                yield return new AgentAlgorithm(agent, key, "rsa-sha2-256", RsaSha256);
            }

            yield return new AgentAlgorithm(agent, key, key.Algorithm, 0);
        }
    }

    internal sealed class AgentAlgorithm(SshAgent agent, AgentKey key, string name, uint flags) : HostAlgorithm(name)
    {
        public override byte[] Data => key.Blob;

        public override byte[] Sign(byte[] data) => agent.Sign(key, data, flags);

        public override bool VerifySignature(byte[] data, byte[] signature) => throw new NotSupportedException();
    }

    private ref struct Reader(byte[] data, int at)
    {
        private int _at = at;

        public uint UInt32()
        {
            if (_at + 4 > data.Length)
            {
                throw new IOException(Strings.Server_AgentRefused);
            }

            var value = BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(_at));
            _at += 4;
            return value;
        }

        public byte[] Bytes()
        {
            var length = UInt32();
            if (length > data.Length - _at)
            {
                throw new IOException(Strings.Server_AgentRefused);
            }

            var bytes = data.AsSpan(_at, (int)length).ToArray();
            _at += (int)length;
            return bytes;
        }
    }

    private static class Writer
    {
        public static int Bytes(byte[] to, int at, byte[] bytes)
        {
            BinaryPrimitives.WriteUInt32BigEndian(to.AsSpan(at), (uint)bytes.Length);
            bytes.CopyTo(to, at + 4);
            return at + 4 + bytes.Length;
        }
    }

    private const int MaxMessage = 256 * 1024;

    private sealed class OpenSshPipe(NamedPipeClientStream pipe) : IAgentChannel
    {
        public static OpenSshPipe? TryOpen()
        {
            if (!OperatingSystem.IsWindows())
            {
                return null;
            }

            var pipe = new NamedPipeClientStream(".", "openssh-ssh-agent", PipeDirection.InOut);
            try
            {
                pipe.Connect(500);
                return new OpenSshPipe(pipe);
            }
            catch (Exception ex) when (ex is TimeoutException or IOException or UnauthorizedAccessException)
            {
                pipe.Dispose();
                return null;
            }
        }

        public byte[] Request(byte[] message)
        {
            lock (pipe)
            {
                var length = new byte[4];
                BinaryPrimitives.WriteUInt32BigEndian(length, (uint)message.Length);
                pipe.Write(length);
                pipe.Write(message);
                pipe.Flush();
                pipe.ReadExactly(length);
                var size = BinaryPrimitives.ReadUInt32BigEndian(length);
                if (size is 0 or > MaxMessage)
                {
                    throw new IOException(Strings.Server_AgentRefused);
                }

                var answer = new byte[size];
                pipe.ReadExactly(answer);
                return answer;
            }
        }
    }

    private sealed partial class Pageant : IAgentChannel
    {
        private const int WmCopyData = 0x004A;
        private const int Size = 8192;
        private static readonly nint CopyDataId = unchecked((nint)0x804e50ba);

        [StructLayout(LayoutKind.Sequential)]
        private struct CopyData
        {
            public nint Data;
            public int Length;
            public nint Pointer;
        }

        public static bool IsRunning() => FindWindowW("Pageant", "Pageant") != 0;

        public byte[] Request(byte[] message)
        {
            if (message.Length + 4 > Size)
            {
                throw new IOException(Strings.Server_AgentRefused);
            }

            var window = FindWindowW("Pageant", "Pageant");
            if (window == 0)
            {
                throw new IOException(Strings.Server_NoAgent);
            }

            var name = "PageantRequest" + Environment.CurrentManagedThreadId.ToString("x8", System.Globalization.CultureInfo.InvariantCulture) + Guid.NewGuid().ToString("N")[..8];
            using var map = MemoryMappedFile.CreateNew(name, Size, MemoryMappedFileAccess.ReadWrite);
            using var view = map.CreateViewAccessor(0, Size);
            var length = new byte[4];
            BinaryPrimitives.WriteUInt32BigEndian(length, (uint)message.Length);
            view.WriteArray(0, length, 0, 4);
            view.WriteArray(4, message, 0, message.Length);
            var nameBytes = Encoding.ASCII.GetBytes(name + "\0");
            var handle = GCHandle.Alloc(nameBytes, GCHandleType.Pinned);
            try
            {
                var data = new CopyData { Data = CopyDataId, Length = nameBytes.Length, Pointer = handle.AddrOfPinnedObject() };
                if (SendMessageW(window, WmCopyData, 0, ref data) == 0)
                {
                    throw new IOException(Strings.Server_AgentRefused);
                }
            }
            finally
            {
                handle.Free();
            }

            view.ReadArray(0, length, 0, 4);
            var size = BinaryPrimitives.ReadUInt32BigEndian(length);
            if (size is 0 or > Size - 4)
            {
                throw new IOException(Strings.Server_AgentRefused);
            }

            var answer = new byte[size];
            view.ReadArray(4, answer, 0, (int)size);
            return answer;
        }

        [LibraryImport("user32.dll", StringMarshalling = StringMarshalling.Utf16)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        private static partial nint FindWindowW(string className, string windowName);

        [LibraryImport("user32.dll")]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        private static partial nint SendMessageW(nint window, int message, nint wParam, ref CopyData lParam);
    }
}

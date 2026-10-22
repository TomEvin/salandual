// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.Platform.Windows/Shell/SingleInstance.cs

using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Text;

namespace Salandual.Platform.Windows.Shell;

public static partial class SingleInstance
{
    private const int MaxMessage = 64 * 1024;

    public static string NameFor(string dataDirectory)
    {
        var hash = 14695981039346656037UL;
        foreach (var c in Path.GetFullPath(dataDirectory).TrimEnd('\\').ToUpperInvariant())
        {
            hash = (hash ^ c) * 1099511628211UL;
        }

        return "Salandual-" + hash.ToString("x16", System.Globalization.CultureInfo.InvariantCulture);
    }

    public static bool TryHandOff(string name, string currentDirectory, IReadOnlyList<string> args, int timeoutMs = 2000)
    {
        if (!Mutex.TryOpenExisting(MutexName(name), out var mutex))
        {
            return false;
        }

        mutex.Dispose();
        try
        {
            using var pipe = new NamedPipeClientStream(".", name, PipeDirection.InOut, PipeOptions.CurrentUserOnly);
            pipe.Connect(timeoutMs);
            _ = AllowSetForegroundWindow(AsfwAny);
            var message = Encode(currentDirectory, args);
            pipe.Write(BitConverter.GetBytes(message.Length));
            pipe.Write(message);
            pipe.Flush();
            return pipe.ReadByte() == 1;
        }
        catch (Exception ex) when (ex is IOException or TimeoutException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    public static IDisposable Listen(string name, Action<string, IReadOnlyList<string>> received) => new Server(name, received);

    private static string MutexName(string name) => name + "-running";

    private static byte[] Encode(string currentDirectory, IReadOnlyList<string> args)
    {
        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true))
        {
            writer.Write(currentDirectory);
            writer.Write(args.Count);
            foreach (var arg in args)
            {
                writer.Write(arg);
            }
        }

        return stream.ToArray();
    }

    internal static (string CurrentDirectory, string[] Args)? Decode(byte[] message)
    {
        try
        {
            using var reader = new BinaryReader(new MemoryStream(message), Encoding.UTF8);
            var currentDirectory = reader.ReadString();
            var count = reader.ReadInt32();
            if (count is < 0 or > 64)
            {
                return null;
            }

            var args = new string[count];
            for (var i = 0; i < count; i++)
            {
                args[i] = reader.ReadString();
            }

            return (currentDirectory, args);
        }
        catch (Exception ex) when (ex is EndOfStreamException or IOException or FormatException)
        {
            return null;
        }
    }

    private sealed class Server : IDisposable
    {
        private readonly string _name;
        private readonly Action<string, IReadOnlyList<string>> _received;
        private readonly Mutex _running;
        private readonly CancellationTokenSource _stop = new();

        public Server(string name, Action<string, IReadOnlyList<string>> received)
        {
            _name = name;
            _received = received;
            _running = new Mutex(false, MutexName(name));
            _ = Task.Run(LoopAsync);
        }

        private async Task LoopAsync()
        {
            var token = _stop.Token;
            while (!token.IsCancellationRequested)
            {
                try
                {
                    await using var pipe = new NamedPipeServerStream(_name, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                    await pipe.WaitForConnectionAsync(token).ConfigureAwait(false);
                    using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
                    timeout.CancelAfter(TimeSpan.FromSeconds(5));
                    var length = new byte[4];
                    await pipe.ReadExactlyAsync(length, timeout.Token).ConfigureAwait(false);
                    var size = BitConverter.ToInt32(length);
                    if (size is <= 0 or > MaxMessage)
                    {
                        continue;
                    }

                    var message = new byte[size];
                    await pipe.ReadExactlyAsync(message, timeout.Token).ConfigureAwait(false);
                    if (Decode(message) is { } start)
                    {
                        _received(start.CurrentDirectory, start.Args);
                        pipe.WriteByte(1);
                        await pipe.FlushAsync(timeout.Token).ConfigureAwait(false);
                    }
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception ex) when (ex is IOException or OperationCanceledException or UnauthorizedAccessException or EndOfStreamException)
                {
                    if (ex is not OperationCanceledException && !token.IsCancellationRequested)
                    {
                        await Task.Delay(200, CancellationToken.None).ConfigureAwait(false);
                    }
                }
            }
        }

        public void Dispose()
        {
            _stop.Cancel();
            _running.Dispose();
        }
    }

    private const int AsfwAny = -1;

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool AllowSetForegroundWindow(int processId);
}

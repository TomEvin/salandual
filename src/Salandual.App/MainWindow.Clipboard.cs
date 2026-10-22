// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.App/MainWindow.Clipboard.cs

using Avalonia.Threading;
using Salandual.App.Panels;
using Salandual.App.Resources;
using Salandual.Core.Operations;
using Salandual.Core.Resources;
using Salandual.Platform.Windows.FileSystem;
using Salandual.Platform.Windows.Shell;

namespace Salandual.App;

internal interface IClipboardAccess
{
    uint SequenceNumber { get; }

    bool SetFiles(IReadOnlyList<string> paths, bool cut);

    bool SetText(string text);

    bool SetImage(byte[] dib, byte[]? png) => false;

    void Clear();

    ClipboardContent? Read();
}

internal sealed class WindowsClipboard(Func<nint> owner) : IClipboardAccess
{
    public uint SequenceNumber => FileClipboard.SequenceNumber;

    public bool SetFiles(IReadOnlyList<string> paths, bool cut) => FileClipboard.SetFiles(owner(), paths, cut);

    public bool SetText(string text) => FileClipboard.SetText(owner(), text);

    public bool SetImage(byte[] dib, byte[]? png) => FileClipboard.SetImage(owner(), dib, png);

    public void Clear() => FileClipboard.Clear(owner());

    public ClipboardContent? Read() => FileClipboard.Read(owner());
}

public partial class MainWindow
{
    private IClipboardAccess? _clipboard;
    private HashSet<string> _cutPaths = new(StringComparer.OrdinalIgnoreCase);
    private uint _cutSequence;

    internal IClipboardAccess FileClipboardAccess
    {
        get => _clipboard ??= new WindowsClipboard(() => _hwnd);
        set => _clipboard = value;
    }

    internal IReadOnlyCollection<string> CutPaths => _cutPaths;

    private bool IsCut(string folder, string name) => _cutPaths.Count > 0 && _cutPaths.Contains(WindowsPath.Join(folder, name));

    internal void CopyToClipboard(PanelHost host, bool cut)
    {
        var sources = OperationSources(host);
        if (sources.Count == 0 || host.Panel.Path.Length == 0)
        {
            ShowNotice(cut ? AppStrings.Notice_NothingToCut : AppStrings.Notice_NothingToCopy);
            return;
        }

        var paths = sources.Select(s => s.Path).ToList();
        if (!FileClipboardAccess.SetFiles(paths, cut))
        {
            ShowNotice(AppStrings.Notice_ClipboardBusy);
            return;
        }

        SetCut(cut ? paths : []);
        var what = paths.Count == 1 ? WindowsPath.GetName(paths[0]) : Loc.Plural(paths.Count, AppStrings.Count_Items_One, AppStrings.Count_Items_Other);
        ShowNotice(Loc.Format(cut ? paths.Count == 1 ? AppStrings.Notice_Cut_One : AppStrings.Notice_Cut_Other : AppStrings.Notice_CopiedToClipboard, what));
    }

    private void SetCut(IEnumerable<string> paths)
    {
        _cutPaths = new HashSet<string>(paths, StringComparer.OrdinalIgnoreCase);
        _cutSequence = _cutPaths.Count > 0 ? FileClipboardAccess.SequenceNumber : 0;
        foreach (var host in _panels)
        {
            host.View.InvalidateRows();
        }
    }

    internal void CheckCutStillOnClipboard()
    {
        if (_cutPaths.Count > 0 && FileClipboardAccess.SequenceNumber != _cutSequence)
        {
            SetCut([]);
        }
    }

    internal async Task PasteAsync(PanelHost host, bool intoFocused)
    {
        var panel = host.Panel;
        if (panel.Path.Length == 0)
        {
            return;
        }

        var folder = panel.Path;
        if (intoFocused)
        {
            if (panel.FocusedEntry is not { IsDirectory: true } dir || panel.IsUp(panel.Focus))
            {
                ShowNotice(AppStrings.Notice_FocusAFolder);
                return;
            }

            folder = WindowsPath.Join(panel.Path, dir.Name);
        }

        if (FileClipboardAccess.Read() is not { } content)
        {
            ShowNotice(AppStrings.Notice_ClipboardBusy);
            return;
        }

        if (content.Files.Count > 0)
        {
            PasteFiles(content, folder);
        }
        else if (content.Text is { } text && !string.IsNullOrWhiteSpace(text))
        {
            await PasteTextAsync(host, folder, text, intoFocused);
        }
        else if (content.Image is { } image)
        {
            await SaveAsNewFileAsync(host, folder, AppStrings.Paste_ImageName, () => content.ImageIsPng ? image : PngFromBmp(image), intoFocused);
        }
        else
        {
            ShowNotice(AppStrings.Notice_NothingToPaste);
        }
    }

    private void PasteFiles(Platform.Windows.Shell.ClipboardContent content, string folder)
    {
        var fs = Operations.Files;
        var sources = content.Files.Select(f => new OperationSource(f, fs.Stat(f) is { IsDirectory: true })).ToList();
        var options = _services.Settings.Current.TransferOptions();
        OperationJob job;
        if (content.Cut)
        {
            job = FileOperations.Move(fs, sources, new TransferTarget(folder), options);
            var sequence = FileClipboardAccess.SequenceNumber;
            job.Completion.ContinueWith(_ => Dispatcher.UIThread.Post(() =>
            {
                if (FileClipboardAccess.SequenceNumber == sequence)
                {
                    FileClipboardAccess.Clear();
                }

                SetCut([]);
            }), TaskScheduler.Default);
        }
        else
        {
            var sameFolder = sources.All(s => string.Equals(WindowsPath.GetParent(s.Path), folder, StringComparison.OrdinalIgnoreCase));
            job = FileOperations.Copy(fs, sources, new TransferTarget(folder) { RenameOntoItself = sameFolder }, options);
        }

        Operations.Add(job, toQueue: false);
    }

    private async Task PasteTextAsync(PanelHost host, string folder, string text, bool intoFocused)
    {
        var line = text.Trim().Trim('"');
        var looksLikePath = !line.Contains('\n') && line.Length < 32000 && (Path.IsPathFullyQualified(line) || line.StartsWith(@"\\", StringComparison.Ordinal));
        if (looksLikePath && PathInput.Resolve(line, folder) is { } full)
        {
            var fs = Operations.Files;
            var info = await Task.Run(() => fs.Stat(full));
            if (info is { IsDirectory: true })
            {
                await host.Panel.NavigateAsync(full);
                return;
            }

            if (info is not null && WindowsPath.GetParent(full) is { } parent)
            {
                await host.Panel.NavigateAsync(parent, WindowsPath.GetName(full));
                return;
            }

            ShowNotice(Loc.Format(AppStrings.Notice_PathNotThere, line), (AppStrings.Paste_SaveAsFile, () => _ = SaveAsNewFileAsync(host, folder, AppStrings.Paste_TextName, () => System.Text.Encoding.UTF8.GetBytes(text), intoFocused)));
            return;
        }

        await SaveAsNewFileAsync(host, folder, AppStrings.Paste_TextName, () => System.Text.Encoding.UTF8.GetBytes(text), intoFocused);
    }

    private async Task SaveAsNewFileAsync(PanelHost host, string folder, string name, Func<byte[]> content, bool intoFocused)
    {
        var fs = Operations.Files;
        string? error = null;
        var path = await Task.Run(() =>
        {
            try
            {
                var bytes = content();
                var free = FileNames.FreeName(fs, folder, name);
                var target = WindowsPath.Join(folder, free);
                using (var stream = new FileStream(WindowsPath.ToExtended(target), FileMode.CreateNew, FileAccess.Write))
                {
                    stream.Write(bytes);
                }

                return target;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
            {
                error = ex.Message;
                return null;
            }
        });

        if (path is null)
        {
            ShowNotice(Loc.Format(AppStrings.Notice_CantSaveClipboard, error));
            return;
        }

        var saved = WindowsPath.GetName(path);
        if (intoFocused)
        {
            ShowNotice(Loc.Format(AppStrings.Notice_SavedClipboardIn, saved, WindowsPath.GetName(folder)));
            return;
        }

        await host.Panel.RefreshAsync(saved, []);
        if (host.Panel.FocusedEntry?.Name == saved)
        {
            host.BeginRename(fs);
        }

        ShowNotice(Loc.Format(AppStrings.Notice_SavedClipboard, saved), (AppStrings.Common_Undo, () =>
        {
            host.CancelRename();
            _ = Task.Run(() =>
            {
                try
                {
                    File.Delete(WindowsPath.ToExtended(path));
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                }
            });
        }));
    }

    private static byte[] PngFromBmp(byte[] bmp)
    {
        using var input = new MemoryStream(bmp);
        using var bitmap = new Avalonia.Media.Imaging.Bitmap(input);
        using var output = new MemoryStream();
        bitmap.Save(output, Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
        return output.ToArray();
    }

    internal async Task CopyNamesAsync(PanelHost host, string id)
    {
        var panel = host.Panel;
        if (panel.Path.Length == 0)
        {
            return;
        }

        string text;
        if (id == "Clipboard.CopyPath")
        {
            text = panel.Path;
        }
        else
        {
            var sources = OperationSources(host);
            if (sources.Count == 0)
            {
                ShowNotice(AppStrings.Notice_NothingToCopy);
                return;
            }

            var paths = sources.Select(s => s.Path).ToList();
            text = id switch
            {
                "Clipboard.CopyName" => string.Join(Environment.NewLine, paths.Select(WindowsPath.GetName)),
                "Clipboard.CopyUncName" => string.Join(Environment.NewLine, await Task.Run(() => paths.Select(UncPath.For).ToList())),
                _ => string.Join(Environment.NewLine, paths),
            };
        }

        if (!FileClipboardAccess.SetText(text))
        {
            ShowNotice(AppStrings.Notice_ClipboardBusy);
            return;
        }

        CheckCutStillOnClipboard();
        var lines = text.Split(Environment.NewLine);
        ShowNotice(lines.Length == 1 ? Loc.Format(AppStrings.Notice_CopiedText, text) : Loc.Format(AppStrings.Notice_CopiedNames, lines.Length));
    }
}

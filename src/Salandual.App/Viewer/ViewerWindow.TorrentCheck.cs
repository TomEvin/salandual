// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.App/Viewer/ViewerWindow.TorrentCheck.cs

using System.Diagnostics;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Salandual.App.Dialogs;
using Salandual.App.Options;
using Salandual.App.Resources;
using Salandual.App.Theme;
using Salandual.Core.Listing;
using Salandual.Core.Resources;
using Salandual.Core.Viewer;
using static Salandual.App.Viewer.ToolbarParts;

namespace Salandual.App.Viewer;

internal enum TorrentCheckState { Running, Done, Stopped, Failed }

internal sealed class TorrentCheckRun(string torrent, TorrentCheck check)
{
    private readonly Stopwatch _clock = Stopwatch.StartNew();

    public string Torrent { get; } = torrent;

    public TorrentCheck Check { get; } = check;

    public CancellationTokenSource Cancel { get; } = new();

    public Task Work { get; set; } = Task.CompletedTask;

    public TorrentCheckState State { get; set; }

    public string? Error { get; set; }

    public bool IsRunning => State == TorrentCheckState.Running;

    public double Speed => _clock.Elapsed.TotalSeconds > 0.2 ? Check.BytesDone / _clock.Elapsed.TotalSeconds : 0;

    public void Stopped() => _clock.Stop();
}

internal sealed partial class ViewerWindow
{
    private readonly Border _checkBar = new() { BorderThickness = new Thickness(1), CornerRadius = Corners.Chip, Padding = new Thickness(12, 8), Margin = new Thickness(12, 0, 12, 8) };
    private readonly TextBox _checkFolder = new() { MinHeight = 28, VerticalContentAlignment = VerticalAlignment.Center };
    private readonly Button _checkBrowse = new() { Content = AppStrings.Common_Browse, MinHeight = 28, Padding = new Thickness(10, 2), FontSize = 12, VerticalAlignment = VerticalAlignment.Center };
    private readonly Button _checkStart = new() { Content = AppStrings.TorrentCheck_Check, MinHeight = 28, MinWidth = 72, Padding = new Thickness(12, 2), FontSize = 12, VerticalAlignment = VerticalAlignment.Center, HorizontalContentAlignment = HorizontalAlignment.Center };
    private readonly Button _checkStop = new() { Content = AppStrings.TorrentCheck_Stop, MinHeight = 28, MinWidth = 72, Padding = new Thickness(12, 2), FontSize = 12, VerticalAlignment = VerticalAlignment.Center, HorizontalContentAlignment = HorizontalAlignment.Center, IsVisible = false };
    private readonly TextBlock _checkError = new() { FontSize = 12, TextWrapping = TextWrapping.Wrap, IsVisible = false, Margin = new Thickness(0, 4, 0, 0) };
    private readonly StackPanel _checkStatus = new() { IsVisible = false, Margin = new Thickness(0, 8, 0, 0) };
    private readonly TextBlock _checkTitle = new() { FontSize = 13, FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap };
    private readonly ProgressBar _checkProgress = new() { Minimum = 0, Maximum = 1, Height = 6, MinHeight = 6, Margin = new Thickness(0, 6) };
    private readonly TextBlock _checkLine = new() { FontSize = 12, TextWrapping = TextWrapping.Wrap };
    private readonly ToggleButton _onlyIncomplete = new() { Content = AppStrings.TorrentCheck_OnlyIncomplete, MinHeight = 28, Padding = new Thickness(10, 2), FontSize = 12, VerticalAlignment = VerticalAlignment.Center, IsVisible = false };
    private readonly DispatcherTimer _checkTimer = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private readonly Grid _filesHead = new() { Margin = new Thickness(12, 0, 12, 2) };
    private readonly Dictionary<string, TorrentCheckCell> _checkCells = new(StringComparer.Ordinal);
    private Button _checkButton = null!;
    private TorrentCheckRun? _check;
    private Dictionary<string, TorrentFileCheck> _checked = new(StringComparer.Ordinal);
    private Dictionary<string, (int NotComplete, int Open)> _checkedFolders = new(StringComparer.Ordinal);
    private string? _checkFolderFor;
    private bool _stopToClose;

    internal TorrentCheckRun? TorrentCheckShown => _check is { } c && string.Equals(c.Torrent, _path, StringComparison.Ordinal) ? c : null;

    internal bool TorrentCheckRunning => _check is { IsRunning: true };

    internal Button TorrentCheckButton => _checkButton;

    internal TextBox TorrentCheckFolder => _checkFolder;

    internal Button TorrentCheckStart => _checkStart;

    internal Button TorrentCheckStop => _checkStop;

    internal TextBlock TorrentCheckError => _checkError;

    internal ToggleButton TorrentOnlyIncomplete => _onlyIncomplete;

    internal TextBlock TorrentCheckTitle => _checkTitle;

    internal TextBlock TorrentCheckLine => _checkLine;

    internal ConfirmDialog? TorrentCheckStopQuestion { get; private set; }

    private bool OnlyIncomplete => _onlyIncomplete.IsVisible && _onlyIncomplete.IsChecked == true;

    internal Action? CheckReport { get; set; }

    internal IReadOnlyDictionary<string, TorrentCheckCell> TorrentCheckCells => _checkCells;

    private Button BuildCheckButton()
    {
        _checkButton = Tool(Label(null, AppStrings.Torrent_Check, null), AppStrings.Torrent_CheckTip);
        _checkButton.Click += (_, _) => ShowCheckFolder();
        return _checkButton;
    }

    internal void ShowCheckFolder()
    {
        ShowTorrentPage(TorrentPage.Files);
        Dispatcher.UIThread.Post(() =>
        {
            if (!TorrentCheckRunning)
            {
                _checkFolder.Focus();
                _checkFolder.SelectAll();
            }
        }, DispatcherPriority.Input);
    }

    private void BuildCheckBar()
    {
        var where = new TextBlock { Text = AppStrings.TorrentCheck_Where, FontSize = 12, VerticalAlignment = VerticalAlignment.Center }.Res(TextBlock.ForegroundProperty, "Brush.SecondaryText");
        _checkFolder.Bind(TextBox.FontFamilyProperty, _checkFolder.GetResourceObservable("Font.Mono"));
        Avalonia.Automation.AutomationProperties.SetName(_checkFolder, AppStrings.TorrentCheck_Where);
        ToolTip.SetTip(_checkStart, AppStrings.Torrent_CheckTip);
        _checkStart.Classes.Add("accent");
        _checkStart.Click += (_, _) => StartCheck();
        _checkStop.Click += (_, _) => StopCheck();
        _checkBrowse.Click += async (_, _) => await BrowseCheckFolderAsync();
        _checkFolder.KeyDown += (_, e) =>
        {
            if (e.Key == Avalonia.Input.Key.Enter && e.KeyModifiers == Avalonia.Input.KeyModifiers.None)
            {
                StartCheck();
                e.Handled = true;
            }
        };

        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto,Auto,Auto"), ColumnSpacing = 8, Children = { where, _checkFolder, _checkBrowse, _checkStart, _checkStop } };
        Grid.SetColumn(_checkFolder, 1);
        Grid.SetColumn(_checkBrowse, 2);
        Grid.SetColumn(_checkStart, 3);
        Grid.SetColumn(_checkStop, 4);
        _checkError.Res(TextBlock.ForegroundProperty, "Brush.Danger");
        _checkTitle.Res(TextBlock.ForegroundProperty, "Brush.Text");
        _checkLine.Res(TextBlock.ForegroundProperty, "Brush.SecondaryText");
        _checkStatus.Children.Add(_checkTitle);
        _checkStatus.Children.Add(_checkProgress);
        _checkStatus.Children.Add(_checkLine);
        _checkBar.Child = new StackPanel { Children = { row, _checkError, _checkStatus } };
        _checkBar.Res(Border.BorderBrushProperty, "Brush.Border").Res(Border.BackgroundProperty, "Brush.WindowBackground");

        _checkTimer.Tick += (_, _) => UpdateCheck();
        Closing += OnClosingWhileChecking;
        Closed += (_, _) =>
        {
            _checkTimer.Stop();
            _check?.Cancel.Cancel();
        };
    }

    private void TorrentLoadedForCheck(string path)
    {
        _checkBar.IsVisible = _request.TorrentFolder is not null;
        if (_request.TorrentFolder is { } offer && !string.Equals(_checkFolderFor, path, StringComparison.Ordinal))
        {
            _checkFolderFor = path;
            if (TorrentCheckShown is null)
            {
                _checkFolder.Text = offer(path);
            }

            _checkError.IsVisible = false;
        }

        UpdateCheck();
        SetFileColumns();
    }

    private async Task BrowseCheckFolderAsync()
    {
        var start = await StorageProvider.TryGetFolderFromPathAsync(_checkFolder.Text ?? "");
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = AppStrings.TorrentCheck_Where, SuggestedStartLocation = start });
        if (folders.Count > 0 && folders[0].TryGetLocalPath() is { } path)
        {
            _checkFolder.Text = path;
        }
    }

    internal void StartCheck()
    {
        if (Torrent is not { } info || TorrentCheckRunning)
        {
            return;
        }

        var picked = (_checkFolder.Text ?? "").Trim().Trim('"');
        string? why;
        try
        {
            why = picked.Length == 0 ? AppStrings.TorrentCheck_TypeFolder
                : !Path.IsPathFullyQualified(picked) || !Directory.Exists(picked) ? AppStrings.TorrentCheck_NoFolder
                : null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException)
        {
            why = AppStrings.TorrentCheck_NoFolder;
        }

        _checkError.Text = why ?? "";
        _checkError.IsVisible = why is not null;
        if (why is not null)
        {
            _checkFolder.Focus();
            return;
        }

        var run = new TorrentCheckRun(_path, new TorrentCheck(info, TorrentCheck.FolderFor(info, Path.GetFullPath(picked))));
        var token = run.Cancel.Token;
        var report = CheckReport ?? (() => { });
        run.Work = Task.Run(() => run.Check.Run(report, token), token).ContinueWith(t => Dispatcher.UIThread.Post(() =>
        {
            run.Stopped();
            run.State = t.IsCanceled || (t.Exception?.InnerException is OperationCanceledException) ? TorrentCheckState.Stopped
                : t.IsFaulted ? TorrentCheckState.Failed
                : TorrentCheckState.Done;
            run.Error = t.Exception?.InnerException is TorrentCheckException e ? e.Message : t.Exception?.InnerException?.Message;
            UpdateCheck();
        }), TaskScheduler.Default);
        _check = run;
        _onlyIncomplete.IsChecked = false;
        _checkCells.Clear();
        ReadCheckResults(run.Check);
        SetFileColumns();
        RebuildFileRows();
        UpdateCheck();
        _checkTimer.Start();
        _filesList.Focus();
    }

    internal void StopCheck() => _check?.Cancel.Cancel();

    private void UpdateCheck()
    {
        var running = TorrentCheckRunning;
        _checkStart.IsVisible = !running;
        _checkStop.IsVisible = running;
        _checkFolder.IsReadOnly = running;
        _checkBrowse.IsEnabled = !running;
        if (TorrentCheckShown is not { } run || Torrent is not { } info)
        {
            _checkStatus.IsVisible = false;
            _onlyIncomplete.IsVisible = false;
            if (!running)
            {
                _checkTimer.Stop();
            }

            return;
        }

        var check = run.Check;
        var format = DisplayFormat.From(_settings.Current.Panels, CultureInfo.CurrentCulture);
        _checkStatus.IsVisible = true;
        _checkProgress.IsVisible = running;
        _checkProgress.Value = check.PieceCount > 0 ? check.PiecesDone / (double)check.PieceCount : 0;
        var pieces = (check.PiecesDone.ToString("N0", CultureInfo.CurrentCulture), check.PieceCount.ToString("N0", CultureInfo.CurrentCulture));
        var folder = TorrentNames.Shown(check.Folder);
        switch (run.State)
        {
            case TorrentCheckState.Done:
                _checkTitle.Text = TorrentCheckSummary(info, check.Results);
                _checkLine.Text = Loc.Format(AppStrings.TorrentCheck_Checked, folder);
                break;
            case TorrentCheckState.Stopped:
                _checkTitle.Text = Loc.Format(AppStrings.TorrentCheck_Stopped, pieces.Item1, pieces.Item2);
                _checkLine.Text = Loc.Format(AppStrings.TorrentCheck_Checked, folder);
                break;
            case TorrentCheckState.Failed:
                _checkTitle.Text = run.Error ?? "";
                _checkLine.Text = Loc.Format(AppStrings.TorrentCheck_Checked, folder);
                break;
            default:
                _checkTitle.Text = Loc.Format(AppStrings.TorrentCheck_Checking, TorrentNames.Shown(info.Name), folder);
                _checkLine.Text = Loc.Format(AppStrings.TorrentCheck_Progress, pieces.Item1, pieces.Item2, Loc.Format(AppStrings.Common_XOfY, format.Size(check.BytesDone), format.Size(info.TotalSize)))
                    + " · " + Salandual.App.Operations.Ui.Speed(format, run.Speed);
                break;
        }

        _onlyIncomplete.IsVisible = run.State == TorrentCheckState.Done;
        ReadCheckResults(check);
        foreach (var (key, cell) in _checkCells)
        {
            cell.Show(CellState(key, cell.IsFolder));
        }

        if (!running)
        {
            _checkTimer.Stop();
            RebuildFileRows();
        }
    }

    private void ReadCheckResults(TorrentCheck check)
    {
        var files = new Dictionary<string, TorrentFileCheck>(StringComparer.Ordinal);
        var folders = new Dictionary<string, (int NotComplete, int Open)>(StringComparer.Ordinal);
        foreach (var result in check.Results)
        {
            files[result.File.Path] = result;
            var open = result.State is TorrentFileState.Waiting or TorrentFileState.Checking;
            var notComplete = !open && result.State != TorrentFileState.Complete;
            var key = "";
            Count(key);
            for (var i = 0; i < result.File.Parts.Count - 1; i++)
            {
                key = key.Length == 0 ? result.File.Parts[i] : key + "\\" + result.File.Parts[i];
                Count(key);
            }

            void Count(string folder)
            {
                var (n, o) = folders.GetValueOrDefault(folder);
                folders[folder] = (n + (notComplete ? 1 : 0), o + (open ? 1 : 0));
            }
        }

        _checked = files;
        _checkedFolders = folders;
    }

    internal static string TorrentCheckSummary(TorrentInfo info, IReadOnlyList<TorrentFileCheck> results)
    {
        var (complete, partly, missing, skipped, share) = TorrentCheck.Count(results);
        var parts = new List<string> { Loc.Plural(complete, AppStrings.TorrentCheck_Complete_One, AppStrings.TorrentCheck_Complete_Other) };
        if (partly > 0)
        {
            parts.Add(Loc.Format(AppStrings.TorrentCheck_Partly, partly));
        }

        if (missing > 0)
        {
            parts.Add(Loc.Format(AppStrings.TorrentCheck_Missing, missing));
        }

        if (skipped > 0)
        {
            parts.Add(Loc.Format(AppStrings.TorrentCheck_Skipped, skipped));
        }

        return Loc.Format(AppStrings.TorrentCheck_Summary, info.Name.Length > 0 ? TorrentNames.Shown(info.Name) : AppStrings.Torrent_NoName,
            string.Join(", ", parts), (Math.Floor(share * 1000) / 10).ToString("0.#", CultureInfo.CurrentCulture));
    }

    private static string CheckPercent(double share)
    {
        var percent = Math.Floor(share * 1000) / 10;
        return percent.ToString(percent >= 10 ? "0" : "0.#", CultureInfo.CurrentCulture);
    }

    private TorrentCheckCell? CheckCell(TorrentRow row)
    {
        if (TorrentCheckShown is null)
        {
            return null;
        }

        var cell = new TorrentCheckCell(row.IsFolder);
        cell.Show(CellState(row.Key, row.IsFolder));
        _checkCells[row.Key] = cell;
        return cell;
    }

    private CellView CellState(string key, bool isFolder)
    {
        if (isFolder)
        {
            var (notComplete, open) = _checkedFolders.GetValueOrDefault(key);
            return notComplete > 0 ? new CellView(Loc.Format(AppStrings.TorrentCheck_StNotComplete, notComplete), "Brush.Warning")
                : open > 0 ? new CellView(TorrentCheckRunning ? AppStrings.TorrentCheck_StChecking : "", "Brush.SecondaryText")
                : new CellView(AppStrings.TorrentCheck_StComplete, "Brush.Success");
        }

        if (!_checked.TryGetValue(key, out var result))
        {
            return new CellView("", "Brush.SecondaryText");
        }

        var differ = result.BadPieces > 0 ? Loc.Plural(result.BadPieces, AppStrings.TorrentCheck_PiecesDiffer_One, AppStrings.TorrentCheck_PiecesDiffer_Other) : null;
        return result.State switch
        {
            TorrentFileState.Waiting => new CellView(TorrentCheckRunning ? AppStrings.TorrentCheck_StWaiting : AppStrings.TorrentCheck_StNotChecked, "Brush.TertiaryText"),
            TorrentFileState.Checking when TorrentCheckRunning => new CellView(Loc.Format(AppStrings.TorrentCheck_StPercent, CheckPercent(result.Done)), "Brush.SecondaryText", Bar: result.Done),
            TorrentFileState.Checking => new CellView(AppStrings.TorrentCheck_StNotChecked, "Brush.TertiaryText"),
            TorrentFileState.Complete => new CellView(AppStrings.TorrentCheck_StComplete, "Brush.Success"),
            TorrentFileState.Missing => new CellView(AppStrings.TorrentCheck_StMissing, "Brush.Danger"),
            TorrentFileState.Skipped => result.Note switch
            {
                TorrentCheckNote.Link => new CellView(AppStrings.TorrentCheck_StLink, "Brush.Warning", AppStrings.TorrentCheck_Why_Link),
                TorrentCheckNote.OnlineOnly => new CellView(AppStrings.TorrentCheck_StOnline, "Brush.Warning", AppStrings.TorrentCheck_Why_Online),
                _ => new CellView(AppStrings.TorrentCheck_StStrange, "Brush.Warning", AppStrings.TorrentCheck_Why_Strange),
            },
            _ => result.Note switch
            {
                TorrentCheckNote.Unreadable => new CellView(AppStrings.TorrentCheck_StUnreadable, "Brush.Danger", AppStrings.TorrentCheck_Why_Unreadable),
                TorrentCheckNote.NoHashes => new CellView(AppStrings.TorrentCheck_StNoHashes, "Brush.Warning", AppStrings.TorrentCheck_Why_NoHashes),
                TorrentCheckNote.SizeDiffers => new CellView(Loc.Format(AppStrings.TorrentCheck_StRight, CheckPercent(result.Share)) + " · " + AppStrings.TorrentCheck_StSize, "Brush.Warning", differ),
                _ => new CellView(Loc.Format(AppStrings.TorrentCheck_StRight, CheckPercent(result.Share)), "Brush.Warning", differ),
            },
        };
    }

    private void SetFileColumns()
    {
        if (_filesHead.ColumnDefinitions.Count != 4)
        {
            _filesHead.ColumnDefinitions = Columns();
        }

        _filesHead.ColumnDefinitions[3].Width = new GridLength(TorrentCheckShown is null ? 0 : 180);
        _filesHead.ColumnSpacing = 8;
        if (_filesHead.Children.Count == 3)
        {
            var col = new TextBlock { Text = AppStrings.TorrentCheck_Col, FontSize = 12 }.Res(TextBlock.ForegroundProperty, "Brush.TertiaryText");
            Grid.SetColumn(col, 3);
            _filesHead.Children.Add(col);
        }

        _filesHead.Children[^1].IsVisible = TorrentCheckShown is not null;
    }

    private ColumnDefinitions Columns() => new(TorrentCheckShown is null ? "*,100,70,0" : "*,100,70,180");

    private void OnClosingWhileChecking(object? sender, WindowClosingEventArgs e)
    {
        if (!TorrentCheckRunning || _stopToClose || e.CloseReason is WindowCloseReason.ApplicationShutdown or WindowCloseReason.OSShutdown)
        {
            _check?.Cancel.Cancel();
            return;
        }

        e.Cancel = true;
        Dispatcher.UIThread.Post(async () =>
        {
            if (await StopCheckFirstAsync(AppStrings.TorrentCheck_StopToClose, AppStrings.TorrentCheck_StopAndClose))
            {
                _stopToClose = true;
                Close();
            }
        });
    }

    private async Task<bool> StopCheckFirstAsync(string question, string stop)
    {
        if (_check is not { IsRunning: true } run)
        {
            return true;
        }

        if (TorrentCheckStopQuestion is not null)
        {
            return false;
        }

        var ask = TorrentCheckStopQuestion = new ConfirmDialog(AppStrings.Torrent_Check.TrimEnd('…'), question, stop, AppStrings.TorrentCheck_KeepChecking);
        bool yes;
        try
        {
            yes = await ask.ShowDialog<bool>(this);
        }
        finally
        {
            TorrentCheckStopQuestion = null;
        }

        if (!yes || !run.IsRunning)
        {
            return yes;
        }

        run.Cancel.Cancel();
        try
        {
            await run.Work;
        }
        catch (OperationCanceledException)
        {
        }

        return true;
    }

    private Task<bool> StopCheckToStepAsync() => StopCheckFirstAsync(AppStrings.TorrentCheck_StopToStep, AppStrings.TorrentCheck_StopCheck);
}

internal readonly record struct CellView(string Text, string Brush, string? Why = null, double? Bar = null);

internal sealed class TorrentCheckCell : Grid
{
    private readonly ProgressBar _bar = new() { Minimum = 0, Maximum = 1, Width = 90, MinWidth = 0, Height = 6, MinHeight = 6, VerticalAlignment = VerticalAlignment.Center, IsVisible = false };
    private readonly TextBlock _text = new() { FontSize = 12, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
    private string? _brush;

    public TorrentCheckCell(bool isFolder)
    {
        IsFolder = isFolder;
        ColumnDefinitions = new ColumnDefinitions("Auto,*");
        ColumnSpacing = 8;
        Children.Add(_bar);
        Children.Add(_text);
        SetColumn(_text, 1);
    }

    public bool IsFolder { get; }

    public string Text => _text.Text ?? "";

    public double? Bar => _bar.IsVisible ? _bar.Value : null;

    public void Show(CellView view)
    {
        _text.Text = view.Text;
        if (_brush != view.Brush)
        {
            _brush = view.Brush;
            _text.Res(TextBlock.ForegroundProperty, view.Brush);
        }

        _bar.IsVisible = view.Bar is not null;
        _bar.Value = view.Bar ?? 0;
        SetColumn(_text, view.Bar is null ? 0 : 1);
        SetColumnSpan(_text, view.Bar is null ? 2 : 1);
        ToolTip.SetTip(this, view.Why);
    }
}

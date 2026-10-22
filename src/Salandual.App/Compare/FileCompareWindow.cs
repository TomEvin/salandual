// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.App/Compare/FileCompareWindow.cs

using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Salandual.App.Dialogs;
using Salandual.App.Editor;
using Salandual.App.Find;
using Salandual.App.Options;
using Salandual.App.Resources;
using Salandual.App.Theme;
using Salandual.Core.Compare;
using Salandual.Core.Listing;
using Salandual.Core.Resources;
using Salandual.Core.Settings;
using Salandual.Core.Viewer;

namespace Salandual.App.Compare;

internal sealed partial class FileCompareWindow : Window
{
    private sealed class FileSide
    {
        public CompareFile? File { get; set; }

        public List<string> Lines { get; set; } = [];

        public List<bool> Copied { get; set; } = [];

        public int Pending { get; set; }

        public Stack<(List<string> Lines, List<bool> Copied, int Pending)> Undo { get; } = new();


        public void Load(CompareFile file)
        {
            File = file;
            Lines = [.. file.Lines];
            Copied = [.. file.Lines.Select(_ => false)];
            Pending = 0;
            Undo.Clear();
        }
    }

    private readonly SettingsService _settings;
    private readonly DisplayFormat _format;
    private readonly DiffView _view = new();
    private readonly DiffStrip _strip;
    private readonly ScrollBar _horizontal = new() { Orientation = Orientation.Horizontal, AllowAutoHide = false, IsVisible = false };
    private readonly Button _prev = new();
    private readonly Button _next = new();
    private readonly TextBlock _position = new() { FontSize = 12, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0), MinWidth = 120 };
    private readonly Button _only = new();
    private readonly Button _spaces = new();
    private readonly Button _case = new();
    private readonly Button _textMode = new();
    private readonly Button _binaryMode = new();
    private readonly Button _swap = new();
    private readonly Button _refresh = new();
    private readonly TextBlock _leftPath = HeaderPath();
    private readonly TextBlock _rightPath = HeaderPath();
    private readonly TextBlock _leftInfo = HeaderInfo();
    private readonly TextBlock _rightInfo = HeaderInfo();
    private readonly TextBlock _summary = StatusText();
    private readonly TextBlock _kind = StatusText();
    private readonly TextBlock _encoding = StatusText();
    private readonly TextBlock _endings = StatusText();
    private readonly TextBlock _line = StatusText();
    private readonly TextBlock _titleText = new() { FontSize = 12, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.PathSegmentEllipsis };
    private readonly TextBlock _hint = StatusText();
    private readonly Button _findButton = new();
    private readonly Button _more = new();
    private readonly Border _findBar = new() { IsVisible = false, BorderThickness = new Thickness(0, 0, 0, 1) };
    private readonly TextBox _findBox = new() { Width = 220, FontSize = 12.5, VerticalAlignment = VerticalAlignment.Center };
    private readonly ToggleButton _matchCase = FindOption("Aa", AppStrings.Viewer_MatchCase);
    private readonly ToggleButton _wholeWords = FindOption("ab", AppStrings.Viewer_WholeWords);
    private readonly ToggleButton _regex = FindOption(".*", AppStrings.Viewer_Regex);
    private readonly Button _findText = FindSegment(AppStrings.Viewer_FindText);
    private readonly Button _findHex = FindSegment(AppStrings.Viewer_FindHexBytes);
    private readonly Button[] _findIn = [FindSegment(AppStrings.FileCompare_FindBoth), FindSegment(AppStrings.FileCompare_FindLeft), FindSegment(AppStrings.FileCompare_FindRight)];
    private readonly Button _findOnly = new();
    private readonly TextBlock _findCount = new() { FontSize = 12, VerticalAlignment = VerticalAlignment.Center, MinWidth = 70, Margin = new Thickness(4, 0) };
    private CancellationTokenSource? _finding;
    private readonly FileSide[] _sides = [new(), new()];
    private readonly List<int> _undoOrder = [];
    private readonly (TextBlock Text, Button Undo, Button Save, StackPanel Panel)[] _pending = [PendingParts(), PendingParts()];
    private readonly Grid _header = new() { Height = 30 };
    private TextDiffOptions _options;
    private bool? _binaryWanted;
    private CancellationTokenSource? _comparing;
    private string? _note;
    private bool _closeConfirmed;

    public FileCompareWindow(SettingsService settings, string left, string right)
    {
        _settings = settings;
        _format = DisplayFormat.From(settings.Current.Panels, CultureInfo.CurrentCulture);
        var files = settings.Current.Compare.Files;
        _options = new TextDiffOptions(files.IgnoreWhiteSpace, files.IgnoreCase);
        _view.OnlyDifferences = files.OnlyDifferences;
        _strip = new DiffStrip(_view);
        Width = 1280;
        Height = 760;
        MinWidth = 640;
        MinHeight = 320;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        this.Res(BackgroundProperty, "Brush.WindowBackground");
        Styles.Add(FindStyles.Create());
        SetFiles(left, right);

        var dock = new DockPanel();
        var tools = BuildTools();
        var findBar = BuildFindBar();
        DockPanel.SetDock(findBar, Dock.Top);
        var header = BuildHeader();
        var status = BuildStatus();
        DockPanel.SetDock(tools, Dock.Top);
        DockPanel.SetDock(header, Dock.Top);
        DockPanel.SetDock(status, Dock.Bottom);
        DockPanel.SetDock(_horizontal, Dock.Bottom);
        dock.Children.Add(tools);
        dock.Children.Add(findBar);
        dock.Children.Add(header);
        dock.Children.Add(status);
        dock.Children.Add(_horizontal);
        DockPanel.SetDock(_strip, Dock.Right);
        dock.Children.Add(new DockPanel { Children = { _strip, _view } });
        Avalonia.Automation.AutomationProperties.SetName(_view, AppStrings.FileCompare_ViewName);

        _view.ViewChanged += UpdateView;
        _view.CopyRequested += (block, toRight) => CopyAcross(block, toRight);
        _horizontal.Scroll += (_, _) => _view.HorizontalOffset = _horizontal.Value;
        var title = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), ColumnSpacing = 8, Children = { AppIcons.Glyph("Icon.Panels", 16, 16, 1.3), _titleText } };
        Grid.SetColumn(_titleText, 1);
        _titleText.Res(TextBlock.ForegroundProperty, "Brush.Text");
        Content = Controls.TitleBarParts.ForWindow(this, title, dock);
        AddHandler(KeyDownEvent, OnKey, RoutingStrategies.Tunnel);
        Opened += (_, _) =>
        {
            _view.Focus();
            Comparing = CompareAsync();
        };
        Closing += OnClosing;
        Closed += (_, _) =>
        {
            _comparing?.Cancel();
            _view.Release();
        };
    }

    internal bool IsModified => _sides.Any(s => s.Pending > 0);

    internal ChoiceDialog? CloseQuestion { get; private set; }

    internal string HintText => _hint.Text ?? "";

    internal string PendingText(int side) => _pending[side].Panel.IsVisible ? _pending[side].Text.Text ?? "" : "";

    internal IReadOnlyList<string> LinesOf(int side) => _sides[side].Lines;

    internal string LeftPath { get; private set; } = "";

    internal string RightPath { get; private set; } = "";

    internal DiffView View => _view;

    internal DiffStrip Strip => _strip;

    internal Task Comparing { get; private set; } = Task.CompletedTask;

    internal string PositionText => _position.Text ?? "";

    internal string SummaryText => _summary.Text ?? "";

    internal string KindText => _kind.Text ?? "";

    internal string EncodingText => _encoding.Text ?? "";

    internal string LineText => _line.Text ?? "";

    internal Button OnlyButton => _only;

    internal Button SpacesButton => _spaces;

    internal Button CaseButton => _case;

    internal Button TextButton => _textMode;

    internal Button BinaryButton => _binaryMode;

    internal Button PreviousButton => _prev;

    internal Button NextButton => _next;

    internal TextDiffOptions Options => _options;

    private void SetFiles(string left, string right)
    {
        LeftPath = left;
        RightPath = right;
        Title = _titleText.Text = Loc.Format(AppStrings.FileCompare_Title, Path.GetFileName(left), Path.GetFileName(right));
        _leftPath.Text = left;
        _rightPath.Text = right;
        ToolTip.SetTip(_leftPath, left);
        ToolTip.SetTip(_rightPath, right);
    }

    internal async Task CompareAsync()
    {
        _comparing?.Cancel();
        var cancel = (_comparing = new CancellationTokenSource()).Token;
        _summary.Text = AppStrings.FileCompare_Comparing;
        _note = null;
        var (left, right) = (LeftPath, RightPath);
        try
        {
            var (l, r) = await Task.Run(() => (CompareFile.Read(left, cancel), CompareFile.Read(right, cancel)), cancel);
            if (cancel.IsCancellationRequested)
            {
                return;
            }

            _sides[0].Load(l);
            _sides[1].Load(r);
            _undoOrder.Clear();
            var tooBig = l.TooBig || r.TooBig;
            var binary = tooBig || (_binaryWanted ?? (l.LooksBinary || r.LooksBinary));
            if (tooBig && _binaryWanted == false)
            {
                _note = AppStrings.FileCompare_TooBig;
            }

            if (binary)
            {
                var diff = await Task.Run(
                    () =>
                    {
                        using var a = new FileStream(left, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 1 << 16, FileOptions.SequentialScan);
                        using var b = new FileStream(right, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 1 << 16, FileOptions.SequentialScan);
                        return BinaryDiff.Compare(a, b, cancel: cancel);
                    },
                    cancel);
                if (cancel.IsCancellationRequested)
                {
                    return;
                }

                _view.ShowBinary(new DiffBytes(left), new DiffBytes(right), diff);
            }
            else if (!await ShowTextAsync(cancel))
            {
                return;
            }
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            _view.Release();
            _summary.Text = Loc.Format(AppStrings.FileCompare_CantRead, e.Message);
            UpdateButtons();
            return;
        }

        UpdateAll();
    }

    private void UpdateAll()
    {
        UpdateHeaders();
        UpdateSummary();
        UpdateButtons();
        UpdateView();

        if (_findBar.IsVisible && (_findBox.Text ?? "").Length > 0)
        {
            Finding = SearchAsync(go: false);
        }
    }

    private async Task<bool> ShowTextAsync(CancellationToken cancel)
    {
        var (a, b) = (_sides[0], _sides[1]);
        List<string> la = [.. a.Lines], lb = [.. b.Lines];
        List<bool> ca = [.. a.Copied], cb = [.. b.Copied];
        var (options, tab) = (_options, _settings.Current.Viewers.TabWidth);
        var colors = _settings.Current.Compare.Files.CodeColors;
        var (langA, langB) = colors ? (a.File!.Language, b.File!.Language) : (SyntaxLanguage.None, SyntaxLanguage.None);
        var (diff, lt, rt) = await Task.Run(() => (TextDiff.Compare(la, lb, options, cancel: cancel), new DiffText(la, langA, tab), new DiffText(lb, langB, tab)), cancel);
        if (cancel.IsCancellationRequested)
        {
            return false;
        }

        _view.ShowText(lt, rt, diff, options, _settings.Current.Compare.Files.Context, ca, cb);
        return true;
    }

    private async Task RecompareAsync(int? block = null)
    {
        _comparing?.Cancel();
        var cancel = (_comparing = new CancellationTokenSource()).Token;
        try
        {
            if (!await ShowTextAsync(cancel))
            {
                return;
            }
        }
        catch (OperationCanceledException)
        {
            return;
        }

        if (block is { } i && _view.CursorBlock < 0 && _view.Blocks.Count > 0)
        {
            _view.MoveCursor(_view.ItemOfRow(_view.Blocks[Math.Min(i, _view.Blocks.Count - 1)].Row));
        }

        UpdateAll();
    }

    internal void CopyAcross(int block, bool toRight)
    {
        if (_view.Text is not { } diff || !Comparing.IsCompleted || block < 0 || block >= diff.Blocks.Count)
        {
            return;
        }

        var (row, count) = _view.TargetRows(block);
        var (source, target) = toRight ? (_sides[0], _sides[1]) : (_sides[1], _sides[0]);
        target.Undo.Push(([.. target.Lines], [.. target.Copied], target.Pending));
        target.Pending += DiffCopy.Apply(diff.Rows, Enumerable.Range(row, count), toRight, source.Lines, target.Lines, target.Copied);
        _undoOrder.Add(toRight ? 1 : 0);
        Comparing = RecompareAsync(block);
    }

    internal void Undo(int? side = null)
    {
        var index = side is { } s ? _undoOrder.LastIndexOf(s) : _undoOrder.Count - 1;
        if (index < 0 || !Comparing.IsCompleted)
        {
            return;
        }

        var file = _sides[_undoOrder[index]];
        _undoOrder.RemoveAt(index);
        (file.Lines, file.Copied, file.Pending) = file.Undo.Pop();
        Comparing = RecompareAsync(_view.CursorBlock >= 0 ? _view.CursorBlock : null);
    }

    internal async Task SaveAllAsync()
    {
        for (var side = 0; side < 2; side++)
        {
            if (_sides[side].Pending > 0 && !await SaveAsync(side))
            {
                return;
            }
        }
    }

    internal async Task<bool> SaveAsync(int side)
    {
        var file = _sides[side];
        if (file.File is not { } read || file.Pending == 0)
        {
            return true;
        }

        var path = side == 0 ? LeftPath : RightPath;
        var name = Path.GetFileName(path);
        if (EditorFiles.Check(path, read.Length, read.LastWriteUtc) != DiskState.Same)
        {
            var over = await Ask(Loc.Format(AppStrings.FileCompare_ChangedOnDisk_Title, name), [AppStrings.FileCompare_ChangedOnDisk_Text], [new("save", AppStrings.FileCompare_SaveOver, "Enter", Primary: true), new("cancel", AppStrings.Common_Cancel, "Esc")]);
            if (over != "save")
            {
                return false;
            }
        }

        var other = _sides[1 - side].File;
        var text = DiffCopy.Join(file.Lines, read.LineEnding ?? other?.LineEnding, read.EndsWithLineBreak || (read.Lines.Count == 0 && (other?.EndsWithLineBreak ?? false)));
        var (encoding, bom) = (read.Encoding, read.BomLength > 0);
        if (EditorFiles.Unrepresentable(text, encoding) is > 0 and var lost)
        {
            var answer = await Ask(
                Loc.Format(AppStrings.Editor_Lost_Title, encoding.Name),
                [Loc.Format(AppStrings.Editor_Lost_Text, lost, encoding.Name)],
                [new("utf8", AppStrings.Editor_Lost_Utf8, Primary: true), new("anyway", AppStrings.Editor_Lost_Anyway), new("cancel", AppStrings.Common_Cancel, "Esc")]);
            if (answer is not ("utf8" or "anyway"))
            {
                return false;
            }

            if (answer == "utf8")
            {
                (encoding, bom) = (Salandual.Core.Viewer.TextEncodings.Default, false);
            }
        }

        try
        {
            var backup = _settings.Current.Editors.Backup && File.Exists(path);
            await Task.Run(() => EditorFiles.Save(path, EditorFiles.Encode(text, encoding, bom), backup));
            var saved = await Task.Run(() => CompareFile.Read(path));
            var (lines, copied) = (file.Lines, file.Copied);
            file.Load(saved);
            if (saved.Lines.SequenceEqual(lines))
            {
                file.Copied = copied;
            }

            _undoOrder.RemoveAll(s => s == side);
            Comparing = RecompareAsync(_view.CursorBlock >= 0 ? _view.CursorBlock : null);
            await Comparing;
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _summary.Text = Loc.Format(AppStrings.Editor_CantSave, name, ex.Message);
            return false;
        }
    }

    private async Task<string?> Ask(string title, IReadOnlyList<string> text, IReadOnlyList<Choice> choices, Action<ChoiceDialog>? shown = null)
    {
        var dialog = new ChoiceDialog(title, text, choices, cancel: "cancel");
        shown?.Invoke(dialog);
        return await dialog.ShowDialog<string?>(this);
    }

    private void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        if (_closeConfirmed || !IsModified)
        {
            return;
        }

        e.Cancel = true;
        _ = AskToCloseAsync();
    }

    private async Task AskToCloseAsync()
    {
        if (CloseQuestion is not null)
        {
            return;
        }

        var names = string.Join(" · ", Enumerable.Range(0, 2).Where(s => _sides[s].Pending > 0).Select(s => Path.GetFileName(s == 0 ? LeftPath : RightPath)));
        var answer = await Ask(
            Loc.Format(AppStrings.FileCompare_Close_Title, names),
            [AppStrings.FileCompare_Close_Text],
            [new("save", AppStrings.Common_Save, "Enter", Primary: true), new("discard", AppStrings.Editor_DontSave, "N", Key.N), new("cancel", AppStrings.Common_Cancel, "Esc")],
            question => CloseQuestion = question);
        CloseQuestion = null;
        if (answer == "discard" || (answer == "save" && await SaveAllSucceededAsync()))
        {
            _closeConfirmed = true;
            Close();
        }

        async Task<bool> SaveAllSucceededAsync()
        {
            await SaveAllAsync();
            return !IsModified;
        }
    }

    internal async Task<bool> CloseAskingAsync()
    {
        if (IsModified)
        {
            Activate();
            await AskToCloseAsync();
            return _closeConfirmed;
        }

        _closeConfirmed = true;
        Close();
        return true;
    }

    internal void CloseWithSalandual()
    {
        _closeConfirmed = true;
        Close();
    }

    internal async Task RefreshAsync()
    {
        if (IsModified)
        {
            var answer = await Ask(AppStrings.FileCompare_Reload_Title, [AppStrings.FileCompare_Reload_Text], [new("reload", AppStrings.FileCompare_Reload_Go, "Enter", Primary: true), new("cancel", AppStrings.Common_Cancel, "Esc")]);
            if (answer != "reload")
            {
                return;
            }
        }

        Comparing = CompareAsync();
    }

    private void UpdateHeaders()
    {
        if (_sides[0].File is null || _sides[1].File is null)
        {
            return;
        }

        _leftInfo.Text = Info(_sides[0].File!);
        _rightInfo.Text = Info(_sides[1].File!);

        for (var side = 0; side < 2; side++)
        {
            var pending = _sides[side].Pending;
            _pending[side].Panel.IsVisible = pending > 0;
            (side == 0 ? _leftInfo : _rightInfo).IsVisible = pending == 0;
            _pending[side].Text.Text = Loc.Plural(pending, AppStrings.FileCompare_Pending_One, AppStrings.FileCompare_Pending_Other);
        }

        string Info(CompareFile file)
        {
            var local = file.LastWriteUtc.ToLocalTime();
            return _format.Size(file.Length) + "  ·  " + _format.Date(local) + " " + _format.Time(local);
        }
    }

    private void UpdateSummary()
    {
        if (_sides[0].File is not { } l || _sides[1].File is not { } r)
        {
            return;
        }

        if (_view.Text is { } text)
        {
            if (text.Blocks.Count == 0)
            {
                _summary.Text = IsModified ? AppStrings.FileCompare_SameNotSaved
                    : _options.IgnoreWhiteSpace || _options.IgnoreCase ? AppStrings.FileCompare_SameIgnoring
                    : l.Length == r.Length && l.Encoding.Id == r.Encoding.Id && l.LineEnding == r.LineEnding && l.BomLength == r.BomLength ? AppStrings.FileCompare_Same
                    : AppStrings.FileCompare_SameText;
            }
            else
            {
                var parts = new List<string>();
                if (text.Changed > 0)
                {
                    parts.Add(Loc.Format(AppStrings.FileCompare_Changed, text.Changed));
                }

                if (text.SpacingOrCase > 0)
                {
                    parts.Add(Loc.Format(AppStrings.FileCompare_SpacingOrCase, text.SpacingOrCase));
                }

                if (text.Added > 0)
                {
                    parts.Add(Loc.Format(AppStrings.FileCompare_Added, text.Added));
                }

                if (text.Removed > 0)
                {
                    parts.Add(Loc.Format(AppStrings.FileCompare_Removed, text.Removed));
                }

                var lines = text.Changed + text.SpacingOrCase + text.Added + text.Removed;
                _summary.Text = Loc.Format(AppStrings.FileCompare_Summary, Differences(text.Blocks.Count), Loc.Plural(lines, AppStrings.FileCompare_Lines_One, AppStrings.FileCompare_Lines_Other), string.Join(", ", parts))
                    + (text.Finished ? "" : "  ·  " + AppStrings.FileCompare_NotFinished);
            }

            _kind.Text = l.Language == r.Language ? LanguageName(l.Language) : LanguageName(l.Language) + " · " + LanguageName(r.Language);
            _encoding.Text = l.Encoding.Id == r.Encoding.Id ? l.Encoding.Name : l.Encoding.Name + " · " + r.Encoding.Name;
            _endings.Text = l.LineEnding == r.LineEnding ? l.LineEnding ?? "" : (l.LineEnding ?? "–") + " · " + (r.LineEnding ?? "–");
            _endings.IsVisible = _endings.Text.Length > 0;
            _encoding.IsVisible = true;
        }
        else if (_view.Binary is { } binary)
        {
            if (binary.Same)
            {
                _summary.Text = AppStrings.FileCompare_Same;
            }
            else
            {
                var line = Loc.Format(AppStrings.FileCompare_BytesSummary, Differences(binary.Blocks.Count), Loc.Plural(binary.DifferentBytes, AppStrings.FileCompare_Bytes_One, AppStrings.FileCompare_Bytes_Other));
                var longer = binary.RightLength - binary.LeftLength;
                if (longer != 0)
                {
                    var bytes = Loc.Plural(Math.Abs(longer), AppStrings.FileCompare_Bytes_One, AppStrings.FileCompare_Bytes_Other);
                    line += "; " + Loc.Format(longer > 0 ? AppStrings.FileCompare_RightLonger : AppStrings.FileCompare_LeftLonger, bytes);
                }

                _summary.Text = line;
            }

            _kind.Text = AppStrings.FileCompare_BinaryKind;
            _encoding.IsVisible = false;
            _endings.IsVisible = false;
        }

        if (_note is not null)
        {
            _summary.Text += "  ·  " + _note;
        }

        static string Differences(int count) => Loc.Plural(count, AppStrings.FileCompare_Count_One, AppStrings.FileCompare_Count_Other);
    }

    internal static string LanguageName(Salandual.Core.Viewer.SyntaxLanguage language) => language switch
    {
        Salandual.Core.Viewer.SyntaxLanguage.Css => "CSS",
        Salandual.Core.Viewer.SyntaxLanguage.JavaScript => "JavaScript",
        _ => EditorWindow.LanguageName(language),
    };

    private void UpdateButtons()
    {
        var binary = _view.IsBinary;
        _textMode.Classes.Set("on", !binary);
        _binaryMode.Classes.Set("on", binary);
        _spaces.IsVisible = _case.IsVisible = !binary;
        _spaces.Classes.Set("on", _options.IgnoreWhiteSpace);
        _case.Classes.Set("on", _options.IgnoreCase);
        _only.Classes.Set("on", _view.OnlyDifferences);
        _textMode.IsEnabled = !(_sides[0].File?.TooBig ?? false) && !(_sides[1].File?.TooBig ?? false);
        _binaryMode.IsEnabled = !IsModified;
        UpdateFindOptions();
        ToolTip.SetTip(_binaryMode, IsModified ? AppStrings.FileCompare_BinaryNotSaved : AppStrings.FileCompare_BinaryTip);
        var gap = _view.Gap.ToString(CultureInfo.InvariantCulture);
        if (_header.ColumnDefinitions.Count != 4 || _header.ColumnDefinitions[1].Width.Value != _view.Gap)
        {
            _header.ColumnDefinitions = new ColumnDefinitions($"*,{gap},*,14");
        }
    }

    private void UpdateView()
    {
        var blocks = _view.Blocks;
        var current = _view.CursorBlock;
        _position.Text = blocks.Count == 0 ? AppStrings.FileCompare_NoDifferences
            : current >= 0 ? Loc.Format(AppStrings.FileCompare_Position, current + 1, blocks.Count)
            : Loc.Plural(blocks.Count, AppStrings.FileCompare_Count_One, AppStrings.FileCompare_Count_Other);
        var row = _view.CursorRow;
        _prev.IsEnabled = blocks.Count > 0 && (current > 0 || (current < 0 && blocks[0].Row < row));
        _next.IsEnabled = blocks.Count > 0 && (current >= 0 ? current < blocks.Count - 1 : blocks[^1].Row > row);
        _line.Text = _view.CursorLine() is not { } line ? ""
            : _view.IsBinary ? Loc.Format(AppStrings.FileCompare_Offset, line.ToString("X8", CultureInfo.InvariantCulture))
            : Loc.Format(AppStrings.FileCompare_Line, line);
        _hint.IsVisible = _view.Text is not null && blocks.Count > 0;
        _hint.Text = _view.Selection is { } s
            ? Loc.Format(AppStrings.FileCompare_Selected, s.To - s.From + 1, blocks[s.Block].Count)
            : AppStrings.FileCompare_ClickHint;
        var extent = _view.HorizontalExtent;
        _horizontal.IsVisible = extent > 0;
        _horizontal.Maximum = extent;
        _horizontal.ViewportSize = _view.TextWidth;
        _horizontal.Value = _view.HorizontalOffset;
    }

    internal void SetOnlyDifferences(bool on)
    {
        _view.OnlyDifferences = on;
        UpdateButtons();
    }

    internal void SetOptions(TextDiffOptions options)
    {
        _options = options;
        UpdateButtons();
        Comparing = _view.Text is not null ? RecompareAsync() : CompareAsync();
    }

    internal void SetBinary(bool binary)
    {
        if (binary && IsModified)
        {
            return;
        }

        _binaryWanted = binary;
        Comparing = CompareAsync();
    }

    internal void Swap()
    {
        SetFiles(RightPath, LeftPath);
        if (_view.Text is not null && _sides[0].File is not null)
        {
            (_sides[0], _sides[1]) = (_sides[1], _sides[0]);
            for (var i = 0; i < _undoOrder.Count; i++)
            {
                _undoOrder[i] = 1 - _undoOrder[i];
            }

            Comparing = RecompareAsync();
        }
        else
        {
            Comparing = CompareAsync();
        }
    }

    private void OnKey(object? sender, KeyEventArgs e)
    {
        var plain = e.KeyModifiers == KeyModifiers.None;
        var ctrl = e.KeyModifiers == KeyModifiers.Control;
        var alt = e.KeyModifiers == KeyModifiers.Alt;
        var shift = e.KeyModifiers == KeyModifiers.Shift;
        var ctrlAlt = e.KeyModifiers == (KeyModifiers.Control | KeyModifiers.Alt);
        var ctrlAltShift = e.KeyModifiers == (KeyModifiers.Control | KeyModifiers.Alt | KeyModifiers.Shift);
        var page = Math.Max(1, _view.VisibleRows - 1);

        if (_findBar.IsKeyboardFocusWithin)
        {
            switch (e.Key)
            {
                case Key.Enter when plain:
                case Key.F3 when plain:
                    NextHit(1);
                    break;
                case Key.Enter when shift:
                case Key.F3 when shift:
                    NextHit(-1);
                    break;
                case Key.Escape when plain:
                    CloseFind();
                    break;
                case Key.F when ctrl:
                case Key.F7 when plain:
                    _findBox.SelectAll();
                    break;
                default:
                    return;
            }

            e.Handled = true;
            return;
        }

        switch (e.Key)
        {
            case Key.F when ctrl:
            case Key.F7 when plain:
                OpenFind();
                break;
            case Key.F3 when plain:
                if (_view.Hits.Count > 0)
                {
                    NextHit(1);
                }
                else
                {
                    OpenFind();
                }

                break;
            case Key.F3 when shift:
                NextHit(-1);
                break;
            case Key.Escape when plain && _findBar.IsVisible:
                CloseFind();
                break;
            case Key.C when ctrl:
                CopyLine();
                break;
            case Key.Right when ctrlAltShift:
                CopyAll(toRight: true);
                break;
            case Key.Left when ctrlAltShift:
                CopyAll(toRight: false);
                break;
            case Key.Right when ctrlAlt:
                CopyAcross(_view.CursorBlock, toRight: true);
                break;
            case Key.Left when ctrlAlt:
                CopyAcross(_view.CursorBlock, toRight: false);
                break;
            case Key.Z when ctrl:
                Undo();
                break;
            case Key.S when ctrl:
                _ = SaveAllAsync();
                break;
            case Key.Escape when plain && _view.ClearSelection():
                break;
            case Key.Down when alt:
                _view.GoToDifference(1);
                break;
            case Key.Up when alt:
                _view.GoToDifference(-1);
                break;
            case Key.Up when plain:
                _view.MoveCursor(_view.CursorItem - 1);
                break;
            case Key.Down when plain:
                _view.MoveCursor(_view.CursorItem + 1);
                break;
            case Key.PageUp when plain:
                _view.ScrollRows(-page);
                _view.MoveCursor(_view.CursorItem - page);
                break;
            case Key.PageDown when plain:
                _view.ScrollRows(page);
                _view.MoveCursor(_view.CursorItem + page);
                break;
            case Key.Home when plain || ctrl:
                _view.MoveCursor(0);
                break;
            case Key.End when plain || ctrl:
                _view.MoveCursor(_view.ItemCount - 1);
                break;
            case Key.Left when plain:
                _view.HorizontalOffset -= 40;
                break;
            case Key.Right when plain:
                _view.HorizontalOffset += 40;
                break;
            case Key.Enter when plain && _view.ItemAt(_view.CursorItem).IsFold:
                _view.Unfold(_view.CursorItem);
                break;
            case Key.D when ctrl:
                SetOnlyDifferences(!_view.OnlyDifferences);
                break;
            case Key.R when ctrl:
                _ = RefreshAsync();
                break;
            case Key.Escape when plain:
                Close();
                break;
            default:
                return;
        }

        e.Handled = true;
    }

    private Control BuildTools()
    {
        Tool(_prev, "Icon.ArrowUp", "Alt+↑", AppStrings.FileCompare_PrevTip, () => _view.GoToDifference(-1));
        Tool(_next, "Icon.ArrowDown", "Alt+↓", AppStrings.FileCompare_NextTip, () => _view.GoToDifference(1));
        Chip(_only, AppStrings.FileCompare_Only, AppStrings.FileCompare_OnlyTip, () => SetOnlyDifferences(!_view.OnlyDifferences));
        Chip(_spaces, AppStrings.FileCompare_IgnoreSpaces, AppStrings.FileCompare_IgnoreSpacesTip, () => SetOptions(_options with { IgnoreWhiteSpace = !_options.IgnoreWhiteSpace }));
        Chip(_case, AppStrings.FileCompare_IgnoreCase, AppStrings.FileCompare_IgnoreCaseTip, () => SetOptions(_options with { IgnoreCase = !_options.IgnoreCase }));
        Chip(_textMode, AppStrings.FileCompare_Text, AppStrings.FileCompare_TextTip, () => SetBinary(false));
        Chip(_binaryMode, AppStrings.FileCompare_Binary, AppStrings.FileCompare_BinaryTip, () => SetBinary(true));
        _textMode.CornerRadius = new CornerRadius(4, 0, 0, 4);
        _binaryMode.CornerRadius = new CornerRadius(0, 4, 4, 0);
        Tool(_swap, "Icon.SwapSides", null, AppStrings.FileCompare_SwapTip, Swap);
        Tool(_refresh, "Icon.RotateRight", "Ctrl+R", AppStrings.FileCompare_RefreshTip, () => _ = RefreshAsync());
        var mode = new StackPanel { Orientation = Orientation.Horizontal, Spacing = -1, Children = { _textMode, _binaryMode } };
        var left = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 4,
            VerticalAlignment = VerticalAlignment.Center,
            Children = { _prev, _next, _position, Separator(), mode, Separator(), _only, Separator(), _spaces, _case },
        };
        Tool(_findButton, "Icon.Find", null, AppStrings.FileCompare_FindTip, ToggleFind);
        Tool(_more, "Icon.More", null, AppStrings.FileCompare_MoreTip, () => { });
        _more.Flyout = BuildMoreMenu();
        var right = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, VerticalAlignment = VerticalAlignment.Center, Children = { _swap, _refresh, _findButton, _more } };
        var dock = new DockPanel { Margin = new Thickness(8, 6) };
        DockPanel.SetDock(right, Dock.Right);
        dock.Children.Add(right);
        dock.Children.Add(left);
        return new Border { BorderThickness = new Thickness(0, 0, 0, 1), Child = dock }.Res(Border.BorderBrushProperty, "Brush.Border");

        static Border Separator() => new Border { Width = 1, Height = 22, Margin = new Thickness(4, 0) }.Res(Border.BackgroundProperty, "Brush.BorderStrong");

        static void Tool(Button button, string icon, string? key, string tip, Action click)
        {
            var content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Children = { AppIcons.Glyph(icon, 16, 16, 1.3) } };
            if (key is not null)
            {
                content.Children.Add(new TextBlock { Text = key, FontSize = 11, Opacity = 0.6, VerticalAlignment = VerticalAlignment.Center });
            }

            button.Content = content;
            button.Classes.Add("ftool");
            button.Focusable = false;
            ToolTip.SetTip(button, tip);
            Avalonia.Automation.AutomationProperties.SetName(button, tip);
            button.Click += (_, _) => click();
        }

        static void Chip(Button button, string text, string tip, Action click)
        {
            button.Content = text;
            button.Classes.Add("chip");
            button.Focusable = false;
            ToolTip.SetTip(button, tip);
            button.Click += (_, _) => click();
        }
    }

    private static TextBlock HeaderPath() => new() { FontSize = 12, FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.PathSegmentEllipsis };

    private static TextBlock HeaderInfo() => new() { FontSize = 12, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0) };

    private static TextBlock StatusText() => new() { FontSize = 12, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };

    private Control BuildHeader()
    {
        var grid = _header;
        grid.ColumnDefinitions = new ColumnDefinitions("*,14,*,14");
        grid.Children.Add(Side(_leftPath, _leftInfo, _pending[0].Panel).At(0));
        grid.Children.Add(new Border { BorderThickness = new Thickness(1, 0) }.Res(Border.BorderBrushProperty, "Brush.Border").At(1));
        grid.Children.Add(Side(_rightPath, _rightInfo, _pending[1].Panel).At(2));
        for (var side = 0; side < 2; side++)
        {
            var s = side;
            ToolTip.SetTip(_pending[s].Undo, AppStrings.FileCompare_UndoTip);
            ToolTip.SetTip(_pending[s].Save, AppStrings.FileCompare_SaveTip);
            _pending[s].Undo.Click += (_, _) => Undo(s);
            _pending[s].Save.Click += (_, _) => _ = SaveAsync(s);
        }

        return new Border { BorderThickness = new Thickness(0, 0, 0, 1), Child = grid }.Res(Border.BackgroundProperty, "Brush.ColumnHeader").Res(Border.BorderBrushProperty, "Brush.Border");

        static Control Side(TextBlock path, TextBlock info, StackPanel pending)
        {
            path.Res(TextBlock.ForegroundProperty, "Brush.Text");
            info.Res(TextBlock.ForegroundProperty, "Brush.SecondaryText");
            var dock = new DockPanel { Margin = new Thickness(10, 0) };
            DockPanel.SetDock(info, Dock.Right);
            DockPanel.SetDock(pending, Dock.Right);
            dock.Children.Add(info);
            dock.Children.Add(pending);
            dock.Children.Add(path);
            return dock;
        }
    }

    private static (TextBlock Text, Button Undo, Button Save, StackPanel Panel) PendingParts()
    {
        var text = new TextBlock { FontSize = 12, FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center };
        text.Res(TextBlock.ForegroundProperty, "Brush.Accent");
        var undo = Small(AppStrings.FileCompare_Undo, "Ctrl+Z");
        var save = Small(AppStrings.Common_Save, "Ctrl+S");
        save.Classes.Add("accent");
        var panel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(12, 0, 0, 0), IsVisible = false, Children = { text, undo, save } };
        return (text, undo, save, panel);

        static Button Small(string label, string key) => new()
        {
            Content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Children = { new TextBlock { Text = label }, new TextBlock { Text = key, FontSize = 11, Opacity = 0.65 } } },
            FontSize = 12,
            Padding = new Thickness(8, 1),
            MinHeight = 22,
            Focusable = false,
            VerticalAlignment = VerticalAlignment.Center,
        };
    }

    private Control BuildStatus()
    {
        var row = new DockPanel { Margin = new Thickness(12, 4) };
        foreach (var text in new[] { _summary, _hint, _kind, _encoding, _endings, _line })
        {
            text.Res(TextBlock.ForegroundProperty, "Brush.SecondaryText");
        }

        var right = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 18, Children = { _hint, _kind, _encoding, _endings, _line } };
        DockPanel.SetDock(right, Dock.Right);
        right.Margin = new Thickness(18, 0, 0, 0);
        row.Children.Add(right);
        row.Children.Add(_summary);
        return new Border { BorderThickness = new Thickness(0, 1, 0, 0), Child = row }.Res(Border.BorderBrushProperty, "Brush.Border");
    }
}

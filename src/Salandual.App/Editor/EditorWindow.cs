// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.App/Editor/EditorWindow.cs

using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using AvaloniaEdit;
using AvaloniaEdit.Document;
using Salandual.App.Controls;
using Salandual.App.Dialogs;
using Salandual.App.Options;
using Salandual.App.Resources;
using Salandual.App.Viewer;
using Salandual.Core.Resources;
using Salandual.Core.Settings;
using Salandual.Core.Viewer;
using Salandual.Platform.Windows.FileSystem;

namespace Salandual.App.Editor;

internal sealed record EditorRequest(string Path)
{
    public bool Anyway { get; init; }

    public Action<string>? View { get; init; }

    public Action<string>? ShowInPanel { get; init; }

    public Action<string>? OpenOptions { get; init; }

    public bool New { get; init; }

    public Action<Uri>? OpenWeb { get; init; }

    public Func<IPageView>? PageView { get; init; }

    internal IClipboardAccess? Clipboard { get; init; }

    public Func<string, IReadOnlyList<EditWithItem>>? EditWith { get; init; }
}

internal sealed partial class EditorWindow : Window
{
    private readonly SettingsService _settings;
    private readonly EditorRequest _request;
    private readonly TextEditor _editor;
    private readonly Run _titleDot = new();
    private readonly Run _titleName = new();
    private readonly Run _titleFolder = new();
    private readonly DispatcherTimer _diskTimer = new() { Interval = TimeSpan.FromSeconds(2) };
    private readonly ContentControl _loading = new();
    private string _path;
    private EditorFile _file;
    private LineEnding? _lineEnding;
    private IndentStyle _indent;
    private SyntaxColorizer? _colorizer;
    private SyntaxLanguage _language;
    private bool _closeConfirmed;
    private bool _diskAsked;
    private (bool Check, bool Folding) _checkSettings = (true, true);

    public EditorWindow(SettingsService settings, EditorRequest request)
    {
        _settings = settings;
        _request = request;
        _path = request.Path;
        _file = EditorFiles.New(Options);
        var remembered = Options;
        Width = remembered.WindowWidth;
        Height = remembered.WindowHeight;
        MinWidth = 520;
        MinHeight = 300;
        WindowStartupLocation = WindowStartupLocation.Manual;
        this.Res(BackgroundProperty, "Brush.WindowBackground");
        Styles.Add(ViewerStyles.Create());
        Styles.Add(new EditorStyles());

        _editor = new TextEditor
        {
            Document = new TextDocument(),
            Padding = new Thickness(6, 2, 0, 0),
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
        };
        _editor.Options.EnableHyperlinks = false;
        _editor.Options.EnableEmailHyperlinks = false;
        _editor.Options.AllowScrollBelowDocument = true;
        _editor.Options.EnableTextDragDrop = true;
        BuildTextMenu();
        _editor.Res(TextEditor.BackgroundProperty, "Brush.CodeSurface");
        _editor.Res(TextEditor.ForegroundProperty, "Brush.CodeInk");
        _editor.Res(TextEditor.LineNumbersForegroundProperty, "Brush.CodeLineNumbers");
        _editor.TextArea.Res(AvaloniaEdit.Editing.TextArea.SelectionBrushProperty, "Brush.CodeSelection");
        _editor.TextArea.SelectionForeground = null;
        _editor.TextArea.SelectionBorder = null;
        _editor.TextArea.SelectionCornerRadius = 0;
        _editor.TextArea.TextView.Res(AvaloniaEdit.Rendering.TextView.CurrentLineBackgroundProperty, "Brush.CodeCurrentLine");
        _editor.TextArea.TextView.CurrentLineBorder = new Pen(Brushes.Transparent, 0);
        _editor.TextArea.Caret.CaretBrush = null;
        _editor.FocusAdorner = null;
        _editor.TextArea.FocusAdorner = null;
        _editor.Bind(TextEditor.FontFamilyProperty, this.GetResourceObservable("Font.Mono"));
        _editor.Bind(TextEditor.FontSizeProperty, this.GetResourceObservable("Font.MonoSize"));

        var titleText = new TextBlock { FontSize = 12, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
        titleText.Inlines!.Add(_titleDot);
        titleText.Inlines.Add(_titleName);
        titleText.Inlines.Add(_titleFolder);
        titleText.Res(TextBlock.ForegroundProperty, "Brush.Text");
        _titleFolder.Bind(Run.ForegroundProperty, titleText.GetResourceObservable("Brush.SecondaryText"));
        _titleDot.Bind(Run.ForegroundProperty, titleText.GetResourceObservable("Brush.Accent"));
        var titleIcon = Salandual.App.Theme.AppIcons.Glyph("Icon.Rename", 16, 16, 1.2);
        Grid.SetColumn(titleText, 1);
        var title = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), ColumnSpacing = 8, Children = { titleIcon, titleText } };

        var toolbar = BuildToolbar();
        BuildFindBar();
        var notices = BuildNotices();
        var status = BuildStatus();
        var problems = BuildProblemsLine();
        var area = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        area.Children.Add(_editor);
        area.Children.Add(_loading);
        var strip = BuildStrip();
        Grid.SetColumn(strip, 1);
        area.Children.Add(strip);
        var body = BuildBody(area);

        var dock = new DockPanel();
        DockPanel.SetDock(toolbar, Dock.Top);
        DockPanel.SetDock(notices, Dock.Top);
        DockPanel.SetDock(_findBar, Dock.Top);
        DockPanel.SetDock(status, Dock.Bottom);
        DockPanel.SetDock(problems, Dock.Bottom);
        dock.Children.Add(toolbar);
        dock.Children.Add(status);
        dock.Children.Add(problems);
        dock.Children.Add(notices);
        dock.Children.Add(_findBar);
        dock.Children.Add(body);
        Content = TitleBarParts.ForWindow(this, title, dock);

        _editor.Document.UndoStack.PropertyChanged += (_, _) => UpdateModified();
        _editor.TextArea.Caret.PositionChanged += (_, _) => UpdateStatus();
        _editor.TextArea.SelectionChanged += (_, _) => UpdateStatus();
        _editor.Document.Changed += (_, _) => OnTextChanged();
        _editor.TextArea.TextView.ScrollOffsetChanged += (_, _) => _strip.InvalidateVisual();
        AddHandler(KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel);
        _settings.Changed += OnSettingsChanged;
        ActualThemeVariantChanged += (_, _) => RefreshColors();
        _diskTimer.Tick += (_, _) => CheckDisk();
        Activated += (_, _) =>
        {
            CheckDisk();
            if (FocusManager?.GetFocusedElement() is null)
            {
                _editor.TextArea.Focus();
            }
        };

        var normalSize = new Size(Width, Height);
        PropertyChanged += (_, e) =>
        {
            if (e.Property == ClientSizeProperty && WindowState == WindowState.Normal)
            {
                normalSize = ClientSize;
            }
        };
        Closing += OnClosing;
        Closed += (_, _) =>
        {
            _settings.Changed -= OnSettingsChanged;
            _diskTimer.Stop();
            _findDelay.Stop();
            _checkDelay.Stop();
            ClosePreviewPage();
            var maximized = WindowState is WindowState.Maximized or WindowState.FullScreen;
            _settings.Update(s => s with { Editors = s.Editors with { WindowMaximized = maximized, WindowWidth = normalSize.Width, WindowHeight = normalSize.Height } });
        };

        ApplySettings();
        UpdateTitle();
        Opened += (_, _) => Loading = LoadAsync();
    }

    private EditorSettings Options => _settings.Current.Editors;

    internal TextEditor Editor => _editor;

    internal string FilePath => _path;

    internal EditorFile File => _file;

    internal SyntaxLanguage Language => _language;

    internal Task Loading { get; private set; } = Task.CompletedTask;

    internal bool IsModified => !_editor.Document.UndoStack.IsOriginalFile;

    internal ChoiceDialog? CloseQuestion { get; private set; }

    private async Task LoadAsync(string? encodingId = null)
    {
        var path = _path;
        ShowLoading(true);
        EditorFile file;
        try
        {
            file = await Task.Run(() => EditorFiles.Load(path, encodingId));
            if (_request.New)
            {
                file = EditorFiles.New(Options) with { Size = file.Size, WriteTimeUtc = file.WriteTimeUtc };
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ShowLoading(false);
            ShowError(Loc.Format(AppStrings.Editor_CantRead, WindowsPath.GetName(path), ex.Message));
            return;
        }

        SetText(file, keepUndo: false);
        ShowLoading(false);
        _editor.TextArea.Caret.Offset = 0;
        _editor.ScrollToHome();
        _editor.Focus();
        if (encodingId is null)
        {
            Dispatcher.UIThread.Post(() =>
            {
                Activate();
                _editor.TextArea.Focus();
                _editor.TextArea.Caret.BringCaretToView();
            }, DispatcherPriority.Background);
        }

        _diskTimer.Start();
        OpenPreviewByItself();
    }

    private void SetText(EditorFile file, bool keepUndo)
    {
        _file = file;
        _lineEnding = file.LineEnding;
        _indent = file.Indent ?? Options.Indent;
        _diskAsked = false;
        HideDiskBar();
        var colors = Options.ColorsFor(WindowsPath.GetName(_path));
        var limit = Options.ColorsUpToBytes(WindowsPath.GetName(_path));
        var plain = _request.Anyway || (colors != SyntaxLanguage.None && limit > 0 && file.Size > limit);
        ShowSizeNotice(plain && colors != SyntaxLanguage.None ? (file.Size, limit) : null);
        SetLanguage(plain ? SyntaxLanguage.None : colors);

        var document = _editor.Document;
        if (keepUndo)
        {
            var caret = _editor.CaretOffset;
            document.Replace(0, document.TextLength, file.Text);
            _editor.CaretOffset = Math.Min(caret, document.TextLength);
        }
        else
        {
            document.Text = file.Text;
            document.UndoStack.ClearAll();
        }

        document.UndoStack.MarkAsOriginalFile();
        _checkDelay.Stop();
        Checking = CheckAsync();
        ApplyIndent();
        _editor.TextArea.TextView.Redraw();
        UpdateModified();
        UpdateStatus();
    }

    private void ShowLoading(bool on)
    {
        _loading.Content = on ? new TextBlock { Text = AppStrings.Editor_Loading, Margin = new Thickness(24), FontSize = 13 }.Res(TextBlock.ForegroundProperty, "Brush.SecondaryText") : null;
        _loading.IsVisible = on;
        _editor.IsVisible = !on;
    }

    internal void SetLanguage(SyntaxLanguage language)
    {
        var transformers = _editor.TextArea.TextView.LineTransformers;
        if (_colorizer is not null)
        {
            transformers.Remove(_colorizer);
            _colorizer = null;
        }

        _language = language;
        if (language != SyntaxLanguage.None)
        {
            _colorizer = new SyntaxColorizer(this, _editor.Document, language);
            transformers.Add(_colorizer);
        }

        _editor.TextArea.TextView.Redraw();
        RestartChecking();
        if (PreviewKindNow is null)
        {
            ShowPreview(false);
        }
        else if (_previewOn)
        {
            LayoutPreview();
            PreviewRendering = RenderPreviewAsync(force: true);
        }

        UpdateToolbar();
        UpdateStatus();
    }

    private void RefreshColors()
    {
        _colorizer?.ForgetLooks();
        _editor.TextArea.TextView.Redraw();
    }

    private void OnTextChanged()
    {
        if (_colorizer is not null)
        {
            Dispatcher.UIThread.Post(() => _editor.TextArea.TextView.Redraw(), DispatcherPriority.Render);
        }

        _editVersion++;
        UpdateStatus();
        FindAgainLater();
        CheckLater();
        PreviewLater();
    }

    private void OnSettingsChanged(object? sender, EventArgs e) => Dispatcher.UIThread.Post(ApplySettings);

    private void ApplySettings()
    {
        var o = Options;
        _editor.WordWrap = o.Wrap;
        _editor.ShowLineNumbers = o.LineNumbers;
        StyleGutter();
        _editor.Options.HighlightCurrentLine = o.HighlightCurrentLine;
        _editor.Options.ShowSpaces = o.ShowSpaces;
        _editor.Options.ShowTabs = o.ShowSpaces;
        _strip.IsVisible = o.Strip;
        if ((o.Check, o.Folding) != _checkSettings)
        {
            _checkSettings = (o.Check, o.Folding);
            RestartChecking();
        }

        if (_previewOn)
        {
            LayoutPreview();
        }

        UpdateToolbar();
    }

    private void StyleGutter()
    {
        foreach (var margin in _editor.TextArea.LeftMargins)
        {
            if (margin is AvaloniaEdit.Editing.LineNumberMargin numbers)
            {
                numbers.Margin = new Thickness(10, 0, 14, 0);
            }
            else if (margin is Avalonia.Controls.Shapes.Line)
            {
                margin.IsVisible = false;
            }
        }
    }

    private void ApplyIndent()
    {
        _editor.Options.ConvertTabsToSpaces = _indent != IndentStyle.Tabs;
        _editor.Options.IndentationSize = _indent == IndentStyle.Spaces2 ? 2 : 4;
    }

    internal void SetIndent(IndentStyle indent)
    {
        _indent = indent;
        ApplyIndent();
        UpdateStatus();
    }

    private void UpdateEditor(Func<EditorSettings, EditorSettings> change) => _settings.Update(s => s with { Editors = change(s.Editors) });

    private void UpdateTitle()
    {
        _titleDot.Text = IsModified ? "● " : "";
        _titleName.Text = WindowsPath.GetName(_path);
        _titleFolder.Text = " — " + (WindowsPath.GetParent(_path) ?? "");
        Title = _titleDot.Text + _titleName.Text + _titleFolder.Text;
    }

    private void UpdateModified()
    {
        UpdateTitle();
        UpdateToolbar();
        UpdateStatus();
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        var mods = e.KeyModifiers;
        var ctrl = mods == KeyModifiers.Control;
        var plain = mods == KeyModifiers.None;
        if (e.Key is not (Key.LeftCtrl or Key.RightCtrl or Key.LeftShift or Key.RightShift or Key.LeftAlt or Key.RightAlt) && ToolKey(e.Key, mods))
        {
            e.Handled = true;
            return;
        }

        switch (e.Key)
        {
            case Key.S when ctrl:
                _ = SaveAsync();
                break;
            case Key.S when mods == (KeyModifiers.Control | KeyModifiers.Shift):
                _ = SaveAsAsync();
                break;
            case Key.F when ctrl:
                OpenFind(replace: false);
                break;
            case Key.H when ctrl:
                OpenFind(replace: true);
                break;
            case Key.G when ctrl:
                OpenGoTo();
                break;
            case Key.R when ctrl:
                _ = ReloadAsync();
                break;
            case Key.D when ctrl:
                DuplicateLine();
                break;
            case Key.F2 when plain:
                UpdateEditor(o => o with { Wrap = !o.Wrap });
                break;
            case Key.F3 when plain && _findBar.IsVisible:
                FindNext(forward: true);
                break;
            case Key.F3 when mods == KeyModifiers.Shift && _findBar.IsVisible:
                FindNext(forward: false);
                break;
            case Key.F3 when plain:
                ViewFile();
                break;
            case Key.F4 when ctrl:
                Close();
                break;
            case Key.Escape when plain && _findBar.IsVisible:
                CloseFind();
                break;
            case Key.Escape when plain && Options.Close == EditorClose.CtrlF4OrEsc:
                Close();
                break;
            default:
                return;
        }

        e.Handled = true;
    }

    private void ViewFile() => _request.View?.Invoke(_path);

    private void DuplicateLine()
    {
        var document = _editor.Document;
        var line = document.GetLineByOffset(_editor.CaretOffset);
        var text = document.GetText(line.Offset, line.TotalLength);
        if (line.DelimiterLength == 0)
        {
            text = (_lineEnding == LineEnding.Lf ? "\n" : "\r\n") + text;
            document.Insert(line.EndOffset, text);
        }
        else
        {
            document.Insert(line.Offset, text);
        }
    }

    internal void TrimTrailingSpaces()
    {
        var document = _editor.Document;
        document.BeginUpdate();
        try
        {
            foreach (var line in document.Lines.Reverse())
            {
                var text = document.GetText(line);
                var trimmed = text.TrimEnd(' ', '\t');
                if (trimmed.Length < text.Length)
                {
                    document.Remove(line.Offset + trimmed.Length, text.Length - trimmed.Length);
                }
            }
        }
        finally
        {
            document.EndUpdate();
        }
    }

    internal async Task<bool> SaveAsync(string? to = null)
    {
        var path = to ?? _path;
        if (Options.TrimOnSave)
        {
            TrimTrailingSpaces();
        }

        var text = EditorFiles.Prepare(_editor.Document.Text, _lineEnding, trim: false, Options.FinalNewline);
        var encoding = _file.Encoding;
        var bom = _file.Bom;
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
                encoding = TextEncodings.Default;
                bom = false;
            }
        }

        try
        {
            var backup = Options.Backup && System.IO.File.Exists(path);
            var (size, time) = await Task.Run(() => EditorFiles.Save(path, EditorFiles.Encode(text, encoding, bom), backup));
            _path = path;
            _file = _file with { Encoding = encoding, Bom = bom, LineEnding = _lineEnding, Size = size, WriteTimeUtc = time };
            _editor.Document.UndoStack.MarkAsOriginalFile();
            _diskAsked = false;
            HideDiskBar();
            HideError();
            UpdateModified();
            if (!IsLive)
            {
                PreviewAfterSave();
            }

            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ShowError(Loc.Format(AppStrings.Editor_CantSave, WindowsPath.GetName(path), ex.Message));
            return false;
        }
    }

    private async Task SaveAsAsync()
    {
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = AppStrings.Editor_SaveAs,
            SuggestedFileName = WindowsPath.GetName(_path),
            SuggestedStartLocation = await StorageProvider.TryGetFolderFromPathAsync(WindowsPath.GetParent(_path) ?? ""),
        });
        if (file?.TryGetLocalPath() is { } path && await SaveAsync(path))
        {
            UpdateTitle();
        }
    }

    internal async Task ReloadAsync(string? encodingId = null)
    {
        try
        {
            var path = _path;
            var file = await Task.Run(() => EditorFiles.Load(path, encodingId ?? (_file.Bom ? null : _file.Encoding.Id)));
            SetText(file, keepUndo: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ShowError(Loc.Format(AppStrings.Editor_CantRead, WindowsPath.GetName(_path), ex.Message));
        }
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

        var answer = await Ask(
            Loc.Format(AppStrings.Editor_Close_Title, WindowsPath.GetName(_path)),
            _problems.Count > 0 ? [AppStrings.Editor_Close_Text, Loc.Format(AppStrings.Editor_Close_Problem, _problems[0].Line, _problems[0].Message)] : [AppStrings.Editor_Close_Text],
            [new("save", AppStrings.Common_Save, "Enter", Primary: true), new("discard", AppStrings.Editor_DontSave, "N", Key.N), new("cancel", AppStrings.Common_Cancel, "Esc")],
            question => CloseQuestion = question);
        CloseQuestion = null;
        if (answer == "discard" || (answer == "save" && await SaveAsync()))
        {
            _closeConfirmed = true;
            Close();
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

    private async Task<string?> Ask(string title, IReadOnlyList<string> text, IReadOnlyList<Choice> choices, Action<ChoiceDialog>? shown = null)
    {
        var dialog = new ChoiceDialog(title, text, choices, cancel: "cancel");
        shown?.Invoke(dialog);
        return await dialog.ShowDialog<string?>(this);
    }

    private void CheckDisk()
    {
        if (_file.WriteTimeUtc == default || _diskAsked || !IsLoaded)
        {
            return;
        }

        switch (EditorFiles.Check(_path, _file.Size, _file.WriteTimeUtc))
        {
            case DiskState.Changed when Options.DiskChange == EditorDiskChange.Reload && !IsModified:
                _ = ReloadAsync();
                break;
            case DiskState.Changed:
                _diskAsked = true;
                ShowDiskBar(gone: false);
                break;
            case DiskState.Gone:
                _diskAsked = true;
                ShowDiskBar(gone: true);
                break;
        }
    }

    internal void CheckDiskNow() => CheckDisk();

    internal void KeepMine()
    {
        HideDiskBar();
        try
        {
            var info = new FileInfo(_path);
            if (info.Exists)
            {
                _file = _file with { Size = info.Length, WriteTimeUtc = info.LastWriteTimeUtc };
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }

        _diskAsked = false;
    }

    private string Size(long bytes) => Salandual.Core.Listing.DisplayFormat.From(_settings.Current.Panels, CultureInfo.CurrentCulture).Size(bytes);
}

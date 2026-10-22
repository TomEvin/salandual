// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.App/Dialogs/CopyMoveDialog.cs

using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Salandual.App.Controls;
using Salandual.App.Options;
using Salandual.App.Resources;
using Salandual.Core.Listing;
using Salandual.Core.Operations;
using Salandual.Core.Resources;
using Salandual.Core.Settings;
using Salandual.Platform.Windows.FileSystem;

namespace Salandual.App.Dialogs;

public sealed record CopyMoveContext(
    OperationKind Kind,
    IReadOnlyList<OperationSource> Sources,
    string SourceFolder,
    long KnownBytes,
    string OtherPanelPath,
    IReadOnlyList<HotPath> HotPaths,
    IReadOnlyList<string> RecentTargets,
    OperationSettings Settings,
    bool ShowHidden,
    bool ConfirmCreate = false);

public sealed record CopyMoveRequest(TransferTarget Target, TransferOptions Options, bool ToQueue, int? ExpectedConflicts, string TypedTarget, bool MaskInSubfolders);

public sealed class CopyMoveDialog : Window
{
    private readonly CopyMoveContext _context;
    private readonly IOperationFileSystem _fs;
    private readonly DisplayFormat _format;
    private readonly Action<OperationSettings> _saveDefaults;
    private readonly TextBox _to;
    private readonly Popup _suggest;
    private readonly StackPanel _suggestions = new() { Spacing = 0 };
    private readonly StackPanel _checks = new() { Spacing = 0 };
    private readonly Border _checksBox;
    private readonly StackPanel _options;
    private readonly Button _disclose;
    private readonly Button _go, _queue;
    private readonly ComboBox _ifExists;
    private readonly CheckBox _times, _attrs, _streams, _filterOn, _skipEmpty, _limitOn, _verify;
    private readonly TextBox _filter, _limit;
    private readonly List<(string Group, string Path, string Hint)> _shown = [];
    private CheckBox? _ontoItself;
    private RadioButton? _maskInside;
    private bool _maskInsideChosen;
    private TransferPreview? _preview;
    private TransferTarget? _target;
    private CancellationTokenSource? _scan;
    private DispatcherTimer? _debounce;
    private int _hot = -1;
    private bool _settingText;
    private bool _blocked;

    public CopyMoveDialog(CopyMoveContext context, IOperationFileSystem fs, DisplayFormat format, Action<OperationSettings> saveDefaults)
    {
        _context = context;
        _fs = fs;
        _format = format;
        _saveDefaults = saveDefaults;
        _maskInsideChosen = context.Settings.MaskInSubfolders;
        var verb = Verb;
        Title = verb;
        Width = 620;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        this.Res(BackgroundProperty, "Brush.PanelBackground");

        var heading = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 15 };
        heading.Res(TextBlock.ForegroundProperty, "Brush.Text");
        var what = context.KnownBytes > 0 ? Loc.Format(AppStrings.Copy_WhatWithSize, FileOperations.Count(context.Sources), format.Size(context.KnownBytes)) : FileOperations.Count(context.Sources);
        Rich.Add(heading.Inlines!, AppStrings.Copy_Heading, text =>
            {
                var run = new Run(text);
                run.Bind(Run.ForegroundProperty, heading.GetResourceObservable("Brush.SecondaryText"));
                return run;
            },
            new Run(verb) { FontWeight = FontWeight.SemiBold },
            new Run(what).Also(r => r.Bind(Run.ForegroundProperty, heading.GetResourceObservable("Brush.SecondaryText"))),
            new Run(FolderName(context.SourceFolder)) { FontWeight = FontWeight.SemiBold });
        Heading = heading;

        _to = new TextBox { VerticalContentAlignment = VerticalAlignment.Center, MinHeight = 32 };
        _to.Res(TextBox.FontFamilyProperty, "Font.Mono");
        Avalonia.Automation.AutomationProperties.SetName(_to, AppStrings.Copy_To);
        _to.AddHandler(KeyDownEvent, OnToKey, RoutingStrategies.Tunnel);
        _to.TextChanged += (_, _) =>
        {
            if (_settingText)
            {
                return;
            }

            ShowSuggestions(typed: true);
            ScheduleCheck();
        };
        var hot = new Button { Content = "★", Width = 32, MinHeight = 32, Padding = new Thickness(0), HorizontalContentAlignment = HorizontalAlignment.Center, Focusable = false };
        ToolTip.SetTip(hot, AppStrings.Copy_HotTip);
        hot.Click += (_, _) => ShowSuggestions(typed: false);
        var browse = new Button { Content = AppStrings.Common_Browse, MinHeight = 32, Padding = new Thickness(12, 0), VerticalContentAlignment = VerticalAlignment.Center, Focusable = false };
        browse.Click += async (_, _) => await BrowseAsync();
        var toRow = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto"), ColumnSpacing = 6 };
        toRow.Children.Add(_to);
        Grid.SetColumn(hot, 1);
        Grid.SetColumn(browse, 2);
        toRow.Children.Add(hot);
        toRow.Children.Add(browse);

        _suggest = new Popup
        {
            PlacementTarget = _to,
            Placement = PlacementMode.Bottom,
            IsLightDismissEnabled = false,
            Child = new Border
            {
                Padding = new Thickness(4),
                CornerRadius = new CornerRadius(8),
                BorderThickness = new Thickness(1),
                MinWidth = 420,
                BoxShadow = BoxShadows.Parse("0 8 24 0 #30000000"),
                Child = new ScrollViewer { Content = _suggestions, MaxHeight = 300, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled },
            }.Res(Border.BackgroundProperty, "Brush.PanelBackground").Res(Border.BorderBrushProperty, "Brush.BorderStrong"),
        };
        _to.LostFocus += (_, _) => Dispatcher.UIThread.Post(() => _suggest.IsOpen = false, DispatcherPriority.Background);

        var toField = new StackPanel { Spacing = 5 };
        toField.Children.Add(OptionsUi.Text(AppStrings.Copy_To, 13, FontWeight.SemiBold));
        toField.Children.Add(toRow);
        toField.Children.Add(OptionsUi.Secondary(AppStrings.Copy_ToHint));
        toField.Children.Add(_suggest);

        _checksBox = new Border { CornerRadius = new CornerRadius(4), BorderThickness = new Thickness(1), Child = _checks, ClipToBounds = true }.Res(Border.BorderBrushProperty, "Brush.Border");

        var s = context.Settings;
        _ifExists = new ComboBox { MinWidth = 260 };
        foreach (var label in new[] { AppStrings.Copy_IfExists_Ask, AppStrings.Copy_IfExists_OverwriteOlder, AppStrings.Copy_IfExists_OverwriteAll, AppStrings.Copy_IfExists_SkipAll, AppStrings.Copy_IfExists_KeepBoth })
        {
            _ifExists.Items.Add(label);
        }

        _ifExists.SelectedIndex = s.IfExists switch
        {
            ExistingFilePolicy.OverwriteOlder => 1,
            ExistingFilePolicy.Overwrite => 2,
            ExistingFilePolicy.Skip => 3,
            ExistingFilePolicy.KeepBoth => 4,
            _ => 0,
        };
        _times = Check(AppStrings.Copy_KeepFolderTimes, s.KeepFolderTimes);
        _attrs = Check(AppStrings.Copy_KeepAttributes, s.KeepAttributes);
        _streams = Check(AppStrings.Copy_KeepStreams, s.KeepStreams);
        var owners = Check(AppStrings.Copy_KeepOwners, false);
        owners.IsEnabled = false;
        _filterOn = Check(AppStrings.Copy_OnlyMatching, s.FilterEnabled);
        _filter = new TextBox { Text = s.Filter, Width = 110, MinHeight = 26, Padding = new Thickness(6, 2), FontSize = 12 };
        _filter.Res(TextBox.FontFamilyProperty, "Font.Mono");
        _skipEmpty = Check(AppStrings.Copy_SkipEmpty, s.SkipEmptyFolders);
        _limitOn = Check(AppStrings.Copy_LimitTo, s.LimitSpeed);
        _limit = new TextBox { Text = s.SpeedLimitMb.ToString("0.#", System.Globalization.CultureInfo.CurrentCulture), Width = 56, MinHeight = 26, Padding = new Thickness(6, 2), FontSize = 12, HorizontalContentAlignment = HorizontalAlignment.Right };
        _verify = Check(AppStrings.Copy_Verify, s.Verify);
        var keep = new StackPanel { Spacing = 4, Children = { OptionsUi.Text(AppStrings.Copy_Keep, 12, FontWeight.SemiBold, "Brush.SecondaryText"), _times, _attrs, _streams, owners } };
        var filterRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Children = { _filterOn, _filter } };
        var limitRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Children = { _limitOn, _limit, OptionsUi.Text("MB/s", 13) } };
        var speed = new StackPanel { Spacing = 4, Children = { OptionsUi.Text(AppStrings.Copy_FilterAndSpeed, 12, FontWeight.SemiBold, "Brush.SecondaryText"), filterRow, _skipEmpty, limitRow, _verify } };
        var columns = new Grid { ColumnDefinitions = new ColumnDefinitions("*,16,*") };
        Grid.SetColumn(speed, 2);
        columns.Children.Add(keep);
        columns.Children.Add(speed);
        var existsRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, Children = { OptionsUi.Text(AppStrings.Copy_IfExists, 13), _ifExists } };
        _options = new StackPanel { Spacing = 12, IsVisible = false, Children = { existsRow, columns } };
        _disclose = new Button { Padding = new Thickness(0, 2), Background = Brushes.Transparent, BorderThickness = new Thickness(0), HorizontalAlignment = HorizontalAlignment.Left };
        _disclose.Click += (_, _) =>
        {
            _options.IsVisible = !_options.IsVisible;
            UpdateDisclose();
        };
        foreach (var box in new[] { _times, _attrs, _streams, _filterOn, _skipEmpty, _limitOn, _verify })
        {
            box.IsCheckedChanged += (_, _) => OptionsChanged();
        }

        _ifExists.SelectionChanged += (_, _) => OptionsChanged();
        _filter.TextChanged += (_, _) => OptionsChanged();
        _limit.TextChanged += (_, _) => OptionsChanged();
        UpdateDisclose();

        var save = Operations.Ui.Link(AppStrings.Copy_SaveDefaults, () =>
        {
            _saveDefaults(CurrentSettings());
            SavedDefaults = true;
        });
        var cancel = new Button { Content = AppStrings.Common_Cancel, MinWidth = 84, Padding = new Thickness(14, 5), HorizontalContentAlignment = HorizontalAlignment.Center };
        cancel.Click += (_, _) => Close(null);
        var queueFirst = context.Settings.Enter == EnterAction.AddToQueue;
        _queue = new Button { Content = queueFirst ? AppStrings.Copy_StartNowShift : AppStrings.Copy_AddToQueueShift, Padding = new Thickness(14, 5) };
        _queue.Click += (_, _) => Accept(toQueue: !queueFirst);
        _go = new Button { Content = queueFirst ? AppStrings.Copy_AddToQueueEnter : Loc.Format(AppStrings.Copy_GoEnter, verb), Padding = new Thickness(14, 5), MinWidth = 96 };
        _go.Classes.Add("accent");
        _go.Click += (_, _) => Accept(toQueue: queueFirst);
        var right = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { cancel, _queue, _go } };
        var footerRow = new DockPanel();
        DockPanel.SetDock(right, Dock.Right);
        footerRow.Children.Add(right);
        footerRow.Children.Add(save);
        var footer = new Border { Padding = new Thickness(20, 12), BorderThickness = new Thickness(0, 1, 0, 0), Child = footerRow }
            .Res(Border.BackgroundProperty, "Brush.WindowBackground").Res(Border.BorderBrushProperty, "Brush.Border");

        var body = new StackPanel { Spacing = 12, Margin = new Thickness(20, 6, 20, 16) };
        body.Children.Add(heading);
        body.Children.Add(toField);
        body.Children.Add(_checksBox);
        body.Children.Add(_disclose);
        body.Children.Add(_options);
        var root = new DockPanel();
        DockPanel.SetDock(footer, Dock.Bottom);
        root.Children.Add(footer);
        root.Children.Add(body);
        Content = TitleBarParts.ForDialog(this, root);

        SetTo(context.OtherPanelPath.Length > 0 ? WindowsPath.Join(context.OtherPanelPath, "") : "");
        Opened += (_, _) =>
        {
            _to.Focus();
            _to.SelectAll();
            ScheduleCheck(immediately: true);
        };
        Closed += (_, _) => _scan?.Cancel();
    }

    internal TextBlock Heading { get; }

    internal TextBox To => _to;

    internal Button Go => _go;

    internal Button QueueButton => _queue;

    internal StackPanel Checks => _checks;

    internal CheckBox? OntoItselfBox => _ontoItself;

    internal RadioButton? MaskInsideOption => _maskInside;

    internal IReadOnlyList<(string Group, string Path, string Hint)> Suggestions => _shown;

    internal bool SuggestionsOpen => _suggest.IsOpen;

    internal TransferPreview? Preview => _preview;

    internal bool SavedDefaults { get; private set; }

    internal Task Checked { get; private set; } = Task.CompletedTask;

    internal void ShowOptions()
    {
        _options.IsVisible = true;
        UpdateDisclose();
    }

    internal ComboBox IfExists => _ifExists;

    private void SetTo(string text)
    {
        _settingText = true;
        _to.Text = text;
        _to.CaretIndex = text.Length;
        _settingText = false;
    }

    private string? FullTarget()
    {
        var text = _to.Text ?? "";
        if (PathInput.Resolve(text, _context.SourceFolder) is not { } full)
        {
            return null;
        }

        var trimmed = text.TrimEnd();
        return (trimmed.EndsWith('\\') || trimmed.EndsWith('/')) && !full.EndsWith('\\') ? full + "\\" : full;
    }

    private void ScheduleCheck(bool immediately = false)
    {
        _debounce?.Stop();
        if (immediately)
        {
            Checked = CheckAsync();
            return;
        }

        _debounce ??= new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
        _debounce.Tick -= OnDebounce;
        _debounce.Tick += OnDebounce;
        _debounce.Start();
        ShowChecking();
    }

    private void OnDebounce(object? sender, EventArgs e)
    {
        _debounce!.Stop();
        Checked = CheckAsync();
    }

    private void ShowChecking()
    {
        _checks.Children.Clear();
        _checks.Children.Add(CheckRow(CheckKind.Info, AppStrings.Copy_Checking, null));
    }

    private async Task CheckAsync()
    {
        _scan?.Cancel();
        var cts = _scan = new CancellationTokenSource(TimeSpan.FromSeconds(1.5));
        var text = _to.Text;
        if (FullTarget() is not { } full)
        {
            _target = null;
            _preview = null;
            Render();
            return;
        }

        var maskInside = _maskInsideChosen;
        var kind = _context.Kind;
        var sources = _context.Sources;
        var filter = _filterOn.IsChecked == true ? _filter.Text : null;
        try
        {
            var (target, preview) = await Task.Run(() =>
            {
                var t = TransferTarget.Resolve(_fs, full, sources.Count) with { MaskInSubfolders = maskInside };
                return (t, TransferPreview.Scan(_fs, kind, sources, t, filter, cts.Token));
            });
            if (cts.IsCancellationRequested && !ReferenceEquals(cts, _scan) || text != _to.Text)
            {
                return;
            }

            _target = target;
            _preview = preview;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            _target = null;
            _preview = null;
        }

        Render();
    }

    private enum CheckKind { Error, Warning, Info }

    private void Render()
    {
        _checks.Children.Clear();
        _ontoItself = null;
        _maskInside = null;
        var blocked = false;
        if (_target is not { } target || _preview is not { } p)
        {
            _checks.Children.Add(CheckRow(CheckKind.Error, string.IsNullOrWhiteSpace(_to.Text) ? _context.Kind == OperationKind.Copy ? AppStrings.Copy_TypeWhereCopy : AppStrings.Copy_TypeWhereMove : AppStrings.Copy_NotAPath, null));
            SetBlocked(true);
            return;
        }

        var root = (WindowsPath.GetRoot(target.Folder) ?? target.Folder).TrimEnd('\\');
        if (p.OntoItself.Count > 0)
        {
            if (_context.Kind == OperationKind.Copy)
            {
                _ontoItself = new CheckBox { Content = AppStrings.Copy_WithNewNames, IsChecked = false };
                _ontoItself.IsCheckedChanged += (_, _) => SetBlocked(BlockedNow());
                var names = new StackPanel { Spacing = 1, Margin = new Thickness(26, 2, 0, 0) };
                foreach (var path in p.OntoItself.Take(2))
                {
                    var name = Path.GetFileName(path);
                    var dot = name.LastIndexOf('.');
                    var renamed = dot > 0 ? $"{name[..dot]} (2){name[dot..]}" : name + " (2)";
                    names.Children.Add(OptionsUi.Text($"{name}  →  {renamed}", 12).Also(t => t.Res(TextBlock.FontFamilyProperty, "Font.Mono")));
                }

                if (p.OntoItself.Count > 2)
                {
                    names.Children.Add(OptionsUi.Text(Loc.Format(AppStrings.Copy_AndMoreSameWay, p.OntoItself.Count - 2), 12, brush: "Brush.SecondaryText"));
                }

                _checks.Children.Add(CheckRow(CheckKind.Warning, Strings.Issue_CopyOntoItself, Loc.Format(p.OntoItself.Count == 1 ? AppStrings.Copy_AlreadyIn_One : AppStrings.Copy_AlreadyIn_Other, FolderName(target.Folder)), new StackPanel { Children = { _ontoItself, names } }));
            }
            else
            {
                _checks.Children.Add(CheckRow(CheckKind.Warning, AppStrings.Copy_AlreadyHere, AppStrings.Copy_MoveOntoItself));
                blocked = true;
            }
        }

        if (p.Target.ReadOnly)
        {
            _checks.Children.Add(CheckRow(CheckKind.Error, Loc.Format(AppStrings.Copy_WriteProtected, root), AppStrings.Copy_NothingWritten));
            blocked = true;
        }

        if (p.NotEnoughSpace)
        {
            var need = p.NeededBytes - p.Target.FreeBytes!.Value;
            _checks.Children.Add(CheckRow(CheckKind.Error, Loc.Format(AppStrings.Copy_NoSpace, root, _format.Size(p.NeededBytes), _format.Size(p.Target.FreeBytes.Value)), Loc.Format(_context.Kind == OperationKind.Copy ? AppStrings.Copy_FewerCopy : AppStrings.Copy_FewerMove, _format.Size(need))));
            blocked = true;
        }

        if (p.TooBig.Count > 0)
        {
            var list = string.Join(", ", p.TooBig.Take(2).Select(t => $"{Path.GetFileName(t.Path)} ({_format.Size(t.Size)})"));
            _checks.Children.Add(CheckRow(CheckKind.Warning, Loc.Plural(p.TooBig.Count, AppStrings.Copy_TooBig_One, AppStrings.Copy_TooBig_Other, root, p.Target.FileSystem), Loc.Format(p.TooBig.Count > 2 ? AppStrings.Copy_TooBigSkippedMore : AppStrings.Copy_TooBigSkipped, list, p.TooBig.Count - 2)));
        }

        if (p.TooLongPaths > 0)
        {
            _checks.Children.Add(CheckRow(CheckKind.Warning, Loc.Plural(p.TooLongPaths, AppStrings.Copy_TooLong_One, AppStrings.Copy_TooLong_Other, root), AppStrings.Copy_TooLongSkipped));
        }

        if (p.OnlineOnlyFiles > 0)
        {
            _checks.Children.Add(CheckRow(CheckKind.Info, Loc.Plural(p.OnlineOnlyFiles, AppStrings.Copy_OnlineOnly_One, AppStrings.Copy_OnlineOnly_Other), Loc.Format(AppStrings.Copy_OnlineOnlyDownload, _format.Size(p.OnlineOnlyBytes))));
        }

        if (p.Conflicts.Count > 0)
        {
            var samples = string.Join(AppStrings.Copy_SampleSeparator, p.Conflicts.Samples.Take(2).Select(c => Loc.Format(AppStrings.Copy_ConflictSample, Path.GetFileName(c.Target), Compare(c))));
            var rule = IfExistsPolicy() == ExistingFilePolicy.Ask
                ? AppStrings.Copy_WillAsk
                : Loc.Format(AppStrings.Copy_OptionsSay, ((string)_ifExists.SelectedItem!).ToLower(CultureInfo.CurrentCulture));
            _checks.Children.Add(CheckRow(CheckKind.Warning, Loc.Plural(p.Conflicts.Count, p.Conflicts.Complete ? AppStrings.Copy_Exist_One : AppStrings.Copy_ExistAtLeast_One, p.Conflicts.Complete ? AppStrings.Copy_Exist_Other : AppStrings.Copy_ExistAtLeast_Other), Loc.Format(AppStrings.Copy_ExistDetail, samples, rule)));
        }

        if (target.CreatesFolder)
        {
            _checks.Children.Add(CheckRow(CheckKind.Info, Loc.Format(AppStrings.Copy_NotYet, FolderName(target.Folder)), Loc.Format(AppStrings.Copy_WillBeCreated, WindowsPath.GetParent(target.Folder) ?? target.Folder)));
        }

        if (target.NewName is { } newName)
        {
            _checks.Children.Add(CheckRow(CheckKind.Info, Loc.Format(_context.Kind == OperationKind.Copy ? AppStrings.Copy_CopiedAs : AppStrings.Copy_MovedAs, newName), null));
        }

        if (p.MaskRenames.Count > 0 && p.MaskInsideCount == 0)
        {
            var (from, to) = p.MaskRenames[0];
            _checks.Children.Add(CheckRow(CheckKind.Info, AppStrings.Copy_MaskNames, Loc.Format(p.MaskRenameCount > 1 ? AppStrings.Copy_MaskSampleMore : AppStrings.Copy_MaskSample, from, to, p.MaskRenameCount - 1)));
        }

        if (p.MaskInsideCount > 0)
        {
            _checks.Children.Add(MaskChoice(p));
        }

        if (p.Links.Count > 0)
        {
            var first = p.Links[0];
            var how = _context.Kind == OperationKind.Copy
                ? p.Links.Count == 1 ? AppStrings.Copy_LinkEmpty_One : AppStrings.Copy_LinkEmpty_Other
                : p.Links.Count == 1 ? AppStrings.Copy_LinkStays_One : AppStrings.Copy_LinkStays_Other;
            _checks.Children.Add(CheckRow(CheckKind.Info, Loc.Plural(p.Links.Count, AppStrings.Copy_Links_One, AppStrings.Copy_Links_Other, Path.GetFileName(first)), how));
        }

        if (!p.Complete)
        {
            _checks.Children.Add(CheckRow(CheckKind.Info, Loc.Plural(p.Files, AppStrings.Copy_CheckedSoFar_One, AppStrings.Copy_CheckedSoFar_Other), AppStrings.Copy_MoreLater));
        }

        if (_checks.Children.Count == 0)
        {
            _checks.Children.Add(CheckRow(CheckKind.Info, AppStrings.Copy_NoSameName, null));
        }

        _blocked = blocked;
        SetBlocked(BlockedNow());
    }

    private bool BlockedNow() => _blocked || (_ontoItself is { IsChecked: not true });

    private void SetBlocked(bool blocked)
    {
        _go.IsEnabled = !blocked;
        _queue.IsEnabled = !blocked;
    }

    private Control MaskChoice(TransferPreview p)
    {
        var only = new RadioButton { Content = AppStrings.Copy_MaskOnlySelected, GroupName = "mask", IsChecked = !_maskInsideChosen };
        var also = new RadioButton { Content = AppStrings.Copy_MaskAlsoInside, GroupName = "mask", IsChecked = _maskInsideChosen };
        _maskInside = also;
        var names = new StackPanel { Spacing = 1, Margin = new Thickness(26, 2, 0, 0) };
        void Fill()
        {
            names.Children.Clear();
            var pairs = p.MaskRenames.Take(2).Concat(_maskInsideChosen ? p.MaskInsideRenames.Take(2) : []);
            foreach (var (from, to) in pairs)
            {
                names.Children.Add(OptionsUi.Text($"{from}  →  {to}", 12).Also(t => t.Res(TextBlock.FontFamilyProperty, "Font.Mono")));
            }
        }

        also.IsCheckedChanged += (_, _) =>
        {
            _maskInsideChosen = also.IsChecked == true;
            Fill();
            _target = _target is null ? null : _target with { MaskInSubfolders = _maskInsideChosen };
        };
        Fill();
        var detail = Loc.Format(AppStrings.Copy_MaskDetail, Loc.Plural(p.MaskRenameCount, AppStrings.Copy_MaskSelected_One, AppStrings.Copy_MaskSelected_Other), Loc.Plural(p.MaskInsideCount, AppStrings.Copy_MaskMore_One, AppStrings.Copy_MaskMore_Other));
        return CheckRow(CheckKind.Warning, AppStrings.Copy_MaskRenames, detail, new StackPanel { Spacing = 2, Children = { only, also, names } });
    }

    private Control CheckRow(CheckKind kind, string title, string? detail, Control? extra = null)
    {
        var (mark, brush) = kind switch
        {
            CheckKind.Error => ("!", "Brush.Danger"),
            CheckKind.Warning => ("!", "Brush.Warning"),
            _ => ("i", "Brush.Accent"),
        };
        var icon = new Border
        {
            Width = 18,
            Height = 18,
            CornerRadius = new CornerRadius(9),
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 1, 10, 0),
            Child = OptionsUi.Text(mark, 11, FontWeight.Bold, kind == CheckKind.Info ? "Brush.OnAccent" : "Brush.PanelBackground").Also(t => t.HorizontalAlignment = HorizontalAlignment.Center),
        }.Res(Border.BackgroundProperty, brush);
        var text = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 13 };
        text.Res(TextBlock.ForegroundProperty, "Brush.Text");
        text.Inlines!.Add(new Run(title) { FontWeight = FontWeight.SemiBold });
        if (detail is not null)
        {
            text.Inlines.Add(new Run(" " + detail));
        }

        var content = new StackPanel { Spacing = 4, Children = { text } };
        if (extra is not null)
        {
            content.Children.Add(extra);
        }

        var row = new DockPanel();
        DockPanel.SetDock(icon, Dock.Left);
        row.Children.Add(icon);
        row.Children.Add(content);
        var border = new Border { Padding = new Thickness(12, 8), Child = row, Tag = kind.ToString() };
        if (_checks.Children.Count > 0)
        {
            border.BorderThickness = new Thickness(0, 1, 0, 0);
            border.Res(Border.BorderBrushProperty, "Brush.Border");
        }

        if (kind == CheckKind.Error)
        {
            border.Res(Border.BackgroundProperty, "Brush.DangerBackground");
        }

        return border;
    }

    private string Compare(ConflictPreview c)
        => c.SourceInfo.LastWriteUtc > c.TargetInfo.LastWriteUtc ? AppStrings.Copy_NewerHere
        : c.SourceInfo.LastWriteUtc < c.TargetInfo.LastWriteUtc ? AppStrings.Copy_OlderHere
        : c.SourceInfo.Size != c.TargetInfo.Size ? AppStrings.Copy_SameTimeOtherSize : AppStrings.Copy_TheSame;

    private void ShowSuggestions(bool typed)
    {
        var text = (_to.Text ?? "").Trim();
        var key = typed ? text.TrimEnd('\\') : "";
        var tail = key[(key.LastIndexOf('\\') + 1)..];
        _shown.Clear();
        void Add(string group, string path, string hint)
        {
            if (path.Length > 0 && !_shown.Any(s => string.Equals(s.Path, path, StringComparison.OrdinalIgnoreCase))
                && !string.Equals(path.TrimEnd('\\'), key, StringComparison.OrdinalIgnoreCase)
                && (key.Length == 0 || path.Contains(tail, StringComparison.OrdinalIgnoreCase)))
            {
                _shown.Add((group, path, hint));
            }
        }

        Add(AppStrings.Copy_Group_OtherPanel, _context.OtherPanelPath, "");
        foreach (var h in _context.HotPaths.Where(h => !string.IsNullOrEmpty(h.Path)).Take(10))
        {
            Add(AppStrings.Copy_Group_HotPaths, HotPathSettings.Expand(h.Path), h.Slot <= 10 ? $"Ctrl+{h.Slot % 10} · {h.Name}" : h.Name);
        }

        foreach (var recent in _context.RecentTargets.Take(8))
        {
            Add(AppStrings.Copy_Group_Recent, recent, "");
        }

        _hot = -1;
        RenderSuggestions();
        if (typed && PathInput.ForCompletion(text, _context.SourceFolder) is { } completion && completion.Prefix.Length > 0)
        {
            _ = AddFoldersAsync(completion.Folder, completion.Prefix, text);
        }
    }

    private async Task AddFoldersAsync(string folder, string prefix, string typed)
    {
        var showHidden = _context.ShowHidden;
        var names = await Task.Run(() => PathInput.Complete(folder, prefix, showHidden, 6));
        if (typed != (_to.Text ?? "").Trim())
        {
            return;
        }

        foreach (var name in names)
        {
            var path = WindowsPath.Join(folder, name);
            if (!_shown.Any(s => string.Equals(s.Path, path, StringComparison.OrdinalIgnoreCase)))
            {
                _shown.Add((AppStrings.Copy_Group_Folders, path, ""));
            }
        }

        RenderSuggestions();
    }

    private void RenderSuggestions()
    {
        _suggestions.Children.Clear();
        string? group = null;
        for (var i = 0; i < _shown.Count; i++)
        {
            var (g, path, hint) = _shown[i];
            if (g != group)
            {
                group = g;
                _suggestions.Children.Add(OptionsUi.Text(g.ToUpperInvariant(), 11, FontWeight.SemiBold, "Brush.TertiaryText").Also(t => t.Margin = new Thickness(10, 6, 10, 2)));
            }

            var index = i;
            var row = new DockPanel { Height = 28 };
            if (hint.Length > 0)
            {
                var h = OptionsUi.Text(hint, 11, brush: "Brush.TertiaryText");
                h.Margin = new Thickness(12, 0, 0, 0);
                DockPanel.SetDock(h, Dock.Right);
                row.Children.Add(h);
            }

            row.Children.Add(OptionsUi.Text(path, 12).Also(t =>
            {
                t.TextTrimming = TextTrimming.PrefixCharacterEllipsis;
                t.Res(TextBlock.FontFamilyProperty, "Font.Mono");
            }));
            var item = new Border { Padding = new Thickness(10, 0), CornerRadius = new CornerRadius(4), Child = row, Background = Brushes.Transparent, Cursor = new Cursor(StandardCursorType.Hand) };
            if (i == _hot)
            {
                item.Res(Border.BackgroundProperty, "Brush.TextSelection");
            }

            item.PointerPressed += (_, e) =>
            {
                Pick(index);
                e.Handled = true;
            };
            _suggestions.Children.Add(item);
        }

        _suggest.IsOpen = _shown.Count > 0 && _to.IsFocused;
    }

    private void Pick(int index)
    {
        SetTo(WindowsPath.Join(_shown[index].Path, ""));
        _suggest.IsOpen = false;
        _hot = -1;
        _to.Focus();
        ScheduleCheck(immediately: true);
    }

    private void OnToKey(object? sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Down when e.KeyModifiers == KeyModifiers.Alt || !_suggest.IsOpen:
                ShowSuggestions(typed: false);
                _hot = _shown.Count > 0 ? 0 : -1;
                RenderSuggestions();
                e.Handled = true;
                break;
            case Key.Down:
                _hot = Math.Min(_shown.Count - 1, _hot + 1);
                RenderSuggestions();
                e.Handled = true;
                break;
            case Key.Up when _suggest.IsOpen:
                _hot = Math.Max(-1, _hot - 1);
                RenderSuggestions();
                e.Handled = true;
                break;
            case Key.Enter when _suggest.IsOpen && _hot >= 0:
                Pick(_hot);
                e.Handled = true;
                break;
            case Key.Escape when _suggest.IsOpen:
                _suggest.IsOpen = false;
                e.Handled = true;
                break;
            case Key.Tab when e.KeyModifiers == KeyModifiers.None:
                if (Complete())
                {
                    e.Handled = true;
                }

                break;
        }
    }

    private bool Complete()
    {
        var text = _to.Text ?? "";
        if (PathInput.ForCompletion(text, _context.SourceFolder) is not { } c || c.Prefix.Length == 0)
        {
            return false;
        }

        var names = PathInput.Complete(c.Folder, c.Prefix, _context.ShowHidden, 1);
        if (names.Count == 0)
        {
            return false;
        }

        SetTo(WindowsPath.Join(WindowsPath.Join(c.Folder, names[0]), ""));
        _suggest.IsOpen = false;
        ScheduleCheck(immediately: true);
        return true;
    }

    private async Task BrowseAsync()
    {
        var start = FullTarget() is { } full ? WindowsPath.GetParent(full.TrimEnd('\\')) ?? full : _context.OtherPanelPath;
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = _context.Kind == OperationKind.Copy ? AppStrings.Copy_BrowseTitleCopy : AppStrings.Copy_BrowseTitleMove,
            AllowMultiple = false,
            SuggestedStartLocation = start is null ? null : await StorageProvider.TryGetFolderFromPathAsync(start),
        });
        if (folders.Count > 0 && folders[0].TryGetLocalPath() is { } path)
        {
            SetTo(WindowsPath.Join(path, ""));
            ScheduleCheck(immediately: true);
        }
    }

    private ExistingFilePolicy IfExistsPolicy() => _ifExists.SelectedIndex switch
    {
        1 => ExistingFilePolicy.OverwriteOlder,
        2 => ExistingFilePolicy.Overwrite,
        3 => ExistingFilePolicy.Skip,
        4 => ExistingFilePolicy.KeepBoth,
        _ => ExistingFilePolicy.Ask,
    };

    private OperationSettings CurrentSettings() => _context.Settings with
    {
        IfExists = IfExistsPolicy(),
        KeepFolderTimes = _times.IsChecked == true,
        KeepAttributes = _attrs.IsChecked == true,
        KeepStreams = _streams.IsChecked == true,
        FilterEnabled = _filterOn.IsChecked == true,
        Filter = _filter.Text ?? "*.*",
        SkipEmptyFolders = _skipEmpty.IsChecked == true,
        LimitSpeed = _limitOn.IsChecked == true,
        SpeedLimitMb = double.TryParse(_limit.Text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.CurrentCulture, out var mb) && mb > 0 ? mb : _context.Settings.SpeedLimitMb,
        Verify = _verify.IsChecked == true,
        MaskInSubfolders = _maskInsideChosen,
    };

    private void OptionsChanged()
    {
        UpdateDisclose();
        if (_preview is not null)
        {
            Render();
        }
    }

    private void UpdateDisclose()
    {
        var current = CurrentSettings();
        var defaults = new OperationSettings();
        var changed = 0;
        changed += current.IfExists != defaults.IfExists ? 1 : 0;
        changed += current.KeepFolderTimes != defaults.KeepFolderTimes ? 1 : 0;
        changed += current.KeepAttributes != defaults.KeepAttributes ? 1 : 0;
        changed += current.KeepStreams != defaults.KeepStreams ? 1 : 0;
        changed += current.FilterEnabled ? 1 : 0;
        changed += current.SkipEmptyFolders ? 1 : 0;
        changed += current.LimitSpeed ? 1 : 0;
        changed += current.Verify ? 1 : 0;
        var text = new TextBlock { FontSize = 13 };
        text.Res(TextBlock.ForegroundProperty, "Brush.Text");
        text.Inlines!.Add(new Run((_options.IsVisible ? "▾ " : "▸ ") + AppStrings.Copy_Options) { FontWeight = FontWeight.SemiBold });
        if (changed > 0)
        {
            text.Inlines.Add(new Run(" · " + Loc.Format(AppStrings.Copy_Changed, changed)));
        }

        _disclose.Content = text;
    }

    internal ConfirmDialog? CreateQuestion { get; private set; }

    private async void Accept(bool toQueue)
    {
        if (!_go.IsEnabled || _target is not { } target)
        {
            return;
        }

        if (target.CreatesFolder && _context.ConfirmCreate)
        {
            var ask = CreateQuestion = new ConfirmDialog(AppStrings.CreateFolder_Title, Loc.Format(AppStrings.CreateFolder_Text, target.Folder), AppStrings.CreateFolder_Create, AppStrings.Common_Cancel);
            bool create;
            try
            {
                create = await ask.ShowDialog<bool>(this);
            }
            finally
            {
                CreateQuestion = null;
            }

            if (!create)
            {
                return;
            }
        }

        var settings = CurrentSettings();
        var options = settings.ToOptions() with { MaxFileSize = _preview?.Target.MaxFileSize };
        var final = target with { MaskInSubfolders = _maskInsideChosen, RenameOntoItself = _ontoItself?.IsChecked == true };
        Close(new CopyMoveRequest(final, options, toQueue, _preview?.Conflicts.Complete == true ? _preview.Conflicts.Count : null, (_to.Text ?? "").Trim(), _maskInsideChosen));
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Handled)
        {
            return;
        }

        if (e.Key == Key.Escape)
        {
            Close(null);
            e.Handled = true;
        }
        else if (e.Key == Key.Enter && e.Source is not Button)
        {
            var queueFirst = _context.Settings.Enter == EnterAction.AddToQueue;
            Accept(toQueue: e.KeyModifiers.HasFlag(KeyModifiers.Shift) ? !queueFirst : queueFirst);
            e.Handled = true;
        }
    }

    private string Verb => _context.Kind == OperationKind.Copy ? AppStrings.Copy_Copy : AppStrings.Copy_Move;

    private static string FolderName(string path) => WindowsPath.GetName(path).TrimEnd('\\') is { Length: > 0 } n ? n : path;


    private static CheckBox Check(string text, bool on) => new() { Content = text, IsChecked = on, FontSize = 13, MinHeight = 26 };
}

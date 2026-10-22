// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.App/Dialogs/PropertiesDialog.cs

using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Salandual.App.Controls;
using Salandual.App.Options;
using Salandual.App.Resources;
using Salandual.App.Theme;
using Salandual.Core.Listing;
using Salandual.Core.Resources;
using Salandual.Platform.Windows.FileSystem;
using Salandual.Platform.Windows.Shell;

namespace Salandual.App.Dialogs;

internal sealed record PropertiesActions(
    Action<string, string?> OpenInOtherPanel,
    Action<PreviousVersion, string> Restore,
    Action<IReadOnlyList<(string Path, FileAttributes Old)>> AttributesChanged,
    Func<string, Task> CopyText,
    Action<string, string>? CompareFiles = null);

internal sealed class PropertiesDialog : Window
{
    private const FileAttributes Simple = FileAttributes.ReadOnly | FileAttributes.Hidden | FileAttributes.Archive | FileAttributes.System;
    private const string CopyGlyph = "Icon.CopyText";

    private readonly string _folder;
    private readonly IReadOnlyList<string> _names;
    private readonly DisplayFormat _format;
    private readonly IShellAccess _shell;
    private readonly PropertiesActions _actions;
    private readonly CancellationTokenSource _cancel = new();
    private readonly StackPanel _tabs = new() { Orientation = Orientation.Horizontal, Spacing = 4, Margin = new Thickness(14, 0) };
    private readonly ContentControl _page = new() { MinHeight = 400 };
    private readonly Dictionary<string, Control> _pages = new(StringComparer.Ordinal);
    private readonly List<(FileAttributes Flag, CheckBox Box)> _attributeBoxes = [];
    private readonly Button _apply;
    private string _tab = "";
    private List<FileDetails> _details = [];

    public PropertiesDialog(string folder, IReadOnlyList<string> names, DisplayFormat format, IShellAccess shell, PropertiesActions actions, IconPixels? bigIcon)
    {
        _folder = folder;
        _names = names;
        _format = format;
        _shell = shell;
        _actions = actions;
        var title = names.Count switch
        {
            0 => WindowsPath.GetName(folder).TrimEnd('\\') is { Length: > 0 } n ? n : folder,
            1 => names[0],
            _ => Loc.Plural(names.Count, AppStrings.Count_Items_One, AppStrings.Count_Items_Other),
        };
        Title = Loc.Format(AppStrings.Props_Title, title);
        Width = 640;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        this.Res(BackgroundProperty, "Brush.PanelBackground");

        Header = BuildHeader(title, bigIcon);
        var paths = Paths;
        var single = paths.Count == 1 ? paths[0] : null;
        AddTab(AppStrings.Props_General, () => GeneralPage());
        if (single is not null && FileSignature.CanBeSigned(single))
        {
            AddTab(AppStrings.Props_Signature, () => SignaturePage(single));
        }

        if (single is not null)
        {
            AddTab(AppStrings.Props_Security, () => SecurityPage(single));
            AddTab(AppStrings.Props_PreviousVersions, () => VersionsPage(single));
        }

        AddLaterTab(AppStrings.Props_Details);
        AddLaterTab(AppStrings.Props_Checksums);

        var windows = new Button { Content = WithIcon("Icon.Windows", AppStrings.Props_WindowsProperties), Padding = new Thickness(12, 5) };
        ToolTip.SetTip(windows, AppStrings.Props_WindowsPropertiesTip);
        windows.Click += (_, _) => _ = _shell.ShowWindowsPropertiesAsync(Handle, _folder, _names);
        var ok = new Button { Content = AppStrings.Common_OK, MinWidth = 84, Padding = new Thickness(14, 5), HorizontalContentAlignment = HorizontalAlignment.Center };
        ok.Classes.Add("accent");
        ok.Click += (_, _) =>
        {
            ApplyAttributes();
            Close();
        };
        var cancel = new Button { Content = AppStrings.Common_Cancel, MinWidth = 84, Padding = new Thickness(14, 5), HorizontalContentAlignment = HorizontalAlignment.Center };
        cancel.Click += (_, _) => Close();
        _apply = new Button { Content = AppStrings.Props_Apply, MinWidth = 84, Padding = new Thickness(14, 5), HorizontalContentAlignment = HorizontalAlignment.Center, IsEnabled = false };
        _apply.Click += (_, _) => ApplyAttributes();
        var footerGrid = new DockPanel();
        DockPanel.SetDock(windows, Dock.Left);
        footerGrid.Children.Add(windows);
        footerGrid.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right, Children = { ok, cancel, _apply } });
        var footer = new Border { Padding = new Thickness(18, 11), BorderThickness = new Thickness(0, 1, 0, 0), Child = footerGrid }
            .Res(Border.BackgroundProperty, "Brush.WindowBackground").Res(Border.BorderBrushProperty, "Brush.Border");

        var tabLine = new Border { BorderThickness = new Thickness(0, 0, 0, 1), Child = _tabs }.Res(Border.BorderBrushProperty, "Brush.Border");
        var top = new StackPanel { Children = { Header, tabLine } };
        var root = new DockPanel();
        DockPanel.SetDock(footer, Dock.Bottom);
        DockPanel.SetDock(top, Dock.Top);
        root.Children.Add(footer);
        root.Children.Add(top);
        root.Children.Add(_page);
        Content = TitleBarParts.ForDialog(this, root);
        ShowTab(AppStrings.Props_General);
        Loading = LoadDetailsAsync();
        Opened += (_, _) => ok.Focus();
        Closed += (_, _) => _cancel.Cancel();
    }

    internal Task Loading { get; }

    internal Control Header { get; }

    internal string CurrentTab => _tab;

    internal IReadOnlyList<string> TabNames => [.. _tabs.Children.OfType<Button>().Where(b => b.IsEnabled).Select(b => (string)b.Tag!)];

    internal IReadOnlyList<CheckBox> AttributeBoxes => [.. _attributeBoxes.Select(a => a.Box)];

    internal Button ApplyButton => _apply;

    internal Task? PageLoading { get; private set; }

    private nint Handle => TryGetPlatformHandle()?.Handle ?? 0;

    private List<string> Paths => _names.Count == 0 ? [_folder] : [.. _names.Select(n => WindowsPath.Join(_folder, n))];

    internal void ShowTab(string name)
    {
        if (!_pages.TryGetValue(name, out var page))
        {
            if (_tabs.Children.OfType<Button>().FirstOrDefault(b => (string?)b.Tag == name)?.CommandParameter is Func<Control> build)
            {
                page = build();
                _pages[name] = page;
            }
            else
            {
                return;
            }
        }

        _tab = name;
        _page.Content = page;
        foreach (var b in _tabs.Children.OfType<Button>())
        {
            var on = (string?)b.Tag == name;
            b.Classes.Set("tab-on", on);
            b.FontWeight = on ? FontWeight.SemiBold : FontWeight.Normal;
            b.BorderThickness = new Thickness(0, 0, 0, on ? 2 : 0);
        }
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
            Close();
            e.Handled = true;
        }
        else if (e.Key == Key.Tab && (e.KeyModifiers & KeyModifiers.Control) != 0)
        {
            var names = TabNames.ToList();
            var i = names.IndexOf(_tab);
            var step = (e.KeyModifiers & KeyModifiers.Shift) != 0 ? names.Count - 1 : 1;
            ShowTab(names[(i + step) % names.Count]);
            e.Handled = true;
        }
    }

    private readonly Border _picture = new() { Width = 40, Height = 40 };

    internal void SetIcon(IconPixels icon) => _picture.Child = new Image { Source = Panels.IconCache.ToBitmap(icon), Width = 40, Height = 40 };

    private Control BuildHeader(string title, IconPixels? icon)
    {
        _picture.Child = AppIcons.Create(_names.Count == 1 ? "File.Properties" : "File.Duplicate", "Brush.SecondaryText");
        if (icon is not null)
        {
            SetIcon(icon);
        }

        Control picture = _picture;
        var name = OptionsUi.Text(title, 17, FontWeight.SemiBold, wrap: true);
        TypeLine = OptionsUi.Text("", 13, brush: "Brush.SecondaryText");
        return new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 12,
            Margin = new Thickness(18, 4, 18, 10),
            Children = { picture, new StackPanel { VerticalAlignment = VerticalAlignment.Center, MaxWidth = 540, Children = { name, TypeLine } } },
        };
    }

    private TextBlock TypeLine { get; set; } = null!;

    private void AddTab(string name, Func<Control> build)
    {
        var b = new Button
        {
            Content = name,
            Tag = name,
            CommandParameter = build,
            Padding = new Thickness(10, 6),
            Background = Brushes.Transparent,
            CornerRadius = default,
            Margin = new Thickness(0, 0, 0, -1),
        };
        b.Res(Button.BorderBrushProperty, "Brush.Accent");
        b.Click += (_, _) => ShowTab(name);
        _tabs.Children.Add(b);
    }

    private void AddLaterTab(string name)
    {
        var later = OptionsUi.Text(Loc.Format(AppStrings.Props_Later, Salandual.Core.Commands.CommandText.Milestone("Phase 4")), 11, brush: "Brush.TertiaryText");
        later.VerticalAlignment = VerticalAlignment.Center;
        _tabs.Children.Add(new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 4,
            Margin = new Thickness(10, 6),
            VerticalAlignment = VerticalAlignment.Center,
            Children = { OptionsUi.Text(name, 13, brush: "Brush.TertiaryText"), later },
        });
    }

    private static Grid Table(IEnumerable<(string? Key, Control? Value)> rows)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("130,*"), Margin = new Thickness(18, 12, 18, 14) };
        var r = 0;
        foreach (var (key, value) in rows)
        {
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            if (key is null && value is null)
            {
                var line = new Border { Height = 1, Margin = new Thickness(0, 6) }.Res(Border.BackgroundProperty, "Brush.Border");
                Grid.SetColumnSpan(line, 2);
                Grid.SetRow(line, r++);
                grid.Children.Add(line);
                continue;
            }

            if (key is null)
            {
                Grid.SetColumnSpan(value!, 2);
            }
            else
            {
                var k = OptionsUi.Text(key, 13, brush: "Brush.SecondaryText");
                k.VerticalAlignment = VerticalAlignment.Center;
                k.Margin = new Thickness(0, 4);
                Grid.SetRow(k, r);
                grid.Children.Add(k);
                Grid.SetColumn(value!, 1);
            }

            value!.VerticalAlignment = VerticalAlignment.Center;
            value.Margin = new Thickness(0, 4);
            Grid.SetRow(value, r++);
            grid.Children.Add(value);
        }

        return grid;
    }

    private static Control Value(string text, string? small = null, bool mono = false)
    {
        var t = OptionsUi.Text(text, 13, wrap: true);
        if (mono)
        {
            t.Res(TextBlock.FontFamilyProperty, "Font.Mono");
            t.FontSize = 12;
        }

        if (small is null)
        {
            return t;
        }

        return new WrapPanel { Children = { t, new TextBlock { Text = small, FontSize = 12, Margin = new Thickness(6, 1, 0, 0), VerticalAlignment = VerticalAlignment.Center }.Res(TextBlock.ForegroundProperty, "Brush.SecondaryText") } };
    }

    private static StackPanel WithIcon(string icon, string text)
    {
        var label = OptionsUi.Text(text, 13);
        label.VerticalAlignment = VerticalAlignment.Center;
        return new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Children = { AppIcons.Create(icon, "Brush.Text")!, label } };
    }

    private static Button SmallButton(string text, Action click, bool enabled = true)
    {
        var b = new Button { Content = text, Padding = new Thickness(8, 2), FontSize = 12, IsEnabled = enabled, VerticalAlignment = VerticalAlignment.Center };
        b.Click += (_, _) => click();
        return b;
    }

    private static Control Waiting(string text) => new StackPanel
    {
        Margin = new Thickness(18, 16),
        Children = { OptionsUi.Text(text, 13, brush: "Brush.SecondaryText") },
    };

    private static Border Status(string icon, string brush, string background, string title, string detail)
    {
        var heading = OptionsUi.Text(title, 13, FontWeight.SemiBold, wrap: true);
        var small = OptionsUi.Text(detail, 12, brush: "Brush.SecondaryText", wrap: true);
        var glyph = AppIcons.Create(icon, brush)!;
        glyph.VerticalAlignment = VerticalAlignment.Top;
        glyph.Margin = new Thickness(0, 1, 0, 0);
        return new Border
        {
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(12, 10),
            Child = new DockPanel { Children = { glyph, new StackPanel { Margin = new Thickness(10, 0, 0, 0), Children = { heading, small } } } },
        }.Res(Border.BackgroundProperty, background);
    }

    private string When(DateTime local) => string.Concat(_format.Date(local), " ", _format.TimeWithSeconds(local));

    private string Bytes(long bytes) => Loc.Format(AppStrings.Props_Bytes, bytes);

    private readonly ContentControl _general = new();

    private Control GeneralPage()
    {
        _general.Content = Waiting(AppStrings.Props_Reading);
        return _general;
    }

    private async Task LoadDetailsAsync()
    {
        var paths = Paths;
        try
        {
            _details = await Task.Run(() => paths.Select(FileDetailsReader.Read).ToList(), _cancel.Token);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _general.Content = Waiting(Loc.Format(AppStrings.Props_ReadFailed, ex.Message));
            return;
        }
        catch (OperationCanceledException)
        {
            return;
        }

        ShowGeneral();
    }

    private void ShowGeneral()
    {
        var rows = new List<(string?, Control?)>();
        var d = _details;
        if (d.Count == 1)
        {
            var one = d[0];
            var ext = Path.GetExtension(one.Path);
            TypeLine.Text = one.TypeName;
            rows.Add((AppStrings.Props_Type, Value(one.IsDirectory || ext.Length == 0 ? one.TypeName : Loc.Format(AppStrings.Props_TypeWithExtension, one.TypeName, ext))));
            if (one.Description is not null || one.Version is not null)
            {
                rows.Add((AppStrings.Props_Description, Value(string.Join(" · ", new[] { one.Description, one.Version is null ? null : Loc.Format(AppStrings.Props_Version, one.Version) }.Where(s => s is not null)))));
            }

            if (!one.IsDirectory && one.OpensWith is not null)
            {
                rows.Add((AppStrings.Props_OpensWith, new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 8,
                    Children = { OptionsUi.Text(one.OpensWith, 13), SmallButton(AppStrings.Props_Change, () => _shell.OpenWith(Handle, one.Path)) },
                }));
            }

            if (one.LinkTarget is not null)
            {
                rows.Add((AppStrings.Props_LinkTarget, Value(one.LinkTarget, mono: true)));
            }
        }
        else
        {
            var types = d.Select(x => x.TypeName).Distinct(StringComparer.CurrentCultureIgnoreCase).ToList();
            TypeLine.Text = types.Count == 1 ? Loc.Format(AppStrings.Props_AllOfType, types[0]) : AppStrings.Props_SeveralTypes;
        }

        rows.Add((AppStrings.Props_Location, LocationValue(d.Count == 1 ? WindowsPath.GetParent(d[0].Path) ?? d[0].Path : _folder)));
        var folders = d.Where(x => x.IsDirectory).ToList();
        var fileBytes = d.Where(x => !x.IsDirectory).Sum(x => x.Size);
        var fileOnDisk = d.Where(x => !x.IsDirectory).Sum(x => x.SizeOnDisk);
        var size = new LiveValue(_format.Size(fileBytes), Bytes(fileBytes));
        var onDisk = new LiveValue(_format.Size(fileOnDisk), Bytes(fileOnDisk));
        SizeValue = size;
        ContainsValue = null;
        rows.Add((AppStrings.Props_Size, size.Control));
        rows.Add((AppStrings.Props_SizeOnDisk, onDisk.Control));
        if (folders.Count > 0)
        {
            var contains = new LiveValue(AppStrings.Props_Counting);
            ContainsValue = contains;
            rows.Insert(rows.Count - 2, (AppStrings.Props_Contains, contains.Control));
            _ = CountAsync(folders.Select(f => f.Path).ToList(), d.Count(x => !x.IsDirectory), fileBytes, fileOnDisk, contains, size, onDisk);
        }

        rows.Add((null, null));
        if (d.Count == 1)
        {
            rows.Add((AppStrings.Props_Created, Value(When(d[0].Created))));
            rows.Add((AppStrings.Props_Modified, Value(When(d[0].Modified))));
            rows.Add((AppStrings.Props_Accessed, Value(When(d[0].Accessed))));
        }
        else if (d.Count > 1)
        {
            var oldest = d.Min(x => x.Modified);
            var newest = d.Max(x => x.Modified);
            rows.Add((AppStrings.Props_Modified, Value(oldest == newest ? When(oldest) : Loc.Format(AppStrings.Props_Range, When(oldest), When(newest)))));
        }

        rows.Add((null, null));
        rows.Add((AppStrings.Props_Attributes, AttributesRow()));
        if (d.Count == 1 && d[0].FromInternet)
        {
            var path = d[0].Path;
            Border? callout = null;
            var unblock = SmallButton(AppStrings.Props_Unblock, () =>
            {
                try
                {
                    FileDetailsReader.Unblock(path);
                    if (callout is not null)
                    {
                        callout.IsVisible = false;
                    }
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    ToolTip.SetTip(callout!, ex.Message);
                }
            });
            var text = OptionsUi.Text(AppStrings.Props_FromInternet, 13, wrap: true);
            text.VerticalAlignment = VerticalAlignment.Center;
            var dock = new DockPanel { Children = { unblock, text } };
            DockPanel.SetDock(unblock, Dock.Right);
            unblock.Margin = new Thickness(10, 0, 0, 0);
            callout = new Border { CornerRadius = new CornerRadius(4), Padding = new Thickness(12, 8), Child = dock, Margin = new Thickness(0, 6, 0, 0) }
                .Res(Border.BackgroundProperty, "Brush.WarningBackground");
            rows.Add((null, callout));
        }

        _general.Content = Table(rows);
    }

    private LiveValue? SizeValue { get; set; }

    private LiveValue? ContainsValue { get; set; }

    internal (string? Size, string? Contains) Shown => (SizeValue?.Text, ContainsValue?.Text);

    private Control LocationValue(string path)
    {
        var copy = new Button { Content = AppIcons.Glyph(CopyGlyph, 14, 14), Padding = new Thickness(4), Background = Brushes.Transparent, BorderThickness = default, VerticalAlignment = VerticalAlignment.Center };
        ToolTip.SetTip(copy, AppStrings.Props_CopyPath);
        Avalonia.Automation.AutomationProperties.SetName(copy, AppStrings.Props_CopyPath);
        copy.Click += (_, _) => _ = _actions.CopyText(path);
        var text = (TextBlock)Value(path, mono: true);
        text.TextWrapping = TextWrapping.NoWrap;
        text.TextTrimming = TextTrimming.PathSegmentEllipsis;
        text.MaxWidth = 420;
        text.VerticalAlignment = VerticalAlignment.Center;
        ToolTip.SetTip(text, path);
        return new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Children = { text, copy } };
    }

    private async Task CountAsync(List<string> folders, int files, long bytes, long onDisk, LiveValue contains, LiveValue size, LiveValue sizeOnDisk)
    {
        long f = files, dirs = 0, b = bytes, disk = onDisk;
        foreach (var folder in folders)
        {
            if (_names.Count > 1)
            {
                dirs++;
            }

            long baseFiles = f, baseDirs = dirs, baseBytes = b, baseDisk = disk;
            try
            {
                var last = await Task.Run(
                    () => FileDetailsReader.CountFolder(folder, c => Dispatcher.UIThread.Post(() => Show(baseFiles + c.Files, baseDirs + c.Folders, baseBytes + c.Bytes, baseDisk + c.BytesOnDisk, done: false)), _cancel.Token),
                    _cancel.Token);
                (f, dirs, b, disk) = (baseFiles + last.Files, baseDirs + last.Folders, baseBytes + last.Bytes, baseDisk + last.BytesOnDisk);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }

        Show(f, dirs, b, disk, done: true);
        CountDone = true;

        void Show(long files, long folders, long bytes, long disk, bool done)
        {
            if (_cancel.IsCancellationRequested)
            {
                return;
            }

            contains.Set(Loc.Format(AppStrings.Props_ContainsValue, Loc.Plural(files, Strings.Count_Files_One, Strings.Count_Files_Other), Loc.Plural(folders, Strings.Count_Folders_One, Strings.Count_Folders_Other)), done ? null : AppStrings.Props_CountingSmall);
            size.Set(_format.Size(bytes), Bytes(bytes));
            sizeOnDisk.Set(_format.Size(disk), Bytes(disk));
        }
    }

    internal bool CountDone { get; private set; }

    private sealed class LiveValue
    {
        private readonly TextBlock _main = OptionsUi.Text("", 13, wrap: true);
        private readonly TextBlock _small = new TextBlock { FontSize = 12, Margin = new Thickness(6, 1, 0, 0), VerticalAlignment = VerticalAlignment.Center }.Res(TextBlock.ForegroundProperty, "Brush.SecondaryText");

        public LiveValue(string text, string? small = null)
        {
            Control = new WrapPanel { Children = { _main, _small } };
            Set(text, small);
        }

        public Control Control { get; }

        public string Text => _main.Text ?? "";

        public void Set(string text, string? small)
        {
            _main.Text = text;
            _small.Text = small ?? "";
            _small.IsVisible = small is not null;
        }
    }

    private Control AttributesRow()
    {
        var panel = new WrapPanel();
        _attributeBoxes.Clear();
        foreach (var (flag, text) in new[] { (FileAttributes.ReadOnly, AppStrings.Props_ReadOnly), (FileAttributes.Hidden, AppStrings.Props_Hidden), (FileAttributes.Archive, AppStrings.Props_Archive), (FileAttributes.System, AppStrings.Props_System) })
        {
            var on = _details.Count(x => x.Attributes.HasFlag(flag));
            var box = new CheckBox
            {
                Content = text,
                IsThreeState = on > 0 && on < _details.Count,
                IsChecked = on == 0 ? false : on == _details.Count ? true : null,
                Margin = new Thickness(0, 0, 16, 0),
            };
            box.IsCheckedChanged += (_, _) =>
            {
                box.IsThreeState = false;
                _apply.IsEnabled = true;
            };
            _attributeBoxes.Add((flag, box));
            panel.Children.Add(box);
        }

        return panel;
    }

    internal void ApplyAttributes()
    {
        if (!_apply.IsEnabled)
        {
            return;
        }

        var changed = new List<(string Path, FileAttributes Old)>();
        foreach (var d in _details)
        {
            var next = d.Attributes;
            foreach (var (flag, box) in _attributeBoxes)
            {
                if (box.IsChecked is { } on)
                {
                    next = on ? next | flag : next & ~flag;
                }
            }

            if ((next & Simple) == (d.Attributes & Simple))
            {
                continue;
            }

            try
            {
                FileDetailsReader.SetSimpleAttributes(d.Path, next);
                changed.Add((d.Path, d.Attributes));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                ToolTip.SetTip(_apply, ex.Message);
            }
        }

        _details = [.. _details.Select(d => changed.Any(c => c.Path == d.Path) ? d with { Attributes = File.GetAttributes(WindowsPath.ToExtended(d.Path)) } : d)];
        _apply.IsEnabled = false;
        if (changed.Count > 0)
        {
            _actions.AttributesChanged(changed);
        }
    }

    private Control SignaturePage(string path)
    {
        var host = new ContentControl { Content = Waiting(AppStrings.Props_Checking) };
        PageLoading = LoadAsync();
        return host;

        async Task LoadAsync()
        {
            SignatureInfo s;
            try
            {
                s = await Task.Run(() => FileSignature.Check(path), _cancel.Token);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                host.Content = Waiting(Loc.Format(AppStrings.Props_ReadFailed, ex.Message));
                return;
            }
            catch (OperationCanceledException)
            {
                return;
            }

            var status = s.State switch
            {
                SignatureState.Valid => Status("Icon.Check", "Brush.Success", "Brush.SuccessBackground", AppStrings.Sig_Valid, s.ThroughCatalog ? AppStrings.Sig_ValidCatalog : AppStrings.Sig_ValidDetail),
                SignatureState.NotSigned => Status("Icon.Info", "Brush.Warning", "Brush.WarningBackground", AppStrings.Sig_NotSigned, AppStrings.Sig_NotSignedDetail),
                SignatureState.Changed => Status("Icon.Cross", "Brush.Danger", "Brush.DangerBackground", AppStrings.Sig_Changed, AppStrings.Sig_ChangedDetail),
                _ => Status("Icon.Info", "Brush.Danger", "Brush.DangerBackground", AppStrings.Sig_NotTrusted, Loc.Format(AppStrings.Sig_NotTrustedDetail, new System.ComponentModel.Win32Exception(s.ErrorCode).Message)),
            };
            var rows = new List<(string?, Control?)> { (null, status) };
            if (s.State != SignatureState.NotSigned)
            {
                rows.Add((AppStrings.Sig_SignedBy, Value(s.SignedBy ?? "—")));
                rows.Add((AppStrings.Sig_IssuedBy, Value(s.IssuedBy ?? "—")));
                rows.Add((AppStrings.Sig_SignedOn, s.SignedOn is { } on ? Value(When(on), AppStrings.Sig_TimeStamp) : Value(AppStrings.Sig_NoTimeStamp)));
                if (s.ValidFrom is { } from && s.ValidTo is { } to)
                {
                    rows.Add((AppStrings.Sig_Certificate, Value(Loc.Format(AppStrings.Sig_ValidRange, _format.Date(from), _format.Date(to)))));
                }

                if (s.Algorithm is not null)
                {
                    rows.Add((AppStrings.Sig_Algorithm, Value(s.Algorithm)));
                }

                if (s.ThroughCatalog)
                {
                    rows.Add((AppStrings.Sig_How, Value(AppStrings.Sig_Catalog)));
                }

                if (s.Certificate is { } cert)
                {
                    rows.Add((null, null));
                    rows.Add(("", SmallButton(AppStrings.Sig_ViewCertificate, () => _shell.ViewCertificate(Handle, cert))));
                }
            }

            host.Content = Table(rows);
        }
    }

    private Control SecurityPage(string path)
    {
        var host = new ContentControl { Content = Waiting(AppStrings.Props_Reading) };
        PageLoading = LoadAsync();
        return host;

        async Task LoadAsync()
        {
            AccessInfo a;
            try
            {
                a = await Task.Run(() => FileAccessReader.Read(path), _cancel.Token);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or SystemException and not OperationCanceledException)
            {
                host.Content = new StackPanel
                {
                    Children = { Waiting(Loc.Format(AppStrings.Sec_ReadFailed, ex.Message)), ChangeButton() },
                };
                return;
            }
            catch (OperationCanceledException)
            {
                return;
            }

            var may = new List<string>();
            if (a.CanRead)
            {
                may.Add(AppStrings.Sec_MayRead);
            }

            if (a.CanChange)
            {
                may.Add(AppStrings.Sec_MayChange);
            }

            if (a.CanDelete)
            {
                may.Add(AppStrings.Sec_MayDelete);
            }

            var isDir = Directory.Exists(WindowsPath.ToExtended(path));
            var youMay = may.Count == 0 ? AppStrings.Sec_MayNothing : Loc.Format(isDir ? AppStrings.Sec_YouMayFolder : AppStrings.Sec_YouMayFile, may.Count == 1 ? may[0] : Loc.Format(Strings.List_LastTwo, string.Join(Strings.List_Separator, may.Take(may.Count - 1)), may[^1]));
            var table = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*,Auto"), Margin = new Thickness(0, 6, 0, 0) };
            void Cell(Control c, int row, int col, bool head = false)
            {
                var b = new Border { Padding = new Thickness(10, 5), BorderThickness = new Thickness(0, row == 0 ? 0 : 1, 0, 0), Child = c };
                b.Res(Border.BorderBrushProperty, "Brush.Border");
                if (head)
                {
                    b.Res(Border.BackgroundProperty, "Brush.WindowBackground");
                }

                Grid.SetRow(b, row);
                Grid.SetColumn(b, col);
                table.Children.Add(b);
            }

            table.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            Cell(OptionsUi.Text(AppStrings.Sec_Who, 12, FontWeight.SemiBold, "Brush.SecondaryText"), 0, 0, head: true);
            Cell(OptionsUi.Text(AppStrings.Sec_May, 12, FontWeight.SemiBold, "Brush.SecondaryText"), 0, 1, head: true);
            Cell(new TextBlock(), 0, 2, head: true);
            var r = 1;
            foreach (var e in a.Entries)
            {
                table.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
                Cell(Value(e.Who, e.IsYou ? AppStrings.Sec_You : null), r, 0);
                Cell(OptionsUi.Text(e.Allowed ? FileAccessReader.LevelText(e.Level) : Loc.Format(AppStrings.Sec_Denied, FileAccessReader.LevelText(e.Level)), 13, brush: e.Allowed ? "Brush.Text" : "Brush.Danger"), r, 1);
                Cell(OptionsUi.Text(e.Inherited ? AppStrings.Sec_Inherited : AppStrings.Sec_SetHere, 11, brush: "Brush.TertiaryText"), r, 2);
                r++;
            }

            var frame = new Border { BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(4), Child = table, ClipToBounds = true }.Res(Border.BorderBrushProperty, "Brush.Border");
            host.Content = Table(
            [
                (AppStrings.Sec_Owner, Value(a.Owner ?? "—")),
                (AppStrings.Sec_YouMayLabel, Value(youMay)),
                (null, frame),
                (null, ChangeButton()),
            ]);
        }

        Control ChangeButton()
        {
            var b = new Button { Content = WithIcon("Icon.Windows", AppStrings.Sec_ChangePermissions), Padding = new Thickness(10, 4), Margin = new Thickness(0, 6, 0, 0) };
            ToolTip.SetTip(b, AppStrings.Sec_ChangePermissionsTip);
            b.Click += (_, _) => _ = _shell.ShowSecurityPageAsync(Handle, path);
            return b;
        }
    }

    private Control VersionsPage(string path)
    {
        var host = new ContentControl { Content = Waiting(AppStrings.Ver_Looking) };
        PageLoading = LoadAsync();
        return host;

        async Task LoadAsync()
        {
            IReadOnlyList<PreviousVersion>? versions;
            try
            {
                versions = await Task.Run(() => PreviousVersions.List(path, _cancel.Token), _cancel.Token);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                versions = null;
            }

            var note = OptionsUi.Text(AppStrings.Ver_Note, 12, brush: "Brush.SecondaryText", wrap: true);
            if (versions is null || versions.Count == 0)
            {
                var windows = new Button { Content = WithIcon("Icon.Windows", AppStrings.Props_WindowsProperties), Padding = new Thickness(10, 4), Margin = new Thickness(0, 8, 0, 0) };
                windows.Click += (_, _) => _ = _shell.ShowWindowsPropertiesAsync(Handle, _folder, _names);
                host.Content = new StackPanel
                {
                    Margin = new Thickness(18, 14),
                    Spacing = 6,
                    Children =
                    {
                        note,
                        OptionsUi.Text(versions is null ? AppStrings.Ver_CantList : AppStrings.Ver_None, 13, wrap: true),
                        windows,
                    },
                };
                return;
            }

            var list = new ListBox { MaxHeight = 260, SelectedIndex = 0 };
            foreach (var v in versions)
            {
                var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*,Auto") };
                var when = OptionsUi.Text(When(v.Taken), 13);
                var from = OptionsUi.Text(v.Path.StartsWith(@"\\localhost\", StringComparison.OrdinalIgnoreCase) ? AppStrings.Ver_RestorePoint : AppStrings.Ver_ServerCopy, 13);
                var size = OptionsUi.Text(Directory.Exists(v.Path) ? "" : _format.Size(v.Size), 13);
                Grid.SetColumn(from, 1);
                Grid.SetColumn(size, 2);
                row.Children.Add(when);
                row.Children.Add(from);
                row.Children.Add(size);
                list.Items.Add(new ListBoxItem { Content = row, Tag = v });
            }

            PreviousVersion? Selected() => (list.SelectedItem as ListBoxItem)?.Tag as PreviousVersion;
            var open = SmallButton(AppStrings.Ver_OpenInOther, () =>
            {
                if (Selected() is { } v)
                {
                    var isDir = Directory.Exists(v.Path);
                    _actions.OpenInOtherPanel(isDir ? v.Path : WindowsPath.GetParent(v.Path) ?? v.Path, isDir ? null : WindowsPath.GetName(v.Path));
                    Close();
                }
            });
            var compare = SmallButton(AppStrings.Ver_Compare, () =>
            {
                if (Selected() is { } v && File.Exists(v.Path) && File.Exists(path))
                {
                    _actions.CompareFiles?.Invoke(v.Path, path);
                }
            });
            var restore = SmallButton(AppStrings.Ver_Restore, () =>
            {
                if (Selected() is { } v)
                {
                    _actions.Restore(v, WindowsPath.GetParent(path) ?? _folder);
                    Close();
                }
            });
            host.Content = new StackPanel
            {
                Margin = new Thickness(18, 14),
                Spacing = 8,
                Children = { note, list, new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { open, compare, restore } } },
            };
        }
    }
}

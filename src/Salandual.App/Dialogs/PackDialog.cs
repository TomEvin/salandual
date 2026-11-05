// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.App/Dialogs/PackDialog.cs

using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Salandual.App.Controls;
using Salandual.App.Options;
using Salandual.App.Resources;
using Salandual.Archives;
using Salandual.Core.Listing;
using Salandual.Core.Operations;
using Salandual.Core.Resources;
using Salandual.Core.Settings;
using Salandual.Platform.Windows.FileSystem;
using Path = System.IO.Path;

namespace Salandual.App.Dialogs;

internal sealed record PackContext(string Heading, string Facts, string Target, string WhereNote, long Size, OperationSettings Settings);

internal sealed record PackDialogResult(string Target, PackSettings Settings, bool ToQueue, bool DeleteAfter);

internal sealed class PackDialog : Window
{
    private static readonly PackFormat[] _formats = [PackFormat.Zip, PackFormat.SevenZip, PackFormat.Tar, PackFormat.TarGz, PackFormat.TarXz];
    private static readonly long[] _volumeSizes = [0, 100L << 20, 700L << 20, 4_000_000_000L, -1];

    private readonly PackContext _context;
    private readonly IOperationFileSystem _fs;
    private readonly DisplayFormat _display;
    private readonly Action<OperationSettings> _saveDefaults;
    private readonly TextBox _to, _password, _ownSize;
    private readonly TextBlock _whereNote = OptionsUi.Text("", 12, brush: "Brush.TertiaryText", wrap: true);
    private readonly TextBlock _passwordNote = OptionsUi.Text("", 12, brush: "Brush.TertiaryText", wrap: true);
    private readonly TextBlock _check = OptionsUi.Text("", 12, brush: "Brush.SecondaryText", wrap: true);
    private readonly TextBlock _mb = OptionsUi.Text(AppStrings.Pack_MB, 12, brush: "Brush.TertiaryText");
    private readonly Ellipse _checkDot = new() { Width = 8, Height = 8, Margin = new Thickness(0, 5, 0, 0), VerticalAlignment = VerticalAlignment.Top };
    private readonly List<ToggleButton> _formatButtons = [];
    private readonly ComboBox _level, _volumes;
    private readonly CheckBox _storeCompressed, _hideNames, _delete;
    private readonly StackPanel _advanced;
    private readonly TextBlock _advancedHead = new() { FontSize = 13 };
    private readonly TextBlock _summary = OptionsUi.Text("", 12, brush: "Brush.SecondaryText", wrap: true);
    private readonly Button _go, _queue;
    private PackFormat _format;

    public PackDialog(PackContext context, IOperationFileSystem fs, DisplayFormat display, Action<OperationSettings> saveDefaults)
    {
        _context = context;
        _fs = fs;
        _display = display;
        _saveDefaults = saveDefaults;
        var s = context.Settings;
        _format = s.FirstPackFormat();
        Title = context.Heading;
        Width = 600;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        this.Res(BackgroundProperty, "Brush.PanelBackground");

        var body = new StackPanel { Spacing = 10, Margin = new Thickness(20, 8, 20, 16) };
        body.Children.Add(OptionsUi.Text(context.Facts, 13, brush: "Brush.SecondaryText", wrap: true));

        _to = new TextBox { MinHeight = 32, VerticalContentAlignment = VerticalAlignment.Center, Text = WithExtension(context.Target, _format) };
        _to.Res(TextBox.FontFamilyProperty, "Font.Mono");
        Avalonia.Automation.AutomationProperties.SetName(_to, AppStrings.Pack_Archive);
        _to.TextChanged += (_, _) => Update();
        var browse = new Button { Content = AppStrings.Common_Browse, MinHeight = 32, Padding = new Thickness(12, 0), VerticalContentAlignment = VerticalAlignment.Center, Focusable = false };
        browse.Click += async (_, _) => await BrowseAsync();
        var toRow = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 6 };
        toRow.Children.Add(_to);
        Grid.SetColumn(browse, 1);
        toRow.Children.Add(browse);
        _whereNote.Text = context.WhereNote;
        body.Children.Add(new StackPanel { Spacing = 5, Children = { OptionsUi.Text(AppStrings.Pack_Archive, 13, FontWeight.SemiBold), toRow, _whereNote } });

        var formats = new StackPanel { Orientation = Orientation.Horizontal };
        for (var i = 0; i < _formats.Length; i++)
        {
            var format = _formats[i];
            var button = new ToggleButton
            {
                Content = FormatLabel(format),
                IsChecked = format == _format,
                CornerRadius = i == 0 ? new CornerRadius(4, 0, 0, 4) : i == _formats.Length - 1 ? new CornerRadius(0, 4, 4, 0) : new CornerRadius(0),
                Padding = new Thickness(12, 5),
                Margin = new Thickness(i == 0 ? 0 : -1, 0, 0, 0),
                MinWidth = 56,
                HorizontalContentAlignment = HorizontalAlignment.Center,
            };
            button.Click += (_, _) => SetFormat(format);
            _formatButtons.Add(button);
            formats.Children.Add(button);
        }

        body.Children.Add(LabelRow(AppStrings.Pack_Format, formats));
        _level = OptionsUi.Combo([(PackLevel.Store, AppStrings.Pack_Level_Store), (PackLevel.Fast, AppStrings.Pack_Level_Fast), (PackLevel.Normal, AppStrings.Pack_Level_Normal), (PackLevel.Maximum, AppStrings.Pack_Level_Maximum)],
            s.PackLevel, _ => Update(), minWidth: 200);
        body.Children.Add(LabelRow(AppStrings.Pack_Compression, _level));
        _password = new TextBox { PasswordChar = '●', MinHeight = 30, Width = 260, VerticalContentAlignment = VerticalAlignment.Center, Classes = { "revealPasswordButton" }, PlaceholderText = AppStrings.Pack_PasswordNone };
        Avalonia.Automation.AutomationProperties.SetName(_password, AppStrings.Pack_Password);
        _password.TextChanged += (_, _) => Update();
        body.Children.Add(LabelRow(AppStrings.Pack_Password, _password));
        _passwordNote.Margin = new Thickness(170, -4, 0, 0);
        body.Children.Add(_passwordNote);

        _volumes = OptionsUi.Combo([(0L, AppStrings.Pack_Volumes_None), (100L << 20, AppStrings.Pack_Volumes_100), (700L << 20, AppStrings.Pack_Volumes_700), (4_000_000_000L, AppStrings.Pack_Volumes_4G), (-1L, AppStrings.Pack_Volumes_Own)],
            0L, _ => Update(), minWidth: 200);
        _ownSize = new TextBox { Width = 90, MinHeight = 28, HorizontalContentAlignment = HorizontalAlignment.Right, Text = 1024.ToString("N0", CultureInfo.CurrentCulture) };
        Avalonia.Automation.AutomationProperties.SetName(_ownSize, AppStrings.Pack_Volumes_OwnName);
        _ownSize.TextChanged += (_, _) => Update();
        _storeCompressed = Check(AppStrings.Pack_StoreCompressed, s.PackStoreCompressed, AppStrings.Pack_StoreCompressedNote);
        _hideNames = Check(AppStrings.Pack_HideNames, false, AppStrings.Pack_HideNamesNote);
        _delete = Check(AppStrings.Pack_DeleteFiles, false, AppStrings.Pack_DeleteFilesNote);
        foreach (var box in new[] { _storeCompressed, _hideNames, _delete })
        {
            box.IsCheckedChanged += (_, _) => Update();
        }

        _advanced = new StackPanel { Spacing = 6, Margin = new Thickness(18, 4, 0, 0), IsVisible = false };
        _advanced.Children.Add(Head(AppStrings.Pack_VolumesHead));
        _advanced.Children.Add(new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 10,
            Children = { OptionsUi.Text(AppStrings.Pack_SplitInto, 13).Also(t => { t.Width = 140; t.VerticalAlignment = VerticalAlignment.Center; }), _volumes, _ownSize, _mb.Also(t => t.VerticalAlignment = VerticalAlignment.Center) },
        });
        _advanced.Children.Add(Head(AppStrings.Pack_CompressionHead).Also(h => h.Margin = new Thickness(0, 8, 0, 0)));
        _advanced.Children.Add(_storeCompressed);
        _advanced.Children.Add(_hideNames);
        _advanced.Children.Add(Head(AppStrings.Unpack_AfterwardsHead).Also(h => h.Margin = new Thickness(0, 8, 0, 0)));
        _advanced.Children.Add(_delete);

        var disclose = new Button { Padding = new Thickness(0, 2), Background = Brushes.Transparent, BorderThickness = new Thickness(0), HorizontalAlignment = HorizontalAlignment.Left, Content = _advancedHead };
        disclose.Click += (_, _) => ToggleAdvanced();
        _summary.Margin = new Thickness(18, 0, 0, 0);
        _summary.Cursor = new Cursor(StandardCursorType.Hand);
        _summary.PointerPressed += (_, e) =>
        {
            ToggleAdvanced();
            e.Handled = true;
        };
        _advancedHead.Res(TextBlock.ForegroundProperty, "Brush.Text");
        AdvancedButton = disclose;
        body.Children.Add(new Border { Height = 1, Margin = new Thickness(0, 4, 0, 0) }.Res(Border.BackgroundProperty, "Brush.Border"));
        body.Children.Add(new StackPanel { Spacing = 2, Children = { disclose, _summary, _advanced } });

        body.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { _checkDot, _check } }.Also(_ => _check.MaxWidth = 520));

        var save = Operations.Ui.Link(AppStrings.Copy_SaveDefaults, () =>
        {
            _saveDefaults(CurrentSettings());
            SavedDefaults = true;
        });
        var cancel = new Button { Content = AppStrings.Common_Cancel, MinWidth = 84, Padding = new Thickness(14, 5), HorizontalContentAlignment = HorizontalAlignment.Center };
        cancel.Click += (_, _) => Close(null);
        var queueFirst = s.Enter == EnterAction.AddToQueue;
        _queue = new Button { Content = queueFirst ? AppStrings.Copy_StartNowShift : AppStrings.Copy_AddToQueueShift, Padding = new Thickness(14, 5) };
        _queue.Click += (_, _) => Accept(toQueue: !queueFirst);
        _go = new Button { Content = queueFirst ? AppStrings.Copy_AddToQueueEnter : Loc.Format(AppStrings.Copy_GoEnter, AppStrings.Pack_Title), Padding = new Thickness(14, 5), MinWidth = 96 };
        _go.Classes.Add("accent");
        _go.Click += (_, _) => Accept(toQueue: queueFirst);
        var right = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { cancel, _queue, _go } };
        var footerRow = new DockPanel();
        DockPanel.SetDock(right, Dock.Right);
        footerRow.Children.Add(right);
        footerRow.Children.Add(save);
        var footer = new Border { Padding = new Thickness(20, 12), BorderThickness = new Thickness(0, 1, 0, 0), Child = footerRow }
            .Res(Border.BackgroundProperty, "Brush.WindowBackground").Res(Border.BorderBrushProperty, "Brush.Border");

        var root = new DockPanel();
        DockPanel.SetDock(footer, Dock.Bottom);
        root.Children.Add(footer);
        root.Children.Add(body);
        Content = TitleBarParts.ForDialog(this, root);
        Update();
        Opened += (_, _) =>
        {
            _to.Focus();
            var text = _to.Text ?? "";
            var name = Path.GetFileName(text);
            var stem = name.Length - ArchiveWriter.Extension(_format).Length;
            _to.SelectionStart = text.Length - name.Length;
            _to.SelectionEnd = text.Length - name.Length + Math.Max(0, stem);
        };
    }

    internal static string FormatLabel(PackFormat format) => format == PackFormat.SevenZip ? "7z" : ArchiveWriter.Extension(format).TrimStart('.').ToUpperInvariant();

    internal TextBox To => _to;

    internal TextBox Password => _password;

    internal Button Go => _go;

    internal Button AdvancedButton { get; }

    internal bool AdvancedOpen => _advanced.IsVisible;

    internal string Summary => _summary.Text ?? "";

    internal string CheckText => _check.Text ?? "";

    internal string PasswordNote => _passwordNote.Text ?? "";

    internal string WhereNote => _whereNote.Text ?? "";

    internal PackFormat Format => _format;

    internal ComboBox Level => _level;

    internal ComboBox Volumes => _volumes;

    internal TextBox OwnSize => _ownSize;

    internal CheckBox HideNamesBox => _hideNames;

    internal CheckBox StoreCompressedBox => _storeCompressed;

    internal CheckBox DeleteBox => _delete;

    internal bool SavedDefaults { get; private set; }

    internal void SetFormat(PackFormat format)
    {
        _format = format;
        for (var i = 0; i < _formats.Length; i++)
        {
            _formatButtons[i].IsChecked = _formats[i] == format;
        }

        var text = _to.Text ?? "";
        _to.Text = WithExtension(text, format);
        _to.CaretIndex = _to.Text.Length;
        Update();
    }

    internal static string WithExtension(string path, PackFormat format)
    {
        if (path.Trim().Length == 0)
        {
            return path;
        }

        foreach (var known in new[] { ".tar.gz", ".tar.xz", ".tgz", ".txz", ".zip", ".7z", ".tar" })
        {
            if (path.EndsWith(known, StringComparison.OrdinalIgnoreCase))
            {
                return path[..^known.Length] + ArchiveWriter.Extension(format);
            }
        }

        return path + ArchiveWriter.Extension(format);
    }

    private void ToggleAdvanced()
    {
        _advanced.IsVisible = !_advanced.IsVisible;
        Update();
    }

    private PackLevel Level_ => _level.SelectedIndex switch
    {
        0 => PackLevel.Store,
        1 => PackLevel.Fast,
        3 => PackLevel.Maximum,
        _ => PackLevel.Normal,
    };

    private bool CanEncrypt => _format is PackFormat.Zip or PackFormat.SevenZip;

    private string? PasswordGiven => CanEncrypt && !string.IsNullOrEmpty(_password.Text) ? _password.Text : null;

    private long? VolumeSize
    {
        get
        {
            var chosen = _volumes.SelectedIndex >= 0 ? _volumeSizes[_volumes.SelectedIndex] : 0;
            if (chosen >= 0)
            {
                return chosen;
            }

            return double.TryParse(_ownSize.Text, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.CurrentCulture, out var mb) && mb >= 0.1 && mb <= 1_000_000
                ? (long)(mb * 1024 * 1024)
                : null;
        }
    }

    private OperationSettings CurrentSettings() => _context.Settings with
    {
        PackFirstFormat = _context.Settings.PackFirstFormat == PackFirstFormat.LastUsed ? PackFirstFormat.LastUsed : (PackFirstFormat)(int)_format,
        PackLastFormat = _format,
        PackLevel = Level_,
        PackStoreCompressed = _storeCompressed.IsChecked == true,
    };

    private string? FullTarget()
    {
        var text = (_to.Text ?? "").Trim();
        if (text.Length == 0)
        {
            return null;
        }

        var folder = Path.GetDirectoryName(_context.Target) ?? "";
        var full = PathInput.Resolve(text, folder);
        return full is null || full.EndsWith('\\') ? null : full;
    }

    private void Update()
    {
        _password.IsEnabled = CanEncrypt;
        _passwordNote.Text = !CanEncrypt ? AppStrings.Pack_PasswordTar : _format == PackFormat.SevenZip ? AppStrings.Pack_PasswordNote7z : AppStrings.Pack_PasswordNoteZip;
        _hideNames.IsEnabled = _format == PackFormat.SevenZip && PasswordGiven is not null;
        _storeCompressed.IsEnabled = _format is PackFormat.Zip or PackFormat.SevenZip;
        _ownSize.IsVisible = _mb.IsVisible = _volumes.SelectedIndex == _volumeSizes.Length - 1;

        _advancedHead.Inlines!.Clear();
        _advancedHead.Inlines.Add(new Run((_advanced.IsVisible ? "▾  " : "▸  ") + AppStrings.Unpack_Advanced) { FontWeight = FontWeight.SemiBold });
        _summary.IsVisible = !_advanced.IsVisible;
        _summary.Text = SummaryText();

        var target = FullTarget();
        var folder = target is null ? null : Path.GetDirectoryName(target);
        var folderThere = folder is not null && _fs.Stat(folder) is { IsDirectory: true };
        var free = folderThere ? _fs.Describe(folder!).FreeBytes : null;
        var drive = target is null ? "" : (WindowsPath.GetRoot(target) ?? target).TrimEnd('\\');
        var fits = free is not { } f || _context.Size <= f;
        var ok = target is not null && folderThere && VolumeSize is not null;
        _checkDot.Res(Shape.FillProperty, ok && fits ? "Brush.Success" : "Brush.Warning");
        _check.Text = target is null ? AppStrings.Pack_TypeName
            : !folderThere ? Loc.Format(AppStrings.Pack_NoFolder, folder ?? "")
            : VolumeSize is null ? AppStrings.Pack_OwnSizeWrong
            : _context.Size < 0 ? free is { } space ? Loc.Format(AppStrings.Pack_Unmeasured, _display.Size(space), drive) : AppStrings.Pack_UnmeasuredNoFree
            : free is { } room ? Loc.Format(fits ? AppStrings.Pack_Fits : AppStrings.Pack_DoesNotFit, _display.Size(_context.Size), _display.Size(room), drive)
            : Loc.Format(AppStrings.Pack_AtMost, _display.Size(_context.Size));
        _go.IsEnabled = _queue.IsEnabled = ok;
    }

    private string SummaryText()
    {
        var parts = new List<string>
        {
            VolumeSize is > 0 and var size ? Loc.Format(AppStrings.Pack_Sum_Parts, _display.Size(size)) : AppStrings.Pack_Sum_One,
        };
        if (_format is PackFormat.Zip or PackFormat.SevenZip)
        {
            parts.Add(_storeCompressed.IsChecked == true ? AppStrings.Pack_Sum_Stored : AppStrings.Pack_Sum_PackedToo);
        }

        if (_hideNames.IsEnabled && _hideNames.IsChecked == true)
        {
            parts.Add(AppStrings.Pack_Sum_Hidden);
        }

        parts.Add(_delete.IsChecked == true ? AppStrings.Pack_Sum_Delete : AppStrings.Pack_Sum_Kept);
        return string.Join(" · ", parts);
    }

    private async Task BrowseAsync()
    {
        var path = FullTarget() ?? _context.Target;
        var folder = Path.GetDirectoryName(path);
        var start = folder is not null && Directory.Exists(folder) ? await StorageProvider.TryGetFolderFromPathAsync(folder) : null;
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions { Title = AppStrings.Pack_BrowseTitle, SuggestedStartLocation = start, SuggestedFileName = Path.GetFileName(path), ShowOverwritePrompt = false });
        if (file?.TryGetLocalPath() is { } chosen)
        {
            _to.Text = WithExtension(chosen, _format);
            _to.CaretIndex = _to.Text.Length;
        }
    }

    internal void Accept(bool toQueue)
    {
        Update();
        if (!_go.IsEnabled || FullTarget() is not { } target || VolumeSize is not { } volume)
        {
            return;
        }

        var settings = new PackSettings(_format, Level_, PasswordGiven, _hideNames.IsEnabled && _hideNames.IsChecked == true, volume, _storeCompressed.IsChecked == true);
        Close(new PackDialogResult(target, settings, toQueue, _delete.IsChecked == true));
    }

    internal void OpenAdvanced()
    {
        if (!_advanced.IsVisible)
        {
            ToggleAdvanced();
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

    private static Control LabelRow(string label, Control control)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("160,*"), ColumnSpacing = 10 };
        var text = OptionsUi.Text(label, 13, brush: "Brush.SecondaryText");
        text.VerticalAlignment = VerticalAlignment.Center;
        grid.Children.Add(text);
        Grid.SetColumn(control, 1);
        control.HorizontalAlignment = HorizontalAlignment.Left;
        grid.Children.Add(control);
        return grid;
    }

    private static TextBlock Head(string text) => OptionsUi.Text(text, 12, FontWeight.SemiBold, "Brush.SecondaryText");

    private static CheckBox Check(string text, bool on, string note)
        => new()
        {
            Content = new StackPanel { Spacing = 1, Children = { OptionsUi.Text(text, 13), OptionsUi.Text(note, 12, brush: "Brush.TertiaryText", wrap: true).Also(t => t.MaxWidth = 460) } },
            IsChecked = on,
            FontSize = 13,
            MinHeight = 26,
        };
}

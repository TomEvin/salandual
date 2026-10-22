// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.App/Dialogs/DeleteDialog.cs

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Salandual.App.Controls;
using Salandual.App.Options;
using Salandual.App.Resources;
using Salandual.App.Theme;
using Salandual.Core.Listing;
using Salandual.Core.Operations;
using Salandual.Core.Resources;
using Salandual.Core.Settings;

namespace Salandual.App.Dialogs;

public sealed record DeleteRequest(bool Recycle, bool? RefusedPermanently, bool ToQueue);

public sealed class DeleteDialog : Window
{
    private const string BinGlyph = "Icon.Bin";
    private readonly IReadOnlyList<OperationSource> _sources;
    private readonly IReadOnlyDictionary<string, long> _sizes;
    private readonly IOperationFileSystem _fs;
    private readonly DisplayFormat _format;
    private readonly string _folder;
    private readonly OperationSettings _settings;
    private readonly List<(string Path, string Reason)> _refused = [];
    private readonly StackPanel _body = new() { Spacing = 14, Margin = new Thickness(20, 6, 20, 16) };
    private readonly Button _go, _queue;
    private readonly CancellationTokenSource _counting = new();
    private bool _permanent;
    private bool _refusedPermanently = true;
    private (int Files, int Folders, long Bytes, bool Complete)? _totals;
    private readonly Dictionary<string, (int Files, long Bytes)> _perFolder = new(StringComparer.OrdinalIgnoreCase);

    public DeleteDialog(IReadOnlyList<OperationSource> sources, IReadOnlyDictionary<string, long> sizes, string folder, bool permanent, IOperationFileSystem fs, DisplayFormat format, OperationSettings settings)
    {
        _sources = sources;
        _sizes = sizes;
        _fs = fs;
        _format = format;
        _folder = folder;
        _settings = settings;
        _permanent = permanent;
        Title = AppStrings.Delete_Title;
        Width = 560;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        this.Res(BackgroundProperty, "Brush.PanelBackground");

        foreach (var source in sources)
        {
            if (!fs.CanRecycle(source.Path, out var reason))
            {
                _refused.Add((source.Path, reason));
            }
        }

        var cancel = new Button { Content = AppStrings.Common_Cancel, MinWidth = 84, Padding = new Thickness(14, 5), HorizontalContentAlignment = HorizontalAlignment.Center };
        cancel.Click += (_, _) => Close(null);
        var queueFirst = settings.Enter == EnterAction.AddToQueue;
        _queue = new Button { Content = queueFirst ? AppStrings.Copy_StartNowShift : AppStrings.Copy_AddToQueueShift, Padding = new Thickness(14, 5) };
        _queue.Click += (_, _) => Accept(toQueue: !queueFirst);
        _go = new Button { Padding = new Thickness(14, 5), MinWidth = 96 };
        _go.Click += (_, _) => Accept(toQueue: queueFirst);
        var footer = new Border
        {
            Padding = new Thickness(20, 12),
            BorderThickness = new Thickness(0, 1, 0, 0),
            Child = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right, Children = { cancel, _queue, _go } },
        }.Res(Border.BackgroundProperty, "Brush.WindowBackground").Res(Border.BorderBrushProperty, "Brush.Border");

        var root = new DockPanel();
        DockPanel.SetDock(footer, Dock.Bottom);
        root.Children.Add(footer);
        root.Children.Add(_body);
        Content = TitleBarParts.ForDialog(this, root);
        Render();
        Counting = CountAsync();
        Opened += (_, _) => _go.Focus();
        Closed += (_, _) => _counting.Cancel();
    }

    internal bool Permanent => _permanent;

    internal Button Go => _go;

    internal Task Counting { get; }

    internal IReadOnlyList<(string Path, string Reason)> Refused => _refused;

    internal void SetPermanent(bool permanent)
    {
        _permanent = permanent;
        Render();
    }

    internal void SetRefusedPermanently(bool permanently)
    {
        _refusedPermanently = permanently;
        Render();
    }

    private async Task CountAsync()
    {
        var token = _counting.Token;
        var sources = _sources;
        var fs = _fs;
        var perFolder = new Dictionary<string, (int, long)>(StringComparer.OrdinalIgnoreCase);
        var result = await Task.Run(() =>
        {
            int files = 0, folders = 0;
            long bytes = 0;
            var complete = true;
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
            timeout.CancelAfter(TimeSpan.FromSeconds(1.5));
            try
            {
                foreach (var source in sources)
                {
                    if (fs.Stat(source.Path) is not { } info)
                    {
                        continue;
                    }

                    if (!info.IsDirectory)
                    {
                        files++;
                        bytes += info.Size;
                        continue;
                    }

                    folders++;
                    if (info.IsLink)
                    {
                        continue;
                    }

                    int f = 0;
                    long b = 0;
                    var stack = new Stack<string>([source.Path]);
                    while (stack.Count > 0)
                    {
                        timeout.Token.ThrowIfCancellationRequested();
                        var dir = stack.Pop();
                        foreach (var child in fs.List(dir, timeout.Token))
                        {
                            if (child.IsDirectory)
                            {
                                folders++;
                                if (!child.IsLink)
                                {
                                    stack.Push(fs.Combine(dir, child.Name));
                                }
                            }
                            else
                            {
                                f++;
                                b += child.Size;
                            }
                        }
                    }

                    files += f;
                    bytes += b;
                    perFolder[source.Path] = (f, b);
                }
            }
            catch (Exception ex) when (ex is OperationCanceledException or IOException or UnauthorizedAccessException)
            {
                complete = false;
            }

            return (files, folders, bytes, complete);
        });
        if (token.IsCancellationRequested)
        {
            return;
        }

        foreach (var (path, value) in perFolder)
        {
            _perFolder[path] = value;
        }

        _totals = result;
        Render();
    }

    private void Render()
    {
        _body.Children.Clear();
        var showRefused = !_permanent && _refused.Count > 0;

        var icon = new Border
        {
            Width = 40,
            Height = 40,
            CornerRadius = new CornerRadius(8),
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 0, 14, 0),
            Child = AppIcons.Glyph(BinGlyph, 24, 24).Also(g => g.HorizontalAlignment = HorizontalAlignment.Center),
        }.Res(Border.BackgroundProperty, _permanent ? "Brush.DangerBackground" : "Brush.Hover");
        var table = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), Margin = new Thickness(0, 6, 0, 0) };
        var rows = new List<(string, string)> { (AppStrings.Delete_Selected, FileOperations.Count(_sources)) };
        if (_sources.Any(s => s.IsDirectory))
        {
            rows.Add((AppStrings.Delete_InAll, _totals is { } t ? Loc.Format(t.Complete ? AppStrings.Delete_FilesInFolders : AppStrings.Delete_AtLeastFilesInFolders, Loc.Plural(t.Files, Strings.Count_Files_One, AppStrings.Count_Files_N0), Loc.Plural(t.Folders, Strings.Count_Folders_One, AppStrings.Count_Folders_N0)) : AppStrings.Delete_Counting));
        }

        rows.Add((AppStrings.Panel_Col_Size, _totals is { } s ? s.Complete ? _format.Size(s.Bytes) : Loc.Format(AppStrings.Delete_AtLeast, _format.Size(s.Bytes)) : AppStrings.Delete_Counting));
        rows.Add((AppStrings.Delete_From, _folder));
        for (var i = 0; i < rows.Count; i++)
        {
            table.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            var label = OptionsUi.Text(rows[i].Item1, 13, brush: "Brush.SecondaryText");
            label.Margin = new Thickness(0, 1, 16, 1);
            var value = OptionsUi.Text(rows[i].Item2, 13, wrap: true);
            Grid.SetRow(label, i);
            Grid.SetRow(value, i);
            Grid.SetColumn(value, 1);
            table.Children.Add(label);
            table.Children.Add(value);
        }

        var heading = OptionsUi.Text(_permanent ? AppStrings.Delete_PermanentlyQ : AppStrings.Delete_ToBinQ, 18, FontWeight.SemiBold);
        var summary = new DockPanel();
        DockPanel.SetDock(icon, Dock.Left);
        summary.Children.Add(icon);
        summary.Children.Add(new StackPanel { Children = { heading, table } });
        _body.Children.Add(summary);

        var list = new StackPanel { Margin = new Thickness(0, 3) };
        foreach (var source in _sources.Take(8))
        {
            var name = Path.GetFileName(source.Path);
            var refused = showRefused && _refused.Any(r => r.Path == source.Path);
            var size = source.IsDirectory
                ? _perFolder.TryGetValue(source.Path, out var f) ? Loc.Plural(f.Files, Strings.Count_Files_One, AppStrings.Count_Files_N0) + " · " + _format.Size(f.Bytes) : ""
                : _sizes.TryGetValue(source.Path, out var known) ? _format.Size(known) : "";
            var row = new DockPanel { Height = 24, Margin = new Thickness(10, 0) };
            var sizeText = OptionsUi.Text(size, 12, brush: "Brush.SecondaryText");
            DockPanel.SetDock(sizeText, Dock.Right);
            row.Children.Add(sizeText);
            row.Children.Add(OptionsUi.Text((source.IsDirectory ? "📁 " : "") + name, 13, brush: refused ? "Brush.Danger" : "Brush.Text").Also(t => t.TextTrimming = TextTrimming.CharacterEllipsis));
            list.Children.Add(row);
        }

        if (_sources.Count > 8)
        {
            list.Children.Add(OptionsUi.Text(Loc.Format(AppStrings.Common_AndMore, _sources.Count - 8), 12, brush: "Brush.TertiaryText").Also(t => t.Margin = new Thickness(10, 2)));
        }

        _body.Children.Add(new Border { BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(4), MaxHeight = 220, Child = new ScrollViewer { Content = list } }.Res(Border.BorderBrushProperty, "Brush.Border"));

        if (showRefused)
        {
            var first = _refused[0];
            var text = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 13 };
            text.Res(TextBlock.ForegroundProperty, "Brush.Danger");
            var one = _refused.Count == 1;
            Rich.Add(text.Inlines!, AppStrings.Delete_CantRecycle, null,
                new Run(one ? Path.GetFileName(first.Path) : Loc.Plural(_refused.Count, AppStrings.Count_Items_One, AppStrings.Count_Items_Other)) { FontWeight = FontWeight.SemiBold },
                new Run(first.Reason));
            var delete = new RadioButton { Content = one ? AppStrings.Delete_ItPermanently : AppStrings.Delete_ThemPermanently, GroupName = "refused", IsChecked = _refusedPermanently };
            var leave = new RadioButton { Content = one ? AppStrings.Ops_LeaveIt : AppStrings.Ops_LeaveThem, GroupName = "refused", IsChecked = !_refusedPermanently };
            delete.IsCheckedChanged += (_, _) => _refusedPermanently = delete.IsChecked == true;
            _body.Children.Add(new Border
            {
                Padding = new Thickness(12, 9),
                CornerRadius = new CornerRadius(4),
                Child = new StackPanel { Spacing = 6, Children = { text, new StackPanel { Orientation = Orientation.Horizontal, Spacing = 16, Children = { delete, leave } } } },
            }.Res(Border.BackgroundProperty, "Brush.DangerBackground"));
        }

        var bin = Segment(AppStrings.Delete_RecycleBin, !_permanent, () => SetPermanent(false));
        var perm = Segment(AppStrings.Delete_Permanently, _permanent, () => SetPermanent(true));
        var hint = OptionsUi.Text(_permanent ? AppStrings.Delete_CantUndo : AppStrings.Delete_CanRestore, 12, brush: _permanent ? "Brush.Danger" : "Brush.TertiaryText");
        _body.Children.Add(new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 10,
            Children =
            {
                OptionsUi.Text(AppStrings.Delete_WhereTo, 13, brush: "Brush.SecondaryText"),
                new Border { Padding = new Thickness(2), CornerRadius = new CornerRadius(4), BorderThickness = new Thickness(1), Child = new StackPanel { Orientation = Orientation.Horizontal, Children = { bin, perm } } }.Res(Border.BorderBrushProperty, "Brush.Border"),
                hint,
            },
        });

        _go.Content = _permanent ? AppStrings.Delete_PermanentlyEnter : AppStrings.Delete_Enter;
        _go.Classes.Remove("accent");
        _go.Classes.Remove("danger");
        if (_permanent)
        {
            _go.Classes.Add("danger");
            _go.Res(Button.BackgroundProperty, "Brush.Danger").Res(Button.ForegroundProperty, "Brush.PanelBackground");
        }
        else
        {
            _go.ClearValue(Button.BackgroundProperty);
            _go.ClearValue(Button.ForegroundProperty);
            _go.Classes.Add("accent");
        }
    }

    private static Button Segment(string text, bool on, Action click)
    {
        var button = new Button { Content = text, Padding = new Thickness(10, 3), BorderThickness = new Thickness(0), FontSize = 13 };
        if (on)
        {
            button.Classes.Add("accent");
        }
        else
        {
            button.Background = Brushes.Transparent;
        }

        button.Click += (_, _) => click();
        return button;
    }

    private void Accept(bool toQueue)
        => Close(new DeleteRequest(!_permanent, !_permanent && _refused.Count > 0 ? _refusedPermanently : null, toQueue));

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
        else if (e.Key == Key.Enter)
        {
            var queueFirst = _settings.Enter == EnterAction.AddToQueue;
            Accept(toQueue: e.KeyModifiers.HasFlag(KeyModifiers.Shift) ? !queueFirst : queueFirst);
            e.Handled = true;
        }
    }

}

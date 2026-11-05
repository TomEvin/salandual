// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.App/MainWindow.FolderLooks.cs

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Salandual.App.Panels;
using Salandual.App.Resources;
using Salandual.Core.Operations;
using Salandual.Core.Settings;
using Salandual.Platform.Windows.FileSystem;

namespace Salandual.App;

public partial class MainWindow
{
    internal static readonly NameColour[] NamedColours = [NameColour.Blue, NameColour.Green, NameColour.Purple, NameColour.Orange, NameColour.Red, NameColour.Teal, NameColour.Pink, NameColour.Grey];

    internal static readonly NameColour[] FolderColours =
    [
        NameColour.Red, NameColour.Coral, NameColour.Orange, NameColour.Peach, NameColour.Amber, NameColour.Yellow, NameColour.Gold, NameColour.Lime, NameColour.Olive, NameColour.Green,
        NameColour.Forest, NameColour.Mint, NameColour.Teal, NameColour.Aqua, NameColour.Cyan, NameColour.Sky, NameColour.Blue, NameColour.Navy, NameColour.Indigo, NameColour.Violet, NameColour.Purple,
        NameColour.Lavender, NameColour.Plum, NameColour.Magenta, NameColour.Pink, NameColour.Rose, NameColour.Maroon, NameColour.Brown, NameColour.Sand, NameColour.Slate, NameColour.Grey, NameColour.Charcoal,
    ];

    private void WireFolderLooks(PanelHost host) => host.Renamed += (_, e) => FolderMoved(e.From, e.To);

    internal void FolderMoved(string from, string to)
        => _services.Settings.Update(s => s.Highlighting.Moved(from, to) is { } moved ? s with { Highlighting = moved } : s);

    private void FoldersMovedBy(OperationJob job)
    {
        if (job.Kind != OperationKind.Move || _services.Settings.Current.Highlighting.Folders.Count == 0)
        {
            return;
        }

        foreach (var (from, to) in job.MovedFolders)
        {
            if (!Directory.Exists(from) && Directory.Exists(to))
            {
                FolderMoved(from, to);
            }
        }
    }

    internal void SetFolderLooks(IReadOnlyList<string> paths, Func<FolderLook, FolderLook?> change)
        => _services.Settings.Update(s =>
        {
            var h = s.Highlighting;
            foreach (var path in paths)
            {
                h = h.WithFolder(path, change(h.Folders.GetValueOrDefault(HighlightSettings.Key(path)) ?? new FolderLook()));
            }

            return s with { Highlighting = h };
        });

    internal MenuItem FolderLookMenu(PanelHost host, IReadOnlyList<string> paths, Point at)
    {
        var item = new MenuItem { Header = AppStrings.FolderLook_Menu };
        item.Click += (_, _) => Avalonia.Threading.Dispatcher.UIThread.Post(() => ShowFolderLookPicker(host, paths, at));
        return item;
    }

    internal FolderLookPicker? FolderLookPicker { get; private set; }

    internal void ShowFolderLookPicker(PanelHost host, IReadOnlyList<string> paths, Point at)
    {
        var folders = _services.Settings.Current.Highlighting.Folders;
        var looks = paths.Select(p => folders.GetValueOrDefault(HighlightSettings.Key(p))).ToList();
        var flyout = new Flyout
        {
            Placement = PlacementMode.AnchorAndGravity,
            PlacementAnchor = Avalonia.Controls.Primitives.PopupPositioning.PopupAnchor.TopLeft,
            PlacementGravity = Avalonia.Controls.Primitives.PopupPositioning.PopupGravity.BottomRight,
            HorizontalOffset = at.X,
            VerticalOffset = at.Y,
        };
        var picker = new FolderLookPicker(looks[0] ?? new FolderLook(), looks.Any(l => l is not null),
            change => SetFolderLooks(paths, change), () => _ = PickFolderImageAsync(host, paths), flyout.Hide);
        picker.Styles.Add(Salandual.App.Find.FindStyles.Create());
        flyout.Content = picker;
        flyout.Opened += (_, _) => picker.FocusFirst();
        flyout.Closed += (_, _) =>
        {
            if (ReferenceEquals(FolderLookPicker, picker))
            {
                FolderLookPicker = null;
            }

            host.View.Focus();
        };
        FolderLookPicker = picker;
        flyout.ShowAt(host.View);
    }

    private async Task PickFolderImageAsync(PanelHost host, IReadOnlyList<string> paths)
    {
        var picked = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = AppStrings.FolderLook_ImageTitle,
            AllowMultiple = false,
            FileTypeFilter = [new FilePickerFileType(AppStrings.FolderLook_Images) { Patterns = [.. FolderMarks.ImageExtensions.Select(e => "*" + e)] }],
        });
        if (picked.Count == 0 || picked[0].TryGetLocalPath() is not { } file)
        {
            return;
        }

        var looks = _services.FolderLooks;
        if (await Task.Run(() => looks.CopyIn(file)) is not { } name)
        {
            host.ShowError(AppStrings.FolderLook_BadImage);
            return;
        }

        SetFolderLooks(paths, l => l with { Image = name, Mark = null });
    }

    internal static string ColourName(NameColour colour) => colour switch
    {
        NameColour.Dimmed => AppStrings.Hl_Dimmed,
        NameColour.Blue => AppStrings.Hl_Blue,
        NameColour.Green => AppStrings.Hl_Green,
        NameColour.Purple => AppStrings.Hl_Purple,
        NameColour.Orange => AppStrings.Hl_Orange,
        NameColour.Red => AppStrings.Hl_Red,
        NameColour.Teal => AppStrings.Hl_Teal,
        NameColour.Pink => AppStrings.Hl_Pink,
        NameColour.Grey => AppStrings.Hl_Grey,
        NameColour.Yellow => AppStrings.Hl_Yellow,
        NameColour.Amber => AppStrings.Hl_Amber,
        NameColour.Gold => AppStrings.Hl_Gold,
        NameColour.Brown => AppStrings.Hl_Brown,
        NameColour.Coral => AppStrings.Hl_Coral,
        NameColour.Maroon => AppStrings.Hl_Maroon,
        NameColour.Rose => AppStrings.Hl_Rose,
        NameColour.Magenta => AppStrings.Hl_Magenta,
        NameColour.Plum => AppStrings.Hl_Plum,
        NameColour.Violet => AppStrings.Hl_Violet,
        NameColour.Indigo => AppStrings.Hl_Indigo,
        NameColour.Navy => AppStrings.Hl_Navy,
        NameColour.Sky => AppStrings.Hl_Sky,
        NameColour.Cyan => AppStrings.Hl_Cyan,
        NameColour.Aqua => AppStrings.Hl_Aqua,
        NameColour.Mint => AppStrings.Hl_Mint,
        NameColour.Forest => AppStrings.Hl_Forest,
        NameColour.Lime => AppStrings.Hl_Lime,
        NameColour.Olive => AppStrings.Hl_Olive,
        NameColour.Sand => AppStrings.Hl_Sand,
        NameColour.Peach => AppStrings.Hl_Peach,
        NameColour.Lavender => AppStrings.Hl_Lavender,
        NameColour.Slate => AppStrings.Hl_Slate,
        NameColour.Charcoal => AppStrings.Hl_Charcoal,
        NameColour.Own => AppStrings.Hl_Own,
        _ => AppStrings.Hl_Normal,
    };

    internal static Control Swatch(NameColour colour)
    {
        var dark = FolderLooks.IsDarkTheme;
        return new Border
        {
            Width = 14,
            Height = 14,
            CornerRadius = new CornerRadius(2),
            VerticalAlignment = VerticalAlignment.Center,
            Background = FolderLooks.FolderBrush(colour, dark) ?? Brushes.Transparent,
        };
    }
}

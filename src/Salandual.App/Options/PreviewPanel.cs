// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.App/Options/PreviewPanel.cs

using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Salandual.App.Panels;
using Salandual.App.Resources;
using Salandual.Core.Listing;
using Salandual.Core.Panels;
using Salandual.Core.Settings;
using Salandual.Core.Vfs;

namespace Salandual.App.Options;

internal sealed class PreviewPanel : Border
{
    private static readonly DateTime _sampleTime = new(2026, 8, 14, 18, 23, 0, DateTimeKind.Utc);

    private readonly SettingsService _settings;
    private readonly FilePanel _panel;
    private readonly FilePanelView _view;
    private readonly InformationLineView _info;

    public PreviewPanel(SettingsService settings, int rows = 6, bool showInfo = true)
    {
        _settings = settings;
        _panel = new FilePanel(new SampleFiles(), EntrySorter.ForCurrentCulture(numericSort: true), a => a());
        _panel.ShowListing(SampleFiles.Listing, watch: false);
        for (var i = 0; i < _panel.Count; i++)
        {
            if (_panel.Items[i].Name is "IMG_0412.HEIC" or "IMG_0413.HEIC")
            {
                _panel.Toggle(i);
            }
        }

        _panel.SetFocus(Enumerable.Range(0, _panel.Count).First(i => _panel.Items[i].Name == "IMG_0413.HEIC"));
        _view = new FilePanelView { Panel = _panel, Focusable = false, IsHitTestVisible = false, IsActivePanel = true };
        _info = new InformationLineView { IsVisible = showInfo };
        Rows = rows;

        var dock = new DockPanel();
        DockPanel.SetDock(_info, Avalonia.Controls.Dock.Bottom);
        dock.Children.Add(_info);
        dock.Children.Add(_view);
        Child = dock;
        CornerRadius = new CornerRadius(8);
        BorderThickness = new Thickness(1);
        ClipToBounds = true;
        this.Res(BorderBrushProperty, "Brush.Border").Res(BackgroundProperty, "Brush.PanelBackground");
        Apply();
    }

    public int Rows { get; }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _settings.Changed += OnSettingsChanged;
        Apply();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        _settings.Changed -= OnSettingsChanged;
    }

    private void OnSettingsChanged(object? sender, SettingsChangedEventArgs e) => Dispatcher.UIThread.Post(Apply);

    private void Apply()
    {
        var s = _settings.Current;
        var format = DisplayFormat.From(s.Panels, CultureInfo.CurrentCulture);
        _view.RowHeight = s.Layout.RowHeight;
        _view.ShowHeader = true;
        _view.Format = format;
        _view.Height = s.Layout.RowHeight * (Rows + 1);
        var focused = _panel.FocusedEntry!.Value;
        _info.Set(InformationLine.Expand(s.Panels.InformationLine, focused, isUp: false, format), InformationLine.Summary(_panel, format, CultureInfo.CurrentCulture));
    }

    private sealed class SampleFiles : IFileSystemProvider
    {
        public static DirectoryListing Listing => new(@"C:\Preview",
        [
            Folder(AppStrings.Sample_Folder),
            File("IMG_0412.HEIC", 3_565_158),
            File("IMG_0413.HEIC", 3_250_585),
            File(AppStrings.Sample_Panorama, 1_258_291),
            File(AppStrings.Sample_Prices, 2_048),
            File(AppStrings.Sample_Passport, 421_888, FileAttributes.Archive | FileAttributes.ReadOnly),
            File(AppStrings.Sample_OldPhotos, 18_874_368, FileAttributes.Archive | FileAttributes.Compressed),
        ], TimeSpan.Zero);

        private static FileEntry Folder(string name) => new(name, 0, _sampleTime.Ticks, FileAttributes.Directory);

        private static FileEntry File(string name, long size, FileAttributes attributes = FileAttributes.Archive) => new(name, size, _sampleTime.Ticks, attributes);

        public ProviderCapabilities Capabilities => ProviderCapabilities.List;

        public Task<DirectoryListing> ListAsync(string path, CancellationToken cancellationToken = default) => Task.FromResult(Listing);

        public string? GetParent(string path) => null;

        public string? GetRoot(string path) => path;

        public string GetName(string path) => path;

        public string Combine(string directory, string name) => directory + "\\" + name;

        public IDisposable Watch(string path, Action<DirectoryChange> onChange, Action<Exception> onError) => new Nothing();

        private sealed class Nothing : IDisposable
        {
            public void Dispose()
            {
            }
        }
    }
}

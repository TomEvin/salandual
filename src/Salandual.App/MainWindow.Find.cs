// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.App/MainWindow.Find.cs

using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Salandual.App.Find;
using Salandual.App.Panels;
using Salandual.App.Resources;
using Salandual.Core.Operations;
using Salandual.Core.Resources;
using Salandual.Core.Search;
using Salandual.Core.Settings;
using Salandual.Platform.Windows.Shell;
using Salandual.Platform.Windows.FileSystem;

namespace Salandual.App;

public partial class MainWindow
{
    private readonly List<FindWindow> _finds = [];

    internal IReadOnlyList<FindWindow> FindWindows => _finds;

    internal FindWindow OpenFind(PanelHost host)
    {
        var folder = PanelFolder(host);
        if (_finds.FirstOrDefault(f => !f.IsSearching && !f.FindsDuplicates) is { } idle)
        {
            idle.LookInFolder(folder);
            idle.WindowState = idle.WindowState == WindowState.Minimized ? WindowState.Normal : idle.WindowState;
            idle.Activate();
            return idle;
        }

        return ShowFindWindow(folder, duplicates: false);
    }

    internal FindWindow OpenDuplicates(PanelHost host, string? named = null, string? lookIn = null)
    {
        var window = _finds.FirstOrDefault(f => !f.IsSearching && f.FindsDuplicates) is { } idle ? idle : ShowFindWindow(PanelFolder(host), duplicates: true);
        if (lookIn is { Length: > 0 })
        {
            window.LookInBox.Text = lookIn;
        }
        else
        {
            window.LookInFolder(PanelFolder(host));
        }

        if (named is { Length: > 0 })
        {
            window.NamedBox.Text = named;
        }

        window.WindowState = window.WindowState == WindowState.Minimized ? WindowState.Normal : window.WindowState;
        window.Activate();
        return window;
    }

    private static string PanelFolder(PanelHost host)
        => host.Panel.Results is { } results && host.Panel.Path.Length == 0 ? results.ReturnPath : host.Panel.Path;

    internal FindWindow OpenSavedSearch(string name)
    {
        var find = OpenFind(ActivePanel);
        find.ApplySaved(name);
        return find;
    }

    private FindWindow ShowFindWindow(string folder, bool duplicates)
    {
        FindWindow? window = null;
        var request = new FindRequest(folder)
        {
            Files = _services.Files,
            OtherPanelFolder = () => OtherPanel(ActivePanel).Panel.Path,
            LocalDrives = () => [.. Drives.List().Where(d => d.Kind is DriveKind.Fixed or DriveKind.Removable).Select(d => d.Root)],
            ShowInPanel = hit => _ = ShowInPanelAsync(ActivePanel, hit.FullPath),
            OpenFolder = path => _ = OpenFolderAsync(path),
            OpenFile = path => _ = OpenFileAsync(path),
            View = (path, siblings, text) => OpenViewer(ActivePanel, path, _services.Settings.Current.Viewers.KindFor(WindowsPath.GetName(path), alternate: false), siblings: siblings, comeBackTo: window, findText: text),
            Edit = path => _ = OpenEditorAsync(ActivePanel, path),
            Properties = (folder, names, windows, owner) => _ = ShowPropertiesAsync(ActivePanel, new MenuTarget(folder, names, false), windows != (_services.Settings.Current.Mouse.AltEnter == PropertiesWindow.Windows), owner),
            OpenWith = path => ShellAccess.OpenWith(window?.TryGetPlatformHandle()?.Handle ?? _hwnd, path),
            CopyOrMove = CopyOrMoveFoundAsync,
            Delete = DeleteFoundAsync,
            Rename = hit => _ = RenameFoundAsync(hit),
            WindowsCommands = FillWindowsCommands,
            OpenOptions = category => _ = OpenOptionsAsync(category),
            OpenDuplicates = (named, lookIn) => OpenDuplicates(ActivePanel, named, lookIn),
            ShowResults = (hits, what) => ShowResultsInPanel(hits, what, keepOrder: duplicates),
            Clipboard = FileClipboardAccess,
        };
        window = new FindWindow(_services.Settings, request, duplicates: duplicates);
        window.Position = FindPosition(window);
        if (_services.Settings.Current.Find.WindowMaximized)
        {
            window.WindowState = WindowState.Maximized;
        }

        _finds.Add(window);
        window.Closed += (_, _) => _finds.Remove(window);
        window.Show();
        return window;
    }

    internal PanelHost ShowResultsInPanel(IReadOnlyList<SearchHit> hits, string what, bool keepOrder)
    {
        var host = _services.Settings.Current.Find.ResultsPanel == ResultsPanel.Other ? OtherPanel(ActivePanel) : ActivePanel;
        var root = SearchResults.CommonFolder(hits.Select(h => h.Folder));
        var entries = hits.Select(h => new Core.Vfs.FileEntry(Relative(h.FullPath), h.Size, h.LastWriteUtcTicks, h.Attributes)).ToList();
        host.Panel.ShowResults(root, entries, what, StatResult, keepOrder);
        SetActive(Array.IndexOf(_panels, host), focus: true);
        Activate();
        return host;

        string Relative(string full)
            => root.Length == 0 ? full : full[root.Length..].TrimStart('\\');
    }

    private static Core.Vfs.FileEntry? StatResult(string path)
    {
        try
        {
            var file = new FileInfo(path);
            if (file.Exists)
            {
                return new Core.Vfs.FileEntry(file.Name, file.Length, file.LastWriteTimeUtc.Ticks, file.Attributes);
            }

            var folder = new DirectoryInfo(path);
            return folder.Exists ? new Core.Vfs.FileEntry(folder.Name, 0, folder.LastWriteTimeUtc.Ticks, folder.Attributes) : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return null;
        }
    }

    private void FillSavedSearchesMenu(MenuItem menu)
    {
        menu.Items.Clear();
        var saved = _services.Settings.Current.Find.Saved;
        if (saved.Count == 0)
        {
            menu.Items.Add(new MenuItem { Header = AppStrings.Find_SavedNone, IsEnabled = false });
        }

        foreach (var search in saved)
        {
            var item = new MenuItem { Header = search.Name };
            ToolTip.SetTip(item, FindWindow.SavedText(search.Search));
            var name = search.Name;
            item.Click += (_, _) => OpenSavedSearch(name);
            menu.Items.Add(item);
        }

        menu.Items.Add(new Separator());
        var manage = new MenuItem { Header = AppStrings.Find_SavedManage };
        manage.Click += (_, _) => _ = OpenOptionsAsync("compare");
        menu.Items.Add(manage);
    }

    private PixelPoint FindPosition(Window find)
    {
        var scale = RenderScaling;
        var x = Position.X + (int)((Bounds.Width - find.Width) * scale / 2) + _finds.Count * 32;
        var y = Position.Y + (int)((Bounds.Height - find.Height) * scale / 2) + _finds.Count * 32;
        return new PixelPoint(Math.Max(x, 0), Math.Max(y, 0));
    }

    private async Task<OperationJob?> CopyOrMoveFoundAsync(OperationKind kind, IReadOnlyList<SearchHit> hits, Window owner)
    {
        if (_operationDialog is not null)
        {
            _operationDialog.Activate();
            return null;
        }

        var sources = hits.Select(h => new OperationSource(h.FullPath, h.IsDirectory)).ToList();
        var known = hits.Where(h => !h.IsDirectory).Sum(h => h.Size);
        var folder = SearchResults.CommonFolder(hits.Select(h => h.Folder));
        return await CopyMoveDialogAsync(ActivePanel, kind, sources, folder, known, OtherPanel(ActivePanel).Panel.Path, owner);
    }

    private async Task<OperationJob?> DeleteFoundAsync(IReadOnlyList<SearchHit> hits, bool inverted, Window owner)
    {
        if (_operationDialog is not null)
        {
            _operationDialog.Activate();
            return null;
        }

        var sources = hits.Select(h => new OperationSource(h.FullPath, h.IsDirectory)).ToList();
        var sizes = hits.Where(h => !h.IsDirectory).ToDictionary(h => h.FullPath, h => h.Size, StringComparer.OrdinalIgnoreCase);
        return await DeleteSourcesAsync(sources, sizes, SearchResults.CommonFolder(hits.Select(h => h.Folder)), inverted, owner);
    }

    private async Task RenameFoundAsync(SearchHit hit)
    {
        await ShowInPanelAsync(ActivePanel, hit.FullPath);
        if (ActivePanel.Panel.FocusedEntry is { } focused && string.Equals(focused.Name, hit.Name, StringComparison.OrdinalIgnoreCase))
        {
            QuickRename(ActivePanel);
        }
    }

    private void FillWindowsCommands(MenuItem item, ContextMenu menu, string folder, IReadOnlyList<string> names, Window owner)
    {
        item.Items.Add(new MenuItem { Header = AppStrings.QuickMenu_Loading, IsEnabled = false });
        IShellMenuHandle? handle = null;
        var closed = false;
        var invoking = false;
        menu.Closed += (_, _) =>
        {
            closed = true;
            if (!invoking)
            {
                handle?.Dispose();
            }
        };
        _ = FillAsync();

        async Task FillAsync()
        {
            try
            {
                handle = await ShellAccess.BuildMenuAsync(folder, names, extended: false);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Runtime.InteropServices.ExternalException or ArgumentException)
            {
                handle = null;
            }

            if (closed)
            {
                handle?.Dispose();
                return;
            }

            item.Items.Clear();
            var added = handle is null ? 0 : AddShellItems(Invoke, item.Items, handle.Items, [], top: true, pins: false);
            if (added == 0)
            {
                item.Items.Add(new MenuItem { Header = AppStrings.QuickMenu_NoWindowsCommands, IsEnabled = false });
            }
        }

        void Invoke(ShellMenuItem command)
        {
            if (handle is not { } h)
            {
                return;
            }

            invoking = true;
            _ = InvokeAsync(h);

            async Task InvokeAsync(IShellMenuHandle h)
            {
                try
                {
                    var screen = owner.PointToScreen(new Point(40, 40));
                    await h.InvokeAsync(command.Id, owner.TryGetPlatformHandle()?.Handle ?? _hwnd, screen.X, screen.Y);
                    foreach (var panel in _panels)
                    {
                        _ = panel.Panel.RefreshAsync();
                    }
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Runtime.InteropServices.ExternalException or ObjectDisposedException)
                {
                    ShowNotice(Loc.Format(AppStrings.Notice_WindowsMenuFailed, ex.Message));
                }
                finally
                {
                    h.Dispose();
                }
            }
        }
    }

    private async Task OpenFolderAsync(string path)
    {
        await ActivePanel.Panel.NavigateAsync(path);
        SetActive(_active, focus: true);
        Activate();
    }

    internal async Task SmokeFindAsync()
    {
        if (!Diagnostics.StartupTrace.IsEnabled || Environment.GetEnvironmentVariable("SALANDUAL_SMOKE_FIND") is not { Length: > 0 } folder || !Directory.Exists(folder))
        {
            return;
        }

        var log = new System.Text.StringBuilder();
        var clock = System.Diagnostics.Stopwatch.StartNew();
        var find = OpenFind(ActivePanel);

        Type(find.LookInBox, Environment.SystemDirectory);
        Type(find.NamedBox, "*.salandual-smoke");
        var searching = find.StartAsync(ResultMode.New);
        await Task.Delay(800);
        log.Append(CultureInfo.InvariantCulture, $"find while searching={find.IsSearching} stop={find.StopButton.IsVisible} search={find.FindButton.IsVisible} named-enabled={find.NamedBox.IsEffectivelyEnabled} lookin-enabled={find.LookInBox.IsEffectivelyEnabled}\n");
        if (Environment.GetEnvironmentVariable("SALANDUAL_SMOKE_CAPTURE") is { Length: > 0 } searchingCaptures && find.TryGetPlatformHandle()?.Handle is { } searchingHwnd)
        {
            Directory.CreateDirectory(searchingCaptures);
            Diagnostics.WindowCapture.Save(searchingHwnd, Path.Combine(searchingCaptures, "find-searching.png"));
        }

        find.Stop();
        await searching;
        log.Append(CultureInfo.InvariantCulture, $"find stopped searching={find.IsSearching} search={find.FindButton.IsVisible} named-enabled={find.NamedBox.IsEffectivelyEnabled}\n");

        Type(find.LookInBox, folder);
        Type(find.NamedBox, "*.md;*.txt");
        Type(find.TextRows[0].Box, "deploy");
        find.AddTextRow(new SearchText("draft", Contains: false));
        await find.StartAsync(ResultMode.New);
        var first = find.Shown.FirstOrDefault(h => h.Line is not null);
        log.Append(CultureInfo.InvariantCulture, $"find shown={find.IsVisible} {clock.ElapsedMilliseconds} ms found={find.Shown.Count} [{string.Join(",", find.Shown.Select(h => h.Name))}] line={first?.Line?.LineNumber}:{first?.Line?.Text} more={first?.Line?.MoreLines} header=\"{find.HeaderText}\" title=\"{find.TitleText}\"\n");
        await Task.Delay(100);
        log.Append(CultureInfo.InvariantCulture, $"find focus-in-results={find.ResultList.IsKeyboardFocusWithin} history named={find.NamedBox.Items.Count} texts={find.TextRows[0].Box.Items.Count} advanced=\"{find.SummaryText}\"\n");

        find.ResultList.SelectedItem = first;
        find.ViewItem(first);
        if (Viewers.Count > 0 && Viewers[^1] is { } viewer)
        {
            await viewer.Loading;
            await viewer.Finding;
            log.Append(CultureInfo.InvariantCulture, $"find view match=\"{(viewer.Document is { } d && viewer.Current.HasSelection ? d.Text(viewer.Current.SelectionStart, viewer.Current.SelectionEnd) : "")}\" hits=\"{viewer.HitsText}\" box=\"{viewer.FindBox.Text}\"\n");
            viewer.Close();
        }

        var menu = find.ShowResultMenu(new Point(20, 20));
        log.Append(CultureInfo.InvariantCulture, $"find menu items={menu?.Items.OfType<MenuItem>().Count()}\n");
        menu?.Close();

        if (Environment.GetEnvironmentVariable("SALANDUAL_SMOKE_CAPTURE") is { Length: > 0 } captures && find.TryGetPlatformHandle()?.Handle is { } hwnd)
        {
            Directory.CreateDirectory(captures);
            await Task.Delay(500);
            Diagnostics.WindowCapture.Save(hwnd, Path.Combine(captures, "find.png"));
            find.AdvancedOpen = true;
            find.TextRows[0].SetKind(TextKind.Hex, find);
            find.MinSizeBox.Text = "1";
            find.ModifiedBox.SelectedIndex = 1;
            find.AttributeBoxes[FileAttributes.System].Rule = AttributeRule.MustNot;
            find.AttributeBoxes[FileAttributes.Archive].Rule = AttributeRule.Must;
            find.Encodings = SearchEncodings.Ansi | SearchEncodings.Utf8;
            await Task.Delay(300);
            Diagnostics.WindowCapture.Save(hwnd, Path.Combine(captures, "find-more.png"));
            log.Append(CultureInfo.InvariantCulture, $"find advanced=\"{find.SummaryText}\" encodings=\"{find.EncodingChip.Content}\"\n");
            find.EncodingChip.Flyout!.ShowAt(find.EncodingChip);
            await Task.Delay(400);
            if (TopLevel.GetTopLevel(find.EncodingBoxes[0])?.TryGetPlatformHandle()?.Handle is { } encodingsHwnd)
            {
                Diagnostics.WindowCapture.Save(encodingsHwnd, Path.Combine(captures, "find-encodings.png"));
            }

            find.EncodingChip.Flyout.Hide();
            find.ModifiedBox.SelectedIndex = 2;
            find.FromDate.SelectedDate = DateTime.Today.AddDays(-3);
            find.FromDate.IsDropDownOpen = true;
            await Task.Delay(500);
            var calendar = Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(find.FromDate).OfType<Avalonia.Controls.Primitives.Popup>().FirstOrDefault()?.Child;
            if (calendar is not null && TopLevel.GetTopLevel(calendar)?.TryGetPlatformHandle()?.Handle is { } calendarHwnd)
            {
                Diagnostics.WindowCapture.Save(calendarHwnd, Path.Combine(captures, "find-calendar.png"));
            }

            find.FromDate.IsDropDownOpen = false;
            find.ResetMore();
            find.Encodings = SearchEncodings.Auto;
            find.AdvancedOpen = false;
            find.TextRows[0].SetKind(TextKind.Text, find);

            var defaults = new FindWindow(_services.Settings, new FindRequest(""), editDefaults: true);
            defaults.Show();
            defaults.AdvancedOpen = true;
            await Task.Delay(500);
            if (defaults.TryGetPlatformHandle()?.Handle is { } defaultsHwnd)
            {
                Diagnostics.WindowCapture.Save(defaultsHwnd, Path.Combine(captures, "find-defaults.png"));
            }

            defaults.Close();
        }

        find.TextRows[0].SetKind(TextKind.Hex, find);
        log.Append(CultureInfo.InvariantCulture, $"find hex=\"{find.TextRows[0].Box.Text}\"\n");
        find.TextRows[0].SetKind(TextKind.Text, find);
        find.RemoveTextRow(find.TextRows[1]);
        Type(find.TextRows[0].Box, "friday");
        await find.StartAsync(ResultMode.Keep);
        log.Append(CultureInfo.InvariantCulture, $"find keep friday={find.Shown.Count} [{string.Join(",", find.Shown.Select(h => h.Name))}]\n");

        Type(find.TextRows[0].Box, "");
        Type(find.NamedBox, "*");
        var running = find.StartAsync(ResultMode.New);
        find.Stop();
        await running;
        log.Append(CultureInfo.InvariantCulture, $"find stopped searching={find.IsSearching} header=\"{find.HeaderText}\"\n");

        Type(find.NamedBox, "*.txt");
        Type(find.TextRows[0].Box, "invoice");
        find.InsideZipBox.IsChecked = true;
        await find.StartAsync(ResultMode.New);
        log.Append(CultureInfo.InvariantCulture, $"find zip found={find.Shown.Count} [{string.Join(",", find.Shown.Select(h => h.FullPath[folder.Length..]))}] log={find.Log.Count}\n");
        find.Close();
        await Task.Delay(100);

        find = OpenFind(ActivePanel);
        log.Append(CultureInfo.InvariantCulture, $"find reopened named=\"{find.NamedBox.Text}\" rows={find.TextRows.Count} text=\"{find.TextRows[0].Text}\" zip={find.InsideZipBox.IsChecked} history={find.NamedBox.Items.Count} advanced=\"{find.SummaryText}\" title=\"{find.TitleText}\"\n");

        var captures1b = Environment.GetEnvironmentVariable("SALANDUAL_SMOKE_CAPTURE");
        Type(find.LookInBox, folder);
        Type(find.NamedBox, "*.md;*.txt");
        await find.StartAsync(ResultMode.New);
        var shown = ShowResultsInPanel([.. find.Shown.Where(h => h.Archive is null)], "*.md;*.txt", keepOrder: false);
        await Task.Delay(300);
        log.Append(CultureInfo.InvariantCulture, $"find panel results={shown.Panel.Results is not null} items={shown.Panel.Count} bar=\"{shown.ResultsText}\" first=\"{shown.Panel.Items.FirstOrDefault().Name}\"\n");
        if (captures1b is { Length: > 0 })
        {
            Diagnostics.WindowCapture.Save(_hwnd, Path.Combine(captures1b, "find-panel.png"));
        }

        shown.Panel.SetFocus(Math.Min(1, shown.Panel.Count - 1));
        var jumpTo = shown.Panel.FocusedEntry?.Name;
        await shown.Panel.GoToResultAsync();
        log.Append(CultureInfo.InvariantCulture, $"find panel jump to=\"{jumpTo}\" path=\"{shown.Panel.Path[Math.Min(folder.Length, shown.Panel.Path.Length)..]}\" focus=\"{shown.Panel.FocusedEntry?.Name}\" title=\"{Title}\"\n");
        await shown.Panel.NavigateAsync(folder);
        shown = ShowResultsInPanel([.. find.Shown.Where(h => h.Archive is null)], "*.md;*.txt", keepOrder: false);
        await shown.Panel.LeaveResultsAsync();
        log.Append(CultureInfo.InvariantCulture, $"find panel back results={shown.Panel.Results is not null} up={shown.Panel.HasUpEntry}\n");

        var duplicates = OpenDuplicates(ActivePanel, "*", folder);
        await duplicates.StartAsync(ResultMode.New);
        log.Append(CultureInfo.InvariantCulture, $"find duplicates groups={duplicates.Groups.Count} [{string.Join(" | ", duplicates.Groups.Select(duplicates.GroupText))}] header=\"{duplicates.HeaderText}\" title=\"{duplicates.Title}\"\n");
        duplicates.SelectAllBut(DuplicateKeep.Newest);
        log.Append(CultureInfo.InvariantCulture, $"find duplicates selected={duplicates.ResultList.SelectedItems?.Count}\n");
        if (captures1b is { Length: > 0 } && duplicates.TryGetPlatformHandle()?.Handle is { } duplicatesHwnd)
        {
            await Task.Delay(400);
            Diagnostics.WindowCapture.Save(duplicatesHwnd, Path.Combine(captures1b, "find-duplicates.png"));
        }

        duplicates.Close();
        var hadSaved = _services.Settings.Current.Find.Saved;
        _services.Settings.Update(s => s with { Find = s.Find with { Saved = [] } });
        await ShowSavedMenu("find-saved-none.png");
        _services.Settings.Update(s => s with { Find = s.Find with { Saved = [new SavedSearch("Markdown with TODO", new SearchDefaults { Named = "*.md;*.markdown;*.txt", LookIn = @"D:\Projects\Documentation\Release notes", Texts = [new SearchText("TODO"), new SearchText("draft", Contains: false)], MinSize = "10" }), new SavedSearch("Logs", new SearchDefaults { Named = "*.log" })] } });
        await ShowSavedMenu("find-saved.png");

        log.Append(CultureInfo.InvariantCulture, $"find saved applied={find.ApplySaved("Markdown with TODO")} named=\"{find.NamedBox.Text}\" text=\"{find.TextRows[0].Text}\"\n");
        _services.Settings.Update(s => s with { Find = s.Find with { Saved = hadSaved } });
        find.MinSizeBox.Text = "12a3456789";
        await Task.Delay(50);
        log.Append(CultureInfo.InvariantCulture, $"find size digits=\"{find.MinSizeBox.Text}\"\n");
        find.MinSizeBox.Text = "5";
        find.MaxSizeBox.Text = "2";
        log.Append(CultureInfo.InvariantCulture, $"find size problem=\"{find.SizeProblem()}\" marked={find.MaxSizeBox.Classes.Contains("error")}\n");
        find.AdvancedOpen = true;
        find.MinSizeBox.Text = "";
        find.MaxSizeBox.Text = "";
        find.ModifiedBox.SelectedIndex = 2;
        find.FromDate.SelectedDate = new DateTime(2026, 10, 6);
        find.ToDate.SelectedDate = new DateTime(2026, 10, 14);
        find.ToDate.IsDropDownOpen = true;
        await Task.Delay(500);
        var rangeTop = Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(find.ToDate).OfType<Avalonia.Controls.Primitives.Popup>().FirstOrDefault()?.Child is { } rangeChild ? TopLevel.GetTopLevel(rangeChild) : null;
        log.Append(CultureInfo.InvariantCulture, $"find range tinted={(rangeTop is null ? -1 : Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(rangeTop).OfType<Avalonia.Controls.Primitives.CalendarDayButton>().Count(d => d.Classes.Contains("inrange")))}\n");
        if (captures1b is { Length: > 0 } && rangeTop?.TryGetPlatformHandle()?.Handle is { } rangeHwnd)
        {
            Diagnostics.WindowCapture.Save(rangeHwnd, Path.Combine(captures1b, "find-range.png"));
        }

        find.ToDate.IsDropDownOpen = false;
        find.FromDate.SelectedDate = new DateTime(2026, 10, 20);
        await Task.Delay(300);
        if (captures1b is { Length: > 0 } && find.TryGetPlatformHandle()?.Handle is { } findHwnd)
        {
            Diagnostics.WindowCapture.Save(findHwnd, Path.Combine(captures1b, "find-dates-bad.png"));
        }

        log.Append(CultureInfo.InvariantCulture, $"find dates problem=\"{find.AdvancedProblem()}\"\n");

        find.Close();
        Diagnostics.StartupTrace.Append(log.ToString());

        async Task ShowSavedMenu(string capture)
        {
            if (find.SavedButton?.Flyout is not MenuFlyout savedMenu)
            {
                return;
            }

            find.Activate();
            savedMenu.ShowAt(find.SavedButton);
            await Task.Delay(400);
            var top = savedMenu.Items.OfType<Control>().FirstOrDefault() is { } firstSaved ? TopLevel.GetTopLevel(firstSaved) : null;
            log.Append(CultureInfo.InvariantCulture, $"find saved menu open={savedMenu.IsOpen} items=[{string.Join(" | ", savedMenu.Items.Select(i => i is MenuItem { Header: var h } ? h is TextBlock t ? t.Text : h?.ToString() : "-"))}] top={top?.GetType().Name} shown={top?.IsVisible}\n");
            if (captures1b is { Length: > 0 } && top?.TryGetPlatformHandle()?.Handle is { } savedHwnd)
            {
                Diagnostics.WindowCapture.Save(savedHwnd, Path.Combine(captures1b, capture));
            }

            savedMenu.Hide();
        }
    }

    private static void Type(ComboBox box, string value) => box.Text = value;
}

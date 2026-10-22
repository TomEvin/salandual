// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.App/MainWindow.Editor.cs

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Primitives.PopupPositioning;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Platform.Storage;
using Salandual.App.Dialogs;
using Salandual.App.Editor;
using Salandual.App.Panels;
using Salandual.App.Options;
using Salandual.App.Resources;
using Salandual.Core.Editing;
using Salandual.Core.Resources;
using Salandual.Core.Settings;
using Salandual.Platform.Windows.FileSystem;
using Salandual.Platform.Windows.Shell;

namespace Salandual.App;

internal sealed record EditWithItem(string Label, string? Note, Action Run, bool Enabled = true);

public partial class MainWindow
{
    private readonly List<EditorWindow> _editors = [];
    private IProgramLauncher? _programs;

    internal IReadOnlyList<EditorWindow> Editors => _editors;

    internal ChoiceDialog? TooBigQuestion { get; private set; }

    internal EditNewFileDialog? EditNewDialog { get; private set; }

    internal ContextMenu? EditWithMenu { get; private set; }

    internal IProgramLauncher Programs
    {
        get => _programs ??= _services.IsIsolated ? new RecordingProgramLauncher() : WindowsProgramLauncher.Instance;
        set => _programs = value;
    }

    internal async Task<EditorWindow?> OpenEditorAsync(PanelHost host)
    {
        if (FocusedFile(host) is not { } path)
        {
            return null;
        }

        return await OpenEditorAsync(host, path);
    }

    internal async Task<EditorWindow?> OpenEditorAsync(PanelHost host, string path, bool anyway = false, bool internalEditor = false, bool newFile = false)
    {
        if (_editors.FirstOrDefault(e => string.Equals(e.FilePath, path, StringComparison.OrdinalIgnoreCase)) is { } open)
        {
            if (open.WindowState == WindowState.Minimized)
            {
                open.WindowState = WindowState.Normal;
            }

            open.Activate();
            return open;
        }

        var editors = _services.Settings.Current.Editors;
        var name = WindowsPath.GetName(path);
        if (!internalEditor && editors.ProgramFor(name) is { } program)
        {
            StartEditor(program, path);
            return null;
        }

        var largest = editors.LargestBytes(name);
        long size = 0;
        try
        {
            size = new FileInfo(path).Length;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }

        if (!anyway && largest > 0 && size > largest)
        {
            var format = Core.Listing.DisplayFormat.From(_services.Settings.Current.Panels, System.Globalization.CultureInfo.CurrentCulture);
            var external = editors.ExternalPrograms().FirstOrDefault();
            List<Choice> choices =
            [
                new("options", AppStrings.Editor_ChangeLimit, Left: true),
                new("view", AppStrings.Editor_View, "F3", Key.F3, Primary: true),
                new("anyway", AppStrings.Editor_OpenAnyway),
            ];
            if (external is not null && ExternalCommand.Parse(external) is { } command)
            {
                choices.Add(new("external", Loc.Format(AppStrings.Editor_EditWithProgram, command.DisplayName)));
            }

            choices.Add(new("cancel", AppStrings.Common_Cancel, "Esc"));
            var question = TooBigQuestion = new ChoiceDialog(
                Loc.Format(AppStrings.Editor_TooBig_Title, name),
                [Loc.Format(AppStrings.Editor_TooBig_Text, format.Size(size), format.Size(largest)), AppStrings.Editor_TooBig_Anyway],
                choices,
                cancel: "cancel",
                width: 620);
            var answer = await question.ShowDialog<string?>(this);
            TooBigQuestion = null;
            switch (answer)
            {
                case "options":
                    _ = OpenOptionsAsync("editors");
                    return null;
                case "view":
                    OpenViewer(host, path, _services.Settings.Current.Viewers.KindFor(name, alternate: false));
                    return null;
                case "external":
                    StartEditor(external!, path);
                    return null;
                case "anyway":
                    anyway = true;
                    break;
                default:
                    return null;
            }
        }

        var request = new EditorRequest(path)
        {
            Anyway = anyway,
            New = newFile,
            View = file => OpenViewer(host, file, _services.Settings.Current.Viewers.KindFor(WindowsPath.GetName(file), alternate: false), fromEditor: true),
            ShowInPanel = file => _ = ShowInPanelAsync(host, file),
            OpenOptions = category => _ = OpenOptionsAsync(category),
            OpenWeb = uri => OpenWebLink(uri),
            PageView = PageViewFactory,
            Clipboard = FileClipboardAccess,
            EditWith = file => EditWithItems(file, internalEditor: null),
        };
        var window = new EditorWindow(_services.Settings, request)
        {
            WindowStartupLocation = WindowStartupLocation.Manual,
        };
        window.Position = ViewerPosition(window, _viewers.Count + _editors.Count);
        if (editors.WindowMaximized)
        {
            window.WindowState = WindowState.Maximized;
        }

        _editors.Add(window);
        var wasActive = false;
        window.Closing += (_, e) =>
        {
            wasActive = !e.Cancel && Viewer.NativeFocus.IsInFront(window);
            if (wasActive)
            {
                Activate();
            }
        };
        window.Closed += (_, _) =>
        {
            _editors.Remove(window);
            if (wasActive)
            {
                Activate();
            }
        };
        window.Show();
        return window;
    }

    internal bool StartEditor(string commandLine, string file)
    {
        if (ExternalCommand.Parse(commandLine) is not { } command)
        {
            return false;
        }

        var problem = Programs.Start(command.Program, command.ArgumentsFor(file), WindowsPath.GetParent(file), out var detail);
        ReportStart(problem, command.DisplayName, detail);
        return problem == StartProblem.None;
    }

    private void StartChosen(string program, string file)
    {
        var problem = Programs.Start(program, [file], WindowsPath.GetParent(file), out var detail);
        ReportStart(problem, Path.GetFileNameWithoutExtension(program), detail);
    }

    private void EditWithWindows(string file)
    {
        var problem = Programs.Edit(file, out var detail);
        if (problem == StartProblem.NoEditor)
        {
            ShowNotice(Loc.Format(AppStrings.EditWith_NoWindowsEditor, WindowsPath.GetName(file)));
        }
        else
        {
            ReportStart(problem, AppStrings.EditWith_Windows, detail);
        }
    }

    private void ReportStart(StartProblem problem, string name, string? detail)
    {
        var text = problem switch
        {
            StartProblem.NotFound => Loc.Format(AppStrings.EditWith_NotFound, name),
            StartProblem.NotAProgram => Loc.Format(AppStrings.EditWith_NotAProgram, name),
            StartProblem.Failed => Loc.Format(AppStrings.EditWith_Failed, name, detail ?? ""),
            _ => null,
        };
        if (text is not null)
        {
            ShowNotice(text, (AppStrings.Editor_Options, () => _ = OpenOptionsAsync("editors")));
        }
    }

    internal IReadOnlyList<EditWithItem> EditWithItems(string file, Action? internalEditor)
    {
        var editors = _services.Settings.Current.Editors;
        var items = new List<EditWithItem>();
        if (internalEditor is not null)
        {
            items.Add(new(AppStrings.EditWith_Internal, "F4", internalEditor));
        }

        foreach (var line in editors.ExternalPrograms())
        {
            if (ExternalCommand.Parse(line) is { } command)
            {
                items.Add(new(command.DisplayName, AppStrings.EditWith_FromTable, () => StartEditor(line, file)));
            }
        }

        items.Add(new(AppStrings.EditWith_Windows, null, () => EditWithWindows(file), Programs.CanEdit(file)));
        items.Add(new(AppStrings.EditWith_Choose, null, () => _ = ChooseProgramAsync(file)));
        return items;
    }

    private async Task ChooseProgramAsync(string file)
    {
        var picked = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = Loc.Format(AppStrings.EditWith_ChooseTitle, WindowsPath.GetName(file)),
            AllowMultiple = false,
            FileTypeFilter = [new FilePickerFileType(AppStrings.EditWith_Programs) { Patterns = ["*.exe"] }],
        });
        if (picked.Count > 0 && picked[0].TryGetLocalPath() is { } program)
        {
            StartChosen(program, file);
        }
    }

    internal ContextMenu? ShowEditWith(PanelHost host)
    {
        if (FocusedFile(host) is not { } path)
        {
            return null;
        }

        var at = host.View.NameBounds(host.Panel.Focus) is { } r ? new Point(r.X + 24, r.Bottom) : new Point(40, 40);
        var menu = new ContextMenu
        {
            PlacementTarget = host.View,
            Placement = PlacementMode.AnchorAndGravity,
            PlacementRect = new Rect(at, new Size(1, 1)),
            PlacementAnchor = PopupAnchor.TopLeft,
            PlacementGravity = PopupGravity.BottomRight,
        };
        menu.Items.Add(new MenuItem { Header = Loc.Format(AppStrings.EditWith_Title, WindowsPath.GetName(path)), IsEnabled = false });
        var items = EditWithItems(path, () => _ = OpenEditorAsync(host, path, internalEditor: true));
        var tableItems = items.Count(i => i.Note == AppStrings.EditWith_FromTable);
        var row = _services.Settings.Current.Editors.ProgramFor(WindowsPath.GetName(path));
        for (var i = 0; i < items.Count; i++)
        {
            if (i == 1 + tableItems)
            {
                menu.Items.Add(new Separator());
            }

            var item = items[i];
            var chosen = i == 0 ? row is null : row is not null && item.Note == AppStrings.EditWith_FromTable && ExternalCommand.Parse(row)?.DisplayName == item.Label;
            menu.Items.Add(EditWithMenuItem(item, chosen));
        }

        menu.Items.Add(new Separator());
        var options = new MenuItem { Header = AppStrings.Editor_Options };
        options.Click += (_, _) => _ = OpenOptionsAsync("editors");
        menu.Items.Add(options);
        menu.Open(host.View);
        EditWithMenu = menu;
        return menu;
    }

    internal static MenuItem EditWithMenuItem(EditWithItem item, bool chosen = false)
    {
        var header = new DockPanel { MinWidth = 220 };
        if (item.Note is { } note)
        {
            var noteText = new TextBlock { Text = note, FontSize = 11, Margin = new Thickness(24, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center }
                .Res(TextBlock.ForegroundProperty, "Brush.TertiaryText");
            DockPanel.SetDock(noteText, Dock.Right);
            header.Children.Add(noteText);
        }

        header.Children.Add(new TextBlock { Text = item.Label, VerticalAlignment = VerticalAlignment.Center });
        var mi = new MenuItem { Header = header, IsEnabled = item.Enabled, Tag = item.Label, ToggleType = MenuItemToggleType.CheckBox, IsChecked = chosen };
        mi.Click += (_, _) => item.Run();
        return mi;
    }

    internal async Task<EditorWindow?> EditNewFileAsync(PanelHost host)
    {
        var folder = host.Panel.Path;
        if (folder.Length == 0 || !Directory.Exists(WindowsPath.ToExtended(folder)))
        {
            return null;
        }

        var dialog = EditNewDialog = new EditNewFileDialog(folder, AppStrings.EditNew_Suggested);
        string? name;
        try
        {
            name = await dialog.ShowDialog<string?>(this);
        }
        finally
        {
            EditNewDialog = null;
        }

        if (name is null)
        {
            return null;
        }

        var path = WindowsPath.Join(folder, name);
        var isNew = false;
        if (!File.Exists(WindowsPath.ToExtended(path)))
        {
            try
            {
                using (new FileStream(WindowsPath.ToExtended(path), FileMode.CreateNew, FileAccess.Write))
                {
                }

                isNew = true;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                ShowNotice(Loc.Format(AppStrings.EditNew_CantCreate, name, ex.Message));
                return null;
            }
        }

        await host.Panel.RefreshAsync(name);
        return await OpenEditorAsync(host, path, internalEditor: true, newFile: isNew);
    }

    private async void OnClosingWithEditors(object? sender, WindowClosingEventArgs e)
    {
        var lists = _unsavedLists.Count > 0 && _services.Settings.Current.Confirmations.CloseUnsavedFileList;
        if (e.CloseReason == WindowCloseReason.OSShutdown || (!_editors.Any(x => x.IsModified) && !_fileCompares.Any(x => x.IsModified) && !lists))
        {
            return;
        }

        e.Cancel = true;
        foreach (var editor in _editors.Where(x => x.IsModified).ToList())
        {
            if (!await editor.CloseAskingAsync())
            {
                return;
            }
        }

        foreach (var compare in _fileCompares.Where(x => x.IsModified).ToList())
        {
            if (!await compare.CloseAskingAsync())
            {
                return;
            }
        }

        if (lists && !await CloseListsAskingAsync())
        {
            return;
        }

        Close();
    }

    internal async Task SmokeEditAsync()
    {
        if (!Diagnostics.StartupTrace.IsEnabled || Environment.GetEnvironmentVariable("SALANDUAL_SMOKE_EDIT") is not { Length: > 0 } file || !File.Exists(file))
        {
            return;
        }

        var clock = System.Diagnostics.Stopwatch.StartNew();
        if (await OpenEditorAsync(ActivePanel, file, internalEditor: true) is not { } editor)
        {
            return;
        }

        for (var i = 0; i < 100 && !editor.IsLoaded; i++)
        {
            await Task.Delay(20);
        }

        await editor.Loading;
        var loaded = clock.ElapsedMilliseconds;
        await editor.Checking;
        var problems = editor.Problems.Count;
        var folds = editor.FoldCount;
        editor.Editor.Document.Insert(0, "x");
        var modified = editor.IsModified;
        editor.Editor.Undo();
        if (Environment.GetEnvironmentVariable("SALANDUAL_SMOKE_CAPTURE") is { Length: > 0 } folder && editor.TryGetPlatformHandle()?.Handle is { } hwnd)
        {
            Directory.CreateDirectory(folder);
            await Task.Delay(500);
            Diagnostics.WindowCapture.Save(hwnd, Path.Combine(folder, $"edit-{Path.GetExtension(file).TrimStart('.')}.png"));
        }

        Diagnostics.StartupTrace.Append(string.Create(System.Globalization.CultureInfo.InvariantCulture,
            $"edit {WindowsPath.GetName(file)} {loaded} ms lines={editor.Editor.Document.LineCount} colours={editor.Language} encoding={editor.File.EncodingName} eol={editor.File.LineEnding} problems={problems} folds={folds} typed={modified} undone={!editor.IsModified}\n"));
        editor.Close();
    }

    internal async Task SmokeEditHtmlAsync()
    {
        if (!Diagnostics.StartupTrace.IsEnabled || Environment.GetEnvironmentVariable("SALANDUAL_SMOKE_EDIT_HTML") is not { Length: > 0 } file || !File.Exists(file)
            || await OpenEditorAsync(ActivePanel, file, internalEditor: true) is not { } editor)
        {
            return;
        }

        await editor.Loading;
        editor.ShowPreview(true);
        async Task<string?> WaitFor(string script, string want)
        {
            string? result = null;
            for (var i = 0; i < 100 && result != want; i++)
            {
                await Task.Delay(100);
                result = editor.PreviewPage is { } page ? await page.RunAsync(script) : null;
            }

            return result;
        }

        var ran = await WaitFor("document.body && document.body.dataset.ran || 'no'", "yes");
        var end = editor.Editor.Document.Text.IndexOf("</main>", StringComparison.Ordinal);
        editor.Editor.Document.Insert(Math.Max(0, end), "<p id=\"added\">typed</p>");
        var updated = await WaitFor("String(!!document.getElementById('added'))", "true");
        editor.Editor.Undo();
        Diagnostics.StartupTrace.Append($"edit html preview={editor.PreviewKindNow} ran={ran} updated={updated} unchanged={!editor.IsModified}\n");
        editor.Close();
    }
}

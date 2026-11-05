// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.App/MainWindow.Language.cs

using Avalonia.Threading;
using Salandual.App.Options;
using Salandual.App.Resources;
using Salandual.Core.Resources;

namespace Salandual.App;

public partial class MainWindow
{
    internal void RestartForLanguage()
    {
        Program.RestartAfterClose = true;
        _options?.Close();
        Close();
    }

    private void LanguageHooks(OptionsWindow options)
    {
        options.RestartNow = RestartForLanguage;
        options.OpenFolderInPanel = folder => _ = ActivePanel.Panel.NavigateAsync(folder);
        options.EditFileAt = EditLanguageFileAt;
        options.OpenWeb = uri => OpenWebLink(uri);
    }

    private void EditLanguageFileAt(string path, int line) => _ = EditAtAsync(path, line);

    private async Task EditAtAsync(string path, int line)
    {
        if (await OpenEditorAsync(ActivePanel, path, internalEditor: true) is { } editor)
        {
            Dispatcher.UIThread.Post(() =>
            {
                var area = editor.Editor.TextArea;
                area.Caret.Line = Math.Clamp(line, 1, Math.Max(editor.Editor.Document.LineCount, 1));
                area.Caret.BringCaretToView();
            }, DispatcherPriority.Background);
        }
    }

    private void ShowLanguageProblems()
    {
        var language = _services.Language;
        if (OptionsWindow.LanguageTrouble(language, warnings: false) is not { } text)
        {
            return;
        }

        if (language.Broken > 0)
        {
            ShowNotice(text, 15, (AppStrings.Opt_Language_Show, () => _ = new LanguageProblemsWindow(language, FileClipboardAccess) { EditFileAt = EditLanguageFileAt }.ShowDialog(this)));
        }
        else
        {
            ShowNotice(text, 15);
        }
    }
}

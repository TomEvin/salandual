// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.App/Viewer/ViewerWindow.Blame.cs

using Avalonia.Input;
using Salandual.App.Resources;
using Salandual.Core.Resources;
using Salandual.Core.Git;

namespace Salandual.App.Viewer;

internal sealed record ViewerBlameColumn(GitBlame Blame, Func<GitBlameRun, string> Label);

internal sealed class ViewerBlame
{
    public required ViewerBlameColumn Column { get; init; }

    public required Func<GitBlameRun, string> Words { get; init; }

    public required Action<GitCommit> OpenCommit { get; init; }

    public required Action<GitCommit> Before { get; init; }
}

internal sealed partial class ViewerWindow
{
    private bool _blameShown = true;
    private bool _gitChord;

    internal bool BlameShown => _text.Blame is not null;

    private void ApplyBlame()
        => _text.Blame = _request.Blame is { } blame && _blameShown && string.Equals(_path, _request.Path, StringComparison.OrdinalIgnoreCase) ? blame.Column : null;

    internal void ToggleBlame()
    {
        _blameShown = !_blameShown;
        ApplyBlame();
        UpdateStatus();
    }

    internal GitBlameRun? CaretRun
        => _text.Blame is { } column && _text.CaretLineNumber is { } line ? column.Blame.RunAt((int)Math.Min(line, int.MaxValue)) : null;

    private bool BlameKey(Key key, KeyModifiers mods)
    {
        if (_request.Blame is not { } blame || _hexMode)
        {
            return false;
        }

        if (_gitChord)
        {
            _gitChord = false;
            if (key == Key.W && mods == KeyModifiers.None)
            {
                ToggleBlame();
                return true;
            }
        }

        if (key == Key.G && mods == (KeyModifiers.Control | KeyModifiers.Shift))
        {
            _gitChord = true;
            return true;
        }

        if (_text.Blame is null)
        {
            return false;
        }

        var page = Math.Max(1, (int)(_text.Bounds.Height / _text.RowHeight) - 1);
        switch (key)
        {
            case Key.Up when mods == KeyModifiers.None:
                _text.MoveCaretLines(-1);
                return true;
            case Key.Down when mods == KeyModifiers.None:
                _text.MoveCaretLines(1);
                return true;
            case Key.PageUp when mods == KeyModifiers.None:
                _text.MoveCaretLines(-page);
                return true;
            case Key.PageDown when mods == KeyModifiers.None:
                _text.MoveCaretLines(page);
                return true;
            case Key.Enter when mods == KeyModifiers.None:
                if (CaretRun is { Commit: { } commit })
                {
                    blame.OpenCommit(commit);
                }
                else
                {
                    ShowStatusNote(AppStrings.Viewer_Blame_NotCommitted);
                }

                return true;
            case Key.Left when mods == KeyModifiers.Alt:
                if (CaretRun is { Commit: { } line })
                {
                    if (line.Parents.Count == 0)
                    {
                        ShowStatusNote(Loc.Format(AppStrings.Viewer_Blame_FirstCommit, line.Short));
                    }
                    else
                    {
                        blame.Before(line);
                    }
                }
                else
                {
                    ShowStatusNote(AppStrings.Viewer_Blame_NotCommitted);
                }

                return true;
            default:
                return false;
        }
    }

    private string? BlameWords() => _request.Blame is { } blame && CaretRun is { } run ? blame.Words(run) : null;
}

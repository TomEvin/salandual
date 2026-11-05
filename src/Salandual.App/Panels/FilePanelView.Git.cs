// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.App/Panels/FilePanelView.Git.cs

using Avalonia.Media;
using Salandual.Core.Git;
using Salandual.Core.Vfs;

namespace Salandual.App.Panels;

public sealed partial class FilePanelView
{
    private GitStates? _git;
    private bool _gitColors = true;
    private (string Path, GitStatus? Status)? _gitShown;

    public bool GitColors { get => _gitColors; set => Restyle(ref _gitColors, value); }

    private bool _gitColumnAuto = true;

    public bool GitColumnAuto { get => _gitColumnAuto; set => Restyle(ref _gitColumnAuto, value); }

    private bool GitColumnShown(bool chosen) => _gitColumnAuto ? GitNow() is not null : chosen;

    internal GitStatus? GitNow()
    {
        if (_git is null || _panel is null || _packedSize is not null || _panel.Path.Length == 0)
        {
            return null;
        }

        if (_gitShown is { } shown && shown.Path == _panel.Path)
        {
            return shown.Status;
        }

        var status = _git.StatusOf(_panel.Path);
        _gitShown = (_panel.Path, status);
        return status;
    }

    private Func<FileEntry, GitState>? _gitStateFor;

    public Func<FileEntry, GitState>? GitStateFor
    {
        get => _gitStateFor;
        set
        {
            _gitStateFor = value;
            ClearTexts();
            InvalidateVisual();
        }
    }

    private (GitState State, bool Inside) GitOf(in FileEntry e, bool up)
    {
        if (GitStateFor is { } own)
        {
            return (up || e.IsDirectory ? GitState.None : own(e), false);
        }

        if (up || GitNow() is not { } status || _panel is null)
        {
            return (GitState.None, false);
        }

        var path = System.IO.Path.IsPathFullyQualified(e.Name) ? e.Name : System.IO.Path.Join(_panel.Path, e.Name);
        var state = status.Of(path, e.IsDirectory, out var inside);
        return (state, inside);
    }

    internal GitState GitStateOf(int index) => GitOf(_panel!.Items[index], _panel.IsUp(index)).State;

    public Func<FileEntry, bool>? SubmoduleFor { get; set; }

    private bool IsSubmodule(in FileEntry e, bool up)
    {
        if (up)
        {
            return false;
        }

        if (GitStateFor is not null)
        {
            return SubmoduleFor?.Invoke(e) == true;
        }

        if (!e.IsDirectory || GitNow() is not { Submodules.Count: > 0 } status || _panel is null)
        {
            return false;
        }

        return status.SubmoduleAt(System.IO.Path.IsPathFullyQualified(e.Name) ? e.Name : System.IO.Path.Join(_panel.Path, e.Name)) is not null;
    }

    private string GitLetterOf(in FileEntry e, bool up)
    {
        if (IsSubmodule(e, up))
        {
            return "S";
        }

        var (state, inside) = GitOf(e, up);
        return GitLetter(state, inside);
    }

    internal string GitLetterAt(int index) => GitLetterOf(_panel!.Items[index], _panel.IsUp(index));

    private IBrush? GitBrush(GitState state) => state switch
    {
        GitState.Changed => _b.GitChanged,
        GitState.Staged or GitState.Added => _b.GitStaged,
        GitState.New => _b.GitNew,
        GitState.Deleted => _b.GitDeleted,
        GitState.Conflicted => _b.GitConflicted,
        GitState.Ignored => _b.GitIgnored,
        _ => null,
    };

    private const double GitTagPadding = 10;

    private readonly Dictionary<IBrush, IBrush> _gitTags = [];

    private IBrush? GitTag(int index)
    {
        if (!_gitColors || _panel is not { } panel || index >= panel.Count || panel.IsHeading(index)
            || GitBrush(GitOf(panel.Items[index], panel.IsUp(index)).State) is not ISolidColorBrush state)
        {
            return null;
        }

        if (!_gitTags.TryGetValue(state, out var tag))
        {
            tag = new Avalonia.Media.Immutable.ImmutableSolidColorBrush(state.Color, 0.2);
            _gitTags[state] = tag;
        }

        return tag;
    }

    internal Color? GitTagOf(int index)
    {
        EnsureTheme();
        return GitTag(index) is ISolidColorBrush b ? b.Color : null;
    }

    internal static string GitLetter(GitState state, bool inside) => inside && state != GitState.Ignored ? "•" : state switch
    {
        GitState.Changed or GitState.Staged => "M",
        GitState.Added => "A",
        GitState.Deleted => "D",
        GitState.New => "?",
        GitState.Conflicted => "U",
        GitState.Ignored => "!",
        _ => "",
    };

    private (IBrush Brush, Typeface Face)? GitNameLook(in FileEntry e, bool up, Typeface face)
    {
        if (!_gitColors)
        {
            return null;
        }

        var (state, _) = GitOf(e, up);
        return GitBrush(state) is { } brush ? (brush, state == GitState.Conflicted ? _bold : face) : null;
    }

    private IBrush RowInk(in FileEntry e, bool up, bool selected, bool cut)
    {
        if (selected)
        {
            return _b.SelectedText;
        }

        return !cut && _gitColors && GitBrush(GitOf(e, up).State) is { } git ? git : _b.SecondaryText;
    }

    internal IBrush RowInkOf(int index)
    {
        EnsureTheme();
        var e = _panel!.Items[index];
        return RowInk(e, _panel.IsUp(index), _panel.IsSelected(index), false);
    }

    private void OnGitChanged(object? sender, EventArgs e)
    {
        _gitShown = null;
        if (_panel is not null && _git?.TopFor(_panel.Path) is not null)
        {
            ClearTexts();
            InvalidateVisual();
        }
    }

    private void GitRelisted()
    {
        _gitShown = null;
        if (_panel is { Path.Length: > 0 } panel && _packedSize is null)
        {
            _git?.Relisted(panel.Path);
        }
    }
}

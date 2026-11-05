// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.App/Panels/DirectoryLine.BackForward.cs

using Avalonia;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Avalonia.Threading;
using Salandual.App.Theme;

namespace Salandual.App.Panels;

public sealed partial class DirectoryLine
{
    private const double NavWidth = 22, NavListWidth = 12;

    internal static readonly TimeSpan HoldTime = TimeSpan.FromMilliseconds(500);

    private bool _showNav, _navList = true, _canBack, _canForward;
    private Rect _back, _backList, _forward, _forwardList;
    private DispatcherTimer? _holdTimer;
    private bool _holdForward;

    public bool ShowBackForward
    {
        get => _showNav;
        set => Set(ref _showNav, value);
    }

    public bool StepsListButtons
    {
        get => _navList;
        set => Set(ref _navList, value);
    }

    public bool HoldForList { get; set; }

    public bool CanGoBack
    {
        get => _canBack;
        set => Set(ref _canBack, value);
    }

    public bool CanGoForward
    {
        get => _canForward;
        set => Set(ref _canForward, value);
    }

    public string? BackTip { get; set; }

    public string? ForwardTip { get; set; }

    public event EventHandler<bool>? StepRequested;

    public event EventHandler<(bool Forward, Rect At)>? StepsListRequested;

    internal Rect BackButtonBounds => _back;

    internal Rect BackListBounds => _backList;

    internal Rect ForwardButtonBounds => _forward;

    internal Rect ForwardListBounds => _forwardList;

    private void Set(ref bool field, bool value)
    {
        if (field != value)
        {
            field = value;
            InvalidateVisual();
        }
    }

    private double DrawBackForward(DrawingContext context, Size size, double x)
    {
        _back = _backList = _forward = _forwardList = default;
        if (!_showNav)
        {
            return x;
        }

        var top = (size.Height - ButtonSize) / 2;
        _back = new Rect(x, top, NavWidth, ButtonSize);
        x = _back.Right;
        if (_navList)
        {
            _backList = new Rect(x, top, NavListWidth, ButtonSize);
            x = _backList.Right;
        }

        _forward = new Rect(x + 2, top, NavWidth, ButtonSize);
        x = _forward.Right;
        if (_navList)
        {
            _forwardList = new Rect(x, top, NavListWidth, ButtonSize);
            x = _forwardList.Right;
        }

        DrawNav(context, _back, _backList, "Icon.ChevronLeft", _canBack, Zone.Back, Zone.BackList);
        DrawNav(context, _forward, _forwardList, "Icon.ChevronRight", _canForward, Zone.Forward, Zone.ForwardList);
        return x + 4;
    }

    private void DrawNav(DrawingContext context, Rect button, Rect list, string glyph, bool on, Zone buttonZone, Zone listZone)
    {
        var hover = on && (_hoverZone == buttonZone || _hoverZone == listZone);
        if (hover)
        {
            context.FillRectangle(_hoverBg, button.Union(list.Width > 0 ? list : button), 4);
        }

        var pen = new ImmutablePen((on ? _text : _track).ToImmutable(), 1.5);
        AppIcons.Draw(context, glyph, button.Center - new Point(8, 8), pen);
        if (list.Width > 0)
        {
            DrawChevron(context, new ImmutablePen((on ? _dim : _track).ToImmutable(), 1.2), list.Center, 2.5);
        }
    }

    private Zone NavZoneAt(Point p)
        => _back.Contains(p) ? Zone.Back
            : _backList.Contains(p) ? Zone.BackList
            : _forward.Contains(p) ? Zone.Forward
            : _forwardList.Contains(p) ? Zone.ForwardList
            : Zone.None;

    private string? NavTip(Zone zone) => zone switch
    {
        Zone.Back => BackTip,
        Zone.Forward => ForwardTip,
        Zone.BackList or Zone.ForwardList => Salandual.App.Resources.AppStrings.Nav_ListTip,
        _ => null,
    };

    private void NavClick(Zone zone)
    {
        switch (zone)
        {
            case Zone.Back when _canBack:
                StepRequested?.Invoke(this, false);
                break;
            case Zone.Forward when _canForward:
                StepRequested?.Invoke(this, true);
                break;
            case Zone.BackList when _canBack:
                StepsListRequested?.Invoke(this, (false, _back.Union(_backList)));
                break;
            case Zone.ForwardList when _canForward:
                StepsListRequested?.Invoke(this, (true, _forward.Union(_forwardList)));
                break;
        }
    }

    private bool StartHold(Zone zone, IPointer pointer)
    {
        if (!HoldForList || zone is not (Zone.Back or Zone.Forward) || !(zone == Zone.Back ? _canBack : _canForward))
        {
            return false;
        }

        _holdForward = zone == Zone.Forward;
        _holdTimer?.Stop();
        _holdTimer = new DispatcherTimer { Interval = HoldTime };
        _holdTimer.Tick += (_, _) =>
        {
            _holdTimer?.Stop();
            _holdTimer = null;
            Held(_holdForward);
        };
        _holdTimer.Start();
        pointer.Capture(this);
        return true;
    }

    internal void Held(bool forward)
        => StepsListRequested?.Invoke(this, (forward, forward ? _forward.Union(_forwardList.Width > 0 ? _forwardList : _forward) : _back.Union(_backList.Width > 0 ? _backList : _back)));

    private bool EndHold(IPointer pointer, bool cancelled)
    {
        if (_holdTimer is not { } timer)
        {
            return false;
        }

        timer.Stop();
        _holdTimer = null;
        pointer.Capture(null);
        if (!cancelled)
        {
            StepRequested?.Invoke(this, _holdForward);
        }

        return true;
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (EndHold(e.Pointer, cancelled: false))
        {
            e.Handled = true;
        }
    }

}

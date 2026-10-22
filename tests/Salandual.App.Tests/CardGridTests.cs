// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : tests/Salandual.App.Tests/CardGridTests.cs

using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Salandual.App.Controls;

namespace Salandual.App.Tests;

public sealed class CardGridTests
{
    private static Border Card(double height, int lane = 0)
    {
        var card = new Border { MinHeight = height };
        CardGrid.SetLane(card, lane);
        return card;
    }

    private static (CardGrid Grid, Border Video, Border Audio1, Border Audio2, Border Subtitles, Border Tags) Film()
    {
        var (video, audio1, audio2, subtitles, tags) = (Card(120), Card(80, 1), Card(80, 1), Card(60, 2), Card(100, 2));
        var grid = new CardGrid { Children = { video, audio1, audio2, subtitles, tags } };
        return (grid, video, audio1, audio2, subtitles, tags);
    }

    private static void Lay(CardGrid grid, double width)
    {
        grid.Measure(new Size(width, double.PositiveInfinity));
        grid.Arrange(new Rect(grid.DesiredSize));
    }

    [AvaloniaFact]
    public void Ultrawide_HasAColumnEach_ForTheVideoItsAudioAndTheRest()
    {
        var (grid, video, audio1, audio2, subtitles, tags) = Film();
        Lay(grid, 1224);

        Assert.Equal(3, grid.Columns);
        Assert.Equal(new Rect(0, 0, 400, 120), video.Bounds);
        Assert.Equal(new Rect(412, 0, 400, 80), audio1.Bounds);
        Assert.Equal(new Rect(412, 92, 400, 80), audio2.Bounds);
        Assert.Equal(new Rect(824, 0, 400, 60), subtitles.Bounds);
        Assert.Equal(new Rect(824, 72, 400, 100), tags.Bounds);
        Assert.Equal(172, grid.DesiredSize.Height);
    }

    [AvaloniaFact]
    public void Wide_PutsTheVideoAndTheRestLeft_AndTheAudioTracksRight()
    {
        var (grid, video, audio1, audio2, subtitles, tags) = Film();
        Lay(grid, 812);

        Assert.Equal(2, grid.Columns);
        Assert.Equal(new Rect(0, 0, 400, 120), video.Bounds);
        Assert.Equal(new Rect(0, 132, 400, 60), subtitles.Bounds);
        Assert.Equal(new Rect(0, 204, 400, 100), tags.Bounds);
        Assert.Equal(new Rect(412, 0, 400, 80), audio1.Bounds);
        Assert.Equal(new Rect(412, 92, 400, 80), audio2.Bounds);
        Assert.Equal(304, grid.DesiredSize.Height);
    }

    [AvaloniaFact]
    public void Narrow_IsOneColumn_InTheReportsOrder()
    {
        var (grid, video, audio1, audio2, subtitles, tags) = Film();
        Lay(grid, 400);

        Assert.Equal(1, grid.Columns);
        var cards = new[] { video, audio1, audio2, subtitles, tags };
        Assert.Equal([0, 132, 224, 316, 388], cards.Select(c => c.Bounds.Top));
        Assert.All(cards, c => Assert.Equal(0, c.Bounds.Left));
    }

    [AvaloniaFact]
    public void AVideoWithoutAudio_HasNoEmptyColumn()
    {
        var (video, tags) = (Card(120), Card(100, 2));
        var grid = new CardGrid { Children = { video, tags } };
        Lay(grid, 1224);

        Assert.Equal(2, grid.Columns);
        Assert.Equal(new Rect(0, 0, 606, 120), video.Bounds);
        Assert.Equal(new Rect(618, 0, 606, 120), tags.Bounds);
    }

    [AvaloniaFact]
    public void Music_KeepsItsCardsSideBySide_EvenUltrawide()
    {
        var (audio, tags) = (Card(80), Card(100));
        var grid = new CardGrid { Children = { audio, tags } };
        Lay(grid, 1224);

        Assert.Equal(new Rect(0, 0, 606, 100), audio.Bounds);
        Assert.Equal(new Rect(618, 0, 606, 100), tags.Bounds);
    }
}

// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.App/Operations/OperationsChip.cs

using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Salandual.App.Options;
using Salandual.App.Resources;
using Salandual.Core.Operations;
using Salandual.Core.Resources;

namespace Salandual.App.Operations;

public sealed class OperationsChip : Border
{
    private readonly TextBlock _text = OptionsUi.Text("", 12);
    private readonly TextBlock _percent = OptionsUi.Text("", 12);
    private readonly TextBlock _more = OptionsUi.Text("", 12, brush: "Brush.TertiaryText");
    private readonly Border _dot = new() { Width = 8, Height = 8, CornerRadius = new CornerRadius(4), VerticalAlignment = VerticalAlignment.Center };
    private readonly ProgressBar _bar = new() { Width = 56, Height = 4, MinHeight = 4, Minimum = 0, Maximum = 100, VerticalAlignment = VerticalAlignment.Center };

    public OperationsChip()
    {
        Height = 26;
        Padding = new Thickness(12, 0);
        Margin = new Thickness(6, 0, 8, 0);
        CornerRadius = new CornerRadius(13);
        BorderThickness = new Thickness(1);
        VerticalAlignment = VerticalAlignment.Center;
        Cursor = new Cursor(StandardCursorType.Hand);
        IsVisible = false;
        Child = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { _dot, _text, _bar, _percent, _more } };
        Normal();
        Avalonia.Automation.AutomationProperties.SetName(this, AppStrings.Ops_Title);
    }

    public OperationJob? Asking { get; private set; }

    internal string Text => _text.Text ?? "";

    internal string Look { get; private set; } = "normal";

    public void Update(IReadOnlyList<OperationJob> jobs)
    {
        IsVisible = jobs.Count > 0;
        if (jobs.Count == 0)
        {
            return;
        }

        var queued = jobs.Count(j => j.State == JobState.Queued);
        var live = jobs.Where(j => !j.IsFinished && j.State != JobState.Queued).ToList();
        Asking = live.FirstOrDefault(j => j.Question is ErrorQuestion or PermanentDeleteQuestion) ?? live.FirstOrDefault(j => j.Question is not null);
        _more.Text = (queued > 0 ? "· " + Loc.Format(AppStrings.Chip_Queued, queued) : "") + (live.Count > 1 ? " · " + Loc.Format(AppStrings.Chip_More, live.Count - 1) : "");
        _more.IsVisible = _more.Text.Length > 0;

        if (Asking is { } asking)
        {
            var error = asking.Question is ErrorQuestion or PermanentDeleteQuestion;
            SetLook(error ? "error" : "ask");
            _text.Text = Loc.Format(error ? AppStrings.Chip_Problem : asking.Question is CheckQuestion ? AppStrings.Chip_Question : AppStrings.Chip_FileExists, Lower(asking.Title));
            _bar.IsVisible = _percent.IsVisible = false;
            return;
        }

        if ((live.FirstOrDefault(j => j.IsActive) ?? live.FirstOrDefault()) is { } shown)
        {
            SetLook("normal");
            var percent = (int)(shown.Progress.Fraction * 100);
            _text.Text = shown.State == JobState.Paused ? Loc.Format(AppStrings.Chip_Paused, shown.Title) : shown.State == JobState.Waiting ? Loc.Format(AppStrings.Chip_Waiting, shown.Title) : shown.Title;
            _bar.Value = percent;
            _percent.Text = percent.ToString(CultureInfo.CurrentCulture) + " %";
            _bar.IsVisible = _percent.IsVisible = true;
            return;
        }

        var problems = jobs.Where(j => j.IsFinished).Sum(j => j.Issues.Count);
        SetLook(problems > 0 ? "issues" : "done");
        _text.Text = problems > 0 ? Loc.Format(AppStrings.Chip_NotDone, problems) : queued > 0 ? AppStrings.Chip_QueueWaiting : AppStrings.Chip_AllDone;
        _bar.IsVisible = _percent.IsVisible = false;
    }

    private void Normal() => SetLook("normal");

    private void SetLook(string look)
    {
        if (look == Look && _styled)
        {
            return;
        }

        Look = look;
        _styled = true;
        var (back, border, text) = look switch
        {
            "error" => ("Brush.DangerBackground", "Brush.Danger", "Brush.Text"),
            "ask" => ("Brush.WarningBackground", "Brush.Warning", "Brush.Text"),
            "issues" => ("Brush.PanelBackground", "Brush.Border", "Brush.Danger"),
            "done" => ("Brush.PanelBackground", "Brush.Border", "Brush.SecondaryText"),
            _ => ("Brush.PanelBackground", "Brush.Border", "Brush.Text"),
        };
        this.Res(BackgroundProperty, back).Res(BorderBrushProperty, border);
        _text.Res(TextBlock.ForegroundProperty, text);
        _dot.IsVisible = look is "error" or "ask";
        _dot.Res(Border.BackgroundProperty, look == "error" ? "Brush.Danger" : "Brush.Warning");
    }

    private bool _styled;

    private static string Lower(string title) => title.Length > 0 ? char.ToLowerInvariant(title[0]) + title[1..] : title;
}

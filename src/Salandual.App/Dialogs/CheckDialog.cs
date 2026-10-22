// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.App/Dialogs/CheckDialog.cs

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Salandual.App.Controls;
using Salandual.App.Options;
using Salandual.App.Resources;
using Salandual.App.Theme;
using Salandual.Core.Operations;

namespace Salandual.App.Dialogs;

public sealed class CheckDialog : Window
{
    private const string MinimizeGlyph = "Icon.Minimize";
    private readonly CheckBox _forAll;

    public CheckDialog(OperationJob job, CheckQuestion question)
    {
        Title = job.Title;
        Width = 520;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        this.Res(BackgroundProperty, "Brush.PanelBackground");

        var (heading, message, yes) = CheckTexts.For(job, question);
        var text = new TextBlock { FontSize = 13, TextWrapping = TextWrapping.Wrap };
        foreach (var inline in message)
        {
            text.Inlines!.Add(inline);
        }

        var folder = Path.GetDirectoryName(question.Path) ?? question.Path;
        var where = OptionsUi.Text(folder, 12, brush: "Brush.SecondaryText");
        where.TextTrimming = TextTrimming.PathSegmentEllipsis;
        var body = new StackPanel { Spacing = 10, Margin = new Thickness(20, 6, 20, 16) };
        body.Children.Add(OptionsUi.Text(heading, 17, FontWeight.SemiBold));
        body.Children.Add(text);
        body.Children.Add(where);

        _forAll = new CheckBox { Content = AppStrings.Check_ForTheRest, VerticalAlignment = VerticalAlignment.Center };
        Yes = new Button { Content = yes, MinWidth = 84, Padding = new Thickness(14, 5), HorizontalContentAlignment = HorizontalAlignment.Center };
        Yes.Classes.Add("accent");
        Yes.Click += (_, _) => Answer(AnswerKind.Yes);
        Skip = new Button { Content = AppStrings.Ops_Skip, MinWidth = 84, Padding = new Thickness(14, 5), HorizontalContentAlignment = HorizontalAlignment.Center };
        Skip.Click += (_, _) => Answer(AnswerKind.Skip);
        var minimize = new Button { Content = AppStrings.Conflict_Minimize, Padding = new Thickness(14, 5) };
        ToolTip.SetTip(minimize, AppStrings.Conflict_MinimizeTip);
        minimize.Click += (_, _) => Close(ConflictChoice.Minimized);
        var cancel = new Button { Content = CheckTexts.CancelJob(job), Padding = new Thickness(14, 5) };
        cancel.Click += (_, _) => Close(new ConflictChoice(new OperationAnswer(AnswerKind.Cancel)));

        var footerRow = new DockPanel();
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { minimize, cancel, Skip, Yes } };
        DockPanel.SetDock(buttons, Dock.Right);
        footerRow.Children.Add(buttons);
        footerRow.Children.Add(_forAll);
        var footer = new Border { Padding = new Thickness(20, 12), BorderThickness = new Thickness(0, 1, 0, 0), Child = footerRow }
            .Res(Border.BackgroundProperty, "Brush.WindowBackground").Res(Border.BorderBrushProperty, "Brush.Border");

        var root = new DockPanel();
        DockPanel.SetDock(footer, Dock.Bottom);
        root.Children.Add(footer);
        root.Children.Add(body);
        var titleMinimize = TitleBarParts.Button(AppIcons.Glyph(MinimizeGlyph, 16, 16), AppStrings.Conflict_MinimizeTitleTip, AppStrings.Conflict_Minimize);
        titleMinimize.Width = 40;
        titleMinimize.Click += (_, _) => Close(ConflictChoice.Minimized);
        Content = TitleBarParts.ForDialog(this, root, titleMinimize);
        Opened += (_, _) => Yes.Focus();
    }

    internal Button Yes { get; }

    internal Button Skip { get; }

    internal CheckBox ForAll => _forAll;

    private void Answer(AnswerKind kind) => Close(new ConflictChoice(new OperationAnswer(kind, _forAll.IsChecked == true)));

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Handled)
        {
            return;
        }

        if (e.Key == Key.Escape)
        {
            Close(ConflictChoice.Minimized);
            e.Handled = true;
        }
    }
}

internal static class CheckTexts
{
    public static (string Heading, Inline[] Message, string Yes) For(OperationJob job, CheckQuestion question)
    {
        var name = new Run(Path.GetFileName(question.Path)) { FontWeight = FontWeight.SemiBold };
        var move = job.Kind == OperationKind.Move;
        return question.Kind switch
        {
            CheckKind.DeleteNonEmptyFolder => (AppStrings.Check_NonEmpty_Heading, Rich.Build(AppStrings.Check_NonEmpty_Text, null, name), AppStrings.Check_Delete),
            CheckKind.DeleteHiddenOrSystem => (AppStrings.Check_DeleteHidden_Heading, Rich.Build(AppStrings.Check_Hidden_Text, null, name), AppStrings.Check_Delete),
            CheckKind.ReplaceHiddenOrSystem => (AppStrings.Check_ReplaceHidden_Heading, Rich.Build(AppStrings.Check_Hidden_Text, null, name), AppStrings.Check_Replace),
            _ => (move ? AppStrings.Check_Merge_HeadingMove : AppStrings.Check_Merge_Heading, Rich.Build(move ? AppStrings.Check_Merge_TextMove : AppStrings.Check_Merge_Text, null, name), move ? AppStrings.Check_MoveInto : AppStrings.Check_CopyInto),
        };
    }

    public static string CancelJob(OperationJob job) => job.Kind switch
    {
        OperationKind.Move => AppStrings.Conflict_CancelMove,
        OperationKind.Copy => AppStrings.Conflict_CancelCopy,
        _ => AppStrings.Check_CancelDelete,
    };
}

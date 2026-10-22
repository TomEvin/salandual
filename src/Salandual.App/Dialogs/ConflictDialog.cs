// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.App/Dialogs/ConflictDialog.cs

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
using Salandual.Core.Listing;
using Salandual.Core.Operations;
using Salandual.Core.Resources;

namespace Salandual.App.Dialogs;

public sealed record ConflictChoice(OperationAnswer? Answer)
{
    public static ConflictChoice Minimized { get; } = new((OperationAnswer?)null);
}

public sealed class ConflictDialog : Window
{
    private const string MinimizeGlyph = "Icon.Minimize";
    private readonly CheckBox _forAll;
    private readonly AnswerKind _primary;
    private readonly Dictionary<Key, AnswerKind> _accessKeys = [];

    public ConflictDialog(OperationJob job, ConflictQuestion question, DisplayFormat format)
    {
        Title = job.Title;
        Width = 640;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        this.Res(BackgroundProperty, "Brush.PanelBackground");

        var newer = question.SourceInfo.LastWriteUtc > question.TargetInfo.LastWriteUtc;
        _primary = newer ? AnswerKind.Overwrite : AnswerKind.Skip;
        var targetFolder = Path.GetDirectoryName(question.Target) ?? question.Target;
        var sourceFolder = Path.GetDirectoryName(question.Source) ?? question.Source;

        var heading = OptionsUi.Text(AppStrings.Conflict_Heading, 17, FontWeight.SemiBold);
        var purpose = new TextBlock { FontSize = 13, TextWrapping = TextWrapping.Wrap };
        purpose.Res(TextBlock.ForegroundProperty, "Brush.SecondaryText");
        Rich.Add(purpose.Inlines!, job.Kind == OperationKind.Move ? AppStrings.Conflict_MovingInto : AppStrings.Conflict_CopyingInto, null,
            new Run(Path.GetFileName(targetFolder) is { Length: > 0 } n ? n : targetFolder) { FontWeight = FontWeight.SemiBold },
            new Run(Root(targetFolder)));

        var name = new Border
        {
            Padding = new Thickness(10, 6),
            CornerRadius = new CornerRadius(4),
            Child = OptionsUi.Text(Path.GetFileName(question.Target), 13).Also(t => t.Res(TextBlock.FontFamilyProperty, "Font.Mono")),
        }.Res(Border.BackgroundProperty, "Brush.Hover");

        var cards = new Grid { ColumnDefinitions = new ColumnDefinitions("*,10,*") };
        var left = Card(job.Kind == OperationKind.Move ? AppStrings.Conflict_Card_Moving : AppStrings.Conflict_Card_Copying, question.SourceInfo, Loc.Format(AppStrings.Conflict_FolderOn, Path.GetFileName(sourceFolder), Root(sourceFolder)), TagFor(question.SourceInfo, question.TargetInfo), format);
        var right = Card(AppStrings.Conflict_Card_There, question.TargetInfo, Loc.Format(AppStrings.Conflict_FolderOn, Path.GetFileName(targetFolder), Root(targetFolder)), TagFor(question.TargetInfo, question.SourceInfo), format);
        Grid.SetColumn(right, 2);
        cards.Children.Add(left);
        cards.Children.Add(right);

        var choices = new Grid { ColumnDefinitions = new ColumnDefinitions("*,8,*,8,*,8,*") };
        var dot = Path.GetFileNameWithoutExtension(question.Target) + " (2)" + Path.GetExtension(question.Target);
        (AnswerKind Kind, string Label, string Help)[] options =
        [
            (AnswerKind.Overwrite, AppStrings.Conflict_Choice_Overwrite, job.Kind == OperationKind.Move ? AppStrings.Conflict_Overwrite_Moving : AppStrings.Conflict_Overwrite_Copying),
            (AnswerKind.Skip, AppStrings.Conflict_Choice_Skip, AppStrings.Conflict_Skip_Help),
            (AnswerKind.KeepBoth, AppStrings.Conflict_Choice_KeepBoth, Loc.Format(AppStrings.Conflict_KeepBoth_Help, dot)),
            (AnswerKind.OverwriteOlder, AppStrings.Conflict_Choice_IfNewer, AppStrings.Conflict_IfNewer_Help),
        ];
        for (var i = 0; i < options.Length; i++)
        {
            var (kind, label, help) = options[i];

            var mark = label.IndexOf('_', StringComparison.Ordinal);
            var (before, key, after) = mark >= 0 && mark + 1 < label.Length
                ? (label[..mark], label[(mark + 1)..(mark + 2)], label[(mark + 2)..])
                : ("", "", label);
            if (key.Length == 1 && Enum.TryParse<Key>(key.ToUpperInvariant(), out var accessKey) && accessKey is >= Key.A and <= Key.Z)
            {
                _accessKeys[accessKey] = kind;
            }

            var button = Choice(before, key, after, help, kind == _primary);
            button.Click += (_, _) => Answer(kind);
            Avalonia.Automation.AutomationProperties.SetName(button, before + key + after);
            Grid.SetColumn(button, i * 2);
            choices.Children.Add(button);
            Choices[kind] = button;
        }

        var remaining = job.ExpectedConflicts is { } expected ? expected - question.ConflictsSoFar : (int?)null;
        _forAll = new CheckBox
        {
            Content = remaining switch
            {
                > 0 => Loc.Plural(remaining.Value, AppStrings.Conflict_SameForOther_One, AppStrings.Conflict_SameForOther_Other),
                _ => AppStrings.Conflict_SameForRest,
            },
            VerticalAlignment = VerticalAlignment.Center,
            IsVisible = remaining is not 0,
        };

        var minimize = new Button { Content = AppStrings.Conflict_Minimize, MinWidth = 96, Padding = new Thickness(14, 5), HorizontalContentAlignment = HorizontalAlignment.Center };
        ToolTip.SetTip(minimize, AppStrings.Conflict_MinimizeTip);
        minimize.Click += (_, _) => Close(ConflictChoice.Minimized);
        var cancel = new Button { Content = job.Kind == OperationKind.Move ? AppStrings.Conflict_CancelMove : AppStrings.Conflict_CancelCopy, Padding = new Thickness(14, 5) };
        cancel.Click += (_, _) => Close(new ConflictChoice(new OperationAnswer(AnswerKind.Cancel)));
        CancelButton = cancel;
        MinimizeButton = minimize;

        var footerRow = new DockPanel();
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { minimize, cancel } };
        DockPanel.SetDock(buttons, Dock.Right);
        footerRow.Children.Add(buttons);
        footerRow.Children.Add(_forAll);
        var footer = new Border { Padding = new Thickness(20, 12), BorderThickness = new Thickness(0, 1, 0, 0), Child = footerRow }
            .Res(Border.BackgroundProperty, "Brush.WindowBackground").Res(Border.BorderBrushProperty, "Brush.Border");

        var body = new StackPanel { Spacing = 12, Margin = new Thickness(20, 6, 20, 16) };
        body.Children.Add(new StackPanel { Children = { heading, purpose } });
        body.Children.Add(name);
        body.Children.Add(cards);
        body.Children.Add(choices);

        var root = new DockPanel();
        DockPanel.SetDock(footer, Dock.Bottom);
        root.Children.Add(footer);
        root.Children.Add(body);

        var titleMinimize = TitleBarParts.Button(AppIcons.Glyph(MinimizeGlyph, 16, 16), AppStrings.Conflict_MinimizeTitleTip, AppStrings.Conflict_Minimize);
        titleMinimize.Width = 40;
        titleMinimize.Click += (_, _) => Close(ConflictChoice.Minimized);
        Content = TitleBarParts.ForDialog(this, root, titleMinimize);
        Opened += (_, _) => Choices[_primary].Focus();
    }

    internal Dictionary<AnswerKind, Button> Choices { get; } = [];

    internal CheckBox ForAll => _forAll;

    internal Button CancelButton { get; }

    internal Button MinimizeButton { get; }

    private void Answer(AnswerKind kind) => Close(new ConflictChoice(new OperationAnswer(kind, _forAll.IsChecked == true)));

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Handled || e.KeyModifiers is not (KeyModifiers.None or KeyModifiers.Shift))
        {
            return;
        }

        AnswerKind? kind = e.Key == Key.Enter ? _primary : _accessKeys.TryGetValue(e.Key, out var byKey) ? byKey : null;
        if (kind is { } k)
        {
            Answer(k);
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            Close(ConflictChoice.Minimized);
            e.Handled = true;
        }
    }

    private static string Root(string path) => (Platform.Windows.FileSystem.WindowsPath.GetRoot(path) ?? path).TrimEnd('\\');

    private static string? TagFor(ItemInfo a, ItemInfo b)
        => a.LastWriteUtc > b.LastWriteUtc ? AppStrings.Conflict_Newer
        : a.Size > b.Size ? AppStrings.Conflict_Larger
        : a.LastWriteUtc == b.LastWriteUtc && a.Size == b.Size ? AppStrings.Conflict_SameSizeTime : null;

    private static Control Card(string heading, ItemInfo info, string where, string? tag, DisplayFormat format)
    {
        var text = new StackPanel { Spacing = 1, VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(OptionsUi.Text(heading, 11, FontWeight.SemiBold, "Brush.TertiaryText"));
        text.Children.Add(OptionsUi.Text($"{format.Kilobytes(info.Size)} · {format.DateAndTime(info.LastWriteUtc.ToLocalTime())}", 13));
        text.Children.Add(OptionsUi.Text(where, 12, brush: "Brush.SecondaryText").Also(t => t.TextTrimming = TextTrimming.CharacterEllipsis));
        if (tag is not null)
        {
            text.Children.Add(Operations.Ui.Tag(tag).Also(b => { b.Margin = new Thickness(0, 2, 0, 0); b.HorizontalAlignment = HorizontalAlignment.Left; }));
        }

        var icon = new Border
        {
            Width = 40,
            Height = 40,
            CornerRadius = new CornerRadius(4),
            Margin = new Thickness(0, 0, 10, 0),
            VerticalAlignment = VerticalAlignment.Top,
            Child = AppIcons.Glyph("Icon.File", 22, 22).Also(g => g.HorizontalAlignment = HorizontalAlignment.Center),
        }.Res(Border.BackgroundProperty, "Brush.Hover");
        var row = new DockPanel();
        DockPanel.SetDock(icon, Dock.Left);
        row.Children.Add(icon);
        row.Children.Add(text);
        return new Border { Padding = new Thickness(11, 9), CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(1), Child = row }
            .Res(Border.BorderBrushProperty, "Brush.Border");
    }

    private static Button Choice(string before, string key, string after, string help, bool primary)
    {
        var label = new TextBlock { FontSize = 13, FontWeight = FontWeight.SemiBold };
        label.Inlines!.Add(new Run(before));
        label.Inlines.Add(new Run(key) { TextDecorations = TextDecorations.Underline });
        label.Inlines.Add(new Run(after));
        var helpText = OptionsUi.Text(help, 11, brush: "Brush.SecondaryText", wrap: true);
        var button = new Button
        {
            Content = new StackPanel { Spacing = 2, Children = { label, helpText } },
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Left,
            VerticalContentAlignment = VerticalAlignment.Top,
            Padding = new Thickness(9, 7),
            BorderThickness = new Thickness(primary ? 2 : 1),
        };
        if (primary)
        {
            button.Res(Button.BorderBrushProperty, "Brush.Accent");
        }

        return button;
    }
}

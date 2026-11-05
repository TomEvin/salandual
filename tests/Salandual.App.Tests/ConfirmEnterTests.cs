// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : tests/Salandual.App.Tests/ConfirmEnterTests.cs

using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Salandual.App.Dialogs;
using Salandual.Core.Settings;

namespace Salandual.App.Tests;

public sealed class ConfirmEnterTests
{
    private static SettingsService Settings => ((App)Application.Current!).Services.Settings;

    private static void With(Func<ConfirmationSettings, ConfirmationSettings> change, Action test)
    {
        var before = Settings.Current.Confirmations;
        Settings.Update(s => s with { Confirmations = change(s.Confirmations) });
        try
        {
            test();
        }
        finally
        {
            Settings.Update(s => s with { Confirmations = before });
        }
    }

    private static string Texts(Button button) => string.Join(" ", button.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text));

    [Fact]
    public void TheDefaults_AreYourAnswers()
    {
        var c = new ConfirmationSettings();
        Assert.True(c.EnterActs(ConfirmGroup.Create));
        Assert.True(c.EnterActs(ConfirmGroup.Save));
        Assert.False(c.EnterActs(ConfirmGroup.Delete));
        Assert.True(c.EnterActs(ConfirmGroup.Close));
    }

    [AvaloniaFact]
    public void ConfirmDialog_EnterFollowsTheGroup_NoGroupKeepsTheSecondAnswer()
    {
        With(c => new ConfirmationSettings(), () =>
        {
            var create = new ConfirmDialog("Create the folder?", "x", "Create", "Cancel", group: ConfirmGroup.Create);
            Assert.Same(create.Yes, create.EnterButton);
            Assert.True(create.Yes.IsDefault);
            Assert.Contains("accent", create.Yes.Classes);
            Assert.DoesNotContain("accent", create.No.Classes);

            var logs = new ConfirmDialog("Delete the logs?", "x", "Delete", "Cancel", group: ConfirmGroup.Delete);
            Assert.Same(logs.No, logs.EnterButton);

            var look = new ConfirmDialog("Keep the new look?", "x", "Revert", "Keep", "Cancel", ConfirmGroup.Save, actionIsYes: false);
            Assert.Same(look.No, look.EnterButton);

            var flat = new ConfirmDialog("Read on?", "x", "Read on", "Stop here");
            Assert.Same(flat.No, flat.EnterButton);
        });

        With(c => c with { EnterCreate = false, EnterSave = false, EnterDelete = true }, () =>
        {
            var create = new ConfirmDialog("t", "x", "Create", "Cancel", group: ConfirmGroup.Create);
            Assert.Same(create.No, create.EnterButton);
            var logs = new ConfirmDialog("t", "x", "Delete", "Cancel", group: ConfirmGroup.Delete);
            Assert.Same(logs.Yes, logs.EnterButton);
            var look = new ConfirmDialog("t", "x", "Revert", "Keep", "Cancel", ConfirmGroup.Save, actionIsYes: false);
            Assert.Same(look.Yes, look.EnterButton);
        });
    }

    [AvaloniaFact]
    public void ChoiceDialog_OnCancel_TheCancelAnswerTakesEnter()
    {
        List<Choice> choices = [new("cancel", "Cancel", "Esc", Key.Escape), new("delete", "Delete", "Enter", Key.Enter, Primary: true)];
        With(c => new ConfirmationSettings(), () =>
        {
            var dialog = new ChoiceDialog("Delete from the archive?", ["x"], choices, "cancel", group: ConfirmGroup.Delete);
            dialog.Show();
            Dispatcher.UIThread.RunJobs();
            var enter = Assert.Single(dialog.Buttons, b => b.IsDefault);
            Assert.Equal("cancel", enter.Tag);
            Assert.Contains("accent", enter.Classes);
            var delete = dialog.Buttons.Single(b => (string?)b.Tag == "delete");
            Assert.DoesNotContain("Enter", Texts(delete), StringComparison.Ordinal);
            Assert.DoesNotContain("accent", delete.Classes);
            dialog.Close();

            var plain = new ChoiceDialog("t", ["x"], choices, "cancel");
            Assert.Equal("delete", Assert.Single(plain.Buttons, b => b.IsDefault).Tag);
            var save = new ChoiceDialog("t", ["x"], [new("save", "Save", "Enter", Primary: true), new("cancel", "Cancel", "Esc")], "cancel", group: ConfirmGroup.Save);
            Assert.Equal("save", Assert.Single(save.Buttons, b => b.IsDefault).Tag);
        });
    }

    [AvaloniaFact]
    public void PutBackAndPackExists_FollowCreating()
    {
        With(c => c with { EnterCreate = false }, () =>
        {
            var putBack = new PutBackDialog("Notes.txt", "Old.zip");
            Assert.Contains("accent", putBack.Keep.Classes);
            Assert.DoesNotContain("accent", putBack.PutBack.Classes);
            Assert.Equal("Keep only the copy  Enter", putBack.Keep.Content);
            Assert.Equal("Put it back", putBack.PutBack.Content);

            var pack = new PackExistsDialog("Old.zip", "x", "Old (2).zip", canAdd: true);
            Assert.Contains("accent", pack.CancelButton.Classes);
            Assert.DoesNotContain("accent", pack.Ok.Classes);
        });

        With(c => new ConfirmationSettings(), () =>
        {
            var putBack = new PutBackDialog("Notes.txt", "Old.zip");
            Assert.Contains("accent", putBack.PutBack.Classes);
            Assert.Equal("Put it back  Enter", putBack.PutBack.Content);
            Assert.Contains("accent", new PackExistsDialog("Old.zip", "x", "Old (2).zip", canAdd: true).Ok.Classes);
        });
    }
}

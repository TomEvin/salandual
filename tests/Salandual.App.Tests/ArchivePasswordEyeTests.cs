// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : tests/Salandual.App.Tests/ArchivePasswordEyeTests.cs

using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Salandual.App.Dialogs;

namespace Salandual.App.Tests;

public sealed class ArchivePasswordEyeTests
{
    [AvaloniaFact]
    public void ThePasswordDialog_HasTheEye_AndNoCheckbox()
    {
        var dialog = new ArchivePasswordDialog("Holiday.7z", wrongBefore: false);
        dialog.Show();
        Dispatcher.UIThread.RunJobs();
        var box = dialog.PasswordBox;
        box.Text = "titok";
        box.Focus();
        Dispatcher.UIThread.RunJobs();

        Assert.Empty(dialog.GetVisualDescendants().OfType<CheckBox>());
        Assert.Contains("revealPasswordButton", box.Classes);
        var eye = box.GetVisualDescendants().OfType<ToggleButton>().Single();
        Assert.False(box.RevealPassword);
        eye.IsChecked = true;
        Dispatcher.UIThread.RunJobs();
        Assert.True(box.RevealPassword);
        eye.IsChecked = false;
        Dispatcher.UIThread.RunJobs();
        Assert.False(box.RevealPassword);

        if (Environment.GetEnvironmentVariable("SALANDUAL_CAPTURE_DIR") is { Length: > 0 } dir)
        {
            Directory.CreateDirectory(dir);
            dialog.CaptureRenderedFrame()!.Save(Path.Combine(dir, "password-eye.png"), Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
            eye.IsChecked = true;
            Dispatcher.UIThread.RunJobs();
            dialog.CaptureRenderedFrame()!.Save(Path.Combine(dir, "password-eye-shown.png"), Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
        }

        dialog.Close();
    }
}

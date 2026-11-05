// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.App/Options/OptionsWindow.Operations.cs

using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Salandual.App.Resources;
using Salandual.Core.Commands;
using Salandual.Core.Operations;
using Salandual.Core.Resources;
using Salandual.Core.Settings;
using static Salandual.App.Options.OptionsUi;

namespace Salandual.App.Options;

public sealed partial class OptionsWindow
{

    private void AddOperations()
    {
        const string page = "operations";
        var kw = AppStrings.Opt_Operations_Kw;
        var copy = AppStrings.OptSec_CopyMove;
        Add(page, copy, AppStrings.Opt_IfExists, AppStrings.Opt_IfExists_Desc, () => Segmented(
            [
                (ExistingFilePolicy.Ask, AppStrings.Opt_IfExists_Ask),
                (ExistingFilePolicy.OverwriteOlder, AppStrings.Opt_IfExists_Older),
                (ExistingFilePolicy.Overwrite, AppStrings.Opt_IfExists_Overwrite),
                (ExistingFilePolicy.Skip, AppStrings.Opt_IfExists_Skip),
                (ExistingFilePolicy.KeepBoth, AppStrings.Opt_IfExists_KeepBoth),
            ],
            Current.Operations.IfExists, v => UpdateOperations(o => o with { IfExists = v })), wide: true, keywords: kw);
        Add(page, copy, AppStrings.Opt_KeepFolderDates, AppStrings.Opt_KeepFolderDates_Desc,
            () => Toggle(Current.Operations.KeepFolderTimes, on => UpdateOperations(o => o with { KeepFolderTimes = on })), keywords: kw);
        Add(page, copy, AppStrings.Opt_KeepAttributes, AppStrings.Opt_KeepAttributes_Desc,
            () => Toggle(Current.Operations.KeepAttributes, on => UpdateOperations(o => o with { KeepAttributes = on })), keywords: kw);
        Add(page, copy, AppStrings.Opt_KeepStreams, AppStrings.Opt_KeepStreams_Desc,
            () => Toggle(Current.Operations.KeepStreams, on => UpdateOperations(o => o with { KeepStreams = on })), keywords: kw);
        Add(page, copy, AppStrings.Opt_Verify, AppStrings.Opt_Verify_Desc,
            () => Toggle(Current.Operations.Verify, on => UpdateOperations(o => o with { Verify = on })), keywords: kw);
        Add(page, copy, AppStrings.Opt_SkipEmpty, null,
            () => Toggle(Current.Operations.SkipEmptyFolders, on => UpdateOperations(o => o with { SkipEmptyFolders = on })), keywords: kw);
        Add(page, copy, AppStrings.Opt_CopyMask, AppStrings.Opt_CopyMask_Desc, CopyMaskEditor, keywords: kw);
        Add(page, copy, AppStrings.Opt_SpeedLimit, AppStrings.Opt_SpeedLimit_Desc, SpeedLimitEditor, keywords: kw);

        var queue = AppStrings.OptSec_Queue;
        Add(page, queue, AppStrings.Opt_EnterAction, AppStrings.Opt_EnterAction_Desc, () => Segmented(
            [(EnterAction.StartNow, AppStrings.Opt_EnterAction_Now), (EnterAction.AddToQueue, AppStrings.Opt_EnterAction_Queue)],
            Current.Operations.Enter, v => UpdateOperations(o => o with { Enter = v })), keywords: kw);
        Add(page, queue, AppStrings.Opt_NotifyDone, AppStrings.Opt_NotifyDone_Desc,
            () => Toggle(Current.Operations.NotifyWhenDone, on => UpdateOperations(o => o with { NotifyWhenDone = on })), keywords: kw);

        var running = AppStrings.OptSec_WhileRunning;
        Add(page, running, AppStrings.Opt_ProgressShow, AppStrings.Opt_ProgressShow_Desc, () => Segmented(
            [(ProgressShow.Window, AppStrings.Opt_ProgressShow_Window), (ProgressShow.Chip, AppStrings.Opt_ProgressShow_Chip)],
            Current.Operations.Progress, v => UpdateOperations(o => o with { Progress = v })), keywords: AppStrings.Opt_ProgressShow_Kw);
        Add(page, running, AppStrings.Opt_ProgressBlocks, AppStrings.Opt_ProgressBlocks_Desc,
            () => EnabledWhile(Toggle(Current.Operations.ProgressBlocks, on => UpdateOperations(o => o with { ProgressBlocks = on })), s => s.Operations.Progress == ProgressShow.Window), keywords: AppStrings.Opt_ProgressShow_Kw);
        Add(page, running, AppStrings.Opt_CloseWhenDone, AppStrings.Opt_CloseWhenDone_Desc,
            () => EnabledWhile(Toggle(Current.Operations.CloseWhenDone, on => UpdateOperations(o => o with { CloseWhenDone = on })), s => s.Operations.Progress == ProgressShow.Window), keywords: AppStrings.Opt_ProgressShow_Kw);
        Add(page, running, AppStrings.Opt_SameDisk, AppStrings.Opt_SameDisk_Desc, () => Combo(
            [(SameDiskRule.Wait, AppStrings.Opt_SameDisk_Wait), (SameDiskRule.HardDiskOnly, AppStrings.Opt_SameDisk_HardDisk), (SameDiskRule.Never, AppStrings.Opt_SameDisk_Never)],
            Current.Operations.SameDisk, v => UpdateOperations(o => o with { SameDisk = v }), minWidth: 220), keywords: AppStrings.Opt_SameDisk_Kw);

        Add(page, AppStrings.OptSec_Delete, AppStrings.Opt_DeleteTo, AppStrings.Opt_DeleteTo_Desc, () => Segmented(
            [(true, AppStrings.Opt_DeleteTo_Bin), (false, AppStrings.Opt_DeleteTo_Permanently)],
            Current.Operations.RecycleByDefault, v => UpdateOperations(o => o with { RecycleByDefault = v })), keywords: kw);

        Add(page, AppStrings.OptSec_NewFolder, AppStrings.Opt_MoveIntoNew, AppStrings.Opt_MoveIntoNew_Desc,
            () => Toggle(Current.Operations.MoveIntoNewFolder, on => UpdateOperations(o => o with { MoveIntoNewFolder = on })), keywords: kw);

        var archives = AppStrings.OptSec_Archives;
        Add(page, archives, AppStrings.Opt_UnpackFolder, AppStrings.Opt_UnpackFolder_Desc, () => Segmented(
            [
                (UnpackFolderRule.WhenNeeded, AppStrings.Opt_UnpackFolder_WhenNeeded),
                (UnpackFolderRule.Always, AppStrings.Opt_UnpackFolder_Always),
                (UnpackFolderRule.Never, AppStrings.Opt_UnpackFolder_Never),
            ],
            Current.Operations.UnpackFolder, v => UpdateOperations(o => o with { UnpackFolder = v })), keywords: kw + " " + AppStrings.Opt_Archives_Kw);
        Add(page, archives, AppStrings.Opt_Suspicious, AppStrings.Opt_Suspicious_Desc, SuspiciousEditor, wide: true, keywords: kw + " " + AppStrings.Opt_Archives_Kw);

        var pack = AppStrings.OptSec_Pack;
        var packKw = kw + " " + AppStrings.Opt_Archives_Kw + " " + AppStrings.Opt_Pack_Kw;
        Add(page, pack, AppStrings.Opt_PackFormat, AppStrings.Opt_PackFormat_Desc, () => Combo(
            [
                (PackFirstFormat.Zip, "ZIP"),
                (PackFirstFormat.SevenZip, "7z"),
                (PackFirstFormat.Tar, "TAR"),
                (PackFirstFormat.TarGz, "TAR.GZ"),
                (PackFirstFormat.TarXz, "TAR.XZ"),
                (PackFirstFormat.LastUsed, AppStrings.Opt_PackFormat_Last),
            ],
            Current.Operations.PackFirstFormat, v => UpdateOperations(o => o with { PackFirstFormat = v })), keywords: packKw);
        Add(page, pack, AppStrings.Opt_PackWhere, AppStrings.Opt_PackWhere_Desc, () => Segmented(
            [(PackWhere.BesideFiles, AppStrings.Opt_PackWhere_Beside), (PackWhere.OtherPanel, AppStrings.Opt_PackWhere_Other)],
            Current.Operations.PackWhere, v => UpdateOperations(o => o with { PackWhere = v })), keywords: packKw);
        Add(page, pack, AppStrings.Opt_PackExisting, AppStrings.Opt_PackExisting_Desc, () => Combo(
            [
                (PackExisting.Ask, AppStrings.Opt_PackExisting_Ask),
                (PackExisting.Add, AppStrings.Opt_PackExisting_Add),
                (PackExisting.Replace, AppStrings.Opt_PackExisting_Replace),
                (PackExisting.NewName, AppStrings.Opt_PackExisting_NewName),
            ],
            Current.Operations.PackExisting, v => UpdateOperations(o => o with { PackExisting = v })), keywords: packKw);

        Add(page, AppStrings.OptSec_InsideArchive, AppStrings.Opt_ArchiveEditSave, AppStrings.Opt_ArchiveEditSave_Desc, () => Combo(
            [
                (ArchiveEditSave.Ask, AppStrings.Opt_ArchiveEditSave_Ask),
                (ArchiveEditSave.PutBack, AppStrings.Opt_ArchiveEditSave_PutBack),
                (ArchiveEditSave.ReadOnly, AppStrings.Opt_ArchiveEditSave_ReadOnly),
            ],
            Current.Operations.ArchiveEditSave, v => UpdateOperations(o => o with { ArchiveEditSave = v }), minWidth: 260), keywords: packKw);
    }

    private Control SuspiciousEditor()
    {
        TextBox Box(int value, string name, Func<int, OperationSettings, OperationSettings> set, Func<OperationSettings, int> get)
        {
            var box = new TextBox { Text = value.ToString("N0", CultureInfo.CurrentCulture), Width = 80, HorizontalContentAlignment = HorizontalAlignment.Right };
            Avalonia.Automation.AutomationProperties.SetName(box, name);
            box.LostFocus += (_, _) =>
            {
                if (int.TryParse(box.Text, NumberStyles.Integer | NumberStyles.AllowThousands, CultureInfo.CurrentCulture, out var n) && n > 0)
                {
                    UpdateOperations(o => set(n, o));
                }

                box.Text = get(Current.Operations).ToString("N0", CultureInfo.CurrentCulture);
            };
            return box;
        }

        var ratio = Box(Current.Operations.UnpackAskRatio, AppStrings.Opt_Suspicious_Ratio, (n, o) => o with { UnpackAskRatio = n }, o => o.UnpackAskRatio);
        var mb = Box(Current.Operations.UnpackAskMb, AppStrings.Opt_Suspicious_Mb, (n, o) => o with { UnpackAskMb = n }, o => o.UnpackAskMb);
        TextBlock Words(string text) => Text(text, 13).Also(t => t.VerticalAlignment = VerticalAlignment.Center);
        return new WrapPanel
        {
            Children =
            {
                Words(AppStrings.Opt_Suspicious_Before).Also(t => t.Margin = new Thickness(0, 0, 8, 0)), ratio,
                Words(AppStrings.Opt_Suspicious_Middle).Also(t => t.Margin = new Thickness(8, 0)), mb,
                Words(AppStrings.Opt_Suspicious_After).Also(t => t.Margin = new Thickness(8, 0, 0, 0)),
            },
        };
    }

    private void UpdateOperations(Func<OperationSettings, OperationSettings> change) => Settings.Update(s => s with { Operations = change(s.Operations) });

    private Control CopyMaskEditor()
    {
        var box = new TextBox { Text = Current.Operations.Filter, Width = 150 };
        box.Res(TextBox.FontFamilyProperty, "Font.Mono");
        Avalonia.Automation.AutomationProperties.SetName(box, AppStrings.Opt_CopyMask);
        box.LostFocus += (_, _) => UpdateOperations(o => o with { Filter = string.IsNullOrWhiteSpace(box.Text) ? "*.*" : box.Text.Trim() });
        var toggle = Toggle(Current.Operations.FilterEnabled, on => UpdateOperations(o => o with { FilterEnabled = on }));
        return new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, Children = { box, toggle } };
    }

    private Control SpeedLimitEditor()
    {
        static string Shown(double mb) => mb.ToString("0.#", CultureInfo.CurrentCulture);
        var box = new TextBox { Text = Shown(Current.Operations.SpeedLimitMb), Width = 70, HorizontalContentAlignment = HorizontalAlignment.Right };
        Avalonia.Automation.AutomationProperties.SetName(box, AppStrings.Opt_SpeedLimit);
        box.LostFocus += (_, _) =>
        {
            if (double.TryParse(box.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out var mb) && mb > 0)
            {
                UpdateOperations(o => o with { SpeedLimitMb = mb });
            }

            box.Text = Shown(Current.Operations.SpeedLimitMb);
        };
        var toggle = Toggle(Current.Operations.LimitSpeed, on => UpdateOperations(o => o with { LimitSpeed = on }));
        return new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { box, Text(AppStrings.Opt_SpeedLimit_Unit, 13).Also(t => t.VerticalAlignment = VerticalAlignment.Center), toggle } };
    }

    private void AddConfirmations()
    {
        const string page = "confirm";
        var kw = AppStrings.Opt_Confirm_Kw;
        void Check(string section, string title, string? description, Func<ConfirmationSettings, bool> get, Func<ConfirmationSettings, bool, ConfirmationSettings> set)
            => Add(page, section, title, description, () => Toggle(get(Current.Confirmations), on => Settings.Update(s => s with { Confirmations = set(s.Confirmations, on) })), keywords: kw);
        void Later(string title, string milestone)
            => Add(page, AppStrings.OptSec_Later, title, Loc.Format(AppStrings.Opt_ArrivesIn, CommandText.Milestone(milestone)), () => Toggle(true, _ => { }, enabled: false), keywords: kw);

        var enter = AppStrings.OptSec_ConfirmEnter;
        void Enter(string title, string description, Func<ConfirmationSettings, bool> get, Func<ConfirmationSettings, bool, ConfirmationSettings> set)
            => Add(page, enter, title, description, () => Segmented([(true, AppStrings.Common_OK), (false, AppStrings.Common_Cancel)],
                get(Current.Confirmations), on => Settings.Update(s => s with { Confirmations = set(s.Confirmations, on) })), keywords: kw);
        Enter(AppStrings.Opt_EnterCreate, AppStrings.Opt_EnterCreate_Desc, c => c.EnterCreate, (c, on) => c with { EnterCreate = on });
        Enter(AppStrings.Opt_EnterSave, AppStrings.Opt_EnterSave_Desc, c => c.EnterSave, (c, on) => c with { EnterSave = on });
        Enter(AppStrings.Opt_EnterDelete, AppStrings.Opt_EnterDelete_Desc, c => c.EnterDelete, (c, on) => c with { EnterDelete = on });
        Enter(AppStrings.Opt_EnterClose, AppStrings.Opt_EnterClose_Desc, c => c.EnterClose, (c, on) => c with { EnterClose = on });

        var delete = AppStrings.OptSec_Delete;
        Check(delete, AppStrings.Opt_ConfirmDelete, AppStrings.Opt_ConfirmDelete_Desc, c => c.Delete, (c, on) => c with { Delete = on });
        Check(delete, AppStrings.Opt_ConfirmNonEmpty, null, c => c.DeleteNonEmptyFolder, (c, on) => c with { DeleteNonEmptyFolder = on });
        Check(delete, AppStrings.Opt_ConfirmDeleteHidden, null, c => c.DeleteHiddenOrSystem, (c, on) => c with { DeleteHiddenOrSystem = on });

        var copy = AppStrings.OptSec_CopyMove;
        Check(copy, AppStrings.Opt_ConfirmReplaceHidden, AppStrings.Opt_ConfirmReplaceHidden_Desc, c => c.ReplaceHiddenOrSystem, (c, on) => c with { ReplaceHiddenOrSystem = on });
        Check(copy, AppStrings.Opt_ConfirmMerge, null, c => c.MergeFolders, (c, on) => c with { MergeFolders = on });
        Check(copy, AppStrings.Opt_ConfirmCreate, Loc.Format(AppStrings.Opt_ConfirmCreate_Desc, AppStrings.Sample_NewFolderPath), c => c.CreateTargetFolder, (c, on) => c with { CreateTargetFolder = on });
        Check(copy, AppStrings.Opt_ConfirmDragDrop, AppStrings.Opt_ConfirmDragDrop_Desc, c => c.DragDropMenu, (c, on) => c with { DragDropMenu = on });

        var app = AppStrings.OptSec_Salandual;
        Check(app, AppStrings.Opt_ConfirmClose, AppStrings.Opt_ConfirmClose_Desc, c => c.CloseApp, (c, on) => c with { CloseApp = on });
        Check(app, AppStrings.Opt_ConfirmCloseJobs, AppStrings.Opt_ConfirmCloseJobs_Desc, c => c.CloseWithJobs, (c, on) => c with { CloseWithJobs = on });
        Check(app, AppStrings.Opt_ConfirmLeftovers, AppStrings.Opt_ConfirmLeftovers_Desc, c => c.DeleteLeftoverTemp, (c, on) => c with { DeleteLeftoverTemp = on });

        Check(AppStrings.OptSec_SearchConfirm, AppStrings.Opt_ConfirmCloseSearch, AppStrings.Opt_ConfirmCloseSearch_Desc, c => c.CloseSearch, (c, on) => c with { CloseSearch = on });

        var list = AppStrings.OptSec_FileListConfirm;
        Check(list, AppStrings.Opt_ConfirmRemoveListLine, AppStrings.Opt_ConfirmRemoveListLine_Desc, c => c.RemoveFileListLine, (c, on) => c with { RemoveFileListLine = on });
        Check(list, AppStrings.Opt_ConfirmCloseList, AppStrings.Opt_ConfirmCloseList_Desc, c => c.CloseUnsavedFileList, (c, on) => c with { CloseUnsavedFileList = on });

        Later(AppStrings.Opt_ConfirmLater_Ntfs, "Phase 3");
        Later(AppStrings.Opt_ConfirmLater_Mail, "Phase 3");
    }
}

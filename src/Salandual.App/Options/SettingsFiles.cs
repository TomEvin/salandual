// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.App/Options/SettingsFiles.cs

using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Salandual.App.Dialogs;
using Salandual.App.Resources;
using Salandual.Core;
using Salandual.Core.Resources;
using Salandual.Core.Settings;

namespace Salandual.App.Options;

internal sealed class SettingsFiles(AppServices services, Window owner, Action<string, Action?> report)
{
    private SettingsService Settings => services.Settings;

    public async Task ExportAsync()
    {
        if (await SettingsTransferDialog.ForExport(Settings.Current).ShowDialog<object?>(owner) is not ExportChoice choice)
        {
            return;
        }

        var file = await owner.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = AppStrings.Xfer_Export_Title,
            SuggestedFileName = SettingsTransfer.SuggestedName(DateTime.Now),
            DefaultExtension = SettingsTransfer.Extension,
            FileTypeChoices = [FileType],
        });
        if (file?.TryGetLocalPath() is { } path)
        {
            ExportTo(path, choice);
        }
    }

    public async Task ImportAsync()
    {
        if (await PickImportAsync() is var (import, parts))
        {
            Import(import, parts);
        }
    }

    internal async Task<(SettingsImport Import, IReadOnlySet<SettingsPart> Parts)?> PickImportAsync()
    {
        var files = await owner.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = AppStrings.Xfer_Import_Title,
            FileTypeFilter = [FileType, new FilePickerFileType(AppStrings.Xfer_SettingsJson) { Patterns = ["*.json"] }, FilePickerFileTypes.All],
        });
        if (files is [var picked] && picked.TryGetLocalPath() is { } path && Read(path) is { } import
            && await SettingsTransferDialog.ForImport(import).ShowDialog<object?>(owner) is IReadOnlySet<SettingsPart> parts)
        {
            return (import, parts);
        }

        return null;
    }

    public async Task ResetAllAsync()
    {
        if (await SettingsTransferDialog.ForResetAll(Settings.Current).ShowDialog<object?>(owner) is ResetChoice choice)
        {
            ResetAll(choice);
        }
    }

    private static FilePickerFileType FileType => new(AppStrings.Xfer_FileType) { Patterns = ["*" + SettingsTransfer.Extension] };

    internal void ExportTo(string path, ExportChoice choice)
    {
        try
        {
            File.WriteAllText(path, SettingsTransfer.Export(Settings.Current, choice, DateTime.Now, ProductInfo.DisplayName + " " + ProductInfo.Version));
            report(Loc.Format(AppStrings.Xfer_Saved, Path.GetFileName(path)), null);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            report(Loc.Format(AppStrings.Xfer_ExportFailed, Path.GetFileName(path), ex.Message), null);
        }
    }

    internal SettingsImport? Read(string path)
    {
        try
        {
            var import = SettingsTransfer.Read(path, Settings.Current);
            if (import.Changes.Count == 0)
            {
                report(Loc.Format(AppStrings.Xfer_NothingToImport, Path.GetFileName(path)), null);
                return null;
            }

            return import;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            report(Loc.Format(AppStrings.Xfer_ImportFailed, Path.GetFileName(path), ex.Message), null);
            return null;
        }
    }

    internal void Import(SettingsImport import, IReadOnlySet<SettingsPart> parts)
    {
        var count = import.Changes.Count(c => parts.Contains(c.Part));
        Replace(import.Apply(parts), "import", Loc.Plural(count, AppStrings.Xfer_Imported_One, AppStrings.Xfer_Imported_Other, Path.GetFileName(import.File)));
    }

    internal void ResetAll(ResetChoice choice) => Replace(SettingsTransfer.ResetAll(Settings.Current, choice), "reset", AppStrings.Xfer_ResetDone);

    private void Replace(AppSettings next, string what, string done)
    {
        var before = Settings.Current;
        try
        {
            SettingsTransfer.KeepCopy(before, services.DataDirectory, what, DateTime.Now);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            report(Loc.Format(AppStrings.Xfer_CopyFailed, ex.Message), null);
            return;
        }

        Settings.Update(_ => next);
        report(done, () =>
        {
            Settings.Update(_ => before);
            report(AppStrings.Xfer_Undone, null);
        });
    }
}

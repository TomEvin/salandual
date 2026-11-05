// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.App/Options/LanguageExportWindow.cs

using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Salandual.App.Controls;
using Salandual.App.Resources;
using Salandual.Core.Resources;

namespace Salandual.App.Options;

public sealed class LanguageExportWindow : Window
{
    private readonly string _folder;
    private readonly IReadOnlyList<string> _folders;
    private readonly TextBox _name;
    private readonly TextBlock _error;
    private readonly Button _export;

    public LanguageExportWindow(string folder, IReadOnlyList<string> folders)
    {
        _folder = folder;
        _folders = folders;
        Title = AppStrings.LangExport_Title;
        Width = 520;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        this.Res(BackgroundProperty, "Brush.PanelBackground");

        _name = new TextBox { MinHeight = 32, VerticalContentAlignment = VerticalAlignment.Center, PlaceholderText = AppStrings.LangExport_NameHint, Text = SuggestedName() };
        Avalonia.Automation.AutomationProperties.SetName(_name, AppStrings.LangExport_File);
        _error = OptionsUi.Text("", 12, brush: "Brush.Danger", wrap: true);
        _error.IsVisible = false;
        _export = new Button { Content = AppStrings.LangExport_Export, MinWidth = 96, Padding = new Thickness(14, 5), HorizontalContentAlignment = HorizontalAlignment.Center };
        _name.TextChanged += (_, _) => Validate();
        Validate();

        var body = new StackPanel { Spacing = 6, Margin = new Thickness(20, 6, 20, 16) };
        body.Children.Add(OptionsUi.Secondary(Loc.Format(AppStrings.LangExport_Lead, Entries().Count)));
        var fileHead = OptionsUi.Text(AppStrings.LangExport_File, 13, FontWeight.SemiBold);
        fileHead.Margin = new Thickness(0, 6, 0, 0);
        body.Children.Add(fileHead);
        body.Children.Add(_name);
        body.Children.Add(_error);
        body.Children.Add(OptionsUi.Secondary(AppStrings.LangExport_Note));

        _export.Classes.Add("accent");
        _export.Click += (_, _) => Accept();
        var cancel = new Button { Content = AppStrings.Common_Cancel, MinWidth = 84, Padding = new Thickness(14, 5), HorizontalContentAlignment = HorizontalAlignment.Center };
        cancel.Click += (_, _) => Close(null);
        var footer = new Border
        {
            Padding = new Thickness(20, 12),
            BorderThickness = new Thickness(0, 1, 0, 0),
            Child = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right, Children = { cancel, _export } },
        }.Res(Border.BackgroundProperty, "Brush.WindowBackground").Res(Border.BorderBrushProperty, "Brush.Border");

        var root = new DockPanel();
        DockPanel.SetDock(footer, Dock.Bottom);
        root.Children.Add(footer);
        root.Children.Add(body);
        Content = TitleBarParts.ForDialog(this, root);
        Opened += (_, _) => _name.Focus();
    }

    internal TextBox NameBox => _name;

    internal TextBlock ErrorText => _error;

    internal Button ExportButton => _export;

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Handled)
        {
            return;
        }

        if (e.Key == Key.Escape)
        {
            Close(null);
            e.Handled = true;
        }
        else if (e.Key == Key.Enter)
        {
            Accept();
            e.Handled = true;
        }
    }

    private string SuggestedName()
    {
        var name = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName + ".json";
        return IsNewLanguage(name) && !Taken(name) ? name : "";
    }

    private static bool IsNewLanguage(string name)
        => LanguageFiles.IsLanguageFileName(name) && LanguageFiles.TryParseId(name[..^5], out _, out var changes) && changes is null;

    private bool Taken(string name)
        => File.Exists(Path.Combine(_folder, name)) || LanguageFiles.Find(name[..^5], _folders) is not null;

    private string FileName()
    {
        var name = (_name.Text ?? "").Trim();
        return name.Length > 0 && !name.EndsWith(".json", StringComparison.OrdinalIgnoreCase) ? name + ".json" : name;
    }

    private bool Validate()
    {
        var name = FileName();
        var problem = name.Length == 0 ? null
            : !IsNewLanguage(name) ? AppStrings.LangExport_BadName
            : Taken(name) ? Loc.Format(AppStrings.LangExport_Exists, name)
            : null;
        _error.Text = problem ?? "";
        _error.IsVisible = problem is not null;
        _export.IsEnabled = name.Length > 0 && problem is null;
        return _export.IsEnabled;
    }

    internal void Accept()
    {
        if (!Validate())
        {
            return;
        }

        var name = FileName();
        LanguageFiles.TryParseId(name[..^5], out var code, out _);
        var version = typeof(LanguageExportWindow).Assembly.GetName().Version ?? new Version(0, 0, 0);
        var header = new LanguageHeader(
            LanguageFiles.OwnName(code),
            Author: "",
            Contact: "",
            Copyright: "",
            License: "GPL-3.0-or-later",
            ForVersion: version.ToString(3),
            Updated: LanguageHeader.Date(DateTime.Today));
        var path = Path.Combine(_folder, name);
        try
        {
            LanguageFiles.Write(path, header, Entries().Select(e => KeyValuePair.Create(e.Key, e.English)));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _error.Text = ex.Message;
            _error.IsVisible = true;
            return;
        }

        Close(path);
    }

    internal static IReadOnlyList<(string Key, string English)> Entries()
    {
        var all = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var manager in AppServices.Originals)
        {
            if (manager.GetResourceSet(CultureInfo.InvariantCulture, createIfNotExists: true, tryParents: false) is { } set)
            {
                foreach (System.Collections.DictionaryEntry entry in set)
                {
                    if (entry.Key is string key && entry.Value is string text)
                    {
                        all.TryAdd(key, text);
                    }
                }
            }
        }

        return [.. all.Select(kv => (kv.Key, kv.Value))];
    }
}

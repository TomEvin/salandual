// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.App/Options/OptionsWindow.Servers.cs

using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Salandual.App.Resources;
using Salandual.Core.Resources;
using Salandual.Core.Settings;
using Salandual.Remote;
using static Salandual.App.Options.OptionsUi;

namespace Salandual.App.Options;

public sealed partial class OptionsWindow
{
    public const string ServersPage = "servers";

    private void UpdateServers(Func<ServerSettings, ServerSettings> change) => Settings.Update(s => s with { Servers = change(s.Servers) });

    private void AddServers()
    {
        var kw = AppStrings.Opt_Servers_Kw;
        Add(ServersPage, AppStrings.OptSec_Connecting, AppStrings.Opt_CloseConnecting, AppStrings.Opt_CloseConnecting_Desc,
            () => Toggle(Current.Servers.CloseConnecting, on => UpdateServers(x => x with { CloseConnecting = on })), keywords: kw);
        Add(ServersPage, AppStrings.OptSec_Connecting, AppStrings.Opt_ServersInMenu, AppStrings.Opt_ServersInMenu_Desc, ServersInMenuPicker, keywords: AppStrings.Opt_ServersInMenu_Kw);
        Add(ServersPage, AppStrings.OptSec_SavedSignIns, null, AppStrings.Opt_SavedSignIns_Desc, SavedSignIns, wide: true, keywords: AppStrings.Opt_SavedSignIns_Kw);
        Add(ServersPage, AppStrings.OptSec_TrustedKeys, null, AppStrings.Opt_TrustedKeys_Desc, TrustedKeys, wide: true, keywords: AppStrings.Opt_TrustedKeys_Kw);
        Add(ServersPage, AppStrings.OptSec_TrustedKeys, AppStrings.Opt_KnownHosts, AppStrings.Opt_KnownHosts_Desc,
            () => Toggle(Current.Servers.ReadKnownHosts, on => UpdateServers(x => x with { ReadKnownHosts = on })), keywords: AppStrings.Opt_TrustedKeys_Kw);
        Add(ServersPage, AppStrings.OptSec_ServerDefaults, AppStrings.Opt_ServerDefaults, AppStrings.Opt_ServerDefaults_Desc,
            () => ActionButton(AppStrings.Opt_ServerDefaults_Button, () => _ = EditServerDefaultsAsync()), keywords: AppStrings.Opt_ServerDefaults_Kw);
    }

    internal Dialogs.ServerSettingsDialog? OpenServerDefaults { get; private set; }

    internal Dialogs.ConfirmDialog? ForgetAllQuestion { get; private set; }

    private async Task EditServerDefaultsAsync()
    {
        var dialog = OpenServerDefaults = new Dialogs.ServerSettingsDialog(null, Current.Servers.Defaults, null, _services.Servers.Secrets);
        if (await dialog.ShowDialog<Dialogs.ServerSettingsChoice?>(this) is { } choice)
        {
            UpdateServers(x => x with { Defaults = choice.Over(x.Defaults) });
        }

        OpenServerDefaults = null;
    }

    internal sealed record KeptSignIn(string Who, string User, string What, string? Target);

    internal IReadOnlyList<KeptSignIn> KeptSignIns()
    {
        var store = _services.Servers.Secrets;
        var kept = new HashSet<string>(store.List(SecretSource.Prefix), StringComparer.OrdinalIgnoreCase);
        var rows = new List<KeptSignIn>();
        foreach (var b in Current.Servers.Bookmarks)
        {
            var server = ServerAddress.Of(b);
            var any = false;
            foreach (var kind in new[] { SecretKind.Password, SecretKind.Passphrase })
            {
                var target = SecretSource.Target(server, kind);
                if (kept.Remove(target))
                {
                    rows.Add(new KeptSignIn(b.Name, b.User, kind == SecretKind.Password ? AppStrings.Opt_Kept_Password : AppStrings.Opt_Kept_Passphrase, target));
                    any = true;
                }
            }

            if (!any)
            {
                rows.Add(new KeptSignIn(b.Name, b.User, AppStrings.Opt_Kept_Nothing, null));
            }
        }

        foreach (var target in kept.Order(StringComparer.OrdinalIgnoreCase))
        {
            var name = target[SecretSource.Prefix.Length..];
            var proxy = name.StartsWith("proxy://", StringComparison.OrdinalIgnoreCase);
            var passphrase = name.EndsWith("#passphrase", StringComparison.OrdinalIgnoreCase);
            name = passphrase ? name[..^"#passphrase".Length] : name;
            var at = name.IndexOf("://", StringComparison.Ordinal) is var s and >= 0 ? name[(s + 3)..] : name;
            var user = at.LastIndexOf('@') is var u and > 0 ? at[..u] : "";
            rows.Add(new KeptSignIn(proxy ? Loc.Format(AppStrings.Opt_Kept_Proxy, at[(user.Length > 0 ? user.Length + 1 : 0)..]) : name, user,
                proxy ? AppStrings.Opt_Kept_ProxyPassword : passphrase ? AppStrings.Opt_Kept_Passphrase : AppStrings.Opt_Kept_Password, target));
        }

        return rows;
    }

    private Control SavedSignIns()
    {
        var host = new StackPanel { Spacing = 8 };
        void Fill()
        {
            host.Children.Clear();
            var rows = KeptSignIns();
            var table = Table([AppStrings.Opt_Kept_Who, AppStrings.Opt_Kept_User, AppStrings.Opt_Kept_What, ""]);
            foreach (var row in rows)
            {
                Control action = new TextBlock();
                if (row.Target is { } target)
                {
                    action = SmallButton(AppStrings.Opt_Kept_Forget, () =>
                    {
                        _services.Servers.Secrets.Delete(target);
                        Fill();
                    }, Loc.Format(AppStrings.Opt_Kept_ForgetName, row.Who));
                }

                TableRow(table, [Text(row.Who, 13), Text(row.User, 13, brush: "Brush.SecondaryText"), Text(row.What, 13, brush: row.Target is null ? "Brush.TertiaryText" : "Brush.Text"), action]);
            }

            host.Children.Add(rows.Count == 0 ? Secondary(AppStrings.Opt_Kept_NoBookmarks) : table);
            var all = ActionButton(AppStrings.Opt_Kept_ForgetAll, () => _ = ForgetAllAsync(Fill), enabled: rows.Any(r => r.Target is not null));
            host.Children.Add(new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 10,
                Children = { all, Text(AppStrings.Opt_Kept_NoExport, 12, brush: "Brush.TertiaryText").Also(t => t.VerticalAlignment = VerticalAlignment.Center) },
            });
        }

        Fill();
        return host;
    }

    private async Task ForgetAllAsync(Action done)
    {
        var ask = ForgetAllQuestion = new Dialogs.ConfirmDialog(AppStrings.Opt_Kept_ForgetAllTitle, AppStrings.Opt_Kept_ForgetAllText, AppStrings.Opt_Kept_ForgetAll, AppStrings.Common_Cancel);
        var yes = await ask.ShowDialog<bool>(this);
        ForgetAllQuestion = null;
        if (yes)
        {
            var store = _services.Servers.Secrets;
            foreach (var target in store.List(SecretSource.Prefix))
            {
                store.Delete(target);
            }
        }

        done();
    }

    private Control TrustedKeys()
    {
        var host = new StackPanel { Spacing = 8 };
        void Fill()
        {
            host.Children.Clear();
            var trusted = _services.Servers.Trusted;
            var keys = trusted.Keys;
            if (keys.Count == 0)
            {
                host.Children.Add(Secondary(AppStrings.Opt_Trusted_None));
                return;
            }

            var table = Table([AppStrings.Opt_Trusted_Server, AppStrings.Opt_Trusted_Key, AppStrings.Opt_Trusted_Since, ""]);
            foreach (var key in keys)
            {
                var fingerprint = key.Fingerprint.Length > 18 ? key.Fingerprint[..18] + "…" : key.Fingerprint;
                var keyText = Text(key.Algorithm + " " + fingerprint, 12);
                keyText.Res(TextBlock.FontFamilyProperty, "Font.Mono");
                ToolTip.SetTip(keyText, key.Algorithm + " " + key.Fingerprint);
                var k = key;
                TableRow(table, [Text(key.Server, 13), keyText, Text(key.Since.ToLocalTime().ToString("MMMM d, yyyy", Loc.TextCulture), 13, brush: "Brush.SecondaryText"),
                    SmallButton(AppStrings.Opt_Trusted_Remove, () =>
                    {
                        trusted.Remove(k);
                        Fill();
                    }, Loc.Format(AppStrings.Opt_Trusted_RemoveName, key.Server))]);
            }

            host.Children.Add(table);
        }

        Fill();
        return host;
    }

    private static Grid Table(IReadOnlyList<string> heads)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("2*,1.4*,1.6*,Auto"), ColumnSpacing = 14, RowSpacing = 6 };
        grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        for (var i = 0; i < heads.Count; i++)
        {
            var head = Text(heads[i], 12, brush: "Brush.TertiaryText");
            Grid.SetColumn(head, i);
            grid.Children.Add(head);
        }

        return grid;
    }

    private static void TableRow(Grid grid, IReadOnlyList<Control> cells)
    {
        grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        var row = grid.RowDefinitions.Count - 1;
        for (var i = 0; i < cells.Count; i++)
        {
            cells[i].VerticalAlignment = VerticalAlignment.Center;
            if (cells[i] is TextBlock t)
            {
                t.TextTrimming = Avalonia.Media.TextTrimming.CharacterEllipsis;
            }

            Grid.SetRow(cells[i], row);
            Grid.SetColumn(cells[i], i);
            grid.Children.Add(cells[i]);
        }
    }

    private static Button SmallButton(string text, Action click, string automationName)
    {
        var button = new Button { Content = text, Padding = new Thickness(10, 3), FontSize = 12 };
        Avalonia.Automation.AutomationProperties.SetName(button, automationName);
        button.Click += (_, _) => click();
        return button;
    }
}

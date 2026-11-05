// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.App/Dialogs/ConnectServerDialog.cs

using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Platform.Storage;
using Salandual.App.Controls;
using Salandual.App.Options;
using Salandual.App.Resources;
using Salandual.Core.Resources;
using Salandual.Core.Settings;
using Salandual.Remote;

namespace Salandual.App.Dialogs;

internal sealed record ConnectServerResult(ServerBookmark Server, bool Saved);

internal sealed class ConnectServerDialog : Window
{
    private readonly List<ServerBookmark> _bookmarks;
    private readonly Action<IReadOnlyList<ServerBookmark>> _save;
    private readonly ListBox _list = new() { MinHeight = 260 };
    private readonly ComboBox _kind, _signIn;
    private readonly TextBox _host, _port, _user, _keyFile, _startIn, _name;
    private readonly Grid _keyRow;
    private readonly ISecretStore? _secrets;
    private readonly Button _forget = new() { Content = AppStrings.Server_ForgetPassword, Padding = new Thickness(10, 4), IsVisible = false, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _error = OptionsUi.Text("", 12, brush: "Brush.Danger", wrap: true);
    private bool _filling;
    private readonly ServerOptions _defaults;
    private ServerSettingsChoice _own = ServerSettingsChoice.None;
    private readonly TextBlock _settingsNote = OptionsUi.Text("", 12, brush: "Brush.TertiaryText", wrap: true);

    private static readonly ServerKind[] _kinds = [ServerKind.Sftp, ServerKind.Ftp, ServerKind.Ftps, ServerKind.FtpPlain];

    public ConnectServerDialog(IReadOnlyList<ServerBookmark> bookmarks, ServerBookmark? start, Action<IReadOnlyList<ServerBookmark>> save, ISecretStore? secrets = null, ServerOptions? defaults = null)
    {
        _defaults = defaults ?? ServerOptions.BuiltIn;
        _secrets = secrets;
        _bookmarks = [.. bookmarks];
        _save = save;
        Title = AppStrings.Server_ConnectTitle;
        Width = 720;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        this.Res(BackgroundProperty, "Brush.PanelBackground");

        _kind = new ComboBox { ItemsSource = new[] { AppStrings.Server_KindSftp, AppStrings.Server_KindFtp, AppStrings.Server_KindFtps, AppStrings.Server_KindFtpPlain }, SelectedIndex = 0, MinWidth = 180 };
        _signIn = new ComboBox { ItemsSource = new[] { AppStrings.Server_SignInPassword, AppStrings.Server_SignInKeyFile, AppStrings.Server_SignInAgent }, SelectedIndex = 0, MinWidth = 120 };
        _host = Field(AppStrings.Server_Host);
        _port = Field(AppStrings.Server_Port);
        _port.Width = 80;
        _port.HorizontalAlignment = HorizontalAlignment.Left;
        _user = Field(AppStrings.Server_User);
        _keyFile = Field(AppStrings.Server_KeyFile);
        _startIn = Field(AppStrings.Server_StartIn);
        _startIn.PlaceholderText = AppStrings.Server_StartInHint;
        _name = Field(AppStrings.Server_Name);
        _name.PlaceholderText = AppStrings.Server_NameHint;
        _error.IsVisible = false;

        var browse = new Button { Content = AppStrings.Server_Browse, Padding = new Thickness(10, 4), VerticalAlignment = VerticalAlignment.Center };
        browse.Click += (_, _) => _ = BrowseKeyAsync();
        _keyRow = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 6 };
        _keyRow.Children.Add(_keyFile);
        Grid.SetColumn(browse, 1);
        _keyRow.Children.Add(browse);
        var authRow = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), ColumnSpacing = 8 };
        authRow.Children.Add(_signIn);
        _forget.Click += (_, _) => ForgetKept();
        Grid.SetColumn(_forget, 2);
        authRow.Children.Add(_forget);
        Grid.SetColumn(_keyRow, 1);
        authRow.Children.Add(_keyRow);

        var portLabel = OptionsUi.Text(AppStrings.Server_Port, 13);
        portLabel.VerticalAlignment = VerticalAlignment.Center;
        var protocolRow = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto"), ColumnSpacing = 8 };
        protocolRow.Children.Add(_kind);
        Grid.SetColumn(portLabel, 1);
        protocolRow.Children.Add(portLabel);
        Grid.SetColumn(_port, 2);
        protocolRow.Children.Add(_port);
        _kind.HorizontalAlignment = HorizontalAlignment.Stretch;

        var fields = new Grid { ColumnDefinitions = new ColumnDefinitions("110,*"), RowSpacing = 8 };
        var row = 0;
        void Add(string label, Control control)
        {
            fields.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            var text = OptionsUi.Text(label, 13);
            text.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetRow(text, row);
            Grid.SetRow(control, row);
            Grid.SetColumn(control, 1);
            fields.Children.Add(text);
            fields.Children.Add(control);
            control.Tag = text;
            row++;
        }

        Add(AppStrings.Server_Name, _name);
        Add(AppStrings.Server_Host, _host);
        Add(AppStrings.Server_StartIn, _startIn);
        Add(AppStrings.Server_User, _user);
        Add(AppStrings.Server_SignIn, authRow);
        Add(AppStrings.Server_Kind, protocolRow);
        Settings = new Button { Content = AppStrings.Server_Settings, Padding = new Thickness(10, 4), VerticalAlignment = VerticalAlignment.Center };
        Settings.Click += (_, _) => _ = OpenSettingsAsync();
        _settingsNote.VerticalAlignment = VerticalAlignment.Center;
        Add("", new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, Children = { Settings, _settingsNote } });

        var right = new StackPanel { Spacing = 10, Margin = new Thickness(16, 12, 20, 14) };
        right.Children.Add(fields);
        right.Children.Add(OptionsUi.Text(AppStrings.Server_NoPasswordKept, 12, brush: "Brush.TertiaryText", wrap: true));
        right.Children.Add(_error);

        _list.SelectionChanged += (_, _) => Fill();
        var delete = new Button { Content = AppStrings.Server_Delete, Padding = new Thickness(10, 4) };
        delete.Click += (_, _) => DeleteSelected();
        Delete = delete;
        var left = new DockPanel { Width = 190, Margin = new Thickness(12, 12, 0, 14) };
        var leftHead = OptionsUi.Text(AppStrings.Server_Bookmarks, 12, brush: "Brush.TertiaryText");
        DockPanel.SetDock(leftHead, Dock.Top);
        DockPanel.SetDock(delete, Dock.Bottom);
        delete.Margin = new Thickness(0, 8, 0, 0);
        left.Children.Add(leftHead);
        left.Children.Add(delete);
        left.Children.Add(_list);

        Save = new Button { Content = AppStrings.Server_Save, Padding = new Thickness(14, 5) };
        Save.Click += (_, _) => SaveBookmark();
        Connect = new Button { Content = AppStrings.Server_Connect, Padding = new Thickness(14, 5), IsDefault = true };
        Connect.Classes.Add("accent");
        Connect.Click += (_, _) => Accept();
        var cancel = new Button { Content = AppStrings.Common_Cancel, Padding = new Thickness(14, 5) };
        cancel.Click += (_, _) => Close(null);
        var footer = new Border
        {
            Padding = new Thickness(20, 12),
            BorderThickness = new Thickness(0, 1, 0, 0),
            Child = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right, Children = { Save, Connect, cancel } },
        }.Res(Border.BackgroundProperty, "Brush.WindowBackground").Res(Border.BorderBrushProperty, "Brush.Border");

        var split = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,Auto,*") };
        var line = new Border { Width = 1, Margin = new Thickness(12, 12, 0, 14) }.Res(Border.BackgroundProperty, "Brush.Border");
        Grid.SetColumn(line, 1);
        Grid.SetColumn(right, 2);
        split.Children.Add(left);
        split.Children.Add(line);
        split.Children.Add(right);

        var root = new DockPanel();
        DockPanel.SetDock(footer, Dock.Bottom);
        root.Children.Add(footer);
        root.Children.Add(split);
        Content = TitleBarParts.ForDialog(this, root);

        _kind.SelectionChanged += (_, _) => KindChanged();
        _signIn.SelectionChanged += (_, _) => ShowKeyRow();
        foreach (var box in new[] { _host, _port, _user })
        {
            box.TextChanged += (_, _) => ShowKept();
        }

        FillList();
        var at = start is null ? -1 : _bookmarks.FindIndex(b => b.Name == start.Name);
        if (at >= 0)
        {
            _list.SelectedIndex = at;
        }
        else if (start is not null)
        {
            _list.SelectedIndex = _bookmarks.Count;
            Show(start);
        }
        else
        {
            _list.SelectedIndex = _bookmarks.Count > 0 ? 0 : _bookmarks.Count;
        }

        Opened += (_, _) => (_host.Text is { Length: > 0 } ? (Control)Connect : _host).Focus();
    }

    private static TextBox Field(string name)
    {
        var box = new TextBox { MinHeight = 30, VerticalContentAlignment = VerticalAlignment.Center };
        Avalonia.Automation.AutomationProperties.SetName(box, name);
        return box;
    }

    internal ComboBox Kind => _kind;

    internal ComboBox SignIn => _signIn;

    internal TextBox Host => _host;

    internal TextBox Port => _port;

    internal TextBox User => _user;

    internal TextBox KeyFile => _keyFile;

    internal TextBox StartIn => _startIn;

    internal TextBox NameBox => _name;

    internal ListBox List => _list;

    internal TextBlock Error => _error;

    internal Button Save { get; }

    internal Button Connect { get; }

    internal Button Delete { get; }

    internal Button Settings { get; }

    internal TextBlock SettingsNote => _settingsNote;

    internal ServerSettingsChoice Own => _own;

    internal IReadOnlyList<ServerBookmark> Bookmarks => _bookmarks;

    private void FillList()
    {
        _filling = true;
        var selected = _list.SelectedIndex;
        _list.ItemsSource = _bookmarks.Select(b => (object)(b.Name + "  ·  " + KindTag(b.Kind))).Append(AppStrings.Server_NewBookmark).ToList();
        _list.SelectedIndex = Math.Min(selected, _bookmarks.Count);
        _filling = false;
    }

    internal static string KindTag(ServerKind kind) => kind switch
    {
        ServerKind.Sftp => "SFTP",
        ServerKind.Ftps => "FTPS",
        ServerKind.FtpPlain => AppStrings.Server_NotEncrypted,
        _ => "FTP",
    };

    private void Fill()
    {
        if (_filling)
        {
            return;
        }

        Show(_list.SelectedIndex >= 0 && _list.SelectedIndex < _bookmarks.Count ? _bookmarks[_list.SelectedIndex] : new ServerBookmark());
        Delete.IsEnabled = _list.SelectedIndex >= 0 && _list.SelectedIndex < _bookmarks.Count;
    }

    private void Show(ServerBookmark b)
    {
        _filling = true;
        _kind.SelectedIndex = Array.IndexOf(_kinds, b.Kind);
        _host.Text = b.Host;
        _port.Text = b.PortOrDefault.ToString(System.Globalization.CultureInfo.InvariantCulture);
        _user.Text = b.User;
        _signIn.SelectedIndex = b.SignIn switch { ServerSignIn.KeyFile => 1, ServerSignIn.Agent => 2, _ => 0 };
        _keyFile.Text = b.KeyFile;
        _startIn.Text = b.StartIn;
        _name.Text = b.Name;
        _plainAllowed = b.PlainAllowed;
        _own = ServerSettingsChoice.Of(b);
        _filling = false;
        ShowSettingsNote();
        ShowKeyRow();
        _error.IsVisible = false;
    }

    private bool _plainAllowed;

    private void KindChanged()
    {
        if (_filling)
        {
            return;
        }

        if (_kinds.Select(ServerBookmark.DefaultPort).Select(p => p.ToString(System.Globalization.CultureInfo.InvariantCulture)).Contains(_port.Text))
        {
            _port.Text = ServerBookmark.DefaultPort(SelectedKind).ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        ShowKeyRow();
    }

    private ServerKind SelectedKind => _kinds[Math.Max(0, _kind.SelectedIndex)];

    private void ShowKeyRow()
    {
        var sftp = SelectedKind == ServerKind.Sftp;
        if (!sftp && _signIn.SelectedIndex != 0)
        {
            var filling = _filling;
            _filling = true;
            _signIn.SelectedIndex = 0;
            _filling = filling;
        }

        _signIn.IsEnabled = sftp;
        _keyRow.IsVisible = sftp && _signIn.SelectedIndex == 1;
        ShowKept();
    }

    private ServerAddress? TypedServer()
    {
        var host = (_host.Text ?? "").Trim();
        if (!ServerAddress.IsHostName(host))
        {
            return null;
        }

        var kind = SelectedKind;
        var port = int.TryParse((_port.Text ?? "").Trim(), System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var p) && p is >= 1 and <= 65535
            ? p
            : ServerBookmark.DefaultPort(kind);
        return ServerAddress.Of(new ServerBookmark { Kind = kind, Host = host, Port = port, User = (_user.Text ?? "").Trim() });
    }

    private SecretKind TypedSecret => SelectedKind == ServerKind.Sftp && _signIn.SelectedIndex == 1 ? SecretKind.Passphrase : SecretKind.Password;

    private void ShowKept()
    {
        if (_secrets is null)
        {
            return;
        }

        var server = TypedServer();
        var kept = server is not null && _signIn.SelectedIndex != 2 && SecretSource.IsKept(_secrets, server, TypedSecret);
        _forget.Content = TypedSecret == SecretKind.Passphrase ? AppStrings.Server_ForgetPassphrase : AppStrings.Server_ForgetPassword;
        _forget.IsVisible = kept;
    }

    private void ForgetKept()
    {
        if (_secrets is not null && TypedServer() is { } server)
        {
            SecretSource.ForgetKept(_secrets, server);
        }

        ShowKept();
    }

    internal Button Forget => _forget;

    private async Task OpenSettingsAsync()
    {
        var name = (_name.Text ?? "").Trim() is { Length: > 0 } n ? n : (_host.Text ?? "").Trim() is { Length: > 0 } h ? h : AppStrings.Server_NewBookmark;
        var dialog = OpenSettings = new ServerSettingsDialog(name, _defaults, _own, _secrets);
        if (await dialog.ShowDialog<ServerSettingsChoice?>(this) is { } choice)
        {
            _own = choice;
            ShowSettingsNote();
        }

        OpenSettings = null;
    }

    internal ServerSettingsDialog? OpenSettings { get; private set; }

    private void ShowSettingsNote()
    {
        List<string> own = [];
        if (_own.Connection is not null)
        {
            own.Add(AppStrings.SrvSet_TabConnection);
        }

        if (_own.Proxy is not null)
        {
            own.Add(AppStrings.SrvSet_TabProxy);
        }

        if (_own.Transfers is not null)
        {
            own.Add(AppStrings.SrvSet_TabTransfers);
        }

        if (_own.Listing is not null)
        {
            own.Add(AppStrings.SrvSet_TabListing);
        }

        _settingsNote.Text = own.Count == 0 ? AppStrings.Server_SettingsDefaults : Loc.Format(AppStrings.Server_SettingsOwn, string.Join(", ", own));
    }

    private async Task BrowseKeyAsync()
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { Title = AppStrings.Server_KeyFile, AllowMultiple = false });
        if (files.Count > 0 && files[0].TryGetLocalPath() is { } path)
        {
            _keyFile.Text = path;
        }
    }

    internal ServerBookmark? Read()
    {
        var host = (_host.Text ?? "").Trim();
        string? problem = null;
        var port = 0;
        if (host.Length == 0)
        {
            problem = AppStrings.Server_NoHost;
        }
        else if (!ServerAddress.IsHostName(host))
        {
            problem = AppStrings.Server_BadHost;
        }
        else if (!int.TryParse((_port.Text ?? "").Trim(), System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out port) || port is < 1 or > 65535)
        {
            problem = AppStrings.Server_BadPort;
        }
        else if ((_user.Text ?? "").IndexOfAny([':', '/', '\\']) >= 0)
        {
            problem = AppStrings.Server_BadUser;
        }
        else if (SelectedKind == ServerKind.Sftp && (_user.Text ?? "").Trim().Length == 0)
        {
            problem = AppStrings.Server_NoUser;
        }
        else if (SelectedKind == ServerKind.Sftp && _signIn.SelectedIndex == 1 && (_keyFile.Text ?? "").Trim().Length == 0)
        {
            problem = AppStrings.Server_ChooseKeyFile;
        }

        _error.Text = problem ?? "";
        _error.IsVisible = problem is not null;
        if (problem is not null)
        {
            return null;
        }

        var kind = SelectedKind;
        return new ServerBookmark
        {
            Kind = kind,
            Host = host,
            Port = port == ServerBookmark.DefaultPort(kind) ? 0 : port,
            User = (_user.Text ?? "").Trim(),
            SignIn = kind != ServerKind.Sftp ? ServerSignIn.Password : _signIn.SelectedIndex switch { 1 => ServerSignIn.KeyFile, 2 => ServerSignIn.Agent, _ => ServerSignIn.Password },
            KeyFile = kind == ServerKind.Sftp && _signIn.SelectedIndex == 1 ? (_keyFile.Text ?? "").Trim() : "",
            StartIn = (_startIn.Text ?? "").Trim(),
            Name = (_name.Text ?? "").Trim(),
            PlainAllowed = _plainAllowed && kind == ServerKind.Ftp,
            Connection = _own.Connection,
            Proxy = _own.Proxy,
            Transfers = _own.Transfers,
            Listing = _own.Listing,
        };
    }

    internal void SaveBookmark()
    {
        if (Read() is not { } server)
        {
            return;
        }

        server = server with { Name = server.Name.Length > 0 ? server.Name : server.Host };
        var at = _list.SelectedIndex;
        var same = _bookmarks.FindIndex(b => string.Equals(b.Name, server.Name, StringComparison.OrdinalIgnoreCase));
        if (at >= 0 && at < _bookmarks.Count)
        {
            if (same >= 0 && same != at)
            {
                _error.Text = Loc.Format(AppStrings.Server_NameTaken, server.Name);
                _error.IsVisible = true;
                return;
            }

            _bookmarks[at] = server;
        }
        else if (same >= 0)
        {
            _bookmarks[same] = server;
            at = same;
        }
        else
        {
            _bookmarks.Add(server);
            at = _bookmarks.Count - 1;
        }

        _save(_bookmarks);
        FillList();
        _filling = true;
        _list.SelectedIndex = at;
        _filling = false;
        Show(server);
        Delete.IsEnabled = true;
    }

    private void DeleteSelected()
    {
        var at = _list.SelectedIndex;
        if (at < 0 || at >= _bookmarks.Count)
        {
            return;
        }

        _bookmarks.RemoveAt(at);
        _save(_bookmarks);
        FillList();
        _list.SelectedIndex = Math.Min(at, _bookmarks.Count);
        Fill();
    }

    internal void Accept()
    {
        if (Read() is not { } server)
        {
            return;
        }

        var at = _list.SelectedIndex;
        var saved = at >= 0 && at < _bookmarks.Count;
        if (saved)
        {
            server = server with { Name = server.Name.Length > 0 ? server.Name : _bookmarks[at].Name };
        }

        Close(new ConnectServerResult(server, saved));
    }

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
        else if (e.Key == Key.Enter && e.Source is not ListBoxItem)
        {
            Accept();
            e.Handled = true;
        }
    }
}

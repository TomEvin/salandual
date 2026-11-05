// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.App/Dialogs/ServerSettingsDialog.cs

using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Salandual.App.Controls;
using Salandual.App.Options;
using Salandual.App.Resources;
using Salandual.Core.Resources;
using Salandual.Core.Settings;
using Salandual.Remote;

namespace Salandual.App.Dialogs;

internal sealed record ServerSettingsChoice(ConnectionOptions? Connection, ProxyOptions? Proxy, TransferSettingsForServer? Transfers, ListingOptions? Listing)
{
    public static ServerSettingsChoice None { get; } = new(null, null, null, null);

    public static ServerSettingsChoice Of(ServerBookmark b) => new(b.Connection, b.Proxy, b.Transfers, b.Listing);

    public ServerOptions Over(ServerOptions defaults) => new()
    {
        Connection = Connection ?? defaults.Connection,
        Proxy = Proxy ?? defaults.Proxy,
        Transfers = Transfers ?? defaults.Transfers,
        Listing = Listing ?? defaults.Listing,
    };
}

internal sealed class ServerSettingsDialog : Window
{
    private const int Tabs4 = 4;
    private const double FieldWidth = 320;

    private readonly ServerOptions _defaults;
    private readonly bool _forDefaults;
    private readonly ISecretStore? _secrets;
    private readonly CheckBox[] _useDefaults = new CheckBox[Tabs4];
    private readonly Grid[] _tabBodies = new Grid[Tabs4];
    private readonly ScrollViewer[] _scrolls = new ScrollViewer[Tabs4];
    private readonly ServerOptions _own;

    private readonly ComboBox _tls = Combo(AppStrings.SrvSet_Tls12And13, AppStrings.SrvSet_Tls13);
    private readonly ComboBox _data = Combo(AppStrings.SrvSet_Passive, AppStrings.SrvSet_Active);
    private readonly CheckBox _keepAlive = new() { Content = AppStrings.SrvSet_KeepAliveOn };
    private readonly NumericUpDown _keepEvery = Number(10, 3600), _keepStop = Number(1, 1440), _connect = Number(3, 300), _answer = Number(5, 600),
        _retries = Number(0, 10), _retryApart = Number(1, 300), _atOnce = Number(1, 8), _proxyPort = Number(0, 65535), _cacheMinutes = Number(1, 1440);

    private readonly ComboBox _proxy = Combo(AppStrings.SrvSet_ProxyNone, AppStrings.SrvSet_ProxyWindows, AppStrings.SrvSet_ProxySocks, AppStrings.SrvSet_ProxyHttp);
    private readonly TextBox _proxyHost = Box(AppStrings.SrvSet_ProxyHost, FieldWidth), _proxyUser = Box(AppStrings.SrvSet_ProxyUser, FieldWidth);
    private readonly TextBox _proxyPassword = new() { PasswordChar = '•', Width = FieldWidth, MinHeight = 28, VerticalContentAlignment = VerticalAlignment.Center };
    private readonly Button _forgetProxy = new() { Content = AppStrings.SrvSet_ForgetProxyPassword, Padding = new Thickness(10, 4) };
    private readonly List<Control> _proxyRows = [];
    private readonly TextBlock _proxyWindowsNote;

    private readonly ComboBox _mode = Combo(AppStrings.SrvSet_Binary, AppStrings.SrvSet_Ascii, AppStrings.SrvSet_Automatic);
    private readonly TextBox _asciiNames = Box(AppStrings.SrvSet_AsciiFor, 400);
    private readonly ComboBox _uploadRights = Combo(AppStrings.SrvSet_RightsServer, AppStrings.SrvSet_RightsNumber);
    private readonly TextBox _uploadNumber = Box(AppStrings.Perm_Number, 72);
    private readonly TextBlock _uploadMeaning = Unit("");
    private readonly List<Control> _uploadNumberRow = [];
    private readonly CheckBox _compress = new() { Content = AppStrings.SrvSet_Compress };

    private readonly ComboBox _serverType = Combo(AppStrings.SrvSet_TypeAuto, "Unix", AppStrings.SrvSet_TypeWindows, "VMS", "IBM z/OS", "IBM i", "MLSD");
    private readonly ComboBox _listCommand = Combo(AppStrings.SrvSet_ListAuto, "LIST -a", "LIST");
    private readonly ComboBox _encoding;
    private readonly ComboBox _timeZone;
    private readonly CheckBox _hidden = new() { Content = AppStrings.SrvSet_ShowHidden };
    private readonly CheckBox _cache = new() { Content = AppStrings.SrvSet_CacheFor };

    private static readonly int[] _codePages = [0, 65001, 1250, 1252, 1251, 1253, 1254, 1257, 28592, 932, 936, 949, 950];
    private static readonly int?[] _zones = [null, .. Enumerable.Range(-24, 53).Select(h => (int?)(h * 30))];

    public ServerSettingsDialog(string? title, ServerOptions defaults, ServerSettingsChoice? own, ISecretStore? secrets)
    {
        _defaults = defaults;
        _forDefaults = title is null;
        _secrets = secrets;
        _own = _forDefaults ? defaults : (own ?? ServerSettingsChoice.None).Over(defaults);
        Title = title is null ? AppStrings.SrvSet_TitleDefaults : Loc.Format(AppStrings.SrvSet_Title, title);
        Width = 720;
        Height = 600;
        SizeToContent = SizeToContent.Manual;
        CanResize = false;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        this.Res(BackgroundProperty, "Brush.PanelBackground");

        _encoding = Combo([.. _codePages.Select(EncodingName)]);
        _timeZone = Combo([.. _zones.Select(ZoneName)]);

        var connection = new Form(Body(0));
        connection.Heading(AppStrings.SrvSet_FtpOnly);
        connection.Row(AppStrings.SrvSet_TlsVersions, _tls);
        connection.Row(AppStrings.SrvSet_DataConnection, _data);
        var activeNote = connection.Note(AppStrings.SrvSet_ActiveNote);
        _data.SelectionChanged += (_, _) => activeNote.IsVisible = _data.SelectedIndex == 1;
        connection.Heading(_keepAlive);
        connection.Row(AppStrings.SrvSet_KeepEvery, _keepEvery, Unit(AppStrings.SrvSet_Seconds));
        connection.Row(AppStrings.SrvSet_KeepStop, _keepStop, Unit(AppStrings.SrvSet_MinutesUnused));
        connection.Heading(AppStrings.SrvSet_GroupTries);
        connection.Row(AppStrings.SrvSet_ConnectTimeout, _connect, Unit(AppStrings.SrvSet_Seconds));
        connection.Row(AppStrings.SrvSet_AnswerTimeout, _answer, Unit(AppStrings.SrvSet_Seconds));
        connection.Row(AppStrings.SrvSet_Retries, _retries);
        connection.Row(AppStrings.SrvSet_RetryWait, _retryApart, Unit(AppStrings.SrvSet_Seconds));
        connection.Row(AppStrings.SrvSet_AtOnce, _atOnce, Unit(AppStrings.SrvSet_AtOnceNote));

        var proxy = new Form(Body(1));
        proxy.Row(AppStrings.SrvSet_Proxy, _proxy);
        proxy.Note(AppStrings.SrvSet_ProxyBoth);
        _proxyWindowsNote = proxy.Note(AppStrings.SrvSet_ProxyWindowsNote);
        _proxyRows.AddRange(proxy.Row(AppStrings.SrvSet_ProxyHost, _proxyHost));
        _proxyRows.AddRange(proxy.Row(AppStrings.Server_Port, _proxyPort));
        _proxyRows.AddRange(proxy.Row(AppStrings.SrvSet_ProxyUser, _proxyUser));
        _proxyRows.AddRange(proxy.Row(AppStrings.SrvSet_ProxyPassword, _proxyPassword, _forgetProxy));
        _proxyRows.Add(proxy.Note(AppStrings.SrvSet_ProxyPasswordNote));
        Avalonia.Automation.AutomationProperties.SetName(_proxyPassword, AppStrings.SrvSet_ProxyPassword);
        _forgetProxy.Click += (_, _) => ForgetProxyPassword();
        _proxy.SelectionChanged += (_, _) => ShowProxy();
        _proxyHost.TextChanged += (_, _) => ShowProxyKept();
        _proxyUser.TextChanged += (_, _) => ShowProxyKept();
        _proxyPort.ValueChanged += (_, _) => ShowProxyKept();

        var transfers = new Form(Body(2));
        transfers.Heading(AppStrings.SrvSet_FtpOnly);
        transfers.Row(AppStrings.SrvSet_TransferMode, _mode);
        transfers.Row(AppStrings.SrvSet_AsciiFor, _asciiNames);
        transfers.Note(AppStrings.SrvSet_AsciiNote);
        _mode.SelectionChanged += (_, _) => _asciiNames.IsEnabled = _mode.SelectedIndex == 2 && !UsingDefaults(2);
        transfers.Heading(AppStrings.SrvSet_GroupUpload);
        transfers.Row(AppStrings.SrvSet_UploadRights, _uploadRights);
        _uploadMeaning.TextWrapping = Avalonia.Media.TextWrapping.Wrap;
        _uploadNumberRow.AddRange(transfers.Row(AppStrings.Perm_Number, _uploadNumber, _uploadMeaning));
        _uploadRights.SelectionChanged += (_, _) => ShowUploadNumber();
        _uploadNumber.TextChanged += (_, _) => ShowUploadMeaning();
        transfers.Heading(AppStrings.SrvSet_SftpOnly);
        transfers.Row(AppStrings.SrvSet_Compression, _compress);
        transfers.Note(AppStrings.SrvSet_CompressNote);
        transfers.Heading(AppStrings.SrvSet_GroupCopyDialog);
        transfers.Note(AppStrings.SrvSet_CopyDialogNote);

        var listing = new Form(Body(3));
        listing.Heading(AppStrings.SrvSet_FtpOnly);
        listing.Row(AppStrings.SrvSet_ServerType, _serverType);
        listing.Row(AppStrings.SrvSet_ListCommand, _listCommand);
        listing.Row(AppStrings.SrvSet_Encoding, _encoding);
        listing.Row(AppStrings.SrvSet_TimeZone, _timeZone);
        listing.Heading(AppStrings.SrvSet_GroupListing);
        listing.Row(AppStrings.SrvSet_HiddenFiles, _hidden);
        listing.Row(AppStrings.SrvSet_Cache, _cache);
        listing.Row(AppStrings.SrvSet_CacheKeepFor, _cacheMinutes, Unit(AppStrings.SrvSet_Minutes));
        listing.Note(AppStrings.SrvSet_CacheNote);

        Tabs = new TabControl { Margin = new Thickness(16, 8, 16, 0) };
        string[] names = [AppStrings.SrvSet_TabConnection, AppStrings.SrvSet_TabProxy, AppStrings.SrvSet_TabTransfers, AppStrings.SrvSet_TabListing];
        for (var i = 0; i < Tabs4; i++)
        {
            var tab = i;
            var page = new StackPanel { Spacing = 8, Margin = new Thickness(4, 10, 12, 8) };
            if (!_forDefaults)
            {
                var use = _useDefaults[i] = new CheckBox { Content = AppStrings.SrvSet_UseDefaults };
                use.IsCheckedChanged += (_, _) => UseDefaultsChanged(tab);
                page.Children.Add(use);
                page.Children.Add(new Border { Height = 1, Margin = new Thickness(0, 0, 0, 2) }.Res(Border.BackgroundProperty, "Brush.Border"));
            }

            page.Children.Add(_tabBodies[i]);
            var scroll = _scrolls[i] = new ScrollViewer { Content = page, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled };
            Tabs.Items.Add(new TabItem { Header = names[i], Content = scroll, FontSize = 13 });
        }

        Error = OptionsUi.Text("", 12, brush: "Brush.Danger", wrap: true);
        Error.IsVisible = false;
        Error.Margin = new Thickness(20, 0, 20, 8);

        var restore = new Button { Content = AppStrings.SrvSet_Restore, Padding = new Thickness(14, 5) };
        restore.Click += (_, _) => Restore();
        Ok = new Button { Content = AppStrings.Common_OK, Padding = new Thickness(14, 5), MinWidth = 84, IsDefault = true };
        Ok.Classes.Add("accent");
        Ok.Click += (_, _) => Accept();
        var cancel = new Button { Content = AppStrings.Common_Cancel, Padding = new Thickness(14, 5), MinWidth = 84 };
        cancel.Click += (_, _) => Close(null);
        var buttons = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto") };
        buttons.Children.Add(restore);
        var right = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { Ok, cancel } };
        Grid.SetColumn(right, 2);
        buttons.Children.Add(right);
        var footer = new Border { Padding = new Thickness(20, 12), BorderThickness = new Thickness(0, 1, 0, 0), Child = buttons }
            .Res(Border.BackgroundProperty, "Brush.WindowBackground").Res(Border.BorderBrushProperty, "Brush.Border");

        var root = new DockPanel();
        DockPanel.SetDock(footer, Dock.Bottom);
        root.Children.Add(footer);
        DockPanel.SetDock(Error, Dock.Bottom);
        root.Children.Add(Error);
        var nextTime = OptionsUi.Text(AppStrings.SrvSet_NextTime, 12, brush: "Brush.TertiaryText", wrap: true);
        nextTime.Margin = new Thickness(20, 4, 20, 10);
        DockPanel.SetDock(nextTime, Dock.Bottom);
        root.Children.Add(nextTime);
        root.Children.Add(Tabs);
        Content = TitleBarParts.ForDialog(this, root);

        if (!_forDefaults)
        {
            _useDefaults[0].IsChecked = own?.Connection is null;
            _useDefaults[1].IsChecked = own?.Proxy is null;
            _useDefaults[2].IsChecked = own?.Transfers is null;
            _useDefaults[3].IsChecked = own?.Listing is null;
        }

        LoadConnection(UsingDefaults(0) ? defaults.Connection : _own.Connection);
        LoadProxy(UsingDefaults(1) ? defaults.Proxy : _own.Proxy);
        LoadTransfers(UsingDefaults(2) ? defaults.Transfers : _own.Transfers);
        LoadListing(UsingDefaults(3) ? defaults.Listing : _own.Listing);
        for (var i = 0; i < Tabs4; i++)
        {
            _tabBodies[i].IsEnabled = !UsingDefaults(i);
        }

        activeNote.IsVisible = _data.SelectedIndex == 1;

        Tabs.SelectedIndex = 2;
        foreach (var c in _uploadNumberRow)
        {
            c.IsVisible = true;
        }

        Opened += FitToTransfers;
        Opened += (_, _) => Tabs.Focus();
    }

    private void FitToTransfers(object? sender, EventArgs e)
    {
        var scroll = _scrolls[2];
        UpdateLayout();
        Opened -= FitToTransfers;
        if (scroll.Viewport.Height > 0 && scroll.Extent.Height > 0)
        {
            Height = Math.Max(300, Math.Ceiling(Height + scroll.Extent.Height - scroll.Viewport.Height));
        }

        ShowUploadNumber();
        Tabs.SelectedIndex = 0;
    }

    internal TabControl Tabs { get; }

    internal ScrollViewer TabScroll(int tab) => _scrolls[tab];

    internal TextBlock Error { get; }

    internal Button Ok { get; }

    internal IReadOnlyList<CheckBox> UseDefaults => _useDefaults;

    internal ComboBox DataConnectionBox => _data;

    internal ComboBox ModeBox => _mode;

    internal ComboBox ProxyBox => _proxy;

    internal TextBox ProxyHost => _proxyHost;

    internal TextBox ProxyUser => _proxyUser;

    internal TextBox ProxyPassword => _proxyPassword;

    internal Button ForgetProxy => _forgetProxy;

    internal NumericUpDown KeepEvery => _keepEvery;

    internal TextBox AsciiNames => _asciiNames;

    internal ComboBox UploadRightsBox => _uploadRights;

    internal TextBox UploadNumber => _uploadNumber;

    internal TextBlock UploadMeaning => _uploadMeaning;

    internal CheckBox Compress => _compress;

    internal ComboBox ServerTypeBox => _serverType;

    internal ComboBox EncodingBox => _encoding;

    internal ComboBox TimeZoneBox => _timeZone;

    internal CheckBox Hidden => _hidden;

    internal CheckBox CacheBox => _cache;

    internal NumericUpDown CacheMinutes => _cacheMinutes;

    private static ComboBox Combo(params string[] items) => new() { ItemsSource = items, SelectedIndex = 0, Width = FieldWidth };

    private static NumericUpDown Number(int min, int max)
        => new() { Minimum = min, Maximum = max, Increment = 1, FormatString = "0", ParsingNumberStyle = System.Globalization.NumberStyles.None, Width = 72, MinHeight = 28, ShowButtonSpinner = false, HorizontalContentAlignment = HorizontalAlignment.Right };

    private static TextBox Box(string name, double width)
    {
        var box = new TextBox { Width = width, MinHeight = 28, VerticalContentAlignment = VerticalAlignment.Center };
        Avalonia.Automation.AutomationProperties.SetName(box, name);
        return box;
    }

    private static TextBlock Unit(string text)
    {
        var t = OptionsUi.Text(text, 13, brush: "Brush.SecondaryText");
        t.VerticalAlignment = VerticalAlignment.Center;
        return t;
    }

    private Grid Body(int tab) => _tabBodies[tab] = new Grid
    {
        ColumnDefinitions = new ColumnDefinitions("190,Auto,*"),
        ColumnSpacing = 10,
        RowSpacing = 4,
    };

    private sealed class Form(Grid grid)
    {
        private int NextRow()
        {
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            return grid.RowDefinitions.Count - 1;
        }

        private void Put(Control c, int row, int column, int span = 1)
        {
            Grid.SetRow(c, row);
            Grid.SetColumn(c, column);
            Grid.SetColumnSpan(c, span);
            grid.Children.Add(c);
        }

        public void Heading(string text) => Heading(OptionsUi.Text(text, 13, Avalonia.Media.FontWeight.SemiBold));

        public void Heading(Control heading)
        {
            heading.Margin = new Thickness(0, grid.RowDefinitions.Count == 0 ? 0 : 8, 0, 0);
            if (heading is CheckBox box)
            {
                box.FontWeight = Avalonia.Media.FontWeight.SemiBold;
            }

            Put(heading, NextRow(), 0, 3);
        }

        public Control[] Row(string label, Control field, Control? after = null)
        {
            var row = NextRow();
            var text = OptionsUi.Text(label, 13);
            text.VerticalAlignment = VerticalAlignment.Center;
            text.TextWrapping = Avalonia.Media.TextWrapping.Wrap;
            Put(text, row, 0);
            field.HorizontalAlignment = HorizontalAlignment.Left;
            field.VerticalAlignment = VerticalAlignment.Center;
            Put(field, row, 1, after is null ? 2 : 1);
            if (after is null)
            {
                return [text, field];
            }

            after.HorizontalAlignment = HorizontalAlignment.Left;
            after.VerticalAlignment = VerticalAlignment.Center;
            Put(after, row, 2);
            return [text, field, after];
        }

        public TextBlock Note(string text)
        {
            var t = OptionsUi.Text(text, 12, brush: "Brush.TertiaryText", wrap: true);
            Put(t, NextRow(), 1, 2);
            return t;
        }
    }

    private static string EncodingName(int codePage) => codePage switch
    {
        0 => AppStrings.SrvSet_EncodingAuto,
        65001 => "UTF-8",
        1250 => AppStrings.SrvSet_Enc1250,
        1252 => AppStrings.SrvSet_Enc1252,
        1251 => AppStrings.SrvSet_Enc1251,
        1253 => AppStrings.SrvSet_Enc1253,
        1254 => AppStrings.SrvSet_Enc1254,
        1257 => AppStrings.SrvSet_Enc1257,
        28592 => AppStrings.SrvSet_Enc28592,
        932 => AppStrings.SrvSet_Enc932,
        936 => AppStrings.SrvSet_Enc936,
        949 => AppStrings.SrvSet_Enc949,
        _ => AppStrings.SrvSet_Enc950,
    };

    private static string ZoneName(int? minutes)
    {
        if (minutes is not { } m)
        {
            return AppStrings.SrvSet_ZoneAuto;
        }

        var span = TimeSpan.FromMinutes(Math.Abs(m));
        return "UTC" + (m < 0 ? "−" : "+") + span.Hours.ToString("00", System.Globalization.CultureInfo.InvariantCulture) + ":" + span.Minutes.ToString("00", System.Globalization.CultureInfo.InvariantCulture);
    }

    private bool UsingDefaults(int tab) => !_forDefaults && _useDefaults[tab].IsChecked == true;

    private void LoadConnection(ConnectionOptions o)
    {
        _tls.SelectedIndex = (int)o.Tls;
        _data.SelectedIndex = (int)o.Data;
        _keepAlive.IsChecked = o.KeepAlive;
        _keepEvery.Value = o.KeepAliveSeconds;
        _keepStop.Value = o.KeepAliveStopMinutes;
        _connect.Value = o.ConnectSeconds;
        _answer.Value = o.AnswerSeconds;
        _retries.Value = o.Retries;
        _retryApart.Value = o.RetrySeconds;
        _atOnce.Value = o.Connections;
    }

    private void LoadProxy(ProxyOptions p)
    {
        _proxy.SelectedIndex = (int)p.Kind;
        _proxyHost.Text = p.Host;
        _proxyPort.Value = p.Port is > 0 ? p.Port : (p.Kind == ProxyKind.Http ? 8080 : 1080);
        _proxyUser.Text = p.User;
        _proxyPassword.Text = "";
        ShowProxy();
    }

    private void LoadTransfers(TransferSettingsForServer t)
    {
        _mode.SelectedIndex = (int)t.Mode;
        _asciiNames.Text = t.AsciiNames;
        _asciiNames.IsEnabled = t.Mode == TransferMode.Automatic;
        _uploadRights.SelectedIndex = t.UploadMode is null ? 0 : 1;
        _uploadNumber.Text = t.UploadMode is null ? "644" : t.UploadPermissions;
        ShowUploadNumber();
        _compress.IsChecked = t.Compress;
    }

    private void LoadListing(ListingOptions l)
    {
        _serverType.SelectedIndex = (int)l.ServerType;
        _listCommand.SelectedIndex = (int)l.ListCommand;
        _encoding.SelectedIndex = Math.Max(0, Array.IndexOf(_codePages, l.CodePage));
        _timeZone.SelectedIndex = Math.Max(0, Array.IndexOf(_zones, l.TimeZoneMinutes));
        _hidden.IsChecked = l.ShowHidden;
        _cache.IsChecked = l.Cache;
        _cacheMinutes.Value = l.CacheMinutes;
    }

    private static int Value(NumericUpDown box, int fallback) => box.Value is { } v ? (int)v : fallback;

    private ConnectionOptions ReadConnection() => new ConnectionOptions
    {
        Tls = (TlsVersions)Math.Max(0, _tls.SelectedIndex),
        Data = (DataConnection)Math.Max(0, _data.SelectedIndex),
        KeepAlive = _keepAlive.IsChecked == true,
        KeepAliveSeconds = Value(_keepEvery, 60),
        KeepAliveStopMinutes = Value(_keepStop, 30),
        ConnectSeconds = Value(_connect, 20),
        AnswerSeconds = Value(_answer, 30),
        Retries = Value(_retries, 3),
        RetrySeconds = Value(_retryApart, 10),
        Connections = Value(_atOnce, 2),
    }.Normalize();

    private ProxyOptions ReadProxy() => new ProxyOptions
    {
        Kind = (ProxyKind)Math.Max(0, _proxy.SelectedIndex),
        Host = (_proxyHost.Text ?? "").Trim(),
        Port = Value(_proxyPort, 0),
        User = (_proxyUser.Text ?? "").Trim(),
    }.Normalize();

    private TransferSettingsForServer ReadTransfers() => new TransferSettingsForServer
    {
        Mode = (TransferMode)Math.Max(0, _mode.SelectedIndex),
        AsciiNames = _asciiNames.Text ?? "",
        UploadPermissions = _uploadRights.SelectedIndex == 1 ? (_uploadNumber.Text ?? "").Trim() : "",
        Compress = _compress.IsChecked == true,
    }.Normalize();

    private ListingOptions ReadListing() => new ListingOptions
    {
        ServerType = (ServerType)Math.Max(0, _serverType.SelectedIndex),
        ListCommand = (ListCommand)Math.Max(0, _listCommand.SelectedIndex),
        CodePage = _codePages[Math.Max(0, _encoding.SelectedIndex)],
        TimeZoneMinutes = _zones[Math.Max(0, _timeZone.SelectedIndex)],
        ShowHidden = _hidden.IsChecked == true,
        Cache = _cache.IsChecked == true,
        CacheMinutes = Value(_cacheMinutes, 10),
    }.Normalize();

    private void UseDefaultsChanged(int tab)
    {
        var useDefaults = UsingDefaults(tab);
        switch (tab)
        {
            case 0:
                LoadConnection(useDefaults ? _defaults.Connection : _own.Connection);
                break;
            case 1:
                LoadProxy(useDefaults ? _defaults.Proxy : _own.Proxy);
                break;
            case 2:
                LoadTransfers(useDefaults ? _defaults.Transfers : _own.Transfers);
                break;
            default:
                LoadListing(useDefaults ? _defaults.Listing : _own.Listing);
                break;
        }

        _tabBodies[tab].IsEnabled = !useDefaults;
        if (tab == 2)
        {
            _asciiNames.IsEnabled = _mode.SelectedIndex == 2;
        }
    }

    private void ShowProxy()
    {
        var own = _proxy.SelectedIndex is 2 or 3;
        foreach (var c in _proxyRows)
        {
            c.IsVisible = own;
        }

        _proxyWindowsNote.IsVisible = _proxy.SelectedIndex == 1;
        ShowProxyKept();
    }

    private string? ProxyTarget()
    {
        var p = ReadProxy();
        return p.Kind is ProxyKind.Socks5 or ProxyKind.Http && p.Host.Length > 0 && p.User.Length > 0 ? RemoteOptions.ProxyTarget(p) : null;
    }

    private void ShowProxyKept()
    {
        var target = ProxyTarget();
        _forgetProxy.IsVisible = _proxyPassword.IsVisible && target is not null && _secrets is not null && _secrets.List(target).Contains(target, StringComparer.OrdinalIgnoreCase);
        _proxyPassword.PlaceholderText = _forgetProxy.IsVisible ? AppStrings.SrvSet_ProxyPasswordKept : "";
    }

    private void ForgetProxyPassword()
    {
        if (ProxyTarget() is { } target)
        {
            _secrets?.Delete(target);
        }

        ShowProxyKept();
    }

    private void ShowUploadNumber()
    {
        foreach (var c in _uploadNumberRow)
        {
            c.IsVisible = _uploadRights.SelectedIndex == 1;
        }

        ShowUploadMeaning();
    }

    private void ShowUploadMeaning()
    {
        var number = (_uploadNumber.Text ?? "").Trim();
        if (new TransferSettingsForServer { UploadPermissions = number }.UploadMode is not { } mode)
        {
            _uploadMeaning.Text = AppStrings.Perm_BadNumber;
            _uploadMeaning.Res(TextBlock.ForegroundProperty, "Brush.Danger");
            return;
        }

        _uploadMeaning.Res(TextBlock.ForegroundProperty, "Brush.SecondaryText");
        _uploadMeaning.Text = PermissionMeaning(mode);
    }

    internal static string PermissionMeaning(int mode)
    {
        string Who(string who, int bits)
        {
            List<string> can = [];
            if ((bits & 4) != 0)
            {
                can.Add(AppStrings.Perm_Read);
            }

            if ((bits & 2) != 0)
            {
                can.Add(AppStrings.Perm_Write);
            }

            if ((bits & 1) != 0)
            {
                can.Add(AppStrings.Perm_Run);
            }

            return Loc.Format(AppStrings.SrvSet_PermWho, who, can.Count == 0 ? AppStrings.SrvSet_PermNothing : string.Join(AppStrings.SrvSet_PermAnd, can));
        }

        return string.Join(" · ", Who(AppStrings.Perm_Owner, mode >> 6 & 7), Who(AppStrings.Perm_Group, mode >> 3 & 7), Who(AppStrings.Perm_Others, mode & 7));
    }

    internal void Restore()
    {
        if (_forDefaults)
        {
            LoadConnection(ServerOptions.BuiltIn.Connection);
            LoadProxy(ServerOptions.BuiltIn.Proxy);
            LoadTransfers(ServerOptions.BuiltIn.Transfers);
            LoadListing(ServerOptions.BuiltIn.Listing);
            return;
        }

        foreach (var use in _useDefaults)
        {
            use.IsChecked = true;
        }
    }

    internal void Accept()
    {
        var transfers = ReadTransfers();
        if (_uploadRights.SelectedIndex == 1 && transfers.UploadMode is null)
        {
            Refuse(2, AppStrings.Perm_BadNumber);
            return;
        }

        var proxy = ReadProxy();
        if (proxy.Kind is ProxyKind.Socks5 or ProxyKind.Http && !ServerAddress.IsHostName(proxy.Host))
        {
            Refuse(1, AppStrings.SrvSet_BadProxy);
            return;
        }

        if (_secrets is not null && ProxyTarget() is { } target && _proxyPassword.Text is { Length: > 0 } password)
        {
            _secrets.Write(target, proxy.User, password);
        }

        _proxyPassword.Text = "";
        var connection = ReadConnection();
        Close(_forDefaults
            ? new ServerSettingsChoice(connection, proxy, transfers, ReadListing())
            : new ServerSettingsChoice(UsingDefaults(0) ? null : connection, UsingDefaults(1) ? null : proxy, UsingDefaults(2) ? null : transfers, UsingDefaults(3) ? null : ReadListing()));
    }

    private void Refuse(int tab, string text)
    {
        Tabs.SelectedIndex = tab;
        Error.Text = text;
        Error.IsVisible = true;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (!e.Handled && e.Key == Key.Escape)
        {
            Close(null);
            e.Handled = true;
        }
    }
}

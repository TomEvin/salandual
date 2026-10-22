// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.Core/Settings/AppSettings.cs

using Salandual.Core.Resources;

namespace Salandual.Core.Settings;

public sealed record AppSettings
{
    public const int CurrentSchemaVersion = 8;

    public int SchemaVersion { get; set; } = CurrentSchemaVersion;
    public AppearanceSettings Appearance { get; set; } = new();
    public LayoutSettings Layout { get; set; } = new();
    public PanelSettings Panels { get; set; } = new();
    public AdvancedSettings Advanced { get; set; } = new();

    public HistorySettings History { get; set; } = new();

    public HotPathSettings HotPaths { get; set; } = new();

    public DriveMenuSettings DriveMenu { get; set; } = new();

    public OperationSettings Operations { get; set; } = new();

    public MouseSettings Mouse { get; set; } = new();

    public ViewerSettings Viewers { get; set; } = new();

    public EditorSettings Editors { get; set; } = new();

    public FindSettings Find { get; set; } = new();

    public CompareSettings Compare { get; set; } = new();

    public FileListSettings FileList { get; set; } = new();

    public ConfirmationSettings Confirmations { get; set; } = new();

    public GeneralSettings General { get; set; } = new();

    public KeyboardSettings Keyboard { get; set; } = new();

    public Salandual.Core.Operations.TransferOptions TransferOptions() => (Confirmations ?? new()).Apply(Operations.ToOptions());

    public AppSettings Normalize() => this with
    {
        SchemaVersion = CurrentSchemaVersion,
        Appearance = (Appearance ?? new()).Normalize(),
        Layout = (Layout ?? new()).Normalize(),
        Panels = (Panels ?? new()).Normalize(),
        Advanced = Advanced ?? new(),
        History = (History ?? new()).Normalize(),
        HotPaths = (HotPaths ?? new()).Normalize(),
        DriveMenu = (DriveMenu ?? new()).Normalize(),
        Operations = (Operations ?? new()).Normalize(),
        Mouse = (Mouse ?? new()).Normalize(),
        Viewers = (Viewers ?? new()).Normalize(),
        Editors = (Editors ?? new()).Normalize(),
        Find = (Find ?? new()).Normalize(),
        Compare = (Compare ?? new()).Normalize(),
        FileList = (FileList ?? new()).Normalize(),
        Confirmations = Confirmations ?? new(),
        General = General ?? new(),
        Keyboard = (Keyboard ?? new()).Normalize(),
    };
}

public sealed record KeyboardSettings
{
    public Salandual.Core.Commands.KeyMapName KeyMap { get; set; } = Salandual.Core.Commands.KeyMapName.Salamander;

    internal KeyboardSettings Normalize() => this with { KeyMap = Enum.IsDefined(KeyMap) ? KeyMap : Salandual.Core.Commands.KeyMapName.Salamander };
}

public enum QuickSearchStart
{
    Letters,

    CtrlAltLetter,
}

public sealed record AppearanceSettings
{
    public ThemeSchedule Schedule { get; set; } = new();

    public List<ColorTheme> Themes { get; set; } = [];

    public FontSetting PanelFont { get; set; } = new() { Size = FontSetting.DefaultPanelSize };
    public FontSetting MonoFont { get; set; } = new() { Family = "Cascadia Mono" };

    public static AppearanceSettings Fixed(string themeId) => new() { Schedule = new ThemeSchedule { Mode = ScheduleMode.None, Active = themeId } };

    internal AppearanceSettings Normalize()
    {
        var builtIn = new HashSet<string>([.. ThemeCatalog.BuiltIn.Select(t => t.Id), .. ThemeCatalog.Shipped.Select(t => t.Id)], StringComparer.Ordinal);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var themes = new List<ColorTheme>();
        foreach (var theme in Themes ?? [])
        {
            var t = theme?.Normalize();
            if (t is not null && t.Id.Length > 0 && !builtIn.Contains(t.Id) && seen.Add(t.Id))
            {
                themes.Add(t);
            }
        }

        var normalized = this with
        {
            Themes = themes,
            PanelFont = (PanelFont ?? new() { Size = FontSetting.DefaultPanelSize }).Normalize(),
            MonoFont = (MonoFont ?? new() { Family = "Cascadia Mono" }).Normalize(),
        };
        return normalized with { Schedule = (Schedule ?? new()).Normalize(normalized) };
    }

    public bool IsEquivalentTo(AppearanceSettings? other)
        => other is not null
           && System.Text.Json.JsonSerializer.Serialize(this, SettingsJsonContext.Default.AppearanceSettings)
              == System.Text.Json.JsonSerializer.Serialize(other, SettingsJsonContext.Default.AppearanceSettings);
}

public sealed record FontSetting
{
    public const double MinSize = 9;
    public const double MaxSize = 24;
    public const double DefaultPanelSize = 12;

    public string? Family { get; set; }
    public double Size { get; set; } = 13;

    internal FontSetting Normalize() => this with
    {
        Family = string.IsNullOrWhiteSpace(Family) ? null : Family.Trim(),
        Size = Math.Clamp(double.IsFinite(Size) ? Size : 13, MinSize, MaxSize),
    };
}

public enum BackgroundPlacement { Panels, Window }

public enum BackgroundFit { Fill, Fit, Stretch, Tile, Center }

public enum BackgroundPosition { Center, TopLeft, TopRight, BottomLeft, BottomRight }

public sealed record BackgroundImageSettings
{
    public const double MinOpacity = 0.05;
    public const double MaxOpacity = 0.60;

    public string? Path { get; set; }
    public BackgroundPlacement Placement { get; set; } = BackgroundPlacement.Panels;
    public BackgroundFit Fit { get; set; } = BackgroundFit.Fill;
    public BackgroundPosition Position { get; set; } = BackgroundPosition.Center;
    public double Opacity { get; set; } = 0.15;

    public bool Blur { get; set; }

    internal BackgroundImageSettings Normalize() => this with
    {
        Path = string.IsNullOrWhiteSpace(Path) ? null : Path,
        Opacity = Math.Clamp(double.IsFinite(Opacity) ? Opacity : 0.15, MinOpacity, MaxOpacity),
    };
}

public enum StripMode { PerPanel, Shared }

public enum TabBarMode { WhenNeeded, Always, Never }

public enum MenuButtonStyle { AppIcon, Hamburger }

public enum MenuRow { InTitleBar, OwnRow }

public sealed record LayoutSettings
{
    public const int MinRowHeight = 20;
    public const int MaxRowHeight = 32;

    public static readonly IReadOnlyList<(string Name, int Height)> RowHeightStops = [(Strings.RowHeight_Compact, 22), (Strings.RowHeight_Default, 24), (Strings.RowHeight_Comfortable, 28)];

    public int RowHeight { get; set; } = 24;
    public StripMode Strip { get; set; } = StripMode.PerPanel;
    public TabBarMode TabBar { get; set; } = TabBarMode.WhenNeeded;
    public BarVisibility Bars { get; set; } = new();

    public MenuButtonStyle MenuButton { get; set; } = MenuButtonStyle.AppIcon;

    public MenuRow MenuRow { get; set; } = MenuRow.InTitleBar;

    public string WindowTitle { get; set; } = Panels.WindowTitle.Default;

    public bool TitleShowsVersion { get; set; }

    public DirectoryLineSettings DirectoryLine { get; set; } = new();

    public double PanelSplit { get; set; } = 0.5;

    public const double MinPanelSplit = 0.1;
    public const double MaxPanelSplit = 0.9;

    internal LayoutSettings Normalize() => this with
    {
        RowHeight = Math.Clamp(RowHeight, MinRowHeight, MaxRowHeight),
        Bars = Bars ?? new(),
        DirectoryLine = (DirectoryLine ?? new()).Normalize(),
        MenuButton = Enum.IsDefined(MenuButton) ? MenuButton : MenuButtonStyle.AppIcon,
        MenuRow = Enum.IsDefined(MenuRow) ? MenuRow : MenuRow.InTitleBar,
        WindowTitle = string.IsNullOrWhiteSpace(WindowTitle) ? Panels.WindowTitle.Default : WindowTitle,
        PanelSplit = double.IsFinite(PanelSplit) ? Math.Clamp(PanelSplit, MinPanelSplit, MaxPanelSplit) : 0.5,
    };
}

public enum ChangeDirectoryStyle
{
    InBar,

    Dialog,
}

public enum WorkingDirectoriesScope
{
    PerPanel,

    Shared,
}

public sealed record DirectoryLineSettings
{
    public ChangeDirectoryStyle ChangeDirectory { get; set; } = ChangeDirectoryStyle.InBar;
    public WorkingDirectoriesScope WorkingDirectories { get; set; } = WorkingDirectoriesScope.PerPanel;

    public bool MaximizeButton { get; set; } = true;

    internal DirectoryLineSettings Normalize() => this with
    {
        ChangeDirectory = Enum.IsDefined(ChangeDirectory) ? ChangeDirectory : ChangeDirectoryStyle.InBar,
        WorkingDirectories = Enum.IsDefined(WorkingDirectories) ? WorkingDirectories : WorkingDirectoriesScope.PerPanel,
    };
}

public sealed record BarVisibility
{
    public bool MenuBar { get; set; } = true;
    public bool Toolbar { get; set; } = true;
    public bool UserMenuBar { get; set; } = true;
    public bool DriveStrip { get; set; } = true;

    public bool HotPathBar { get; set; } = true;
    public bool DirectoryLine { get; set; } = true;
    public bool ColumnHeader { get; set; } = true;
    public bool InformationLine { get; set; } = true;
    public bool CommandLine { get; set; } = true;
    public bool FunctionKeyBar { get; set; } = true;

    public bool MiddleToolbar { get; set; } = true;
    public bool OperationsIndicator { get; set; } = true;
}

public sealed record PanelSettings
{
    public bool ShowHiddenFiles { get; set; }
    public bool NumericSort { get; set; } = true;
    public bool SelectFoldersWithNumPlus { get; set; }

    public bool ClickToRename { get; set; } = true;

    public bool ExtensionColumn { get; set; } = true;
    public const string DefaultInformationLine = "$(FileName): $(FileSize), $(FileDate), $(FileTime), $(FileAttributes)";

    public string InformationLine { get; set; } = DefaultInformationLine;

    public SizeUnits SizeUnits { get; set; } = SizeUnits.Windows;

    public string DateFormat { get; set; } = DateFormatIso;

    public TimeFormat TimeFormat { get; set; } = TimeFormat.Windows;

    public QuickSearchStart QuickSearch { get; set; } = QuickSearchStart.Letters;

    public const string DateFormatIso = "iso";
    public const string DateFormatSystem = "system";

    internal PanelSettings Normalize() => this with
    {
        InformationLine = string.IsNullOrWhiteSpace(InformationLine) ? DefaultInformationLine : InformationLine,
        SizeUnits = Enum.IsDefined(SizeUnits) ? SizeUnits : SizeUnits.Windows,
        DateFormat = NormalizeDateFormat(DateFormat),
        TimeFormat = Enum.IsDefined(TimeFormat) ? TimeFormat : TimeFormat.Windows,
        QuickSearch = Enum.IsDefined(QuickSearch) ? QuickSearch : QuickSearchStart.Letters,
    };

    private static string NormalizeDateFormat(string? format)
    {
        if (string.IsNullOrWhiteSpace(format))
        {
            return DateFormatIso;
        }

        format = format.Trim();
        if (format.Equals(DateFormatIso, StringComparison.OrdinalIgnoreCase) || format.Equals(DateFormatSystem, StringComparison.OrdinalIgnoreCase))
        {
            return format.ToLowerInvariant();
        }

        try
        {
            var culture = System.Globalization.CultureInfo.GetCultureInfo(format, predefinedOnly: true);
            return culture.Name.Length > 0 ? culture.Name : DateFormatIso;
        }
        catch (System.Globalization.CultureNotFoundException)
        {
            return DateFormatIso;
        }
    }
}

public enum TimeFormat
{
    Windows,

    Hours24,

    Hours12,
}

public enum SizeUnits
{
    Windows,

    Binary,

    Decimal,

    Bytes,
}

public enum RenderingMode { Gpu, Software }

public sealed record AdvancedSettings
{
    public RenderingMode Rendering { get; set; } = RenderingMode.Gpu;
}

public sealed record HistorySettings
{
    public const int MaxItems = 20;

    public List<string> SelectMasks { get; set; } = [];

    public List<string> UnselectMasks { get; set; } = [];

    public List<string> CopyTargets { get; set; } = [];

    public static List<string> Push(IEnumerable<string>? list, string value)
        => Clean([value, .. list ?? []]);

    internal HistorySettings Normalize() => this with
    {
        SelectMasks = Clean(SelectMasks),
        UnselectMasks = Clean(UnselectMasks),
        CopyTargets = Clean(CopyTargets),
    };

    private static List<string> Clean(IEnumerable<string?>? list)
    {
        var result = new List<string>();
        foreach (var item in list ?? [])
        {
            var value = item?.Trim();
            if (!string.IsNullOrEmpty(value) && !result.Contains(value, StringComparer.OrdinalIgnoreCase) && result.Count < MaxItems)
            {
                result.Add(value);
            }
        }

        return result;
    }
}

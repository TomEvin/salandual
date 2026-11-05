// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.Core/Settings/AppSettings.cs

using System.Text.Json.Serialization;
using Salandual.Core.Panels;
using Salandual.Core.Resources;

namespace Salandual.Core.Settings;

public sealed record AppSettings
{
    public const int CurrentSchemaVersion = 10;

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

    public UserMenu.UserMenuSettings UserMenu { get; set; } = new();

    public CommandLine.CommandLineSettings CommandLine { get; set; } = new();

    public GeneralSettings General { get; set; } = new();

    public KeyboardSettings Keyboard { get; set; } = new();

    public QuickOpenSettings QuickOpen { get; set; } = new();

    public WindowSettings Windows { get; set; } = new();

    public SharingSettings Sharing { get; set; } = new();

    public HighlightSettings Highlighting { get; set; } = new();

    public ServerSettings Servers { get; set; } = new();

    public Salandual.Core.Operations.TransferOptions TransferOptions() => (Confirmations ?? new()).Apply(Operations.ToOptions());

    public AppSettings Normalize() => this with
    {
        SchemaVersion = CurrentSchemaVersion,
        Appearance = (Appearance ?? new()).Normalize(),
        Layout = (Layout ?? new()).Normalize(),
        Panels = (Panels ?? new()).Normalize() with { DimHidden = true },
        Highlighting = (Highlighting ?? new()).Normalize(Panels?.DimHidden ?? true),
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
        UserMenu = (UserMenu ?? new()).Normalize(),
        CommandLine = (CommandLine ?? new()).Normalize(),
        General = General ?? new(),
        Keyboard = (Keyboard ?? new()).Normalize(),
        QuickOpen = (QuickOpen ?? new()).Normalize(),
        Windows = (Windows ?? new()).Normalize(),
        Sharing = (Sharing ?? new()).Normalize(),
        Servers = (Servers ?? new()).Normalize(),
    };
}

public enum QuickOpenFileEnter
{
    GoTo,

    Open,
}

public sealed record QuickOpenSettings
{
    public bool Remember { get; set; } = true;

    public QuickOpenFileEnter FileEnter { get; set; } = QuickOpenFileEnter.GoTo;

    public QuickOpenFilesFrom FilesFrom { get; set; } = QuickOpenFilesFrom.Best;

    public List<string> SkipDrives { get; set; } = [];

    public const int MinWalkEntries = 50_000, MaxWalkEntries = 5_000_000, MinWalkSeconds = 3, MaxWalkSeconds = 60;

    public int WalkEntries { get; set; } = 500_000;

    public int WalkSeconds { get; set; } = 15;

    public bool SkipHidden { get; set; } = true;

    public bool SkipSystem { get; set; } = true;

    public bool SkipWindowsFolders { get; set; } = true;

    public List<string> SkipFolders { get; set; } = ["node_modules", ".git"];

    internal QuickOpenSettings Normalize() => this with
    {
        FileEnter = Enum.IsDefined(FileEnter) ? FileEnter : QuickOpenFileEnter.GoTo,
        FilesFrom = Enum.IsDefined(FilesFrom) ? FilesFrom : QuickOpenFilesFrom.Best,
        SkipDrives = [.. (SkipDrives ?? []).Where(d => d is { Length: 2 } && char.IsAsciiLetter(d[0]) && d[1] == ':').Select(d => d.ToUpperInvariant()).Distinct()],
        WalkEntries = Math.Clamp(WalkEntries, MinWalkEntries, MaxWalkEntries),
        WalkSeconds = Math.Clamp(WalkSeconds, MinWalkSeconds, MaxWalkSeconds),
        SkipFolders = QuickOpen.WalkFilter.CleanList(SkipFolders),
    };

    public QuickOpen.WalkFilter Filter(bool forDrives) => new(SkipHidden, SkipSystem, forDrives && SkipWindowsFolders, SkipFolders);
}

public enum QuickOpenFilesFrom
{
    Best,

    NoEverything,

    WalkOnly,

    Off,
}

public sealed record KeyboardSettings
{
    public const int MaxYourKeys = 1000;

    public Salandual.Core.Commands.KeyMapName KeyMap { get; set; } = Salandual.Core.Commands.KeyMapName.Salamander;

    public List<Salandual.Core.Commands.CommandKeys> Keys { get; set; } = [];

    internal KeyboardSettings Normalize() => this with
    {
        KeyMap = Enum.IsDefined(KeyMap) ? KeyMap : Salandual.Core.Commands.KeyMapName.Salamander,
        Keys = Salandual.Core.Commands.CommandKeys.Clean(Keys),
    };
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

    public List<string> KeptThemes { get; set; } = [];

    public bool FollowTextSize { get; set; } = true;

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
            KeptThemes = [.. (KeptThemes ?? []).Where(id => !string.IsNullOrEmpty(id)).Distinct(StringComparer.Ordinal)],
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

public enum NewTabOpens { SameFolder, Place }

public enum LockedTab { CantClose, StaysInFolder }

public enum TabColourLook { Bar, Tint, None }

public enum TabsAtStart { All, ActiveOnly }

public enum ManyTabs { Scroll, Rows }

public sealed record TabSettings
{
    public NewTabOpens NewTab { get; set; } = NewTabOpens.SameFolder;

    public string NewTabPlace { get; set; } = "";

    public LockedTab Locked { get; set; } = LockedTab.CantClose;

    public TabColourLook Colours { get; set; } = TabColourLook.Bar;

    public TabsAtStart AtStart { get; set; } = TabsAtStart.All;

    public ManyTabs Many { get; set; } = ManyTabs.Scroll;

    internal TabSettings Normalize() => this with
    {
        NewTab = Enum.IsDefined(NewTab) ? NewTab : NewTabOpens.SameFolder,
        NewTabPlace = NewTabPlace?.Trim() ?? "",
        Locked = Enum.IsDefined(Locked) ? Locked : LockedTab.CantClose,
        Colours = Enum.IsDefined(Colours) ? Colours : TabColourLook.Bar,
        AtStart = Enum.IsDefined(AtStart) ? AtStart : TabsAtStart.All,
        Many = Enum.IsDefined(Many) ? Many : ManyTabs.Scroll,
    };
}

public enum TabBarMode { WhenNeeded, Always, Never }

public enum MenuButtonStyle { AppIcon, Hamburger }

public enum MenuRow { InTitleBar, OwnRow }

public enum BarHeight { Lower, Normal, Taller }

public sealed record LayoutSettings
{
    public const int MinRowHeight = 20;
    public const int MaxRowHeight = 32;

    public static readonly IReadOnlyList<(string Name, int Height)> RowHeightStops = [(Strings.RowHeight_Compact, 22), (Strings.RowHeight_Default, 24), (Strings.RowHeight_Comfortable, 28)];

    public int RowHeight { get; set; } = 24;
    public StripMode Strip { get; set; } = StripMode.PerPanel;
    public TabBarMode TabBar { get; set; } = TabBarMode.WhenNeeded;
    public BarVisibility Bars { get; set; } = new();

    public BarHeight BarHeight { get; set; } = BarHeight.Normal;

    public BarBlockSettings Blocks { get; set; } = new();

    public MenuButtonStyle MenuButton { get; set; } = MenuButtonStyle.AppIcon;

    public MenuRow MenuRow { get; set; } = MenuRow.InTitleBar;

    public string WindowTitle { get; set; } = Panels.WindowTitle.Default;

    public bool TitleShowsVersion { get; set; }

    public DirectoryLineSettings DirectoryLine { get; set; } = new();

    public BackForwardSettings BackForward { get; set; } = new();

    public double PanelSplit { get; set; } = 0.5;

    public const double MinPanelSplit = 0.1;
    public const double MaxPanelSplit = 0.9;

    internal LayoutSettings Normalize() => this with
    {
        RowHeight = Math.Clamp(RowHeight, MinRowHeight, MaxRowHeight),
        Bars = Bars ?? new(),
        Blocks = (Blocks ?? new()).Normalize(),
        DirectoryLine = (DirectoryLine ?? new()).Normalize(),
        BackForward = (BackForward ?? new()).Normalize(),
        MenuButton = Enum.IsDefined(MenuButton) ? MenuButton : MenuButtonStyle.AppIcon,
        MenuRow = Enum.IsDefined(MenuRow) ? MenuRow : MenuRow.InTitleBar,
        WindowTitle = string.IsNullOrWhiteSpace(WindowTitle) ? Panels.WindowTitle.Default : WindowTitle,
        PanelSplit = double.IsFinite(PanelSplit) ? Math.Clamp(PanelSplit, MinPanelSplit, MaxPanelSplit) : 0.5,
    };
}

public enum BackForwardPlace
{
    Bar,

    DirectoryLines,

    Both,

    Off,
}

public enum StepsListOpens
{
    Button,

    Hold,
}

public enum StepsListed
{
    ThisPanel,

    BothPanels,
}

public sealed record BackForwardSettings
{
    public const int DefaultStepsKept = 50, MinStepsKept = 10, MaxStepsKept = 200;

    public BackForwardPlace Place { get; set; } = BackForwardPlace.Bar;

    public StepsListOpens ListOpens { get; set; } = StepsListOpens.Button;

    public StepsListed Listed { get; set; } = StepsListed.ThisPanel;

    public int StepsKept { get; set; } = DefaultStepsKept;

    public bool MouseButtons { get; set; } = true;

    [JsonIgnore]
    public bool InBar => Place is BackForwardPlace.Bar or BackForwardPlace.Both;

    [JsonIgnore]
    public bool InDirectoryLines => Place is BackForwardPlace.DirectoryLines or BackForwardPlace.Both;

    public BackForwardSettings WithBar(bool on) => this with
    {
        Place = (on, InDirectoryLines) switch
        {
            (true, true) => BackForwardPlace.Both,
            (true, false) => BackForwardPlace.Bar,
            (false, true) => BackForwardPlace.DirectoryLines,
            _ => BackForwardPlace.Off,
        },
    };

    internal BackForwardSettings Normalize() => this with
    {
        Place = Enum.IsDefined(Place) ? Place : BackForwardPlace.Bar,
        ListOpens = Enum.IsDefined(ListOpens) ? ListOpens : StepsListOpens.Button,
        Listed = Enum.IsDefined(Listed) ? Listed : StepsListed.ThisPanel,
        StepsKept = Math.Clamp(StepsKept, MinStepsKept, MaxStepsKept),
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

    public int FoldersKept { get; set; } = Panels.WorkingDirectories.DefaultLimit;

    public bool MaximizeButton { get; set; } = true;

    internal DirectoryLineSettings Normalize() => this with
    {
        ChangeDirectory = Enum.IsDefined(ChangeDirectory) ? ChangeDirectory : ChangeDirectoryStyle.InBar,
        WorkingDirectories = Enum.IsDefined(WorkingDirectories) ? WorkingDirectories : WorkingDirectoriesScope.PerPanel,
        FoldersKept = Math.Clamp(FoldersKept, Panels.WorkingDirectories.MinLimit, Panels.WorkingDirectories.MaxLimit),
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

    public SizeStyle SizeStyle { get; set; } = SizeStyle.Short;

    public DigitGrouping DigitGrouping { get; set; } = DigitGrouping.Windows;

    public bool InfoSizesAsPanel { get; set; }

    public FolderSizeText FolderSize { get; set; } = FolderSizeText.Folder;

    public ColumnAlignment ColumnAlignment { get; set; } = ColumnAlignment.NumbersRight;

    public LongNames LongNames { get; set; } = LongNames.CutEnd;

    public bool RowStripes { get; set; }

    public bool DimHidden { get; set; } = true;

    public MarkLook FocusLook { get; set; } = MarkLook.WholeRow;

    public MarkLook SelectionLook { get; set; } = MarkLook.WholeRow;

    public bool FocusBackground { get; set; } = true;

    public MarkLook FocusBackgroundLook { get; set; } = MarkLook.WholeRow;

    public IReadOnlyList<ColumnView> ColumnViews { get; set; } = [];

    public string DateFormat { get; set; } = DateFormatIso;

    public TimeFormat TimeFormat { get; set; } = TimeFormat.Windows;

    public bool HideDotFiles { get; set; }

    public bool FilterThisFolderOnly { get; set; }

    public bool QuickFilterFirst { get; set; }

    public bool HiddenNamesStay { get; set; }

    public Panels.FlatFolderShows FlatFolders { get; set; }

    public int FlatAskAbove { get; set; } = DefaultFlatAskAbove;

    public int ColumnsWidth { get; set; } = DefaultColumnsWidth;

    public bool ColumnsPreview { get; set; } = true;

    public bool PeekOnHover { get; set; }

    public TabSettings Tabs { get; set; } = new();

    public GitPanelSettings Git { get; set; } = new();

    public QuickSearchStart QuickSearch { get; set; } = QuickSearchStart.Letters;

    public PanelView NewPanelView { get; set; } = PanelView.Detailed;

    public bool AltWheelViews { get; set; } = true;

    public bool ViewButton { get; set; } = true;

    public int ThumbnailSize { get; set; } = DefaultThumbnailSize;

    public const int DefaultThumbnailSize = 112, MinThumbnailSize = 48, MaxThumbnailSize = 256;

    public const int DefaultFlatAskAbove = 100_000;

    public const int DefaultColumnsWidth = 32, MinColumnsWidth = 12, MaxColumnsWidth = 80;

    public ThumbnailFiles ThumbnailsOf { get; set; } = ThumbnailFiles.All;

    public TileLines TileLines { get; set; } = TileLines.SizeAndDate;

    public const string DateFormatIso = "iso";
    public const string DateFormatSystem = "system";

    public const string DateFormatRelative = "relative";

    internal PanelSettings Normalize() => this with
    {
        InformationLine = string.IsNullOrWhiteSpace(InformationLine) ? DefaultInformationLine : InformationLine,
        Tabs = (Tabs ?? new()).Normalize(),
        Git = Git ?? new(),
        SizeUnits = Enum.IsDefined(SizeUnits) && SizeUnits != SizeUnits.Bytes ? SizeUnits : SizeUnits.Windows,
        SizeStyle = SizeUnits == SizeUnits.Bytes ? SizeStyle.Bytes : Enum.IsDefined(SizeStyle) ? SizeStyle : SizeStyle.Short,
        DigitGrouping = Enum.IsDefined(DigitGrouping) ? DigitGrouping : DigitGrouping.Windows,
        FolderSize = Enum.IsDefined(FolderSize) ? FolderSize : FolderSizeText.Folder,
        ColumnAlignment = Enum.IsDefined(ColumnAlignment) ? ColumnAlignment : ColumnAlignment.NumbersRight,
        LongNames = Enum.IsDefined(LongNames) ? LongNames : LongNames.CutEnd,
        ExtensionColumn = true,
        ColumnViews = NormalizeColumnViews(ColumnViews, ExtensionColumn),
        DateFormat = NormalizeDateFormat(DateFormat),
        TimeFormat = Enum.IsDefined(TimeFormat) ? TimeFormat : TimeFormat.Windows,
        QuickSearch = Enum.IsDefined(QuickSearch) ? QuickSearch : QuickSearchStart.Letters,
        NewPanelView = PanelViews.Valid(NewPanelView),
        ThumbnailSize = ThumbnailSize == 0 ? DefaultThumbnailSize : Math.Clamp(ThumbnailSize, MinThumbnailSize, MaxThumbnailSize),
        ThumbnailsOf = Enum.IsDefined(ThumbnailsOf) ? ThumbnailsOf : ThumbnailFiles.All,
        TileLines = Enum.IsDefined(TileLines) ? TileLines : TileLines.SizeAndDate,
        FlatFolders = Enum.IsDefined(FlatFolders) ? FlatFolders : Panels.FlatFolderShows.Column,
        FlatAskAbove = FlatAskAbove < 0 ? DefaultFlatAskAbove : FlatAskAbove,
        ColumnsWidth = ColumnsWidth == 0 ? DefaultColumnsWidth : Math.Clamp(ColumnsWidth, MinColumnsWidth, MaxColumnsWidth),
    };

    private static IReadOnlyList<ColumnView> NormalizeColumnViews(IReadOnlyList<ColumnView>? views, bool extensionColumn)
    {
        IReadOnlyList<ColumnView> list = [];
        foreach (var v in (views ?? []).Where(v => v is not null && PanelViews.HasColumns(v.View)).GroupBy(v => v.View).Select(g => g.Last()))
        {
            list = Panels.ColumnViews.With(list, v);
        }

        if (!extensionColumn && list.All(v => v.View != PanelView.Detailed))
        {
            var detailed = Panels.ColumnViews.Default(PanelView.Detailed);
            list = Panels.ColumnViews.With(list, Panels.ColumnViews.Toggle(detailed, PanelColumn.Extension));
        }

        return list;
    }

    private static string NormalizeDateFormat(string? format)
    {
        if (string.IsNullOrWhiteSpace(format))
        {
            return DateFormatIso;
        }

        format = format.Trim();
        if (format.Equals(DateFormatIso, StringComparison.OrdinalIgnoreCase) || format.Equals(DateFormatSystem, StringComparison.OrdinalIgnoreCase) || format.Equals(DateFormatRelative, StringComparison.OrdinalIgnoreCase))
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

public enum SizeStyle
{
    Short,

    Kilobytes,

    Bytes,
}

public enum DigitGrouping
{
    Windows,

    Space,

    None,
}

public enum FolderSizeText
{
    Folder,

    Dir,
    Blank,
}

public enum ColumnAlignment
{
    NumbersRight,
    AllLeft,
}

public enum MarkLook
{
    WholeRow,

    NameOnly,
}

public enum LongNames
{
    CutEnd,

    CutMiddle,
}

public enum ThumbnailFiles
{
    All,

    PicturesAndVideos,
}

public enum TileLines
{
    SizeAndDate,

    TypeAndSize,
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

    public List<string> FilterMasks { get; set; } = [];

    public List<string> CopyTargets { get; set; } = [];

    public List<string> NetworkFolders { get; set; } = [];

    public List<CommandLine.CommandHistoryEntry> Commands { get; set; } = [];

    public List<QuickOpen.UsageEntry> QuickFolders { get; set; } = [];

    public List<QuickOpen.UsageEntry> QuickFiles { get; set; } = [];

    public List<QuickOpen.UsageEntry> QuickCommands { get; set; } = [];

    public static List<string> Push(IEnumerable<string>? list, string value)
        => Clean([value, .. list ?? []]);

    public static List<CommandLine.CommandHistoryEntry> Push(IEnumerable<CommandLine.CommandHistoryEntry>? list, CommandLine.CommandHistoryEntry entry, int max)
        => CleanCommands([entry, .. list ?? []], max);

    internal HistorySettings Normalize() => this with
    {
        SelectMasks = Clean(SelectMasks),
        UnselectMasks = Clean(UnselectMasks),
        FilterMasks = Clean(FilterMasks),
        CopyTargets = Clean(CopyTargets),
        NetworkFolders = Clean(NetworkFolders),
        Commands = CleanCommands(Commands, CommandLine.CommandLineSettings.MaxRemembered),
        QuickFolders = QuickOpen.Usage.Clean(QuickFolders, QuickOpen.Usage.MaxFolders),
        QuickFiles = QuickOpen.Usage.Clean(QuickFiles, QuickOpen.Usage.MaxFiles),
        QuickCommands = QuickOpen.Usage.Clean(QuickCommands, QuickOpen.Usage.MaxCommands),
    };

    private static List<CommandLine.CommandHistoryEntry> CleanCommands(IEnumerable<CommandLine.CommandHistoryEntry?>? list, int max)
    {
        var result = new List<CommandLine.CommandHistoryEntry>();
        foreach (var entry in list ?? [])
        {
            var text = entry?.Text?.Trim();
            if (!string.IsNullOrEmpty(text) && !result.Any(e => e.Text == text) && result.Count < max)
            {
                result.Add(new(text, entry!.Folder ?? ""));
            }
        }

        return result;
    }

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

public sealed record GitPanelSettings
{
    public bool ColorNames { get; set; } = true;

    public bool ColumnInRepositories { get; set; } = true;

    public bool NetworkDrives { get; set; }
}

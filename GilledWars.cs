using Blish_HUD;
using Blish_HUD.Controls;
using Blish_HUD.Input;
using Blish_HUD.Modules;
using Blish_HUD.Modules.Managers;
using Blish_HUD.Settings;
using GilledWars;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

using Gw2Sharp;
using Gw2Sharp.Models;  // For Languages enum
using Gw2Sharp.WebApi.V2.Models;
using Color = Microsoft.Xna.Framework.Color;
using File = System.IO.File;

namespace Gorthax.Gilledwars
{
    public enum CornerIconType
    {
        Bait,
        Hook,
        Hook2,
        Lure,
        Net,
        FishMaster
    }

    public enum TodTextLayout
    {
        Right,
        Left,
        Top,
        Bottom,
        OnImage,
        Hidden
    }

    [Export(typeof(Module))]
    public class GilledWars : Module
    {
        private static readonly Logger Logger = Logger.GetLogger<GilledWars>();
        public static GilledWars Instance { get; private set; }

        // Sentinel value for "All Species" filter - language-independent, never shown to user directly
        private const string AllSpeciesKey = "__ALL_SPECIES__";

        internal SettingsManager SettingsManager => ModuleParameters.SettingsManager;
        internal ContentsManager ContentsManager => ModuleParameters.ContentsManager;
        internal DirectoriesManager DirectoriesManager => ModuleParameters.DirectoriesManager;
        internal Gw2ApiManager Gw2ApiManager => ModuleParameters.Gw2ApiManager;

        private string ModuleDirectory => DirectoriesManager.GetFullDirectoryPath("gilledwars");

        private SettingEntry<KeyBinding> ToggleHotkey { get; set; }
        private SettingEntry<string> _customApiKey { get; set; }
        private SettingEntry<string> _drfToken { get; set; }
        private SettingEntry<string> _discordWebhookUrl { get; set; }
        private SettingEntry<CornerIconType> _cornerIconChoice;
        private SettingEntry<bool> _autoStartLogging;

        // --- Time of Day Variables ---
        private SettingEntry<bool> _showTimeOfDayWidget;
        private SettingEntry<bool> _lockTimeOfDayWidget;
        private SettingEntry<int> _todWidgetSize;
        private SettingEntry<TodTextLayout> _todTextLayout;
        private SettingEntry<int> _todLocX; 
        private SettingEntry<int> _todLocY; 

        private Panel _timeOfDayPanel;
        private Image _todIcon;
        private Label _todLabel;
        private bool _isTodDragging = false;
        private Point _todDragOffset;

        private Blish_HUD.Content.AsyncTexture2D _texDawn;
        private Blish_HUD.Content.AsyncTexture2D _texDay;
        private Blish_HUD.Content.AsyncTexture2D _texDusk;
        private Blish_HUD.Content.AsyncTexture2D _texCaughtCheck;
        private Blish_HUD.Content.AsyncTexture2D _texNight;
        private string _currentTodPhase = "";

        // --- API Config ---
        private const string API_BASE_URL = "https://api.gilledwars.com";
        private static readonly HttpClient _httpClient = CreateHttpClient();

        private static HttpClient CreateHttpClient()
        {
            var client = new HttpClient();
            client.DefaultRequestHeaders.Add("X-Gilled-Wars-Client", "SecureBlishModule_v1");
            return client;
        }

        // --- UI Elements ---
        private Panel _mainWindow;
        private Panel _casualPanel, _tournamentPanel;
        private Panel _recentCatchesPanel, _fishLogPanel;
        private Panel _tourneyHostPanel, _tourneyParticipantPanel;

        // --- Dedicated Tournament UI ---
        private Panel _tourneyActivePanel;
        private Label _activeTimerLabel;
        private Label _waitingRoomLabel;
        private Label _activeSyncTimerLabel;
        private FlowPanel _activeCoolerList;
        private StandardButton _activeMeasureBtn;
        private StandardButton _activeEndBtn;
        private StandardButton _activeRecopyBtn;
        private StandardButton _activeExitBtn;

        private CornerIcon _cornerIcon;

        // --- Data & Filtering ---
        private List<FishUIEntry> _allFishEntries = new List<FishUIEntry>();
        private List<FlowPanel> _categoryPanels = new List<FlowPanel>();
        private List<TournamentCatch> _recentCatches = new List<TournamentCatch>();
        private List<TournamentCatch> _tourneyCatches = new List<TournamentCatch>();
        private Dictionary<int, int> _startInventory = new Dictionary<int, int>();
        private HashSet<int> _caughtFishIds = new HashSet<int>();
        private Dictionary<int, PersonalBestRecord> _personalBests = new Dictionary<int, PersonalBestRecord>();
        private Label _lbDynamicTitleLabel;
        private bool _isAnalyzerMinified = false;
        private int _analyzerGridCols = 4;
        private string _openAnalyzerLocationName;
        private int _openAnalyzerAchievementId;
        private int _openAnalyzerSubMax;
        private string _openAnalyzerSubDescription;

        // --- Profit Tracker Variables ---
        private SettingEntry<bool> _showProfitWidget;
        private SettingEntry<int> _profitLocX;
        private SettingEntry<int> _profitLocY;
        private int _fineFilletPriceCopper = 0;

        private Panel _profitWidgetPanel;
        private Label _profitTotalLabel;
        private Label _gphLabel;
        private bool _isProfitDragging = false;
        private Point _profitDragOffset;

        private int _ambergrisPriceCopper = 0;
        private int _filletPriceCopper = 0;
        private int _sessionTotalCopper = 0;
        private DateTime _sessionStartTime = DateTime.MinValue;

        // --- DRF WebSocket Variables ---
        private System.Net.WebSockets.ClientWebSocket _drfSocket;
        private System.Threading.CancellationTokenSource _drfCts;
        private Task _drfReceiveTask;

        // --- State Variables ---
        private static readonly Random _rnd = new Random();
        private bool _isCasualLoggingActive = false;
        private DateTime _lastSubmitTime = DateTime.MinValue;
        private DateTime _lastAutoSubmitTime = DateTime.Now;
        private string _localAccountName = "UnknownAccount";
        private Panel _leaderboardWindow;
        private Dropdown _lbSortDropdown;
        private FlowPanel _lbListPanel;
        private bool _isDraggingLeaderboard;
        private Point _leaderboardDragOffset;
        private List<LeaderboardEntry> _cachedLeaderboardData = null;
        private DateTime _lastLeaderboardFetchTime = DateTime.MinValue;
        private Panel _speciesSelectionWindow;
        private StandardButton _speciesFilterBtn;
        private string _currentlySelectedSpecies = AllSpeciesKey;
        private TextBox _speciesSearchBox;
        private string[] _junkMessages;
        private string[] _treasureMessages;

        private Panel _achievementPanel;
        private Panel _achievementResultsPanel;
        private Panel _achievementLegendPanel;
        private bool _isDraggingAchievement;
        private Point _achievementDragOffset;
        private bool _isSpeciesSelectionDragging;
        private Point _speciesSelectionDragOffset;
        private Panel _metaProgressWindow;
        private Point _metaDragOffset;
        private bool _isMetaDragging = false;
        private List<FlowPanel> _allMetaSubPanels = new List<FlowPanel>();
        private Panel _currentlyExpandedRow = null;

        // --- Currently Biting Variables ---
        private SettingEntry<bool> _autoStartBitingWidget;        private Panel _bitingWidgetPanel;
        private SettingEntry<string> _selectedLanguage;
        private FlowPanel _bitingFishList;
        private Label _bitingTitleLabel;
        private bool _isBitingDragging = false;
        private Point _bitingDragOffset;
        private int _lastBitingMapId = 0;
        private SettingEntry<int> _bitingLocX;
        private SettingEntry<int> _bitingLocY;
        private bool _showAllBitingFish = false;
        private StandardButton _bitingToggleBtn;

        // --- Tournament Variables ---
        private bool _isTournamentActive = false;
        private bool _isTourneyWaitingRoom = false;
        private DateTime _tourneyStartTimeUtc;
        private DateTime _tourneyEndTimeUtc;
        private string _tourneyRoomCode = "";
        private Dropdown _hostWinFactorDrop;
        private int _tourneyTargetItemId = 0;
        private string _tourneyWinFactor = "Weight";

        private Label _casualSyncTimerLabel;
        private DateTime _nextSyncTime;
        private bool _isSyncTimerActive = false;

        private StandardButton _casualLogToggleBtn;
        private StandardButton _casualMeasureBtn;
        private Checkbox _useDrfCheckbox;

        private string _lastGeneratedCode = "";

        private bool _isDragging, _isActivePanelDragging, _isDraggingSummary, _isCompactDragging, _isDraggingTarget;
        private Point _dragOffset, _activePanelDragOffset, _summaryDragOffset, _compactDragOffset, _targetDragOffset;

        private Panel _currentSummaryWindow;
        private Panel _targetSelectionWindow;

        // --- Compact Casual UI ---
        private Panel _casualCompactPanel;
        private FlowPanel _compactCoolerList;
        private StandardButton _compactFishLogBtn;
        private StandardButton _compactMaxBtn;

        private string _tourneyModeUsed = "API";
        private bool _isTourneyWrapUpActive = false;
        private DateTime _tourneyWrapUpEndTime;
        private bool _didMidWrapUpPing = false;

        // --- Anti-Cheat UI ---
        private bool _isCheater = false;
        private Label _cheaterLabel;
        private double _cheaterTimer = 25.0;

        [ImportingConstructor]
        public GilledWars([Import("ModuleParameters")] ModuleParameters moduleParameters) : base(moduleParameters) { Instance = this; }

        protected override void DefineSettings(SettingCollection settings)
        {
            ToggleHotkey = settings.DefineSetting("ToggleHotkey", new KeyBinding(ModifierKeys.Ctrl | ModifierKeys.Alt, Keys.F), () => "Toggle UI", () => "Open/Close UI");
            _customApiKey = settings.DefineSetting("CustomApiKey", "", () => "Custom API Key", () => "Paste an API key with 'inventories', 'characters', and 'progression' permissions here.");
            _drfToken = settings.DefineSetting("DrfToken", "", () => "DRF Token", () => "Paste your drf.rs token here for Real-Time tracking.");
            _discordWebhookUrl = settings.DefineSetting("DiscordWebhookUrl", "", () => "Discord Webhook (Host Only)", () => "Paste a Discord channel Webhook URL to automatically post your tournament results!");
            _cornerIconChoice = settings.DefineSetting("CornerIconChoice", CornerIconType.Hook, () => "Corner Icon", () => "Choose the icon displayed in the top-left menu.");
            _autoStartLogging = settings.DefineSetting("AutoStartLogging", false, () => "Auto-Start Logging", () => "Automatically starts Casual Logging when you load into the game.");
            _autoStartBitingWidget = settings.DefineSetting("AutoStartBitingWidget", false, () => "Auto-Open Biting Widget", () => "Automatically opens the 'Currently Biting' widget on load based on your map.");
            _bitingLocX = settings.DefineSetting("BitingLocX", 10);
            _bitingLocY = settings.DefineSetting("BitingLocY", 300);
            _showProfitWidget = settings.DefineSetting("ShowProfitWidget", false, () => "Show Live Profit Widget", () => "Displays an estimated session profit and GpH tracker.");
            _profitLocX = settings.DefineSetting("ProfitLocX", 10);
            _profitLocY = settings.DefineSetting("ProfitLocY", 500);


            _showTimeOfDayWidget = settings.DefineSetting("ShowTimeOfDay", true, () => "Show Time of Day", () => "Displays a widget showing current Tyrian time.");
            _lockTimeOfDayWidget = settings.DefineSetting("LockTimeOfDay", false, () => "Lock Time of Day Widget", () => "Prevents the widget from being dragged accidentally.");

            _todWidgetSize = settings.DefineSetting("TodWidgetSize", 48, () => "Widget Icon Size", () => "Changes the size of the Time of Day icon.");
            _todWidgetSize.SetRange(16, 128);

            _todTextLayout = settings.DefineSetting("TodTextLayout", TodTextLayout.Right, () => "Text Layout", () => "Where should the time text be relative to the icon?");

            
            _todLocX = settings.DefineSetting("TodLocX", 100);
            _todLocY = settings.DefineSetting("TodLocY", 100);
            _selectedLanguage = settings.DefineSetting("SelectedLanguage", "auto", () => "UI Language", () => "Language used for the Gilled Wars interface. 'auto' follows your BlishHUD language setting.");
        }

        private Blish_HUD.Content.AsyncTexture2D GetCornerIconTexture(CornerIconType type)
        {
            switch (type)
            {
                case CornerIconType.Bait: return ContentsManager.GetTexture("images/bait.png");
                case CornerIconType.Hook: return ContentsManager.GetTexture("images/hook.png");
                case CornerIconType.Hook2: return ContentsManager.GetTexture("images/hook2.png");
                case CornerIconType.Lure: return ContentsManager.GetTexture("images/lure.png");
                case CornerIconType.Net: return ContentsManager.GetTexture("images/net.png");
                case CornerIconType.FishMaster: return ContentsManager.GetTexture("images/fishmaster.png");
                default: return ContentsManager.GetTexture("images/hook.png");
            }
        }

        private void OnMouseLeftButtonReleased(object sender, MouseEventArgs e)
        {
            // --- SAVE TIME OF DAY LOCATION ---
            if (_isTodDragging && _timeOfDayPanel != null)
            {
                _todLocX.Value = _timeOfDayPanel.Location.X;
                _todLocY.Value = _timeOfDayPanel.Location.Y;
            }

            // --- SAVE BITING WIDGET LOCATION ---
            if (_isBitingDragging && _bitingWidgetPanel != null)
            {
                _bitingLocX.Value = _bitingWidgetPanel.Location.X;
                _bitingLocY.Value = _bitingWidgetPanel.Location.Y;
            }

            // --- SAVE PROFIT WIDGET LOCATION ---
            if (_isProfitDragging && _profitWidgetPanel != null)
            {
                _profitLocX.Value = _profitWidgetPanel.Location.X;
                _profitLocY.Value = _profitWidgetPanel.Location.Y;
            }

            // Safely turn off all dragging flags AFTER saving!
            _isProfitDragging = false;
            _isBitingDragging = false;
            _isDragging = false;
            _isActivePanelDragging = false;
            _isDraggingSummary = false;
            _isCompactDragging = false;
            _isDraggingTarget = false;
            _isDraggingLeaderboard = false;
            _isDraggingAchievement = false;
            _isSpeciesSelectionDragging = false;
            _isMetaDragging = false;
            _isTodDragging = false;
        }

        private string DetectLanguage()
        {
            // If the user has manually picked a language via the flag buttons, honour that.
            // "auto" (or empty) means "follow BlishHUD's language setting".
            if (_selectedLanguage.Value != "auto" && !string.IsNullOrEmpty(_selectedLanguage.Value))
                return _selectedLanguage.Value;

            // Read BlishHUD's own language setting (set in BlishHUD Settings → General → Language)
            // This is GameService.Overlay.UserLocale - the BlishHUD UI language, NOT the GW2 game language
            var blishLocale = GameService.Overlay.UserLocale.Value;
            switch (blishLocale)
            {
                case Gw2Sharp.WebApi.Locale.French:  return "fr";
                case Gw2Sharp.WebApi.Locale.German:  return "de";
                case Gw2Sharp.WebApi.Locale.Spanish: return "es";
                default: return "en";
            }
        }

        private void ApplyLanguage(string langCode)
        {
            _selectedLanguage.Value = langCode;
            var culture = new System.Globalization.CultureInfo(langCode);
            System.Threading.Thread.CurrentThread.CurrentUICulture = culture;
            System.Threading.Thread.CurrentThread.CurrentCulture = culture;
            strings.Culture = culture;
            _junkMessages = strings.JunkMessages.Split('|');
            _treasureMessages = strings.TreasureMessages.Split('|');

            // Rebuild main UI
            if (_mainWindow != null) { _mainWindow.Dispose(); _mainWindow = null; }
            if (_casualCompactPanel != null) { _casualCompactPanel.Dispose(); _casualCompactPanel = null; }
            BuildMainWindow();
            BuildCasualCompactPanel();

            // Rebuild floating widgets
            if (_profitWidgetPanel != null) { _profitWidgetPanel.Dispose(); _profitWidgetPanel = null; }
            BuildProfitWidget();

            if (_timeOfDayPanel != null) { _timeOfDayPanel.Dispose(); _timeOfDayPanel = null; }
            BuildTimeOfDayWidget();

            // Biting widget rebuilds itself on next refresh
            if (_bitingWidgetPanel != null)
            {
                bool wasVisible = _bitingWidgetPanel.Visible;
                _bitingWidgetPanel.Dispose();
                _bitingWidgetPanel = null;
                if (wasVisible) ToggleBitingWidget(true);
            }
        }

        protected override void OnModuleLoaded(EventArgs e)
        {
            string langCode = DetectLanguage();

            var culture = new System.Globalization.CultureInfo(langCode);
            System.Threading.Thread.CurrentThread.CurrentUICulture = culture;
            System.Threading.Thread.CurrentThread.CurrentCulture = culture;

            // Forces the resource manager to return strings in the correct language
            strings.Culture = culture;

            // Initialize message arrays after culture is set so they load in the correct language
            _junkMessages = strings.JunkMessages.Split('|');
            _treasureMessages = strings.TreasureMessages.Split('|');

            Logger.Info($"[GilledWars] Culture: {culture.Name} | Test: {strings.StartLoggingBtnText}");

            // This ensures fish names in tooltips translate via API
            // Gw2ApiManager.Gw2ApiClient.Connection.Locale = currentLocale;  // Temporarily disabled for debugging
            Logger.Info("Gw2ApiClient Full Type: " + Gw2ApiManager.Gw2ApiClient.GetType().FullName);

            LoadFishDatabase();

            // Load account and PBs FIRST while fish names are still English (needed for v1→v2 migration)
            _ = InitializeAccountAndLoadAsync();

            // Then localize fish names in the background
            _ = LocalizeFishNamesAsync();

            BuildMainWindow();
            BuildCasualCompactPanel();
            GameService.Input.Mouse.LeftMouseButtonReleased += OnMouseLeftButtonReleased;

            _cornerIcon = new CornerIcon()
            {
                Icon = GetCornerIconTexture(_cornerIconChoice.Value),
                BasicTooltipText = strings.CornerIconTooltip,
                Priority = 5
            };


            _cornerIconChoice.SettingChanged += (s, ev) => {
                _cornerIcon.Icon = GetCornerIconTexture(ev.NewValue);
            };

            _cornerIcon.Click += (s, ev) => {
                if (_casualCompactPanel != null && _casualCompactPanel.Visible)
                {
                    _casualCompactPanel.Visible = false;
                    if (_mainWindow != null) _mainWindow.Visible = true;
                    ScreenNotification.ShowNotification(strings.ExpandingMainView);
                    return;
                }

                if (_mainWindow != null)
                {
                    _mainWindow.Visible = !_mainWindow.Visible;
                }
            };

            ToggleHotkey.Value.Enabled = true;
            ToggleHotkey.Value.Activated += OnToggleHotkeyActivated;

            _cheaterLabel = new Label
            {
                Parent = GameService.Graphics.SpriteScreen,
                TextColor = Color.Red,
                Font = GameService.Content.DefaultFont32,
                AutoSizeWidth = true,
                AutoSizeHeight = true,
                Visible = false,
                ZIndex = 9999,
                StrokeText = true,
                ShowShadow = true
            };


            _texDawn = ContentsManager.GetTexture("images/tod_dawn.png");
            _texDay = ContentsManager.GetTexture("images/tod_day.png");
            _texDusk = ContentsManager.GetTexture("images/tod_dusk.png");
            _texNight = ContentsManager.GetTexture("images/tod_night.png");
            _texCaughtCheck = ContentsManager.GetTexture("images/caught_check.png"); // --- NEW: Added caught checkmark ---


            BuildTimeOfDayWidget();

            RefreshFishLogUI();
            if (_autoStartLogging.Value)
            {
                _ = StartCasualLogging();
            }

            if (_autoStartBitingWidget.Value) ToggleBitingWidget(true);
            _ = FetchTradingPostPrices();
            BuildProfitWidget();

            base.OnModuleLoaded(e);
        }

        private async Task FetchTradingPostPrices()
        {
            try
            {
                // 96347 = Ambergris, 95673 = Flawless Fillet, 96762 = Fine Fillet
                var prices = await Gw2ApiManager.Gw2ApiClient.V2.Commerce.Prices.ManyAsync(new[] { 96347, 95673, 96762 });
                var ambergris = prices.FirstOrDefault(p => p.Id == 96347);
                var fillet = prices.FirstOrDefault(p => p.Id == 95673);
                var fineFillet = prices.FirstOrDefault(p => p.Id == 96762);

                if (ambergris != null) _ambergrisPriceCopper = ambergris.Sells.UnitPrice;
                if (fillet != null) _filletPriceCopper = fillet.Sells.UnitPrice;
                if (fineFillet != null) _fineFilletPriceCopper = fineFillet.Sells.UnitPrice;
            }
            catch (Exception ex)
            {
                Logger.Warn(ex, "Failed to fetch TP prices for Profit Tracker.");
            }
            finally
            {
                // HARD FALLBACKS: Guarantees the math works even if the API drops the connection!
                if (_ambergrisPriceCopper <= 0) _ambergrisPriceCopper = 15000;
                if (_filletPriceCopper <= 0) _filletPriceCopper = 200;
                if (_fineFilletPriceCopper <= 0) _fineFilletPriceCopper = 25;
            }
        }

        private string FormatMoney(int totalCopper)
        {
            int g = totalCopper / 10000;
            int s = (totalCopper % 10000) / 100;
            int c = totalCopper % 100;
            return $"{g}g {s}s {c}c";
        }

        private void BuildProfitWidget()
        {
            _profitWidgetPanel = new Panel
            {
                Parent = GameService.Graphics.SpriteScreen,
                Size = new Point(220, 65),
                Location = new Point(_profitLocX.Value, _profitLocY.Value),
                BackgroundColor = new Color(13, 27, 42) * 0.9f,
                ShowBorder = true,
                Visible = _showProfitWidget.Value,
                ZIndex = 950
            };

            var hBar = new Panel { Parent = _profitWidgetPanel, Size = new Point(220, 20), BackgroundColor = Color.Black * 0.6f, Location = new Point(0, 0) };
            new Label { Text = strings.ProfitTrackerBtnText, Parent = hBar, Location = new Point(5, 0), Font = GameService.Content.DefaultFont14, TextColor = new Color(201, 168, 76), AutoSizeWidth = true };

            // --- NEW: Added the sleek orange [WIP] tag to the far right of the header! ---
            new Label { Text = "[WIP]", Parent = hBar, Location = new Point(180, 2), Font = GameService.Content.DefaultFont12, TextColor = Color.Orange, AutoSizeWidth = true };

            hBar.LeftMouseButtonPressed += (s, e) => {
                _isProfitDragging = true;
                _profitDragOffset = new Point(GameService.Input.Mouse.Position.X - _profitWidgetPanel.Location.X, GameService.Input.Mouse.Position.Y - _profitWidgetPanel.Location.Y);
            };

            _profitTotalLabel = new Label { Text = strings.ProfitTotalDefault, Parent = _profitWidgetPanel, Location = new Point(10, 25), Font = GameService.Content.DefaultFont14, TextColor = Color.LimeGreen, AutoSizeWidth = true };
            _gphLabel = new Label { Text = strings.GphDefault, Parent = _profitWidgetPanel, Location = new Point(10, 45), Font = GameService.Content.DefaultFont12, TextColor = Color.LightGray, AutoSizeWidth = true };

            _showProfitWidget.SettingChanged += (s, e) => _profitWidgetPanel.Visible = e.NewValue;
        }

        private void UpdateTodLayout()
        {
            if (_timeOfDayPanel == null || _todIcon == null || _todLabel == null) return;

            int iconSize = _todWidgetSize.Value;
            _todIcon.Size = new Point(iconSize, iconSize);

            
            int textW = 90;
            int textH = 20;

            
            int pw = Math.Max(textW, iconSize);

            switch (_todTextLayout.Value)
            {
                case TodTextLayout.Right:
                    _todIcon.Location = new Point(0, 0);
                    _todLabel.Size = new Point(textW, textH);
                    _todLabel.Location = new Point(iconSize + 5, (iconSize - textH) / 2);
                    _todLabel.HorizontalAlignment = HorizontalAlignment.Left;
                    _timeOfDayPanel.Size = new Point(iconSize + 5 + textW, Math.Max(iconSize, textH));
                    _todLabel.Visible = true;
                    break;
                case TodTextLayout.Left:
                    _todLabel.Size = new Point(textW, textH);
                    _todLabel.Location = new Point(0, (iconSize - textH) / 2);
                    _todLabel.HorizontalAlignment = HorizontalAlignment.Right;
                    _todIcon.Location = new Point(textW + 5, 0);
                    _timeOfDayPanel.Size = new Point(textW + 5 + iconSize, Math.Max(iconSize, textH));
                    _todLabel.Visible = true;
                    break;
                case TodTextLayout.Bottom:
                    _todIcon.Location = new Point((pw - iconSize) / 2, 0);
                    _todLabel.Size = new Point(textW, textH);
                    _todLabel.Location = new Point((pw - textW) / 2, iconSize + 2);
                    _todLabel.HorizontalAlignment = HorizontalAlignment.Center;
                    _timeOfDayPanel.Size = new Point(pw, iconSize + 2 + textH);
                    _todLabel.Visible = true;
                    break;
                case TodTextLayout.Top:
                    _todLabel.Size = new Point(textW, textH);
                    _todLabel.Location = new Point((pw - textW) / 2, 0);
                    _todLabel.HorizontalAlignment = HorizontalAlignment.Center;
                    _todIcon.Location = new Point((pw - iconSize) / 2, textH + 2);
                    _timeOfDayPanel.Size = new Point(pw, textH + 2 + iconSize);
                    _todLabel.Visible = true;
                    break;
                case TodTextLayout.OnImage:
                    _todIcon.Location = new Point(0, 0);
                    _todLabel.Size = new Point(iconSize, textH);
                    _todLabel.Location = new Point(0, (iconSize - textH) / 2);
                    _todLabel.HorizontalAlignment = HorizontalAlignment.Center;
                    _timeOfDayPanel.Size = new Point(iconSize, iconSize);
                    _todLabel.Visible = true;
                    break;
                case TodTextLayout.Hidden:
                    _todIcon.Location = new Point(0, 0);
                    _timeOfDayPanel.Size = new Point(iconSize, iconSize);
                    _todLabel.Visible = false;
                    break;
            }
        }

        private void BuildTimeOfDayWidget()
        {
            _timeOfDayPanel = new Panel
            {
                Parent = GameService.Graphics.SpriteScreen,
                Location = new Point(_todLocX.Value, _todLocY.Value),
                BackgroundColor = Color.Transparent,
                ShowBorder = false,
                Visible = _showTimeOfDayWidget.Value,
                ZIndex = 900
            };

            _todIcon = new Image { Parent = _timeOfDayPanel, Texture = _texDay };
            _todLabel = new Label
            {
                Parent = _timeOfDayPanel,
                Font = GameService.Content.DefaultFont16,
                TextColor = Color.White,
                ShowShadow = true,
                StrokeText = true,
                Text = strings.Loading
            };

            UpdateTodLayout();

            _todWidgetSize.SettingChanged += (s, e) => UpdateTodLayout();
            _todTextLayout.SettingChanged += (s, e) => UpdateTodLayout();
            _showTimeOfDayWidget.SettingChanged += (s, e) => _timeOfDayPanel.Visible = e.NewValue;

            _timeOfDayPanel.LeftMouseButtonPressed += (s, ev) => {
                if (!_lockTimeOfDayWidget.Value &&
                   (GameService.Input.Mouse.ActiveControl == _timeOfDayPanel ||
                    GameService.Input.Mouse.ActiveControl == _todLabel ||
                    GameService.Input.Mouse.ActiveControl == _todIcon))
                {
                    _isTodDragging = true;
                    _todDragOffset = new Point(GameService.Input.Mouse.Position.X - _timeOfDayPanel.Location.X, GameService.Input.Mouse.Position.Y - _timeOfDayPanel.Location.Y);
                }
            };
        }

        private void ShowLeaderboardWindow()
        {
            Color deepNavyBg = new Color(13, 27, 42);
            Color darkTealPanel = new Color(26, 47, 69);
            Color agedGoldText = new Color(201, 168, 76);

            if (_leaderboardWindow != null)
            {
                _leaderboardWindow.Visible = true;
                return;
            }

            _leaderboardWindow = new Panel
            {
                Parent = GameService.Graphics.SpriteScreen,
                Size = new Point(480, 620),
                Location = new Point(400, 150),
                ShowBorder = true,
                BackgroundColor = deepNavyBg,
                ZIndex = 1000,
                ClipsBounds = false
            };

            var headerBar = new Panel { Parent = _leaderboardWindow, Size = new Point(_leaderboardWindow.Width, 30), BackgroundColor = Color.Black * 0.6f, Location = new Point(0, 0) };

            _lbDynamicTitleLabel = new Label { Text = strings.LeaderboardTitle, Parent = headerBar, Location = new Point(10, 5), Font = GameService.Content.DefaultFont16, TextColor = agedGoldText, AutoSizeWidth = true };

            var closeX = new Label { Text = "X", Parent = headerBar, Location = new Point(headerBar.Width - 25, 5), Font = GameService.Content.DefaultFont16, TextColor = Color.Red, AutoSizeWidth = true };
            closeX.Click += (s, e) => { _leaderboardWindow.Visible = false; if (_speciesSelectionWindow != null) _speciesSelectionWindow.Visible = false; };
            closeX.MouseEntered += (s, e) => { closeX.TextColor = Color.White; };
            closeX.MouseLeft += (s, e) => { closeX.TextColor = Color.Red; };

            headerBar.LeftMouseButtonPressed += (s, ev) => {
                _isDraggingLeaderboard = true;
                _leaderboardDragOffset = new Point(GameService.Input.Mouse.Position.X - _leaderboardWindow.Location.X, GameService.Input.Mouse.Position.Y - _leaderboardWindow.Location.Y);
            };

            new Label { Text = strings.SortLabel, Parent = _leaderboardWindow, Location = new Point(10, 45), AutoSizeWidth = true };
            _lbSortDropdown = new Dropdown() { Parent = _leaderboardWindow, Location = new Point(50, 40), Width = 90 };
            _lbSortDropdown.Items.Add(strings.WeightDropdown);
            _lbSortDropdown.Items.Add(strings.LengthDropdown);
            _lbSortDropdown.SelectedItem = strings.WeightDropdown;
            _lbSortDropdown.ValueChanged += async (s, e) => { await RefreshLeaderboardData(); };

            new Label { Text = strings.FishLabel, Parent = _leaderboardWindow, Location = new Point(150, 45), AutoSizeWidth = true };
            _speciesFilterBtn = new StandardButton { Text = strings.AllSpeciesFilterBtn, Parent = _leaderboardWindow, Location = new Point(190, 40), Width = 140 };
            _speciesFilterBtn.Click += (s, e) => ShowSpeciesPicker();

            var refreshBtn = new StandardButton { Text = strings.RefreshBtn, Parent = _leaderboardWindow, Location = new Point(350, 40), Width = 90 };
            refreshBtn.Click += async (s, e) => {
                double elapsedMinutes = (DateTime.Now - _lastLeaderboardFetchTime).TotalMinutes;
                if (elapsedMinutes < 5 && _cachedLeaderboardData != null)
                {
                    int remaining = 5 - (int)elapsedMinutes;
                    ScreenNotification.ShowNotification(string.Format(strings.RefreshCooldown, remaining), ScreenNotification.NotificationType.Warning);
                    return;
                }
                refreshBtn.Enabled = false;
                _cachedLeaderboardData = null;
                _lastLeaderboardFetchTime = DateTime.MinValue;
                await RefreshLeaderboardData();
                refreshBtn.Enabled = true;
                ScreenNotification.ShowNotification(strings.LeaderboardRefreshed);
            };

            _lbListPanel = new FlowPanel()
            {
                Parent = _leaderboardWindow,
                Location = new Point(10, 85),
                Size = new Point(460, 520),
                CanScroll = true,
                FlowDirection = ControlFlowDirection.SingleTopToBottom
            };

            _leaderboardWindow.Visible = true;
            _ = RefreshLeaderboardData();
        }

        private async Task RefreshLeaderboardData()
        {
            if (_lbListPanel == null || _leaderboardWindow == null) return;

            _lbListPanel.ClearChildren();
            new Label { Text = strings.LoadingData, Parent = _lbListPanel, AutoSizeWidth = true, TextColor = Microsoft.Xna.Framework.Color.Yellow };

            try
            {
                // If the user picked the translated "Length" word, use "length", otherwise default to "weight" for the API.
                string sortMode = _lbSortDropdown.SelectedItem == strings.LengthDropdown ? "length" : "weight";
                string selectedSpecies = _currentlySelectedSpecies;

                if (_cachedLeaderboardData == null || (DateTime.Now - _lastLeaderboardFetchTime).TotalMinutes >= 10)
                {
                    var response = await _httpClient.GetAsync($"{API_BASE_URL}/get-leaderboard");

                    if (response.IsSuccessStatusCode)
                    {
                        string json = await response.Content.ReadAsStringAsync();
                        _cachedLeaderboardData = JsonConvert.DeserializeObject<List<LeaderboardEntry>>(json);
                        _lastLeaderboardFetchTime = DateTime.Now;
                    }
                    else
                    {
                        _lbListPanel.ClearChildren();
                        new Label { Text = strings.ServerError, Parent = _lbListPanel, TextColor = Microsoft.Xna.Framework.Color.Red, AutoSizeWidth = true };
                        return;
                    }
                }

                _lbListPanel.ClearChildren();
                if (_lbDynamicTitleLabel != null)
                {
                    _lbDynamicTitleLabel.Text = selectedSpecies == AllSpeciesKey ? $"Global Top 10 ({sortMode.ToUpper()})" : $"Top 10 {selectedSpecies}";
                }

                if (_cachedLeaderboardData == null || _cachedLeaderboardData.Count == 0)
                {
                    new Label { Text = strings.NoRecordsFound, Parent = _lbListPanel, AutoSizeWidth = true };
                    return;
                }

                var filteredRecords = _cachedLeaderboardData.Where(r => r.RecordType == sortMode);

                if (selectedSpecies != AllSpeciesKey)
                {
                    filteredRecords = filteredRecords.Where(r => r.FishName.Equals(selectedSpecies, StringComparison.OrdinalIgnoreCase));
                }

                var top10List = filteredRecords
                    .OrderByDescending(r => sortMode == "weight" ? r.Weight : r.Length)
                    .Take(10)
                    .ToList();

                if (top10List.Count == 0)
                {
                    new Label { Text = strings.NoCatchesYet, Parent = _lbListPanel, AutoSizeWidth = true, TextColor = Microsoft.Xna.Framework.Color.LightGray };
                    return;
                }

                var headerRow = new Panel { Parent = _lbListPanel, Width = _lbListPanel.Width - 20, Height = 30 };
                new Label { Text = strings.RankLabel, Parent = headerRow, Location = new Point(5, 5), Width = 45, TextColor = Microsoft.Xna.Framework.Color.Cyan, Font = GameService.Content.DefaultFont16 };
                new Label { Text = strings.AnglerLabel, Parent = headerRow, Location = new Point(65, 5), Width = 160, TextColor = Microsoft.Xna.Framework.Color.Cyan, Font = GameService.Content.DefaultFont16 };
                new Label { Text = strings.SpeciesLabel, Parent = headerRow, Location = new Point(235, 5), Width = 110, TextColor = Microsoft.Xna.Framework.Color.Cyan, Font = GameService.Content.DefaultFont16 };
                new Label { Text = sortMode == "weight" ? "Weight" : "Length", Parent = headerRow, Location = new Point(355, 5), Width = 65, TextColor = Microsoft.Xna.Framework.Color.Cyan, Font = GameService.Content.DefaultFont16 };

                new Image { Texture = ContentService.Textures.Pixel, Parent = _lbListPanel, Width = _lbListPanel.Width - 25, Height = 2, Tint = Microsoft.Xna.Framework.Color.Gray * 0.5f };

                int rank = 1;
                var customSilver = new Microsoft.Xna.Framework.Color(190, 210, 230);
                var customBronze = new Microsoft.Xna.Framework.Color(205, 127, 50);

                foreach (var entry in top10List)
                {
                    var row = new Panel { Parent = _lbListPanel, Width = _lbListPanel.Width - 25, Height = 40 };
                    Microsoft.Xna.Framework.Color rankColor = rank == 1 ? Microsoft.Xna.Framework.Color.Gold : (rank == 2 ? customSilver : (rank == 3 ? customBronze : Microsoft.Xna.Framework.Color.White));

                    new Label { Text = $"#{rank}", Parent = row, Location = new Point(5, 10), Width = 45, TextColor = rankColor, Font = GameService.Content.DefaultFont18 };
                    new Image { Texture = ContentService.Textures.Pixel, Parent = row, Location = new Point(55, 5), Width = 1, Height = 30, Tint = Microsoft.Xna.Framework.Color.White * 0.1f };

                    string countryCode = (entry.Country ?? "xx").ToLower();
                    int nameXPos = 65;
                    if (countryCode != "xx")
                    {
                        try
                        {
                            var flagTex = ContentsManager.GetTexture($"flags/{countryCode}.png");
                            new Image { Texture = flagTex, Parent = row, Location = new Point(65, 12), Size = new Point(24, 16) };
                            nameXPos = 95;
                        }
                        catch { }
                    }
                    new Label { Text = entry.PlayerName, Parent = row, Location = new Point(nameXPos, 10), Width = 225 - nameXPos, TextColor = rankColor, Font = GameService.Content.DefaultFont14, AutoSizeWidth = false };

                    new Image { Texture = ContentService.Textures.Pixel, Parent = row, Location = new Point(230, 5), Width = 1, Height = 30, Tint = Microsoft.Xna.Framework.Color.White * 0.1f };

                    int speciesXPos = 235;
                    var dbFish = _allFishEntries.FirstOrDefault(x => x.Data.Name.Equals(entry.FishName, StringComparison.OrdinalIgnoreCase))?.Data;
                    if (dbFish != null)
                    {
                        string safeName = dbFish.Name.Replace(" ", "_").Replace("'", "").Replace("-", "");
                        new Image { Texture = ContentsManager.GetTexture($"images/{safeName}.png"), Parent = row, Location = new Point(235, 4), Size = new Point(32, 32), BasicTooltipText = dbFish.Name };
                        speciesXPos = 270;
                    }
                    new Label { Text = entry.FishName, Parent = row, Location = new Point(speciesXPos, 10), Width = 350 - speciesXPos, TextColor = Microsoft.Xna.Framework.Color.LightGray, WrapText = false, Font = GameService.Content.DefaultFont12 };

                    new Image { Texture = ContentService.Textures.Pixel, Parent = row, Location = new Point(350, 5), Width = 1, Height = 30, Tint = Microsoft.Xna.Framework.Color.White * 0.1f };

                    string statText = sortMode == "weight" ? $"{entry.Weight} lbs" : $"{entry.Length} in";
                    new Label { Text = statText, Parent = row, Location = new Point(355, 10), Width = 75, TextColor = Microsoft.Xna.Framework.Color.White, Font = GameService.Content.DefaultFont14 };

                    new Image { Texture = ContentService.Textures.Pixel, Parent = _lbListPanel, Width = _lbListPanel.Width - 25, Height = 1, Tint = Microsoft.Xna.Framework.Color.White * 0.15f };
                    rank++;
                }
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Failed to load in-game leaderboard.");
                _lbListPanel.ClearChildren();
                new Label { Text = strings.NetworkError, Parent = _lbListPanel, TextColor = Microsoft.Xna.Framework.Color.Red, AutoSizeWidth = true };
            }
        }

        private async Task ShowMetaProgressWindow()
        {
            Color deepNavyBg = new Color(13, 27, 42);
            Color darkTealPanel = new Color(26, 47, 69);
            Color agedGoldText = new Color(201, 168, 76);

            if (_metaProgressWindow == null)
            {
                _metaProgressWindow = new Panel { ShowBorder = true, Size = new Point(540, 550), Location = new Point(350, 150), Parent = GameService.Graphics.SpriteScreen, BackgroundColor = deepNavyBg, ZIndex = 1100, ClipsBounds = false };
            }

            _metaProgressWindow.ClearChildren();
            _allMetaSubPanels.Clear();
            _currentlyExpandedRow = null;
            _metaProgressWindow.Visible = true;

            var scroll = new FlowPanel { Parent = _metaProgressWindow, Size = new Point(_metaProgressWindow.Width - 10, _metaProgressWindow.Height - 40), Location = new Point(5, 35), FlowDirection = ControlFlowDirection.SingleTopToBottom, CanScroll = true, ControlPadding = new Vector2(0, 5) };
            new Label { Text = strings.FetchingAchievements, Parent = scroll, Font = GameService.Content.DefaultFont14, TextColor = Color.Yellow, AutoSizeWidth = true };

            try
            {
                var metaIds = new List<int> { 6478, 6109, 6284, 6201, 6279, 6111 };
                int[] metaMaxes = { 5, 10, 15, 20, 25, 30 };
                int[] base30 = { 6068, 6179, 6330, 6344, 6363, 6317, 6106, 6489, 6336, 6342, 6258, 6506, 6471, 6224, 6439, 6505, 6263, 6153, 6484, 6475, 6227, 6509, 6250, 6339, 6264, 6192, 6466, 6402, 6393, 6110 };

                var allTargetIds = metaIds.Concat(base30).Distinct().ToList();
                var allDefs = await Gw2ApiManager.Gw2ApiClient.V2.Achievements.ManyAsync(allTargetIds);
                var accAchievements = await Gw2ApiManager.Gw2ApiClient.V2.Account.Achievements.GetAsync();

                int realCompletedCollections = 0;
                foreach (int bId in base30) { if (accAchievements.FirstOrDefault(a => a.Id == bId)?.Done == true) realCompletedCollections++; }

                scroll.ClearChildren();

                
                var hBar = new Panel { Parent = _metaProgressWindow, Size = new Point(_metaProgressWindow.Width, 30), BackgroundColor = Color.Black * 0.6f, Location = new Point(0, 0) };
                new Label { Text = strings.MetaTrackerTitle, Parent = hBar, Location = new Point(10, 5), Font = GameService.Content.DefaultFont16, TextColor = agedGoldText, AutoSizeWidth = true };
                var closeX = new Label { Text = "X", Parent = hBar, Location = new Point(hBar.Width - 25, 5), Font = GameService.Content.DefaultFont16, TextColor = Color.Red, AutoSizeWidth = true };
                closeX.Click += (s, e) => _metaProgressWindow.Visible = false;
                closeX.MouseEntered += (s, e) => { closeX.TextColor = Color.White; };
                closeX.MouseLeft += (s, e) => { closeX.TextColor = Color.Red; };
                hBar.LeftMouseButtonPressed += (s, ev) => { _isMetaDragging = true; _metaDragOffset = new Point(GameService.Input.Mouse.Position.X - _metaProgressWindow.Location.X, GameService.Input.Mouse.Position.Y - _metaProgressWindow.Location.Y); };

                for (int i = 0; i < metaIds.Count; i++)
                {
                    int mId = metaIds[i];
                    var def = allDefs.FirstOrDefault(x => x.Id == mId);
                    if (def == null) continue;

                    var progress = accAchievements.FirstOrDefault(a => a.Id == mId);
                    int max = metaMaxes[i];
                    int current = (mId == 6279) ? (progress?.Current ?? 0) : realCompletedCollections;
                    bool isDone = progress?.Done ?? false;
                    if (isDone || current > max) current = max;

                    
                    var row = new Panel { Parent = scroll, Width = 500, Height = 55, BackgroundColor = darkTealPanel, ShowBorder = true };
                    new Label { Text = def.Name, Parent = row, Location = new Point(10, 5), Font = GameService.Content.DefaultFont16, TextColor = isDone ? Color.LimeGreen : Color.White, AutoSizeWidth = true };
                    new Label { Text = $"{current} / {max}", Parent = row, Location = new Point(430, 5), Font = GameService.Content.DefaultFont14, TextColor = Color.Cyan, AutoSizeWidth = true };
                    var barBg = new Panel { Parent = row, Location = new Point(10, 30), Size = new Point(480, 15), BackgroundColor = Color.Black * 0.5f };
                    new Panel { Parent = barBg, Size = new Point((int)(480 * ((float)current / (max > 0 ? max : 1))), 15), BackgroundColor = isDone ? Color.LimeGreen : agedGoldText };

                    var subContainer = new FlowPanel { Parent = scroll, Width = 500, HeightSizingMode = SizingMode.AutoSize, FlowDirection = ControlFlowDirection.LeftToRight, ControlPadding = new Vector2(5, 5), Visible = false };
                    _allMetaSubPanels.Add(subContainer);

                    row.Click += (s, e) => {
                        bool wasActive = (_currentlyExpandedRow == row);
                        foreach (var p in _allMetaSubPanels) p.Visible = false;
                        if (wasActive) { _currentlyExpandedRow = null; }
                        else
                        {
                            subContainer.ClearChildren();
                            BuildSubAchievements(def, subContainer, current, isDone, accAchievements);
                            subContainer.Visible = true; _currentlyExpandedRow = row;
                        }
                        scroll.RecalculateLayout();
                    };
                }
            }
            catch (Exception ex) { Logger.Error(ex, "Meta Tracker failed."); }
        }

        private void BuildSubAchievements(Gw2Sharp.WebApi.V2.Models.Achievement metaDef, FlowPanel container, int currentProgress, bool isFullyDone, IReadOnlyList<Gw2Sharp.WebApi.V2.Models.AccountAchievement> accProgress)
        {
            int[] subIds = {
                6068, 6179, 6330, 6344, 6363, 6317, 6106, 6489, 6336, 6342, 6258, 6506, 6471, 6224, 6439, 6505,
                6263, 6153, 6484, 6475, 6227, 6509, 6250, 6339, 6264, 6192, 6466, 6402, 6393, 6110,
                7114, 7804,
                8168, 8246, 8554
            };

            var gridPanel = new FlowPanel
            {
                Parent = container,
                FlowDirection = ControlFlowDirection.LeftToRight,
                OuterControlPadding = new Vector2(5, 5),
                ControlPadding = new Vector2(5, 5),
                Width = container.Width - 20,
                CanScroll = true,
                HeightSizingMode = SizingMode.AutoSize
            };

            foreach (int subId in subIds)
            {
                var btn = new StandardButton
                {
                    Parent = gridPanel,
                    Text = strings.Loading,
                    Width = 150,
                    Height = 35
                };

                var subTask = Gw2ApiManager.Gw2ApiClient.V2.Achievements.GetAsync(subId);

                subTask.ContinueWith(t => {
                    if (t.Status == System.Threading.Tasks.TaskStatus.RanToCompletion)
                    {
                        var subAch = t.Result;
                        var subProg = accProgress.FirstOrDefault(a => a.Id == subId);
                        bool subDone = subProg?.Done ?? false;

                        string cleanName = subAch.Name.Replace(" Fisher", "");
                        btn.Text = (subDone ? "✓ " : "") + cleanName;
                        btn.BasicTooltipText = subAch.Description ?? "";
                    }
                }, TaskScheduler.FromCurrentSynchronizationContext());

                btn.Click += async (s, e) => {
                    btn.Enabled = false;
                    var subAch = await subTask;
                    int subCurrent = accProgress.FirstOrDefault(a => a.Id == subId)?.Current ?? 0;
                    int subMax = subAch.Bits?.Count ?? 0;
                    string cleanName = subAch.Name.Replace(" Fisher", "");

                    await ShowAchievementResultsPanel(cleanName, subId, subCurrent, subMax, subAch.Description ?? "");
                    btn.Enabled = true;
                };
            }
        }

        private void ShowSpeciesPicker()
        {
            if (_speciesSelectionWindow != null) { _speciesSelectionWindow.Visible = !_speciesSelectionWindow.Visible; return; }

            _speciesSelectionWindow = new Panel { Title = "Filter by Species", Parent = GameService.Graphics.SpriteScreen, Size = new Point(280, 500), Location = new Point(_leaderboardWindow.Right + 5, _leaderboardWindow.Top), ShowBorder = true, BackgroundColor = new Color(0, 0, 0, 220), ZIndex = 1100 };
            _speciesSearchBox = new TextBox { Parent = _speciesSelectionWindow, Location = new Point(10, 10), Width = 240, PlaceholderText = strings.SearchSpeciesPlaceholder };

            var scroll = new FlowPanel { Parent = _speciesSelectionWindow, Size = new Point(260, 420), Location = new Point(10, 50), CanScroll = true, FlowDirection = ControlFlowDirection.SingleTopToBottom };

            Action<string> populateList = (filter) => {
                scroll.ClearChildren();
                var allBtn = new StandardButton { Text = strings.AllSpeciesFilterBtn, Parent = scroll, Width = 230 };
                allBtn.Click += async (s, e) => { _currentlySelectedSpecies = AllSpeciesKey; _speciesFilterBtn.Text = strings.AllSpeciesFilterBtn; _speciesSelectionWindow.Visible = false; await RefreshLeaderboardData(); };

                var filteredNames = _allFishEntries.Select(x => x.Data.Name).Distinct().Where(n => string.IsNullOrEmpty(filter) || n.ToLower().Contains(filter.ToLower())).OrderBy(n => n);
                foreach (var name in filteredNames)
                {
                    var fBtn = new StandardButton { Text = name, Parent = scroll, Width = 230 };
                    fBtn.Click += async (s, e) => { _currentlySelectedSpecies = name; _speciesFilterBtn.Text = name.Length > 15 ? name.Substring(0, 12) + "..." : name; _speciesSelectionWindow.Visible = false; await RefreshLeaderboardData(); };
                }
            };

            populateList("");
            _speciesSearchBox.TextChanged += (s, e) => populateList(_speciesSearchBox.Text);
        }

        private void CopyToClipboard(string text)
        {
            if (string.IsNullOrEmpty(text)) return;
            try
            {
                var thread = new System.Threading.Thread(() =>
                {
                    try { System.Windows.Forms.Clipboard.SetText(text); }
                    catch (Exception ex) { Logger.Error(ex, "WinForms Clipboard failed."); }
                });
                thread.SetApartmentState(System.Threading.ApartmentState.STA);
                thread.Start();
                thread.Join();
            }
            catch (Exception ex) { Logger.Error(ex, "Threaded clipboard copy failed."); }
        }

        private string GetGlobalSeed()
        {
            byte[] buffer = new byte[32];
            byte[] seedData = { 0xA7, 0xE2, 0xD9, 0xC8, 0xF1, 0xB4, 0x8D, 0x7E, 0x2F, 0x6A, 0x53, 0x4C, 0x35, 0x1E, 0x07, 0xF8,
                                0xE1, 0xCA, 0xB3, 0x9C, 0x85, 0x6E, 0x57, 0x40, 0x29, 0x12, 0xFB, 0xE4, 0xCD, 0xB6, 0x9F, 0x88 };
            byte[] seedKey = { 0x55, 0xAA, 0x33, 0xCC, 0x11, 0xEE, 0x77, 0x99 };
            for (int i = 0; i < seedData.Length; i++)
            {
                buffer[i] = (byte)(seedData[i] ^ seedKey[i % seedKey.Length]);
                buffer[i] = (byte)((buffer[i] << 3) | (buffer[i] >> 5));
                buffer[i] ^= (byte)(i * 0x37);
            }
            for (int i = 0; i < buffer.Length; i += 2)
            {
                if (i + 1 < buffer.Length)
                {
                    byte temp = buffer[i];
                    buffer[i] = buffer[i + 1];
                    buffer[i + 1] = temp;
                }
            }
            return BitConverter.ToString(buffer, 0, 13).Replace("-", "");
        }

        private static string DescrambleString(string input, int index, string seed)
        {
            if (string.IsNullOrEmpty(input)) return input;
            byte[] bytes = Convert.FromBase64String(input);
            byte[] key = BitConverter.GetBytes(seed.GetHashCode() ^ index);
            for (int i = 0; i < bytes.Length; i++) bytes[i] ^= key[i % key.Length];
            return Encoding.UTF8.GetString(bytes);
        }

        private static int DescrambleInt(int value, int index, string seed)
        {
            return value ^ (seed.GetHashCode() ^ index);
        }

        private static double DescrambleDouble(double value, int index, string seed)
        {
            long bits = BitConverter.DoubleToInt64Bits(value);
            bits ^= (long)(seed.GetHashCode() ^ index) << 32 | (uint)(seed.GetHashCode() ^ index);
            return BitConverter.Int64BitsToDouble(bits);
        }

        private string GenerateSignature(double weight, double length, int itemId, bool isSuperPb, string salt)
        {
            // Always use InvariantCulture so decimal separator is always '.' regardless of UI language
            string raw = string.Format(System.Globalization.CultureInfo.InvariantCulture,
                "{0}|{1:F2}|{2:F2}|{3}|{4}", salt, weight, length, itemId, isSuperPb);
            using (SHA256 sha256 = SHA256.Create())
            {
                byte[] bytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(raw));
                return Convert.ToBase64String(bytes).Substring(0, 12);
            }
        }

        // Legacy signature used fish name instead of itemId — kept for migrating existing PB files
        private string GenerateSignatureLegacy(double weight, double length, string name, bool isSuperPb, string salt)
        {
            // Always use InvariantCulture so decimal separator is always '.' regardless of UI language
            string raw = string.Format(System.Globalization.CultureInfo.InvariantCulture,
                "{0}|{1:F2}|{2:F2}|{3}|{4}", salt, weight, length, name, isSuperPb);
            using (SHA256 sha256 = SHA256.Create())
            {
                byte[] bytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(raw));
                return Convert.ToBase64String(bytes).Substring(0, 12);
            }
        }

        private string GenerateMasterSignature(string payload, string salt)
        {
            using (SHA256 sha256 = SHA256.Create())
            {
                byte[] bytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(payload + salt));
                return Convert.ToBase64String(bytes).Substring(0, 12);
            }
        }

        private byte[] GetRawSeedBuffer()
        {
            byte[] buffer = new byte[32];
            byte[] seedData = { 0xA7, 0xE2, 0xD9, 0xC8, 0xF1, 0xB4, 0x8D, 0x7E, 0x2F, 0x6A, 0x53, 0x4C, 0x35, 0x1E, 0x07, 0xF8,
                        0xE1, 0xCA, 0xB3, 0x9C, 0x85, 0x6E, 0x57, 0x40, 0x29, 0x12, 0xFB, 0xE4, 0xCD, 0xB6, 0x9F, 0x88 };
            byte[] seedKey = { 0x55, 0xAA, 0x33, 0xCC, 0x11, 0xEE, 0x77, 0x99 };
            for (int i = 0; i < seedData.Length; i++)
            {
                buffer[i] = (byte)(seedData[i] ^ seedKey[i % seedKey.Length]);
                buffer[i] = (byte)((buffer[i] << 3) | (buffer[i] >> 5));
                buffer[i] ^= (byte)(i * 0x37);
            }
            for (int i = 0; i < buffer.Length; i += 2)
            {
                if (i + 1 < buffer.Length)
                {
                    byte temp = buffer[i];
                    buffer[i] = buffer[i + 1];
                    buffer[i + 1] = temp;
                }
            }
            return buffer;
        }

        private void LoadFishDatabase()
        {
            try
            {
                string json;
                using (var stream = ContentsManager.GetFileStream("GilledWarsMasterFishList.json"))
                using (var reader = new StreamReader(stream)) { json = reader.ReadToEnd(); }
                var db = JsonConvert.DeserializeObject<List<FishData>>(json);

                byte[] rawBuffer = GetRawSeedBuffer();
                string legacySeed = Encoding.UTF8.GetString(rawBuffer, 0, 13);

                for (int i = 0; i < db.Count; i++)
                {
                    var f = db[i];
                    f.Name = DescrambleString(f.Name, i, legacySeed);
                    f.Rarity = DescrambleString(f.Rarity, i, legacySeed);
                    f.Location = DescrambleString(f.Location, i, legacySeed);
                    f.Time = DescrambleString(f.Time, i, legacySeed);
                    f.Bait = DescrambleString(f.Bait, i, legacySeed);
                    f.FishingHole = DescrambleString(f.FishingHole, i, legacySeed);
                    f.ItemId = DescrambleInt(f.ItemId, i, legacySeed);
                    f.MinW = DescrambleDouble(f.MinW, i, legacySeed);
                    f.MaxW = DescrambleDouble(f.MaxW, i, legacySeed);
                    f.MinL = DescrambleDouble(f.MinL, i, legacySeed);
                    f.MaxL = DescrambleDouble(f.MaxL, i, legacySeed);

                    _allFishEntries.Add(new FishUIEntry { Data = f });
                }
            }
            catch (Exception ex) { Logger.Error(ex, "JSON Load Fail"); }
        }

        private async Task InitializeAccountAndLoadAsync()
        {
            string newDir = ModuleDirectory;

            try
            {
                Directory.CreateDirectory(newDir);
                string testFile = Path.Combine(newDir, "permissions_check.txt");
                File.WriteAllText(testFile, "Gilled Wars Write Test - Success");
                File.Delete(testFile);
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "CRITICAL: Could not write to module storage.");
            }

            bool accountFound = false;

            if (!string.IsNullOrWhiteSpace(_customApiKey.Value))
            {
                try
                {
                    var connection = new Gw2Sharp.Connection(_customApiKey.Value);
                    using (var client = new Gw2Sharp.Gw2Client(connection))
                    {
                        var acc = await client.WebApi.V2.Account.GetAsync();
                        _localAccountName = acc.Name.Replace(".", "_");
                        accountFound = true;
                    }
                }
                catch { Logger.Warn("Custom API Key failed."); }
            }

            if (!accountFound)
            {
                int retries = 15;
                while (retries > 0)
                {
                    if (Gw2ApiManager.HasPermissions(new[] { Gw2Sharp.WebApi.V2.Models.TokenPermission.Account }))
                    {
                        var acc = await Gw2ApiManager.Gw2ApiClient.V2.Account.GetAsync();
                        _localAccountName = acc.Name.Replace(".", "_");
                        accountFound = true;
                        break;
                    }
                    await Task.Delay(800);
                    retries--;
                }
            }

            if (!accountFound) _localAccountName = "UnknownAccount";

            LoadPersonalBests();
            RefreshFishLogUI();
        }

        private string GetPbFileName()
        {
            return _localAccountName == "UnknownAccount"
                ? "personal_bests.json"
                : $"personal_bests_{_localAccountName}.json";
        }

        private void LoadPersonalBests()
        {
            string path = Path.Combine(ModuleDirectory, GetPbFileName());
            _isCheater = false;
            _personalBests = new Dictionary<int, PersonalBestRecord>();

            if (!File.Exists(path)) return;

            try
            {
                string json = File.ReadAllText(path);
                string seed = GetGlobalSeed();

                // Try loading as versioned file first (Version 2+)
                PersonalBestFile pbFile = null;
                try { pbFile = JsonConvert.DeserializeObject<PersonalBestFile>(json); } catch { }

                if (pbFile != null && pbFile.Version >= 2 && pbFile.Records != null)
                {
                    // Version 2: itemId-based signatures — validate normally
                    Logger.Info("[GilledWars] Loading PB file version 2");
                    foreach (var kvp in pbFile.Records)
                    {
                        int itemId = kvp.Key;
                        var rec = kvp.Value;
                        ValidateSubRecord(rec.BestWeight, itemId, seed);
                        ValidateSubRecord(rec.BestLength, itemId, seed);
                        _personalBests[itemId] = rec;
                        _caughtFishIds.Add(itemId);
                    }
                }
                else
                {
                    // Version 1 (legacy): name-based signatures — migrate to version 2
                    Logger.Info("[GilledWars] Migrating PB file from version 1 to version 2");
                    Dictionary<int, PersonalBestRecord> legacy = null;
                    try { legacy = JsonConvert.DeserializeObject<Dictionary<int, PersonalBestRecord>>(json); } catch { }

                    if (legacy != null)
                    {
                        foreach (var kvp in legacy)
                        {
                            int itemId = kvp.Key;
                            var rec = kvp.Value;
                            string englishName = _allFishEntries.FirstOrDefault(x => x.Data.ItemId == itemId)?.Data.Name ?? "Unknown";

                            MigrateSubRecord(rec.BestWeight, itemId, englishName, seed);
                            MigrateSubRecord(rec.BestLength, itemId, englishName, seed);

                            _personalBests[itemId] = rec;
                            _caughtFishIds.Add(itemId);
                        }
                        // Save immediately as version 2
                        SavePersonalBests();
                        Logger.Info("[GilledWars] PB migration complete — saved as version 2");
                    }
                }
            }
            catch (Exception ex) { Logger.Error(ex, "Failed to load personal bests"); }
        }

        // Validate a SubRecord that was saved with version 2 (itemId-based signature)
        private void ValidateSubRecord(SubRecord sub, int itemId, string seed)
        {
            if (sub == null) return;
            string cName = sub.CharacterName ?? "Unknown";
            string expected = GenerateSignature(sub.Weight, sub.Length, itemId, sub.IsSuperPb, seed + cName + _localAccountName);
            if (sub.Signature != expected)
            {
                Logger.Warn($"[GilledWars] CHEAT CHECK FAIL itemId={itemId} stored={sub.Signature} expected={expected} weight={sub.Weight} len={sub.Length} char={cName} account={_localAccountName} super={sub.IsSuperPb} seed={seed}");
                sub.IsCheater = _isCheater = true;
            }
            else
            {
                Logger.Info($"[GilledWars] CHEAT CHECK OK itemId={itemId}");
            }
        }

        // Migrate a SubRecord from version 1 (name-based) to version 2 (itemId-based)
        // Verifies the old signature first — if it matches, re-signs with new format
        // If old sig doesn't match either, marks as cheater
        private void MigrateSubRecord(SubRecord sub, int itemId, string englishName, string seed)
        {
            if (sub == null) return;
            string cName = sub.CharacterName ?? "Unknown";

            // Check if already using new format (re-migration safety)
            string expectedNew = GenerateSignature(sub.Weight, sub.Length, itemId, sub.IsSuperPb, seed + cName + _localAccountName);
            if (sub.Signature == expectedNew) return;

            // Verify against old name-based format
            string expectedOld = GenerateSignatureLegacy(sub.Weight, sub.Length, englishName, sub.IsSuperPb, seed + cName + _localAccountName);
            if (sub.Signature == expectedOld)
            {
                // Legitimate record — re-sign with new format
                sub.Signature = expectedNew;
                sub.EnglishFishName = englishName; // Preserve for reference
                Logger.Info($"[GilledWars] Migrated record for {englishName} (itemId {itemId})");
            }
            else
            {
                // Neither signature matches — tampered record
                Logger.Warn($"[GilledWars] Migration: invalid signature for {englishName} (itemId {itemId}) — marking as cheater");
                sub.IsCheater = _isCheater = true;
            }
        }

        private void SavePersonalBests()
        {
            string path = Path.Combine(ModuleDirectory, GetPbFileName());

            try
            {
                var pbFile = new PersonalBestFile { Version = 2, Records = _personalBests };
                File.WriteAllText(path, JsonConvert.SerializeObject(pbFile, Newtonsoft.Json.Formatting.Indented));
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Failed to save personal bests");
            }
        }

        private async Task<Dictionary<int, int>> GetActiveCharacterBags()
        {
            if (string.IsNullOrWhiteSpace(_customApiKey.Value)) return null;
            string charName = GameService.Gw2Mumble.PlayerCharacter.Name;
            if (string.IsNullOrEmpty(charName)) return null;

            try
            {
                var connection = new Gw2Sharp.Connection(_customApiKey.Value);
                using (var client = new Gw2Sharp.Gw2Client(connection))
                {
                    var character = await client.WebApi.V2.Characters[charName].GetAsync();
                    if (character == null || character.Bags == null) return null;

                    var inventory = new Dictionary<int, int>();
                    foreach (var bag in character.Bags)
                    {
                        if (bag == null) continue;
                        foreach (var item in bag.Inventory)
                        {
                            if (item != null)
                            {
                                if (inventory.ContainsKey(item.Id)) inventory[item.Id] += item.Count;
                                else inventory[item.Id] = item.Count;
                            }
                        }
                    }
                    return inventory;
                }
            }
            catch { return null; }
        }

        private async Task CheckApiForNewCatches()
        {
            var currentInventory = await GetActiveCharacterBags();
            if (currentInventory == null)
            {
                ScreenNotification.ShowNotification(strings.ApiErrorBags, ScreenNotification.NotificationType.Error);
                return;
            }

            int newCatches = 0;
            if (_startInventory.Count > 0)
            {
                foreach (var kvp in currentInventory)
                {
                    int itemId = kvp.Key;
                    int currentCount = kvp.Value;
                    int oldCount = _startInventory.ContainsKey(itemId) ? _startInventory[itemId] : 0;

                    if (currentCount > oldCount)
                    {
                        int diff = currentCount - oldCount;
                        for (int i = 0; i < diff; i++)
                        {
                            ProcessCaughtFish(itemId);
                            newCatches++;
                        }
                    }
                }
            }
            _startInventory = currentInventory;
            ScreenNotification.ShowNotification(string.Format(strings.FishMeasuredCount, currentInventory.Values.Sum(), newCatches));
        }

        private async Task TakeInventorySnapshot()
        {
            if (string.IsNullOrWhiteSpace(_customApiKey.Value))
            {
                ScreenNotification.ShowNotification(strings.ApiErrorKey, ScreenNotification.NotificationType.Error);
                return;
            }

            var inv = await GetActiveCharacterBags();
            if (inv != null)
            {
                _startInventory = inv;
                ScreenNotification.ShowNotification(string.Format(strings.SnapshotSaved, inv.Values.Sum()));
            }
            else
            {
                ScreenNotification.ShowNotification(strings.ApiErrorKeyOrChar, ScreenNotification.NotificationType.Error);
            }
        }

        private async Task StartDrfListener()
        {
            if (string.IsNullOrWhiteSpace(_drfToken.Value))
            {
                ScreenNotification.ShowNotification(strings.DrfErrorToken, ScreenNotification.NotificationType.Error);
                return;
            }

            try
            {
                AllowToSetUserAgentHeaderForWebSockets();
                _drfSocket = new System.Net.WebSockets.ClientWebSocket();
                _drfSocket.Options.SetRequestHeader("User-Agent", $"GilledWarsAnglers/0.1.0 BlishHUD/1.3.0");
                _drfCts = new System.Threading.CancellationTokenSource();

                string drfUrl = "wss://drf.rs/ws";
                await _drfSocket.ConnectAsync(new Uri(drfUrl), _drfCts.Token);

                var authBuffer = new ArraySegment<byte>(Encoding.UTF8.GetBytes($"Bearer {_drfToken.Value}"));
                await _drfSocket.SendAsync(authBuffer, System.Net.WebSockets.WebSocketMessageType.Text, true, _drfCts.Token);

                ScreenNotification.ShowNotification(strings.DrfConnected, ScreenNotification.NotificationType.Warning);

                _drfReceiveTask = Task.Run(ReceiveDrfMessages, _drfCts.Token);
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Failed to connect to DRF WebSocket.");
                ScreenNotification.ShowNotification(strings.DrfFailed, ScreenNotification.NotificationType.Error);
            }
        }

        private async Task ReceiveDrfMessages()
        {
            var buffer = new byte[8192];
            try
            {
                while (_drfSocket != null && _drfSocket.State == System.Net.WebSockets.WebSocketState.Open && _drfCts != null && !_drfCts.Token.IsCancellationRequested)
                {
                    var result = await _drfSocket.ReceiveAsync(new ArraySegment<byte>(buffer), _drfCts.Token);
                    if (result.MessageType == System.Net.WebSockets.WebSocketMessageType.Close)
                    {
                        await _drfSocket.CloseAsync(System.Net.WebSockets.WebSocketCloseStatus.NormalClosure, "", _drfCts.Token);
                    }
                    else if (result.MessageType == System.Net.WebSockets.WebSocketMessageType.Text)
                    {
                        string message = Encoding.UTF8.GetString(buffer, 0, result.Count);
                        try
                        {
                            Newtonsoft.Json.Linq.JObject drfData = Newtonsoft.Json.Linq.JObject.Parse(message);
                            if (drfData != null && (string)drfData["kind"] == "data")
                            {
                                var items = drfData["payload"]?["drop"]?["items"] as Newtonsoft.Json.Linq.JObject;
                                if (items != null)
                                {
                                    var itemsDict = items.ToObject<Dictionary<string, int>>();
                                    if (itemsDict != null)
                                    {
                                        foreach (var kvp in itemsDict)
                                        {
                                            if (int.TryParse(kvp.Key, out int itemId))
                                            {
                                                int count = kvp.Value;
                                                if (count > 0)
                                                {
                                                    for (int i = 0; i < count; i++) ProcessCaughtFish(itemId);
                                                }
                                            }
                                        }
                                    }
                                }
                            }
                        }
                        catch { }
                    }
                }
            }
            catch { }
        }

        private void StopDrfListener()
        {
            try
            {
                if (_drfCts != null && !_drfCts.IsCancellationRequested) _drfCts.Cancel();
                if (_drfSocket != null) { _drfSocket.Abort(); _drfSocket.Dispose(); }
                _drfCts?.Dispose();
            }
            catch (Exception ex) { Logger.Error(ex, "Error safely stopping DRF WebSocket."); }
            finally { _drfSocket = null; _drfCts = null; }
        }

        private void StopCasualLogging()
        {
            if (_isCasualLoggingActive)
            {
                _isCasualLoggingActive = false;
                _isSyncTimerActive = false;

                if (_casualLogToggleBtn != null) _casualLogToggleBtn.Text = strings.StartLoggingBtnText;
                if (_casualMeasureBtn != null) _casualMeasureBtn.Enabled = false;
                if (_casualSyncTimerLabel != null) _casualSyncTimerLabel.Visible = false;
                if (_useDrfCheckbox != null) _useDrfCheckbox.Enabled = true;

                StopDrfListener();

                if (_casualCompactPanel != null) _casualCompactPanel.Visible = false;
            }
        }

        private static void AllowToSetUserAgentHeaderForWebSockets()
        {
            var assembly = typeof(System.Net.HttpWebRequest).Assembly;
            foreach (var headerInfoTableFieldInfo in assembly.GetType("System.Net.HeaderInfoTable").GetFields(System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static))
            {
                if (headerInfoTableFieldInfo.Name == "HeaderHashTable")
                {
                    var headerHashTable = headerInfoTableFieldInfo.GetValue(null) as System.Collections.Hashtable;
                    if (headerHashTable == null) return;

                    foreach (string key in headerHashTable.Keys)
                    {
                        var headerInfo = headerHashTable[key];
                        foreach (var headerInfoFieldInfo in assembly.GetType("System.Net.HeaderInfo").GetFields(System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance))
                        {
                            if (headerInfoFieldInfo.Name == "IsRequestRestricted")
                            {
                                var isRequestRestricted = (bool)headerInfoFieldInfo.GetValue(headerInfo);
                                if (isRequestRestricted)
                                    headerInfoFieldInfo.SetValue(headerInfo, false);
                            }
                        }
                    }
                }
            }
        }

        private void ProcessCaughtFish(int itemId)
        {
            var matchingFish = _allFishEntries.FirstOrDefault(x => x.Data.ItemId == itemId)?.Data;
            if (matchingFish == null) return;

            // --- 1. ABORT CHECKS GO FIRST! ---
            // If it's junk or treasure, show the notification and EXIT BEFORE DOING ANY MATH.
            bool isJunk = (matchingFish.Rarity != null && matchingFish.Rarity.Equals("Junk", StringComparison.OrdinalIgnoreCase)) ||
                          (matchingFish.Location != null && matchingFish.Location.Contains("Trash Collector"));

            bool isTreasure = (matchingFish.Name != null && (
                                  matchingFish.Name.IndexOf("Treasure", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                  matchingFish.Name.IndexOf("Chest", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                  matchingFish.Name.IndexOf("Box", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                  matchingFish.Name.IndexOf("Runestone", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                  matchingFish.Name.IndexOf("Cache", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                  matchingFish.Name.IndexOf("Message", StringComparison.OrdinalIgnoreCase) >= 0
                              )) ||
                              (matchingFish.Location != null && matchingFish.Location.Contains("Treasure Collector"));

            if (isJunk)
            {
                string msg = _junkMessages[_rnd.Next(_junkMessages.Length)];
                ScreenNotification.ShowNotification(msg, ScreenNotification.NotificationType.Error);
                return; // Kills the script entirely. No cooler, no profit, no fake weights.
            }

            if (isTreasure)
            {
                string msg = _treasureMessages[_rnd.Next(_treasureMessages.Length)];
                ScreenNotification.ShowNotification(msg, ScreenNotification.NotificationType.Warning);
                return; // Kills the script entirely. No cooler, no profit, no fake weights.
            }


            // --- 2. PROFIT TRACKER MATH GOES SECOND ---
            if (_isCasualLoggingActive && _sessionStartTime != DateTime.MinValue)
            {
                if (matchingFish.Rarity.Equals("Legendary", StringComparison.OrdinalIgnoreCase))
                {
                    _sessionTotalCopper += _ambergrisPriceCopper;
                }
                else if (matchingFish.Rarity.Equals("Ascended", StringComparison.OrdinalIgnoreCase))
                {
                    _sessionTotalCopper += _filletPriceCopper; // Directly uses the 14s Flawless Fillet price
                }
                else if (matchingFish.Rarity.Equals("Exotic", StringComparison.OrdinalIgnoreCase) || matchingFish.Rarity.Equals("Rare", StringComparison.OrdinalIgnoreCase))
                {
                    // Takes 5 Fantastic/Flavorful fillets to craft 1 Flawless, so they are worth 20%
                    _sessionTotalCopper += (int)(_filletPriceCopper * 0.20);
                }
                else if (matchingFish.Rarity.Equals("Masterwork", StringComparison.OrdinalIgnoreCase))
                {
                    _sessionTotalCopper += _fineFilletPriceCopper * 2;
                }
                // --- FIX: Grouped Fine (Blue) and Basic (White) together! ---
                else if (matchingFish.Rarity.Equals("Fine", StringComparison.OrdinalIgnoreCase) || matchingFish.Rarity.Equals("Basic", StringComparison.OrdinalIgnoreCase))
                {
                    bool isCrustacean = matchingFish.Name.IndexOf("Crab", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                        matchingFish.Name.IndexOf("Crawfish", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                        matchingFish.Name.IndexOf("Prawn", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                        matchingFish.Name.IndexOf("Shrimp", StringComparison.OrdinalIgnoreCase) >= 0;

                    if (isCrustacean)
                    {
                        // Crustaceans only give meat, but we don't track meat price yet, so we just use fine fillet as a placeholder
                        _sessionTotalCopper += _fineFilletPriceCopper;
                    }
                    else
                    {
                        _sessionTotalCopper += _fineFilletPriceCopper;
                    }
                }
            }


            // --- 3. WEIGHT & LENGTH MATH ---
            double minW = matchingFish.MinW > 0 ? matchingFish.MinW : 1.0;
            double maxW = matchingFish.MaxW > minW ? matchingFish.MaxW : minW + 5.0;
            double minL = matchingFish.MinL > 0 ? matchingFish.MinL : 5.0;
            double maxL = matchingFish.MaxL > minL ? matchingFish.MaxL : minL + 10.0;

            double r = _rnd.NextDouble();
            double skewed = Math.Pow(r, 3.2);

            double weight = Math.Round(minW + (maxW - minW) * skewed, 2);
            double length = Math.Round(minL + (maxL - minL) * skewed, 2);

            bool isSuperPb = false;
            if (_rnd.NextDouble() <= 0.00005)
            {
                isSuperPb = true;
                double bonusMult = 1.01 + (Math.Pow(_rnd.NextDouble(), 4.0) * 0.11);
                weight = Math.Round(maxW * bonusMult, 2);
                length = Math.Round(maxL * bonusMult, 2);
            }

            string charName = GameService.Gw2Mumble.PlayerCharacter.Name ?? "Unknown";
            string globalSig = GenerateSignature(weight, length, itemId, isSuperPb, GetGlobalSeed() + charName + _localAccountName);
            string tSig = _isTournamentActive ? GenerateSignature(weight, length, itemId, isSuperPb, _tourneyRoomCode) : "";

            bool isNewPbWeight = false;
            bool isNewPbLength = false;

            if (!_personalBests.ContainsKey(itemId)) _personalBests[itemId] = new PersonalBestRecord();
            var pbObj = _personalBests[itemId];

            bool usedDrf = _useDrfCheckbox != null && _useDrfCheckbox.Checked;

            if (pbObj.BestWeight == null || weight > pbObj.BestWeight.Weight)
            {
                isNewPbWeight = true;
                pbObj.BestWeight = new SubRecord { Weight = weight, Length = length, Signature = globalSig, IsCheater = false, IsSuperPb = isSuperPb, CaughtWithDrf = usedDrf, CharacterName = charName, IsSubmitted = false };
            }
            if (pbObj.BestLength == null || length > pbObj.BestLength.Length)
            {
                isNewPbLength = true;
                pbObj.BestLength = new SubRecord { Weight = weight, Length = length, Signature = globalSig, IsCheater = false, IsSuperPb = isSuperPb, CaughtWithDrf = usedDrf, CharacterName = charName, IsSubmitted = false };
            }

            if (isNewPbWeight || isNewPbLength) { SavePersonalBests(); RefreshFishLogUI(); }

            _caughtFishIds.Add(itemId);

            string characterName = GameService.Gw2Mumble.PlayerCharacter.Name;
            if (string.IsNullOrEmpty(characterName)) characterName = "UnknownPlayer";

            var catchRecord = new TournamentCatch
            {
                Id = itemId,
                Name = matchingFish.Name,
                Weight = weight,
                Length = length,
                Rarity = matchingFish.Rarity,
                CharacterName = characterName,
                Signature = globalSig,
                TourneySig = tSig,
                IsNewPb = (isNewPbWeight || isNewPbLength),
                IsSuperPb = isSuperPb
            };

            _recentCatches.Insert(0, catchRecord);
            if (_recentCatches.Count > 20) _recentCatches.RemoveAt(_recentCatches.Count - 1);

            if (_recentCatchesPanel != null && _recentCatchesPanel.Visible) UpdateRecentCatchesUI();
            if (_casualCompactPanel != null && _casualCompactPanel.Visible) UpdateCompactCooler();
            if (_achievementResultsPanel != null && _achievementResultsPanel.Visible && _openAnalyzerAchievementId != 0)
            {
                _ = ShowAchievementResultsPanel(_openAnalyzerLocationName, _openAnalyzerAchievementId, 0, _openAnalyzerSubMax, _openAnalyzerSubDescription);
            }

            // --- 4. UI UPDATES & NOTIFICATIONS ---
            string pbAlert = isSuperPb ? " - SUPER PB!" : (catchRecord.IsNewPb ? " - NEW PB!" : "");
            var notifType = isSuperPb ? ScreenNotification.NotificationType.Warning : ScreenNotification.NotificationType.Info;

            if (_isTournamentActive && !_isTourneyWaitingRoom)
            {
                if (_tourneyTargetItemId == 0 || _tourneyTargetItemId == itemId)
                {
                    _tourneyCatches.Add(catchRecord);
                    UpdateActiveTourneyCoolerUI();
                    ScreenNotification.ShowNotification(string.Format(strings.TourneyCatchAlert, catchRecord.Name, catchRecord.Weight, catchRecord.Length, pbAlert), notifType);
                }
                else ScreenNotification.ShowNotification(string.Format(strings.CaughtAlert, catchRecord.Name, catchRecord.Weight, catchRecord.Length, pbAlert), notifType);
            }
            else ScreenNotification.ShowNotification(string.Format(strings.CaughtAlert, catchRecord.Name, catchRecord.Weight, catchRecord.Length, pbAlert), notifType);

            // --- Force the Biting Widget to update the checkmarks immediately! ---
            if (_bitingWidgetPanel != null && _bitingWidgetPanel.Visible) RefreshBitingWidget();
        }
        private void BuildMainWindow()
        {
            // --- THEME COLORS ---
            Color deepNavyBg = new Color(13, 27, 42);
            Color darkTealPanel = new Color(26, 47, 69);
            Color agedGoldText = new Color(201, 168, 76);

            // --- MAIN LODGE WINDOW ---
            _mainWindow = new Panel
            {
                ShowBorder = true,
                Size = new Point(650, 500), 
                Location = new Point(300, 300),
                Parent = GameService.Graphics.SpriteScreen,
                Visible = false,
                BackgroundColor = deepNavyBg,
                ClipsBounds = false,
                ZIndex = 1000
            };

            // --- HEADER BAR ---
            var headerBar = new Panel
            {
                Parent = _mainWindow,
                Size = new Point(_mainWindow.Width, 30),
                Location = new Point(0, 0),
                BackgroundColor = Color.Black * 0.6f
            };
            var titleLabel = new Label
            {
                
                Text = strings.CornerIconSubtitle,
                Parent = headerBar,
                Location = new Point(10, 5),
                Font = GameService.Content.DefaultFont16,
                TextColor = agedGoldText,
                AutoSizeWidth = true
            };

            var xCloseBtn = new Label
            {
                Text = "X",
                Parent = headerBar,
                Location = new Point(headerBar.Width - 25, 5),
                Font = GameService.Content.DefaultFont16,
                TextColor = Color.Red,
                AutoSizeWidth = true,
                BasicTooltipText = strings.CloseLodgeTooltip 
            };

            // Flag buttons — click to override language, click active flag again to reset to BlishHUD auto
            var flagEN = new Image { Texture = ContentsManager.GetTexture("flags/gb.png"), Parent = headerBar, Size = new Point(26, 18), Location = new Point(headerBar.Width - 135, 6), BasicTooltipText = "English (click again to reset to BlishHUD language)" };
            var flagFR = new Image { Texture = ContentsManager.GetTexture("flags/fr.png"), Parent = headerBar, Size = new Point(26, 18), Location = new Point(headerBar.Width - 107, 6), BasicTooltipText = "Français (click again to reset to BlishHUD language)" };
            var flagDE = new Image { Texture = ContentsManager.GetTexture("flags/de.png"), Parent = headerBar, Size = new Point(26, 18), Location = new Point(headerBar.Width - 79, 6),  BasicTooltipText = "Deutsch (click again to reset to BlishHUD language)" };
            var flagES = new Image { Texture = ContentsManager.GetTexture("flags/es.png"), Parent = headerBar, Size = new Point(26, 18), Location = new Point(headerBar.Width - 51, 6),  BasicTooltipText = "Español (click again to reset to BlishHUD language)" };

            Action updateFlagOpacity = () =>
            {
                string cur = DetectLanguage();
                string saved = _selectedLanguage.Value;
                // Full brightness = active language; slightly dim = auto-detected; very dim = inactive
                flagEN.Opacity = cur == "en" ? 1.0f : 0.3f;
                flagFR.Opacity = cur == "fr" ? 1.0f : 0.3f;
                flagDE.Opacity = cur == "de" ? 1.0f : 0.3f;
                flagES.Opacity = cur == "es" ? 1.0f : 0.3f;
                // Add a subtle gold tint to whichever flag is manually pinned (not auto)
            };
            updateFlagOpacity();

            flagEN.Click += (s, e) => {
                string next = _selectedLanguage.Value == "en" ? "auto" : "en";
                ApplyLanguage(next == "auto" ? DetectLanguage() : next);
                _selectedLanguage.Value = next;
                updateFlagOpacity();
                ScreenNotification.ShowNotification(next == "auto" ? "Language reset to BlishHUD default." : "Language: English — reopen UI to apply.", ScreenNotification.NotificationType.Info);
            };
            flagFR.Click += (s, e) => {
                string next = _selectedLanguage.Value == "fr" ? "auto" : "fr";
                ApplyLanguage(next == "auto" ? DetectLanguage() : next);
                _selectedLanguage.Value = next;
                updateFlagOpacity();
                ScreenNotification.ShowNotification(next == "auto" ? "Langue réinitialisée." : "Langue: Français — rouvrir l'UI pour appliquer.", ScreenNotification.NotificationType.Info);
            };
            flagDE.Click += (s, e) => {
                string next = _selectedLanguage.Value == "de" ? "auto" : "de";
                ApplyLanguage(next == "auto" ? DetectLanguage() : next);
                _selectedLanguage.Value = next;
                updateFlagOpacity();
                ScreenNotification.ShowNotification(next == "auto" ? "Sprache zurückgesetzt." : "Sprache: Deutsch — UI neu öffnen.", ScreenNotification.NotificationType.Info);
            };
            flagES.Click += (s, e) => {
                string next = _selectedLanguage.Value == "es" ? "auto" : "es";
                ApplyLanguage(next == "auto" ? DetectLanguage() : next);
                _selectedLanguage.Value = next;
                updateFlagOpacity();
                ScreenNotification.ShowNotification(next == "auto" ? "Idioma restablecido." : "Idioma: Español — reabrir UI para aplicar.", ScreenNotification.NotificationType.Info);
            };

            xCloseBtn.Click += (s, e) => { _mainWindow.Visible = false; };
            xCloseBtn.MouseEntered += (s, e) => { xCloseBtn.TextColor = Color.White; };
            xCloseBtn.MouseLeft += (s, e) => { xCloseBtn.TextColor = Color.Red; };

            headerBar.LeftMouseButtonPressed += (s, ev) => {
                if (GameService.Input.Mouse.ActiveControl == headerBar || GameService.Input.Mouse.ActiveControl == titleLabel)
                {
                    _isDragging = true;
                    _dragOffset = new Point(GameService.Input.Mouse.Position.X - _mainWindow.Location.X, GameService.Input.Mouse.Position.Y - _mainWindow.Location.Y);
                }
            };

            // --- LEFT RAIL NAVIGATION ---
            var navPanel = new FlowPanel
            {
                Parent = _mainWindow,
                Location = new Point(0, 30),
                Size = new Point(50, _mainWindow.Height - 30),
                BackgroundColor = darkTealPanel,
                FlowDirection = ControlFlowDirection.SingleTopToBottom,
                OuterControlPadding = new Vector2(9, 15), 
                ControlPadding = new Vector2(0, 30) 
            };

            // --- MAIN CONTENT AREA ---
            var contentHost = new Panel
            {
                Parent = _mainWindow,
                Location = new Point(50, 30), 
                Size = new Point(_mainWindow.Width - 50, _mainWindow.Height - 30),
                BackgroundColor = Color.Transparent
            };

            // --- PANELS (Docked inside Content Host) ---
            _casualPanel = new Panel { Parent = contentHost, Size = contentHost.Size, Visible = true };
            _tournamentPanel = new Panel { Parent = contentHost, Size = contentHost.Size, Visible = false };
            _fishLogPanel = new Panel { Parent = contentHost, Size = contentHost.Size, Visible = false };
            _achievementPanel = new Panel { Parent = contentHost, Size = contentHost.Size, Visible = false };

            
            void ShowPanel(Panel active)
            {
                _casualPanel.Visible = _tournamentPanel.Visible = _fishLogPanel.Visible = _achievementPanel.Visible = false;
                active.Visible = true;
            }
            // --- NAVIGATION ICONS ---
            var casualBtnHost = new Panel { Parent = navPanel, Size = new Point(32, 32), BasicTooltipText = strings.CasualFishingTooltip };
            
            var casualIcon = new Image { Texture = ContentsManager.GetTexture("images/casualico.png"), Parent = casualBtnHost, Size = new Point(24, 32), Location = new Point(4, 0) };

            casualBtnHost.Click += (s, ev) => ShowPanel(_casualPanel);
            casualIcon.Click += (s, ev) => ShowPanel(_casualPanel);

            var tourneyIcon = new Image { Texture = ContentsManager.GetTexture("images/tournamentico.png"), Parent = navPanel, Size = new Point(32, 32), BasicTooltipText = strings.TourneyModeTooltip };
            tourneyIcon.Click += (s, ev) => ShowPanel(_tournamentPanel);

            var logIcon = new Image { Texture = ContentsManager.GetTexture("images/fishlogico.png"), Parent = navPanel, Size = new Point(32, 32), BasicTooltipText = strings.FishLogTooltip };
            logIcon.Click += (s, ev) => ShowPanel(_fishLogPanel);

            var leaderboardIcon = new Image { Texture = ContentsManager.GetTexture("images/leaderboardico.png"), Parent = navPanel, Size = new Point(32, 32), BasicTooltipText = strings.LeaderboardTooltip };
            leaderboardIcon.Click += (s, ev) => { ScreenNotification.ShowNotification(strings.LoadingTop10); ShowLeaderboardWindow(); };

            var webIcon = new Image { Texture = ContentsManager.GetTexture("images/websiteico.png"), Parent = navPanel, Size = new Point(32, 32), BasicTooltipText = strings.OpenWebsiteTooltip };
            webIcon.Click += (s, ev) => { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo { FileName = "https://www.gilledwars.com", UseShellExecute = true }); };

            // --- BUILD SUB-UI ---
            BuildFishLogGrid(_fishLogPanel);
            BuildCasualUI(_casualPanel);
            BuildTournamentUI(_tournamentPanel);
            BuildActiveTournamentWidget();
        }

        private void BuildFishLogGrid(Panel parent)
        {
            if (parent == null) return;

            _categoryPanels.Clear();

            var filterPanel = new Panel { Parent = parent, Location = new Point(10, 0), Size = new Point(parent.Width - 20, 110) };

            /// ROW 1
            var searchBar = new TextBox { Parent = filterPanel, Location = new Point(0, 0), Width = 140, PlaceholderText = strings.SearchFishPlaceholder };
            var rarityDrop = new Dropdown { Parent = filterPanel, Location = new Point(150, 0), Width = 110 };
            var locationDrop = new Dropdown { Parent = filterPanel, Location = new Point(270, 0), Width = 130 };
            var holeDrop = new Dropdown { Parent = filterPanel, Location = new Point(410, 0), Width = 110 };

            // ROW 2
            var timeDrop = new Dropdown { Parent = filterPanel, Location = new Point(0, 35), Width = 100 };
            var baitDrop = new Dropdown { Parent = filterPanel, Location = new Point(110, 35), Width = 120 };
            var collapseBtn = new StandardButton { Text = strings.CollapseBtnText, Parent = filterPanel, Location = new Point(240, 35), Width = 100 };
            var revealBtn = new StandardButton { Text = strings.RevealBtnText, Parent = filterPanel, Location = new Point(350, 35), Width = 100 };
            var resetFiltersBtn = new StandardButton { Text = strings.ResetFiltersBtnText, Parent = filterPanel, Location = new Point(460, 35), Width = 130 };

            // ROW 3
            var pushLeaderboardBtn = new StandardButton { Text = strings.PushPbBtnText, Parent = filterPanel, Location = new Point(0, 70), Width = 150, BasicTooltipText = strings.PushPbTooltip };
            var zoneAnalyzerBtn = new StandardButton { Text = strings.ZoneAnalyzerBtnText, Parent = filterPanel, Location = new Point(160, 70), Width = 150, BasicTooltipText = strings.ZoneAnalyzerTooltip };
            var metaProgressBtn = new StandardButton { Text = strings.MetaProgressBtnText, Parent = filterPanel, Location = new Point(320, 70), Width = 150, BasicTooltipText = strings.MetaProgressTooltip };

            var scroll = new FlowPanel { Parent = parent, Location = new Point(10, 115), Size = new Point(parent.Width - 20, parent.Height - 120), CanScroll = true, FlowDirection = ControlFlowDirection.SingleTopToBottom };

            void ApplyFilters()
            {
                // Guard against being called before dropdowns are fully initialized
                if (rarityDrop.SelectedItem == null || locationDrop.SelectedItem == null ||
                    holeDrop.SelectedItem == null || timeDrop.SelectedItem == null ||
                    baitDrop.SelectedItem == null) return;

                bool hasFilter = !string.IsNullOrEmpty(searchBar.Text) ||
                                 rarityDrop.SelectedItem != "All Rarities" ||
                                 locationDrop.SelectedItem != "All Locations" ||
                                 holeDrop.SelectedItem != "All Holes" ||
                                 timeDrop.SelectedItem != "All Times" ||
                                 baitDrop.SelectedItem != "All Baits";

                foreach (var cat in _categoryPanels)
                {
                    bool anyVisible = false;
                    foreach (var entry in _allFishEntries.Where(x => x.CategoryPanel == cat))
                    {
                        bool match = true;
                        if (!string.IsNullOrEmpty(searchBar.Text) && !entry.Data.Name.ToLower().Contains(searchBar.Text.ToLower())) match = false;
                        if (rarityDrop.SelectedItem != null && rarityDrop.SelectedItem != "All Rarities" && entry.Data.Rarity != rarityDrop.SelectedItem) match = false;
                        if (locationDrop.SelectedItem != null && locationDrop.SelectedItem != "All Locations" && entry.Data.Location != locationDrop.SelectedItem) match = false;
                        if (holeDrop.SelectedItem != null && holeDrop.SelectedItem != "All Holes" && !entry.Data.FishingHole.Contains(holeDrop.SelectedItem)) match = false;
                        if (timeDrop.SelectedItem != null && timeDrop.SelectedItem != "All Times" && !entry.Data.Time.Contains(timeDrop.SelectedItem == "Any" ? "Any" : timeDrop.SelectedItem)) match = false;
                        if (baitDrop.SelectedItem != null && baitDrop.SelectedItem != "All Baits" && !entry.Data.Bait.Contains(baitDrop.SelectedItem)) match = false;

                        entry.Icon.Visible = match;
                        if (match) anyVisible = true;
                    }

                    cat.Visible = anyVisible;


                    if (hasFilter && anyVisible) { cat.Collapsed = false; }
                    else if (!hasFilter) { cat.Collapsed = false; }
                }
                scroll.Invalidate();
                scroll.VerticalScrollOffset = 0;
            }

            searchBar.TextChanged += (s, e) => ApplyFilters();
            rarityDrop.ValueChanged += (s, e) => ApplyFilters();
            locationDrop.ValueChanged += (s, e) => ApplyFilters();
            holeDrop.ValueChanged += (s, e) => ApplyFilters();
            timeDrop.ValueChanged += (s, e) => ApplyFilters();
            baitDrop.ValueChanged += (s, e) => ApplyFilters();
            collapseBtn.Click += (s, e) => { foreach (var c in _categoryPanels) c.Collapsed = true; };
            revealBtn.Click += (s, e) => { foreach (var c in _categoryPanels) c.Collapsed = false; };

            resetFiltersBtn.Click += (s, e) => {
                searchBar.Text = ""; rarityDrop.SelectedItem = "All Rarities"; locationDrop.SelectedItem = "All Locations";
                holeDrop.SelectedItem = "All Holes"; timeDrop.SelectedItem = "All Times"; baitDrop.SelectedItem = "All Baits";
                ApplyFilters();
            };

            pushLeaderboardBtn.Click += async (s, e) => {
                if ((DateTime.Now - _lastSubmitTime).TotalMinutes < 5)
                {
                    double rem = 5.0 - (DateTime.Now - _lastSubmitTime).TotalMinutes;
                    // RESTORED & LOCALIZED: This is the line that was missing.
                    ScreenNotification.ShowNotification(string.Format(strings.SubmitCooldown, rem.ToString("F1")), ScreenNotification.NotificationType.Error);
                    return;
                }

                pushLeaderboardBtn.Enabled = false;
                pushLeaderboardBtn.Text = strings.UploadingText;
                await ForceUploadPB();
                _lastSubmitTime = DateTime.Now;
                pushLeaderboardBtn.Text = strings.PushPbBtnText;
                pushLeaderboardBtn.Enabled = true;
            };

            zoneAnalyzerBtn.Click += async (s, ev) => {
                zoneAnalyzerBtn.Enabled = false;
                zoneAnalyzerBtn.Text = strings.Scanning;
                try
                {
                    int currentMapId = GameService.Gw2Mumble.CurrentMap.Id;
                    if (currentMapId == 0) return;

                    var mapInfo = await Gw2ApiManager.Gw2ApiClient.V2.Maps.GetAsync(currentMapId);
                    var achievementMap = new Dictionary<string, int> {
                { "Kryta", 6068 }, { "Shiverpeak", 6179 }, { "Ascalon", 6330 }, { "Maguuma Jungle", 6344 }, { "Ruins of Orr", 6363 },
                { "Southsun Cove", 6106 }, { "Sandswept Isles", 6106 }, { "Elona", 6106 }, 
                { "Crystal Desert", 6317 }, { "Ring of Fire", 6489 }, { "Seitung Province", 6336 }, { "New Kaineng City", 6342 },
                { "The Echovald Wilds", 6258 }, { "Dragon's End", 6506 }, { "Skywatch Archipelago", 7114 }, { "Amnytas", 7114 },
                { "Inner Nayos", 7114 }, { "Lowland Shore", 8168 }, { "Janthir Syntri", 8168 }, { "Mistburned Barrens", 8554 },
                { "Castora", 8900 }
            };

                    string target = achievementMap.Keys.FirstOrDefault(k => mapInfo.Name.Contains(k) || (mapInfo.RegionName != null && mapInfo.RegionName.Contains(k))) ?? "All Locations";

                    if (target != "All Locations")
                    {
                        int achId = achievementMap[target];
                        var achDef = await Gw2ApiManager.Gw2ApiClient.V2.Achievements.GetAsync(achId);
                        int subCurrent = 0;
                        int subMax = achDef.Tiers?.LastOrDefault()?.Count ?? achDef.Bits?.Count ?? 0;
                        string subDescription = achDef.Description ?? "No description available.";
                        await ShowAchievementResultsPanel(target, achId, subCurrent, subMax, subDescription);
                    }
                    else { ScreenNotification.ShowNotification(strings.NoAchievFound, ScreenNotification.NotificationType.Error); }
                }
                catch (Exception ex) { Logger.Error(ex, "Zone Analyzer failed."); }
                finally { zoneAnalyzerBtn.Enabled = true; zoneAnalyzerBtn.Text = strings.ZoneAnalyzerBtnText; }
            };

            metaProgressBtn.Click += async (s, ev) => {
                metaProgressBtn.Enabled = false;
                metaProgressBtn.Text = strings.Loading;
                await ShowMetaProgressWindow();
                metaProgressBtn.Text = strings.MetaProgressBtnText;
                metaProgressBtn.Enabled = true;
            };

            var uniqueHoles = new HashSet<string>();
            foreach (var entry in _allFishEntries) { if (!string.IsNullOrEmpty(entry.Data.FishingHole)) { foreach (var sh in entry.Data.FishingHole.Replace("None, ", "").Split(',').Select(h => h.Trim())) uniqueHoles.Add(sh); } }
            rarityDrop.Items.Add("All Rarities"); foreach (var r in _allFishEntries.Select(x => x.Data.Rarity).Distinct()) rarityDrop.Items.Add(r); rarityDrop.SelectedItem = "All Rarities";
            locationDrop.Items.Add("All Locations"); foreach (var l in _allFishEntries.Select(x => x.Data.Location).Where(loc => !loc.StartsWith("Avid", StringComparison.OrdinalIgnoreCase)).Distinct()) locationDrop.Items.Add(l); locationDrop.SelectedItem = "All Locations";
            holeDrop.Items.Add("All Holes"); foreach (var h in uniqueHoles.OrderBy(x => x)) if (h != "None") holeDrop.Items.Add(h); holeDrop.SelectedItem = "All Holes";
            timeDrop.Items.Add("All Times"); timeDrop.Items.Add("Daytime"); timeDrop.Items.Add("Nighttime"); timeDrop.Items.Add("Dawn/Dusk"); timeDrop.Items.Add("Any"); timeDrop.SelectedItem = "All Times";
            baitDrop.Items.Add("All Baits"); foreach (var b in _allFishEntries.Select(x => x.Data.Bait).Distinct()) baitDrop.Items.Add(b); baitDrop.SelectedItem = "All Baits";

            foreach (var group in _allFishEntries.Select(x => x.Data).Where(x => x.Location != "Any" && !x.Location.StartsWith("Avid", StringComparison.OrdinalIgnoreCase)).GroupBy(x => x.Location).OrderBy(x => x.Key))
            {
                var p = new FlowPanel { Parent = scroll, Title = group.Key, CanCollapse = true, ShowBorder = true, Width = parent.Width - 40, HeightSizingMode = SizingMode.AutoSize, FlowDirection = ControlFlowDirection.LeftToRight };
                _categoryPanels.Add(p);

                foreach (var fish in group)
                {
                    string safeName = fish.Name.Replace(" ", "_").Replace("'", "").Replace("-", "");
                    bool isCaught = _caughtFishIds.Contains(fish.ItemId);
                    string pbWText = strings.NoneLogged; string pbLText = strings.NoneLogged;

                   
                    var tintColor = isCaught ? Color.White : Color.Black * 0.4f;

                    if (_personalBests.TryGetValue(fish.ItemId, out var rec))
                    {
                        if (rec.BestWeight != null) pbWText = rec.BestWeight.IsCheater ? strings.CheaterDetected : $"{rec.BestWeight.Weight} lbs";
                        if (rec.BestLength != null) pbLText = rec.BestLength.IsCheater ? strings.CheaterDetected : $"{rec.BestLength.Length} in";
                        if ((rec.BestWeight != null && rec.BestWeight.IsSuperPb) || (rec.BestLength != null && rec.BestLength.IsSuperPb)) tintColor = Color.Gold;
                    }

                    string tooltip = $"{fish.Name}\nRarity: {fish.Rarity}\nLocation: {fish.Location}\nHole: {fish.FishingHole}\nTime: {fish.Time}\nBait: {fish.Bait}";
                    bool isColl = (fish.Rarity != null && fish.Rarity.Equals("Junk", StringComparison.OrdinalIgnoreCase)) || (fish.Location != null && fish.Location.Contains("Collector"));
                    if (!isColl) tooltip += $"\n\nPB Weight: {pbWText}\nPB Length: {pbLText}";

                    var img = new Image { Parent = p, Size = new Point(64, 64), BasicTooltipText = tooltip, Texture = ContentsManager.GetTexture($"images/{safeName}.png"), Tint = tintColor };

                    img.Click += (sender, ev) => {
                        byte[] linkData = new byte[6]; linkData[0] = 0x02; linkData[1] = 0x01;
                        BitConverter.GetBytes(fish.ItemId).CopyTo(linkData, 2);
                        string code = $"[&{Convert.ToBase64String(linkData)}]";
                        string clip = code;

                        if (!isColl && _personalBests.TryGetValue(fish.ItemId, out var pbRec))
                        {
                            double w = pbRec.BestWeight?.Weight ?? 0;
                            double l = pbRec.BestLength?.Length ?? 0;
                            if (w > 0 || l > 0) { clip = string.Format(strings.PbClipText, code, w, l); }
                        }
                        System.Windows.Forms.Clipboard.SetText(clip);
                        ScreenNotification.ShowNotification(string.Format(strings.FishCodeCopied, fish.Name));
                    };

                    var entry = _allFishEntries.First(x => x.Data.ItemId == fish.ItemId);
                    entry.Icon = img;
                    entry.CategoryPanel = p;
                }
            }
            new Panel { Parent = scroll, Width = parent.Width - 40, Height = 60 };
        }

        private async Task ShowAchievementResultsPanel(string locationName, int achievementId, int subCurrent, int subMax, string subDescription)
        {
            _openAnalyzerLocationName = locationName; _openAnalyzerAchievementId = achievementId; _openAnalyzerSubMax = subMax; _openAnalyzerSubDescription = subDescription;
            Color deepNavyBg = new Color(13, 27, 42); Color darkTealPanel = new Color(26, 47, 69); Color agedGoldText = new Color(201, 168, 76);

            if (_achievementResultsPanel == null)
            {
                _achievementResultsPanel = new Panel { Parent = GameService.Graphics.SpriteScreen, Location = new Point(400, 100), ShowBorder = true, BackgroundColor = deepNavyBg, ZIndex = 1001, ClipsBounds = false };
            }
            if (_achievementLegendPanel == null)
            {
                _achievementLegendPanel = new Panel { Parent = GameService.Graphics.SpriteScreen, Size = new Point(800, 45), ShowBorder = true, BackgroundColor = darkTealPanel, ZIndex = 1002 };
                int lx = 5; string[] rarityNames = { "Legendary", "Ascended", "Exotic", "Rare", "Masterwork", "Fine", "Basic" };
                foreach (var rName in rarityNames) { var rColor = GetRarityColor(rName); new Label { Text = "■", Parent = _achievementLegendPanel, Location = new Point(lx, 15), TextColor = rColor, Font = GameService.Content.DefaultFont14, AutoSizeWidth = true }; new Label { Text = rName, Parent = _achievementLegendPanel, Location = new Point(lx + 15, 15), TextColor = rColor, Font = GameService.Content.DefaultFont12, AutoSizeWidth = true }; lx += (rName.Length * 7) + 20; }
            }

            _achievementResultsPanel.Visible = true;

            try
            {
                var achievementDef = await Gw2ApiManager.Gw2ApiClient.V2.Achievements.GetAsync(achievementId);
                var accAchievements = await Gw2ApiManager.Gw2ApiClient.V2.Account.Achievements.GetAsync();

                if (_isAnalyzerMinified) { _achievementResultsPanel.Size = new Point(320, 520); _achievementLegendPanel.Visible = false; }
                else { _achievementResultsPanel.Size = new Point(800, 600); _achievementLegendPanel.Visible = true; _achievementLegendPanel.Location = new Point(_achievementResultsPanel.Location.X, _achievementResultsPanel.Location.Y + _achievementResultsPanel.Height + 2); }


                // --- FLICKER FIX: Build into a temporary container first! ---
                var ghostContainer = new Panel { Size = _achievementResultsPanel.Size, BackgroundColor = Color.Transparent };

                var hBar = new Panel { Parent = ghostContainer, Size = new Point(ghostContainer.Width, 30), BackgroundColor = Color.Black * 0.6f, Location = new Point(0, 0) };

                // --- NEW BULLETPROOF DRAG LOGIC ---
                hBar.LeftMouseButtonPressed += (s, ev) => {
                    _isDraggingAchievement = true;
                    _achievementDragOffset = new Point(GameService.Input.Mouse.Position.X - _achievementResultsPanel.Location.X, GameService.Input.Mouse.Position.Y - _achievementResultsPanel.Location.Y);
                };

                string titleText = _isAnalyzerMinified ? locationName : $"{locationName} Progress";
                new Label { Text = titleText, Parent = hBar, Location = new Point(10, 5), Font = GameService.Content.DefaultFont16, TextColor = agedGoldText, AutoSizeWidth = true };

                var closeX = new Label { Text = "X", Parent = hBar, Location = new Point(hBar.Width - 25, 5), Font = GameService.Content.DefaultFont16, TextColor = Color.Red, AutoSizeWidth = true };
                closeX.Click += (s, e) => { _achievementResultsPanel.Visible = false; _achievementLegendPanel.Visible = false; _openAnalyzerAchievementId = 0; };
                closeX.MouseEntered += (s, e) => { closeX.TextColor = Color.White; };
                closeX.MouseLeft += (s, e) => { closeX.TextColor = Color.Red; };

                var minMaxBtn = new StandardButton { Text = _isAnalyzerMinified ? "[+]" : "[-]", Parent = hBar, Location = new Point(hBar.Width - 75, 2), Width = 40, Height = 26, BasicTooltipText = strings.ToggleGridView };
                minMaxBtn.Click += async (s, e) => { _isAnalyzerMinified = !_isAnalyzerMinified; await ShowAchievementResultsPanel(locationName, achievementId, subCurrent, subMax, subDescription); };

                var progress = accAchievements.FirstOrDefault(a => a.Id == achievementId);
                var completedBits = progress?.Bits ?? new List<int>();
                int totalBits = achievementDef.Bits?.Count ?? 0;
                List<int> trueMissingBits = new List<int>();

                if (achievementDef.Bits != null)
                {
                    for (int i = 0; i < achievementDef.Bits.Count; i++)
                    {
                        var idProp = achievementDef.Bits[i].GetType().GetProperty("Id");
                        if (idProp == null) continue;
                        int fishItemId = (int)idProp.GetValue(achievementDef.Bits[i]);
                        if (!completedBits.Contains(i) && !_caughtFishIds.Contains(fishItemId)) trueMissingBits.Add(i);
                    }
                }
                int missingCount = trueMissingBits.Count;

                if (_isAnalyzerMinified)
                {
                    var scrollContainer = new FlowPanel { Parent = ghostContainer, Location = new Point(10, 35), Size = new Point(ghostContainer.Width - 20, ghostContainer.Height - 45), CanScroll = true, FlowDirection = ControlFlowDirection.SingleTopToBottom, ControlPadding = new Vector2(0, 10) };
                    new Label { Text = $"Missing Fish ({missingCount})", Parent = scrollContainer, AutoSizeWidth = true, TextColor = Color.Red, Font = GameService.Content.DefaultFont14 };
                    var missingGrid = new FlowPanel { Parent = scrollContainer, Width = scrollContainer.Width - 15, HeightSizingMode = SizingMode.AutoSize, FlowDirection = ControlFlowDirection.LeftToRight };
                    new Label { Text = $"All Zone Fish ({totalBits})", Parent = scrollContainer, AutoSizeWidth = true, TextColor = Color.Cyan, Font = GameService.Content.DefaultFont14 };
                    var allGrid = new FlowPanel { Parent = scrollContainer, Width = scrollContainer.Width - 15, HeightSizingMode = SizingMode.AutoSize, FlowDirection = ControlFlowDirection.LeftToRight };

                    if (achievementDef.Bits != null)
                    {
                        for (int i = 0; i < achievementDef.Bits.Count; i++)
                        {
                            var idProp = achievementDef.Bits[i].GetType().GetProperty("Id");
                            if (idProp == null) continue;
                            int fishItemId = (int)idProp.GetValue(achievementDef.Bits[i]);
                            var dbFish = _allFishEntries.FirstOrDefault(x => x.Data.ItemId == fishItemId)?.Data;

                            if (dbFish != null)
                            {
                                bool isMissing = trueMissingBits.Contains(i);
                                
                                if (isMissing) CreateFishIconWithBorder(dbFish, missingGrid, 48, true);
                                CreateFishIconWithBorder(dbFish, allGrid, 48, isMissing);
                            }
                        }
                    }
                }
                else
                {
                    var progressHeader = new Panel { Parent = ghostContainer, Location = new Point(10, 45), Size = new Point(780, 35) };
                    var listContainer = new FlowPanel { Parent = ghostContainer, Location = new Point(10, 115), Size = new Point(780, 475), CanScroll = true, FlowDirection = ControlFlowDirection.SingleTopToBottom, ControlPadding = new Vector2(0, 2) };
                    new Label { Text = $"{locationName.ToUpper()}:", Parent = progressHeader, Location = new Point(0, 5), Font = GameService.Content.DefaultFont18, AutoSizeWidth = true, TextColor = agedGoldText };
                    new Label { Text = $"MISSING: {missingCount} / {totalBits}", Parent = progressHeader, Location = new Point(310, 7), Font = GameService.Content.DefaultFont14, AutoSizeWidth = true, TextColor = Color.Cyan };

                    if (missingCount == 0 && totalBits > 0) { new Label { Text = "✓ ACHIEVEMENT COMPLETE! GOOD JOB!", Parent = listContainer, Location = new Point(20, 20), Font = GameService.Content.DefaultFont16, TextColor = Color.LimeGreen, AutoSizeWidth = true }; }
                    else
                    {
                        var columnHeader = new Panel { Parent = ghostContainer, Location = new Point(10, 85), Size = new Point(780, 30), BackgroundColor = Color.Black * 0.4f };
                        new Label { Text = strings.NameColumnHeader, Parent = columnHeader, Location = new Point(60, 5), Width = 170, TextColor = agedGoldText, Font = GameService.Content.DefaultFont16 };
                        new Label { Text = strings.BaitColumnHeader, Parent = columnHeader, Location = new Point(240, 5), Width = 130, TextColor = agedGoldText, Font = GameService.Content.DefaultFont16 };
                        new Label { Text = strings.TimeColumnHeader, Parent = columnHeader, Location = new Point(380, 5), Width = 150, TextColor = agedGoldText, Font = GameService.Content.DefaultFont16 };
                        new Label { Text = strings.HoleColumnHeader, Parent = columnHeader, Location = new Point(540, 5), Width = 230, TextColor = agedGoldText, Font = GameService.Content.DefaultFont16 };

                        if (achievementDef.Bits != null)
                        {
                            for (int i = 0; i < achievementDef.Bits.Count; i++)
                            {
                                if (!trueMissingBits.Contains(i)) continue;
                                var idProp = achievementDef.Bits[i].GetType().GetProperty("Id");
                                if (idProp == null) continue;
                                int fishItemId = (int)idProp.GetValue(achievementDef.Bits[i]);
                                var dbFish = _allFishEntries.FirstOrDefault(x => x.Data.ItemId == fishItemId)?.Data;
                                var row = new Panel { Parent = listContainer, Width = 760, Height = 55, BackgroundColor = darkTealPanel, ShowBorder = true };

                                if (dbFish != null)
                                {
                                    string safeName = dbFish.Name.Replace(" ", "_").Replace("'", "").Replace("-", "");
                                    new Image { Texture = ContentsManager.GetTexture($"images/{safeName}.png"), Parent = row, Location = new Point(10, 7), Size = new Point(40, 40) };
                                    new Label { Text = dbFish.Name, Parent = row, Location = new Point(60, 5), Size = new Point(170, 45), WrapText = true, VerticalAlignment = VerticalAlignment.Middle, TextColor = GetRarityColor(dbFish.Rarity), Font = GameService.Content.DefaultFont14 };
                                    new Label { Text = dbFish.Bait, Parent = row, Location = new Point(240, 5), Size = new Point(130, 45), WrapText = true, VerticalAlignment = VerticalAlignment.Middle, TextColor = Color.LightGray, Font = GameService.Content.DefaultFont12 };
                                    new Label { Text = dbFish.Time, Parent = row, Location = new Point(380, 5), Size = new Point(150, 45), WrapText = true, VerticalAlignment = VerticalAlignment.Middle, TextColor = Color.White, Font = GameService.Content.DefaultFont12 };
                                    new Label { Text = dbFish.FishingHole, Parent = row, Location = new Point(540, 5), Size = new Point(210, 45), WrapText = true, VerticalAlignment = VerticalAlignment.Middle, TextColor = Color.LightGray, Font = GameService.Content.DefaultFont12 };
                                }
                            }
                        }
                    }
                }

                // --- INSTANT SWAP ---
                _achievementResultsPanel.ClearChildren();
                ghostContainer.Parent = _achievementResultsPanel;
                ghostContainer.Location = new Point(0, 0);
            }
            catch (Exception ex) { Logger.Error(ex, "Achievement Detail Panel failed."); }
        }

        private Color GetRarityColor(string rarity)
        {
            switch (rarity)
            {
                case "Legendary": return Color.DarkOrange;
                case "Ascended": return Color.Violet;
                case "Exotic": return Color.Orange;
                case "Rare": return Color.Yellow;
                case "Masterwork": return Color.LimeGreen;
                case "Fine": return Color.DeepSkyBlue;
                default: return Color.White;
            }
        }

        private Panel CreateFishIconWithBorder(FishData fish, Container parent, int size = 48, bool isMissing = true)
        {
            Color rarityColor = GetRarityColor(fish.Rarity);
            string tooltip = $"{fish.Name} [{fish.Rarity}]\nBait: {fish.Bait}\nTime: {fish.Time}\nHole: {fish.FishingHole}";

            var borderPanel = new Panel
            {
                Parent = parent,
                Size = new Point(size, size),
                BackgroundColor = rarityColor, 
                BasicTooltipText = tooltip
            };

            string safeName = fish.Name.Replace(" ", "_").Replace("'", "").Replace("-", "");
            var img = new Image
            {
                Texture = ContentsManager.GetTexture($"images/{safeName}.png"),
                Parent = borderPanel,
                Size = new Point(size - 4, size - 4),
                Location = new Point(2, 2),
                Tint = isMissing ? Color.White : Color.White * 0.4f, 
                BasicTooltipText = tooltip
            };

            // --- NEW: Add a green checkmark to the corner if the fish is caught ---
            new Image
            {
                Texture = _texCaughtCheck,
                Parent = borderPanel,
                Size = new Point(16, 16), 
             
                Location = new Point(size - 18, size - 18),
                Visible = !isMissing 
            };

            return borderPanel;
        }

        private void BuildCasualUI(Panel parent)
        {
            Color agedGoldText = new Color(201, 168, 76);


            _casualLogToggleBtn = new StandardButton { Text = strings.StartLoggingBtnText, Parent = parent, Location = new Point(10, 10), Width = 120 };
            _casualMeasureBtn = new StandardButton { Text = strings.MeasureFishBtnText, Parent = parent, Location = new Point(140, 10), Width = 120, Enabled = false };
            _useDrfCheckbox = new Checkbox
            {
                Text = strings.UseDrfCheckboxText,
                Parent = parent,
                Location = new Point(275, 15),
                BasicTooltipText = strings.UseDrfTooltip,
                Checked = !string.IsNullOrWhiteSpace(_drfToken.Value)
            };
            var compactBtn = new StandardButton { Text = strings.CompactModeBtnText, Parent = parent, Location = new Point(440, 10), Width = 120 };
            var bitingBtn = new StandardButton { Text = strings.WhatsBitingBtnText, Parent = parent, Location = new Point(440, 45), Width = 140, BasicTooltipText = strings.WhatsBitingTooltip };
            bitingBtn.Click += (s, e) => ToggleBitingWidget();

            var profitBtn = new StandardButton { Text = strings.ProfitTrackerBtnText, Parent = parent, Location = new Point(310, 45), Width = 120, BasicTooltipText = strings.ProfitTrackerTooltip };
            profitBtn.Click += (s, e) => {
                _showProfitWidget.Value = !_showProfitWidget.Value;
            };

            _casualSyncTimerLabel = new Label { Text = "05:00", Parent = parent, Location = new Point(140, 45), AutoSizeWidth = true, TextColor = Color.Yellow, Visible = false };

            new Label { Text = strings.RecentCatchesText, Parent = parent, Location = new Point(10, 60), AutoSizeWidth = true, Font = GameService.Content.DefaultFont18, TextColor = agedGoldText };


            _recentCatchesPanel = new FlowPanel
            {
                Parent = parent,
                Location = new Point(10, 85),
                Size = new Point(parent.Width - 20, parent.Height - 90),
                FlowDirection = ControlFlowDirection.LeftToRight,
                CanScroll = true,
                ControlPadding = new Vector2(10, 10) 
            };

            compactBtn.Click += (s, e) => {
                if (_isCasualLoggingActive)
                {
                    _mainWindow.Visible = false;
                    _casualCompactPanel.Visible = true;
                    UpdateCompactCooler();
                }
            };

            _casualLogToggleBtn.Click += async (s, e) => {
                if (_isCasualLoggingActive)
                {
                    StopCasualLogging();
                    _mainWindow.Visible = true;
                    ScreenNotification.ShowNotification(strings.CasualLoggingStopped);
                }
                else
                {
                    await StartCasualLogging();
                }
            };

            _casualMeasureBtn.Click += async (s, e) => {
                _casualMeasureBtn.Enabled = false;
                _casualSyncTimerLabel.Text = "strings.Measuring";
                await CheckApiForNewCatches();
                _nextSyncTime = DateTime.Now.AddMinutes(5);
                _isSyncTimerActive = true;
            };

            UpdateRecentCatchesUI();
        }

        private async Task StartCasualLogging()
        {
            if (_isCasualLoggingActive) return;

            _isCasualLoggingActive = true;
            if (_casualLogToggleBtn != null) _casualLogToggleBtn.Text = strings.StopLoggingBtnText;
            if (_useDrfCheckbox != null) _useDrfCheckbox.Enabled = false;

            
            bool useDrf = !string.IsNullOrWhiteSpace(_drfToken.Value);

            if (!useDrf)
            {
                if (_casualMeasureBtn != null) _casualMeasureBtn.Enabled = false;
                await TakeInventorySnapshot();
                _nextSyncTime = DateTime.Now.AddMinutes(5);
                _isSyncTimerActive = true;
                if (_casualSyncTimerLabel != null)
                {
                    _casualSyncTimerLabel.Text = "05:00";
                    _casualSyncTimerLabel.TextColor = Color.Yellow;
                    _casualSyncTimerLabel.Visible = true;
                }
            }
            else
            {
                if (_casualMeasureBtn != null) _casualMeasureBtn.Enabled = false;
                if (_casualSyncTimerLabel != null)
                {
                    _casualSyncTimerLabel.Text = strings.DrfActive;
                    _casualSyncTimerLabel.TextColor = Color.LimeGreen;
                    _casualSyncTimerLabel.Visible = true;
                }
                if (_useDrfCheckbox != null) _useDrfCheckbox.Checked = true; 
                _ = StartDrfListener();
            }

            _lastAutoSubmitTime = DateTime.Now;

           
            _sessionStartTime = DateTime.Now;
            _sessionTotalCopper = 0;

            UpdateRecentCatchesUI();
        }

        private void ToggleBitingWidget(bool forceOpen = false)
        {
            if (_bitingWidgetPanel == null)
            {
                
                _bitingWidgetPanel = new Panel { Parent = GameService.Graphics.SpriteScreen, Width = 320, HeightSizingMode = SizingMode.AutoSize, Location = new Point(_bitingLocX.Value, _bitingLocY.Value), BackgroundColor = new Color(13, 27, 42) * 0.9f, ShowBorder = true, Visible = false, ZIndex = 950 };

                var hBar = new Panel { Parent = _bitingWidgetPanel, Size = new Point(320, 30), BackgroundColor = Color.Black * 0.6f, Location = new Point(0, 0) };
                _bitingTitleLabel = new Label { Text = strings.BitingNowLoading, Parent = hBar, Location = new Point(10, 5), Font = GameService.Content.DefaultFont14, TextColor = new Color(201, 168, 76), AutoSizeWidth = true };

                _bitingToggleBtn = new StandardButton { Text = strings.MissingLabel, Parent = hBar, Location = new Point(hBar.Width - 95, 2), Width = 65, Height = 26, BasicTooltipText = strings.ToggleBitingTooltip };
                _bitingToggleBtn.Click += async (s, e) => {
                    _showAllBitingFish = !_showAllBitingFish;
                    _bitingToggleBtn.Text = _showAllBitingFish ? strings.AllLabel : strings.MissingLabel;
                    await RefreshBitingWidget();
                };

                var closeX = new Label { Text = "X", Parent = hBar, Location = new Point(hBar.Width - 25, 5), Font = GameService.Content.DefaultFont14, TextColor = Color.Red, AutoSizeWidth = true };
                closeX.Click += (s, e) => _bitingWidgetPanel.Visible = false;

                hBar.LeftMouseButtonPressed += (s, e) => {
                    if (GameService.Input.Mouse.ActiveControl == hBar || GameService.Input.Mouse.ActiveControl == _bitingTitleLabel)
                    {
                        _isBitingDragging = true;
                        _bitingDragOffset = new Point(GameService.Input.Mouse.Position.X - _bitingWidgetPanel.Location.X, GameService.Input.Mouse.Position.Y - _bitingWidgetPanel.Location.Y);
                    }
                };

                
                _bitingFishList = new FlowPanel { Parent = _bitingWidgetPanel, Location = new Point(10, 35), Width = 300, HeightSizingMode = SizingMode.AutoSize, CanScroll = false, FlowDirection = ControlFlowDirection.LeftToRight, ControlPadding = new Vector2(5, 5) };
            }

            _bitingWidgetPanel.Visible = forceOpen || !_bitingWidgetPanel.Visible;
            if (_bitingWidgetPanel.Visible) _ = RefreshBitingWidget();
        }

        private async Task RefreshBitingWidget()
        {
            if (_bitingWidgetPanel == null || !_bitingWidgetPanel.Visible) return;

            int currentMapId = GameService.Gw2Mumble.CurrentMap.Id;
            if (currentMapId == 0) return;

            string currentPhase = _currentTodPhase;
            if (string.IsNullOrEmpty(currentPhase)) currentPhase = "Day";

            var achievementMap = new Dictionary<string, int> {
                { "Kryta", 6068 }, { "Shiverpeak", 6179 }, { "Ascalon", 6330 }, { "Maguuma Jungle", 6344 }, { "Ruins of Orr", 6363 },
                { "Southsun Cove", 6106 }, { "Sandswept Isles", 6106 }, { "Elona", 6106 }, // --- Desert Isles Fisher explicit mapping! ---
                { "Crystal Desert", 6317 }, { "Ring of Fire", 6489 }, { "Seitung Province", 6336 }, { "New Kaineng City", 6342 },
                { "The Echovald Wilds", 6258 }, { "Dragon's End", 6506 }, { "Skywatch Archipelago", 7114 }, { "Amnytas", 7114 },
                { "Inner Nayos", 7114 }, { "Lowland Shore", 8168 }, { "Janthir Syntri", 8168 }, { "Mistburned Barrens", 8554 },
                { "Castora", 8900 }
            };

            try
            {
                var mapInfo = await Gw2ApiManager.Gw2ApiClient.V2.Maps.GetAsync(currentMapId);
                string target = achievementMap.Keys.FirstOrDefault(k => mapInfo.Name.Contains(k) || (mapInfo.RegionName != null && mapInfo.RegionName.Contains(k)));

                if (target == null) { _bitingTitleLabel.Text = strings.NoFishingZone; _bitingFishList.ClearChildren(); return; }

                _bitingTitleLabel.Text = $"{strings.BitingNowTitle}: {target} ({currentPhase})";

                int achId = achievementMap[target];
                var achievementDef = await Gw2ApiManager.Gw2ApiClient.V2.Achievements.GetAsync(achId);
                var accAchievements = await Gw2ApiManager.Gw2ApiClient.V2.Account.Achievements.GetAsync();
                var progress = accAchievements.FirstOrDefault(a => a.Id == achId);
                var completedBits = progress?.Bits ?? new List<int>();

                _bitingFishList.ClearChildren();
                int matchCount = 0;

                if (achievementDef.Bits != null)
                {
                    for (int i = 0; i < achievementDef.Bits.Count; i++)
                    {
                        var idProp = achievementDef.Bits[i].GetType().GetProperty("Id");
                        if (idProp == null) continue;
                        int fishItemId = (int)idProp.GetValue(achievementDef.Bits[i]);

                        bool isCaught = completedBits.Contains(i) || _caughtFishIds.Contains(fishItemId);

                        
                        if (!_showAllBitingFish && isCaught) continue;

                        var dbFish = _allFishEntries.FirstOrDefault(x => x.Data.ItemId == fishItemId)?.Data;
                        if (dbFish != null)
                        {
                            bool timeMatch = false;
                            string fTime = dbFish.Time.ToLower();
                            string cPhase = currentPhase.ToLower();

                            if (fTime.Contains("any")) timeMatch = true;
                            else if (cPhase == "dawn" && fTime.Contains("dawn")) timeMatch = true;
                            else if (cPhase == "day" && fTime.Contains("daytime")) timeMatch = true;
                            else if (cPhase == "dusk" && fTime.Contains("dusk")) timeMatch = true;
                            else if (cPhase == "night" && fTime.Contains("nighttime")) timeMatch = true;

                            if (timeMatch)
                            {
                                
                                CreateFishIconWithBorder(dbFish, _bitingFishList, 38, !isCaught);
                                matchCount++;
                            }
                        }
                    }
                }

                string emptyText = _showAllBitingFish ? "Nothing biting right now!" : "You caught everything biting right now!";
                if (matchCount == 0) new Label { Text = emptyText, Parent = _bitingFishList, AutoSizeWidth = true, TextColor = Color.LimeGreen };

                
                new Panel { Parent = _bitingFishList, Width = 290, Height = 5 };
            }
            catch (Exception ex)
            {
                Logger.Warn(ex, "Biting Widget failed on load. Retrying...");

                // --- SMART RETRY LOOP ---
                _bitingTitleLabel.Text = strings.BitingApiSyncing;
                await Task.Delay(2000); 
                _lastBitingMapId = 0;   
            }
        }

        private void UpdateRecentCatchesUI()
        {
            if (_recentCatchesPanel == null || !(_recentCatchesPanel is FlowPanel flowPanel)) return;
            flowPanel.ClearChildren();

            Color darkTealPanel = new Color(26, 47, 69);

            foreach (var c in _recentCatches.Take(20))
            {
                
                var card = new Panel { Parent = flowPanel, Size = new Point(100, 130), BackgroundColor = darkTealPanel, ShowBorder = true };

                Color catchColor = Color.White;
                if (c.IsSuperPb) catchColor = Color.Gold;
                else if (c.IsNewPb) catchColor = Color.DeepSkyBlue;

                var dbFish = _allFishEntries.FirstOrDefault(x => x.Data.ItemId == c.Id)?.Data;
                if (dbFish != null)
                {
                    string safeName = dbFish.Name.Replace(" ", "_").Replace("'", "").Replace("-", "");
                    new Image { Texture = ContentsManager.GetTexture($"images/{safeName}.png"), Parent = card, Size = new Point(56, 56), Location = new Point(22, 5), BasicTooltipText = c.Name };
                }

                new Label { Text = $"{c.Weight} lbs", Parent = card, Location = new Point(0, 65), Width = 100, HorizontalAlignment = HorizontalAlignment.Center, TextColor = catchColor, Font = GameService.Content.DefaultFont14 };

                
                new Label { Text = $"{c.Length} in", Parent = card, Location = new Point(0, 85), Width = 100, HorizontalAlignment = HorizontalAlignment.Center, TextColor = Color.LightGray, Font = GameService.Content.DefaultFont12 };
            }
        }

        private void RefreshFishLogUI()
        {
            foreach (var entry in _allFishEntries)
            {
                var fish = entry.Data;
                var img = entry.Icon;
                if (img == null) continue;

                bool isCaught = _caughtFishIds.Contains(fish.ItemId) || _personalBests.ContainsKey(fish.ItemId);
                string pbWText = strings.NoneLogged;
                string pbLText = strings.NoneLogged;

                var tint = isCaught ? Color.White : Color.Gray * 0.5f;

                if (_personalBests.TryGetValue(fish.ItemId, out var rec))
                {
                    if (rec.BestWeight != null)
                    {
                        if (rec.BestWeight.IsCheater) pbWText = strings.CheaterDetected;
                        else if (rec.BestWeight.IsSuperPb) { pbWText = $"[SUPER] {rec.BestWeight.Weight} lbs"; tint = Color.Gold; }
                        else pbWText = $"{rec.BestWeight.Weight} lbs";
                    }

                    if (rec.BestLength != null)
                    {
                        if (rec.BestLength.IsCheater) pbLText = strings.CheaterDetected;
                        else if (rec.BestLength.IsSuperPb) { pbLText = $"[SUPER] {rec.BestLength.Length} in"; tint = Color.Gold; }
                        else pbLText = $"{rec.BestLength.Length} in";
                    }
                }

                img.Tint = tint;

                bool isCollector = (fish.Rarity != null && fish.Rarity.Equals("Junk", StringComparison.OrdinalIgnoreCase)) ||
                                   (fish.Location != null && fish.Location.Contains("Collector"));

                // Note: Rarity, Location, etc., are currently hardcoded, but we can fix those next!
                string tooltip = $"{fish.Name}\n{strings.BaitLabel}: {fish.Bait}\n{fish.Rarity}\n{fish.Location}\n{fish.Time}";

                if (!isCollector)
                {
                    tooltip += $"\n\n{strings.PbWeightLabel}: {pbWText}\n{strings.PbLengthLabel}: {pbLText}";
                }

                img.BasicTooltipText = tooltip;
            }
        }

        private void ShowTargetSelectionWindow(StandardButton targetBtn)
        {
            if (_targetSelectionWindow != null) _targetSelectionWindow.Dispose();
            _targetSelectionWindow = new Panel { Title = "Select Target Species", Parent = GameService.Graphics.SpriteScreen, Size = new Point(400, 500), Location = new Point(450, 200), ShowBorder = true, BackgroundColor = new Color(0, 0, 0, 230), CanScroll = false };

            _targetSelectionWindow.LeftMouseButtonPressed += (s, e) => {
                if (GameService.Input.Mouse.ActiveControl == _targetSelectionWindow) { _isDraggingTarget = true; _targetDragOffset = new Point(GameService.Input.Mouse.Position.X - _targetSelectionWindow.Location.X, GameService.Input.Mouse.Position.Y - _targetSelectionWindow.Location.Y); }
            };

            var closeBtn = new StandardButton { Text = strings.CloseBtnText, Parent = _targetSelectionWindow, Location = new Point(125, 430), Width = 150 };
            closeBtn.Click += (s, e) => _targetSelectionWindow.Dispose();

            var scroll = new FlowPanel { Parent = _targetSelectionWindow, Size = new Point(380, 420), CanScroll = true, FlowDirection = ControlFlowDirection.SingleTopToBottom };

            var allBtn = new StandardButton { Text = strings.AllSpeciesFilterBtn, Parent = scroll, Width = 350 };
            allBtn.Click += (s, e) => { _tourneyTargetItemId = 0; targetBtn.Text = strings.TargetAllSpecies; _targetSelectionWindow.Dispose(); };

            var groups = _allFishEntries.GroupBy(x => x.Data.Location).OrderBy(x => x.Key);
            foreach (var g in groups)
            {
                var p = new FlowPanel { Parent = scroll, Title = g.Key, CanCollapse = true, ShowBorder = true, Width = 360, HeightSizingMode = SizingMode.AutoSize, FlowDirection = ControlFlowDirection.SingleTopToBottom, Collapsed = true };
                foreach (var fish in g.OrderBy(x => x.Data.Name))
                {
                    var fBtn = new StandardButton { Text = fish.Data.Name, Parent = p, Width = 330 };
                    fBtn.Click += (s, e) => { _tourneyTargetItemId = fish.Data.ItemId; targetBtn.Text = strings.TargetPrefix + fish.Data.Name; _targetSelectionWindow.Dispose(); };
                }
            }
        }
        private void BuildTournamentUI(Panel parent)
        {
            Color darkTealPanel = new Color(26, 47, 69);
            Color agedGoldText = new Color(201, 168, 76);

            var topNav = new Panel { Parent = parent, Size = new Point(parent.Width, 40), Location = new Point(0, 5) };
            var hostModeBtn = new StandardButton { Text = strings.HostTournamentBtnText, Parent = topNav, Location = new Point(10, 0), Width = 150 };
            var partModeBtn = new StandardButton { Text = strings.JoinTournamentBtnText, Parent = topNav, Location = new Point(170, 0), Width = 150 };

            _tourneyHostPanel = new Panel { Parent = parent, Location = new Point(0, 45), Size = new Point(parent.Width, parent.Height - 45), Visible = true };
            _tourneyParticipantPanel = new Panel { Parent = parent, Location = new Point(0, 45), Size = new Point(parent.Width, parent.Height - 45), Visible = false };

            hostModeBtn.Click += (s, e) => { _tourneyHostPanel.Visible = true; _tourneyParticipantPanel.Visible = false; };
            partModeBtn.Click += (s, e) => { _tourneyHostPanel.Visible = false; _tourneyParticipantPanel.Visible = true; };


            // --- HOST PANEL SETTINGS ---
            var hostSettingsBg = new Panel { Parent = _tourneyHostPanel, Location = new Point(10, 5), Size = new Point(550, 175), BackgroundColor = darkTealPanel, ShowBorder = true };
            new Label { Text = strings.HostSetupTitle, Parent = hostSettingsBg, Location = new Point(10, 5), AutoSizeWidth = true, TextColor = agedGoldText, Font = GameService.Content.DefaultFont16 };

            new Label { Text = strings.StartDelayLabel, Parent = hostSettingsBg, Location = new Point(10, 35), AutoSizeWidth = true, TextColor = Color.LightGray };
            var hostStartDelayDrop = new Dropdown { Parent = hostSettingsBg, Location = new Point(10, 55), Width = 130 };
            hostStartDelayDrop.Items.Add("Start Immediately"); hostStartDelayDrop.Items.Add("2 Minutes"); hostStartDelayDrop.Items.Add("5 Minutes"); hostStartDelayDrop.Items.Add("10 Minutes");

            new Label { Text = strings.DurationLabel, Parent = hostSettingsBg, Location = new Point(160, 35), AutoSizeWidth = true, TextColor = Color.LightGray };
            var hostTimerMin = new TextBox { Parent = hostSettingsBg, Location = new Point(160, 55), Width = 100, Text = "30" };

            new Label { Text = strings.TrackingModeLabel, Parent = hostSettingsBg, Location = new Point(280, 35), AutoSizeWidth = true, TextColor = Color.LightGray };
            var hostTrackingModeDrop = new Dropdown { Parent = hostSettingsBg, Location = new Point(280, 55), Width = 160 };
            hostTrackingModeDrop.Items.Add("API (5-Min Wait)"); hostTrackingModeDrop.Items.Add("DRF (Real-Time)"); hostTrackingModeDrop.SelectedItem = "DRF (Real-Time)";

            new Label { Text = strings.TargetSpeciesLabel, Parent = hostSettingsBg, Location = new Point(10, 95), AutoSizeWidth = true, TextColor = Color.LightGray };
            var targetSpeciesBtn = new StandardButton { Text = strings.TargetAllSpecies, Parent = hostSettingsBg, Location = new Point(10, 115), Width = 200 };
            targetSpeciesBtn.Click += (s, e) => { ShowTargetSelectionWindow(targetSpeciesBtn); };

            new Label { Text = strings.WinFactorLabel, Parent = hostSettingsBg, Location = new Point(230, 95), AutoSizeWidth = true, TextColor = Color.LightGray };
            _hostWinFactorDrop = new Dropdown { Parent = hostSettingsBg, Location = new Point(230, 115), Width = 130 };
            _hostWinFactorDrop.Items.Add(strings.WeightDropdown);
            _hostWinFactorDrop.Items.Add(strings.LengthDropdown);
            _hostWinFactorDrop.SelectedItem = strings.WeightDropdown;

            var genKeyBtn = new StandardButton { Text = strings.CreateRoomBtn, Parent = hostSettingsBg, Location = new Point(380, 110), Width = 150, Height = 35 };


            // --- MANUAL VERIFICATION & INFO ---
            var backupBg = new Panel { Parent = _tourneyHostPanel, Location = new Point(10, 185), Size = new Point(550, 230), BackgroundColor = Color.Black * 0.4f, ShowBorder = true };
            new Label { Text = strings.ManualVerifyTitle, Parent = backupBg, Location = new Point(10, 5), AutoSizeWidth = true, TextColor = agedGoldText };

            var verifyInput = new TextBox { Parent = backupBg, Location = new Point(10, 30), Width = 250, PlaceholderText = strings.PasteCodePlaceholder };
            var verifyBtn = new StandardButton { Text = strings.VerifyCodeBtn, Parent = backupBg, Location = new Point(270, 30), Width = 120 };

            new Label
            {
                Parent = backupBg,
                Location = new Point(10, 70),
                Width = 530,
                Height = 150,
                WrapText = true,
                TextColor = Color.LightGray,
                Text = strings.TrackingModesInfo
            };


            // --- PARTICIPANT PANEL SETTINGS ---
            var partSettingsBg = new Panel { Parent = _tourneyParticipantPanel, Location = new Point(10, 10), Size = new Point(550, 140), BackgroundColor = darkTealPanel, ShowBorder = true };
            new Label { Text = strings.JoinRoomTitle, Parent = partSettingsBg, Location = new Point(10, 5), AutoSizeWidth = true, TextColor = agedGoldText, Font = GameService.Content.DefaultFont16 };

            new Label { Text = strings.EnterRoomCodeLabel, Parent = partSettingsBg, Location = new Point(10, 40), AutoSizeWidth = true, TextColor = Color.LightGray };
            var partSessionKey = new TextBox { Parent = partSettingsBg, Location = new Point(10, 65), Width = 180, PlaceholderText = "e.g. GW-1234" };
            var joinBtn = new StandardButton { Text = strings.JoinRoomBtn, Parent = partSettingsBg, Location = new Point(200, 65), Width = 150, Height = 32 };


            genKeyBtn.Click += async (s, e) => {
                genKeyBtn.Enabled = false; genKeyBtn.Text = strings.Creating;
                string charName = GameService.Gw2Mumble.PlayerCharacter.Name;
                if (string.IsNullOrEmpty(charName)) charName = "Host";
                int delayMins = 0;
                if (hostStartDelayDrop.SelectedItem == "2 Minutes") delayMins = 2;
                if (hostStartDelayDrop.SelectedItem == "5 Minutes") delayMins = 5;
                if (hostStartDelayDrop.SelectedItem == "10 Minutes") delayMins = 10;
                // --- Translate the Win Factor back to English for the API ---
                string mode = hostTrackingModeDrop.SelectedItem.Contains("API") ? "API" : "DRF";
                string apiWinFactor = _hostWinFactorDrop.SelectedItem == strings.LengthDropdown ? "Length" : "Weight";

                var payload = new { hostName = charName, startDelayMins = delayMins, durationMins = int.Parse(hostTimerMin.Text), mode = mode, targetId = _tourneyTargetItemId, winFactor = apiWinFactor, webhookUrl = _discordWebhookUrl.Value };
                try
                {
                    string jsonPayload = JsonConvert.SerializeObject(payload);
                    var content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");
                    var response = await _httpClient.PostAsync($"{API_BASE_URL}/create", content);
                    var resultString = await response.Content.ReadAsStringAsync();
                    if (response.IsSuccessStatusCode)
                    {
                        var resultObj = JsonConvert.DeserializeObject<Dictionary<string, string>>(resultString);
                        if (resultObj != null && resultObj.ContainsKey("roomCode"))
                        {
                            string code = resultObj["roomCode"]; CopyToClipboard(code); ScreenNotification.ShowNotification(string.Format(strings.RoomCreated, code));
                        }
                    }
                    else { ScreenNotification.ShowNotification("strings.ApiErrorRoom", ScreenNotification.NotificationType.Error); }
                }
                catch (Exception ex) { Logger.Error(ex, "Failed to connect to API."); ScreenNotification.ShowNotification("Network Error: Could not connect to API.", ScreenNotification.NotificationType.Error); }
                genKeyBtn.Enabled = true; genKeyBtn.Text = strings.CreateRoomBtn;
            };

            verifyBtn.Click += (s, e) => {
                try
                {
                    string decoded = Encoding.UTF8.GetString(Convert.FromBase64String(verifyInput.Text));
                    int lastPipeIndex = decoded.LastIndexOf('|');
                    if (lastPipeIndex == -1) throw new Exception();
                    string rawPayload = decoded.Substring(0, lastPipeIndex);
                    string providedSig = decoded.Substring(lastPipeIndex + 1);
                    string[] payloadParts = rawPayload.Split('|');
                    string roomCode = payloadParts[0];
                    string expectedSig = GenerateMasterSignature(rawPayload, roomCode);
                    if (providedSig != expectedSig) { ShowTournamentSummary("Error", "TAMPERED RESULTS\nSignatures do not match.", Color.Red); return; }
                    string charName = payloadParts[1];
                    List<TournamentCatch> reconstructedCatches = new List<TournamentCatch>();
                    for (int i = 2; i < payloadParts.Length; i++)
                    {
                        string[] fishData = payloadParts[i].Split(',');
                        int fId = int.Parse(fishData[0]); double fWt = double.Parse(fishData[1]); double fLen = double.Parse(fishData[2]);
                        var dbFish = _allFishEntries.FirstOrDefault(x => x.Data.ItemId == fId)?.Data;
                        if (dbFish != null) { reconstructedCatches.Add(new TournamentCatch { Id = fId, Name = dbFish.Name, Weight = fWt, Length = fLen, Rarity = dbFish.Rarity, CharacterName = charName }); }
                    }
                    string currentHostWinFactor = _hostWinFactorDrop.SelectedItem;
                    ShowTournamentSummary("Verified Results", null, Color.LimeGreen, reconstructedCatches, currentHostWinFactor);
                }
                catch { ShowTournamentSummary("Error", "INVALID DATA\nEnsure the player copied the entire string.", Color.Red); }
            };

            joinBtn.Click += async (s, e) => {
                joinBtn.Enabled = false; joinBtn.Text = strings.Joining;
                try
                {
                    string roomCode = partSessionKey.Text.Trim().ToUpper();
                    if (!roomCode.StartsWith("GW-") || roomCode.Length != 8) { ScreenNotification.ShowNotification(strings.InvalidRoomCode, ScreenNotification.NotificationType.Error); joinBtn.Enabled = true; joinBtn.Text = strings.JoinRoomBtn; return; }
                    var response = await _httpClient.GetAsync($"{API_BASE_URL}/join/{roomCode}");
                    string resultString = await response.Content.ReadAsStringAsync();
                    if (response.IsSuccessStatusCode)
                    {
                        var tData = JsonConvert.DeserializeObject<Dictionary<string, object>>(resultString);

                        // --- SAFETY NET: HARD STOP LOGGING ---
                        StopCasualLogging();
                        StopDrfListener();

                        _tourneyRoomCode = roomCode; _tourneyModeUsed = tData["mode"].ToString(); _tourneyTargetItemId = Convert.ToInt32(tData["targetId"]); _tourneyWinFactor = tData["winFactor"].ToString();
                        int durMins = Convert.ToInt32(tData["durationMins"]); long startTimeMs = Convert.ToInt64(tData["startTime"]);
                        _tourneyStartTimeUtc = DateTimeOffset.FromUnixTimeMilliseconds(startTimeMs).UtcDateTime; _tourneyEndTimeUtc = _tourneyStartTimeUtc.AddMinutes(durMins);
                        _tourneyCatches.Clear();

                        string targetName = strings.AllSpeciesFilterBtn;
                        if (_tourneyTargetItemId != 0) { var targetFish = _allFishEntries.FirstOrDefault(x => x.Data.ItemId == _tourneyTargetItemId); if (targetFish != null) targetName = targetFish.Data.Name; }
                        if (_tourneyActivePanel != null) _tourneyActivePanel.Title = $"{targetName} ({_tourneyWinFactor})";

                        UpdateActiveTourneyCoolerUI();

                        if (DateTime.UtcNow < _tourneyStartTimeUtc)
                        {
                            _isTourneyWaitingRoom = true;
                            _waitingRoomLabel.Visible = true;
                            _activeTimerLabel.Visible = false;
                            _activeMeasureBtn.Enabled = false;

                            // --- WAITING ROOM UI OVERHAUL ---
                            if (_activeSyncTimerLabel != null)
                            {
                                _activeSyncTimerLabel.Text = _tourneyModeUsed == "DRF" ? strings.DrfPaused : strings.ApiPaused;
                                _activeSyncTimerLabel.TextColor = Color.LightGray;
                                _activeSyncTimerLabel.Visible = true;
                            }
                            ScreenNotification.ShowNotification(strings.DrfResumeWait, ScreenNotification.NotificationType.Warning);
                        }
                        else
                        {
                            _isTourneyWaitingRoom = false;
                            _isTournamentActive = true;
                            _waitingRoomLabel.Visible = false;
                            _activeTimerLabel.Visible = true;
                            StartTrackingMode();
                            ScreenNotification.ShowNotification(strings.TourneyStarted);
                        }

                        _mainWindow.Visible = false; _tourneyActivePanel.Visible = true;
                    }
                    else { ScreenNotification.ShowNotification(strings.RoomNotFound, ScreenNotification.NotificationType.Error); }
                }
                catch (Exception ex) { Logger.Error(ex, "API Join Failed"); ScreenNotification.ShowNotification(strings.NetworkError, ScreenNotification.NotificationType.Error); }
                joinBtn.Enabled = true; joinBtn.Text = strings.JoinRoomBtn;
            };
        }

        private async void StartTrackingMode()
        {
            if (_tourneyModeUsed == "API")
            {
                await TakeInventorySnapshot();
                _activeMeasureBtn.Enabled = false;
                _nextSyncTime = DateTime.Now.AddMinutes(5);
                _isSyncTimerActive = true;
                _activeSyncTimerLabel.TextColor = Color.Yellow;
                _activeSyncTimerLabel.Visible = true;
            }
            else
            {
                _activeMeasureBtn.Enabled = false;
                _activeSyncTimerLabel.Text = "DRF Active";
                _activeSyncTimerLabel.TextColor = Color.LimeGreen;
                _activeSyncTimerLabel.Visible = true;
                _ = StartDrfListener();
            }
        }

        private void BuildActiveTournamentWidget()
        {
            _tourneyActivePanel = new Panel
            {
                Title = "Active Tournament",
                Parent = GameService.Graphics.SpriteScreen,
                Size = new Point(320, 350),
                Location = new Point(400, 300),
                ShowBorder = true,
                BackgroundColor = new Color(13, 27, 42),
                Visible = false
            };

            _tourneyActivePanel.LeftMouseButtonPressed += (s, ev) => {
                if (GameService.Input.Mouse.ActiveControl == _tourneyActivePanel) { _isActivePanelDragging = true; _activePanelDragOffset = new Point(GameService.Input.Mouse.Position.X - _tourneyActivePanel.Location.X, GameService.Input.Mouse.Position.Y - _tourneyActivePanel.Location.Y); }
            };

            _activeTimerLabel = new Label { Text = "00:00", Parent = _tourneyActivePanel, Location = new Point(10, 10), Font = GameService.Content.DefaultFont32, TextColor = Color.White, AutoSizeWidth = true, Visible = false };
            _waitingRoomLabel = new Label { Text = strings.StartingIn, Parent = _tourneyActivePanel, Location = new Point(10, 10), Font = GameService.Content.DefaultFont18, TextColor = Color.Yellow, AutoSizeWidth = true, Visible = false };

            _activeSyncTimerLabel = new Label { Text = "05:00", Parent = _tourneyActivePanel, Location = new Point(190, 85), AutoSizeWidth = true, TextColor = Color.Yellow, Visible = false };

            _activeMeasureBtn = new StandardButton { Text = strings.MeasureFishBtnText, Parent = _tourneyActivePanel, Location = new Point(180, 10), Width = 110, Enabled = false };

            _activeEndBtn = new StandardButton { Text = strings.EndSubmitBtn, Parent = _tourneyActivePanel, Location = new Point(10, 50), Width = 120 };

            _activeRecopyBtn = new StandardButton { Text = strings.ReCopyCodeBtn, Parent = _tourneyActivePanel, Location = new Point(10, 50), Width = 120, Visible = false, BasicTooltipText = strings.ReCopyCodeTooltip };
            _activeExitBtn = new StandardButton { Text = strings.ExitTourneyBtn, Parent = _tourneyActivePanel, Location = new Point(140, 50), Width = 120, Visible = false, BasicTooltipText = strings.ExitTourneyTooltip };

            new Label { Text = strings.Top5CatchesLabel, Parent = _tourneyActivePanel, Location = new Point(10, 85), AutoSizeWidth = true, TextColor = Color.Cyan };
            _activeCoolerList = new FlowPanel { Parent = _tourneyActivePanel, Location = new Point(10, 110), Size = new Point(290, 230), CanScroll = true, FlowDirection = ControlFlowDirection.SingleTopToBottom };

            _activeMeasureBtn.Click += async (s, e) => {
                _activeMeasureBtn.Enabled = false;
                _activeSyncTimerLabel.Text = strings.Measuring;
                await CheckApiForNewCatches();
                _nextSyncTime = DateTime.Now.AddMinutes(5);
                _isSyncTimerActive = true;
            };

            _activeEndBtn.Click += async (s, e) => await CompleteTournamentAsync(strings.TourneyEndedManualTitle, strings.TourneyResultsInitial);

            _activeRecopyBtn.Click += (s, e) => {
                if (!string.IsNullOrEmpty(_lastGeneratedCode))
                {
                    CopyToClipboard(_lastGeneratedCode);
                    ScreenNotification.ShowNotification(strings.BackupCopied);
                }
            };

            _activeExitBtn.Click += (s, e) => {
                StopDrfListener();
                StopCasualLogging(); // Guarantee a clean slate before auto-resuming

                // --- Fully kill all tournament background states! ---
                _isTournamentActive = false;
                _isTourneyWaitingRoom = false;
                _isTourneyWrapUpActive = false;
                _isSyncTimerActive = false;

                _lastGeneratedCode = "";
                _tourneyRoomCode = "";
                _tourneyCatches.Clear();
                _activeCoolerList.ClearChildren();

                _activeRecopyBtn.Visible = false;
                _activeExitBtn.Visible = false;

                _activeEndBtn.Visible = true;
                _activeMeasureBtn.Visible = true;
                _activeMeasureBtn.Enabled = false;

                _waitingRoomLabel.Visible = false;
                _activeTimerLabel.Visible = false;

                _tourneyActivePanel.Visible = false;
                _mainWindow.Visible = true;
                ScreenNotification.ShowNotification(strings.ExitedTournament, ScreenNotification.NotificationType.Info);
                _ = StartCasualLogging();
            };
        }

        private void BuildCasualCompactPanel()
        {
            Color deepNavyBg = new Color(13, 27, 42);
            Color agedGoldText = new Color(201, 168, 76);

            _casualCompactPanel = new Panel
            {
                Parent = GameService.Graphics.SpriteScreen,
                Size = new Point(320, 240),
                Location = new Point(400, 300),
                ShowBorder = true,
                BackgroundColor = deepNavyBg,
                Visible = false,
                ZIndex = 1001
            };

            var headerBar = new Panel { Parent = _casualCompactPanel, Size = new Point(_casualCompactPanel.Width, 30), Location = new Point(0, 0), BackgroundColor = Color.Black * 0.6f };
            var titleLabel = new Label { Text = strings.CasualFishingLabel, Parent = headerBar, Location = new Point(10, 5), Font = GameService.Content.DefaultFont16, TextColor = agedGoldText, AutoSizeWidth = true };

            headerBar.LeftMouseButtonPressed += (s, ev) => {
                _isCompactDragging = true;
                _compactDragOffset = new Point(GameService.Input.Mouse.Position.X - _casualCompactPanel.Location.X, GameService.Input.Mouse.Position.Y - _casualCompactPanel.Location.Y);
            };

            new Label { Text = strings.RecentCatchesCompact, Parent = _casualCompactPanel, Location = new Point(10, 40), AutoSizeWidth = true, Font = GameService.Content.DefaultFont14, TextColor = Color.LightGray };

            _compactCoolerList = new FlowPanel { Parent = _casualCompactPanel, Location = new Point(10, 65), Size = new Point(300, 130), FlowDirection = ControlFlowDirection.SingleTopToBottom };

            _compactMaxBtn = new StandardButton { Text = strings.MaximizeUI, Parent = _casualCompactPanel, Location = new Point(100, 200), Width = 120 };

            _compactMaxBtn.Click += (s, e) => {
                _casualCompactPanel.Visible = false;
                _mainWindow.Visible = true;
            };
        }



        private void UpdateCompactCooler()
        {
            if (_compactCoolerList == null) return;
            _compactCoolerList.ClearChildren();
            foreach (var c in _recentCatches.Take(5))
            {
                Color catchColor = Color.White;
                if (c.IsSuperPb) catchColor = Color.Gold;
                else if (c.IsNewPb) catchColor = Color.DeepSkyBlue;

                new Label
                {
                    Text = $"{c.Name} - {c.Weight} lbs | {c.Length} in",
                    Parent = _compactCoolerList,
                    AutoSizeWidth = true,
                    TextColor = catchColor,
                    Font = GameService.Content.DefaultFont18
                };
            }
        }

        private async Task CompleteTournamentAsync(string title, string msg)
        {
            
            _isTournamentActive = false;
            _isTourneyWaitingRoom = false;
            _isTourneyWrapUpActive = false;
            if (_activeMeasureBtn != null) _activeMeasureBtn.Enabled = false;
            _isSyncTimerActive = false;

            StopDrfListener();
            StopCasualLogging(); 

            var sortedCatches = _tourneyWinFactor == "Length"
                ? _tourneyCatches.OrderByDescending(x => x.Length)
                : _tourneyCatches.OrderByDescending(x => x.Weight);

            var top5 = sortedCatches.Take(5).ToList();
            List<string> parts = new List<string>();

            string charName = GameService.Gw2Mumble.PlayerCharacter.Name;
            if (string.IsNullOrEmpty(charName)) charName = "UnknownPlayer";

            parts.Add(_tourneyRoomCode);
            parts.Add(charName);

            var catchPayloadList = new List<object>();

            foreach (var c in top5)
            {
                parts.Add($"{c.Id},{c.Weight},{c.Length}");
                catchPayloadList.Add(new { id = c.Id, name = c.Name, weight = c.Weight, length = c.Length });
            }

            string rawPayload = string.Join("|", parts);
            string masterSig = GenerateMasterSignature(rawPayload, _tourneyRoomCode);

            _lastGeneratedCode = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{rawPayload}|{masterSig}"));

            try
            {
                string accountName = "UnknownAccount";
                if (Gw2ApiManager.HasPermissions(new[] { Gw2Sharp.WebApi.V2.Models.TokenPermission.Account }))
                {
                    var acc = await Gw2ApiManager.Gw2ApiClient.V2.Account.GetAsync();
                    accountName = acc.Name;
                }

                var payload = new
                {
                    accountName = accountName,
                    playerName = charName,
                    verifyCode = _lastGeneratedCode,
                    catches = catchPayloadList
                };

                string jsonPayload = JsonConvert.SerializeObject(payload);
                var content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");

                var response = await _httpClient.PostAsync($"{API_BASE_URL}/submit/{_tourneyRoomCode}", content);
                if (response.IsSuccessStatusCode)
                {
                    msg = strings.TourneyPostedDiscord;
                    ScreenNotification.ShowNotification(strings.ResultsSubmitted);
                }
                else
                {
                    msg = strings.TourneyApiFailed;
                    CopyToClipboard(_lastGeneratedCode);
                }
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Failed to submit results.");
                msg = strings.TourneyNetworkError;
                CopyToClipboard(_lastGeneratedCode);
            }

            if (_activeEndBtn != null) _activeEndBtn.Visible = false;
            if (_activeMeasureBtn != null) _activeMeasureBtn.Visible = false;

            if (_activeRecopyBtn != null) _activeRecopyBtn.Visible = true;
            if (_activeExitBtn != null) _activeExitBtn.Visible = true;

            ShowTournamentSummary(title, msg, Color.Cyan, _tourneyCatches, _tourneyWinFactor);

            
            ScreenNotification.ShowNotification(strings.TourneyComplete, ScreenNotification.NotificationType.Warning);
            _ = StartCasualLogging();
        }


        private void UpdateActiveTourneyCoolerUI()
        {
            if (_activeCoolerList == null) return;
            _activeCoolerList.ClearChildren();

            var sorted = _tourneyWinFactor == "Length"
                ? _tourneyCatches.OrderByDescending(x => x.Length)
                : _tourneyCatches.OrderByDescending(x => x.Weight);

            foreach (var c in sorted.Take(5))
            {
                Color catchColor = Color.White;
                if (c.IsSuperPb) catchColor = Color.Gold;
                else if (c.IsNewPb) catchColor = Color.DeepSkyBlue;

                // --- BOOM: 65 pixels tall. No more clipping! ---
                var row = new Panel { Parent = _activeCoolerList, Size = new Point(270, 65), BackgroundColor = Color.Black * 0.4f, ShowBorder = true };

                var dbFish = _allFishEntries.FirstOrDefault(x => x.Data.ItemId == c.Id)?.Data;
                if (dbFish != null)
                {
                    string safeName = dbFish.Name.Replace(" ", "_").Replace("'", "").Replace("-", "");
                    // Centered icon in the new 65px row
                    new Image { Texture = ContentsManager.GetTexture($"images/{safeName}.png"), Parent = row, Location = new Point(5, 16), Size = new Point(32, 32) };
                }

                string statText = _tourneyWinFactor == "Length"
                    ? $"{c.Length} in | {c.Weight} lbs"
                    : $"{c.Weight} lbs | {c.Length} in";

                
                new Label { Text = $"{c.Name}", Parent = row, Location = new Point(45, 8), AutoSizeWidth = true, AutoSizeHeight = true, TextColor = catchColor, Font = GameService.Content.DefaultFont14 };
                new Label { Text = statText, Parent = row, Location = new Point(45, 34), AutoSizeWidth = true, AutoSizeHeight = true, TextColor = Color.LightGray, Font = GameService.Content.DefaultFont12 };
            }
        }

        private void ShowTournamentSummary(string title, string errorMsg, Color ColorTheme, List<TournamentCatch> catches = null, string winFactor = "Weight")
        {
            Color deepNavyBg = new Color(13, 27, 42);
            Color darkTealPanel = new Color(26, 47, 69);
            Color agedGoldText = new Color(201, 168, 76);

            if (_currentSummaryWindow != null) _currentSummaryWindow.Dispose();

            _currentSummaryWindow = new Panel
            {
                Parent = GameService.Graphics.SpriteScreen,
                Size = new Point(450, 480),
                Location = new Point(500, 300),
                ShowBorder = true,
                BackgroundColor = deepNavyBg,
                ClipsBounds = false
            };

            
            var hBar = new Panel { Parent = _currentSummaryWindow, Size = new Point(_currentSummaryWindow.Width, 30), BackgroundColor = Color.Black * 0.6f, Location = new Point(0, 0) };
            new Label { Text = title, Parent = hBar, Location = new Point(10, 5), Font = GameService.Content.DefaultFont16, TextColor = agedGoldText, AutoSizeWidth = true };

            var closeX = new Label { Text = "X", Parent = hBar, Location = new Point(hBar.Width - 25, 5), Font = GameService.Content.DefaultFont16, TextColor = Color.Red, AutoSizeWidth = true };
            closeX.Click += (s, e) => _currentSummaryWindow.Dispose();
            closeX.MouseEntered += (s, e) => closeX.TextColor = Color.White;
            closeX.MouseLeft += (s, e) => closeX.TextColor = Color.Red;

            hBar.LeftMouseButtonPressed += (s, e) => {
                _isDraggingSummary = true;
                _summaryDragOffset = new Point(GameService.Input.Mouse.Position.X - _currentSummaryWindow.Location.X, GameService.Input.Mouse.Position.Y - _currentSummaryWindow.Location.Y);
            };

            var list = new FlowPanel { Parent = _currentSummaryWindow, Location = new Point(20, 45), Size = new Point(410, 380), CanScroll = true, FlowDirection = ControlFlowDirection.SingleTopToBottom, ControlPadding = new Vector2(0, 5) };

            if (!string.IsNullOrEmpty(errorMsg))
            {
                new Label { Text = errorMsg, Parent = list, AutoSizeHeight = true, WrapText = true, Width = 380, TextColor = ColorTheme, Font = GameService.Content.DefaultFont18 };
            }

            if (catches != null && catches.Count > 0)
            {
                new Label { Text = $"Angler: {catches.FirstOrDefault()?.CharacterName ?? GameService.Gw2Mumble.PlayerCharacter.Name}", Parent = list, AutoSizeWidth = true, Font = GameService.Content.DefaultFont18, TextColor = agedGoldText };

                var sorted = winFactor == "Length" ? catches.OrderByDescending(x => x.Length) : catches.OrderByDescending(x => x.Weight);

                foreach (var c in sorted.Take(5))
                {
                    
                    var row = new Panel { Parent = list, Size = new Point(380, 65), BackgroundColor = darkTealPanel, ShowBorder = true };

                    var dbFish = _allFishEntries.FirstOrDefault(x => x.Data.ItemId == c.Id)?.Data;
                    if (dbFish != null)
                    {
                        string safeName = dbFish.Name.Replace(" ", "_").Replace("'", "").Replace("-", "");
                        new Image { Texture = ContentsManager.GetTexture($"images/{safeName}.png"), Parent = row, Location = new Point(5, 16), Size = new Point(32, 32) };
                    }

                    string statText = winFactor == "Length" ? $"{c.Length} in | {c.Weight} lbs" : $"{c.Weight} lbs | {c.Length} in";

                    
                    new Label { Text = $"[{c.Rarity}] {c.Name}", Parent = row, Location = new Point(45, 8), AutoSizeWidth = true, AutoSizeHeight = true, TextColor = Color.White, Font = GameService.Content.DefaultFont14 };
                    new Label { Text = statText, Parent = row, Location = new Point(45, 34), AutoSizeWidth = true, AutoSizeHeight = true, TextColor = Color.LightGray, Font = GameService.Content.DefaultFont12 };
                }
            }

            var closeBtn = new StandardButton { Text = strings.CloseBtnText, Parent = _currentSummaryWindow, Location = new Point(150, 435), Width = 150 };
            closeBtn.Click += (s, e) => _currentSummaryWindow.Dispose();
        }

        protected override void Update(GameTime gt)
        {
            
            if (_isProfitDragging && _profitWidgetPanel != null)
            {
                _profitWidgetPanel.Location = new Point(GameService.Input.Mouse.Position.X - _profitDragOffset.X, GameService.Input.Mouse.Position.Y - _profitDragOffset.Y);
            }

            
            if (_isCasualLoggingActive && _profitWidgetPanel != null && _profitWidgetPanel.Visible && _sessionStartTime != DateTime.MinValue)
            {
                _profitTotalLabel.Text = $"Total: {FormatMoney(_sessionTotalCopper)}";

                double elapsedHours = (DateTime.Now - _sessionStartTime).TotalHours;
                if (elapsedHours > 0.0005)
                {
                    int gphCopper = (int)(_sessionTotalCopper / elapsedHours);
                    _gphLabel.Text = $"GpH: {FormatMoney(gphCopper)}";
                }
            }

            if (_isBitingDragging && _bitingWidgetPanel != null)
            {
                _bitingWidgetPanel.Location = new Point(GameService.Input.Mouse.Position.X - _bitingDragOffset.X, GameService.Input.Mouse.Position.Y - _bitingDragOffset.Y);
            }

            
            if (GameService.Gw2Mumble.CurrentMap.Id != _lastBitingMapId)
            {
                _lastBitingMapId = GameService.Gw2Mumble.CurrentMap.Id;
                _ = RefreshBitingWidget();
            }

            if (_isCasualLoggingActive && _useDrfCheckbox != null && _useDrfCheckbox.Checked)
            {
                if ((DateTime.Now - _lastAutoSubmitTime).TotalMinutes >= 15)
                {
                    _lastAutoSubmitTime = DateTime.Now;
                    _ = ForceUploadPB(true);
                }
            }



            if (_isDragging && _mainWindow != null)
            {
                _mainWindow.Location = new Point(GameService.Input.Mouse.Position.X - _dragOffset.X, GameService.Input.Mouse.Position.Y - _dragOffset.Y);
            }
            if (_isActivePanelDragging && _tourneyActivePanel != null)
            {
                _tourneyActivePanel.Location = new Point(GameService.Input.Mouse.Position.X - _activePanelDragOffset.X, GameService.Input.Mouse.Position.Y - _activePanelDragOffset.Y);
            }
            if (_isDraggingSummary && _currentSummaryWindow != null)
            {
                _currentSummaryWindow.Location = new Point(GameService.Input.Mouse.Position.X - _summaryDragOffset.X, GameService.Input.Mouse.Position.Y - _summaryDragOffset.Y);
            }
            if (_isCompactDragging && _casualCompactPanel != null)
            {
                _casualCompactPanel.Location = new Point(GameService.Input.Mouse.Position.X - _compactDragOffset.X, GameService.Input.Mouse.Position.Y - _compactDragOffset.Y);
            }
            if (_isDraggingTarget && _targetSelectionWindow != null)
            {
                _targetSelectionWindow.Location = new Point(GameService.Input.Mouse.Position.X - _targetDragOffset.X, GameService.Input.Mouse.Position.Y - _targetDragOffset.Y);
            }
            if (_isDraggingLeaderboard && _leaderboardWindow != null)
            {
                _leaderboardWindow.Location = new Point(GameService.Input.Mouse.Position.X - _leaderboardDragOffset.X, GameService.Input.Mouse.Position.Y - _leaderboardDragOffset.Y);
            }
            if (_isSpeciesSelectionDragging && _speciesSelectionWindow != null)
            {
                _speciesSelectionWindow.Location = new Point(GameService.Input.Mouse.Position.X - _speciesSelectionDragOffset.X, GameService.Input.Mouse.Position.Y - _speciesSelectionDragOffset.Y);
            }
            if (_isDraggingAchievement && _achievementResultsPanel != null)
            {
                _achievementResultsPanel.Location = new Point(
                    GameService.Input.Mouse.Position.X - _achievementDragOffset.X,
                    GameService.Input.Mouse.Position.Y - _achievementDragOffset.Y
                );
                if (_achievementLegendPanel != null)
                {
                    _achievementLegendPanel.Location = new Point(
                        _achievementResultsPanel.Location.X,
                        _achievementResultsPanel.Location.Y + _achievementResultsPanel.Height + 2
                    );
                }
            }
            if (_isMetaDragging && _metaProgressWindow != null)
            {
                _metaProgressWindow.Location = new Point(
                    GameService.Input.Mouse.Position.X - _metaDragOffset.X,
                    GameService.Input.Mouse.Position.Y - _metaDragOffset.Y
                );
            }

            if (_isTodDragging && _timeOfDayPanel != null)
            {
                _timeOfDayPanel.Location = new Point(GameService.Input.Mouse.Position.X - _todDragOffset.X, GameService.Input.Mouse.Position.Y - _todDragOffset.Y);
            }

            if (_timeOfDayPanel != null && _timeOfDayPanel.Visible)
            {
                double currentCycle = DateTime.UtcNow.TimeOfDay.TotalMinutes % 120;
                string phase = "";
                double remainingMinutes = 0;
                Blish_HUD.Content.AsyncTexture2D targetTex = null;
                Color phaseColor = Color.White;

                if (currentCycle >= 25 && currentCycle < 30)
                {
                    phase = "Dawn";
                    remainingMinutes = 30 - currentCycle;
                    targetTex = _texDawn;
                    phaseColor = new Color(255, 200, 150);
                }
                else if (currentCycle >= 30 && currentCycle < 100)
                {
                    phase = "Day";
                    remainingMinutes = 100 - currentCycle;
                    targetTex = _texDay;
                    phaseColor = Color.LightSkyBlue;
                }
                else if (currentCycle >= 100 && currentCycle < 105)
                {
                    phase = "Dusk";
                    remainingMinutes = 105 - currentCycle;
                    targetTex = _texDusk;
                    phaseColor = Color.Orange;
                }
                else
                {
                    phase = "Night";
                    if (currentCycle >= 105)
                    {
                        remainingMinutes = 145 - currentCycle;
                    }
                    else
                    {
                        remainingMinutes = 25 - currentCycle;
                    }
                    targetTex = _texNight;
                    phaseColor = new Color(220, 190, 255);
                }

                TimeSpan timeRemaining = TimeSpan.FromMinutes(remainingMinutes);
                _todLabel.Text = $"{phase}: {timeRemaining.Minutes:D2}:{timeRemaining.Seconds:D2}";
                _todLabel.TextColor = phaseColor;

                if (_currentTodPhase != phase)
                {
                    bool isFirstLoad = string.IsNullOrEmpty(_currentTodPhase);
                    _currentTodPhase = phase;
                    _ = RefreshBitingWidget();

                    if (isFirstLoad)
                    {
                        _todIcon.Texture = targetTex;
                    }
                    else
                    {
                        GameService.Animation.Tweener.Tween(_todIcon, new { Opacity = 0f }, 0.5f)
                            .OnComplete(() => {
                                _todIcon.Texture = targetTex;
                                GameService.Animation.Tweener.Tween(_todIcon, new { Opacity = 1f }, 0.5f);
                            });
                    }
                }
            }

            if (_isCheater && _cheaterLabel != null)
            {
                _cheaterTimer += gt.ElapsedGameTime.TotalSeconds;

                if (_cheaterTimer >= 25.0)
                {
                    _cheaterTimer = 0;
                    int maxX = Math.Max(100, GameService.Graphics.SpriteScreen.Width - 800);
                    int maxY = Math.Max(100, GameService.Graphics.SpriteScreen.Height - 100);
                    _cheaterLabel.Location = new Point(_rnd.Next(50, maxX), _rnd.Next(50, maxY));

                    string charName = GameService.Gw2Mumble.PlayerCharacter.Name;
                    if (string.IsNullOrEmpty(charName)) charName = "Player";
                    _cheaterLabel.Text = $"CHEATER DETECTED: {charName}\nPlease disable Gilled Wars or revert your personal best file\nto stop this message!";
                }

                if (_cheaterTimer < 5.0)
                {
                    _cheaterLabel.Visible = true;
                    if (_cheaterTimer < 1.0) _cheaterLabel.Opacity = (float)_cheaterTimer;
                    else if (_cheaterTimer > 4.0) _cheaterLabel.Opacity = (float)(5.0 - _cheaterTimer);
                    else _cheaterLabel.Opacity = 1.0f;
                }
                else
                {
                    _cheaterLabel.Visible = false;
                }
            }

            if (_isSyncTimerActive)
            {
                TimeSpan rem = _nextSyncTime - DateTime.Now;
                if (rem.TotalSeconds <= 0)
                {
                    _isSyncTimerActive = false;
                    if (_casualMeasureBtn != null) _casualMeasureBtn.Enabled = true;
                    if (_activeMeasureBtn != null) _activeMeasureBtn.Enabled = true;

                    if (_casualSyncTimerLabel != null && _casualSyncTimerLabel.Visible)
                    {
                        _casualSyncTimerLabel.Text = strings.Ready;
                        _casualSyncTimerLabel.TextColor = Color.LimeGreen;
                    }
                    if (_activeSyncTimerLabel != null && _activeSyncTimerLabel.Visible)
                    {
                        _activeSyncTimerLabel.Text = "strings.Ready";
                        _activeSyncTimerLabel.TextColor = Color.LimeGreen;
                    }
                }
                else
                {
                    string timeText = string.Format("{0:D2}:{1:D2}", rem.Minutes, rem.Seconds);
                    if (_casualSyncTimerLabel != null && _casualSyncTimerLabel.Visible)
                    {
                        _casualSyncTimerLabel.Text = timeText;
                        _casualSyncTimerLabel.TextColor = Color.Yellow;
                    }
                    if (_activeSyncTimerLabel != null && _activeSyncTimerLabel.Visible)
                    {
                        _activeSyncTimerLabel.Text = timeText;
                        _activeSyncTimerLabel.TextColor = Color.Yellow;
                    }
                }
            }

            if (_isTourneyWaitingRoom)
            {
                TimeSpan waitTime = _tourneyStartTimeUtc - DateTime.UtcNow;
                if (waitTime.TotalSeconds <= 0)
                {
                    _isTourneyWaitingRoom = false;
                    _isTournamentActive = true;
                    if (_waitingRoomLabel != null) _waitingRoomLabel.Visible = false;
                    if (_activeTimerLabel != null) _activeTimerLabel.Visible = true;

                    StartTrackingMode();
                    ScreenNotification.ShowNotification("strings.TourneyBegin", ScreenNotification.NotificationType.Warning);
                }
                else
                {
                    if (_waitingRoomLabel != null)
                    {
                        _waitingRoomLabel.Text = $"Starting in: {waitTime.Minutes:D2}:{waitTime.Seconds:D2}";
                    }
                }
            }

            if (_isTournamentActive && !_isTourneyWaitingRoom)
            {
                TimeSpan rem = _tourneyEndTimeUtc - DateTime.UtcNow;
                if (rem.TotalSeconds <= 0)
                {
                    _isTournamentActive = false;
                    if (_activeTimerLabel != null) _activeTimerLabel.Visible = false;
                    _isSyncTimerActive = false;
                    if (_activeSyncTimerLabel != null) _activeSyncTimerLabel.Visible = false;

                    if (_tourneyModeUsed == "API")
                    {
                        ScreenNotification.ShowNotification(strings.TournamentOverWeighing, ScreenNotification.NotificationType.Warning, null, 10);
                        _ = CheckApiForNewCatches();
                        _isTourneyWrapUpActive = true;
                        _tourneyWrapUpEndTime = DateTime.Now.AddSeconds(30);
                        _didMidWrapUpPing = false;
                    }
                    else
                    {
                        _ = CompleteTournamentAsync(strings.TourneyEndedTitle, strings.TourneySubmitting);
                    }
                }
                else
                {
                    if (_activeTimerLabel != null)
                    {
                        _activeTimerLabel.Text = string.Format("{0:D2}:{1:D2}", rem.Minutes, rem.Seconds);
                    }
                }
            }

            if (_isTourneyWrapUpActive)
            {
                TimeSpan remWrap = _tourneyWrapUpEndTime - DateTime.Now;
                if (remWrap.TotalSeconds <= 15 && !_didMidWrapUpPing)
                {
                    _didMidWrapUpPing = true;
                    _ = CheckApiForNewCatches();
                }

                if (remWrap.TotalSeconds <= 0)
                {
                    _isTourneyWrapUpActive = false;
                    _ = CompleteTournamentAsync(strings.TourneyEndedTitle, strings.TourneySubmitting);
                }
            }
        }

        private async Task ForceUploadPB(bool isAuto = false)
        {
            try
            {
                var unsubmittedKvps = _personalBests.Where(kvp =>
                    (kvp.Value.BestWeight != null && !kvp.Value.BestWeight.IsSubmitted && !kvp.Value.BestWeight.IsCheater) ||
                    (kvp.Value.BestLength != null && !kvp.Value.BestLength.IsSubmitted && !kvp.Value.BestLength.IsCheater)).ToList();

                if (!unsubmittedKvps.Any())
                {
                    
                    if (!isAuto) ScreenNotification.ShowNotification(strings.NoNewSubmissions, ScreenNotification.NotificationType.Info);
                    return;
                }

                if (!isAuto) ScreenNotification.ShowNotification($"Pushing {unsubmittedKvps.Count} unsubmitted PBs to Leaderboard...");

                string accountName = _localAccountName;
                var catchesToSubmit = new List<object>();
                var recordsToMark = new List<SubRecord>();

                foreach (var kvp in unsubmittedKvps)
                {
                    int itemId = kvp.Key;
                    var record = kvp.Value;

                    var fishInfo = _allFishEntries.FirstOrDefault(f => f.Data.ItemId == itemId)?.Data;
                    if (fishInfo == null) continue;

                    if (record.BestWeight != null && !record.BestWeight.IsSubmitted && !record.BestWeight.IsCheater)
                    {
                        catchesToSubmit.Add(new { itemId = itemId, name = fishInfo.Name, weight = record.BestWeight.Weight, length = record.BestWeight.Length, characterName = record.BestWeight.CharacterName, accountName = accountName, isSuper = record.BestWeight.IsSuperPb, signature = record.BestWeight.Signature, type = "weight", location = fishInfo.Location });
                        recordsToMark.Add(record.BestWeight);
                    }

                    if (record.BestLength != null && !record.BestLength.IsSubmitted && !record.BestLength.IsCheater)
                    {
                        catchesToSubmit.Add(new { itemId = itemId, name = fishInfo.Name, weight = record.BestLength.Weight, length = record.BestLength.Length, characterName = record.BestLength.CharacterName, accountName = accountName, isSuper = record.BestLength.IsSuperPb, signature = record.BestLength.Signature, type = "length", location = fishInfo.Location });
                        recordsToMark.Add(record.BestLength);
                    }
                }

                if (!catchesToSubmit.Any()) return;

                var payload = new { catches = catchesToSubmit };
                string jsonPayload = JsonConvert.SerializeObject(payload);
                var content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");

                var response = await _httpClient.PostAsync($"{API_BASE_URL}/submit-leaderboard", content);

                if (response.IsSuccessStatusCode)
                {
                    foreach (var rec in recordsToMark) { rec.IsSubmitted = true; }
                    SavePersonalBests();


                    if (!isAuto) ScreenNotification.ShowNotification(strings.SubmitCooldown.Replace("{0}", "5"), ScreenNotification.NotificationType.Warning);  // Assuming a 5-min default cooldown; adjust if needed
                    else ScreenNotification.ShowNotification(strings.AutoSyncSuccess, ScreenNotification.NotificationType.Info);
                }
                else
                {
                    if (!isAuto) ScreenNotification.ShowNotification(strings.ServerRejected, ScreenNotification.NotificationType.Error);
                }
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Failed to force upload PBs.");
                if (!isAuto) ScreenNotification.ShowNotification("Failed to connect to leaderboards.", ScreenNotification.NotificationType.Error);
            }
        }

        private void OnToggleHotkeyActivated(object sender, EventArgs e)
        {
            if (_mainWindow != null)
            {
                _mainWindow.Visible = !_mainWindow.Visible;
            }
            ScreenNotification.ShowNotification(strings.UiToggled);
        }

        // --- PASTE THE NEW METHOD HERE ---
        private async Task LocalizeFishNamesAsync()
        {
            try
            {
                var allItemIds = _allFishEntries.Select(x => x.Data.ItemId).Distinct().ToList();
                var itemsTask = await Gw2ApiManager.Gw2ApiClient.V2.Items.ManyAsync(allItemIds);

                foreach (var item in itemsTask)
                {
                    var fishEntry = _allFishEntries.FirstOrDefault(x => x.Data.ItemId == item.Id);
                    if (fishEntry != null)
                    {
                        fishEntry.Data.Name = item.Name;
                    }
                }

                RefreshFishLogUI();
                Logger.Info("strings.FishLocalizedSuccess");
            }
            catch (Exception ex)
            {
                Logger.Warn(ex, "strings.FishLocalizedFail");
            }
        }

        protected override void Unload()
        {
            GameService.Input.Mouse.LeftMouseButtonReleased -= OnMouseLeftButtonReleased;
            if (ToggleHotkey != null)
            {
                ToggleHotkey.Value.Activated -= OnToggleHotkeyActivated;
            }

            StopDrfListener();

            _cheaterLabel?.Dispose();
            _cornerIcon?.Dispose();
            _mainWindow?.Dispose();
            _tourneyActivePanel?.Dispose();
            _casualCompactPanel?.Dispose();
            _currentSummaryWindow?.Dispose();
            _targetSelectionWindow?.Dispose();

            _timeOfDayPanel?.Dispose();
            _leaderboardWindow?.Dispose();
            _speciesSelectionWindow?.Dispose();
            _achievementResultsPanel?.Dispose();
            _achievementLegendPanel?.Dispose();
            _metaProgressWindow?.Dispose();
        }
    }

    public class PersonalBestRecord
    {
        public double Weight { get; set; }
        public double Length { get; set; }
        public string Signature { get; set; }
        public bool IsCheater { get; set; }
        public bool IsSuperPb { get; set; }

        public SubRecord BestWeight { get; set; }
        public SubRecord BestLength { get; set; }
    }

    public class SubRecord
    {
        public double Weight { get; set; }
        public double Length { get; set; }
        public string Signature { get; set; }
        public bool IsCheater { get; set; }
        public bool IsSuperPb { get; set; }
        public bool CaughtWithDrf { get; set; }
        public string CharacterName { get; set; }
        public bool IsSubmitted { get; set; }
        // Stored during migration so legacy verification can use the correct English name
        public string EnglishFishName { get; set; }
    }

    public class PersonalBestFile
    {
        // Version 1 = original (name-based signatures)
        // Version 2 = itemId-based signatures (language-independent)
        public int Version { get; set; } = 1;
        public Dictionary<int, PersonalBestRecord> Records { get; set; } = new Dictionary<int, PersonalBestRecord>();
    }

    public class FishData
    {
        [JsonProperty("name")] public string Name { get; set; }
        [JsonProperty("rarity")] public string Rarity { get; set; }
        [JsonProperty("location")] public string Location { get; set; }
        [JsonProperty("time")] public string Time { get; set; }
        [JsonProperty("bait")] public string Bait { get; set; }
        [JsonProperty("fishing_hole")] public string FishingHole { get; set; }
        [JsonProperty("item_id")] public int ItemId { get; set; }
        [JsonProperty("min_weight")] public double MinW { get; set; } = 1.0;
        [JsonProperty("max_weight")] public double MaxW { get; set; } = 10.0;
        [JsonProperty("min_length")] public double MinL { get; set; } = 5.0;
        [JsonProperty("max_length")] public double MaxL { get; set; } = 20.0;
    }

    public class FishUIEntry
    {
        public FishData Data { get; set; }
        public Image Icon { get; set; }
        public FlowPanel CategoryPanel { get; set; }
    }

    public class LeaderboardEntry
    {
        [JsonProperty("player_name")] public string PlayerName { get; set; }
        [JsonProperty("fish_name")] public string FishName { get; set; }
        [JsonProperty("weight")] public double Weight { get; set; }
        [JsonProperty("length")] public double Length { get; set; }
        [JsonProperty("record_type")] public string RecordType { get; set; }
        [JsonProperty("country")] public string Country { get; set; }
    }

    public class TournamentCatch
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public double Weight { get; set; }
        public double Length { get; set; }
        public string Rarity { get; set; }
        public string CharacterName { get; set; }
        public string Signature { get; set; }
        public string TourneySig { get; set; }

        public bool IsNewPb { get; set; }
        public bool IsSuperPb { get; set; }
    }
}

using Blish_HUD;
using Blish_HUD.Controls;
using Blish_HUD.Input;
using Blish_HUD.Modules;
using Blish_HUD.Modules.Managers;
using Blish_HUD.Settings;
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

namespace Gorthax.Gilledwars
{
    [Export(typeof(Module))]
    public class GilledWars : Module
    {
        private static readonly Logger Logger = Logger.GetLogger<GilledWars>();
        public static GilledWars Instance { get; private set; }

        internal SettingsManager SettingsManager => ModuleParameters.SettingsManager;
        internal ContentsManager ContentsManager => ModuleParameters.ContentsManager;
        internal DirectoriesManager DirectoriesManager => ModuleParameters.DirectoriesManager;
        internal Gw2ApiManager Gw2ApiManager => ModuleParameters.Gw2ApiManager;

        private string ModuleDirectory => DirectoriesManager.GetFullDirectoryPath("gilledwars");

        private SettingEntry<KeyBinding> ToggleHotkey { get; set; }
        private SettingEntry<string> _customApiKey { get; set; }
        private SettingEntry<string> _drfToken { get; set; }
        private SettingEntry<string> _discordWebhookUrl { get; set; }

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

        // --- DRF WebSocket Variables ---
        private System.Net.WebSockets.ClientWebSocket _drfSocket;
        private System.Threading.CancellationTokenSource _drfCts;
        private Task _drfReceiveTask;

        // --- State Variables ---
        private static readonly Random _rnd = new Random();
        private bool _isCasualLoggingActive = false;
        private DateTime _lastSubmitTime = DateTime.MinValue;
        private string _localAccountName = "UnknownAccount";
        private Panel _leaderboardWindow;
        private Dropdown _lbSortDropdown;
        private Dropdown _lbSpeciesDropdown;
        private FlowPanel _lbListPanel;
        private bool _isDraggingLeaderboard;
        private Point _leaderboardDragOffset;
        private List<LeaderboardEntry> _cachedLeaderboardData = null;
        private DateTime _lastLeaderboardFetchTime = DateTime.MinValue;
        private Panel _speciesSelectionWindow;
        private StandardButton _speciesFilterBtn;
        private string _currentlySelectedSpecies = "All Species";
        private TextBox _speciesSearchBox;
        private static readonly string[] _junkMessages = new string[]
{
    "You caught... trash! The oceans are healing.",
    "A soggy boot! A true angler's prize.",
    "Just some literal garbage. Better luck next cast!",
    "You reeled in a tangled mess. Peak gameplay.",
    "Is it a legendary fish?! No... it's just debris."
};

        private static readonly string[] _treasureMessages = new string[]
{
    "Woah, Shiny! You caught some actual treasure!",
    "A sunken chest! Hope it's not full of more boots.",
    "Treasure! You're gonna be rich... probably.",
    "You reeled in the jackpot! Nice catch!",
    "Move over, Blackbeard! Sunken loot acquired."
};
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
        private FlowPanel _metaSubContainer = null;
        private Panel _currentlyExpandedRow = null;

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
        }


        private void OnMouseLeftButtonReleased(object sender, MouseEventArgs e)
        {
            _isDragging = false;
            _isActivePanelDragging = false;
            _isDraggingSummary = false;
            _isCompactDragging = false;
            _isDraggingTarget = false;
            _isDraggingLeaderboard = false;
            _isDraggingAchievement = false;
            _isSpeciesSelectionDragging = false;
            _isMetaDragging = false;
        }
        protected override void OnModuleLoaded(EventArgs e)
        {
            LoadFishDatabase();
            // DevEncryptMasterFishList();

            _ = InitializeAccountAndLoadAsync();

            BuildMainWindow();
            BuildCasualCompactPanel();
            GameService.Input.Mouse.LeftMouseButtonReleased += OnMouseLeftButtonReleased;

            _cornerIcon = new CornerIcon()
            {
                Icon = ContentsManager.GetTexture("images/603243.png"),
                BasicTooltipText = "Gilled Wars",
                Priority = 5
            };

            _cornerIcon.Click += (s, ev) => {
                // 1. If we are in Compact mode, expand back to the Main Window
                if (_casualCompactPanel != null && _casualCompactPanel.Visible)
                {
                    _casualCompactPanel.Visible = false;
                    if (_mainWindow != null) _mainWindow.Visible = true;
                    ScreenNotification.ShowNotification("Expanding to Main View");
                    return;
                }

                // 2. Otherwise, perform the standard toggle for the Main Window
                if (_mainWindow != null)
                {
                    _mainWindow.Visible = !_mainWindow.Visible;
                }
            };

            // Clean Hotkey Registration
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

            RefreshFishLogUI();
            base.OnModuleLoaded(e);
        }

        private void ShowLeaderboardWindow()
        {
            if (_leaderboardWindow != null)
            {
                _leaderboardWindow.Visible = true;
                return;
            }

            _leaderboardWindow = new Panel
            {
                Title = "Global Leaderboards",
                Parent = GameService.Graphics.SpriteScreen,
                Size = new Point(460, 600),
                Location = new Point(400, 150),
                ShowBorder = true,
                BackgroundColor = new Color(0, 0, 0, 240),
                ZIndex = 1000
            };

            // --- CLOSE BUTTON ---
            var closeBtn = new StandardButton { Text = "Close", Parent = _leaderboardWindow, Location = new Point(340, 10), Width = 90 };
            closeBtn.Click += (s, e) => {
                _leaderboardWindow.Visible = false;
                if (_speciesSelectionWindow != null) _speciesSelectionWindow.Visible = false;
            };

            // --- REFRESH BUTTON WITH 5-MINUTE COOLDOWN ---
            var refreshBtn = new StandardButton
            {
                Text = "Refresh",
                Parent = _leaderboardWindow,
                Location = new Point(340, 45), // Placed directly under the Close button
                Width = 90,
                BasicTooltipText = "Force fetch latest leaderboard data (5-minute cooldown)."
            };

            refreshBtn.Click += async (s, e) => {
                double elapsedMinutes = (DateTime.Now - _lastLeaderboardFetchTime).TotalMinutes;

                // Check if 5 minutes have passed since the last successful fetch
                if (elapsedMinutes < 5 && _cachedLeaderboardData != null)
                {
                    int remaining = 5 - (int)elapsedMinutes;
                    ScreenNotification.ShowNotification($"Refresh is on cooldown! Wait {remaining}m.", ScreenNotification.NotificationType.Warning);
                    return;
                }

                refreshBtn.Enabled = false; // Disable while loading
                _cachedLeaderboardData = null; // Clear existing cache
                _lastLeaderboardFetchTime = DateTime.MinValue; // Force immediate refresh

                await RefreshLeaderboardData();

                refreshBtn.Enabled = true;
                ScreenNotification.ShowNotification("Leaderboard Refreshed!");
            };

            _leaderboardWindow.LeftMouseButtonPressed += (s, ev) => {
                if (GameService.Input.Mouse.ActiveControl == _leaderboardWindow)
                {
                    _isDraggingLeaderboard = true;
                    _leaderboardDragOffset = new Point(GameService.Input.Mouse.PositionRaw.X - _leaderboardWindow.Location.X, GameService.Input.Mouse.PositionRaw.Y - _leaderboardWindow.Location.Y);
                }
            };

            // 1. Sort Dropdown (Weight/Length)
            new Label { Text = "Sort:", Parent = _leaderboardWindow, Location = new Point(10, 15), AutoSizeWidth = true };
            _lbSortDropdown = new Dropdown() { Parent = _leaderboardWindow, Location = new Point(50, 10), Width = 90 };
            _lbSortDropdown.Items.Add("Weight");
            _lbSortDropdown.Items.Add("Length");
            _lbSortDropdown.SelectedItem = "Weight";
            _lbSortDropdown.ValueChanged += async (s, e) => { await RefreshLeaderboardData(); };

            // 2. Species Selection Button
            new Label { Text = "Fish:", Parent = _leaderboardWindow, Location = new Point(150, 15), AutoSizeWidth = true };
            _speciesFilterBtn = new StandardButton
            {
                Text = "All Species",
                Parent = _leaderboardWindow,
                Location = new Point(190, 10),
                Width = 140,
                BasicTooltipText = "Click to select a specific fish species to filter."
            };
            _speciesFilterBtn.Click += (s, e) => ShowSpeciesPicker();

            // 3. The List Panel
            _lbListPanel = new FlowPanel()
            {
                Parent = _leaderboardWindow,
                Location = new Point(10, 85), // Shifted down to avoid overlapping the Refresh button
                Size = new Point(440, 500),
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
            new Label { Text = "Loading data...", Parent = _lbListPanel, AutoSizeWidth = true, TextColor = Microsoft.Xna.Framework.Color.Yellow };

            try
            {
                string sortMode = _lbSortDropdown.SelectedItem.ToLower();
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
                        new Label { Text = "Server Error: Waiting for website API...", Parent = _lbListPanel, TextColor = Microsoft.Xna.Framework.Color.Red, AutoSizeWidth = true };
                        return;
                    }
                }

                _lbListPanel.ClearChildren();
                _leaderboardWindow.Title = selectedSpecies == "All Species" ? $"Global Top 10 ({sortMode.ToUpper()})" : $"Top 10 {selectedSpecies}";

                if (_cachedLeaderboardData == null || _cachedLeaderboardData.Count == 0)
                {
                    new Label { Text = "No records found.", Parent = _lbListPanel, AutoSizeWidth = true };
                    return;
                }

                var filteredRecords = _cachedLeaderboardData.Where(r => r.RecordType == sortMode);

                if (selectedSpecies != "All Species")
                {
                    filteredRecords = filteredRecords.Where(r => r.FishName.Equals(selectedSpecies, StringComparison.OrdinalIgnoreCase));
                }

                var top10List = filteredRecords
                    .OrderByDescending(r => sortMode == "weight" ? r.Weight : r.Length)
                    .Take(10)
                    .ToList();

                if (top10List.Count == 0)
                {
                    new Label { Text = "No catches logged for this species yet.", Parent = _lbListPanel, AutoSizeWidth = true, TextColor = Microsoft.Xna.Framework.Color.LightGray };
                    return;
                }

                // --- HEADER ---
                var headerRow = new Panel { Parent = _lbListPanel, Width = _lbListPanel.Width - 20, Height = 30 };
                new Label { Text = "Rank", Parent = headerRow, Location = new Point(5, 5), Width = 45, TextColor = Microsoft.Xna.Framework.Color.Cyan, Font = GameService.Content.DefaultFont16 };
                new Label { Text = "Angler", Parent = headerRow, Location = new Point(65, 5), Width = 160, TextColor = Microsoft.Xna.Framework.Color.Cyan, Font = GameService.Content.DefaultFont16 };
                new Label { Text = "Species", Parent = headerRow, Location = new Point(235, 5), Width = 110, TextColor = Microsoft.Xna.Framework.Color.Cyan, Font = GameService.Content.DefaultFont16 };
                new Label { Text = sortMode == "weight" ? "Weight" : "Length", Parent = headerRow, Location = new Point(355, 5), Width = 65, TextColor = Microsoft.Xna.Framework.Color.Cyan, Font = GameService.Content.DefaultFont16 };

                new Image { Texture = ContentService.Textures.Pixel, Parent = _lbListPanel, Width = _lbListPanel.Width - 25, Height = 2, Tint = Microsoft.Xna.Framework.Color.Gray * 0.5f };

                // --- ROWS ---
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
                new Label { Text = "Network Error!", Parent = _lbListPanel, TextColor = Microsoft.Xna.Framework.Color.Red, AutoSizeWidth = true };
            }
        }

        private async Task ShowMetaProgressWindow()
        {
            if (_metaProgressWindow == null)
            {
                _metaProgressWindow = new Panel { ShowBorder = true, Size = new Point(540, 550), Location = new Point(350, 150), Parent = GameService.Graphics.SpriteScreen, BackgroundColor = new Color(20, 20, 20, 240), ZIndex = 1100, ClipsBounds = false };
            }

            _metaProgressWindow.ClearChildren();
            _allMetaSubPanels.Clear();
            _currentlyExpandedRow = null;
            _metaProgressWindow.Visible = true;

            var scroll = new FlowPanel { Parent = _metaProgressWindow, Size = new Point(_metaProgressWindow.Width - 10, _metaProgressWindow.Height - 40), Location = new Point(5, 35), FlowDirection = ControlFlowDirection.SingleTopToBottom, CanScroll = true, ControlPadding = new Vector2(0, 5) };
            new Label { Text = "Fetching all achievement data...", Parent = scroll, Font = GameService.Content.DefaultFont14, TextColor = Color.Yellow, AutoSizeWidth = true };

            try
            {
                // 1. EXACT ORDER REQUESTED: Big Reel, Fishmongers, Buoyant, Hooks, Guild Hall, Cod
                var metaIds = new List<int> { 6478, 6109, 6284, 6201, 6279, 6111 };
                int[] metaMaxes = { 5, 10, 15, 20, 25, 30 }; // Hardcoded exact max values

                // The base 30 fishing collections required for the titles
                int[] base30 = { 6068, 6179, 6330, 6344, 6363, 6317, 6106, 6489, 6336, 6342, 6258, 6506, 6471, 6224, 6439, 6505, 6263, 6153, 6484, 6475, 6227, 6509, 6250, 6339, 6264, 6192, 6466, 6402, 6393, 6110 };

                var allTargetIds = metaIds.Concat(base30).Distinct().ToList();
                var allDefs = await Gw2ApiManager.Gw2ApiClient.V2.Achievements.ManyAsync(allTargetIds);
                var accAchievements = await Gw2ApiManager.Gw2ApiClient.V2.Account.Achievements.GetAsync();

                // Calculate REAL progress to fix the "9/15" API bug
                int realCompletedCollections = 0;
                foreach (int bId in base30)
                {
                    if (accAchievements.FirstOrDefault(a => a.Id == bId)?.Done == true) realCompletedCollections++;
                }

                scroll.ClearChildren();
                var hBar = new Panel { Parent = _metaProgressWindow, Size = new Point(_metaProgressWindow.Width, 30), BackgroundColor = Color.Black * 0.8f, Location = new Point(0, 0) };
                new Label { Text = "Meta Achievement Tracker", Parent = hBar, Location = new Point(10, 5), Font = GameService.Content.DefaultFont16, TextColor = Color.Gold, AutoSizeWidth = true };
                var closeX = new Label { Text = "X", Parent = hBar, Location = new Point(hBar.Width - 25, 5), Font = GameService.Content.DefaultFont16, TextColor = Color.Red, AutoSizeWidth = true };
                closeX.Click += (s, e) => _metaProgressWindow.Visible = false;
                hBar.LeftMouseButtonPressed += (s, ev) => { _isMetaDragging = true; _metaDragOffset = new Point(GameService.Input.Mouse.PositionRaw.X - _metaProgressWindow.Location.X, GameService.Input.Mouse.PositionRaw.Y - _metaProgressWindow.Location.Y); };

                for (int i = 0; i < metaIds.Count; i++)
                {
                    int mId = metaIds[i];
                    var def = allDefs.FirstOrDefault(x => x.Id == mId);
                    if (def == null) continue;

                    var progress = accAchievements.FirstOrDefault(a => a.Id == mId);
                    int max = metaMaxes[i];

                    // Bypass the buggy API current value and use our real count (except for Guild Hall donations)
                    int current = (mId == 6279) ? (progress?.Current ?? 0) : realCompletedCollections;
                    bool isDone = progress?.Done ?? false;
                    if (isDone || current > max) current = max;

                    var row = new Panel { Parent = scroll, Width = 500, Height = 55, BackgroundColor = Color.Black * 0.4f, ShowBorder = true };
                    new Label { Text = def.Name, Parent = row, Location = new Point(10, 5), Font = GameService.Content.DefaultFont16, TextColor = isDone ? Color.LimeGreen : Color.White, AutoSizeWidth = true };
                    new Label { Text = $"{current} / {max}", Parent = row, Location = new Point(430, 5), Font = GameService.Content.DefaultFont14, TextColor = Color.Cyan, AutoSizeWidth = true };
                    var barBg = new Panel { Parent = row, Location = new Point(10, 30), Size = new Point(480, 15), BackgroundColor = Color.DarkGray * 0.5f };
                    new Panel { Parent = barBg, Size = new Point((int)(480 * ((float)current / (max > 0 ? max : 1))), 15), BackgroundColor = isDone ? Color.LimeGreen : Color.Cyan };

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

        // --- THE NEW UNIFIED TIER-AWARE DRILL DOWN ---
        private void BuildSubAchievements(Gw2Sharp.WebApi.V2.Models.Achievement metaDef, FlowPanel container, int currentProgress, bool isFullyDone, IReadOnlyList<Gw2Sharp.WebApi.V2.Models.AccountAchievement> accProgress)
        {
            // The exact list of sub-achievement IDs provided from the text file
            int[] subIds = {
        6068, 6179, 6330, 6344, 6363, 6317, 6106, 6489, 6336, 6342, 6258, 6506, 6471, 6224, 6439, 6505, // Base + Trash/Treasure
        6263, 6153, 6484, 6475, 6227, 6509, 6250, 6339, 6264, 6192, 6466, 6402, 6393, 6110, // Avid
        7114, 7804, // SotO
        8168, 8246, 8554 // Janthir
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
                    Text = "Loading...",
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

                    // Calls the Result Panel we just fixed to show the actual fish
                    await ShowAchievementResultsPanel(cleanName, subId, subCurrent, subMax, subAch.Description ?? "");
                    btn.Enabled = true;
                };
            }
        }
        private void ShowSpeciesPicker()
        {
            if (_speciesSelectionWindow != null) { _speciesSelectionWindow.Visible = !_speciesSelectionWindow.Visible; return; }

            _speciesSelectionWindow = new Panel { Title = "Filter by Species", Parent = GameService.Graphics.SpriteScreen, Size = new Point(280, 500), Location = new Point(_leaderboardWindow.Right + 5, _leaderboardWindow.Top), ShowBorder = true, BackgroundColor = new Color(0, 0, 0, 220), ZIndex = 1100 };
            _speciesSearchBox = new TextBox { Parent = _speciesSelectionWindow, Location = new Point(10, 10), Width = 240, PlaceholderText = "Search species..." };

            var scroll = new FlowPanel { Parent = _speciesSelectionWindow, Size = new Point(260, 420), Location = new Point(10, 50), CanScroll = true, FlowDirection = ControlFlowDirection.SingleTopToBottom };

            Action<string> populateList = (filter) => {
                scroll.ClearChildren();
                var allBtn = new StandardButton { Text = "All Species", Parent = scroll, Width = 230 };
                allBtn.Click += async (s, e) => { _currentlySelectedSpecies = "All Species"; _speciesFilterBtn.Text = "All Species"; _speciesSelectionWindow.Visible = false; await RefreshLeaderboardData(); };

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

        private string GetLegacySeed1()
        {
            byte[] _mS = { 0x01, 0x3c, 0x33, 0x33, 0x34, 0x3b, 0x2c };
            byte[] s = new byte[_mS.Length];
            for (int i = 0; i < _mS.Length; i++) s[i] = (byte)(_mS[i] ^ 0x55);
            return Encoding.UTF8.GetString(s) + "031388";
        }

        private string GetLegacySeed2()
        {
            byte[] _d1 = { 0xA7, 0xE2, 0xD9, 0xC8, 0xF1, 0xB4, 0x8D, 0x7E };
            byte[] _d2 = { 0x2F, 0x6A, 0x53, 0x4C, 0x35 };
            byte[] _k1 = { 0x55, 0xAA, 0x33, 0xCC };
            byte[] b = new byte[_d1.Length + _d2.Length];
            for (int i = 0; i < _d1.Length; i++)
            {
                byte val = (byte)(_d1[i] ^ _k1[i % _k1.Length]);
                b[i] = (byte)((val << 3) | (val >> 5));
            }
            for (int i = 0; i < _d2.Length; i++)
            {
                b[i + _d1.Length] = (byte)(_d2[i] ^ 0x37);
            }
            return Encoding.UTF8.GetString(b);
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

        private string GenerateSignature(double weight, double length, string name, bool isSuperPb, string salt)
        {

            string raw = $"{salt}|{weight:F2}|{length:F2}|{name}|{isSuperPb}";
            using (SHA256 sha256 = SHA256.Create())
            {
                byte[] bytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(raw));
                return Convert.ToBase64String(bytes).Substring(0, 12);
            }
        }


        private string GenerateOldestSignature(double weight, double length, string name, string salt)
        {
            string raw = $"{salt}|{weight}|{length}|{name}";
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
            string newDir = ModuleDirectory; // Path: Documents\Guild Wars 2\addons\blishhud\storage\gilledwars

            try
            {
                // 1. FORCE DIRECTORY CREATION
                // This ensures the folder exists, even if OneDrive is trying to 'offload' it.
                Directory.CreateDirectory(newDir);

                // 2. THE PERMISSIONS POKE (OneDrive Fix)
                // We write a tiny file and delete it immediately to force Windows/OneDrive 
                // to grant write access to this folder right now.
                string testFile = Path.Combine(newDir, "permissions_check.txt");
                File.WriteAllText(testFile, "Gilled Wars Write Test - Success");
                File.Delete(testFile);

                Logger.Info($"[GilledWars] Storage directory verified: {newDir}");
            }
            catch (Exception ex)
            {
                // If this fails, the user likely has a strict OneDrive 'Read-Only' lock.
                Logger.Error(ex, "CRITICAL: Could not write to module storage. OneDrive or Permissions issue.");
                ScreenNotification.ShowNotification("Gilled Wars: Folder Access Error! Check your Documents permissions.", ScreenNotification.NotificationType.Error);
            }

            // --- MIGRATION SECTION ---
            // Old folders we want to completely remove (without nuking the new one!)
            string[] oldDirs = {
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                     "Guild Wars 2", "addons", "blishhud", "gilledwarsanglers"),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                     "Guild Wars 2", "addons", "blishhud", "gilledwarsanglers_OLD")
    };

            bool didMigrate = false;

            foreach (string oldDir in oldDirs)
            {
                if (!Directory.Exists(oldDir)) continue;

                Logger.Info($"[GilledWars] Found old folder to clean: {oldDir}");

                foreach (string oldFile in Directory.GetFiles(oldDir, "personal_bests*.json"))
                {
                    string dest = Path.Combine(newDir, Path.GetFileName(oldFile));
                    try
                    {
                        if (File.Exists(dest)) File.Delete(dest);
                        File.Move(oldFile, dest);
                        Logger.Info($"[GilledWars] Migrated: {Path.GetFileName(oldFile)}");
                        didMigrate = true;
                    }
                    catch (Exception ex)
                    {
                        Logger.Warn(ex, $"Failed to migrate {Path.GetFileName(oldFile)}");
                    }
                }

                try
                {
                    Directory.Delete(oldDir, true);
                    Logger.Info($"[GilledWars] ✅ Completely deleted old folder: {oldDir}");
                }
                catch (Exception ex)
                {
                    Logger.Warn(ex, $"Could not delete {oldDir} (files may be locked)");
                }
            }

            if (didMigrate)
            {
                ScreenNotification.ShowNotification("Gilled Wars: Files migrated & old folder cleaned up!", ScreenNotification.NotificationType.Info);
            }

            // --- ACCOUNT DETECTION ---
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

            // Final load and UI refresh
            LoadPersonalBests();
            RefreshFishLogUI();
            _ = FetchTrueFishingAchievementsAsync();

            Logger.Info($"[GilledWars] Module fully initialized for: {_localAccountName}");
        }

        private void LoadPersonalBests()
        {
            string fileName = _localAccountName == "UnknownAccount"
                ? "personal_bests.json"
                : $"personal_bests_{_localAccountName}.json";

            string path = Path.Combine(ModuleDirectory, fileName);

            _isCheater = false;

            if (File.Exists(path))
            {
                try
                {
                    string json = File.ReadAllText(path);
                    var loaded = JsonConvert.DeserializeObject<Dictionary<int, PersonalBestRecord>>(json)
                                 ?? new Dictionary<int, PersonalBestRecord>();

                    _personalBests = new Dictionary<int, PersonalBestRecord>();
                    string seed = GetGlobalSeed();

                    foreach (var kvp in loaded)
                    {
                        int itemId = kvp.Key;
                        var rec = kvp.Value;

                        void ValidateRecord(SubRecord sub)
                        {
                            if (sub == null) return;
                            string cName = sub.CharacterName ?? "Unknown";
                            string expected = GenerateSignature(sub.Weight, sub.Length,
                                _allFishEntries.FirstOrDefault(x => x.Data.ItemId == itemId)?.Data.Name ?? "Unknown",
                                sub.IsSuperPb, seed + cName + _localAccountName);

                            if (sub.Signature != expected)
                                sub.IsCheater = _isCheater = true;
                        }

                        ValidateRecord(rec.BestWeight);
                        ValidateRecord(rec.BestLength);

                        _personalBests[itemId] = rec;
                        _caughtFishIds.Add(itemId);
                    }
                }
                catch (Exception ex) { Logger.Error(ex, "Failed to load personal bests"); }
            }
        }

        private void SavePersonalBests()
        {
            string fileName = _localAccountName == "UnknownAccount"
                ? "personal_bests.json"
                : $"personal_bests_{_localAccountName}.json";

            string path = Path.Combine(ModuleDirectory, fileName);

            try
            {
                File.WriteAllText(path, JsonConvert.SerializeObject(_personalBests));
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Failed to save personal bests");
            }
        }

        public async Task FetchTrueFishingAchievementsAsync()
        {
            try
            {
                int codAchievementId = 6112; // "Cod Swimming Amongst Mere Minnows"
                var codAchievement = await Gw2ApiManager.Gw2ApiClient.V2.Achievements.GetAsync(codAchievementId);

                var fishingAchievementIds = new List<int>();

                foreach (var bit in codAchievement.Bits)
                {
                    // Check if the bit is an Achievement bit by looking at its Type property
                    if (bit.Type.ToString().Contains("Achievement"))
                    {
                        // Use Reflection to grab the "Id" property safely
                        var idProp = bit.GetType().GetProperty("Id");
                        if (idProp != null)
                        {
                            int bitId = (int)idProp.GetValue(bit);
                            fishingAchievementIds.Add(bitId);
                        }
                    }
                }

                // SotO & JW Expansions
                fishingAchievementIds.Add(7114); // Horn of Maguuma Fisher (Replaces Skywatch/Amnytas)
                fishingAchievementIds.Add(8168); // Janthir Fisher
                fishingAchievementIds.Add(8554); // Mistburned Barrens Fisher
                fishingAchievementIds.Add(8900); // Castora Fisher

                var fishingCollections = await Gw2ApiManager.Gw2ApiClient.V2.Achievements.ManyAsync(fishingAchievementIds);

                // Temporarily just logging them so you can see it working!
                foreach (var collection in fishingCollections)
                {
                    Logger.Info($"[GilledWars] Clean Fishing Collection Loaded: {collection.Name}");
                }
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Failed to fetch verified fishing achievements.");
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
                ScreenNotification.ShowNotification("API Error: Could not read bags.", ScreenNotification.NotificationType.Error);
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
            ScreenNotification.ShowNotification($"Current Fish Measured! Scanned {currentInventory.Values.Sum()} items. Found {newCatches} new.");
        }

        private async Task TakeInventorySnapshot()
        {
            if (string.IsNullOrWhiteSpace(_customApiKey.Value))
            {
                ScreenNotification.ShowNotification("API Error: Paste Custom API Key in Module Settings!", ScreenNotification.NotificationType.Error);
                return;
            }

            var inv = await GetActiveCharacterBags();
            if (inv != null)
            {
                _startInventory = inv;
                ScreenNotification.ShowNotification($"API: Snapshot saved! Tracking {inv.Values.Sum()} items in bags.");
            }
            else
            {
                ScreenNotification.ShowNotification("API Error: Invalid Key or Character Data!", ScreenNotification.NotificationType.Error);
            }
        }

        private async Task StartDrfListener()
        {
            if (string.IsNullOrWhiteSpace(_drfToken.Value))
            {
                ScreenNotification.ShowNotification("DRF Error: Token is missing!", ScreenNotification.NotificationType.Error);
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

                ScreenNotification.ShowNotification("DRF Connected! Tracking in real-time.", ScreenNotification.NotificationType.Warning);

                _drfReceiveTask = Task.Run(ReceiveDrfMessages, _drfCts.Token);
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Failed to connect to DRF WebSocket.");
                ScreenNotification.ShowNotification("DRF Connection Failed! Check Token.", ScreenNotification.NotificationType.Error);
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

                if (_casualLogToggleBtn != null) _casualLogToggleBtn.Text = "Start Logging";
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

            
            bool isJunk = (matchingFish.Rarity != null && matchingFish.Rarity.Equals("Junk", StringComparison.OrdinalIgnoreCase)) ||
                          (matchingFish.Location != null && matchingFish.Location.Contains("Trash Collector"));

            bool isTreasure = (matchingFish.Name != null && (matchingFish.Name.Contains("Treasure") || matchingFish.Name.Contains("Chest"))) ||
                              (matchingFish.Location != null && matchingFish.Location.Contains("Treasure Collector"));

            if (isJunk)
            {
                string msg = _junkMessages[_rnd.Next(_junkMessages.Length)];
                ScreenNotification.ShowNotification(msg, ScreenNotification.NotificationType.Error);
                return; 
            }

            if (isTreasure)
            {
                string msg = _treasureMessages[_rnd.Next(_treasureMessages.Length)];
                ScreenNotification.ShowNotification(msg, ScreenNotification.NotificationType.Warning);
                return; 
            }

          
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
            string globalSig = GenerateSignature(weight, length, matchingFish.Name, isSuperPb, GetGlobalSeed() + charName + _localAccountName);
            string tSig = _isTournamentActive ? GenerateSignature(weight, length, matchingFish.Name, isSuperPb, _tourneyRoomCode) : "";

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
            if (_recentCatches.Count > 5) _recentCatches.RemoveAt(_recentCatches.Count - 1);

            if (_recentCatchesPanel != null && _recentCatchesPanel.Visible) UpdateRecentCatchesUI();
            if (_casualCompactPanel != null && _casualCompactPanel.Visible) UpdateCompactCooler();

            string pbAlert = isSuperPb ? " - SUPER PB!" : (catchRecord.IsNewPb ? " - NEW PB!" : "");
            var notifType = isSuperPb ? ScreenNotification.NotificationType.Warning : ScreenNotification.NotificationType.Info;

            if (_isTournamentActive && !_isTourneyWaitingRoom)
            {
                if (_tourneyTargetItemId == 0 || _tourneyTargetItemId == itemId)
                {
                    _tourneyCatches.Add(catchRecord);
                    UpdateActiveTourneyCoolerUI();
                    ScreenNotification.ShowNotification($"Tourney Catch: {catchRecord.Name} ({catchRecord.Weight} lbs, {catchRecord.Length} in){pbAlert}", notifType);
                }
                else ScreenNotification.ShowNotification($"Caught: {catchRecord.Name} ({catchRecord.Weight} lbs, {catchRecord.Length} in){pbAlert}", notifType);
            }
            else ScreenNotification.ShowNotification($"Caught: {catchRecord.Name} ({catchRecord.Weight} lbs, {catchRecord.Length} in){pbAlert}", notifType);
        }

        private void BuildMainWindow()
        {
            // --- 1. MAIN WINDOW SETUP ---
            _mainWindow = new Panel
            {
                // REMOVED 'Title' to disable Blish HUD's native blue header
                ShowBorder = true,
                Size = new Point(620, 580),
                Location = new Point(300, 300),
                Parent = GameService.Graphics.SpriteScreen,
                Visible = false,
                BackgroundColor = new Color(30, 30, 30, 230),
                ClipsBounds = false
            };

            // --- 2. CUSTOM HEADER BAR ---
            // We build our own header bar so we have 100% control over the layout
            var headerBar = new Panel
            {
                Parent = _mainWindow,
                Size = new Point(_mainWindow.Width, 30),
                Location = new Point(0, 0),
                BackgroundColor = Color.Black * 0.8f // Dark header background
            };

            // Custom Title Text
            var titleLabel = new Label
            {
                Text = "Gilled Wars (visit www.GilledWars.com)",
                Parent = headerBar,
                Location = new Point(10, 5),
                Font = GameService.Content.DefaultFont16,
                TextColor = Color.Gold,
                AutoSizeWidth = true
            };

            // Custom "X" Close Button (Now safely inside our custom header)
            var xCloseBtn = new Label
            {
                Text = "X",
                Parent = headerBar,
                Location = new Point(headerBar.Width - 25, 5), // Firmly placed on the right
                Font = GameService.Content.DefaultFont16,
                TextColor = Color.Red,
                AutoSizeWidth = true,
                BasicTooltipText = "Close Gilled Wars"
            };

            xCloseBtn.Click += (s, e) => { _mainWindow.Visible = false; };
            xCloseBtn.MouseEntered += (s, e) => { xCloseBtn.TextColor = Color.White; };
            xCloseBtn.MouseLeft += (s, e) => { xCloseBtn.TextColor = Color.Red; };

            // Standard dragging logic, attached to our custom header
            headerBar.LeftMouseButtonPressed += (s, ev) => {
                if (GameService.Input.Mouse.ActiveControl == headerBar || GameService.Input.Mouse.ActiveControl == titleLabel)
                {
                    _isDragging = true;
                    _dragOffset = new Point(GameService.Input.Mouse.PositionRaw.X - _mainWindow.Location.X, GameService.Input.Mouse.PositionRaw.Y - _mainWindow.Location.Y);
                }
            };

            // --- 3. TOP NAVIGATION BAR ---
            // Buttons sit safely below our custom header at Y: 40
            var casualBtn = new StandardButton { Text = "Casual Fishing", Parent = _mainWindow, Location = new Point(10, 40), Width = 150 };
            var tourneyBtn = new StandardButton { Text = "Tournament Mode", Parent = _mainWindow, Location = new Point(170, 40), Width = 150 };

            var linkButton = new StandardButton
            {
                Text = "Website",
                Parent = _mainWindow,
                Location = new Point(330, 40),
                Width = 100,
                BasicTooltipText = "Opens gilledwars.com in your web browser."
            };
            linkButton.Click += (s, ev) => {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo { FileName = "https://www.gilledwars.com", UseShellExecute = true });
            };

            var inGameLbBtn = new StandardButton
            {
                Text = "In-Game Top 10",
                Parent = _mainWindow,
                Location = new Point(440, 40),
                Width = 150,
                BasicTooltipText = "View the live top 10 without leaving the game!"
            };
            inGameLbBtn.Click += (s, ev) => {
                ScreenNotification.ShowNotification("Loading Top 10...");
                ShowLeaderboardWindow();
            };

            // --- 4. SUB-PANEL INITIALIZATION ---
            // Shifted down to Y: 80 so it doesn't overlap the buttons
            _casualPanel = new Panel { Parent = _mainWindow, Size = new Point(600, 490), Location = new Point(10, 80), Visible = true };
            _tournamentPanel = new Panel { Parent = _mainWindow, Size = new Point(600, 490), Location = new Point(10, 80), Visible = false };

            // --- 5. THE CORE LOG PANEL & THE NEW ACHIEVEMENT ANALYZER PANEL ---
            _fishLogPanel = new Panel
            {
                Parent = _casualPanel,
                Location = new Point(0, 65),
                Size = new Point(_casualPanel.Width, _casualPanel.Height - 65),
                Visible = false
            };

            _achievementPanel = new Panel
            {
                Parent = _casualPanel,
                Location = new Point(0, 65),
                Size = new Point(_casualPanel.Width, _casualPanel.Height - 65),
                Visible = false
            };

            // --- 6. BUTTON LOGIC (Swapping View Contexts) ---
            casualBtn.Click += (s, ev) => {
                _casualPanel.Visible = true;
                _tournamentPanel.Visible = false;
            };

            tourneyBtn.Click += (s, ev) => {
                _casualPanel.Visible = false;
                _tournamentPanel.Visible = true;
            };

            // --- 7. BUILD UI SECTIONS ---
            BuildFishLogGrid(_fishLogPanel);
            BuildCasualUI(_casualPanel);
            BuildTournamentUI(_tournamentPanel);
            BuildActiveTournamentWidget();
        }

        private void BuildFishLogGrid(Panel parent)
        {
            if (parent == null) return;

            // --- 1. FILTER PANEL (Original Layout) ---
            var filterPanel = new Panel { Parent = parent, Location = new Point(10, 0), Size = new Point(580, 110) };

            var searchBar = new TextBox { Parent = filterPanel, Location = new Point(0, 0), Width = 115, PlaceholderText = "Search..." };
            var rarityDrop = new Dropdown { Parent = filterPanel, Location = new Point(120, 0), Width = 125 };
            var locationDrop = new Dropdown { Parent = filterPanel, Location = new Point(250, 0), Width = 150 };
            var holeDrop = new Dropdown { Parent = filterPanel, Location = new Point(405, 0), Width = 165 };

            var timeDrop = new Dropdown { Parent = filterPanel, Location = new Point(0, 35), Width = 115 };
            var baitDrop = new Dropdown { Parent = filterPanel, Location = new Point(120, 35), Width = 125 };
            var collapseBtn = new StandardButton { Text = "Collapse", Parent = filterPanel, Location = new Point(250, 35), Width = 75 };
            var revealBtn = new StandardButton { Text = "Reveal", Parent = filterPanel, Location = new Point(330, 35), Width = 70 };
            var resetFiltersBtn = new StandardButton { Text = "Reset Filters", Parent = filterPanel, Location = new Point(0, 70), Width = 100 };

            var pushLeaderboardBtn = new StandardButton
            {
                Text = "Push PBs to Leaderboard",
                Parent = filterPanel,
                Location = new Point(120, 70),
                Width = 200,
                BasicTooltipText = "Submit your valid DRF-tracked catches to the global leaderboards!"
            };

            var zoneAnalyzerBtn = new StandardButton
            {
                Text = "Zone Analyzer",
                Parent = filterPanel,
                Location = new Point(330, 70),
                Width = 110, // Slightly shrunk to fit the Meta button
                BasicTooltipText = "Analyze current map for missing achievement fish via API!"
            };

            // --- NEW BUTTON: META ACHIEVEMENTS ---
            var metaProgressBtn = new StandardButton
            {
                Text = "Meta Progress",
                Parent = filterPanel,
                Location = new Point(450, 70), // Right next to Zone Analyzer
                Width = 120,
                BasicTooltipText = "Track your progress towards Cod Swimming and other big titles!"
            };

            // --- 2. GRID AREA (Original 64x64 Icons) ---
            var scroll = new FlowPanel { Parent = parent, Location = new Point(10, 135), Size = new Point(580, parent.Height - 140), CanScroll = true, FlowDirection = ControlFlowDirection.SingleTopToBottom };

            void ApplyFilters()
            {
                foreach (var cat in _categoryPanels)
                {
                    bool anyVisible = false;
                    foreach (var entry in _allFishEntries.Where(x => x.CategoryPanel == cat))
                    {
                        bool match = true;
                        if (!string.IsNullOrEmpty(searchBar.Text) && !entry.Data.Name.ToLower().Contains(searchBar.Text.ToLower())) match = false;
                        if (rarityDrop.SelectedItem != "All Rarities" && entry.Data.Rarity != rarityDrop.SelectedItem) match = false;
                        if (locationDrop.SelectedItem != "All Locations" && entry.Data.Location != locationDrop.SelectedItem) match = false;
                        if (holeDrop.SelectedItem != "All Holes" && !entry.Data.FishingHole.Contains(holeDrop.SelectedItem)) match = false;
                        if (timeDrop.SelectedItem != "All Times" && !entry.Data.Time.Contains(timeDrop.SelectedItem == "Any" ? "Any" : timeDrop.SelectedItem)) match = false;
                        if (baitDrop.SelectedItem != "All Baits" && !entry.Data.Bait.Contains(baitDrop.SelectedItem)) match = false;

                        entry.Icon.Visible = match;
                        if (match) anyVisible = true;
                    }
                    cat.Visible = anyVisible;
                }
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
                // --- COOLDOWN CHECK ---
                if ((DateTime.Now - _lastSubmitTime).TotalMinutes < 5)
                {
                    double rem = 5.0 - (DateTime.Now - _lastSubmitTime).TotalMinutes;
                    ScreenNotification.ShowNotification($"Please wait {rem:F1} minutes before pushing again.", ScreenNotification.NotificationType.Error);
                    return;
                }

                pushLeaderboardBtn.Enabled = false;
                pushLeaderboardBtn.Text = "Uploading...";

                // Call the upload logic
                await ForceUploadPB();

                // Reset timer and button state
                _lastSubmitTime = DateTime.Now;
                pushLeaderboardBtn.Text = "Push PBs to Leaderboard";
                pushLeaderboardBtn.Enabled = true;
            };

            zoneAnalyzerBtn.Click += async (s, ev) => {
                zoneAnalyzerBtn.Enabled = false;
                zoneAnalyzerBtn.Text = "Scanning...";
                try
                {
                    int currentMapId = GameService.Gw2Mumble.CurrentMap.Id;
                    if (currentMapId == 0) return;

                    var mapInfo = await Gw2ApiManager.Gw2ApiClient.V2.Maps.GetAsync(currentMapId);

                    var achievementMap = new Dictionary<string, int> {
    { "Kryta", 6068 },
    { "Shiverpeak", 6179 },
    { "Ascalon", 6330 },
    { "Maguuma Jungle", 6344 },
    { "Ruins of Orr", 6363 },
    { "Crystal Desert", 6317 },
    { "Elona", 6106 },
    { "Ring of Fire", 6489 },
    { "Seitung Province", 6336 },
    { "New Kaineng City", 6342 },
    { "The Echovald Wilds", 6258 },
    { "Dragon's End", 6506 },
    // Secrets of the Obscure (All map to Horn of Maguuma)
    { "Skywatch Archipelago", 7114 },
    { "Amnytas", 7114 },
    { "Inner Nayos", 7114 },
    // Janthir Wilds
    { "Lowland Shore", 8168 },
    { "Janthir Syntri", 8168 },
    { "Mistburned Barrens", 8554 }
};

                    string target = achievementMap.Keys.FirstOrDefault(k =>
                        mapInfo.Name.Contains(k) ||
                        (mapInfo.RegionName != null && mapInfo.RegionName.Contains(k))
                    ) ?? "All Locations";

                    if (target != "All Locations")
                    {
                        int achId = achievementMap[target];

                        // Fetch the sub-achievement to get real progress / description
                        var achDef = await Gw2ApiManager.Gw2ApiClient.V2.Achievements.GetAsync(achId);

                        int subCurrent = 0;
                        int subMax = achDef.Tiers?.LastOrDefault()?.Count ?? achDef.Bits?.Count ?? 0;
                        string subDescription = achDef.Description ?? "No description available.";

                        // NOW CALLING WITH ALL 5 ARGUMENTS — this fixes line 1663
                        await ShowAchievementResultsPanel(target, achId, subCurrent, subMax, subDescription);
                    }
                    else
                    {
                        ScreenNotification.ShowNotification("No fishing achievement found for this area.", ScreenNotification.NotificationType.Error);
                    }
                }
                catch (Exception ex)
                {
                    Logger.Error(ex, "Zone Analyzer failed.");
                    ScreenNotification.ShowNotification("Zone Analyzer failed.", ScreenNotification.NotificationType.Error);
                }
                finally
                {
                    zoneAnalyzerBtn.Enabled = true;
                    zoneAnalyzerBtn.Text = "Zone Analyzer";
                }
            };

            // Hooking up the Meta Progress Button click event
            metaProgressBtn.Click += async (s, ev) => {
                metaProgressBtn.Enabled = false;
                metaProgressBtn.Text = "Loading...";
                await ShowMetaProgressWindow();
                metaProgressBtn.Text = "Meta Progress";
                metaProgressBtn.Enabled = true;
            };

            // Populate Dropdowns
            var uniqueHoles = new HashSet<string>();
            foreach (var entry in _allFishEntries) { if (!string.IsNullOrEmpty(entry.Data.FishingHole)) { foreach (var sh in entry.Data.FishingHole.Replace("None, ", "").Split(',').Select(h => h.Trim())) uniqueHoles.Add(sh); } }
            rarityDrop.Items.Add("All Rarities"); foreach (var r in _allFishEntries.Select(x => x.Data.Rarity).Distinct()) rarityDrop.Items.Add(r); rarityDrop.SelectedItem = "All Rarities";
            locationDrop.Items.Add("All Locations"); foreach (var l in _allFishEntries.Select(x => x.Data.Location).Distinct()) locationDrop.Items.Add(l); locationDrop.SelectedItem = "All Locations";
            holeDrop.Items.Add("All Holes"); foreach (var h in uniqueHoles.OrderBy(x => x)) if (h != "None") holeDrop.Items.Add(h); holeDrop.SelectedItem = "All Holes";
            timeDrop.Items.Add("All Times"); timeDrop.Items.Add("Daytime"); timeDrop.Items.Add("Nighttime"); timeDrop.Items.Add("Dawn/Dusk"); timeDrop.Items.Add("Any"); timeDrop.SelectedItem = "All Times";
            baitDrop.Items.Add("All Baits"); foreach (var b in _allFishEntries.Select(x => x.Data.Bait).Distinct()) baitDrop.Items.Add(b); baitDrop.SelectedItem = "All Baits";

            // --- 3. BUILD THE ORIGINAL ICON GRID ---
            foreach (var group in _allFishEntries.Select(x => x.Data).Where(x => x.Location != "Any").GroupBy(x => x.Location).OrderBy(x => x.Key))
            {
                var p = new FlowPanel { Parent = scroll, Title = group.Key, CanCollapse = true, ShowBorder = true, Width = 550, HeightSizingMode = SizingMode.AutoSize, FlowDirection = ControlFlowDirection.LeftToRight };
                _categoryPanels.Add(p);

                foreach (var fish in group)
                {
                    string safeName = fish.Name.Replace(" ", "_").Replace("'", "").Replace("-", "");
                    bool isCaught = _caughtFishIds.Contains(fish.ItemId);
                    string pbWText = "NONE LOGGED"; string pbLText = "NONE LOGGED";
                    var tintColor = isCaught ? Color.White : Color.Gray * 0.5f;

                    if (_personalBests.TryGetValue(fish.ItemId, out var rec))
                    {
                        if (rec.BestWeight != null) pbWText = rec.BestWeight.IsCheater ? "CHEATER DETECTED" : $"{rec.BestWeight.Weight} lbs";
                        if (rec.BestLength != null) pbLText = rec.BestLength.IsCheater ? "CHEATER DETECTED" : $"{rec.BestLength.Length} in";
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

                            // Restoration of your exact requested text format
                            if (w > 0 || l > 0)
                            {
                                clip = $"{code} my PB weight for this guy is: {w} lbs and my PB for length is: {l} in";
                            }
                        }

                        System.Windows.Forms.Clipboard.SetText(clip);
                        ScreenNotification.ShowNotification($"Copied {fish.Name} code!");
                    };

                    var entry = _allFishEntries.First(x => x.Data.ItemId == fish.ItemId);
                    entry.Icon = img;
                    entry.CategoryPanel = p;
                }
            }

            // The spacer at the end to prevent the bottom row from getting cut off
            new Panel { Parent = scroll, Width = 550, Height = 60 };
        }


        private async Task ShowAchievementResultsPanel(string locationName, int achievementId, int subCurrent, int subMax, string subDescription)
        {
            // --- 1. WINDOW INITIALIZATION (WIDENED TO 800) ---
            if (_achievementResultsPanel == null)
            {
                _achievementResultsPanel = new Panel
                {
                    Title = $"{locationName} Progress",
                    Parent = GameService.Graphics.SpriteScreen,
                    Size = new Point(800, 600), // WIDENED
                    Location = new Point(400, 100),
                    ShowBorder = true,
                    BackgroundColor = new Color(0, 0, 0, 240),
                    ZIndex = 1001
                };

                _achievementResultsPanel.LeftMouseButtonPressed += (s, ev) =>
                {
                    if (GameService.Input.Mouse.ActiveControl == _achievementResultsPanel)
                    {
                        _isDraggingAchievement = true;
                        _achievementDragOffset = new Point(GameService.Input.Mouse.PositionRaw.X - _achievementResultsPanel.Location.X, GameService.Input.Mouse.PositionRaw.Y - _achievementResultsPanel.Location.Y);
                    }
                };
            }
            else
            {
                _achievementResultsPanel.Title = $"{locationName} Progress";
            }

            if (_achievementLegendPanel == null)
            {
                _achievementLegendPanel = new Panel
                {
                    Parent = GameService.Graphics.SpriteScreen,
                    Size = new Point(800, 60), // WIDENED
                    ShowBorder = true,
                    BackgroundColor = new Color(0, 0, 0, 240),
                    ZIndex = 1002
                };
            }

            _achievementResultsPanel.Visible = true;
            _achievementLegendPanel.Visible = true;
            _achievementResultsPanel.ClearChildren();
            _achievementLegendPanel.ClearChildren();

            _achievementLegendPanel.Location = new Point(_achievementResultsPanel.Location.X, _achievementResultsPanel.Location.Y + _achievementResultsPanel.Height + 2);

            // --- 2. RARITY LEGEND ---
            int lx = 5;
            string[] rarityNames = { "Legendary", "Ascended", "Exotic", "Rare", "Masterwork", "Fine", "Basic" };
            foreach (var rName in rarityNames)
            {
                var rColor = GetRarityColor(rName);
                new Label { Text = "■", Parent = _achievementLegendPanel, Location = new Point(lx, 10), TextColor = rColor, Font = GameService.Content.DefaultFont14, AutoSizeWidth = true };
                new Label { Text = rName, Parent = _achievementLegendPanel, Location = new Point(lx + 15, 10), TextColor = rColor, Font = GameService.Content.DefaultFont12, AutoSizeWidth = true };
                lx += (rName.Length * 7) + 20;
            }

            var closeBtn = new StandardButton { Text = "Close", Parent = _achievementResultsPanel, Location = new Point(690, 10), Width = 90 }; // SHIFTED RIGHT
            closeBtn.Click += (s, e) => { _achievementResultsPanel.Visible = false; _achievementLegendPanel.Visible = false; };

            var progressHeader = new Panel { Parent = _achievementResultsPanel, Location = new Point(10, 45), Size = new Point(780, 35) }; // WIDENED
            var listContainer = new FlowPanel { Parent = _achievementResultsPanel, Location = new Point(10, 115), Size = new Point(780, 470), CanScroll = true, FlowDirection = ControlFlowDirection.SingleTopToBottom, ControlPadding = new Vector2(0, 2) }; // WIDENED

            // --- 3. DATA FETCHING & BIT CROSS-REFERENCING ---
            try
            {
                var achievementDef = await Gw2ApiManager.Gw2ApiClient.V2.Achievements.GetAsync(achievementId);
                var accAchievements = await Gw2ApiManager.Gw2ApiClient.V2.Account.Achievements.GetAsync();
                var progress = accAchievements.FirstOrDefault(a => a.Id == achievementId);

                var completedBits = progress?.Bits ?? new List<int>();
                int totalBits = achievementDef.Bits?.Count ?? 0;
                int missingCount = totalBits - completedBits.Count;

                new Label { Text = $"{locationName.ToUpper()}:", Parent = progressHeader, Location = new Point(0, 5), Font = GameService.Content.DefaultFont18, AutoSizeWidth = true, TextColor = Color.Gold };
                new Label { Text = $"MISSING: {missingCount} / {totalBits}", Parent = progressHeader, Location = new Point(310, 7), Font = GameService.Content.DefaultFont14, AutoSizeWidth = true, TextColor = Color.Cyan };

                if (missingCount == 0 && totalBits > 0)
                {
                    new Label { Text = "✓ ACHIEVEMENT COMPLETE! GOOD JOB!", Parent = listContainer, Location = new Point(20, 20), Font = GameService.Content.DefaultFont16, TextColor = Color.LimeGreen, AutoSizeWidth = true };
                    return;
                }

                // Header for the list columns (EXPANDED TIME COLUMN)
                var columnHeader = new Panel { Parent = _achievementResultsPanel, Location = new Point(10, 85), Size = new Point(780, 30), BackgroundColor = Color.Black * 0.3f };
                new Label { Text = "NAME", Parent = columnHeader, Location = new Point(60, 5), Width = 170, TextColor = Color.Cyan, Font = GameService.Content.DefaultFont16 };
                new Label { Text = "BAIT", Parent = columnHeader, Location = new Point(240, 5), Width = 130, TextColor = Color.Cyan, Font = GameService.Content.DefaultFont16 };
                new Label { Text = "TIME", Parent = columnHeader, Location = new Point(380, 5), Width = 150, TextColor = Color.Cyan, Font = GameService.Content.DefaultFont16 }; // WIDENED MASSIVELY
                new Label { Text = "HOLE", Parent = columnHeader, Location = new Point(540, 5), Width = 230, TextColor = Color.Cyan, Font = GameService.Content.DefaultFont16 }; // SHIFTED & WIDENED

                new Image { Texture = ContentService.Textures.Pixel, Parent = listContainer, Width = 760, Height = 2, Tint = Color.Gray * 0.5f };

                if (achievementDef.Bits != null)
                {
                    for (int i = 0; i < achievementDef.Bits.Count; i++)
                    {
                        if (completedBits.Contains(i)) continue;

                        var bit = achievementDef.Bits[i];
                        var idProp = bit.GetType().GetProperty("Id");
                        if (idProp == null) continue;
                        int fishItemId = (int)idProp.GetValue(bit);

                        var dbFish = _allFishEntries.FirstOrDefault(x => x.Data.ItemId == fishItemId)?.Data;

                        var row = new Panel { Parent = listContainer, Width = 760, Height = 55, BackgroundColor = Color.Black * 0.2f, ShowBorder = true };

                        if (dbFish != null)
                        {
                            string safeName = dbFish.Name.Replace(" ", "_").Replace("'", "").Replace("-", "");

                            new Image { Texture = ContentsManager.GetTexture($"images/{safeName}.png"), Parent = row, Location = new Point(10, 7), Size = new Point(40, 40) };
                            new Image { Texture = ContentService.Textures.Pixel, Parent = row, Location = new Point(55, 5), Width = 1, Height = 45, Tint = Color.White * 0.1f };

                            new Label { Text = dbFish.Name, Parent = row, Location = new Point(60, 5), Size = new Point(170, 45), WrapText = true, VerticalAlignment = VerticalAlignment.Middle, TextColor = GetRarityColor(dbFish.Rarity), Font = GameService.Content.DefaultFont14 };
                            new Image { Texture = ContentService.Textures.Pixel, Parent = row, Location = new Point(235, 5), Width = 1, Height = 45, Tint = Color.White * 0.1f };

                            new Label { Text = dbFish.Bait, Parent = row, Location = new Point(240, 5), Size = new Point(130, 45), WrapText = true, VerticalAlignment = VerticalAlignment.Middle, TextColor = Color.LightGray, Font = GameService.Content.DefaultFont12 };
                            new Image { Texture = ContentService.Textures.Pixel, Parent = row, Location = new Point(375, 5), Width = 1, Height = 45, Tint = Color.White * 0.1f };

                            new Label { Text = dbFish.Time, Parent = row, Location = new Point(380, 5), Size = new Point(150, 45), WrapText = true, VerticalAlignment = VerticalAlignment.Middle, TextColor = Color.White, Font = GameService.Content.DefaultFont12 };
                            new Image { Texture = ContentService.Textures.Pixel, Parent = row, Location = new Point(535, 5), Width = 1, Height = 45, Tint = Color.White * 0.1f };

                            new Label { Text = dbFish.FishingHole, Parent = row, Location = new Point(540, 5), Size = new Point(210, 45), WrapText = true, VerticalAlignment = VerticalAlignment.Middle, TextColor = Color.LightGray, Font = GameService.Content.DefaultFont12 };
                        }
                        else
                        {
                            new Label { Text = "?", Parent = row, Location = new Point(20, 15), Font = GameService.Content.DefaultFont18, TextColor = Color.Red };
                            new Label { Text = $"API ID: {fishItemId} (MISSING FROM JSON)", Parent = row, Location = new Point(60, 17), Font = GameService.Content.DefaultFont14, TextColor = Color.White, AutoSizeWidth = true };
                        }

                        new Image { Texture = ContentService.Textures.Pixel, Parent = listContainer, Width = 760, Height = 1, Tint = Color.White * 0.15f };
                    }
                }

                new Panel { Parent = listContainer, Width = 760, Height = 60 };
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Achievement Detail Panel failed.");
            }
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

        private async Task ShowAchievementAnalysis(string locationName, int achievementId)
        {
            // --- 1. PANEL NAVIGATION (LEADERBOARD BEHAVIOR) ---
            _fishLogPanel.Visible = false;
            _achievementPanel.Visible = true;
            _achievementPanel.ClearChildren();

            // --- 2. HEADER PANEL (Matches Leaderboard Title Bar) ---
            var headerPanel = new Panel
            {
                Parent = _achievementPanel,
                Size = new Point(580, 50),
                Location = new Point(10, 0),
                BackgroundColor = Color.Black * 0.5f
            };

            var backBtn = new StandardButton
            {
                Parent = headerPanel,
                Text = "<- BACK",
                Location = new Point(5, 10),
                Width = 75
            };
            backBtn.Click += (s, e) => {
                _achievementPanel.Visible = false;
                _fishLogPanel.Visible = true;
            };

            new Label
            {
                Text = $"{locationName.ToUpper()} ACHIEVEMENT PROGRESS",
                Parent = headerPanel,
                Location = new Point(90, 12),
                Font = GameService.Content.DefaultFont18,
                AutoSizeWidth = true,
                TextColor = Color.Gold
            };

            // --- 3. COLUMN HEADERS (Mimics Leaderboard Headers) ---
            var columnHeader = new Panel
            {
                Parent = _achievementPanel,
                Location = new Point(10, 55),
                Size = new Point(580, 30),
                BackgroundColor = Color.Black * 0.3f
            };

            new Label { Text = "ICON", Parent = columnHeader, Location = new Point(10, 5), Font = GameService.Content.DefaultFont12, TextColor = Color.LightGray };
            new Label { Text = "FISH NAME", Parent = columnHeader, Location = new Point(65, 5), Font = GameService.Content.DefaultFont12, TextColor = Color.LightGray };
            new Label { Text = "BAIT", Parent = columnHeader, Location = new Point(230, 5), Font = GameService.Content.DefaultFont12, TextColor = Color.LightGray };
            new Label { Text = "TIME", Parent = columnHeader, Location = new Point(370, 5), Font = GameService.Content.DefaultFont12, TextColor = Color.LightGray };
            new Label { Text = "LOCATION", Parent = columnHeader, Location = new Point(480, 5), Font = GameService.Content.DefaultFont12, TextColor = Color.LightGray };

            // --- 4. SCROLLABLE DATA LIST ---
            var list = new FlowPanel
            {
                Parent = _achievementPanel,
                Location = new Point(10, 90),
                Size = new Point(580, 400),
                CanScroll = true,
                FlowDirection = ControlFlowDirection.SingleTopToBottom,
                ControlPadding = new Vector2(0, 2)
            };

            try
            {
                var achievementDef = await Gw2ApiManager.Gw2ApiClient.V2.Achievements.GetAsync(achievementId);
                var allAccountAchievements = await Gw2ApiManager.Gw2ApiClient.V2.Account.Achievements.GetAsync();
                var progress = allAccountAchievements.FirstOrDefault(a => a.Id == achievementId);
                var bits = progress?.Bits ?? new List<int>();

                for (int i = 0; i < achievementDef.Bits.Count; i++)
                {
                    if (!bits.Contains(i) && achievementDef.Bits[i] is Gw2Sharp.WebApi.V2.Models.AchievementItemBit itemBit)
                    {
                        var dbFish = _allFishEntries.FirstOrDefault(x => x.Data.ItemId == (int)itemBit.Id)?.Data;
                        if (dbFish == null) continue;

                        // MIMICS LEADERBOARD ROW
                        var row = new Panel { Parent = list, Width = 560, Height = 45, BackgroundColor = Color.Black * 0.2f, ShowBorder = true };

                        // COLUMN 1: ICON (Rank slot)
                        new Image { Parent = row, Size = new Point(36, 36), Location = new Point(5, 4), Texture = ContentsManager.GetTexture($"images/{dbFish.Name.Replace(" ", "_")}.png") };

                        // COLUMN 2: NAME (Angler slot)
                        new Label { Text = dbFish.Name, Parent = row, Location = new Point(65, 12), AutoSizeWidth = true, TextColor = GetRarityColor(dbFish.Rarity), Font = GameService.Content.DefaultFont14 };

                        // COLUMN 3: BAIT (Species slot)
                        new Label { Text = dbFish.Bait, Parent = row, Location = new Point(230, 12), AutoSizeWidth = true, TextColor = Color.Cyan };

                        // COLUMN 4: TIME (Weight slot)
                        new Label { Text = dbFish.Time, Parent = row, Location = new Point(370, 12), AutoSizeWidth = true, TextColor = Color.Yellow };

                        // COLUMN 5: LOCATION
                        new Label { Text = dbFish.Location, Parent = row, Location = new Point(480, 12), AutoSizeWidth = true, TextColor = Color.LightGray, Font = GameService.Content.DefaultFont12 };
                    }
                }

                if (list.Children.Count == 0)
                {
                    new Label { Text = "✔ Area Fully Logged!", Parent = list, AutoSizeWidth = true, TextColor = Color.LimeGreen, Padding = new Thickness(10, 50, 0, 0) };
                }
            }
            catch
            {
                new Label { Text = "API Link Failed - Verify Key Permissions", Parent = list, AutoSizeWidth = true, TextColor = Color.Red };
            }
        }



        private void BuildCasualUI(Panel parent)
        {
            var chestIcon = new Image { Parent = parent, Location = new Point(10, 0), Size = new Point(48, 48), BasicTooltipText = "View Fish Cooler", Texture = ContentsManager.GetTexture("images/603243.png") };
            var logBtn = new StandardButton { Text = "Fish Log", Parent = parent, Location = new Point(70, 10), Width = 100 };

            _casualLogToggleBtn = new StandardButton { Text = "Start Logging", Parent = parent, Location = new Point(180, 10), Width = 120 };
            var compactBtn = new StandardButton { Text = "Compact", Parent = parent, Location = new Point(530, 10), Width = 80 };

            compactBtn.Click += (s, e) => {
                if (_isCasualLoggingActive)
                {
                    _mainWindow.Visible = false;
                    _casualCompactPanel.Visible = true;
                    UpdateCompactCooler();
                }
            };

            _useDrfCheckbox = new Checkbox { Text = "Use DRF (Real-Time)", Parent = parent, Location = new Point(180, 45), BasicTooltipText = "Requires drf.rs Addon installed and Token in settings." };

            _casualMeasureBtn = new StandardButton { Text = "Measure Fish", Parent = parent, Location = new Point(310, 10), Width = 120, Enabled = false };
            _casualSyncTimerLabel = new Label { Text = "05:00", Parent = parent, Location = new Point(440, 15), AutoSizeWidth = true, TextColor = Color.Yellow, Visible = false };

            _casualLogToggleBtn.Click += async (s, e) => {
                if (_isCasualLoggingActive)
                {
                    StopCasualLogging();
                    _mainWindow.Size = new Point(620, 580);
                    _mainWindow.Visible = true;
                    ScreenNotification.ShowNotification("Casual Logging Stopped.");
                }
                else
                {
                    _isCasualLoggingActive = true;
                    _casualLogToggleBtn.Text = "Stop Logging";
                    _useDrfCheckbox.Enabled = false;

                    if (!_useDrfCheckbox.Checked)
                    {
                        _casualMeasureBtn.Enabled = false;
                        await TakeInventorySnapshot();
                        _nextSyncTime = DateTime.Now.AddMinutes(5);
                        _isSyncTimerActive = true;
                        _casualSyncTimerLabel.TextColor = Color.Yellow;
                        _casualSyncTimerLabel.Visible = true;
                    }
                    else
                    {
                        _casualMeasureBtn.Enabled = false;
                        _casualSyncTimerLabel.Text = "DRF Active";
                        _casualSyncTimerLabel.TextColor = Color.LimeGreen;
                        _casualSyncTimerLabel.Visible = true;
                        _ = StartDrfListener();
                    }

                    _mainWindow.Visible = false;
                    _casualCompactPanel.Visible = true;
                    UpdateCompactCooler();
                }
            };

            _casualMeasureBtn.Click += async (s, e) => {
                _casualMeasureBtn.Enabled = false;
                _casualSyncTimerLabel.Text = "Measuring...";
                await CheckApiForNewCatches();

                _nextSyncTime = DateTime.Now.AddMinutes(5);
                _isSyncTimerActive = true;
            };

            _recentCatchesPanel = new Panel { Parent = parent, Location = new Point(0, 65), Size = new Point(parent.Width, parent.Height - 65), Visible = true };

            chestIcon.Click += (s, e) => {
                _recentCatchesPanel.Visible = true;
                _fishLogPanel.Visible = false;
                UpdateRecentCatchesUI();
            };

            logBtn.Click += (s, e) => {
                _recentCatchesPanel.Visible = false;
                _fishLogPanel.Visible = true;
            };

            BuildRecentCatchesUI(_recentCatchesPanel);
        }

        private void BuildRecentCatchesUI(Panel parent)
        {
            new Label { Text = "Fish Cooler (Last 5 Catches)", Parent = parent, Location = new Point(10, 0), AutoSizeWidth = true, Font = GameService.Content.DefaultFont18, TextColor = Color.Cyan };
        }

        private void UpdateRecentCatchesUI()
        {
            if (_recentCatchesPanel == null) return;
            _recentCatchesPanel.ClearChildren();
            BuildRecentCatchesUI(_recentCatchesPanel);
            int y = 40;
            foreach (var c in _recentCatches.Take(5))
            {
                Color catchColor = Color.White;
                if (c.IsSuperPb) catchColor = Color.Gold;
                else if (c.IsNewPb) catchColor = Color.DeepSkyBlue;

                new Label
                {
                    Text = $"{c.Name} - {c.Weight} lbs | {c.Length} in",
                    Parent = _recentCatchesPanel,
                    Location = new Point(20, y),
                    AutoSizeWidth = true,
                    TextColor = catchColor,
                    Font = GameService.Content.DefaultFont18
                };
                y += 30;
            }
        }

        private void RefreshFishLogUI()
        {
            foreach (var entry in _allFishEntries)
            {
                var fish = entry.Data;
                var img = entry.Icon; // No 'as Panel' conversion needed
                if (img == null) continue;

                // 1. Check caught status and prepare PB text
                bool isCaught = _caughtFishIds.Contains(fish.ItemId) || _personalBests.ContainsKey(fish.ItemId);
                string pbWText = "NONE LOGGED";
                string pbLText = "NONE LOGGED";

                // Default tint: White if caught, dimmed gray if not
                var tint = isCaught ? Color.White : Color.Gray * 0.5f;

                // 2. Fetch Personal Best records if they exist
                if (_personalBests.TryGetValue(fish.ItemId, out var rec))
                {
                    if (rec.BestWeight != null)
                    {
                        if (rec.BestWeight.IsCheater) pbWText = "CHEATER DETECTED";
                        else if (rec.BestWeight.IsSuperPb) { pbWText = $"[SUPER] {rec.BestWeight.Weight} lbs"; tint = Color.Gold; }
                        else pbWText = $"{rec.BestWeight.Weight} lbs";
                    }

                    if (rec.BestLength != null)
                    {
                        if (rec.BestLength.IsCheater) pbLText = "CHEATER DETECTED";
                        else if (rec.BestLength.IsSuperPb) { pbLText = $"[SUPER] {rec.BestLength.Length} in"; tint = Color.Gold; }
                        else pbLText = $"{rec.BestLength.Length} in";
                    }
                }

                // 3. Apply updates directly to the Image icon
                img.Tint = tint;

                // 4. Update the Tooltip (Hiding PB info for Junk/Treasure)
                bool isCollector = (fish.Rarity != null && fish.Rarity.Equals("Junk", StringComparison.OrdinalIgnoreCase)) ||
                                   (fish.Location != null && fish.Location.Contains("Collector"));

                string tooltip = $"{fish.Name}\nRarity: {fish.Rarity}\nLocation: {fish.Location}\nHole: {fish.FishingHole}\nTime: {fish.Time}\nBait: {fish.Bait}";

                if (!isCollector)
                {
                    tooltip += $"\n\nPB Weight: {pbWText}\nPB Length: {pbLText}";
                }

                img.BasicTooltipText = tooltip;
            }
        }

        private void ShowTargetSelectionWindow(StandardButton targetBtn)
        {
            if (_targetSelectionWindow != null) _targetSelectionWindow.Dispose();
            _targetSelectionWindow = new Panel { Title = "Select Target Species", Parent = GameService.Graphics.SpriteScreen, Size = new Point(400, 500), Location = new Point(450, 200), ShowBorder = true, BackgroundColor = new Color(0, 0, 0, 230), CanScroll = false };

            _targetSelectionWindow.LeftMouseButtonPressed += (s, e) => {
                if (GameService.Input.Mouse.ActiveControl == _targetSelectionWindow) { _isDraggingTarget = true; _targetDragOffset = new Point(GameService.Input.Mouse.PositionRaw.X - _targetSelectionWindow.Location.X, GameService.Input.Mouse.PositionRaw.Y - _targetSelectionWindow.Location.Y); }
            };

            var closeBtn = new StandardButton { Text = "Close", Parent = _targetSelectionWindow, Location = new Point(125, 430), Width = 150 };
            closeBtn.Click += (s, e) => _targetSelectionWindow.Dispose();

            var scroll = new FlowPanel { Parent = _targetSelectionWindow, Size = new Point(380, 420), CanScroll = true, FlowDirection = ControlFlowDirection.SingleTopToBottom };

            var allBtn = new StandardButton { Text = "All Species", Parent = scroll, Width = 350 };
            allBtn.Click += (s, e) => { _tourneyTargetItemId = 0; targetBtn.Text = "Target: All Species"; _targetSelectionWindow.Dispose(); };

            var groups = _allFishEntries.GroupBy(x => x.Data.Location).OrderBy(x => x.Key);
            foreach (var g in groups)
            {
                var p = new FlowPanel { Parent = scroll, Title = g.Key, CanCollapse = true, ShowBorder = true, Width = 360, HeightSizingMode = SizingMode.AutoSize, FlowDirection = ControlFlowDirection.SingleTopToBottom, Collapsed = true };
                foreach (var fish in g.OrderBy(x => x.Data.Name))
                {
                    var fBtn = new StandardButton { Text = fish.Data.Name, Parent = p, Width = 330 };
                    fBtn.Click += (s, e) => { _tourneyTargetItemId = fish.Data.ItemId; targetBtn.Text = $"Target: {fish.Data.Name}"; _targetSelectionWindow.Dispose(); };
                }
            }
        }

        private void BuildTournamentUI(Panel parent)
        {
            var hostModeBtn = new StandardButton { Text = "Host", Parent = parent, Location = new Point(10, 0), Width = 100 };
            var partModeBtn = new StandardButton { Text = "Participant", Parent = parent, Location = new Point(115, 0), Width = 100 };

            _tourneyHostPanel = new Panel { Parent = parent, Location = new Point(0, 40), Size = new Point(parent.Width, parent.Height - 40), Visible = true };
            _tourneyParticipantPanel = new Panel { Parent = parent, Location = new Point(0, 40), Size = new Point(parent.Width, parent.Height - 40), Visible = false };

            hostModeBtn.Click += (s, e) => { _tourneyHostPanel.Visible = true; _tourneyParticipantPanel.Visible = false; };
            partModeBtn.Click += (s, e) => { _tourneyHostPanel.Visible = false; _tourneyParticipantPanel.Visible = true; };

            // --- HOST PANEL ---
            new Label { Text = "Host Tournament Setup", Parent = _tourneyHostPanel, Location = new Point(10, 0), AutoSizeWidth = true, TextColor = Color.Yellow };

            new Label { Text = "Start Delay", Parent = _tourneyHostPanel, Location = new Point(10, 25), AutoSizeWidth = true, TextColor = Color.LightGray, BasicTooltipText = "How long until the tournament starts for everyone." };
            new Label { Text = "Mins", Parent = _tourneyHostPanel, Location = new Point(160, 25), AutoSizeWidth = true, TextColor = Color.LightGray };
            new Label { Text = "Tracking Mode", Parent = _tourneyHostPanel, Location = new Point(230, 25), AutoSizeWidth = true, TextColor = Color.LightGray };

            var hostStartDelayDrop = new Dropdown { Parent = _tourneyHostPanel, Location = new Point(10, 45), Width = 130 };
            hostStartDelayDrop.Items.Add("Start Immediately");
            hostStartDelayDrop.Items.Add("2 Minutes");
            hostStartDelayDrop.Items.Add("5 Minutes");
            hostStartDelayDrop.Items.Add("10 Minutes");

            var hostTimerMin = new TextBox { Parent = _tourneyHostPanel, Location = new Point(160, 45), Width = 60, Text = "30" };

            var hostTrackingModeDrop = new Dropdown { Parent = _tourneyHostPanel, Location = new Point(230, 45), Width = 150 };
            hostTrackingModeDrop.Items.Add("API (5-Min Wait)");
            hostTrackingModeDrop.Items.Add("DRF (Real-Time)");
            hostTrackingModeDrop.SelectedItem = "DRF (Real-Time)";

            new Label { Text = "Target Species", Parent = _tourneyHostPanel, Location = new Point(10, 80), AutoSizeWidth = true, TextColor = Color.LightGray };
            new Label { Text = "Win Factor", Parent = _tourneyHostPanel, Location = new Point(230, 80), AutoSizeWidth = true, TextColor = Color.LightGray };

            var targetSpeciesBtn = new StandardButton { Text = "Target: All Species", Parent = _tourneyHostPanel, Location = new Point(10, 100), Width = 200, BasicTooltipText = "Click to select a specific fish for the tournament." };
            targetSpeciesBtn.Click += (s, e) => { ShowTargetSelectionWindow(targetSpeciesBtn); };

            _hostWinFactorDrop = new Dropdown { Parent = _tourneyHostPanel, Location = new Point(230, 100), Width = 120 };
            _hostWinFactorDrop.Items.Add("Weight");
            _hostWinFactorDrop.Items.Add("Length");

            var genKeyBtn = new StandardButton { Text = "Create Room", Parent = _tourneyHostPanel, Location = new Point(10, 150), Width = 150 };

            genKeyBtn.Click += async (s, e) => {
                genKeyBtn.Enabled = false;
                genKeyBtn.Text = "Creating...";

                string charName = GameService.Gw2Mumble.PlayerCharacter.Name;
                if (string.IsNullOrEmpty(charName)) charName = "Host";

                int delayMins = 0;
                if (hostStartDelayDrop.SelectedItem == "2 Minutes") delayMins = 2;
                if (hostStartDelayDrop.SelectedItem == "5 Minutes") delayMins = 5;
                if (hostStartDelayDrop.SelectedItem == "10 Minutes") delayMins = 10;

                string mode = hostTrackingModeDrop.SelectedItem.Contains("API") ? "API" : "DRF";

                var payload = new
                {
                    hostName = charName,
                    startDelayMins = delayMins,
                    durationMins = int.Parse(hostTimerMin.Text),
                    mode = mode,
                    targetId = _tourneyTargetItemId,
                    winFactor = _hostWinFactorDrop.SelectedItem,
                    webhookUrl = _discordWebhookUrl.Value
                };

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
                            string code = resultObj["roomCode"]; // This will now be GW-XXXXX
                            CopyToClipboard(code);
                            ScreenNotification.ShowNotification($"Room {code} created & copied!");
                        }
                    }
                    else
                    {
                        ScreenNotification.ShowNotification("API Error: Could not create room.", ScreenNotification.NotificationType.Error);
                    }
                }
                catch (Exception ex)
                {
                    Logger.Error(ex, "Failed to connect to API.");
                    ScreenNotification.ShowNotification("Network Error: Could not connect to API.", ScreenNotification.NotificationType.Error);
                }

                genKeyBtn.Enabled = true;
                genKeyBtn.Text = "Create Room";
            };

            var verifyInput = new TextBox { Parent = _tourneyHostPanel, Location = new Point(10, 195), Width = 300, PlaceholderText = "Paste Member End Code Here..." };
            var verifyBtn = new StandardButton { Text = "Manual Verify", Parent = _tourneyHostPanel, Location = new Point(10, 230), Width = 150, BasicTooltipText = "Backup tool in case API submission fails." };

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

                    if (providedSig != expectedSig)
                    {
                        ShowTournamentSummary("Error", "TAMPERED RESULTS\nSignatures do not match.", Color.Red);
                        return;
                    }

                    string charName = payloadParts[1];
                    List<TournamentCatch> reconstructedCatches = new List<TournamentCatch>();

                    for (int i = 2; i < payloadParts.Length; i++)
                    {
                        string[] fishData = payloadParts[i].Split(',');
                        int fId = int.Parse(fishData[0]);
                        double fWt = double.Parse(fishData[1]);
                        double fLen = double.Parse(fishData[2]);

                        var dbFish = _allFishEntries.FirstOrDefault(x => x.Data.ItemId == fId)?.Data;
                        if (dbFish != null)
                        {
                            reconstructedCatches.Add(new TournamentCatch
                            {
                                Name = dbFish.Name,
                                Weight = fWt,
                                Length = fLen,
                                Rarity = dbFish.Rarity,
                                CharacterName = charName
                            });
                        }
                    }

                    string currentHostWinFactor = _hostWinFactorDrop.SelectedItem;
                    ShowTournamentSummary("Verified Results", null, Color.LimeGreen, reconstructedCatches, currentHostWinFactor);
                }
                catch { ShowTournamentSummary("Error", "INVALID DATA\nEnsure the player copied the entire string.", Color.Red); }
            };

            new Label
            {
                Parent = _tourneyHostPanel,
                Location = new Point(10, 280),
                Width = 550,
                Height = 200,
                WrapText = true,
                TextColor = Color.LightGray,
                Text = "TRACKING MODES EXPLAINED:\n\n" +
                       "API (5-Min Wait): Uses official GW2 servers. Highly secure, no extra downloads needed. Catch detection delayed by ArenaNet's cache.\n\n" +
                       "DRF (Real-Time): Uses the drf.rs memory reader. Instant catch detection! Participants MUST install the 3rd-party DRF .dll to participate."
            };

            // --- PARTICIPANT PANEL ---
            new Label { Text = "Participant Join", Parent = _tourneyParticipantPanel, Location = new Point(10, 0), AutoSizeWidth = true, TextColor = Color.Cyan };
            var partSessionKey = new TextBox { Parent = _tourneyParticipantPanel, Location = new Point(10, 30), Width = 150, PlaceholderText = "Code (e.g. GW-1234)" };

            var joinBtn = new StandardButton { Text = "Join Room", Parent = _tourneyParticipantPanel, Location = new Point(10, 70), Width = 150 };

            joinBtn.Click += async (s, e) => {
                joinBtn.Enabled = false;
                joinBtn.Text = "Joining...";

                try
                {
                    string roomCode = partSessionKey.Text.Trim().ToUpper();
                    if (!roomCode.StartsWith("GW-") || roomCode.Length != 8)
                    {
                        ScreenNotification.ShowNotification("Invalid Code Format! Use GW-XXXXX", ScreenNotification.NotificationType.Error);
                        joinBtn.Enabled = true; joinBtn.Text = "Join Room";
                        return;
                    }

                    var response = await _httpClient.GetAsync($"{API_BASE_URL}/join/{roomCode}");
                    string resultString = await response.Content.ReadAsStringAsync();

                    if (response.IsSuccessStatusCode)
                    {
                        var tData = JsonConvert.DeserializeObject<Dictionary<string, object>>(resultString);

                        StopCasualLogging();

                        _tourneyRoomCode = roomCode;
                        _tourneyModeUsed = tData["mode"].ToString();
                        _tourneyTargetItemId = Convert.ToInt32(tData["targetId"]);
                        _tourneyWinFactor = tData["winFactor"].ToString();
                        int durMins = Convert.ToInt32(tData["durationMins"]);
                        long startTimeMs = Convert.ToInt64(tData["startTime"]);

                        _tourneyStartTimeUtc = DateTimeOffset.FromUnixTimeMilliseconds(startTimeMs).UtcDateTime;
                        _tourneyEndTimeUtc = _tourneyStartTimeUtc.AddMinutes(durMins);

                        _tourneyCatches.Clear();

                        string targetName = "All Species";
                        if (_tourneyTargetItemId != 0)
                        {
                            var targetFish = _allFishEntries.FirstOrDefault(x => x.Data.ItemId == _tourneyTargetItemId);
                            if (targetFish != null) targetName = targetFish.Data.Name;
                        }
                        if (_tourneyActivePanel != null) _tourneyActivePanel.Title = $"{targetName} ({_tourneyWinFactor})";

                        UpdateActiveTourneyCoolerUI();

                        // Check if tournament is in the future or past
                        if (DateTime.UtcNow < _tourneyStartTimeUtc)
                        {
                            _isTourneyWaitingRoom = true;
                            _waitingRoomLabel.Visible = true;
                            _activeTimerLabel.Visible = false;
                            _activeMeasureBtn.Enabled = false;
                            ScreenNotification.ShowNotification("Entered Waiting Room...");
                        }
                        else
                        {
                            _isTourneyWaitingRoom = false;
                            _isTournamentActive = true;
                            _waitingRoomLabel.Visible = false;
                            _activeTimerLabel.Visible = true;
                            StartTrackingMode();
                            ScreenNotification.ShowNotification("Tournament Fishing Started!");
                        }

                        _mainWindow.Visible = false;
                        _tourneyActivePanel.Visible = true;
                    }
                    else
                    {
                        ScreenNotification.ShowNotification("Room Not Found or Expired!", ScreenNotification.NotificationType.Error);
                    }
                }
                catch (Exception ex)
                {
                    Logger.Error(ex, "API Join Failed");
                    ScreenNotification.ShowNotification("Network Error!", ScreenNotification.NotificationType.Error);
                }

                joinBtn.Enabled = true;
                joinBtn.Text = "Join Room";
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
                Size = new Point(320, 280),
                Location = new Point(400, 300),
                ShowBorder = true,
                BackgroundColor = new Color(0, 0, 0, 200),
                Visible = false
            };

            _tourneyActivePanel.LeftMouseButtonPressed += (s, ev) => {
                if (GameService.Input.Mouse.ActiveControl == _tourneyActivePanel) { _isActivePanelDragging = true; _activePanelDragOffset = new Point(GameService.Input.Mouse.PositionRaw.X - _tourneyActivePanel.Location.X, GameService.Input.Mouse.PositionRaw.Y - _tourneyActivePanel.Location.Y); }
            };

            _activeTimerLabel = new Label { Text = "00:00", Parent = _tourneyActivePanel, Location = new Point(10, 10), Font = GameService.Content.DefaultFont32, TextColor = Color.White, AutoSizeWidth = true, Visible = false };
            _waitingRoomLabel = new Label { Text = "Starting in...", Parent = _tourneyActivePanel, Location = new Point(10, 10), Font = GameService.Content.DefaultFont18, TextColor = Color.Yellow, AutoSizeWidth = true, Visible = false };

            _activeSyncTimerLabel = new Label { Text = "05:00", Parent = _tourneyActivePanel, Location = new Point(100, 20), AutoSizeWidth = true, TextColor = Color.Yellow, Visible = false };

            _activeMeasureBtn = new StandardButton { Text = "Measure Fish", Parent = _tourneyActivePanel, Location = new Point(180, 10), Width = 110, Enabled = false };

            _activeEndBtn = new StandardButton { Text = "End & Submit", Parent = _tourneyActivePanel, Location = new Point(10, 50), Width = 120 };

            _activeRecopyBtn = new StandardButton { Text = "Re-Copy Code", Parent = _tourneyActivePanel, Location = new Point(10, 50), Width = 120, Visible = false, BasicTooltipText = "Manual backup." };
            _activeExitBtn = new StandardButton { Text = "Exit Tourney", Parent = _tourneyActivePanel, Location = new Point(140, 50), Width = 120, Visible = false, BasicTooltipText = "Close and return to UI." };

            new Label { Text = "Top 5 Catches:", Parent = _tourneyActivePanel, Location = new Point(10, 85), AutoSizeWidth = true, TextColor = Color.Cyan };
            _activeCoolerList = new FlowPanel { Parent = _tourneyActivePanel, Location = new Point(10, 110), Size = new Point(280, 150), FlowDirection = ControlFlowDirection.SingleTopToBottom };

            _activeMeasureBtn.Click += async (s, e) => {
                _activeMeasureBtn.Enabled = false;
                _activeSyncTimerLabel.Text = "Measuring...";
                await CheckApiForNewCatches();
                _nextSyncTime = DateTime.Now.AddMinutes(5);
                _isSyncTimerActive = true;
            };

            _activeEndBtn.Click += async (s, e) => await CompleteTournamentAsync("Tournament Ended Manually", "Results submitted to server.");

            _activeRecopyBtn.Click += (s, e) => {
                if (!string.IsNullOrEmpty(_lastGeneratedCode))
                {
                    CopyToClipboard(_lastGeneratedCode);
                    ScreenNotification.ShowNotification("Backup Code Re-copied!");
                }
            };

            _activeExitBtn.Click += (s, e) => {
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
            };
        }

        private void BuildCasualCompactPanel()
        {
            _casualCompactPanel = new Panel
            {
                Title = "Casual Fishing",
                Parent = GameService.Graphics.SpriteScreen,
                Size = new Point(340, 240),
                Location = new Point(400, 300),
                ShowBorder = true,
                BackgroundColor = new Color(0, 0, 0, 200),
                Visible = false
            };

            _casualCompactPanel.LeftMouseButtonPressed += (s, ev) => {
                if (GameService.Input.Mouse.ActiveControl == _casualCompactPanel)
                {
                    _isCompactDragging = true;
                    _compactDragOffset = new Point(GameService.Input.Mouse.PositionRaw.X - _casualCompactPanel.Location.X, GameService.Input.Mouse.PositionRaw.Y - _casualCompactPanel.Location.Y);
                }
            };

            new Label { Text = "Fish Cooler (Last 5 Catches)", Parent = _casualCompactPanel, Location = new Point(10, 10), AutoSizeWidth = true, Font = GameService.Content.DefaultFont18, TextColor = Color.Cyan };

            _compactCoolerList = new FlowPanel { Parent = _casualCompactPanel, Location = new Point(10, 40), Size = new Point(310, 120), FlowDirection = ControlFlowDirection.SingleTopToBottom };

            _compactFishLogBtn = new StandardButton { Text = "Fish Log", Parent = _casualCompactPanel, Location = new Point(10, 170), Width = 100 };
            _compactMaxBtn = new StandardButton { Text = "Max Size", Parent = _casualCompactPanel, Location = new Point(120, 170), Width = 100 };

            _compactFishLogBtn.Click += (s, e) => {
                _casualCompactPanel.Visible = false;
                _mainWindow.Visible = true;
                _recentCatchesPanel.Visible = false;
                _fishLogPanel.Visible = true;
            };

            _compactMaxBtn.Click += (s, e) => {
                _casualCompactPanel.Visible = false;
                _mainWindow.Visible = true;
                _recentCatchesPanel.Visible = true;
                _fishLogPanel.Visible = false;
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
            _isTourneyWrapUpActive = false;
            if (_activeMeasureBtn != null) _activeMeasureBtn.Enabled = false;
            _isSyncTimerActive = false;

            StopDrfListener();

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
                    msg = "Results successfully auto-posted to host's Discord!";
                    ScreenNotification.ShowNotification("Results Submitted to Server!");
                }
                else
                {
                    msg = "API submission failed. Please copy backup code manually.";
                    CopyToClipboard(_lastGeneratedCode);
                }
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Failed to submit results.");
                msg = "Network Error. Backup code copied to clipboard.";
                CopyToClipboard(_lastGeneratedCode);
            }

            if (_activeEndBtn != null) _activeEndBtn.Visible = false;
            if (_activeMeasureBtn != null) _activeMeasureBtn.Visible = false;

            if (_activeRecopyBtn != null) _activeRecopyBtn.Visible = true;
            if (_activeExitBtn != null) _activeExitBtn.Visible = true;

            ShowTournamentSummary(title, msg, Color.Cyan, _tourneyCatches, _tourneyWinFactor);
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

                string statText = _tourneyWinFactor == "Length"
                    ? $"{c.Length} in | {c.Weight} lbs"
                    : $"{c.Weight} lbs | {c.Length} in";

                new Label
                {
                    Text = $"{c.Name} - {statText}",
                    Parent = _activeCoolerList,
                    AutoSizeWidth = true,
                    TextColor = catchColor
                };
            }
        }

        private void ShowTournamentSummary(string title, string errorMsg, Color ColorTheme, List<TournamentCatch> catches = null, string winFactor = "Weight")
        {
            if (_currentSummaryWindow != null) _currentSummaryWindow.Dispose();
            _currentSummaryWindow = new Panel { Title = title, Parent = GameService.Graphics.SpriteScreen, Size = new Point(450, 450), Location = new Point(500, 300), ShowBorder = true, BackgroundColor = new Color(0, 0, 0, 230) };
            _currentSummaryWindow.LeftMouseButtonPressed += (s, e) => {
                if (GameService.Input.Mouse.ActiveControl == _currentSummaryWindow) { _isDraggingSummary = true; _summaryDragOffset = new Point(GameService.Input.Mouse.PositionRaw.X - _currentSummaryWindow.Location.X, GameService.Input.Mouse.PositionRaw.Y - _currentSummaryWindow.Location.Y); }
            };
            var list = new FlowPanel { Parent = _currentSummaryWindow, Location = new Point(20, 40), Size = new Point(410, 300), CanScroll = true, FlowDirection = ControlFlowDirection.SingleTopToBottom };

            if (!string.IsNullOrEmpty(errorMsg))
            {
                new Label { Text = errorMsg, Parent = list, AutoSizeHeight = true, WrapText = true, Width = 380, TextColor = ColorTheme, Font = GameService.Content.DefaultFont18 };
            }

            if (catches != null && catches.Count > 0)
            {
                new Label { Text = $"Angler: {catches.FirstOrDefault()?.CharacterName ?? GameService.Gw2Mumble.PlayerCharacter.Name}", Parent = list, AutoSizeWidth = true, Font = GameService.Content.DefaultFont18, TextColor = ColorTheme };

                var sorted = winFactor == "Length"
                    ? catches.OrderByDescending(x => x.Length)
                    : catches.OrderByDescending(x => x.Weight);

                foreach (var c in sorted.Take(5))
                {
                    string statText = winFactor == "Length" ? $"{c.Length} in | {c.Weight} lbs" : $"{c.Weight} lbs | {c.Length} in";
                    new Label { Text = $"[{c.Rarity}] {c.Name} - {statText}", Parent = list, AutoSizeWidth = true, TextColor = Color.White };
                }
            }
            var close = new StandardButton { Text = "Close", Parent = _currentSummaryWindow, Location = new Point(150, 360), Width = 150 };
            close.Click += (s, e) => _currentSummaryWindow.Dispose();
        }

        protected override void Update(GameTime gt)
        {
            // --- 1. MAIN UI PANEL DRAGGING ---
            if (_isDragging && _mainWindow != null)
            {
                _mainWindow.Location = new Point(GameService.Input.Mouse.PositionRaw.X - _dragOffset.X, GameService.Input.Mouse.PositionRaw.Y - _dragOffset.Y);
            }
            if (_isActivePanelDragging && _tourneyActivePanel != null)
            {
                _tourneyActivePanel.Location = new Point(GameService.Input.Mouse.PositionRaw.X - _activePanelDragOffset.X, GameService.Input.Mouse.PositionRaw.Y - _activePanelDragOffset.Y);
            }
            if (_isDraggingSummary && _currentSummaryWindow != null)
            {
                _currentSummaryWindow.Location = new Point(GameService.Input.Mouse.PositionRaw.X - _summaryDragOffset.X, GameService.Input.Mouse.PositionRaw.Y - _summaryDragOffset.Y);
            }
            if (_isCompactDragging && _casualCompactPanel != null)
            {
                _casualCompactPanel.Location = new Point(GameService.Input.Mouse.PositionRaw.X - _compactDragOffset.X, GameService.Input.Mouse.PositionRaw.Y - _compactDragOffset.Y);
            }
            if (_isDraggingTarget && _targetSelectionWindow != null)
            {
                _targetSelectionWindow.Location = new Point(GameService.Input.Mouse.PositionRaw.X - _targetDragOffset.X, GameService.Input.Mouse.PositionRaw.Y - _targetDragOffset.Y);
            }
            if (_isDraggingLeaderboard && _leaderboardWindow != null)
            {
                _leaderboardWindow.Location = new Point(GameService.Input.Mouse.PositionRaw.X - _leaderboardDragOffset.X, GameService.Input.Mouse.PositionRaw.Y - _leaderboardDragOffset.Y);
            }
            if (_isSpeciesSelectionDragging && _speciesSelectionWindow != null)
            {
                _speciesSelectionWindow.Location = new Point(GameService.Input.Mouse.PositionRaw.X - _speciesSelectionDragOffset.X, GameService.Input.Mouse.PositionRaw.Y - _speciesSelectionDragOffset.Y);
            }
            if (_isDraggingAchievement && _achievementResultsPanel != null)
            {
                _achievementResultsPanel.Location = new Point(
                    GameService.Input.Mouse.PositionRaw.X - _achievementDragOffset.X,
                    GameService.Input.Mouse.PositionRaw.Y - _achievementDragOffset.Y
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
                    GameService.Input.Mouse.PositionRaw.X - _metaDragOffset.X,
                    GameService.Input.Mouse.PositionRaw.Y - _metaDragOffset.Y
                );
            }

            // --- 2. ANTI-CHEAT OVERLAY LOGIC ---
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

            // --- 3. CASUAL/ACTIVE SYNC TIMER ---
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
                        _casualSyncTimerLabel.Text = "Ready!";
                        _casualSyncTimerLabel.TextColor = Color.LimeGreen;
                    }
                    if (_activeSyncTimerLabel != null && _activeSyncTimerLabel.Visible)
                    {
                        _activeSyncTimerLabel.Text = "Ready!";
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

            // --- 4. TOURNAMENT WAITING ROOM ---
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
                    ScreenNotification.ShowNotification("Tournament Has Begun!", ScreenNotification.NotificationType.Warning);
                }
                else
                {
                    if (_waitingRoomLabel != null)
                    {
                        _waitingRoomLabel.Text = $"Starting in: {waitTime.Minutes:D2}:{waitTime.Seconds:D2}";
                    }
                }
            }

            // --- 5. ACTIVE TOURNAMENT TIMER ---
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
                        ScreenNotification.ShowNotification("Tournament over! Weighing all your fish, please wait...", ScreenNotification.NotificationType.Warning, null, 10);
                        _ = CheckApiForNewCatches();
                        _isTourneyWrapUpActive = true;
                        _tourneyWrapUpEndTime = DateTime.Now.AddSeconds(30);
                        _didMidWrapUpPing = false;
                    }
                    else
                    {
                        _ = CompleteTournamentAsync("Tournament Ended", "Submitting your catches to the server...");
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

            // --- 6. TOURNAMENT WRAP-UP LOGIC ---
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
                    _ = CompleteTournamentAsync("Tournament Ended", "Submitting your catches to the server...");
                }
            }
        }


        private async Task ForceUploadPB()
        {
            try
            {
                // 1. Gather all unsubmitted Personal Bests using KeyValuePair so we have the ItemId (the Key)
                var unsubmittedKvps = _personalBests.Where(kvp =>
                    (kvp.Value.BestWeight != null && !kvp.Value.BestWeight.IsSubmitted) ||
                    (kvp.Value.BestLength != null && !kvp.Value.BestLength.IsSubmitted)).ToList();

                // 2. The exact check and message you asked for
                if (!unsubmittedKvps.Any())
                {
                    ScreenNotification.ShowNotification("No new submissions found.", ScreenNotification.NotificationType.Info);
                    return;
                }

                ScreenNotification.ShowNotification($"Pushing {unsubmittedKvps.Count} unsubmitted PBs to Leaderboard...");

                // 3. Process each unsubmitted record
                foreach (var kvp in unsubmittedKvps)
                {
                    int itemId = kvp.Key;
                    var record = kvp.Value;

                    var fishInfo = _allFishEntries.FirstOrDefault(f => f.Data.ItemId == itemId)?.Data;
                    if (fishInfo == null) continue;

                    string safeName = fishInfo.Name.Replace(" ", "_").Replace("'", "").Replace("-", "");
                    string accountName = GameService.Gw2Mumble.PlayerCharacter.Name;
                    if (string.IsNullOrEmpty(accountName)) accountName = "Unknown"; // Fallback if API fails

                    // Weight Submission
                    if (record.BestWeight != null && !record.BestWeight.IsSubmitted && !record.BestWeight.IsCheater)
                    {
                        var payload = new
                        {
                            player_name = accountName,
                            fish_name = safeName,
                            weight = record.BestWeight.Weight,
                            length = 0.0,
                            // Sending current time since your file doesn't track historical catch times
                            catch_time = DateTime.UtcNow.ToString("o")
                        };

                        string jsonPayload = System.Text.Json.JsonSerializer.Serialize(payload);
                        var content = new System.Net.Http.StringContent(jsonPayload, System.Text.Encoding.UTF8, "application/json");

                        using (var client = new System.Net.Http.HttpClient())
                        {
                            var response = await client.PostAsync("https://gilledwars.com/api/submit_catch", content);
                            if (response.IsSuccessStatusCode)
                            {
                                record.BestWeight.IsSubmitted = true;
                            }
                        }
                    }

                    // Length Submission
                    if (record.BestLength != null && !record.BestLength.IsSubmitted && !record.BestLength.IsCheater)
                    {
                        var payload = new
                        {
                            player_name = accountName,
                            fish_name = safeName,
                            weight = 0.0,
                            length = record.BestLength.Length,
                            // Sending current time since your file doesn't track historical catch times
                            catch_time = DateTime.UtcNow.ToString("o")
                        };

                        string jsonPayload = System.Text.Json.JsonSerializer.Serialize(payload);
                        var content = new System.Net.Http.StringContent(jsonPayload, System.Text.Encoding.UTF8, "application/json");

                        using (var client = new System.Net.Http.HttpClient())
                        {
                            var response = await client.PostAsync("https://gilledwars.com/api/submit_catch", content);
                            if (response.IsSuccessStatusCode)
                            {
                                record.BestLength.IsSubmitted = true;
                            }
                        }
                    }
                }

                // Save local state to flag them as submitted permanently
                SavePersonalBests();

                // The exact success message you requested
                ScreenNotification.ShowNotification("Fish submitted to leaderboards, Good Luck!", ScreenNotification.NotificationType.Warning);
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Failed to force upload PBs.");
                ScreenNotification.ShowNotification("Failed to connect to leaderboards.", ScreenNotification.NotificationType.Error);
            }
        }

        private void OnToggleHotkeyActivated(object sender, EventArgs e)
        {
            if (_mainWindow != null)
            {
                _mainWindow.Visible = !_mainWindow.Visible;
            }
            ScreenNotification.ShowNotification("Gilled Wars UI Toggled!");
        }

        protected override void Unload()
        {
            // Unhook global events and hotkeys
            GameService.Input.Mouse.LeftMouseButtonReleased -= OnMouseLeftButtonReleased;
            if (ToggleHotkey != null)
            {
                ToggleHotkey.Value.Activated -= OnToggleHotkeyActivated;
            }

            StopDrfListener();

            // Dispose UI
            _cheaterLabel?.Dispose();
            _cornerIcon?.Dispose();
            _mainWindow?.Dispose();
            _tourneyActivePanel?.Dispose();
            _casualCompactPanel?.Dispose();
            _currentSummaryWindow?.Dispose();
            _targetSelectionWindow?.Dispose();
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
        public Image Icon { get; set; } // Changed from Image to Control
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


        // UI Color Flags
        public bool IsNewPb { get; set; }
        public bool IsSuperPb { get; set; }
    }
}

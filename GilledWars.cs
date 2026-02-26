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

namespace Gorthax.Gilled
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
        }
        protected override void OnModuleLoaded(EventArgs e)
        {
            LoadFishDatabase();

            _ = InitializeAccountAndLoadAsync(); // <--- NEW PATIENT LOADER

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
                if (_mainWindow != null) _mainWindow.Visible = !_mainWindow.Visible;
            };

            ToggleHotkey.Value.Enabled = true;
            ToggleHotkey.Value.Activated += (s, ev) => {
                if (_mainWindow != null) _mainWindow.Visible = !_mainWindow.Visible;
                ScreenNotification.ShowNotification("Gilled Wars UI Toggled!");
            };

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
    // 1. FOLDER MIGRATION LOGIC
    try
    {
        string oldHardcodedPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Guild Wars 2", "addons", "blishhud", "gilledwarsanglers");
        string newBlishPath = ModuleDirectory; // This uses the path from DirectoriesManager

        if (Directory.Exists(oldHardcodedPath) && !Directory.Exists(newBlishPath))
        {
            Logger.Info($"Migrating old folder {oldHardcodedPath} to {newBlishPath}");
            // Move handles the move across same drive or copy/delete across different drives
            Directory.Move(oldHardcodedPath, newBlishPath);
        }
    }
    catch (Exception ex) { Logger.Warn(ex, "Failed to migrate old module folder."); }

    // 2. ACCOUNT INITIALIZATION
    try
    {
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
            catch { Logger.Warn("Custom API Key failed. Falling back to Blish HUD API."); }
        }

        if (!accountFound)
        {
            int retries = 10; 
            while (retries > 0)
            {
                if (Gw2ApiManager.HasPermissions(new[] { Gw2Sharp.WebApi.V2.Models.TokenPermission.Account }))
                {
                    var acc = await Gw2ApiManager.Gw2ApiClient.V2.Account.GetAsync();
                    _localAccountName = acc.Name.Replace(".", "_");
                    accountFound = true;
                    break; 
                }
                await Task.Delay(1000); 
                retries--;
            }
        }

        // 3. FILE MIGRATION (Move generic .json to account-specific .json)
        if (accountFound)
        {
            string oldFile = Path.Combine(ModuleDirectory, "personal_bests.json");
            string newFile = Path.Combine(ModuleDirectory, $"personal_bests_{_localAccountName}.json");

            if (File.Exists(oldFile) && !File.Exists(newFile))
            {
                File.Move(oldFile, newFile);
            }
        }
    }
    catch (Exception ex) { Logger.Error(ex, "Fatal error during account initialization."); }

    LoadPersonalBests();
    RefreshFishLogUI();
}

   private void LoadPersonalBests()
{
    string fileName = _localAccountName == "UnknownAccount" ? "personal_bests.json" : $"personal_bests_{_localAccountName}.json";
    string path = Path.Combine(ModuleDirectory, fileName);

    _isCheater = false;

    if (File.Exists(path))
    {
        try
        {
            string json = File.ReadAllText(path);
            var loaded = JsonConvert.DeserializeObject<Dictionary<int, PersonalBestRecord>>(json) ?? new Dictionary<int, PersonalBestRecord>();
            _personalBests = new Dictionary<int, PersonalBestRecord>();

            string seed = GetGlobalSeed();

            foreach (var kvp in loaded)
            {
                int itemId = kvp.Key;
                var rec = kvp.Value;
                var dbFish = _allFishEntries.FirstOrDefault(x => x.Data.ItemId == itemId)?.Data;
                string fishName = dbFish != null ? dbFish.Name : "Unknown";

                void ValidateRecord(SubRecord sub)
                {
                    if (sub == null) return;
                    string cName = sub.CharacterName ?? "Unknown";
                    string expectedSig = GenerateSignature(sub.Weight, sub.Length, fishName, sub.IsSuperPb, seed + cName + _localAccountName);

                    if (sub.Signature != expectedSig)
                    {
                        sub.IsCheater = true;
                        _isCheater = true;
                    }
                }

                ValidateRecord(rec.BestWeight);
                ValidateRecord(rec.BestLength);

                _personalBests.Add(itemId, rec);
                _caughtFishIds.Add(itemId);
            }
        }
        catch (Exception ex) { Logger.Error(ex, "Failed to load personal bests."); }
    }
}

private void SavePersonalBests()
{
    string fileName = _localAccountName == "UnknownAccount" ? "personal_bests.json" : $"personal_bests_{_localAccountName}.json";
    string path = Path.Combine(ModuleDirectory, fileName);

    try {
        File.WriteAllText(path, JsonConvert.SerializeObject(_personalBests));
    } catch (Exception ex) {
        Logger.Error(ex, "Failed to save personal bests.");
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
            string saltedSeed = GetGlobalSeed() + charName;

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

            if (isNewPbWeight || isNewPbLength) SavePersonalBests(); RefreshFishLogUI();

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
            _mainWindow = new Panel { Title = "Gilled Wars Anglers", ShowBorder = true, Size = new Point(620, 580), Location = new Point(300, 300), Parent = GameService.Graphics.SpriteScreen, Visible = false, BackgroundColor = new Color(30, 30, 30, 180) };
            _mainWindow.LeftMouseButtonPressed += (s, ev) => {
                if (GameService.Input.Mouse.ActiveControl == _mainWindow) { _isDragging = true; _dragOffset = new Point(GameService.Input.Mouse.PositionRaw.X - _mainWindow.Location.X, GameService.Input.Mouse.PositionRaw.Y - _mainWindow.Location.Y); }
            };

            var casualBtn = new StandardButton { Text = "Casual Fishing", Parent = _mainWindow, Location = new Point(10, 10), Width = 150 };
            var tourneyBtn = new StandardButton { Text = "Tournament Mode", Parent = _mainWindow, Location = new Point(170, 10), Width = 150 };

            _casualPanel = new Panel { Parent = _mainWindow, Size = new Point(600, 500), Location = new Point(10, 50), Visible = true };
            _tournamentPanel = new Panel { Parent = _mainWindow, Size = new Point(600, 500), Location = new Point(10, 50), Visible = false };

            casualBtn.Click += (s, ev) => { _casualPanel.Visible = true; _tournamentPanel.Visible = false; };
            tourneyBtn.Click += (s, ev) => { _casualPanel.Visible = false; _tournamentPanel.Visible = true; };

            _fishLogPanel = new Panel { Parent = _casualPanel, Location = new Point(0, 65), Size = new Point(_casualPanel.Width, _casualPanel.Height - 65), Visible = false };

            BuildFishLogGrid(_fishLogPanel);
            BuildCasualUI(_casualPanel);
            BuildTournamentUI(_tournamentPanel);
            BuildActiveTournamentWidget();
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

        private void BuildFishLogGrid(Panel parent)
        {
            if (parent != null)
            {
                var filterPanel = new Panel { Parent = parent, Location = new Point(10, 0), Size = new Point(580, 110) };

                var searchBar = new TextBox { Parent = filterPanel, Location = new Point(0, 0), Width = 115, PlaceholderText = "Search..." };
                var rarityDrop = new Dropdown { Parent = filterPanel, Location = new Point(120, 0), Width = 125 };
                var locationDrop = new Dropdown { Parent = filterPanel, Location = new Point(250, 0), Width = 150 };
                var holeDrop = new Dropdown { Parent = filterPanel, Location = new Point(405, 0), Width = 165 };

                var timeDrop = new Dropdown { Parent = filterPanel, Location = new Point(0, 35), Width = 115 };
                var baitDrop = new Dropdown { Parent = filterPanel, Location = new Point(120, 35), Width = 125 };
                var collapseBtn = new StandardButton { Text = "Collapse", Parent = filterPanel, Location = new Point(250, 35), Width = 75 };
                var revealBtn = new StandardButton { Text = "Reveal", Parent = filterPanel, Location = new Point(330, 35), Width = 70 };
                var resetFiltersBtn = new StandardButton { Text = "Reset Filters", Parent = filterPanel, Location = new Point(0, 70), Width = 100, BasicTooltipText = "Clear all active filters." };
                var pushLeaderboardBtn = new StandardButton
                {
                    Text = "Push PBs to Leaderboard",
                    Parent = filterPanel,
                    Location = new Point(120, 70),
                    Width = 200,
                    BasicTooltipText = "Submit your valid DRF-tracked catches to the global leaderboards!"
                };

                pushLeaderboardBtn.Click += async (sender, ev) =>
                {
                    if (_isCheater)
                    {
                        ScreenNotification.ShowNotification("Submission Rejected: Tampered Data Detected", ScreenNotification.NotificationType.Error);
                        return;
                    }

                    // --- 5 MINUTE COOLDOWN CHECK ---
                    if ((DateTime.Now - _lastSubmitTime).TotalMinutes < 5)
                    {
                        ScreenNotification.ShowNotification("Button on Cool down. Please wait a few minutes.", ScreenNotification.NotificationType.Warning);
                        return;
                    }

                    pushLeaderboardBtn.Enabled = false;
                    pushLeaderboardBtn.Text = "Pushing...";

                    // --- NEW: FETCH ACCOUNT NAME ---
                    string accountName = "UnknownAccount";
                    try
                    {
                        if (Gw2ApiManager.HasPermissions(new[] { Gw2Sharp.WebApi.V2.Models.TokenPermission.Account }))
                        {
                            var acc = await Gw2ApiManager.Gw2ApiClient.V2.Account.GetAsync();
                            accountName = acc.Name;
                        }
                    }
                    catch { Logger.Warn("Could not fetch Account Name for Leaderboard."); }

                    var eligibleCatches = new List<object>();
                    string charName = GameService.Gw2Mumble.PlayerCharacter.Name ?? "UnknownPlayer";

                    // Track which records we are sending so we can flag them as submitted later
                    var submittedWeights = new List<SubRecord>();
                    var submittedLengths = new List<SubRecord>();

                    foreach (var kvp in _personalBests)
                    {
                        int fId = kvp.Key;
                        var rec = kvp.Value;
                        var dbFish = _allFishEntries.FirstOrDefault(x => x.Data.ItemId == fId)?.Data;
                        string fName = dbFish != null ? dbFish.Name : "Unknown";
                        string fLoc = dbFish != null ? dbFish.Location : "Unknown";

                        // Check Weight Record - Send stored signature directly
                        if (rec.BestWeight != null && rec.BestWeight.CaughtWithDrf && !rec.BestWeight.IsCheater && !rec.BestWeight.IsSubmitted)
                        {
                            eligibleCatches.Add(new
                            {
                                accountName = _localAccountName,
                                itemId = fId,
                                name = fName,
                                weight = rec.BestWeight.Weight,
                                length = rec.BestWeight.Length,
                                signature = rec.BestWeight.Signature, // Use the signature exactly as stored
                                isSuper = rec.BestWeight.IsSuperPb,
                                type = "weight",
                                characterName = rec.BestWeight.CharacterName ?? "Unknown",
                                location = fLoc
                            });
                            submittedWeights.Add(rec.BestWeight);
                        }

                        // Check Length Record - Send stored signature directly
                        if (rec.BestLength != null && rec.BestLength.CaughtWithDrf && !rec.BestLength.IsCheater && !rec.BestLength.IsSubmitted)
                        {
                            eligibleCatches.Add(new
                            {
                                accountName = _localAccountName,
                                itemId = fId,
                                name = fName,
                                weight = rec.BestLength.Weight,
                                length = rec.BestLength.Length,
                                signature = rec.BestLength.Signature, // Use the signature exactly as stored
                                isSuper = rec.BestLength.IsSuperPb,
                                type = "length",
                                characterName = rec.BestLength.CharacterName ?? "Unknown",
                                location = fLoc
                            });
                            submittedLengths.Add(rec.BestLength);
                        }
                    }

                    if (eligibleCatches.Count == 0)
                    {
                        ScreenNotification.ShowNotification("No new PB recorded.", ScreenNotification.NotificationType.Warning);
                        pushLeaderboardBtn.Enabled = true;
                        pushLeaderboardBtn.Text = "Push PBs to Leaderboard";
                        return;
                    }

                    // Set cooldown
                    _lastSubmitTime = DateTime.Now;

                    var payload = new { catches = eligibleCatches };
                    try
                    {
                        string jsonPayload = JsonConvert.SerializeObject(payload);
                        var content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");

                        var response = await _httpClient.PostAsync($"{API_BASE_URL}/submit-leaderboard", content);

                        if (response.IsSuccessStatusCode)
                        {
                            // Flag all successfully sent catches as submitted and save the file
                            foreach (var w in submittedWeights) w.IsSubmitted = true;
                            foreach (var l in submittedLengths) l.IsSubmitted = true;
                            SavePersonalBests();

                            ScreenNotification.ShowNotification("PB submitted to the Leaderboards, good luck!");
                        }
                        else
                        {
                            ScreenNotification.ShowNotification("Server Error: Leaderboard update failed.", ScreenNotification.NotificationType.Error);
                        }
                    }
                    catch (Exception ex)
                    {
                        Logger.Error(ex, "Failed to submit to leaderboards.");
                        ScreenNotification.ShowNotification("Network Error!", ScreenNotification.NotificationType.Error);
                    }

                    pushLeaderboardBtn.Enabled = true;
                    pushLeaderboardBtn.Text = "Push PBs to Leaderboard";
                };
                var scroll = new FlowPanel { Parent = parent, Location = new Point(10, 115), Size = new Point(580, parent.Height - 120), CanScroll = true, FlowDirection = ControlFlowDirection.SingleTopToBottom };

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

                            if (match) { anyVisible = true; entry.Icon.Parent = null; entry.Icon.Parent = cat; entry.Icon.Visible = true; }
                            else { entry.Icon.Parent = null; entry.Icon.Visible = false; }
                        }
                        if (anyVisible) { cat.Parent = null; cat.Parent = scroll; cat.Visible = true; }
                        else { cat.Parent = null; cat.Visible = false; }
                    }
                }

                searchBar.TextChanged += (sender, ev) => ApplyFilters();
                rarityDrop.ValueChanged += (sender, ev) => ApplyFilters();
                locationDrop.ValueChanged += (sender, ev) => ApplyFilters();
                holeDrop.ValueChanged += (sender, ev) => ApplyFilters();
                timeDrop.ValueChanged += (sender, ev) => ApplyFilters();
                baitDrop.ValueChanged += (sender, ev) => ApplyFilters();

                collapseBtn.Click += (sender, ev) => { foreach (var c in _categoryPanels) c.Collapsed = true; };
                revealBtn.Click += (sender, ev) => { foreach (var c in _categoryPanels) c.Collapsed = false; };

                resetFiltersBtn.Click += (sender, ev) => {
                    searchBar.Text = "";
                    rarityDrop.SelectedItem = "All Rarities";
                    locationDrop.SelectedItem = "All Locations";
                    holeDrop.SelectedItem = "All Holes";
                    timeDrop.SelectedItem = "All Times";
                    baitDrop.SelectedItem = "All Baits";
                    ApplyFilters();
                };

                try
                {
                    var uniqueHoles = new HashSet<string>();
                    foreach (var entry in _allFishEntries)
                    {
                        var f = entry.Data;
                        if (string.IsNullOrEmpty(f.FishingHole)) f.FishingHole = "Unknown";
                        f.FishingHole = f.FishingHole.Replace("None, ", "");
                        if (f.FishingHole == "None") f.FishingHole = "Open Water";

                        var splitHoles = f.FishingHole.Split(',').Select(h => h.Trim());
                        foreach (var sh in splitHoles) if (!string.IsNullOrEmpty(sh)) uniqueHoles.Add(sh);
                    }

                    rarityDrop.Items.Add("All Rarities"); foreach (var r in _allFishEntries.Select(x => x.Data.Rarity).Distinct()) rarityDrop.Items.Add(r); rarityDrop.SelectedItem = "All Rarities";
                    locationDrop.Items.Add("All Locations"); foreach (var l in _allFishEntries.Select(x => x.Data.Location).Distinct()) locationDrop.Items.Add(l); locationDrop.SelectedItem = "All Locations";
                    holeDrop.Items.Add("All Holes"); foreach (var h in uniqueHoles.OrderBy(x => x)) holeDrop.Items.Add(h); holeDrop.SelectedItem = "All Holes";

                    timeDrop.Items.Add("All Times"); timeDrop.Items.Add("Daytime"); timeDrop.Items.Add("Nighttime"); timeDrop.Items.Add("Dawn/Dusk"); timeDrop.Items.Add("Any"); timeDrop.SelectedItem = "All Times";
                    baitDrop.Items.Add("All Baits"); foreach (var b in _allFishEntries.Select(x => x.Data.Bait).Distinct()) baitDrop.Items.Add(b); baitDrop.SelectedItem = "All Baits";

                    foreach (var group in _allFishEntries.Select(x => x.Data).GroupBy(x => x.Location).OrderBy(x => x.Key))
                    {
                        var p = new FlowPanel { Parent = scroll, Title = group.Key, CanCollapse = true, ShowBorder = true, Width = 550, HeightSizingMode = SizingMode.AutoSize, FlowDirection = ControlFlowDirection.LeftToRight };
                        _categoryPanels.Add(p);
                        foreach (var fish in group)
                        {
                            string safeName = fish.Name.Replace(" ", "_").Replace("'", "").Replace("-", "");
                            bool isCaught = _caughtFishIds.Contains(fish.ItemId);
                            string pbWText = "NONE LOGGED";
                            string pbLText = "NONE LOGGED";
                            var tintColor = isCaught ? Color.White : Color.Gray * 0.5f;

                            if (_personalBests.ContainsKey(fish.ItemId))
                            {
                                var rec = _personalBests[fish.ItemId];

                                if (rec.BestWeight != null)
                                {
                                    if (rec.BestWeight.IsCheater) pbWText = "CHEATER DETECTED";
                                    else if (rec.BestWeight.IsSuperPb) { pbWText = $"[SUPER] {rec.BestWeight.Weight} lbs | {rec.BestWeight.Length} in"; tintColor = Color.Gold; }
                                    else pbWText = $"{rec.BestWeight.Weight} lbs | {rec.BestWeight.Length} in";
                                }

                                if (rec.BestLength != null)
                                {
                                    if (rec.BestLength.IsCheater) pbLText = "CHEATER DETECTED";
                                    else if (rec.BestLength.IsSuperPb) { pbLText = $"[SUPER] {rec.BestLength.Length} in | {rec.BestLength.Weight} lbs"; tintColor = Color.Gold; }
                                    else pbLText = $"{rec.BestLength.Length} in | {rec.BestLength.Weight} lbs";
                                }
                            }

                            var img = new Image
                            {
                                Parent = p,
                                Size = new Point(64, 64),
                                BasicTooltipText = $"{fish.Name}\nRarity: {fish.Rarity}\nLocation: {fish.Location}\nHole: {fish.FishingHole}\nTime: {fish.Time}\nBait: {fish.Bait}\n\nPB Weight: {pbWText}\nPB Length: {pbLText}",
                                Texture = ContentsManager.GetTexture($"images/{safeName}.png"),
                                Tint = tintColor
                            };

                            img.Click += (sender, ev) => {
                                byte[] linkData = new byte[6]; linkData[0] = 0x02; linkData[1] = 0x01;
                                BitConverter.GetBytes(fish.ItemId).CopyTo(linkData, 2);
                                CopyToClipboard($"[&{Convert.ToBase64String(linkData)}]");
                                ScreenNotification.ShowNotification($"Link copied: {fish.Name}");
                            };

                            var entry = _allFishEntries.First(x => x.Data.ItemId == fish.ItemId);
                            entry.Icon = img;
                            entry.CategoryPanel = p;
                        }
                    }
                }
                catch (Exception ex) { Logger.Error(ex, "UI Build Fail"); }
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

        private void RefreshFishLogUI()
        {
            foreach (var entry in _allFishEntries)
            {
                var fish = entry.Data;
                var img = entry.Icon;
                if (img == null) continue;

                bool isCaught = _caughtFishIds.Contains(fish.ItemId) || _personalBests.ContainsKey(fish.ItemId);
                string pbWText = "NONE LOGGED";
                string pbLText = "NONE LOGGED";
                var tint = isCaught ? Color.White : Color.Gray * 0.5f;

                if (_personalBests.TryGetValue(fish.ItemId, out var rec))
                {
                    if (rec.BestWeight != null)
                    {
                        if (rec.BestWeight.IsCheater) pbWText = "CHEATER DETECTED";
                        else if (rec.BestWeight.IsSuperPb)
                        {
                            pbWText = $"[SUPER] {rec.BestWeight.Weight} lbs | {rec.BestWeight.Length} in";
                            tint = Color.Gold;
                        }
                        else pbWText = $"{rec.BestWeight.Weight} lbs | {rec.BestWeight.Length} in";
                    }

                    if (rec.BestLength != null)
                    {
                        if (rec.BestLength.IsCheater) pbLText = "CHEATER DETECTED";
                        else if (rec.BestLength.IsSuperPb)
                        {
                            pbLText = $"[SUPER] {rec.BestLength.Length} in | {rec.BestLength.Weight} lbs";
                            tint = Color.Gold;
                        }
                        else pbLText = $"{rec.BestLength.Length} in | {rec.BestLength.Weight} lbs";
                    }
                }

                img.Tint = tint;
                img.BasicTooltipText = $"{fish.Name}\nRarity: {fish.Rarity}\nLocation: {fish.Location}\nHole: {fish.FishingHole}\nTime: {fish.Time}\nBait: {fish.Bait}\n\nPB Weight: {pbWText}\nPB Length: {pbLText}";
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
            if (_isDragging && _mainWindow != null) _mainWindow.Location = new Point(GameService.Input.Mouse.PositionRaw.X - _dragOffset.X, GameService.Input.Mouse.PositionRaw.Y - _dragOffset.Y);
            if (_isActivePanelDragging && _tourneyActivePanel != null) _tourneyActivePanel.Location = new Point(GameService.Input.Mouse.PositionRaw.X - _activePanelDragOffset.X, GameService.Input.Mouse.PositionRaw.Y - _activePanelDragOffset.Y);
            if (_isDraggingSummary && _currentSummaryWindow != null) _currentSummaryWindow.Location = new Point(GameService.Input.Mouse.PositionRaw.X - _summaryDragOffset.X, GameService.Input.Mouse.PositionRaw.Y - _summaryDragOffset.Y);
            if (_isCompactDragging && _casualCompactPanel != null) _casualCompactPanel.Location = new Point(GameService.Input.Mouse.PositionRaw.X - _compactDragOffset.X, GameService.Input.Mouse.PositionRaw.Y - _compactDragOffset.Y);
            if (_isDraggingTarget && _targetSelectionWindow != null) _targetSelectionWindow.Location = new Point(GameService.Input.Mouse.PositionRaw.X - _targetDragOffset.X, GameService.Input.Mouse.PositionRaw.Y - _targetDragOffset.Y);

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
                    if (_waitingRoomLabel != null) _waitingRoomLabel.Text = $"Starting in: {waitTime.Minutes:D2}:{waitTime.Seconds:D2}";
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
                    if (_activeTimerLabel != null) _activeTimerLabel.Text = string.Format("{0:D2}:{1:D2}", rem.Minutes, rem.Seconds);
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
                    _ = CompleteTournamentAsync("Tournament Ended", "Submitting your catches to the server...");
                }
            }
        }

        protected override void Unload()
        {
            GameService.Input.Mouse.LeftMouseButtonReleased -= OnMouseLeftButtonReleased;
            StopDrfListener();
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
        public Image Icon { get; set; }
        public FlowPanel CategoryPanel { get; set; }
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


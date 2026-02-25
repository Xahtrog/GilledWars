# 🎣 Gilled Wars

**Gilled Wars** is the ultimate competitive fishing module for Guild Wars 2 (via Blish HUD). Track your Personal Bests, push your catches to a live Global Leaderboard, and host real-time fishing tournaments with your friends!

## ✨ Features

* **Global Leaderboards:** Compete against the community for the Heaviest and Longest catches across all of Tyria.
* **Real-Time Tracking (DRF):** Utilizes the `drf.rs` memory reader to instantly detect when you catch a fish and calculate its precise weight and length.
* **API Fallback Tracking:** Don't want to use memory readers? You can still track your catches using the official GW2 API (updates every 5 minutes based on inventory changes).
* **Live Fish Log & Cooler:** View your lifetime Personal Bests for every fish, complete with Location, Hole, Bait, and Time-of-Day requirements.
* **Custom Tournaments:** Host private fishing tournaments with custom time limits, delayed starts, and specific target species. 
* **Discord Integration:** Automatically post tournament results to your guild's Discord server via Webhooks.
* **Anti-Cheat Security:** Submissions to the Global Leaderboard are mathematically cryptographically signed to prevent spoofing and tampered data.

---

## 📥 Installation

1. Install **[Blish HUD](https://blishhud.com/)**.
2. Open the Blish HUD module repo in-game.
3. Search for **Gilled Wars** and click Install.
4. Enable the module.

---

## ⚙️ Setup & Configuration

To get the most out of Gilled Wars, you need to configure a few settings in the Blish HUD Module Settings menu.

### 1. GW2 API Key (Required for API Tracking)
If you are playing without the DRF memory reader, the module needs to scan your bags to see what you caught.
* Go to your [ArenaNet Account Page](https://account.arena.net/applications).
* Create a new API key with the following permissions: **`inventories`**, **`characters`**, and **`progression`**.
* Paste the key into the **Custom API Key** field in the module settings.

### 2. DRF Token (Required for Real-Time Tracking & Global Leaderboards)
To submit catches to the Global Leaderboard, you **must** use DRF. This ensures all weights and lengths are verified and cheat-free.
* Download and install the [drf.rs addon](https://github.com/dlamkins/drf.rs).
* Follow the DRF instructions to generate your personal WebSocket Token.
* Paste your token into the **DRF Token** field in the module settings.

### 3. Discord Webhook (Optional - For Tournament Hosts)
* In your Discord Server, go to Channel Settings -> Integrations -> Webhooks -> New Webhook.
* Copy the Webhook URL.
* Paste it into the **Discord Webhook URL** field in the module settings.

---

## 📖 How to Use

Press **`Ctrl + Alt + F`** (default) to toggle the Gilled Wars UI.

### 🎒 Casual Fishing & Fish Log
1. Click **Start Logging**. 
2. If using DRF, your catches will appear in your "Fish Cooler" the exact second you reel them in. 
3. If using the API, the module will take a snapshot of your inventory. Click **Measure Fish** every 5 minutes to scan your bags for new catches.
4. Click **Fish Log** to view your Personal Bests. Your log can be filtered by Location, Rarity, Bait, and Time of Day.
5. Click **Push PBs to Leaderboard** to send your verified catches to the Global Database!

### 🏆 Tournament Mode
**Hosting a Tournament:**
1. Switch to the **Tournament Mode** tab and click **Host**.
2. Set your Start Delay, Duration, Tracking Mode (DRF vs API), Target Species, and Win Factor (Heaviest vs Longest).
3. Click **Create Room**. This will generate a unique 5-character Room Code (e.g., `GW-ABC12`) and copy it to your clipboard.
4. Share this code with your friends!

**Joining a Tournament:**
1. Switch to the **Tournament Mode** tab and click **Participant**.
2. Paste the Room Code provided by the host.
3. Click **Join Room**. You will be placed in the Waiting Room until the host's start delay finishes.
4. Once the timer ends, start fishing! 
5. When the tournament timer runs out, your top 5 catches will be automatically packaged, verified, and sent to the Host's Discord webhook.

---

## ❓ FAQ & Troubleshooting

* **My fish log says "NONE LOGGED" even though I caught a fish!**
  Ensure you have clicked "Start Logging" before fishing. If you are using API mode, you must click "Measure Fish" for the catches to register. 
* **The "Push PBs to Leaderboard" button rejected my submission!**
  Only catches tracked using **DRF (Real-Time)** are eligible for the Global Leaderboards. API-tracked catches are local only. Furthermore, modifying your `personal_bests.json` file manually will trigger the Anti-Cheat and permanently flag those entries as invalid.
* **My UI is lagging when I leave the window open.**
  Click the **Compact** button to shrink the UI down to a tiny, non-intrusive Fish Cooler widget!

---

## 📜 License
This project is licensed under the MIT License - see the LICENSE file for details.

*Not affiliated with ArenaNet, Guild Wars 2, or NCSOFT. All Guild Wars 2 assets and data belong to ArenaNet.*

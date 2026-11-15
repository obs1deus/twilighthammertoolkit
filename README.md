# Twilight Hammer Toolkit

A one-stop Windows installer for [OctoWoW](https://octowow.st/): Twilight Hammer Sync, popular raid addons, client mods, and optional client tweaks, all from a single app.


Current version: **v0.1.8**
<img width="668" height="900" alt="image" src="https://github.com/user-attachments/assets/99a3605f-5b9b-4b88-802d-191c86275dd5" />


- **Guild Portal:** https://twilighthammer.hccpride.workers.dev/
- **Releases:** https://github.com/obs1deus/twilighthammertoolkit/releases

## Features

- **Twilight Hammer Sync** installer with three editions (Guild Sync, Auction Only, Bank and Guild Sync) and a background uploader.
- **Addon and client-mod catalog** with raid addons, loot helpers, and client-side mods. The Toolkit downloads each component from its upstream source.
- **Client tweaks** for the OctoWoW Modernized client: auto-loot, memory, nameplate range, field of view, render distance, ground clutter, camera distance, and background sound.
- **Check my PC** recommends a starting render distance based on your RAM and CPU threads.
- **Automatic dependency handling.** Required components are added to the install plan for you, and detected prerequisites are reused.
- **Backups** of the executable, config, and replaced package files before anything is changed.
- **Update checks** for the Toolkit itself and for the addons and client tools it installed.
- **Windows Security help** for when an upstream file gets blocked.
- **No extras needed.** No Node.js, no PowerShell, and no administrator access when your game folder is writable.

## Requirements

- Windows
- An OctoWoW install (the folder containing `WoW.exe`)
- OctoWoW closed while you install

The modern interface uses Microsoft Edge WebView2. If WebView2 can't start, the Toolkit opens the native Windows interface instead, with the same options.

## Getting started

1. Download the latest zip from [Releases](https://github.com/obs1deus/twilighthammertoolkit/releases) and extract it.
2. Close OctoWoW and run `TwilightHammerToolkit.exe`.
3. Choose your game folder. Double-click the folder field or press **F4** in it to browse.
4. Pick a Sync edition, or choose **Skip Sync** if you only want addons or client tweaks.
5. Tick any optional components you want. Everything starts unchecked.
6. Optionally turn on **Apply client tweaks** and adjust the values.
7. Review the install plan and confirm.

If you installed DLL mods, start the game with **VanillaFixes** so they load.

## Twilight Hammer Sync

Sync captures game data with an in-game addon and uploads it to the Twilight Hammer guild portal.

| Edition | What it captures |
| --- | --- |
| **Guild Sync** | Professions, the Twilight Hammer roster, and auction scans |
| **Auction Only** | Completed Aegis Exchange, Aux, and OctoAuctionator scans |
| **Bank and Guild Sync** | Everything in Guild Sync, plus Twilightbank bags, bank contents, and gold |

**How the uploader works**

- It registers a Windows scheduled task that checks your OctoWoW game files for changes every two minutes while you're signed in to Windows.
- New captures are sent over HTTPS.
- These editions upload without a device key or Discord sign-in.
- Choosing a Sync edition replaces any existing Twilight Hammer Sync setup.

**Getting captures to save**

Check that the addon is enabled at character selection. With SuperWoW installed, captures save immediately. Without it, log out or reload to write them to disk.

**Uninstalling Sync**

Click **Uninstall Sync** under AddOns & Tools. This removes the uploader and its scheduled task, and you can optionally remove the WoW addon too (close OctoWoW first). Saved captures, sync logs, and portal data are kept.

## AddOns & Tools

Optional components are grouped like this in the app.

### Must-have raid addons

| Component | What it does |
| --- | --- |
| TWThreat | Threat meter showing your threat relative to the tank |
| Rook's RaidVisuals | OctoWoW client patch that changes spell visuals and sounds to make raid mechanics easier to see |
| BigWigs | Boss ability timers and raid warnings (prodigeomix fork) |

### More raid & character addons

| Component | What it does |
| --- | --- |
| _LazyPig | Configurable shortcuts and automation, such as dismounting and accepting invites |
| DopingControl | Checks raid consumables, buffs, debuffs, resistances, hit, and enchants before a pull |
| AutoMarker | Automatically assigns raid target icons to configured enemies |
| RollFor | Helps manage loot rolls and track who rolled on an item |
| SmartLoot | Moves and compacts loot roll frames, and can remember your Need, Greed, or Pass choices |
| BetterCharacterStats | Adds more detailed combat stats to the character panel |
| Rinse | Helps identify and dispel removable debuffs on party and raid members |
| ChronicleCompanion | Adds extended unit data to combat logs for ChronicleClassic (requires Nampower) |

### Client tools & compatibility

| Component | What it does |
| --- | --- |
| Nampower | Client mod that improves spell handling and adds functions for compatible addons |
| NampowerSettings | In-game settings panel for Nampower |
| ClassicAPI | Adds newer WoW API functions to the vanilla client |
| UnitXP_SP3 | Client mod (brues-code) that exposes extra unit information, including distances |
| UnitXP_SP3_Addon | Companion addon for UnitXP_SP3 |
| VanillaFixes | Launcher and fixes for the 1.12 client that can also load compatible DLL mods |
| VanillaHelpers | Helper library adding file access, minimap functions, and graphics features |
| SuperWoW | Extends vanilla APIs, including the live file export that Sync uses |
| SuperAPI | Companion compatibility addon for SuperWoW |
| Chronicle Sidekick | Windows tray app that uploads combat logs to Chronicle |

**Chronicle Sidekick** installs as its own app and can be opened from the Start menu. Sign in to Chronicle there to upload logs.

### Auction House addon

Choose one, or keep your current setup:

- **Aegis Exchange**: auction search, full scans, and pricing tools
- **Aux**: auction search, pricing, and posting tools
- **OctoAuctionator**

## Client tweaks

Tweaks apply to the verified OctoWoW Modernized client (`WoW_Modernized.exe`) and `Config.wtf`. They're opt-in: nothing changes unless you tick **Apply client tweaks**, and unchecked switches leave the client's current behavior alone.

| Tweak | Default in app | Notes |
| --- | --- | --- |
| Always auto-loot | Off | Loots automatically. Hold the bound auto-loot key (Shift by default) for manual loot |
| Large Address Aware | On | Lets the 32-bit client use more than 2 GB of memory |
| Nameplate range | 30 yd | Vanilla is 20, Classic is 41 (range 1 to 100) |
| Field of view | 110° | Vanilla is 90°. 110° suits widescreen (range 60 to 150) |
| Render distance | 2200 | Vanilla is 777 (range 777 to 3000). Very high values can crash when entering the world |
| Ground clutter distance | 300 | Vanilla is 70 (range 0 to 1000) |
| Camera distance | 75 | Vanilla is 50. Toolkit limit is 100 |
| Background sounds | On | Keeps game audio playing while the window is in the background |

**Check my PC** estimates a starting render distance from your RAM and CPU threads. **Use recommendation** fills the field so you can review it before applying.

## Backups

Before applying changes, the Toolkit saves the previous executable, config, and any replaced package files to a timestamped folder:

```
<game folder>\.twilight-toolkit\backups\yyyyMMdd-HHmmss
```

## Updates

**Check for updates** looks for new Toolkit releases on GitHub and for new versions of the catalog addons and client tools the Toolkit installed. Installs made outside the Toolkit may show an unknown version. The Toolkit update link opens the GitHub release page.

## Windows Security

Some upstream mods (DLL injectors in particular) can trigger antivirus warnings. If Windows blocks a downloaded file, click **Windows Security** in the footer. It gives you:

- Steps for reviewing the entry in Protection History
- Copyable game-folder and Toolkit-folder paths
- A link to submit a suspected false positive to Microsoft
- **Check again**, which re-checks selected or Toolkit-managed component files

The Toolkit never changes your antivirus settings. Compare the detected file and publisher against the upstream release before allowing anything, and if a blocked download stops setup, rerun with that component unchecked.

## Downloads and security

- Components are downloaded from their upstream GitHub releases or the OctoWoW git host (`octowow.st`) only. Other hosts are rejected.
- Downloads are size-limited and redirect-limited, and setup stops if an upstream source changes mid-install so you can retry against the current revision.
- Everything installs into your OctoWoW folder. The Toolkit doesn't need administrator access when that folder is writable.

## Credits

The Toolkit packages and installs work by many other authors. Each component comes from its own upstream project and keeps its own license. Thanks to the authors of BigWigs (prodigeomix), RollFor, _LazyPig, AutoMarker, SmartLoot, BetterCharacterStats, Rinse, DopingControl, ChronicleCompanion, Chronicle Sidekick, Nampower, ClassicAPI, UnitXP_SP3, VanillaFixes, VanillaHelpers, SuperWoW, SuperAPI, Aegis Exchange, Aux, OctoAuctionator, and the rest of the OctoWoW community.

The modern interface uses Microsoft Edge WebView2. See `WebView2-LICENSE.txt` and `WebView2-NOTICE.txt` for its license and notices.

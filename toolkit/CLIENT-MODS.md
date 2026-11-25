# Client plugins and restore history

Mods → Client includes AuctionQueryThrottle, PerfBoost, TransmogFix, WeirdPerformance, BigCursor, CustomAssets, LogSessions, MinimapIcons, PNG Screenshots, WorldMarkers and the existing client extensions. AutoLogin is an integrated toggle in Client Tweaks. Editing is unlocked; Apply Tweaks opens a review before changing files.

Plugins download at install time. Standalone WeirdUtils modules use the upstream v0.7.0 releases, with WorldMarkers v0.7.1. PerfBoost uses the checksum-verified RetroCro archive; WeirdPerformance uses the checksum-verified Dusk payload because its original Dusk92 release repository is unavailable. URLs and SHA-256 pins are recorded in ClientModSources.cs. AuctionQueryThrottle follows brues-code/AuctionQueryThrottle releases. No third-party DLLs are bundled in the Launcher.

Sources:
- https://codeberg.org/MarcelineVQ/WeirdUtils
- https://github.com/RetroCro/TurtleWoW-Mods
- https://github.com/brues-code/AuctionQueryThrottle
- https://github.com/MarcelineVQ/turtle-autologin
- https://octowow.st/git/Dusk/Modernization-Tool
- https://github.com/tubtubs/vanilla-tweaks (MIT; custom login-screen patch offsets)
- https://github.com/hannesmann/vanillafixes

AutoLogin requires Nampower and a compatible client with custom login-screen support. The Launcher checks compatibility, backs up changes, and uses VanillaFixes to launch the supported client. The original game executable is not patched. A per-user WOW_ENCRYPTION_KEY is created only if none exists, reused for Nampower DPAPI encryption, passed to the launched process, and never logged. The game mod saves accounts in Imports/logins.txt. Keep this file private. Disabling AutoLogin removes its load entry and Lua/XML files with backups, preserving other login-screen entries, saved accounts, and the encryption key.

History & Restore records file changes starting with this build, including newly installed files and package metadata. Restoring backs up the current files, validates backup hashes, rejects conflicts with later edits, and records the restore so it can be undone. Existing older backup folders remain on disk but cannot provide complete rollback receipts. This history covers game-folder addon, mod and tweak files; it does not reverse Sync scheduled tasks, Windows environment keys, or separate Windows applications.

Mods → Client groups Nampower, UnitXP SP3, SuperWoW, TransmogFix, PerfBoost, WeirdPerformance, VanillaHelpers, ClassicAPI and AuctionQueryThrottle under Recommended. Optional contains WorldMarkers, PNG Screenshots, MinimapIcons, LogSessions, CustomAssets, BigCursor, Interact, No1600x1200 and VanillaMultiMonitorFix. Interact installs its binding addon as a dependency. Existing monitor preferences are retained.

Discord Presence uses https://octowow.st/git/Dusk/WowPresence (the earlier GitHub repository is unavailable). Its switch defaults on, but enabling it is still reviewed before installation. All character detail options default off, explicitly overriding the upstream all-fields mask. Name, guild, race, faction, class, level and zone are separate opt-ins. Existing custom Discord application IDs are preserved. Presence configuration and the loader entry participate in rollback.

Safety Limits defaults on for engine values: render 1500, clutter 300, nameplates 41, camera zoom 100, horizontal FoV 130 degrees, audio channels 64. These are conservative caps, not a guarantee of stability on every system. The installer enforces the same bounds. Advanced ranges require turning Safety Limits off and are identified in the review. Gameplay & Sound includes camera skip, unlimited script memory, cross-faction resurrection, custom login-screen, per-client NX compatibility, and backed-up WDB clearing options. Windows-wide DEP policy is never changed. Client patch data is attributed under the included vanilla-tweaks MIT license.

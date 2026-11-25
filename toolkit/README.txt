Twilight Hammer Launcher v0.1.49

Open TwilightHammerLauncher.exe and choose the game folder.
Addon-only changes can run while OctoWoW is open. Run /reload afterward
or restart if a newly installed addon is missing. Close the game for
client mods, patches, client tweaks, and Sync setup.
Use the settings gear to change the saved game folder, choose launch options,
configure Discord Presence, or open the log. Updates are checked automatically.
The preview-style interface uses Microsoft Edge WebView2. If WebView2 cannot
start, Launcher opens the native Windows interface instead.
Select your Guild Sync, Auction Only, or Bank and Guild Sync edition.
Choose Skip Sync if you only want addons or client tweaks. Optional
components start unchecked. Setup uses Windows components and does not
require Node.js, PowerShell, or administrator access to a writable game folder.

Addons & Tools
The Launcher downloads the selected components from their upstream sources.
It adds required components to the install plan automatically and reuses
detected prerequisites when possible. Use VanillaFixes to start the game
when installing DLL mods. Chronicle Sidekick is installed as a separate app
and can be opened from the Start menu. Sign in to Chronicle there to upload logs.

Client Tweaks
Changes are opt-in and apply to the verified OctoWoW Modernized client build.
Unchecked switches leave current behavior alone. Always auto-loot reverses
the default loot behavior. Check my PC estimates a starting render distance
from RAM and CPU threads. Use recommendation fills the field for review.
Setup saves the previous executable, config, and replaced package files under
.twilight-toolkit\backups in your game folder.

Check for updates
Checks Launcher releases on GitHub, catalog components, and Git addons with
a saved source. Installed Addons displays addon versions when available.
Checks run at launch and every 30 minutes while the Launcher is open.
Update Available opens the changelog and installs a verified new executable.
The trash button backs up removed addons and keeps saved game settings.

Windows Security
If Windows blocks an upstream file, click Windows Security in the footer for
Protection History guidance, copyable paths, and the Microsoft file review link. Check again checks selected or Launcher-managed component files.

Sync uploader
Checks OctoWoW game files for changes every two minutes while signed in to
Windows. Auction Only is open to everyone. Guild Sync requires a one-time Discord
sign-in and device authorization on the portal. Bank and Guild Sync additionally
requires a guild officer or admin to approve uploads from Twilightbank and Twilightbags.
Existing Twilight Hammer Sync setup is replaced if you choose a Sync edition.
To remove it, click Uninstall Sync under Addons & Tools. This removes the
uploader and scheduled task. You can also remove the addon. Saved captures,
sync logs, and portal data are kept.

Guild Portal: https://twilighthammer.hccpride.workers.dev/
GitHub: https://github.com/obs1deus/twilighthammertoolkit

# Twilight Hammer Sync

WoW capture addon and Windows uploader for the Twilight Hammer OctoWoW portal.
The setup app embeds the addon and registers the uploader in Windows Task Scheduler.
Every two minutes it checks OctoWoW game files for changed captures while the
Windows user is signed in. The uploader sends new captures over HTTPS.

## Editions

- Guild Sync captures professions, the Twilight Hammer roster, and auction scans.
- Auction Only captures completed Aegis Exchange, Aux, and OctoAuctionator scans.
- Bank and Guild Sync adds Twilightbank bags, bank contents, and gold.

## Build

On Windows, run `./build.ps1` in PowerShell. It uses the Windows .NET Framework
C# compiler and writes the three executables under `dist/`. PowerShell is only
used to build the executables. The installers and scheduled uploader do not
require PowerShell or Node.js to run.

To install, close OctoWoW, double-click the executable for your edition, and
select the OctoWoW folder if prompted. Check that the addon is enabled at
character selection. With SuperWoW, captures save immediately. Otherwise,
log out or reload to save them.

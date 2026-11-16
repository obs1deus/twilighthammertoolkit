# Twilight Hammer Toolkit privacy policy

The Windows Toolkit checks GitHub for Toolkit releases at launch and periodically while open. Addon update checks and installer downloads contact the relevant upstream hosts, including GitHub or the Git host provided by the user. Those hosts receive ordinary connection information such as IP address. The Toolkit keeps its settings and diagnostic log on the PC.

## Optional Twilight Hammer Sync

Choosing a Sync edition installs a Windows scheduled task that checks OctoWoW game files for new captures. Eligible captures are sent to the [Twilight Hammer portal](https://twilighthammer.hccpride.workers.dev/). Choose **Skip Sync** to install addons and client tweaks without setting up the uploader.

| Edition | Data sent |
| --- | --- |
| Auction Only | Auction listings for Y'Shaarj Alliance, including item, price, and in-game seller data when present. The portal displays auction data publicly. The uploader removes the scanning character and guild fields from the auction payload. |
| Guild Sync | Auction data plus Twilight Hammer roster and captured profession data. Private guild uploads require authorization through Discord on the guild portal. |
| Bank and Guild Sync | Guild Sync data plus inventory and gold captures for Twilightbank. Authorization is required through the guild portal. |

For Guild Sync and Bank and Guild Sync, the uploader stores its authorization encrypted for the current Windows user. The portal opens in your browser for authorization. The Toolkit and uploader do not ask for your Discord password.

To stop future uploads, open **Addons & Tools → Twilight Hammer Sync → Uninstall**. This removes the uploader and the scheduled task and offers to remove the game addon. Uninstalling leaves previously uploaded records in the portal.

The Toolkit renders its interface with Microsoft Edge WebView2. External services have separate privacy policies. See the [GitHub privacy statement](https://docs.github.com/en/site-policy/privacy-policies/github-general-privacy-statement), [Discord privacy policy](https://discord.com/privacy), and [Microsoft privacy statement](https://privacy.microsoft.com/privacystatement).

For privacy questions, contact the [Toolkit maintainer](https://github.com/obs1deus).

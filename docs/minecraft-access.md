---
title: "Home"
---

Minecraft Access is a [Minecraft] mod that specifically helps visually impaired players play Minecraft.
It is an integration and replacement for [a series of previous mods][accessible-minecraft].
This mod primarily borrows the help of a screen reader to describe (narrate) the game interface,
and incorporates sound cues to provide orientation perception in this 3D world.
Currently, this mod [has enough features][features] to help visually impaired players play the game normally.

This mod supports:

* Game version `1.19.3` and later
* On [Fabric] and [NeoForge] mod loaders
* On Windows, Linux, and MacOS operating systems
  (on MacOS you may need to use an external monitor or decrease the GUI scale for inventory controls to work,
  but this is being worked on)
* On [Pojav Launcher] on iOS (Android is not supported yet)
* Works despite the language setting of the game
  (though the mod-specific narration will [fall back to English][i18n-fallback]
  if the mod does not support the language yet)

Each version of this mod will be pre-released on [GitHub] and announced in the [Playability Discord server]
first as a beta testing stage, after one week of feedback collection,
the version will be released on [Modrinth] and [CurseForge].

## Useful Links

* [Playability Discord server] - Join our Discord server if you want to chat with this mod's users and developers.

## Known Issues

Check [GitHub issues] for known issues.
If you are having problems with the mod, it is probably best to ask in the [Playability Discord server]
before creating an issue, unless you know for sure that it is a bug in the mod.

## Contributions

Any type of contribution is welcome:

* Be one of the first to try out new versions and help us find bugs and issues.
* Improve this mod's documentation for better readability and accessibility.
* Help us [translate] this mod into other languages.
* Create more text or video tutorials about how to play the game with this mod ([examples][tutorials]).
* Make sound effects for this mod.
* For development contributions, please read [CONTRIBUTING.md] for more details.

## Developer API
Information about the client-side API is available at its [Javadoc][client-javadoc].

[Minecraft]: https://minecraft.net
[accessible-minecraft]: https://github.com/accessible-minecraft
[features]: https://mcaccess.org/faq#is-the-mod-enough-to-play-the-game-normally
[Fabric]: https://fabricmc.net/use/installer/
[NeoForge]: https://neoforged.net
[Pojav Launcher]: https://pojavlauncher.app
[i18n-fallback]: {{% relref "/features#i18n-fallback-mechanism" %}}
[GitHub]: https://github.com/minecraft-access/minecraft-access/releases
[Playability Discord server]: https://discord.mcaccess.org/
[Modrinth]: https://modrinth.com/mod/minecraft-access/versions
[CurseForge]: https://legacy.curseforge.com/minecraft/mc-mods/blind-accessibility/files
[GitHub issues]: https://github.com/minecraft-access/minecraft-access/issues
[translate]: https://mcaccess.org/faq#how-can-i-contribute-to-i18n
[tutorials]: {{% relref "/good-resources#gameplay-with-this-mod" %}}
[CONTRIBUTING.md]: https://github.com/minecraft-access/minecraft-access/blob/dev/CONTRIBUTING.md
[client-javadoc]: /api/client/javadoc/


---
title: "Mod Setup"
---

Currently, this mod supports the Windows, Linux, and MacOS operating systems
(on MacOS you may need to use an external monitor or decrease the GUI scale for inventory controls to work, but this is
being worked on).
The latest version of this mod (as well as other mod dependencies) can be downloaded at the [releases page].


## Tutorial for Beginners: From Purchasing the Game To Installing This Mod

This tutorial tries to guide you step by step on how to set the whole thing up.

If you want to chat with this mod's users and developers, please join [our Discord server].
If you know where this tutorial could be improved, please also let us know.

> The paragraphs in the block quotes are additional nonsense.
> They don’t provide help with the installation,
> but they can give you a little more insight into things that are relevant.
> If you don't want to read them, skip them and move on, you won't miss anything important.

## Purchase the Game and Download the Launcher

### Purchase the Game

There are two editions of Minecraft. Minecraft: Java Edition and Minecraft: Bedrock Edition
(usually just referred to as Minecraft).
This Mod is only available for the Java Edition.
Don't worry about buying the wrong one, the two editions are sold in a bundle.

Before you purchase the game,
make sure you have a computer that meets the [minimum requirements] of Minecraft: Java Edition!

> While we needn't concern ourselves with the Bedrock Edition,
> I would like you to know the differences between the two editions:
>
> At one time Minecraft only had one edition, the Java Edition.
> Since Microsoft acquired Mojang Studios, they’ve launched a new Bedrock Edition.
> The Java Edition is available for PC (Windows, Mac, and Linux),
> while the newer Bedrock Edition is available for windows, smartphones, game consoles, etc.
>
> Please also note that the Bedrock Edition in the next purchase link only contains the version that runs on Windows.
> Bedrock Edition on other platforms, such as on smartphones,
> needs to be purchased separately in the platform's respective application store.

If you have a redeem code for this game, [here is the redeem page][redeem], no need to buy the game.
Or if you have Xbox Game Pass, you can also get Minecraft using your active subscription.

There are currently two purchasable versions of Minecraft, [the Deluxe Collection] and [the bare game].
It is worth noting that things that the Deluxe Collection has included that the bare game doesn't are just virtual
currency (Minecoins) and items only usable in the Bedrock Edition that we can't use in the Java Edition.

The links will jump to the pages that correspond to the language of your browser, and there is a `CHECKOUT` button.
Click it,
and then the webpage will redirect you to the payment flow, then it will require you to sign in with Microsoft.
Your purchase will be tied to a Microsoft account;
when you log into the launcher, you also need to log into this account.
Keep going after you’ve logged in, fill in the purchase information, and click on `confirm`, that's all.

### Use a Modpack Version of the Mod

If this is your first time playing Minecraft, it may be desirable to use a premade,
community created modpack with essential mods and configurations that are set up out of the box.
You can find a list of available packs [here][modpacks].
If you choose to utilize a modpack, there is no need to return to this guide after you finish that one,
as it is not relevant to modpack users.

### Download the Launcher

> You can use the launcher to choose which version of Minecraft you want to run
> or to modify the game launch configuration.
> There are many kinds of launchers,
> anyone can write their own launcher to provide a customized Minecraft game launching experience.
> Microsoft provides The launcher we will download, let's call it the official launcher.

At the end of the purchase process there will be a `Download Game` button to download the launcher.
If you've missed it, [here is the launcher installer download page][minecraft-download].

On Windows, the launcher installer is a normal executable file, run it and wait for it to finish.
On MacOS, the launcher is a DMG file. Open it, then copy the Minecraft application bundle by pressing `command+C` on it,
then open the `Applications` folder with `command+shift+A`, then paste the application you just copied with `command+V`.
On Linux, there will be instructions for various distributions linked further down the downloads page.
If the installation is successful, you will now have a new application called `Minecraft Launcher`.

The next steps require starting the launcher once first before installing the mod loader to let it generate the game
folder, so try [starting the launcher](#start-the-game), then start Minecraft: Java Edition.
Something may need to be prepared every time the launcher starts,
especially the first time you launch it as it has to install the entire game.
This may take a few minutes,
and you can listen to the screen reader's progress bar sound effect to find out the progress.
The first time you start the launcher, it will require you to sign in with your Microsoft account.

## Dependencies for Speech

On Windows, you must have either [NVDA] or [JAWS] running for the mod to speak.
NVDA is free and open source while JAWS is proprietary and costs money.
If you use Microsoft Narrator, or don't use a screen reader, you will not hear things like UI controls, inventory items,
block and entity names, or chat messages read out.

On Linux, the mod uses [Speech Dispatcher] for speech output.
You should be able to install it with your distribution's package manager,
probably under the name `speech-dispatcher` or `speechd`.
You may also have to install [eSpeak NG] for speech to work.
If you do not hear speech,
run the `spd-say test` command before you start the game to make sure Speech Dispatcher has started and is working.
If you do not hear anything after running that command,
check your Speech Dispatcher configuration in either `~/.config/speech-dispatcher/` or `/etc/speech-dispatcher/`,
and kill the `speech-dispatcher` process before testing again. If you use Orca, it will already be installed and set up.

## Choose and Install Your Chosen Mod Loader

> Like any other game, the mod loader is responsible for loading the mod files into the game at launch.
> The two mod loaders that Minecraft Access currently supports are Fabric and NeoForge.


Mods for Fabric and NeoForge aren’t compatible with each other.
Also, the same loader for a certain game version is almost always not compatible with mod files which support other game
versions.
E.G.: if I install a mod for 1.17, it usually will not work on later or earlier versions of the game like 1.16 or 1.18.

Which one should you choose? Fabric or NeoForge?
Fabric is the preferred loader for most of this mod's users,
as it has a simple installer and fast updates to the latest game version.
If you’re new to Minecraft, Fabric will be easier to set up and provide you with all the mods you will need.

### Install Fabric

Fabric is the recommended loader for new players who’re using this guide.
The Fabric mod loader can be downloaded [here][fabric].
Fabric provides an executable installer for Windows, click the `Download for Windows` button to download it.
The executable file is named `fabric-installer-<installer-version-number>.exe`,
for example, `fabric-installer-0.15.0.exe`.
We’ve received reports that some screen readers such as NVDA can’t read the installer's interface.
If your current input method is not English.
Try to switch to the English input method first,
if the problem still persists,
please download the jar form of the installer by clicking the `Download universal jar` button,
just below the `Download for Windows` button.

If you are on Linux or MacOS,
download the jar version of the installer using the "Download universal jar" button, and execute it.
It will be named the same as the `.exe` file except for the file extension,
for example, `fabric-installer-0.15.0.jar`.
You must have Java installed for this to work.
On MacOS, download Java from the [downloads page][adoptium].
Make sure to choose aarch64 if you are on Apple Silicon and x86_64 if you are on Intel.
On Linux, google how to install OpenJDK on your distribution.

Start the installer, a window pops up for you to choose the installation configurations:

1. The `Client` tab is selected by default, don't change it.
2. The first combo box is for selecting the game version you want to install,
   please refer to the current game version that is supported by this mod on the [releases page]
   (under the `Mod Version Compatibility` section), and change the combo box to select that game version.
3. Next, there is a checkbox for showing game snapshot versions; you can ignore it.
4. The second combo box is for selecting the Fabric loader version, the latest version is selected by default,
   you should also not touch this either.
5. Then there is an input field for specifying the installation location,
   the installer will recognize the correct folder automatically if you’ve been following this guide correctly,
   for example `C:\Users\username\AppData\Roaming\.minecraft`,
   that's where we want it to install so no need to change this either.
   Copy this path to somewhere like Notepad, TextEdit, or Pluma for further usage when installing mod files.
6. And then a checkbox for `Create profile`, checked by default,
   we want it to create a profile in the official launcher, so there is no need to change this option either.
7. Finally, click the `Install` button to start the installation.
   It will download some files from the Internet, if the network is fast, the installation takes less than a minute.
   A pop-up will show up to notify you that the installation is successful.

### Install NeoForge

NeoForge is for advanced users who wish to heavily modify their game; the installation process for it is more complex.
See the [advanced guide] for some more detailed instructions for installing Java, NeoForge,
and having more than one mod directory.

## Install Your Mods

There are several popular mod download platforms,
such as [Modrinth] and [CurseForge];
generally, mod developers will release their work on multiple mod listing platforms at the same time.
Mod developers will release mod files that are compatible with different game versions.
Please always keep this in mind when downloading mods, as incompatible mods will crash the game at startup.
It is worth mentioning that some mods can support multiple versions with one same mod file,
and generally this will be mentioned in the mod file name.
It is also good to note that all mod files have the `.jar` extension,
but some downloader or browsers may download these files in a compressed format like `.zip`,
in this case you will have to extract the real mod file from the compressed one.

Neither mod loaders nor the original game provides the ability to manage mod files; you need to manage them manually.
This guide will describe how to download and install mods manually;
you can also manage them automatically with an extra application such as
[Modrinth][modrinth-app] or [CurseForge][curseforge-app].

Now let's download mod files.
To download this mod and the dependencies of this mod,
it is recommended you download them from the Minecraft Access's official [releases page],
where you can find download links of suitable versions of the required mods,
under the `Mod Version Compatibility` section of each release.
By the way, you may be interested in the mods provided in the [good resources] page of the documentation,
they are good mods that our visually impaired users have found and tested through practice.

After all the mods you want are downloaded,
you can move on to putting them into the right location for the mod loader to recognize them.
The default path on Windows is: `%appdata%\.minecraft\mods`
(the `%appdata%` is a shortcut for `C:\Users\username\AppData\Roaming`),
you can directly paste it into File Explorer then press the enter key to jump to it or paste it into the run box
accessed with the Windows+R keys and click enter.
Note that on Linux, the default game directory is `~/.minecraft` and the mod directory is `~/.minecraft/mods`.
On MacOS, the default game directory is `~/Library/Application Support/minecraft`
and the mod directory is `~/Library/Application Support/minecraft/mods`.
Press `command+shift+G` to open `Go to Folder`, then type or paste this folder path and press enter.
We'll put downloaded mod files inside it,
both Fabric and NeoForge will load mods from this folder by default if installed.
You can look up how to use alternate installation locations if you wish to maintain both Fabric and NeoForge
installations at the same time.
You will need to start the game once after installing the mod loader for the `mods` folder to be created.

## Start the Game

The first time you start the launcher, it requires you to sign in with your Microsoft account.

The launcher's sidebar contains not only `Minecraft: Java Edition`, it also includes `Minecraft for Windows`,
and even two derivative games of Minecraft.
The sidebar also contains an entry for the settings of the launcher itself.
If you want to change your username or player appearance, for Java Edition,
you need to go to the launcher settings screen, `Accounts` tab, select your account, `More Options` button,
`Manage Minecraft: Java Edition profile`, then a web page will be opened in your browser,
which leads you to the profile management page.

The main screen of `Minecraft: Java Edition` in the launcher contains four tabs:

* `Play` - Where you can choose which profile to play and start the game.
* `Installations` - Where you can manage different game profiles for different game versions or game configurations.
* `Skins` - You can upload skin files here to change your appearance in game.
* `Patch Notes` - Update logs about the game.

Select the profile created by the mod loader installer and start it, for Fabric,
the profile name is `fabric-loader-<game-version>`,
for NeoForge, the name is simply `NeoForge` but the subtitle contains the game version.
If everything has worked correctly, you can start exploring the game.

When you launch the game for the first time, you should hear a message that says `Press enter to enable narrator`.
If you hear this, press enter before closing the game again.
If you don't hear this message, or want to make sure the narrator is enabled,
you can delete `options.txt` from the game directory (the `mods` folder without `\mods` or `/mods`).
In that case, the narrator should be enabled by default when you first launch the game with Minecraft Access.
If you do not hear speech, press `Control+B` to enable the narrator,
keep pressing until it switches to `Narrator Narrates All`.

If the game crashes and a bunch of error logs pop up,
the biggest probability is that there is an incompatibility between the mod files and mod loader,
or between the mod files and game version.
Please read the [Update the Game and Mods](#update-the-game-and-mods) section.
And here is a [complex self-help FAQ] for you to address the problem,
from not being able to hear the narration to a full game crash.

## Changing Speech Settings

On Windows, Minecraft Access will use the same speech settings as your screen reader (JAWS or NVDA).
On Linux, it will use your default Speech Dispatcher settings
(usually found in `~/.config/speech-dispatcher/speechd.conf` or `/etc/speech-dispatcher/speechd.conf`)
You must restart Speech Dispatcher for changes to take effect.

On MacOS, to change the voice, pitch, and volume the mod uses, open System Settings > Accessibility > Spoken Content,
and change the settings there.
Changing your default speech rate will not affect Minecraft Access, so you must change that from the configuration menu.

1. Open Minecraft and enter a world
2. Press F4 followed by 0 to open the config menu
3. Press Control+Tab until you hear "Speech Tab"
4. Press tab until you hear "Speech Rate (MacOS)"
5. Type in a new percentage and press tab
6. Exit the config menu by pressing escape

## Update the Game and Mods
For [Fabric](#install-fabric),
run the fabric installer again and change the installed game version in the Fabric installer screen,
for [NeoForge](#install-neoforge), download another installer for the game version you want and install it.
You need to check all mod files in the `mods` folder for compatibility with the new game version,
and replace or remove incompatible mods.

To update mods, first recall what is mentioned above:

1. Mods of Fabric and NeoForge aren’t compatible with each other.
2. The same loader for a certain game version is almost always not compatible with mod files which support other game
   versions.
   E.G.: if I install a mod for 1.17,
   it usually will not work on later or earlier versions of the game like 1.16 or 1.18.
3. By default, both Fabric and NeoForge load mods from the folder `%appdata%\.minecraft\mods` on Windows,
   `~/.minecraft/mods` on Linux, and `~/Library/Application Support/minecraft/mods` on MacOS.

Here are some examples to help you understand:

* Fabric loader with any NeoForge mod file in the `mods` folder, will crash, and vice versa.
* Fabric loader for game version `1.19.3`, with a Fabric mod file for game version `1.20.1` will crash.
* All mod files in the `mods` folder need to be conflict-free for the game to start.

Based on these conditions, if you want to update some mod versions while keeping the game version unchanged,
you should replace the corresponding mod files.
For mods that have dependencies like Minecraft Access,
you also need to be aware if any of the dependent mods' versions have changed.

Another situation that causes the game to crash or misbehave is a conflict between mods,
which is challenging to show in the error log because it is hard to predict.
For this type of error, there is a simple yet cumbersome method for all modded games:
move all mod files out of the Mods folder and into a temporary folder,
then add them back to the Mods folder one by one, try to start the game every time you move back a file.

[releases page]: https://github.com/minecraft-access/minecraft-access/releases/latest
[our discord server]: https://discord.mcaccess.org/
[minecraft]: https://www.minecraft.net/en-us/store/minecraft-java-bedrock-edition-pc
[minimum requirements]: https://help.minecraft.net/hc/en-us/articles/4409225939853-System-Requirements-for-Minecraft-Java-Edition
[redeem]: https://www.minecraft.net/en-us/redeem
[the Deluxe Collection]: https://www.minecraft.net/en-us/store/minecraft-deluxe-collection-pc
[the bare game]: https://www.minecraft.net/store/minecraft-java-bedrock-edition-pc
[modpacks]: {{% relref "/good-resources#community-modpacks" %}}
[minecraft-download]: https://www.minecraft.net/download
[NVDA]: https://nvaccess.org/download/
[JAWS]: https://support.freedomscientific.com/Downloads/JAWS
[Speech Dispatcher]: https://freebsoft.org/speechd
[eSpeak NG]: https://github.com/espeak-ng/espeak-ng
[fabric]: https://fabricmc.net/use/installer/
[adoptium]: https://adoptium.net/temurin/releases/?os=any&arch=x64&package=jdk
[releases page]: https://github.com/minecraft-access/minecraft-access/releases/latest
[advanced guide]: {{% relref "./advanced" %}}
[Modrinth]: https://modrinth.com/mods
[CurseForge]: https://legacy.curseforge.com/minecraft/mc-mods
[modrinth-app]: https://modrinth.com/app
[curseforge-app]: https://www.curseforge.com/download/app
[good resources]: {{% relref "/good-resources#quality-of-life-mods" %}}
[complex self-help FAQ]: https://mcaccess.org/faq#self-help-guide-for-abnormal-situations


---
title: "Set up NeoForge (ADVANCED)"
---

Setting up NeoForge is a more advanced process. This guide is only recommended for those who **KNOW** they need it.

## Install Java

> Java is a programming language, Minecraft Java Edition and mod loaders are written in this language.
> It's like installing a driver on your computer to use a certain piece of hardware.

The NeoForge installer requires Java to be installed on your PC to run it.
You should consult the NeoForge documentation to find the recommended version of Java to install
to ensure maximum compatibility and future support.
[Here is the download page for our recommended Java distribution][adoptium].
Run the installer and keep clicking Next, there's nothing to say.
You must restart your computer after the Java installation is completed.

## Install NeoForge

NeoForge requires you to select the right version of the game at the time you download the installer.
Reports have been received that higher versions of NeoForge that are compatible with the game version may not be
compatible with this mod (this mod is always built on one specific NeoForge version),
so it is recommended to download the tested version of NeoForge from the link in the [releases page]
(under the `Mod Version Compatibility` section).
If you still want to download the latest version of NeoForge for some reason,
[here is the download page for that version of NeoForge][neoforge].
Select the `Installer` button under the `Download Recommended` section.
An executable jar file will be downloaded with the name format `neoforge-<neoforge-version>-installer.jar`,
for example `neoforge-26.1.2.68-beta-installer.jar`.

Before running the installer, download and run the patch file from [here] so that you can run the jar file directly in
Windows File Explorer. If you downloaded the installer for Windows, skip this step.
Now you can run the NeoForge jar file like a normal executable file,
a window will pop up for you to choose the installation configuration:
The NeoForge installer supports multiple languages, so if you want to change the language, you can do this here.

1. First is a radio box for selecting install client or server, `Install client` is selected by default,
   don't change this.
2. Then there is an input field for specifying the installation location,
   normally the installer will automatically recognize the official loader's folder.
   For example `C:\Users\username\AppData\Roaming\.minecraft`,
   that's where we want it to install so no need to change this either.
   Copy this path to somewhere like Notepad, TextEdit, or Pluma for further usage when installing mod files.
3. Now click the `OK` button to start the installation.
   It will download some files from the Internet, if the network is fast,
   the installation should take about two to three minutes.
   A pop-up will show up to remind you that the installation is successful

All done, you can now continue onto installing mods.

## Set up multiple installations in the Minecraft Launcher

If you wish
to have both Fabric and NeoForge installations of the game
using the official launcher, you can change the installation location in the launcher
to achieve this.
Note that when you’re installing a mod loader,
you must install it to the default directory
and then change the profile game directory separately using the steps below.
Also note that each game directory has not only its own mods folder,
but also its own worlds, screenshots, resource packs, etc. folders.

1. Open the Minecraft Launcher.
2. Go to the `Minecraft: Java Edition` tab.
3. Click the installations tab.
4. Find the profile you wish to edit, most likely Fabric <game version> or NeoForge.
5. Click the more options button that shows up after/to the right of the profile name, play, and locate folder buttons.
6. Click the edit button.
7. Tab until you hear `game directory`.
8. Click the browse button and find where you want this installation to be located.
9. Click the select button.
10. You should be returned to the profile options menu.
11. Click the save button at the bottom of the menu.
12. launch the game to create all the files and folders.

[releases page]: https://github.com/minecraft-access/minecraft-access/releases/latest
[adoptium]: https://adoptium.net/temurin/releases/?os=any&arch=x64&package=jdk
[neoforge]: https://neoforged.net/
[jarfix]: https://johann.loefflmann.net/en/software/jarfix/index.html


---
title: "Features"
---

This page contains details for all the features that are currently in the mod.

If you have any questions about original game functions,
please search on the [Minecraft wiki] first before asking for help.
If you have any suggestions on improvements to existing features or about a new feature,
you can [join the discord server][discord] or [post an issue][issues].

There is also a page for [viewing all sound effects][sounds] used in this mod.

## Camera Controls

Like most 3D games, you can rotate the camera 360 degrees freely in Minecraft with the mouse,
and the direction you look is the direction to go forward while you press the W key.
This feature allows you to control the camera (your in-game facing direction and crosshair) through the keyboard.
The mod will automatically report the current direction as the camera moves,
such as `North`, `South East`, `Up`, `Down`, and `Straight`.

See also: [Keybindings]({{% relref "/keybindings#camera-controls" %}}),
[Configuration]({{% relref "/config#camera-controls" %}})

## Mouse Simulation

This feature allows you to simulate five mouse operations (left middle right click, scroll up and down) through the
keyboard, while the original mouse input still works.
This feature supports continuous key pressing,
e.g., destroying a block requires continuous pressing of the left mouse button against that block.

You MUST keep the vanilla `Attack/Destroy` key, `Use Item/Place Block`
key and `Pick Block` key set to the default mouse keys.
This feature will only simulate mouse operations,
not directly execute the attack or place operation; it needs the mouse key bindings as a medium.

See also: [Keybindings]({{% relref "/keybindings#mouse-simulation" %}}),
[Configuration]({{% relref "/config#mouse-simulation" %}})

## Read Crosshair

This mod will automatically speak the information of the block and entity
(i.e., living creatures and moving objects) you’re targeting with your crosshair
(i.e., what you're looking at).
The crosshair is in the center of the screen.

If you're looking at a block, the mod will speak which side of that block you’re looking at;
this is useful for recognizing directions and placing blocks accurately.
For example, if the mod says `Stone North`, it means you're looking at the north side of a stone block;
this indicates that you’re within 180 degrees of where this side is facing, not necessarily directly in front of it.
You can determine your current location with the help of your relative position to the static block.

For simple targets, the mod will only speak the name (and side if it's a block),
such as `Pig`, `Cow`, `Grass Block Up`, `Stone North`.
For functional blocks or entities with multiple forms, the mod will also speak current state of the block,
such as `Ripe Wheat Crops Up`, `White Sheep Shearable`, `Opened Oak Door South`, `Powered Dispenser West Facing East`.

According to the [wiki][breaking], the breaking distance in survival mode is 4.5 blocks (in Java Edition),
but the `ReadCrosshair` feature will speak targets at most 6 blocks away.
So if the mod says something, but you can't interact with it, move forward a little closer to it.

See also: [Configuration]({{% relref "/config#read-crosshair" %}})

### Relative Position Sound Cue

Whenever you're looking at a block or entity,
the mod will play a piano sound cue to indicate the relative location between you and the target.
Volume to represent distance, the louder the sound, the closer the distance.
Pitch to represent elevation, the higher the sound, the higher the target relative to you.
You can turn off this feature or change the sound volume in config.

See also: [Configuration]({{% relref "/config#relative-position-sound-cue" %}})

### Partial Speaking

Make the mod only (or only not) speak entities/blocks that you've configured.
This feature can be easily misused, so it is not enabled by default.
This works as a whitelist or a blacklist depending on how you choose to set it up.
For example,
you can disable the announcement of grass blocks from the narration
by setting the mode to blacklist and adding grass as a phrase.

See also: [Configuration]({{% relref "/config#partial-speaking" %}})

## Inventory Controls

This feature allows you to operate various screens using the keyboard instead of the mouse.
In fact, this is a feature that makes almost all screens accessible, not just the inventory screen.
The vast majority of screens contain operations for transferring and using items,
so it's appropriate to call this feature `Inventory Controls`.

We divide the various parts of each screen into different slot groups, following the original design of the screens.
One slot group contains one or more slots,
arranged in a grid shape, each of which may be empty or contain one type of items (one stack at max).
You can switch focused group, move to different slots within a group,
or pick up items from slots in one group and place them into slots in another group (to transfer or use them).

Use the mouse simulation keys in the [`Mouse Simulation`](#mouse-simulation) feature to preform operations such as item
transferring and button clicking.
The left mouse button will pick up and put down the full number of items in the slot.
The right mouse button will pick up half of the items or put one item down if you're already holding a compatible item.
The middle mouse key can be used in creative mode to pick up a full stack of items from the creative inventory item
list.
The mod will speak what is currently in the slot, such as `Empty Slot` or `64 Stone`.
When you pick up the full number of items in a slot,
the mod will speak `Empty Slot` to represent the current state of the slot.
When you try to place items in an occupied slot,
it will instead swap with the items already in the slot,
resulting in the items in the slot being placed onto your cursor.

Please note that:

1. Item speaking in `Scrollable Recipes Group` under the `Crafting Screen` will be delayed about one second,
   please wait after pressing the move slot key to hear the item name.
2. In the `Scrollable Recipes Group` on the `Crafting Screen`, except the first page,
   sometimes you will hear nothing while you're moving between slots.
   It's best to keep items you want to craft within the first page of the recipe book by moving unused items out of your
   inventory.
3. After you put things into an [Anvil], the cursor will be automatically focused onto the rename input text field,
   all you need to do is press the `Enter` key to go back to slot selection.
4. Servers will build custom inventory screens to serve as special option menus,
   some servers like hypixel will continuously change the option (item) names,
   which will cause this mod to continuously speak the changed item name.
   Disable the `Speak Focused Slot Changes` config to solve this problem.

See also: [Configuration]({{% relref "/config#inventory-controls" %}}),
[Keybindings]({{% relref "/keybindings#inventory-controls" %}})

## Points of Interest

This feature will scan and notify (with sound cues) you of (pre-configured) special blocks and all types of entities
around you. You can listen to the sound cues for different types of blocks and entities on the Sound Demo page.

What blocks are considered special? Those you tend to miss.
In Minecraft, not every block is cube-shaped — for example, buttons are tiny squares, glass panes are thin bars,
ladders are a thin layer attached to another block, you'll find it difficult to point precisely at these blocks.
Precious blocks like raw ores can be seen at a glance by sighted players,
but visually impaired players will need to point at them to know their existence.

By the same logic, entities (players, animals, monsters) are movable,
so scanning and locking onto them helps you get close to and interact with them.
Be aware that if you keep any menu screen open,
the mod will stop scanning and notifying you of entities for a cleaner screen narration,
which may get you in danger if there are monsters around you.

See also: [Configuration]({{% relref "/config#point-of-interest" %}}),
[Keybindings]({{% relref "/keybindings#point-of-interest" %}})

### Object Tracker

The object tracker allows you to get an idea of your surroundings by listing all POI blocks and entities near you,
allowing you to hear where they are and lock onto any of them.

All special blocks and entities (objects) are grouped in the following groups:
- Entities
    - Your pets
    - Other pets
    - Bosses
    - Hostile mobs
    - Passive mobs
    - Players
    - Vehicles
    - Items
- Blocks
    - Ores
    - Doors
    - Fluids
    - Functional blocks
    - Blocks with interface

There is one other group that's special called other blocks.
In this group, one of every block type surrounding you will be listed.
This can be useful when trying to find something specific that isn't in the groups above.

### POI Locking

You can lock onto the object currently being targeted by the object tracker with a key,
then your camera will follow the target as it moves, so you can approach it more easily.
For example, when you want to mine an ore, capture an animal or fight a monster.

Note that:

* The mod will continue locking on the position where the eye of ender disappears
  (if you enabled `Auto Lock on to Eye of Ender when Used` in the config).
* The mod will automatically stop locking on ladders when you start climbing them,
  so you can directly climb the ladder without manually pressing the unlock key.
* The mod will automatically stop locking on blocks if they are destroyed or changed
  (for example, a [door] is opened or closed, or a [piston] is activated).

#### Bow aim assist

When you start drawing the bow, you will automatically lock onto the nearest hostile mob.
A sound will play indicating if you can hit your target and how far the bow has been drawn.
If the sound playing is a piano, you can shoot the target, and if it's a bass, you can't shoot the target.
The sound will increase in pitch 3 times as you draw the bow, stopping when the bow is fully drawn.

See also: [Configuration]({{% relref "/config#entitiesblocks-locking" %}}),
[Keybindings]({{% relref "/keybindings#point-of-interest" %}})

### POI Marking

Sometimes you may need to search for a special type of block that is not in the pre-configured list.
You can mark the type while you’re pointing at it (with your crosshair).
You can set the mod to only scan and notify for marked types
or suppress scanning and notifying for pre-configured types.

Additionally, when a block is marked,
a new group will appear at the top of the object tracker containing all blocks of that type nearby.

See also: [Configuration]({{% relref "/config#entitiesblocks-marking" %}}),
[Keybindings]({{% relref "/keybindings#point-of-interest" %}})

## Fall Detector

This feature will alert you with a foot stomping sound cue when you're near the edge of a big drop.
It will play a sound effect for every location that meets the set threshold distance, the louder the sound,
the closer you are to the edge.

See also: [Configuration]({{% relref "/config#fall-detector" %}})

## Access Menu

This menu can be accessed by pressing the F4 key by default
and integrates a number of helper functions which can be executed from within the menu
or by using the individual hotkey directly.
Note that the individual function keys aren't bound by default, aside from narrate target,
so you will need to bind the ones you want to use yourself.

| Function                              | Description                                                                                                                                        |
|---------------------------------------|----------------------------------------------------------------------------------------------------------------------------------------------------|
| 1. Block and fluid target information | Speak the name and information of the targeted block, the range (20 blocks) is much more than the `Read Crosshair` triggering distance (6 blocks). |
| 2. Block and fluid target position    | Speak the x y z position of the targeted block                                                                                                     |
| 3. Light level                        | Speak the [light] level of the player's position                                                                                                   |
| 4. Find Closest Water Source          | Find the closest [water] source block in the set range, play a [Item plops] bubbling sound at target position                                      |
| 5. Find Closest Lava Source           | Find the closest [lava] source block in the set range,  play a [Item plops] bubbling sound at target position                                      |
| 6. Biome                              | Speak the name of the [biome] that the player is currently in                                                                                      |
| 7. Time of Day                        | Speak the current time of the in-game [Daylight Cycle]                                                                                             |
| 8. XP                                 | Speak the player's current [experience] level and progress                                                                                         |
| 9. Refresh screen Reader              | Refresh the screen Reader                                                                                                                          |
| 0. Open Config Menu                   | Opens the Config Menu which can be used to change the configs of this mod that take effect immediately                                             |

See also: [Configuration]({{% relref "/config#access-menu" %}}),
[Keybindings]({{% relref "/keybindings#access-menu" %}})

## Player Condition

Some features that help you understand the status of the character you control.

### Position Narrator

Minecraft has a built-in [absolute coordinate system][coordinates], x-axis for the longitude, z for the latitude,
and y for the elevation. This feature provides keystrokes to read out coordinates.

See also: [Configuration]({{% relref "/config#position-narrator" %}}),
[Keybindings]({{% relref "/keybindings#position-narrator" %}})

### Player Status

This feature adds a key to speak your current status,
which includes things like health, hunger, armor, air remaining (if in water), etc.
The mod will automatically speak gaining and losing effects.

See also: [Configuration]({{% relref "/config#health-n-hunger" %}}),
[Keybindings]({{% relref "/keybindings#player-status" %}})

### Player Warnings

This feature warns you using a metal sound when your [air] (only tracked when you're submerged in water),
[health], or [hunger] fall below the set thresholds.
You'll hear something like `Warning, Health is {current health}`.
If you enable the `Play Sound` config, you'll also hear a sound cue along with the warning words.
You may also want to learn about [the various damage types][damage] in the game.

#### Durability Warnings
This feature warns you when the durability value of your held items or worn armor reaches set thresholds.

See also: [Configuration]({{% relref "/config#player-warnings" %}})

## Speak Text Editing

The mod will simulate the feedback you get when typing text in other software's input boxes.
It will speak the text that you delete, select, cursor over on the [Chat Screen][chat],
[Command Block Screen], and so on.
The mod also simplifies the original [command] suggestion narration for a better typing experience with lesser annoying
too-detailed narrations.
The content of one command suggestion narration will be like
`{the order of focused suggestion}x{total number of suggestions} {suggestion} selected`,
the format can be customized in the config.

See also: [Configuration]({{% relref "/config#other-configurations" %}})

## Other Small Features

Some small features that are automatically triggered based on your actions.

See also: [Configuration]({{% relref "/config#general" %}}), [Keybindings]({{% relref "/keybindings#general" %}})

### Menu Fix

This mod will automatically move the cursor position aside when opening certain menus to prevent narrate unnecessary
contents under the cursor.
You can close this feature in the configuration if you have some sight and find it annoying.

### Speak Held Item

When you switch held items, speak the name and number of items in your hand (main hand only).
The item [durability] information is included.
If you feel that continuous item count reporting is annoying,
you can disable it in the [configuration]({{% relref "/config#other-configurations" %}}).

### Biome Indicator

Speak the name of the [biome] when you enter one.

### XP Indicator

Speak when your [experience] level is increased or decreased.

### Speak Picked Up Items While Holding Fishing Rod

This feature will speak the items that you pick up while you're holding a fishing rod,
even if you're not actually fishing.

### Speak Chat Messages

Speak [chat] messages, so you won't miss your friends' conversation.
If you feel that too many chat messages are too noisy,
you can turn off showing chat messages in the original `Chat Settings...` options,
or press `P`
to open the [Social Interactions Screen] to mute particular players.

See also: [Keybindings]({{% relref "/keybindings#speak-chat-messages" %}})

### Play a sound for new chat messages

By default, the mod | will play a sound when sending or recieving a message in chat.
The volume of this is controlled by the UI sounds slider in the game settings.

### Speak Action Bar Messages

Messages shown in the form of the [action bar] are common in modded multiplayer servers,
usually they are used as server announcements or for showing mod-specific information,
for example, in hypixel it shows extra `health, defense, mana` values of players.
When you feel like the mod is repeating action bar messages,
that's because the messages are partially updated but the mod will speak the whole sentence,
try enabling the `Only Speak Action Bar Updates` config in the
[configuration]({{% relref "/config#other-configurations" %}})
and find what you like the most.

### I18N Fallback Mechanism

Minecraft has support for many languages, when referring to languages that this mod supports,
we mean text that is introduced by this mod and doesn't exist in the original game.
This mod has a fallback mechanism for I18n in case it fails on unsupported languages or text that is not currently
translated in supported languages.
If any text is not translated to your language in I18N yet, the mod will use the English version instead.
Setting the game to your familiar language is recommended, even if it's not supported by this mod,
since you can still benefit from the translation of the game's original text, such as the names of blocks or entities.

[Minecraft wiki]: https://minecraft.wiki/?search
[discord]: https://discord.mcaccess.org/
[issues]: https://github.com/minecraft-access/minecraft-access/issues
[sounds]: {{% relref "/sounds" %}}
[breaking]: https://minecraft.wiki/w/Breaking#Basics_of_breaking
[Anvil]: https://minecraft.wiki/w/Anvil
[door]: https://minecraft.wiki/w/Door
[piston]: https://minecraft.wiki/w/Piston
[light]: https://minecraft.wiki/w/Light
[water]: https://minecraft.wiki/w/Water
[Item plops]: https://minecraft.wiki/w/Item_(entity)#Sounds
[lava]: https://minecraft.wiki/w/Lava
[biome]: https://minecraft.wiki/w/Biome
[Daylight Cycle]: https://minecraft.wiki/w/Daylight_cycle
[experience]: https://minecraft.wiki/w/Experience
[coordinates]: https://minecraft.wiki/w/Coordinates
[air]: https://minecraft.wiki/w/Damage#Drowning
[health]: https://minecraft.wiki/w/Health
[hunger]: https://minecraft.wiki/w/Hunger
[damage]: https://minecraft.wiki/w/Damage
[chat]: https://minecraft.wiki/w/Chat
[Command Block Screen]: https://minecraft.wiki/w/Command_Block#Modification
[command]: https://minecraft.wiki/w/Commands
[durability]: https://minecraft.wiki/w/Durability
[Social Interactions Screen]: https://minecraft.wiki/w/Social_interactions
[action bar]: https://minecraft.wiki/w/Commands/title


---
title: "Keybindings"
---

This page contains all the keybindings added by the mod.

You can change these keybindings in the settings (open `Options...` then `Controls..` then `Key Binds..`),
all keybinding setting groups that are provided by this mod have a `Minecraft Access:` prefix to differentiate them from
the original keybinding settings.

You may find that some features have duplicate keys, such as I, J, K, L as the arrow keys in various features.
It's ok since the same key takes effect in different interfaces for different functions.

You may want to take a look at [all the original game controls][controls] as well.

## Camera Controls

| Single Key                        | Default Keybinding | Description                                                                                                                          |
|-----------------------------------|--------------------|--------------------------------------------------------------------------------------------------------------------------------------|
| Look Up                           | `I`                | Move the camera vertically up by the `Normal Rotating Angle` config value                                                            |
| Look Right                        | `L`                | Move the camera horizontally right by the `Normal Rotating Angle` config value                                                       |
| Look Down                         | `K`                | Move the camera vertically down by the `Normal Rotating Angle` config value                                                          |
| Look Left                         | `J`                | Move the camera horizontally left by the `Normal Rotating Angle` config value                                                        |
| Center Camera                     | `,`                | Look straight ahead: Turn the camera to the closest of the eight cardinal directions and reset vertical angle to horizontal position |
| Look Straight Down                | `.`                | Turn the camera to the look down at feet direction                                                                                   |
| Speak Facing Horizontal Direction | `H`                | Speak current horizontal facing direction                                                                                            |

| Key Combination                 | Default Keybinding | Description                                                                                                                                         |
|---------------------------------|--------------------|-----------------------------------------------------------------------------------------------------------------------------------------------------|
| Modified Look Up                | `Alt` + `I`        | Move the camera vertically up by the `Modified Rotating Angle` config value                                                                         |
| Modified Look Right             | `Alt` + `L`        | Move the camera vertically right by the `Modified Rotating Angle` config value                                                                      |
| Modified Look Down              | `Alt` + `K`        | Move the camera vertically down by the `Modified Rotating Angle` config value                                                                       |
| Modified Look Left              | `Alt` + `J`        | Move the camera vertically left by the `Modified Rotating Angle` config value                                                                       |
| Look North                      | `Control` + `I`    | Turn the camera to the north                                                                                                                        |
| Look East                       | `Control` + `L`    | Turn the camera to the east                                                                                                                         |
| Look South                      | `Control` + `K`    | Turn the camera to the south                                                                                                                        |
| Look West                       | `Control` + `J`    | Turn the camera to the west                                                                                                                         |
| Look Behind                     | `Alt` + `,`        | Look straight back: Turn the camera to the opposite of the closest of the eight cardinal directions and reset vertical angle to horizontal position |
| Look Straight up                | `Alt` + `.`        | Turn the camera to the look above head direction                                                                                                    |
| Speak Facing Vertacle Direction | `Alt` + `H`        | Speak current vertical facing direction                                                                                                             |

See also: [Feature Description]({{% relref "/features#camera-controls" %}}),
[Configuration]({{% relref "/config#camera-controls" %}})

## Mouse Simulation

| Single Key              | Default Keybinding | Description                                                                        |
|-------------------------|--------------------|------------------------------------------------------------------------------------|
| Left Mouse Sim          | `[`                | Simulate left mouse key, default value of the original `Attack/Destroy` key        |
| Middle Mouse Sim        | `\`                | Simulate middle mouse key, default value of the original `Pick Block` key          |
| Right Mouse Sim         | `]`                | Simulate right mouse key, default value of the original `Use Item/Place Block` key |
| Mouse Wheel Scroll Up   | `;`                | Simulate mouse wheel scroll up, switching items in hotbar forward                  |
| Mouse Wheel Scroll Down | `'`                | Simulate mouse wheel scroll down, switching items in hotbar backward               |

See also: [Feature Description]({{% relref "/features#mouse-simulation" %}}),
[Configuration]({{% relref "/config#mouse-simulation" %}})

## Inventory Controls

| Single Key       | Default Keybinding | Description                                                                                                      |
|------------------|--------------------|------------------------------------------------------------------------------------------------------------------|
| Menu Move Up     | `I`                | Focus to the slot above                                                                                          |
| Menu Move Right  | `L`                | Focus to the slot right                                                                                          |
| Menu Move Down   | `K`                | Focus to the slot below                                                                                          |
| Menu Move Left   | `J`                | Focus to the slot left                                                                                           |
| Next Group       | `C`                | Select next slot group                                                                                           |
| Next Tab         | `V`                | Select next tab                                                                                                  |
| Toggle Craftable | `R`                | Switch between `show all` and `show only` craftable recipes in recipe book group                                 |
| Fuel Status      | `U`                | Narrate the remaining percent on fuel and time until item is done being processed in furnaces and brewing stands |
| Jump to Textbox  | `T`                | Select the search box or text box                                                                                |
| Enter            | not re-mappable    | Deselect the search box or text box                                                                              |

| Key Combination           | Default Keybinding | Description                             |
|---------------------------|--------------------|-----------------------------------------|
| Previous Group            | `Shift` + `C`      | Select previous slot group              |
| Previous Tab              | `Shift` + `V`      | Select previous tab                     |
| Previous Recipe Book Page | `Shift` + `I`      | Select previous page of the Recipe Book |
| Next Recipe Book Page     | `Shift` + `K`      | Select next page of the Recipe Book     |

`Switching Tabs`, `Toggling Craftable`, `Jumping to Textbox` and `Enter` keys only works when there is a corresponding
component in the opened screen.
Recipe Book page turning only works when `Recipe Book Group` is selected.
Search on the [wiki] for the description of screens if you're not familiar with them.

See also: [Feature Description]({{% relref "/features#inventory-controls" %}}),
[Configuration]({{% relref "/config#inventory-controls" %}})

## Point of Interest

| Single Key             | Default Keybinding | Description                                                                          |
|------------------------|--------------------|--------------------------------------------------------------------------------------|
| Next Item              | `Page Down`        | Select next object in current group                                                  |
| Previous Item          | `Page Up`          | Select previous object in current group                                              |
| Narrate current object | `Home`             | Narrate current object tracker object                                                |
| Target nearest object  | `End`              | Target the nearest object relative to your current position, regardless of its group |
| Locking Key            | `Y`                | Lock onto the block or entity that's currently being targetted by the object tracker |

| Key Combination       | Default Keybinding      | Description                                                                          |
|-----------------------|-------------------------|--------------------------------------------------------------------------------------|
| Next Group            | `Control` + `Page Down` | Select next object tracker group                                                     |
| Previous Group        | `Control` + `Page Up`   | Select previous object tracker group                                                 |
| Target Nearest Entity | `Control` + `End`       | Target the nearest entity relative to your current position, regardless of its group |
| Target Nearest Block  | `Shift` + `End`         | Target the nearest block relative to your current position, regardless of its group  |
| Unlock                | `Alt` + `Y`             | Unlock from the currently locked entity or block                                     |
| Mark Target           | `Control` + `Y`         | Mark the block or entity currently targeted with crosshair                           |
| Unmark Target         | `Control` + `Alt` + `Y` | Unmark from the target                                                               |

See also: [Feature Description]({{% relref "/features#points-of-interest" %}}),
[Configuration]({{% relref "/config#point-of-interest" %}})

## Position Narrator

| Single Key            | Default Keybinding | Description                       |
|-----------------------|--------------------|-----------------------------------|
| Speak Player Position | `V`                | Speak the player's x y z position |

| Key Combination      | Default Keybinding | Description               |
|----------------------|--------------------|---------------------------|
| Narrate Z Coordinate | `Alt` + `Z`        | Speak the player's z-axis |
| Narrate X Coordinate | `Alt` + `X`        | Speak the player's x-axis |
| Narrate Y Coordinate | `Alt` + `C`        | Speak the player's y-axis |

See also: [Feature Description]({{% relref "/features#position-narrator" %}}),
[Configuration]({{% relref "/config#position-narrator" %}})

## Speak Player Status

| Single Key                  | Default Keybinding | Description                                                                                |
|-----------------------------|--------------------|--------------------------------------------------------------------------------------------|
| Speak Player Status         | `R`                | Speak the player's current health, hunger, armor, and air and frost exposure if applicable |
| Narrate Main Hand Held Item | `\``               | Narrate the item the player currently holds in their main hand                             |
| Narrate Next Bossbar        | `U`                | Narrates the next visible bossbar on screen                                                |

| Key Combination                 | Default Keybinding | Description                                                                   |
|---------------------------------|--------------------|-------------------------------------------------------------------------------|
| Speak Important Player Statuses | `Alt` + `R`        | Speak only the conditional statuses of the player like air and frost exposure |
| Narrate Player Effects          | `Control` + `R`    | Speak currently active effects                                                |
| Narrate Offhand Held Item       | `Alt` + `\``       | Narrate the item the player currently holds in their offhand                  |
| Narrate Previous Bossbar        | `Shift` + `U`      | Narrates the previous visible bossbar on screen                               |

See also: [Feature Description]({{% relref "/features#player-status" %}}),
[Configuration]({{% relref "/config#player-status" %}})

## Access Menu

| Single Key       | Default Keybinding | Description                                                             |
|------------------|--------------------|-------------------------------------------------------------------------|
| Open Access Menu | `F4`               | Open or close the Access Menu                                           |
| Narrate Target   | `B`                | Narrates the thing you are looking at                                   |
| Upper number row | not re-mappable    | When Access Menu is opened, execute corresponding Access Menu functions |

| Key Combination          | Description                                  |
|--------------------------|----------------------------------------------|
| `Alt` + Upper number row | Execute corresponding Access Menu functions. |

All functions in the Access Menu have unique keybindings that can be set in the game's controls settings menu.
The only function that is bound by default is the narrate target function,
and all other function keys are left up to you to bind if you want to use them.

See also: [Feature Description]({{% relref "/features#access-menu" %}}),
[Configuration]({{% relref "/config#access-menu" %}})

## Book Reading

| Single Key           | Default Keybinding | Description                               |
|----------------------|--------------------|-------------------------------------------|
| Repeat Page Contents | `R`                | Repeats the text of the current book page |

## Speak Chat Messages

| Key Combination                                                       | Description                                                                                                                                    |
|-----------------------------------------------------------------------|------------------------------------------------------------------------------------------------------------------------------------------------|
| `Alt` + Number keys (upper number keys and numpad keys) (1 through 0) | Speak previous chat message (again) corresponding to the number, 1 for the most recent message, 2 for the second most recent message and so on |
| `Alt` + `-` or `Alt` + `Numpad -`                                     | Go back a page in the chat history to the next set of 10 messages                                                                              |
| `Alt` + `Control` + `-` or `Alt` + `Control` + `Numpad -`             | Move back in the chat history by 5 pages (if there is less than 5 pages left you will be moved to the last available page)                     |
| `Alt` + `=` or `Alt` + `Numpad +`                                     | Go forward a page towards the most recent message                                                                                              |
| `Alt` + `Control` + `=` or `Alt` + `Control` + `Numpad +`             | Move back in the chat history by 5 pages (if there is less than 5 pages left you will be moved to the last available page)                     |
| `Alt` + `\`` or `Alt` + `Numpad *`                                    | Go to the most recent page of messages                                                                                                         |
| `Alt` + `Control` + `\`` or `Alt` + `Control` + `Numpad *`            | Go to the oldest page of messages available to your client                                                                                     |

This feature only works while the Chat Screen is open.
These keys aren't re-mappable.
The chat message will be spoken when that message shows up, whether the sender is you or not.
These keys are used to repeat previous chat messages.

See also: [Feature Description]({{% relref "/features#speak-chat-messages" %}})

## Cloth Config Menu Controls

These controls work on any config menu that is powered by Cloth Config.

| Single Key         | Default Keybinding | Description          |
|--------------------|--------------------|----------------------|
| Tab                | not re-mappable    | Focus on next option |
| Enter or Space     | not re-mappable    | Interact             |

| Key Combination                  | Description                        |
|----------------------------------|------------------------------------|
| `Shift` + `Tab`                  | Focus on previous option           |
| `Control` + `Tab`                | Switch to next config category     |
| `Control` + `Shift` + `Tab`      | Switch to previous config category |

## General

| Key Combination | Default Keybinding | Description                                                                   |
|-----------------|--------------------|-------------------------------------------------------------------------------|
| Menu Fix        | `Alt` + `R`        | Perform [`Menu Fix`]({{% relref "/features#menu-fix" %}}) feature if possible |

[controls]: https://minecraft.wiki/w/Controls#Java_Edition
[wiki]: https://minecraft.wiki/?search


---
title: "Configuration"
---

This page contains all the configuration that controls the features on-off and behavior.

The configuration can be modified in two ways: via the config menu or directly editing the configuration file.
The `Config Menu` can be opened via opening the `Access Menu` (press F4) then click `Open Config Menu` button,
or through this mod's settings in mod management menu.
The configuration file (named as `minecraft_access.json`) can be found in the `{minecraft directory}/config` directory.
You can use Notepad or any text editor to edit the file.
You can also use online JSON editor like [Json Editor Online], [Json Formatter] or [Code Beautify JSON Online Editor].

If you want to reset the config back to default values, there are reset buttons aside options in the config menu.

## Camera Controls

| Configuration           | Default Value | Description                                                                                   |
|-------------------------|---------------|-----------------------------------------------------------------------------------------------|
| Enabled                 | true          | Whether to enable this feature                                                                |
| Normal Rotating Angle   | 22.5          | The rotation angle when we press the camera moving keys                                       |
| Modified Rotating Angle | 11.25         | The rotation angle when we press the camera moving keys while holding down the `Left Alt` key |
| Delay (in milliseconds) | 250           | Cooldown between two feature executions                                                       |

See also: [Feature Description]({{% relref "/features#camera-controls" %}}),
[keybindings]({{% relref "/keybindings#camera-controls" %}})

## Mouse Simulation

| Configuration                  | Default Value | Description                                         |
|--------------------------------|---------------|-----------------------------------------------------|
| Enabled                        | true          | Whether to enable this feature                      |
| Scroll Delay (in milliseconds) | 150           | Cooldown between two mouse wheel scroll simulations |

See also: [Feature Description]({{% relref "/features#mouse-simulation" %}}),
[keybindings]({{% relref "/keybindings#mouse-simulation" %}})

## Read Crosshair

| Configuration                                      | Default Value         | Description                                                                     |
|----------------------------------------------------|-----------------------|---------------------------------------------------------------------------------|
| Enabled                                            | true                  | Whether to enable this feature                                                  |
| Speak Block Sides                                  | true                  | Enable speaking of the side of block as well                                    |
| Disable Speaking Consecutive Blocks With Same Name | false                 | Disable speaking the block if the previous block was also same                  |
| Repeat Speaking Interval (in milliseconds)         | 0 (for tuning it off) | Repeat speaking for the given amount of time, even you haven't moved the camera |

Config `Disable Speaking Consecutive Blocks With Same Name` option can be useful
when you don't want to hear repetitive speaking of a large area with the same block type.

See also: [Feature Description]({{% relref "/features#read-crosshair" %}})

### Relative Position Sound Cue

| Configuration    | Default Value | Description                    |
|------------------|---------------|--------------------------------|
| Enabled          | true          | Whether to enable this feature |
| Min Sound Volume | 0.25          | Min volume of the sound cue    |
| Max Sound Volume | 0.4           | Max volume of the sound cue    |

See also: [Feature Description]({{% relref "/features#relative-position-sound-cue" %}})

### Partial Speaking

| Configuration   | Default Value                                                                                            | Description                                                                                                            |
|-----------------|----------------------------------------------------------------------------------------------------------|------------------------------------------------------------------------------------------------------------------------|
| Enabled         | true                                                                                                     | Whether to enable this feature                                                                                         |
| White List Mode | true                                                                                                     | If true, only speak what have been configured. Set to false for blacklist mode, only not speak what you've configured  |
| Fuzzy Mode      | true                                                                                                     | Whether to do fuzzy matching, for example `bed` will match all colors of beds, `door` will match all textures of doors |
| Target Mode     | `block`                                                                                                  | Which type would you like to apply this feature to, either `all`, `entity` or `block`                                  |
| Targets         | [`slab`,&ZeroWidthSpace;`planks`,&ZeroWidthSpace;`block`,&ZeroWidthSpace;`stone`,&ZeroWidthSpace;`sign`] | Indicated what to be spoken                                                                                            |

The `Targets` config can only be configured in `config.json` file.
Values are written in Minecraft resource location format, the so-called
`snake_case` (consists of lowercase letters with underscores).
For example, the White Bed is written in `white_bed`.
There are lots of exceptions, the `Smooth Quartz Block` is written in `smooth_quartz`,
the `Block of Diamond` is written in `diamond_block`,
so please check the correct values in [this wiki link](https://minecraft.wiki/w/Java_Edition_data_values#Blocks)
(expand to show the list by clicking `Blocks[show]` then `Item form's ID[show]`).

See also: [Feature Description]({{% relref "/features#partial-speaking" %}})

## Inventory Controls

| Configuration                                                                             | Default Value | Description                                                                                                                                                        |
|-------------------------------------------------------------------------------------------|---------------|--------------------------------------------------------------------------------------------------------------------------------------------------------------------|
| Enabled                                                                                   | true          | Whether to enable this feature                                                                                                                                     |
| Auto Open Recipe Book                                                                     | true          | Automatically opens the recipe                                                                                                                                     |
| book on opening the inventory (in creative/survival inventory screen and crafting screen) |               |                                                                                                                                                                    |
| Row and Column Format in Crafting Input Slots                                             | `%dx%d`       | The speaking format of row and column prefix, two `%d` are representing the position of row and column                                                             |
| Speak Focused Slot Changes                                                                | true          | Speak if the content of focused slot changes, close it if you hear the mod is continuously repeating changes of item name, some mods like Hypixel will cause that. |
| Delay (in milliseconds)                                                                   | 250           | Cooldown between two feature executions                                                                                                                            |

Most recipes [require][crafting] their ingredients to be arranged in a specific way on the crafting grid
(a.k.a. our crafting input group).
That's how `Row and Column Format in Crafting Input Slots` config can help you,
you'll hear something like `1x2 Empty Slot` which represent you're locating at row one and column two slot inside the
crafting input group, and it contains nothing.

See also: [Feature Description]({{% relref "/features#inventory-controls" %}}),
[keybindings]({{% relref "/keybindings#inventory-controls" %}})

## Point of Interest

| Configuration                           | Default Value | Description                                                                    |
|-----------------------------------------|---------------|--------------------------------------------------------------------------------|
| Speak Relative Distance to Entity/Block | false         | Speak relative distance to the target when using the object tracker or locking |

See also: [Feature Description]({{% relref "/features#points-of-interest" %}}),
[keybindings]({{% relref "/keybindings#point-of-interest" %}})

### Blocks

| Configuration                       | Default Value | Description                                                 |
|-------------------------------------|---------------|-------------------------------------------------------------|
| Enabled                             | true          | Whether to enable scanning blocks                           |
| Detect Fluid Blocks                 | true          | Enable detecting fluid blocks (water and lava)              |
| Range                               | 6             | Range of blocks to scan                                     |
| Play Sound                          | true          | Play a sound cue at positions of detected blocks            |
| Sound Volume                        | 0.25          | Volume of the sound cue                                     |
| Play Sound for Other Blocks as well | false         | Play sound cue for other blocks other than the ores as well |
| Delay (in milliseconds)             | 3000          | Execute at set intervals                                    |

### Entities

| Configuration           | Default Value | Description                                        |
|-------------------------|---------------|----------------------------------------------------|
| Enabled                 | true          | Whether to enable scanning entities                |
| Range                   | 6             | Range of entities to scan                          |
| Play Sound              | true          | Play a sound cue at positions of detected entities |
| Sound Volume            | 0.25          | Volume of the sound cue                            |
| Delay (in milliseconds) | 3000          | Execute at set intervals                           |

### Entities/Blocks Locking

| Configuration                          | Default Value | Description                                                                  |
|----------------------------------------|---------------|------------------------------------------------------------------------------|
| Auto Lock on to Eye of Ender when Used | true          | Automatically lock on to the [Eye of Ender] when used                        |
| Delay (in milliseconds)                | 100           | Cooldown between two feature executions                                      |
| Bow aim assist                         | true          | Enable automatic temporary locking onto the nearest monster when using a bow |
| Aim assist sound                       | true          | Enables audio cues for bow aim assist                                        |
| Aim assist sound volume                | 0.5           | Controls the volume of the aim assist audio cues                             |

### Entities/Blocks Marking

| Configuration                      | Default Value | Description                                                             |
|------------------------------------|---------------|-------------------------------------------------------------------------|
| Enabled                            | true          | Whether to enable this feature                                          |
| Suppress Other POI When Marking On | true          | When marking on, suppress scanning and notifying on pre-configured ones |

## Position Narrator

These configs are under `General` section in the config file and config menu.

| Configuration            | Default Value      | Description                                                                                          |
|--------------------------|--------------------|------------------------------------------------------------------------------------------------------|
| Enable Position Narrator | true               | Whether to enable this feature                                                                       |
| Position Narrator Format | `{x}x, {y}y, {z}z` | The speaking format of the position, `{x}`, `{y}`, `{z}` represent the corresponding axis's position |

See also: [Feature Description]({{% relref "/features#position-narrator" %}}),
[keybindings]({{% relref "/keybindings#position-narrator" %}})

## Player Status

This config is under `Features` section in the config file and config menu.

| Configuration       | Default Value | Description                                                                          |
|---------------------|---------------|--------------------------------------------------------------------------------------|
| Enable PlayerStatus | true          | Whether to enable this feature and automatically effect gaining and losing narration |

See also: [Feature Description]({{% relref "/features#player-status" %}}),
[keybindings]({{% relref "/keybindings#player-status" %}})

## Player Warnings

| Configuration           | Default Value | Description                                                 |
|-------------------------|---------------|-------------------------------------------------------------|
| Enabled                 | true          | Whether to enable this feature                              |
| Play Sound              | true          | Play a sound cue when any of the status reach the threshold |
| Health Threshold First  | 6             | The first threshold for health                              |
| Health Threshold Second | 3             | The seconds threshold for health                            |
| Hunger Threshold        | 3             | The threshold for hunger/food                               |
| Air Threshold           | 3             | The threshold for air when you're submerged in water        |

### Durability Warnings

| Configuration    | Default Value | Description                                                            |
|------------------|---------------|------------------------------------------------------------------------|
| EnableHeldItems  | true          | Whether to check the durability of main and off hand items             |
| EnableWornArmor  | true          | Whether to check the durability of armor that the player is wearing    |
| First Threshold  | 10            | The durability number that you want the less severe warning to play at |
| Second Threshold | 3             | The durability number that you want the more severe warning to play at |


See also: [Feature Description]({{% relref "/features#player-warnings" %}})

## Fall Detector

| Configuration           | Default Value | Description                         |
|-------------------------|---------------|-------------------------------------|
| Enabled                 | true          | Whether to enable this feature      |
| Range                   | 6             | Range of drop to scan               |
| Depth Threshold         | 4             | The threshold for playing the sound |
| Sound Volume            | 0.25          | Volume of the sound cue             |
| Delay (in milliseconds) | 2500          | Execute at set intervals            |

See also: [Feature Description]({{% relref "/features#fall-detector" %}})

## Access Menu

| Configuration | Default Value | Description                    |
|---------------|---------------|--------------------------------|
| Enabled       | true          | Whether to enable this feature |

See also: [Feature Description]({{% relref "/features#access-menu" %}}),
[keybindings]({{% relref "/keybindings#access-menu" %}})

### Fluid Detector

| Configuration | Default Value | Description                                                                                                  |
|---------------|---------------|--------------------------------------------------------------------------------------------------------------|
| Range         | 10            | Range of fluid to scan, be careful, the default 10 blocks will already make the game lag for a second or two |
| Sound Volume  | 0.25          | Volume of the sound cue                                                                                      |

### Features

| Configuration                                    | Default Value | Description                                                                                                            |
|--------------------------------------------------|---------------|------------------------------------------------------------------------------------------------------------------------|
| Enable Biome Indicator                           | true          | Whether to enable [`Biome Indicator`]({{% relref "/features#biome-indicator" %}}) feature                              |
| Always Narrate dimension name in biome indicator | false         | This option will include the current dimension name whenever a biome indicator announcement is narrated                |
| Enable Time Indicator                            | true          | Whether to enable time of day Indicator                                                                                |
| Enable XP Indicator                              | true          | Whether to enable [`XP Indicator`]({{% relref "/features#xp-indicator" %}}) feature                                    |
| Enable Facing Direction                          | true          | Whether to automatically speak the current direction as the camera moves                                               |
| Speak Action Bar Messages                        | true          | Whether to speak the messages updated in [action bar], useful when you're in modded multiplayer servers                |
| Only Speak Action Bar Updates                    | false         | Only speak changed part of action bar message when the message is partially updated, useful for some mods like Hypixel |
| Speak Picked Up Items                            | WHEN_FISHING  | Whether to speak any items you pick up (either ALWAYS, WHEN_FISHING, or NEVER)                                         |
| Report Held Items Count When Changed             | true          | Whether to report the number of held items when it changed                                                             |
| Play a sound for new chat messages               | true          | Whether to play a sound when sending or recieving a chat message                                                       |

## General

| Configuration                          | Default Value | Description                                                                                                                                                               |
|----------------------------------------|---------------|---------------------------------------------------------------------------------------------------------------------------------------------------------------------------|
| Command Suggestion Narrator Format     | `%dx%d %s`    | The speaking format of the command suggestion, two `%d` represent the order of focused suggestion and total number of suggestions, `%s` represents the suggestion content |
| Use 12 Hour Time Format                | false         | Whether to use 12 hour time format when speaking the time                                                                                                                 |
| Enable Menu Fix                        | true          | Whether to enable [`Menu Fix`]({{% relref "/features#menu-fix" %}}) feature                                                                                               |
| Debug Mode                             | true          | Developer config, whether to print debug messages into log                                                                                                                |
| Multiple Click Speed (in milliseconds) | 750           | The maximum time interval between two keystrokes in multiple click operations like `double-click`                                                                         |

See also: [Feature Description]({{% relref "/features#other-small-features" %}})

[Json Editor Online]: https://jsoneditoronline.org/
[Json Formatter]: https://jsonformatter.org/json-editor
[Code Beautify JSON Online Editor]: https://jsonformatter.org/json-editor
[crafting]: https://minecraft.wiki/w/Crafting
[Eye of Ender]: https://minecraft.wiki/w/Eye_of_Ender
[action bar]: https://minecraft.wiki/w/Commands/title


---
title: "Good Resources"
---

## Helpful Links

* [Documentation for this mod](https://github.com/minecraft-access/minecraft-access) -
  Check what features this mod provides.
* [Playability Discord server](https://discord.mcaccess.org/) -
  You can join our Discord server if you need help setting up the mod or with any issue related to
  Minecraft Java Edition.

## Quality of Life Mods

Here are some mods that can improve your game experience.
There are guides about [installing]({{% relref "/setup/basic#install-your-mods" %}}) and
[upgrading]({{% relref "/setup/basic#update-the-game-and-mods" %}}) mods.

1. Client side, which you can use in both a single-player and multiplayer game:
    * Presence Footsteps ([Fabric](https://modrinth.com/mod/presence-footsteps),
      [NeoForge port](https://www.curseforge.com/minecraft/mc-mods/presence-footsteps-forge)):
      Footstep sound enhancement mod. If the Fabric version of the mod isn't working,
      select `Default sound pack` in the resource pack menu of your game.
      If the mod does not support your game version, you should use this resource pack
      instead:
      [Presence Footsteps: Remastered Sounds Pack](https://modrinth.com/resourcepack/presense-footsteps-sounds),
      put it in the `%appdata%\.minecraft\resourcepacks` folder.
    * Just Enough Items ([Fabric and NeoForge](https://modrinth.com/mod/jei)):
      Item and recipe viewing mod, far better than the similar recipe book feature in the original game
      (but this mod isn't 100% accessible with Minecraft Access, you need some vision to use it).
    * Sound Physics Remastered ([Fabric and NeoForge](https://modrinth.com/mod/sound-physics-remastered)):
      Provides realistic sound attenuation, reverberation, and absorption through blocks.
      I'm sure this mod consumes resources, so install it only when you're confident in your PC hardware.
    * Jade ([Fabric and NeoForge](https://modrinth.com/mod/jade)):
      Shows/speaks more detailed information about what you are looking at when you press the narration key.
      You must remap the Jade keys to not conflict with this mod's numpad mappings.
      There is also a fully accessible in-game config menu to change what types of information the mod will narrate.

2. Server side, which you can only use in a single-player game
   (unless the multiplayer server admins add them to the server as well):
    * Tree Harvester ([Fabric and NeoForge](https://modrinth.com/mod/tree-harvester)):
      Harvest trees and huge mushrooms instantly with an axe.
      It's a common scenario in Minecraft that you can't reach the logs and leaves at a higher place when standing on
      the ground, with this mod, you won't bother yourself with getting yourself up to reach them.
    * Carry On ([Fabric and NeoForge](https://modrinth.com/mod/carry-on)):
      Allows players to pick up, carry, and place some blocks (such as Chests) and animals without breaking blocks
      or guiding animals with a leash.
    * FTB Ultimine ([Fabric](https://www.curseforge.com/minecraft/mc-mods/ftb-ultimine-fabric)):
      This mod can make you operate on the same type of blocks around you by operating only once,
      like harvesting crops, mining a vein, and chopping trees.
    * Simple Voice Chat ([Fabric and NeoForge](https://modrinth.com/plugin/simple-voice-chat)):
      A proximity voice chat for Minecraft.

## Community Modpacks

* Visually Impaired Access Mods+Fabric ([Fabric](https://modrinth.com/modpack/TAT3EDpw)):
  The community member [@BrailleBennett](https://github.com/BrailleBennett)
  has created a modpack that contains performence mods, along with many pre-configured accessibility mods.
  It is recommended for people who want a well rounded, accessible experience while playing,
  and don't want to worry about finding and configuring any mods.

## Tutorial Resources

### General Minecraft tutorials

These resources teach you how to play Minecraft
but lack detail about controlling using this mod since they’re playing without this mod.
It is recommended to learn how to use this mod first, then learn how to play the game.
Minecraft has a long history since 2009, and there are many tutorials about it,
almost every question has already been answered several times on the Internet.
But due to this long history, there is lots of outdated information about this game as well,
keep in mind to add game variant and game version as keywords while searching.
(example: java 26.1)

* Wiki website in multiple languages, continually updated: [Minecraft Wiki](https://minecraft.wiki/w/Minecraft_Wiki)
  by hardworking volunteers. Has details and guides on everything about Minecraft,
  it also has [many text tutorials](https://minecraft.wiki/w/Tutorials) on it.
* Tutorial video series on YouTube in English, last updated 2024-06-21:
  [Minecraft Survival Guide (Season 3)](https://www.youtube.com/watch?v=VfpHTJsn9I4&list=PLgENJ0iY3XBjmydGuzYTtDwfxuR6lN8KC)
  by [Pixlriffs](https://www.youtube.com/@Pixlriffs).
  A tutorial series on survival mode in Minecraft 1.20 (and later on 1.21), includes a lot of topics.

For a specific question, you can:

* Ask AI, no kidding, AI + search engine platforms like [Perplexity](https://www.perplexity.ai/),
  [Bing AI](https://www.bing.com/chat) and [Google Gemini](https://gemini.google.com/app)
  do know a lot about Minecraft since they are fed with a huge amount of knowledge about it.
* Read the corresponding [wiki](https://minecraft.wiki/) page.
  The wiki describes every detail of a topic, but you may find it's too lengthy.
* Traditional way, search on search engines like Google.

### Gameplay with this mod

These resources are tutorials and showcases about how to play Minecraft with this mod
(many thanks for making them, creators), resources are ordered in reverse chronological order, first is newest.

#### Tutorial with this mod

* Game play video series on YouTube in English, last updated 2024-06-22:
  [The TrueBlindCraft Guide Series](https://www.youtube.com/playlist?list=PLXcBFfRlLcpiDpULQB3L2aX-AQb4-Atbq)
  by TrueBlindGaming.
  A tutorial series to help visually impaired/blind players who use the MineCraft Access mod to play MineCraft.
  Playing in Minecraft 1.20. Includes world creation, tree finding, navigation, and more.
* Building tutorial video on Bilibili in Chinese (no English subtitles), uploaded 2023-07-01:
  [一个全盲视障者玩家的基建经验，来教盲人小伙伴们如何修房子](https://www.bilibili.com/video/BV1eh411N7y1) by 红马尾战士紫月sama.
  A building tutorial about how to build a basic square shape house with this mod.
* Installation tutorial video on Bilibili in Chinese (no English subtitles), uploaded 2023-05-06:
  [【我的世界·MC】视障辅助MOD安装与使用教程](https://www.bilibili.com/video/BV1W24y1T7f9) by 红马尾战士紫月sama.
  The tutorial includes mod installation, game launching, and brief instructions on mod usage and game playing.
  Partially outdated, tolk is now installed automatically,
  and this mod doesn't need the `fabric-api` mod as a dependency for now,
  see the [setup guide]({{% relref "/setup/basic" %}}) for full current instructions in text form.
* Installation tutorial video on YouTube in English, uploaded 2023-03-21:
  [How to Install Mine Craft Access, a mine craft java mod for the blind](https://www.youtube.com/watch?v=MTRG3S0xeeg)
  by Logic Pro X Gaming.
  A tutorial includes mod installation and game launching.
  Partially outdated, tolk is now installed automatically,
  and this mod doesn't need the `fabric-api` mod as a dependency for now,
  see the [setup guide]({{% relref "/setup/basic" %}}) for full current instructions in text form.

#### Gameplay with this mod without intentional teaching

* Video on Bilibili in Chinese (no English subtitles), uploaded 2023-04-16:
  [视障者要怎么玩我的世界，除了有无障碍辅助还要有对空间的理解力有要求吧？mc无障碍mod展示](https://www.bilibili.com/video/BV1Dc411n738)
  by 红马尾战士紫月sama. Brief showcase of playing Minecraft with this mod.
* Video series on YouTube in English, last updated 2022-03-30:
  [TrueBlindCraft](https://www.youtube.com/playlist?list=PLXcBFfRlLcpipJM2GK6cV4wrQmAM34AWG) by TrueBlindGaming.
  This playlist includes all minecraft gameplay videos on TrueBlindGaming's channel since 2021.
* Video series on YouTube in English, last updated 2022-02-09:
  [The oldest blind server in mine craft](https://www.youtube.com/playlist?list=PL2nN_7cg-MaHB7b8wC3VJuwxLKSB4OBH5)
  by Logic Pro X Gaming. This playlist includes all minecraft gameplay videos on Logic Pro's channel since 2019.
  `This is a playlist from the start to current footage on the blind craft server that we did not lose. Enjoy`

## Beginners Guide

Hi, I'm Boholder, a player and mod developer of Minecraft.
First of all, I'm sighted, but that didn't stop me from being bored by this game the first time I played it.
I didn't know how to play it, except to break and collect blocks,
rise myself very high by stacking blocks underneath me then jumping off to die,
and to be attacked by monsters to die when it comes to night.
Boring, and a bit of scaring because there are monsters at night, this is my first impression of Minecraft.

One day I accidentally discovered a Minecraft forum when I was surfing the web,
and the interesting posts on this forum made me realise that this game wasn't that simple.
I followed the posts, tutorials to learn to play this game, read every page of the wiki site,
and play the game in single player mode.
This game became my favorite game.
Later I decided it was too lonely to play alone, I started playing in servers, posted in forums, attending events,
made quite a few friends (some of whom I'm still in touch with).
One of the reasons I chose to major in software engineering was because I wanted to write mods for this game,
this game has really impacted my life.
I hope I can share some joy of Minecraft with you through this mod.
Ok, enough nonsense, so... where do we start?

### Create a Good World

You can learn this part with the following resources:

* [The TrueBlindCraft Guide::Episode 1: navigating your world and finding your first Tree](https://www.youtube.com/watch?v=fdDpLoCuWBs&list=PLXcBFfRlLcpiDpULQB3L2aX-AQb4-Atbq&index=1)
* [Minecraft Wiki Tutorial: Menu screen](https://minecraft.wiki/w/Tutorials/Menu_screen)

Because Minecraft's world generation is randomized,
you may run into the difficulty of trying to generate multiple worlds,
but all of their spawn locations are bad.
You can search good [world generation seeds](https://minecraft.wiki/w/Seed_(level_generation)) on Google.
And set them when you're creating single player worlds.
Be sure to include the game version and `Java Edition` in your Google search.
I highly recommend you lower the difficulty of the game to get a better experience,
especially if you're new to the game,
such as changing the [difficulty](https://minecraft.wiki/w/Difficulty) to `Peaceful` to avoid monster spawning.

### Game Control

You can learn this part with the following resources:

* [The TrueBlindCraft Guide::Episode 3: Controls for MineCraft and the MineCraft-Access Mod](https://www.youtube.com/watch?v=tPERR_oYzn8&list=PLXcBFfRlLcpiDpULQB3L2aX-AQb4-Atbq&index=3)
* [Minecraft Wiki Tutorial: Beginner's guide](https://minecraft.wiki/w/Tutorials/Beginner%27s_guide)

Minecraft is a [first-person](https://en.wikipedia.org/wiki/First-person_(video_games)) 3D game,
its control design is similar to other first-person games,
you need to control your in-game character with keyboard AND mouse
(don't worry, this mod provides pure keyboard controlling replacement).
If you have some usable vision and can see the game screen
(can distinguish the individual blocks that make up the world), it is recommended to play with keyboard and mouse.
If you’re unable to see the game screen,
it is recommended to use a full-size keyboard which has the numeric keypad on the right.

You can move around with [W A S D](https://en.wikipedia.org/wiki/Arrow_keys#WASD_keys), jump with space,
crouch with ctrl, sprint with shift (these are default keybindings).
A first-person game means the game screen shows from the viewpoint of your in-game character's eyes.
You can rotate character's view (or `camera`, in industry term) smoothly with mouse.
The term of this mouse controlling system is
[Free look (also known as mouse look)](https://en.wikipedia.org/wiki/Free_look).
This type of control might be unfamiliar to you if you haven't played similar games before,
it's sort of like controlling a tank, moving and looking is separated as two operations,
your can operate them at same time.
The direction you're facing is your `front`, so if you want to go back,
turn your head 180 degrees to look back then press W to move forward,
or press S to move backward while you still looking at front.

You can `target at` (adjust view with mouse until the target is at the center of the view)
things you're interested in and interact with mouse click.
Some operations require continually pressing keys,
like pressing W to move forwards, or pressing the left mouse button to break blocks.
You can learn vanilla game controls on [corresponding wiki page](https://minecraft.wiki/w/Controls#Java_Edition)
(ignore the F3 key) or read keybinding settings in game.
Controls provided by this mod are described in mod manual pages:
[Camera Controls]({{% relref "/keybindings#camera-controls" %}}),
[Mouse Simulation]({{% relref "/keybindings#mouse-simulation" %}}).

### Finding Trees

You can learn this part with the following resources:

* [The TrueBlindCraft Guide::Episode 1: navigating your world and finding your first Tree](https://www.youtube.com/watch?v=fdDpLoCuWBs&list=PLXcBFfRlLcpiDpULQB3L2aX-AQb4-Atbq&index=1)
* [Minecraft Wiki: Coordinates](https://minecraft.wiki/w/Coordinates)
* [Minecraft Wiki: Biome](https://minecraft.wiki/w/Biome)
* [Minecraft Wiki: Tree](https://minecraft.wiki/w/Tree)

Trees consist of log blocks and leaves blocks, we need log blocks as a resource for crafting.
Although each tree has a straight log block trunk and a canopy made of leaves blocks,
the height and shape of the tree is randomly generated;
some short trees will have leaves that come down low to the ground and enclose the logs two or three layers
(as TrueBlind encounters in his guide episode 2),
and some taller trees may have logs which float up high that you can't collect from the ground
(they're used to mimic tree branches).
If you’re targeting at a tree, you will hear the mod speak log block or leaves block.

How to move your character to find trees?
First get to a proper biome that has trees in it at spawned by making new worlds,
or by using the `/locate biome forest` and `/tp x y z` commands to find and teleport to it.
Then move around to see if you can encounter a tree.
You can use TrueBlind's `reference point` method
to remember coordinates as a teleport target when you're lost in the world.
If you can no longer walk forward, you can pick up the mouse and move it horizontally.
You can also keep pressing one of the camera horizontal control keys to turn your head around,
or press A or D while walking to move left or right.
All of these actions are to change the part of the world you’re viewing.

Once you find a log block, you can use the [POI Marking feature]({{% relref "/features#poi-marking" %}}).
When you’re targeting a log block, press `Ctrl + Y` to mark that log block type,
the POI feature will only hint for the log blocks that are around you with sounds,
instead of hinting preset POI blocks.
When you no longer want to find trees, press `Ctrl + Alt + Y` to cancel the marking,
you don't need to target the currently marked target when canceling.

More convenient features for finding surrounding trees will be introduced in the future.

### Commands

You can learn this part with the following resources:

* [Official Tutorials: How to Use Commands in Minecraft](https://www.minecraft.net/en-us/article/minecraft-commands)
* [Minecraft Wiki: Commands](https://minecraft.wiki/w/Commands)

Some commands are very convenient when you're playing single-player worlds.
You can use them to gain advantages or to improve your game experience.
Remember to turn on `Allow Cheats` option when you create the world.



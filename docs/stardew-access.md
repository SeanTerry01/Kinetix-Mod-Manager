# Stardew Access

Stardew Access is a Stardew Valley mod that focuses on making the game accessible to blind screen reader users on Windows, Linux and Mac OS. It adds narration through the active screen reader to the game, the menus, the character dialogue, and more to give the player information about their farmer and the world around them. Nearby objects, monsters, and favorite locations are only a few keystrokes away with the object tracker. Planting, harvesting, construction, and mining are all made easy via narration of tiles and the tile reader.

This is an offline copy bundled with Kinetix Mod Manager. When you are connected to the internet, reopen this viewer to load the latest, complete documentation straight from the mod's developers.

## Features Overview

### Game Narration

Tap into your screen reader to narrate menus, tiles, character dialogue, chests, health, money, time of day, and much more to play Stardew Valley without sight. Some information is narrated on request: the player's map and coordinates, health and energy, currency, and more. Warnings are also included for full inventory, low health, low energy, and being out very late.

If you use NVDA, make sure that "sleep mode" is turned on for Stardew Valley, or that "Speech interrupt for typed characters" in NVDA's keyboard settings is turned off, so narration is not cut short.

### Tile Reader and Tile Viewer

The tile reader is the core of Stardew Access: it reads the tile the player is currently facing. The tile viewer extends this by letting you move a tile cursor anywhere on the map to explore, place items, and interact with machines. You can walk to a selected tile with a keystroke, or open the Tile Info Menu for more detail.

### Object Tracker

Browse every object on a map, organized by category and proximity. Get coordinates and distance, automatically travel to the object of your choosing, and store favorite locations on each map for quick access with less searching.

### Grid Movement

Automatically snap your farmer to the tile grid when walking around. Great for planting crops and cleaning up your farm.

## Installation

You must own a copy of Stardew Valley on Windows, Mac, or Linux. The recommended path is the Accessible Stardew Setup (ASS) installer, which installs SMAPI, Stardew Access, and its dependencies (Kokoro and Project Fluent) automatically. Manual installation is also supported: install SMAPI from smapi.io, then add Stardew Access, Kokoro, and Project Fluent to your Mods folder.

## Keybindings

All keybindings may be modified in the config except for escape.

### Global Keys

| Key | Description |
|-----|-------------|
| left ctrl + enter | primary left mouse click |
| left shift + enter | primary right mouse click |
| [ | secondary left mouse click |
| ] | secondary right mouse click |
| h | narrate health and stamina; also speaks active buffs and debuffs in the inventory page |
| left alt + k | narrate current map |
| k | narrate current coordinates |
| q | narrate time, date, and season |
| r | narrate current money; also speaks currency and event items in the inventory page |
| left alt + j | narrate the tile the player is standing on |
| j | narrate the tile the player is facing |
| c | primary info key (see Primary Info Key) |
| esc | close menus; stop interacting with text boxes |
| left/right alt + space | repeat last spoken text; double or triple press for older phrases |

### Tile Viewer Keys

| Key | Description |
|-----|-------------|
| l | toggle relative cursor lock |
| left ctrl + enter | move the player to the focused tile |
| left shift + enter | open the Tile Info Menu for the focused tile |
| arrow keys | move the tile cursor one tile up, down, left, or right |
| left shift + arrow keys | move the tile cursor pixel by pixel |

### Grid Movement Keys

| Key | Description |
|-----|-------------|
| i | toggle grid movement |
| left ctrl | disable grid movement while held |

### Object Tracker Keys

| Key | Description |
|-----|-------------|
| left ctrl + page up | previous category |
| left ctrl + page down | next category |
| page up | previous object |
| page down | next object |
| left ctrl + home | move to the selected object |
| home | narrate info about the selected object |
| end | narrate the selected object's tile location |
| esc | stop walking to a selected tile or object |
| ~ | toggle between alphabetical and proximity sorting |
| alt + 1 through 0 | favorite slot info (slots 1 to 10) |
| alt + double-tap 1 through 0 | set a favorite slot, or travel to it if already set |
| alt + triple-tap 1 through 0 | clear a favorite slot |
| alt + minus / alt + equals | previous / next stack of favorites |

### Menu Keys

In menus with an inventory (shops, chests): press `i` to select the first item in the crafting, chest, or shop area, and `left shift + i` to select the first item in your own inventory.

In the Junimo Note or Community Center menu: `i` and `left shift + i` move between bundle items, `c` and `left shift + c` move through your inventory, `v` and `left shift + v` move between bundle ingredient slots, `p` moves to the purchase button, and backspace moves to the back button.

### Primary Info Key

`c` by default acts as a context info key in several menus: it moves to the next recipe in the Crafting Menu, speaks the current blueprint in the Construction Menu, speaks an animal's details in the Animal Info Menu, donates the focused item in the Museum Menu, speaks pond info in the Fish Pond Menu, and speaks the current quest in the Quest Menu.

## Commands

Stardew Access adds console commands (use the SMAPI console). A selection:

| Command | Description |
|---------|-------------|
| readtile | toggle the Read Tile feature |
| snapmouse | toggle the Snap Mouse feature |
| flooring | toggle reading flooring |
| watered | toggle speaking watered or unwatered for crops |
| mark [0-9] | mark the player's current position at an index |
| marklist | list all marked positions |
| buildlist | list all buildings for selection |
| buildsel [index] | select a building to place a farm animal in |
| hnspercent | toggle speaking health and stamina as a percentage |
| warning | toggle the warnings feature |
| tts | toggle the screen reader / text to speech |
| refsr | refresh the screen reader |
| radar | toggle the Radar feature |

## Configuration

Stardew Access is configured via `config.json` in the `Mods/stardew-access/` folder, created the first time you run the game with the mod loaded. Edit it with a plain text editor such as Notepad or Notepad++ (not a word processor). The config covers mouse-simulation keys, tile reader and tile viewer options, grid movement, the object tracker, the radar, menu keys, the fishing mini-game, and narration verbosity.

## Useful Links

- Stardew Access on Nexus Mods: https://www.nexusmods.com/stardewvalley/mods/16205
- Source code and full documentation: https://github.com/stardew-access/stardew-access
- Discord server: https://discord.gg/yQjjsDqWQX

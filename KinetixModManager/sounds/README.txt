Sound themes
============

Every folder in here is a sound theme, and a theme is chosen by the game that is
loaded rather than by the user: load Skyrim and you hear Skyrim's sounds, load
Minecraft and you hear Minecraft's. That is the point of them. You can tell which
game a session belongs to by ear, before anything is read out.

A game is joined to its theme in Kinetix.Core/GameProfiles.cs, by the SoundTheme
line in its profile. The folder in here has to be named exactly that.

A user who would rather pick a theme themselves turns on "Let me choose the sound
theme" in Settings; until they do, the theme follows the game.

Adding a game's sounds
----------------------

Make a folder named after the game's SoundTheme, then a folder inside it for each
event below, and put one .ogg in each. Nothing else is needed - no code change, no
registration. Any event you have not authored yet plays the Default theme's sound,
so a half-finished pack is quieter than Default rather than silent.

    connect            Connected to where this game's mods come from.
                       For Minecraft this is different: it plays when you join a
                       multiplayer server, read from the game's own log.
    disconnect         Disconnected, no API key yet, the session closed, or the
                       program is closing. For Minecraft: leaving a server.
    enable             One or more mods were switched on.
    disable            One or more mods were switched off or deleted.
    error              Something failed - a download, an install, a check.
    loading_indicator  Pulses while a background update check is running.
    load_complete      An operation finished: an install, an update, an import, a
                       check, a load-order sort.
    logo               Startup sounds. Unlike the others this holds as many .ogg
                       files as you like; the user picks one in Settings.

Keep them short. These land while the screen reader is talking, which is what makes
them useful - the sound says what happened without interrupting the sentence.

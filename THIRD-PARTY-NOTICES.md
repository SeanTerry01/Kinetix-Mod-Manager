# Third-party notices

Kinetix Mod Manager's own code and its Default sound theme are licensed under the GNU General Public License,
version 3. See [LICENSE](LICENSE). This file lists what is **not** covered by that licence: the libraries the manager
uses, each under its own licence, and the sounds in the game themes.

## Game sounds

The sound themes named after games (Fallout 4, Minecraft, Moonlight Peaks, Skyrim, Stardew Valley and The Witcher 3)
use sounds taken from those games. They belong to the games' publishers and developers, not to this project, and the
GPL does not apply to them. They are included so that the manager can sound like the game it is managing, and will
be removed at the request of their owner.

| Theme | Sounds from | Belonging to |
|---|---|---|
| Fallout 4 | Fallout 4 | Bethesda Softworks |
| Minecraft | Minecraft: Java Edition | Mojang Studios / Microsoft |
| Moonlight Peaks | Moonlight Peaks | Little Chicken Game Company / XSEED Games |
| Skyrim | The Elder Scrolls V: Skyrim | Bethesda Softworks |
| Stardew Valley | Stardew Valley | ConcernedApe |
| The Witcher 3 | The Witcher 3: Wild Hunt | CD PROJEKT RED |

Only the files in `KinetixModManager/sounds/Default` (`sounds\Default` in the installed manager) are the project's own.

## Libraries

| Library | Used for | Licence |
|---|---|---|
| [Tolk](https://github.com/dkager/tolk) | Speaking through the screen reader | LGPL-3.0 |
| [NVDA Controller Client](https://github.com/nvaccess/nvda) | Speaking through NVDA (used by Tolk) | LGPL-2.1 |
| [NAudio](https://github.com/naudio/NAudio) | Playing sounds | MIT |
| [NVorbis](https://github.com/NVorbis/NVorbis) and [NAudio.Vorbis](https://github.com/naudio/Vorbis) | Reading .ogg sound files | MIT |
| [SharpCompress](https://github.com/adamhathcock/sharpcompress) | Unpacking .zip, .7z and .rar mods | MIT |
| [Newtonsoft.Json](https://github.com/JamesNK/Newtonsoft.Json) | Reading and writing JSON | MIT |
| [YamlDotNet](https://github.com/aaubry/YamlDotNet) | Reading the LOOT masterlist | MIT |
| [K4os.Compression.LZ4](https://github.com/MiloszKrajewski/K4os.Compression.LZ4) | Reading Skyrim save files | MIT |
| [Microsoft.Data.Sqlite](https://github.com/dotnet/efcore) | Importing from the Modrinth App | MIT |
| [SQLitePCLRaw](https://github.com/ericsink/SQLitePCL.raw) | SQLite for Microsoft.Data.Sqlite | Apache-2.0 |
| [SQLite](https://sqlite.org) | The database engine | Public domain |
| [Microsoft WebView2](https://learn.microsoft.com/microsoft-edge/webview2/) | The built-in browser for Nexus Mods | Microsoft WebView2 SDK licence (BSD-style) |
| [.NET](https://github.com/dotnet/runtime) | The runtime the manager ships with | MIT |
| [Gir.Core](https://github.com/gircore/gir.core) | GTK 4 for the Linux version | MIT |

The tests also use [xUnit](https://github.com/xunit/xunit) (Apache-2.0), which is not shipped with the manager.

Tolk and the NVDA Controller Client are shipped unchanged, as separate DLLs in the `lib` folder, as the LGPL allows.
Each library's full licence text is available at the link above.

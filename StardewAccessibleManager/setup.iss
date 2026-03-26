; Inno Setup Script for Stardew Valley Accessible Manager
; Compatible with Single-File .NET 10 Publishing

[Setup]
AppId={{D37D0B2E-6D3F-4B1C-BD03-9A1D2E3F4B5C}
AppName=Stardew Valley Accessible Manager
AppVersion=1.0.1
AppPublisher=Audi Venture Games
AppPublisherURL=https://www.nexusmods.com/stardewvalley/mods/32385
AppSupportURL=https://www.nexusmods.com/stardewvalley/mods/32385
AppUpdatesURL=https://www.nexusmods.com/stardewvalley/mods/32385
DefaultDirName={autopf}\StardewAccessibleManager
DefaultGroupName=Stardew Accessible Manager
AllowNoIcons=yes
; Targets x64 exclusively
ArchitecturesAllowed=x64
ArchitecturesInstallIn64BitMode=x64
; Output configuration
OutputDir=Setup
OutputBaseFilename=StardewAccessibleManager_Setup
Compression=lzma
SolidCompression=yes
WizardStyle=modern
; Screen reader optimization
DisableProgramGroupPage=yes
DisableWelcomePage=no

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
; The Single-File EXE
Source: "bin\Release\net10.0-windows\publish\StardewAccessibleManager.exe"; DestDir: "{app}"; Flags: ignoreversion
; Native DLLs (Tolk, NVDA Controller, etc.)
Source: "bin\Release\net10.0-windows\publish\*.dll"; DestDir: "{app}"; Flags: ignoreversion
; Manual for help menu
Source: "bin\Release\net10.0-windows\publish\MANUAL.md"; DestDir: "{app}"; Flags: ignoreversion
; Sound themes and initial folders - using skipifsourcedoesntexist to allow empty folders
Source: "bin\Release\net10.0-windows\publish\sounds\*"; DestDir: "{app}\sounds"; Flags: ignoreversion recursesubdirs createallsubdirs skipifsourcedoesntexist
Source: "bin\Release\net10.0-windows\publish\profiles\*"; DestDir: "{app}\profiles"; Flags: ignoreversion recursesubdirs createallsubdirs skipifsourcedoesntexist

[Dirs]
; Ensure these directories exist even if they are empty in the installer
Name: "{app}\sounds"
Name: "{app}\profiles"
Name: "{app}\downloads"
Name: "{app}\backups"

[Icons]
Name: "{group}\Stardew Valley Accessible Manager"; Filename: "{app}\StardewAccessibleManager.exe"
Name: "{autodesktop}\Stardew Valley Accessible Manager"; Filename: "{app}\StardewAccessibleManager.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\StardewAccessibleManager.exe"; Description: "{cm:LaunchProgram,Stardew Valley Accessible Manager}"; Flags: nowait postinstall skipifsilent

[Registry]
; Register nxm:// protocol for Nexus Mods downloads
Root: HKCU; Subkey: "Software\Classes\nxm"; ValueType: string; ValueName: ""; ValueData: "URL:Nexus Mod Manager Protocol"; Flags: uninsdeletekey
Root: HKCU; Subkey: "Software\Classes\nxm"; ValueType: string; ValueName: "URL Protocol"; ValueData: ""; Flags: uninsdeletekey
Root: HKCU; Subkey: "Software\Classes\nxm\DefaultIcon"; ValueType: string; ValueName: ""; ValueData: "{app}\StardewAccessibleManager.exe,0"
Root: HKCU; Subkey: "Software\Classes\nxm\shell\open\command"; ValueType: string; ValueName: ""; ValueData: """{app}\StardewAccessibleManager.exe"" ""%1"""

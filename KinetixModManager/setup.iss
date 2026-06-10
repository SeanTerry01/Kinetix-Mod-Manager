; Inno Setup Script for Kinetix Mod Manager
; Compatible with Single-File .NET 10 Publishing

[Setup]
AppId={{D37D0B2E-6D3F-4B1C-BD03-9A1D2E3F4B5C}
AppName=Kinetix Mod Manager
AppVersion=1.2.2
AppPublisher=Audi Venture Games
AppPublisherURL=https://www.nexusmods.com/stardewvalley/mods/32385
AppSupportURL=https://www.nexusmods.com/stardewvalley/mods/32385
AppUpdatesURL=https://www.nexusmods.com/stardewvalley/mods/32385
DefaultDirName={autopf}\KinetixModManager
DefaultGroupName=Kinetix Mod Manager
AllowNoIcons=yes
; Targets x64 exclusively
ArchitecturesAllowed=x64
ArchitecturesInstallIn64BitMode=x64
; Output configuration
OutputDir=Setup
OutputBaseFilename=KinetixModManager_Setup
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
Source: "bin\Release\net10.0-windows\win-x64\publish\KinetixModManager.exe"; DestDir: "{app}"; Flags: ignoreversion
; Native DLLs (Tolk, NVDA Controller, etc.)
Source: "lib\Tolk.dll"; DestDir: "{app}"; Flags: ignoreversion
Source: "lib\nvdaControllerClient.dll"; DestDir: "{app}"; Flags: ignoreversion
Source: "lib\nvdaControllerClient64.dll"; DestDir: "{app}"; Flags: ignoreversion
Source: "bin\Release\net10.0-windows\win-x64\publish\*.dll"; DestDir: "{app}"; Flags: ignoreversion
; Manual for help menu
Source: "bin\Release\net10.0-windows\win-x64\publish\MANUAL.md"; DestDir: "{app}"; Flags: ignoreversion
; Sound themes and initial folders - using skipifsourcedoesntexist to allow empty folders
Source: "bin\Release\net10.0-windows\win-x64\publish\sounds\*"; DestDir: "{app}\sounds"; Flags: ignoreversion recursesubdirs createallsubdirs skipifsourcedoesntexist
Source: "bin\Release\net10.0-windows\win-x64\publish\profiles\*"; DestDir: "{app}\profiles"; Flags: ignoreversion recursesubdirs createallsubdirs skipifsourcedoesntexist

[Dirs]
; Ensure these directories exist even if they are empty in the installer
Name: "{app}\sounds"
Name: "{app}\profiles"
Name: "{app}\downloads"
Name: "{app}\backups"

[Icons]
Name: "{group}\Kinetix Mod Manager"; Filename: "{app}\KinetixModManager.exe"
Name: "{autodesktop}\Kinetix Mod Manager"; Filename: "{app}\KinetixModManager.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\KinetixModManager.exe"; Description: "{cm:LaunchProgram,Kinetix Mod Manager}"; Flags: nowait postinstall skipifsilent

[Registry]
; Register nxm:// protocol for Nexus Mods downloads
Root: HKCU; Subkey: "Software\Classes\nxm"; ValueType: string; ValueName: ""; ValueData: "URL:Nexus Mod Manager Protocol"; Flags: uninsdeletekey
Root: HKCU; Subkey: "Software\Classes\nxm"; ValueType: string; ValueName: "URL Protocol"; ValueData: ""; Flags: uninsdeletekey
Root: HKCU; Subkey: "Software\Classes\nxm\DefaultIcon"; ValueType: string; ValueName: ""; ValueData: "{app}\KinetixModManager.exe,0"
Root: HKCU; Subkey: "Software\Classes\nxm\shell\open\command"; ValueType: string; ValueName: ""; ValueData: """{app}\KinetixModManager.exe"" ""%1"""

[Code]
function GetUninstallString(): String;
var
  sUninstPath: String;
  sUninstString: String;
begin
  sUninstPath := 'Software\Microsoft\Windows\CurrentVersion\Uninstall\{D37D0B2E-6D3F-4B1C-BD03-9A1D2E3F4B5C}_is1';
  sUninstString := '';
  if not RegQueryStringValue(HKLM, sUninstPath, 'UninstallString', sUninstString) then
    RegQueryStringValue(HKCU, sUninstPath, 'UninstallString', sUninstString);
  Result := sUninstString;
end;

function InitializeSetup(): Boolean;
var
  V: Integer;
  sUninstString: String;
  sUninstDir: String;
begin
  Result := True;
  sUninstString := GetUninstallString();
  if sUninstString <> '' then
  begin
    sUninstDir := RemoveQuotes(sUninstString);
    sUninstDir := ExtractFilePath(sUninstDir);
    
    // Check if the old path contains the old folder name
    if Pos('StardewAccessibleManager', sUninstDir) > 0 then
    begin
      if MsgBox('The installer has detected a previous installation of Stardew Valley Accessible Manager in a different folder. Would you like to automatically uninstall it before installing Kinetix Mod Manager?', mbInformation, MB_YESNO) = IDYES then
      begin
        // Run uninstaller silently
        Exec(RemoveQuotes(sUninstString), '/SILENT /NORESTART', '', SW_SHOW, ewWaitUntilTerminated, V);
      end;
    end;
  end;
end;

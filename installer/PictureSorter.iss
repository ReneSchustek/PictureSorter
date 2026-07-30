; Inno-Setup-Skript für PictureSorter. Erzeugt die Setup.exe aus dem
; self-contained Publish – die Anwendung bringt .NET und die Windows App Runtime
; selbst mit und läuft ohne Vorinstallation.
;
; Nicht von Hand aufrufen, sondern über:
;   tools\publish.ps1
;
; Die Zielnutzerin hat wenig PC-Erfahrung: Installation deshalb ohne Adminrechte
; („nur für mich"), mit Desktop-Verknüpfung als Standard.

#ifndef MyAppVersion
  #define MyAppVersion "1.3.1"
#endif
#ifndef PublishDir
  #define PublishDir "..\dist\publish"
#endif

#define MyAppName "PictureSorter"
#define MyAppPublisher "Rene Schustek"
#define MyAppURL "https://github.com/ReneSchustek/PictureSorter"
#define MyAppExeName "PictureSorter.exe"

[Setup]
; Stabile AppId – niemals ändern: Sie identifiziert die Installation für
; Aktualisierung und Deinstallation.
AppId={{B7F3A1C2-4D8E-4A19-9C61-5E2B7D0A8F34}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}
AppUpdatesURL={#MyAppURL}/releases
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
DisableDirPage=no
AllowUNCPath=no
; Ohne Adminrechte installierbar; wer will, kann im Dialog auf „für alle Benutzer"
; wechseln (dann fragt Windows nach).
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputBaseFilename=PictureSorter-Setup-v{#MyAppVersion}
SetupIconFile=..\src\PictureSorter.App\Assets\AppIcon.ico
UninstallDisplayIcon={app}\{#MyAppExeName}
UninstallDisplayName={#MyAppName} {#MyAppVersion}
WizardStyle=modern
Compression=lzma2/max
SolidCompression=yes
VersionInfoVersion={#MyAppVersion}
VersionInfoCompany={#MyAppPublisher}
VersionInfoProductName={#MyAppName}

[Languages]
Name: "german"; MessagesFile: "compiler:Languages\German.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: recursesubdirs createallsubdirs ignoreversion

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{group}\{cm:UninstallProgram,{#MyAppName}}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#MyAppName}}"; Flags: nowait postinstall skipifsilent

#ifndef SourceDir
  #error Supply /DSourceDir=full-path-to-portable-directory
#endif
#define AppName "ЛовиКадр"
#define AppVersion "0.1.0"
#define AppExe "LoviKadr.exe"

[Setup]
AppId={{D775F941-FA98-40AD-B96F-1417858DD72E}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher=iHoooK
DefaultDirName={localappdata}\Programs\LoviKadr
DefaultGroupName={#AppName}
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir=..\release
OutputBaseFilename=LoviKadr-Setup-x64
SetupIconFile=..\src\PromptixCapture\Assets\promptix-capture.ico
UninstallDisplayIcon={app}\{#AppExe}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
CloseApplications=yes
RestartApplications=no
LicenseFile=..\LICENSE
MinVersion=10.0.19041

[Languages]
Name: "russian"; MessagesFile: "compiler:Languages\Russian.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Создать ярлык на рабочем столе"; Flags: unchecked
Name: "autostart"; Description: "Запускать при входе в Windows"; Flags: unchecked

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\{#AppExe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; Tasks: desktopicon
Name: "{group}\Удалить {#AppName}"; Filename: "{uninstallexe}"

[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "LoviKadr"; ValueData: """{app}\{#AppExe}"" --startup"; Tasks: autostart; Flags: uninsdeletevalue

[Run]
Filename: "{app}\{#AppExe}"; Description: "Запустить ЛовиКадр"; Flags: nowait postinstall skipifsilent

; Do not delete Pictures, Videos, settings or logs during uninstall.

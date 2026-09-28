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
DefaultDirName={autopf}\LoviKadr
DefaultGroupName={#AppName}
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir=..\release
OutputBaseFilename=LoviKadr-Setup-x64
SetupIconFile=..\src\PromptixCapture\Assets\lovi-kadr.ico
UninstallDisplayIcon={app}\{#AppExe}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
CloseApplications=yes
RestartApplications=no
LicenseFile=..\LICENSE
MinVersion=10.0.19041

[Languages]
Name: "russian"; MessagesFile: "compiler:Languages\Russian.isl"; LicenseFile: "License.ru.txt"
Name: "english"; MessagesFile: "compiler:Default.isl"

[CustomMessages]
russian.DesktopIconTask=Создать значок на рабочем столе
english.DesktopIconTask=Create a desktop icon
russian.AutostartTask=Запускать ЛовиКадр при входе в Windows
english.AutostartTask=Start LoviKadr when signing in to Windows

[Tasks]
Name: "desktopicon"; Description: "{cm:DesktopIconTask}"; Flags: unchecked
Name: "autostart"; Description: "{cm:AutostartTask}"; Flags: unchecked

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\{#AppExe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; Tasks: desktopicon
Name: "{group}\Удалить {#AppName}"; Filename: "{uninstallexe}"

[Run]
Filename: "{app}\{#AppExe}"; Parameters: "--enable-autostart"; Tasks: autostart; Flags: runasoriginaluser runhidden
Filename: "{app}\{#AppExe}"; Description: "Запустить ЛовиКадр"; Flags: nowait postinstall skipifsilent

[UninstallRun]
Filename: "{app}\{#AppExe}"; Parameters: "--disable-autostart"; RunOnceId: "RemoveAutostart"; Flags: runhidden skipifdoesntexist

; Do not delete Pictures, Videos, settings or logs during uninstall.

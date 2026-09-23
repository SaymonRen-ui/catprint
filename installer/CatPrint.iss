; CatPrint — скрипт установщика для Inno Setup 6.
; Как собрать:
;   1. Установи Inno Setup (https://jrsoftware.org/isdl.php) — 1 минута.
;   2. Открой этот файл в Inno Setup и нажми Compile (Ctrl+F9).
;   3. Готовый CatPrint-Setup-1.0.0.exe появится в папке installer-output.
; Собирает self-contained версию: .NET на ПК пользователя НЕ нужен.

#define MyAppName "CatPrint"
#define MyAppVersion "1.0.0"
#define MyAppPublisher "CatPrint"
#define MyAppExeName "CatPrint.exe"

[Setup]
AppId={{8E2B4F1A-7C3D-4A5B-9D2F-1A2B3C4D5E6F}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
OutputDir=..\installer-output
OutputBaseFilename=CatPrint-Setup-{#MyAppVersion}
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible

[Languages]
Name: "russian"; MessagesFile: "compiler:Languages\Russian.isl"

[Tasks]
Name: "desktopicon"; Description: "Создать значок на рабочем столе"; GroupDescription: "Дополнительно:"; Flags: unchecked

[Files]
Source: "..\publish\standalone\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "Запустить {#MyAppName}"; Flags: nowait postinstall skipifsilent

; Siberia Connect — установщик для Windows (Inno Setup 6)
#define AppVer GetEnv("APP_VERSION")
#if AppVer == ""
  #define AppVer "1.0.0"
#endif

[Setup]
AppId={{6F1C2A4E-8B3D-4E7A-9C51-2D0B5E7F9A13}
AppName=Siberia Connect
AppVersion={#AppVer}
AppVerName=Siberia Connect {#AppVer}
AppPublisher=Siberia Connect
AppPublisherURL=https://t.me/SibirConnectBot
AppSupportURL=https://t.me/SibirHelpBot
DefaultDirName={localappdata}\Programs\Siberia Connect
DisableDirPage=yes
DisableProgramGroupPage=yes
DisableReadyPage=yes
PrivilegesRequired=lowest
OutputDir=..\out
OutputBaseFilename=SiberiaConnect-Setup-{#AppVer}
SetupIconFile=..\assets\app.ico
UninstallDisplayIcon={app}\SiberiaConnect.exe
UninstallDisplayName=Siberia Connect
WizardStyle=modern
WizardImageFile=w.bmp,w2.bmp
WizardSmallImageFile=s.bmp,s2.bmp
Compression=lzma2/max
SolidCompression=yes
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
CloseApplications=force
RestartApplications=no
VersionInfoVersion={#AppVer}
VersionInfoProductName=Siberia Connect

[Languages]
Name: "ru"; MessagesFile: "compiler:Languages\Russian.isl"

[Tasks]
Name: "desktopicon"; Description: "Ярлык на рабочем столе"

[Files]
Source: "..\publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "..\deps\MicrosoftEdgeWebview2Setup.exe"; DestDir: "{tmp}"; Flags: deleteafterinstall; Check: NeedsWebView2

[Icons]
Name: "{userprograms}\Siberia Connect"; Filename: "{app}\SiberiaConnect.exe"
Name: "{userdesktop}\Siberia Connect"; Filename: "{app}\SiberiaConnect.exe"; Tasks: desktopicon

[Registry]
Root: HKCU; Subkey: "Software\Classes\siberiaconnect"; ValueType: string; ValueName: ""; ValueData: "URL:Siberia Connect"; Flags: uninsdeletekey
Root: HKCU; Subkey: "Software\Classes\siberiaconnect"; ValueType: string; ValueName: "URL Protocol"; ValueData: ""
Root: HKCU; Subkey: "Software\Classes\siberiaconnect\DefaultIcon"; ValueType: string; ValueName: ""; ValueData: "{app}\SiberiaConnect.exe,0"
Root: HKCU; Subkey: "Software\Classes\siberiaconnect\shell\open\command"; ValueType: string; ValueName: ""; ValueData: """{app}\SiberiaConnect.exe"" ""%1"""

[Run]
Filename: "{tmp}\MicrosoftEdgeWebview2Setup.exe"; Parameters: "/silent /install"; StatusMsg: "Устанавливаю компонент Microsoft WebView2…"; Check: NeedsWebView2; Flags: waituntilterminated
Filename: "{app}\SiberiaConnect.exe"; Description: "Запустить Siberia Connect"; Flags: nowait postinstall skipifsilent
; обновление из приложения (тихая установка): сразу запускаем новую версию, при необходимости с подключением
Filename: "{app}\SiberiaConnect.exe"; Parameters: "{param:scargs|}"; Flags: nowait; Check: WizardSilent

[UninstallRun]
Filename: "{app}\SiberiaConnect.exe"; Parameters: "--cleanup"; Flags: runhidden waituntilterminated; RunOnceId: "ScCleanup"

[Code]
function HasWebView2(Root: Integer; Key: String): Boolean;
var V: String;
begin
  Result := RegQueryStringValue(Root, Key, 'pv', V) and (V <> '') and (V <> '0.0.0.0');
end;

function NeedsWebView2(): Boolean;
begin
  Result := not (HasWebView2(HKLM, 'SOFTWARE\WOW6432Node\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}')
    or HasWebView2(HKLM, 'SOFTWARE\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}')
    or HasWebView2(HKCU, 'Software\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}'));
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
var Code: Integer;
begin
  // Перед обновлением: остановить VPN и вернуть настройки прокси
  if FileExists(ExpandConstant('{app}\SiberiaConnect.exe')) then
    Exec(ExpandConstant('{app}\SiberiaConnect.exe'), '--cleanup --update', '', SW_HIDE, ewWaitUntilTerminated, Code);
  Result := '';
end;

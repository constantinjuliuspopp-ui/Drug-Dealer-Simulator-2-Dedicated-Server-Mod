; DDS2 Multiplayer Mod – Installer (Inno Setup 6)
; Wird von build.ps1 mit /DAppVersion=x.y.z aufgerufen, nachdem out\stage befüllt ist.

#ifndef AppVersion
  #define AppVersion "0.0.0"
#endif
; /DVariant=Server oder /DVariant=Client: gleiches Setup, nur andere Vorauswahl und anderer Dateiname.
#ifndef Variant
  #define Variant "Client"
#endif
#define StageDir "..\out\stage"
#define AppExe "DDS2Mod.exe"
#define ServerExe "DDS2Server.exe"

[Setup]
AppId={{131AE2D8-9C98-4DE9-A7F1-E2E0299CF0C0}
AppName=DDS2 Multiplayer Mod
AppVersion={#AppVersion}
AppPublisher=DDS2 Multiplayer Mod (inoffiziell, nicht von den Spieleentwicklern)
DefaultDirName={autopf}\DDS2 Multiplayer Mod
DefaultGroupName=DDS2 Multiplayer Mod
PrivilegesRequired=admin
OutputDir=..\dist
OutputBaseFilename=DDS2Mod-{#Variant}-Setup-{#AppVersion}
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
UninstallDisplayName=DDS2 Multiplayer Mod
UninstallDisplayIcon={app}\{#AppExe}
SetupLogging=yes

[Languages]
Name: "de"; MessagesFile: "compiler:Languages\German.isl"

[Types]
; Die erste Art ist vorausgewählt.
#if Variant == "Server"
Name: "server"; Description: "Dedicated Server – dieser PC ist der Server"
Name: "client"; Description: "Client-Mod – auf Servern mitspielen oder selbst hosten"
#else
Name: "client"; Description: "Client-Mod – auf Servern mitspielen oder selbst hosten"
Name: "server"; Description: "Dedicated Server – dieser PC ist der Server"
#endif
Name: "full"; Description: "Beides (Client-Mod und Dedicated Server)"
Name: "custom"; Description: "Benutzerdefiniert"; Flags: iscustom

[Components]
Name: "mod"; Description: "Mod im Spielordner (UE4SS + DDS2Net, wird von beiden Arten gebraucht)"; Types: client server full custom; Flags: fixed
Name: "client"; Description: "DDS2 Multiplayer (Launcher: Server beitreten, Favoriten, hosten)"; Types: client full
Name: "server"; Description: "Dedicated Server (DDS2Server.exe, Ordner Dokumente\DDS2 Server, SteamCMD-Skript)"; Types: server full

[Tasks]
Name: "desktopicon"; Description: "Desktop-Verknüpfungen anlegen"
Name: "firewall"; Description: "Windows-Firewall: eingehende UDP-Verbindungen zum Server erlauben (Spiel und Abfrage-Port)"; Components: server
Name: "autostart"; Description: "DDS2 Server bei der Windows-Anmeldung automatisch starten (startet auch Steam im Hintergrund)"; Components: server; Flags: unchecked

[Files]
Source: "{#StageDir}\app\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "{#StageDir}\game\*"; DestDir: "{code:GameWin64}"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "{#StageDir}\server\server.cfg"; DestDir: "{code:ServerDir}"; Components: server; Flags: onlyifdoesntexist uninsneveruninstall
Source: "{#StageDir}\server\install_server.bat"; DestDir: "{code:ServerDir}"; Components: server; Flags: ignoreversion

[Registry]
; Protokoll dds2mod:// – die Mod startet damit beim normalen Spielstart den unsichtbaren Helfer (DDS2Mod.exe, Agent-Modus)
Root: HKA; Subkey: "Software\Classes\dds2mod"; ValueType: string; ValueName: ""; ValueData: "URL:DDS2 Multiplayer Mod"; Flags: uninsdeletekey
Root: HKA; Subkey: "Software\Classes\dds2mod"; ValueType: string; ValueName: "URL Protocol"; ValueData: ""
Root: HKA; Subkey: "Software\Classes\dds2mod\shell\open\command"; ValueType: string; ValueName: ""; ValueData: """{app}\{#AppExe}"" ""%1"""

[Icons]
Name: "{group}\DDS2 Multiplayer"; Filename: "{app}\{#AppExe}"; Components: client
Name: "{group}\DDS2 Server starten"; Filename: "{app}\{#ServerExe}"; Parameters: "--config ""{code:ServerDir}\server.cfg"""; WorkingDir: "{code:ServerDir}"; Components: server
Name: "{group}\Server-Ordner öffnen"; Filename: "{code:ServerDir}"; Components: server
Name: "{group}\Spiel per SteamCMD aktualisieren"; Filename: "{code:ServerDir}\install_server.bat"; WorkingDir: "{code:ServerDir}"; Components: server
Name: "{group}\Anleitung"; Filename: "{app}\README.md"
Name: "{group}\DDS2 Multiplayer deinstallieren"; Filename: "{uninstallexe}"
Name: "{autodesktop}\DDS2 Multiplayer"; Filename: "{app}\{#AppExe}"; Tasks: desktopicon; Components: client
Name: "{autodesktop}\DDS2 Server"; Filename: "{app}\{#ServerExe}"; Parameters: "--config ""{code:ServerDir}\server.cfg"""; WorkingDir: "{code:ServerDir}"; Tasks: desktopicon; Components: server
; Autostart-Ordner des Benutzers: läuft in seiner Windows-Sitzung, wo auch Steam läuft
Name: "{userstartup}\DDS2 Server"; Filename: "{app}\{#ServerExe}"; Parameters: "--config ""{code:ServerDir}\server.cfg"""; WorkingDir: "{code:ServerDir}"; Tasks: autostart; Components: server

[Run]
; Firewall-Regeln erst löschen (Update), dann neu anlegen. Gelten nur für die beiden Programme, nicht für Ports allgemein.
Filename: "{sys}\netsh.exe"; Parameters: "advfirewall firewall delete rule name=""DDS2 Server - Spiel"""; Flags: runhidden; Tasks: firewall
Filename: "{sys}\netsh.exe"; Parameters: "advfirewall firewall delete rule name=""DDS2 Server - Abfrage"""; Flags: runhidden; Tasks: firewall
Filename: "{sys}\netsh.exe"; Parameters: "advfirewall firewall add rule name=""DDS2 Server - Spiel"" dir=in action=allow protocol=UDP program=""{code:ShippingExe}"""; Flags: runhidden; Tasks: firewall
Filename: "{sys}\netsh.exe"; Parameters: "advfirewall firewall add rule name=""DDS2 Server - Abfrage"" dir=in action=allow protocol=UDP program=""{app}\{#ServerExe}"""; Flags: runhidden; Tasks: firewall
Filename: "{app}\{#AppExe}"; Description: "DDS2 Multiplayer jetzt starten"; Flags: postinstall nowait skipifsilent runasoriginaluser; Components: client
Filename: "{code:ServerDir}"; Description: "Server-Ordner öffnen (server.cfg anpassen)"; Flags: postinstall shellexec nowait skipifsilent runasoriginaluser; Components: server

[UninstallRun]
; Benutzer-Configs des Spiels (Engine.ini/Game.ini) aus dem Backup wiederherstellen.
Filename: "{app}\{#AppExe}"; Parameters: "--restore"; Flags: runhidden waituntilterminated; RunOnceId: "RestoreIni"
Filename: "{sys}\netsh.exe"; Parameters: "advfirewall firewall delete rule name=""DDS2 Server - Spiel"""; Flags: runhidden; RunOnceId: "FwGame"
Filename: "{sys}\netsh.exe"; Parameters: "advfirewall firewall delete rule name=""DDS2 Server - Abfrage"""; Flags: runhidden; RunOnceId: "FwQuery"

[UninstallDelete]
Type: files; Name: "{app}\gamedir.txt"
Type: files; Name: "{code:ServerDir}\start_server.bat"
Type: filesandordirs; Name: "{code:ServerDir}\ipc"

[Code]
var
  GameDirPage: TInputDirWizardPage;
  GameDirFound: Boolean;
  UninstGameDir: String;

// Liefert den Index-ten Wert in Anführungszeichen einer VDF-Zeile ("key" "value" -> 2 = value).
function QuotedToken(const Line: String; Index: Integer): String;
var
  I, Count, StartPos: Integer;
  InQuote: Boolean;
begin
  Result := '';
  Count := 0;
  StartPos := 0;
  InQuote := False;
  for I := 1 to Length(Line) do
    if Line[I] = '"' then
    begin
      if not InQuote then
      begin
        InQuote := True;
        StartPos := I + 1;
      end else
      begin
        InQuote := False;
        Count := Count + 1;
        if Count = Index then
        begin
          Result := Copy(Line, StartPos, I - StartPos);
          Exit;
        end;
      end;
    end;
end;

procedure AddLibrary(Libs: TStringList; Path: String);
begin
  StringChangeEx(Path, '/', '\', True);
  StringChangeEx(Path, '\\', '\', True);
  if (Path <> '') and (Libs.IndexOf(Path) < 0) then
    Libs.Add(Path);
end;

function ReadInstallDir(const Manifest: String): String;
var
  Lines: TArrayOfString;
  I: Integer;
begin
  Result := 'Drug Dealer Simulator 2';
  if LoadStringsFromFile(Manifest, Lines) then
    for I := 0 to GetArrayLength(Lines) - 1 do
      if Pos('"installdir"', Lines[I]) > 0 then
      begin
        Result := QuotedToken(Lines[I], 2);
        Exit;
      end;
end;

function IsGameDir(const Dir: String): Boolean;
begin
  Result := (Dir <> '') and FileExists(AddBackslash(Dir) + 'DrugDealerSimulator2.exe');
end;

// Läuft Drug Dealer Simulator 2? Gezielt nach dem Prozess fragen – ein "UnrealWindow" haben auch andere
// Unreal-Programme wie der Epic Games Launcher.
function IsGameRunning(): Boolean;
var
  ResultCode: Integer;
begin
  Result := Exec(ExpandConstant('{cmd}'),
    '/C tasklist /FI "IMAGENAME eq DrugDealerSimulator2-Win64-Shipping.exe" /NH | find /I "DrugDealerSimulator2" >nul',
    '', SW_HIDE, ewWaitUntilTerminated, ResultCode) and (ResultCode = 0);
end;

// Eintrag "Steam App 1708850" unter Programme und Features
function UninstallLocation(): String;
var
  Key: String;
begin
  Result := '';
  Key := 'SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\Steam App 1708850';
  if not RegQueryStringValue(HKLM64, Key, 'InstallLocation', Result) then
    if not RegQueryStringValue(HKLM32, Key, 'InstallLocation', Result) then
      Result := '';
  if not IsGameDir(Result) then Result := '';
end;

// Letzter Ausweg: übliche Steam-Ordner auf allen Laufwerken
function ScanDrives(): String;
var
  D, K: Integer;
  Drive, Dir: String;
  Candidates: TArrayOfString;
begin
  Result := '';
  SetArrayLength(Candidates, 9);
  Candidates[0] := 'SteamLibrary';
  Candidates[1] := 'Steam';
  Candidates[2] := 'Program Files (x86)\Steam';
  Candidates[3] := 'Program Files\Steam';
  Candidates[4] := 'Games\Steam';
  Candidates[5] := 'Games\SteamLibrary';
  Candidates[6] := 'Spiele\Steam';
  Candidates[7] := 'Spiele\SteamLibrary';
  Candidates[8] := 'Steam\SteamLibrary';
  for D := Ord('C') to Ord('Z') do
  begin
    Drive := Chr(D) + ':\';
    if DirExists(Drive) then
      for K := 0 to GetArrayLength(Candidates) - 1 do
      begin
        Dir := Drive + Candidates[K] + '\steamapps\common\Drug Dealer Simulator 2';
        if IsGameDir(Dir) then
        begin
          Result := Dir;
          Exit;
        end;
      end;
  end;
end;

// Sucht das Spiel über Steam-Registry und libraryfolders.vdf.
function FindGameDir(): String;
var
  Libs: TStringList;
  Root, Manifest, Dir: String;
  Lines: TArrayOfString;
  I, J, RootCount: Integer;
begin
  Result := '';
  Libs := TStringList.Create;
  try
    if RegQueryStringValue(HKCU, 'Software\Valve\Steam', 'SteamPath', Root) then AddLibrary(Libs, Root);
    if RegQueryStringValue(HKLM32, 'SOFTWARE\Valve\Steam', 'InstallPath', Root) then AddLibrary(Libs, Root);
    if RegQueryStringValue(HKLM64, 'SOFTWARE\Valve\Steam', 'InstallPath', Root) then AddLibrary(Libs, Root);
    RootCount := Libs.Count;
    for I := 0 to RootCount - 1 do
      if LoadStringsFromFile(AddBackslash(Libs[I]) + 'steamapps\libraryfolders.vdf', Lines) then
        for J := 0 to GetArrayLength(Lines) - 1 do
          if Pos('"path"', Lines[J]) > 0 then
            AddLibrary(Libs, QuotedToken(Lines[J], 2));
    for I := 0 to Libs.Count - 1 do
    begin
      Manifest := AddBackslash(Libs[I]) + 'steamapps\appmanifest_1708850.acf';
      if FileExists(Manifest) then
      begin
        Dir := AddBackslash(Libs[I]) + 'steamapps\common\' + ReadInstallDir(Manifest);
        if IsGameDir(Dir) then
        begin
          Result := Dir;
          Exit;
        end;
      end;
    end;
  finally
    Libs.Free;
  end;
  if Result = '' then Result := UninstallLocation();
  if Result = '' then Result := ScanDrives();
end;

procedure InitializeWizard;
var
  Found: String;
begin
  GameDirPage := CreateInputDirPage(wpSelectComponents, 'Spielordner',
    'Wo ist Drug Dealer Simulator 2 installiert?',
    'Der Ordner wurde automatisch über Steam gesucht. Falls er nicht stimmt, wähle den Ordner, in dem DrugDealerSimulator2.exe liegt.'
    + ' Für einen Server, dessen Spiel per SteamCMD installiert wurde (install_server.bat), dessen Ordner wählen, z. B. C:\DDS2Server\game.',
    False, '');
  GameDirPage.Add('Spielordner:');
  // Reihenfolge: Ordner der letzten Installation (wenn es ihn noch gibt), Steam, per SteamCMD installiertes Spiel
  Found := GetPreviousData('GameDir', '');
  if not IsGameDir(Found) then Found := FindGameDir();
  if (Found = '') and IsGameDir(ExpandConstant('{sd}\DDS2Server\game')) then
    Found := ExpandConstant('{sd}\DDS2Server\game');
  GameDirPage.Values[0] := Found;
  GameDirFound := Found <> '';
end;

// Spielordner automatisch gefunden: Seite überspringen (der Ordner steht dann in der Zusammenfassung).
// Bei "Dedicated Server" bleibt sie sichtbar, weil das Spiel dort oft per SteamCMD woanders liegt.
function ShouldSkipPage(PageID: Integer): Boolean;
begin
  Result := (PageID = GameDirPage.ID) and GameDirFound and not WizardIsComponentSelected('server');
end;

function UpdateReadyMemo(Space, NewLine, MemoUserInfoInfo, MemoDirInfo, MemoTypeInfo, MemoComponentsInfo,
  MemoGroupInfo, MemoTasksInfo: String): String;
begin
  Result := MemoDirInfo + NewLine + NewLine
    + 'Spielordner (automatisch gefunden):' + NewLine + Space + GameDirPage.Values[0] + NewLine + NewLine
    + MemoTypeInfo + NewLine + NewLine + MemoComponentsInfo;
  if MemoGroupInfo <> '' then Result := Result + NewLine + NewLine + MemoGroupInfo;
  if MemoTasksInfo <> '' then Result := Result + NewLine + NewLine + MemoTasksInfo;
end;

procedure RegisterPreviousData(PreviousDataKey: Integer);
begin
  SetPreviousData(PreviousDataKey, 'GameDir', GameDirPage.Values[0]);
end;

function NextButtonClick(CurPageID: Integer): Boolean;
begin
  Result := True;
  if CurPageID = GameDirPage.ID then
  begin
    if not IsGameDir(GameDirPage.Values[0]) then
    begin
      MsgBox('In diesem Ordner liegt keine DrugDealerSimulator2.exe. Bitte den Spielordner auswählen.', mbError, MB_OK);
      Result := False;
    end;
  end;
  // Erst direkt vor dem Installieren prüfen (die Ordner-Seite wird oft übersprungen)
  if (CurPageID = wpReady) and IsGameRunning() then
  begin
    MsgBox('Drug Dealer Simulator 2 läuft gerade. Bitte das Spiel beenden und dann erneut auf "Installieren" klicken.', mbError, MB_OK);
    Result := False;
  end;
end;

function GameWin64(Param: String): String;
begin
  Result := AddBackslash(GameDirPage.Values[0]) + 'DrugDealerSimulator2\Binaries\Win64';
end;

function ShippingExe(Param: String): String;
begin
  Result := GameWin64('') + '\DrugDealerSimulator2-Win64-Shipping.exe';
end;

function ServerDir(Param: String): String;
begin
  Result := ExpandConstant('{userdocs}\DDS2 Server');
end;

// Vorhandene fremde Dateien (z. B. eine andere UE4SS-Installation) umbenennen statt überschreiben.
procedure BackupItem(const Path: String);
begin
  if (FileExists(Path) or DirExists(Path))
     and not FileExists(Path + '.dds2mod-backup') and not DirExists(Path + '.dds2mod-backup') then
    if not RenameFile(Path, Path + '.dds2mod-backup') then
      RaiseException('Backup fehlgeschlagen: ' + Path);
end;

procedure WriteServerScript();
var
  Content: AnsiString;
  Script: String;
begin
  if LoadStringFromFile(ExpandConstant('{app}\start_server.bat.template'), Content) then
  begin
    Script := String(Content);
    StringChangeEx(Script, '@DDS2SERVER_EXE@', ExpandConstant('{app}\{#ServerExe}'), True);
    SaveStringToFile(ServerDir('') + '\start_server.bat', AnsiString(Script), False);
  end;
end;

procedure CurStepChanged(CurStep: TSetupStep);
var
  W: String;
begin
  if CurStep = ssInstall then
  begin
    W := GameWin64('');
    // Bei einem Update ist die vorhandene UE4SS-Installation unsere eigene: nicht sichern.
    if not FileExists(W + '\ue4ss\Mods\DDS2Net\Scripts\main.lua') then
    begin
      BackupItem(W + '\dwmapi.dll');
      BackupItem(W + '\ue4ss');
      if FileExists(W + '\UE4SS.dll') then
      begin
        BackupItem(W + '\UE4SS.dll');
        BackupItem(W + '\UE4SS-settings.ini');
        BackupItem(W + '\Mods');
      end;
    end;
  end;
  if CurStep = ssPostInstall then
  begin
    SaveStringToFile(ExpandConstant('{app}\gamedir.txt'), GameDirPage.Values[0], False);
    if WizardIsComponentSelected('server') then
      WriteServerScript();
  end;
end;

function InitializeUninstall(): Boolean;
var
  S: AnsiString;
begin
  Result := True;
  if IsGameRunning() then
  begin
    MsgBox('Bitte zuerst Drug Dealer Simulator 2 beenden.', mbError, MB_OK);
    Result := False;
    Exit;
  end;
  if LoadStringFromFile(ExpandConstant('{app}\gamedir.txt'), S) then
    UninstGameDir := Trim(String(S));
end;

procedure RestoreItem(const Path: String);
begin
  if FileExists(Path + '.dds2mod-backup') or DirExists(Path + '.dds2mod-backup') then
  begin
    if DirExists(Path) then
      DelTree(Path, True, True, True)
    else if FileExists(Path) then
      DeleteFile(Path);
    RenameFile(Path + '.dds2mod-backup', Path);
  end;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  W: String;
begin
  if (CurUninstallStep = usPostUninstall) and (UninstGameDir <> '') then
  begin
    W := AddBackslash(UninstGameDir) + 'DrugDealerSimulator2\Binaries\Win64';
    // UE4SS-Laufzeitdateien (Logs, Cache, Dumps) stehen nicht im Installationsprotokoll.
    if DirExists(W + '\ue4ss') then
      DelTree(W + '\ue4ss', True, True, True);
    RestoreItem(W + '\dwmapi.dll');
    RestoreItem(W + '\ue4ss');
    RestoreItem(W + '\UE4SS.dll');
    RestoreItem(W + '\UE4SS-settings.ini');
    RestoreItem(W + '\Mods');
  end;
end;

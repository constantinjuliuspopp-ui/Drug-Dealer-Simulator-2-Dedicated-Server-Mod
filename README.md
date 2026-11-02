# DDS2 Multiplayer Mod

Direkt-IP-Multiplayer und Dedicated Server für **Drug Dealer Simulator 2** (Steam, Windows).
Inoffizielle Fan-Mod, nicht von den Entwicklern des Spiels.

> **Version 0.3 – ungetestet im echten Spiel.** Gebaut gegen Spiel-Build `23009320` (Unreal Engine 5.3).
> Bitte Rückmeldung geben, was funktioniert und was nicht.

## Was die Mod kann

| Funktion | Stand 0.3 |
|---|---|
| Server per IP/Hostname + Port beitreten | ✅ im Programm **DDS2 Multiplayer** oder im Spielmenü |
| Favoriten mit Name, IP, Port, **Ping und Spielerzahl** | ✅ `%APPDATA%\DDS2Mod\favorites.json` |
| Prüfung vor dem Verbinden: Zeitüberschreitung, Versionskonflikt, Server voll, falsches Passwort | ✅ über den Abfrage-Port des Servers |
| Menüpunkt „Server beitreten“ im Hauptmenü des Spiels | ✅ vorläufiges Design, oben rechts bzw. Taste **F8** – Originaldesign folgt in 0.4 |
| **Dedicated Server** `DDS2Server.exe` mit Konsole, ohne Spielfenster | ✅ Spiel läuft ohne Grafik als Host (zu testen) |
| Server-Befehle `status`, `players`, `kick`, `save`, `stop` | ✅ |
| Welt automatisch laden, Autosave, Log mit Zeitstempel, Neustart nach Absturz | ✅ (zu testen) |
| Spiel auf dem Server per **SteamCMD** installieren/aktualisieren | ✅ `install_server.bat` (mit Steam-Anmeldung) |
| Ein Installer mit Auswahl „Client-Mod“ / „Dedicated Server“, Backups, Deinstallation | ✅ |
| Relay für Spieler ohne Portfreigabe (auch Linux) | geplant (0.5) |

**Grenzen, die das Spiel vorgibt:** Es gibt keine offizielle Server-Version des Spiels. Der Dedicated Server startet
deshalb das normale Spiel ohne Fenster. Das Spiel hat einen Steam-Kopierschutz: Auf dem Server-PC muss der
**Steam-Client laufen und mit einem Konto angemeldet sein, dem das Spiel gehört**. Ohne Steam-Anmeldung geht es nicht.
Der Server ist im Spiel selbst ein Spieler (Host) und belegt einen Platz.

## Installation

1. Spiel mindestens **einmal über Steam starten** (bis ins Hauptmenü) und beenden.
2. `DDS2Mod-Setup-<Version>.exe` ausführen und die Art wählen:
   - **Client-Mod** – du willst auf Servern mitspielen oder selbst hosten.
   - **Dedicated Server** – dieser PC soll der Server sein.
   - **Beides** – z. B. zum Testen.
3. Der Installer findet das Spiel automatisch über Steam. Falls nicht: Ordner auswählen, in dem
   `DrugDealerSimulator2.exe` liegt.

Der Installer legt vor jeder Änderung Backups an:
- vorhandene `dwmapi.dll`/UE4SS-Dateien im Spielordner → `*.dds2mod-backup`
- Spiel-Configs `Engine.ini`/`Game.ini` → `*.dds2mod-backup` (bei jedem Start über DDS2 Multiplayer / DDS2 Server)
- Spielstände → `%LOCALAPPDATA%\DrugDealerSimulator2\Saved\DDS2Mod-Backups\` (die letzten 10)

**Deinstallation** über Windows-Einstellungen → Apps. Dabei werden UE4SS und die Mod entfernt,
gesicherte Dateien zurückgelegt, die Configs und Firewall-Regeln zurückgesetzt. Spielstände bleiben erhalten.

## Mitspielen

1. **DDS2 Multiplayer** starten (Startmenü oder Desktop).
2. IP oder Hostname und Port des Servers eintragen (Standard `7777`), ggf. Passwort.
3. **Verbinden**. Vorher fragt DDS2 Multiplayer den Server ab und meldet sofort:
   - **Zeitüberschreitung** – der Server antwortet nicht. Du kannst trotzdem verbinden, z. B. bei einem Spieler, der
     selbst hostet; der hat keinen Abfrage-Port.
   - **Versionskonflikt** – andere Mod-Version oder anderes Spiel-Update.
   - **Server voll**
   - **Passwort fehlt / falsches Passwort**
   - **Server lädt noch**
4. Mit **Als Favorit speichern** merkst du dir den Server. Die Liste zeigt **Ping**, **Spieler** (Mitspieler / Maximum)
   und den Status (online, online mit Passwort „PW“, offline, startet, andere Mod-Version) und aktualisiert sich alle
   30 Sekunden.

**Oder direkt im Spiel:** In DDS2 Multiplayer auf **Mit Mod starten** klicken. Im Hauptmenü des Spiels steht
oben rechts **„Server beitreten (Direkt-IP)“**. Anklicken oder **F8** drücken, Adresse eintippen
(z. B. `192.168.178.20` oder `server.example.org:7777`) und **Enter** drücken. Deine Favoriten stehen darunter.
Das Passwort kommt aus dem Feld **Passwort** in DDS2 Multiplayer, weil es beim Spielstart festgelegt wird.

**Normal starten (Steam-Koop)** startet das Spiel ohne Mod-Einstellungen – dann funktionieren
Steam-Einladungen wie gewohnt.

**Selbst hosten und mitspielen:** **Mit Mod starten**, im Spiel den Spielstand laden und ein Koop-Spiel starten.
Mitspieler verbinden sich mit deiner IP auf Port 7777.

## Dedicated Server

### Was du brauchst

- Einen Windows-PC (10/11, 64 Bit), der laufen bleibt. Etwa 4–8 GB freier Arbeitsspeicher.
- Ein **Steam-Konto mit dem Spiel** für diesen PC. Willst du gleichzeitig selbst auf einem anderen PC spielen, brauchst du
  ein **zweites Konto mit eigener Kopie**: Steam lässt ein Konto nur auf einem PC gleichzeitig spielen.
- Steam-Client auf dem Server-PC, gestartet und angemeldet (Kopierschutz des Spiels).

### Spiel installieren – Weg 1: Steam-Client (einfach)

Spiel ganz normal über Steam installieren. Steam hält es automatisch aktuell.

### Spiel installieren – Weg 2: SteamCMD

SteamCMD ist Valves Kommandozeilen-Werkzeug zum Installieren und Aktualisieren. Für dieses Spiel braucht es eine
Anmeldung mit dem Konto, dem das Spiel gehört (anonym geht nicht, weil es keine eigene Server-App gibt).

1. `install_server.bat` ausführen (liegt neben dem Setup bzw. nach der Installation in `Dokumente\DDS2 Server`).
2. Es lädt SteamCMD von Valve nach `C:\DDS2Server\steamcmd`, fragt nach dem Steam-Benutzernamen und startet SteamCMD.
   **Passwort und Steam-Guard-Code gibst du in SteamCMD selbst ein** – das Skript sieht und speichert sie nicht.
3. Das Spiel landet in `C:\DDS2Server\game` (anderer Ordner: `install_server.bat "D:\Spiele\DDS2"`).
4. Danach `DDS2Mod-Setup` ausführen, **Dedicated Server** wählen und als Spielordner `C:\DDS2Server\game` angeben.
   Der Installer schlägt ihn automatisch vor.

**Updates:** Server mit `stop` beenden, `install_server.bat` erneut ausführen
(Startmenü → DDS2 Multiplayer Mod → „Spiel per SteamCMD aktualisieren“). Mod-Dateien bleiben dabei erhalten.
Ist dasselbe Konto gerade im Steam-Client angemeldet, kann Steam es dort abmelden – dann wieder anmelden.

### Welt vorbereiten

Auf dem Server-PC einmal das Spiel starten und eine Welt anlegen („Neues Kartell“, Koop) – oder eine vorhandene Welt
kopieren: Ordner `%LOCALAPPDATA%\DrugDealerSimulator2\Saved\SaveGames\Cartels\<Name>` vom eigenen PC in denselben Pfad
auf dem Server-PC. Ab dann liegen **Welt und Fortschritt auf dem Server**.

### Konfigurieren: `Dokumente\DDS2 Server\server.cfg`

| Einstellung | Bedeutung |
|---|---|
| `ServerName` | Name in der Favoritenliste der Mitspieler |
| `Port` | Spielport, UDP (Standard 7777) |
| `QueryPort` | Abfrage-Port, UDP; 0 = Spielport + 1 (7778) |
| `MaxPlayers` | Mitspieler **ohne** den Server (Standard 2; das Spiel ist für 3 Spieler ausgelegt, mehr ist experimentell) |
| `Password` | leer = kein Passwort |
| `World` | Welt (Ordnername unter `SaveGames\Cartels` oder Name im Spiel); leer = zuletzt gespielte |
| `AutosaveMinutes` | Autosave-Intervall, 0 = aus |
| `Headless` | `true` = ohne Fenster (Standard). Klappt das nicht, startet der Server das Spiel einmal mit kleinem Fenster |
| `AutoRestart` | Spiel nach Absturz neu starten (höchstens 3× in 10 Minuten) |
| `GameDir` | Spielordner, falls nicht automatisch gefunden |

### Starten

**DDS2 Server** (Desktop/Startmenü) oder `start_server.bat` im Server-Ordner. Parameter überschreiben die cfg:

```
DDS2Server.exe --port 7777 --maxplayers 2 --world "Welt1"
start_server.bat --port 7777 --maxplayers 2 --world "Welt1" --password geheim
```

Weitere Parameter: `--config <Datei>`, `--queryport <Port>`, `--headless`, `--windowed`, `--help`.

Ablauf im Log: Steam-Prüfung → Spielstände → Spiel gestartet (ohne Fenster) → Hauptmenü → Welt wird als Koop geladen →
Lobby → „Welt läuft – Spieler können sich verbinden“.

### Konsolenbefehle

| Befehl | Wirkung |
|---|---|
| `status` | Zustand, Welt, Mitspieler, Ports, Laufzeit |
| `players` | Mitspieler mit Ping |
| `kick <Name>` | Spieler entfernen |
| `save` | Speichern wie im Pausemenü; das Log meldet, wenn die Datei geschrieben wurde |
| `stop` | speichern und beenden (auch Strg+C oder Fenster schließen) |
| `load <Name>` / `start` | Welt laden (im Hauptmenü) / Spiel aus der Lobby starten – nur nötig, wenn der Automatismus hängt |

Logs: `Dokumente\DDS2 Server\logs` (die letzten 30). Spielstand-Backups bei jedem Start (siehe Installation).

## Port-Freigabe (Port-Forwarding)

| Port | Protokoll | Wofür | Pflicht? |
|---|---|---|---|
| 7777 | **UDP** | Spiel | ja |
| 7778 | **UDP** | Abfrage-Port (Ping, Spielerzahl, Version, Passwortprüfung) | empfohlen – ohne ihn erscheint der Server als „offline“, Verbinden geht trotzdem |

**TCP muss nicht freigegeben werden**, Unreal und die Abfrage nutzen nur UDP.

1. Lokale IP des Server-PCs herausfinden: `ipconfig` → „IPv4-Adresse“ (z. B. `192.168.178.20`).
   Im Router als **feste IP** für diesen PC eintragen.
2. Im Router zwei Portfreigaben anlegen: **UDP 7777** und **UDP 7778 → 192.168.178.20**.
   (Fritz!Box: Internet → Freigaben → Gerät für Freigaben hinzufügen → Neue Freigabe → Protokoll UDP.)
3. Windows-Firewall: Der Installer legt bei „Dedicated Server“ auf Wunsch die Regeln an
   (`DDS2 Server - Spiel`, `DDS2 Server - Abfrage`). Sonst beim ersten Start **Zugriff zulassen** für
   `DrugDealerSimulator2-Win64-Shipping.exe` und `DDS2Server.exe`.
4. Mitspieler verbinden sich mit der **öffentlichen IP** (z. B. „Wie ist meine IP“ im Browser) oder einem DynDNS-Namen.

Hast du **DS-Lite / CGNAT** (häufig bei Kabel- und Glasfaseranschlüssen), funktioniert eine IPv4-Portfreigabe nicht.
Dann hilft ein VPN wie ZeroTier/Tailscale/Radmin VPN, ein Server mit eigener öffentlicher IP oder später das Relay (0.5).

## Fehlerbehebung

| Meldung / Problem | Lösung |
|---|---|
| „Zeitüberschreitung“ | Läuft der Server und zeigt „Welt läuft“? IP/Port prüfen, UDP 7777 und 7778 im Router freigeben, Firewall. |
| „Versionskonflikt“ | Beide brauchen dieselbe Mod-Version (Installer) und dasselbe Spiel-Update (Steam bzw. `install_server.bat`). |
| „Der Server ist voll“ | `MaxPlayers` in der server.cfg erhöhen (über 2 experimentell) oder warten. |
| „Falsches Passwort“ / „passwortgeschützt“ | Passwort in DDS2 Multiplayer eintragen; Groß-/Kleinschreibung beachten. |
| „Der Server lädt die Welt noch“ | In einer Minute erneut versuchen. Im Server-Log steht, wo er gerade ist. |
| Server: „Steam läuft nicht“ | Steam-Client starten und mit dem Konto anmelden, dem das Spiel gehört. |
| Server: „Start ohne Fenster hat nicht geklappt“ | Läuft dann mit kleinem Fenster weiter. Dauerhaft: `Headless=false`. Log schicken. |
| Server: „Steam hat das Spiel selbst neu gestartet“ | Spiel beenden, prüfen dass Steam läuft, Server neu starten. Kein zweites Spiel auf demselben PC. |
| Server: „keine neue Spielstand-Datei“ nach `save` | Das Spiel lässt nur in Abständen speichern. Später erneut versuchen. |
| Server lädt die Welt nicht | Das Log zeigt beim Start „Spielstände: …“. Einen davon bei `World=` eintragen. |
| `install_server.bat`: Spiel nicht installiert | Meldung von SteamCMD lesen: Passwort, Steam Guard, besitzt das Konto das Spiel? |
| „Mod ist nicht installiert“ | Installer erneut ausführen (nach Spiel-Updates kann Steam Dateien zurücksetzen). |
| Spiel stürzt beim Start ab | Deinstallieren → läuft das Spiel ohne Mod? Log: `…\Binaries\Win64\ue4ss\UE4SS.log` |
| Kein „Server beitreten“ im Hauptmenü | Spiel über DDS2 Multiplayer starten. In `ue4ss\UE4SS.log` nach `[DDS2Net]` suchen. |
| Steam-Koop geht nicht mehr | In DDS2 Multiplayer **Normal starten (Steam-Koop)** benutzen. |

Logs für Fehlerberichte:
- Server: `Dokumente\DDS2 Server\logs\`
- UE4SS: `<Spielordner>\DrugDealerSimulator2\Binaries\Win64\ue4ss\UE4SS.log`
- Spiel: `%LOCALAPPDATA%\DrugDealerSimulator2\Saved\Logs\`

## Für Entwickler

| Ordner | Inhalt |
|---|---|
| `src/shared/` | gemeinsamer C#-Code (Konfiguration, Abfrage-Protokoll `ServerQuery`, `JoinCheck`, IPC), Lua-Mod `DDS2Net` |
| `src/client/` | Launcher **DDS2 Multiplayer** (`src/DDS2Mod.csproj`) |
| `src/server/` | **DDS2Server.exe** (`DDS2Server.csproj`), `start_server.bat`, `install_server.bat` |
| `installer/` | Inno-Setup-Skript, UE4SS-Modliste |
| `tools/` | Proof-of-Concept-Skripte, `iostore-names` (liest Namen aus den Spiel-Paketen, nur lesen) |
| `docs/` | Analysen, Testanleitungen |

Abfrage-Protokoll (UDP, Text): Anfrage `DDS2Q|1|<nonce>` auf 128 Byte aufgefüllt, Antwort `DDS2R|1|<nonce>` plus Zeilen
`name= mod= protocol= build= state= players= max= password= world= challenge=`. Passwortprüfung per
HMAC-SHA256 über eine Einmal-Challenge, das Passwort selbst wird nie übertragen. Details in `src/shared/ServerQuery.cs`.

Bauen (benötigt .NET 8 SDK, Inno Setup 6, UE4SS in `third_party\ue4ss-experimental`):

```
.\build.ps1
```

## Rechtliches

- Enthält **keine** Original-Spieldateien. UE4SS steht unter MIT-Lizenz (`licenses\UE4SS-LICENSE.txt`).
  `install_server.bat` lädt nur SteamCMD von Valve; das Spiel kommt über das eigene Steam-Konto.
- Weder DRM noch Anti-Cheat noch die Steam-Besitzprüfung werden umgangen: Jeder Spieler und jeder
  Server braucht Steam und eine eigene Kopie des Spiels.

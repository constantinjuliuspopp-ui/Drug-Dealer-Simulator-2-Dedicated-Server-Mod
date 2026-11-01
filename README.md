# DDS2 Multiplayer Mod

Direkt-IP-Multiplayer und Server-Modus für **Drug Dealer Simulator 2** (Steam, Windows).
Inoffizielle Fan-Mod, nicht von den Entwicklern des Spiels.

> **Version 0.2 – ungetestet im echten Spiel.** Gebaut gegen Spiel-Build `23009320` (Unreal Engine 5.3).
> Bitte Rückmeldung geben, was funktioniert und was nicht.

## Was die Mod kann

| Funktion | Stand 0.2 |
|---|---|
| Server per IP/Hostname + Port beitreten | ✅ über das Programm **DDS2 Multiplayer** oder im Spielmenü |
| Menüpunkt „Server beitreten“ im Hauptmenü des Spiels | ✅ oben rechts bzw. Taste **F8** (zu testen) |
| Favoritenliste (Name, IP, Port) | ✅ `%APPDATA%\DDS2Mod\favorites.json` |
| Verständliche Fehlermeldungen (Zeitüberschreitung, Version, Server voll) | ✅ |
| Versionscheck Client ↔ Server | ✅ über die Netzwerk-Version der Engine (zu testen) |
| Server-Passwort | ✅ ebenfalls über die Netzwerk-Version (zu testen) |
| Server-Modus mit Konsole (`status`, `kick`, `save`, `start`, `load`, `stop`), Log mit Zeitstempel | ✅ `save` = „Speichern“ im Pausemenü (zu testen) |
| Welt automatisch laden und Spiel aus der Lobby starten | ✅ (zu testen) – notfalls im Spielfenster von Hand |
| Echter Headless-Server ohne Grafik | ❌ technisch nicht möglich (siehe unten) |

## Installation

1. Spiel mindestens **einmal über Steam starten** (bis ins Hauptmenü) und beenden.
2. `DDS2Mod-Setup-<Version>.exe` ausführen.
3. Der Installer findet das Spiel automatisch über Steam. Falls nicht: Ordner auswählen, in dem
   `DrugDealerSimulator2.exe` liegt.
4. Optional **„Server-Modus“** auswählen, wenn dieser PC Server sein soll.

Der Installer legt vor jeder Änderung Backups an:
- vorhandene `dwmapi.dll`/UE4SS-Dateien im Spielordner → `*.dds2mod-backup`
- Spiel-Configs `Engine.ini`/`Game.ini` → `*.dds2mod-backup` (beim Start über DDS2 Multiplayer)
- Spielstände → `%LOCALAPPDATA%\DrugDealerSimulator2\Saved\DDS2Mod-Backups\` (die letzten 10)

**Deinstallation** über Windows-Einstellungen → Apps. Dabei werden UE4SS und die Mod entfernt,
gesicherte Dateien zurückgelegt und die Configs wiederhergestellt. Spielstände bleiben erhalten.

## Mitspielen

1. **DDS2 Multiplayer** starten (Startmenü oder Desktop).
2. IP oder Hostname und Port des Servers eintragen (Standard `7777`), ggf. Passwort.
3. **Verbinden** – das Spiel startet und verbindet sich direkt.
4. Mit **Als Favorit speichern** merkst du dir den Server.

**Oder direkt im Spiel:** In DDS2 Multiplayer auf **Mit Mod starten** klicken. Im Hauptmenü des Spiels steht
oben rechts **„Server beitreten (Direkt-IP)“**. Anklicken oder **F8** drücken, Adresse eintippen
(z. B. `192.168.178.20` oder `server.example.org:7777`) und **Enter** drücken. Deine Favoriten aus
DDS2 Multiplayer stehen darunter und lassen sich anklicken. Das Passwort kommt aus dem Feld **Passwort**
in DDS2 Multiplayer, weil es beim Spielstart festgelegt wird.

**Normal starten (Steam-Koop)** startet das Spiel ohne Mod-Einstellungen – dann funktionieren
Steam-Einladungen wie gewohnt. Solange das Spiel über DDS2 Multiplayer läuft, gehen Steam-Einladungen nicht.

## Server hosten

Es gibt zwei Wege:

**A) Selbst hosten und mitspielen:** In DDS2 Multiplayer auf **Mit Mod starten** klicken,
im Spiel den Spielstand laden und wie gewohnt ein Koop-Spiel starten.

**B) Server-Modus („Pseudo-Dedicated“):**
1. Im Installer „Server-Modus“ wählen. Der Ordner `Dokumente\DDS2 Server` enthält `server.cfg` und `start_server.bat`.
2. `server.cfg` anpassen:

   | Einstellung | Bedeutung |
   |---|---|
   | `ServerName` | Anzeigename im Log |
   | `Port` | UDP-Port (Standard 7777) |
   | `MaxPlayers` | Spielerlimit inkl. Host (Spiel ist für 3 ausgelegt) |
   | `Password` | leer = kein Passwort |
   | `World` | Name des Spielstands, wie er im Menü „Fortsetzen“ steht |
   | `AutosaveMinutes` | Autosave-Intervall, 0 = aus |
   | `GameDir` | optional: Spielordner, falls nicht automatisch gefunden |

3. **DDS2 Server** (Desktop) oder `start_server.bat` starten. Parameter überschreiben die cfg:
   ```
   start_server.bat --port 7777 --maxplayers 4 --world "MeineWelt"
   ```
4. Sobald das Hauptmenü erscheint, lädt der Server die Welt `World` automatisch als Koop-Spiel und startet sie
   aus der Lobby. Klappt das nicht, im Spielfenster von Hand laden oder `load <Name>` bzw. `start` eingeben.
   Danach kann das Fenster minimiert werden.
5. Konsolenbefehle:

   | Befehl | Wirkung |
   |---|---|
   | `status` | Zustand (Hauptmenü / Lobby / Welt) und Spieler mit Ping |
   | `kick <Name>` | Spieler entfernen |
   | `save` | Speichern wie im Pausemenü; das Log meldet, wenn die Datei geschrieben wurde |
   | `start` | Spiel aus der Lobby starten |
   | `load <Name>` | anderen Spielstand laden (im Hauptmenü) |
   | `stop` | speichern und beenden |

   Logs liegen in `Dokumente\DDS2 Server\logs`. Weitere Einstellungen, z. B. den automatischen Lobby-Start,
   findest du in `<Spielordner>\DrugDealerSimulator2\Binaries\Win64\ue4ss\Mods\DDS2Net\config.txt`.

**Wichtig zum Server-Modus:**
- Das Spiel gibt es nur als normalen Client. Ein Server ohne Grafik ist deshalb nicht möglich. Die Server-Instanz ist ein
  ganz normales Spiel, das hostet. Der Host belegt einen Spielerplatz.
- Die Server-Instanz braucht Steam und ein Konto, dem das Spiel gehört. Am einfachsten auf einem PC,
  auf dem du selbst nicht gleichzeitig mit demselben Konto auf einem anderen Rechner spielst.
- Welt und Fortschritt liegen im Spielstand des Server-PCs.

## Port-Freigabe (Port-Forwarding)

Unreal nutzt **nur UDP**. TCP muss nicht freigegeben werden.

1. Lokale IP des Server-PCs herausfinden: `ipconfig` → „IPv4-Adresse“ (z. B. `192.168.178.20`).
   Am besten im Router als **feste IP** für diesen PC eintragen.
2. Im Router eine Portfreigabe anlegen: **UDP 7777 → 192.168.178.20:7777**.
   (Fritz!Box: Internet → Freigaben → Gerät für Freigaben hinzufügen → Neue Freigabe → Protokoll UDP.)
3. Windows-Firewall: Beim ersten Hosten fragt Windows nach – **Zugriff zulassen**.
   Falls die Abfrage weggeklickt wurde: Windows-Sicherheit → Firewall → „App durch Firewall zulassen“ →
   `DrugDealerSimulator2-Win64-Shipping.exe` erlauben.
4. Mitspieler verbinden sich mit deiner **öffentlichen IP** (z. B. über „Wie ist meine IP“ im Browser) oder
   einem DynDNS-Namen.

Hast du **DS-Lite / CGNAT** (häufig bei Kabel- und Glasfaseranschlüssen), funktioniert eine IPv4-Portfreigabe nicht.
Dann hilft nur ein VPN wie ZeroTier/Tailscale/Radmin VPN. Darüber verbindet ihr euch mit der VPN-IP.

## Fehlerbehebung

| Meldung / Problem | Lösung |
|---|---|
| „Zeitüberschreitung“ | Läuft der Server und hostet er schon? IP/Port prüfen, UDP-Port im Router freigeben, Firewall. |
| „Falsche Version oder falsches Passwort“ | Beide brauchen dieselbe Mod-Version (Installer) und dasselbe Spiel-Update. Passwort prüfen. |
| „Server hat abgelehnt (vermutlich voll)“ | Spielerlimit erreicht (`MaxPlayers`, Host zählt mit). |
| „Mod ist nicht installiert“ | Installer erneut ausführen (nach Spiel-Updates kann Steam Dateien zurücksetzen). |
| Spiel stürzt beim Start ab | Installer deinstallieren → prüfen, ob das Spiel ohne Mod läuft. Log: `…\Binaries\Win64\ue4ss\UE4SS.log` |
| Steam-Koop geht nicht mehr | In DDS2 Multiplayer **Normal starten (Steam-Koop)** benutzen. |
| Server: „keine neue Spielstand-Datei“ nach `save` | Das Spiel lässt nur in Abständen speichern. Später erneut versuchen oder im Spielfenster speichern. |
| Server lädt die Welt nicht | `World` muss so heißen wie im Menü „Fortsetzen“. Das Server-Log zeigt die Liste „Spielstände im Menü“. |
| Kein „Server beitreten“ im Hauptmenü | Spiel über DDS2 Multiplayer starten. In `ue4ss\UE4SS.log` nach `[DDS2Net]` suchen. |

Logs für Fehlerberichte:
- Spiel: `%LOCALAPPDATA%\DrugDealerSimulator2\Saved\Logs\`
- UE4SS: `<Spielordner>\DrugDealerSimulator2\Binaries\Win64\ue4ss\UE4SS.log`
- Server: `Dokumente\DDS2 Server\logs\`

## Für Entwickler

| Ordner | Inhalt |
|---|---|
| `src/shared/` | Mod-Version, Formate, gemeinsamer C#-Code, Lua-Mod `DDS2Net` (läuft in Client und Server) |
| `src/client/` | Client-Oberfläche |
| `src/server/` | Server-Konsole, `start_server.bat` |
| `installer/` | Inno-Setup-Skript, UE4SS-Modliste |
| `tools/poc/` | Proof-of-Concept-Skripte |
| `tools/iostore-names/` | liest Namen aus den Spiel-Paketen (nur lesen); braucht `oo2core.dll` aus einer Unreal-Engine-Installation |
| `docs/` | Analyse, Testanleitungen |

Bauen (benötigt .NET 8 SDK, Inno Setup 6, UE4SS in `third_party\ue4ss-experimental`):

```
.\build.ps1
```

## Rechtliches

- Enthält **keine** Original-Spieldateien. UE4SS steht unter MIT-Lizenz (`licenses\UE4SS-LICENSE.txt`).
- Weder DRM noch Anti-Cheat noch die Steam-Besitzprüfung werden umgangen: Jeder Spieler und jede
  Server-Instanz braucht Steam und eine eigene Kopie des Spiels.

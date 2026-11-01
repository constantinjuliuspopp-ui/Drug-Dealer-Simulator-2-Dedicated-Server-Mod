# Testplan Version 0.1

Ersetzt die PoC-Skripte aus `tools/poc` – der Installer erledigt jetzt alles.
Bei jedem Test notieren: **was du gemacht hast, was passiert ist, Fehlermeldung (Text)**.

## T1 – Installation

1. Spiel einmal über Steam bis ins Hauptmenü starten und beenden.
2. `dist\DDS2Mod-Setup-0.1.0.exe` ausführen, Typ **„Client und Server-Modus“** wählen.
3. Prüfen: Wurde der Spielordner automatisch erkannt?
4. Danach existieren `…\Binaries\Win64\dwmapi.dll` und `…\Binaries\Win64\ue4ss\`.

## T2 – Spiel startet mit Mod

1. **DDS2 Multiplayer** starten → **Selbst hosten (Direkt-IP)**.
2. Startet das Spiel normal? (Absturz = sofort melden, Datei `ue4ss\UE4SS.log` mitschicken)
3. In `ue4ss\UE4SS.log` nach `[DDS2Net]` suchen – steht dort `Version 0.1.0`?

## T3 – Objekt-Dump (wichtig für die nächste Version)

1. Im Spiel einen Spielstand laden.
2. **Strg+J** drücken (erzeugt `ue4ss\UE4SS_ObjectDump.txt`, kann kurz ruckeln).
3. Im Spiel einmal **manuell speichern**.
4. Datei `UE4SS_ObjectDump.txt` aufbewahren – daraus ermittle ich Welt-Laden und Speichern für den Server.

## T4 – Hosten über IP

1. Spielstand als Koop-Host starten (wie gewohnt). Firewall-Abfrage: **zulassen**.
2. DDS2 Multiplayer zeigt jetzt „Du hostest auf Port 7777/UDP – 1 Spieler“?
3. Zusätzlich in PowerShell: `Get-NetUDPEndpoint -LocalPort 7777` → eine Zeile erwartet.

## T5 – Beitreten

- **Gleicher PC:** in DDS2 Multiplayer IP `127.0.0.1` → **Verbinden**, bei der Rückfrage „weitere Instanz“ **Ja**.
- **Zweiter PC im Heimnetz:** dort ebenfalls installieren, LAN-IP des Hosts eintragen (`ipconfig`).

Erwartet: zweites Spiel lädt in die Welt des Hosts, Status „Verbunden mit …“.
Prüfen: Sehen sich beide? Namen korrekt? Tür öffnen, Gegenstand aufheben, Handel?

## T6 – Fehlerfälle

| Test | Erwartete Meldung |
|---|---|
| Verbinden mit IP, auf der kein Server läuft | „Zeitüberschreitung …“ |
| Host mit Passwort `abc`, Client mit `xyz` | „falsche Version oder falsches Passwort“ |
| Server `MaxPlayers=1`, dann Client verbinden | „Server hat abgelehnt (vermutlich voll)“ |
| Hostname `gibtsnicht.invalid` | „Hostname … wurde nicht gefunden“ |

## T7 – Server-Modus

1. `Dokumente\DDS2 Server\server.cfg` prüfen, dann **DDS2 Server** (Desktop) starten.
2. Konsole erscheint mit Zeitstempeln, Spielfenster öffnet sich (1280×720).
3. Spielstand laden und hosten → Konsole meldet „Welt läuft als Host“.
4. Client verbinden → Konsole meldet „Spieler verbunden: …“.
5. Befehle testen: `status`, `kick <Name>`, `save` (meldet in 0.1 „nicht konfiguriert“), `stop`.

## T8 – Zurück zu Steam-Koop und Deinstallation

1. Im Programm **Normal starten (Steam-Koop)** → Steam-Einladung an einen Freund funktioniert?
2. Deinstallieren (Windows-Einstellungen → Apps) → Spiel startet danach normal ohne Mod,
   `dwmapi.dll` und `ue4ss\` sind weg.

## Was du mir schickst

- Ergebnisse der Tests (kurz)
- `…\Binaries\Win64\ue4ss\UE4SS.log`
- `UE4SS_ObjectDump.txt` aus T3 (groß – gerne gezippt)
- Spiel-Log: `%LOCALAPPDATA%\DrugDealerSimulator2\Saved\Logs\DrugDealerSimulator2.log`
- Bei Server-Tests: `Dokumente\DDS2 Server\logs\`

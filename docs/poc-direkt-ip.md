# Proof of Concept: Direkt-IP testen

Ziel: herausfinden, ob DDS2 Koop über eine normale IP-Verbindung statt über Steam-P2P
funktioniert. Dafür wird **keine** Spieldatei verändert, nur die Benutzer-`Engine.ini` in
`%LOCALAPPDATA%\DrugDealerSimulator2\Saved\Config\Windows\` (mit Backup).

Alle Befehle in PowerShell im Ordner `tools\poc` ausführen. Falls PowerShell Skripte
blockiert, so starten:

```
powershell -ExecutionPolicy Bypass -File .\Enable-DirectIP.ps1
```

## Test 0 – Vorbereitung

1. Spiel über Steam starten, bis ins Hauptmenü gehen, beenden.
2. Prüfen, ob der Ordner `%LOCALAPPDATA%\DrugDealerSimulator2\Saved` jetzt existiert.
   **Falls nicht:** sag mir, wo das Spiel seine Spielstände/Configs ablegt.

## Test 1 – Host lauscht auf IP

1. `.\Enable-DirectIP.ps1`
2. Spiel normal starten und **wie gewohnt ein Koop-Spiel hosten** (Spielstand laden/erstellen).
3. Kommt eine Windows-Firewall-Abfrage: für **private Netzwerke** zulassen.
4. Im Spiel bleiben und in einer zweiten PowerShell prüfen:
   ```
   Get-NetUDPEndpoint -LocalPort 7777
   ```
   **Erwartet:** eine Zeile mit `LocalPort 7777`. Keine Ausgabe = Host lauscht nicht auf IP.

## Test 2 – Zweite Instanz auf demselben PC

Host aus Test 1 läuft weiter.

1. `.\Join-DirectIP.ps1` (verbindet mit `127.0.0.1:7777`)
2. **Erwartet:** zweites Spielfenster lädt direkt in die Welt des Hosts.
3. Mögliche Ergebnisse, die mich interessieren:
   - zweite Instanz startet gar nicht (Steam „läuft bereits“) → dann Test 3 mit zweitem PC
   - hängt im Ladebildschirm / landet im Hauptmenü / Fehlermeldung (Text abschreiben)

## Test 3 – Zweiter PC im selben Netzwerk (falls verfügbar)

1. Auf dem Host-PC die LAN-IP ermitteln: `ipconfig` → „IPv4-Adresse“, z. B. `192.168.178.20`.
2. Auf dem zweiten PC (eigenes Steam-Konto mit dem Spiel): Repo-Ordner kopieren,
   `.\Enable-DirectIP.ps1`, dann `.\Join-DirectIP.ps1 -Address 192.168.178.20`

## Wenn verbunden – bitte darauf achten

- Sehen sich beide Spieler gegenseitig, stimmen die Namen?
- Funktionieren Interaktionen (Tür öffnen, Gegenstand aufheben, Handel)?
- Nach Trennen und Neu-Verbinden: ist der Fortschritt des Clients noch da?

## Test 4 – Zurücksetzen

1. `.\Disable-DirectIP.ps1`
2. Koop über Steam-Einladung sollte wieder normal funktionieren.

## Ergebnis an mich

Nach jedem Test (auch wenn es klappt):

```
.\Get-DDS2Diag.ps1
```

Das legt `DDS2-Diagnose.txt` auf deinen Desktop. Schick mir den Inhalt und kurz,
was du gesehen hast. Steht dort „KEIN Log gefunden“, schreibt das Spiel keine Logs –
dann reichen mir deine Beobachtungen.

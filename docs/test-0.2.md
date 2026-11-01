# Testplan Version 0.2

Neu in 0.2: Menü „Server beitreten“ im Spiel, Welt-Autoload und Lobby-Start im Server-Modus,
`save` über die Speicherfunktion des Spiels. Die Tests T1, T2, T4, T6 und T8 aus
[test-0.1.md](test-0.1.md) gelten weiter. Bei jedem Test notieren: **was du gemacht hast, was passiert ist,
genauer Text von Meldungen**.

Vorher: 0.1 deinstallieren ist nicht nötig, `DDS2Mod-Setup-0.2.0.exe` einfach drüber installieren.

## Woher die Funktionsnamen stammen

Ermittelt aus den Blueprints des Spiels (Build 23009320) mit `tools/iostore-names`, nur gelesen, nichts verändert:

| Zweck | Aufruf |
|---|---|
| Spielstand als Koop starten | `BP_MainMenuPC_C:RequestLaunchSavedGame(CartelAlias, Coop, Advertise, BackupFile)` |
| Liste der Spielstände | `OneCartelWidget_C` (Felder `Alias`, `txtName`) im Hauptmenü |
| Spiel aus der Lobby starten | `BP_LobbyPC_C:RequestStartGame()` |
| Speichern | `BP_DDS2_PlayerController_C:RequestSaveCartelProgress()` (Pausemenü-Knopf „Speichern“) |

Die genaue Reihenfolge und die Typen der Parameter liest die Mod zur Laufzeit aus. Klappt ein Aufruf nicht,
steht im Log eine Zeile mit `[Parameter: …]`. Diese Zeile brauche ich.

## T9 – Menü „Server beitreten“ im Spiel

1. **DDS2 Multiplayer** → **Mit Mod starten**.
2. Im Hauptmenü des Spiels: Steht oben rechts **„Server beitreten (Direkt-IP) [F8]“**? (Screenshot)
3. Anklicken oder F8 → klappt das Feld mit Adresse, „Verbinden“ und Favoriten auf?
4. Lässt sich in das Adressfeld tippen? Sind die Texte lesbar?
5. Mit laufendem Host (zweite Instanz oder zweiter PC) `127.0.0.1` bzw. die LAN-IP eintippen → **Enter**.
   Erwartet: „Verbinde mit …“, das Spiel lädt in die Welt des Hosts, DDS2 Multiplayer zeigt „Verbunden“.
6. Eine nicht erreichbare IP probieren → Fehlermeldung im Menü und in DDS2 Multiplayer?
7. Spiel einmal **über Steam** starten (ohne DDS2 Multiplayer): Das Menü zeigt den Hinweis,
   dass Direkt-IP aus ist. Steam-Koop muss normal funktionieren.

Falls nichts erscheint: in `ue4ss\UE4SS.log` nach `Menü` suchen und die Zeilen schicken.

## T10 – Server lädt die Welt selbst

1. In `Dokumente\DDS2 Server\server.cfg` bei `World=` den Namen deines Spielstands eintragen, so wie er
   im Spiel unter „Fortsetzen“ steht.
2. **DDS2 Server** starten. Erwartete Log-Zeilen, nacheinander:
   - `Hauptmenü erreicht – suche Spielstand '…'`
   - `Spielstände im Menü: Name [alias], …`
   - `Lade Spielstand '…' als Koop-Host ...`
   - `Lobby läuft als Host …` → nach ca. 8 s `Spielstart angefordert`
   - `Welt läuft als Host …`
3. Bleibt es bei einem Schritt hängen: die letzten Log-Zeilen notieren und im Spielfenster nachsehen, was dort steht.
   Dann von Hand weitermachen. Zum Testen der Befehle: `load <Name>` im Hauptmenü, `start` in der Lobby.
4. Musste der Host in der Lobby erst einen Charakter wählen? Bitte notieren.

## T11 – Speichern über die Server-Konsole

1. Welt läuft, `save` eingeben.
2. Erwartet: `Speichern angestoßen` und kurz danach `Spielstand geschrieben (Dateiname)`.
3. Sofort noch einmal `save`: Kommt eine Warnung „keine neue Spielstand-Datei“? Das Spiel hat einen Mindestabstand.
4. `stop`: speichert, beendet nach ca. 10 s das Spiel, die Konsole meldet „Server gestoppt“.
5. Server neu starten: Ist der Fortschritt von vorher da?

## T12 – Diagnose-Befehle (nur wenn etwas nicht klappt)

In der Server-Konsole:

```
dump BP_MainMenuPC_C
dump OneCartelWidget_C
dump BP_LobbyPC_C
dump BP_DDS2_PlayerController_C
```

Jeder Befehl schreibt `Dokumente\DDS2 Server\ipc\dump-<Klasse>.txt`. Er funktioniert nur, wenn die Klasse gerade
existiert: die ersten beiden im Hauptmenü, `BP_LobbyPC_C` in der Lobby, den letzten in der Welt. Die Dateien schicken.

## Was du mir schickst

- Ergebnisse T9–T12 (kurz), Screenshot vom Hauptmenü mit dem neuen Menü
- `Dokumente\DDS2 Server\logs\` (neueste Datei)
- `…\Binaries\Win64\ue4ss\UE4SS.log`
- ggf. die `dump-*.txt`-Dateien

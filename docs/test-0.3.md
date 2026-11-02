# Testplan Version 0.3 – Dedicated Server

Neu in 0.3: `DDS2Server.exe` (Spiel ohne Fenster als Host), Abfrage-Port mit Ping/Spielerzahl/Passwortprüfung,
Fehlerprüfung vor dem Verbinden, `players`, Neustart nach Absturz, SteamCMD-Skript, ein Installer mit Auswahl.
T9–T12 aus [test-0.2.md](test-0.2.md) gelten weiter.

**Aufbau:** zwei PCs mit je eigenem Steam-Konto, das das Spiel besitzt. **Server-PC** und **Spieler-PC**.
Bei jedem Test notieren: was du gemacht hast, was passiert ist, genauer Text von Meldungen.

## T13 – Installer

1. Auf beiden PCs die alte Version **nicht** deinstallieren, `DDS2Mod-Setup-0.3.0.exe` drüber installieren.
2. Server-PC: Art **Dedicated Server**, Häkchen bei der Firewall-Frage lassen.
   Erwartet: Desktop-Verknüpfung **DDS2 Server**, in `Dokumente\DDS2 Server` liegen `server.cfg`, `start_server.bat`,
   `install_server.bat`. Am Ende öffnet sich der Server-Ordner.
3. Spieler-PC: Art **Client-Mod**. Erwartet: Verknüpfung **DDS2 Multiplayer**, kein Server-Eintrag.
4. Bestehende `server.cfg` aus 0.2 wird nicht überschrieben – neue Einstellungen (`Headless`, `QueryPort`, `AutoRestart`)
   gelten dann mit Standardwerten. Zum Vergleich steht die neue Vorlage in der README.

## T14 – Server startet ohne Fenster (wichtigster Test)

1. Server-PC: Steam starten und anmelden. **DDS2 Server** starten.
2. Erwartete Log-Zeilen:
   - `Abfrage-Port 7778/UDP bereit`
   - `Spiel gestartet (ohne Fenster, PID …)`
   - `Spiel im Hauptmenü.` → `Lade Spielstand …` → `Lobby läuft als Host …` → `Welt läuft – Spieler können sich … verbinden.`
3. Erscheint ein Spielfenster? Läuft im Task-Manager `DrugDealerSimulator2-Win64-Shipping.exe`? Wie viel RAM/CPU?
4. Falls stattdessen `Der Start ohne Fenster hat nicht geklappt. Neuer Versuch mit kleinem Fenster.` kommt:
   Läuft es danach mit Fenster bis „Welt läuft“? Bitte das Log und `…\ue4ss\UE4SS.log` schicken.
5. Wie lange dauert es vom Start bis „Welt läuft“?
6. `status` eingeben. Erwartet: `Zustand: Welt läuft (beitretbar) | Welt: … | Mitspieler: 0/2 | Ports: …`.

## T15 – Favoriten mit Ping und Spielerzahl

1. Spieler-PC: **DDS2 Multiplayer** öffnen, LAN-IP des Server-PCs eintragen (Port 7777), **Als Favorit speichern**.
2. Erwartet in der Liste: Ping in ms, Spieler `0/2`, Status `online`.
3. Server stoppen (`stop`) → nach **Aktualisieren**: `offline`. Server starten → während des Ladens `startet`, dann `online`.

## T16 – Fehlermeldungen vor dem Verbinden

1. **Passwort:** In `server.cfg` `Password=test` setzen, Server neu starten.
   - Ohne Passwort verbinden → „Dieser Server ist passwortgeschützt …“
   - Mit `falsch` → „Falsches Passwort.“
   - Mit `test` → Spiel startet und verbindet.
2. **Zeitüberschreitung:** eine IP ohne Server eintragen (z. B. `192.168.178.254`) → Rückfrage „antwortet nicht … Trotzdem
   verbinden?“ → **Nein** → Meldung „Zeitüberschreitung …“.
3. **Voll** (optional): `MaxPlayers=1`, zweiter Mitspieler versucht beizutreten → „Der Server ist voll (1/1 Spieler).“

## T17 – Spielen, Befehle, Speichern

1. Spieler-PC verbindet sich (Doppelklick auf den Favoriten). Server-Log: `Spieler verbunden: <Name>`.
2. `players` → dein Name mit Ping. Der Server selbst steht **nicht** in der Liste.
3. Etwas im Spiel ändern (z. B. Geld ausgeben), dann `save` → `Spielstand geschrieben (…)`.
4. `kick <Name>` → Spieler fliegt raus, Meldung beim Spieler?
5. `stop` → `speichere und beende ...` → nach ca. 10–20 s `Spiel beendet` und `Server gestoppt.`
6. Server neu starten, wieder verbinden: Ist die Änderung aus Schritt 3 da?
7. Gleicher Ablauf, aber statt `stop` das Konsolenfenster mit **X** schließen: Beendet sich das Spiel nach ca. 15 s selbst
   (Task-Manager)? Ist der Stand gespeichert?

## T18 – Neustart nach Absturz

1. Welt läuft. Im Task-Manager `DrugDealerSimulator2-Win64-Shipping.exe` beenden.
2. Erwartet: `Spiel beendet (Exit-Code …)` → `Unerwartetes Ende – Neustart in 10 Sekunden` → Spiel startet neu, lädt die Welt.

## T19 – SteamCMD (optional, nur wenn du diesen Weg nutzen willst)

1. `install_server.bat` ausführen, Benutzername eingeben, in SteamCMD Passwort und Steam-Guard-Code eingeben.
2. Erwartet: Spiel in `C:\DDS2Server\game`, am Ende „Fertig“.
3. Setup erneut ausführen, Dedicated Server, Spielordner `C:\DDS2Server\game` (sollte vorgeschlagen werden).
4. Steam-Client anmelden, **DDS2 Server** starten. **Wichtig:** Startet das Spiel aus diesem Ordner, oder meldet der
   Server „Steam hat das Spiel selbst neu gestartet“ bzw. öffnet Steam einen Installationsdialog?
   Das kann ich ohne dich nicht prüfen.

## Was du mir schickst

- Ergebnisse T13–T18 (kurz), bei T14 unbedingt das Server-Log
- `Dokumente\DDS2 Server\logs\` (neueste Datei), `…\Binaries\Win64\ue4ss\UE4SS.log` vom Server-PC
- bei Problemen in T15/T16: Screenshot von DDS2 Multiplayer

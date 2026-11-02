# Testplan Version 0.4 – Menü „Server beitreten“ im Design des Spiels

Neu in 0.4: Der Menüpunkt sitzt jetzt in der Knopfliste des Hauptmenüs. Fenster, Knöpfe, Eingabefelder, Schriften und
Klänge werden von den echten Widgets des Spiels übernommen. Fehler erscheinen als Popup des Spiels. Favoriten zeigen
Ping und Spielerzahl, und vor dem Verbinden prüft das Menü den Server.
Die Tests aus [test-0.3.md](test-0.3.md) gelten weiter, besonders T14 (Server ohne Fenster).

**Aufbau:** Server-PC mit laufendem **DDS2 Server** (0.4), Spieler-PC mit **DDS2 Multiplayer** (0.4).
Auf dem Spieler-PC: **Mit Mod starten** klicken und DDS2 Multiplayer offen lassen.

Bitte bei jedem Test einen **Screenshot** machen (F12 in Steam oder Win+Shift+S), dazu notieren, was passiert ist.

## T20 – Menüpunkt im Hauptmenü

1. Steht **SERVER BEITRETEN** direkt unter „Kartell beitreten“ (oder wie der Knopf auf Deutsch heißt)?
2. Sieht er genauso aus wie die anderen Menüpunkte (Schrift, Größe, Farbe)? Ändert er sich beim Drüberfahren wie
   die anderen, und kommt derselbe Klang?
3. Funktionieren alle anderen Menüpunkte (Weiter, Neues Kartell, Einstellungen, Beenden) noch normal?
4. Falls der Punkt stattdessen **oben rechts** steht oder etwas komisch aussieht: in
   `…\Binaries\Win64\ue4ss\UE4SS.log` alle Zeilen mit `Menü` heraussuchen und schicken.

## T21 – Fenster

1. Klick auf **SERVER BEITRETEN**. Erwartet: Hintergrund wird unscharf/dunkel, in der Mitte erscheint ein Fenster mit
   Überschrift, Adressfeld, Passwortfeld, **VERBINDEN**, **ABBRECHEN**, Favoritenliste.
2. Sehen Rahmen, Eingabefelder und Knöpfe aus wie im Fenster „Neues Kartell“ bzw. in der Serversuche des Spiels?
3. Kann man in beide Felder tippen? Zeigt das Passwortfeld Punkte?
4. **ABBRECHEN**, **Esc** und ein Klick neben das Fenster schließen es jeweils. **F8** öffnet/schließt.

## T22 – Favoriten mit Ping und Spielerzahl

1. Vorher in DDS2 Multiplayer den Server als Favorit speichern.
2. Im Fenster: Steht der Favorit da mit Ping (z. B. `12 ms`), Spielern (`0/2`) und Status `ONLINE`?
3. Server stoppen, Fenster schließen und wieder öffnen → nach ein paar Sekunden `OFFLINE`.
4. Klick auf den Favoriten → „Prüfe …“ → „Verbinde mit …“ → Spiel lädt in die Welt des Servers.

## T23 – Fehler als Popup des Spiels

1. **Zeitüberschreitung:** Adresse `192.168.178.254` (ohne Server) → nach ca. 3 s Popup „SERVER ANTWORTET NICHT“.
   Erneut **VERBINDEN** → versucht es trotzdem (dauert, dann Verbindungsfehler).
2. **Passwort:** Server mit `Password=test`. Ohne Passwort → Popup „PASSWORT … passwortgeschützt“.
   Mit `falsch` → „Falsches Passwort.“
3. **Neues Passwort:** Mit `test` → Meldung „Das Passwort wird übernommen …“ → das Spiel beendet sich, startet neu und
   verbindet automatisch. Klappt das?
4. **Voll** (optional): `MaxPlayers=1` am Server, zweiter Spieler versucht beizutreten → Popup „… voll …“.
5. Wie sehen die Popups aus? Steht Überschrift und Text drin, gibt es einen OK-Knopf? Falls leer oder falsch:
   Screenshot und `UE4SS.log` (Zeile `Spiel-Popup DisplayConfirmWidget(...)`) schicken. Abhilfe bis dahin:
   `GamePopups=false` in `…\ue4ss\Mods\DDS2Net\config.txt`.

## T24 – ohne DDS2 Multiplayer

1. DDS2 Multiplayer schließen, während das Spiel läuft. Im Spielmenü verbinden → nach 8 s verbindet es ohne Prüfung.
2. Spiel normal über Steam starten → Menüpunkt da, Verbinden zeigt „Direkt-IP ist nur aktiv, wenn …“.

## Was du mir schickst

- Screenshots von Hauptmenü, Fenster und einem Popup
- Ergebnisse T20–T24 (kurz)
- `…\Binaries\Win64\ue4ss\UE4SS.log` vom Spieler-PC

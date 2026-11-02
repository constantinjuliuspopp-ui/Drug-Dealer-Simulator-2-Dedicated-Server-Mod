# Phase 1 (neu): Dedicated Server und Original-Menü – Analyse und Empfehlung

Stand 02.11.2026, Spiel-Build `23009320`. Ergänzt [phase1-analyse.md](phase1-analyse.md) um die neuen Ziele:
Server ohne gespieltes Spiel, SteamCMD, Menü im Originaldesign, Favoriten mit Ping/Spielerzahl.

## 1. Befunde

| Frage | Befund | Woher |
|---|---|---|
| Engine | Unreal Engine 5.3 (eigener Build „UnrealEngine_5_3_DDS2“), Inhalte in IoStore, unverschlüsselt | Phase 1 |
| Mod-Weg | UE4SS + Lua-Mod; ist in 0.2.1 eingebaut und lädt im Spiel (Video vom 01.11.) | Video |
| Koop-Netcode | Unreal-Replikation, Listen-Server, Steam-Sitzungen (OnlineSubsystemSteam + AdvancedSessions), max. 3 Spieler, Spielstand beim Host | Phase 1 |
| Offizieller Dedicated Server / SteamCMD-App-ID | **Keiner gefunden.** Die App 1708850 hat ein einziges Inhalts-Depot (`1708851`). Keine Server-App in Shop, Suche oder lokalem Steam-Cache. SteamDB blockt den direkten Abruf, daher nicht 100 % bestätigt. | appmanifest, Websuche |
| Server-Build im Spiel? | **Nein.** Nur ein „Game“-Target. `-server` startet bei solchen Builds keinen Dedicated Server. | Phase 1 |
| Kopierschutz | **SteamStub-DRM** (`.bind`-Sektion in der Exe) + `SteamAPI_Init`/`RestartAppIfNecessary`. Die Exe startet nur mit **laufendem, angemeldetem Steam-Client** und einem Konto, dem das Spiel gehört. | PE-Sektionen der Exe |
| Spielstände | `%LOCALAPPDATA%\DrugDealerSimulator2\Saved\SaveGames\Cartels\<Welt>\`, Speichern über Rama Save System; Steam-Cloud ist aktiv | Dateisystem |
| UI-Bausteine für Original-Design | Vorhanden und nutzbar: Popup `/Game/GUI/FrontScreens/ConfirmWidget` mit Texteingabe (wird schon für Session-Passwörter benutzt), Server-Listeneintrag `OneSessionListWidget` (Hostname, Spielerzahl, Verbinden-Knopf), Schriften BigShoulders/Inconsolata, Klänge `MouseHover`, `ConfirmButton`, `Click`, Button-Material `MainButtonMat` | Paket-Index |

## 2. Was die Regeln bedeuten

- **„Ohne Steam-Anmeldung“ geht für einen Server mit Spiellogik nicht.** SteamCMD lädt ein Bezahlspiel nur mit dem
  Login eines Kontos, dem das Spiel gehört (anonym nur bei echten Server-Apps, die es hier nicht gibt). Und selbst
  heruntergeladen startet die Exe wegen SteamStub nur mit angemeldetem Steam-Client. Das zu umgehen (z. B. DRM entfernen)
  wäre genau die verbotene Umgehung – mache ich nicht.
- **Ein Konto spielt immer nur auf einem PC.** Willst du selbst mitspielen, während der Server läuft, braucht der
  Server-PC ein **zweites Konto mit eigener Kopie**. Familienfreigabe hilft nicht (keine gleichzeitige Nutzung).

## 3. Optionen

### A) Headless-Server aus den Spieldateien + Mod (Weiterentwicklung von 0.2.1)

Server-PC: Steam-Client angemeldet (Konto mit Spiel), Spiel per Steam oder SteamCMD installiert, `DDS2Server.exe`
startet das Spiel **ohne Grafik** (`-nullrhi -nosound`, ohne Fenster) als Koop-Host, die Mod lädt die Welt, die Konsole
steuert alles.

| | |
|---|---|
| Machbarkeit | **Ja**, mit Einschränkungen. Der Großteil läuft schon in 0.2.1 (Autoload, Konsole, save/kick/stop, Autosave). Offen: ob das Spiel mit `-nullrhi` stabil läuft – muss getestet werden. Fallback: kleines Fenster, minimiert. |
| Spielstand auf dem Server | **Ja** (Welt liegt im Spielstand des Server-PCs). |
| Einschränkungen | Steam-Client + Konto mit Spiel nötig; nur Windows (Linux höchstens über Proton, nicht unterstützt); der Host ist technisch ein Spieler und belegt einen Platz (bei 3 Spielern also 2 Mitspieler – mehr ist experimentell); RAM-/CPU-Bedarf wie das Spiel ohne Grafik (geschätzt 4–8 GB RAM). |
| SteamCMD | Möglich für Installation/Update **mit** Login (`+login <konto> +app_update 1708850 validate`), einmalig mit Steam-Guard-Code. Starten trotzdem nur mit laufendem Steam-Client. |
| Aufwand | gering bis mittel (1–2 Iterationen) |

### B) Komplett eigener Server (Spiellogik nachbauen, ohne Spieldateien, ohne Anmeldung)

| | |
|---|---|
| Machbarkeit | **Praktisch nein.** Die gesamte Spiellogik (Kunden, Dealer, Polizei, Wirtschaft, Produktion, Quests, Speichern) steckt in Hunderten Blueprints. Ein eigener Server müsste außerdem das Unreal-Replikationsprotokoll Bit für Bit nachbilden (Kanäle, Bunches, NetGUIDs, Property-Layouts jeder Klasse), damit die echten Clients ihn akzeptieren. Jedes Spiel-Update würde ihn brechen. |
| Aufwand | Personenjahre, Ergebnis unsicher. Nicht empfohlen. |

### C) Relay-Server ohne Spiellogik

Ein kleines Programm (läuft auch auf Linux, ohne Spieldateien, ohne Steam) nimmt Verbindungen auf Port 7777 an und
leitet sie an einen Spieler weiter, der zu Hause hostet. Der Host verbindet sich von innen zum Relay, braucht also
**keine Portfreigabe**.

| | |
|---|---|
| Machbarkeit | **Ja**, gut machbar (UDP-Weiterleitung + kleiner Host-Agent im Launcher). |
| Spielstand auf dem Server | **Nein** – Welt und Fortschritt bleiben beim hostenden Spieler; ohne ihn kein Spiel. |
| Vorteil | Billiger VPS/Linux genügt, keine Spielkopie auf dem Server, umgeht CGNAT/DS-Lite-Probleme. |
| Aufwand | mittel |

## 4. Empfehlung

**A als Dedicated Server**, dazu optional **C als Zusatz** für Leute ohne Portfreigabe. B nicht.

A ist der einzige Weg, bei dem Welt und Spielerdaten wirklich auf dem Server liegen, ohne DRM oder Besitzprüfung
anzufassen. Die Bedingung „ohne Steam-Anmeldung“ lässt sich damit nicht erfüllen – das gibt das Spiel nicht her.

## 5. Plan für Phase 2 (nach Freigabe)

1. **Server headless:** `DDS2Server.exe` (eigener Name, Parameter `--port --maxplayers --world`), Spiel mit
   `-nullrhi -nosound` ohne Fenster, Fallback minimiertes Fenster; Befehl `players`; Abfrage-Port (UDP 7778) für
   Name, Spielerzahl, Version, Passwort-Pflicht.
2. **SteamCMD-Skript:** `install_server.bat` lädt/aktualisiert Spiel und hält es aktuell (mit Login), Anleitung in der README.
3. **Menü im Originaldesign:** statt der selbstgebauten Box die Original-Bausteine des Spiels: eigener Eintrag
   „SERVER BEITRETEN“ in der Hauptmenü-Liste (gleicher Stil wie „SERVER DURCHSUCHEN“), Liste aus `OneSessionListWidget`
   für Favoriten (Name, IP, Ping, Spieler), Eingabe über das Spiel-Popup `ConfirmWidget` mit Texteingabe, Original-Klänge.
   Weg: zur Laufzeit per Lua. Wenn das an Grenzen stößt: eigenes UMG-Paket, dafür braucht es den Unreal Editor **5.3**
   (installiert ist 5.8, das passt nicht).
4. **Favoriten mit Ping/Spielerzahl, eindeutige Fehler:** Launcher fragt den Abfrage-Port ab; dadurch werden „falsches
   Passwort“, „Server voll“ und „Versionskonflikt“ **vor** dem Verbinden unterscheidbar (heute sehen Passwort und
   Version gleich aus).
5. **Installer:** Auswahl „Client-Mod“ / „Dedicated Server“ (gibt es schon als zwei Setups, wird zu einer Auswahl).
6. **Linux:** nur für das optionale Relay (C); der Spiel-Server selbst bleibt Windows.

## 6. Was du vorab testen kannst (hilft bei der Entscheidung)

Auf dem Server-PC mit Steam angemeldet, Spiel mit 0.2.1 installiert, in `server.cfg` `Headless=true` setzen und
**DDS2 Server** starten. Läuft das Spiel ohne Fenster bis „Welt läuft als Host“ und kann sich ein Mitspieler verbinden,
ist der wichtigste Teil von A bestätigt. Server-Log schicken.

## Quellen

- Websuche ohne Treffer zu einer DDS2-Server-App, u. a. [SteamSpy 1708850](https://steamspy.com/app/1708850),
  [Steam-Shop](https://store.steampowered.com/app/1708850)
- [Valve: SteamCMD](https://developer.valvesoftware.com/wiki/De/SteamCMD)
- lokale Prüfung: `appmanifest_1708850.acf`, PE-Sektionen der Shipping-Exe, IoStore-Paketindex

# Testplan Version 0.5 – Relay (Hosten ohne Portfreigabe)

Neu in 0.5: **DDS2Relay** (Linux/Windows, ohne Spieldateien) und der Relay-Agent im Launcher. Dazu beantwortet der
Launcher beim Hosten jetzt selbst den Abfrage-Port (Ping/Spielerzahl auch bei selbst gehosteten Spielen).
Die Tests aus [test-0.3.md](test-0.3.md) und [test-0.4.md](test-0.4.md) gelten weiter.

**Aufbau, drei Rechner:**
- **Relay**: ein Rechner mit öffentlicher IP, z. B. ein Linux-VPS. Zum ersten Ausprobieren reicht auch ein dritter PC
  im Heimnetz (dann LAN-IP statt öffentlicher IP).
- **Host-PC**: spielt und hostet die Welt, **ohne** Portfreigabe.
- **Spieler-PC**: tritt bei.

Alle mit 0.5. Bei jedem Test notieren, was passiert ist, und die genauen Meldungen.

## T25 – Relay starten

1. Linux: `DDS2Relay-0.5.0-linux-x64.tar.gz` entpacken, `sh start_relay.sh`.
   Windows: `DDS2Relay-0.5.0-windows.zip` entpacken, `start_relay.bat`.
2. Erwartet im Log: `DDS2Relay 0.5.0 läuft. Ports (UDP): Spiel 7777, Abfrage 7778, Host-Anmeldung 7779`
   und `Relay-Schlüssel für den Host: …`.
3. Firewall: UDP 7777–7779 eingehend öffnen (VPS-Anbieter, `ufw`, Windows-Firewall).
4. `status` eingeben → `Kein Host angemeldet.`
5. **Wichtig:** Ich konnte die Linux-Version hier nicht starten (kein Linux da). Startet sie? Falls nicht: genaue
   Fehlermeldung und Distribution (`cat /etc/os-release`) schicken.

## T26 – Host meldet sich an

1. Host-PC: DDS2 Multiplayer öffnen, **Relay** = Adresse des Relays, **Relay-Schlüssel** eintragen, Haken setzen.
2. **Mit Mod starten**. Erwartet im Launcher nach ein paar Sekunden: `Relay: Über Relay erreichbar: Mitspieler verbinden
   sich mit <Relay>:7777.` Im Relay-Log: `Host angemeldet: …`.
3. Gegenprobe: falschen Schlüssel eintragen → `Relay lehnt ab: falscher Relay-Schlüssel`.
4. Welt als Koop laden. Launcher zeigt `Du hostest … | über Relay: <Relay>:7777`.

## T27 – Mitspieler über das Relay

1. Spieler-PC: Favorit mit der Adresse des **Relays**, Port 7777, anlegen.
   Erwartet: Ping und `0/2`, Status online (der Ping enthält den Umweg über das Relay).
2. Verbinden – im Launcher oder im Spielmenü (SERVER BEITRETEN).
   Erwartet: Spieler landet in der Welt des Hosts. Relay-Log: `Mitspieler … verbunden`.
3. 10–15 Minuten zusammen spielen: Ruckelt es, gibt es Verbindungsabbrüche? Ping-Werte notieren.
4. Passwort am Host (Feld **Passwort** vor dem Start) → Spieler ohne/mit falschem Passwort bekommt die
   passende Meldung, mit richtigem kommt er rein.

## T28 – Abbrüche

1. Host schließt DDS2 Multiplayer, während der Spieler drin ist → Warnung beim Schließen; Spieler verliert die
   Verbindung (erwartet).
2. Relay neu starten, während der Host verbunden ist → Launcher meldet `Relay antwortet nicht mehr – verbinde neu ...`,
   danach wieder `Über Relay erreichbar`.

## T29 – Selbst hosten mit Portfreigabe (ohne Relay)

Mit Portfreigabe UDP 7777 **und 7778** beim Host: Sieht der Spieler-PC im Favoriten Ping und Spielerzahl des Hosts?

## Was du mir schickst

- Ergebnisse T25–T29 (kurz), Relay-Log (`logs/relay-*.log` im Relay-Ordner)
- Screenshot vom Launcher des Hosts während T26/T27

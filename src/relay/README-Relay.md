# DDS2Relay – Hosten ohne Portfreigabe

Das Relay ist ein kleines Programm für einen Server mit öffentlicher IP (z. B. ein günstiger Linux-VPS oder ein
Rechner mit Portfreigabe). Es braucht **keine Spieldateien und kein Steam**. Es reicht die Pakete nur weiter:

```
Mitspieler ──UDP 7777/7778──▶ DDS2Relay ◀──UDP 7779── DDS2 Multiplayer beim Host ──▶ Spiel des Hosts
```

Der Host verbindet sich **von innen nach außen** zum Relay. Deshalb braucht er keine Portfreigabe, und auch
DS-Lite/CGNAT-Anschlüsse funktionieren. Welt und Spielstand bleiben beim Host: Ohne laufendes Spiel beim Host gibt es
nichts zum Beitreten.

## Einrichten unter Linux

```
mkdir -p /opt/dds2relay && cd /opt/dds2relay
tar xzf DDS2Relay-<Version>-linux-x64.tar.gz
sh start_relay.sh
```

Beim ersten Start wird `relay.cfg` angelegt und ein **Relay-Schlüssel** erzeugt. Er steht im Log:
`Relay-Schlüssel für den Host: …`. Mit Strg+C beenden.

Als Dienst (startet mit dem Server): Anleitung in `dds2relay.service`.

Firewall des Servers: **UDP 7777, 7778 und 7779** eingehend erlauben, z. B.
```
ufw allow 7777:7779/udp
```
Bei Cloud-Anbietern zusätzlich in der Firewall/Security-Group des Anbieters.

## Einrichten unter Windows

`DDS2Relay-<Version>-windows.zip` entpacken, `start_relay.bat` starten. Windows-Firewall-Abfrage zulassen,
im Router UDP 7777–7779 auf diesen PC freigeben.

## Beim Host (spielt und hostet die Welt)

1. **DDS2 Multiplayer** öffnen.
2. **Relay:** Adresse des Relays eintragen (z. B. `relay.example.org` oder `203.0.113.5`; anderer Anmelde-Port: `:7800`).
3. **Relay-Schlüssel** eintragen, Haken bei **„Beim Hosten über das Relay erreichbar sein“**.
4. **Mit Mod starten**, Spielstand als Koop laden.
5. Unten steht: `über Relay: relay.example.org:7777`.

## Bei den Mitspielern

Ganz normal **Server beitreten** mit der Adresse des Relays und Port 7777 (im Launcher oder im Spielmenü).
Ping, Spielerzahl und Passwortprüfung funktionieren ebenfalls über das Relay.

## relay.cfg

| Einstellung | Bedeutung |
|---|---|
| `GamePort` | UDP-Port für die Mitspieler (Standard 7777) |
| `QueryPort` | UDP-Abfrage-Port für Ping/Spielerzahl (Standard 7778) |
| `AgentPort` | UDP-Port, an dem sich der Host anmeldet (Standard 7779) |
| `MaxClients` | gleichzeitige Mitspieler (Standard 8) |
| `Secret` | Relay-Schlüssel; nur wer ihn kennt, kann sich als Host anmelden |

Mehrere Hosts: je Host ein eigenes Relay mit anderen Ports und eigenem Ordner starten.

## Konsolenbefehle

`status` (Host angemeldet? wie viele Mitspieler?), `stop`.

## Sicherheit

- Der Host meldet sich mit einem HMAC über den Schlüssel an; der Schlüssel selbst wird nie übertragen.
  Wiederholte (mitgeschnittene) Anmeldepakete werden abgelehnt.
- Das Relay sieht nur die Spielpakete, kann sie aber nicht sinnvoll lesen oder verändern, ohne das Spiel zu stören.
- Schlüssel wechseln: `Secret=` in `relay.cfg` leeren und das Relay neu starten.

## Fehlerbehebung

| Meldung | Lösung |
|---|---|
| Launcher: „Relay … antwortet nicht“ | Läuft DDS2Relay? Ist UDP 7779 in der Server-Firewall offen? Adresse richtig? |
| Launcher: „falscher Relay-Schlüssel“ | Schlüssel aus `relay.cfg` / Relay-Log genau übernehmen. |
| Launcher: „Uhrzeit … weicht ab“ | Uhrzeit von Host-PC oder Server stimmt nicht (Zeitsynchronisierung einschalten). |
| Relay: „kein Host angemeldet“ | Der Host hat DDS2 Multiplayer nicht offen oder den Relay-Haken nicht gesetzt. |
| Relay: „Relay voll“ | `MaxClients` erhöhen. |
| Mitspieler: Zeitüberschreitung | UDP 7777/7778 am Relay-Server offen? Hat der Host die Welt als Koop geladen? |

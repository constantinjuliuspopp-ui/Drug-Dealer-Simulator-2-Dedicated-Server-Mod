# Phase 1 – Analyse (Stand 2026-11-01, Spiel-Build 23009320)

## Befunde

- **Engine:** Unreal Engine 5.3, Custom-Build `UnrealEngine_5_3_DDS2`.
- **Inhalte:** IoStore (`pakchunk0-Windows.utoc/.ucas`) und `.pak` v11 – nicht verschlüsselt, nicht signiert.
- **Anti-Cheat:** keins (kein EasyAntiCheat, kein BattlEye).
- **Netcode:** Unreal-Replikation als Listen-Server. OnlineSubsystemSteam mit `USteamNetDriver`,
  Steam-Lobbys über AdvancedSessions/AdvancedSteamSessions, eigenes Session-Subsystem
  (`DdsJoinSession`, `DdsUserSessionSettings`, `MultiplayerOn…Complete`).
- `USteamNetDriver` kennt `bIsPassthrough`/`bIsLanMatch`, `UIpNetDriver` ist einkompiliert
  → normale IP-Verbindungen sind technisch vorhanden.
- Koop offiziell bis 3 Spieler, Fortschritt im Spielstand des Hosts.
- Nur Game-Target, kein Server-Target.

## Machbarkeit

| Ziel | Bewertung |
|---|---|
| Direkt-IP-Join | machbar, mittleres Risiko (Spiel-Logik erwartet eigenen Session-Ablauf) |
| Headless Dedicated Server | **nicht machbar** ohne Quellcode des Spiels |
| Pseudo-Dedicated (Host im Hintergrund, `-nullrhi`) | machbar, mittleres bis hohes Risiko; Host belegt einen Spielerplatz und braucht ein Steam-Konto mit dem Spiel |

## Architektur

- `DDS2Mod.exe` (C#/.NET): Launcher für Client- und Server-Modus, Server-Konsole, liest `server.cfg`.
- UE4SS-Mod im Spiel: Host/Join, Versionscheck, Kick/Save, Kommunikation mit dem Launcher (Named Pipe).
- Blueprint-Mod (.pak): Menüpunkt „Server beitreten“ (braucht UE 5.3 Editor zum Bauen).
- Inno-Setup-Installer.

# Dedicated Server (DDS2Server.exe)

Eigenes Konsolenprojekt `DDS2Server.csproj`, nutzt den Code aus `src/shared`.

- `ServerMode.cs` – liest `server.cfg` + Parameter, prüft Steam/Spielordner/Mod, setzt die Netzwerk-Einstellungen,
  startet das Spiel ohne Fenster (`-nullrhi -nosound`, Fallback kleines Fenster), steuert die DDS2Net-Mod über den
  IPC-Ordner, beantwortet den Abfrage-Port, Autosave, Neustart nach Absturz, Log in `logs/`.
- `start_server.bat` – Vorlage; der Installer setzt den Pfad zur `DDS2Server.exe` ein.
- `install_server.bat` – installiert/aktualisiert das Spiel per SteamCMD (Anmeldung in SteamCMD selbst).

Befehle: `status`, `players`, `kick <Name>`, `save`, `stop`, `load <Welt>`, `start`, `help`.

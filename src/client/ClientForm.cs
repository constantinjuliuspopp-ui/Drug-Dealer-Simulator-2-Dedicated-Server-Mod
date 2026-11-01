using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using DDS2Mod.Shared;

namespace DDS2Mod.Client;

/// <summary>Client-Oberfläche: Server beitreten, Favoriten, selbst hosten, Status.</summary>
public sealed class ClientForm : Form
{
    static readonly TimeSpan ConnectTimeout = TimeSpan.FromMinutes(3);

    readonly ListBox _favorites = new() { Dock = DockStyle.Fill, IntegralHeight = false };
    readonly TextBox _name = new() { Dock = DockStyle.Fill };
    readonly TextBox _host = new() { Dock = DockStyle.Fill, PlaceholderText = "z. B. 192.168.178.20 oder server.example.org" };
    readonly NumericUpDown _port = new() { Minimum = 1, Maximum = 65535, Value = ModInfo.DefaultPort, Width = 90 };
    readonly TextBox _password = new() { Dock = DockStyle.Fill, UseSystemPasswordChar = true, PlaceholderText = "leer = kein Passwort" };
    readonly Button _connect = new() { Text = "Verbinden", AutoSize = true };
    readonly Button _hostGame = new() { Text = "Mit Mod starten (hosten / im Spiel beitreten)", AutoSize = true };
    readonly Button _steam = new() { Text = "Normal starten (Steam-Koop)", AutoSize = true };
    readonly Button _saveFavorite = new() { Text = "Als Favorit speichern", AutoSize = true };
    readonly Button _removeFavorite = new() { Text = "Favorit entfernen", AutoSize = true };
    readonly Label _status = new() { Dock = DockStyle.Fill, AutoSize = false, Text = "Bereit." };
    readonly System.Windows.Forms.Timer _timer = new() { Interval = 1000 };

    readonly IpcChannel _ipc = new(Path.Combine(ModInfo.AppDataDir, "ipc-client"));
    List<Favorite> _favoriteList = Favorites.Load();
    Process? _game;
    string _expectedMode = "";
    string _target = "";
    DateTime _launchedAt;
    bool _gotError, _reachedMode;

    public ClientForm()
    {
        Text = $"DDS2 Multiplayer {ModInfo.Version}";
        MinimumSize = new Size(720, 420);
        Size = new Size(780, 460);
        StartPosition = FormStartPosition.CenterScreen;
        Font = new Font("Segoe UI", 9.5f);

        var fields = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, AutoSize = true };
        fields.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        fields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        AddRow(fields, "Name", _name);
        AddRow(fields, "IP / Hostname", _host);
        AddRow(fields, "Port", _port);
        AddRow(fields, "Passwort", _password);

        var favButtons = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true };
        favButtons.Controls.AddRange(new Control[] { _saveFavorite, _removeFavorite });
        var mainButtons = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true };
        mainButtons.Controls.AddRange(new Control[] { _connect, _hostGame, _steam });

        var right = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, Padding = new Padding(8, 0, 0, 0) };
        right.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        right.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        right.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        right.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        right.Controls.Add(fields);
        right.Controls.Add(favButtons);
        right.Controls.Add(mainButtons);
        right.Controls.Add(_status);

        var left = new GroupBox { Text = "Favoriten", Dock = DockStyle.Fill };
        left.Controls.Add(_favorites);

        var split = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, Padding = new Padding(10) };
        split.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 250));
        split.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        split.Controls.Add(left);
        split.Controls.Add(right);
        Controls.Add(split);
        AcceptButton = _connect;

        _favorites.SelectedIndexChanged += (_, _) => FillFromFavorite();
        _favorites.DoubleClick += async (_, _) => await ConnectAsync();
        _saveFavorite.Click += (_, _) => SaveFavorite();
        _removeFavorite.Click += (_, _) => RemoveFavorite();
        _connect.Click += async (_, _) => await ConnectAsync();
        _hostGame.Click += (_, _) => HostGame();
        _steam.Click += (_, _) => StartViaSteam();
        _timer.Tick += (_, _) => Poll();
        FormClosing += OnClosing;

        RefreshFavorites();
        CleanupLeftovers();
        try { Favorites.ExportForGame(_favoriteList); } catch (IOException) { }
    }

    static void AddRow(TableLayoutPanel panel, string label, Control control)
    {
        panel.Controls.Add(new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(3, 7, 3, 3) });
        panel.Controls.Add(control);
    }

    // Einstellungen einer früheren Sitzung zurücksetzen, falls das Spiel nicht mehr läuft.
    void CleanupLeftovers()
    {
        if (IniOverride.IsApplied() && !GameLocator.IsGameRunning())
            IniOverride.RestoreAll();
    }

    void RefreshFavorites()
    {
        _favorites.BeginUpdate();
        _favorites.Items.Clear();
        foreach (var f in _favoriteList) _favorites.Items.Add(f);
        _favorites.EndUpdate();
    }

    void FillFromFavorite()
    {
        if (_favorites.SelectedItem is not Favorite f) return;
        _name.Text = f.Name;
        _host.Text = f.Host;
        _port.Value = f.Port;
    }

    void SaveFavorite()
    {
        var host = _host.Text.Trim();
        if (Uri.CheckHostName(host) == UriHostNameType.Unknown)
        {
            SetStatus("Bitte eine gültige IP oder einen Hostnamen eingeben.", error: true);
            return;
        }
        var name = string.IsNullOrWhiteSpace(_name.Text) ? host : _name.Text.Trim();
        _favoriteList.RemoveAll(f => f.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        _favoriteList.Add(new Favorite(name, host, (int)_port.Value));
        _favoriteList = _favoriteList.OrderBy(f => f.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
        Favorites.Save(_favoriteList);
        RefreshFavorites();
        SetStatus($"Favorit '{name}' gespeichert.");
    }

    void RemoveFavorite()
    {
        if (_favorites.SelectedItem is not Favorite f) return;
        _favoriteList.Remove(f);
        Favorites.Save(_favoriteList);
        RefreshFavorites();
    }

    async Task ConnectAsync()
    {
        var host = _host.Text.Trim();
        var port = (int)_port.Value;
        if (Uri.CheckHostName(host) == UriHostNameType.Unknown)
        {
            SetStatus("Bitte eine gültige IP oder einen Hostnamen eingeben (ohne Port).", error: true);
            return;
        }

        IPAddress address;
        try
        {
            SetStatus($"Löse {host} auf ...");
            var addresses = await Dns.GetHostAddressesAsync(host);
            address = addresses.FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork)
                ?? throw new SocketException((int)SocketError.HostNotFound);
        }
        catch (SocketException)
        {
            SetStatus($"Hostname '{host}' wurde nicht gefunden.", error: true);
            return;
        }

        _target = $"{host}:{port}";
        StartGame("client", new[] { $"{address}:{port}" }, ModInfo.DefaultPort, $"Spiel startet und verbindet mit {_target} ...");
    }

    void HostGame() =>
        StartGame("host", Array.Empty<string>(), (int)_port.Value,
            $"Spiel startet. Hosten: Spielstand als Koop laden – andere verbinden sich auf Port {(int)_port.Value}/UDP. "
            + "Beitreten: im Hauptmenü F8 drücken oder oben rechts auf \"Server beitreten\" klicken.");

    void StartGame(string expectedMode, string[] gameArgs, int listenPort, string message)
    {
        var gameDir = GameLocator.FindGameDir();
        if (gameDir is null)
        {
            SetStatus("Drug Dealer Simulator 2 wurde nicht gefunden. Bitte den Installer erneut ausführen.", error: true);
            return;
        }
        if (!GameProcess.IsModInstalled(gameDir))
        {
            SetStatus("Die Mod ist im Spielordner nicht installiert. Bitte den Installer ausführen.", error: true);
            return;
        }
        if (GameLocator.IsGameRunning() && MessageBox.Show(this,
                "Das Spiel läuft bereits. Trotzdem eine weitere Instanz starten (z. B. Test neben einem eigenen Server)?",
                Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
            return;

        try
        {
            SaveBackup.Create();
            IniOverride.Apply(new OverrideSettings(listenPort, 3, _password.Text));
            _ipc.Reset();
            _game = GameProcess.Launch(gameDir, "client", _ipc.Dir, gameArgs);
        }
        catch (Exception ex)
        {
            SetStatus($"Start fehlgeschlagen: {ex.Message}", error: true);
            return;
        }

        _expectedMode = expectedMode;
        _launchedAt = DateTime.Now;
        _gotError = _reachedMode = false;
        SetRunning(true);
        SetStatus(message);
        _timer.Start();
    }

    void StartViaSteam()
    {
        if (GameLocator.IsGameRunning())
        {
            SetStatus("Bitte zuerst das laufende Spiel beenden.", error: true);
            return;
        }
        IniOverride.RestoreAll();
        GameProcess.LaunchViaSteam();
        SetStatus("Spiel über Steam gestartet – ohne Mod-Einstellungen, Steam-Koop funktioniert normal.");
    }

    void Poll()
    {
        foreach (var e in _ipc.ReadNewEvents())
        {
            if (e.Text.StartsWith("Verbinde (Spielmenü) mit ", StringComparison.Ordinal))
            {
                // Beitritt über das Menü im Spiel: ab jetzt wie ein Verbinden aus dem Launcher überwachen.
                _target = e.Text["Verbinde (Spielmenü) mit ".Length..];
                _expectedMode = "client";
                _launchedAt = DateTime.Now;
                _gotError = _reachedMode = false;
                SetStatus($"Verbinde mit {_target} ...");
                continue;
            }
            if (!ErrorTexts.IsError(e)) continue;
            _gotError = true;
            SetStatus(ErrorTexts.Describe(e), error: true);
        }

        var status = _ipc.ReadStatus();
        if (status is { IsStale: false })
        {
            if (status.Version != ModInfo.Version)
                SetStatus($"Mod-Version im Spiel ({status.Version}) passt nicht zum Launcher ({ModInfo.Version}). Bitte neu installieren.", error: true);
            else if (status.Mode == "client")
            {
                _reachedMode = true;
                SetStatus($"Verbunden mit {(_target.Length > 0 ? _target : "Server")} – {status.Players.Count} Spieler.");
            }
            else if (status.Mode == "host")
            {
                _reachedMode = true;
                SetStatus($"Du hostest auf Port {(int)_port.Value}/UDP – {status.Players.Count} Spieler: {string.Join(", ", status.Players.Select(p => p.Name))}");
            }
        }

        if (_expectedMode == "client" && !_reachedMode && !_gotError && DateTime.Now - _launchedAt > ConnectTimeout)
        {
            _gotError = true;
            SetStatus("Zeitüberschreitung: Keine Verbindung zustande gekommen. Läuft der Server, stimmen IP/Port, ist UDP-Port freigegeben?", error: true);
        }

        if (_game is { HasExited: true })
        {
            _timer.Stop();
            _game = null;
            if (!GameLocator.IsGameRunning()) IniOverride.RestoreAll();
            SetRunning(false);
            if (!_gotError) SetStatus("Spiel beendet. Mod-Einstellungen wurden zurückgesetzt.");
        }
    }

    void SetRunning(bool running)
    {
        _connect.Enabled = _hostGame.Enabled = _steam.Enabled = !running;
    }

    void SetStatus(string text, bool error = false)
    {
        _status.Text = text;
        _status.ForeColor = error ? Color.Firebrick : SystemColors.ControlText;
    }

    void OnClosing(object? sender, FormClosingEventArgs e)
    {
        if (_game is { HasExited: false })
        {
            var answer = MessageBox.Show(this,
                "Das Spiel läuft noch. Die Mod-Einstellungen bleiben aktiv, bis du DDS2 Multiplayer wieder öffnest. Trotzdem schließen?",
                Text, MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
            if (answer != DialogResult.Yes) e.Cancel = true;
        }
    }
}

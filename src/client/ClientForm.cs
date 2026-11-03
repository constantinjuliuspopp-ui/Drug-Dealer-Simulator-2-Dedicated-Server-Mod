using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using DDS2Mod.Shared;

namespace DDS2Mod.Client;

/// <summary>Client-Oberfläche: Server beitreten, Favoriten, selbst hosten, Status.</summary>
public sealed class ClientForm : Form
{
    static readonly TimeSpan ConnectTimeout = TimeSpan.FromMinutes(3);

    readonly ListView _favorites = new()
    {
        Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, MultiSelect = false, HideSelection = false,
        HeaderStyle = ColumnHeaderStyle.Nonclickable,
    };
    readonly TextBox _name = new() { Dock = DockStyle.Fill };
    readonly TextBox _host = new() { Dock = DockStyle.Fill, PlaceholderText = "z. B. 192.168.178.20 oder server.example.org" };
    readonly NumericUpDown _port = new() { Minimum = 1, Maximum = 65535, Value = ModInfo.DefaultPort, Width = 90 };
    readonly TextBox _password = new() { Dock = DockStyle.Fill, UseSystemPasswordChar = true, PlaceholderText = "leer = kein Passwort" };
    readonly Button _connect = new() { Text = "Verbinden", AutoSize = true };
    readonly Button _hostGame = new() { Text = "Mit Mod starten (hosten / im Spiel beitreten)", AutoSize = true };
    readonly Button _steam = new() { Text = "Normal starten (Steam-Koop)", AutoSize = true };
    readonly Button _saveFavorite = new() { Text = "Als Favorit speichern", AutoSize = true };
    readonly Button _removeFavorite = new() { Text = "Favorit entfernen", AutoSize = true };
    readonly Button _refresh = new() { Text = "Aktualisieren", AutoSize = true };
    readonly System.Windows.Forms.Timer _refreshTimer = new() { Interval = 30_000 };
    readonly TextBox _relayAddress = new() { Dock = DockStyle.Fill, PlaceholderText = "optional: relay.example.org – nur zum Hosten ohne Portfreigabe" };
    readonly TextBox _relaySecret = new() { Dock = DockStyle.Fill, UseSystemPasswordChar = true, PlaceholderText = "Schlüssel aus dem Relay-Log / relay.cfg" };
    readonly CheckBox _useRelay = new() { Text = "Beim Hosten über das Relay erreichbar sein", AutoSize = true };
    readonly Label _status = new() { Dock = DockStyle.Fill, AutoSize = false, Text = "Bereit." };
    readonly System.Windows.Forms.Timer _timer = new() { Interval = 1000 };

    readonly IpcChannel _ipc = new(Path.Combine(ModInfo.AppDataDir, "ipc-client"));
    List<Favorite> _favoriteList = Favorites.Load();
    readonly Dictionary<Favorite, ServerInfo?> _infos = new();
    string _activePassword = "";     // Passwort, mit dem das laufende Spiel gestartet wurde
    bool _joinBusy;
    (string Host, int Port)? _restartTarget;
    Process? _game;
    string _expectedMode = "";
    string _target = "";
    DateTime _launchedAt;
    bool _gotError, _reachedMode;
    readonly LauncherSettings _settings = LauncherSettings.Load();
    // Beim Hosten: eigener Abfrage-Port (Ping/Spielerzahl) und optional der Relay-Agent
    CancellationTokenSource? _hostCts;
    ServerQuery.Responder? _hostQuery;
    RelayAgent? _relayAgent;
    volatile ModStatus? _lastStatus;
    string _gameBuild = "";

    public ClientForm()
    {
        Text = $"DDS2 Multiplayer {ModInfo.Version}";
        MinimumSize = new Size(900, 540);
        Size = new Size(1000, 580);
        StartPosition = FormStartPosition.CenterScreen;
        Font = new Font("Segoe UI", 9.5f);

        var fields = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, AutoSize = true };
        fields.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        fields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        AddRow(fields, "Name", _name);
        AddRow(fields, "IP / Hostname", _host);
        AddRow(fields, "Port", _port);
        AddRow(fields, "Passwort", _password);
        AddRow(fields, "Relay", _relayAddress);
        AddRow(fields, "Relay-Schlüssel", _relaySecret);
        fields.Controls.Add(new Label());
        fields.Controls.Add(_useRelay);
        _relayAddress.Text = _settings.RelayAddress;
        _relaySecret.Text = _settings.RelaySecret;
        _useRelay.Checked = _settings.UseRelay;

        var favButtons = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true };
        favButtons.Controls.AddRange(new Control[] { _saveFavorite, _removeFavorite, _refresh });
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
        _favorites.Columns.Add("Name", 130);
        _favorites.Columns.Add("Adresse", 140);
        _favorites.Columns.Add("Ping", 55, HorizontalAlignment.Right);
        _favorites.Columns.Add("Spieler", 55, HorizontalAlignment.Right);
        _favorites.Columns.Add("Status", 70);

        var split = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, Padding = new Padding(10) };
        split.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 470));
        split.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        split.Controls.Add(left);
        split.Controls.Add(right);
        Controls.Add(split);
        AcceptButton = _connect;

        _favorites.SelectedIndexChanged += (_, _) => FillFromFavorite();
        _favorites.DoubleClick += async (_, _) => await ConnectAsync();
        _refresh.Click += async (_, _) => await RefreshServerInfoAsync();
        _refreshTimer.Tick += async (_, _) => await RefreshServerInfoAsync();
        Shown += async (_, _) => { _refreshTimer.Start(); await RefreshServerInfoAsync(); };
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
        foreach (var f in _favoriteList)
            _favorites.Items.Add(new ListViewItem(new[] { f.Name, $"{f.Host}:{f.Port}", "", "", "…" }) { Tag = f });
        _favorites.EndUpdate();
    }

    Favorite? SelectedFavorite => _favorites.SelectedItems.Count > 0 ? _favorites.SelectedItems[0].Tag as Favorite : null;

    bool _refreshing;

    /// <summary>Fragt alle Favoriten parallel über den Abfrage-Port ab (Ping, Spieler, Zustand).</summary>
    async Task RefreshServerInfoAsync()
    {
        if (_refreshing || _favorites.Items.Count == 0) return;
        _refreshing = true;
        try
        {
            var items = _favorites.Items.Cast<ListViewItem>().ToList();
            await Task.WhenAll(items.Select(async item =>
            {
                var f = (Favorite)item.Tag!;
                var ep = await ResolveAsync(f.Host, f.EffectiveQueryPort);
                var info = ep is null ? null : await ServerQuery.QueryAsync(ep, attempts: 2);
                _infos[f] = info;
                if (item.ListView is null) return; // Liste wurde inzwischen neu aufgebaut
                item.SubItems[2].Text = info is null ? "–" : $"{info.Ping} ms";
                item.SubItems[3].Text = info is null ? "–" : $"{info.Players}/{info.MaxPlayers}";
                item.SubItems[4].Text = info is null ? "offline"
                    : info.ModVersion != ModInfo.Version ? $"Mod {info.ModVersion}"
                    : info.IsJoinable ? (info.HasPassword ? "online, PW" : "online")
                    : "startet";
                item.ForeColor = info is { IsJoinable: true } && info.ModVersion == ModInfo.Version ? SystemColors.WindowText : SystemColors.GrayText;
            }));
            // Für die Favoritenliste im Menü des Spiels
            try { Favorites.ExportForGame(_favoriteList, _infos); } catch (IOException) { }
        }
        finally { _refreshing = false; }
    }

    static async Task<IPEndPoint?> ResolveAsync(string host, int port)
    {
        try
        {
            var address = (await Dns.GetHostAddressesAsync(host)).FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork);
            return address is null ? null : new IPEndPoint(address, port);
        }
        catch (SocketException) { return null; }
    }

    void FillFromFavorite()
    {
        if (SelectedFavorite is not { } f) return;
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
        _ = RefreshServerInfoAsync();
    }

    void RemoveFavorite()
    {
        if (SelectedFavorite is not { } f) return;
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

        SetStatus($"Löse {host} auf ...");
        var queryPort = SelectedFavorite is { } fav && fav.Host == host && fav.Port == port ? fav.EffectiveQueryPort : ServerQuery.DefaultQueryPort(port);
        var queryEp = await ResolveAsync(host, queryPort);
        if (queryEp is null)
        {
            SetStatus($"Hostname '{host}' wurde nicht gefunden.", error: true);
            return;
        }
        var address = queryEp.Address;

        // Vorab-Prüfung über den Abfrage-Port: erreichbar, Version, voll, Passwort.
        _connect.Enabled = false;
        SetStatus($"Frage {host} ab ...");
        var gameDir = GameLocator.FindGameDir();
        var verdict = await JoinCheck.RunAsync(queryEp, gameDir is null ? "" : GameLocator.ReadBuildId(gameDir), _password.Text);
        _connect.Enabled = true;
        if (verdict.Problem == JoinProblem.NoAnswer)
        {
            if (MessageBox.Show(this, $"Der Server antwortet nicht auf dem Abfrage-Port {queryPort}/UDP.\n\n"
                    + "Läuft der Server bzw. das Spiel des Hosts? Ist UDP " + queryPort + " freigegeben? Ist nur der Spielport "
                    + "freigegeben, klappt das Verbinden trotzdem.\n\nTrotzdem verbinden?", Text, MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
            {
                SetStatus(verdict.Message, error: true);
                return;
            }
        }
        else if (!verdict.Ok)
        {
            SetStatus(verdict.Message, error: true);
            if (verdict.Problem is JoinProblem.PasswordNeeded or JoinProblem.WrongPassword) _password.Focus();
            return;
        }

        _target = verdict.Info is { Name.Length: > 0 } i ? $"{i.Name} ({host}:{port})" : $"{host}:{port}";
        StartGame("client", new[] { $"{address}:{port}" }, ModInfo.DefaultPort, $"Spiel startet und verbindet mit {_target} ...");
    }

    void HostGame()
    {
        string relayHost = "";
        var relayPort = RelayProtocol.DefaultAgentPort;
        if (_useRelay.Checked && (!RelayProtocol.TryParseAddress(_relayAddress.Text, out relayHost, out relayPort) || _relaySecret.Text.Trim().Length == 0))
        {
            SetStatus("Relay: bitte Adresse (z. B. relay.example.org) und Schlüssel eintragen – oder den Haken entfernen.", error: true);
            return;
        }
        _settings.RelayAddress = _relayAddress.Text.Trim();
        _settings.RelaySecret = _relaySecret.Text.Trim();
        _settings.UseRelay = _useRelay.Checked;
        try { _settings.Save(); } catch (IOException) { }

        var port = (int)_port.Value;
        StartGame("host", Array.Empty<string>(), port,
            $"Spiel startet. Hosten: Spielstand als Koop laden – andere verbinden sich auf Port {port}/UDP"
            + (_useRelay.Checked ? $" oder über das Relay {relayHost}." : ".")
            + " Beitreten: im Hauptmenü auf \"SERVER BEITRETEN\" klicken.");
        if (_game != null) StartHostServices(port, _useRelay.Checked ? relayHost : null, relayPort);
    }

    void StartHostServices(int port, string? relayHost, int relayPort)
    {
        _hostCts = new CancellationTokenSource();
        var queryPort = ServerQuery.DefaultQueryPort(port);
        try
        {
            _hostQuery = new ServerQuery.Responder(queryPort, _activePassword, HostInfo);
            _ = _hostQuery.RunAsync(_hostCts.Token);
        }
        catch (SocketException)
        {
            _hostQuery = null; // Port belegt (z. B. eigener Server läuft): Hosten geht trotzdem, nur ohne Ping-Anzeige
        }
        if (relayHost is null) return;
        _relayAgent = new RelayAgent(relayHost, relayPort, _relaySecret.Text.Trim(), port, queryPort);
        _relayAgent.Status += (message, isError) => BeginInvoke(() => SetStatus("Relay: " + message, isError));
        _relayAgent.Start();
    }

    void StopHostServices()
    {
        _hostCts?.Cancel();
        _hostQuery?.Dispose();
        _relayAgent?.Dispose();
        _hostCts = null;
        _hostQuery = null;
        _relayAgent = null;
    }

    // Was der eigene Abfrage-Port beim Hosten meldet (läuft auf einem Hintergrund-Thread).
    ServerInfo HostInfo()
    {
        var s = _lastStatus;
        var state = s is null || s.IsStale ? "starting"
            : s.Mode == "host" ? s.Place
            : s.Mode == "client" ? "client"
            : s.Place == "world" ? "singleplayer"
            : s.Place;
        var players = s is { Mode: "host", IsStale: false } ? s.Others.Count() : 0;
        return new ServerInfo($"{Environment.UserName}s Spiel", ModInfo.Version, ServerQuery.ProtocolVersion, _gameBuild, state,
            players, 2, _activePassword.Length > 0, "");
    }

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
            _activePassword = _password.Text;
            _gameBuild = GameLocator.ReadBuildId(gameDir);
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

    /// <summary>Beitritt aus dem Menü im Spiel: Server prüfen und dem Spiel antworten.</summary>
    async Task HandleJoinRequestAsync(JoinRequest req)
    {
        _joinBusy = true;
        try
        {
            var fav = _favoriteList.FirstOrDefault(f => f.Host.Equals(req.Host, StringComparison.OrdinalIgnoreCase) && f.Port == req.Port);
            var gameDir = GameLocator.FindGameDir();
            var r = await JoinRequests.EvaluateAsync(req, _activePassword, gameDir is null ? "" : GameLocator.ReadBuildId(gameDir),
                fav?.EffectiveQueryPort ?? ServerQuery.DefaultQueryPort(req.Port));
            JoinRequests.Respond(_ipc.Dir, req.Id, r);
            if (r.Result == "restart" && r.NewPassword != null)
            {
                // Das Passwort gehört zur Netzwerk-Version, die das Spiel nur beim Start liest.
                _password.Text = r.NewPassword;
                _restartTarget = (req.Host, req.Port);
                SetStatus($"Neues Passwort für {req.Host}:{req.Port} – das Spiel startet neu und verbindet dann ...");
                _ipc.Send("quit");
            }
            else if (r.Result != "ok")
                SetStatus(r.Message, error: true);
        }
        catch (IOException ex) { SetStatus($"Anfrage aus dem Spiel fehlgeschlagen: {ex.Message}", error: true); }
        finally { _joinBusy = false; }
    }

    void Poll()
    {
        var refresh = Path.Combine(_ipc.Dir, JoinRequests.RefreshFile);
        if (File.Exists(refresh))
        {
            try { File.Delete(refresh); } catch (IOException) { }
            _ = RefreshServerInfoAsync();
        }
        if (!_joinBusy && JoinRequests.TryTake(_ipc.Dir) is { } request)
            _ = HandleJoinRequestAsync(request);
        // Favorit speichern/entfernen aus dem Menü im Spiel
        if (AgentMode.ApplyFavoriteRequests(_ipc.Dir))
        {
            _favoriteList = Favorites.Load();
            _infos.Clear();
            RefreshFavorites();
            _ = RefreshServerInfoAsync();
        }

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

        var status = _lastStatus = _ipc.ReadStatus();
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
                var relay = _relayAgent switch
                {
                    { Connected: true } a => $" | über Relay: {a.RelayHost}:{a.RelayGamePort}",
                    { } a => " | Relay: " + (a.LastError.Length > 0 ? a.LastError : "verbinde ..."),
                    null => "",
                };
                SetStatus($"Du hostest auf Port {(int)_port.Value}/UDP – {status.Players.Count} Spieler: {string.Join(", ", status.Players.Select(p => p.Name))}{relay}",
                    _relayAgent is { Connected: false, LastError.Length: > 0 });
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
            StopHostServices();
            if (!GameLocator.IsGameRunning()) IniOverride.RestoreAll();
            SetRunning(false);
            if (_restartTarget is { } restart)
            {
                _restartTarget = null;
                _host.Text = restart.Host;
                _port.Value = restart.Port;
                BeginInvoke(new Action(async () => await ConnectAsync()));
            }
            else if (!_gotError) SetStatus("Spiel beendet. Mod-Einstellungen wurden zurückgesetzt.");
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
                "Das Spiel läuft noch. Die Mod-Einstellungen bleiben aktiv, bis du DDS2 Multiplayer wieder öffnest."
                + (_relayAgent != null ? " Die Verbindung zum Relay wird getrennt – Mitspieler über das Relay fliegen raus." : "")
                + " Trotzdem schließen?",
                Text, MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
            if (answer != DialogResult.Yes) e.Cancel = true;
        }
        if (!e.Cancel) StopHostServices();
    }
}

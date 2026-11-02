--[[
    DDS2Net – Direkt-IP-Multiplayer für Drug Dealer Simulator 2 (UE4SS-Lua-Mod)

    Läuft in jeder Spielinstanz (Client und Server). Kommunikation mit DDS2Mod.exe
    über Dateien im Ordner aus der Umgebungsvariable DDS2_IPC_DIR:
      commands.txt  Launcher -> Mod  (eine Zeile pro Befehl, wird nur angehängt)
      events.log    Mod -> Launcher  (zeit|level|text)
      status.txt    Mod -> Launcher  (key=value, wird alle 2 s neu geschrieben)

    Spielfunktionen (aus den Blueprints des Spiels, Build 23009320):
      BP_MainMenuPC_C:RequestLaunchSavedGame(CartelAlias, Coop, Advertise, BackupFile)
      BP_LobbyPC_C:RequestStartGame()
      BP_DDS2_PlayerController_C:RequestSaveCartelProgress()   (= "Speichern" im Pausemenü)
]]

local MOD_VERSION = "@MOD_VERSION@"
local IPC_DIR = os.getenv("DDS2_IPC_DIR")
local ROLE = os.getenv("DDS2_ROLE") or "client"
local WORLD = os.getenv("DDS2_WORLD") or ""
local DEFAULT_PORT = tonumber(os.getenv("DDS2_DEFAULT_PORT") or "") or 7777

local NetworkFailures = {
    [0] = "NetDriverAlreadyExists", "NetDriverCreateFailure", "NetDriverListenFailure",
    "ConnectionLost", "ConnectionTimeout", "FailureReceived", "OutdatedClient",
    "OutdatedServer", "PendingConnectionFailure", "NetGuidMismatch", "NetChecksumMismatch",
}
local TravelFailures = {
    [0] = "NoLevel", "LoadMapFailure", "InvalidURL", "PackageMissing", "PackageVersion",
    "NoDownload", "TravelFailure", "CheatCommands", "PendingNetGameCreateFailure",
    "CloudSaveFailure", "ServerTravelFailure", "ClientTravelFailure",
}

local function Log(msg)
    print("[DDS2Net] " .. msg .. "\n")
end

local function Event(level, msg)
    Log(level .. ": " .. msg)
    if not IPC_DIR then return end
    local f = io.open(IPC_DIR .. "\\events.log", "a")
    if f then
        f:write(os.date("%Y-%m-%d %H:%M:%S") .. "|" .. level .. "|" .. msg:gsub("[\r\n]", " ") .. "\n")
        f:close()
    end
end

-- Konfiguration (Mods\DDS2Net\config.txt, key=value)
local function ModDir()
    local src = debug.getinfo(1, "S").source:gsub("^@", "")
    return src:match("^(.*)[/\\]Scripts[/\\][^/\\]+$")
end

local Config = {
    SaveObjectClass = "BP_DDS2_PlayerController_C",
    SaveFunction = "RequestSaveCartelProgress",
    Advertise = "false",
    AutoStartLobby = "true",
    GamePopups = "true",
}
do
    local f = io.open((ModDir() or ".") .. "\\config.txt", "r")
    if f then
        for line in f:lines() do
            local k, v = line:match("^%s*([%w_]+)%s*=%s*(.-)%s*$")
            if k and Config[k] ~= nil and v ~= "" then Config[k] = v end
        end
        f:close()
    end
end

-- Hilfsfunktionen für Engine-Objekte
local function IsLive(obj)
    return obj ~= nil and obj:IsValid() and not obj:GetFullName():find("Default__", 1, true)
end

local function Kismet()
    return StaticFindObject("/Script/Engine.Default__KismetSystemLibrary")
end

local function MakeText(s)
    local ok, text = pcall(FText, s)
    if ok and text then return text end
    return StaticFindObject("/Script/Engine.Default__KismetTextLibrary"):Conv_StringToText(s)
end

local function ToStr(v)
    if v == nil then return nil end
    if type(v) == "string" or type(v) == "number" or type(v) == "boolean" then return tostring(v) end
    local ok, s = pcall(function() return v:ToString() end)
    return ok and s or nil
end

local function LocalController()
    for _, pc in ipairs(FindAllOf("PlayerController") or {}) do
        if IsLive(pc) and pc:IsLocalController() then return pc end
    end
    return nil
end

local function FirstLive(className)
    for _, obj in ipairs(FindAllOf(className) or {}) do
        if IsLive(obj) then return obj end
    end
    return nil
end

local function NetMode(ctx)
    if not ctx then return "loading" end
    local k = Kismet()
    if k:IsStandalone(ctx) then return "standalone" end
    if k:IsServer(ctx) then return "host" end
    return "client"
end

-- Wo ist die Instanz gerade? (an den Controller-Klassen des Spiels erkennbar)
local function Place(pc)
    if not pc then return "loading" end
    local cls = pc:GetClass():GetFName():ToString()
    if cls == "BP_MainMenuPC_C" then return "menu" end
    if cls == "BP_LobbyPC_C" then return "lobby" end
    return "world"
end

local function PlayerName(ps)
    local ok, name = pcall(function() return ps:GetPlayerName():ToString() end)
    return ok and name or "?"
end

local function PlayerPing(ps)
    local ok, ping = pcall(function() return ps.CompressedPing * 4 end)
    return ok and ping or -1
end

local function Players()
    local list = {}
    for _, ps in ipairs(FindAllOf("PlayerState") or {}) do
        if IsLive(ps) then
            table.insert(list, { name = PlayerName(ps), ping = PlayerPing(ps) })
        end
    end
    return list
end

--[[ Blueprint-Funktionen mit benannten Parametern aufrufen.
     UE4SS übergibt Parameter nur nach Position; die Reihenfolge lesen wir zur Laufzeit
     aus der UFunction. Nicht angegebene Parameter bekommen einen Standardwert. ]]
local function FindFunction(obj, name)
    local cls = obj:GetClass()
    while cls and cls:IsValid() do
        local path = cls:GetFullName():match("^%S+%s+(.+)$")
        local fn = path and StaticFindObject(path .. ":" .. name)
        if fn and fn:IsValid() then return fn end
        cls = cls:GetSuperStruct()
    end
    return nil
end

local CPF_Parm, CPF_ReturnParm = 0x80, 0x400

local function FunctionParams(fn)
    local list = {}
    fn:ForEachProperty(function(p)
        local okParm, isParm = pcall(function() return p:HasAnyPropertyFlags(CPF_Parm) end)
        if okParm and not isParm then return end
        local okRet, isRet = pcall(function() return p:HasAnyPropertyFlags(CPF_ReturnParm) end)
        if okRet and isRet then return end
        table.insert(list, { name = p:GetFName():ToString(), type = p:GetClass():GetFName():ToString(), prop = p })
    end)
    return list
end

local function Normalize(s)
    local n = (s or ""):lower():gsub("[%s_]", "")
    return n
end

local Convert

-- Blueprint-Strukturen: Felder heißen intern "Name_12_ABCDEF…"; zugeordnet wird über den Teil vor der Nummer.
-- Nicht angegebene Unterstrukturen werden aus denselben (flachen) Werten befüllt.
local function BuildStruct(prop, values, depth)
    local okS, st = pcall(function() return prop:GetStruct() end)
    if not okS or not st then return values end
    local lookup = {}
    for k, v in pairs(values) do
        if type(k) == "string" then lookup[Normalize(k)] = v end
    end
    local out = {}
    st:ForEachProperty(function(p)
        local name = p:GetFName():ToString()
        local ptype = p:GetClass():GetFName():ToString()
        local v = lookup[Normalize((name:gsub("_%d+_%x+$", "")))]
        if v ~= nil then
            out[name] = Convert(v, ptype, p, (depth or 0) + 1)
        elseif ptype == "StructProperty" and (depth or 0) < 3 then
            local nested = BuildStruct(p, values, (depth or 0) + 1)
            if next(nested) ~= nil then out[name] = nested end
        end
    end)
    return out
end

Convert = function(value, ptype, prop, depth)
    if ptype == "BoolProperty" then
        if type(value) == "string" then return value:lower() == "true" or value == "1" end
        return value and true or false
    elseif ptype == "StrProperty" then return value ~= nil and tostring(value) or ""
    elseif ptype == "NameProperty" then return FName(value ~= nil and tostring(value) or "None", EFindName.FNAME_Add)
    elseif ptype == "TextProperty" then return MakeText(value ~= nil and tostring(value) or "")
    elseif ptype:find("Int") or ptype == "ByteProperty" or ptype == "FloatProperty"
        or ptype == "DoubleProperty" or ptype == "EnumProperty" then return tonumber(value) or 0
    elseif ptype == "StructProperty" then
        if type(value) == "table" and prop and getmetatable(value) == nil then return BuildStruct(prop, value, depth) end
        return value ~= nil and value or {}
    elseif ptype == "ArrayProperty" or ptype == "MapProperty" or ptype == "SetProperty" then
        return type(value) == "table" and value or {}
    end
    return value
end

-- named: { ParamName = Wert } (Groß/Klein und Leerzeichen egal) oder Liste von Werten nach Position
local function CallNamed(obj, name, named)
    local fn = FindFunction(obj, name)
    if not fn then return false, "Funktion " .. name .. " nicht gefunden", "" end
    local params = FunctionParams(fn)
    local lookup = {}
    for k, v in pairs(named or {}) do
        if type(k) == "string" then lookup[Normalize(k)] = v end
    end
    local args, sig = {}, {}
    for i, p in ipairs(params) do
        local v = lookup[Normalize(p.name)]
        if v == nil and named then v = named[i] end
        -- Struktur-Parameter ohne eigenen Eintrag: Felder aus den übrigen benannten Werten füllen
        if v == nil and p.type == "StructProperty" and next(lookup) ~= nil then v = named end
        args[i] = Convert(v, p.type, p.prop)
        table.insert(sig, p.name .. ":" .. p.type:gsub("Property$", ""))
    end
    local ok, err = pcall(function()
        if #params == 0 then obj[name](obj) else obj[name](obj, table.unpack(args, 1, #params)) end
    end)
    return ok, err, table.concat(sig, ", ")
end

-- Speichern ------------------------------------------------------------------
local function SaveTarget()
    local fallback = nil
    for _, obj in ipairs(FindAllOf(Config.SaveObjectClass) or {}) do
        if IsLive(obj) then
            local okLocal, isLocal = pcall(function() return obj:IsLocalController() end)
            if not okLocal or isLocal then return obj end
            fallback = fallback or obj
        end
    end
    return fallback
end

local function DoSave()
    local target = SaveTarget()
    if not target then
        Event("WARN", "save: kein " .. Config.SaveObjectClass .. " – Speichern geht nur, wenn die Welt geladen ist")
        return false
    end
    local ok, err, sig = CallNamed(target, Config.SaveFunction, {})
    if ok then
        Event("INFO", "Speichern angestoßen (" .. Config.SaveFunction .. ")")
    else
        Event("ERROR", "save fehlgeschlagen: " .. tostring(err) .. " [Parameter: " .. sig .. "]")
    end
    return ok
end

-- Welt laden (Server) ----------------------------------------------------------
local function FieldText(obj, names)
    for _, n in ipairs(names) do
        local ok, v = pcall(function() return obj[n] end)
        if ok and v ~= nil then
            -- TextBlock-Widget (txtName ...) oder direkt ein String/Text/Name
            local okText, s = pcall(function() return v:GetText():ToString() end)
            if not okText then s = ToStr(v) end
            if s and s ~= "" then return s end
        end
    end
    return nil
end

local function SaveEntries()
    local list = {}
    for _, w in ipairs(FindAllOf("OneCartelWidget_C") or {}) do
        if IsLive(w) then
            table.insert(list, {
                widget = w,
                alias = FieldText(w, { "Alias", "CartelAlias", "txtAlias" }),
                name = FieldText(w, { "Cartel Custom Name", "CartelCustomName", "CustomName", "txtName" }),
            })
        end
    end
    return list
end

local function Matches(entry, world)
    local w = Normalize(world)
    return Normalize(entry.alias) == w or Normalize(entry.name) == w
end

local function LaunchSave(menuPC, alias)
    local advertise = Config.Advertise:lower() == "true"
    local ok, err, sig = CallNamed(menuPC, "RequestLaunchSavedGame",
        { CartelAlias = alias, Coop = true, Advertise = advertise, BackupFile = "" })
    if ok then
        Event("INFO", "Lade Spielstand '" .. alias .. "' als Koop-Host ...")
    else
        Event("ERROR", "Spielstand laden fehlgeschlagen: " .. tostring(err) .. " [Parameter: " .. sig .. "]")
    end
    return ok
end

-- Ablauf: wait -> menu -> direct (RequestLaunchSavedGame) -> falls nach 20 s noch im Menü:
-- ui-open ("Weiter" öffnen) -> ui-play ("Spielen" beim passenden Kartell) -> done
local autoload = { state = ROLE == "server" and WORLD ~= "" and "wait" or "off", since = 0 }

local CONTINUE_EVENT = "BndEvt__MainMenuWidget_btnContinue_K2Node_ComponentBoundEvent_8_OnButtonClickedEvent__DelegateSignature"
local PLAY_EVENT = "BndEvt__OneCartelWidget_btnCartelPlay_K2Node_ComponentBoundEvent_0_OnButtonClickedEvent__DelegateSignature"

local function SetLoadState(state)
    autoload.state, autoload.since = state, os.time()
end

local function LoadTick(pc, place)
    local s = autoload.state
    if s == "off" or s == "done" then return end
    if place ~= "menu" then
        if s ~= "wait" and place ~= "loading" then
            Event("INFO", "Hauptmenü verlassen – Welt wird geladen")
            SetLoadState("done")
        end
        return
    end
    local elapsed = os.time() - autoload.since

    if s == "wait" then
        SetLoadState("menu")
        Event("INFO", "Hauptmenü erreicht – lade Spielstand '" .. WORLD .. "'")
    elseif s == "menu" and elapsed >= 4 then -- Menü erst fertig aufbauen lassen
        LaunchSave(pc, WORLD)
        SetLoadState("direct")
    elseif s == "direct" and elapsed >= 20 then
        Event("WARN", "Direktes Laden hat nicht gewirkt – versuche es über 'Weiter' im Menü")
        local menu = FirstLive("MainMenuWidget_C")
        local ok, err = false, "MainMenuWidget nicht gefunden"
        if menu then ok, err = CallNamed(menu, CONTINUE_EVENT, {}) end
        if not ok then Event("ERROR", "'Weiter' öffnen fehlgeschlagen: " .. tostring(err)) end
        SetLoadState("ui-open")
    elseif s == "ui-open" and elapsed >= 3 then
        local found, names = nil, {}
        for _, e in ipairs(SaveEntries()) do
            table.insert(names, (e.name or "?") .. " [" .. (e.alias or "?") .. "]")
            if not found and Matches(e, WORLD) then found = e end
        end
        Event("INFO", "Spielstände im Menü: " .. (#names > 0 and table.concat(names, ", ") or "keine"))
        if not found then
            Event("WARN", "Spielstand '" .. WORLD .. "' nicht im Menü gefunden – bitte im Spielfenster von Hand laden (Koop)")
            SetLoadState("done")
            return
        end
        -- Koop-Reiter wählen (Reihenfolge unbekannt: 1 = Koop angenommen), dann "Spielen"
        local okTab = pcall(function() found.widget.CoopSwitcher:SwitchTabByIndex(1) end)
        local ok, err, sig = CallNamed(found.widget, PLAY_EVENT, {})
        Event(ok and "INFO" or "ERROR", ok and ("'Spielen' bei '" .. (found.name or WORLD) .. "' ausgelöst" .. (okTab and "" or " (Koop-Reiter nicht umgeschaltet)"))
            or ("'Spielen' fehlgeschlagen: " .. tostring(err) .. " [Parameter: " .. sig .. "]"))
        SetLoadState("ui-play")
    elseif s == "ui-play" and elapsed >= 20 then
        Event("WARN", "Welt startet nicht automatisch – bitte im Spielfenster von Hand laden (Koop)")
        SetLoadState("done")
    end
end

-- Lobby (Host startet das Spiel) -----------------------------------------------
local lobby = { since = nil, tried = false }

local function DoStart()
    local pc = FirstLive("BP_LobbyPC_C")
    if not pc or not pc:IsLocalController() then
        pc = LocalController()
    end
    if not pc or Place(pc) ~= "lobby" then
        Event("WARN", "start: die Instanz ist nicht in der Lobby")
        return
    end
    local ok, err, sig = CallNamed(pc, "RequestStartGame", {})
    Event(ok and "INFO" or "ERROR", ok and "Spielstart angefordert" or ("start fehlgeschlagen: " .. tostring(err) .. " [Parameter: " .. sig .. "]"))
end

local function LobbyTick(place)
    if place ~= "lobby" then
        lobby.since, lobby.tried = nil, false
        return
    end
    if not lobby.since then
        lobby.since = os.time()
        Event("INFO", "Lobby erreicht. Mitspieler können jetzt beitreten.")
    end
    if ROLE == "server" and Config.AutoStartLobby:lower() == "true" and not lobby.tried and os.time() - lobby.since >= 8 then
        lobby.tried = true
        DoStart()
    end
end

-- Weitere Befehle -------------------------------------------------------------
local function DoKick(name)
    name = (name or ""):lower()
    if name == "" then
        Event("WARN", "kick: Spielername fehlt")
        return
    end
    for _, pc in ipairs(FindAllOf("PlayerController") or {}) do
        if IsLive(pc) and not pc:IsLocalController() and IsLive(pc.PlayerState)
            and PlayerName(pc.PlayerState):lower() == name then
            local ok, err = pcall(function()
                pc:ClientReturnToMainMenuWithTextReason(MakeText("Du wurdest vom Server entfernt."))
            end)
            Event(ok and "INFO" or "ERROR", ok and ("Spieler gekickt: " .. name) or ("kick fehlgeschlagen: " .. tostring(err)))
            return
        end
    end
    Event("WARN", "kick: kein verbundener Spieler namens '" .. name .. "'")
end

local function Quit()
    local pc = LocalController()
    if pc then Kismet():ExecuteConsoleCommand(pc, "quit", pc) end
end

local function DoStop()
    local saving = DoSave()
    Event("INFO", saving and "Server wird in 10 Sekunden beendet (Speichern läuft)" or "Server wird beendet")
    if saving then
        ExecuteWithDelay(10000, function() ExecuteInGameThread(Quit) end)
    else
        Quit()
    end
end

-- Diagnose: "call <Klasse> <Funktion> [Werte...]" und "dump <Klasse>"
local function DoCall(arg)
    local className, fname, rest = arg:match("^(%S+)%s+(%S+)%s*(.-)$")
    if not className then
        Event("WARN", "call: Benutzung call <Klasse> <Funktion> [Werte ...]")
        return
    end
    local obj = FirstLive(className)
    if not obj then
        Event("WARN", "call: kein Objekt der Klasse " .. className)
        return
    end
    local values = {}
    for v in rest:gmatch("%S+") do table.insert(values, v) end
    local ok, err, sig = CallNamed(obj, fname, values)
    Event(ok and "INFO" or "ERROR", "call " .. className .. ":" .. fname .. "(" .. sig .. ") -> " .. (ok and "ok" or tostring(err)))
end

local function DoDump(className)
    local obj = FirstLive(className)
    if not obj then
        Event("WARN", "dump: kein Objekt der Klasse " .. tostring(className))
        return
    end
    local out = {}
    local cls = obj:GetClass()
    while cls and cls:IsValid() do
        table.insert(out, "== " .. cls:GetFullName())
        pcall(function()
            cls:ForEachFunction(function(fn)
                local ok, params = pcall(FunctionParams, fn)
                local sig = {}
                for _, p in ipairs(ok and params or {}) do table.insert(sig, p.name .. ":" .. p.type) end
                table.insert(out, "  fn " .. fn:GetFName():ToString() .. "(" .. table.concat(sig, ", ") .. ")")
            end)
        end)
        cls:ForEachProperty(function(p)
            local val = ""
            local okVal, v = pcall(function() return obj[p:GetFName():ToString()] end)
            if okVal then val = ToStr(v) or "" end
            table.insert(out, "  " .. p:GetClass():GetFName():ToString() .. " " .. p:GetFName():ToString() .. " = " .. val)
        end)
        if cls:GetFName():ToString() == "Object" then break end
        cls = cls:GetSuperStruct()
    end
    local path = (IPC_DIR or ModDir() or ".") .. "\\dump-" .. className .. ".txt"
    local f = io.open(path, "w")
    if f then
        f:write(table.concat(out, "\n") .. "\n")
        f:close()
    end
    Event("INFO", "dump geschrieben: " .. path)
end

local function HandleCommand(line)
    local cmd, arg = line:match("^%s*(%S+)%s*(.-)%s*$")
    if not cmd then return end
    cmd = cmd:lower()
    if cmd == "save" then DoSave()
    elseif cmd == "kick" then DoKick(arg)
    elseif cmd == "stop" then DoStop()
    elseif cmd == "start" then DoStart()
    elseif cmd == "load" then
        if arg ~= "" then WORLD = arg end
        SetLoadState(WORLD ~= "" and "wait" or "off")
        Event("INFO", "Lade Welt '" .. WORLD .. "', sobald das Hauptmenü erreicht ist")
    elseif cmd == "quit" then
        -- Launcher startet das Spiel neu (z. B. neues Server-Passwort)
        Event("INFO", "Spiel wird auf Wunsch von DDS2 Multiplayer beendet")
        Quit()
    elseif cmd == "call" then DoCall(arg)
    elseif cmd == "dump" then DoDump(arg)
    else Event("WARN", "Unbekannter Befehl: " .. cmd) end
end

-- Befehlsdatei abfragen: nur neu angehängte, vollständige Zeilen lesen
local commandPos = 0
local function PollCommands()
    local f = io.open(IPC_DIR .. "\\commands.txt", "rb")
    if not f then return end
    local size = f:seek("end")
    if size < commandPos then commandPos = 0 end
    f:seek("set", commandPos)
    local data = f:read("a") or ""
    f:close()
    local lastNewline = data:match(".*()\n")
    if not lastNewline then return end
    commandPos = commandPos + lastNewline
    for line in data:sub(1, lastNewline):gmatch("([^\r\n]+)") do
        ExecuteInGameThread(function()
            local ok, err = pcall(HandleCommand, line)
            if not ok then Event("ERROR", "Befehl '" .. line .. "': " .. tostring(err)) end
        end)
    end
end

local function WriteStatus(pc, mode, place)
    local lines = {
        "version=" .. MOD_VERSION,
        "role=" .. ROLE,
        "mode=" .. mode,
        "place=" .. place,
        "time=" .. os.time(),
    }
    if mode ~= "loading" then
        -- eigener Spieler (beim Server der Host-Charakter), damit der Launcher ihn nicht als Mitspieler zählt
        local okSelf, selfName = pcall(function() return PlayerName(pc.PlayerState) end)
        if okSelf and selfName and selfName ~= "?" then table.insert(lines, "self=" .. selfName) end
        for _, p in ipairs(Players()) do
            table.insert(lines, "player=" .. p.name .. "|" .. p.ping)
        end
    end
    local f = io.open(IPC_DIR .. "\\status.txt", "w")
    if f then
        f:write(table.concat(lines, "\n") .. "\n")
        f:close()
    end
end

-- Menü "Server beitreten" ------------------------------------------------------
local okMenu, Menu = pcall(require, "menu")
if okMenu then
    local okInit, err = pcall(Menu.Init, {
        Log = Log, Event = Event, IsLive = IsLive, Kismet = Kismet, MakeText = MakeText,
        LocalController = LocalController, DefaultPort = DEFAULT_PORT,
        DirectIpActive = IPC_DIR ~= nil, IpcDir = IPC_DIR, ToStr = ToStr,
        CallNamed = CallNamed, GamePopups = Config.GamePopups ~= "false",
    })
    if not okInit then
        Log("Menü deaktiviert: " .. tostring(err))
        Menu = nil
    end
else
    Log("menu.lua nicht geladen: " .. tostring(Menu))
    Menu = nil
end

-- Fehler-Hooks (BlueprintImplementableEvents der GameInstance)
local function TryHook(path, fn)
    local ok, err = pcall(RegisterHook, path, fn)
    if not ok then Log("Hook fehlgeschlagen: " .. path .. " (" .. tostring(err) .. ")") end
end

TryHook("/Script/Engine.GameInstance:HandleNetworkError", function(_, failureType, isServer)
    local id = failureType:get()
    Event("NETERROR", (NetworkFailures[id] or tostring(id)) .. (isServer:get() and " (server)" or ""))
    if Menu then Menu.ShowError("Verbindungsfehler: " .. (NetworkFailures[id] or tostring(id))) end
end)

TryHook("/Script/Engine.GameInstance:HandleTravelError", function(_, failureType)
    local id = failureType:get()
    Event("TRAVELERROR", TravelFailures[id] or tostring(id))
    if Menu then Menu.ShowError("Verbindungsfehler: " .. (TravelFailures[id] or tostring(id))) end
end)

Log("Version " .. MOD_VERSION .. ", Rolle " .. ROLE .. ", IPC " .. tostring(IPC_DIR) .. ", Welt '" .. WORLD .. "'")

local function Tick()
    local pc = LocalController()
    local place = Place(pc)
    if Menu then
        local ok, err = pcall(Menu.Tick)
        if not ok then Log("Menü: " .. tostring(err)) end
    end
    if not IPC_DIR then return end
    WriteStatus(pc, NetMode(pc), place)
    LoadTick(pc, place)
    LobbyTick(place)
end

if IPC_DIR then
    Event("INFO", "DDS2Net " .. MOD_VERSION .. " geladen (" .. ROLE .. ")")
    LoopAsync(500, function()
        local ok, err = pcall(PollCommands)
        if not ok then Log("PollCommands: " .. tostring(err)) end
        return false
    end)
end

LoopAsync(2000, function()
    ExecuteInGameThread(function()
        local ok, err = pcall(Tick)
        if not ok then Log("Tick: " .. tostring(err)) end
    end)
    return false
end)

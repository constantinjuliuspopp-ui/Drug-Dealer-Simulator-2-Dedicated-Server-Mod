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
-- Über DDS2 Multiplayer / DDS2 Server gestartet: Umgebungsvariablen gesetzt. Beim normalen Start über Steam
-- übernimmt ein unsichtbarer Helfer (DDS2Mod.exe, Agent-Modus) den Austausch über einen festen Ordner.
local LAUNCHED = os.getenv("DDS2_IPC_DIR") ~= nil
local IPC_DIR = os.getenv("DDS2_IPC_DIR") or ((os.getenv("APPDATA") or ".") .. "\\DDS2Mod\\ipc-game")
local ROLE = os.getenv("DDS2_ROLE") or "client"
local WORLD = os.getenv("DDS2_WORLD") or ""
local DEFAULT_PORT = tonumber(os.getenv("DDS2_DEFAULT_PORT") or "") or 7777
-- Server: Bilder bzw. Spiel-Takte pro Sekunde begrenzen (ohne Grafik rechnet das Spiel sonst ungebremst)
local MAX_FPS = tonumber(os.getenv("DDS2_MAX_FPS") or "")

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
    IpHosting = "false",
    CustomIntro = "true",
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

local Menu -- menu.lua, wird weiter unten geladen

local CPF_Parm, CPF_ReturnParm = 0x80, 0x400

local function FunctionParams(fn)
    local list = {}
    local inLocals = false
    fn:ForEachProperty(function(p)
        if inLocals then return end
        local name = p:GetFName():ToString()
        local okParm, isParm = pcall(function() return p:HasAnyPropertyFlags(CPF_Parm) end)
        if okParm and not isParm then return end
        local okRet, isRet = pcall(function() return p:HasAnyPropertyFlags(CPF_ReturnParm) end)
        if okRet and isRet then return end
        -- Ohne Property-Flags (manche UE4SS-Versionen): Parameter stehen vorn, danach kommen die
        -- lokalen Blueprint-Variablen (CallFunc_…, K2Node_…, Temp_…) – ab da abbrechen.
        if not okParm and (name:find("^CallFunc_") or name:find("^K2Node_") or name:find("^Temp_")) then
            inLocals = true
            return
        end
        table.insert(list, { name = name, type = p:GetClass():GetFName():ToString(), prop = p })
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
    -- SHUTDOWN: der Launcher/Server weiß dann, dass das Ende gewollt ist (kein Absturz)
    Event("SHUTDOWN", saving and "Server wird in 10 Sekunden beendet (Speichern läuft)" or "Server wird beendet")
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

-- Diagnose "pawns": alle Spielfiguren mit Spielername, Sichtbarkeit und Komponenten (für Namensschilder usw.)
local function DoPawns()
    local out = {}
    for _, pawn in ipairs(FindAllOf("Character") or {}) do
        if IsLive(pawn) then
            local name = "?"
            pcall(function() local ps = pawn.PlayerState; if ps and ps:IsValid() then name = PlayerName(ps) end end)
            local hiddenFlag, z = "?", "?"
            pcall(function() hiddenFlag = tostring(pawn.bHidden) end)
            pcall(function() z = string.format("%.0f", pawn:K2_GetActorLocation().Z) end)
            table.insert(out, pawn:GetClass():GetFName():ToString() .. "  Spieler=" .. name .. "  bHidden=" .. hiddenFlag .. "  Z=" .. z)
            local id = pawn:GetAddress()
            for _, cls in ipairs({ "WidgetComponent", "TextRenderComponent", "AIPerceptionStimuliSourceComponent" }) do
                for _, comp in ipairs(FindAllOf(cls) or {}) do
                    local ok, owner = pcall(function() return comp:GetOwner() end)
                    if ok and owner and owner:IsValid() and owner:GetAddress() == id then
                        table.insert(out, "    " .. cls .. " " .. comp:GetFName():ToString())
                    end
                end
            end
        end
    end
    local path = (IPC_DIR or ModDir() or ".") .. "\\pawns.txt"
    local f = io.open(path, "w")
    if f then f:write(table.concat(out, "\n") .. "\n") f:close() end
    Event("INFO", "pawns: " .. #out .. " Zeilen -> " .. path)
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
        Event("SHUTDOWN", "Spiel wird auf Wunsch von DDS2 Multiplayer beendet")
        Quit()
    elseif cmd == "menu" then
        if Menu then Menu.Command(arg) else Event("WARN", "menu: Menü nicht geladen") end
    elseif cmd == "call" then DoCall(arg)
    elseif cmd == "dump" then DoDump(arg)
    elseif cmd == "pawns" then DoPawns()
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
        local ok, err = pcall(HandleCommand, line)
        if not ok then Event("ERROR", "Befehl '" .. line .. "': " .. tostring(err)) end
    end
end

-- Regelmäßig im Spiel-Thread ausführen. LoopInGameThreadWithDelay vermeidet den Umweg
-- LoopAsync → ExecuteInGameThread, der im Test zeitweise alle Schleifen der Mod angehalten hat.
local function GameLoop(ms, name, fn)
    local function Step()
        local ok, err = pcall(fn)
        if not ok then Log(name .. ": " .. tostring(err)) end
        return false
    end
    if LoopInGameThreadWithDelay then
        LoopInGameThreadWithDelay(ms, Step)
    else
        LoopAsync(ms, function() ExecuteInGameThread(Step) return false end)
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

-- Helfer für Ping/Prüfung/Favoriten: beim Start über den Launcher ist das der Launcher selbst,
-- sonst der unsichtbare Agent (schreibt alle 0,5 s seine Uhrzeit in agent.txt).
local function HelperAlive()
    if LAUNCHED then return true end
    local f = io.open(IPC_DIR .. "\\agent.txt", "r")
    if not f then return false end
    local t = tonumber(f:read("a") or "")
    f:close()
    return t ~= nil and math.abs(os.time() - t) < 10
end

-- Normaler Start: Agent einmal über das vom Installer registrierte Protokoll dds2mod:// starten (ohne Fenster).
local agentStarted = false
local function StartAgent(pc)
    if LAUNCHED or agentStarted or not pc then return end
    agentStarted = true
    if HelperAlive() then return end
    local ok, err = pcall(function() Kismet():LaunchURL("dds2mod://agent") end)
    Log(ok and "Hintergrund-Helfer gestartet (dds2mod://agent)" or ("Hintergrund-Helfer nicht gestartet: " .. tostring(err)))
end

-- Menü "Server beitreten" ------------------------------------------------------
local okMenu
okMenu, Menu = pcall(require, "menu")
if okMenu then
    local okInit, err = pcall(Menu.Init, {
        Log = Log, Event = Event, IsLive = IsLive, Kismet = Kismet, MakeText = MakeText,
        LocalController = LocalController, DefaultPort = DEFAULT_PORT,
        IpcDir = IPC_DIR, ToStr = ToStr, HelperAlive = HelperAlive,
        CallNamed = CallNamed, GameLoop = GameLoop,
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

local lastNetError = 0
local function OnNetworkError(failureType, isServer)
    local id = failureType:get()
    -- Basis- und Blueprint-Hook können beide auslösen: nur einmal melden
    if os.time() - lastNetError < 2 then return end
    lastNetError = os.time()
    local okServer, server = pcall(function() return isServer:get() end)
    Event("NETERROR", (NetworkFailures[id] or tostring(id)) .. (okServer and server and " (server)" or ""))
    if Menu then Menu.ShowError("Verbindungsfehler: " .. (NetworkFailures[id] or tostring(id))) end
end

TryHook("/Script/Engine.GameInstance:HandleNetworkError", function(_, failureType, isServer) OnNetworkError(failureType, isServer) end)
-- Das Spiel überschreibt HandleNetworkError in seinem Blueprint – dann löst nur dieser Hook aus.
TryHook("/Game/Blueprints/SystemClasses/BP_DDS2_GameInstance.BP_DDS2_GameInstance_C:HandleNetworkError",
    function(_, failureType, isServer) OnNetworkError(failureType, isServer) end)

TryHook("/Script/Engine.GameInstance:HandleTravelError", function(_, failureType)
    local id = failureType:get()
    Event("TRAVELERROR", TravelFailures[id] or tostring(id))
    if Menu then Menu.ShowError("Verbindungsfehler: " .. (TravelFailures[id] or tostring(id))) end
end)

Log("Version " .. MOD_VERSION .. ", Rolle " .. ROLE .. ", IPC " .. tostring(IPC_DIR) .. ", Welt '" .. WORLD .. "'")

--[[ Direkt-IP beim Hosten: Das Spiel hostet über den SteamNetDriver (Steam-P2P, kein UDP-Spielport).
     Der SteamNetDriver arbeitet als Server nur dann über normales IP ("Passthrough", lauscht auf dem Port aus
     der URL, Standard 7777), wenn die Listen-URL die Option bIsLanMatch enthält. Clients, die mit einer IP-Adresse
     verbinden, nutzen das automatisch. Darum hier: an jeden Level-Aufruf mit "listen" ?bIsLanMatch anhängen.
     (Den Treiber gegen IpNetDriver zu tauschen bricht den Koop-Start des Spiels ab – im Test ausprobiert.) ]]
local function WithLanOption(s)
    if type(s) ~= "string" then return nil end
    local lower = s:lower()
    if not lower:find("listen", 1, true) or lower:find("bislanmatch", 1, true) then return nil end
    return s .. "?bIsLanMatch"
end

local function PatchParam(param, what)
    local ok, value = pcall(function() return param:get():ToString() end)
    if not ok then return end
    local patched = WithLanOption(value)
    if not patched then return end
    local okSet, err = pcall(function() param:set(patched) end)
    if okSet then
        Event("INFO", "Direkt-IP: Hosten über IP (" .. what .. " '" .. value .. "' -> '" .. patched .. "')")
    else
        Event("ERROR", "Direkt-IP: " .. what .. " nicht angepasst (" .. tostring(err) .. ")")
    end
end

-- Über DDS2 Multiplayer / DDS2 Server gestartet: immer über IP hosten. Bei normalem Start über Steam nur mit
-- IpHosting=true in config.txt – sonst blieben Steam-Einladungen und Steam-Koop wie gewohnt (die gehen nur über Steam-P2P).
if LAUNCHED or Config.IpHosting == "true" then
    -- UGameplayStatics::OpenLevel(WorldContextObject, LevelName, bAbsolute, Options)
    TryHook("/Script/Engine.GameplayStatics:OpenLevel", function(_, _, _, _, options) PatchParam(options, "OpenLevel") end)
    TryHook("/Script/Engine.GameplayStatics:OpenLevelBySoftObjectPtr", function(_, _, _, _, options) PatchParam(options, "OpenLevel") end)
    -- Konsolenbefehl "open Karte?listen" bzw. "servertravel …"
    TryHook("/Script/Engine.KismetSystemLibrary:ExecuteConsoleCommand", function(_, _, command) PatchParam(command, "Befehl") end)
end

local appliedFpsPlace = nil
local function ApplyMaxFps(pc, place)
    if not MAX_FPS or MAX_FPS <= 0 or not pc or place == appliedFpsPlace then return end
    appliedFpsPlace = place
    local ok, err = pcall(function() Kismet():ExecuteConsoleCommand(pc, "t.MaxFPS " .. MAX_FPS, pc) end)
    if ok then Log("t.MaxFPS " .. MAX_FPS .. " gesetzt (" .. place .. ")") else Log("t.MaxFPS: " .. tostring(err)) end
end

--[[ Intro beim Spielstart (/Game/GUI/Intro/GameIntroWidget): Logo durch Images\intro_logo.png ersetzen
     ("DEDICATED SERVER") und bei "Developer" zusätzlich "Prime Mods" anzeigen. Das Widget zeigt die Titelkarten
     nacheinander aus seiner Liste SubList (BP_Struct_IntroTitle: Title/Name) – darum wird dort der Name ergänzt;
     als Rückfall wird die gerade angezeigte Karte (GameIntroTitle: txtTitle/txtNam) direkt geändert.
     Abschaltbar mit CustomIntro=false in config.txt. Auf dem Server (ohne Bildschirm) aus. ]]
local INTRO_LOGO_FILE = (ModDir() or ".") .. "\\Images\\intro_logo.png"
local INTRO_EXTRA_NAME = "Prime Mods"
local INTRO_POLL_SECONDS = 300
local SUB_TITLE = "Title_4_4E36C01742255A677CB9B8A36096FB31"
local SUB_NAME = "Name_3_843BA214497695D10BDD35A7381A9D04"
local intro = { started = os.time(), widget = nil, logo = false, list = false, cards = {} }

local function IsDeveloperTitle(s)
    s = (s or ""):lower():gsub("^%s+", ""):gsub("%s+$", "")
    return s == "developer" or s == "developers" or s == "entwickler"
end

local function WithExtraName(s)
    s = s or ""
    if s:find(INTRO_EXTRA_NAME, 1, true) then return nil end
    return (s == "" and "" or (s .. "\n")) .. INTRO_EXTRA_NAME
end

-- Logo-Bild laden und ins Image "GameLogo" setzen; Seitenverhältnis des Bildes behalten (in den alten Rahmen einpassen).
local function ReplaceIntroLogo(widget)
    local okImg, img = pcall(function() return widget.GameLogo end)
    if not okImg or not img or not img:IsValid() then return false end
    local f = io.open(INTRO_LOGO_FILE, "rb")
    if not f then
        Event("WARN", "Intro: Bild fehlt (" .. INTRO_LOGO_FILE .. ")")
        return true -- nicht weiter versuchen
    end
    f:close()
    local lib = StaticFindObject("/Script/Engine.Default__KismetRenderingLibrary")
    if not lib or not lib:IsValid() then return false end
    local okTex, tex = pcall(function() return lib:ImportFileAsTexture2D(widget, INTRO_LOGO_FILE) end)
    if not okTex or not tex or not tex:IsValid() then
        Event("WARN", "Intro: Bild nicht geladen (" .. tostring(tex) .. ")")
        return true
    end
    local okSet, errSet = pcall(function() img:SetBrushFromTexture(tex, false) end)
    if not okSet then
        Event("WARN", "Intro: Logo nicht gesetzt (" .. tostring(errSet) .. ")")
        return true
    end
    -- Rahmen (CanvasPanelSlot) an das Seitenverhältnis anpassen, Mitte bleibt gleich
    pcall(function()
        local tw, th = tex:Blueprint_GetSizeX(), tex:Blueprint_GetSizeY()
        local slot = img.Slot
        if not (tw > 0 and th > 0 and slot and slot:IsValid()) then return end
        if not slot:GetFullName():find("CanvasPanelSlot", 1, true) then return end
        local anchors = slot:GetAnchors()
        if anchors.Minimum.X ~= anchors.Maximum.X or anchors.Minimum.Y ~= anchors.Maximum.Y then return end
        local size, pos, align = slot:GetSize(), slot:GetPosition(), slot:GetAlignment()
        local w, h = size.X, size.Y
        if not (w > 0 and h > 0) then return end
        local scale = math.min(w / tw, h / th)
        local nw, nh = tw * scale, th * scale
        slot:SetSize({ X = nw, Y = nh })
        slot:SetPosition({ X = pos.X + (0.5 - align.X) * (w - nw), Y = pos.Y + (0.5 - align.Y) * (h - nh) })
    end)
    Event("INFO", "Intro: Logo ersetzt (" .. INTRO_LOGO_FILE:match("[^/\\]+$") .. ")")
    return true
end

-- In der Titelliste bei "Developer" den Namen ergänzen. true = erledigt (oder nicht möglich).
local function ExtendIntroList(widget)
    local okList, list = pcall(function() return widget.SubList end)
    if not okList or not list then return false end
    local okNum, num = pcall(function() return #list end)
    if not okNum or num == 0 then return false end
    for i = 1, num do
        local okTitle, title = pcall(function() return list[i][SUB_TITLE]:ToString() end)
        if okTitle and IsDeveloperTitle(title) then
            local okName, name = pcall(function() return list[i][SUB_NAME]:ToString() end)
            local new = WithExtraName(okName and name or "")
            if not new then return true end
            pcall(function() list[i][SUB_NAME] = MakeText(new) end)
            local okBack, back = pcall(function() return list[i][SUB_NAME]:ToString() end)
            if okBack and back == new then
                Event("INFO", "Intro: '" .. INTRO_EXTRA_NAME .. "' bei '" .. title .. "' ergänzt")
            else
                Log("Intro: Titelliste nicht änderbar – ändere die angezeigte Karte")
            end
            return true
        end
    end
    Log("Intro: keine Karte 'Developer' in der Titelliste (" .. num .. " Einträge)")
    return true
end

-- Rückfall: angezeigte Titelkarte direkt ändern
local function ExtendShownCards()
    for _, card in ipairs(FindAllOf("GameIntroTitle_C") or {}) do
        if IsLive(card) then
            local id = card:GetAddress()
            if not intro.cards[id] then
                local okTitle, title = pcall(function() return card.txtTitle:GetText():ToString() end)
                if okTitle and title and title ~= "" then
                    -- andere Karten merken; "Developer" jedes Mal prüfen (das Spiel könnte den Namen neu setzen)
                    if not IsDeveloperTitle(title) then
                        intro.cards[id] = true
                    else
                        local okName, name = pcall(function() return card.txtNam:GetText():ToString() end)
                        local new = WithExtraName(okName and name or "")
                        if new and pcall(function() card.txtNam:SetText(MakeText(new)) end) then
                            Event("INFO", "Intro: '" .. INTRO_EXTRA_NAME .. "' auf der Karte '" .. title .. "' ergänzt")
                        end
                    end
                end
            end
        end
    end
end

local function IntroTick()
    if os.time() - intro.started > INTRO_POLL_SECONDS then return end
    local widget = FindFirstOf("GameIntroWidget_C")
    if IsLive(widget) then
        local id = widget:GetAddress()
        if intro.widget ~= id then
            intro.widget, intro.logo, intro.list, intro.cards = id, false, false, {}
            Log("Intro gefunden")
        end
        if not intro.logo then intro.logo = ReplaceIntroLogo(widget) end
        if not intro.list then intro.list = ExtendIntroList(widget) end
        ExtendShownCards()
    end
end

if ROLE ~= "server" and Config.CustomIntro ~= "false" then
    GameLoop(100, "Intro", IntroTick)
end

--[[ Dedicated Server: die Figur des Server-Kontos verstecken.
     Das Spiel kennt nur "ein Spieler hostet" – der Server hat deshalb eine eigene Figur in der Welt.
     Sie wird unsichtbar (bHidden wird an alle Mitspieler repliziert, damit auch ihr Namensschild), für Spieler,
     Fahrzeuge, Kamera und Sichtlinien durchlässig und bei der KI-Wahrnehmung abgemeldet, damit NPCs und Polizei sie
     nicht beachten. Sie bleibt an ihrem Platz: Das Spiel setzt neue Mitspieler neben den Host – in 0.5.3/0.5.4 fiel die
     geparkte Figur ohne Kollision ins Meer, und Mitspieler erschienen unter Wasser. Wird alle 2 s geprüft. ]]
local HIDE_HOST = ROLE == "server" and os.getenv("DDS2_HIDE_HOST") == "1"
local hidden = { id = nil, badSince = nil }

local function Addr(o)
    local ok, a = pcall(function() return o:GetAddress() end)
    return ok and a or nil
end

local function UnregisterPerception(pawn)
    local id, count = Addr(pawn), 0
    for _, comp in ipairs(FindAllOf("AIPerceptionStimuliSourceComponent") or {}) do
        if IsLive(comp) then
            local okOwner, owner = pcall(function() return comp:GetOwner() end)
            if okOwner and owner and owner:IsValid() and Addr(owner) == id then
                if pcall(function() comp:UnregisterFromPerceptionSystem() end) then count = count + 1 end
            end
        end
    end
    return count
end

-- Kollisionskanäle (ECollisionChannel) und Antworten (ECollisionResponse)
local ECC_PAWN, ECC_VISIBILITY, ECC_CAMERA, ECC_VEHICLE = 2, 3, 4, 6
local ECR_IGNORE = 0
local MOVE_FALLING, MOVE_SWIMMING = 3, 4

-- Figur für Spieler, Fahrzeuge, Kamera und Sichtlinien "durchlässig" machen – der Boden (WorldStatic) hält sie weiter.
local function IgnoreOthers(pawn)
    for _, name in ipairs({ "CapsuleComponent", "Mesh" }) do
        pcall(function()
            local comp = pawn[name]
            if comp and comp:IsValid() then
                for _, ch in ipairs({ ECC_PAWN, ECC_VISIBILITY, ECC_CAMERA, ECC_VEHICLE }) do
                    comp:SetCollisionResponseToChannel(ch, ECR_IGNORE)
                end
            end
        end)
    end
end

local function HideHostCharacter(pc, place)
    if not HIDE_HOST or place ~= "world" or not pc then return end
    local okPawn, pawn = pcall(function() return pc.Pawn end)
    if not okPawn or not pawn or not pawn:IsValid() then return end
    local id = Addr(pawn)
    local first = hidden.id ~= id
    if first then
        hidden.id = id
        hidden.badSince = nil
        -- Reste aus 0.5.3/0.5.4 (Kollision aus, Schwerkraft 0, Bewegung aus) zurücknehmen
        pcall(function() pawn:SetActorEnableCollision(true) end)
        pcall(function()
            local mv = pawn.CharacterMovement
            mv.GravityScale = 1.0
            mv:SetMovementMode(1, 0) -- MOVE_Walking
        end)
    end
    -- jedes Mal setzen (billig), falls das Spiel es zurücksetzt; bHidden wird an alle Mitspieler übertragen
    pcall(function() pawn:SetActorHiddenInGame(true) end)
    IgnoreOthers(pawn)

    -- Rettung: im Wasser oder länger im freien Fall (z. B. Position aus 0.5.3/0.5.4 gespeichert) -> zum Startpunkt.
    -- Neue Mitspieler erscheinen bei der Figur des Hosts.
    local okMode, mode = pcall(function() return pawn.CharacterMovement.MovementMode end)
    if okMode and (mode == MOVE_FALLING or mode == MOVE_SWIMMING) then
        hidden.badSince = hidden.badSince or os.time()
        if os.time() - hidden.badSince >= 5 then
            local start = FindFirstOf("PlayerStart")
            if IsLive(start) then
                local okLoc, loc = pcall(function() return start:K2_GetActorLocation() end)
                if okLoc and pcall(function() pawn:K2_SetActorLocation({ X = loc.X, Y = loc.Y, Z = loc.Z + 100 }, false, {}, true) end) then
                    Event("WARN", "Server-Figur war im Wasser/freien Fall – zum Startpunkt der Karte zurückgesetzt")
                end
            end
            hidden.badSince = nil
        end
    else
        hidden.badSince = nil
    end

    if first then
        local perception = UnregisterPerception(pawn)
        Event("INFO", "Server-Figur versteckt (unsichtbar, für Spieler durchlässig"
            .. (perception > 0 and ", für NPCs/Polizei abgemeldet" or ", keine KI-Wahrnehmung gefunden") .. ")")
    end
end

-- Mitspieler: versteckte Figuren auch lokal durchlässig machen und ihre Namensschilder (WidgetComponents)
-- ausblenden. bHidden überträgt das Spiel vom Server; Kollision und Namensmarkierung rechnet jeder Client selbst.
local hiddenWidgets, passthrough = {}, {}
local function HideMarkersOfHiddenPawns(place)
    if ROLE == "server" or place ~= "world" then return end
    for _, pawn in ipairs(FindAllOf("Character") or {}) do
        if IsLive(pawn) then
            local id = pawn:GetAddress()
            if not passthrough[id] then
                local okHidden, isHidden = pcall(function() return pawn.bHidden end)
                if okHidden and isHidden == true then
                    IgnoreOthers(pawn)
                    passthrough[id] = true
                end
            end
        end
    end
    for _, comp in ipairs(FindAllOf("WidgetComponent") or {}) do
        if IsLive(comp) then
            local id = comp:GetAddress()
            if not hiddenWidgets[id] then
                local okOwner, owner = pcall(function() return comp:GetOwner() end)
                if okOwner and owner and owner:IsValid() then
                    local okHidden, isHidden = pcall(function() return owner.bHidden end)
                    if okHidden and isHidden == true then
                        pcall(function() comp:SetVisibility(false, true) end)
                        pcall(function() comp:SetHiddenInGame(true, true) end)
                        hiddenWidgets[id] = true
                        Log("Namensschild einer versteckten Figur ausgeblendet")
                    end
                end
            end
        end
    end
end

local function Tick()
    local pc = LocalController()
    local place = Place(pc)
    StartAgent(pc)
    ApplyMaxFps(pc, place)
    local okHide, errHide = pcall(HideHostCharacter, pc, place)
    if not okHide then Log("Server-Figur: " .. tostring(errHide)) end
    local okMarkers, errMarkers = pcall(HideMarkersOfHiddenPawns, place)
    if not okMarkers then Log("Namensschilder: " .. tostring(errMarkers)) end
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
    GameLoop(500, "PollCommands", PollCommands)
end

GameLoop(2000, "Tick", Tick)

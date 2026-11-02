--[[
    Menü "Server beitreten" im Hauptmenü des Spiels – im Design des Spiels.

    Nichts davon ist eine eigene Grafik: Stil, Schrift, Farben und Klänge werden zur Laufzeit von den
    echten Widgets des Hauptmenüs kopiert (MainMenuWidget_C):
      btnJoinCartel           Vorlage für den neuen Menüpunkt "SERVER BEITRETEN" (wird direkt dahinter eingefügt)
      btnCartelApply/-Cancel  Vorlage für die Knöpfe im Fenster (wie im Fenster "Neues Kartell")
      InputSessionSearch      Vorlage für das Adressfeld, InputPassword für das Passwortfeld
      JoinHolder …            Vorlage für den Fensterrahmen
    Fehlermeldungen stehen im Fenster (Fehlerton des Spiels). Das Popup des Spiels
    (BlueprintHelpersLib:DisplayConfirmWidget) lässt sich aus Lua nicht aufrufen – das Spiel stürzt ab.
    Klappt eine Vorlage nicht, gibt es einen schlichten Ersatz; ganz ohne Hauptmenü-Knöpfe erscheint
    der Menüpunkt oben rechts. Taste F8 öffnet/schließt immer.

    Vor dem Verbinden fragt das Menü DDS2 Multiplayer (Launcher) über den IPC-Ordner:
      join-request.txt   Mod -> Launcher   id|host|port|passwort
      join-response.txt  Launcher -> Mod   id|ok|noanswer|password|restart|error|Text
    Favoriten mit Ping/Spielerzahl kommen aus %APPDATA%\DDS2Mod\favorites.txt (schreibt der Launcher):
      Name|Host|Port|Ping|Spieler|Max|Zustand
]]

local M = {}

local ctx           -- Hilfsfunktionen aus main.lua
local ui            -- Widgets der aktuellen Hauptmenü-Instanz
local isOpen = false
local lastStatus
local counter = 0
local pending       -- laufende Anfrage an den Launcher
local forceTarget   -- nach "antwortet nicht": erneuter Klick verbindet ohne Prüfung
local requestCounter = 0
local anim          -- Einblend-Animation
local warned = {}
local favText = nil
local lastFavRead = 0

local MAX_FAVORITES = 8
local REQUEST_TIMEOUT = 8
local VISIBLE, COLLAPSED, SELF_HIT_TEST_INVISIBLE = 0, 1, 4
local COLOR_ERROR = { R = 0.95, G = 0.32, B = 0.25, A = 1.0 }
local COLOR_TEXT = { R = 0.92, G = 0.92, B = 0.92, A = 1.0 }
local COLOR_DIM = { R = 0.55, G = 0.55, B = 0.55, A = 1.0 }
local SOUND_ERROR = "/Game/Audio/Sounds/UI/NotificationNeg.NotificationNeg"
local SOUND_OPEN = "/Game/Audio/Sounds/UI/HintPopup.HintPopup"

local function Warn(key, msg)
    if warned[key] then return end
    warned[key] = true
    ctx.Log("Menü: " .. msg)
end

-- Führt fn aus; schlägt es fehl, wird das einmal pro Schlüssel ins Log geschrieben.
local function Try(key, fn)
    local ok, err = pcall(fn)
    if not ok then Warn(key, key .. " nicht übernommen (" .. tostring(err) .. ")") end
    return ok
end

local function Valid(o)
    if o == nil then return false end
    local ok, v = pcall(function() return o:IsValid() end)
    return ok and v == true
end

local function Field(obj, name)
    local ok, v = pcall(function() return obj[name] end)
    if ok and Valid(v) then return v end
    return nil
end

local function ClassName(o)
    local ok, n = pcall(function() return o:GetClass():GetFName():ToString() end)
    return ok and type(n) == "string" and n or ""
end

-- UE4SS liefert bei jedem Zugriff ein neues Lua-Objekt: Identität über die Adresse (bzw. den vollen Namen) prüfen.
local function Identity(o)
    local ok, a = pcall(function() return o:GetAddress() end)
    if ok and a then return a end
    local okN, n = pcall(function() return o:GetFullName() end)
    return okN and n or nil
end

local function IsOurs(w)
    local ok, n = pcall(function() return w:GetFName():ToString() end)
    return ok and type(n) == "string" and n:find("^DDS2Net_") ~= nil
end

-- Eigene Widgets aus einer früheren Einfügung entfernen (z. B. nach Neuladen der Mod).
local function RemoveOurs(panel)
    local okN, n = pcall(function() return panel:GetChildrenCount() end)
    if not okN or type(n) ~= "number" then return 0 end
    local removed = 0
    for i = n - 1, 0, -1 do
        local ok, ch = pcall(function() return panel:GetChildAt(i) end)
        if ok and Valid(ch) and IsOurs(ch) then
            pcall(function() panel:RemoveChild(ch) end)
            removed = removed + 1
        end
    end
    return removed
end

local function Hovered(w)
    if not Valid(w) then return false end
    local ok, v = pcall(function() return w:IsHovered() end)
    return ok and v == true
end

local function New(className, outer)
    counter = counter + 1
    return StaticConstructObject(StaticFindObject("/Script/UMG." .. className), outer,
        FName("DDS2Net_" .. className .. "_" .. counter, EFindName.FNAME_Add))
end

local function UpperDE(s)
    return (s:upper():gsub("\195\164", "\195\132"):gsub("\195\182", "\195\150"):gsub("\195\188", "\195\156"))
end

-- Erstes Widget einer der Klassen unterhalb von w (Breitensuche, begrenzte Tiefe).
local function FindDescendant(w, classes, depth)
    depth = depth or 0
    if not Valid(w) or depth > 5 then return nil end
    if classes[ClassName(w)] then return w end
    local okC, c = pcall(function() return w:GetContent() end)
    if okC and Valid(c) then
        local r = FindDescendant(c, classes, depth + 1)
        if r then return r end
    end
    local okN, n = pcall(function() return w:GetChildrenCount() end)
    if okN and type(n) == "number" then
        for i = 0, n - 1 do
            local okI, ch = pcall(function() return w:GetChildAt(i) end)
            if okI then
                local r = FindDescendant(ch, classes, depth + 1)
                if r then return r end
            end
        end
    end
    return nil
end

local function FirstText(w) return FindDescendant(w, { TextBlock = true }) end

-- Text in derselben Schreibweise wie die Vorlage (das Spiel schreibt Menüknöpfe groß).
local function LikeSource(srcText, s)
    if not Valid(srcText) then return s end
    local ok, t = pcall(function() return srcText:GetText():ToString() end)
    if ok and type(t) == "string" and t:find("%a") and t == t:upper() then return UpperDE(s) end
    return s
end

local function FallbackFont(size)
    local font = StaticFindObject("/Game/GUI/Fonts/Inconsolata/InconsolataBody.InconsolataBody")
    if Valid(font) then return { FontObject = font, Size = size } end
    return nil
end

-- Schrift/Farbe/Schatten einer Vorlage übernehmen; size = andere Größe, color = feste Farbe.
local function StyleText(dst, src, size, color)
    local styled = false
    if Valid(src) then
        if size then
            styled = Try("Schriftgröße", function()
                local f = src.Font
                dst:SetFont({ FontObject = f.FontObject, TypefaceFontName = f.TypefaceFontName, Size = size, LetterSpacing = f.LetterSpacing })
            end)
        else
            styled = Try("Schrift", function() dst:SetFont(src.Font) end)
        end
        if not color then Try("Textfarbe", function() dst:SetColorAndOpacity(src.ColorAndOpacity) end) end
        -- Das Spiel schreibt Menütexte klein und lässt sie per TextTransformPolicy groß anzeigen.
        Try("Schreibweise", function() dst:SetTextTransformPolicy(src.TextTransformPolicy) end)
        Try("Textschatten", function()
            dst:SetShadowOffset(src.ShadowOffset)
            dst:SetShadowColorAndOpacity(src.ShadowColorAndOpacity)
        end)
    end
    if not styled then pcall(function() dst:SetFont(FallbackFont(size or 18)) end) end
    if color or not Valid(src) then
        pcall(function() dst:SetColorAndOpacity({ SpecifiedColor = color or COLOR_TEXT, ColorUseRule = 0 }) end)
    end
end

local function Label(tree, s, src, size, color)
    local t = New("TextBlock", tree)
    StyleText(t, src, size, color)
    t:SetText(ctx.MakeText(s))
    return t
end

local function SetLabel(t, s, color)
    if not Valid(t) then return end
    t:SetText(ctx.MakeText(s))
    if color then pcall(function() t:SetColorAndOpacity({ SpecifiedColor = color, ColorUseRule = 0 }) end) end
end

-- Knopf im Stil eines vorhandenen Spielknopfs (Bild, Hover/Pressed, Klänge, Innenabstand, Schrift).
local function MakeButton(tree, srcBtn, text, size, content)
    local b = New("Button", tree)
    local srcText = Valid(srcBtn) and FirstText(srcBtn) or nil
    if Valid(srcBtn) then
        Try("Knopf-Stil", function() b:SetStyle(srcBtn.WidgetStyle) end)
        Try("Knopf-Farbe", function()
            b:SetColorAndOpacity(srcBtn.ColorAndOpacity)
            b:SetBackgroundColor(srcBtn.BackgroundColor)
        end)
    end
    local label = content or Label(tree, LikeSource(srcText, text), srcText, size)
    local slot = b:SetContent(label)
    if Valid(srcBtn) then
        Try("Knopf-Innenabstand", function()
            local s = srcBtn:GetContentSlot()
            if not Valid(s) or not Valid(slot) then return end
            slot:SetPadding(s.Padding)
            slot:SetHorizontalAlignment(s.HorizontalAlignment)
            slot:SetVerticalAlignment(s.VerticalAlignment)
        end)
    end
    return b, label
end

-- Eigenschaft (FProperty) eines Objekts suchen, auch in Oberklassen.
local function FindProperty(obj, name)
    local found
    pcall(function()
        local cls = obj:GetClass()
        for _ = 1, 20 do
            if found or not Valid(cls) then break end
            cls:ForEachProperty(function(p)
                if not found and p:GetFName():ToString() == name then found = p end
            end)
            cls = cls:GetSuperStruct()
        end
    end)
    return found
end

local SIMPLE_PROPS = {
    BoolProperty = true, ByteProperty = true, Int8Property = true, IntProperty = true, Int64Property = true,
    UInt16Property = true, UInt32Property = true, FloatProperty = true, DoubleProperty = true,
    EnumProperty = true, NameProperty = true, ObjectProperty = true,
}

-- Struktur-Wert in eine Lua-Tabelle kopieren (UE4SS kann Strukturen nur aus Tabellen zuweisen).
local function StructToTable(value, struct, depth)
    local t = {}
    struct:ForEachProperty(function(p)
        local name = p:GetFName():ToString()
        local ptype = p:GetClass():GetFName():ToString()
        local ok, v = pcall(function() return value[name] end)
        if not ok or v == nil then return end
        if ptype == "StructProperty" then
            if depth < 6 then
                local okS, sub = pcall(function() return StructToTable(v, p:GetStruct(), depth + 1) end)
                if okS then t[name] = sub end
            end
        elseif ptype == "ObjectProperty" then
            if Valid(v) then t[name] = v end
        elseif SIMPLE_PROPS[ptype] then
            t[name] = v
        end
    end)
    return t
end

-- Struktur-Eigenschaft von src nach dst kopieren (für Widgets ohne Setter, z. B. EditableTextBox.WidgetStyle).
local function CopyStruct(dst, src, name)
    local prop = FindProperty(src, name)
    if not prop then error("Eigenschaft " .. name .. " nicht gefunden") end
    dst[name] = StructToTable(src[name], prop:GetStruct(), 0)
end

local function MakeInput(tree, src, hint, password)
    local e = New("EditableTextBox", tree)
    if Valid(src) then Try("Eingabefeld-Stil", function() CopyStruct(e, src, "WidgetStyle") end) end
    pcall(function() e:SetHintText(ctx.MakeText(hint)) end)
    if password then pcall(function() e:SetIsPassword(true) end) end
    return e
end

local function Sized(tree, child, width)
    local s = New("SizeBox", tree)
    pcall(function() s:SetWidthOverride(width) end)
    s:SetContent(child)
    return s
end

local function AddV(box, child, padTop, hAlign)
    local slot = box:AddChildToVerticalBox(child)
    pcall(function() slot:SetPadding({ Left = 0, Top = padTop or 0, Right = 0, Bottom = 0 }) end)
    if hAlign then pcall(function() slot:SetHorizontalAlignment(hAlign) end) end
    return slot
end

local function AddH(box, child, padLeft)
    local slot = box:AddChildToHorizontalBox(child)
    pcall(function() slot:SetPadding({ Left = padLeft or 0, Top = 0, Right = 0, Bottom = 0 }) end)
    pcall(function() slot:SetVerticalAlignment(2) end) -- VAlign_Center
    return slot
end

-- Slot-Layout als einfache Lua-Werte (bleiben gültig, auch wenn das Widget umgehängt wird).
local function SnapshotSlot(slot)
    local s = {}
    if not Valid(slot) then return s end
    pcall(function() local p = slot.Padding; s.padding = { Left = p.Left, Top = p.Top, Right = p.Right, Bottom = p.Bottom } end)
    pcall(function() s.h = slot.HorizontalAlignment end)
    pcall(function() s.v = slot.VerticalAlignment end)
    pcall(function() local z = slot.Size; s.size = { SizeRule = z.SizeRule, Value = z.Value } end)
    return s
end

local function ApplySlot(slot, s)
    if s.padding then pcall(function() slot:SetPadding(s.padding) end) end
    if type(s.h) == "number" then pcall(function() slot:SetHorizontalAlignment(s.h) end) end
    if type(s.v) == "number" then pcall(function() slot:SetVerticalAlignment(s.v) end) end
    if s.size then pcall(function() slot:SetSize(s.size) end) end
end

local LIST_PANELS = { VerticalBox = true, HorizontalBox = true, ScrollBox = true, WrapBox = true }

-- widget direkt hinter anchor einfügen. UMG kann aus Lua nur anhängen: die nachfolgenden Einträge
-- werden dafür kurz entfernt und mit ihrem Layout wieder angehängt.
local function InsertAfter(parent, anchor, widget)
    if not LIST_PANELS[ClassName(parent)] then return false end
    local idx = parent:GetChildIndex(anchor)
    local count = parent:GetChildrenCount()
    if type(idx) ~= "number" or type(count) ~= "number" or idx < 0 then return false end
    local template = SnapshotSlot(anchor.Slot)
    local moved = {}
    for i = idx + 1, count - 1 do
        local ch = parent:GetChildAt(i)
        table.insert(moved, { w = ch, s = SnapshotSlot(ch.Slot) })
    end
    for _, m in ipairs(moved) do parent:RemoveChild(m.w) end
    ApplySlot(parent:AddChild(widget), template)
    for _, m in ipairs(moved) do ApplySlot(parent:AddChild(m.w), m.s) end
    return true
end

-- Spiel-Klang abspielen (nur wenn das Spiel ihn schon geladen hat)
local function PlaySound(path)
    pcall(function()
        local snd = StaticFindObject(path)
        if not Valid(snd) then return end
        StaticFindObject("/Script/Engine.Default__GameplayStatics"):PlaySound2D(ctx.LocalController(), snd, 1.0, 1.0, 0.0, nil, nil, true)
    end)
end

-- Status / Fehler -------------------------------------------------------------
local function SetStatus(s, isError)
    lastStatus = { text = s, isError = isError }
    if ui then SetLabel(ui.status, s, isError and COLOR_ERROR or nil) end
end

local function ShowError(text, _title)
    SetStatus(text, true)
    PlaySound(SOUND_ERROR)
end

-- Fenster öffnen/schließen -----------------------------------------------------
local function SetOpen(open)
    isOpen = open
    if not ui then return end
    ui.blur:SetVisibility(open and VISIBLE or COLLAPSED)
    ui.window:SetVisibility(open and SELF_HIT_TEST_INVISIBLE or COLLAPSED)
    if open then
        anim = { step = 0 }
        pcall(function() ui.window:SetRenderOpacity(0) end)
        pcall(function() ui.address:SetKeyboardFocus() end)
        PlaySound(SOUND_OPEN)
        lastFavRead = 0
        -- Launcher soll Ping/Spielerzahl jetzt neu abfragen
        if ctx.IpcDir then
            local f = io.open(ctx.IpcDir .. "\\refresh-favorites.txt", "w")
            if f then f:close() end
        end
    end
end

-- Favoriten ---------------------------------------------------------------------
local function ParseFavorite(line)
    local f = {}
    for part in (line:gsub("[\r\n]", "") .. "|"):gmatch("([^|]*)|") do table.insert(f, part) end
    if #f < 3 or f[2] == "" or not tonumber(f[3]) then return nil end
    return {
        name = f[1] ~= "" and f[1] or f[2], host = f[2], port = tonumber(f[3]),
        ping = tonumber(f[4] or ""), players = tonumber(f[5] or ""), max = tonumber(f[6] or ""), state = f[7] or "",
    }
end

local function ReadFavoritesText()
    local appData = os.getenv("APPDATA")
    if not appData then return "" end
    local f = io.open(appData .. "\\DDS2Mod\\favorites.txt", "r")
    if not f then return "" end
    local s = f:read("a") or ""
    f:close()
    return s
end

local function ParseFavorites(text)
    local list = {}
    for line in text:gmatch("[^\n]+") do
        local fav = ParseFavorite(line)
        if fav and #list < MAX_FAVORITES then table.insert(list, fav) end
    end
    return list
end

local STATE_TEXT = { online = "ONLINE", pw = "PASSWORT", offline = "OFFLINE", starting = "STARTET", version = "VERSION?" }

local function FavoriteCells(fav)
    local ping = fav.ping and fav.ping >= 0 and (fav.ping .. " ms") or "–"
    local players = fav.players and fav.max and (fav.players .. "/" .. fav.max) or "–"
    return { fav.name, fav.host .. ":" .. fav.port, ping, players, STATE_TEXT[fav.state] or "" }
end

local COLUMNS = { 250, 230, 90, 90, 110 }

local function MakeRow(tree, cells, srcText, srcBtn, isHeader)
    local box = New("HorizontalBox", tree)
    local labels = {}
    for i, width in ipairs(COLUMNS) do
        local t = Label(tree, cells[i] or "", srcText, nil, isHeader and COLOR_DIM or nil)
        labels[i] = t
        AddH(box, Sized(tree, t, width), i == 1 and 0 or 8)
    end
    if isHeader then return box, labels end
    local b = MakeButton(tree, srcBtn, "", nil, box)
    return b, labels
end

local function RenderFavorites()
    if not ui then return end
    local list = ParseFavorites(favText or "")
    if #list ~= #ui.rows or not ui.favsRendered then
        ui.favsRendered = true
        pcall(function() ui.favBox:ClearChildren() end)
        ui.rows = {}
        for _, fav in ipairs(list) do
            local b, labels = MakeRow(ui.tree, FavoriteCells(fav), ui.srcBody, ui.srcRow, false)
            AddV(ui.favBox, b, 4)
            table.insert(ui.rows, { button = b, labels = labels, fav = fav })
        end
        SetLabel(ui.noFavs, #list == 0 and "Noch keine Favoriten – in DDS2 Multiplayer anlegen." or "")
        return
    end
    for i, fav in ipairs(list) do
        local row = ui.rows[i]
        row.fav = fav
        local cells = FavoriteCells(fav)
        local dim = fav.state == "offline" or fav.state == "version"
        for c, t in ipairs(row.labels) do SetLabel(t, cells[c], dim and COLOR_DIM or nil) end
    end
end

local function RefreshFavorites(force)
    if not ui or not isOpen then return end
    if not force and os.time() - lastFavRead < 3 then return end
    lastFavRead = os.time()
    local text = ReadFavoritesText()
    if text ~= favText or force then
        favText = text
        RenderFavorites()
    end
end

-- Beitreten ---------------------------------------------------------------------
local function IpcPath(name) return ctx.IpcDir .. "\\" .. name end

local function ReadFile(path)
    local f = io.open(path, "r")
    if not f then return nil end
    local s = f:read("a")
    f:close()
    return s
end

local function OpenLevel(host, port, displayName)
    local target = host .. ":" .. port
    local pc = ctx.LocalController()
    if not pc then
        ShowError("Spieler-Controller nicht gefunden – bitte erneut versuchen.")
        return
    end
    SetStatus("Verbinde mit " .. (displayName and displayName ~= "" and (displayName .. " (" .. target .. ")") or target) .. " ...")
    ctx.Event("INFO", "Verbinde (Spielmenü) mit " .. target)
    ctx.Kismet():ExecuteConsoleCommand(pc, "open " .. target, pc)
end

local function TextOf(input)
    local ok, s = pcall(function() return input:GetText():ToString() end)
    return ok and type(s) == "string" and s or ""
end

local function RequestJoin(host, port, displayName)
    if not ctx.DirectIpActive then
        ShowError("Direkt-IP ist nur aktiv, wenn das Spiel über \"DDS2 Multiplayer\" gestartet wurde.", "DIREKT-IP AUS")
        return
    end
    if not host or host == "" then
        ShowError("Bitte eine IP-Adresse oder einen Hostnamen eingeben.", "ADRESSE FEHLT")
        return
    end
    if pending then return end
    port = port or ctx.DefaultPort
    if forceTarget and forceTarget.host == host and forceTarget.port == port and os.time() - forceTarget.t < 60 then
        forceTarget = nil
        OpenLevel(host, port, displayName)
        return
    end
    forceTarget = nil
    requestCounter = requestCounter + 1
    local id = os.time() .. "-" .. requestCounter
    os.remove(IpcPath("join-response.txt"))
    -- erst in eine Hilfsdatei schreiben, dann umbenennen: der Launcher liest nie eine halbe Zeile
    local f = io.open(IpcPath("join-request.tmp"), "w")
    if not f then
        OpenLevel(host, port, displayName)
        return
    end
    f:write(table.concat({ id, host, tostring(port), ui and TextOf(ui.password) or "" }, "|") .. "\n")
    f:close()
    os.remove(IpcPath("join-request.txt"))
    os.rename(IpcPath("join-request.tmp"), IpcPath("join-request.txt"))
    pending = { id = id, host = host, port = port, name = displayName, t = os.time() }
    SetStatus("Prüfe " .. (displayName or host) .. " ...")
end

local function HandleResponse(p, result, msg)
    if result == "ok" then
        OpenLevel(p.host, p.port, msg ~= "" and msg or p.name)
    elseif result == "noanswer" then
        forceTarget = { host = p.host, port = p.port, t = os.time() }
        ShowError(msg .. " Nochmal klicken, um es trotzdem zu versuchen.", "SERVER ANTWORTET NICHT")
    elseif result == "password" then
        ShowError(msg, "PASSWORT")
        if ui then pcall(function() ui.password:SetKeyboardFocus() end) end
    elseif result == "restart" then
        SetStatus(msg)
    else
        ShowError(msg ~= "" and msg or "Unbekannter Fehler.")
    end
end

local function CheckPending()
    if not pending then return end
    local s = ReadFile(IpcPath("join-response.txt"))
    if s then
        local id, result, msg = s:match("^([^|]*)|([^|\r\n]*)|?([^\r\n]*)")
        if id == pending.id then
            os.remove(IpcPath("join-response.txt"))
            local p = pending
            pending = nil
            HandleResponse(p, result, msg or "")
            return
        end
    end
    if os.time() - pending.t >= REQUEST_TIMEOUT then
        -- Launcher antwortet nicht (z. B. geschlossen): ohne Vorabprüfung verbinden.
        local p = pending
        pending = nil
        ctx.Log("Menü: keine Antwort von DDS2 Multiplayer – verbinde ohne Prüfung")
        OpenLevel(p.host, p.port, p.name)
    end
end

local function ConnectTyped()
    -- Port aus dem eigenen Feld; "host:port" im Adressfeld geht weiterhin und hat Vorrang.
    local host, inlinePort = TextOf(ui.address):match("^%s*([^:%s]+):?(%d*)%s*$")
    local port = tonumber(inlinePort) or tonumber((TextOf(ui.port):match("^%s*(%d+)%s*$")))
    if port and (port < 1 or port > 65535) then port = nil end
    RequestJoin(host, port)
end

-- Aufbau ------------------------------------------------------------------------
-- Widget mit diesem Namen unterhalb von w suchen (auch nicht als Variable veröffentlichte Widgets).
local function FindByName(w, name, depth)
    depth = depth or 0
    if not Valid(w) or depth > 12 then return nil end
    local ok, n = pcall(function() return w:GetFName():ToString() end)
    if ok and n == name then return w end
    local okC, c = pcall(function() return w:GetContent() end)
    if okC and Valid(c) then
        local r = FindByName(c, name, depth + 1)
        if r then return r end
    end
    local okN, count = pcall(function() return w:GetChildrenCount() end)
    if okN and type(count) == "number" then
        for i = 0, count - 1 do
            local okI, ch = pcall(function() return w:GetChildAt(i) end)
            if okI then
                local r = FindByName(ch, name, depth + 1)
                if r then return r end
            end
        end
    end
    return nil
end

-- Vorlage ist das Fenster "Wähle Kartell" (ContinueHolder) des Hauptmenüs, Build 23009320:
--   BackgroundBlur, Image (Vollbild-Hintergrund), BgTopLeft_1/BgTopRight_1 (Ecken),
--   TextBlock_7 (Titel), TextBlock_8 (Untertitel), Image_612 + SearchCartelInput (Suchfeld), btnContinueCancel ("Storno").
local function Sources(menu)
    local main = Field(menu, "btnJoinCartel") or Field(menu, "btnNewCartel") or Field(menu, "btnSettings") or Field(menu, "btnContinue")
    local cont = Field(menu, "ContinueHolder")
    local function InCont(name) return cont and FindByName(cont, name) or nil end
    local apply = Field(menu, "btnCartelApply") or main
    return {
        main = main,
        mainText = main and FirstText(main),
        apply = apply,
        cancel = Field(menu, "btnContinueCancel") or Field(menu, "btnCartelCancel") or apply,
        blur = InCont("BackgroundBlur"),
        background = InCont("Image"),
        cornerLeft = InCont("BgTopLeft_1"),
        cornerRight = InCont("BgTopRight_1"),
        title = InCont("TextBlock_7") or (main and FirstText(main)),
        subtitle = InCont("TextBlock_8") or Field(menu, "txtNoSessions"),
        inputBg = InCont("Image_612"),
        input = Field(menu, "SearchCartelInput"),
    }
end

-- Bild wie die Vorlage (Brush, Farbe) und – in einer CanvasPanel – an derselben Stelle.
local function CopyImage(tree, canvas, src, z)
    if not Valid(src) then return nil end
    local img = New("Image", tree)
    Try("Bild", function()
        img:SetBrush(src.Brush)
        img:SetColorAndOpacity(src.ColorAndOpacity)
    end)
    -- Deckkraft inkl. der Container darüber (die Vorlage ist teilweise durchsichtig)
    Try("Bild-Deckkraft", function()
        local opacity, w = 1.0, src
        for _ = 1, 4 do
            if not Valid(w) then break end
            opacity = opacity * (w:GetRenderOpacity() or 1.0)
            w = w:GetParent()
        end
        img:SetRenderOpacity(opacity)
    end)
    local slot = canvas:AddChild(img)
    Try("Bild-Position", function()
        local s = src.Slot
        if not Valid(s) or ClassName(s) ~= "CanvasPanelSlot" then error("keine CanvasPanelSlot") end
        slot:SetAnchors(s:GetAnchors())
        slot:SetOffsets(s:GetOffsets())
        slot:SetAlignment(s:GetAlignment())
        slot:SetAutoSize(s:GetAutoSize())
    end)
    pcall(function() slot:SetZOrder(z) end)
    return img
end

-- Eingabefeld wie das Suchfeld des Spiels: Hintergrundbild + EditableText mit dessen Stil.
local function Input(tree, src, hint, width, password, text)
    local frame = New("Border", tree)
    local copied = Valid(src.inputBg) and Try("Feld-Hintergrund", function()
        frame:SetBrush(src.inputBg.Brush)
        frame:SetBrushColor(src.inputBg.ColorAndOpacity)
    end)
    if not copied then pcall(function() frame:SetBrushColor({ R = 0, G = 0, B = 0, A = 0.85 }) end) end
    pcall(function() frame:SetPadding({ Left = 10, Top = 4, Right = 10, Bottom = 4 }) end)
    pcall(function() frame:SetVerticalAlignment(2) end)
    local e = New("EditableText", tree)
    if Valid(src.input) then Try("Eingabefeld-Stil", function() CopyStruct(e, src.input, "WidgetStyle") end) end
    pcall(function() e:SetHintText(ctx.MakeText(hint)) end)
    if password then pcall(function() e:SetIsPassword(true) end) end
    if text then pcall(function() e:SetText(ctx.MakeText(text)) end) end
    frame:SetContent(e)
    local box = New("SizeBox", tree)
    pcall(function() box:SetWidthOverride(width) end)
    pcall(function() box:SetHeightOverride(32) end)
    box:SetContent(frame)
    return e, box
end

-- Beschriftete Spalte (Überschrift klein und groß geschrieben wie im Spiel, darunter das Feld).
local function Column(tree, src, caption, child)
    local col = New("VerticalBox", tree)
    AddV(col, Label(tree, UpperDE(caption), src.subtitle, nil, COLOR_DIM), 0)
    AddV(col, child, 4)
    return col
end

local function BuildWindow(menu, w, src)
    local tree = menu.WidgetTree
    local root = tree.RootWidget

    -- Vollbild-Fenster wie "Wähle Kartell"
    w.window = New("CanvasPanel", tree)
    local winSlot = root:AddChild(w.window)
    pcall(function()
        winSlot:SetAnchors({ Minimum = { X = 0, Y = 0 }, Maximum = { X = 1, Y = 1 } })
        winSlot:SetOffsets({ Left = 0, Top = 0, Right = 0, Bottom = 0 })
        winSlot:SetZOrder(1000)
    end)

    -- unscharfer Hintergrund; ein Klick darauf (neben den Inhalt) schließt das Fenster
    w.blur = New("BackgroundBlur", tree)
    local blurStrength = 10
    if Valid(src.blur) then pcall(function() blurStrength = src.blur.BlurStrength end) end
    pcall(function() w.blur:SetBlurStrength(blurStrength) end)
    if not Valid(src.background) then pcall(function() w.blur:SetBrushColor({ R = 0, G = 0, B = 0, A = 0.6 }) end) end
    local blurSlot = w.window:AddChild(w.blur)
    pcall(function()
        blurSlot:SetAnchors({ Minimum = { X = 0, Y = 0 }, Maximum = { X = 1, Y = 1 } })
        blurSlot:SetOffsets({ Left = 0, Top = 0, Right = 0, Bottom = 0 })
    end)
    local bg = CopyImage(tree, w.window, src.background, 1)
    if bg then pcall(function() bg:SetVisibility(3) end) end -- HitTestInvisible: Klicks gehen an den Blur
    for _, corner in ipairs({ src.cornerLeft, src.cornerRight }) do
        local img = CopyImage(tree, w.window, corner, 2)
        if img then pcall(function() img:SetVisibility(3) end) end
    end

    local body = New("VerticalBox", tree)
    local bodySlot = w.window:AddChild(body)
    pcall(function()
        bodySlot:SetAnchors({ Minimum = { X = 0.5, Y = 0 }, Maximum = { X = 0.5, Y = 0 } })
        bodySlot:SetAlignment({ X = 0.5, Y = 0 })
        bodySlot:SetPosition({ X = 0, Y = 24 })
        bodySlot:SetAutoSize(true)
        bodySlot:SetZOrder(3)
    end)

    local center = 2 -- HAlign_Center
    AddV(body, Label(tree, "Server beitreten", src.title, nil), 0, center)
    AddV(body, Label(tree, "Adresse des Servers eingeben und verbinden – oder einen Favoriten anklicken", src.subtitle, nil), 4, center)

    local inputs = New("HorizontalBox", tree)
    local addressBox, portBox, passwordBox
    w.address, addressBox = Input(tree, src, "z. B. 192.168.178.20 oder server.example.org", 380, false)
    w.port, portBox = Input(tree, src, tostring(ctx.DefaultPort), 100, false, tostring(ctx.DefaultPort))
    w.password, passwordBox = Input(tree, src, "falls nötig", 220, true)
    AddH(inputs, Column(tree, src, "IP / Hostname", addressBox), 0)
    AddH(inputs, Column(tree, src, "Port", portBox), 16)
    AddH(inputs, Column(tree, src, "Passwort", passwordBox), 16)
    AddV(body, inputs, 22, center)

    w.connect = MakeButton(tree, src.apply, "Verbinden")
    AddV(body, w.connect, 18, center)

    AddV(body, Label(tree, UpperDE("Favoriten"), src.title, 16), 30, center)
    AddV(body, (MakeRow(tree, { "NAME", "ADRESSE", "PING", "SPIELER", "STATUS" }, src.subtitle, nil, true)), 8)
    w.favBox = New("VerticalBox", tree)
    AddV(body, w.favBox, 2)
    w.noFavs = Label(tree, "", src.subtitle, nil, COLOR_DIM)
    AddV(body, w.noFavs, 4, center)
    w.rows = {}

    if not ctx.DirectIpActive then
        AddV(body, Label(tree, "Hinweis: Spiel über \"DDS2 Multiplayer\" starten, sonst ist Direkt-IP aus.", src.subtitle, nil, COLOR_ERROR), 14, center)
    end
    -- Statuszeile mit fester Breite und Umbruch, damit lange Fehlermeldungen die Tabelle nicht verschieben
    w.status = Label(tree, "", src.subtitle, nil)
    pcall(function() w.status:SetAutoWrapText(true) end)
    pcall(function() w.status:SetJustification(1) end) -- ETextJustify::Center
    AddV(body, Sized(tree, w.status, 820), 14, center)

    -- "Abbrechen" unten mittig wie "Storno"
    w.cancel = MakeButton(tree, src.cancel, "Abbrechen")
    local cancelSlot = w.window:AddChild(w.cancel)
    pcall(function()
        cancelSlot:SetAnchors({ Minimum = { X = 0.5, Y = 1 }, Maximum = { X = 0.5, Y = 1 } })
        cancelSlot:SetAlignment({ X = 0.5, Y = 1 })
        cancelSlot:SetPosition({ X = 0, Y = -36 })
        cancelSlot:SetAutoSize(true)
        cancelSlot:SetZOrder(3)
    end)

    w.tree = tree
    w.srcBody = src.subtitle
    w.srcRow = src.cancel
end

-- Menüpunkt in die Knopfliste des Hauptmenüs einfügen (hinter "Kartell beitreten").
local function BuildEntry(menu, w, src)
    local tree = menu.WidgetTree
    w.entry = MakeButton(tree, src.main, "Server beitreten")
    if Valid(src.main) then
        local anchor = src.main
        local parent = anchor:GetParent()
        -- Knopf steckt evtl. in einer Hülle (SizeBox o. Ä.): bis zur Liste hochgehen
        for _ = 1, 3 do
            if not Valid(parent) or LIST_PANELS[ClassName(parent)] then break end
            anchor = parent
            parent = anchor:GetParent()
        end
        if Valid(parent) and LIST_PANELS[ClassName(parent)] then
            local item = w.entry
            if anchor ~= src.main and ClassName(anchor) == "SizeBox" then
                item = New("SizeBox", tree)
                Try("Hülle", function()
                    if anchor.bOverride_WidthOverride then item:SetWidthOverride(anchor.WidthOverride) end
                    if anchor.bOverride_HeightOverride then item:SetHeightOverride(anchor.HeightOverride) end
                end)
                item:SetContent(w.entry)
            end
            local stale = RemoveOurs(parent)
            if stale > 0 then ctx.Log("Menü: " .. stale .. " alte(r) Menüpunkt(e) entfernt") end
            local ok, inserted = pcall(InsertAfter, parent, anchor, item)
            if ok and inserted then
                w.entryInMenu = true
                return
            end
            Warn("insert", "Einfügen in die Knopfliste fehlgeschlagen (" .. tostring(inserted) .. ") – Menüpunkt oben rechts")
        else
            Warn("parent", "Knopfliste des Hauptmenüs nicht gefunden (" .. ClassName(parent) .. ") – Menüpunkt oben rechts")
        end
    else
        Warn("source", "Vorlage btnJoinCartel nicht gefunden – Menüpunkt oben rechts")
    end
    -- Ersatz: oben rechts
    local slot = tree.RootWidget:AddChild(w.entry)
    pcall(function()
        slot:SetAnchors({ Minimum = { X = 1, Y = 0 }, Maximum = { X = 1, Y = 0 } })
        slot:SetAlignment({ X = 1, Y = 0 })
        slot:SetPosition({ X = -40, Y = 40 })
        slot:SetAutoSize(true)
        slot:SetZOrder(999)
    end)
end

local function Build(menu)
    local root = menu.WidgetTree.RootWidget
    if not Valid(root) then
        ctx.Log("Menü: Wurzel-Widget des Hauptmenüs nicht gefunden")
        return nil
    end
    local src = Sources(menu)
    local w = { menu = menu, menuId = Identity(menu) }
    RemoveOurs(root)
    BuildEntry(menu, w, src)
    BuildWindow(menu, w, src)
    local missing = {}
    for _, k in ipairs({ "main", "apply", "cancel", "blur", "background", "cornerLeft", "cornerRight", "title", "subtitle", "inputBg", "input" }) do
        if not src[k] then table.insert(missing, k) end
    end
    ctx.Log("Menü \"Server beitreten\" eingefügt (" .. (w.entryInMenu and "in der Knopfliste" or "oben rechts")
        .. (#missing > 0 and ", ohne Vorlage: " .. table.concat(missing, ", ") or ", alle Vorlagen gefunden") .. ")")
    return w
end

-- Wird regelmäßig aus main.lua (Game-Thread) aufgerufen: Menü bauen, sobald das Hauptmenü sichtbar ist.
function M.Tick()
    local menu = FindFirstOf("MainMenuWidget_C")
    if not ctx.IsLive(menu) or not menu:IsInViewport() then
        ui = nil
        return
    end
    if ui and ui.menuId == Identity(menu) and Valid(ui.entry) then return end
    local ok, result = pcall(Build, menu)
    if not ok or not result then
        ctx.Log("Menü konnte nicht gebaut werden: " .. tostring(result))
        ui = nil
        return
    end
    ui = result
    favText = nil
    SetOpen(isOpen)
    if lastStatus then SetStatus(lastStatus.text, lastStatus.isError) end
end

-- Widget-Baum als Text (Diagnose zum Nachbauen von Fenstern des Spiels)
local function DescribeTree(w, depth, out)
    if not Valid(w) or depth > 12 then return end
    local cls = ClassName(w)
    local okN, name = pcall(function() return w:GetFName():ToString() end)
    local info = {}
    -- Achtung: Methoden auf leeren Objekten (nullptr) bringen UE4SS zum Absturz – pcall hilft da nicht. Immer erst Valid().
    local function ObjName(o) return Valid(o) and o:GetFName():ToString() or "-" end
    pcall(function() if cls == "TextBlock" then table.insert(info, "text='" .. w:GetText():ToString() .. "' size=" .. tostring(w.Font.Size) .. " transform=" .. tostring(w.TextTransformPolicy)) end end)
    pcall(function() if cls == "Image" then table.insert(info, "brush=" .. ObjName(w.Brush.ResourceObject) .. " draw=" .. tostring(w.Brush.DrawAs)) end end)
    pcall(function() if cls == "Border" then table.insert(info, "brush=" .. ObjName(w.Background.ResourceObject)) end end)
    pcall(function() table.insert(info, "vis=" .. tostring(w:GetVisibility())) end)
    pcall(function()
        local s = w.Slot
        if Valid(s) and ClassName(s) == "CanvasPanelSlot" then
            local o = s:GetOffsets()
            table.insert(info, string.format("offs=%.0f,%.0f,%.0f,%.0f", o.Left, o.Top, o.Right, o.Bottom))
        end
    end)
    table.insert(out, string.rep("  ", depth) .. cls .. " " .. (okN and name or "?") .. "  " .. table.concat(info, " "))
    local okC, c = pcall(function() return w:GetContent() end)
    if okC and Valid(c) and cls ~= "Button" then DescribeTree(c, depth + 1, out) return end
    local okK, n = pcall(function() return w:GetChildrenCount() end)
    if okK and type(n) == "number" then
        for i = 0, n - 1 do
            local okI, ch = pcall(function() return w:GetChildAt(i) end)
            if okI then DescribeTree(ch, depth + 1, out) end
        end
    end
end

-- Diagnose über commands.txt: "menu open", "menu close", "menu join <adresse>", "menu tree <Feld des Hauptmenüs>"
function M.Command(arg)
    local sub, rest = (arg or ""):match("^(%S*)%s*(.-)$")
    if not ui then
        ctx.Event("WARN", "menu: Hauptmenü nicht offen")
    elseif sub == "tree" then
        local target = rest == "" and ui.menu.WidgetTree.RootWidget or Field(ui.menu, rest)
        local out = {}
        DescribeTree(target, 0, out)
        local path = ctx.IpcDir .. "\\tree-" .. (rest ~= "" and rest or "root") .. ".txt"
        local f = io.open(path, "w")
        if f then f:write(table.concat(out, "\n") .. "\n") f:close() end
        ctx.Event("INFO", "menu tree: " .. #out .. " Widgets -> " .. path)
    elseif sub == "open" then SetOpen(true)
    elseif sub == "close" then SetOpen(false)
    elseif sub == "join" then
        local host, port = rest:match("^%s*([^:%s]+):?(%d*)%s*$")
        RequestJoin(host, tonumber(port))
    else
        ctx.Event("WARN", "menu: open | close | join <adresse>")
    end
end

function M.ShowError(text)
    pending = nil
    ShowError(text)
end

-- schnelle Schleife: Einblenden, Antworten des Launchers, Favoriten
local function Pump()
    if anim and ui then
        anim.step = anim.step + 1
        local t = math.min(anim.step / 5, 1)
        pcall(function() ui.window:SetRenderOpacity(t) end)
        if t >= 1 then anim = nil end
    end
    if ctx.IpcDir then CheckPending() end
    RefreshFavorites(false)
end

local function OnGameThread(fn)
    return function()
        ExecuteInGameThread(function()
            if not ui then return end
            local ok, err = pcall(fn)
            if not ok then ctx.Log("Menü: " .. tostring(err)) end
        end)
    end
end

function M.Init(context)
    ctx = context
    RegisterKeyBind(Key.F8, OnGameThread(function() SetOpen(not isOpen) end))
    RegisterKeyBind(Key.ESCAPE, OnGameThread(function() if isOpen then SetOpen(false) end end))
    RegisterKeyBind(Key.RETURN, OnGameThread(function()
        if isOpen then ConnectTyped() end
    end))
    -- UMG-Klick-Events lassen sich aus Lua nicht binden: Mausklick + Hover-Zustand prüfen.
    -- Klang und Hover-Bild kommen trotzdem vom kopierten Knopfstil.
    RegisterKeyBind(Key.LEFT_MOUSE_BUTTON, OnGameThread(function()
        if Hovered(ui.entry) then SetOpen(not isOpen) return end
        if not isOpen then return end
        if Hovered(ui.connect) then ConnectTyped() return end
        if Hovered(ui.cancel) then SetOpen(false) return end
        for _, row in ipairs(ui.rows) do
            if Hovered(row.button) then
                RequestJoin(row.fav.host, row.fav.port, row.fav.name)
                return
            end
        end
        if Hovered(ui.blur) then SetOpen(false) end
    end))
    ctx.GameLoop(100, "Menü", function()
        if ui then Pump() end
    end)
end

return M

--[[
    Menü "Server beitreten" im Hauptmenü des Spiels.

    Wird zur Laufzeit aus UMG-Bausteinen gebaut und in die Wurzel des
    MainMenuWidget gehängt (keine eigenen .pak-Dateien nötig). Bedienung:
      F8 oder Klick auf "Server beitreten"  Menü auf/zu
      Enter oder Klick auf "Verbinden"      zur eingegebenen Adresse verbinden
      Klick auf einen Favoriten             zu diesem Server verbinden
    Favoriten kommen aus %APPDATA%\DDS2Mod\favorites.txt (schreibt DDS2Mod.exe).
]]

local M = {}

local ctx          -- Hilfsfunktionen aus main.lua
local ui           -- Widgets der aktuellen Hauptmenü-Instanz
local isOpen = false
local lastStatus = nil
local counter = 0

local MAX_FAVORITES = 9
local COLOR_TEXT = { R = 0.92, G = 0.92, B = 0.92, A = 1.0 }
local COLOR_DIM = { R = 0.65, G = 0.65, B = 0.65, A = 1.0 }
local COLOR_ERROR = { R = 1.0, G = 0.35, B = 0.3, A = 1.0 }
local VISIBLE, COLLAPSED = 0, 1

local function UmgClass(name)
    return StaticFindObject("/Script/UMG." .. name)
end

local function New(className, outer)
    counter = counter + 1
    return StaticConstructObject(UmgClass(className), outer, FName("DDS2Net_" .. className .. "_" .. counter, EFindName.FNAME_Add))
end

local function Font(size)
    local font = StaticFindObject("/Game/GUI/Fonts/Inconsolata/InconsolataBody.InconsolataBody")
    if font and font:IsValid() then return { FontObject = font, Size = size } end
    return nil
end

local function SetLabel(textBlock, s, color)
    textBlock:SetText(ctx.MakeText(s))
    if color then
        pcall(function() textBlock:SetColorAndOpacity({ SpecifiedColor = color, ColorUseRule = 0 }) end)
    end
end

local function Label(tree, s, size, color)
    local t = New("TextBlock", tree)
    pcall(function()
        local f = Font(size)
        if f then t:SetFont(f) end
    end)
    SetLabel(t, s, color or COLOR_TEXT)
    return t
end

local function TextButton(tree, s, size)
    local b = New("Button", tree)
    local t = Label(tree, s, size, { R = 0.1, G = 0.1, B = 0.1, A = 1.0 })
    b:SetContent(t)
    return b, t
end

local function AddTo(box, child, padTop)
    local slot = box:AddChildToVerticalBox(child)
    pcall(function() slot:SetPadding({ Left = 0, Top = padTop or 4, Right = 0, Bottom = 0 }) end)
    return slot
end

local function ReadFavorites()
    local list = {}
    local appData = os.getenv("APPDATA")
    if not appData then return list end
    local f = io.open(appData .. "\\DDS2Mod\\favorites.txt", "r")
    if not f then return list end
    for line in f:lines() do
        local name, host, port = line:match("^([^|]*)|([^|]+)|(%d+)%s*$")
        if host and #list < MAX_FAVORITES then
            table.insert(list, { name = name, host = host, port = tonumber(port) })
        end
    end
    f:close()
    return list
end

local function SetStatus(s, isError)
    lastStatus = { text = s, isError = isError }
    if ui and ui.status:IsValid() then SetLabel(ui.status, s, isError and COLOR_ERROR or COLOR_DIM) end
end

local function SetOpen(open)
    isOpen = open
    if ui and ui.panel:IsValid() then
        ui.panel:SetVisibility(open and VISIBLE or COLLAPSED)
        if open then pcall(function() ui.address:SetKeyboardFocus() end) end
    end
end

local function Connect(host, port)
    if not ctx.DirectIpActive then
        SetStatus("Direkt-IP ist nur aktiv, wenn das Spiel über \"DDS2 Multiplayer\" gestartet wurde.", true)
        return
    end
    if not host or host == "" then
        SetStatus("Bitte eine IP-Adresse oder einen Hostnamen eingeben.", true)
        return
    end
    local target = host .. ":" .. (port or ctx.DefaultPort)
    local pc = ctx.LocalController()
    if not pc then
        SetStatus("Spieler-Controller nicht gefunden – bitte erneut versuchen.", true)
        return
    end
    SetStatus("Verbinde mit " .. target .. " ...")
    ctx.Event("INFO", "Verbinde (Spielmenü) mit " .. target)
    ctx.Kismet():ExecuteConsoleCommand(pc, "open " .. target, pc)
end

local function ConnectTyped()
    local text = ui.address:GetText():ToString()
    local host, port = text:match("^%s*([^:%s]+):?(%d*)%s*$")
    Connect(host, tonumber(port))
end

local function FindRootPanel(menu)
    local root = menu.WidgetTree.RootWidget
    if root and root:IsValid() then return root end
    return nil
end

local function Build(menu)
    local root = FindRootPanel(menu)
    if not root then
        ctx.Log("Menü: Wurzel-Widget des Hauptmenüs nicht gefunden")
        return nil
    end
    local tree = menu.WidgetTree
    local w = {}

    w.box = New("VerticalBox", tree)
    w.toggle = TextButton(tree, "Server beitreten (Direkt-IP)  [F8]", 18)
    AddTo(w.box, w.toggle, 0)

    w.panel = New("Border", tree)
    pcall(function() w.panel:SetBrushColor({ R = 0.02, G = 0.02, B = 0.02, A = 0.85 }) end)
    pcall(function() w.panel:SetPadding({ Left = 14, Top = 12, Right = 14, Bottom = 12 }) end)
    AddTo(w.box, w.panel, 6)

    local inner = New("VerticalBox", tree)
    w.panel:SetContent(inner)
    AddTo(inner, Label(tree, "Adresse: IP oder Hostname, optional :Port (Standard " .. ctx.DefaultPort .. ")", 14), 0)

    local size = New("SizeBox", tree)
    pcall(function() size:SetWidthOverride(380) end)
    w.address = New("EditableTextBox", tree)
    pcall(function() w.address:SetHintText(ctx.MakeText("z. B. 192.168.178.20:7777")) end)
    size:SetContent(w.address)
    AddTo(inner, size, 4)

    w.connect = TextButton(tree, "Verbinden  [Enter]", 16)
    AddTo(inner, w.connect, 6)

    w.favorites = {}
    local favs = ReadFavorites()
    AddTo(inner, Label(tree, #favs > 0 and "Favoriten (anklicken zum Verbinden)" or "Keine Favoriten – im Programm \"DDS2 Multiplayer\" anlegen", 14, COLOR_DIM), 12)
    for _, fav in ipairs(favs) do
        local b = TextButton(tree, fav.name .. "  (" .. fav.host .. ":" .. fav.port .. ")", 14)
        AddTo(inner, b, 3)
        table.insert(w.favorites, { button = b, fav = fav })
    end

    if not ctx.DirectIpActive then
        AddTo(inner, Label(tree, "Hinweis: Spiel über \"DDS2 Multiplayer\" starten, sonst ist Direkt-IP aus.", 14, COLOR_ERROR), 10)
    end
    w.status = Label(tree, "", 14, COLOR_DIM)
    AddTo(inner, w.status, 10)

    -- In die Wurzel des Hauptmenüs einhängen: oben rechts, über allem anderen.
    local slot = root:AddChild(w.box)
    pcall(function()
        slot:SetAnchors({ Minimum = { X = 1, Y = 0 }, Maximum = { X = 1, Y = 0 } })
        slot:SetAlignment({ X = 1, Y = 0 })
        slot:SetPosition({ X = -40, Y = 40 })
        slot:SetAutoSize(true)
        slot:SetZOrder(1000)
    end)
    w.menu = menu
    return w
end

-- Wird regelmäßig aus main.lua (Game-Thread) aufgerufen: Menü bauen, sobald das Hauptmenü sichtbar ist.
function M.Tick()
    local menu = FindFirstOf("MainMenuWidget_C")
    if not ctx.IsLive(menu) or not menu:IsInViewport() then
        ui = nil
        return
    end
    if ui and ui.menu == menu and ui.box:IsValid() then return end
    local ok, result = pcall(Build, menu)
    if not ok or not result then
        ctx.Log("Menü konnte nicht gebaut werden: " .. tostring(result))
        ui = nil
        return
    end
    ui = result
    SetOpen(isOpen)
    if lastStatus then SetStatus(lastStatus.text, lastStatus.isError) end
    ctx.Log("Menü \"Server beitreten\" im Hauptmenü eingefügt")
end

function M.ShowError(text)
    SetStatus(text, true)
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
    RegisterKeyBind(Key.RETURN, OnGameThread(function()
        if isOpen then ConnectTyped() end
    end))
    -- UMG-Klick-Events lassen sich aus Lua nicht binden: Mausklick + Hover-Zustand prüfen.
    RegisterKeyBind(Key.LEFT_MOUSE_BUTTON, OnGameThread(function()
        if ui.toggle:IsHovered() then SetOpen(not isOpen) return end
        if not isOpen then return end
        if ui.connect:IsHovered() then ConnectTyped() return end
        for _, entry in ipairs(ui.favorites) do
            if entry.button:IsHovered() then Connect(entry.fav.host, entry.fav.port) return end
        end
    end))
end

return M

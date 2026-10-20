-- QuickStart: the track designer's "Play: custom LAN race on <map>".
--
-- The designer writes target.lua next to this file ({enabled, auto, city_names, route_names, laps}).
-- (Keep big numbers OUT of target.lua: uids like 4294967072 crashed the game at start - lua5.1.dll trap.)
--
-- How the game runs screens (decoded from its menu Lua, _mod_project/LOG.md 2026-10-16): every frame the engine calls
-- the GLOBAL FrameUpdate_Wrapper(dt); it takes the screen's table ContextTable[UIScreen.Context()] (GUI, SCUI and the
-- screen's own FrameUpdate / MessageUpdate ...), sets the globals GUI / SCUI from it and calls its FrameUpdate.
-- So this plugin ticks by wrapping FrameUpdate_Wrapper (after the game's frame), and reads GUI and screen
-- functions from the current context table (falling back to globals).
--
-- With auto = true it walks the menus by itself, doing what the game's own buttons do:
--   start screen at the mode select stage (single player / multiplayer cubes: GUI.current_stage ==
--     GUI.STAGE_MODE_SELECT and UIGlobals.ProfileBootFinished)
--     -> Multiplayer: Amax.SetGameMode(MultiPlayer); TimeLineActive Open_Mp / Close_Sp;
--        Amax.SetPlayingSpGameLatest(false); EnterMpRaceBook(); net_EnableGlobalUpdate(true, true)
--   multiplayer\mpmain.lua -> LAN: RemoveBlaggedProfiles(); net_MpEnter(GameMode.SystemLink);
--        net_StartServiceConnection(false, ConnectionCallback)
--   Multiplayer\MpLan.lua -> Host (Y): GoScreen Multiplayer\Shared\MpModeSelect.lua
--   MpModeSelect -> Custom race: Mp_ResetCustomRace(); <our city / route>; net_MpEnterPlayMode(PlayMode.CustomRace);
--        Amax.SetupRace(MpModeSelect_CreateGame()); NetRace.CreateLanServer(MpModeSelect_CreateGame())
--        -> GoSubScreen Multiplayer\MpLobby.lua
-- Every step runs once, a few frames after its screen opened, inside pcall; an error stops the walk (normal menus).

local M = {
	step = 0,          -- 0 start, 1 multiplayer done, 2 LAN done, 3 host done, 4 lobby asked, -1 stopped / done
	tries = 0,         -- CreateLanServer attempts
	recw = false,
	left_mp = false,   -- the multiplayer menu ran its LeaveMultiplayerMode (its start-up cleanup)
	lm_wrapper = false,
	mpmain_frame = 0,
	track_later = false,      -- record mode: our wrappers (table made in the tick, not at load time)
	pending = false,   -- {name, fn, frames left} (false, not nil: the key must exist from the start)
	ticking = false,
	frames = 0,
	seen = false,      -- screens opened (lowercase GoScreen / PushScreen names) - tables made in the hook, see below
	seen_list = false,
	pob_off = false,  -- "Previously on Blur" switched off for this start
}

M.info = {
	name = "QuickStart",
	description = "Starts a custom LAN race on the map from the track designer",
	version = 0.14,
}

local T = { enabled = false }

-- print_api output is not visible anywhere, so every message is ALSO kept as a Lua string "QSMARK#<n> ..." in
-- the global QUICKSTART_LOG: Lua strings live in the game's memory, map_builder\exe\read_quickstart.py finds them.
local n_log = 0
local function log(...)
	n_log = n_log + 1
	local text = table.concat({ ... }, " ")
	QUICKSTART_LOG = "QSMARK#" .. n_log .. " f" .. tostring(M.frames) .. " step " .. tostring(M.step) .. ": " .. text .. "#END"
	-- the whole history as ONE string kept in a global (single message strings get garbage collected): the
	-- reader takes the "QSALL#<count>" copy with the highest count. Heartbeats are left out (they'd push it out).
	if string.sub(text, 1, 8) ~= "context " then
		local all = (QUICKSTART_ALL_BODY or "") .. "\n" .. n_log .. " f" .. tostring(M.frames) .. " step "
			.. tostring(M.step) .. ": " .. text
		if #all > 60000 then
			all = string.sub(all, -60000)
		end
		QUICKSTART_ALL_BODY = all
		QUICKSTART_ALL = "QSALL#" .. n_log .. "#" .. all .. "#ALLEND"
	end
	pcall(print_api, " [quickstart] " .. text)
end

-- the current screen's context table (GUI, its functions), or nil
local function ctx()
	if type(ContextTable) ~= "table" or type(UIScreen) ~= "table" or type(UIScreen.Context) ~= "function" then
		return nil
	end
	local ok, c = pcall(UIScreen.Context)
	if not ok then
		return nil
	end
	local t = ContextTable[c]
	if type(t) == "table" then
		return t
	end
	return nil
end

-- a screen function / table by name: the current context first, then the globals
local function get(name)
	local c = ctx()
	if c and c[name] ~= nil then
		return c[name]
	end
	return _G[name]
end

local function norm(s)
	return (string.gsub(string.lower(tostring(s or "")), "[^%w]", ""))
end

local function matches(item, wants)
	local n = norm(item.name)
	for _, w in ipairs(wants or {}) do
		if n ~= "" and n == norm(w) then
			return true
		end
	end
	return false
end

local function find_ids(mode)
	local cities = Multiplayer.GetCustomRaceCities(mode) or {}
	-- the game's lists use the City / Route UIDS as ids (signed 32 bit: our FFFFFF20 = -224) and LocString ids as
	-- names (numbers) - so match the uids first (target.lua: city_uid / route_uid, small negative numbers)
	if T.city_uid then
		for _, c in ipairs(cities) do
			if c.id == T.city_uid then
				for _, r in ipairs(Multiplayer.GetCustomRaceRoutes(c.id, mode) or {}) do
					if r.id == T.route_uid then
						return c.id, r.id
					end
				end
				return c.id, nil
			end
		end
	end
	for _, c in ipairs(cities) do                          -- the city by name, then its route by name
		if matches(c, T.city_names) then
			for _, r in ipairs(Multiplayer.GetCustomRaceRoutes(c.id, mode) or {}) do
				if matches(r, T.route_names) then
					return c.id, r.id
				end
			end
			return c.id, nil
		end
	end
	for _, c in ipairs(cities) do                          -- else the route by name in any city
		for _, r in ipairs(Multiplayer.GetCustomRaceRoutes(c.id, mode) or {}) do
			if matches(r, T.route_names) then
				return c.id, r.id
			end
		end
	end
	return nil, nil
end

local function apply(settings)
	local cid, rid = find_ids(settings.game_mode)
	if cid then
		settings.city_id = cid
	end
	if rid then
		settings.route_id = rid
	end
	if T.laps then
		settings.laps = T.laps
	end
	log("city " .. tostring(cid) .. " route " .. tostring(rid))
	if not rid then
		-- show what the game calls its cities / routes (to fix the names the designer writes into target.lua)
		local ok, err = pcall(function()
			local parts = {}
			for _, c in ipairs(Multiplayer.GetCustomRaceCities(settings.game_mode) or {}) do
				local rs = {}
				for _, r in ipairs(Multiplayer.GetCustomRaceRoutes(c.id, settings.game_mode) or {}) do
					rs[#rs + 1] = tostring(r.id) .. "=" .. tostring(r.name)
				end
				parts[#parts + 1] = tostring(c.id) .. "=" .. tostring(c.name) .. " [" .. table.concat(rs, ";") .. "]"
			end
			log("game cities: " .. table.concat(parts, " | "))
		end)
		if not ok then
			log("listing cities failed: " .. tostring(err))
		end
	end
	return cid ~= nil
end

-- ---- the steps ----------------------------------------------------------------------------------------------
local function to_multiplayer()
	Amax.SetGameMode(UIEnums.GameMode.MultiPlayer)
	pcall(UIButtons.TimeLineActive, "Open_Mp", false)
	pcall(UIButtons.TimeLineActive, "Close_Sp", true)
	if Amax.SetPlayingSpGameLatest then
		Amax.SetPlayingSpGameLatest(false)
	end
	get("EnterMpRaceBook")()
	net_EnableGlobalUpdate(true, true)
end

local function to_lan()
	local rb = get("RemoveBlaggedProfiles")
	if type(rb) == "function" then
		rb()
	end
	net_MpEnter(UIEnums.GameMode.SystemLink)
	-- the menu's own ConnectionCallback opens the app SELECTED in its carousel (not LAN when we never moved it):
	-- ours opens LAN. OpenApp (MpMain) reads ContextTable[Main], so it works from any context.
	local open = get("OpenApp")
	log("LAN: OpenApp is " .. type(open) .. ", Lan branch " .. tostring(UIEnums.RwMpBranch and UIEnums.RwMpBranch.Lan))
	net_StartServiceConnection(false, function(ok, ...)
		local extra = ""
		if select("#", ...) > 0 then
			extra = tostring((...))
		end
		log("connection callback: " .. tostring(ok) .. " " .. extra)
		if ok == true then
			local main = ContextTable and UIEnums.Context and ContextTable[UIEnums.Context.Main]
			local g = main and main.GUI
			log("before OpenApp: active_context " .. tostring(g and g.active_context) .. " active "
				.. tostring(g and UIScreen.GetScreenActive and UIScreen.GetScreenActive(g.active_context))
				.. " branch_name " .. tostring(g and g.branch_name and g.branch_name[UIEnums.RwMpBranch.Lan]))
			local ok2, err = pcall(open, UIEnums.RwMpBranch.Lan)
			log("OpenApp LAN -> " .. tostring(ok2) .. " " .. tostring(err))
			if not ok2 then
				M.step = -1
			end
		else
			log("LAN connection failed")
			M.step = -1
		end
	end)
end

local function to_host()
	GoScreen("Multiplayer\\Shared\\MpModeSelect.lua")
end

local try_create          -- defined below (it needs queue / is_mode_select)

local function to_lobby()
	get("Mp_ResetCustomRace")()
	local s = UIGlobals.CustomRaceSettings
	log("custom race defaults: mode " .. tostring(s and s.game_mode) .. " city " .. tostring(s and s.city_id)
		.. " route " .. tostring(s and s.route_id) .. " laps " .. tostring(s and s.laps))
	apply(s)
	net_MpEnterPlayMode(UIEnums.PlayMode.CustomRace)
	local create = get("MpModeSelect_CreateGame")
	Amax.SetupRace(create())
	log("SetupRace done")
	M.tries = 0
	try_create()
end

-- the start screen itself in front, at its mode select stage (not "Previously on Blur" / a popup on top of it)
local function is_start_select(gui)
	return type(gui) == "table" and gui.STAGE_MODE_SELECT ~= nil and gui.current_stage == gui.STAGE_MODE_SELECT
end

-- The start screen's FrameUpdate pushes Intro\PreviouslyOnBlur.lua (the career recap) when
-- Amax.CanShowPreviouslyOnBlur() and UIGlobals.CanShowPreviouslyOnBlur are true - sometimes right when our walk
-- began, and the walk then ran on top of it (broken menus, user 2026-10-20). For a quick start it is switched off:
-- the flag the game itself sets to false after showing it once (the key already exists - no new table entry).
local function no_previously_on_blur()
	if M.pob_off or type(UIGlobals) ~= "table" then
		return
	end
	if UIGlobals.CanShowPreviouslyOnBlur == true then
		UIGlobals.CanShowPreviouslyOnBlur = false
		M.pob_off = true
		log("Previously on Blur switched off for this start")
	end
end

local function at_mode_select()
	local c = ctx()
	local gui = (c and c.GUI) or GUI
	return type(gui) == "table" and gui.STAGE_MODE_SELECT ~= nil and gui.current_stage == gui.STAGE_MODE_SELECT
		and type(UIGlobals) == "table" and UIGlobals.ProfileBootFinished == true
		and type(get("EnterMpRaceBook")) == "function"
end

-- where(gui) = true when the current context is the screen the step belongs to: a step runs only there (the
-- game's own buttons run inside their screen's context; GoScreen from another context showed a half-built screen)
local function queue(step, name, fn, frames, where)
	M.step = step
	M.pending = { name = name, fn = fn, frames = frames or 3, where = where }
	log("queued " .. name)
end

local function cur_gui()
	local c = ctx()
	return c and c.GUI
end

local function is_lan_screen(gui)            -- MpLan: its lobby list
	return type(gui) == "table" and gui.lobby_list_id ~= nil
end

local function is_mode_select(gui)           -- MpModeSelect: options playlists / custom_race
	return type(gui) == "table" and type(gui.options) == "table" and gui.options.custom_race ~= nil
end

-- NetRace.CreateLanServer returned false right after the host screen opened (the LAN search / popups were still
-- busy): try again every 30 frames of the host screen, up to 20 times
try_create = function()
	M.tries = M.tries + 1
	-- as recorded from the real buttons (2026-10-16): in LAN mode (7), after Mp_ResetCustomRace /
	-- net_MpEnterPlayMode / Amax.SetupRace, NetRace.CreateLanServer(MpModeSelect_CreateGame()) -> true -> GoSubScreen
	local mode = Amax.GetGameMode and Amax.GetGameMode()
	local made = NetRace.CreateLanServer(get("MpModeSelect_CreateGame")())
	log("CreateLanServer try " .. M.tries .. " (mode " .. tostring(mode) .. ") -> " .. tostring(made))
	if made == true then
		GoSubScreen("Multiplayer\\MpLobby.lua")
		log("GoSubScreen lobby done")
		if M.track_later then
			-- created with the game's default track: set ours in the lobby, like its custom race screen does
			-- (recorded: Amax.SetupRace({... city_id = -127, route_id = -126 ...}))
			queue(5, "our track in the lobby", function()
				apply(UIGlobals.CustomRaceSettings)
				Amax.SetupRace(get("MpModeSelect_CreateGame")())
				log("lobby track set")
				M.step = -1
			end, 40)
		else
			M.step = -1
		end
	elseif M.tries == 3 and not M.track_later then
		-- not with our track: back to the game's defaults (as the real button), ours later in the lobby
		get("Mp_ResetCustomRace")()
		Amax.SetupRace(get("MpModeSelect_CreateGame")())
		M.track_later = true
		log("creating with the default track, ours goes in from the lobby")
		queue(4, "create LAN game", try_create, 30, is_mode_select)
	elseif M.tries < 8 then
		queue(4, "create LAN game", try_create, 30, is_mode_select)
	else
		log("gave up: CreateLanServer stayed false")
		M.step = -1
	end
end

local WATCH = { "EnterMpRaceBook", "OpenApp", "ConnectionCallback", "MpLan_SetupGame", "MpModeSelect_CreateGame",
	"MpLobby_HostCanPressStart", "FrameUpdate", "MessageUpdate" }

-- every frame, after the game's own frame
-- ---- RECORD mode (target.record = true): the user clicks, the plugin writes every important call --------------
-- (name, arguments, results, game mode, context) into the history: what the real buttons do, to copy exactly.
local REC_GLOBALS = { "GoScreen", "GoSubScreen", "PushScreen", "PopScreen", "net_MpEnter", "net_MpLeave",
	"net_MpEnterPlayMode", "net_StartServiceConnection", "net_StopSessionEnumerator", "net_FlushSessionEnumerator",
	"net_CloseAllSessions", "OpenApp", "LaunchScreen", "Mp_ResetCustomRace", "MpModeSelect_CreateGame",
	"LeaveMultiplayerMode", "ConnectionCallback", "StorageCallback", "EnterMpRaceBook", "Mp_ReturnToPartyLobby" }
local REC_FIELDS = { { "Amax", "SetGameMode" }, { "Amax", "SetPlayMode" }, { "Amax", "SetupRace" },
	{ "NetRace", "CreateLanServer" }, { "NetRace", "EnumerateLanGames" }, { "NetRace", "SetRaceParameters" },
	{ "NetRace", "Delete" }, { "UIScreen", "SetNextScreen" }, { "Multiplayer", "GetCustomRaceDefaults" } }

local function fmt(v, depth)
	if type(v) == "table" then
		if (depth or 0) > 0 then
			return "{..}"
		end
		local parts, n = {}, 0
		for k, x in pairs(v) do
			n = n + 1
			if n > 24 then
				parts[#parts + 1] = "..."
				break
			end
			parts[#parts + 1] = tostring(k) .. "=" .. fmt(x, (depth or 0) + 1)
		end
		return "{" .. table.concat(parts, " ") .. "}"
	elseif type(v) == "function" then
		return "fn"
	end
	return tostring(v)
end

local function pack(...)
	return { n = select("#", ...), ... }
end

local function fmt_all(t)
	local parts = {}
	for i = 1, t.n do
		parts[#parts + 1] = fmt(t[i])
	end
	return table.concat(parts, ", ")
end

local function rec_wrap(name, f)
	local w
	w = function(...)
		local args = pack(...)
		local res = pack(f(...))
		local okm, mode = pcall(function() return Amax.GetGameMode() end)
		local okc, cx = pcall(UIScreen.Context)
		log("CALL " .. name .. "(" .. fmt_all(args) .. ") -> " .. fmt_all(res) .. "   [mode " .. tostring(mode)
			.. " ctx " .. tostring(cx) .. "]")
		return unpack(res, 1, res.n)
	end
	M.recw[w] = true
	return w
end

local function rec_tick()
	if not M.recw then
		M.recw = {}                    -- made here (the game's Lua), not at load time (see M.seen)
	end
	for _, name in ipairs(REC_GLOBALS) do
		local f = rawget(_G, name)
		if type(f) == "function" and not M.recw[f] then
			_G[name] = rec_wrap(name, f)
		end
	end
	for _, tf in ipairs(REC_FIELDS) do
		local tbl = rawget(_G, tf[1])
		if type(tbl) == "table" then
			local f = tbl[tf[2]]
			if type(f) == "function" and not M.recw[f] then
				tbl[tf[2]] = rec_wrap(tf[1] .. "." .. tf[2], f)
			end
		end
	end
end

local function tick()
	if T.record then
		M.frames = M.frames + 1
		pcall(rec_tick)
		return
	end
	if M.step < 0 then
		return
	end
	M.frames = M.frames + 1
	local lm = rawget(_G, "LeaveMultiplayerMode")
	if type(lm) == "function" and lm ~= M.lm_wrapper then
		M.lm_wrapper = function(...)
			M.left_mp = true
			log("the multiplayer menu ran LeaveMultiplayerMode")
			return lm(...)
		end
		_G.LeaveMultiplayerMode = M.lm_wrapper
	end
	-- which screen this context is, from the functions in ITS table (apps like the LAN screen are opened with
	-- UIScreen.SetNextScreen inside the multiplayer menu: no GoScreen to watch)
	local c = ctx()
	-- a screen script's helper functions are plain GLOBALS (the context table only gets FrameUpdate / MessageUpdate
	-- ...): a screen is recognised by its GoScreen (M.seen) and / or a global it defines appearing
	local function g(name)
		return type(rawget(_G, name)) == "function"
	end
	if M.frames % 120 == 0 then                                   -- heartbeat
		local seen = {}
		for _, n in ipairs(WATCH) do
			if g(n) then
				seen[#seen + 1] = n
			end
		end
		local ctxs = {}
		if type(ContextTable) == "table" then
			for k, v in pairs(ContextTable) do
				ctxs[#ctxs + 1] = tostring(k)
			end
		end
		local okc, cx = pcall(UIScreen.Context)
		log("context " .. tostring(cx) .. " of {" .. table.concat(ctxs, ",") .. "}; globals: " .. table.concat(seen, ",")
			.. "; screens seen: " .. table.concat(M.seen_list or {}, ","))
	end
	if M.step == 0 then
		pcall(no_previously_on_blur)
	end
	if not M.pending then
		if M.step == 0 and at_mode_select() then
			queue(1, "to multiplayer", to_multiplayer, 20, is_start_select)   -- let the cubes finish appearing
		elseif M.step == 1 and M.seen and M.seen["multiplayer\\mpmain.lua"] and g("OpenApp")
				and (M.left_mp or M.frames - M.mpmain_frame > 900) then
			-- recorded: ~185 frames after the menu opens it runs its own cleanup (LaunchScreen(0) ... net_MpLeave,
			-- NetRace.Delete, LeaveMultiplayerMode -> mode 3): LAN must come AFTER that or it is undone
			queue(2, "LAN", to_lan, 45)                             -- the multiplayer menu
		elseif M.step == 2 and g("MpLan_SetupGame") and is_lan_screen(cur_gui()) then
			queue(3, "host", to_host, 45, is_lan_screen)            -- the LAN screen (an app: no GoScreen)
		elseif M.step == 3 and g("MpModeSelect_CreateGame") and is_mode_select(cur_gui()) then
			queue(4, "custom race", to_lobby, 25, is_mode_select)   -- the host (mode select) screen
		end
	end
	local p = M.pending
	if p then
		if p.where and not p.where(cur_gui()) then
			return                                                  -- not this screen's context: wait
		end
		p.frames = p.frames - 1
		if p.frames <= 0 then
			M.pending = false
			log("running " .. p.name)
			local ok, err = pcall(p.fn)
			if not ok then
				log(p.name .. " FAILED: " .. tostring(err))
				M.step = -1
			end
		end
	end
end

local function start_ticking()
	if M.ticking or type(FrameUpdate_Wrapper) ~= "function" then
		return
	end
	M.ticking = true
	local ORG = FrameUpdate_Wrapper
	FrameUpdate_Wrapper = function(...)
		local r = ORG(...)
		tick()
		return r
	end
end

function M.GoScreen(screen, _opts)
	if not T.enabled or (not T.record and (not T.auto or M.step < 0)) then
		return
	end
	start_ticking()
	local s = string.lower(tostring(screen))
	if not M.seen then
		-- NOT at load time: this file's top level runs in lua5.1.dll (Amax's loader), and an EMPTY table made there
		-- points at the DLL's dummy node; the game's own Lua then writes into the DLL on the first insert = crash
		-- (Blur_mod.exe+0x6E8519 writing lua5.1.dll+0x3DA88, 2026-10-16). Tables made here are the game's.
		M.seen = {}
		M.seen_list = {}
	end
	if not M.seen[s] then
		M.seen[s] = true
		if s == "multiplayer\\mpmain.lua" then
			M.mpmain_frame = M.frames
		end
		M.seen_list[#M.seen_list + 1] = s
		log("screen " .. s)
	end
	if s == "multiplayer\\mplobby.lua" then
		M.step = -1                -- done: from here on the menus are yours
	end
end

M.PushScreen = M.GoScreen

function M.init()
	local ok, t = pcall(require, "quickstart.target")
	if ok and type(t) == "table" then
		T = t
	end
	log("target: " .. table.concat(T.city_names or {}, "/") .. " > " .. table.concat(T.route_names or {}, "/"))
	if T.enabled and (T.auto or T.record) then
		pcall(start_ticking)
	end
	return true
end

return M

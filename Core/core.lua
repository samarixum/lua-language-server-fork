local util    = require('script.utility')
local version = require('script.version')

--local useMoonSharpPaths = _MOONSHARP == true
local fs = require('bee.filesystem')

require('script.config.env')

local function createCodeFormatFallback()
    local fallback = {}

    function fallback.set_default_config() end
    function fallback.set_clike_comments_symbol() end
    function fallback.set_nonstandard_symbol() end
    function fallback.update_config() return true end

    function fallback.update_name_style_config() end
    function fallback.name_style_analysis()
        return true, {}
    end

    function fallback.spell_load_dictionary_from_path()
        return true
    end

    function fallback.spell_load_dictionary_from_buffer()
        return true
    end

    function fallback.spell_analysis()
        return true, {}
    end

    function fallback.spell_suggest()
        return true, {}
    end

    function fallback.diagnose_file()
        return true, {}
    end

    function fallback.format(_, text)
        return true, text
    end

    function fallback.range_format(_, text)
        return true, text, 0, 0
    end

    function fallback.type_format(_, text)
        return true, text, 0, 0
    end

    return fallback
end

do
    package.preload = package.preload or {}
    package.preload['code_format'] = function()
        return createCodeFormatFallback()
    end
end

local function getValue(value)
    if value == 'true' or value == nil then
        value = true
    elseif value == 'false' then
        value = false
    elseif tonumber(value) then
        value = tonumber(value)
    elseif value:sub(1, 1) == '"' and value:sub(-1, -1) == '"' then
        value = value:sub(2, -2)
    end
    return value
end

local function loadArgs()
    ---@type string?
    local lastKey
    for _, v in ipairs(arg) do
        ---@type string?
        local key, tail = v:match '^%-%-([%w_]+)(.*)$'
        local value
        if key then
            value   = tail:match '=(.+)'
            lastKey = nil
            if not value then
                lastKey = key
            end
        else
            if lastKey then
                key     = lastKey
                value   = v
                lastKey = nil
            end
        end
        if key then
            _G[key:upper():gsub('-', '_')] = getValue(value)
        end
    end
end

loadArgs()

local currentPath = debug.getinfo(1, 'S').source:sub(2)
print('Current path:', currentPath)
local rootPath    = currentPath:gsub('[/\\]*[^/\\]-$', '')
print('Root path:', rootPath)


rootPath = (rootPath == '' and '.' or rootPath)
print('Expanded root path:', (rootPath))
ROOT     = fs.path(((util.expandPath(rootPath))))
print('ROOT path:', ROOT)

local function resolvePath(pathValue, fallback)
    if type(pathValue) == 'string' then
        return fs.path(((util.expandPath(pathValue)) or fallback))
    end
    if pathValue then
        return fs.path(pathValue)
    end
    return fs.path(fallback)
end

LOGPATH  = resolvePath(LOGPATH, ROOT / 'log')
print('LOGPATH:', LOGPATH)
METAPATH = resolvePath(METAPATH, ROOT / 'meta')
print('METAPATH:', METAPATH)

util.enableCloseFunction()
util.enableFormatString()

--collectgarbage('generational', 10, 50)
--collectgarbage('incremental', 120, 120, 0)
collectgarbage('param', 'minormul', 10)
collectgarbage('param', 'minormajor', 50)

LOGLEVEL = LOGLEVEL or 'debug'

---@diagnostic disable-next-line: lowercase-global
log = require 'script.log'
log.init(ROOT, LOGPATH / 'service.log')
if LOGLEVEL then
    log.level = tostring(LOGLEVEL):lower()
end

print('Lua _VERSION: ', _VERSION)                         -- 'MoonSharp 2.0.0.0' or 'Lua 5.5'
if _MOONSHARP then
    print('_MOONSHARP.version: ', _MOONSHARP.version)     -- '2.0.0.0'
    print('_MOONSHARP.luacompat: ', _MOONSHARP.luacompat) -- 'Lua 5.2'
    print('_MOONSHARP.platform: ', _MOONSHARP.platform)   -- 'core.dotnet.clr4.netcore'
    print('_MOONSHARP.is_aot: ', _MOONSHARP.is_aot)       -- false
    print('_MOONSHARP.is_unity: ', _MOONSHARP.is_unity)   -- false
    print('_MOONSHARP.is_mono: ', _MOONSHARP.is_mono)     -- false
    print('_MOONSHARP.is_clr4: ', _MOONSHARP.is_clr4)     -- true
    print('_MOONSHARP.is_pcl: ', _MOONSHARP.is_pcl)       -- false
    print('_MOONSHARP.banner: ', _MOONSHARP.banner)       -- """Copyright (C) 2014-2016 Marco Mastropaolo \nhttp://www.moonsharp.org"""
end

log.info('Lua Lsp startup, root: ', ROOT)
log.info('ROOT:', ROOT:string())
log.info('LOGPATH:', LOGPATH)
log.info('METAPATH:', METAPATH)
log.info('VERSION:', version.getVersion())

print('including script/tracy.lua')
require 'script.tracy'

print('xpcall dofile debugger.lua')
xpcall(dofile, log.debug, (ROOT / 'debugger.lua'):string())

print('including script/cli/init.lua')
require 'script.cli.init'

print('setup service')
local ok, service = xpcall(require, log.error, 'script.service.service')
if not ok then
    error(service)
end

print('start service')
service.start()

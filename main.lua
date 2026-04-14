local util    = require 'utility'
local version = require 'version'

local useMoonSharpPaths = _MOONSHARP == true
local fs = nil
if not useMoonSharpPaths then
    fs = require 'bee.filesystem'
end

local function joinPath(left, right)
    local separator = package.config:sub(1, 1)
    left = tostring(left)
    right = tostring(right)

    if left:sub(-1) ~= separator then
        left = left .. separator
    end

    if right:sub(1, 1) == separator then
        right = right:sub(2)
    end

    return left .. right
end

local function makePath(pathText)
    local pathValue = tostring(pathText)

    if not useMoonSharpPaths then
        return fs.path(pathValue)
    end

    return setmetatable({ value = pathValue }, {
        __tostring = function(self)
            return self.value
        end,
        __index = {
            string = function(self)
                return self.value
            end,
        },
        __div = function(self, rhs)
            return makePath(joinPath(self.value, rhs))
        end,
    })
end

require 'config.env'

local function getValue(value)
    if     value == 'true' or value == nil then
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
local rootPath    = currentPath:gsub('[/\\]*[^/\\]-$', '')

rootPath = (rootPath == '' and '.' or rootPath)
ROOT     = makePath(util.expandPath(rootPath))
LOGPATH  = LOGPATH  and makePath(util.expandPath(LOGPATH))  or (ROOT / 'log')
METAPATH = METAPATH and makePath(util.expandPath(METAPATH)) or (ROOT / 'meta')

util.enableCloseFunction()
util.enableFormatString()

--collectgarbage('generational', 10, 50)
--collectgarbage('incremental', 120, 120, 0)
collectgarbage('param', 'minormul', 10)
collectgarbage('param', 'minormajor', 50)

---@diagnostic disable-next-line: lowercase-global
log = require 'log'
log.init(ROOT, useMoonSharpPaths and (LOGPATH / 'service.log') or (fs.path(LOGPATH) / 'service.log'))
if LOGLEVEL then
    log.level = tostring(LOGLEVEL):lower()
end

log.info('Lua Lsp startup, root: ', ROOT)
log.info('ROOT:', ROOT:string())
log.info('LOGPATH:', LOGPATH)
log.info('METAPATH:', METAPATH)
log.info('VERSION:', version.getVersion())

require 'tracy'

xpcall(dofile, log.debug, (ROOT / 'debugger.lua'):string())

require 'cli'

local _, service = xpcall(require, log.error, 'service')

service.start()

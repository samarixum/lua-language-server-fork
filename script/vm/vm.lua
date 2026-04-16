
print('including script/vm.lua')
local guide     = require("script.parser.guide")
local files     = require("script.files")
local timer     = require("script.timer")
local setmetatable   = setmetatable
local log            = log
local logError       = log and log.error or function (err)
    print(err)
end
local xpcall         = xpcall
local mathHuge       = math.huge

print('vm-1')

local weakMT = { __mode = 'kv' }

---@class vm
local m = {}

m.ID_SPLITE = '\x1F'

function m.getSpecial(source)
    if not source then
        return nil
    end
    return source.special
end

print('vm-2')

---@param source parser.object
---@return string?
function m.getKeyName(source)
    if not source then
        return nil
    end
    if source.type == 'call' then
        local special = m.getSpecial(source.node)
        if special == 'rawset'
        or special == 'rawget' then
            return guide.getKeyNameOfLiteral(source.args[2])
        end
    end
    return guide.getKeyName(source)
end

print('vm-3')


function m.getKeyType(source)
    if not source then
        return nil
    end
    if source.type == 'call' then
        local special = m.getSpecial(source.node)
        if special == 'rawset'
        or special == 'rawget' then
            return guide.getKeyTypeOfLiteral(source.args[2])
        end
    end
    return guide.getKeyType(source)
end

print('vm-4')

---@param source parser.object
---@return parser.object?
function m.getObjectValue(source)
    if source.value then
        return source.value
    end
    if source.special == 'rawset' then
        return source.args and source.args[3]
    end
    return nil
end

print('vm-5')

---@param source parser.object
---@return parser.object?
function m.getObjectFunctionValue(source)
    local value = m.getObjectValue(source)
    if value == nil then return end
    if value.type == 'function' or value.type == 'doc.type.function' then
        return value
    end
    if value.type == 'getlocal' then
        return m.getObjectFunctionValue(value.node)
    end
    return value
end

m.cacheTracker = setmetatable({}, weakMT)

print('vm-6')

function m.flushCache()
    if m.cache then
        m.cache.dead = true
    end
    m.cacheVersion = files.globalVersion
    m.cache = {}
    m.cacheActiveTime = mathHuge
    m.locked = setmetatable({}, weakMT)
    m.cacheTracker[m.cache] = true
end

print('vm-7')

function m.getCache(name, weak)
    if m.cacheVersion ~= files.globalVersion then
        m.flushCache()
    end
    m.cacheActiveTime = timer.clock()
    if not m.cache[name] then
        m.cache[name] = weak and setmetatable({}, weakMT) or {}
    end
    return m.cache[name]
end

print('vm-8')

local function init()
    m.flushCache()

    -- 可以在一段时间不活动后清空缓存，不过目前看起来没有必要
    --timer.loop(1, function ()
    --    if timer.clock() - m.cacheActiveTime > 10.0 then
    --        log.info('Flush cache: Inactive')
    --        m.flushCache()
    --        collectgarbage()
    --    end
    --end)
end

print('vm-9')

xpcall(init, logError)

print('vm.lua loaded')

return m

local timer = require("script.timer")

local wkmt = { __mode = 'k' }

---@class await
local m = {}
m.type = 'await'

m.coMap = setmetatable({}, wkmt)
m.idMap = {}
m.delayQueue = {}
m.delayQueueIndex = 1
m.needClose = {}
m._enable = true

-- Lua 5.2 compatibility for checking if a coroutine can yield
local function is_yieldable(co)
    if coroutine.isyieldable then
        if co then return coroutine.isyieldable(co) else return coroutine.isyieldable() end
    end
    local current_co, is_main = coroutine.running()
    if co and co ~= current_co then
        return coroutine.status(co) == 'suspended' or coroutine.status(co) == 'running'
    end
    return not is_main
end

local function setID(id, co, callback)
    if not is_yieldable(co) then
        return
    end
    if not m.idMap[id] then
        m.idMap[id] = setmetatable({}, wkmt)
    end
    m.idMap[id][co] = callback or true
end

--- Set error handler
---@param errHandle function # Function called with the error stack trace as an argument when an error occurs
function m.setErrorHandle(errHandle)
    m.errorHandle = errHandle
end

function m.checkResult(co, ...)
    local suc, err = ...
    if not suc and m.errorHandle then
        m.errorHandle(debug.traceback(co, err))
    end
    return ...
end

--- Create a task
---@param callback async fun()
function m.call(callback, ...)
    local co = coroutine.create(callback)
    local closers = {}
    m.coMap[co] = {
        closers  = closers,
        priority = false,
    }
    for i = 1, select('#', ...) do
        local id = select(i, ...)
        if not id then
            break
        end
        setID(id, co)
    end

    local currentCo = coroutine.running()
    local current = m.coMap[currentCo]
    if current then
        for closer in pairs(current.closers) do
            closers[closer] = true
            closer(co)
        end
    end
    return m.checkResult(co, coroutine.resume(co))
end

--- Create a task, suspend the current thread, and resume it once the task is completed / return nil if the task is closed
---@async
function m.await(callback, ...)
    if not is_yieldable() then
        return callback(...)
    end
    return m.wait(function (resume, ...)
        m.call(function ()
            -- pcall is used for Lua 5.2 compatibility to replace 5.4's <close> feature
            local ok, result = pcall(callback)
            if ok then
                resume(result)
            else
                resume()
                error(result)
            end
        end, ...)
    end, ...)
end

--- Set an id for batch closing tasks
function m.setID(id, callback)
    local co = coroutine.running()
    setID(id, co, callback)
end

--- Batch close tasks by id
function m.close(id)
    local map = m.idMap[id]
    if not map then
        return
    end
    m.idMap[id] = nil
    for co, callback in pairs(map) do
        if coroutine.status(co) == 'suspended' then
            map[co] = nil
            if type(callback) == 'function' then
                xpcall(callback, log.error)
            end
            -- Note: coroutine.close(co) is omitted here as it is Lua 5.4 specific.
            -- In Lua 5.2, unreferenced coroutines are automatically garbage collected.
        end
    end
end

function m.hasID(id, co)
    co = co or coroutine.running()
    return m.idMap[id] and m.idMap[id][co] ~= nil
end

function m.unique(id, callback)
    m.close(id)
    m.setID(id, callback)
end

--- Sleep for a period of time
---@param time number
---@async
function m.sleep(time)
    if not is_yieldable() then
        if m.errorHandle then
            m.errorHandle(debug.traceback('Cannot yield'))
        end
        return
    end
    local co = coroutine.running()
    timer.wait(time, function ()
        if coroutine.status(co) ~= 'suspended' then
            return
        end
        return m.checkResult(co, coroutine.resume(co))
    end)
    return coroutine.yield()
end

--- Wait until awakened
---@param callback function
---@async
function m.wait(callback, ...)
    local co = coroutine.running()
    local resumed
    callback(function (...)
        if resumed then
            return
        end
        resumed = true
        if coroutine.status(co) ~= 'suspended' then
            return
        end
        return m.checkResult(co, coroutine.resume(co, ...))
    end, ...)
    return coroutine.yield()
end

--- Delay
---@async
function m.delay()
    if not m._enable then
        return
    end
    if not is_yieldable() then
        return
    end
    local co = coroutine.running()
    local current = m.coMap[co]
    -- TODO
    if current.priority then
        return
    end
    m.delayQueue[#m.delayQueue+1] = function ()
        if coroutine.status(co) ~= 'suspended' then
            return
        end
        return m.checkResult(co, coroutine.resume(co))
    end
    return coroutine.yield()
end

local throttledDelayer = {}
throttledDelayer.__index = throttledDelayer

---@async
function throttledDelayer:delay()
    if not m._enable then
        return
    end
    self.calls = self.calls + 1
    if self.calls == self.factor then
        self.calls = 0
        return m.delay()
    end
end

function m.newThrottledDelayer(factor)
    return setmetatable({
        factor = factor,
        calls = 0,
    }, throttledDelayer)
end

--- Stop then close
---@async
function m.stop()
    if not is_yieldable() then
        return
    end
    m.needClose[#m.needClose+1] = coroutine.running()
    coroutine.yield()
end

local function warnStepTime(passed, waker)
    if passed < 2 then
        log.warn(('Await step takes [%.3f] sec.'):format(passed))
        return
    end
    for i = 1, 100 do
        local name, v = debug.getupvalue(waker, i)
        if not name then
            return
        end
        if name == 'co' then
            log.warn(debug.traceback(v, ('[fire]Await step takes [%.3f] sec.'):format(passed)))
            return
        end
    end
end

--- Step
function m.step()
    for i = #m.needClose, 1, -1 do
        -- Note: coroutine.close(m.needClose[i]) is omitted for 5.2 compatibility
        m.needClose[i] = nil
    end

    local resume = m.delayQueue[m.delayQueueIndex]
    if resume then
        m.delayQueue[m.delayQueueIndex] = false
        m.delayQueueIndex = m.delayQueueIndex + 1
        local clock = os.clock()
        resume()
        local passed = os.clock() - clock
        if passed > 0.5 then
            warnStepTime(passed, resume)
        end
        return true
    else
        for i = 1, #m.delayQueue do
            m.delayQueue[i] = nil
        end
        m.delayQueueIndex = 1
        return false
    end
end

function m.setPriority(n)
    m.coMap[coroutine.running()].priority = true
end

function m.enable()
    m._enable = true
end

function m.disable()
    m._enable = false
end

return m

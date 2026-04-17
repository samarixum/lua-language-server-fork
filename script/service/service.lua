print('#############################loading service/service.lua')


print('service-1')
local pub    = require("script.pub.pub")
print('service-1')
local await  = require("script.await")
print('service-2')
local timer  = require("script.timer")
print('service-3')
local proto  = require("script.proto")
print('service-4')
local vm     = require("script.vm")
print('service-5')
local util   = require("script.utility")
print('service-6')
local files  = require("script.files")
print('service-7')
local lang   = require("script.language")
print('service-8')
local ws     = require("script.workspace")
print('service-9')
local time   = require("bee.time")
print('service-10')
local fw     = require("script.filewatch")
print('service-11')
local furi   = require("script.file-uri")
print('service-12')
local net    = require("script.service.net")
print('service-13')
local client = require("script.client")
print('service-14')
require("script.jsonc")
print('service-15')
require("script.json-beautify")
print('service-16')


---@class service
local m = {}
m.type = 'service'
m.idleClock = 0.0
m.sleeping = false

local function countMemory()
    local mems = {}
    local total  = 0
    local memory = collectgarbage 'count' or 0
    mems[0] = memory
    total = total + memory
    for id, brave in ipairs(pub.allBraves) do
        local memory = brave.memory or 0
        mems[id] = memory
        total = total + memory
    end
    return total, mems
end

function m.reportMemoryCollect()
    local totalMemBefore = countMemory()
    local clock = os.clock()
    collectgarbage()
    local passed = os.clock() - clock
    local totalMemAfter, mems  = countMemory()

    local lines = {}
    lines[#lines+1] = '    --------------- Memory ---------------'
    lines[#lines+1] = ('        Total: %.3f(%.3f) MB'):format(totalMemAfter / 1000.0, totalMemBefore / 1000.0)
    for i = 0, #mems do
        lines[#lines+1] = ('        # %02d : %.3f MB'):format(i, mems[i] / 1000.0)
    end
    lines[#lines+1] = ('        Collect garbage takes [%.3f] sec'):format(passed)
    return table.concat(lines, '\n')
end

function m.reportMemory()
    local totalMem, mems = countMemory()

    local lines = {}
    lines[#lines+1] = '    --------------- Memory ---------------'
    lines[#lines+1] = ('        Total: %.3f MB'):format(totalMem / 1000.0)
    for i = 0, #mems do
        lines[#lines+1] = ('        # %02d : %.3f MB'):format(i, mems[i] / 1000.0)
    end
    return table.concat(lines, '\n')
end

function m.reportTask()
    local total     = 0
    local running   = 0
    local suspended = 0
    local normal    = 0
    local dead      = 0

    for co in pairs(await.coMap) do
        total = total + 1
        local status = coroutine.status(co)
        if status == 'running' then
            running = running + 1
        elseif status == 'suspended' then
            suspended = suspended + 1
        elseif status == 'normal' then
            normal = normal + 1
        elseif status == 'dead' then
            dead = dead + 1
        end
    end

    local lines = {}
    lines[#lines+1] = '    --------------- Coroutine ---------------'
    lines[#lines+1] = ('        Total:     %d'):format(total)
    lines[#lines+1] = ('        Running:   %d'):format(running)
    lines[#lines+1] = ('        Suspended: %d'):format(suspended)
    lines[#lines+1] = ('        Normal:    %d'):format(normal)
    lines[#lines+1] = ('        Dead:      %d'):format(dead)
    return table.concat(lines, '\n')
end

function m.reportCache()
    local total = 0
    local dead  = 0

    for cache in pairs(vm.cacheTracker) do
        total = total + 1
        if cache.dead then
            dead = dead + 1
        end
    end

    local lines = {}
    lines[#lines+1] = '    --------------- Cache ---------------'
    lines[#lines+1] = ('        Total: %d'):format(total)
    lines[#lines+1] = ('        Dead:  %d'):format(dead)
    return table.concat(lines, '\n')
end

function m.reportProto()
    local holdon  = 0
    local waiting = 0

    for _ in pairs(proto.holdon) do
        holdon = holdon + 1
    end
    for _ in pairs(proto.waiting) do
        waiting = waiting + 1
    end

    local lines = {}
    lines[#lines+1] = '    ---------------  RPC  ---------------'
    lines[#lines+1] = ('        Holdon:   %d'):format(holdon)
    lines[#lines+1] = ('        Waiting:  %d'):format(waiting)
    return table.concat(lines, '\n')
end

function m.report()
    local t = timer.loop(600.0, function ()
        local lines = {}
        lines[#lines+1] = ''
        lines[#lines+1] = '========= Medical Examination Report ========='
        lines[#lines+1] = m.reportMemory()
        lines[#lines+1] = m.reportTask()
        lines[#lines+1] = m.reportCache()
        lines[#lines+1] = m.reportProto()
        lines[#lines+1] = '=============================================='

        log.info(table.concat(lines, '\n'))
    end)
    t:onTimer()
end

function m.eventLoop()
    pub.task('timer', 1)
    pub.on('wakeup', function ()
        m.reportStatus()
        fw.update()
    end)

    local function busy()
        if not m.workingClock then
            m.workingClock = time.monotonic()
            m.reportStatus()
        end
    end

    local function idle()
        if m.workingClock then
            m.workingClock = nil
            m.reportStatus()
        end
    end

    local function doSomething()
        timer.update()
        pub.step(false)
        if await.step() then
            busy()
            return true
        end
        return false
    end

    local function sleep()
        while true do
            idle()
            for _ = 1, 10 do
                net.update(100)
                if doSomething() then
                    return
                end
            end
            pub.step(true)
        end
    end

    while true do
        net.update()
        log.debug('net update')
        local clock = time.monotonic()
        while time.monotonic() - clock < 100 do
            doSomething()
        end
        if doSomething() then
            goto CONTINUE
        end
        sleep()
        ::CONTINUE::
    end
end

local showStatusTip = math.random(100) == 1

function m.reportStatus()
    if not client.getOption('statusBar') then
        return
    end
    local info = {}
    if m.workingClock and time.monotonic() - m.workingClock > 100 then
        info.text = '$(loading~spin)Lua'
    elseif m.sleeping then
        info.text = "💤Lua"
    else
        info.text = '😺Lua'
    end

    local tooltips = {}
    local memory = collectgarbage('count') or 0
    local params = {
        ast = files.countStates(),
        max = files.fileCount,
        mem = memory / 1000,
    }
    for i, scp in ipairs(ws.folders) do
        tooltips[i] = lang.script('WINDOW_LUA_STATUS_WORKSPACE', furi.decode(scp.uri))
    end
    tooltips[#tooltips+1] = lang.script('WINDOW_LUA_STATUS_CACHED_FILES', params)
    tooltips[#tooltips+1] = lang.script('WINDOW_LUA_STATUS_MEMORY_COUNT', params)
    if showStatusTip then
        tooltips[#tooltips+1] = lang.script('WINDOW_LUA_STATUS_TIP')
    end

    info.tooltip = table.concat(tooltips, '\n')
    if util.equal(m.lastInfo, info) then
        return
    end
    m.lastInfo = info
    proto.notify('$/status/report', info)
end

function m.sayHello()
    proto.notify('$/hello', {'world'})
end

function m.lockCache()
    local fs = require("bee.filesystem")
    local sp = require("bee.subprocess")
    local cacheDir = string.format('%s/cache', LOGPATH:string())
    local myCacheDir = string.format('%s/%d'
        , cacheDir
        , sp.get_id()
    )
    fs.create_directories(fs.path(myCacheDir))
    local err
    m.lockFile, err = io.open(myCacheDir .. '/.lock', 'wb')
    if err then
        log.error(err)
    end
end

function m.start()
    util.enableCloseFunction()
    await.setErrorHandle(log.error)
    pub.recruitBraves(4)
    if COMPILECORES and COMPILECORES > 0 then
        pub.recruitBraves(COMPILECORES, 'compile')
    end
    if SOCKET then
        assert(math.tointeger(SOCKET), '`socket` must be integer')
        proto.listen('socket', SOCKET)
    else
        proto.listen('stdio')
    end
    m.report()
    m.lockCache()

    require("script.provider")

    m.sayHello()

    m.eventLoop()
end

return m

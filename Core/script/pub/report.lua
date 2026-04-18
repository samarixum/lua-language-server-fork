local pub   = require("script.pub.pub")
local await = require("script.await")

pub.on('log', function (params, brave)
    log.raw(brave.id, params.level, params.msg, params.src, params.line, params.clock)
end)

pub.on('mem', function (count, brave)
    brave.memory = count
end)

pub.on('proto', function (params)
    local proto = require("script.proto")
    await.call(function ()
        if params.method then
            proto.doMethod(params)
        else
            proto.doResponse(params)
        end
    end)
end)

pub.on('protoerror', function (err)
    log.warn('Load proto error:', err)
    os.exit(0, true)
end)

pub.on('wakeup', function () end)

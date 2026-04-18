local files = require("script.files")
local guide = require("script.parser.guide")
local vm    = require("script.vm")
local lang  = require("script.language")
local await = require("script.await")

---@async
return function (uri, callback)
    local state = files.getState(uri)
    if not state then
        return
    end

    ---@async
    guide.eachSourceType(state.ast, 'call', function (source)
        await.delay()
        local currentFunc = guide.getParentFunction(source)
        if currentFunc and source.node.special == 'assert' and source.args[1] then
            local argNode = vm.compileNode(source.args[1])
            if argNode:alwaysTruthy() then
                callback {
                    start   = source.node.start,
                    finish  = source.node.finish,
                    message = lang.script('DIAG_UNNECESSARY_ASSERT'),
                }
            end
        end
    end)
end

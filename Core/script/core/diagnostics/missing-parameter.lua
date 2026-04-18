local files  = require("script.files")
local guide  = require("script.parser.guide")
local vm     = require("script.vm")
local lang   = require("script.language")
local await  = require("script.await")

---@async
return function (uri, callback)
    local state = files.getState(uri)
    if not state then
        return
    end

    ---@async
    guide.eachSourceType(state.ast, 'call', function (source)
        await.delay()
        local _, callArgs = vm.countList(source.args)

        local funcNode = vm.compileNode(source.node)
        local funcArgs = vm.countParamsOfNode(funcNode)

        if callArgs >= funcArgs then
            return
        end

        callback {
            start  = source.start,
            finish = source.finish,
            message = lang.script('DIAG_MISS_ARGS', funcArgs, callArgs),
        }
    end)
end

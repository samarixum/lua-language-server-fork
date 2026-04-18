local files    = require("script.files")
local globalModule = require("script.vm.global")
local variable = require("script.vm.variable")

---@async
files.watch(function (ev, uri)
    if ev == 'update' then
        globalModule.dropUri(uri)
    end
    if ev == 'remove' then
        globalModule.dropUri(uri)
    end
    if ev == 'compile' then
        local state = files.getLastState(uri)
        if state then
            globalModule.compileAst(state.ast)
            variable.compileAst(state.ast)
        end
    end
end)

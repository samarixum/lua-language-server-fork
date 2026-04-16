local files   = require("script.files")
local guide   = require("script.parser.guide")
local lang    = require("script.language")
local vm      = require("script.vm")
local await   = require("script.await")

local types = {
    'local',
    'setlocal',
    'setglobal',
    'getglobal',
    'setfield',
    'setindex',
    'tablefield',
    'tableindex',
}

---@async
return function (uri, callback)
    local ast = files.getState(uri)
    if not ast then
        return
    end

    ---@async
    guide.eachSourceTypes(ast.ast, types, function (source)
        await.delay()
        if vm.getInfer(source):view(uri) == 'unknown' then
            callback {
                start   = source.start,
                finish  = source.finish,
                message = lang.script('DIAG_UNKNOWN'),
            }
        end
    end)
end

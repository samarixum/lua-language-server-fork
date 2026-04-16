local files  = require("script.files")
local guide  = require("script.parser.guide")
local define = require("script.proto.define")
local lang   = require("script.language")
local vm     = require("script.vm")

return function (uri, callback)
    local ast = files.getState(uri)
    if not ast then
        return
    end

    if vm.isMetaFile(uri) then
        return
    end

    guide.eachSourceType(ast.ast, 'function', function (source)
        if #source == 0 then
            return
        end
        local args = source.args
        if not args then
            return
        end

        for _, arg in ipairs(args) do
            if arg.type == '...' then
                if not arg.ref and not arg.name then
                    callback {
                        start   = arg.start,
                        finish  = arg.finish,
                        tags    = { define.DiagnosticTag.Unnecessary },
                        message = lang.script.DIAG_UNUSED_VARARG,
                    }
                end
            end
        end
    end)
end

local files   = require("script.files")
local guide   = require("script.parser.guide")
local lang    = require("script.language")
local define  = require("script.proto.define")

-- reports 'return' without any return values at the end of functions
return function (uri, callback)
    local ast = files.getState(uri)
    if not ast then
        return
    end

    guide.eachSourceType(ast.ast, 'return', function (source)
        if not source.parent or source.parent.type ~= "function" then
            return
        end
        if #source > 0 then
            return
        end
        callback {
            start   = source.start,
            finish  = source.finish,
            tags    = { define.DiagnosticTag.Unnecessary },
            message = lang.script.DIAG_REDUNDANT_RETURN,
        }
    end)
end

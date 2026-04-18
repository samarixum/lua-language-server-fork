local files  = require("script.files")
local guide  = require("script.parser.guide")
local define = require("script.proto.define")
local lang   = require("script.language")

return function (uri, callback)
    local ast = files.getState(uri)
    if not ast then
        return
    end

    guide.eachSourceType(ast.ast, 'label', function (source)
        if not source.ref then
            callback {
                start   = source.start,
                finish  = source.finish,
                tags    = { define.DiagnosticTag.Unnecessary },
                message = lang.script('DIAG_UNUSED_LABEL', source[1]),
            }
        end
    end)
end

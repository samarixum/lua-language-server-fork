local files       = require("script.files")
local converter   = require("script.proto.converter")
local log         = require("script.log")
local pformatting = require("script.provider.formatting")


---@async
return function(uri, callback)
    local state = files.getState(uri)
    if not state then
        return
    end
    local text = state.originText

    local suc, codeFormat  = pcall(require, 'code_format')
    if not suc then
        return
    end

    pformatting.updateConfig(uri)

    local status, diagnosticInfos = codeFormat.diagnose_file(uri, text)

    if not status then
        if diagnosticInfos ~= nil then
            log.error(diagnosticInfos)
        end

        return
    end

    if diagnosticInfos then
        for _, diagnosticInfo in ipairs(diagnosticInfos) do
            callback {
                start   = converter.unpackPosition(state, diagnosticInfo.range.start),
                finish  = converter.unpackPosition(state, diagnosticInfo.range["end"]),
                message = diagnosticInfo.message
            }
        end
    end
end

local files     = require("script.files")
local converter = require("script.proto.converter")
local log       = require("script.log")
local nameStyle = require("script.provider.name-style")


---@async
return function (uri, callback)
    local state = files.getState(uri)
    if not state then
        return
    end
    local text = state.originText

    local status, diagnosticInfos = nameStyle.nameStyleCheck(uri, text)

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
                message = diagnosticInfo.message,
                data    = diagnosticInfo.data
            }
        end
    end
end

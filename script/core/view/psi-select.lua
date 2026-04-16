local files = require("script.files")
local guide = require("script.parser.guide")
local converter = require("script.proto.converter")

return function(uri, position)
    local state = files.getState(uri)
    if not state then
        return
    end

    local pos = converter.unpackPosition(state, position)
    return { data = guide.positionToOffset(state, pos) }
end

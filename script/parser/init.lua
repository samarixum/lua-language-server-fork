print('including parser/init.lua')

local api = {
    compile    = require("script.parser.compile"),
    lines      = require("script.parser.lines"),
    guide      = require("script.parser.guide"),
    luadoc     = require("script.parser.luadoc").luadoc,
}

return api

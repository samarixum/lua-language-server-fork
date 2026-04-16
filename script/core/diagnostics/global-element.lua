local files     = require("script.files")
local guide     = require("script.parser.guide")
local lang      = require("script.language")
local config    = require("script.config")
local vm        = require("script.vm")
local util      = require("script.utility")

local function isDocClass(source)
    if not source.bindDocs then
        return false
    end
    for _, doc in ipairs(source.bindDocs) do
        if doc.type == 'doc.class' then
            return true
        end
    end
    return false
end

local function isGlobalRegex(name, definedGlobalRegex)
    if not definedGlobalRegex then
        return false
    end

    for _, pattern in ipairs(definedGlobalRegex) do
        if name:match(pattern) then
            return true
        end
    end

    return false
end

-- If global elements are discouraged by coding convention, this diagnostic helps with reminding about that
-- Exceptions may be added to Lua.diagnostics.globals
return function (uri, callback)
    local ast = files.getState(uri)
    if not ast then
        return
    end

    ---@type table<string, boolean?>
    local definedGlobal = util.arrayToHash(config.get(uri, 'Lua.diagnostics.globals'))
    local definedGlobalRegex = config.get(uri, 'Lua.diagnostics.globalsRegex')

    guide.eachSourceType(ast.ast, 'setglobal', function (source)
        local name = guide.getKeyName(source)
        if not name or definedGlobal[name] then
            return
        end
        -- If the assignment is marked as doc.class, then it is considered allowed 
        if isDocClass(source) then
            return
        end
        if isGlobalRegex(name, definedGlobalRegex) then
            return
        end
        if definedGlobal[name] == nil then
            definedGlobal[name] = false
            local globalVar = vm.getGlobal('variable', name)
            if globalVar then
                for _, set in ipairs(globalVar:getSets(uri)) do
                    if vm.isMetaFile(guide.getUri(set)) then
                        definedGlobal[name] = true
                        return
                    end
                end
            end
        end
        callback {
            start   = source.start,
            finish  = source.finish,
            message = lang.script.DIAG_GLOBAL_ELEMENT,
        }
    end)
end

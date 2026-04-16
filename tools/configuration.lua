--[[
    File: configuration.lua
    Purpose: Processes raw configuration templates into a structured schema for LLS.

    This script iterates through internal template definitions and maps them to
    a standardized format that includes type validation, default values, and
    localization keys for the Lua Language Server.

    Key Functions:
    * Type Mapping: Translates internal types (e.g., 'Hash', 'Or', 'Integer')
        into standard types like 'object', 'boolean', or union types.
    * Localization Key Generation: Automatically generates placeholder keys
        (e.g., %config.diagnostics.XXX%) used by the localization system to
        display human-readable descriptions in the editor.
    * Complex Structure Handling:
        * Arrays: Defines item types and valid enums for list-based settings.
        * Hashes (Objects): Handles both fixed properties (with specific keys)
            and pattern-based properties (dictionary-style settings).
    * Schema Normalization: Sets the scope to 'resource' and ensures that
        default values are correctly represented, including handling empty
        JSON objects.

    Integration:
    The resulting 'config' table is typically used by the language server
    to validate user settings in .luarc.json and by build scripts to
    generate localized documentation.
]]

local json     = require 'script.json'
local template = require 'script.config.template'
local util     = require 'script.utility'

local function getType(temp)
    if temp.name == 'Boolean' then
        return 'boolean'
    end
    if temp.name == 'String' then
        return 'string'
    end
    if temp.name == 'Integer' then
        return 'integer'
    end
    if temp.name == 'Nil' then
        return 'null'
    end
    if temp.name == 'Array' then
        return 'array'
    end
    if temp.name == 'Hash' then
        return 'object'
    end
    if temp.name == 'Or' then
        return { getType(temp.subs[1]), getType(temp.subs[2]) }
    end
    error('Unknown type: ' .. temp.name)
end

local function getDefault(temp)
    local default = temp.default
    if default == nil and temp.hasDefault then
        default = json.null
    end
    if  type(default) == 'table'
    and not next(default)
    and getType(temp) == 'object' then
        default = json.createEmptyObject()
    end
    return default
end

local function getEnum(temp)
    return temp.enums
end

local function getEnumDesc(name, temp)
    if not temp.enums then
        return nil
    end
    local descs = {}

    for _, enum in ipairs(temp.enums) do
        descs[#descs+1] = name:gsub('^Lua', '%%config') .. '.' .. enum .. '%'
    end

    return descs
end

local function insertArray(conf, temp)
    conf.items = {
        type = getType(temp.sub),
        enum = getEnum(temp.sub),
    }
end

local function insertHash(name, conf, temp)
    conf.title = name:match '[^%.]+$'
    conf.additionalProperties = false

    if type(conf.default) == 'table' and next(conf.default) then
        local default = conf.default
        conf.default = nil
        conf.properties = {}
        local descHead = name:gsub('^Lua', '%%config')
        if util.stringStartWith(descHead, '%config.diagnostics') then
            descHead = '%config.diagnostics'
        end
        for key, value in pairs(default) do
            conf.properties[key] = {
                type    = getType( temp.subvalue),
                default = value,
                enum    = getEnum( temp.subvalue),
                description = descHead .. '.' .. key .. '%',
            }
        end
    else
        conf.patternProperties = {
            ['.*'] = {
                type    = getType( temp.subvalue),
                default = getDefault( temp.subvalue),
                enum    = getEnum( temp.subvalue),
            }
        }
    end
end

local config = {}

for name, temp in pairs(template) do
    if not util.stringStartWith(name, 'Lua.') then
        goto CONTINUE
    end
    config[name] = {
        scope   = 'resource',
        type    = getType(temp),
        default = getDefault(temp),
        enum    = getEnum(temp),

        markdownDescription      = name:gsub('^Lua', '%%config') .. '%',
        markdownEnumDescriptions = getEnumDesc(name, temp),
    }

    if temp.name == 'Array' then
        insertArray(config[name], temp)
    end

    if temp.name == 'Hash' then
        insertHash(name, config[name], temp)
    end

    ::CONTINUE::
end

return config

--[[
    File: build-doc.lua
    Purpose: Automated Documentation Generator for Lua Language Server Configurations

    This script is responsible for generating localized Markdown documentation (config.md)
    for all available server settings. It acts as a bridge between the internal
    configuration definitions and the user-facing documentation found in the `doc/` directory.

    Key Functions:
    1.  Locale Aggregation: Scans the `locale/` directory to load language-specific
        strings for settings descriptions and diagnostic groups.
    2.  Metadata Processing: Parses configuration objects to determine data types,
        default values, and valid enum options.
    3.  Markdown Construction: Utilizes a markdown provider to programmatically
        build structured documentation for every setting, formatted with TypeScript-style
        types and JSON-beautified default values.
    4.  Localized Output: Generates a distinct `config.md` for every supported language
        and saves them in their respective `doc/<lang>/` subdirectories.

    Note: This is a build-time utility script typically used by the VS Code client
    build process to ensure documentation stays in sync with the codebase.
]]

-- add tools directory to require path resolutionlist

-- tools items
local config   = require 'tools.configuration'

local fs       = require 'bee.filesystem'
local markdown = require 'script.provider.markdown'
local util     = require 'script.utility'
local lloader  = require 'script.locale-loader'
local json     = require 'script.json-beautify'
local diagd    = require 'script.proto.diagnostic'

local function mergeDiagnosticGroupLocale(locale)
    for groupName, names in pairs(diagd.diagnosticGroups) do
        local key = ('config.diagnostics.%s'):format(groupName)
        local list = {}
        for name in util.sortPairs(names) do
            list[#list+1] = ('* %s'):format(name)
        end
        local desc = table.concat(list, '\n')
        locale[key] = desc
    end
end

local function getLocale()
    local locale = {}

    for dirPath in fs.pairs(fs.path 'locale') do
        local lang = dirPath:filename():string()
        local text = util.loadFile((dirPath / 'setting.lua'):string())
        if text then
            locale[lang] = lloader(text, lang)
            -- add `config.diagnostics.XXX`
            mergeDiagnosticGroupLocale(locale[lang])
        end
    end

    return locale
end

local localeMap = getLocale()

local function getDesc(lang, desc)
    if not desc then
        return nil
    end
    if desc:sub(1, 1) ~= '%' or desc:sub(-1, -1) ~= '%' then
        return desc
    end
    local locale = localeMap[lang]
    if not locale then
        return desc
    end
    local id = desc:sub(2, -2)
    return locale[id]
end

local function view(conf)
    if type(conf.type) == 'table' then
        local subViews = {}
        for i = 1, #conf.type do
            subViews[i] = conf.type[i]
        end
        return table.concat(subViews, ' | ')
    elseif conf.type == 'array' then
        return ('Array<%s>'):format(view(conf.items))
    elseif conf.type == 'object' then
        if conf.properties then
            local _, first = next(conf.properties)
            assert(first)
            return ('object<string, %s>'):format(view(first))
        elseif conf.patternProperties then
            local _, first = next(conf.patternProperties)
            assert(first)
            return ('Object<string, %s>'):format(view(first))
        else
            return '**Unknown object type!!**'
        end
    else
        return tostring(conf.type)
    end
end

local function buildType(md, lang, conf)
    md:add('md', '## type')
    md:add('ts', view(conf))
end

local function buildDesc(md, lang, conf)
    local desc = conf.markdownDescription or conf.description
    desc = getDesc(lang, desc)
    if desc then
        md:add('md', desc)
    else
        md:add('md', '**Missing description!!**')
    end
    md:emptyLine()
end

local function buildDefault(md, lang, conf)
    local default = conf.default
    if default == json.null then
        default = nil
    end
    md:add('md', '## default')
    if conf.type == 'object' then
        if not default then
            default = {}
            for k, v in pairs(conf.properties) do
                default[k] = v.default
            end
        end
        local list = util.getTableKeys(default, true)
        if #list == 0 then
            md:add('jsonc', '{}')
            return
        end
        md:add('jsonc', '{')
        for i, k in ipairs(list) do
            local desc = getDesc(lang, conf.properties[k].description)
            if desc then
                md:add('jsonc', '    /*')
                md:add('jsonc', ('    %s'):format(desc:gsub('\n', '\n    ')))
                md:add('jsonc', '    */')
            end
            if i == #list then
                md:add('jsonc',('    %s: %s'):format(json.encode(k), json.encode(default[k])))
            else
                md:add('jsonc',('    %s: %s,'):format(json.encode(k), json.encode(default[k])))
            end
        end
        md:add('jsonc', '}')
    else
        md:add('jsonc', ('%s'):format(json.encode(default)))
    end
end

local function buildEnum(md, lang, conf)
    if conf.enum then
        md:add('md', '## enum')
        md:emptyLine()
        for i, enum in ipairs(conf.enum) do
            local desc = getDesc(lang, conf.markdownEnumDescriptions and conf.markdownEnumDescriptions[i])
            if desc then
                md:add('md', ('* ``%s``: %s'):format(json.encode(enum), desc))
            else
                md:add('md', ('* ``%s``'):format(json.encode(enum)))
            end
        end
        md:emptyLine()
        return
    end

    if conf.type == 'object' and conf.properties then
        local _, first = next(conf.properties)
        if first and first.enum then
            md:add('md', '## enum')
            md:emptyLine()
            for i, enum in ipairs(first.enum) do
                local desc = getDesc(lang, conf.markdownEnumDescriptions and conf.markdownEnumDescriptions[i])
                if desc then
                    md:add('md', ('* ``%s``: %s'):format(json.encode(enum), desc))
                else
                    md:add('md', ('* ``%s``'):format(json.encode(enum)))
                end
            end
            md:emptyLine()
            return
        end
    end

    if conf.type == 'array' and conf.items.enum then
        md:add('md', '## enum')
        md:emptyLine()
        for i, enum in ipairs(conf.items.enum) do
            local desc = getDesc(lang, conf.markdownEnumDescriptions and conf.markdownEnumDescriptions[i])
            if desc then
                md:add('md', ('* ``%s``: %s'):format(json.encode(enum), desc))
            else
                md:add('md', ('* ``%s``'):format(json.encode(enum)))
            end
        end
        md:emptyLine()
        return
    end
end

local function buildMarkdown(lang)
    local dir = fs.path 'doc' / lang
    fs.create_directories(dir)
    local configDoc = markdown()

    for name, conf in util.sortPairs(config) do
        configDoc:add('md', '# ' .. name:gsub('^Lua%.', ''))
        configDoc:emptyLine()
        buildDesc(configDoc, lang, conf)
        buildType(configDoc, lang, conf)
        buildEnum(configDoc, lang, conf)
        buildDefault(configDoc, lang, conf)
    end

    util.saveFile((dir / 'config.md'):string(), configDoc:string())
end

for lang in pairs(localeMap) do
    buildMarkdown(lang)
end

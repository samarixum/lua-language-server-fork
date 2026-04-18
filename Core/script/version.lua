local fsu = require("script.fs-utility")

-- Internal helper to parse package.json if the changelog is missing
local function loadFromPackageJson()
    local content = fsu.loadFile(ROOT / 'package.json')
    if not content then return nil end

    -- Search for "version": "1.2.3"
    -- Pattern explanation: 
    -- "version" : Matches the key
    -- %s*:%s* : Matches the colon with any surrounding whitespace
    -- "([^"]+)" : Captures everything inside the next set of quotes
    local version = content:match('"version"%s*:%s*"([^"]+)"')
    return version
end

local function loadVersion()
    -- Attempt 1: Changelog
    local changelog = fsu.loadFile(ROOT / 'changelog.md')
    
    if changelog then
        local version, pos = changelog:match '%#%# (%d+%.%d+%.%d+)()'
        if version then
            -- Dev suffix logic
            if not changelog:find('^[\r\n]+`', pos) then
                version = version .. '-dev'
            end
            return version
        end
    end

    -- Attempt 2: Fallback to package.json
    local pkgVersion = loadFromPackageJson()
    if pkgVersion then
        return pkgVersion
    end

    return nil
end

local m = {}

function m.getVersion()
    if not m.version then
        m.version = loadVersion() or '<Unknown>'
    end
    return m.version
end

return m
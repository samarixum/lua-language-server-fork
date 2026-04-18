local proto  = require("script.proto")
local client = require("script.client")
local config = require("script.config")
local ws     = require("script.workspace")

local isEnable = false

local function allWords()
    local str = '\t\n.:(\'"[,#*@|=-{ +?'
    local mark = {}
    local list = {}
    for c in str:gmatch '.' do
        list[#list+1] = c
        mark[c] = true
    end
    for _, scp in ipairs(ws.folders) do
        local postfix = config.get(scp.uri, 'Lua.completion.postfix')
        if postfix ~= '' and not mark[postfix] then
            list[#list+1] = postfix
            mark[postfix] = true
        end
        local separator = config.get(scp.uri, 'Lua.completion.requireSeparator')
        if not mark[separator] then
            list[#list+1] = separator
            mark[separator] = true
        end
    end
    return list
end

local function enable(_uri)
    if isEnable then
        return
    end
    if not client.getAbility('textDocument.completion.dynamicRegistration') then
        return
    end
    isEnable = true
    log.info('Enable completion.')
    proto.request('client/registerCapability', {
        registrations = {
            {
                id = 'completion',
                method = 'textDocument/completion',
                registerOptions = {
                    resolveProvider = true,
                    triggerCharacters = allWords(),
                },
            },
        }
    })
end

local function disable(_uri)
    if not isEnable then
        return
    end
    if not client.getAbility('textDocument.completion.dynamicRegistration') then
        return
    end
    isEnable = false
    log.info('Disable completion.')
    proto.request('client/unregisterCapability', {
        unregisterations = {
            {
                id = 'completion',
                method = 'textDocument/completion',
            },
        }
    })
end

config.watch(function (uri, key, value)
    if key == '' then
        key   = 'Lua.completion.enable'
        value = config.get(uri, key)
    end
    if key == 'Lua.completion.enable' then
        if value == true then
            enable(uri)
        else
            disable(uri)
        end
    end
    if key == 'Lua.completion.postfix' then
        if config.get(uri, 'Lua.completion.enable') then
            disable(uri)
            enable(uri)
        end
    end
end)

return {
    enable   = enable,
    disable  = disable,
    allWords = allWords,
}

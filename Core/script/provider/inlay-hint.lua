local proto          = require("script.proto")
local client         = require("script.client")
local json           = require("script.json")
local config         = require("script.config")

local function refresh()
    if not client.isReady() then
        return
    end
    if not client.getAbility 'workspace.inlayHint.refreshSupport' then
        return
    end
    log.debug('Refresh inlay hints.')
    proto.request('workspace/inlayHint/refresh', json.null)
end

config.watch(function (_uri, key, _value, _oldValue)
    if key == '' then
        refresh()
    end
    if key:find '^Lua.runtime'
    or key:find '^Lua.workspace'
    or key:find '^Lua.hint'
    or key:find '^files' then
        refresh()
    end
end)

return {}

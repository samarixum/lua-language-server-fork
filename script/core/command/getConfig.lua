local config = require("script.config")
local client = require("script.client")
local await  = require("script.await")

---@async
return function (data)
    local uri = data[1].uri
    local key = data[1].key
    while not client:isReady() do
        await.sleep(0.1)
    end
    return config.get(uri, key)
end

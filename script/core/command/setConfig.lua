local client = require("script.client")
local await  = require("script.await")

---@async
---@param changes config.change[]
return function (changes)
    while not client:isReady() do
        await.sleep(0.1)
    end
    client.setConfig(changes)
end

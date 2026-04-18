local files  = require("script.files")
local guide  = require("script.parser.guide")
local vm     = require("script.vm")
local lang   = require("script.language")
local await  = require("script.await")

---@async
return function (uri, callback)
    local state = files.getState(uri)
    if not state then
        return
    end

    ---@async
    guide.eachSourceType(state.ast, 'function', function (source)
        local returns = source.returns
        if not returns then
            return
        end
        await.delay()
        local _, max = vm.countReturnsOfSource(source)
        for _, ret in ipairs(returns) do
            local rmin, rmax = vm.countList(ret)
            if rmin > max then
                for i = max + 1, #ret - 1 do
                    callback {
                        start   = ret[i].start,
                        finish  = ret[i].finish,
                        message = lang.script('DIAG_REDUNDANT_RETURN_VALUE', {
                            max  = max,
                            rmax = i,
                        }),
                    }
                end
                if #ret == rmax then
                    callback {
                        start   = ret[#ret].start,
                        finish  = ret[#ret].finish,
                        message = lang.script('DIAG_REDUNDANT_RETURN_VALUE', {
                            max  = max,
                            rmax = rmax,
                        }),
                    }
                else
                    callback {
                        start   = ret[#ret].start,
                        finish  = ret[#ret].finish,
                        message = lang.script('DIAG_REDUNDANT_RETURN_VALUE_RANGE', {
                            max  = max,
                            rmin = #ret,
                            rmax = rmax,
                        }),
                    }
                end
            end
        end
    end)
end

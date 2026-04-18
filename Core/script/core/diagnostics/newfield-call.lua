local files = require("script.files")
local guide = require("script.parser.guide")
local lang  = require("script.language")
local await = require("script.await")
local sub   = require("script.core.substring")

---@async
return function (uri, callback)
    local state = files.getState(uri)
    local text  = files.getText(uri)
    if not state or not text then
        return
    end

    ---@async
    guide.eachSourceType(state.ast, 'table', function (source)
        await.delay()
        for i = 1, #source do
            local field = source[i]
            if field.type ~= 'tableexp' then
                goto CONTINUE
            end
            local call = field.value
            if not call then
                goto CONTINUE
            end
            if call.type ~= 'call' then
                return
            end
            local func = call.node
            local args = call.args
            if args then
                local funcLine = guide.rowColOf(func.finish)
                local argsLine = guide.rowColOf(args.start)
                if argsLine > funcLine then
                    callback {
                        start   = call.start,
                        finish  = call.finish,
                        message = lang.script('DIAG_PREFIELD_CALL'
                            , sub(state)(func.start + 1, func.finish)
                            , sub(state)(args.start + 1, args.finish)
                        )
                    }
                end
            end
            ::CONTINUE::
        end
    end)
end

local DEBUG = true
local function dprint(...) if DEBUG then print("[DEBUG]", ...) end end

dprint("Lua Version: " .. _VERSION)
if _MOONSHARP then dprint("Running in MoonSharp environment") end


-- debug print all args
if arg then
    for k, v in pairs(arg) do
        dprint("arg[" .. tostring(k) .. "] = " .. tostring(v))
    end
else
    dprint("No args table found")
end


local main, exec
local i = 1
while arg[i] do
    dprint("Processing arg[" .. i .. "]: " .. tostring(arg[i]))
    if arg[i] == '-E' then
        -- Skip
    elseif arg[i] == '-e' then
        i = i + 1
        local expr = assert(arg[i], "'-e' needs argument")
        dprint("Executing immediate expression: " .. expr)
        assert(load(expr, "=(command line)"))()
        exec = true
    elseif not main and arg[i]:sub(1, 1) ~= '-' then
        main = i
        dprint("Main script identified at index: " .. main .. " (Value: " .. arg[main] .. ")")
    elseif arg[i]:sub(1, 2) == '--' then
        dprint("Found '--', stopping argument parse")
        break
    end
    i = i + 1
end

if exec and not main then
    dprint("Execution finished via -e with no main script. Exiting.")
    return
end

if main then
    dprint("Shifting arguments for main script...")
    -- Handle negative indices (if any)
    for i = -1, -999, -1 do
        if not arg[i] then
            for j = i + 1, -1 do
                arg[j - main + 1] = arg[j]
            end
            break
        end
    end
    -- Shift positive indices
    for j = 1, #arg do
        arg[j - main] = arg[j]
    end
    -- Cleanup trailing
    for j = #arg - main + 1, #arg do
        arg[j] = nil
    end
    dprint("New arg[0]: " .. tostring(arg[0]))
end

local root
do
    if main then
        if _MOONSHARP then
            dprint("Determining root via ThisScriptDir...")
            local sep = package.config:sub(1, 1)
            local scriptDir = ThisScriptDir or arg[0]:match("^(.*)" .. sep .. "[^" .. sep .. "]+$")
            root = scriptDir and scriptDir:match("^(.*)" .. sep .. "[^" .. sep .. "]+$") or '.'
            if root == '' then root = '.' end
        else
            dprint("Determining root via bee.filesystem...")

            local fs = require 'bee.filesystem'
            local mainPath = fs.path(arg[0])
            root = mainPath:parent_path():string()
            if root == '' then root = '.' end
        end

    elseif package.cpath then
        dprint("Determining root via package.cpath...")
        local sep = package.config:sub(1, 1)
        if sep == '\\' then sep = '/\\' end
        local pattern = "[" .. sep .. "]+[^" .. sep .. "]+"
        root = package.cpath:match("([^;]+)" .. pattern .. pattern .. "$")
        arg[0] = root .. package.config:sub(1, 1) .. 'main.lua'
    elseif ThisScriptDir then
        -- ThisScriptDir is a full path string to this scripts current directory, navigate to parent and use as rootPath
        dprint("Determining root via ThisScriptDir...")
        local sep = package.config:sub(1, 1)
        root = ThisScriptDir:match("^(.*)" .. sep .. "[^" .. sep .. "]+$")
        if root == '' then root = '.' end
    end
    root = root:gsub('[/\\]', package.config:sub(1, 1))
    dprint("Resolved root path: " .. root)
end

package.path = table.concat({
    root .. "/script/?.lua",
    root .. "/script/?/init.lua",
}, ";"):gsub('/', package.config:sub(1, 1))

dprint("Final package.path: " .. package.path)

-- Custom Loader Logic
local function custom_loader(name)
    dprint("Require call: searching for '" .. name .. "'")
    local filename, err = package.searchpath(name, package.path)

    if not filename then
        dprint("  - Not found in package.path: " .. tostring(err))
        return err
    end

    dprint("  - Found file: " .. filename)
    local f = io.open(filename, "r")
    if not f then return 'cannot open file:' .. filename end

    local buf = f:read('*a')
    f:close()

    local relative = filename
    if root and filename:sub(1, #root) == root then
        relative = filename:sub(#root + 2)
    end

    dprint("  - Loading chunk: @" .. relative)
    local init, loadErr = load(buf, '@' .. relative)
    if not init then
        dprint("  - COMPILATION ERROR: " .. tostring(loadErr))
        return loadErr
    end

    return init, filename
end



if (arg[0]) then
    dprint("Executing entry point: " .. tostring(arg[0]))
    dprint("------------------------------------------")

    dprint("Installing loader into package.searchers[2]")
    package.searchers[2] = custom_loader

    local status, result = pcall(function()
        return assert(loadfile(arg[0]))(table.unpack(arg))
    end)

    if not status then
        print("\n[CRITICAL ERROR DURING EXECUTION]")
        print(result)
    end

else
    dprint("No entry point specified, skipping execution")
end
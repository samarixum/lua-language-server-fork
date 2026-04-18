-- Bootstrap script for Lua language server to execute any given lua file or the default core.lua as the main entry point.
-- in prod this file is renamed to main.lua to support the expected default of luamakes lls.exe

local DEBUG = true
function Dprint(...) if DEBUG then print("[DEBUG]", ...) end end

Dprint("Lua Version: " .. _VERSION)
if _MOONSHARP then Dprint("Running in MoonSharp environment") end


-- debug print all args
if arg then
    for k, v in pairs(arg) do
        Dprint("arg[" .. tostring(k) .. "] = " .. tostring(v))
    end
else
    Dprint("No args table found")
end


local main, exec
local i = 1
while arg[i] do
    Dprint("Processing arg[" .. i .. "]: " .. tostring(arg[i]))
    if arg[i] == '-E' then
        -- Skip
    elseif arg[i] == '-e' then
        i = i + 1
        local expr = assert(arg[i], "'-e' needs argument")
        Dprint("Executing immediate expression: " .. expr)
        assert(load(expr, "=(command line)"))()
        exec = true
    elseif not main and arg[i]:sub(1, 1) ~= '-' then
        main = i
        Dprint("Main script identified at index: " .. main .. " (Value: " .. arg[main] .. ")")
    elseif arg[i]:sub(1, 2) == '--' then
        Dprint("Found '--', stopping argument parse")
        break
    end
    i = i + 1
end

if exec and not main then
    Dprint("Execution finished via -e with no main script. Exiting.")
    return
end

if main then
    Dprint("Shifting arguments for main script...")
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
    Dprint("New arg[0]: " .. tostring(arg[0]))
end

local root
local abRoot
do
    Dprint("\n[Bootstrap] Resolving paths based on arg[-1]: " .. tostring(arg[-1]))

    -- debug print all args again for clarity after potential modifications
    if arg then
        for k, v in pairs(arg) do
            Dprint("arg[" .. tostring(k) .. "] = " .. tostring(v))
        end
    else
        Dprint("No args table found")
    end

    local fs = require 'bee.filesystem'
    Dprint("Initial arg[-1] (EXE path): " .. tostring(arg[-1]))
    local exe_arg = fs.path(arg[-1])

    -- 1. Get the absolute path of the EXE from arg[-1]
    local exe_path = fs.absolute(exe_arg)
    -- exe_path = A:/RemakeEngine/Extension/submodules/server/bin/lua-language-server.exe

    -- 2. strip exe filename
    local bin_dir = exe_path:parent_path()
    -- bin_dir = A:/RemakeEngine/Extension/submodules/server/bin

    -- 3. strip bin to get /server/
    local root_dir = bin_dir:parent_path()
    -- root_dir = A:/RemakeEngine/Extension/submodules/server

    -- 3. set other main.lua path, this is not the bin/main.lua (thats this bootstrap.lua, and somtimes also make/bootstrap.lua) but the root /server/main.lua that is the entry point for the real lua logic
    local CoreLuaPath = bin_dir / "core.lua"
    Dprint("Resolved Core Lua Path: " .. CoreLuaPath:string())

    -- fix the arg[0] to point to the main.lua when no main script is specified
    if not main then
        arg[0] = CoreLuaPath:string()
        Dprint("No main script specified, setting arg[0] to: " .. arg[0])
    end

    -- 4. Check main Lua script exists
    if not fs.exists(CoreLuaPath:string()) then
        Dprint("\n[CRITICAL ERROR] Could not find main Lua script!")
        Dprint("Looked at: " .. CoreLuaPath:string())
        os.exit(1)
    end

    root = bin_dir:string()
    abRoot = root -- test using bin dir
    --abRoot = root_dir:string()
    Dprint("Resolved root path: " .. root)
    Dprint("Resolved absolute root path: " .. abRoot)
end

-- 1. Store the paths in a local table
local paths = {
    abRoot .. "/?.lua",             -- For root level modules
    abRoot .. "/?/?.lua",           -- FIX: This will map 'script.proto' to 'script/proto/proto.lua'
    abRoot .. "/?/init.lua",        -- Standard Lua package pattern
}

-- 2. Iterate and print each entry
Dprint("package.paths:")
for i, path in ipairs(paths) do
    Dprint(i, path)
end

-- 3. Proceed with your concatenation and gsub logic
package.path = table.concat(paths, ";"):gsub('/', package.config:sub(1, 1))

Dprint("Final package.paths: " .. package.path)

-- Custom Loader Logic
local function custom_loader(name)
    Dprint("[bin/main.lua] LUA 5.5 : Require call: searching for '" .. name .. "'")
    local filename, err = package.searchpath(name, package.path)

    if not filename then
        --Dprint("  - Not found in package.path: " .. tostring(err))
        return err
    end

    --Dprint("  - Found file: " .. filename)
    local f = io.open(filename, "r")
    if not f then return 'cannot open file:' .. filename end

    local buf = f:read('*a')
    f:close()

    local relative = filename
    if root and filename:sub(1, #root) == root then
        relative = filename:sub(#root + 2)
    end

    --Dprint("  - Loading chunk: @" .. relative)
    local init, loadErr = load(buf, '@' .. relative)
    if not init then
        --Dprint("  - COMPILATION ERROR: " .. tostring(loadErr))
        return loadErr
    end

    return init, filename
end

-- ensure arg[0] is not set to this bootstrap script
if arg[0] and arg[0] == (abRoot .. "/bootstrap.lua") then
    print("\n[CRITICAL ERROR] arg[0] is set to bootstrap.lua, which will cause require() to fail!")
    print("Please ensure you are running the correct entry point and that arg[0] is set appropriately.")
    os.exit(1)
end


if (arg[0]) then
    Dprint("Executing entry point: " .. tostring(arg[0]))
    Dprint("------------------------------------------")

    if not _MOONSHARP then
        Dprint("replacing package.searchers[2] with custom loader for Lua 5.5 host")
        package.searchers[2] = custom_loader
    else
        Dprint("Using host-provided require shim")
    end

    local status, result = pcall(function()
        return assert(loadfile(arg[0]))(table.unpack(arg))
    end)

    if not status then
        print("\n[CRITICAL ERROR DURING EXECUTION]")
        print(result)
    end

else
    Dprint("No entry point specified, skipping execution")
end


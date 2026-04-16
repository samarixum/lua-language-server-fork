---@meta _
---@version 5.2


-- normally a standard lua item but doesnt exist in moonsharp due to its embedded focus
---@type string[]
arg = {}
arg = arg or {}

--- Arguments passed to the script (1-based index).
---@type table<integer, string>
argv = {}

--- The number of arguments passed to the script.
---@type integer
argc = 0

---@class _MOONSHARP
---@field version string The version of the MoonSharp interpreter.
---@field luacompat string The Lua compatibility level MoonSharp emulates.
---@field platform string The platform name MoonSharp is running on.
---@field is_aot boolean True if running on an AOT platform.
---@field is_unity boolean True if running inside Unity.
---@field is_mono boolean True if running on Mono.
---@field is_clr4 boolean True if running on .NET 4.x.
---@field is_pcl boolean True if running as a portable class library.
---@field banner string The REPL-style MoonSharp banner.
_MOONSHARP = {}


-- not available in moonsharp
---@deprecated
package.cpath = nil

-- path to the currently executing script's directory
---@type string
ThisScriptDir = ""

--path to the currently executing script
---@type string
ThisScriptPath = ""


-- added in this env
math.tointeger = function ()
end

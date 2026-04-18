if _G['HELP'] then
    require("script.cli.help")
    os.exit(0, true)
end

if _G['VERSION'] then
    require("script.cli.version")
    os.exit(0, true)
end

if _G['CHECK'] then
    local ret = require("script.cli.check").runCLI()
    os.exit(ret, true)
end

if _G['CHECK_WORKER'] then
    local ret = require("script.cli.check_worker").runCLI()
    os.exit(ret or 0, true)
end

if _G['DOC_UPDATE'] then
    require("script.cli.doc") .runCLI()
    os.exit(0, true)
end

if _G['DOC'] then
    require("script.cli.doc") .runCLI()
    os.exit(0, true)
end

if _G['VISUALIZE'] then
	local ret = require("script.cli.visualize") .runCLI()
	os.exit(ret or 0, true)
end

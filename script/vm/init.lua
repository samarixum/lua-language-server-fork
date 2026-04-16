local vm = require("script.vm.vm")

---@alias vm.object parser.object | vm.generic

require("script.vm.compiler")
require("script.vm.value")
require("script.vm.node")
require("script.vm.def")
require("script.vm.ref")
require("script.vm.field")
require("script.vm.doc")
require("script.vm.type")
require("script.vm.library")
require("script.vm.tracer")
require("script.vm.infer")
require("script.vm.generic")
require("script.vm.sign")
require("script.vm.variable")
require("script.vm.global")
require("script.vm.function")
require("script.vm.operator")
require("script.vm.visible")
require("script.vm.precompile")

return vm

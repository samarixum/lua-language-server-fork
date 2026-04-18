local pub     = require("script.pub.pub")
local proto   = require("script.proto.proto")

pub.on('proto', function (message)
	if message.method then
		proto.doMethod(message)
	else
		proto.doResponse(message)
	end
end)

pub.on('protoerror', function (err)
	log.error(err)
end)

return proto

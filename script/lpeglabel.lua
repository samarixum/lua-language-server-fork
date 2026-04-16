print('loading lpeglabel.lua...')

local m = {}

local pattern_mt = {}
pattern_mt.__index = pattern_mt

local function is_pattern(value)
    return type(value) == 'table' and getmetatable(value) == pattern_mt
end

local function copy_values(values)
    local out = {}
    for index = 1, #values do
        out[index] = values[index]
    end
    return out
end

local function copy_map(values)
    local out = {}
    for key, value in pairs(values or {}) do
        out[key] = value
    end
    return out
end

local function normalize(value)
    if is_pattern(value) then
        return value
    end
    return m.P(value)
end

local function new_pattern(kind, data)
    data = data or {}
    data.kind = kind
    return setmetatable(data, pattern_mt)
end

local function append_values(target, values)
    for index = 1, #values do
        target[#target + 1] = values[index]
    end
end

local function build_table_capture(values)
    local result = {}
    local arrayIndex = 1

    for index = 1, #values do
        local item = values[index]
        if type(item) == 'table' and item._lpeg_group then
            if item.name then
                if #item.values == 1 then
                    result[item.name] = item.values[1]
                else
                    result[item.name] = copy_values(item.values)
                end
            else
                append_values(result, item.values)
                arrayIndex = #result + 1
            end
        else
            result[arrayIndex] = item
            arrayIndex = arrayIndex + 1
        end
    end

    return result
end

local function match_char_set(set, byte)
    for index = 1, #set do
        if string.byte(set, index) == byte then
            return true
        end
    end
    return false
end

local function match_range_set(ranges, byte)
    for index = 1, #ranges do
        local range = ranges[index]
        if byte >= range[1] and byte <= range[2] then
            return true
        end
    end
    return false
end

local function match_node(node, subject, position, context)
    local kind = node.kind

    if kind == 'empty' then
        return position, {}
    end

    if kind == 'fail' then
        return nil, nil, node.label
    end

    if kind == 'eos' then
        if position > #subject then
            return position, {}
        end
        return nil, nil, node.label
    end

    if kind == 'literal' then
        local value = node.value
        if subject:sub(position, position + #value - 1) == value then
            return position + #value, {}
        end
        return nil, nil, node.label
    end

    if kind == 'set' then
        local byte = string.byte(subject, position)
        if byte and match_char_set(node.value, byte) then
            return position + 1, {}
        end
        return nil, nil, node.label
    end

    if kind == 'range' then
        local byte = string.byte(subject, position)
        if byte and match_range_set(node.value, byte) then
            return position + 1, {}
        end
        return nil, nil, node.label
    end

    if kind == 'any' then
        local count = node.count or 1
        if count == 0 then
            return position, {}
        end
        if position + count - 1 <= #subject then
            return position + count, {}
        end
        return nil, nil, node.label
    end

    if kind == 'sequence' then
        local savedNamedCaptures = context.named_captures
        local workingNamedCaptures = copy_map(savedNamedCaptures)
        local workingContext = {
            args = context.args,
            grammar = context.grammar,
            named_captures = workingNamedCaptures,
        }

        local currentPosition = position
        local captures = {}
        for index = 1, #node.parts do
            local nextPosition, nextCaptures, nextError = match_node(node.parts[index], subject, currentPosition, workingContext)
            if not nextPosition then
                return nil, nil, nextError
            end
            currentPosition = nextPosition
            append_values(captures, nextCaptures)
        end

        context.named_captures = workingNamedCaptures
        return currentPosition, captures
    end

    if kind == 'choice' then
        local leftContext = {
            args = context.args,
            grammar = context.grammar,
            named_captures = copy_map(context.named_captures),
        }
        local nextPosition, nextCaptures = match_node(node.left, subject, position, leftContext)
        if nextPosition then
            context.named_captures = leftContext.named_captures
            return nextPosition, nextCaptures
        end

        local rightContext = {
            args = context.args,
            grammar = context.grammar,
            named_captures = copy_map(context.named_captures),
        }
        local rightPosition, rightCaptures, rightError = match_node(node.right, subject, position, rightContext)
        if rightPosition then
            context.named_captures = rightContext.named_captures
            return rightPosition, rightCaptures
        end
        return nil, nil, rightError
    end

    if kind == 'repeat' then
        local workingNamedCaptures = copy_map(context.named_captures)
        local workingContext = {
            args = context.args,
            grammar = context.grammar,
            named_captures = workingNamedCaptures,
        }

        local captures = {}
        local currentPosition = position
        local count = 0

        while true do
            if node.max and count >= node.max then
                break
            end

            local nextPosition, nextCaptures = match_node(node.child, subject, currentPosition, workingContext)
            if not nextPosition then
                break
            end
            if nextPosition == currentPosition then
                break
            end

            currentPosition = nextPosition
            count = count + 1
            append_values(captures, nextCaptures)
        end

        if count < node.min then
            return nil, nil, node.label
        end

        context.named_captures = workingNamedCaptures
        return currentPosition, captures
    end

    if kind == 'and' then
        local nextPosition = match_node(node.child, subject, position, context)
        if nextPosition then
            return position, {}
        end
        return nil, nil, node.label
    end

    if kind == 'not' then
        local nextPosition = match_node(node.child, subject, position, context)
        if nextPosition then
            return nil, nil, node.label
        end
        return position, {}
    end

    if kind == 'difference' then
        local nextPosition, nextCaptures, nextError = match_node(node.left, subject, position, context)
        if not nextPosition then
            return nil, nil, nextError
        end
        local probePosition = match_node(node.right, subject, position, context)
        if probePosition then
            return nil, nil, node.label
        end
        return nextPosition, nextCaptures
    end

    if kind == 'capture_string' then
        local nextPosition = match_node(node.child, subject, position, context)
        if not nextPosition then
            return nil, nil, node.label
        end
        return nextPosition, { subject:sub(position, nextPosition - 1) }
    end

    if kind == 'capture_substitute' then
        local nextPosition = match_node(node.child, subject, position, context)
        if not nextPosition then
            return nil, nil, node.label
        end
        return nextPosition, { subject:sub(position, nextPosition - 1) }
    end

    if kind == 'capture_constant' then
        return position, copy_values(node.values)
    end

    if kind == 'capture_position' then
        return position, { position }
    end

    if kind == 'capture_backref' then
        local value = context.named_captures and context.named_captures[node.name]
        if value == nil then
            return nil, nil, node.label or ('undefined capture: ' .. tostring(node.name))
        end
        if type(value) == 'table' then
            return position, copy_values(value)
        end
        return position, { value }
    end

    if kind == 'capture_group' then
        local nextPosition, nextCaptures, nextError = match_node(node.child, subject, position, context)
        if not nextPosition then
            return nil, nil, nextError
        end
        if node.name then
            if not context.named_captures then
                context.named_captures = {}
            end
            if #nextCaptures == 1 then
                context.named_captures[node.name] = nextCaptures[1]
            else
                context.named_captures[node.name] = copy_values(nextCaptures)
            end
        end
        return nextPosition, nextCaptures
    end

    if kind == 'capture_table' then
        local nextPosition, nextCaptures, nextError = match_node(node.child, subject, position, context)
        if not nextPosition then
            return nil, nil, nextError
        end
        return nextPosition, { build_table_capture(nextCaptures) }
    end

    if kind == 'capture_fold' then
        local nextPosition, nextCaptures, nextError = match_node(node.child, subject, position, context)
        if not nextPosition then
            return nil, nil, nextError
        end
        if #nextCaptures == 0 then
            return nil, nil, node.label
        end
        local value = nextCaptures[1]
        for index = 2, #nextCaptures do
            value = node.fn(value, nextCaptures[index])
        end
        return nextPosition, { value }
    end

    if kind == 'capture_matchtime' then
        local nextPosition, nextCaptures, nextError = match_node(node.child, subject, position, context)
        if not nextPosition then
            return nil, nil, nextError
        end

        local results = { node.fn(subject, nextPosition, table.unpack(nextCaptures, 1, #nextCaptures)) }
        if results[1] == nil or results[1] == false then
            return nil, nil, node.label
        end
        if type(results[1]) == 'number' then
            local newPosition = results[1]
            local captures = {}
            for index = 2, #results do
                captures[#captures + 1] = results[index]
            end
            return newPosition, captures
        end
        return nextPosition, results
    end

    if kind == 'capture_argument' then
        local value = context.args[node.index]
        return position, { value }
    end

    if kind == 'capture_transform' then
        local nextPosition, nextCaptures, nextError = match_node(node.child, subject, position, context)
        if not nextPosition then
            return nil, nil, nextError
        end

        local result
        if type(node.transform) == 'function' then
            result = node.transform(table.unpack(nextCaptures, 1, #nextCaptures))
        else
            result = node.transform
        end

        if is_pattern(result) then
            local transformedPosition, transformedCaptures, transformedError = match_node(result, subject, nextPosition, context)
            if not transformedPosition then
                return nil, nil, transformedError
            end
            return transformedPosition, transformedCaptures
        end

        if type(result) == 'table' and result._lpeg_group then
            return nextPosition, { result }
        end

        return nextPosition, { result }
    end

    if kind == 'reference' then
        local grammar = context.grammar
        if not grammar then
            return nil, nil, node.label or ('undefined reference: ' .. tostring(node.name))
        end
        local rule = grammar.rules[node.name]
        if not rule then
            return nil, nil, node.label or ('undefined reference: ' .. tostring(node.name))
        end
        return match_node(rule, subject, position, context)
    end

    if kind == 'grammar' then
        local childContext = {
            args = context.args,
            grammar = node,
            named_captures = copy_map(context.named_captures),
        }
        local startRule = node.rules[node.start]
        if not startRule then
            return nil, nil, 'missing start rule: ' .. tostring(node.start)
        end
        return match_node(startRule, subject, position, childContext)
    end

    if kind == 'function' then
        local results = { node.fn(subject, position, table.unpack(context.args, 1, #context.args)) }
        if results[1] == nil or results[1] == false then
            return nil, nil, node.label
        end
        if type(results[1]) == 'number' then
            local captures = {}
            for index = 2, #results do
                captures[#captures + 1] = results[index]
            end
            return results[1], captures
        end
        return position, results
    end

    error('unsupported pattern kind: ' .. tostring(kind))
end

local function apply_repeat(child, count)
    count = tonumber(count) or 0
    if count == 0 then
        return new_pattern('repeat', { child = child, min = 0 })
    end
    if count == 1 then
        return new_pattern('repeat', { child = child, min = 1 })
    end
    if count < 0 then
        return new_pattern('repeat', { child = child, min = 0, max = -count })
    end
    return new_pattern('repeat', { child = child, min = count })
end

function pattern_mt.__add(left, right)
    return new_pattern('choice', { left = normalize(left), right = normalize(right) })
end

function pattern_mt.__mul(left, right)
    local leftPattern = normalize(left)
    local rightPattern = normalize(right)

    if leftPattern.kind == 'sequence' then
        local parts = copy_values(leftPattern.parts)
        parts[#parts + 1] = rightPattern
        return new_pattern('sequence', { parts = parts })
    end

    return new_pattern('sequence', { parts = { leftPattern, rightPattern } })
end

function pattern_mt.__sub(left, right)
    return new_pattern('difference', { left = normalize(left), right = normalize(right) })
end

function pattern_mt.__pow(left, right)
    return apply_repeat(normalize(left), right)
end

function pattern_mt.__unm(child)
    return new_pattern('not', { child = normalize(child) })
end

function pattern_mt.__len(child)
    return new_pattern('and', { child = normalize(child) })
end

function pattern_mt.__div(child, transform)
    return new_pattern('capture_transform', { child = normalize(child), transform = transform })
end

function pattern_mt.__call(self, subject, init, ...)
    return self:match(subject, init, ...)
end

function pattern_mt:match(subject, init, ...)
    if type(subject) ~= 'string' then
        error('lpeglabel: match expects a string subject', 2)
    end

    local startPosition = tonumber(init) or 1
    local context = {
        args = { ... },
        grammar = self.kind == 'grammar' and self or nil,
        named_captures = {},
    }

    local nextPosition, captures, err = match_node(self, subject, startPosition, context)
    if not nextPosition then
        return nil, err
    end

    if #captures == 0 then
        return nextPosition
    end
    if #captures == 1 then
        return captures[1]
    end
    return table.unpack(captures, 1, #captures)
end

local function build_set(chars)
    return new_pattern('set', { value = chars })
end

local function build_range(ranges)
    return new_pattern('range', { value = ranges })
end

local function build_literal(value)
    return new_pattern('literal', { value = value })
end

function m.P(value)
    local tp = type(value)
    if tp == 'table' then
        if is_pattern(value) then
            return value
        end

        local start = value[1]
        local rules = {}
        for key, rule in pairs(value) do
            if type(key) == 'string' then
                rules[key] = normalize(rule)
            end
        end

        return new_pattern('grammar', {
            start = start,
            rules = rules,
        })
    end

    if tp == 'string' then
        return build_literal(value)
    end

    if tp == 'number' then
        if value == 0 then
            return new_pattern('empty')
        end
        if value == -1 then
            return new_pattern('eos')
        end
        if value > 0 then
            return new_pattern('any', { count = value })
        end
        return new_pattern('fail')
    end

    if tp == 'boolean' then
        if value then
            return new_pattern('empty')
        end
        return new_pattern('fail')
    end

    if tp == 'function' then
        return new_pattern('function', { fn = value })
    end

    if value == nil then
        return new_pattern('empty')
    end

    return build_literal(tostring(value))
end

function m.S(chars)
    if type(chars) ~= 'string' then
        chars = tostring(chars or '')
    end
    return build_set(chars)
end

function m.R(...)
    local ranges = {}
    for index = 1, select('#', ...) do
        local pair = select(index, ...)
        if type(pair) ~= 'string' or #pair < 2 then
            error('lpeglabel.R expects two-character range strings', 2)
        end
        ranges[#ranges + 1] = { string.byte(pair, 1), string.byte(pair, 2) }
    end
    return build_range(ranges)
end

function m.C(child)
    return new_pattern('capture_string', { child = normalize(child) })
end

function m.Cs(child)
    return new_pattern('capture_substitute', { child = normalize(child) })
end

function m.Ct(child)
    return new_pattern('capture_table', { child = normalize(child) })
end

function m.Cg(child, name)
    return new_pattern('capture_group', { child = normalize(child), name = name })
end

function m.Cb(name)
    return new_pattern('capture_backref', { name = name })
end

function m.Cc(...)
    return new_pattern('capture_constant', { values = { ... } })
end

function m.Cp()
    return new_pattern('capture_position')
end

function m.Cmt(child, fn)
    return new_pattern('capture_matchtime', { child = normalize(child), fn = fn })
end

function m.Cf(child, fn)
    return new_pattern('capture_fold', { child = normalize(child), fn = fn })
end

function m.Carg(index)
    return new_pattern('capture_argument', { index = tonumber(index) or 1 })
end

function m.V(name)
    return new_pattern('reference', { name = name })
end

function m.T(label)
    return new_pattern('fail', { label = label })
end

function m.type(value)
    if is_pattern(value) then
        return 'pattern'
    end
    return type(value)
end

function m.locale(predef)
    local function make_chars(first, last)
        local chars = {}
        for code = first, last do
            chars[#chars + 1] = string.char(code)
        end
        return table.concat(chars)
    end

    predef.alpha = build_set(make_chars(65, 90) .. make_chars(97, 122))
    predef.cntrl = build_set(make_chars(0, 31) .. string.char(127))
    predef.digit = build_set(make_chars(48, 57))
    predef.graph = build_set(make_chars(33, 126))
    predef.lower = build_set(make_chars(97, 122))
    predef.punct = build_set("!\"#$%&'()*+,-./:;<=>?@[\\]^_`{|}~")
    predef.space = build_set(" \t\n\r\v\f")
    predef.upper = build_set(make_chars(65, 90))
    predef.alnum = predef.alpha + predef.digit
    predef.xdigit = build_set("0123456789ABCDEFabcdef")
end

function m.match(pattern, subject, init, ...)
    return normalize(pattern):match(subject, init, ...)
end

print('lpeglabel.lua loaded')

return m
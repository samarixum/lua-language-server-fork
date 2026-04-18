local floor = math.floor
local type = type

local UINT32 = 4294967296
local MASK = UINT32 - 1
local SIGN = 2147483648

local function toInteger(value)
    local integer = math.tointeger and math.tointeger(value)
    if integer == nil then
        if type(value) == 'number' and value % 1 == 0 then
            integer = value
        else
            error('number expected', 3)
        end
    end
    return integer
end

local function toUnsigned(value)
    local integer = toInteger(value) % UINT32
    if integer < 0 then
        integer = integer + UINT32
    end
    return integer
end

local function toShift(value)
    return floor(toInteger(value))
end

local function combine(a, b, predicate)
    a = toUnsigned(a)
    b = toUnsigned(b)
    local result = 0
    local bit = 1
    for _ = 1, 32 do
        local abit = a % 2
        local bbit = b % 2
        if predicate(abit, bbit) then
            result = result + bit
        end
        a = (a - abit) / 2
        b = (b - bbit) / 2
        bit = bit * 2
    end
    return result
end

local function band(...)
    local count = select('#', ...)
    if count == 0 then
        return MASK
    end
    local result = toUnsigned(select(1, ...))
    for i = 2, count do
        result = combine(result, select(i, ...), function (a, b)
            return a == 1 and b == 1
        end)
    end
    return result
end

local function bor(...)
    local count = select('#', ...)
    if count == 0 then
        return 0
    end
    local result = 0
    for i = 1, count do
        result = combine(result, select(i, ...), function (a, b)
            return a == 1 or b == 1
        end)
    end
    return result
end

local function bxor(...)
    local count = select('#', ...)
    if count == 0 then
        return 0
    end
    local result = 0
    for i = 1, count do
        result = combine(result, select(i, ...), function (a, b)
            return (a + b) == 1
        end)
    end
    return result
end

local function bnot(value)
    return MASK - toUnsigned(value)
end

local function rshift(value, disp)
    value = toUnsigned(value)
    disp = toShift(disp)
    if disp < 0 then
        return lshift(value, -disp)
    end
    if disp >= 32 then
        return 0
    end
    return floor(value / (2 ^ disp))
end

local function lshift(value, disp)
    value = toUnsigned(value)
    disp = toShift(disp)
    if disp < 0 then
        return rshift(value, -disp)
    end
    if disp >= 32 then
        return 0
    end
    local result = 0
    local target = 2 ^ disp
    for _ = 1, 32 - disp do
        local bit = value % 2
        if bit == 1 then
            result = result + target
        end
        value = (value - bit) / 2
        target = target * 2
    end
    return result
end

local function arshift(value, disp)
    value = toUnsigned(value)
    disp = toShift(disp)
    if disp < 0 then
        return lshift(value, -disp)
    end
    if disp >= 32 then
        return value >= SIGN and MASK or 0
    end
    local result = rshift(value, disp)
    if value >= SIGN and disp > 0 then
        local fill = MASK - rshift(MASK, disp)
        result = bor(result, fill)
    end
    return result
end

local function extract(value, field, width)
    value = toUnsigned(value)
    field = toShift(field)
    width = width == nil and 1 or toShift(width)
    if field < 0 or field > 31 then
        error('field out of range', 2)
    end
    if width < 1 or width > 32 then
        error('width out of range', 2)
    end
    if field + width > 32 then
        error('width out of range', 2)
    end
    if width == 32 then
        return value
    end
    local mask = lshift(1, width) - 1
    return band(rshift(value, field), mask)
end

local function replace(value, replacement, field, width)
    value = toUnsigned(value)
    replacement = toUnsigned(replacement)
    field = toShift(field)
    width = width == nil and 1 or toShift(width)
    if field < 0 or field > 31 then
        error('field out of range', 2)
    end
    if width < 1 or width > 32 then
        error('width out of range', 2)
    end
    if field + width > 32 then
        error('width out of range', 2)
    end
    if width == 32 then
        return replacement
    end
    local mask = lshift(1, width) - 1
    local cleared = band(value, bnot(lshift(mask, field)))
    local inserted = lshift(band(replacement, mask), field)
    return bor(cleared, inserted)
end

return {
    band = band,
    bor = bor,
    bxor = bxor,
    bnot = bnot,
    lshift = lshift,
    rshift = rshift,
    arshift = arshift,
    extract = extract,
    replace = replace,
}
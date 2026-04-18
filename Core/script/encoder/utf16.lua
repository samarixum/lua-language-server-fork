local error = error
local strchar = string.char
local strbyte = string.byte
local strmatch = string.match
local tconcat = table.concat

local function be_tochar(code)
    return strchar(math.floor(code / 0x100), code % 0x100)
end

local function be_tobyte(s, i)
    local h, l = strbyte(s, i, i+1)
    if not h or not l then
        return nil
    end
    return h * 0x100 + l
end

local function le_tochar(code)
    return strchar(code % 0x100, math.floor(code / 0x100))
end

local function le_tobyte(s, i)
    local l, h = strbyte(s, i, i+1)
    if not h or not l then
        return nil
    end
    return h * 0x100 + l
end

local function utf16char(tochar, code)
    if code < 0x10000 then
        return tochar(code)
    else
        code = code - 0x10000
        return tochar(0xD800 + math.floor(code / 0x400))..tochar(0xDC00 + (code % 0x400))
    end
end

local function utf16next(s, n, tobyte)
    if n > #s then
        return
    end
    local code1 = tobyte(s, n)
    if not code1 then
        return
    end
    if code1 < 0xD800 or code1 >= 0xE000 then
        return n+2, code1
    elseif code1 >= 0xD800 and code1 < 0xDC00 then
        n = n + 2
        if n > #s then
            return n --invaild
        end
        local code2 = tobyte(s, n)
        if not code2 then
            return n --invaild
        end
        if code2 < 0xDC00 or code2 >= 0xE000 then
            return n --invaild
        end
        local code = 0x10000 + (code1 - 0xD800) * 0x400 + (code2 - 0xDC00)
        return n+2, code
    else
        return n+2 --invaild
    end
end

local function utf16codes(s, tobyte)
    return function (_, n)
        return utf16next(s, n, tobyte)
    end, s, 1
end

local function utf8char(code)
    if code < 0x80 then
        return strchar(code)
    elseif code < 0x800 then
        return strchar(
            math.floor(code / 0x40) + 0xC0,
            (code % 0x40) + 0x80
        )
    elseif code < 0x10000 then
        return strchar(
            math.floor(code / 0x1000) + 0xE0,
            math.floor(code / 0x40) % 0x40 + 0x80,
            (code % 0x40) + 0x80
        )
    else
        return strchar(
            math.floor(code / 0x40000) + 0xF0,
            math.floor(code / 0x1000) % 0x40 + 0x80,
            math.floor(code / 0x40) % 0x40 + 0x80,
            (code % 0x40) + 0x80
        )
    end
end

local function utf8next(s, n)
    if n > #s then
        return
    end

    local b1 = strbyte(s, n)
    if not b1 then
        return
    end
    if b1 < 0x80 then
        return n + 1, b1
    end

    local b2 = strbyte(s, n + 1)
    local b3 = strbyte(s, n + 2)
    local b4 = strbyte(s, n + 3)

    if b1 >= 0xC2 and b1 <= 0xDF and b2 and b2 >= 0x80 and b2 <= 0xBF then
        return n + 2, (b1 - 0xC0) * 0x40 + (b2 - 0x80)
    elseif b1 == 0xE0 and b2 and b2 >= 0xA0 and b2 <= 0xBF and b3 and b3 >= 0x80 and b3 <= 0xBF then
        return n + 3, (b1 - 0xE0) * 0x1000 + (b2 - 0x80) * 0x40 + (b3 - 0x80)
    elseif b1 >= 0xE1 and b1 <= 0xEC and b2 and b2 >= 0x80 and b2 <= 0xBF and b3 and b3 >= 0x80 and b3 <= 0xBF then
        return n + 3, (b1 - 0xE0) * 0x1000 + (b2 - 0x80) * 0x40 + (b3 - 0x80)
    elseif b1 == 0xED and b2 and b2 >= 0x80 and b2 <= 0x9F and b3 and b3 >= 0x80 and b3 <= 0xBF then
        return n + 3, (b1 - 0xE0) * 0x1000 + (b2 - 0x80) * 0x40 + (b3 - 0x80)
    elseif b1 >= 0xEE and b1 <= 0xEF and b2 and b2 >= 0x80 and b2 <= 0xBF and b3 and b3 >= 0x80 and b3 <= 0xBF then
        return n + 3, (b1 - 0xE0) * 0x1000 + (b2 - 0x80) * 0x40 + (b3 - 0x80)
    elseif b1 == 0xF0 and b2 and b2 >= 0x90 and b2 <= 0xBF and b3 and b3 >= 0x80 and b3 <= 0xBF and b4 and b4 >= 0x80 and b4 <= 0xBF then
        return n + 4, (b1 - 0xF0) * 0x40000 + (b2 - 0x80) * 0x1000 + (b3 - 0x80) * 0x40 + (b4 - 0x80)
    elseif b1 >= 0xF1 and b1 <= 0xF3 and b2 and b2 >= 0x80 and b2 <= 0xBF and b3 and b3 >= 0x80 and b3 <= 0xBF and b4 and b4 >= 0x80 and b4 <= 0xBF then
        return n + 4, (b1 - 0xF0) * 0x40000 + (b2 - 0x80) * 0x1000 + (b3 - 0x80) * 0x40 + (b4 - 0x80)
    elseif b1 == 0xF4 and b2 and b2 >= 0x80 and b2 <= 0x8F and b3 and b3 >= 0x80 and b3 <= 0xBF and b4 and b4 >= 0x80 and b4 <= 0xBF then
        return n + 4, (b1 - 0xF0) * 0x40000 + (b2 - 0x80) * 0x1000 + (b3 - 0x80) * 0x40 + (b4 - 0x80)
    end

    return n + 1, nil
end

local function utf8codes(s)
    return utf8next, s, 1
end

return function (what, replace)
    local tobyte, tochar
    if what == "be" then
        tobyte = be_tobyte
        tochar = be_tochar
    else
        tobyte = le_tobyte
        tochar = le_tochar
    end
    local utf8replace  = replace and utf8char(replace)
    local utf16replace = replace and utf16char(tochar, replace)
    local function toutf8(s)
        local r = {}
        for _, code in utf16codes(s, tobyte) do
            if code == nil then
                if replace then
                    r[#r+1] = utf8replace
                else
                    error "invalid UTF-16 code"
                end
            else
                r[#r+1] = utf8char(code)
            end
        end
        return tconcat(r)
    end
    local function fromutf8(s)
        local r = {}
        for _, code in utf8codes(s) do
            if code == nil then
                if replace then
                    r[#r+1] = utf16replace
                else
                    error "invalid UTF-8 code"
                end
            else
                r[#r+1] = utf16char(tochar, code)
            end
        end
        return tconcat(r)
    end
    return {
        toutf8 = toutf8,
        fromutf8 = fromutf8,
    }
end

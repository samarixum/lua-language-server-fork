local ansi    = require 'encoder.ansi'
local utf16   = require 'encoder.utf16'
local utf16le = utf16('le', 0xFFFD)
local utf16be = utf16('be', 0xFFFD)

local strbyte = string.byte

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

local function utf8len(s, i, j)
    i = i or 1
    j = j or #s
    local count = 0
    local pos = i
    while pos <= j do
        local nextPos, code = utf8next(s, pos)
        if not nextPos or code == nil then
            return nil
        end
        count = count + 1
        pos = nextPos
    end
    return count
end

local function utf8offset(s, n, i)
    i = i or 1
    if n == 0 then
        return i
    end
    if n < 0 then
        return nil
    end

    local pos = i
    for _ = 1, n - 1 do
        local nextPos, code = utf8next(s, pos)
        if not nextPos or code == nil then
            return nil
        end
        pos = nextPos
    end
    return pos
end

---@alias encoder.encoding '"utf8"'|'"utf16"'|'"utf16le"'|'"utf16be"'

---@alias encoder.bom '"no"'|'"yes"'|'"auto"'

local m = {}

---@param encoding encoder.encoding
---@param s        string
---@param i?       integer
---@param j?       integer
function m.len(encoding, s, i, j)
    i = i or 1
    j = j or #s
    if encoding == 'utf16'
    or encoding == 'utf16le' then
        local us = utf16le.fromutf8(s:sub(i, j))
        return math.floor(#us / 2)
    end
    if encoding == 'utf16be' then
        local us = utf16be.fromutf8(s:sub(i, j))
        return math.floor(#us / 2)
    end
    if encoding == 'utf8' then
        return utf8len(s, i, j)
    end
    log.error('Unsupport len encoding:', encoding)
    return j - i + 1
end

---@param encoding encoder.encoding
---@param s        string
---@param n        integer
---@param i?       integer
function m.offset(encoding, s, n, i)
    i = i or 1
    if encoding == 'utf16'
    or encoding == 'utf16le' then
        local line = s:match('[^\r\n]*', i)
        if not line:find '[\x80-\xff]' then
            return n + i - 1
        end
        local us = utf16le.fromutf8(line)
        local os = utf16le.toutf8(us:sub(1, n * 2 - 2))
        return #os + i
    end
    if encoding == 'utf16be' then
        local line = s:match('[^\r\n]*', i)
        if not line:find '[\x80-\xff]' then
            return n + i - 1
        end
        local us = utf16be.fromutf8(line)
        local os = utf16be.toutf8(us:sub(1, n * 2 - 2))
        return #os + i
    end
    if encoding == 'utf8' then
        return utf8offset(s, n, i)
    end
    log.error('Unsupport offset encoding:', encoding)
    return n + i - 1
end

---@param encoding encoder.encoding
---@param text string
---@param bom encoder.bom
---@return string
function m.encode(encoding, text, bom)
    if encoding == 'utf8' then
        if bom == 'yes' then
            text = '\xEF\xBB\xBF' .. text
        end
        return text
    end
    if encoding == 'ansi' then
        return ansi.fromutf8(text)
    end
    if encoding == 'utf16'
    or encoding == 'utf16le' then
        text = utf16le.fromutf8(text)
        if bom == 'yes'
        or bom == 'auto' then
            text = '\xFF\xFE' .. text
        end
        return text
    end
    if encoding == 'utf16be' then
        text = utf16be.fromutf8(text)
        if bom == 'yes'
        or bom == 'auto' then
            text = '\xFE\xFF' .. text
        end
        return text
    end
    log.error('Unsupport encode encoding:', encoding)
    return text
end

---@param encoding encoder.encoding
---@param text string
---@return string
function m.decode(encoding, text)
    if encoding == 'utf8' then
        return text
    end
    if encoding == 'ansi' then
        return ansi.toutf8(text)
    end
    if encoding == 'utf16'
    or encoding == 'utf16le' then
        return utf16le.toutf8(text)
    end
    if encoding == 'utf16be' then
        return utf16be.toutf8(text)
    end
    log.error('Unsupport encode encoding:', encoding)
    return text
end

return m

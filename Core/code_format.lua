local fallback = {}

function fallback.set_default_config() end
function fallback.set_clike_comments_symbol() end
function fallback.set_nonstandard_symbol() end
function fallback.update_config() return true end

function fallback.update_name_style_config() end
function fallback.name_style_analysis()
    return true, {}
end

function fallback.spell_load_dictionary_from_path()
    return true
end

function fallback.spell_load_dictionary_from_buffer()
    return true
end

function fallback.spell_analysis()
    return true, {}
end

function fallback.spell_suggest()
    return true, {}
end

function fallback.diagnose_file()
    return true, {}
end

function fallback.format(_, text)
    return true, text
end

function fallback.range_format(_, text)
    return true, text, 0, 0
end

function fallback.type_format(_, text)
    return true, text, 0, 0
end

return fallback


local util   = require("script.utility")
local define = require("script.proto.define")
local diag   = require("script.proto.diagnostic")

---@class config.unit
---@field caller function
---@field checker function
---@field loader function
---@field _checker fun(self: config.unit, value: any): boolean
---@field name     string
---@field setDefault fun(self: config.unit, default: any): config.unit
---@field setEnums fun(self: config.unit, enums: table): config.unit
---@operator call: config.unit
local mt = {}
mt.__index = mt

local unitAliases = setmetatable({}, { __mode = 'k' })

function mt:__call(...)
    self:caller(...)
    return self
end

-- Replaced __shr (>>) with a method for MoonSharp compatibility
function mt:setDefault(default)
    self.default = default
    self.hasDefault = true
    return self
end

-- Replaced __shl (<<) with a method for MoonSharp compatibility
function mt:setEnums(enums)
    self.enums = enums
    return self
end

function mt:checker(v)
    if self.enums then
        local ok
        for _, enum in ipairs(self.enums) do
            if util.equal(enum, v) then
                ok = true
                break
            end
        end
        local aliases = unitAliases[self]
        if not ok and aliases then
            for _, alias in ipairs(aliases) do
                if util.equal(alias, v) then
                    ok = true
                    break
                end
            end
        end
        if not ok then
            return false
        end
    end
    return self:_checker(v)
end

local units = {}

local function register(name, default, checker, loader, caller)
    units[name] = {
        name     = name,
        default  = default,
        _checker = checker,
        loader   = loader,
        caller   = caller,
    }
end


---@class config.master
---@field [string] config.unit
local Type = setmetatable({}, { __index = function (_, name)
    local unit = {}
    for k, v in pairs(units[name]) do
        unit[k] = v
    end
    return setmetatable(unit, mt)
end })

register('Boolean', false, function (self, v)
    return type(v) == 'boolean'
end, function (self, v)
    return v
end)

register('Integer', 0, function (self, v)
    return type(v) == 'number'
end, function (self, v)
    return math.floor(v)
end)

register('String', '', function (self, v)
    return type(v) == 'string'
end, function (self, v)
    return tostring(v)
end)

register('Nil', nil, function (self, v)
    return type(v) == 'nil'
end, function (self, v)
    return nil
end)

register('Array', {}, function (self, value)
    return type(value) == 'table'
end, function (self, value)
    local t = {}
    if #value == 0 then
        for k in pairs(value) do
            if self.sub:checker(k) then
                t[#t+1] = self.sub:loader(k)
            end
        end
    else
        for _, v in ipairs(value) do
            if self.sub:checker(v) then
                t[#t+1] = self.sub:loader(v)
            end
        end
    end
    return t
end, function (self, sub)
    self.sub = sub
end)

register('Hash', {}, function (self, value)
    if type(value) == 'table' then
        if #value == 0 then
            for k, v in pairs(value) do
                if not self.subkey:checker(k)
                or not self.subvalue:checker(v) then
                    return false
                end
            end
        else
            if not self.subvalue:checker(true) then
                return false
            end
            for _, v in ipairs(value) do
                if not self.subkey:checker(v) then
                    return false
                end
            end
        end
        return true
    end
    if type(value) == 'string' then
        return  self.subkey:checker('')
            and self.subvalue:checker(true)
    end
end, function (self, value)
    if type(value) == 'table' then
        local t = {}
        if #value == 0 then
            for k, v in pairs(value) do
                t[k] = v
            end
        else
            for _, k in pairs(value) do
                t[k] = true
            end
        end
        return t
    end
    if type(value) == 'string' then
        local t = {}
        for s in value:gmatch('[^' .. self.sep .. ']+') do
            t[s] = true
        end
        return t
    end
end, function (self, subkey, subvalue, sep)
    self.subkey   = subkey
    self.subvalue = subvalue
    self.sep      = sep
end)

register('Or', nil, function (self, value)
    for _, sub in ipairs(self.subs) do
        if mt.checker(sub, value) then
            return true
        end
    end
    return false
end, function (self, value)
    for _, sub in ipairs(self.subs) do
        if mt.checker(sub, value) then
            local loader = rawget(sub, 'loader')
            return loader(sub, value)
        end
    end
end, function (self, ...)
    self.subs = { ... }
end)

---@format disable-next
local template = {
    ['Lua.runtime.version']                 = Type.String:setDefault('Moonsharp 2.0.0.0'):setEnums({
                                                'Moonsharp 2.0.0.0',
                                                'Lua 5.5'
                                            }),
    ['Lua.runtime.path']                    = Type.Array(Type.String):setDefault({
                                                "?.lua",
                                                "?/init.lua",
                                            }),
    ['Lua.runtime.pathStrict']              = Type.Boolean:setDefault(false),
    ['Lua.runtime.special']                 = Type.Hash(
                                                Type.String,
                                                Type.String:setDefault('require'):setEnums({
                                                    '_G',
                                                    'rawset',
                                                    'rawget',
                                                    'setmetatable',
                                                    'require',
                                                    'dofile',
                                                    'loadfile',
                                                    'pcall',
                                                    'xpcall',
                                                    'assert',
                                                    'error',
                                                    'type',
                                                    'os.exit',
                                                })
                                            ),
    ['Lua.runtime.meta']                    = Type.String:setDefault('${version} ${language} ${encoding}'),
    ['Lua.runtime.unicodeName']             = Type.Boolean,
    ['Lua.runtime.nonstandardSymbol']       = Type.Array(Type.String:setEnums({
                                                '//', '/**/',
                                                '`',
                                                '+=', '-=', '*=', '/=', '%=', '^=', '//=',
                                                '|=', '&=', '<<=', '>>=',
                                                '||', '&&', '!', '!=',
                                                'continue',
                                                '|lambda|',
                                            })),
    ['Lua.runtime.plugin']                  = Type.Or(Type.String, Type.Array(Type.String)) ,
    ['Lua.runtime.pluginArgs']              = Type.Or(Type.Array(Type.String), Type.Hash(Type.String, Type.String)),
    ['Lua.runtime.fileEncoding']            = Type.String:setDefault('utf8'):setEnums({
                                                'utf8',
                                                'ansi',
                                                'utf16le',
                                                'utf16be',
                                            }),
    ['Lua.runtime.builtin']                 = Type.Hash(
                                                Type.String:setEnums(util.getTableKeys(define.BuiltIn, true)),
                                                Type.String:setDefault('default'):setEnums({
                                                    'default',
                                                    'enable',
                                                    'disable',
                                                })
                                            )
                                            :setDefault(util.deepCopy(define.BuiltIn)),
    ['Lua.diagnostics.enable']              = Type.Boolean:setDefault(true),
    ['Lua.diagnostics.globals']             = Type.Array(Type.String),
    ['Lua.diagnostics.globalsRegex']        = Type.Array(Type.String),

    -- this line breaks moonsharp on Nil value
    --    ['Lua.diagnostics.disable']             = Type.Array(Type.String:setEnums(util.getTableKeys(diag.getDiagAndErrNameMap(), true))),

    ['Lua.diagnostics.severity']            = Type.Hash(
                                                Type.String:setEnums(util.getTableKeys(define.DiagnosticDefaultNeededFileStatus, true)),
                                                Type.String:setEnums({
                                                    'Error',
                                                    'Warning',
                                                    'Information',
                                                    'Hint',
                                                    'Error!',
                                                    'Warning!',
                                                    'Information!',
                                                    'Hint!',
                                                })
                                            )
                                            :setDefault(util.deepCopy(define.DiagnosticDefaultSeverity)),
    ['Lua.diagnostics.neededFileStatus']    = Type.Hash(
                                                Type.String:setEnums(util.getTableKeys(define.DiagnosticDefaultNeededFileStatus, true)),
                                                Type.String:setEnums({
                                                    'Any',
                                                    'Opened',
                                                    'None',
                                                    'Any!',
                                                    'Opened!',
                                                    'None!',
                                                })
                                            )
                                            :setDefault(util.deepCopy(define.DiagnosticDefaultNeededFileStatus)),
    ['Lua.diagnostics.groupSeverity']       = Type.Hash(
                                                Type.String:setEnums(util.getTableKeys(define.DiagnosticDefaultGroupSeverity, true)),
                                                Type.String:setEnums({
                                                    'Error',
                                                    'Warning',
                                                    'Information',
                                                    'Hint',
                                                    'Fallback',
                                                })
                                            )
                                            :setDefault(util.deepCopy(define.DiagnosticDefaultGroupSeverity)),
    ['Lua.diagnostics.groupFileStatus']     = Type.Hash(
                                                Type.String:setEnums(util.getTableKeys(define.DiagnosticDefaultGroupFileStatus, true)),
                                                Type.String:setEnums({
                                                    'Any',
                                                    'Opened',
                                                    'None',
                                                    'Fallback',
                                                })
                                            )
                                            :setDefault(util.deepCopy(define.DiagnosticDefaultGroupFileStatus)),
    ['Lua.diagnostics.enableScheme']        = Type.Array(Type.String):setDefault({ 'file' }),
    ['Lua.diagnostics.workspaceEvent']      = Type.String:setDefault('OnSave'):setEnums({
                                                'OnChange',
                                                'OnSave',
                                                'None',
                                            }),
    ['Lua.diagnostics.workspaceDelay']      = Type.Integer:setDefault(3000),
    ['Lua.diagnostics.workspaceRate']       = Type.Integer:setDefault(100),
    ['Lua.diagnostics.libraryFiles']        = Type.String:setDefault('Opened'):setEnums({
                                                'Enable',
                                                'Opened',
                                                'Disable',
                                            }),
    ['Lua.diagnostics.ignoredFiles']        = Type.String:setDefault('Opened'):setEnums({
                                                'Enable',
                                                'Opened',
                                                'Disable',
                                            }),
    ['Lua.diagnostics.unusedLocalExclude']  = Type.Array(Type.String),
    ['Lua.workspace.ignoreDir']             = Type.Array(Type.String):setDefault({
                                                '.vscode',
                                            }),
    ['Lua.workspace.ignoreSubmodules']      = Type.Boolean:setDefault(true),
    ['Lua.workspace.useGitIgnore']          = Type.Boolean:setDefault(true),
    ['Lua.workspace.maxPreload']            = Type.Integer:setDefault(5000),
    ['Lua.workspace.preloadFileSize']       = Type.Integer:setDefault(500),
    ['Lua.workspace.library']               = Type.Array(Type.String),
    ['Lua.workspace.checkThirdParty']       = Type.Or(Type.String:setDefault('Ask'):setEnums({
                                                'Ask',
                                                'Apply',
                                                'ApplyInMemory',
                                                'Disable',
                                            }), Type.Boolean),
    ['Lua.workspace.userThirdParty']        = Type.Array(Type.String),
    ['Lua.completion.enable']               = Type.Boolean:setDefault(true),
    ['Lua.completion.callSnippet']          = Type.String:setDefault('Disable'):setEnums({
                                                'Disable',
                                                'Both',
                                                'Replace',
                                            }),
    ['Lua.completion.keywordSnippet']       = Type.String:setDefault('Replace'):setEnums({
                                                'Disable',
                                                'Both',
                                                'Replace',
                                            }),
    ['Lua.completion.displayContext']       = Type.Integer:setDefault(0),
    ['Lua.completion.workspaceWord']        = Type.Boolean:setDefault(true),
    ['Lua.completion.showWord']             = Type.String:setDefault('Fallback'):setEnums({
                                                'Enable',
                                                'Fallback',
                                                'Disable',
                                            }),
    ['Lua.completion.autoRequire']          = Type.Boolean:setDefault(true),
    ['Lua.completion.maxSuggestCount']      = Type.Integer:setDefault(100),
    ['Lua.completion.showParams']           = Type.Boolean:setDefault(true),
    ['Lua.completion.requireSeparator']     = Type.String:setDefault('.'),
    ['Lua.completion.postfix']              = Type.String:setDefault('@'),
    ['Lua.signatureHelp.enable']            = Type.Boolean:setDefault(true),
    ['Lua.hover.enable']                    = Type.Boolean:setDefault(true),
    ['Lua.hover.viewString']                = Type.Boolean:setDefault(true),
    ['Lua.hover.viewStringMax']             = Type.Integer:setDefault(1000),
    ['Lua.hover.viewNumber']                = Type.Boolean:setDefault(true),
    ['Lua.hover.previewFields']             = Type.Integer:setDefault(10),
    ['Lua.hover.enumsLimit']                = Type.Integer:setDefault(5),
    ['Lua.hover.expandAlias']               = Type.Boolean:setDefault(true),
    ['Lua.semantic.enable']                 = Type.Boolean:setDefault(true),
    ['Lua.semantic.variable']               = Type.Boolean:setDefault(true),
    ['Lua.semantic.annotation']             = Type.Boolean:setDefault(true),
    ['Lua.semantic.keyword']                = Type.Boolean:setDefault(false),
    ['Lua.hint.enable']                     = Type.Boolean:setDefault(false),
    ['Lua.hint.paramType']                  = Type.Boolean:setDefault(true),
    ['Lua.hint.setType']                    = Type.Boolean:setDefault(false),
    ['Lua.hint.paramName']                  = Type.String:setDefault('All'):setEnums({
                                                'All',
                                                'Literal',
                                                'Disable',
                                            }),
    ['Lua.hint.await']                      = Type.Boolean:setDefault(true),
    ['Lua.hint.awaitPropagate']             = Type.Boolean:setDefault(false),
    ['Lua.hint.arrayIndex']                 = Type.String:setDefault('Auto'):setEnums({
                                                'Enable',
                                                'Auto',
                                                'Disable',
                                            }),
    ['Lua.hint.semicolon']                  = Type.String:setDefault('SameLine'):setEnums({
                                                'All',
                                                'SameLine',
                                                'Disable',
                                            }),
    ['Lua.window.statusBar']                = Type.Boolean:setDefault(true),
    ['Lua.window.progressBar']              = Type.Boolean:setDefault(true),
    ['Lua.codeLens.enable']                 = Type.Boolean:setDefault(false),
    ['Lua.format.enable']                   = Type.Boolean:setDefault(true),
    ['Lua.format.defaultConfig']            = Type.Hash(Type.String, Type.String)
                                            :setDefault({}),
    ['Lua.typeFormat.config']               = Type.Hash(Type.String, Type.String)
                                            :setDefault({
                                                format_line = "true",
                                                auto_complete_end = "true",
                                                auto_complete_table_sep = "true"
                                            }),
    ['Lua.spell.dict']                      = Type.Array(Type.String),
    ['Lua.nameStyle.config']                = Type.Hash(Type.String, Type.Or(Type.String, Type.Array(Type.Hash(Type.String, Type.String))))
                                            :setDefault({}),
    ['Lua.misc.parameters']                 = Type.Array(Type.String),
    ['Lua.misc.executablePath']             = Type.String,
    ['Lua.language.fixIndent']              = Type.Boolean:setDefault(true),
    ['Lua.language.completeAnnotation']     = Type.Boolean:setDefault(true),
    ['Lua.type.castNumberToInteger']        = Type.Boolean:setDefault(true),
    ['Lua.type.weakUnionCheck']             = Type.Boolean:setDefault(false),
    ['Lua.type.maxUnionVariants']           = Type.Integer:setDefault(0),
    ['Lua.type.weakNilCheck']               = Type.Boolean:setDefault(false),
    ['Lua.type.inferParamType']             = Type.Boolean:setDefault(false),
    ['Lua.type.checkTableShape']            = Type.Boolean:setDefault(false),
    ['Lua.type.inferTableSize']             = Type.Integer:setDefault(10),
    ['Lua.doc.privateName']                 = Type.Array(Type.String),
    ['Lua.doc.protectedName']               = Type.Array(Type.String),
    ['Lua.doc.packageName']                 = Type.Array(Type.String),
    ['Lua.doc.regengine']                   = Type.String:setDefault('glob'):setEnums({
                                                'glob',
                                                'lua',
                                            }),
    --testma
    ["Lua.docScriptPath"]                   = Type.String,
    ["Lua.addonRepositoryPath"]             = Type.String,
    -- VSCode
    ["Lua.addonManager.enable"]             = Type.Boolean:setDefault(true),
    ["Lua.addonManager.repositoryPath"]     = Type.String,
    ["Lua.addonManager.repositoryBranch"]   = Type.String,
    ['files.associations']                  = Type.Hash(Type.String, Type.String),
                                            -- copy from VSCode default
    ['files.exclude']                       = Type.Hash(Type.String, Type.Boolean):setDefault({
                                                ["**/.DS_Store"] = true,
                                                ["**/.git"]      = true,
                                                ["**/.hg"]       = true,
                                                ["**/.svn"]      = true,
                                                ["**/CVS"]       = true,
                                                ["**/Thumbs.db"] = true,
                                            }),
    ['editor.semanticHighlighting.enabled'] = Type.Or(Type.Boolean, Type.String),
    ['editor.acceptSuggestionOnEnter']      = Type.String:setDefault('on'),
}

do
    local versionUnit = template['Lua.runtime.version']
    unitAliases[versionUnit] = {
        'Lua 5.2',
    }
    local defaultLoader = versionUnit.loader
    versionUnit.loader = function (self, value)
        if value == 'Lua 5.2' then
            return 'Moonsharp 2.0.0.0'
        end
        return defaultLoader(self, value)
    end
end


return template
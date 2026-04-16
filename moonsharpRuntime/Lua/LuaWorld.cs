using MoonSharp.Interpreter;

namespace Moonsharpy.ScriptEngines.Lua;

/// <summary>
/// Represents the Lua execution context for a single script, including the main Script object and all STATIC tables/functions exposed to Lua.
/// This is created fresh for each script execution and passed to all helper methods that register functions/tables in the Lua environment.
/// It also tracks any IDisposable resources created during execution to ensure they can be cleaned up at the end of the script's lifecycle, preventing resource leaks across multiple script executions.
/// </summary>
internal class LuaWorld {
    /* :: :: Properties :: START :: */

    /// <summary>
    /// Gets the MoonSharp script instance for this Lua execution context.
    /// </summary>
    internal Script LuaScript { get; }

    /// <summary>
    /// Gets the path of the script being executed.
    /// </summary>
    internal string LuaScriptPath { get; }

    /// <summary>
    /// Gets the script argument table exposed to Lua as <c>arg</c>.
    /// </summary>
    internal Table ScriptArguments { get; private set; }

    /// <summary>
    /// Resolves builtin modules such as bee.filesystem, bee.sys, and bee.lua before package.path lookup.
    /// </summary>
    internal BuiltinModuleResolver? BuiltinModuleResolver { get; set; }

    /* :: :: Properties :: END :: */
    // //
    /* :: :: Fields :: START :: */

    private readonly List<System.IDisposable> _openDisposables = new List<IDisposable>();
    private readonly Lock _openDisposablesLock = new Lock();

    /* :: :: Fields :: END :: */
    // //
    /* :: :: Helpers :: START :: */

    private static string? ResolveModulePath(string moduleName, string packagePath) {
        var modulePath = moduleName.Replace('.', System.IO.Path.DirectorySeparatorChar).Replace('/', System.IO.Path.DirectorySeparatorChar);

        foreach (var template in packagePath.Split(';')) {
            if (string.IsNullOrWhiteSpace(template)) {
                continue;
            }

            var candidatePath = template.Replace("?", modulePath);
            if (System.IO.File.Exists(candidatePath)) {
                return candidatePath;
            }
        }

        return null;
    }

    private static string PrepareMoonSharpSource(string source) {
        return source.Replace("<close>", string.Empty);
    }

    private Table GetOrCreateGlobalTable(string name) {
        var value = LuaScript.Globals.Get(name);
        if (value.Type == DataType.Table) {
            return value.Table;
        }

        var table = new Table(LuaScript);
        LuaScript.Globals[name] = table;
        return table;
    }

    private static bool TryGetIntegerLike(double value, out long integerValue) {
        integerValue = 0;
        if (double.IsNaN(value) || double.IsInfinity(value)) {
            return false;
        }

        var truncated = System.Math.Truncate(value);
        if (truncated != value) {
            return false;
        }

        if (truncated < long.MinValue || truncated > long.MaxValue) {
            return false;
        }

        integerValue = (long)truncated;
        return true;
    }

    private static bool TryReadCodePoint(string text, int charIndex, out int codePoint, out int charsConsumed) {
        codePoint = 0;
        charsConsumed = 0;

        if (charIndex < 0 || charIndex >= text.Length) {
            return false;
        }

        char first = text[charIndex];
        if (char.IsHighSurrogate(first)) {
            if (charIndex + 1 >= text.Length || !char.IsLowSurrogate(text[charIndex + 1])) {
                return false;
            }

            codePoint = char.ConvertToUtf32(first, text[charIndex + 1]);
            charsConsumed = 2;
            return true;
        }

        if (char.IsLowSurrogate(first)) {
            return false;
        }

        codePoint = first;
        charsConsumed = 1;
        return true;
    }

    private static bool TryGetCodePointAtIndex(string text, int codePointIndex, out int charIndex, out int codePoint, out int charsConsumed) {
        charIndex = 0;
        codePoint = 0;
        charsConsumed = 0;

        if (codePointIndex < 1) {
            return false;
        }

        var currentIndex = 1;
        var position = 0;
        while (position < text.Length) {
            if (!TryReadCodePoint(text, position, out codePoint, out charsConsumed)) {
                return false;
            }

            if (currentIndex == codePointIndex) {
                charIndex = position;
                return true;
            }

            position += charsConsumed;
            currentIndex++;
        }

        return false;
    }

    private void InstallMathCompatibility() {
        var mathTable = GetOrCreateGlobalTable("math");

        mathTable["tointeger"] = DynValue.NewCallback((context, callbackArguments) => {
            if (callbackArguments.Count < 1 || callbackArguments[0].Type != DataType.Number) {
                return DynValue.NewNil();
            }

            double value = callbackArguments[0].Number;
            if (double.IsNaN(value) || double.IsInfinity(value)) {
                return DynValue.NewNil();
            }

            double integerValue = System.Math.Truncate(value);
            if (integerValue != value) {
                return DynValue.NewNil();
            }

            return DynValue.NewNumber(integerValue);
        }, "math.tointeger");

        mathTable["type"] = DynValue.NewCallback((context, callbackArguments) => {
            if (callbackArguments.Count < 1 || callbackArguments[0].Type != DataType.Number) {
                return DynValue.NewNil();
            }

            return TryGetIntegerLike(callbackArguments[0].Number, out _)
                ? DynValue.NewString("integer")
                : DynValue.NewString("float");
        }, "math.type");

        mathTable["ult"] = DynValue.NewCallback((context, callbackArguments) => {
            if (callbackArguments.Count < 2) {
                return DynValue.NewNil();
            }

            if (!TryGetIntegerLike(callbackArguments[0].Number, out var left) || !TryGetIntegerLike(callbackArguments[1].Number, out var right)) {
                return DynValue.NewNil();
            }

            var leftUnsigned = unchecked((ulong)left);
            var rightUnsigned = unchecked((ulong)right);
            return DynValue.NewBoolean(leftUnsigned < rightUnsigned);
        }, "math.ult");

        mathTable["maxinteger"] = DynValue.NewNumber(9007199254740991d);
        mathTable["mininteger"] = DynValue.NewNumber(-9007199254740991d);
        mathTable["pi"] = DynValue.NewNumber(System.Math.PI);
        mathTable["huge"] = DynValue.NewNumber(double.PositiveInfinity);
    }

    private void InstallTableCompatibility() {
        var table = GetOrCreateGlobalTable("table");

        table["move"] = DynValue.NewCallback((context, callbackArguments) => {
            if (callbackArguments.Count < 4 || callbackArguments[0].Type != DataType.Table || callbackArguments[1].Type != DataType.Number || callbackArguments[2].Type != DataType.Number || callbackArguments[3].Type != DataType.Number) {
                return DynValue.NewNil();
            }

            var sourceTable = callbackArguments[0].Table;
            var startIndex = (int)callbackArguments[1].Number;
            var endIndex = (int)callbackArguments[2].Number;
            var targetIndex = (int)callbackArguments[3].Number;
            var destinationTable = callbackArguments.Count > 4 && callbackArguments[4].Type == DataType.Table ? callbackArguments[4].Table : sourceTable;

            if (endIndex < startIndex) {
                return DynValue.NewTable(destinationTable);
            }

            var count = endIndex - startIndex + 1;
            var values = new DynValue[count];
            for (var i = 0; i < count; i++) {
                values[i] = sourceTable.Get(startIndex + i);
            }

            for (var i = 0; i < count; i++) {
                destinationTable[targetIndex + i] = values[i];
            }

            return DynValue.NewTable(destinationTable);
        }, "table.move");

        table["pack"] = DynValue.NewCallback((context, callbackArguments) => {
            var resultTable = new Table(LuaScript);
            for (var i = 0; i < callbackArguments.Count; i++) {
                resultTable[i + 1] = callbackArguments[i];
            }

            resultTable["n"] = DynValue.NewNumber(callbackArguments.Count);
            return DynValue.NewTable(resultTable);
        }, "table.pack");

        table["unpack"] = DynValue.NewCallback((context, callbackArguments) => {
            if (callbackArguments.Count < 1 || callbackArguments[0].Type != DataType.Table) {
                return DynValue.NewNil();
            }

            var sourceTable = callbackArguments[0].Table;
            var startIndex = callbackArguments.Count > 1 && callbackArguments[1].Type == DataType.Number ? (int)callbackArguments[1].Number : 1;
            var hasExplicitEnd = callbackArguments.Count > 2 && callbackArguments[2].Type == DataType.Number;
            var endIndex = hasExplicitEnd ? (int)callbackArguments[2].Number : int.MaxValue;

            if (startIndex < 1) {
                startIndex = 1;
            }

            var values = new List<DynValue>();
            for (var index = startIndex; index <= endIndex; index++) {
                var value = sourceTable.Get(index);
                if (!hasExplicitEnd && value.Type == DataType.Nil) {
                    break;
                }

                values.Add(value);
                if (index == int.MaxValue) {
                    break;
                }
            }

            return DynValue.NewTuple(values.ToArray());
        }, "table.unpack");
    }

    private void InstallUtf8Compatibility() {
        var utf8Table = GetOrCreateGlobalTable("utf8");
        utf8Table["charpattern"] = DynValue.NewString("[\u0000-\u007F\u00C2-\u00F4][\u0080-\u00BF]*");

        utf8Table["char"] = DynValue.NewCallback((context, callbackArguments) => {
            if (callbackArguments.Count < 1) {
                return DynValue.NewNil();
            }

            var builder = new System.Text.StringBuilder();
            for (var i = 0; i < callbackArguments.Count; i++) {
                if (callbackArguments[i].Type != DataType.Number) {
                    return DynValue.NewNil();
                }

                builder.Append(char.ConvertFromUtf32((int)callbackArguments[i].Number));
            }

            return DynValue.NewString(builder.ToString());
        }, "utf8.char");

        utf8Table["codes"] = DynValue.NewCallback((context, callbackArguments) => {
            if (callbackArguments.Count < 1 || callbackArguments[0].Type != DataType.String) {
                return DynValue.NewTuple(DynValue.NewNil(), DynValue.NewString("utf8.codes expects a string"));
            }

            var text = callbackArguments[0].String;
            var position = 0;
            var iterator = DynValue.NewCallback((_, __) => {
                if (position >= text.Length) {
                    return DynValue.NewNil();
                }

                if (!TryReadCodePoint(text, position, out var codePoint, out var charsConsumed)) {
                    return DynValue.NewNil();
                }

                var currentPosition = position + 1;
                position += charsConsumed;
                return DynValue.NewTuple(DynValue.NewNumber(currentPosition), DynValue.NewNumber(codePoint));
            }, "utf8.codes.iter");

            return DynValue.NewTuple(iterator, DynValue.NewString(text), DynValue.NewNumber(0));
        }, "utf8.codes");

        utf8Table["codepoint"] = DynValue.NewCallback((context, callbackArguments) => {
            if (callbackArguments.Count < 1 || callbackArguments[0].Type != DataType.String) {
                return DynValue.NewNil();
            }

            var text = callbackArguments[0].String;
            var startIndex = callbackArguments.Count > 1 && callbackArguments[1].Type == DataType.Number ? (int)callbackArguments[1].Number : 1;
            var endIndex = callbackArguments.Count > 2 && callbackArguments[2].Type == DataType.Number ? (int)callbackArguments[2].Number : startIndex;

            if (startIndex < 1) {
                startIndex = 1;
            }

            if (endIndex < startIndex) {
                return DynValue.NewNil();
            }

            var values = new List<DynValue>();
            for (var index = 1; index <= endIndex; index++) {
                if (!TryGetCodePointAtIndex(text, index, out _, out var codePoint, out _)) {
                    return DynValue.NewNil();
                }

                if (index >= startIndex) {
                    values.Add(DynValue.NewNumber(codePoint));
                }
            }

            return values.Count == 0 ? DynValue.NewNil() : DynValue.NewTuple(values.ToArray());
        }, "utf8.codepoint");

        utf8Table["len"] = DynValue.NewCallback((context, callbackArguments) => {
            if (callbackArguments.Count < 1 || callbackArguments[0].Type != DataType.String) {
                return DynValue.NewNil();
            }

            var text = callbackArguments[0].String;
            var count = 0;
            var position = 0;
            while (position < text.Length) {
                if (!TryReadCodePoint(text, position, out _, out var charsConsumed)) {
                    return DynValue.NewTuple(DynValue.NewNil(), DynValue.NewNumber(position + 1));
                }

                position += charsConsumed;
                count++;
            }

            return DynValue.NewTuple(DynValue.NewNumber(count));
        }, "utf8.len");

        utf8Table["offset"] = DynValue.NewCallback((context, callbackArguments) => {
            if (callbackArguments.Count < 2 || callbackArguments[0].Type != DataType.String || callbackArguments[1].Type != DataType.Number) {
                return DynValue.NewNil();
            }

            var text = callbackArguments[0].String;
            var codePointOffset = (int)callbackArguments[1].Number;
            var startIndex = callbackArguments.Count > 2 && callbackArguments[2].Type == DataType.Number ? (int)callbackArguments[2].Number : 1;

            if (startIndex < 1) {
                startIndex = 1;
            }

            if (codePointOffset == 0) {
                return DynValue.NewNumber(startIndex);
            }

            if (codePointOffset < 0) {
                return DynValue.NewNil();
            }

            var current = 1;
            var position = 0;
            while (position < text.Length) {
                if (!TryReadCodePoint(text, position, out _, out var charsConsumed)) {
                    return DynValue.NewNil();
                }

                if (current >= startIndex && current == startIndex + codePointOffset - 1) {
                    return DynValue.NewNumber(position + 1);
                }

                position += charsConsumed;
                current++;
            }

            return DynValue.NewNil();
        }, "utf8.offset");
    }

    private void InstallRequireShim() {
        LuaScript.Globals["require"] = DynValue.NewCallback((context, callbackArguments) => {
            if (callbackArguments.Count < 1) {
                throw new System.ArgumentException("require expects a module name", nameof(callbackArguments));
            }

            var moduleNameValue = callbackArguments[0];
            if (moduleNameValue.Type != DataType.String) {
                throw new System.ArgumentException("require expects a string module name", nameof(callbackArguments));
            }

            var moduleName = moduleNameValue.String;
            var packageValue = LuaScript.Globals.Get("package");
            if (packageValue.Type != DataType.Table) {
                throw new System.InvalidOperationException("Lua package table is not available");
            }

            var packageTable = packageValue.Table;
            var loadedValue = packageTable.Get("loaded");
            if (loadedValue.Type != DataType.Table) {
                loadedValue = DynValue.NewTable(LuaScript);
                packageTable["loaded"] = loadedValue;
            }

            var loadedTable = loadedValue.Table;
            var cachedValue = loadedTable.Get(moduleName);
            if (cachedValue.Type != DataType.Nil && cachedValue.Type != DataType.Void && cachedValue.Type != DataType.Boolean || cachedValue.Boolean) {
                return cachedValue;
            }

            if (BuiltinModuleResolver != null && BuiltinModuleResolver(this, moduleName, out var builtinModule)) {
                loadedTable[moduleName] = builtinModule;
                return builtinModule;
            }

            var packagePathValue = packageTable.Get("path");
            var packagePath = packagePathValue.Type == DataType.String ? packagePathValue.String : string.Empty;
            var modulePath = ResolveModulePath(moduleName, packagePath);

            if (modulePath == null) {
                throw new System.IO.FileNotFoundException($"module '{moduleName}' not found");
            }

            loadedTable[moduleName] = DynValue.NewBoolean(true);

            try {
                var source = PrepareMoonSharpSource(System.IO.File.ReadAllText(modulePath));
                var chunk = LuaScript.LoadString(source, codeFriendlyName: modulePath);
                System.Console.Error.WriteLine($"[Moonsharpy] require('{moduleName}') -> loading {modulePath}");
                var result = LuaScript.Call(chunk) ?? DynValue.NewNil();
                System.Console.Error.WriteLine($"[Moonsharpy] require('{moduleName}') -> result type {result.Type}");

                if (result.Type == DataType.Nil || result.Type == DataType.Void) {
                    result = DynValue.NewBoolean(true);
                }

                loadedTable[moduleName] = result;
                return result;
            } catch (Exception ex) {
                System.Console.Error.WriteLine($"[Moonsharpy] require('{moduleName}') failed: {ex}");
                loadedTable[moduleName] = DynValue.NewNil();
                throw;
            }
        }, "require");

        LuaScript.Globals["loadfile"] = DynValue.NewCallback((context, callbackArguments) => {
            if (callbackArguments.Count < 1 || callbackArguments[0].Type != DataType.String) {
                return DynValue.NewTuple(DynValue.NewNil(), DynValue.NewString("loadfile expects a filename"));
            }

            var fileName = callbackArguments[0].String;
            if (string.IsNullOrWhiteSpace(fileName)) {
                return DynValue.NewTuple(DynValue.NewNil(), DynValue.NewString("loadfile expects a filename"));
            }

            try {
                var fullPath = System.IO.Path.GetFullPath(fileName);
                var source = PrepareMoonSharpSource(System.IO.File.ReadAllText(fullPath));
                var chunk = LuaScript.LoadString(source, codeFriendlyName: fullPath);
                return chunk;
            } catch (Exception ex) {
                return DynValue.NewTuple(DynValue.NewNil(), DynValue.NewString(ex.Message));
            }
        }, "loadfile");

        LuaScript.Globals["dofile"] = DynValue.NewCallback((context, callbackArguments) => {
            if (callbackArguments.Count < 1 || callbackArguments[0].Type != DataType.String) {
                throw new System.ArgumentException("dofile expects a filename", nameof(callbackArguments));
            }

            var fileName = callbackArguments[0].String;
            if (string.IsNullOrWhiteSpace(fileName)) {
                throw new System.ArgumentException("dofile expects a filename", nameof(callbackArguments));
            }

            var fullPath = System.IO.Path.GetFullPath(fileName);
            var source = PrepareMoonSharpSource(System.IO.File.ReadAllText(fullPath));
            var chunk = LuaScript.LoadString(source, codeFriendlyName: fullPath);
            return LuaScript.Call(chunk);
        }, "dofile");
    }

    /* :: :: Helpers :: END :: */
    // //
    /* :: :: Nested Types :: START :: */



    /* :: :: Nested Types :: END :: */
    // //
    /* :: :: Constructors :: START :: */

    // Constructor
    /// <summary>
    /// Creates a new Lua world for a single script execution.
    /// </summary>
    internal LuaWorld(Script _luaScript, string _scriptPath) {
        LuaScript = _luaScript;
        LuaScriptPath = _scriptPath;

        var fullScriptPath = System.IO.Path.GetFullPath(_scriptPath);
        var fullScriptDir = System.IO.Path.GetDirectoryName(fullScriptPath) ?? AppContext.BaseDirectory;

        LuaScript.Globals["ThisScriptPath"] = DynValue.NewString(fullScriptPath);
        LuaScript.Globals["ThisScriptDir"] = DynValue.NewString(fullScriptDir);
        InstallRequireShim();
        InstallMathCompatibility();
        InstallTableCompatibility();
        InstallUtf8Compatibility();

        // global tables, alongside sdk table, to be set as Script.Globals[""] in LuaScriptAction.private.cs::SetupCoreFunctions()
        // here only for centralized management of all tables


        ScriptArguments = new Table(LuaScript);

    }

    /* :: :: Constructors :: END :: */
    // //
    /* :: :: Methods :: START :: */

    /// <summary>
    /// Tracks a disposable resource created for this Lua execution.
    /// </summary>
    internal void RegisterDisposable(System.IDisposable? disposable) {
        if (disposable == null) {
            return;
        }

        lock (_openDisposablesLock) {
            _openDisposables.Add(disposable);
        }
    }

    /// <summary>
    /// Exposes forwarded command-line arguments to Lua as the global <c>arg</c> table and the compatibility <c>argv</c>/<c>argc</c> globals.
    /// </summary>
    internal void SetScriptArguments(System.Collections.Generic.IEnumerable<string>? scriptArguments) {
        ScriptArguments = new Table(LuaScript);

        var forwardedArgs = new List<DynValue>();
        var processPath = System.Environment.ProcessPath;
        var executableValue = DynValue.NewString(string.IsNullOrWhiteSpace(processPath) ? LuaScriptPath : processPath);
        var mainScriptValue = DynValue.NewString("!main.lua");

        ScriptArguments[-1] = executableValue;
        ScriptArguments[0] = mainScriptValue;

        var argMetadata = new Table(LuaScript);
        argMetadata["__index"] = DynValue.NewCallback((_, callbackArgs) => {
            if (callbackArgs.Count < 2) {
                return DynValue.NewNil();
            }

            var key = callbackArgs[1];
            if (key.Type != DataType.Number) {
                return DynValue.NewNil();
            }

            var index = (int)key.Number;
            if (index == -1) {
                return executableValue;
            }

            if (index == 0) {
                return mainScriptValue;
            }

            if (index > 0 && index <= forwardedArgs.Count) {
                return forwardedArgs[index - 1];
            }

            return DynValue.NewNil();
        }, "arg_index");

        argMetadata["__pairs"] = DynValue.NewCallback((_, _) => {
            var orderedKeys = new List<DynValue>();
            var orderedValues = new List<DynValue>();

            for (var i = 1; i <= forwardedArgs.Count; i++) {
                orderedKeys.Add(DynValue.NewNumber(i));
                orderedValues.Add(forwardedArgs[i - 1]);
            }

            orderedKeys.Add(DynValue.NewNumber(-1));
            orderedValues.Add(executableValue);

            orderedKeys.Add(DynValue.NewNumber(0));
            orderedValues.Add(mainScriptValue);

            var position = -1;
            var iterator = DynValue.NewCallback((_, _) => {
                position++;
                if (position >= orderedKeys.Count) {
                    return DynValue.NewNil();
                }

                return DynValue.NewTuple(orderedKeys[position], orderedValues[position]);
            }, "arg_pairs_iter");

            return DynValue.NewTuple(iterator, DynValue.NewNil(), DynValue.NewNil());
        }, "arg_pairs");

        ScriptArguments.MetaTable = argMetadata;

        var packageValue = LuaScript.Globals.Get("package");
        if (packageValue.Type == DataType.Table) {
            var packageTable = packageValue.Table;
            var searchersValue = packageTable.Get("searchers");

            if (searchersValue.Type == DataType.Table) {
                packageTable["loaders"] = searchersValue.Table;
            } else {
                var searchersTable = new Table(LuaScript);
                packageTable["searchers"] = searchersTable;
                packageTable["loaders"] = searchersTable;
            }

            var syntheticCPath = System.IO.Path.Combine(System.AppContext.BaseDirectory, "?.dll");
            var existingCPath = packageTable.Get("cpath");

            if (existingCPath.Type == DataType.String && !string.IsNullOrWhiteSpace(existingCPath.String)) {
                packageTable["cpath"] = syntheticCPath + ";" + existingCPath.String;
            } else {
                packageTable["cpath"] = syntheticCPath;
            }
        }

        var argvTable = new Table(LuaScript);
        var argc = 0;

        var index = 1;
        if (scriptArguments is not null) {
            foreach (var scriptArgument in scriptArguments) {
                var argumentValue = DynValue.NewString(scriptArgument);
                forwardedArgs.Add(argumentValue);
                ScriptArguments[index++] = argumentValue;
                argvTable[++argc] = argumentValue;
            }
        }

        LuaScript.Globals["arg"] = ScriptArguments;
        LuaScript.Globals["argv"] = argvTable;
        LuaScript.Globals["argc"] = argc;
    }

    /// <summary>
    /// Removes a disposable resource from tracking once it has been closed.
    /// </summary>
    internal void UnregisterDisposable(System.IDisposable? disposable) {
        if (disposable == null) {
            return;
        }

        lock (_openDisposablesLock) {
            _openDisposables.Remove(disposable);
        }
    }

    /// <summary>
    /// Disposes any tracked resources that remain open at the end of execution.
    /// </summary>
    internal void DisposeOpenDisposables() {
        System.IDisposable[] disposables;
        lock (_openDisposablesLock) {
            disposables = _openDisposables.ToArray();
            _openDisposables.Clear();
        }

        foreach (System.IDisposable disposable in disposables) {
            try {
                disposable.Dispose();
            } catch (Exception ex) {
                System.Console.Error.WriteLine($"Error disposing LuaWorld resource: {ex}");
            }
        }
    }

    /* :: :: Methods :: END :: */

}


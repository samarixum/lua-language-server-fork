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

    private void InstallMathCompatibility() {
        var mathValue = LuaScript.Globals.Get("math");
        Table mathTable;
        if (mathValue.Type == DataType.Table) {
            mathTable = mathValue.Table;
        } else {
            mathTable = new Table(LuaScript);
            LuaScript.Globals["math"] = mathTable;
        }

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

        mathTable["maxinteger"] = DynValue.NewNumber(9007199254740991d);
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
                var source = System.IO.File.ReadAllText(modulePath);
                var chunk = LuaScript.LoadString(source, codeFriendlyName: modulePath);
                var result = LuaScript.Call(chunk);

                if (result.Type == DataType.Nil || result.Type == DataType.Void) {
                    result = DynValue.NewBoolean(true);
                }

                loadedTable[moduleName] = result;
                return result;
            } catch {
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
                var source = System.IO.File.ReadAllText(fullPath);
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
            var source = System.IO.File.ReadAllText(fullPath);
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

        ScriptArguments[-1] = DynValue.NewString(string.IsNullOrWhiteSpace(processPath) ? LuaScriptPath : processPath);
        ScriptArguments[0] = DynValue.NewString("!main.lua");

        var argMetadata = new Table(LuaScript);
        argMetadata["__pairs"] = DynValue.NewCallback((context, callbackArgs) => {
            var orderedKeys = new List<DynValue>();
            var orderedValues = new List<DynValue>();

            for (var i = 1; i <= forwardedArgs.Count; i++) {
                orderedKeys.Add(DynValue.NewNumber(i));
                orderedValues.Add(ScriptArguments.Get(i));
            }

            orderedKeys.Add(DynValue.NewNumber(-1));
            orderedValues.Add(ScriptArguments.Get(-1));

            orderedKeys.Add(DynValue.NewNumber(0));
            orderedValues.Add(ScriptArguments.Get(0));

            var position = -1;
            var iterator = DynValue.NewCallback((iteratorContext, iteratorArgs) => {
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


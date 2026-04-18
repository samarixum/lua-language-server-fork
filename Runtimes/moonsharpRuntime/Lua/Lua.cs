// lua interpreter
using MoonSharp.Interpreter;

namespace Moonsharpy.ScriptEngines.Lua;

// custom exception to signal script exit without treating it as an error
internal class ScriptExitException(int exitCode = 0) : Exception {
    internal int ExitCode { get; } = exitCode;
}

/// <summary>
/// entry point for executing a Lua script, called from Moonsharpy.ScriptEngines.Helpers.EmbeddedActionDispatcher
/// </summary>
public sealed class Main(string scriptPath, IEnumerable<string>? args, CoreModules coreModules) {

    private readonly string _scriptPath = scriptPath;
    private readonly string[] _args = args is null ? System.Array.Empty<string>() : args as string[] ?? new List<string>(args).ToArray();
    private readonly CoreModules _coreModules = coreModules;

    /// <summary>
    /// Returns true when the exception chain contains the known MoonSharp iterator prep NullReferenceException.
    /// </summary>
    /// <param name="exception">The exception to inspect.</param>
    /// <returns>True when the stack indicates the known ExecIterPrep failure path.</returns>
    private static bool IsMoonSharpIteratorPrepNullReference(Exception exception) {
        Exception? current = exception;
        while (current is not null) {
            if (current is NullReferenceException && current.StackTrace?.Contains("MoonSharp.Interpreter.Execution.VM.Processor.ExecIterPrep", StringComparison.Ordinal) == true) {
                return true;
            }

            current = current.InnerException;
        }

        return false;
    }

    //
    public async Task ExecuteAsync(CancellationToken cancellationToken = default(CancellationToken)) {
        bool ok = false;
        int exitCode = 0;
        System.Exception? executionError = null;
        LuaWorld? LuaWorld = null;
        try {
            if (!System.IO.File.Exists(this._scriptPath)) {
                throw new System.IO.FileNotFoundException("Lua script not found", this._scriptPath);
            }

            // read script code
            string code = await System.IO.File.ReadAllTextAsync(this._scriptPath, cancellationToken);
            // create new Lua script environment with the requested module preset, all sandboxing is done manually
            Script LuaScript = new Script(this._coreModules);
            // object to hold all exposed tables
            LuaWorld = new LuaWorld(LuaScript, this._scriptPath);
            LuaWorld.BuiltinModuleResolver = BeeCompatibility.TryLoadModule;
            LuaDebugCompatibility.Install(LuaWorld);

#if DEBUG
            System.Console.Error.WriteLine($"Running lua script '{this._scriptPath}' with {this._args.Length} args...");
            System.Console.Error.WriteLine($"input args: {string.Join(", ", this._args)}");
#endif

            // ::
            // ::

            // Create a fresh object array specifically for this call
            object[] argsForLua = new object[this._args.Length];
            Array.Copy(this._args, argsForLua, this._args.Length);

            LuaWorld.SetScriptArguments(this._args);

            await System.Threading.Tasks.Task.Run(() => {
                LuaWorld.LuaScript.Call(LuaWorld.LuaScript.LoadString(code, codeFriendlyName: this._scriptPath), argsForLua);
            }, cancellationToken).ConfigureAwait(false);
            ok = true;
            exitCode = 0;

        } catch (ScriptExitException exitEx) {
            // Script called os.exit, treat as normal exit without error
            exitCode = exitEx.ExitCode;
            ok = exitEx.ExitCode == 0;
            if (!ok) {
                executionError = new System.InvalidOperationException($"Lua script exited with non-zero code {exitCode}.");
            }
        } catch (MoonSharp.Interpreter.SyntaxErrorException syntaxEx) {
            // Catches parse errors (e.g. missing 'end', unexpected symbols) before the script even runs
            System.Console.Error.WriteLine($"Lua Syntax Error: {syntaxEx.DecoratedMessage}");
            System.Console.Error.WriteLine($"Error: {syntaxEx.Message}");

            exitCode = 1;
            executionError = syntaxEx;
        } catch (MoonSharp.Interpreter.ScriptRuntimeException luaEx) {
            // luaEx.DecoratedMessage contains the file path and line number
            string luaErrorMessage = luaEx.DecoratedMessage;

            System.Console.Error.WriteLine($"Lua Runtime Error: {luaErrorMessage}");
            System.Console.Error.WriteLine($"Error: {luaEx.Message}");

            // Print the detailed Lua Call Stack cleanly
            if (luaEx.CallStack != null && luaEx.CallStack.Count > 0) {
                System.Console.Error.WriteLine("Lua Stack Trace:");
                foreach (var frame in luaEx.CallStack) {
                    string functionName = string.IsNullOrEmpty(frame.Name) ? "main chunk" : frame.Name;
                    string location = "[C# / native code]";

                    if (frame.Location != null) {
                        string fileName = this._scriptPath;

                        // Try to get the specific file/chunk name from MoonSharp using SourceIdx
                        if (LuaWorld?.LuaScript != null) {
                            try {
                                var source = LuaWorld.LuaScript.GetSourceCode(frame.Location.SourceIdx);
                                if (source != null && !string.IsNullOrEmpty(source.Name)) {
                                    fileName = source.Name;
                                }
                            } catch {
                                // Fallback to _scriptPath if lookup fails
                            }
                        }

                        location = $"{fileName}:line {frame.Location.FromLine}";
                    }

                    System.Console.Error.WriteLine($"  at {functionName} in {location}");
                }
            }

            // Mark as failed so the engine actually reports it as a failure
            exitCode = 1;
            executionError = luaEx;
        } catch (Exception ex) {

            // 1. Check for the specific VM IndexOutOfRange crash
            if (ex is IndexOutOfRangeException &&
                ex.StackTrace?.Contains("MoonSharp.Interpreter.Execution.VM.Processor.Processing_Loop") == true) {
                string vmErrorMessage =
                    $"CRITICAL: MoonSharp VM Internal Crash (IndexOutOfRangeException) while executing '{this._scriptPath}'. " +
                    "This usually indicates a stack overflow or an infinite metamethod loop.";

                System.Console.Error.WriteLine(vmErrorMessage);

                executionError = new InvalidOperationException(vmErrorMessage, ex);
            } else if (IsMoonSharpIteratorPrepNullReference(ex)) {
                string compatibilityMessage = "Lua compatibility limitation: MoonSharp 'for ... in' iteration failure. Use pairs()/ipairs() instead.";
                executionError = new InvalidOperationException(compatibilityMessage, ex);
                System.Console.Error.WriteLine(compatibilityMessage);
            } else {
                System.Console.Error.WriteLine($"Unhandled Exception in Lua Provider: {ex.Message}");
                executionError = ex;
            }

            exitCode = 1;
        } finally {
            LuaWorld?.DisposeOpenDisposables();
        }

        if (!ok || exitCode != 0) {
            throw new System.InvalidOperationException(
                message: $"Lua script failed with exit code {exitCode}: '{this._scriptPath}'",
                innerException: executionError
            );
        }

        if (!ok) {
            throw new System.InvalidOperationException(
                message: $"Lua script failed with exit code {exitCode}: '{this._scriptPath}'",
                innerException: executionError
            );
        }
    }

}

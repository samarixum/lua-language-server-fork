using MoonSharp.Interpreter;
using MoonSharp.Interpreter.Debugging;

namespace Moonsharpy.ScriptEngines.Lua;

internal static class LuaDebugCompatibility {
    internal static void Install(LuaWorld world) {
        var script = world.LuaScript;
        var debugValue = script.Globals.Get("debug");

        Table debugTable;
        if (debugValue.Type == DataType.Table) {
            debugTable = debugValue.Table;
        } else {
            debugTable = new Table(script);
            script.Globals["debug"] = debugTable;
        }

        debugTable["getinfo"] = DynValue.NewCallback((context, args) => BuildGetInfoResult(world, context, args), "debug.getinfo");
    }

    private static DynValue BuildGetInfoResult(LuaWorld world, ScriptExecutionContext context, CallbackArguments args) {
        var script = world.LuaScript;
        DynValue target = args.Count > 0 ? args[0] : DynValue.NewNumber(1);
        var what = args.Count > 1 && args[1].Type == DataType.String && !string.IsNullOrEmpty(args[1].String)
            ? args[1].String
            : "flnStu";

        FrameInfo frame;
        bool targetIsFunction = target.Type == DataType.Function || target.Type == DataType.ClrFunction;

        if (target.Type == DataType.Number) {
            int level = (int)target.Number;
            if (!TryResolveFrameForLevel(world, context, level, out frame)) {
                return DynValue.NewNil();
            }
        } else {
            frame = ResolveCallingFrame(world, context);
        }

        var info = new Table(script);
        string source = "@" + NormalizePath(frame.Source);

        if (NeedsField(what, 'S')) {
            info["source"] = DynValue.NewString(source);
            info["short_src"] = DynValue.NewString(source);
            info["linedefined"] = DynValue.NewNumber(Math.Max(frame.Line, 0));
            info["lastlinedefined"] = DynValue.NewNumber(Math.Max(frame.Line, 0));
            info["what"] = DynValue.NewString(targetIsFunction || !frame.IsMainChunk ? "Lua" : "main");
        }

        if (NeedsField(what, 'l')) {
            info["currentline"] = DynValue.NewNumber(frame.Line);
        }

        if (NeedsField(what, 'n')) {
            if (!string.IsNullOrWhiteSpace(frame.Name)) {
                info["name"] = DynValue.NewString(frame.Name);
                info["namewhat"] = DynValue.NewString("Lua");
            } else {
                info["name"] = DynValue.NewNil();
                info["namewhat"] = DynValue.NewString(string.Empty);
            }
        }

        if (NeedsField(what, 't')) {
            info["istailcall"] = DynValue.NewBoolean(false);
        }

        if (NeedsField(what, 'u')) {
            info["nups"] = DynValue.NewNumber(0);
            info["nparams"] = DynValue.NewNumber(0);
            info["isvararg"] = DynValue.NewBoolean(false);
        }

        if (NeedsField(what, 'f')) {
            if (targetIsFunction) {
                info["func"] = target;
            } else {
                info["func"] = frame.Func.Type is DataType.Function or DataType.ClrFunction
                    ? frame.Func
                    : DynValue.NewNil();
            }
        }

        if (NeedsField(what, 'L')) {
            var activeLines = new Table(script);
            activeLines[1] = DynValue.NewBoolean(true);
            info["activelines"] = activeLines;
        }

        return DynValue.NewTable(info);
    }

    private static bool NeedsField(string what, char field) {
        return what.IndexOf(field) >= 0;
    }

    private static bool TryResolveFrameForLevel(LuaWorld world, ScriptExecutionContext context, int level, out FrameInfo frame) {
        if (level < 0) {
            frame = default;
            return false;
        }

        try {
            Coroutine coroutine = context.GetCallingCoroutine();

            int[] skipCandidates = new[] {
                Math.Max(level, 0),
                Math.Max(level - 1, 0),
                Math.Max(level + 1, 0),
            };

            bool hasFallback = false;
            FrameInfo fallback = default;

            foreach (int skip in skipCandidates) {
                WatchItem[] stack = coroutine.GetStackTrace(skip, context.CallingLocation);
                if (stack.Length == 0) {
                    continue;
                }

                for (int i = 0; i < stack.Length; i++) {
                    FrameInfo candidate = CreateFrameInfo(world, stack[i].Location, stack[i].Name, stack[i].Value);
                    if (!hasFallback) {
                        fallback = candidate;
                        hasFallback = true;
                    }

                    if (stack[i].Location != null) {
                        frame = candidate;
                        return true;
                    }
                }
            }

            if (hasFallback) {
                frame = fallback;
                return true;
            }
        } catch {
            // If stack trace APIs are unavailable in this execution path, fall back to the immediate caller location.
        }

        if (level == 1) {
            frame = ResolveCallingFrame(world, context);
            return true;
        }

        frame = default;
        return false;
    }

    private static FrameInfo ResolveCallingFrame(LuaWorld world, ScriptExecutionContext context) {
        return CreateFrameInfo(world, context.CallingLocation, null, DynValue.NewNil());
    }

    private static FrameInfo CreateFrameInfo(LuaWorld world, SourceRef? sourceRef, string? name, DynValue func) {
        int line = -1;
        string source = world.LuaScriptPath;

        if (sourceRef != null) {
            line = sourceRef.FromLine;

            try {
                SourceCode? code = world.LuaScript.GetSourceCode(sourceRef.SourceIdx);
                if (code != null && !string.IsNullOrWhiteSpace(code.Name)) {
                    source = code.Name;
                }
            } catch {
                // Fall back to LuaScriptPath when source lookup fails.
            }
        }

        bool isMainChunk = string.IsNullOrWhiteSpace(name) || string.Equals(name, "main chunk", StringComparison.OrdinalIgnoreCase);
        return new FrameInfo(source, line, name, isMainChunk, func);
    }

    private static string NormalizePath(string path) {
        return string.IsNullOrEmpty(path) ? string.Empty : path.Replace('\\', '/');
    }

    private readonly struct FrameInfo {
        internal FrameInfo(string source, int line, string? name, bool isMainChunk, DynValue func) {
            Source = source;
            Line = line;
            Name = name;
            IsMainChunk = isMainChunk;
            Func = func;
        }

        internal string Source { get; }
        internal int Line { get; }
        internal string? Name { get; }
        internal bool IsMainChunk { get; }
        internal DynValue Func { get; }
    }
}
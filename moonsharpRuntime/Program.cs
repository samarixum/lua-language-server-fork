using System;
using System.Collections.Generic;
using System.Linq;
using MoonSharp.Interpreter;

namespace Moonsharpy;


public static class Program {

    private static bool TryResolveCoreModulesPreset(string presetName, out CoreModules preset, out string errorMessage) {
        if (Enum.TryParse(presetName, ignoreCase: true, out preset) && Enum.IsDefined(typeof(CoreModules), preset)) {
            errorMessage = string.Empty;
            return true;
        }

        errorMessage = $"Invalid preset '{presetName}'. Valid CoreModules names: {string.Join(", ", Enum.GetNames<CoreModules>())}";
        return false;
    }

    private static bool TryParseLaunchArguments(string[] args, out string scriptPath, out CoreModules preset, out string[] scriptArgs, out string errorMessage) {
        scriptPath = string.Empty;
        preset = CoreModules.Preset_Default;
        scriptArgs = Array.Empty<string>();
        errorMessage = string.Empty;

        if (args.Length == 0) {
            errorMessage = "No script provided.";
            return false;
        }

        var forwardedArgs = new List<string>(args.Length);

        for (var index = 0; index < args.Length; index++) {
            var currentArg = args[index];

            if (string.Equals(currentArg, "--preset", StringComparison.OrdinalIgnoreCase)) {
                if (index + 1 >= args.Length) {
                    errorMessage = "Missing value for --preset.";
                    return false;
                }

                var presetName = args[++index];
                if (!TryResolveCoreModulesPreset(presetName, out preset, out errorMessage)) {
                    return false;
                }

                continue;
            }

            if (string.IsNullOrEmpty(scriptPath) && !currentArg.StartsWith("--", StringComparison.Ordinal)) {
                scriptPath = currentArg;
            }

            forwardedArgs.Add(currentArg);
        }

        if (string.IsNullOrWhiteSpace(scriptPath)) {
            errorMessage = "No script provided.";
            return false;
        }

        scriptArgs = forwardedArgs.ToArray();
        return true;
    }


    [STAThread]
    public static async System.Threading.Tasks.Task<int> Main(string[] args) {
        try {
            if (!TryParseLaunchArguments(args, out var scriptPath, out var preset, out var scriptArgs, out var errorMessage)) {
                await System.Console.Error.WriteLineAsync(errorMessage);
                return 1;
            }

            var moonsharp = new ScriptEngines.Lua.Main(scriptPath, scriptArgs, preset);

            await moonsharp.ExecuteAsync();

            return 1;
        } catch (System.Exception ex) {
            await System.Console.Error.WriteLineAsync($"Critical Engine Failure: {ex.Message}");
            return 1;
        }
    }

    // //
}
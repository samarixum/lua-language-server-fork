using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Threading.Tasks;

using MoonSharp.Interpreter;

namespace Moonsharpy;


public static class Program {

    private static string ResolveDefaultScriptPath() {
        var baseDirectory = AppContext.BaseDirectory;
        return Path.Combine(baseDirectory, "main.lua");
    }

    private static bool IsHelpArgument(string argument) {
        return string.Equals(argument, "/?", StringComparison.OrdinalIgnoreCase);
    }

    private static async Task PrintUsageAsync() {
        await Console.Out.WriteLineAsync("Moonsharpy Lua host");
        await Console.Out.WriteLineAsync("Usage: moonsharpy [--moonsharp-preset <CoreModules>] [script arguments...]");
        await Console.Out.WriteLineAsync(string.Empty);
        await Console.Out.WriteLineAsync("Options:");
        await Console.Out.WriteLineAsync("  /?                        Show this help text and exit.");
        await Console.Out.WriteLineAsync("  --moonsharp-preset <name> Select a MoonSharp CoreModules preset.");
        await Console.Out.WriteLineAsync(string.Empty);
        await Console.Out.WriteLineAsync("All other arguments are forwarded to the Lua script unchanged.");
    }

    private static bool TryResolveCoreModulesPreset(string presetName, out CoreModules preset, out string errorMessage) {
        if (Enum.TryParse(presetName, ignoreCase: true, out preset) && Enum.IsDefined(typeof(CoreModules), preset)) {
            errorMessage = string.Empty;
            return true;
        }

        errorMessage = $"Invalid preset '{presetName}'. Valid CoreModules names: {string.Join(", ", Enum.GetNames<CoreModules>())}";
        return false;
    }

    private static bool TryParseLaunchArguments(string[] args, out string scriptPath, out CoreModules preset, out string[] scriptArgs, out string errorMessage) {
        scriptPath = ResolveDefaultScriptPath();
        preset = CoreModules.Preset_Complete;
        scriptArgs = Array.Empty<string>();
        errorMessage = string.Empty;

        var forwardedArgs = new List<string>(args.Length);

        for (var index = 0; index < args.Length; index++) {
            var currentArg = args[index];

            if (string.Equals(currentArg, "--moonsharp-preset", StringComparison.OrdinalIgnoreCase)) {
                if (index + 1 >= args.Length) {
                    errorMessage = "Missing value for --moonsharp-preset.";
                    return false;
                }

                var presetName = args[++index];
                if (!TryResolveCoreModulesPreset(presetName, out preset, out errorMessage)) {
                    return false;
                }

                continue;
            }

            forwardedArgs.Add(currentArg);
        }

        if (string.IsNullOrWhiteSpace(scriptPath)) {
            errorMessage = "No script provided.";
            return false;
        }

        if (!File.Exists(scriptPath)) {
            errorMessage = $"Lua script not found: {scriptPath}";
            return false;
        }

        scriptArgs = forwardedArgs.ToArray();
        return true;
    }


    [STAThread]
    public static async Task<int> Main(string[] args) {
        try {
            if (args.Any(IsHelpArgument)) {
                await PrintUsageAsync();
                return 0;
            }

            if (!TryParseLaunchArguments(args, out var scriptPath, out var preset, out var scriptArgs, out var errorMessage)) {
                await Console.Error.WriteLineAsync(errorMessage);
                return 1;
            }

            var moonsharp = new ScriptEngines.Lua.Main(scriptPath, scriptArgs, preset);

            await moonsharp.ExecuteAsync();

            return 0;
        } catch (Exception ex) {
            await Console.Error.WriteLineAsync($"Critical Engine Failure: {ex.Message}");
            return 1;
        }
    }

    // //
}

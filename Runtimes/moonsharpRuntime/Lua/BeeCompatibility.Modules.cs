using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using MoonSharp.Interpreter;

namespace Moonsharpy.ScriptEngines.Lua;

internal static partial class BeeCompatibility {
    private const int SelectRead = 1;
    private const int SelectWrite = 2;

    private const int EpollIn = 0x001;
    private const int EpollPri = 0x002;
    private const int EpollOut = 0x004;
    private const int EpollErr = 0x008;
    private const int EpollHup = 0x010;
    private const int EpollRdNorm = 0x040;
    private const int EpollRdBand = 0x080;
    private const int EpollWrNorm = 0x100;
    private const int EpollWrBand = 0x200;
    private const int EpollMsg = 0x400;
    private const int EpollRdHup = 0x2000;

    private static readonly object ChannelsLock = new object();
    private static readonly Dictionary<string, BeeChannel> Channels = new Dictionary<string, BeeChannel>(StringComparer.Ordinal);

    private static readonly object ThreadHandlesLock = new object();
    private static readonly Dictionary<int, BeeThreadHandle> ThreadHandles = new Dictionary<int, BeeThreadHandle>();
    private static readonly ConcurrentQueue<string> ThreadErrors = new ConcurrentQueue<string>();
    private static int NextThreadHandleId;
    private static int NextBeeThreadId;

    private static readonly object WarningLock = new object();
    private static readonly HashSet<string> WarningOnce = new HashSet<string>(StringComparer.Ordinal);

    private static readonly object NilSentinel = new object();

    [ThreadStatic]
    private static int CurrentBeeThreadId;

    private static void RegisterCompatibilityUserData() {
    }

    private static Table CreateBeeRootModule(LuaWorld world) {
        Table root = CreateFilesystemModule(world);

        root["filesystem"] = root;
        root["sys"] = CreateSysModule(world);
        root["time"] = CreateTimeModule(world);
        root["platform"] = CreatePlatformModule(world);
        root["thread"] = CreateThreadModule(world);
        root["channel"] = CreateChannelModule(world);
        root["select"] = CreateSelectModule(world);
        root["epoll"] = CreateEpollModule(world);
        root["subprocess"] = CreateSubprocessModule(world);
        root["socket"] = CreateSocketModule(world);
        root["filewatch"] = CreateFilewatchModule(world);
        root["windows"] = CreateWindowsModule(world);
        root["serialization"] = CreateSerializationModule(world);
        root["lua"] = root;

        return root;
    }

    private static Table CreatePlatformModule(LuaWorld world) {
        Script script = world.LuaScript;
        Table module = new Table(script);

        string osLower = GetPlatformOsLower();
        string osUpper = GetPlatformOsUpper(osLower);
        string arch = GetPlatformArch();

        module["os"] = DynValue.NewString(osLower);
        module["OS"] = DynValue.NewString(osUpper);
        module["Arch"] = DynValue.NewString(arch);

        if (OperatingSystem.IsWindows()) {
            module["Compiler"] = DynValue.NewString("msvc");
            module["CRT"] = DynValue.NewString("msvc");
        } else {
            module["Compiler"] = DynValue.NewString("clang");
            module["CRT"] = DynValue.NewString("libc++");
        }

        module["CompilerVersion"] = DynValue.NewString(string.Empty);
        module["CRTVersion"] = DynValue.NewString(string.Empty);

#if DEBUG
        module["DEBUG"] = DynValue.NewBoolean(true);
#else
        module["DEBUG"] = DynValue.NewBoolean(false);
#endif

        Version version = Environment.OSVersion.Version;
        Table osVersion = new Table(script);
        osVersion["major"] = DynValue.NewNumber(version.Major);
        osVersion["minor"] = DynValue.NewNumber(version.Minor);
        osVersion["revision"] = DynValue.NewNumber(version.Build >= 0 ? version.Build : 0);
        module["os_version"] = osVersion;

        return module;
    }

    private static Table CreateWindowsModule(LuaWorld world) {
        Script script = world.LuaScript;
        Table module = new Table(script);

        module["u2a"] = DynValue.NewCallback((context, args) => {
            if (args.Count < 1 || args[0].Type != DataType.String) {
                return DynValue.NewString(string.Empty);
            }

            return DynValue.NewString(args[0].String);
        }, "bee.windows.u2a");

        module["a2u"] = DynValue.NewCallback((context, args) => {
            if (args.Count < 1 || args[0].Type != DataType.String) {
                return DynValue.NewString(string.Empty);
            }

            return DynValue.NewString(args[0].String);
        }, "bee.windows.a2u");

        module["filemode"] = DynValue.NewCallback((context, args) => DynValue.NewBoolean(true), "bee.windows.filemode");
        module["isatty"] = DynValue.NewCallback((context, args) => DynValue.NewBoolean(false), "bee.windows.isatty");

        module["write_console"] = DynValue.NewCallback((context, args) => {
            if (args.Count < 2 || args[1].Type != DataType.String) {
                return DynValue.NewNumber(0);
            }

            string message = args[1].String;
            Console.Write(message);
            return DynValue.NewNumber(message.Length);
        }, "bee.windows.write_console");

        module["is_ssd"] = DynValue.NewCallback((context, args) => DynValue.NewBoolean(false), "bee.windows.is_ssd");

        return module;
    }

    private static Table CreateSerializationModule(LuaWorld world) {
        Script script = world.LuaScript;
        Table module = new Table(script);

        module["pack"] = DynValue.NewCallback((context, args) => {
            Table packed = new Table(script);
            for (int i = 0; i < args.Count; i++) {
                packed[i + 1] = args[i];
            }
            packed["n"] = DynValue.NewNumber(args.Count);
            return DynValue.NewTable(packed);
        }, "bee.serialization.pack");

        module["unpack"] = DynValue.NewCallback((context, args) => {
            if (args.Count < 1 || args[0].Type != DataType.Table) {
                return DynValue.NewNil();
            }

            Table source = args[0].Table;
            int start = args.Count > 1 && args[1].Type == DataType.Number ? (int)args[1].Number : 1;
            int finish;
            DynValue count = source.Get("n");
            if (args.Count > 2 && args[2].Type == DataType.Number) {
                finish = (int)args[2].Number;
            } else if (count.Type == DataType.Number) {
                finish = (int)count.Number;
            } else {
                finish = source.Length;
            }

            if (finish < start) {
                return DynValue.NewNil();
            }

            int outputCount = finish - start + 1;
            DynValue[] tuple = new DynValue[outputCount];
            for (int i = 0; i < outputCount; i++) {
                tuple[i] = source.Get(start + i);
            }

            return DynValue.NewTuple(tuple);
        }, "bee.serialization.unpack");

        return module;
    }

    private static Table CreateThreadModule(LuaWorld world) {
        Script script = world.LuaScript;
        Table module = new Table(script);

        module["id"] = DynValue.NewNumber(CurrentBeeThreadId);

        module["create"] = DynValue.NewCallback((context, args) => {
            if (args.Count < 1 || args[0].Type != DataType.String) {
                throw new ArgumentException("thread.create expects a source string", nameof(args));
            }

            string source = args[0].String;
            object?[] serializedArgs = new object?[Math.Max(args.Count - 1, 0)];
            for (int i = 1; i < args.Count; i++) {
                serializedArgs[i - 1] = SerializeDynValue(args[i]);
            }

            int workerId = Interlocked.Increment(ref NextBeeThreadId);
            BeeThreadHandle handle = new BeeThreadHandle(() => {
                RunBeeThreadSource(world, source, serializedArgs, workerId);
            });

            int handleId = Interlocked.Increment(ref NextThreadHandleId);
            lock (ThreadHandlesLock) {
                ThreadHandles[handleId] = handle;
            }

            Table threadHandle = new Table(script);
            threadHandle["_bee_thread_handle"] = DynValue.NewNumber(handleId);
            return DynValue.NewTable(threadHandle);
        }, "bee.thread.create");

        module["wait"] = DynValue.NewCallback((context, args) => {
            if (args.Count < 1 || !TryGetThreadHandle(args[0], out int handleId, out BeeThreadHandle? handle)) {
                return DynValue.NewNil();
            }

            handle.Wait();

            lock (ThreadHandlesLock) {
                if (handle.IsCompleted) {
                    ThreadHandles.Remove(handleId);
                }
            }

            return DynValue.NewNil();
        }, "bee.thread.wait");

        module["sleep"] = DynValue.NewCallback((context, args) => {
            int milliseconds = args.Count > 0 && args[0].Type == DataType.Number
                ? Math.Max((int)args[0].Number, 0)
                : 0;

            Thread.Sleep(milliseconds);
            return DynValue.NewNil();
        }, "bee.thread.sleep");

        module["errlog"] = DynValue.NewCallback((context, args) => {
            if (ThreadErrors.TryDequeue(out string? message)) {
                return DynValue.NewString(message);
            }

            return DynValue.NewNil();
        }, "bee.thread.errlog");

        module["setname"] = DynValue.NewCallback((context, args) => {
            if (args.Count > 0 && args[0].Type == DataType.String) {
                try {
                    if (Thread.CurrentThread.Name == null) {
                        Thread.CurrentThread.Name = args[0].String;
                    }
                } catch {
                    // Keep compatibility behavior lenient.
                }
            }
            return DynValue.NewNil();
        }, "bee.thread.setname");

        module["preload_module"] = DynValue.NewCallback((context, args) => DynValue.NewNil(), "bee.thread.preload_module");

        return module;
    }

    private static Table CreateChannelModule(LuaWorld world) {
        Script script = world.LuaScript;
        Table module = new Table(script);

        module["create"] = DynValue.NewCallback((context, args) => {
            string name = RequireStringArgument(args, 0, "channel.create");

            lock (ChannelsLock) {
                if (Channels.ContainsKey(name)) {
                    throw new InvalidOperationException($"Duplicate channel '{name}'");
                }

                Channels[name] = new BeeChannel();
            }

            return DynValue.NewTable(CreateChannelBox(script, name));
        }, "bee.channel.create");

        module["destroy"] = DynValue.NewCallback((context, args) => {
            string name = RequireStringArgument(args, 0, "channel.destroy");
            lock (ChannelsLock) {
                Channels.Remove(name);
            }
            return DynValue.NewNil();
        }, "bee.channel.destroy");

        module["query"] = DynValue.NewCallback((context, args) => {
            string name = RequireStringArgument(args, 0, "channel.query");
            if (TryGetChannelByName(name, out BeeChannel? _)) {
                return DynValue.NewTable(CreateChannelBox(script, name));
            }

            return DynValue.NewNil();
        }, "bee.channel.query");

        return module;
    }

    private static Table CreateSelectModule(LuaWorld world) {
        Script script = world.LuaScript;
        Table module = new Table(script);

        module["SELECT_READ"] = DynValue.NewNumber(SelectRead);
        module["SELECT_WRITE"] = DynValue.NewNumber(SelectWrite);

        module["create"] = DynValue.NewCallback((context, args) => {
            return DynValue.NewTable(CreateSelectContextTable(script));
        }, "bee.select.create");

        return module;
    }

    private static Table CreateEpollModule(LuaWorld world) {
        Script script = world.LuaScript;
        Table module = new Table(script);

        module["EPOLLIN"] = DynValue.NewNumber(EpollIn);
        module["EPOLLPRI"] = DynValue.NewNumber(EpollPri);
        module["EPOLLOUT"] = DynValue.NewNumber(EpollOut);
        module["EPOLLERR"] = DynValue.NewNumber(EpollErr);
        module["EPOLLHUP"] = DynValue.NewNumber(EpollHup);
        module["EPOLLRDNORM"] = DynValue.NewNumber(EpollRdNorm);
        module["EPOLLRDBAND"] = DynValue.NewNumber(EpollRdBand);
        module["EPOLLWRNORM"] = DynValue.NewNumber(EpollWrNorm);
        module["EPOLLWRBAND"] = DynValue.NewNumber(EpollWrBand);
        module["EPOLLMSG"] = DynValue.NewNumber(EpollMsg);
        module["EPOLLRDHUP"] = DynValue.NewNumber(EpollRdHup);

        module["create"] = DynValue.NewCallback((context, args) => {
            return DynValue.NewTable(CreateEpollContextTable(script));
        }, "bee.epoll.create");

        return module;
    }

    private static Table CreateSubprocessModule(LuaWorld world) {
        Script script = world.LuaScript;
        Table module = new Table(script);

        module["get_id"] = DynValue.NewCallback((context, args) => {
            return DynValue.NewNumber(Environment.ProcessId);
        }, "bee.subprocess.get_id");

        module["setenv"] = DynValue.NewCallback((context, args) => {
            if (args.Count < 2 || args[0].Type != DataType.String || args[1].Type != DataType.String) {
                return DynValue.NewTuple(DynValue.NewNil(), DynValue.NewString("setenv expects (name, value) strings"));
            }

            try {
                Environment.SetEnvironmentVariable(args[0].String, args[1].String);
                return DynValue.NewBoolean(true);
            } catch (Exception ex) {
                return DynValue.NewTuple(DynValue.NewNil(), DynValue.NewString(ex.Message));
            }
        }, "bee.subprocess.setenv");

        module["spawn"] = DynValue.NewCallback((context, args) => {
            if (args.Count < 1 || args[0].Type != DataType.Table) {
                return DynValue.NewTuple(DynValue.NewNil(), DynValue.NewString("spawn expects options table"));
            }

            Table options = args[0].Table;
            DynValue commandValue = options.Get(1);
            if (commandValue.Type != DataType.String || string.IsNullOrWhiteSpace(commandValue.String)) {
                return DynValue.NewTuple(DynValue.NewNil(), DynValue.NewString("spawn options[1] must be command string"));
            }

            ProcessStartInfo startInfo = new ProcessStartInfo(commandValue.String) {
                UseShellExecute = false,
                RedirectStandardInput = false,
                RedirectStandardOutput = false,
                RedirectStandardError = false,
            };

            for (int i = 2; ; i++) {
                DynValue arg = options.Get(i);
                if (arg.Type == DataType.Nil || arg.Type == DataType.Void) {
                    break;
                }

                startInfo.ArgumentList.Add(arg.CastToString() ?? string.Empty);
            }

            DynValue cwd = options.Get("cwd");
            if (cwd.Type == DataType.String && !string.IsNullOrWhiteSpace(cwd.String)) {
                startInfo.WorkingDirectory = cwd.String;
            }

            DynValue env = options.Get("env");
            if (env.Type == DataType.Table) {
                foreach (TablePair pair in env.Table.Pairs) {
                    if (pair.Key.Type == DataType.String && pair.Value.Type == DataType.String) {
                        startInfo.Environment[pair.Key.String] = pair.Value.String;
                    }
                }
            }

            try {
                Process process = new Process {
                    StartInfo = startInfo,
                    EnableRaisingEvents = false,
                };

                if (!process.Start()) {
                    return DynValue.NewTuple(DynValue.NewNil(), DynValue.NewString("process failed to start"));
                }

                BeeSubprocessHandle handle = new BeeSubprocessHandle(world, process);
                return DynValue.NewTable(CreateSubprocessHandleTable(script, handle));
            } catch (Exception ex) {
                return DynValue.NewTuple(DynValue.NewNil(), DynValue.NewString(ex.Message));
            }
        }, "bee.subprocess.spawn");

        module["peek"] = DynValue.NewCallback((context, args) => {
            LogCompatibilityWarning("bee.subprocess.peek");
            return DynValue.NewTuple(DynValue.NewNil(), DynValue.NewString("bee.subprocess.peek is not implemented"));
        }, "bee.subprocess.peek");

        module["filemode"] = DynValue.NewCallback((context, args) => {
            LogCompatibilityWarning("bee.subprocess.filemode");
            return DynValue.NewNil();
        }, "bee.subprocess.filemode");

        module["select"] = DynValue.NewCallback((context, args) => {
            LogCompatibilityWarning("bee.subprocess.select");
            return DynValue.NewTuple(DynValue.NewNil(), DynValue.NewString("bee.subprocess.select is not implemented"));
        }, "bee.subprocess.select");

        return module;
    }

    private static Table CreateSocketModule(LuaWorld world) {
        Script script = world.LuaScript;
        Table module = new Table(script);

        module["create"] = DynValue.NewCallback((context, args) => {
            LogCompatibilityWarning("bee.socket.create");
            return DynValue.NewTuple(DynValue.NewNil(), DynValue.NewString("bee.socket is not implemented in MoonSharp compatibility mode"));
        }, "bee.socket.create");

        module["endpoint"] = DynValue.NewCallback((context, args) => {
            LogCompatibilityWarning("bee.socket.endpoint");
            return DynValue.NewTuple(DynValue.NewNil(), DynValue.NewString("bee.socket.endpoint is not implemented"));
        }, "bee.socket.endpoint");

        module["pair"] = DynValue.NewCallback((context, args) => {
            LogCompatibilityWarning("bee.socket.pair");
            return DynValue.NewTuple(DynValue.NewNil(), DynValue.NewNil());
        }, "bee.socket.pair");

        module["fd"] = DynValue.NewCallback((context, args) => {
            LogCompatibilityWarning("bee.socket.fd");
            return DynValue.NewNil();
        }, "bee.socket.fd");

        return module;
    }

    private static Table CreateFilewatchModule(LuaWorld world) {
        Script script = world.LuaScript;
        Table module = new Table(script);

        module["create"] = DynValue.NewCallback((context, args) => {
            return DynValue.NewTable(CreateFilewatchHandleTable(script));
        }, "bee.filewatch.create");

        return module;
    }

    private static Table CreateChannelBox(Script script, string name) {
        Table box = new Table(script);
        box["_bee_channel_name"] = DynValue.NewString(name);

        box["push"] = DynValue.NewCallback((context, args) => {
            if (args.Count < 1 || !TryResolveChannelFromBox(args[0], out BeeChannel? channel)) {
                return DynValue.NewNil();
            }

            object?[] payload = new object?[Math.Max(args.Count - 1, 0)];
            for (int i = 1; i < args.Count; i++) {
                payload[i - 1] = SerializeDynValue(args[i]);
            }

            channel.Push(payload);
            return DynValue.NewNil();
        }, "bee.channel.box.push");

        box["pop"] = DynValue.NewCallback((context, args) => {
            if (args.Count < 1 || !TryResolveChannelFromBox(args[0], out BeeChannel? channel)) {
                return DynValue.NewTuple(DynValue.NewBoolean(false), DynValue.NewNil());
            }

            if (!channel.TryPop(out object?[]? payload)) {
                return DynValue.NewTuple(DynValue.NewBoolean(false), DynValue.NewNil());
            }

            DynValue[] tuple = new DynValue[payload.Length + 1];
            tuple[0] = DynValue.NewBoolean(true);
            for (int i = 0; i < payload.Length; i++) {
                tuple[i + 1] = DeserializeDynValue(script, payload[i]);
            }

            return DynValue.NewTuple(tuple);
        }, "bee.channel.box.pop");

        box["fd"] = DynValue.NewCallback((context, args) => {
            if (args.Count < 1 || args[0].Type != DataType.Table) {
                return DynValue.NewNil();
            }

            DynValue nameValue = args[0].Table.Get("_bee_channel_name");
            if (nameValue.Type != DataType.String) {
                return DynValue.NewNil();
            }

            return DynValue.NewTable(CreateChannelFd(script, nameValue.String));
        }, "bee.channel.box.fd");

        return box;
    }

    private static Table CreateChannelFd(Script script, string channelName) {
        Table fd = new Table(script);
        string key = "channel:" + channelName;

        fd["_bee_fd_key"] = DynValue.NewString(key);
        fd["_bee_channel_name"] = DynValue.NewString(channelName);

        Table meta = new Table(script);
        meta["__tostring"] = DynValue.NewCallback((context, args) => {
            return DynValue.NewString(key);
        }, "bee.channel.fd.__tostring");
        fd.MetaTable = meta;

        return fd;
    }

    private static Table CreateSelectContextTable(Script script) {
        SelectContext state = new SelectContext();
        Table ctx = new Table(script);

        ctx["wait"] = DynValue.NewCallback((context, args) => {
            int timeout = args.Count > 1 && args[1].Type == DataType.Number
                ? (int)args[1].Number
                : 0;

            List<SelectReady> ready = state.Wait(timeout);
            int index = -1;

            DynValue iterator = DynValue.NewCallback((iteratorContext, iteratorArgs) => {
                index++;
                if (index >= ready.Count) {
                    return DynValue.NewNil();
                }

                SelectReady item = ready[index];
                return DynValue.NewTuple(item.Payload, DynValue.NewNumber(item.Events));
            }, "bee.select.wait_iter");

            return DynValue.NewTuple(iterator, DynValue.NewNil(), DynValue.NewNil());
        }, "bee.select.ctx.wait");

        ctx["close"] = DynValue.NewCallback((context, args) => {
            state.Close();
            return DynValue.NewNil();
        }, "bee.select.ctx.close");

        ctx["event_add"] = DynValue.NewCallback((context, args) => {
            if (args.Count < 3) {
                return DynValue.NewBoolean(false);
            }

            DynValue fd = args[1];
            int events = args[2].Type == DataType.Number ? (int)args[2].Number : 0;
            DynValue payload = args.Count > 3 ? args[3] : DynValue.NewNil();

            bool ok = state.EventAdd(fd, events, payload);
            return DynValue.NewBoolean(ok);
        }, "bee.select.ctx.event_add");

        ctx["event_mod"] = DynValue.NewCallback((context, args) => {
            if (args.Count < 3) {
                return DynValue.NewBoolean(false);
            }

            DynValue fd = args[1];
            int events = args[2].Type == DataType.Number ? (int)args[2].Number : 0;

            bool ok = state.EventMod(fd, events);
            return DynValue.NewBoolean(ok);
        }, "bee.select.ctx.event_mod");

        ctx["event_del"] = DynValue.NewCallback((context, args) => {
            if (args.Count < 2) {
                return DynValue.NewBoolean(false);
            }

            bool ok = state.EventDel(args[1]);
            return DynValue.NewBoolean(ok);
        }, "bee.select.ctx.event_del");

        return ctx;
    }

    private static Table CreateEpollContextTable(Script script) {
        EpollContext state = new EpollContext();
        Table ctx = new Table(script);

        ctx["wait"] = DynValue.NewCallback((context, args) => {
            int timeout = args.Count > 1 && args[1].Type == DataType.Number
                ? (int)args[1].Number
                : 0;

            List<EpollReady> ready = state.Wait(timeout);
            int index = -1;

            DynValue iterator = DynValue.NewCallback((iteratorContext, iteratorArgs) => {
                index++;
                if (index >= ready.Count) {
                    return DynValue.NewNil();
                }

                EpollReady item = ready[index];
                return DynValue.NewTuple(item.Payload, DynValue.NewNumber(item.Events));
            }, "bee.epoll.wait_iter");

            return DynValue.NewTuple(iterator, DynValue.NewNil(), DynValue.NewNil());
        }, "bee.epoll.ctx.wait");

        ctx["close"] = DynValue.NewCallback((context, args) => {
            state.Close();
            return DynValue.NewNil();
        }, "bee.epoll.ctx.close");

        ctx["event_add"] = DynValue.NewCallback((context, args) => {
            if (args.Count < 3) {
                return DynValue.NewNil();
            }

            DynValue fd = args[1];
            int events = args[2].Type == DataType.Number ? (int)args[2].Number : 0;
            DynValue payload = args.Count > 3 ? args[3] : DynValue.NewNil();

            return state.EventAdd(fd, events, payload) ? DynValue.NewBoolean(true) : DynValue.NewNil();
        }, "bee.epoll.ctx.event_add");

        ctx["event_mod"] = DynValue.NewCallback((context, args) => {
            if (args.Count < 3) {
                return DynValue.NewNil();
            }

            DynValue fd = args[1];
            int events = args[2].Type == DataType.Number ? (int)args[2].Number : 0;

            return state.EventMod(fd, events) ? DynValue.NewBoolean(true) : DynValue.NewNil();
        }, "bee.epoll.ctx.event_mod");

        ctx["event_del"] = DynValue.NewCallback((context, args) => {
            if (args.Count < 2) {
                return DynValue.NewNil();
            }

            return state.EventDel(args[1]) ? DynValue.NewBoolean(true) : DynValue.NewNil();
        }, "bee.epoll.ctx.event_del");

        return ctx;
    }

    private static Table CreateSubprocessHandleTable(Script script, BeeSubprocessHandle handle) {
        Table processTable = new Table(script);
        processTable["stdout"] = DynValue.NewNil();
        processTable["stderr"] = DynValue.NewNil();
        processTable["stdin"] = DynValue.NewNil();

        processTable["wait"] = DynValue.NewCallback((context, args) => {
            try {
                int exitCode = handle.Wait();
                return DynValue.NewNumber(exitCode);
            } catch (Exception ex) {
                return DynValue.NewTuple(DynValue.NewNil(), DynValue.NewString(ex.Message));
            }
        }, "bee.subprocess.process.wait");

        processTable["kill"] = DynValue.NewCallback((context, args) => {
            return DynValue.NewBoolean(handle.Kill());
        }, "bee.subprocess.process.kill");

        processTable["get_id"] = DynValue.NewCallback((context, args) => {
            return DynValue.NewNumber(handle.GetId());
        }, "bee.subprocess.process.get_id");

        processTable["is_running"] = DynValue.NewCallback((context, args) => {
            return DynValue.NewBoolean(handle.IsRunning());
        }, "bee.subprocess.process.is_running");

        processTable["resume"] = DynValue.NewCallback((context, args) => {
            return DynValue.NewBoolean(false);
        }, "bee.subprocess.process.resume");

        processTable["native_handle"] = DynValue.NewCallback((context, args) => {
            return DynValue.NewNumber(handle.GetNativeHandle());
        }, "bee.subprocess.process.native_handle");

        processTable["detach"] = DynValue.NewCallback((context, args) => {
            handle.Detach();
            return DynValue.NewBoolean(true);
        }, "bee.subprocess.process.detach");

        return processTable;
    }

    private static Table CreateFilewatchHandleTable(Script script) {
        Table watch = new Table(script);

        watch["add"] = DynValue.NewCallback((context, args) => DynValue.NewBoolean(true), "bee.filewatch.watch.add");
        watch["set_recursive"] = DynValue.NewCallback((context, args) => DynValue.NewBoolean(true), "bee.filewatch.watch.set_recursive");
        watch["set_follow_symlinks"] = DynValue.NewCallback((context, args) => DynValue.NewBoolean(true), "bee.filewatch.watch.set_follow_symlinks");
        watch["set_filter"] = DynValue.NewCallback((context, args) => DynValue.NewBoolean(true), "bee.filewatch.watch.set_filter");
        watch["select"] = DynValue.NewCallback((context, args) => DynValue.NewTuple(DynValue.NewNil(), DynValue.NewNil()), "bee.filewatch.watch.select");

        return watch;
    }

    private static void RunBeeThreadSource(LuaWorld parentWorld, string source, object?[] serializedArgs, int beeThreadId) {
        LuaWorld? workerWorld = null;
        try {
            CurrentBeeThreadId = beeThreadId;

            Script workerScript = new Script(CoreModules.Preset_Complete);
            workerWorld = new LuaWorld(workerScript, parentWorld.LuaScriptPath);
            workerWorld.BuiltinModuleResolver = TryLoadModule;
            LuaDebugCompatibility.Install(workerWorld);
            workerWorld.SetScriptArguments(Array.Empty<string>());

            DynValue chunk = workerScript.LoadString(source, codeFriendlyName: "[bee.thread]");

            object[] workerArgs = new object[serializedArgs.Length];
            for (int index = 0; index < serializedArgs.Length; index++) {
                workerArgs[index] = DeserializeDynValue(workerScript, serializedArgs[index]);
            }

            workerScript.Call(chunk, workerArgs);
        } catch (Exception ex) {
            ThreadErrors.Enqueue(ex.ToString());
        } finally {
            workerWorld?.DisposeOpenDisposables();
            CurrentBeeThreadId = 0;
        }
    }

    private static bool TryGetThreadHandle(DynValue value, out int handleId, out BeeThreadHandle? handle) {
        handleId = 0;
        handle = null;

        if (value.Type != DataType.Table) {
            return false;
        }

        DynValue id = value.Table.Get("_bee_thread_handle");
        if (id.Type != DataType.Number) {
            return false;
        }

        handleId = (int)id.Number;
        lock (ThreadHandlesLock) {
            return ThreadHandles.TryGetValue(handleId, out handle);
        }
    }

    private static bool TryResolveChannelFromBox(DynValue boxValue, out BeeChannel? channel) {
        channel = null;
        if (boxValue.Type != DataType.Table) {
            return false;
        }

        DynValue name = boxValue.Table.Get("_bee_channel_name");
        if (name.Type != DataType.String) {
            return false;
        }

        return TryGetChannelByName(name.String, out channel);
    }

    private static bool TryGetChannelByName(string name, out BeeChannel? channel) {
        lock (ChannelsLock) {
            return Channels.TryGetValue(name, out channel);
        }
    }

    private static string? GetFdKey(DynValue fdValue) {
        if (fdValue.Type != DataType.Table) {
            return null;
        }

        DynValue key = fdValue.Table.Get("_bee_fd_key");
        if (key.Type == DataType.String && !string.IsNullOrWhiteSpace(key.String)) {
            return key.String;
        }

        DynValue channelName = fdValue.Table.Get("_bee_channel_name");
        if (channelName.Type == DataType.String && !string.IsNullOrWhiteSpace(channelName.String)) {
            return "channel:" + channelName.String;
        }

        return null;
    }

    private static string? GetChannelNameFromFd(DynValue fdValue) {
        if (fdValue.Type != DataType.Table) {
            return null;
        }

        DynValue channelName = fdValue.Table.Get("_bee_channel_name");
        if (channelName.Type == DataType.String && !string.IsNullOrWhiteSpace(channelName.String)) {
            return channelName.String;
        }

        return null;
    }

    private static object? SerializeDynValue(DynValue value) {
        switch (value.Type) {
        case DataType.Nil:
        case DataType.Void:
            return NilSentinel;
        case DataType.Boolean:
            return value.Boolean;
        case DataType.Number:
            return value.Number;
        case DataType.String:
            return value.String;
        case DataType.Table:
            return SerializeTable(value.Table);
        default:
            LogCompatibilityWarning("serialize:" + value.Type);
            return value.ToPrintString();
        }
    }

    private static DynValue DeserializeDynValue(Script script, object? value) {
        if (value == null || ReferenceEquals(value, NilSentinel)) {
            return DynValue.NewNil();
        }

        if (value is bool booleanValue) {
            return DynValue.NewBoolean(booleanValue);
        }

        if (value is byte or sbyte or short or ushort or int or uint or long or ulong or float or double or decimal) {
            return DynValue.NewNumber(Convert.ToDouble(value));
        }

        if (value is string stringValue) {
            return DynValue.NewString(stringValue);
        }

        if (value is SerializedLuaTable serializedTable) {
            Table table = new Table(script);
            foreach (SerializedLuaTable.Entry entry in serializedTable.Entries) {
                DynValue key = DeserializeDynValue(script, entry.Key);
                if (key.Type == DataType.Nil || key.Type == DataType.Void) {
                    continue;
                }

                table.Set(key, DeserializeDynValue(script, entry.Value));
            }
            return DynValue.NewTable(table);
        }

        return DynValue.NewString(value.ToString() ?? string.Empty);
    }

    private static SerializedLuaTable SerializeTable(Table table) {
        List<SerializedLuaTable.Entry> entries = new List<SerializedLuaTable.Entry>();
        foreach (TablePair pair in table.Pairs) {
            object? key = SerializeDynValue(pair.Key);
            if (ReferenceEquals(key, NilSentinel)) {
                continue;
            }

            object? serializedValue = SerializeDynValue(pair.Value);
            entries.Add(new SerializedLuaTable.Entry(key, serializedValue));
        }

        return new SerializedLuaTable(entries);
    }

    private static string RequireStringArgument(CallbackArguments args, int index, string functionName) {
        if (index >= args.Count || args[index].Type != DataType.String || string.IsNullOrWhiteSpace(args[index].String)) {
            throw new ArgumentException($"{functionName} expects a string argument", functionName);
        }

        return args[index].String;
    }

    private static void LogCompatibilityWarning(string api) {
        lock (WarningLock) {
            if (!WarningOnce.Add(api)) {
                return;
            }
        }

        Console.WriteLine($"[moonsharpy] compatibility fallback active for {api}");
    }

    private static string GetPlatformOsLower() {
        if (OperatingSystem.IsWindows()) {
            return "windows";
        }
        if (OperatingSystem.IsMacOS()) {
            return "macos";
        }
        if (OperatingSystem.IsLinux()) {
            return "linux";
        }
        if (OperatingSystem.IsAndroid()) {
            return "android";
        }
        if (OperatingSystem.IsIOS()) {
            return "ios";
        }

        return "unknown";
    }

    private static string GetPlatformOsUpper(string osLower) {
        return osLower switch {
            "windows" => "Windows",
            "macos" => "macOS",
            "linux" => "Linux",
            "android" => "Android",
            "ios" => "iOS",
            _ => "unknown",
        };
    }

    private static string GetPlatformArch() {
        return RuntimeInformation.ProcessArchitecture switch {
            Architecture.X86 => "x86",
            Architecture.X64 => "x86_64",
            Architecture.Arm => "arm",
            Architecture.Arm64 => "arm64",
            _ => "unknown",
        };
    }

    private sealed class BeeThreadHandle {
        private readonly Thread Thread;
        private readonly ManualResetEventSlim Completed = new ManualResetEventSlim(false);

        internal BeeThreadHandle(ThreadStart start) {
            Thread = new Thread(() => {
                try {
                    start();
                } finally {
                    Completed.Set();
                }
            }) {
                IsBackground = true,
            };

            Thread.Start();
        }

        internal bool IsCompleted => Completed.IsSet;

        internal void Wait() {
            Thread.Join();
        }
    }

    private sealed class BeeChannel {
        private readonly object Sync = new object();
        private readonly Queue<object?[]> Queue = new Queue<object?[]>();
        private readonly ManualResetEvent HasDataSignal = new ManualResetEvent(false);

        internal WaitHandle WaitHandle => HasDataSignal;

        internal bool HasData {
            get {
                lock (Sync) {
                    return Queue.Count > 0;
                }
            }
        }

        internal void Push(object?[] values) {
            lock (Sync) {
                Queue.Enqueue(values);
                HasDataSignal.Set();
            }
        }

        internal bool TryPop(out object?[]? values) {
            lock (Sync) {
                if (Queue.Count == 0) {
                    values = null;
                    return false;
                }

                values = Queue.Dequeue();
                if (Queue.Count == 0) {
                    HasDataSignal.Reset();
                }
                return true;
            }
        }
    }

    private sealed class SelectContext {
        private readonly object Sync = new object();
        private readonly Dictionary<string, SelectRegistration> Registrations = new Dictionary<string, SelectRegistration>(StringComparer.Ordinal);

        internal bool EventAdd(DynValue fd, int events, DynValue payload) {
            string? key = GetFdKey(fd);
            if (string.IsNullOrWhiteSpace(key)) {
                return false;
            }

            SelectRegistration registration = new SelectRegistration(key, fd, payload, events, GetChannelNameFromFd(fd));
            lock (Sync) {
                if (Registrations.ContainsKey(key)) {
                    return false;
                }

                Registrations[key] = registration;
                return true;
            }
        }

        internal bool EventMod(DynValue fd, int events) {
            string? key = GetFdKey(fd);
            if (string.IsNullOrWhiteSpace(key)) {
                return false;
            }

            lock (Sync) {
                if (!Registrations.TryGetValue(key, out SelectRegistration? registration)) {
                    return false;
                }

                registration.Events = events;
                return true;
            }
        }

        internal bool EventDel(DynValue fd) {
            string? key = GetFdKey(fd);
            if (string.IsNullOrWhiteSpace(key)) {
                return false;
            }

            lock (Sync) {
                return Registrations.Remove(key);
            }
        }

        internal List<SelectReady> Wait(int timeout) {
            List<SelectReady> ready = CollectReady();
            if (ready.Count == 0 && timeout != 0) {
                WaitForEvents(timeout);
                ready = CollectReady();
            }
            return ready;
        }

        internal void Close() {
            lock (Sync) {
                Registrations.Clear();
            }
        }

        private List<SelectReady> CollectReady() {
            List<SelectRegistration> registrations;
            lock (Sync) {
                registrations = new List<SelectRegistration>(Registrations.Values);
            }

            List<SelectReady> ready = new List<SelectReady>();
            foreach (SelectRegistration registration in registrations) {
                int activeEvents = 0;

                if ((registration.Events & SelectRead) != 0
                    && registration.ChannelName != null
                    && TryGetChannelByName(registration.ChannelName, out BeeChannel? channel)
                    && channel.HasData) {
                    activeEvents |= SelectRead;
                }

                if (activeEvents == 0) {
                    continue;
                }

                DynValue payload = IsNilLike(registration.Payload) ? registration.Fd : registration.Payload;
                ready.Add(new SelectReady(payload, activeEvents));
            }

            return ready;
        }

        private void WaitForEvents(int timeout) {
            List<WaitHandle> handles = new List<WaitHandle>();
            List<SelectRegistration> registrations;
            lock (Sync) {
                registrations = new List<SelectRegistration>(Registrations.Values);
            }

            foreach (SelectRegistration registration in registrations) {
                if ((registration.Events & SelectRead) == 0 || registration.ChannelName == null) {
                    continue;
                }

                if (TryGetChannelByName(registration.ChannelName, out BeeChannel? channel)) {
                    handles.Add(channel.WaitHandle);
                }
            }

            if (handles.Count == 0) {
                if (timeout < 0) {
                    Thread.Sleep(10);
                } else if (timeout > 0) {
                    Thread.Sleep(timeout);
                }
                return;
            }

            int timeoutMs = timeout < 0 ? Timeout.Infinite : timeout;
            try {
                WaitHandle.WaitAny(handles.ToArray(), timeoutMs);
            } catch (NotSupportedException) {
                Thread.Sleep(timeout < 0 ? 10 : timeout);
            }
        }

        private sealed class SelectRegistration {
            internal SelectRegistration(string key, DynValue fd, DynValue payload, int events, string? channelName) {
                Key = key;
                Fd = fd;
                Payload = payload;
                Events = events;
                ChannelName = channelName;
            }

            internal string Key { get; }
            internal DynValue Fd { get; }
            internal DynValue Payload { get; }
            internal int Events { get; set; }
            internal string? ChannelName { get; }
        }
    }

    private sealed class EpollContext {
        private readonly object Sync = new object();
        private readonly Dictionary<string, EpollRegistration> Registrations = new Dictionary<string, EpollRegistration>(StringComparer.Ordinal);

        internal bool EventAdd(DynValue fd, int events, DynValue payload) {
            string? key = GetFdKey(fd);
            if (string.IsNullOrWhiteSpace(key)) {
                return false;
            }

            EpollRegistration registration = new EpollRegistration(key, fd, payload, events, GetChannelNameFromFd(fd));
            lock (Sync) {
                if (Registrations.ContainsKey(key)) {
                    return false;
                }

                Registrations[key] = registration;
                return true;
            }
        }

        internal bool EventMod(DynValue fd, int events) {
            string? key = GetFdKey(fd);
            if (string.IsNullOrWhiteSpace(key)) {
                return false;
            }

            lock (Sync) {
                if (!Registrations.TryGetValue(key, out EpollRegistration? registration)) {
                    return false;
                }

                registration.Events = events;
                return true;
            }
        }

        internal bool EventDel(DynValue fd) {
            string? key = GetFdKey(fd);
            if (string.IsNullOrWhiteSpace(key)) {
                return false;
            }

            lock (Sync) {
                return Registrations.Remove(key);
            }
        }

        internal List<EpollReady> Wait(int timeout) {
            List<EpollReady> ready = CollectReady();
            if (ready.Count == 0 && timeout != 0) {
                WaitForEvents(timeout);
                ready = CollectReady();
            }
            return ready;
        }

        internal void Close() {
            lock (Sync) {
                Registrations.Clear();
            }
        }

        private List<EpollReady> CollectReady() {
            List<EpollRegistration> registrations;
            lock (Sync) {
                registrations = new List<EpollRegistration>(Registrations.Values);
            }

            List<EpollReady> ready = new List<EpollReady>();
            foreach (EpollRegistration registration in registrations) {
                int activeEvents = 0;
                int readMask = EpollIn | EpollPri | EpollRdNorm;

                if ((registration.Events & readMask) != 0
                    && registration.ChannelName != null
                    && TryGetChannelByName(registration.ChannelName, out BeeChannel? channel)
                    && channel.HasData) {
                    activeEvents |= registration.Events & readMask;
                }

                if (activeEvents == 0) {
                    continue;
                }

                DynValue payload = IsNilLike(registration.Payload) ? registration.Fd : registration.Payload;
                ready.Add(new EpollReady(payload, activeEvents));
            }

            return ready;
        }

        private void WaitForEvents(int timeout) {
            List<WaitHandle> handles = new List<WaitHandle>();
            List<EpollRegistration> registrations;
            lock (Sync) {
                registrations = new List<EpollRegistration>(Registrations.Values);
            }

            foreach (EpollRegistration registration in registrations) {
                int readMask = EpollIn | EpollPri | EpollRdNorm;
                if ((registration.Events & readMask) == 0 || registration.ChannelName == null) {
                    continue;
                }

                if (TryGetChannelByName(registration.ChannelName, out BeeChannel? channel)) {
                    handles.Add(channel.WaitHandle);
                }
            }

            if (handles.Count == 0) {
                if (timeout < 0) {
                    Thread.Sleep(10);
                } else if (timeout > 0) {
                    Thread.Sleep(timeout);
                }
                return;
            }

            int timeoutMs = timeout < 0 ? Timeout.Infinite : timeout;
            try {
                WaitHandle.WaitAny(handles.ToArray(), timeoutMs);
            } catch (NotSupportedException) {
                Thread.Sleep(timeout < 0 ? 10 : timeout);
            }
        }

        private sealed class EpollRegistration {
            internal EpollRegistration(string key, DynValue fd, DynValue payload, int events, string? channelName) {
                Key = key;
                Fd = fd;
                Payload = payload;
                Events = events;
                ChannelName = channelName;
            }

            internal string Key { get; }
            internal DynValue Fd { get; }
            internal DynValue Payload { get; }
            internal int Events { get; set; }
            internal string? ChannelName { get; }
        }
    }

    private sealed class BeeSubprocessHandle : IDisposable {
        private readonly LuaWorld World;
        private readonly Process Process;
        private bool Detached;
        private bool Disposed;

        internal BeeSubprocessHandle(LuaWorld world, Process process) {
            World = world;
            Process = process;
            World.RegisterDisposable(this);
        }

        internal int Wait() {
            Process.WaitForExit();
            return Process.ExitCode;
        }

        internal bool Kill() {
            try {
                if (Process.HasExited) {
                    return true;
                }

                Process.Kill(entireProcessTree: true);
                return true;
            } catch {
                return false;
            }
        }

        internal int GetId() {
            try {
                return Process.Id;
            } catch {
                return -1;
            }
        }

        internal bool IsRunning() {
            try {
                return !Process.HasExited;
            } catch {
                return false;
            }
        }

        internal long GetNativeHandle() {
            try {
                return Process.Handle.ToInt64();
            } catch {
                return 0;
            }
        }

        internal void Detach() {
            Detached = true;
            World.UnregisterDisposable(this);
        }

        public void Dispose() {
            if (Disposed) {
                return;
            }

            Disposed = true;
            try {
                if (!Detached) {
                    Process.Dispose();
                }
            } finally {
                World.UnregisterDisposable(this);
            }
        }
    }

    private readonly struct SelectReady {
        internal SelectReady(DynValue payload, int events) {
            Payload = payload;
            Events = events;
        }

        internal DynValue Payload { get; }
        internal int Events { get; }
    }

    private readonly struct EpollReady {
        internal EpollReady(DynValue payload, int events) {
            Payload = payload;
            Events = events;
        }

        internal DynValue Payload { get; }
        internal int Events { get; }
    }

    private sealed class SerializedLuaTable {
        internal SerializedLuaTable(List<Entry> entries) {
            Entries = entries;
        }

        internal List<Entry> Entries { get; }

        internal readonly struct Entry {
            internal Entry(object? key, object? value) {
                Key = key;
                Value = value;
            }

            internal object? Key { get; }
            internal object? Value { get; }
        }
    }
}

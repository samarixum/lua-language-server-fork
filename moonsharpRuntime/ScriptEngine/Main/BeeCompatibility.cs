using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using MoonSharp.Interpreter;

namespace Moonsharpy.ScriptEngines.Lua;

internal delegate bool BuiltinModuleResolver(LuaWorld world, string moduleName, out DynValue moduleValue);

internal static class BeeCompatibility {
    private const int CopyOptionNone = 0;
    private const int CopyOptionSkipExisting = 1;
    private const int CopyOptionOverwriteExisting = 2;
    private const int CopyOptionUpdateExisting = 4;
    private const int CopyOptionRecursive = 8;
    private const int CopyOptionDirectoriesOnly = 64;

    private const int PermOptionReplace = 0;
    private const int PermOptionAdd = 1;
    private const int PermOptionRemove = 2;

    private const int UserWriteMask = 0x80;

    static BeeCompatibility() {
        UserData.RegisterType<FileLockHandle>();
    }

    internal static bool TryLoadModule(LuaWorld world, string moduleName, out DynValue moduleValue) {
        switch (moduleName) {
        case "bee.filesystem":
            moduleValue = DynValue.NewTable(CreateFilesystemModule(world));
            return true;
        case "bee.sys":
            moduleValue = DynValue.NewTable(CreateSysModule(world));
            return true;
        case "bee.time":
            moduleValue = DynValue.NewTable(CreateTimeModule(world));
            return true;
        case "bee.lua":
        case "bee": {
            var root = CreateFilesystemModule(world);
            root["filesystem"] = root;
            root["sys"] = CreateSysModule(world);
            root["time"] = CreateTimeModule(world);
            moduleValue = DynValue.NewTable(root);
            return true;
        }
        default:
            moduleValue = DynValue.NewNil();
            return false;
        }
    }

    private static Table CreateFilesystemModule(LuaWorld world) {
        Script script = world.LuaScript;
        Table module = new Table(script);

        Table pathMeta = BuildPathMeta(script);
        Table statusMeta = BuildStatusMeta(script);
        Table entryMeta = BuildEntryMeta(script, pathMeta, statusMeta);

        DynValue NewPathValue(string value) {
            return DynValue.NewTable(CreatePathTable(script, pathMeta, value));
        }

        DynValue NewStatusValue(string value, bool followSymlink) {
            return DynValue.NewTable(CreateStatusTable(script, statusMeta, value, followSymlink));
        }

        DynValue NewEntryValue(string value) {
            return DynValue.NewTable(CreateDirectoryEntryTable(script, entryMeta, value));
        }

        module["path"] = DynValue.NewCallback((context, args) => {
            if (args.Count < 1 || IsNilLike(args[0])) {
                return NewPathValue(string.Empty);
            }

            return NewPathValue(GetPathValue(args[0]));
        }, "bee.filesystem.path");

        module["status"] = DynValue.NewCallback((context, args) => {
            string path = RequirePathArgument(args, 0, "status");
            return NewStatusValue(path, followSymlink: true);
        }, "bee.filesystem.status");

        module["symlink_status"] = DynValue.NewCallback((context, args) => {
            string path = RequirePathArgument(args, 0, "symlink_status");
            return NewStatusValue(path, followSymlink: false);
        }, "bee.filesystem.symlink_status");

        module["exists"] = DynValue.NewCallback((context, args) => DynValue.NewBoolean(ResolveStatus(RequirePathArgument(args, 0, "exists"), true).Exists), "bee.filesystem.exists");
        module["is_directory"] = DynValue.NewCallback((context, args) => DynValue.NewBoolean(ResolveStatus(RequirePathArgument(args, 0, "is_directory"), true).IsDirectory), "bee.filesystem.is_directory");
        module["is_regular_file"] = DynValue.NewCallback((context, args) => DynValue.NewBoolean(ResolveStatus(RequirePathArgument(args, 0, "is_regular_file"), true).IsRegularFile), "bee.filesystem.is_regular_file");

        module["create_directory"] = DynValue.NewCallback((context, args) => DynValue.NewBoolean(CreateDirectory(RequirePathArgument(args, 0, "create_directory"), recursive: false)), "bee.filesystem.create_directory");
        module["create_directories"] = DynValue.NewCallback((context, args) => DynValue.NewBoolean(CreateDirectory(RequirePathArgument(args, 0, "create_directories"), recursive: true)), "bee.filesystem.create_directories");

        module["rename"] = DynValue.NewCallback((context, args) => {
            Rename(RequirePathArgument(args, 0, "rename"), RequirePathArgument(args, 1, "rename"));
            return DynValue.NewNil();
        }, "bee.filesystem.rename");

        module["remove"] = DynValue.NewCallback((context, args) => DynValue.NewBoolean(Remove(RequirePathArgument(args, 0, "remove"))), "bee.filesystem.remove");
        module["remove_all"] = DynValue.NewCallback((context, args) => DynValue.NewNumber(RemoveAll(RequirePathArgument(args, 0, "remove_all"))), "bee.filesystem.remove_all");

        module["current_path"] = DynValue.NewCallback((context, args) => {
            if (args.Count < 1 || IsNilLike(args[0])) {
                return NewPathValue(ToGenericPath(Directory.GetCurrentDirectory()));
            }

            Directory.SetCurrentDirectory(ToNativePath(RequirePathArgument(args, 0, "current_path")));
            return DynValue.NewNil();
        }, "bee.filesystem.current_path");

        module["copy"] = DynValue.NewCallback((context, args) => {
            string from = RequirePathArgument(args, 0, "copy");
            string to = RequirePathArgument(args, 1, "copy");
            int options = ReadOption(args, 2, CopyOptionNone);
            Copy(from, to, options);
            return DynValue.NewNil();
        }, "bee.filesystem.copy");

        module["copy_file"] = DynValue.NewCallback((context, args) => {
            string from = RequirePathArgument(args, 0, "copy_file");
            string to = RequirePathArgument(args, 1, "copy_file");
            int options = ReadOption(args, 2, CopyOptionNone);
            return DynValue.NewBoolean(CopyFile(from, to, options));
        }, "bee.filesystem.copy_file");

        module["absolute"] = DynValue.NewCallback((context, args) => NewPathValue(GetAbsolutePath(RequirePathArgument(args, 0, "absolute"))), "bee.filesystem.absolute");
        module["canonical"] = DynValue.NewCallback((context, args) => NewPathValue(GetAbsolutePath(RequirePathArgument(args, 0, "canonical"))), "bee.filesystem.canonical");

        module["relative"] = DynValue.NewCallback((context, args) => {
            string path = RequirePathArgument(args, 0, "relative");
            string basePath = args.Count > 1 && !IsNilLike(args[1]) ? GetPathValue(args[1]) : ToGenericPath(Directory.GetCurrentDirectory());
            return NewPathValue(GetRelativePath(path, basePath));
        }, "bee.filesystem.relative");

        module["last_write_time"] = DynValue.NewCallback((context, args) => {
            string path = RequirePathArgument(args, 0, "last_write_time");
            if (args.Count < 2 || IsNilLike(args[1])) {
                return DynValue.NewNumber(GetLastWriteTime(path));
            }

            SetLastWriteTime(path, (long)args[1].Number);
            return DynValue.NewNil();
        }, "bee.filesystem.last_write_time");

        module["permissions"] = DynValue.NewCallback((context, args) => {
            string path = RequirePathArgument(args, 0, "permissions");
            if (args.Count < 2 || IsNilLike(args[1])) {
                return DynValue.NewNumber(GetPermissions(path));
            }

            int perms = (int)args[1].Number;
            int option = ReadOption(args, 2, PermOptionReplace);
            SetPermissions(path, perms, option);
            return DynValue.NewNil();
        }, "bee.filesystem.permissions");

        module["create_symlink"] = DynValue.NewCallback((context, args) => {
            CreateSymlink(RequirePathArgument(args, 0, "create_symlink"), RequirePathArgument(args, 1, "create_symlink"));
            return DynValue.NewNil();
        }, "bee.filesystem.create_symlink");

        module["create_directory_symlink"] = DynValue.NewCallback((context, args) => {
            CreateDirectorySymlink(RequirePathArgument(args, 0, "create_directory_symlink"), RequirePathArgument(args, 1, "create_directory_symlink"));
            return DynValue.NewNil();
        }, "bee.filesystem.create_directory_symlink");

        module["create_hard_link"] = DynValue.NewCallback((context, args) => {
            CreateHardLink(RequirePathArgument(args, 0, "create_hard_link"), RequirePathArgument(args, 1, "create_hard_link"));
            return DynValue.NewNil();
        }, "bee.filesystem.create_hard_link");

        module["temp_directory_path"] = DynValue.NewCallback((context, args) => NewPathValue(Path.GetTempPath()), "bee.filesystem.temp_directory_path");

        module["pairs"] = DynValue.NewCallback((context, args) => CreatePairsIterator(script, entryMeta, pathMeta, statusMeta, RequirePathArgument(args, 0, "pairs"), recursive: false), "bee.filesystem.pairs");
        module["pairs_r"] = DynValue.NewCallback((context, args) => CreatePairsIterator(script, entryMeta, pathMeta, statusMeta, RequirePathArgument(args, 0, "pairs_r"), recursive: true), "bee.filesystem.pairs_r");

        module["exe_path"] = DynValue.NewCallback((context, args) => NewPathValue(GetExePath()), "bee.filesystem.exe_path");
        module["dll_path"] = DynValue.NewCallback((context, args) => NewPathValue(GetDllPath()), "bee.filesystem.dll_path");
        module["fullpath"] = DynValue.NewCallback((context, args) => NewPathValue(GetAbsolutePath(RequirePathArgument(args, 0, "fullpath"))), "bee.filesystem.fullpath");

        module["filelock"] = DynValue.NewCallback((context, args) => {
            string path = RequirePathArgument(args, 0, "filelock");
            FileLockHandle? handle = FileLockHandle.TryCreate(world, path);
            if (handle == null) {
                return DynValue.NewNil();
            }

            return DynValue.FromObject(script, handle);
        }, "bee.filesystem.filelock");

        module["copy_options"] = CreateCopyOptionsTable(script);
        module["perm_options"] = CreatePermOptionsTable(script);
        module["directory_options"] = CreateDirectoryOptionsTable(script);

        return module;
    }

    private static Table CreateSysModule(LuaWorld world) {
        Script script = world.LuaScript;
        Table module = new Table(script);

        module["exe_path"] = DynValue.NewCallback((context, args) => DynValue.NewTable(CreatePathTable(script, BuildPathMeta(script), GetExePath())), "bee.sys.exe_path");
        module["dll_path"] = DynValue.NewCallback((context, args) => DynValue.NewTable(CreatePathTable(script, BuildPathMeta(script), GetDllPath())), "bee.sys.dll_path");
        module["fullpath"] = DynValue.NewCallback((context, args) => DynValue.NewTable(CreatePathTable(script, BuildPathMeta(script), GetAbsolutePath(RequirePathArgument(args, 0, "fullpath")))), "bee.sys.fullpath");

        module["filelock"] = DynValue.NewCallback((context, args) => {
            string path = RequirePathArgument(args, 0, "filelock");
            FileLockHandle? handle = FileLockHandle.TryCreate(world, path);
            if (handle == null) {
                return DynValue.NewNil();
            }

            return DynValue.FromObject(script, handle);
        }, "bee.sys.filelock");

        return module;
    }

    private static Table CreateTimeModule(LuaWorld world) {
        Script script = world.LuaScript;
        Table module = new Table(script);

        module["time"] = DynValue.NewCallback((context, args) => DynValue.NewNumber(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()), "bee.time.time");
        module["monotonic"] = DynValue.NewCallback((context, args) => DynValue.NewNumber(GetMonotonicMilliseconds()), "bee.time.monotonic");
        module["thread"] = DynValue.NewCallback((context, args) => DynValue.NewNumber(GetThreadCpuMilliseconds()), "bee.time.thread");

        return module;
    }

    private static Table BuildPathMeta(Script script) {
        Table methods = new Table(script);

        methods["string"] = DynValue.NewCallback((context, args) => DynValue.NewString(GetPathValue(args[0])), "path.string");
        methods["filename"] = DynValue.NewCallback((context, args) => DynValue.NewTable(CreatePathTable(script, BuildPathMeta(script), GetFilename(GetPathValue(args[0])))), "path.filename");
        methods["parent_path"] = DynValue.NewCallback((context, args) => DynValue.NewTable(CreatePathTable(script, BuildPathMeta(script), GetParentPath(GetPathValue(args[0])))), "path.parent_path");
        methods["stem"] = DynValue.NewCallback((context, args) => DynValue.NewTable(CreatePathTable(script, BuildPathMeta(script), GetStem(GetPathValue(args[0])))), "path.stem");
        methods["extension"] = DynValue.NewCallback((context, args) => DynValue.NewString(GetExtension(GetPathValue(args[0]))), "path.extension");
        methods["is_absolute"] = DynValue.NewCallback((context, args) => DynValue.NewBoolean(IsAbsolutePath(GetPathValue(args[0]))), "path.is_absolute");
        methods["is_relative"] = DynValue.NewCallback((context, args) => DynValue.NewBoolean(!IsAbsolutePath(GetPathValue(args[0]))), "path.is_relative");

        methods["remove_filename"] = DynValue.NewCallback((context, args) => {
            Table self = args[0].Table;
            self["_value"] = DynValue.NewString(RemoveFilename(GetPathValue(args[0])));
            return DynValue.NewTable(self);
        }, "path.remove_filename");

        methods["replace_filename"] = DynValue.NewCallback((context, args) => {
            Table self = args[0].Table;
            self["_value"] = DynValue.NewString(ReplaceFilename(GetPathValue(args[0]), GetPathValue(args[1])));
            return DynValue.NewTable(self);
        }, "path.replace_filename");

        methods["replace_extension"] = DynValue.NewCallback((context, args) => {
            Table self = args[0].Table;
            self["_value"] = DynValue.NewString(ReplaceExtension(GetPathValue(args[0]), GetPathValue(args[1])));
            return DynValue.NewTable(self);
        }, "path.replace_extension");

        methods["equal_extension"] = DynValue.NewCallback((context, args) => {
            return DynValue.NewBoolean(PathStringComparer.Equals(GetExtension(GetPathValue(args[0])), GetExtension(GetPathValue(args[1]))));
        }, "path.equal_extension");

        methods["lexically_normal"] = DynValue.NewCallback((context, args) => {
            return DynValue.NewTable(CreatePathTable(script, BuildPathMeta(script), LexicallyNormalizePath(GetPathValue(args[0]))));
        }, "path.lexically_normal");

        Table meta = new Table(script);
        meta["__index"] = methods;
        meta["__div"] = DynValue.NewCallback((context, args) => DynValue.NewTable(CreatePathTable(script, BuildPathMeta(script), JoinPath(GetPathValue(args[0]), GetPathValue(args[1])))), "path.__div");
        meta["__concat"] = DynValue.NewCallback((context, args) => DynValue.NewTable(CreatePathTable(script, BuildPathMeta(script), GetPathValue(args[0]) + GetPathValue(args[1]))), "path.__concat");
        meta["__eq"] = DynValue.NewCallback((context, args) => DynValue.NewBoolean(PathEquals(GetPathValue(args[0]), GetPathValue(args[1]))), "path.__eq");
        meta["__tostring"] = DynValue.NewCallback((context, args) => DynValue.NewString(GetPathValue(args[0])), "path.__tostring");
        meta["__debugger_tostring"] = meta["__tostring"];

        return meta;
    }

    private static Table BuildStatusMeta(Script script) {
        Table methods = new Table(script);

        methods["type"] = DynValue.NewCallback((context, args) => DynValue.NewString(GetStatusFieldString(args[0].Table, "_status_type")), "status.type");
        methods["exists"] = DynValue.NewCallback((context, args) => DynValue.NewBoolean(GetStatusFieldBoolean(args[0].Table, "_status_exists")), "status.exists");
        methods["is_directory"] = DynValue.NewCallback((context, args) => DynValue.NewBoolean(GetStatusFieldBoolean(args[0].Table, "_status_is_directory")), "status.is_directory");
        methods["is_regular_file"] = DynValue.NewCallback((context, args) => DynValue.NewBoolean(GetStatusFieldBoolean(args[0].Table, "_status_is_regular_file")), "status.is_regular_file");

        Table meta = new Table(script);
        meta["__index"] = methods;
        meta["__eq"] = DynValue.NewCallback((context, args) => {
            string aType = GetStatusFieldString(args[0].Table, "_status_type");
            string bType = GetStatusFieldString(args[1].Table, "_status_type");
            int aPerm = GetStatusFieldInt(args[0].Table, "_status_permissions");
            int bPerm = GetStatusFieldInt(args[1].Table, "_status_permissions");
            return DynValue.NewBoolean(PathStringComparer.Equals(aType, bType) && aPerm == bPerm);
        }, "status.__eq");
        meta["__tostring"] = DynValue.NewCallback((context, args) => DynValue.NewString(GetStatusFieldString(args[0].Table, "_status_type")), "status.__tostring");
        meta["__debugger_tostring"] = meta["__tostring"];

        return meta;
    }

    private static Table BuildEntryMeta(Script script, Table pathMeta, Table statusMeta) {
        Table methods = new Table(script);

        methods["path"] = DynValue.NewCallback((context, args) => {
            string path = GetStatusFieldString(args[0].Table, "_path");
            return DynValue.NewTable(CreatePathTable(script, pathMeta, path));
        }, "entry.path");

        methods["status"] = DynValue.NewCallback((context, args) => {
            string path = GetStatusFieldString(args[0].Table, "_path");
            return DynValue.NewTable(CreateStatusTable(script, statusMeta, path, followSymlink: true));
        }, "entry.status");

        methods["symlink_status"] = DynValue.NewCallback((context, args) => {
            string path = GetStatusFieldString(args[0].Table, "_path");
            return DynValue.NewTable(CreateStatusTable(script, statusMeta, path, followSymlink: false));
        }, "entry.symlink_status");

        methods["type"] = DynValue.NewCallback((context, args) => {
            string path = GetStatusFieldString(args[0].Table, "_path");
            return DynValue.NewString(ResolveStatus(path, true).Type);
        }, "entry.type");

        methods["exists"] = DynValue.NewCallback((context, args) => {
            string path = GetStatusFieldString(args[0].Table, "_path");
            return DynValue.NewBoolean(ResolveStatus(path, true).Exists);
        }, "entry.exists");

        methods["is_directory"] = DynValue.NewCallback((context, args) => {
            string path = GetStatusFieldString(args[0].Table, "_path");
            return DynValue.NewBoolean(ResolveStatus(path, true).IsDirectory);
        }, "entry.is_directory");

        methods["is_regular_file"] = DynValue.NewCallback((context, args) => {
            string path = GetStatusFieldString(args[0].Table, "_path");
            return DynValue.NewBoolean(ResolveStatus(path, true).IsRegularFile);
        }, "entry.is_regular_file");

        methods["refresh"] = DynValue.NewCallback((context, args) => {
            string path = GetStatusFieldString(args[0].Table, "_path");
            PopulateStatusFields(args[0].Table, ResolveStatus(path, true));
            return DynValue.NewNil();
        }, "entry.refresh");

        Table meta = new Table(script);
        meta["__index"] = methods;
        meta["__tostring"] = DynValue.NewCallback((context, args) => DynValue.NewString(GetStatusFieldString(args[0].Table, "_path")), "entry.__tostring");
        meta["__debugger_tostring"] = meta["__tostring"];

        return meta;
    }

    private static Table CreatePathTable(Script script, Table pathMeta, string value) {
        Table table = new Table(script);
        table["_value"] = DynValue.NewString(NormalizeGenericPath(value));
        table.MetaTable = pathMeta;
        return table;
    }

    private static Table CreateStatusTable(Script script, Table statusMeta, string value, bool followSymlink) {
        Table table = new Table(script);
        table["_path"] = DynValue.NewString(NormalizeGenericPath(value));
        PopulateStatusFields(table, ResolveStatus(value, followSymlink));
        table.MetaTable = statusMeta;
        return table;
    }

    private static Table CreateDirectoryEntryTable(Script script, Table entryMeta, string value) {
        Table table = new Table(script);
        table["_path"] = DynValue.NewString(NormalizeGenericPath(value));
        table.MetaTable = entryMeta;
        return table;
    }

    private static DynValue CreatePairsIterator(Script script, Table entryMeta, Table pathMeta, Table statusMeta, string directoryPath, bool recursive) {
        string nativePath = ToNativePath(directoryPath);
        if (!Directory.Exists(nativePath)) {
            throw new DirectoryNotFoundException($"Directory not found: {directoryPath}");
        }

        List<string> entries = new List<string>();
        CollectEntries(nativePath, recursive, entries);

        int index = -1;
        DynValue iterator = DynValue.NewCallback((context, args) => {
            index++;
            if (index >= entries.Count) {
                return DynValue.NewNil();
            }

            string entryPath = ToGenericPath(entries[index]);
            DynValue pathValue = DynValue.NewTable(CreatePathTable(script, pathMeta, entryPath));
            DynValue entryValue = DynValue.NewTable(CreateDirectoryEntryTable(script, entryMeta, entryPath));
            return DynValue.NewTuple(pathValue, entryValue);
        }, recursive ? "bee.filesystem.pairs_r_iter" : "bee.filesystem.pairs_iter");

        return DynValue.NewTuple(iterator, DynValue.NewNil(), DynValue.NewNil());

        static void CollectEntries(string root, bool recurse, List<string> output) {
            foreach (string child in Directory.EnumerateFileSystemEntries(root)) {
                output.Add(child);
                if (recurse && Directory.Exists(child) && !IsReparsePoint(child)) {
                    CollectEntries(child, recurse, output);
                }
            }
        }
    }

    private static Table CreateCopyOptionsTable(Script script) {
        Table table = new Table(script);
        table["none"] = DynValue.NewNumber(CopyOptionNone);
        table["skip_existing"] = DynValue.NewNumber(CopyOptionSkipExisting);
        table["overwrite_existing"] = DynValue.NewNumber(CopyOptionOverwriteExisting);
        table["update_existing"] = DynValue.NewNumber(CopyOptionUpdateExisting);
        table["recursive"] = DynValue.NewNumber(CopyOptionRecursive);
        table["copy_symlinks"] = DynValue.NewNumber(16);
        table["skip_symlinks"] = DynValue.NewNumber(32);
        table["directories_only"] = DynValue.NewNumber(CopyOptionDirectoriesOnly);
        table["create_symlinks"] = DynValue.NewNumber(128);
        table["create_hard_links"] = DynValue.NewNumber(256);
        table["__in_recursive_copy"] = DynValue.NewNumber(512);
        return table;
    }

    private static Table CreatePermOptionsTable(Script script) {
        Table table = new Table(script);
        table["replace"] = DynValue.NewNumber(PermOptionReplace);
        table["add"] = DynValue.NewNumber(PermOptionAdd);
        table["remove"] = DynValue.NewNumber(PermOptionRemove);
        table["nofollow"] = DynValue.NewNumber(4);
        return table;
    }

    private static Table CreateDirectoryOptionsTable(Script script) {
        Table table = new Table(script);
        table["none"] = DynValue.NewNumber(0);
        table["follow_directory_symlink"] = DynValue.NewNumber(1);
        table["skip_permission_denied"] = DynValue.NewNumber(2);
        return table;
    }

    private static string RequirePathArgument(CallbackArguments args, int index, string functionName) {
        if (index >= args.Count || IsNilLike(args[index])) {
            throw new ArgumentException($"{functionName} expects a path argument", functionName);
        }

        return GetPathValue(args[index]);
    }

    private static bool IsNilLike(DynValue value) {
        return value.Type == DataType.Nil || value.Type == DataType.Void;
    }

    private static int ReadOption(CallbackArguments args, int index, int defaultValue) {
        if (index >= args.Count || IsNilLike(args[index]) || args[index].Type != DataType.Number) {
            return defaultValue;
        }

        return (int)args[index].Number;
    }

    private static string GetPathValue(DynValue value) {
        if (value.Type == DataType.String) {
            return NormalizeGenericPath(value.String);
        }

        if (value.Type == DataType.Table) {
            DynValue raw = value.Table.Get("_value");
            if (raw.Type == DataType.String) {
                return NormalizeGenericPath(raw.String);
            }
        }

        throw new ArgumentException("Expected path value.");
    }

    private static void PopulateStatusFields(Table table, StatusSnapshot status) {
        table["_status_type"] = DynValue.NewString(status.Type);
        table["_status_exists"] = DynValue.NewBoolean(status.Exists);
        table["_status_is_directory"] = DynValue.NewBoolean(status.IsDirectory);
        table["_status_is_regular_file"] = DynValue.NewBoolean(status.IsRegularFile);
        table["_status_permissions"] = DynValue.NewNumber(status.Permissions);
    }

    private static string GetStatusFieldString(Table table, string field) {
        DynValue value = table.Get(field);
        return value.Type == DataType.String ? value.String : string.Empty;
    }

    private static bool GetStatusFieldBoolean(Table table, string field) {
        DynValue value = table.Get(field);
        return value.Type == DataType.Boolean && value.Boolean;
    }

    private static int GetStatusFieldInt(Table table, string field) {
        DynValue value = table.Get(field);
        return value.Type == DataType.Number ? (int)value.Number : 0;
    }

    private static bool CreateDirectory(string pathValue, bool recursive) {
        string path = ToNativePath(pathValue);
        if (File.Exists(path)) {
            throw new IOException($"Path exists as file: {pathValue}");
        }

        if (!recursive) {
            string? parent = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(parent) && !Directory.Exists(parent)) {
                throw new DirectoryNotFoundException($"Parent directory does not exist: {pathValue}");
            }

            if (Directory.Exists(path)) {
                return false;
            }

            Directory.CreateDirectory(path);
            return true;
        }

        if (Directory.Exists(path)) {
            return false;
        }

        Directory.CreateDirectory(path);
        return true;
    }

    private static void Rename(string fromValue, string toValue) {
        string from = ToNativePath(fromValue);
        string to = ToNativePath(toValue);

        if (!File.Exists(from) && !Directory.Exists(from)) {
            throw new FileNotFoundException($"Source not found: {fromValue}");
        }

        if (File.Exists(to) || Directory.Exists(to)) {
            throw new IOException($"Destination already exists: {toValue}");
        }

        if (File.Exists(from)) {
            File.Move(from, to);
            return;
        }

        Directory.Move(from, to);
    }

    private static bool Remove(string pathValue) {
        string path = ToNativePath(pathValue);

        if (File.Exists(path)) {
            File.Delete(path);
            return true;
        }

        if (!Directory.Exists(path)) {
            return false;
        }

        if (IsReparsePoint(path)) {
            Directory.Delete(path);
            return true;
        }

        using (var enumerator = Directory.EnumerateFileSystemEntries(path).GetEnumerator()) {
            if (enumerator.MoveNext()) {
                throw new IOException($"Directory is not empty: {pathValue}");
            }
        }

        Directory.Delete(path);
        return true;
    }

    private static long RemoveAll(string pathValue) {
        string path = ToNativePath(pathValue);
        if (File.Exists(path)) {
            File.Delete(path);
            return 1;
        }

        if (!Directory.Exists(path)) {
            return 0;
        }

        if (IsReparsePoint(path)) {
            Directory.Delete(path);
            return 1;
        }

        return RemoveAllDirectory(path);
    }

    private static long RemoveAllDirectory(string path) {
        long count = 1;
        foreach (string entry in Directory.EnumerateFileSystemEntries(path)) {
            if (Directory.Exists(entry) && !IsReparsePoint(entry)) {
                count += RemoveAllDirectory(entry);
                continue;
            }

            if (Directory.Exists(entry)) {
                Directory.Delete(entry);
            } else {
                File.Delete(entry);
            }
            count++;
        }

        Directory.Delete(path);
        return count;
    }

    private static void Copy(string fromValue, string toValue, int options) {
        string from = ToNativePath(fromValue);
        string to = ToNativePath(toValue);

        if (File.Exists(from)) {
            CopyFile(fromValue, toValue, options);
            return;
        }

        if (!Directory.Exists(from)) {
            throw new FileNotFoundException($"Source not found: {fromValue}");
        }

        if ((options & CopyOptionRecursive) == 0) {
            throw new IOException("Directory copy requires recursive option.");
        }

        if (!Directory.Exists(to)) {
            Directory.CreateDirectory(to);
        }

        foreach (string entry in Directory.EnumerateFileSystemEntries(from)) {
            string name = Path.GetFileName(entry);
            string destination = Path.Combine(to, name);

            if (Directory.Exists(entry) && !IsReparsePoint(entry)) {
                Directory.CreateDirectory(destination);
                if ((options & CopyOptionDirectoriesOnly) == 0) {
                    Copy(ToGenericPath(entry), ToGenericPath(destination), options | CopyOptionRecursive);
                }
                continue;
            }

            if ((options & CopyOptionDirectoriesOnly) == 0) {
                CopyFile(ToGenericPath(entry), ToGenericPath(destination), options);
            }
        }
    }

    private static bool CopyFile(string fromValue, string toValue, int options) {
        string from = ToNativePath(fromValue);
        string to = ToNativePath(toValue);

        if (!File.Exists(from)) {
            throw new FileNotFoundException($"Source not found: {fromValue}");
        }

        if (File.Exists(to)) {
            if ((options & CopyOptionSkipExisting) != 0) {
                return false;
            }

            if ((options & CopyOptionUpdateExisting) != 0 && File.GetLastWriteTimeUtc(from) <= File.GetLastWriteTimeUtc(to)) {
                return false;
            }

            bool allowOverwrite = (options & CopyOptionOverwriteExisting) != 0 || (options & CopyOptionUpdateExisting) != 0;
            if (!allowOverwrite) {
                throw new IOException($"Destination already exists: {toValue}");
            }
        }

        File.Copy(from, to, overwrite: true);
        return true;
    }

    private static string GetAbsolutePath(string pathValue) {
        return ToGenericPath(Path.GetFullPath(ToNativePath(pathValue)));
    }

    private static string GetRelativePath(string pathValue, string baseValue) {
        string pathAbs = GetAbsolutePath(pathValue);
        string baseAbs = GetAbsolutePath(baseValue);

        if (!CanComputeRelative(pathAbs, baseAbs)) {
            return string.Empty;
        }

        return NormalizeGenericPath(Path.GetRelativePath(ToNativePath(baseAbs), ToNativePath(pathAbs)));
    }

    private static bool CanComputeRelative(string pathAbs, string baseAbs) {
        if (!OperatingSystem.IsWindows()) {
            return IsAbsolutePath(pathAbs) && IsAbsolutePath(baseAbs);
        }

        string? pathRoot = Path.GetPathRoot(ToNativePath(pathAbs));
        string? baseRoot = Path.GetPathRoot(ToNativePath(baseAbs));
        return !string.IsNullOrEmpty(pathRoot)
            && !string.IsNullOrEmpty(baseRoot)
            && string.Equals(pathRoot, baseRoot, StringComparison.OrdinalIgnoreCase);
    }

    private static long GetLastWriteTime(string pathValue) {
        string path = ToNativePath(pathValue);
        if (File.Exists(path) || Directory.Exists(path)) {
            return new DateTimeOffset(File.GetLastWriteTimeUtc(path)).ToUnixTimeSeconds();
        }

        throw new FileNotFoundException($"Path not found: {pathValue}");
    }

    private static void SetLastWriteTime(string pathValue, long unixSeconds) {
        string path = ToNativePath(pathValue);
        DateTime utc = DateTimeOffset.FromUnixTimeSeconds(unixSeconds).UtcDateTime;
        if (File.Exists(path) || Directory.Exists(path)) {
            File.SetLastWriteTimeUtc(path, utc);
            return;
        }

        throw new FileNotFoundException($"Path not found: {pathValue}");
    }

    private static int GetPermissions(string pathValue) {
        string path = ToNativePath(pathValue);
        if (!File.Exists(path) && !Directory.Exists(path)) {
            throw new FileNotFoundException($"Path not found: {pathValue}");
        }

        FileAttributes attrs = File.GetAttributes(path);
        return (attrs & FileAttributes.ReadOnly) != 0 ? 0 : UserWriteMask;
    }

    private static void SetPermissions(string pathValue, int perms, int option) {
        string path = ToNativePath(pathValue);
        if (!File.Exists(path) && !Directory.Exists(path)) {
            throw new FileNotFoundException($"Path not found: {pathValue}");
        }

        FileAttributes attrs = File.GetAttributes(path);
        bool writable = (perms & UserWriteMask) != 0;

        if (option == PermOptionAdd) {
            writable = true;
        } else if (option == PermOptionRemove) {
            writable = false;
        }

        if (writable) {
            attrs &= ~FileAttributes.ReadOnly;
        } else {
            attrs |= FileAttributes.ReadOnly;
        }

        File.SetAttributes(path, attrs);
    }

    private static void CreateSymlink(string targetValue, string linkValue) {
        string target = ToNativePath(targetValue);
        string link = ToNativePath(linkValue);

        if (Directory.Exists(target)) {
            Directory.CreateSymbolicLink(link, target);
            return;
        }

        File.CreateSymbolicLink(link, target);
    }

    private static void CreateDirectorySymlink(string targetValue, string linkValue) {
        Directory.CreateSymbolicLink(ToNativePath(linkValue), ToNativePath(targetValue));
    }

    private static void CreateHardLink(string targetValue, string linkValue) {
        string target = ToNativePath(targetValue);
        string link = ToNativePath(linkValue);

        if (!File.Exists(target)) {
            throw new FileNotFoundException($"Target not found: {targetValue}");
        }

        if (File.Exists(link)) {
            throw new IOException($"Link destination already exists: {linkValue}");
        }

        File.Copy(target, link, overwrite: false);
    }

    private static string GetExePath() {
        string? processPath = Environment.ProcessPath;
        if (!string.IsNullOrWhiteSpace(processPath)) {
            return ToGenericPath(processPath);
        }

        string? entry = Assembly.GetEntryAssembly()?.Location;
        if (!string.IsNullOrWhiteSpace(entry)) {
            return ToGenericPath(entry);
        }

        return ToGenericPath(AppContext.BaseDirectory);
    }

    private static string GetDllPath() {
        string location = Assembly.GetExecutingAssembly().Location;
        if (!string.IsNullOrWhiteSpace(location)) {
            return ToGenericPath(location);
        }

        return GetExePath();
    }

    private static StatusSnapshot ResolveStatus(string pathValue, bool followSymlink) {
        string path = ToNativePath(pathValue);

        if (!File.Exists(path) && !Directory.Exists(path)) {
            return new StatusSnapshot("not_found", false, false, false, 0);
        }

        FileAttributes attrs = File.GetAttributes(path);
        bool isSymlink = (attrs & FileAttributes.ReparsePoint) != 0;

        if (isSymlink && !followSymlink) {
            return new StatusSnapshot("symlink", true, false, false, GetPermissions(pathValue));
        }

        if (Directory.Exists(path)) {
            return new StatusSnapshot("directory", true, true, false, GetPermissions(pathValue));
        }

        if (File.Exists(path)) {
            return new StatusSnapshot("regular", true, false, true, GetPermissions(pathValue));
        }

        return new StatusSnapshot("unknown", true, false, false, GetPermissions(pathValue));
    }

    private static bool IsReparsePoint(string path) {
        try {
            return (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;
        } catch {
            return false;
        }
    }

    private static long GetMonotonicMilliseconds() {
        return (long)(Stopwatch.GetTimestamp() * 1000.0 / Stopwatch.Frequency);
    }

    private static long GetThreadCpuMilliseconds() {
        try {
            using Process process = Process.GetCurrentProcess();
            return (long)process.TotalProcessorTime.TotalMilliseconds;
        } catch {
            // Fall back to monotonic time when process CPU time cannot be queried.
            return GetMonotonicMilliseconds();
        }
    }

    private static string NormalizeGenericPath(string value) {
        return string.IsNullOrEmpty(value) ? string.Empty : value.Replace('\\', '/');
    }

    private static string ToNativePath(string value) {
        return NormalizeGenericPath(value).Replace('/', Path.DirectorySeparatorChar);
    }

    private static string ToGenericPath(string value) {
        return NormalizeGenericPath(value);
    }

    private static bool IsAbsolutePath(string value) {
        value = NormalizeGenericPath(value);
        if (string.IsNullOrEmpty(value)) {
            return false;
        }

        if (value.StartsWith("//", StringComparison.Ordinal)) {
            return true;
        }

        if (value.Length >= 3 && char.IsLetter(value[0]) && value[1] == ':' && value[2] == '/') {
            return true;
        }

        if (!OperatingSystem.IsWindows() && value.StartsWith("/", StringComparison.Ordinal)) {
            return true;
        }

        return false;
    }

    private static string LexicallyNormalizePath(string value) {
        value = NormalizeGenericPath(value);
        if (string.IsNullOrEmpty(value)) {
            return string.Empty;
        }

        bool absolute = IsAbsolutePath(value);
        string prefix = string.Empty;

        if (value.StartsWith("//", StringComparison.Ordinal)) {
            prefix = "//";
            value = value.Substring(2);
        } else if (value.Length >= 2 && char.IsLetter(value[0]) && value[1] == ':') {
            prefix = value.Substring(0, 2);
            value = value.Length > 2 ? value.Substring(2) : string.Empty;
            if (value.StartsWith("/", StringComparison.Ordinal)) {
                prefix += "/";
                value = value.Substring(1);
            }
        } else if (value.StartsWith("/", StringComparison.Ordinal)) {
            prefix = "/";
            value = value.Substring(1);
        }

        bool trailingSlash = value.EndsWith("/", StringComparison.Ordinal);

        List<string> stack = new List<string>();
        foreach (string part in value.Split('/', StringSplitOptions.RemoveEmptyEntries)) {
            if (part == ".") {
                continue;
            }

            if (part == "..") {
                if (stack.Count > 0 && stack[stack.Count - 1] != "..") {
                    stack.RemoveAt(stack.Count - 1);
                } else if (!absolute) {
                    stack.Add(part);
                }
                continue;
            }

            stack.Add(part);
        }

        string normalized = prefix + string.Join("/", stack);
        if (trailingSlash && normalized.Length > 0 && !normalized.EndsWith("/", StringComparison.Ordinal)) {
            normalized += "/";
        }
        return normalized;
    }

    private static string JoinPath(string left, string right) {
        left = NormalizeGenericPath(left);
        right = NormalizeGenericPath(right);

        if (string.IsNullOrEmpty(left)) {
            return right;
        }

        if (IsAbsolutePath(right)) {
            return right;
        }

        if (OperatingSystem.IsWindows() && (right.StartsWith("/", StringComparison.Ordinal) || right.StartsWith("\\", StringComparison.Ordinal))) {
            string normalizedLeft = LexicallyNormalizePath(left);
            if (normalizedLeft.Length >= 2 && char.IsLetter(normalizedLeft[0]) && normalizedLeft[1] == ':') {
                string drive = normalizedLeft.Substring(0, 2);
                string rhs = right.TrimStart('/', '\\');
                return drive + "/" + rhs;
            }
        }

        if (left.EndsWith("/", StringComparison.Ordinal)) {
            left = left.TrimEnd('/');
        }

        if (string.IsNullOrEmpty(right)) {
            return left;
        }

        return left + "/" + right;
    }

    private static bool PathEquals(string left, string right) {
        return PathStringComparer.Equals(LexicallyNormalizePath(left), LexicallyNormalizePath(right));
    }

    private static string GetFilename(string value) {
        value = NormalizeGenericPath(value);
        if (string.IsNullOrEmpty(value) || value.EndsWith("/", StringComparison.Ordinal)) {
            return string.Empty;
        }

        int index = value.LastIndexOf('/');
        return index < 0 ? value : value.Substring(index + 1);
    }

    private static string GetParentPath(string value) {
        value = NormalizeGenericPath(value);
        if (string.IsNullOrEmpty(value)) {
            return string.Empty;
        }

        if (value.EndsWith("/", StringComparison.Ordinal)) {
            return TrimTrailingSeparators(value);
        }

        int index = value.LastIndexOf('/');
        if (index < 0) {
            return string.Empty;
        }

        if (index == 0) {
            return "/";
        }

        if (index == 2 && value.Length >= 3 && char.IsLetter(value[0]) && value[1] == ':' && value[2] == '/') {
            return value.Substring(0, 3);
        }

        return value.Substring(0, index);
    }

    private static string RemoveFilename(string value) {
        value = NormalizeGenericPath(value);
        if (string.IsNullOrEmpty(value)) {
            return string.Empty;
        }

        if (value.EndsWith("/", StringComparison.Ordinal)) {
            return TrimTrailingSeparators(value) + "/";
        }

        string parent = GetParentPath(value);
        return string.IsNullOrEmpty(parent) ? string.Empty : parent + "/";
    }

    private static string GetStem(string value) {
        string filename = GetFilename(value);
        if (string.IsNullOrEmpty(filename)) {
            return string.Empty;
        }

        string extension = GetExtension(filename);
        if (string.IsNullOrEmpty(extension)) {
            return filename;
        }

        return filename.Substring(0, filename.Length - extension.Length);
    }

    private static string GetExtension(string value) {
        string filename = GetFilename(value);
        if (string.IsNullOrEmpty(filename)) {
            return string.Empty;
        }

        int dot = filename.LastIndexOf('.');
        if (dot <= 0) {
            return string.Empty;
        }

        if (dot == filename.Length - 1) {
            return ".";
        }

        return filename.Substring(dot);
    }

    private static string ReplaceFilename(string value, string filename) {
        string parent = GetParentPath(value);
        string name = NormalizeGenericPath(filename);
        if (string.IsNullOrEmpty(parent)) {
            return name;
        }

        return TrimTrailingSeparators(parent) + "/" + name;
    }

    private static string ReplaceExtension(string value, string extension) {
        value = NormalizeGenericPath(value);
        extension = NormalizeGenericPath(extension);

        if (!string.IsNullOrEmpty(extension) && !extension.StartsWith(".", StringComparison.Ordinal)) {
            extension = "." + extension;
        }

        string filename = GetFilename(value);
        if (string.IsNullOrEmpty(filename)) {
            return value + extension;
        }

        int dot = filename.LastIndexOf('.');
        if (dot <= 0) {
            return value + extension;
        }

        string basePath = value.Substring(0, value.Length - (filename.Length - dot));
        return basePath + extension;
    }

    private static string TrimTrailingSeparators(string value) {
        value = NormalizeGenericPath(value);
        if (string.IsNullOrEmpty(value)) {
            return string.Empty;
        }

        if (value == "/" || value == "//") {
            return value;
        }

        if (value.Length == 3 && char.IsLetter(value[0]) && value[1] == ':' && value[2] == '/') {
            return value;
        }

        return value.TrimEnd('/');
    }

    private static class PathStringComparer {
        internal static bool Equals(string left, string right) {
            return OperatingSystem.IsWindows()
                ? string.Equals(left, right, StringComparison.OrdinalIgnoreCase)
                : string.Equals(left, right, StringComparison.Ordinal);
        }
    }

    private readonly struct StatusSnapshot {
        internal StatusSnapshot(string type, bool exists, bool isDirectory, bool isRegularFile, int permissions) {
            Type = type;
            Exists = exists;
            IsDirectory = isDirectory;
            IsRegularFile = isRegularFile;
            Permissions = permissions;
        }

        internal string Type { get; }
        internal bool Exists { get; }
        internal bool IsDirectory { get; }
        internal bool IsRegularFile { get; }
        internal int Permissions { get; }
    }

    internal sealed class FileLockHandle : IDisposable {
        private readonly LuaWorld _world;
        private readonly FileStream _stream;
        private bool _disposed;

        private FileLockHandle(LuaWorld world, FileStream stream) {
            _world = world;
            _stream = stream;
            _world.RegisterDisposable(this);
        }

        internal static FileLockHandle? TryCreate(LuaWorld world, string pathValue) {
            try {
                string path = ToNativePath(pathValue);
                string? dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) {
                    Directory.CreateDirectory(dir);
                }

                FileStream stream = new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
                return new FileLockHandle(world, stream);
            } catch {
                return null;
            }
        }

        public void close() {
            Dispose();
        }

        public void Dispose() {
            if (_disposed) {
                return;
            }

            _disposed = true;
            try {
                _stream.Dispose();
            } finally {
                _world.UnregisterDisposable(this);
            }
        }
    }
}

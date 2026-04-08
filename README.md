# lua-language-server (fork)

This README documents the top-level items currently in this directory.
If a purpose is not clear from repository contents, it is listed as UNKNOWN.

## Directories

| Name | Purpose |
| --- | --- |
| `bin` | Built server binaries and runtime bootstrap files. |
| `build` | Build artifacts and intermediate outputs. |
| `doc` | User-facing documentation content. |
| `locale` | Localization files used by the server. |
| `log` | Runtime and test log outputs/samples. |
| `lua` | Symbolic link to `script`; reason for link is UNKNOWN. |
| `make` | Build helper sources and platform-specific build inputs. |
| `meta` | API/type metadata used for completion and analysis. |
| `script` | Core Lua language server implementation. |
| `submodules` | External source dependencies pulled as git submodules. |
| `test` | Test cases and fixtures. |
| `tools` | Utility scripts for maintenance/build data generation. |

## Files

| Name | Purpose |
| --- | --- |
| `.editorconfig` | Editor formatting and indentation rules. |
| `.gitignore` | Git ignore rules for generated files/artifacts. |
| `.gitmodules` | Git submodule declarations and source URLs. |
| `.luarc.json` | Lua language server development settings for this repo. |
| `.make.bat` | Windows helper script to initialize submodules and run `luamake`. |
| `.pre-commit-hooks.yaml` | Pre-commit hook config to run Lua checks. |
| `debugger.lua` | Optional debug bootstrap used in development mode. |
| `Dockerfile` | Linux container definition for building the server toolchain. |
| `errors.json` | UNKNOWN (stored diagnostics JSON sample/output). |
| `LICENSE` | Project license text (MIT). |
| `lua-language-server` | Launcher script that runs `main.lua` via `bee`. |
| `lua-language-server-scm-1.rockspec` | LuaRocks package manifest for the server distribution. |
| `main.lua` | Main server entrypoint and process initialization. |
| `make.lua` | Primary `luamake` build script for native/server targets. |
| `package.json` | Minimal package metadata (`name` and `version`). |
| `README.md` | This documentation file. |
| `test.lua` | Test runner entrypoint for local/server test execution. |
| `theme-tokens.md` | Reference documentation for syntax/semantic token scopes. |
| `zig-cc-wrapper.sh` | Wrapper script to call `zig c++` while filtering incompatible flags. |



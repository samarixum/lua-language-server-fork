# lua-language-server (moonsharp)

This repository contains a fork of the Lua Language Server.

This project was implemented for standard lua but is now being slowly refactored to support moonsharp only by rewriting the 5.2 api definitions and removing any non moonsharp compatible code. The project is still in early stages and is not yet ready for production use.



This document provides an overview of the directory structure and key files within the project.

## Workspace Libraries

`Lua.workspace.library` tells the language server to index extra directories as external code libraries. This is the right place for generated code, engine SDKs, or other sources that should participate in completion, hover, diagnostics, and navigation without being treated as normal workspace files.

Files under workspace libraries are read as library content. They are available to the analyzer, but refactors such as rename are not meant to rewrite them.

Example:

```jsonc
"Lua.workspace.library": [
	"${workspaceFolder}/vendor/lua",
	"${workspaceFolder}/generated/api"
]
```

## Remake Engine API Support

This server ships a built-in third-party library definition for Remake Engine at `meta/submodules/remake-engine`.

When `Lua.workspace.checkThirdParty` is enabled, the server can detect and apply the Remake Engine library automatically. You can also enable it explicitly with `Lua.workspace.userThirdParty`.

Once applied, the Remake Engine API definitions are loaded through `Lua.workspace.library`, exposing engine helpers such as `Game_Root`, `UIMode`, and the `sdk` module family. MoonSharp runtime globals now come from the core runtime templates.

The Remake Engine API is intended for Moonsharp 2.0.0.0 projects.

## 📂 Project Structure

### Source & Configuration
These directories house the core logic, documentation, and metadata required for the language server to function.

| Name | Description |
| :--- | :--- |
| `script` | **Core Logic:** Contains the primary Lua implementation of the language server. |

## meta is the LuaCATS (Lua Comment and Type System)
## These repositories contain .lua files with type annotations.
## They don't contain the functional code of the libraries themselves
## they provide the LLS with the Type Descriptions etc
| `meta` | **API Metadata:** Type definitions and metadata used for autocompletion and static analysis. |
files in meta

#built in
spell/ -- contains two text files, dictionary.txt and lua_dict.txt, which are used for spell checking and autocompletion of english words and Lua keywords, respectively.
submodules/ -- third party dependencies via git
template/ -- template

#generated
563e45af/ --empty
default utf8/ -- built-in utf8 lua api definitions
Lua 5.4 zh-cn utf8/ -- built-in Lua 5.4 api definitions
LuaJIT zh-cn utf8/ -- built-in LuaJIT api definitions

| `locale` | **Localization:** Files used by vscode client build script `build-settings.lua` to generate localized `package.nls.json` files. |
| `doc` | **Documentation:** User-facing guides and technical documentation. |
doc contains for each language a config.md documenting all vscode configs listed in locale/<lang>/setting.lua config. entries

### Build & Dependencies
Tools and external sources required to compile or extend the server.

| Name | Description |
| :--- | :--- |
| `submodules` | External Git dependencies. |
| `make` | Platform-specific build inputs and helper sources. |
| `tools` | lua utilities and data generation scripts. |

### Development & Testing
Resources for contributors to verify code quality and debug.

| Name | Description |
| :--- | :--- |
| `test` | Test cases, fixtures, and unit tests. |
| `log` | Runtime logs and test output samples. |
| `bin` | Compiled binaries and runtime bootstrap files. |
| `build` | Intermediate build artifacts. |

---

## 📄 Key Files

### Core Entry Points
| File | Role |
| :--- | :--- |
| `main.lua` | **Entrypoint:** Primary server initialization logic. |
| `lua-language-server` | **Launcher:** Shell script to execute `main.lua` via the `bee` runtime. |
| `make.lua` | **Build Script:** The primary `luamake` definition for all targets. |
| `test.lua` | **Test Runner:** Entrypoint for executing the test suite. |

### Configuration & Environment
| File | Role |
| :--- | :--- |
| `.luarc.json` | Project-specific Lua Language Server settings. |
| `.editorconfig` | Enforces consistent indentation and formatting across editors. |
| `Dockerfile` | Defines the Linux container for the build toolchain. |
| `.pre-commit-hooks.yaml` | Configuration for automated Lua checks before committing. |

### Metadata & Legal
| File | Role |
| :--- | :--- |
| `package.json` | Project metadata (name, version). |
| `LICENSE` | MIT License text. |
| `theme-tokens.md` | Documentation for syntax and semantic token scopes. |
| `lua-language-server-scm-1.rockspec` | LuaRocks manifest for distribution. |











---
Dirs
- .betterGit
- .vscode

- bin   - contains compiled binaries and runtime 'make/bootstrap.lua' named 'main.lua'.
- build - C build artifacts, including object files and intermediate outputs.

- doc    - localisation for vscode client config settings
- locale - localisation for more things also includes the config settings

- log    - logs created by lls 'script\log.lua'

- make   - contains files for building the lls.exe also includes the bootstrap.lua file renamed in bin as main.lua which is the entry point for the lls.exe subsequently calling the main.lua in the root of the project

- meta   - contains 4 build artifact folders for built in lua type definitions and a spell folder with two text files, dictionary.txt and lua_dict.txt, which are used for spell checking and autocompletion of english words and Lua keywords, respectively. also contains the submodules folder which contains third party type definitions such as the remake engine api definitions.

- moonsharpRuntime - contains the new moonsharp lls (intended to replace the current lls.exe) written in C# using moonsharp as the runtime.

- script
- submodules
- test
- tools

Files
- .editorconfig
- .gitignore
- .gitmodules
- .luarc.json
- .make.bat
- .pre-commit-hooks.yaml
- debugger.lua
- errors.json
- LICENSE
- lua-language-server
- lua-language-server-scm-1.rockspec
- main.lua
- make.lua
- package.json
- README.md
- test.lua

---






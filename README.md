# lua-language-server (moonsharp)

This repository contains a fork of the Lua Language Server.

This document provides an overview of the directory structure and key files within the project.

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
| `zig-cc-wrapper` | Wrapper for `zig c++` to filter incompatible build flags. |

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

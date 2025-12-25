# Whisperleaf - Gemini Instructions

When working on this project, adhere to the following strict rules:

1. **No Whole-File Replacements**: Always use the `replace` tool to modify specific blocks of code. Do not overwrite entire files with `write_file` if they already exist and contain logic.
2. **Do Not Modify `.csproj`**: Never attempt to change `Whisperleaf.csproj` or any other project configuration files. If a dependency or configuration change is needed, ask the user.
3. **No Git Commands**: Do not run any `git` commands (e.g., `git add`, `git commit`, `git status`). The user will handle version control.
4. **Ask Before Risky Operations**: Always seek explicit confirmation before performing any operations that could be considered risky, such as deleting files or making broad architectural changes.
5. **Stick to Existing Patterns**: Follow the established architectural patterns and coding styles found in the `Graphics`, `AssetPipeline`, and `Editor` directories.

## Project Overview

Whisperleaf is a custom C# rendering engine and editor using Veldrid. It features a modern PBR rendering pipeline, a comprehensive asset processing system, and a full-featured ImGui-based scene editor.

### Key Features
- **Rendering**: Veldrid backend, PBR, Shadow Mapping (Atlas), Light Culling, and a Render Pass system.
- **Editor**: ImGui docking interface with Scene Outliner, Inspector, Asset Browser, and Viewport with Gizmos.
- **Assets**: Custom pipeline converting GLTF/FBX/OBJ to optimized `.wlmesh` and `.wltex` formats.

### Directory Structure
- **AssetPipeline**: Asset importing (Assimp/GLTF), processing (tangents, texture packing), and caching (`.wlmesh`, `.wltex`).
- **Editor**: ImGui windows, tools, and interaction logic (`EditorManager`, `Gizmos`).
- **Graphics**: Core rendering logic (`Renderer`), render passes (`GltfPass`, `ShadowPass`), shaders, and GPU resources.
- **Platform**: Windowing (SDL2) and input management.
- **Resources**: Runtime assets (scenes, models, textures).

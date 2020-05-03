# unity-precompiler
Takes all the asmdefs in a project, compiles them to dlls, and fixes up assets to point at the updated guid/fileIDs. Useful if you have lots of asmdefs and it's slowing down your iteration speed.

## Standard Usage
You probably just want to run the "all", which combines all the stages.

**UnityPrecompiler.exe all -s srcPath -d dstPath [-# defines] [-c configuration] [-x extensions] [-p pluginDir]**
 - **srcPath:** path to source project directory
 - **dstPath:** path to target project directory
 - **defines:** Preprocessor defines used to determine class info. Space separated, e.g.: "UNITY_EDITOR UNITY_WSA"
 - **configuration:** Configuration to build assemblies (Debug/Release). Defaults to "Debug"
 - **pluginDir:** Optional plugin Directory relative to Assets directory in destination project directory. Defaults to 'Plugins'
 - **extensions:** Optional set of extension to only fix up. Space separated, e.g.: "unity prefab mat asset cubemap ..."

Example:

UnityPrecompiler.exe all -s "D:\Code\Experiments\MRTK" -d "D:\Code\Experiments\MRTKCompiled" --defines "WINDOWS_UWP UNITY_EDITOR UNITY_WSA" -c Debug -p "MRTK\Plugins"

### Stage 1: Copy
Copies Assets, ProjectSettings and Packages folders files from srcPath to dstPath to prepare for next stages.
Uses robocopy to mirror them for faster subsequent runs.
Excludes *.cs, *.asmdef in target project since we're compiling them, so note that loose files outside of asmdefs won't get copied over.

**UnityPrecompiler.exe copy -s srcPath -d dstPath**
 - **srcPath:** path to source project directory
 - **dstPath:** path to target project directory


### Stage 2: Compile
Compiles the dlls with pdbs & mdbs, and produces .map files that hold the information to map script guids to their new locations in the dlls. Also tracks and applies scriptExecutionOrder.

**UnityPrecompiler.exe compile -s srcPath -d dstPath [-# defines] [-c configuration] [-p pluginDir]**
 - **srcPath:** path to source project directory
 - **dstPath:** path to target project directory
 - **defines:** preprocessor defines used to determine class info. Space separated, e.g.: "UNITY_EDITOR UNITY_WSA"
 - **pluginDir:** Plugin Directory relative to Assets directory in destination project directory. Defaults to 'Plugins'
 - **configuration:** Configuration to build assemblies (Debug/Release). Defaults to "Debug"

### Stage 3: Fixup
Fixes up all the assets in the dstPath project using the .map information in pluginDir.

**UnityPrecompiler.exe fixup -d dstPath [-x extensions] [-p pluginDir]**
 - **dstPath:** path to target project directory
 - **extensions:** optional set of extension to only fix up. Space separated, e.g.: "unity prefab mat asset cubemap ..."
 - **pluginDir:** Plugin Directory relative to Assets directory in destination project directory. Defaults to 'Plugins'

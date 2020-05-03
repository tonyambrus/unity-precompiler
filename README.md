# unity-precompiler
Takes all the asmdefs in a project, compiles them to dlls, and fixes up assets to point at the updated guid/fileIDs. Useful if you have lots of asmdefs and it's slowing down your iteration speed.

## Standard Usage
You probably just want to run the "all", which combines all the stages.

**UnityPrecompiler.exe compile -s srcPath -d dstPath [-Defines defines] [-c configuration] [-x extensions] [-p pluginDir]**
 - **srcPath:** path to source project directory
 - **dstPath:** path to target project directory
 - **defines:** preprocessor defines used to determine class info
 - **configuration:** Configuration to build assemblies (Debug/Release). Defaults to "Debug"
 - **pluginDir:** Plugin Directory relative to Assets directory in destination project directory. Defaults to 'Plugins'
 - **extensions:** optional set of extension to only fix up. Space separated, e.g.: "unity prefab mat asset cubemap ..."

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
**UnityPrecompiler.exe compile -s srcPath -d dstPath [-defines defines] [-c configuration]**
 - **srcPath:** path to source project directory
 - **dstPath:** path to target project directory
 - **defines:** preprocessor defines used to determine class info. Space separated, e.g.: "UNITY_EDITOR UNITY_WSA"
 - **configuration:** Configuration to build assemblies (Debug/Release). Defaults to "Debug"

### Stage 3: Fixup
**UnityPrecompiler.exe fixup -d dstPath [-x extensions]**
 - **dstPath:** path to target project directory
 - **extensions:** optional set of extension to only fix up. Space separated, e.g.: "unity prefab mat asset cubemap ..."
 - **pluginDir:** Plugin Directory relative to Assets directory in destination project directory. Defaults to 'Plugins'

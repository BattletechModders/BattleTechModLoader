# BattleTechModLoader
A simple mod loader and injector for HBS's BattleTech PC game.

***PLEASE DO NOT RELEASE ANY MODS BASED ON THIS YET! STILL IN ACTIVE DEVELOPMENT!***

## How It Works
Using Mono.Cecil, the injector hooks the mod loader into the Assembly-CSharp.dll. After the Assembly-CSharp.dll file has been modified, on game load, the mod loader tries to load DLL files from `\BATTLETECH\Mods\` and call a static method in any class called "Init" (if there are multiple, it will simply call all of them). Mods then use Harmony to patch/decorate the game's functions at runtime. 

A log is generated at `\Mods\BTModLoader.log` overwriting any previous log. This log contains a record all of the Harmony hooks that happen as a result of all of the DLL files `Init` calls.

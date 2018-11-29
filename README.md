# BattleTechModLoader

A simple mod loader and injector for HBS's BattleTech PC game.

## Installing/Updating

[A step-by-step guide to installing BTML + ModTek + ModTek mods.](https://github.com/janxious/ModTek/wiki/The-Drop-Dead-Simple-Guide-to-Installing-BTML-&-ModTek-&-ModTek-mods)

### Windows
Download the zip, and extract the entire contents (the `.exe` and all of the `.dll`s) into your `\BATTLETECH\BattleTech_Data\Managed\` folder. Run `BattleTechModLoaderInjector.exe`, it'll pop open a console window and run through the process of modifying `Assembly-CSharp.dll`, including making a backup into `Assembly-CSharp.dll.orig`. Don't worry if the assembly is slightly smaller than it started, it will still work, unless the console window shows an exception during the writing process. If this happens, just delete it and rename the backup to have your original file back.

### macOS/Linux
Download the zip, and extract the entire contents (the `.exe` and all of the `.dll`s) into your `\BATTLETECH\BattleTech_Data\Mods\` folder. Then from a command line, run:

```
mono BattleTechModLoaderInjector.exe /install /manageddir=../BattleTech_Data/Managed/
```

You can also put it in a different folder and alter the values passed to `/manageddir`.

### Other installation notes

If the game patches or somehow replaces `Assembly-CSharp.dll`, running the injector will patch it again. Since the mod loader and the injector are very simple, it should be pretty resistant to updates changing the assembly.

Updates to BattleTechModLoader itself will almost certainly not require you to re-inject an already injected assembly. Simply replace all of the included files.

*Note: Running the injector on an already injected game won't bork anything, as it checks to see if the assembly has already been injected.*

## Note For Modders

Because of its extremely simple nature, you should be careful about basing your mod on BTML itself -- if you're developing a tool or mod that won't any need additional files (or advanced features, such as dependancies, load order, etc.), this might be a good fit.

I'm currently working on another utility -- [ModTek](https://github.com/janxious/ModTek/tree/master/ModTek) that will provide a better all around experience for users and modders alike. It uses a `mod.json` file for each mod that defines its `.dll`, an entry point, dependancies, load-order, and facilitates importing/modifying additional files into the game. As of 5/5/2018, this mod is in ***Heavy*** developement, and won't be ready to go for a while yet. If you'd like to help out, jump into either BattleTech Discord ([1](https://discord.gg/ncTCh3k) or [2](https://discord.gg/fxXr8nV)) and ask around there or message me directly on [Reddit](https://www.reddit.com/user/Mpstark/).

## How It Works

BTML uses [Mono.Cecil](https://github.com/jbevain/cecil) to parse `Assembly-CSharp.dll` and find a predetermined point (`BattleTech.GameInstance`'s constructor) in code to inject a single method call into `BattleTechModLoader.dll`, which will load `0Harmony.dll`, and then any `.dll` file contained within the root of `\BATTLETECH\Mods\`. After loading each assembly, the loader will look for any `public static Init()` functions on any of the classes defined in the assembly and will invoke all of them.

A log is generated at `\Mods\BTModLoader.log` overwriting any previous log. This log additionally contains a record all of the Harmony hooks that happen at mod loading time.

## Building It
In each project folder there is an example project user file (e.g. `BattleTechModLoaderInjector.csproj.user.example`). You can copy that file and rename it without the `.example` ending and then update it to point to your managed DLL folder. The Injector user file is also setup to run the injector in the managed folder, so you can test the injector without leaving VStudio.

## Licence

BTML is provided under the "Unlicence", which releases the work into the public domain.

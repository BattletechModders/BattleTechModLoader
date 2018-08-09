using System;
using System.IO;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Mono.Options;

namespace BattleTechModLoader
{
    using System.Diagnostics;
    using System.Reflection;
    using static Console;

    internal static class BTMLInjector
    {
        private const string InjectedDllFileName = "BattleTechModLoader.dll";

        private const string GameDllFileName = "Assembly-CSharp.dll";
        private const string BackupFileExt   = ".orig";

        private const string HookType     = "BattleTech.Main";
        private const string HookMethod   = "Start";
        private const string InjectType   = "BattleTechModLoader.BTModLoader";
        private const string InjectMethod = "Init";

        private static int Main(string[] args)
        {
            var returnCode = 0;
            var requireKeyPress = true;
            var detecting = false;
            var helping = false;
            var installing = true;
            var restoring = false;
            var updating = false;
            var versioning = false;

            try
            {
                var options = new OptionSet()
                {
                    { "d|detect",     "Detect if the BTG assembly is already injected",   v => detecting = v != null },
                    { "h|?|help",     "Print this useful help message",                   v => helping = v != null },
                    { "i|install",    "Install the Mod (Default Behavior)",               v => installing = v != null },
                    { "y|nokeypress", "Anwser prompts affirmatively",                     v => requireKeyPress = v == null },
                    { "r|restore",    "Restore pristine BTG assembly to folder",          v => restoring = v != null },
                    { "u|update",     "Update injected BTG assembly to current version",  v => updating = v != null },
                    { "v|version",    "Print the BattleTechModInjector version number",   v => versioning = v != null }
                };

                try
                {
                    options.Parse(args);
                }
                catch (OptionException e)
                {
                    SayOptionException(e);
                    returnCode = 2;
                    return returnCode;
                }

                if (versioning)
                {
                    SayVersion();
                    return returnCode;
                }

                var directory = Directory.GetCurrentDirectory();
                var gameDllPath = Path.Combine(directory, GameDllFileName);
                var gameDllBackupPath = Path.Combine(directory, GameDllFileName + BackupFileExt);
                var injectDllPath = Path.Combine(directory, InjectedDllFileName);

                bool isCurrentInjection;
                var injected = IsInjected(gameDllPath, out isCurrentInjection);

                if (detecting)
                {
                    SayInjectedStatus(injected);
                    return returnCode;
                }

                SayHeader();

                if (helping)
                {
                    SayHelp(options);
                    return returnCode;
                }

                if (restoring) {
                    if (injected)
                    {
                        Restore(gameDllPath, gameDllBackupPath);
                    }
                    else
                    {
                        SayAlreadyRestored();
                    }
                    PromptForKey(requireKeyPress);
                    return returnCode;
                }

                if (updating) {
                    if (injected) {
                        var yes = PromptForUpdateYesNo(requireKeyPress);
                        if (yes) {
                            if (File.Exists(gameDllBackupPath)) {
                                Restore(gameDllPath, gameDllBackupPath);
                            }
                            else
                            {
                                SayException(new FileNotFoundException());
                                SayHowToRecover();
                            }
                            Inject(gameDllPath, injectDllPath);
                        }
                        else
                        {
                            SayUpdateCanceled();
                        }
                    }
                    PromptForKey(requireKeyPress);
                    return returnCode;
                }

                if (installing)
                {
                    if (!injected)
                    {
                        // TODO: detect backup corruption
                        Backup(gameDllPath, gameDllBackupPath);
                        Inject(gameDllPath, injectDllPath);
                    }
                    else
                    {
                        SayAlreadyInjected(isCurrentInjection);
                    }
                    PromptForKey(requireKeyPress);
                    return returnCode;
                }
            }
            catch (Exception e)
            {
                SayException(e);
            }

            // if we got here something went badly
            returnCode = 1;
            return returnCode;
        }

        private static void SayHowToRecover()
        {
            WriteLine("You will need the original assembly file named as assembly-csharp.dll.orig beside the injector for /restore to work.");
            WriteLine("You may need to reinstall or use Steam's file verification function if you have no other backup.");
        }

        private static void SayInjectedStatus(bool injected)
        {
            if (injected) {
                WriteLine("true");
            } else {
                WriteLine("false");
            }
        }

        private static void Backup(string filePath, string backupFilePath)
        {
            File.Copy(filePath, backupFilePath, true);
            WriteLine($"{Path.GetFileName(filePath)} backed up to {Path.GetFileName(backupFilePath)}");
        }

        private static void Restore(string filePath, string backupFilePath)
        {
            File.Copy(backupFilePath, filePath, true);
            WriteLine($"{Path.GetFileName(backupFilePath)} restored to {Path.GetFileName(filePath)}");
        }

        private static void Inject(string hookFilePath, string injectFilePath)
        {
            WriteLine($"Injecting {Path.GetFileName(hookFilePath)} with {InjectType}.{InjectMethod} at {HookType}.{HookMethod}");

            using (var game = ModuleDefinition.ReadModule(hookFilePath, new ReaderParameters { ReadWrite = true }))
            using (var injecting = ModuleDefinition.ReadModule(injectFilePath))
            {
                // get the methods that we're hooking and injecting
                var injectedMethod = injecting.GetType(InjectType).Methods.Single(x => x.Name == InjectMethod);
                var hookedMethod = game.GetType(HookType).Methods.First(x => x.Name == HookMethod);

                // If the return type is an iterator -- need to go searching for its MoveNext method which contains the actual code you'll want to inject
                if (hookedMethod.ReturnType.Name.Equals("IEnumerator"))
                {
                    var nestedIterator = game.GetType(HookType).NestedTypes.First(x => x.Name.Contains(HookMethod) && x.Name.Contains("Iterator"));
                    hookedMethod = nestedIterator.Methods.First(x => x.Name.Equals("MoveNext"));
                }

                // As of BattleTech v1.1 the Start() iterator method of BattleTech.Main has this at the end
                //
                //  ...
                //
                //      Serializer.PrepareSerializer();
                //      this.activate.enabled = true;
                //      yield break;
                //
                //  }
                //

                // We want to inject after the PrepareSerializer call -- so search for that call in the CIL
                int targetInstruction = -1;
                for (int i = 0; i < hookedMethod.Body.Instructions.Count; i++)
                {
                    var instruction = hookedMethod.Body.Instructions[i];
                    if (instruction.OpCode.Code.Equals(Code.Call) && instruction.OpCode.OperandType.Equals(OperandType.InlineMethod))
                    {
                        MethodReference methodReference = (MethodReference)instruction.Operand;
                        if (methodReference.Name.Contains("PrepareSerializer"))
                        {
                            targetInstruction = i;
                        }
                    }
                }

                if (targetInstruction != -1)
                {
                    hookedMethod.Body.GetILProcessor().InsertAfter(hookedMethod.Body.Instructions[targetInstruction],
                        Instruction.Create(OpCodes.Call, game.ImportReference(injectedMethod)));
                    // save the modified assembly
                    WriteLine($"Writing back to {Path.GetFileName(hookFilePath)}...");
                    game.Write();
                    WriteLine("Injection complete!");
                }
                else
                {
                    WriteLine($"Could not locate injection point!");
                }
            }
        }

        private static bool IsInjected(string dllPath, out bool isCurrentInjection)
        {
            isCurrentInjection = false;
            using (var dll = ModuleDefinition.ReadModule(dllPath))
            {
                foreach (TypeDefinition type in dll.Types)
                {
                    // Assume we only ever inject in BattleTech classes
                    if (!type.FullName.StartsWith("BattleTech", StringComparison.Ordinal)) continue;

                    // Standard methods
                    foreach (var methodDefinition in type.Methods)
                    {
                        if (IsHookInstalled(methodDefinition, out isCurrentInjection))
                        {
                            return true;
                        }
                    }

                    // Also have to check in places like IEnumerator generated methods (Nested)
                    foreach (var nestedType in type.NestedTypes)
                    {
                        foreach (var methodDefinition in nestedType.Methods)
                        {
                            if (IsHookInstalled(methodDefinition, out isCurrentInjection))
                            {
                                return true;
                            }
                        }
                    }
                }
            }
            return false;
        }

        private static bool IsHookInstalled(MethodDefinition methodDefinition, out bool isCurrentInjection)
        {
            isCurrentInjection = false;
            if (methodDefinition.Body == null) return false;

            foreach (var instruction in methodDefinition.Body.Instructions)
            {
                if (instruction.OpCode.Equals(OpCodes.Call) &&
                    instruction.Operand.ToString().Equals($"System.Void {InjectType}::{InjectMethod}()"))
                {
                    isCurrentInjection =
                        methodDefinition.FullName.Contains(HookType) &&
                        methodDefinition.FullName.Contains(HookMethod);
                    return true;
                }
            }
            return false;
        }

        private static void SayHelp(OptionSet p)
        {
            WriteLine("Usage: BattleTechModLoaderInjector.exe [OPTIONS]+");
            WriteLine("Inject the BattleTech game assembly with an entry point for mod enablement.");
            WriteLine("If no options are specified, the program assumes you want to /install.");
            WriteLine();
            WriteLine("Options:");
            p.WriteOptionDescriptions(Out);
        }

        private static void SayVersion()
        {
            WriteLine(GetProductVersion());
        }

        public static string GetProductVersion()
        {
            Assembly assembly = Assembly.GetExecutingAssembly();
            FileVersionInfo fvi = FileVersionInfo.GetVersionInfo(assembly.Location);
            return fvi.FileVersion;
        }

        private static void SayOptionException(OptionException e)
        {
            SayHeader();
            Write("BattleTechModLoaderInjector.exe: ");
            WriteLine(e.Message);
            WriteLine("Try `BattleTechModLoaderInjector.exe --help' for more information.");
        }

        private static void SayHeader()
        {
            WriteLine("BattleTechModLoader Injector");
            WriteLine("----------------------------");
        }

        private static void SayWasNotInjected()
        {
            WriteLine($"{GameDllFileName} was not previously injected.");
        }

        private static void SayAlreadyInjected(bool isCurrentInjection)
        {
            if (isCurrentInjection)
            {
                WriteLine($"ERROR: {GameDllFileName} already injected at {InjectType}.{InjectMethod}.");
            }
            else
            {
                WriteLine($"ERROR: {GameDllFileName} already injected with an older BattleTechModLoader Injector.  Please revert the file and re-run injector!");
            }
        }

        private static void SayAlreadyRestored()
        {
            WriteLine($"{GameDllFileName} already restored.");
        }

        private static void SayUpdateCanceled()
        {
            WriteLine($"{GameDllFileName} update cancelled.");
        }

        private static void SayException(Exception e)
        {
            WriteLine($"ERROR: An exception occured: {e}");
        }

        private static bool PromptForUpdateYesNo(bool requireKeyPress)
        {
            if (!requireKeyPress) return true;
            WriteLine("Would you like to update your assembly now? (y/n)");
            var key = ReadKey();
            return (key.Key == ConsoleKey.Y);
        }

        private static void PromptForKey(bool requireKeyPress)
        {
            if (!requireKeyPress) return;
            WriteLine("Press any key to continue.");
            ReadKey();
        }
    }
}

using System;
using System.IO;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;
using System.Collections.Generic;

namespace BattleTechModLoader
{
    using static Console;

    internal static class BTMLInjector
    {
        private const string INJECTED_DLL_FILE_NAME = "BattleTechModLoader.dll";

        private const string GAME_DLL_FILE_NAME = "Assembly-CSharp.dll";
        private const string BACKUP_EXT = ".orig";

        private const string CURRENT_INJECT_TYPE = "BattleTechModLoader.BTModLoader";
        private const string CURRENT_INJECT_METHOD = "Init";
        private const string CURRENT_HOOK_TYPE = "BattleTech.Main";
        private const string CURRENT_HOOK_METHOD = "Start";
        private static readonly Dictionary<string, string> OLD_HOOKS = new Dictionary<string, string>()
        {
            { "BattleTech.GameInstance", ".ctor" }
        };

        private static int Main(string[] args)
        {
            var directory = Directory.GetCurrentDirectory();

            var gameDllPath = Path.Combine(directory, GAME_DLL_FILE_NAME);
            var gameDllBackupPath = Path.Combine(directory, GAME_DLL_FILE_NAME + BACKUP_EXT);
            var injectDllPath = Path.Combine(directory, INJECTED_DLL_FILE_NAME);

            WriteLine("BattleTechModLoader Injector");
            WriteLine("----------------------------");

            var returnCode = 0;
            try
            {
                bool isCurrentInjection;
                var injected = IsInjected(gameDllPath, out isCurrentInjection);
                var needsInjection = !injected;
                var installing = !args.Contains("/restore");

                if (installing)
                {
                    if (needsInjection)
                    {
                        Backup(gameDllPath, gameDllBackupPath);
                        Inject(gameDllPath, CURRENT_HOOK_TYPE, CURRENT_HOOK_METHOD, injectDllPath, CURRENT_INJECT_TYPE, CURRENT_INJECT_METHOD);
                    }
                    else
                    {
                        if (isCurrentInjection)
                        {
                            WriteLine($"{GAME_DLL_FILE_NAME} already injected with {CURRENT_INJECT_TYPE}.{CURRENT_INJECT_METHOD}.");
                        }
                        else
                        {
                            WriteLine($"ERROR: {GAME_DLL_FILE_NAME} already injected with an older BattleTechModLoader Injector.  Please revert the file and re-run injector!");
                        }
                    }
                }
                else
                {
                    if (injected)
                    {
                        Restore(gameDllPath, gameDllBackupPath);
                    }
                    else
                    {
                        WriteLine($"{GAME_DLL_FILE_NAME} already restored.");
                    }
                }
            }
            catch (Exception e)
            {
                WriteLine($"An exception occured: {e}");
                returnCode = 1;
            }

            // if executed from e.g. a setup or test tool, don't prompt
            // ReSharper disable once InvertIf
            if (!args.Contains("/nokeypress"))
            {
                WriteLine("Press any key to continue.");
                ReadKey();
            }

            return returnCode;
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

        private static void Inject(string hookFilePath, string hookType, string hookMethod, string injectFilePath,
            string injectType, string injectMethod)
        {
            WriteLine($"Injecting {Path.GetFileName(hookFilePath)} with {injectType}.{injectMethod} at {hookType}.{hookMethod}");

            using (var game = ModuleDefinition.ReadModule(hookFilePath, new ReaderParameters { ReadWrite = true }))
            using (var injected = ModuleDefinition.ReadModule(injectFilePath))
            {
                // get the methods that we're hooking and injecting
                var injectedMethod = injected.GetType(injectType).Methods.Single(x => x.Name == injectMethod);
                var hookedMethod = game.GetType(hookType).Methods.First(x => x.Name == hookMethod);

                // If the return type is an iterator -- need to go searching for its MoveNext method which contains the actual code you'll want to inject
                if (hookedMethod.ReturnType.Name.Equals("IEnumerator"))
                {
                    var nestedIterator = game.GetType(hookType).NestedTypes.First(x => x.Name.Contains(hookMethod) && x.Name.Contains("Iterator"));
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
                if (CheckCurrentHook(dll)) {
                    isCurrentInjection = true;
                    return true;
                }
                if (CheckOldHooks(dll)) {
                    return true;
                }
            }
            return false;
        }

        private static bool CheckCurrentHook(ModuleDefinition dll) {
            var method = dll.GetType(CURRENT_HOOK_TYPE).Methods.First(x => x.Name == CURRENT_HOOK_METHOD);
            return IsHookInstalled(method);
        }

        private static bool CheckOldHooks(ModuleDefinition dll) {
            foreach (var typeMethodPair in OLD_HOOKS)
            {
                var definition = dll.GetType(typeMethodPair.Key);
                if (definition == null) continue;
                var method = definition.Methods.First(x => x.Name == typeMethodPair.Value);
                if (method == null) continue;
                if (IsHookInstalled(method)) return true;
            }
            return false;
        }

        private static bool IsHookInstalled(MethodDefinition methodDefinition)
        {
            if (methodDefinition.Body != null)
            {
                foreach (var instruction in methodDefinition.Body.Instructions)
                {
                    if (instruction.OpCode.Equals(OpCodes.Call) &&
                        instruction.Operand.ToString().Equals($"System.Void {CURRENT_INJECT_TYPE}::{CURRENT_INJECT_METHOD}()"))
                    {
                        return true;
                    }
                }
            }
            return false;
        }
    }
}
 
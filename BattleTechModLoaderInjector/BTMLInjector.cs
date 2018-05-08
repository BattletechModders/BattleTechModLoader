using System;
using System.IO;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace BattleTechModLoader
{
    using static Console;

    internal static class BTMLInjector
    {
        private const string INJECTED_DLL_FILE_NAME = "BattleTechModLoader.dll";
        private const string INJECT_TYPE = "BattleTechModLoader.BTModLoader";
        private const string INJECT_METHOD = "Init";

        private const string GAME_DLL_FILE_NAME = "Assembly-CSharp.dll";
        private const string BACKUP_EXT = ".orig";

        private const string HOOK_TYPE = "BattleTech.GameInstance";
        private const string HOOK_METHOD = ".ctor";

        private static int Main()
        {
            var directory = Directory.GetCurrentDirectory();

            var gameDllPath = Path.Combine(directory, GAME_DLL_FILE_NAME);
            var gameDllBackupPath = Path.Combine(directory, GAME_DLL_FILE_NAME + BACKUP_EXT);
            var injectDllPath = Path.Combine(directory, INJECTED_DLL_FILE_NAME);

            WriteLine("BattleTechModLoader Injector");
            WriteLine("----------------------------");

            try
            {
                if (!IsInjected(gameDllPath, HOOK_TYPE, HOOK_METHOD, injectDllPath, INJECT_TYPE, INJECT_METHOD))
                {
                    Backup(gameDllPath, gameDllBackupPath);
                    Inject(gameDllPath, HOOK_TYPE, HOOK_METHOD, injectDllPath, INJECT_TYPE, INJECT_METHOD);
                }
                else
                {
                    WriteLine($"{GAME_DLL_FILE_NAME} already injected with {INJECT_TYPE}.{INJECT_METHOD}.");
                }
            }
            catch (Exception e)
            {
                WriteLine($"An exception occured: {e}");
            }

            // if executed from e.g. a setup or test tool, don't prompt
            // ReSharper disable once InvertIf
            if (Environment.UserInteractive)
            {
                WriteLine("Press any key to continue.");
                ReadKey();
            }

            return 0;
        }

        private static void Backup(string filePath, string backupFilePath)
        {
            if (File.Exists(backupFilePath))
                File.Delete(backupFilePath);

            File.Copy(filePath, backupFilePath);

            WriteLine($"{Path.GetFileName(filePath)} backed up to {Path.GetFileName(backupFilePath)}");
        }

        private static void Inject(string hookFilePath, string hookType, string hookMethod, string injectFilePath,
            string injectType, string injectMethod)
        {
            WriteLine($"Injecting {Path.GetFileName(hookFilePath)} with {injectType}.{injectMethod} at {hookType}.{hookMethod}");

            using (var game = ModuleDefinition.ReadModule(hookFilePath, new ReaderParameters {ReadWrite = true}))
            using (var injected = ModuleDefinition.ReadModule(injectFilePath))
            {
                // get the methods that we're hooking and injecting
                var injectedMethod = injected.GetType(injectType).Methods.Single(x => x.Name == injectMethod);
                var hookedMethod = game.GetType(hookType).Methods.First(x => x.Name == hookMethod);

                // inject our method into the beginning of the hooks method
                hookedMethod.Body.GetILProcessor().InsertBefore(hookedMethod.Body.Instructions[0],
                    Instruction.Create(OpCodes.Call, game.ImportReference(injectedMethod)));

                // save the modified assembly
                WriteLine($"Writing back to {Path.GetFileName(hookFilePath)}...");
                game.Write();
                WriteLine("Injection complete!");
            }
        }

        // ReSharper disable once UnusedParameter.Local // injectFilePath
        private static bool IsInjected(string hookFilePath, string hookType, string hookMethod, string injectFilePath,
            string injectType, string injectMethod)
        {
            using (var game = ModuleDefinition.ReadModule(hookFilePath))
            {
                // get the methods that we're hooking and injecting
                var hookedMethod = game.GetType(hookType).Methods.First(x => x.Name == hookMethod);

                // check if we've been injected
                foreach (var instruction in hookedMethod.Body.Instructions)
                {
                    if (instruction.OpCode.Equals(OpCodes.Call)
                        && instruction.Operand.ToString().Equals(
                            $"System.Void {injectType}::{injectMethod}()"))
                    {
                        return true;
                    }
                }
            }

            return false;
        }
    }
}
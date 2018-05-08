using System;
using System.IO;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace BattleTechModLoader
{
    using static Console;

    // ReSharper disable once InconsistentNaming
    internal static class BTMLInjector
    {
        private const string InjectedDllFileName = "BattleTechModLoader.dll";
        private const string InjectType = "BattleTechModLoader.BTModLoader";
        private const string InjectMethod = "Init";

        private const string GameDllFileName = "Assembly-CSharp.dll";
        private const string BackupExt = ".orig";

        private const string HookType = "BattleTech.GameInstance";
        private const string HookMethod = ".ctor";

        /// <summary>
        /// Entry point for the BTML Injector CLI application.
        /// </summary>
        /// <param name="args">System provided arguments.</param>
        // ReSharper disable once UnusedParameter.Local
        private static int Main(string[] args)
        {
            var directory = Directory.GetCurrentDirectory();

            var gameDllPath = Path.Combine(directory, GameDllFileName);
            var gameDllBackupPath = Path.Combine(directory, GameDllFileName + BackupExt);
            var injectDllPath = Path.Combine(directory, InjectedDllFileName);

            WriteLine("BattleTechModLoader Injector");
            WriteLine("----------------------------");

            try
            {
                if (!IsInjected(gameDllPath, HookType, HookMethod, injectDllPath, InjectType, InjectMethod))
                {
                    Backup(gameDllPath, gameDllBackupPath);
                    Inject(gameDllPath, HookType, HookMethod, injectDllPath, InjectType, InjectMethod);
                }
                else
                {
                    WriteLine($"{GameDllFileName} already injected with {InjectType}.{InjectMethod}.");
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
            WriteLine(
                $"Injecting {Path.GetFileName(hookFilePath)} with {injectType}.{injectMethod} at {hookType}.{hookMethod}");

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

        // ReSharper disable once UnusedParameter.Local
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
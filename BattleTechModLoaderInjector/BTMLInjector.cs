using System;
using System.IO;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace BattleTechModLoader
{
    public class BTMLInjector
    {
        static void Main(string[] args)
        {
            string directory = Directory.GetCurrentDirectory();

            string injectedDLLFileName = "BattleTechModLoader.dll";
            string injectType = "BattleTechModLoader.BTModLoader";
            string injectMethod = "Init";

            string gameDLLFileName = "Assembly-CSharp.dll";
            string backupExt = ".orig";

            string hookType = "BattleTech.GameInstance";
            string hookMethod = ".ctor";
            
            string gameDLLPath = Path.Combine(directory, gameDLLFileName);
            string gameDLLBackupPath = Path.Combine(directory, gameDLLFileName + backupExt);
            string injectDLLPath = Path.Combine(directory, injectedDLLFileName);

            Console.WriteLine("BattleTechModLoader Injector");
            Console.WriteLine("----------------------------");

            try
            {
                if (!IsInjected(gameDLLPath, hookType, hookMethod, injectDLLPath, injectType, injectMethod))
                {
                    Backup(gameDLLPath, gameDLLBackupPath);
                    Inject(gameDLLPath, hookType, hookMethod, injectDLLPath, injectType, injectMethod);
                }
                else
                {
                    Console.WriteLine("{0} already injected with {1}.{2}.", gameDLLFileName, injectType, injectMethod);
                }
            }
            catch (Exception e)
            {
                Console.WriteLine("An exception occured: {0}", e.ToString());
            }

            Console.WriteLine("Press any key to continue.");
            Console.ReadKey();
        }

        static void Backup(string filePath, string backupFilePath)
        {
            if (File.Exists(backupFilePath))
                File.Delete(backupFilePath);

            File.Copy(filePath, backupFilePath);

            Console.WriteLine("{0} backed up to {1}", Path.GetFileName(filePath), Path.GetFileName(backupFilePath));
        }

        static void Inject(string hookFilePath, string hookType, string hookMethod, string injectFilePath, string injectType, string injectMethod)
        {
            Console.WriteLine("Injecting {0} with {1}.{2} at {3}.{4}", Path.GetFileName(hookFilePath), injectType, injectMethod, hookType, hookMethod);

            using (var game = ModuleDefinition.ReadModule(hookFilePath, new ReaderParameters { ReadWrite = true }))
            using (var injected = ModuleDefinition.ReadModule(injectFilePath))
            {
                // get the methods that we're hooking and injecting
                var injectedMethod = injected.GetType(injectType).Methods.Single(x => x.Name == injectMethod);
                var hookedMethod = game.GetType(hookType).Methods.First(x => x.Name == hookMethod);

                // inject our method into the beginning of the hooks method
                hookedMethod.Body.GetILProcessor().InsertBefore(hookedMethod.Body.Instructions[0], Instruction.Create(OpCodes.Call, game.ImportReference(injectedMethod)));

                // save the modified assembly
                Console.WriteLine("Writing back to {0}...", Path.GetFileName(hookFilePath));
                game.Write();
                Console.WriteLine("Injection complete!");
            }
        }

        static bool IsInjected(string hookFilePath, string hookType, string hookMethod, string injectFilePath, string injectType, string injectMethod)
        {
            using (var game = ModuleDefinition.ReadModule(hookFilePath))
            {
                // get the methods that we're hooking and injecting
                var hookedMethod = game.GetType(hookType).Methods.First(x => x.Name == hookMethod);

                // check if we've been injected
                foreach (var instruction in hookedMethod.Body.Instructions)
                {
                    if (instruction.OpCode.Equals(OpCodes.Call) && instruction.Operand.ToString().Equals(String.Format("System.Void {0}::{1}()", injectType, injectMethod)))
                    {
                        return true;
                    }
                }
            }

            return false;
        }
    }
}

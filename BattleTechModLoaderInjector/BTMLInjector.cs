using System;
using System.IO;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Mono.Options;

namespace BattleTechModLoader
{
#if RTML
    using System.Collections.Generic;
    using Newtonsoft.Json;
    using Ionic.Zip;
#endif
    using System.Diagnostics;
    using System.Reflection;


    using static Console;

    internal static class BTMLInjector
    {
        // Return Codes, Codified
        private const int RC_Normal                      = 0;
        private const int RC_UnhandledState              = 1;
        private const int RC_BadOptions                  = 2;
        private const int RC_MissingBackupFile           = 3;
        private const int RC_BackupFileInjected          = 4;
        private const int RC_BadManagedDirectoryProvided = 5;
        private const int RC_MissingModLoaderAssembly    = 6;
        private const int RC_RequiredGameVersionMismatch = 7;
        // /Return Codes

        private const string ModLoaderDllFileName = "BattleTechModLoader.dll";

        private const string GameDllFileName = "Assembly-CSharp.dll";
        private const string BackupFileExt   = ".orig";

        private const string HookType     = "BattleTech.Main";
        private const string HookMethod   = "Start";
        private const string InjectType   = "BattleTechModLoader.BTModLoader";
        private const string InjectMethod = "Init";

        private const string GameVersionType  = "VersionInfo";
        private const string GameVersionConst = "CURRENT_VERSION_NUMBER";

        private static readonly ReceivedOptions opt = new ReceivedOptions();
        private static readonly OptionSet Options   = new OptionSet()
        {
            { "d|detect",
                "Detect if the BTG assembly is already injected",
                v => opt.detecting = v != null },
            { "g|gameversion",
                "Print the BTG version number",
                v => opt.gameVersion = v != null },
            { "h|?|help",
                "Print this useful help message",
                v => opt.helping = v != null },
            { "i|install",
                "Install the Mod (this is the default behavior)",
                v => opt.installing = v != null },
            { "manageddir=",
                "specify managed dir where BTG's Assembly-CSharp.dll is located",
                v => opt.managedDir = v},
            { "y|nokeypress",
                "Anwser prompts affirmatively",
                v => opt.requireKeyPress = v == null },
            { "reqmismatchmsg=",
                "Print msg if required version check fails",
                v => opt.requiredGameVersionMismatchMessage = v },
            { "requiredversion=",
                "Don't continue with /install, /update, etc. if the BTG game version does not match given argument",
                v => opt.requiredGameVersion = v },
            { "r|restore",
                "Restore pristine backup BTG assembly to folder",
                v => opt.restoring = v != null },
            { "u|update",
                "Update mod loader injection of BTG assembly to current BTML version",
                v => opt.updating = v != null },
            { "v|version",
                "Print the BattleTechModInjector version number",
                v => opt.versioning = v != null },
        };

        private static int Main(string[] args)
        {
            try
            {
                try
                {
                    ParseOptions(args);
                }
                catch (OptionException e)
                {
                    SayOptionException(e);
                    return RC_BadOptions;
                }

                if (opt.helping)
                {
                    SayHelp(Options);
                    return RC_Normal;
                }

                if (opt.versioning)
                {
                    SayVersion();
                    return RC_Normal;
                }

                var directory = Directory.GetCurrentDirectory();
                FileInfo givenManagedDir;
                if (!string.IsNullOrEmpty(opt.managedDir))
                {
                    givenManagedDir = new FileInfo(opt.managedDir);
                    if (!givenManagedDir.Exists && !givenManagedDir.Directory.Exists)
                    {
                        SayManagedDirMissingError(opt.managedDir);
                        return RC_BadManagedDirectoryProvided;
                    }
                    directory = givenManagedDir.Directory.FullName;
                }

                var gameDllPath = Path.Combine(directory, GameDllFileName);
                var gameDllBackupPath = Path.Combine(directory, GameDllFileName + BackupFileExt);
                var modLoaderDllPath = Path.Combine(directory, ModLoaderDllFileName);
                if (!new FileInfo(gameDllPath).Exists)
                {
                    SayGameAssemblyMissingError(opt.managedDir);
                    return RC_BadManagedDirectoryProvided;
                }
                if (!new FileInfo(modLoaderDllPath).Exists)
                {
                    SayModLoaderAssemblyMissingError(modLoaderDllPath);
                    return RC_MissingModLoaderAssembly;
                }

                bool isCurrentInjection;
                string version;
                var injected = IsInjected(gameDllPath, out isCurrentInjection, out version);

                if (opt.gameVersion)
                {
                    SayGameVersion(version);
                    return RC_Normal;
                }
                if (!string.IsNullOrEmpty(opt.requiredGameVersion))
                {
                    if (opt.requiredGameVersion != version)
                    {
                        SayRequiredGameVersion(version, opt.requiredGameVersion);
                        SayRequiredGameVersionMismatchMessage(opt.requiredGameVersionMismatchMessage);
                        PromptForKey(opt.requireKeyPress);
                        return RC_RequiredGameVersionMismatch;
                    }
                }

                if (opt.detecting)
                {
                    SayInjectedStatus(injected);
                    return RC_Normal;
                }

                SayHeader();



                if (opt.restoring)
                {
                    if (injected)
                    {
                        Restore(gameDllPath, gameDllBackupPath);
                    }
                    else
                    {
                        SayAlreadyRestored();
                    }
                    PromptForKey(opt.requireKeyPress);
                    return RC_Normal;
                }

                if (opt.updating)
                {
                    if (injected)
                    {
                        var yes = PromptForUpdateYesNo(opt.requireKeyPress);
                        if (yes)
                        {
                            Restore(gameDllPath, gameDllBackupPath);
                            Inject(gameDllPath, modLoaderDllPath);
                        }
                        else
                        {
                            SayUpdateCanceled();
                        }
                    }

                    PromptForKey(opt.requireKeyPress);
                    return RC_Normal;
                }

                if (opt.installing)
                {
                    if (!injected)
                    {
                        Backup(gameDllPath, gameDllBackupPath);
                        Inject(gameDllPath, modLoaderDllPath);
                    }
                    else
                    {
                        SayAlreadyInjected(isCurrentInjection);
                    }
                    PromptForKey(opt.requireKeyPress);
                    return RC_Normal;
                }
            }
            catch (BackupFileNotFound e)
            {
                SayException(e);
                SayHowToRecoverMissingBackup(e.BackupFileName);
                return RC_MissingBackupFile;
            }
            catch (BackupFileInjected e)
            {
                SayException(e);
                SayHowToRecoverInjectedBackup(e.BackupFileName);
                return RC_BackupFileInjected;
            }
            catch (Exception e)
            {
                SayException(e);
            }

            return RC_UnhandledState;
        }

        private static void ParseOptions(string[] cmdargs)
        {

            Options.Parse(cmdargs);
        }

        private static void SayInjectedStatus(bool injected)
        {
            WriteLine(injected.ToString().ToLower());
        }

        private static void Backup(string filePath, string backupFilePath)
        {
            File.Copy(filePath, backupFilePath, true);
            WriteLine($"{Path.GetFileName(filePath)} backed up to {Path.GetFileName(backupFilePath)}");
        }

        private static void Restore(string filePath, string backupFilePath)
        {
            if (!File.Exists(backupFilePath))
            {
                throw new BackupFileNotFound();
            }
            if (IsInjected(backupFilePath))
            {
                throw new BackupFileInjected();
            }
            File.Copy(backupFilePath, filePath, true);
            WriteLine($"{Path.GetFileName(backupFilePath)} restored to {Path.GetFileName(filePath)}");
        }

        private static void Inject(string hookFilePath, string injectFilePath)
        {
            string oldDirectory = Directory.GetCurrentDirectory();
            string newDirectory = Path.GetDirectoryName(hookFilePath);
            Directory.SetCurrentDirectory(newDirectory);
            WriteLine($"Injecting {Path.GetFileName(hookFilePath)} with {InjectType}.{InjectMethod} at {HookType}.{HookMethod}");
            var success = true;
            using (var game = ModuleDefinition.ReadModule(hookFilePath, new ReaderParameters { ReadWrite = true }))
            using (var injecting = ModuleDefinition.ReadModule(injectFilePath))
            {
                success = InjectModHookPoint(game, injecting);
#if RTML
                if (success) success = InjectNewFactions(game);
#endif

                if (success) success = WriteNewAssembly(hookFilePath, game);
            }
            if (!success)
            {
                WriteLine("Failed to inject the game assembly.");
            }
            Directory.SetCurrentDirectory(oldDirectory);
        }

        private static bool WriteNewAssembly(string hookFilePath, ModuleDefinition game)
        {
            // save the modified assembly
            WriteLine($"Writing back to {Path.GetFileName(hookFilePath)}...");
            game.Write();
            WriteLine("Injection complete!");
            return true;
        }

        private static bool InjectModHookPoint(ModuleDefinition game, ModuleDefinition injecting)
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

            if (targetInstruction == -1) return false;
            hookedMethod.Body.GetILProcessor().InsertAfter(hookedMethod.Body.Instructions[targetInstruction],
                Instruction.Create(OpCodes.Call, game.ImportReference(injectedMethod)));
            return true;
        }

#if RTML
        private const string FactionsFileName = "rt-factions.zip";
        private const int EnumStartingId = 5000;

        private static bool InjectNewFactions(ModuleDefinition game)
        {
            Write("Injecting factions... ");
            var enumAttributes =
                Mono.Cecil.FieldAttributes.Public |
                Mono.Cecil.FieldAttributes.Static |
                Mono.Cecil.FieldAttributes.Literal |
                Mono.Cecil.FieldAttributes.HasDefault;
            var factions = ReadFactions();
            var factionBase = game.GetType("BattleTech.Faction");
            foreach (var faction in factions)
            {
                var newField = new FieldDefinition(faction.Name, enumAttributes, factionBase) { Constant = faction.Id };
                factionBase.Fields.Add(newField);
            }
            WriteLine($"Injected {factions.Count} factions.");
            return true;
        }

        private static List<FactionStub> ReadFactions()
        {
            var directory = Directory.GetCurrentDirectory();
            var factionPath = Path.Combine(directory, FactionsFileName);
            var factionDefinition = new { Faction = "" };
            var factions = new List<FactionStub>();
            var id = EnumStartingId;
            using (var archive = ZipFile.Read(factionPath))
            {
                foreach (var entry in archive)
                {
                    if (entry.FileName.StartsWith("faction_", StringComparison.OrdinalIgnoreCase) &&
                        entry.FileName.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                    {
                        using (var reader = new StreamReader(entry.OpenReader()))
                        {
                            var raw = reader.ReadToEnd();
                            var faction = JsonConvert.DeserializeAnonymousType(raw, factionDefinition);
                            factions.Add(new FactionStub() { Name = faction.Faction, Id = id });
                            id++;
                        }
                    }
                }
            }

            return factions;
        }

        private struct FactionStub
        {
            public string Name;
            public int Id;
        }
#endif

        private static bool IsInjected(string dllPath)
        {
            return IsInjected(dllPath, out _, out _);
        }
        private static bool IsInjected(string dllPath, out bool isCurrentInjection, out string gameVersion)
        {
            isCurrentInjection = false;
            gameVersion = "";
            var detectedInject = false;
            using (var dll = ModuleDefinition.ReadModule(dllPath))
            {
                foreach (TypeDefinition type in dll.Types)
                {
                    // Standard methods
                    foreach (var methodDefinition in type.Methods)
                    {
                        if (IsHookInstalled(methodDefinition, out isCurrentInjection))
                        {
                            detectedInject = true;
                        }
                    }

                    // Also have to check in places like IEnumerator generated methods (Nested)
                    foreach (var nestedType in type.NestedTypes)
                    {
                        foreach (var methodDefinition in nestedType.Methods)
                        {
                            if (IsHookInstalled(methodDefinition, out isCurrentInjection))
                            {
                                detectedInject = true;
                            }
                        }
                    }
                    if (type.FullName == GameVersionType)
                    {
                        var fieldInfo = type.Fields.First(x => x.IsLiteral && !x.IsInitOnly && x.Name == GameVersionConst);
                        if (null != fieldInfo) gameVersion = fieldInfo.Constant.ToString();
                    }
                    if (detectedInject && !string.IsNullOrEmpty(gameVersion)) return detectedInject;
                }
            }
            return detectedInject;
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
            SayHeader();
            WriteLine("Usage: BattleTechModLoaderInjector.exe [OPTIONS]+");
            WriteLine("Inject the BattleTech game assembly with an entry point for mod enablement.");
            WriteLine("If no options are specified, the program assumes you want to /install.");
            WriteLine();
            WriteLine("Options:");
            p.WriteOptionDescriptions(Out);
        }

        private static void SayGameVersion(string version)
        {
            WriteLine(version);
        }
        private static void SayRequiredGameVersion(string version, string expectedVersion)
        {
            WriteLine($"Expected BTG v{expectedVersion}");
            WriteLine($"Actual BTG v{version}");
        }
        private static void SayRequiredGameVersionMismatchMessage(string msg)
        {
            if (!string.IsNullOrEmpty(msg))
                WriteLine(msg);
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

        private static void SayManagedDirMissingError(string givenManagedDir)
        {
            SayHeader();
            WriteLine($"ERROR: We could not find the directory '{givenManagedDir}'. Are you sure it exists?");
        }

        private static void SayGameAssemblyMissingError(string givenManagedDir)
        {
            SayHeader();
            WriteLine($"ERROR: We could not find the BTG assembly {GameDllFileName} in directory '{givenManagedDir}'.\n" +
                      "Are you sure that is the correct directory?");
        }

        private static void SayModLoaderAssemblyMissingError(string expectedModLoaderAssemblyPath)
        {
            SayHeader();
            WriteLine($"ERROR: We could not find the BTG assembly {ModLoaderDllFileName} at '{expectedModLoaderAssemblyPath}'.\n" +
                      $"Is {ModLoaderDllFileName} in the correct place? It should be in the same directory as this injector executable.");
        }

        private static void SayHeader()
        {
            WriteLine("BattleTechModLoader Injector");
            WriteLine("----------------------------");
        }

        private static void SayHowToRecoverMissingBackup(string backupFileName)
        {
            WriteLine("----------------------------");
            WriteLine($"The backup game assembly file must be in the directory with the injector for /restore to work. The backup file should be named \"{backupFileName}\".");
            WriteLine("You may need to reinstall or use Steam/GOG's file verification function if you have no other backup.");
        }

        private static void SayHowToRecoverInjectedBackup(string backupFileName)
        {
            WriteLine("----------------------------");
            WriteLine($"The backup game assembly file named \"{backupFileName}\" was already BTML injected. Something has gone wrong.");
            WriteLine("You may need to reinstall or use Steam/GOG's file verification function if you have no other backup.");
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

    public class BackupFileInjected : Exception
    {
        private string backupFileName;
        public string BackupFileName => backupFileName;

        public BackupFileInjected(string backupFileName = "Assembly-CSharp.dll.orig") :
            base(FormulateMessage(backupFileName))
        {
            this.backupFileName = backupFileName;
        }

        private static string FormulateMessage(string backupFileName)
        {
            return $"The backup file \"{backupFileName}\" was BTML-injected.";
        }
    }

    public class BackupFileNotFound : FileNotFoundException
    {
        private string backupFileName;
        public string BackupFileName => backupFileName;

        public BackupFileNotFound(string backupFileName = "Assembly-CSharp.dll.orig") :
            base(FormulateMessage(backupFileName))
        {
            this.backupFileName = backupFileName;
        }

        private static string FormulateMessage(string backupFileName)
        {
            return $"The backup file \"{backupFileName}\" could not be found.";
        }
    }
}

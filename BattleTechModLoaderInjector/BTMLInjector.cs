using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Mono.Options;
using static System.Console;
#if RTML
using System.Collections.Generic;
using Ionic.Zip;
using Newtonsoft.Json;
using FieldAttributes = Mono.Cecil.FieldAttributes;
#endif

namespace BattleTechModLoaderInjector
{
    internal static class BTMLInjector
    {
        // return Codes
        private const int RC_NORMAL = 0;
        private const int RC_UNHANDLED_STATE = 1;
        private const int RC_BAD_OPTIONS = 2;
        private const int RC_MISSING_BACKUP_FILE = 3;
        private const int RC_BACKUP_FILE_INJECTED = 4;
        private const int RC_BAD_MANAGED_DIRECTORY_PROVIDED = 5;
        private const int RC_MISSING_MOD_LOADER_ASSEMBLY = 6;
        private const int RC_REQUIRED_GAME_VERSION_MISMATCH = 7;

        private const string MOD_LOADER_DLL_FILE_NAME = "BattleTechModLoader.dll";
        private const string GAME_DLL_FILE_NAME = "Assembly-CSharp.dll";
        private const string BACKUP_FILE_EXT = ".orig";

        private const string HOOK_TYPE = "BattleTech.Main";
        private const string HOOK_METHOD = "Start";
        private const string INJECT_TYPE = "BattleTechModLoader.BTModLoader";
        private const string INJECT_METHOD = "Init";

        private const string GAME_VERSION_TYPE = "VersionInfo";
        private const string GAME_VERSION_CONST = "CURRENT_VERSION_NUMBER";

        // ReSharper disable once InconsistentNaming
        private static readonly ReceivedOptions OptionsIn = new ReceivedOptions();

        // ReSharper disable once InconsistentNaming
        private static readonly OptionSet Options = new OptionSet
        {
            {
                "d|detect",
                "Detect if the BTG assembly is already injected",
                v => OptionsIn.Detecting = v != null
            },
            {
                "g|gameversion",
                "Print the BTG version number",
                v => OptionsIn.GameVersion = v != null
            },
            {
                "h|?|help",
                "Print this useful help message",
                v => OptionsIn.Helping = v != null
            },
            {
                "i|install",
                "Install the Mod (this is the default behavior)",
                v => OptionsIn.Installing = v != null
            },
            {
                "manageddir=",
                "specify managed dir where BTG's Assembly-CSharp.dll is located",
                v => OptionsIn.ManagedDir = v
            },
            {
                "y|nokeypress",
                "Anwser prompts affirmatively",
                v => OptionsIn.RequireKeyPress = v == null
            },
            {
                "reqmismatchmsg=",
                "Print msg if required version check fails",
                v => OptionsIn.RequiredGameVersionMismatchMessage = v
            },
            {
                "requiredversion=",
                "Don't continue with /install, /update, etc. if the BTG game version does not match given argument",
                v => OptionsIn.RequiredGameVersion = v
            },
            {
                "r|restore",
                "Restore pristine backup BTG assembly to folder",
                v => OptionsIn.Restoring = v != null
            },
            {
                "u|update",
                "Update mod loader injection of BTG assembly to current BTML version",
                v => OptionsIn.Updating = v != null
            },
            {
                "v|version",
                "Print the BattleTechModInjector version number",
                v => OptionsIn.Versioning = v != null
            }
        };

        private static int Main(string[] args)
        {
            try
            {
                try
                {
                    Options.Parse(args);
                }
                catch (OptionException e)
                {
                    SayOptionException(e);
                    return RC_BAD_OPTIONS;
                }

                if (OptionsIn.Helping)
                {
                    SayHelp(Options);
                    return RC_NORMAL;
                }

                if (OptionsIn.Versioning)
                {
                    SayVersion();
                    return RC_NORMAL;
                }

                var directory = Directory.GetCurrentDirectory();
                if (!string.IsNullOrEmpty(OptionsIn.ManagedDir))
                {
                    if (!Directory.Exists(OptionsIn.ManagedDir))
                    {
                        SayManagedDirMissingError(OptionsIn.ManagedDir);
                        return RC_BAD_MANAGED_DIRECTORY_PROVIDED;
                    }

                    directory = Path.GetFullPath(OptionsIn.ManagedDir);
                }

                var gameDllPath = Path.Combine(directory, GAME_DLL_FILE_NAME);
                var gameDllBackupPath = Path.Combine(directory, GAME_DLL_FILE_NAME + BACKUP_FILE_EXT);
                var modLoaderDllPath = Path.Combine(directory, MOD_LOADER_DLL_FILE_NAME);

                if (!File.Exists(gameDllPath))
                {
                    SayGameAssemblyMissingError(OptionsIn.ManagedDir);
                    return RC_BAD_MANAGED_DIRECTORY_PROVIDED;
                }

                if (!File.Exists(modLoaderDllPath))
                {
                    SayModLoaderAssemblyMissingError(modLoaderDllPath);
                    return RC_MISSING_MOD_LOADER_ASSEMBLY;
                }

                var injected = IsInjected(gameDllPath, out var isCurrentInjection, out var gameVersion);

                if (OptionsIn.GameVersion)
                {
                    SayGameVersion(gameVersion);
                    return RC_NORMAL;
                }

                if (!string.IsNullOrEmpty(OptionsIn.RequiredGameVersion)
                    && OptionsIn.RequiredGameVersion != gameVersion)
                {
                    SayRequiredGameVersion(gameVersion, OptionsIn.RequiredGameVersion);
                    SayRequiredGameVersionMismatchMessage(OptionsIn.RequiredGameVersionMismatchMessage);
                    PromptForKey(OptionsIn.RequireKeyPress);
                    return RC_REQUIRED_GAME_VERSION_MISMATCH;
                }

                if (OptionsIn.Detecting)
                {
                    SayInjectedStatus(injected);
                    return RC_NORMAL;
                }

                SayHeader();

                if (OptionsIn.Restoring)
                {
                    if (injected)
                        Restore(gameDllPath, gameDllBackupPath);
                    else
                        SayAlreadyRestored();

                    PromptForKey(OptionsIn.RequireKeyPress);
                    return RC_NORMAL;
                }

                if (OptionsIn.Updating)
                {
                    if (injected)
                    {
                        if (PromptForUpdateYesNo(OptionsIn.RequireKeyPress))
                        {
                            Restore(gameDllPath, gameDllBackupPath);
                            Inject(gameDllPath, modLoaderDllPath);
                        }
                        else
                        {
                            SayUpdateCanceled();
                        }
                    }

                    PromptForKey(OptionsIn.RequireKeyPress);
                    return RC_NORMAL;
                }

                if (OptionsIn.Installing)
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

                    PromptForKey(OptionsIn.RequireKeyPress);
                    return RC_NORMAL;
                }
            }
            catch (BackupFileNotFound e)
            {
                SayException(e);
                SayHowToRecoverMissingBackup(e.BackupFileName);
                return RC_MISSING_BACKUP_FILE;
            }
            catch (BackupFileInjected e)
            {
                SayException(e);
                SayHowToRecoverInjectedBackup(e.BackupFileName);
                return RC_BACKUP_FILE_INJECTED;
            }
            catch (Exception e)
            {
                SayException(e);
            }

            return RC_UNHANDLED_STATE;
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
                throw new BackupFileNotFound();

            if (IsInjected(backupFilePath))
                throw new BackupFileInjected();

            File.Copy(backupFilePath, filePath, true);
            WriteLine($"{Path.GetFileName(backupFilePath)} restored to {Path.GetFileName(filePath)}");
        }

        private static void Inject(string hookFilePath, string injectFilePath)
        {
            var oldDirectory = Directory.GetCurrentDirectory();
            var newDirectory = Path.GetDirectoryName(hookFilePath);
            Directory.SetCurrentDirectory(newDirectory ?? throw new InvalidOperationException());

            WriteLine($"Injecting {Path.GetFileName(hookFilePath)} with {INJECT_TYPE}.{INJECT_METHOD} at {HOOK_TYPE}.{HOOK_METHOD}");

            using (var game = ModuleDefinition.ReadModule(hookFilePath, new ReaderParameters { ReadWrite = true }))
            using (var injecting = ModuleDefinition.ReadModule(injectFilePath))
            {
                var success = InjectModHookPoint(game, injecting);
#if RTML
                // TODO: remove RTML #if here
                success |= InjectNewFactions(game);
#endif
                success |= WriteNewAssembly(hookFilePath, game);

                if (!success)
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
            var injectedMethod = injecting.GetType(INJECT_TYPE).Methods.Single(x => x.Name == INJECT_METHOD);
            var hookedMethod = game.GetType(HOOK_TYPE).Methods.First(x => x.Name == HOOK_METHOD);

            // If the return type is an iterator -- need to go searching for its MoveNext method which contains the actual code you'll want to inject
            if (hookedMethod.ReturnType.Name.Equals("IEnumerator"))
            {
                var nestedIterator = game.GetType(HOOK_TYPE).NestedTypes.First(x =>
                    x.Name.Contains(HOOK_METHOD) && x.Name.Contains("Iterator"));
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
            var targetInstruction = -1;
            for (var i = 0; i < hookedMethod.Body.Instructions.Count; i++)
            {
                var instruction = hookedMethod.Body.Instructions[i];
                if (instruction.OpCode.Code.Equals(Code.Call) && instruction.OpCode.OperandType.Equals(OperandType.InlineMethod))
                {
                    var methodReference = (MethodReference) instruction.Operand;
                    if (methodReference.Name.Contains("PrepareSerializer"))
                        targetInstruction = i;
                }
            }

            if (targetInstruction == -1)
                return false;

            hookedMethod.Body.GetILProcessor().InsertAfter(hookedMethod.Body.Instructions[targetInstruction],
                Instruction.Create(OpCodes.Call, game.ImportReference(injectedMethod)));

            return true;
        }

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
                foreach (var type in dll.Types)
                {
                    // Standard methods
                    foreach (var methodDefinition in type.Methods)
                    {
                        if (IsHookInstalled(methodDefinition, out isCurrentInjection))
                            detectedInject = true;
                    }

                    // Also have to check in places like IEnumerator generated methods (Nested)
                    foreach (var nestedType in type.NestedTypes)
                    foreach (var methodDefinition in nestedType.Methods)
                    {
                        if (IsHookInstalled(methodDefinition, out isCurrentInjection))
                            detectedInject = true;
                    }

                    if (type.FullName == GAME_VERSION_TYPE)
                    {
                        var fieldInfo = type.Fields.First(x => x.IsLiteral && !x.IsInitOnly && x.Name == GAME_VERSION_CONST);

                        if (null != fieldInfo)
                            gameVersion = fieldInfo.Constant.ToString();
                    }

                    if (detectedInject && !string.IsNullOrEmpty(gameVersion))
                        return true;
                }
            }

            return detectedInject;
        }

        private static bool IsHookInstalled(MethodDefinition methodDefinition, out bool isCurrentInjection)
        {
            isCurrentInjection = false;

            if (methodDefinition.Body == null)
                return false;

            foreach (var instruction in methodDefinition.Body.Instructions)
            {
                if (instruction.OpCode.Equals(OpCodes.Call) &&
                    instruction.Operand.ToString().Equals($"System.Void {INJECT_TYPE}::{INJECT_METHOD}()"))
                {
                    isCurrentInjection =
                        methodDefinition.FullName.Contains(HOOK_TYPE) &&
                        methodDefinition.FullName.Contains(HOOK_METHOD);
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

        private static string GetProductVersion()
        {
            var assembly = Assembly.GetExecutingAssembly();
            var fvi = FileVersionInfo.GetVersionInfo(assembly.Location);
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
            WriteLine($"ERROR: We could not find the BTG assembly {GAME_DLL_FILE_NAME} in directory '{givenManagedDir}'.\n" +
                "Are you sure that is the correct directory?");
        }

        private static void SayModLoaderAssemblyMissingError(string expectedModLoaderAssemblyPath)
        {
            SayHeader();
            WriteLine($"ERROR: We could not find the BTG assembly {MOD_LOADER_DLL_FILE_NAME} at '{expectedModLoaderAssemblyPath}'.\n" +
                $"Is {MOD_LOADER_DLL_FILE_NAME} in the correct place? It should be in the same directory as this injector executable.");
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

        private static void SayAlreadyInjected(bool isCurrentInjection)
        {
            WriteLine(isCurrentInjection
                ? $"ERROR: {GAME_DLL_FILE_NAME} already injected at {INJECT_TYPE}.{INJECT_METHOD}."
                : $"ERROR: {GAME_DLL_FILE_NAME} already injected with an older BattleTechModLoader Injector.  Please revert the file and re-run injector!");
        }

        private static void SayAlreadyRestored()
        {
            WriteLine($"{GAME_DLL_FILE_NAME} already restored.");
        }

        private static void SayUpdateCanceled()
        {
            WriteLine($"{GAME_DLL_FILE_NAME} update cancelled.");
        }

        private static void SayException(Exception e)
        {
            WriteLine($"ERROR: An exception occured: {e}");
        }

        private static bool PromptForUpdateYesNo(bool requireKeyPress)
        {
            if (!requireKeyPress)
                return true;

            WriteLine("Would you like to update your assembly now? (y/n)");
            return ReadKey().Key == ConsoleKey.Y;
        }

        private static void PromptForKey(bool requireKeyPress)
        {
            if (!requireKeyPress)
                return;

            WriteLine("Press any key to continue.");
            ReadKey();
        }

#if RTML
        private const string FACTIONS_FILE_NAME = "rt-factions.zip";
        private const int ENUM_STARTING_ID = 5000;
        private const FieldAttributes ENUM_ATTRIBUTES = FieldAttributes.Public | FieldAttributes.Static | FieldAttributes.Literal | FieldAttributes.HasDefault;

        private static bool InjectNewFactions(ModuleDefinition game)
        {
            Write("Injecting factions... ");

            var factions = ReadFactions();
            var factionBase = game.GetType("BattleTech.Faction");

            foreach (var faction in factions)
                factionBase.Fields.Add(new FieldDefinition(faction.Name, ENUM_ATTRIBUTES, factionBase) { Constant = faction.Id });

            WriteLine($"Injected {factions.Count} factions.");
            return true;
        }

        private static List<FactionStub> ReadFactions()
        {
            var directory = Directory.GetCurrentDirectory();
            var factionPath = Path.Combine(directory, FACTIONS_FILE_NAME);
            var factionDefinition = new { Faction = "" };
            var factions = new List<FactionStub>();
            var id = ENUM_STARTING_ID;

            using (var archive = ZipFile.Read(factionPath))
            {
                foreach (var entry in archive)
                {
                    if (!entry.FileName.StartsWith("faction_", StringComparison.OrdinalIgnoreCase)
                        || !entry.FileName.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                        continue;

                    using (var reader = new StreamReader(entry.OpenReader()))
                    {
                        var raw = reader.ReadToEnd();
                        var faction = JsonConvert.DeserializeAnonymousType(raw, factionDefinition);
                        factions.Add(new FactionStub { Name = faction.Faction, Id = id });
                        id++;
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
    }

    public class BackupFileInjected : Exception
    {
        public BackupFileInjected(string backupFileName = "Assembly-CSharp.dll.orig") : base(FormulateMessage(backupFileName))
        {
            BackupFileName = backupFileName;
        }

        public string BackupFileName { get; }

        private static string FormulateMessage(string backupFileName)
        {
            return $"The backup file \"{backupFileName}\" was BTML-injected.";
        }
    }

    public class BackupFileNotFound : FileNotFoundException
    {
        public BackupFileNotFound(string backupFileName = "Assembly-CSharp.dll.orig") : base(FormulateMessage(backupFileName))
        {
            BackupFileName = backupFileName;
        }

        public string BackupFileName { get; }

        private static string FormulateMessage(string backupFileName)
        {
            return $"The backup file \"{backupFileName}\" could not be found.";
        }
    }
}

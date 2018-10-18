using System;
using System.IO;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Mono.Options;

namespace BattleTechModLoader
{
#if RTML
    using System.IO.Compression;
    using System.Collections.Generic;
    using Newtonsoft.Json;
#endif
    using System.Diagnostics;
    using System.Reflection;


    using static Console;

    internal static class BTMLInjector
    {
        // Return Codes, Codified
        private const int RC_Normal = 0;
        private const int RC_UnhandledState = 1;
        private const int RC_BadOptions = 2;
        private const int RC_MissingBackupFile = 3;
        private const int RC_BackupFileInjected = 4;
        private const int RC_RequiredGameVersionMismatch = 5;
        // /Return Codes

        private const string InjectedDllFileName = "BattleTechModLoader.dll";

        private const string GameDllFileName = "Assembly-CSharp.dll";
        private const string BackupFileExt   = ".orig";

        private const string HookType     = "BattleTech.Main";
        private const string HookMethod   = "Start";
        private const string InjectType   = "BattleTechModLoader.BTModLoader";
        private const string InjectMethod = "Init";

        private const string GameVersionType  = "VersionInfo";
        private const string GameVersionConst = "CURRENT_VERSION_NUMBER";

        private static int Main(string[] args)
        {
            var returnCode = RC_Normal;
            var requireKeyPress = true;
            var detecting = false;
            var requiredGameVersion = String.Empty;
            var requiredGameVersionMismatchMessage = String.Empty;
            var gameVersion = false;
            var helping = false;
            var installing = true;
            var restoring = false;
            var updating = false;
            var versioning = false;

            try
            {
                var options = new OptionSet()
                {
                    { "d|detect",
                        "Detect if the BTG assembly is already injected",
                        v => detecting = v != null },
                    { "g|gameversion",
                        "Print the BTG version number",
                        v => gameVersion = v != null },
                    { "h|?|help",
                        "Print this useful help message",
                        v => helping = v != null },
                    { "i|install",
                        "Install the Mod (this is the default behavior)",
                        v => installing = v != null },
                    { "y|nokeypress",
                        "Anwser prompts affirmatively",
                        v => requireKeyPress = v == null },
                    { "r|restore",
                        "Restore pristine backup BTG assembly to folder",
                        v => restoring = v != null },
                    { "u|update",
                        "Update injected BTG assembly to current version",
                        v => updating = v != null },
                    { "v|version",
                        "Print the BattleTechModInjector version number",
                        v => versioning = v != null },
                    { "requiredversion=", 
                        "Don't continue with /install, /update, etc. if the BTG game version does not match given argument",
                        v => requiredGameVersion = v },
                    { "reqmismatchmsg=",
                        "Print msg if required version check fails",
                        v => requiredGameVersionMismatchMessage = v },
                };

                try
                {
                    options.Parse(args);
                }
                catch (OptionException e)
                {
                    SayOptionException(e);
                    returnCode = RC_BadOptions;
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
                string version;
                var injected = IsInjected(gameDllPath, out isCurrentInjection, out version);

                if (gameVersion)
                {
                    SayGameVersion(version);
                    return returnCode;
                }
                if (!string.IsNullOrEmpty(requiredGameVersion))
                {
                    if (requiredGameVersion != version)
                    {
                        SayRequiredGameVersion(version, requiredGameVersion);
                        returnCode = RC_RequiredGameVersionMismatch;
                        SayRequiredGameVersionMismatchMessage(requiredGameVersionMismatchMessage);
                        PromptForKey(requireKeyPress);
                        return returnCode;
                    }
                }

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

                if (updating)
                {
                    if (injected)
                    {
                        var yes = PromptForUpdateYesNo(requireKeyPress);
                        if (yes)
                        {
                            Restore(gameDllPath, gameDllBackupPath);
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
            catch (BackupFileNotFound e)
            {
                SayException(e);
                SayHowToRecoverMissingBackup(e.BackupFileName);
                returnCode = RC_MissingBackupFile;
                return returnCode;
            }
            catch (BackupFileInjected e)
            {
                SayException(e);
                SayHowToRecoverInjectedBackup(e.BackupFileName);
                returnCode = RC_BackupFileInjected;
                return returnCode;
            }
            catch (Exception e)
            {
                SayException(e);
            }

            returnCode = RC_UnhandledState;
            return returnCode;
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
            if (!File.Exists(backupFilePath))
            {
                throw new BackupFileNotFound();
            }
            if (IsInjected(backupFilePath)) {
                throw new BackupFileInjected();
            }
            File.Copy(backupFilePath, filePath, true);
            WriteLine($"{Path.GetFileName(backupFilePath)} restored to {Path.GetFileName(filePath)}");
        }

        private static void Inject(string hookFilePath, string injectFilePath)
        {
            WriteLine($"Injecting {Path.GetFileName(hookFilePath)} with {InjectType}.{InjectMethod} at {HookType}.{HookMethod}");
            var success = true;
            using (var game = ModuleDefinition.ReadModule(hookFilePath, new ReaderParameters { ReadWrite = true }))
            using (var injecting = ModuleDefinition.ReadModule(injectFilePath))
            {
                success = InjectModHookPoint(game, injecting);
#if RTML
                if (success) success = InjectNewFactions(game, injecting);
#endif
                if (success) success = WriteNewAssembly(hookFilePath, game);
            }
            if (!success)
            {
                WriteLine("Failed to inject the game assembly.");
            }
        }

        private static bool WriteNewAssembly(string hookFilePath, ModuleDefinition game)
        {
            // save the modified assembly
            WriteLine($"Writing back to {game.FileName}...");
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

        private static bool InjectNewFactions(ModuleDefinition game, ModuleDefinition injecting)
        {
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
            return true;
        }

        private static List<FactionStub> ReadFactions()
        {
            var directory = Directory.GetCurrentDirectory();
            var factionPath = Path.Combine(directory, FactionsFileName);
            var factionDefinition = new { Faction = "" };
            var factions = new List<FactionStub>();
            var id = EnumStartingId;
            Write("Injecting factions... ");
            using (ZipArchive archive = ZipFile.OpenRead(factionPath))
            {
                foreach (ZipArchiveEntry entry in archive.Entries)
                {
                    if (entry.FullName.StartsWith("faction_", StringComparison.OrdinalIgnoreCase) &&
                        entry.FullName.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                    {
                        using (StreamReader reader = new StreamReader(entry.Open()))
                        {
                            var raw = reader.ReadToEnd();
                            var faction = JsonConvert.DeserializeAnonymousType(raw, factionDefinition);
                            factions.Add(new FactionStub() { Name = faction.Faction, Id = id });
                            id++;
                        }
                    }
                }
            }
            WriteLine($"Injected {factions.Count} factions.");
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

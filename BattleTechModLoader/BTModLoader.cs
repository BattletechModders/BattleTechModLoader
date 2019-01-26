using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using BattleTech;
using Harmony;

// ReSharper disable UnusedMember.Global
// ReSharper disable MemberCanBePrivate.Global

namespace BattleTechModLoader
{
    using static Logger;

    public static class BTModLoader
    {
        private const BindingFlags PUBLIC_STATIC_BINDING_FLAGS = BindingFlags.Public | BindingFlags.Static;
        private static readonly List<string> IGNORE_FILE_NAMES = new List<string>()
        {
            "0Harmony.dll",
            "BattleTechModLoader.dll"
        };

        public static string ModDirectory { get; private set; }

        public static Assembly LoadDLL(string path, string methodName = "Init", string typeName = null,
            object[] parameters = null, BindingFlags bFlags = PUBLIC_STATIC_BINDING_FLAGS)
        {
            var fileName = Path.GetFileName(path);

            if (!File.Exists(path))
            {
                LogWithDate($"Failed to load {fileName} at path {path}, because it doesn't exist at that path.");
                return null;
            }

            try
            {
                var assembly = Assembly.LoadFrom(path);
                var name = assembly.GetName();
                var version = name.Version;
                var types = new List<Type>();

                // if methodName is null, don't try to run an entry point
                if (methodName == null)
                    return assembly;

                // find the type/s with our entry point/s
                if (typeName == null)
                {
                    types.AddRange(assembly.GetTypes().Where(x => x.GetMethod(methodName, bFlags) != null));
                }
                else
                {
                    types.Add(assembly.GetType(typeName));
                }

                if (types.Count == 0)
                {
                    LogWithDate($"{fileName} (v{version}): Failed to find specified entry point: {typeName ?? "NotSpecified"}.{methodName}");
                    return null;
                }

                // run each entry point
                foreach (var type in types)
                {
                    var entryMethod = type.GetMethod(methodName, bFlags);
                    var methodParams = entryMethod?.GetParameters();

                    if (methodParams == null)
                        continue;

                    if (methodParams.Length == 0)
                    {
                        LogWithDate($"{fileName} (v{version}): Found and called entry point \"{entryMethod}\" in type \"{type.FullName}\"");
                        entryMethod.Invoke(null, null);
                        continue;
                    }

                    // match up the passed in params with the method's params, if they match, call the method
                    if (parameters != null && methodParams.Length == parameters.Length
                        && !methodParams.Where((info, i) => parameters[i]?.GetType() != info.ParameterType).Any())
                    {
                        LogWithDate($"{fileName} (v{version}): Found and called entry point \"{entryMethod}\" in type \"{type.FullName}\"");
                        entryMethod.Invoke(null, parameters);
                        continue;
                    }

                    // failed to call entry method of parameter mismatch
                    // diagnosing problems of this type is pretty hard
                    LogWithDate($"{fileName} (v{version}): Provided params don't match {type.Name}.{entryMethod.Name}");
                    Log("\tPassed in Params:");
                    if (parameters != null)
                    {
                        foreach (var parameter in parameters)
                            Log($"\t\t{parameter.GetType()}");
                    }
                    else
                    {
                        Log("\t\t'parameters' is null");
                    }

                    if (methodParams.Length != 0)
                    {
                        Log("\tMethod Params:");
                        foreach (var prm in methodParams)
                            Log($"\t\t{prm.ParameterType}");
                    }
                }

                return assembly;
            }
            catch (Exception e)
            {
                LogException($"{fileName}: While loading a dll, an exception occured", e);
                return null;
            }
        }

        public static void Init()
        {
            var manifestDirectory = Path.GetDirectoryName(VersionManifestUtilities.MANIFEST_FILEPATH)
                ?? throw new InvalidOperationException("Manifest path is invalid.");

            ModDirectory = Path.GetFullPath(
                Path.Combine(manifestDirectory,
                    Path.Combine(Path.Combine(Path.Combine(
                                 "..", "..") , ".."), "Mods")));

            LogPath = Path.Combine(ModDirectory, "BTModLoader.log");

            var btmlVersion = Assembly.GetExecutingAssembly().GetName().Version;

            if (!Directory.Exists(ModDirectory))
                Directory.CreateDirectory(ModDirectory);

            // create log file, overwriting if it's already there
            using (var logWriter = File.CreateText(LogPath))
            {
                logWriter.WriteLine($"BTModLoader -- BTML v{btmlVersion} -- {DateTime.Now}");
            }

            // ReSharper disable once UnusedVariable
            var harmony = HarmonyInstance.Create("io.github.mpstark.BTModLoader");

            // get all dll paths
            var dllPaths = Directory.GetFiles(ModDirectory).Where(x => Path.GetExtension(x).ToLower() == ".dll").ToArray();

            if (dllPaths.Length == 0)
            {
                Log(@"No .DLLs loaded. DLLs must be placed in the root of the folder \BATTLETECH\Mods\.");
                return;
            }

            // load the DLLs
            foreach (var dllPath in dllPaths)
            {
                if (!IGNORE_FILE_NAMES.Contains(Path.GetFileName(dllPath)))
                    LoadDLL(dllPath);
            }
        }
    }
}
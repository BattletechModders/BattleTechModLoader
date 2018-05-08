using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using BattleTech;
using Harmony;
using JetBrains.Annotations;
using UnityEngine.Assertions;

namespace BattleTechModLoader
{
    using static Logger;

    [UsedImplicitly]
    public static class BTModLoader
    {
        private const BindingFlags PUBLIC_STATIC_BINDING_FLAGS = BindingFlags.Public | BindingFlags.Static;

        [UsedImplicitly]
        public static string ModDirectory { get; private set; }


        [UsedImplicitly]
        public static void LoadDLL(string path, string methodName = "Init", string typeName = null,
            object[] prms = null, BindingFlags bFlags = PUBLIC_STATIC_BINDING_FLAGS)
        {
            var fileName = Path.GetFileName(path);

            try
            {
                var assembly = Assembly.LoadFrom(path);
                var types = new List<Type>();

                // find the type/s with our entry point/s
                if (typeName == null)
                    types.AddRange(assembly.GetTypes().Where(x => x.GetMethod(methodName, bFlags) != null));
                else
                    types.Add(assembly.GetType(typeName));

                if (types.Count == 0)
                {
                    LogWithDate($"{fileName}: Failed to find specified entry point: {typeName ?? "NotSpecified"}.{methodName}");
                    return;
                }

                // run each entry point
                foreach (var type in types)
                {
                    var entryMethod = type.GetMethod(methodName, bFlags);
                    //Assert.IsNotNull(entryMethod, nameof(entryMethod) + " != null");
                    Assert.IsNotNull(entryMethod, nameof(entryMethod) + " != null");
                    var methodParams = entryMethod.GetParameters();

                    if (methodParams.Length == 0)
                    {
                        LogWithDate($"{fileName}: Found and called entry point with void param: {type.Name}.{entryMethod.Name}");
                        entryMethod.Invoke(null, null);
                    }
                    else
                    {
                        // match up the passed in params with the method's params, if they match, call the method
                        if (prms != null && methodParams.Length == prms.Length)
                        {
                            var paramsMatch = true;
                            for (var i = 0; i < methodParams.Length; i++)
                                if (prms[i] != null && prms[i].GetType() != methodParams[i].ParameterType)
                                    paramsMatch = false;

                            if (paramsMatch)
                            {
                                LogWithDate($"{fileName}: Found and called entry point with params: {type.Name}.{entryMethod.Name}");
                                entryMethod.Invoke(null, prms);
                                continue;
                            }
                        }

                        // diagnosing problems of this type (haha it's a pun) is pretty hard
                        LogWithDate($"{fileName}: Provided params don't match {type.Name}.{entryMethod.Name}");
                        Log("\tPassed in Params:");
                        if (prms != null)
                            foreach (var prm in prms)
                                Log($"\t\t{prm.GetType()}");
                        else
                            Log("\t\tprms is null");

                        // TODO: confirm assertion
                        Assert.IsNotNull(methodParams, "methodParams != null");
                        //if (methodParams != null)
                        {
                            Log("\tMethod Params:");

                            foreach (var prm in methodParams)
                                Log($"\t\t{prm.ParameterType}");
                        }
                    }
                }
            }
            catch (Exception e)
            {
                LogWithDate($"{fileName}: While loading a dll, an exception occured:\n{e}");
            }
        }

        [UsedImplicitly]
        public static void Init()
        {
            var manifestDirectory = Path.GetDirectoryName(VersionManifestUtilities.MANIFEST_FILEPATH)
                ?? throw new InvalidOperationException("Manifest path is invalid.");
            ModDirectory = Path.GetFullPath(Path.Combine(manifestDirectory, @"..\..\..\Mods\"));
            LogPath = Path.Combine(ModDirectory, "BTModLoader.log");

            // do some simple benchmarking
            var sw = new Stopwatch();
            sw.Start();

            if (!Directory.Exists(ModDirectory))
                Directory.CreateDirectory(ModDirectory);

            // create log file, overwritting if it's already there
            using (var logWriter = File.CreateText(LogPath))
            {
                logWriter.WriteLine($"BTModLoader -- {DateTime.Now}");
            }

            var harmony = HarmonyInstance.Create("io.github.mpstark.BTModLoader");

            // get all dll paths
            var dllPaths = Directory.GetFiles(ModDirectory).Where(x => Path.GetExtension(x).ToLower() == ".dll")
                .ToArray();

            if (dllPaths.Length == 0)
            {
                Log(@"No .dlls loaded. DLLs must be placed in the root of the folder \BATTLETECH\Mods\.");
                return;
            }

            // load the dlls
            foreach (var dllPath in dllPaths)
            {
                Log($"Found DLL: {Path.GetFileName(dllPath)}");
                LoadDLL(dllPath);
            }

            // do some simple benchmarking
            sw.Stop();
            Log("");
            Log($"Took {sw.Elapsed.TotalSeconds} seconds to load mods");

            // print out harmony summary
            var patchedMethods = harmony.GetPatchedMethods().ToArray();
            if (patchedMethods.Length == 0)
            {
                Log("No Harmony Patches loaded.");
                return;
            }

            Log("");
            Log("Harmony Patched Methods (after mod loader startup):");

            foreach (var method in patchedMethods)
            {
                var info = harmony.IsPatched(method);

                if (info == null) continue;

                Assert.IsNotNull(method.ReflectedType, "method.ReflectedType != null");
                Log($"{method.ReflectedType.FullName}.{method.Name}:");

                // prefixes
                if (info.Prefixes.Count != 0)
                    Log("\tPrefixes:");
                foreach (var patch in info.Prefixes)
                    Log($"\t\t{patch.owner}");

                // transpilers
                if (info.Transpilers.Count != 0)
                    Log("\tTranspilers:");
                foreach (var patch in info.Transpilers)
                    Log($"\t\t{patch.owner}");

                // postfixes
                if (info.Postfixes.Count != 0)
                    Log("\tPostfixes:");
                foreach (var patch in info.Postfixes)
                    Log($"\t\t{patch.owner}");
            }

            Log("");
        }
    }
}
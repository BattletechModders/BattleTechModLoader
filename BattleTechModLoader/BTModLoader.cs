using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using BattleTech;
using System.Reflection;
using Harmony;
using System.Diagnostics;

namespace BattleTechModLoader
{
    public static class BTModLoader
    {
        internal static string modDirectory;
        
        // logging
        internal static string logPath;
        internal static void Log(string message, params object[] formatObjects)
        {
            if (logPath != null && logPath != "")
                using (var logWriter = File.AppendText(logPath))
                    logWriter.WriteLine(message, formatObjects);
        }

        internal static void LogWithDate(string message, params object[] formatObjects)
        {
            if (logPath != null && logPath != "")
                using (var logWriter = File.AppendText(logPath))
                    logWriter.WriteLine(DateTime.Now.ToLongTimeString() + " - " + message, formatObjects);
        }

        public static void LoadDLL(string path, string methodName = "Init", string typeName = null, object[] prms = null, BindingFlags bFlags = BindingFlags.Public | BindingFlags.Static)
        {
            var fileName = Path.GetFileName(path);
            
            try
            {
                Assembly assembly = Assembly.LoadFrom(path);
                List<Type> types = new List<Type>();

                // find the type/s with our entry point/s
                if (typeName == null)
                    types.AddRange(assembly.GetTypes().Where(x => x.GetMethod(methodName, bFlags) != null));
                else
                    types.Add(assembly.GetType(typeName));

                // run each entry point
                if (types.Count == 0)
                {
                    LogWithDate("{0}: Failed to find specified entry point: {1}.{2}", fileName, typeName ?? "NotSpecified", methodName);
                    return;
                }

                foreach (var type in types)
                {
                    var entryMethod = type.GetMethod(methodName, bFlags);
                    var methodParams = entryMethod.GetParameters();

                    if (methodParams.Length == 0)
                    {
                        LogWithDate("{0}: Found and called entry point with void param: {1}.{2}", fileName, type.Name, entryMethod.Name);
                        entryMethod.Invoke(null, null);
                    }
                    else
                    {
                        // match up the passed in params with the method's params, if they match, call the method
                        if (prms != null && methodParams.Length == prms.Length)
                        {
                            bool paramsMatch = true;
                            for (int i = 0; i < methodParams.Length; i++)
                            {
                                if (prms[i] != null && prms[i].GetType() != methodParams[i].ParameterType)
                                {
                                    paramsMatch = false;
                                }
                            }

                            if (paramsMatch)
                            {
                                LogWithDate("{0}: Found and called entry point with params: {1}.{2}", fileName, type.Name, entryMethod.Name);
                                entryMethod.Invoke(null, prms);
                                continue;
                            }
                        }

                        // diagnosing problems of this type (haha it's a pun) is pretty hard
                        LogWithDate("{0}: Provided params don't match {1}.{2}", fileName, type.Name, entryMethod.Name);
                        Log("\tPassed in Params:");
                        if (prms != null)
                        {
                            foreach (var prm in prms)
                                Log("\t\t{0}", prm.GetType());
                        }
                        else
                        {
                            Log("\t\tprms is null");
                        }

                        if (methodParams != null)
                        {
                            Log("\tMethod Params:");

                            foreach (var prm in methodParams)
                                Log("\t\t{0}", prm.ParameterType);
                        }
                    }
                }
            }
            catch (Exception e)
            {
               LogWithDate("{0}: While loading a dll, an exception occured:\n{1}", fileName, e.ToString());
            }
        }

        public static void Init()
        {
            modDirectory = Path.Combine(Path.GetDirectoryName(VersionManifestUtilities.MANIFEST_FILEPATH), @"..\..\..\Mods\");
            logPath = Path.Combine(modDirectory, "BTModLoader.log");

            // do some simple benchmarking
            Stopwatch sw = new Stopwatch();
            sw.Start();
            
            if (!Directory.Exists(modDirectory))
                Directory.CreateDirectory(modDirectory);

            // create log file, overwritting if it's already there
            using (var logWriter = File.CreateText(logPath))
                logWriter.WriteLine("BTModLoader -- {0}", DateTime.Now);

            var harmony = HarmonyInstance.Create("io.github.mpstark.BTModLoader");
            
            // get all dll paths
            var dllPaths = Directory.GetFiles(modDirectory).Where(x => Path.GetExtension(x).ToLower() == ".dll");

            if (dllPaths.Count() == 0)
            {
                Log(@"No .dlls loaded. DLLs must be placed in the root of the folder \BATTLETECH\Mods\.");
                return;
            }

            // load the dlls
            foreach (var dllPath in dllPaths)
            {
                Log("Found DLL: {0}", Path.GetFileName(dllPath));
                LoadDLL(dllPath);
            }
            
            // do some simple benchmarking
            sw.Stop();
            Log("");
            Log("Took {0} seconds to load mods", sw.Elapsed.TotalSeconds);

            // print out harmony summary
            var patchedMethods = harmony.GetPatchedMethods();
            if (patchedMethods.Count() == 0)
            {
                Log("No Harmony Patches loaded.");
                return;
            }

            Log("");
            Log("Harmony Patched Methods (after mod loader startup):");

            foreach (var method in patchedMethods)
            {
                var info = harmony.IsPatched(method);

                if (info != null)
                {
                    Log("{0}.{1}.{2}:", method.ReflectedType.Namespace, method.ReflectedType.Name, method.Name);

                    // prefixes
                    if (info.Prefixes.Count != 0)
                        Log("\tPrefixes:");
                    foreach (var patch in info.Prefixes)
                        Log("\t\t{0}", patch.owner);

                    // transpilers
                    if (info.Transpilers.Count != 0)
                        Log("\tTranspilers:");
                    foreach (var patch in info.Transpilers)
                        Log("\t\t{0}", patch.owner);

                    // postfixes
                    if (info.Postfixes.Count != 0)
                        Log("\tPostfixes:");
                    foreach (var patch in info.Postfixes)
                        Log("\t\t{0}", patch.owner);
                }
            }

            Log("");
        }
    }
}

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
        public static string LogPath;
        public static string ModDirectory;

        public static void LoadDLL(string path, StreamWriter logWriter = null, string methodName = "Init", string typeName = null, object[] prms = null, BindingFlags bFlags = BindingFlags.Public | BindingFlags.Static)
        {
            var fileName = Path.GetFileName(path);
            
            try
            {
                Assembly assembly = Assembly.LoadFrom(path);
                List<Type> types = new List<Type>();

                // find the type/s with our entry point/s
                if (typeName == null)
                {
                    types.AddRange(assembly.GetTypes().Where(x => x.GetMethod(methodName, bFlags) != null));
                }
                else
                {
                    types.Add(assembly.GetType(typeName));
                }

                if (types.Count > 0)
                {
                    // run each entry point
                    foreach (var type in types)
                    {
                        var entryMethod = type.GetMethod(methodName, bFlags);
                        var methodParams = entryMethod.GetParameters();

                        if (methodParams.Length == 0)
                        {
                            logWriter?.WriteLine("{0}: Found and called entry point with void param: {1}.{2}", fileName, type.Name, entryMethod.Name);
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
                                    logWriter?.WriteLine("{0}: Found and called entry point with params: {1}.{2}", fileName, type.Name, entryMethod.Name);
                                    entryMethod.Invoke(null, prms);
                                    continue;
                                }
                            }
                            
                            logWriter?.WriteLine("{0}: Provided params don't match {1}.{2}", fileName, type.Name, entryMethod.Name);
                            logWriter?.WriteLine("\tPassed in Params:");
                            if (prms != null)
                            {
                                foreach (var prm in prms)
                                {
                                    logWriter?.WriteLine("\t\t{0}", prm.GetType());
                                }
                            }
                            else
                            {
                                logWriter?.WriteLine("\t\tprms is null");
                            }
                            if (methodParams != null)
                            {
                                logWriter?.WriteLine("\tMethod Params:");
                                foreach (var prm in methodParams)
                                {
                                    logWriter?.WriteLine("\t\t{0}", prm.ParameterType);
                                }
                            }
                        }
                    }
                }
                else
                {
                    logWriter?.WriteLine("{0}: Failed to find specified entry point: {1}.{2}", fileName, (typeName == null) ? typeName : "NotSpecified", methodName);
                }
            }
            catch (Exception e)
            {
                logWriter?.WriteLine("{0}: While loading a dll, an exception occured: {1}", fileName, e.ToString());
            }
        }

        public static void Init()
        {
            ModDirectory = Path.Combine(Path.GetDirectoryName(VersionManifestUtilities.MANIFEST_FILEPATH), @"..\..\..\Mods\");
            LogPath = Path.Combine(ModDirectory, "BTModLoader.log");

            // do some simple benchmarking
            Stopwatch sw = new Stopwatch();
            sw.Start();
            
            if (!Directory.Exists(ModDirectory))
                Directory.CreateDirectory(ModDirectory);

            var harmony = HarmonyInstance.Create("io.github.mpstark.BTModLoader");
            
            using (var logWriter = File.CreateText(LogPath))
            {
                logWriter.WriteLine("BTModLoader -- {0}", DateTime.Now.ToString());

                var dllPaths = new DirectoryInfo(ModDirectory).GetFiles("*.dll", SearchOption.TopDirectoryOnly);
                if (dllPaths.Length == 0)
                {
                    logWriter.WriteLine(@"No .dlls loaded. DLLs must be placed in the root of the folder \BATTLETECH\Mods\.");
                }
                else
                {
                    foreach (var dllFileInfo in dllPaths)
                    {
                        logWriter.WriteLine("Found DLL: {0}", dllFileInfo.Name);
                        LoadDLL(dllFileInfo.ToString(), logWriter);
                    }
                }
                
                // print out harmony summary
                logWriter.WriteLine("");
                logWriter.WriteLine("Harmony Patched Methods (after mod loader startup):");
                var patchedMethods = harmony.GetPatchedMethods();
                foreach (var method in patchedMethods)
                {
                    var info = harmony.IsPatched(method);

                    if (info != null)
                    {
                        logWriter.WriteLine("{0}.{1}.{2}:", method.ReflectedType.Namespace, method.ReflectedType.Name, method.Name);

                        // prefixes
                        if (info.Prefixes.Count != 0)
                            logWriter.WriteLine("\tPrefixes:");
                        foreach (var patch in info.Prefixes)
                        {
                            logWriter.WriteLine("\t\t{0}", patch.owner);
                        }

                        // transpilers
                        if (info.Transpilers.Count != 0)
                            logWriter.WriteLine("\tTranspilers:");
                        foreach (var patch in info.Transpilers)
                        {
                            logWriter.WriteLine("\t\t{0}", patch.owner);
                        }

                        // postfixes
                        if (info.Postfixes.Count != 0)
                            logWriter.WriteLine("\tPostfixes:");
                        foreach (var patch in info.Postfixes)
                        {
                            logWriter.WriteLine("\t\t{0}", patch.owner);
                        }
                    }
                }

                // do some simple benchmarking
                sw.Stop();
                logWriter.WriteLine();
                logWriter.WriteLine("Took {0} seconds to load mods", sw.Elapsed.TotalSeconds);
            }
        }
    }
}

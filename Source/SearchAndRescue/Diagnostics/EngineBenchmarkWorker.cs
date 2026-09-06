using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Xml;
using HarmonyLib;
using UnityEngine;
using Verse;

namespace SearchAndRescue
{
    // Explicit command-line worker only. Each process has its own savedatafolder.
    [HarmonyPatch(typeof(Root), nameof(Root.Update))]
    internal static class EngineBenchmarkWorker
    {
        private static readonly bool enabled = GenCommandLine.CommandLineArgPassed("sar-bench-worker");
        private static bool loading;
        private static float nextPoll;
        private static string runId;
        private static bool buildAfterLoad;
        private static string saveName;
        private static string loadError;
        private static void OnLoadLog(string message, string stack, LogType type)
        {
            if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert)
                loadError = message + "\n" + stack;
        }
        private static void Postfix()
        {
            if (!enabled || LongEventHandler.AnyEventNowOrWaiting) return;
            Application.runInBackground = true;
            Application.targetFrameRate = EngineBenchmarkDiagnostics.Active ? -1 : 15;
            QualitySettings.vSyncCount = 0;
            string directory = EngineBenchmarkDiagnostics.DirectoryPath;
            try
            {
                if (GenCommandLine.CommandLineArgPassed("sar-bench-template"))
                {
                    if (Current.ProgramState != ProgramState.Playing || Find.CurrentMap == null || loading) return;
                    loading = true;
                    Find.TickManager.CurTimeSpeed = TimeSpeed.Paused;
                    GameDataSaveLoader.SaveGame("SAR_Engine_Template");
                    Application.Quit();
                    return;
                }
                if (loading)
                {
                    if (loadError != null) throw new InvalidDataException("Benchmark load failed: " + loadError);
                    if (Current.ProgramState != ProgramState.Playing || Find.CurrentMap == null) return;
                    Find.TickManager.CurTimeSpeed = TimeSpeed.Paused;
                    if (buildAfterLoad)
                    {
                        buildAfterLoad = false;
                        EngineBenchmarkDiagnostics.Build();
                        GameDataSaveLoader.SaveGame(saveName);
                        GameDataSaveLoader.LoadGame(saveName);
                        return;
                    }
                    loading = false;
                    Application.logMessageReceived -= OnLoadLog;
                    EngineBenchmarkDiagnostics.Begin();
                }
                if (EngineBenchmarkDiagnostics.Active)
                {
                    // Real ticks on the main thread, bounded wall time per frame for responsiveness.
                    var watch = Stopwatch.StartNew();
                    while (EngineBenchmarkDiagnostics.Active && watch.ElapsedMilliseconds < 20)
                        Find.TickManager.DoSingleTick();
                    return;
                }
                if (Time.realtimeSinceStartup < nextPoll) return;
                nextPoll = Time.realtimeSinceStartup + .2f;
                Directory.CreateDirectory(directory);
                if (!File.Exists(Path.Combine(directory, "ready"))) File.WriteAllText(Path.Combine(directory, "ready"), "1");
                string queued = Path.Combine(directory, "queued.xml");
                if (!File.Exists(queued)) return;
                File.Copy(queued, Path.Combine(directory, "request.xml"), true);
                File.Delete(queued);
                EngineBenchmarkRequest request = EngineBenchmarkDiagnostics.ReadRequest();
                runId = request.RunId;
                saveName = request.SaveName;
                buildAfterLoad = !File.Exists(Path.Combine(GenFilePaths.SaveDataFolderPath, "Saves", saveName + ".rws"));
                string load = buildAfterLoad ? "SAR_Engine_Template" : saveName;
                if (!File.Exists(Path.Combine(GenFilePaths.SaveDataFolderPath, "Saves", load + ".rws")))
                    throw new FileNotFoundException("Missing benchmark save/template: " + load);
                ValidateSaveProfile(load);
                loadError = null;
                Application.logMessageReceived += OnLoadLog;
                loading = true;
                GameDataSaveLoader.LoadGame(load);
            }
            catch (Exception e)
            {
                loading = false;
                Application.logMessageReceived -= OnLoadLog;
                EngineBenchmarkDiagnostics.Finish("aborted-worker-error");
                File.WriteAllText(Path.Combine(directory, (runId ?? "worker") + ".error"), e.ToString());
                Log.Error("[SAR engine worker] " + e);
            }
        }

        private static void ValidateSaveProfile(string name)
        {
            using (var reader = XmlReader.Create(Path.Combine(GenFilePaths.SaveDataFolderPath, "Saves", name + ".rws")))
            {
                if (!reader.ReadToFollowing("modIds")) throw new InvalidDataException("Missing save mod metadata.");
                var ids = new XmlDocument();
                ids.LoadXml(reader.ReadOuterXml());
                string[] saved = ids.SelectNodes("modIds/li").Cast<XmlNode>().Select(n => n.InnerText.ToLowerInvariant()).ToArray();
                string[] current = ModsConfig.ActiveModsInLoadOrder.Select(m => m.PackageId.ToLowerInvariant()).ToArray();
                if (!saved.SequenceEqual(current)) throw new InvalidDataException("Benchmark save/config mod list mismatch: " + name);
            }
        }
    }
}

using System;
using System.IO;
using System.Linq;
using HarmonyLib;
using RimWorld;
using Verse;
using UnityEngine;
[StaticConstructorOnStartup]
public static class CoordinationSettingsPreview
{
    static int frames;
    static CoordinationSettingsPreview(){new Harmony("sar.coordination.settings.preview").Patch(AccessTools.Method(typeof(Root_Play),"Update"),postfix:new HarmonyMethod(typeof(CoordinationSettingsPreview),nameof(Update)));}
    static void Update(){
        string root=GenFilePaths.SaveDataFolderPath;
        if(!File.Exists(Path.Combine(root,"preview.txt"))||!File.Exists(Path.Combine(root,"standing-results.txt"))||!File.ReadAllText(Path.Combine(root,"standing-results.txt")).Contains("COMPLETE"))return;
        Find.TickManager.CurTimeSpeed=TimeSpeed.Paused;
        if(frames++==0){
            Mod mod=LoadedModManager.GetMod(AccessTools.TypeByName("SearchAndRescue.SearchAndRescueMod"));
            var page=AccessTools.Field(mod.GetType(),"selectedPage");page.SetValue(mod,Enum.Parse(page.FieldType,"Advanced"));
            foreach(var window in Find.WindowStack.Windows.ToArray())if(window.forcePause)window.Close(false);
            Find.WindowStack.Add(new Dialog_ModSettings(mod));
        }
        if(frames==20)ScreenCapture.CaptureScreenshot(Path.Combine(root,"coordination-settings.png"));
        if(frames==40)Application.Quit();
    }
}

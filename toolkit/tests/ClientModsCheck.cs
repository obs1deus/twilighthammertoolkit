using System;
using System.IO;
using System.Reflection;
using System.Collections;
using System.Collections.Generic;
class ClientModsCheck {
    static Assembly assembly;
    static object Call(string type,string name,params object[] args){try{return assembly.GetType(type).GetMethod(name,BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,args);}catch(TargetInvocationException e){throw e.InnerException;}}
    static void Check(bool ok,string message){if(!ok)throw new Exception(message);}
    static void Main(string[] args){
        assembly=Assembly.LoadFrom(args[0]);string game=Path.Combine(Path.GetTempPath(),"th-mod-switch-test-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(game);
        try{
            byte[] dll=new byte[256];dll[0]=0x4d;dll[1]=0x5a;Buffer.BlockCopy(BitConverter.GetBytes(128),0,dll,60,4);Buffer.BlockCopy(BitConverter.GetBytes(0x4550),0,dll,128,4);Buffer.BlockCopy(BitConverter.GetBytes((ushort)0x14c),0,dll,132,2);Buffer.BlockCopy(BitConverter.GetBytes((ushort)0x2000),0,dll,150,2);
            File.WriteAllBytes(Path.Combine(game,"nampower.dll"),dll);File.WriteAllBytes(Path.Combine(game,"perf_boost.dll"),dll);
            string list=Path.Combine(game,"dlls.txt");File.WriteAllText(list,"# custom comment\r\ncustom.dll\r\nperf_boost.dll\r\n");
            object tweaks=Activator.CreateInstance(assembly.GetType("ClientTweaks"),true);
            var changes=(Dictionary<string,bool>)tweaks.GetType().GetField("ModChanges",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(tweaks);
            changes["Nampower"]=true;changes["PerfBoost"]=false;
            Type package=assembly.GetType("Package");Array empty=Array.CreateInstance(package,0);
            Check((bool)Call("ToolkitInstall","RequiresClosedGame","Skip Sync",empty,tweaks),"Mod switches must require closing the game");
            Call("ToolkitInstall","Run",game,"Skip Sync",empty,tweaks,new Action<string>(s=>{}),new Action(()=>{}));
            string applied=File.ReadAllText(list).ToLowerInvariant();
            Check(applied.Contains("nampower.dll")&&!applied.Contains("perf_boost.dll")&&applied.Contains("custom.dll")&&applied.Contains("# custom comment"),"Loader changes lost custom entries or did not apply");
            Check(File.Exists(Path.Combine(game,"perf_boost.dll")),"Disabled mod was deleted");
            var states=(IDictionary)Call("ClientModSettings","Read",game);object state=states["Nampower"];
            Check((bool)state.GetType().GetProperty("Enabled").GetValue(state,null),"Enabled state incorrect");
            Check(((string)state.GetType().GetProperty("Version").GetValue(state,null)).StartsWith("Build "),"Missing DLL version should expose a build identifier");
            var history=(Array)Call("ToolkitHistory","List",game);object entry=history.GetValue(0);string id=(string)entry.GetType().GetProperty("Id").GetValue(entry,null);
            Call("ToolkitHistory","Restore",game,id,new Action(()=>{}));Check(File.ReadAllText(list).Contains("perf_boost.dll")&&!File.ReadAllText(list).ToLowerInvariant().Contains("nampower.dll"),"Mod toggle rollback failed");
            string stage=Path.Combine(game,"stage");Directory.CreateDirectory(stage);bool rejected=false;
            try{Call("ClientModSettings","Stage",game,stage,new Dictionary<string,bool>{{"Missing",true}});}catch(IOException){rejected=true;}
            Check(rejected,"Unknown DLL selection accepted");
            File.WriteAllBytes(Path.Combine(game,"no1600x1200.dll"),dll);File.WriteAllBytes(Path.Combine(game,"VanillaMultiMonitorFix.dll"),dll);File.AppendAllText(list,"no1600x1200.dll\r\n");
            var displayChanges=new Dictionary<string,bool>{{"VanillaMultiMonitorFix",true}};
            Call("ClientModSettings","NormalizeChanges",game,displayChanges);Check(displayChanges.ContainsKey("No1600x1200")&&!displayChanges["No1600x1200"],"Enabling MultiMonitor should disable No1600");
            bool conflict=false;try{Call("ClientModSettings","NormalizeChanges",game,new Dictionary<string,bool>{{"VanillaMultiMonitorFix",true},{"No1600x1200",true}});}catch(IOException){conflict=true;}Check(conflict,"Both display fixes were accepted");
            Call("ClientModSettings","Stage",game,stage,displayChanges);Call("ClientModSettings","ValidateLoader",game,stage);
            string loader=File.ReadAllText(Path.Combine(stage,"dlls.txt"));Check(loader.Contains("VanillaMultiMonitorFix.dll")&&!loader.Contains("no1600x1200.dll"),"Display fix selection was not staged");
            string outside=Path.Combine(Path.GetTempPath(),"th-mod-stage-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(outside);
            try {
                File.WriteAllBytes(Path.Combine(outside,"Nampower.dll"),dll);
                Call("ClientModSettings","Stage",game,outside,new Dictionary<string,bool>{{"Nampower",true}});
                Check(File.ReadAllText(Path.Combine(outside,"dlls.txt")).Contains("Nampower.dll"),"Newly downloaded mod could not be enabled from staging");
            }finally{Directory.Delete(outside,true);}
            Console.WriteLine("PASS DLL enable/disable, custom loader entries retained, disabled files kept, build identity, game-close requirement, rollback and unknown-mod rejection");
        }finally{Directory.Delete(game,true);}
    }
}

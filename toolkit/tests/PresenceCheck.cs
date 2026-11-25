using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Collections;
using System.Net;
class PresenceCheck {
    static Assembly Assembly;const BindingFlags S=BindingFlags.NonPublic|BindingFlags.Static;const BindingFlags F=BindingFlags.NonPublic|BindingFlags.Instance;
    static object Call(string type,string method,params object[] args){try{return Assembly.GetType(type).GetMethod(method,S).Invoke(null,args);}catch(TargetInvocationException e){throw e.InnerException;}}
    static void Check(bool value,string text){if(!value)throw new Exception(text);}
    static string At(string game,string relative){return Path.Combine(game,relative.Replace('/',Path.DirectorySeparatorChar));}
    static void Write(string path,string value){Directory.CreateDirectory(Path.GetDirectoryName(path));File.WriteAllText(path,value);}
    static Array Packages(params string[] names){var catalog=(Array)Assembly.GetType("ToolkitPackages").GetField("Catalog",S).GetValue(null);var values=catalog.Cast<object>().Where(package=>names.Contains((string)package.GetType().GetField("Name",F).GetValue(package))).ToArray();var result=Array.CreateInstance(Assembly.GetType("Package"),values.Length);for(int i=0;i<values.Length;i++)result.SetValue(values[i],i);return result;}
    static object Tweaks(bool enabled,int flags){var value=Activator.CreateInstance(Assembly.GetType("ClientTweaks"),true);value.GetType().GetField("PresenceChange",F).SetValue(value,true);value.GetType().GetField("PresenceEnabled",F).SetValue(value,enabled);value.GetType().GetField("PresenceFlags",F).SetValue(value,flags);return value;}
    static string Latest(string game){object entry=((IEnumerable)Call("ToolkitHistory","List",game)).Cast<object>().First();return (string)entry.GetType().GetProperty("Id").GetValue(entry,null);}
    static void Run(string game,Array packages,object tweaks){Call("ToolkitInstall","Run",game,"Skip Sync",packages,tweaks,new Action<string>(message=>{}),new Action(()=>{}));}
    static void Main(string[] args){
        ServicePointManager.SecurityProtocol=SecurityProtocolType.Tls12;Assembly=System.Reflection.Assembly.LoadFrom(args[0]);
        string root=Path.Combine(Path.GetTempPath(),"th-presence-test-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
        foreach(string name in new[]{"Interact","Interact Addon","No1600x1200","VanillaMultiMonitorFix"}){
            string stage=At(root,"downloads/"+name);Directory.CreateDirectory(stage);object package=Packages(name).GetValue(0);object release=Call("ToolkitPackages","Resolve",package);Call("ToolkitPackages","Stage",package,release,stage);
            if(name=="Interact Addon")Check(File.Exists(At(stage,"Interface/AddOns/Interact/Interact.toc")),"Interact companion missing");
            else Check(Directory.GetFiles(stage,"*.dll").Length>0,"Plugin DLL missing: "+name);
            if(name=="VanillaMultiMonitorFix")Check(!File.Exists(At(stage,"ShowAllDisplayDevices.exe")),"Unrequested monitor diagnostic was staged");
            Console.WriteLine("PASS source package: "+name);
        }
        string game=At(root,"game");Write(At(game,"dlls.txt"),"# keep comment\ncustom.dll\n");
        var defaults=Activator.CreateInstance(Assembly.GetType("ClientTweaks"),true);Run(game,Packages("Discord Presence"),defaults);
        Check(File.ReadAllText(At(game,"WowPresence/discord_broadcast_flags")).Trim()=="0","Upstream all-fields default leaked into installation");
        Check(File.ReadAllText(At(game,"dlls.txt")).Contains("WowPresence.dll"),"Discord DLL not registered");Check(File.Exists(At(game,"WowPresence.exe")),"Discord companion missing");
        string id=File.ReadAllText(At(game,"WowPresence/discord_application_id"));
        Run(game,Packages(),Tweaks(true,9));Check(File.ReadAllText(At(game,"WowPresence/discord_broadcast_flags")).Trim()=="9","Explicit Name+Class opt-in failed");
        Run(game,Packages(),Tweaks(false,9));Check(!File.ReadAllText(At(game,"dlls.txt")).Split('\n').Any(line=>line.Trim()=="WowPresence.dll"),"Disabled presence still registered");
        Check(File.ReadAllText(At(game,"dlls.txt")).Contains("custom.dll"),"Custom loader entry lost");
        Call("ToolkitHistory","Restore",game,Latest(game),new Action(()=>{}));Check(File.ReadAllText(At(game,"dlls.txt")).Contains("WowPresence.dll"),"Presence rollback failed");
        Check(File.ReadAllText(At(game,"WowPresence/discord_application_id"))==id,"Application identity changed");
        string legacy=At(root,"legacy");Write(At(legacy,".modernization_tool/WowPresence/discord_application_id"),"123456789012345678");Write(At(legacy,".modernization_tool/WowPresence/discord_broadcast_flags"),"127");
        string legacyStage=At(root,"legacy-stage");Directory.CreateDirectory(legacyStage);Call("DiscordPresence","StageSettings",legacy,legacyStage,false,0);
        Check(File.ReadAllText(At(legacyStage,".modernization_tool/WowPresence/discord_broadcast_flags")).Trim()=="0","Legacy runtime directory privacy not enforced");
        Check(File.ReadAllText(At(legacyStage,".modernization_tool/WowPresence/discord_application_id")).Trim()=="123456789012345678","Custom application ID overwritten");
        string monitor=At(root,"monitor");Write(At(monitor,"VMMFix_preferred_monitor.txt"),"keep my monitor preference");
        Run(monitor,Packages("VanillaMultiMonitorFix"),defaults);
        Check(File.ReadAllText(At(monitor,"VMMFix_preferred_monitor.txt"))=="keep my monitor preference","Existing monitor preference overwritten");
        Check(!File.Exists(At(monitor,"ShowAllDisplayDevices.exe")),"Unrequested monitor diagnostic installed");
        Console.WriteLine("PASS presence defaults ON with mask 0, explicit detail opt-in, off switch, rollback, legacy directory and custom application ID preservation");
        if(Path.GetDirectoryName(Path.GetFullPath(root)).Equals(Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar),StringComparison.OrdinalIgnoreCase)&&Path.GetFileName(root).StartsWith("th-presence-test-"))Directory.Delete(root,true);
    }
}

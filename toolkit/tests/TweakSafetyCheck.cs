using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
class TweakSafetyCheck {
    static Assembly Assembly;static Type Type;const BindingFlags F=BindingFlags.Instance|BindingFlags.NonPublic;
    static object New(){object t=Activator.CreateInstance(Type,true);Set(t,"Enabled",true);Set(t,"Render",1500m);Set(t,"Clutter",300m);Set(t,"Nameplates",41m);Set(t,"Camera",100m);Set(t,"FieldOfView",130m);Set(t,"PatchSoundChannels",true);Set(t,"SoundChannels",64);return t;}
    static void Set(object t,string name,object value){Type.GetField(name,F).SetValue(t,value);}
    static object Call(string type,string name,params object[] args){try{return Assembly.GetType(type).GetMethod(name,BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,args);}catch(TargetInvocationException e){throw e.InnerException;}}
    static void Validate(object t){try{Type.GetMethod("ValidateLimits",F).Invoke(t,null);}catch(TargetInvocationException e){throw e.InnerException;}}
    static void Check(bool value,string message){if(!value)throw new Exception(message);}
    static void Main(string[] args){
        Assembly=System.Reflection.Assembly.LoadFrom(args[0]);Type=Assembly.GetType("ClientTweaks");Validate(New());
        foreach(string field in new[]{"Render","Clutter","Nameplates","Camera","FieldOfView","SoundChannels"}){
            object t=New();object bad=field=="SoundChannels"?(object)65:field=="Render"?1501m:field=="Clutter"?301m:field=="Nameplates"?42m:field=="Camera"?101m:131m;
            Set(t,field,bad);bool rejected=false;try{Validate(t);}catch(IOException){rejected=true;}Check(rejected,"Safe cap missing: "+field);
        }
        object advanced=New();Set(advanced,"UnsafeLimits",true);Set(advanced,"Render",3000m);Set(advanced,"Clutter",1000m);Set(advanced,"Nameplates",100m);Set(advanced,"FieldOfView",150m);Set(advanced,"SoundChannels",256);Validate(advanced);
        Set(advanced,"Render",3001m);bool hard=false;try{Validate(advanced);}catch(IOException){hard=true;}Check(hard,"Advanced hard ceiling missing");
        Console.WriteLine("PASS all six safe caps, explicit advanced override, advanced hard ceiling");
        string game=Path.Combine(Path.GetTempPath(),"th-safety-test-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(game);string stage=Path.Combine(game,"stage");Directory.CreateDirectory(stage);
        Check((bool)Call("ClientCache","Enabled",game),"Fresh installs should clear WDB by default");
        byte[] image=new byte[4927488];image[0]=0x4d;image[1]=0x5a;Buffer.BlockCopy(BitConverter.GetBytes(0x110),0,image,0x3c,4);Buffer.BlockCopy(BitConverter.GetBytes(0x4550),0,image,0x110,4);Buffer.BlockCopy(BitConverter.GetBytes((ushort)0x10b),0,image,0x128,2);Buffer.BlockCopy(BitConverter.GetBytes((ushort)0x100),0,image,0x16e,2);image[0x2067de]=1;
        File.WriteAllBytes(Path.Combine(game,"WoW_Modernized.exe"),image);Directory.CreateDirectory(Path.Combine(game,".twilight-toolkit"));File.WriteAllText(Path.Combine(game,".twilight-toolkit","toolkit-client.sha256"),(string)Call("ToolkitPackages","Hash",image));
        Directory.CreateDirectory(Path.Combine(game,"WTF"));File.WriteAllText(Path.Combine(game,"WTF","Config.wtf"),"SET scriptMemory \"288\"\r\nSET unrelated \"keep\"\r\n");
        object tweaks=New();foreach(string field in new[]{"CameraSkipFix","UnlimitedScriptMemory","CrossFactionRes","CustomGlues","DepCompatibility","ClearWdb","AlwaysAutoLoot"})Set(tweaks,field,true);
        try{Type.GetMethod("Stage",F).Invoke(tweaks,new object[]{game,stage});}catch(TargetInvocationException e){throw e.InnerException;}
        byte[] changed=File.ReadAllBytes(Path.Combine(stage,"WoW_Modernized.exe"));
        Check(changed[0x02d326]==0xe9&&changed[0x2067de]==0&&changed[0x2f113a]==0xeb,"Extra patch bytes missing");Check((BitConverter.ToUInt16(changed,0x16e)&0x100)==0,"Client NX flag not changed");
        Check(System.Text.Encoding.ASCII.GetString(changed,0x435d38,3)=="64\0","Audio channels patch incorrect");Check(File.ReadAllText(Path.Combine(stage,"WTF","Config.wtf")).Contains("scriptMemory \"0\""),"Script memory setting missing");
        Check(File.ReadAllBytes(Path.Combine(game,"WoW_Modernized.exe")).SequenceEqual(image),"Original fixture changed during staging");
        Console.WriteLine("PASS camera, resurrection, custom glues, per-client NX, script-memory and audio-channel staging; original untouched");
        foreach(string relative in new[]{"WoW_Modernized.exe","WTF/Config.wtf",".twilight-toolkit/toolkit-client.sha256",".twilight-toolkit/tweak-safety.txt",".twilight-toolkit/tweak-script-memory.txt"})
            File.Copy(Path.Combine(stage,relative),Path.Combine(game,relative),true);
        object saved=Call("ClientTweaks","ReadState",game);
        var values=(Dictionary<string,object>)saved.GetType().GetProperty("values").GetValue(saved,null);
        Check((bool)values["autoLoot"]&&(bool)values["scriptMemory"]&&(bool)values["depCompatibility"]&&(bool)values["crossFactionRes"],"Reload did not read persisted tweak switches");
        Check((decimal)values["fov"]==130&&(decimal)values["render"]==1500&&(int)values["channels"]==64,"Reload did not read persisted tweak values");
        Check(((string[])saved.GetType().GetProperty("locked").GetValue(saved,null)).Contains("cameraSkip"),"Built-in camera fix was not flagged");
        object disable=New();
        Type.GetMethod("SelectChanges",F).Invoke(disable,new object[]{new[]{"autoLoot","scriptMemory","depCompatibility","crossFactionRes"}});
        string second=Path.Combine(game,"disable");Directory.CreateDirectory(second);
        Type.GetMethod("Stage",F).Invoke(disable,new object[]{game,second});
        byte[] disabled=File.ReadAllBytes(Path.Combine(second,"WoW_Modernized.exe"));
        Check(disabled[0x0C1ECF]==0x74&&disabled[0x0C2B25]==0x74&&disabled[0x2067de]==4&&(BitConverter.ToUInt16(disabled,0x16e)&0x100)!=0,"Explicit off did not change client patches");
        Check(BitConverter.ToSingle(disabled,0x40fed8)==1500f&&disabled[0x02d326]==0xe9,"Applying switches touched unrelated settings");
        var savedConfig=File.ReadAllText(Path.Combine(second,"WTF","Config.wtf"));
        Check(savedConfig.Contains("scriptMemory \"288\"")&&savedConfig.Contains("unrelated \"keep\""),"Turning off unlimited memory did not restore the previous cap and preserve other settings");
        Console.WriteLine("PASS client settings reloaded from disk, selective Off restored prior behavior, unrelated tweaks preserved");
        string cache=Path.Combine(game,"WDB","enUS");Directory.CreateDirectory(cache);string cacheFile=Path.Combine(cache,"itemcache.wdb");File.WriteAllText(cacheFile,"cached data");File.WriteAllText(Path.Combine(game,".twilight-toolkit","clear-wdb.txt"),"1");File.WriteAllText(Path.Combine(game,"keep.txt"),"keep");
        Call("ClientCache","BeforeLaunch",game);Check(!File.Exists(cacheFile)&&File.ReadAllText(Path.Combine(game,"keep.txt"))=="keep","Cache clearing touched unrelated files");
        var entries=(Array)Call("ToolkitHistory","List",game);Check(entries.Length==1,"Cache backup history missing");object entry=entries.GetValue(0);string id=(string)entry.GetType().GetProperty("Id").GetValue(entry,null);
        Call("ToolkitHistory","Restore",game,id,new Action(()=>{}));Check(File.ReadAllText(cacheFile)=="cached data","Cache restore failed");
        File.WriteAllText(Path.Combine(game,".twilight-toolkit","clear-wdb.txt"),"0");Call("ClientCache","BeforeLaunch",game);Check(File.Exists(cacheFile),"Disabled cache clearing still ran");Console.WriteLine("PASS cache backup, restore and off switch");
        File.WriteAllBytes(Path.Combine(game,"WoW_Modernized.exe"),new byte[11218944]);bool unsupported=false;try{Call("ClientTweaks","ValidateClient",game);}catch(IOException e){unsupported=e.Message.Contains("patch locations");}Check(unsupported,"Unsupported client should explain the build mismatch");Check(new FileInfo(Path.Combine(game,"WoW_Modernized.exe")).Length==11218944,"Validation modified the unsupported client");
        if(Path.GetDirectoryName(Path.GetFullPath(game)).Equals(Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar),StringComparison.OrdinalIgnoreCase)&&Path.GetFileName(game).StartsWith("th-safety-test-"))Directory.Delete(game,true);
    }
}

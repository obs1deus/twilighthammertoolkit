using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Collections;
using System.Net;
class CurrentClientCheck {
    static Assembly assembly;const BindingFlags F=BindingFlags.NonPublic|BindingFlags.Instance;
    static object Call(string type,string name,params object[] args){try{return assembly.GetType(type).GetMethod(name,BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,args);}catch(TargetInvocationException e){throw e.InnerException;}}
    static void Check(bool ok,string message){if(!ok)throw new Exception(message);}
    static void Main(string[] args){
        ServicePointManager.SecurityProtocol=SecurityProtocolType.Tls12;assembly=Assembly.LoadFrom(args[0]);
        string root=Path.Combine(Path.GetTempPath(),"th-current-client-test-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
        try{
            for(int i=1;i<=2;i++){
                byte[] original=File.ReadAllBytes(args[i]);string hash=(string)Call("ToolkitPackages","Hash",original);
                string game=Path.Combine(root,i.ToString()),stage=Path.Combine(root,i+"-stage");Directory.CreateDirectory(game);Directory.CreateDirectory(stage);
                File.WriteAllBytes(Path.Combine(game,"WoW_Modernized.exe"),original);
                Call("ClientTweaks","ValidateImage",game,original);
                var type=assembly.GetType("ClientTweaks");object tweaks=Activator.CreateInstance(type,true);
                foreach(string field in new[]{"Enabled","LargeAddress","BackgroundSounds","AlwaysAutoLoot","CameraSkipFix","CustomGlues","DepCompatibility","ClearWdb","PatchSoundChannels"})type.GetField(field,F).SetValue(tweaks,true);
                foreach(string field in new[]{"Render","Clutter","Nameplates","Camera","FieldOfView"})type.GetField(field,F).SetValue(tweaks,field=="Render"?1500m:field=="Clutter"?300m:field=="Nameplates"?41m:field=="Camera"?75m:110m);
                try{type.GetMethod("Stage",F).Invoke(tweaks,new object[]{game,stage});}catch(TargetInvocationException e){throw e.InnerException;}
                byte[] output=File.ReadAllBytes(Path.Combine(stage,"WoW_Modernized.exe"));
                Check(output.Length==original.Length,"Executable was truncated");Check(output.Skip(4927488).SequenceEqual(original.Skip(4927488)),"Extended client data changed");
                Check(BitConverter.ToSingle(output,0x40fed8)==1500f&&BitConverter.ToSingle(output,0x40c448)==41f,"Current-client settings were not patched");
                Check(output.Skip(0x253c).Take(7).SequenceEqual(original.Skip(0x253c).Take(7))&&output.Skip(0x1b2115).Take(5).SequenceEqual(original.Skip(0x1b2115).Take(5)),"Existing compatibility patches were changed");
                Call("ClientTweaks","ValidateImage",game,output);Call("ClientMods","PrepareLoginClient",game,stage);
                byte[] unknown=(byte[])output.Clone();unknown[0x10000]^=1;bool rejected=false;try{Call("ClientTweaks","ValidateImage",game,unknown);}catch(IOException){rejected=true;}Check(rejected,"Unknown code modification was accepted");
                Check((string)Call("ToolkitPackages","Hash",File.ReadAllBytes(args[i]))==hash,"Original input file was modified");
                Console.WriteLine("PASS current "+original.Length+"-byte "+(i==1?"official":"user")+" client: settings applied to copy, extended data and compatibility options retained, unknown code rejected, AutoLogin compatible; original untouched");
                if(i==1&&args.Length>3){
                    File.Copy(args[i],Path.Combine(game,"WoW.exe"));object torrent=Call("OctoPatch","DownloadTorrent");
                    var files=(IEnumerable)torrent.GetType().GetField("Files",F).GetValue(torrent);
                    object file=files.Cast<object>().Single(f=>(string)f.GetType().GetField("Path",F).GetValue(f)=="WoW.exe");
                    Check((bool)Call("OctoPatch","MatchesLocal",game,torrent,file),"Official reference does not match the torrent checksums");
                    Console.WriteLine("PASS official reference verified against published torrent pieces");
                }
            }
        }finally{Directory.Delete(root,true);}
    }
}

using System;
using System.IO;
using System.Reflection;
using System.Diagnostics;
class SettingsCheck {
    static Assembly assembly;
    static object Call(string type,string name,params object[] args){try{return assembly.GetType(type).GetMethod(name,BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,args);}catch(TargetInvocationException e){throw e.InnerException;}}
    static void Check(bool value,string message){if(!value)throw new Exception(message);}
    static void Main(string[] args){
        assembly=Assembly.LoadFrom(args[0]);
        AppDomain.CurrentDomain.AssemblyResolve+=(sender,e)=>{string name=new AssemblyName(e.Name).Name+".dll";using(var stream=assembly.GetManifestResourceStream(name)){if(stream==null)return null;using(var bytes=new MemoryStream()){stream.CopyTo(bytes);return Assembly.Load(bytes.ToArray());}}};
        string game=Path.Combine(Path.GetTempPath(),"th-settings-test-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(game);
        try{
            foreach(string name in new[]{"WoW.exe","WoW_Modernized.exe","VanillaFixes.exe"})File.WriteAllText(Path.Combine(game,name),"fixture only");
            string selected=(string)Call("RenderedToolkit","ResolveGameClient",game,"auto");
            Check(Path.GetFileName(selected)=="VanillaFixes.exe","Automatic should retain the mod loader");
            var start=new ProcessStartInfo(selected);Call("ClientMods","ConfigureLaunch",game,selected,start);
            Check(start.Arguments=="\""+Path.Combine(game,"WoW_Modernized.exe")+"\"","Automatic did not launch the tweaked executable through VanillaFixes without AutoLogin");
            string standard=(string)Call("RenderedToolkit","ResolveGameClient",game,"standard");var unchanged=new ProcessStartInfo(standard);Call("ClientMods","ConfigureLaunch",game,standard,unchanged);Check(unchanged.Arguments=="","Explicit standard client was overridden");
            File.Delete(Path.Combine(game,"VanillaFixes.exe"));Check(Path.GetFileName((string)Call("RenderedToolkit","ResolveGameClient",game,"auto"))=="WoW_Modernized.exe","Automatic direct tweaked fallback failed");
            File.Delete(Path.Combine(game,"WoW_Modernized.exe"));Check(Path.GetFileName((string)Call("RenderedToolkit","ResolveGameClient",game,"auto"))=="WoW.exe","Standard fallback failed");
            Console.WriteLine("PASS Automatic uses tweaked client via VanillaFixes, works without AutoLogin, preserves explicit standard selection and handles missing loader/client");
        }finally{Directory.Delete(game,true);}
    }
}

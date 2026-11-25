using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Collections;
using System.Collections.Generic;
using System.Net;

class HistoryCheck {
    static Assembly Assembly;
    const BindingFlags Static=BindingFlags.Static|BindingFlags.NonPublic;
    static object Call(string type,string method,params object[] args) {
        // All writes use fresh temporary game fixtures; do not inspect or stop a user's game.
        if((type=="ToolkitHistory"&&method=="Restore")||(type=="ToolkitInstall"&&method=="Run"))
            args=args.Concat(new object[]{new Action(()=>{})}).ToArray();
        try {return Assembly.GetType(type).GetMethod(method,Static).Invoke(null,args);}
        catch(TargetInvocationException error){throw error.InnerException;}
    }
    static string Field(object value,string name) {return Convert.ToString(value.GetType().GetProperty(name).GetValue(value,null));}
    static void Check(bool value,string message){if(!value)throw new Exception(message);}
    static void Write(string path,string contents){Directory.CreateDirectory(Path.GetDirectoryName(path));File.WriteAllText(path,contents);}
    static string[] Entries(string game) {return ((IEnumerable)Call("ToolkitHistory","List",game)).Cast<object>().Select(entry=>Field(entry,"Id")).ToArray();}
    static object Discovery(string game,string id) {
        return ((IEnumerable)Call("MpqDiscovery","List",game,Call("MpqPatches","List",game))).Cast<object>().Single(card=>Field(card,"Id")==id);
    }
    static string At(string game,string path){return Path.Combine(game,path.Replace('/',Path.DirectorySeparatorChar));}
    static void ExpectIOException(Action action,string message) {
        try {action();}catch(IOException){return;}
        throw new Exception(message);
    }
    static void CheckExistingCatalogPatches(string game,byte[] archive,Action gameClosed) {
        Directory.CreateDirectory(At(game,"Data"));
        string herbs=At(game,"Data/patch-V.mpq"),nights=At(game,"Data/patch-N.mpq");
        using(var file=File.Create(herbs)) {
            file.Write(archive,0,archive.Length);
            file.SetLength(575628);
        }
        using(var file=File.Create(nights))file.SetLength(201362);
        var recognized=Discovery(game,"pink-herbs");
        Check(Field(recognized,"Existing")=="True"&&Field(recognized,"Installed")=="True"&&Field(recognized,"Slot")=="patch-V.mpq",
            "Existing Pink Herbs in the legacy V slot did not link to its Discover entry");
        var occupied=Discovery(game,"reforged-n");
        Check(Field(occupied,"Blocked")=="True"&&Field(occupied,"Installed")=="False"&&Field(occupied,"OccupiedSlot")=="patch-N.mpq",
            "An unrelated archive with a matching length was claimed without a valid header");
        Call("MpqPatches","SetEnabled",game,"patch-V.mpq",false,gameClosed);
        var disabled=Discovery(game,"pink-herbs");
        Check(Field(disabled,"Existing")=="True"&&Field(disabled,"Enabled")=="False"&&File.Exists(herbs+".disabled")&&File.Exists(nights),
            "An existing catalog archive lost its identity when switched off");
        Console.WriteLine("PASS existing Discover archive recognition, legacy slot, header check, and toggle");
    }
    static void CheckEnvironmentSet(string game,string source,byte[] archive,Action gameClosed) {
        string data=At(game,"Data");
        Directory.CreateDirectory(data);
        Directory.CreateDirectory(source);
        string[] names={"patch-B.mpq","patch-D.mpq","patch-E.mpq"};
        string[] sources=names.Select(name=>At(source,name)).ToArray();
        string[] targets=names.Select(name=>At(data,name)).ToArray();
        foreach(string path in sources)File.WriteAllBytes(path,archive);
        ExpectIOException(()=>Call("MpqDiscovery","PlanEnvironment",game,sources.Take(2).ToArray()),"An incomplete environment set was accepted");
        File.WriteAllText(sources[2],"invalid archive");
        ExpectIOException(()=>Call("MpqDiscovery","PlanEnvironment",game,sources),"An environment archive with an invalid header was accepted");
        Check(!Directory.EnumerateFileSystemEntries(data).Any(),"Invalid environment plans changed game files");
        File.WriteAllBytes(sources[2],archive);
        var planned=(Array)Call("MpqDiscovery","PlanEnvironment",game,sources);
        Check(planned.Length==names.Length,"A complete environment set did not plan all archives");
        Call("MpqPatches","Install",game,planned,gameClosed);
        foreach(string id in new[]{"reforged-b","reforged-d","reforged-e"}) {
            var card=Discovery(game,id);
            Check(Field(card,"Enabled")=="True"&&Field(card,"Existing")=="False","Imported environment archive did not retain its catalog receipt");
        }
        string conflict=targets[1]+".disabled";
        Directory.CreateDirectory(conflict);
        ExpectIOException(()=>Call("MpqDiscovery","SetEnvironmentEnabled",game,false,gameClosed),"The set switch accepted a blocked destination");
        Check(targets.All(File.Exists),"A blocked environment switch partially applied");
        Directory.Delete(conflict);
        Call("MpqDiscovery","SetEnvironmentEnabled",game,false,gameClosed);
        Check(targets.All(path=>!File.Exists(path)&&File.ReadAllBytes(path+".disabled").SequenceEqual(archive)),"The environment set did not all switch off");
        Call("MpqDiscovery","SetEnvironmentEnabled",game,true,gameClosed);
        Check(targets.All(path=>!File.Exists(path+".disabled")&&File.ReadAllBytes(path).SequenceEqual(archive)),"The environment set did not all switch on");
        string held=At(source,"environment-doodads-held.mpq");
        File.Move(targets[1],held);
        Call("MpqDiscovery","SetEnvironmentEnabled",game,false,gameClosed);
        Check(File.Exists(targets[0]+".disabled")&&File.Exists(targets[2]+".disabled"),"An incomplete environment set could not be disabled");
        ExpectIOException(()=>Call("MpqDiscovery","SetEnvironmentEnabled",game,true,gameClosed),"An incomplete environment set was enabled");
        File.Move(held,targets[1]+".disabled");
        Call("ToolkitHistory","Restore",game,Entries(game).Single());
        Check(!Directory.EnumerateFileSystemEntries(data).Any()&&sources.All(path=>File.ReadAllBytes(path).SequenceEqual(archive)),"Environment rollback changed a source file or left a patch behind");
        Console.WriteLine("PASS grouped environment validation, atomic switches, and rollback");
    }
    static void Main(string[] args) {
        ServicePointManager.SecurityProtocol=SecurityProtocolType.Tls12;
        Assembly=System.Reflection.Assembly.LoadFrom(args[0]);
        string root=Path.Combine(Path.GetTempPath(),"th-history-test-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
        try {
            string game=At(root,"journal"),dll=At(game,"plugin.dll"),addon=At(game,"Interface/AddOns/Test/Test.lua"),backup=At(game,".twilight-toolkit/backups/test/plugin.dll");
            Write(dll,"new");Write(backup,"old");Write(addon,"new addon");Write(At(game,"WTF/Account/saved.lua"),"saved settings");
            var changes=new List<KeyValuePair<string,string>>{new KeyValuePair<string,string>(dll,backup),new KeyValuePair<string,string>(addon,null)};
            Call("ToolkitHistory","Record",game,"Test change",changes);string id=Entries(game).Single();
            Write(dll,"later edit");bool rejected=false;
            try{Call("ToolkitHistory","Restore",game,id);}catch(IOException){rejected=true;}
            Check(rejected&&File.ReadAllText(dll)=="later edit","A conflicting edit was overwritten");
            Write(dll,"new");Write(backup,"damaged backup");rejected=false;
            try{Call("ToolkitHistory","Restore",game,id);}catch(IOException){rejected=true;}
            Check(rejected,"A damaged backup was accepted");Write(backup,"old");
            Call("ToolkitHistory","Restore",game,id);
            Check(File.ReadAllText(dll)=="old"&&!File.Exists(addon),"Restore did not handle replaced and new files");
            Check(File.ReadAllText(At(game,"WTF/Account/saved.lua"))=="saved settings","Addon settings changed");
            string undo=Entries(game).Single(entry=>entry!=id);Call("ToolkitHistory","Restore",game,undo);
            Check(File.ReadAllText(dll)=="new"&&File.ReadAllText(addon)=="new addon","Undo restore failed");
            Console.WriteLine("PASS rollback, redo, later-edit and damaged-backup guards, saved settings");

            game=At(root,"login");Write(At(game,"Data/Interface/GlueXML/GlueXML.toc"),"GlueParent.xml\nAutoLogin.xml\nOtherCustom.xml\n");
            Write(At(game,"Data/Interface/GlueXML/AutoLogin.lua"),"login Lua");Write(At(game,"Data/Interface/GlueXML/AutoLogin.xml"),"login XML");Write(At(game,"Imports/logins.txt"),"account fixture");
            var tweaks=Activator.CreateInstance(Assembly.GetType("ClientTweaks"),true);
            tweaks.GetType().GetField("LoginChange",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(tweaks,true);
            var packages=Array.CreateInstance(Assembly.GetType("Package"),0);
            Call("ToolkitInstall","Run",game,"Skip Sync",packages,tweaks,new Action<string>(message=>{}));
            Check(!(bool)Call("ClientMods","LoginInstalled",game),"AutoLogin was not disabled");
            Check(File.ReadAllText(At(game,"Data/Interface/GlueXML/GlueXML.toc")).Contains("OtherCustom.xml"),"Other login entries were removed");
            Check(File.ReadAllText(At(game,"Imports/logins.txt"))=="account fixture","Saved login data changed");
            Call("ToolkitHistory","Restore",game,Entries(game).Single());
            Check((bool)Call("ClientMods","LoginInstalled",game),"AutoLogin rollback failed");
            Console.WriteLine("PASS AutoLogin disable and rollback, custom login entries and saved accounts preserved");

            game=At(root,"addons");Write(At(game,"Interface/AddOns/Test/Test.toc"),"## Interface: 11200\n## Version: 1.0");Write(At(game,"Interface/AddOns/Test/Test.lua"),"before");
            string stage=At(root,"addon-stage");Write(At(stage,"Interface/AddOns/Test/Test.toc"),"## Interface: 11200\n## Version: 2.0");Write(At(stage,"Interface/AddOns/Test/Test.lua"),"after");
            Call("ToolkitInstall","InstallRepository",game,stage,"https://github.com/example/Test",new[]{"Test"});
            Check(Entries(game).Length==1,"Git install history missing");
            Call("ToolkitHistory","Restore",game,Entries(game).Single());
            Check(File.ReadAllText(At(game,"Interface/AddOns/Test/Test.lua"))=="before","Git update rollback failed");
            Call("ToolkitInstall","RemoveAddon",game,"Test");
            var removal=((IEnumerable)Call("ToolkitHistory","List",game)).Cast<object>().First(entry=>Field(entry,"Title")=="Remove addon: Test");
            Call("ToolkitHistory","Restore",game,Field(removal,"Id"));
            Check(File.ReadAllText(At(game,"Interface/AddOns/Test/Test.lua"))=="before","Removed addon could not be restored");
            Console.WriteLine("PASS Git install/update and addon removal receipts and restores");

            game=At(root,"mods");Write(At(game,"dlls.txt"),"# user comment\nunknown.dll\n");
            var catalog=(Array)Assembly.GetType("ToolkitPackages").GetField("Catalog",Static).GetValue(null);
            var selected=catalog.Cast<object>().Where(package=>new[]{"PerfBoost","PNG Screenshots"}.Contains((string)package.GetType().GetField("Name",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(package))).ToArray();
            packages=Array.CreateInstance(Assembly.GetType("Package"),selected.Length);for(int i=0;i<selected.Length;i++)packages.SetValue(selected[i],i);
            tweaks=Activator.CreateInstance(Assembly.GetType("ClientTweaks"),true);
            Call("ToolkitInstall","Run",game,"Skip Sync",packages,tweaks,new Action<string>(message=>{}));
            string loaders=File.ReadAllText(At(game,"dlls.txt"));
            Check(loaders.Contains("perf_boost.dll")&&loaders.Contains("pngscreenshots.dll")&&loaders.Contains("unknown.dll")&&loaders.Contains("# user comment"),"Plugin registration lost entries or missed filenames");
            Call("ToolkitHistory","Restore",game,Entries(game).Single());
            Check(!File.Exists(At(game,"perf_boost.dll"))&&File.ReadAllText(At(game,"dlls.txt"))=="# user comment\nunknown.dll\n","Plugin rollback failed");
            Console.WriteLine("PASS real plugin install, exact loader names, preserved custom loader entries, and rollback");

            game=At(root,"mpq");
            Write(At(game,"Data/patch-5.mpq"),"official archive fixture");
            Write(At(game,"Data/patch-A.mpq"),"existing custom archive");
            string namedPatch=At(root,"patch-K.mpq"),otherPatch=At(root,"Ambience.mpq");
            byte[] archive=new byte[64];archive[0]=(byte)'M';archive[1]=(byte)'P';archive[2]=(byte)'Q';archive[3]=0x1a;
            File.WriteAllBytes(namedPatch,archive);File.WriteAllBytes(otherPatch,archive);
            object patches=Call("MpqPatches","Plan",game,new[]{namedPatch,otherPatch});
            Call("MpqPatches","Install",game,patches,new Action(()=>{}));
            Check(File.ReadAllBytes(At(game,"Data/patch-K.mpq")).SequenceEqual(archive)&&
                File.ReadAllBytes(At(game,"Data/patch-B.mpq")).SequenceEqual(archive),"MPQ import did not preserve or allocate expected names");
            rejected=false;
            try{Call("MpqPatches","Plan",game,new[]{namedPatch});}catch(IOException){rejected=true;}
            Check(rejected,"MPQ import accepted an existing destination");
            var gameClosed=new Action(()=>{});
            string activePatch=At(game,"Data/patch-K.mpq"),disabledPatch=activePatch+".disabled";
            Call("MpqPatches","SetEnabled",game,"patch-K.mpq",false,gameClosed);
            var listed=((IEnumerable)Call("MpqPatches","List",game)).Cast<object>().Single(patch=>Field(patch,"Name")=="patch-K.mpq");
            Check(Field(listed,"Enabled")=="False"&&!File.Exists(activePatch)&&
                File.ReadAllBytes(disabledPatch).SequenceEqual(archive),"Disabling did not keep the patch and report it off");
            rejected=false;
            try{Call("MpqPatches","Plan",game,new[]{namedPatch});}catch(IOException){rejected=true;}
            Check(rejected,"Import replaced a disabled patch's slot");
            Call("MpqPatches","SetEnabled",game,"patch-K.mpq",true,gameClosed);
            Check(!File.Exists(disabledPatch)&&File.ReadAllBytes(activePatch).SequenceEqual(archive),"Re-enabling failed to restore the original patch");
            rejected=false;
            try{Call("MpqPatches","SetEnabled",game,"patch-5.mpq",false,gameClosed);}catch(IOException){rejected=true;}
            Check(rejected,"An official patch could be disabled");
            Call("MpqPatches","SetEnabled",game,"patch-K.mpq",false,gameClosed);
            Call("ToolkitHistory","Restore",game,Entries(game).Single());
            Check(!File.Exists(activePatch)&&!File.Exists(disabledPatch)&&!File.Exists(At(game,"Data/patch-B.mpq"))&&
                File.ReadAllText(At(game,"Data/patch-5.mpq"))=="official archive fixture"&&File.Exists(namedPatch),"MPQ import rollback changed the source or unrelated game files");
            Console.WriteLine("PASS MPQ naming, on/off, disabled slot protection, and History & Restore");

            string raidPatch=At(game,"Data/patch-O.mpq");
            File.WriteAllBytes(raidPatch,archive);
            Call("MpqPatches","SetEnabled",game,"patch-O.mpq",false,gameClosed);
            var raid=Discovery(game,"rook-raidvisuals");
            Check(Field(raid,"Installed")=="True"&&Field(raid,"Enabled")=="False"&&File.Exists(raidPatch+".disabled"),
                "The disabled RaidVisuals archive was lost from Installed Mods");
            Call("MpqPatches","SetEnabled",game,"patch-O.mpq",true,gameClosed);
            Check(File.ReadAllBytes(raidPatch).SequenceEqual(archive)&&Field(Discovery(game,"rook-raidvisuals"),"Enabled")=="True",
                "RaidVisuals could not be enabled again");
            Console.WriteLine("PASS RaidVisuals shares the patch toggle and keeps its reserved slot");

            Check(Field(Discovery(game,"reforged-a"),"Blocked")=="True","Unrelated files were treated as a Discover install");
            string importedNight=At(root,"patch-9.MPQ"),night=At(game,"Data/patch-Z.mpq");
            Write(At(game,"Data/patch-9.mpq"),"official archive fixture");
            File.WriteAllBytes(importedNight,archive);
            var nightEntry=Call("MpqDiscovery","Find","pretty-night-sky");
            object import=Call("MpqPatches","PlanCatalog",game,nightEntry,importedNight);
            var discoveryImport=Array.CreateInstance(import.GetType(),1);
            discoveryImport.SetValue(import,0);
            Call("MpqPatches","Install",game,discoveryImport,gameClosed);
            Check(Field(Discovery(game,"pretty-night-sky"),"Enabled")=="True"&&File.Exists(night),"Discover did not register the installed card");
            Call("MpqPatches","SetEnabled",game,"patch-Z.mpq",false,gameClosed);
            var disabledNight=Discovery(game,"pretty-night-sky");
            Check(Field(disabledNight,"Installed")=="True"&&Field(disabledNight,"Enabled")=="False","Discover lost its installed patch after toggling");
            Call("ToolkitHistory","Restore",game,Entries(game).First());
            Check(Field(Discovery(game,"pretty-night-sky"),"Installed")=="False"&&!File.Exists(night+".disabled")&&File.Exists(importedNight)&&
                File.ReadAllText(At(game,"Data/patch-9.mpq"))=="official archive fixture","Discover restore changed the source or the official patch");
            Console.WriteLine("PASS Discover ownership, reserved number remap, toggles, and rollback");

            CheckExistingCatalogPatches(At(root,"existing-mpq"),archive,gameClosed);
            CheckEnvironmentSet(At(root,"environment"),At(root,"environment-source"),archive,gameClosed);

            game=At(root,"tweaks");
            // A non-executable binary fixture exercises the recorded-client patch path.
            byte[] image=new byte[4927488];image[0]=0x4d;image[1]=0x5a;Buffer.BlockCopy(BitConverter.GetBytes(0x110),0,image,0x3c,4);Buffer.BlockCopy(BitConverter.GetBytes(0x4550),0,image,0x110,4);Buffer.BlockCopy(BitConverter.GetBytes((ushort)0x10b),0,image,0x128,2);
            string client=At(game,"WoW_Modernized.exe");Directory.CreateDirectory(game);File.WriteAllBytes(client,image);
            Write(At(game,".twilight-toolkit/toolkit-client.sha256"),(string)Call("ToolkitPackages","Hash",image));
            Write(At(game,"WTF/Config.wtf"),"SET farclip \"777\"\nSET unrelated \"keep\"\n");
            tweaks=Activator.CreateInstance(Assembly.GetType("ClientTweaks"),true);
            var instance=BindingFlags.Instance|BindingFlags.NonPublic;
            tweaks.GetType().GetField("Enabled",instance).SetValue(tweaks,true);
            foreach(string name in new[]{"Nameplates","FieldOfView","Render","Clutter","Camera"})
                tweaks.GetType().GetField(name,instance).SetValue(tweaks,(decimal)(name=="Render"?1500:name=="FieldOfView"?110:name=="Camera"?75:name=="Clutter"?300:41));
            Call("ToolkitInstall","Run",game,"Skip Sync",Array.CreateInstance(Assembly.GetType("Package"),0),tweaks,new Action<string>(message=>{}));
            Check(BitConverter.ToSingle(File.ReadAllBytes(client),0x40FED8)==1500f,"Render-distance patch failed");
            Call("ToolkitHistory","Restore",game,Entries(game).Single());
            Check(File.ReadAllBytes(client).SequenceEqual(image)&&File.ReadAllText(At(game,"WTF/Config.wtf"))=="SET farclip \"777\"\nSET unrelated \"keep\"\n","Client tweak restore failed");
            Console.WriteLine("PASS client binary and Config.wtf tweak rollback");
            // Only this test's newly created, resolved temporary directory is removed.
            if(Path.GetDirectoryName(Path.GetFullPath(root)).Equals(Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar),StringComparison.OrdinalIgnoreCase)&&Path.GetFileName(root).StartsWith("th-history-test-"))Directory.Delete(root,true);
        } catch {Console.WriteLine("Failed fixture kept at "+root);throw;}
    }
}

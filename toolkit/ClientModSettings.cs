using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

internal sealed class ClientModState {
    public bool Installed {get;set;}
    public bool Enabled {get;set;}
    public string Version {get;set;}
}
internal static class ClientModSettings {
    private static readonly string[] DisplayFixes={"No1600x1200","VanillaMultiMonitorFix"};
    internal static void NormalizeChanges(string game,Dictionary<string,bool> changes) {
        DxvkSettings.Normalize(game,changes);
        bool toggle;
        if(changes.TryGetValue("SuperWoW",out toggle)&&!toggle&&
           ToolkitInstall.DependencyPresent(game,ToolkitPackages.Catalog.Single(item=>item.Name=="Heal Text Fix")))
            changes["Heal Text Fix"]=false;
        if(DisplayFixes.All(name=>changes.ContainsKey(name)&&changes[name]))
            throw new IOException("Choose either No1600x1200 or VanillaMultiMonitorFix, rather than enabling both.");
        foreach(string name in DisplayFixes) {
            bool enabled;
            if(!changes.TryGetValue(name,out enabled)||!enabled)continue;
            string other=DisplayFixes.Single(item=>item!=name);
            var package=ToolkitPackages.Catalog.Single(item=>item.Name==other);
            if(ToolkitInstall.DependencyPresent(game,package))changes[other]=false;
        }
    }
    internal static void ValidateLoader(string game,string stage) {
        string staged=ToolkitPackages.SafePath(stage,"dlls.txt");
        string path=File.Exists(staged)?staged:ToolkitPackages.SafePath(game,"dlls.txt");
        if(!File.Exists(path))return;
        var lines=File.ReadAllLines(path).ToList();
        var active=new HashSet<string>(lines.Select(line=>line.Trim()),StringComparer.OrdinalIgnoreCase);
        if(DisplayFixes.All(name=>active.Contains(ClientMods.Library(ToolkitPackages.Catalog.Single(item=>item.Name==name)))))
            throw new IOException("Both display fixes are enabled. Turn off No1600x1200 or VanillaMultiMonitorFix in Mods, then apply again. No files were changed.");
        int healing=lines.FindIndex(line=>line.Trim().Equals("healtextfix.dll",StringComparison.OrdinalIgnoreCase));
        int superWow=lines.FindIndex(line=>line.Trim().Equals("SuperWoWhook.dll",StringComparison.OrdinalIgnoreCase));
        if(healing>=0&&superWow>healing) {
            string library=lines[healing];
            lines.RemoveAt(healing);
            lines.Insert(superWow,library);
            ToolkitPackages.Save(stage,"dlls.txt",Encoding.UTF8.GetBytes(String.Join(Environment.NewLine,lines)+Environment.NewLine));
        }
    }
    internal static bool Toggleable(Package package) {return package.Folder=="game"&&package.Name!="VanillaFixes"&&package.Name!="Discord Presence";}
    internal static Dictionary<string,ClientModState> Read(string game) {
        var recorded=ToolkitInstall.Installed(game);
        var result=new Dictionary<string,ClientModState>();
        string renderer=DxvkSettings.Active(game);
        foreach(var package in ToolkitPackages.Catalog.Where(item=>item.Folder=="game")) {
            if(DxvkSettings.IsVariant(package.Name)) {
                bool enabled=renderer==package.Name;
                bool cached=DxvkSettings.Cached(game,package.Name);
                string rendererVersion;
                if(!recorded.TryGetValue(package.Name,out rendererVersion))rendererVersion=DxvkSettings.Resolve(package.Name).Version;
                result[package.Name]=new ClientModState{Installed=enabled||cached,Enabled=enabled,Version=rendererVersion};
                continue;
            }
            string file=ToolkitPackages.SafePath(game,package.Name=="VanillaFixes"?"VanillaFixes.exe":ClientMods.Library(package));
            bool installed=File.Exists(file);string version=null;
            if(installed) {
                try {version=FileVersionInfo.GetVersionInfo(file).FileVersion;}catch(ArgumentException) { }
                if(String.IsNullOrWhiteSpace(version)||version=="0.0.0.0")recorded.TryGetValue(package.Name,out version);
                if(!String.IsNullOrEmpty(version)&&Regex.IsMatch(version,@"^[a-fA-F0-9]{40,64}$"))version="Build "+version.Substring(0,8);
                if(String.IsNullOrWhiteSpace(version))version="Build "+ToolkitPackages.Hash(File.ReadAllBytes(file)).Substring(0,8);
            }
            result[package.Name]=new ClientModState{Installed=installed,Enabled=installed&&ToolkitInstall.DependencyPresent(game,package),Version=version};
        }
        return result;
    }
    internal static void Stage(string game,string stage,Dictionary<string,bool> changes) {
        if(changes.Keys.All(DxvkSettings.IsVariant))return;
        string relative="dlls.txt",file=ToolkitPackages.SafePath(game,relative);
        ToolkitInstall.CheckForLinks(game,file);
        string staged=ToolkitPackages.SafePath(stage,relative);
        var lines=File.Exists(staged)?File.ReadAllLines(staged).ToList():File.Exists(file)?File.ReadAllLines(file).ToList():new List<string>();
        foreach(var change in changes) {
            var package=ToolkitPackages.Catalog.SingleOrDefault(item=>item.Name==change.Key&&Toggleable(item));
            if(package==null)throw new IOException("Unknown client mod switch: "+change.Key);
            if(DxvkSettings.IsVariant(change.Key))continue;
            string library=ClientMods.Library(package);
            lines.RemoveAll(line=>line.Trim().Equals(library,StringComparison.OrdinalIgnoreCase));
            if(change.Value) {
                string dll=ToolkitPackages.SafePath(stage,library);
                bool stagedDll=File.Exists(dll);
                if(!stagedDll)dll=ToolkitPackages.SafePath(game,library);
                ToolkitInstall.CheckForLinks(stagedDll?stage:game,dll);
                if(!File.Exists(dll))throw new IOException("Install "+change.Key+" before enabling it.");
                ClientMods.ValidateDll(File.ReadAllBytes(dll));lines.Add(library);
            }
        }
        var active=new HashSet<string>(lines.Select(line=>line.Trim()),StringComparer.OrdinalIgnoreCase);
        if(active.Contains("no1600x1200.dll")&&active.Contains("VanillaMultiMonitorFix.dll"))
            throw new IOException("Choose one display fix: No1600x1200 or VanillaMultiMonitorFix.");
        ToolkitPackages.Save(stage,relative,Encoding.UTF8.GetBytes(String.Join(Environment.NewLine,lines)+Environment.NewLine));
    }
}

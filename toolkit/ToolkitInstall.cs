using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;

internal sealed class RepositoryAddon {
    public string Source {get;set;}
    public string Fingerprint {get;set;}
}

internal sealed class InstalledAddon {
    public string Name {get;set;}
    public string Version {get;set;}
    public string Source {get;set;}
    public string CatalogName {get;set;}
    public string Notes {get;set;}
}

internal static class ToolkitInstall {
    internal static readonly string SidekickFolder=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"TwilightHammerToolkit","ChronicleSidekick");
    internal static string State(string game) { return Path.Combine(game,".twilight-toolkit","versions.txt"); }
    internal static Dictionary<string,string> Installed(string game) {
        var result=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
        if(File.Exists(State(game)))foreach(string line in File.ReadLines(State(game))) {
            string[] pair=line.Split(new[]{'\t'},2);
            if(pair.Length==2)result[pair[0]]=pair[1];
        }
        return result;
    }
    internal static bool ComponentExists(string game,Package package) {
        if(package.Folder=="Data")return File.Exists(Path.Combine(game,"Data","patch-O.mpq"));
        if(package.Folder=="sidekick")return Directory.Exists(SidekickFolder)&&Directory.EnumerateFiles(SidekickFolder,"Chronicle-Sidekick-windows-*.exe").Any();
        if(package.Name=="VanillaFixes")return File.Exists(Path.Combine(game,"VanillaFixes.exe"))&&File.Exists(Path.Combine(game,"VfPatcher.dll"));
        if(package.Folder=="game")return File.Exists(Path.Combine(game,ModLibrary(package)));
        return File.Exists(Path.Combine(game,"Interface","AddOns",package.Folder,package.Folder+".toc"));
    }
    private static string RepositoryState(string game) {
        return Path.Combine(game,".twilight-toolkit","repositories.json");
    }
    internal static Dictionary<string,RepositoryAddon> Repositories(string game) {
        string file=RepositoryState(game);
        var result=new Dictionary<string,RepositoryAddon>(StringComparer.OrdinalIgnoreCase);
        if(!File.Exists(file))return result;
        var entries=new JavaScriptSerializer().Deserialize<Dictionary<string,RepositoryAddon>>(File.ReadAllText(file));
        foreach(var pair in entries)if(pair.Value!=null)result[pair.Key]=pair.Value;
        return result;
    }
    internal static string TocVersion(string file) {
        return TocField(file,"Version");
    }
    private static string TocField(string file,string field) {
        string prefix="## "+field+":";
        string line=File.ReadLines(file).FirstOrDefault(value=>value.StartsWith(prefix,StringComparison.OrdinalIgnoreCase));
        return line==null?null:line.Substring(prefix.Length).Trim();
    }
    internal static InstalledAddon[] Addons(string game) {
        string root=Path.Combine(game,"Interface","AddOns");
        if(!Directory.Exists(root))return new InstalledAddon[0];
        var repositories=Repositories(game);
        return Directory.EnumerateDirectories(root).Select(folder=>{
            string name=Path.GetFileName(folder);
            string toc=Path.Combine(folder,name+".toc");
            if(!File.Exists(toc))return null;
            var package=ToolkitPackages.Catalog.FirstOrDefault(p=>p.Folder.Equals(name,StringComparison.OrdinalIgnoreCase));
            RepositoryAddon repository;
            repositories.TryGetValue(name,out repository);
            return new InstalledAddon{Name=name,Version=TocVersion(toc),Source=repository==null?null:repository.Source,
                CatalogName=package==null?null:package.Name,Notes=package==null?ReadAddonNotes(toc):null};
        }).Where(addon=>addon!=null).OrderBy(addon=>addon.Name,StringComparer.OrdinalIgnoreCase).ToArray();
    }
    private static string ReadAddonNotes(string toc) {
        string notes=TocField(toc,"Notes");
        if(String.IsNullOrWhiteSpace(notes))return null;
        notes=Regex.Replace(notes,@"\|c[0-9a-f]{8}|\|r","",RegexOptions.IgnoreCase).Trim();
        return notes.Length<=500?notes:notes.Substring(0,500).TrimEnd()+"…";
    }
    internal static void RemoveAddon(string game,string name) {
        if(String.IsNullOrWhiteSpace(name)||Path.GetFileName(name)!=name||
           name.Equals("TwilightHammerSync",StringComparison.OrdinalIgnoreCase))
            throw new IOException("Choose an installed addon to remove.");
        string folder=ToolkitPackages.SafePath(game,Path.Combine("Interface","AddOns",name));
        if(!File.Exists(Path.Combine(folder,name+".toc")))throw new IOException("This addon is no longer installed.");
        string backup=ToolkitPackages.SafePath(game,Path.Combine(".twilight-toolkit","backups","removed-"+Guid.NewGuid().ToString("N"),name));
        string state=RepositoryState(game),versionFile=State(game);
        CheckForLinks(game,folder);
        CheckForLinks(game,backup);
        CheckForLinks(game,state);
        CheckForLinks(game,versionFile);
        var repositories=Repositories(game);
        var installed=Installed(game);
        var catalog=ToolkitPackages.Catalog.FirstOrDefault(package=>package.Folder.Equals(name,StringComparison.OrdinalIgnoreCase));
        Directory.CreateDirectory(Path.GetDirectoryName(backup));
        Directory.Move(folder,backup);
        var changes=new List<KeyValuePair<string,string>>();
        string stage=Path.Combine(Path.GetDirectoryName(backup),"metadata");
        try {
            if(repositories.Remove(name)) {
                SaveRepositories(stage,repositories);
                Replace(RepositoryState(stage),state,Path.Combine(Path.GetDirectoryName(backup),"repositories.json"),changes);
            }
            if(catalog!=null&&installed.Remove(catalog.Name)) {
                ToolkitPackages.Save(stage,Path.Combine(".twilight-toolkit","versions.txt"),
                    Encoding.UTF8.GetBytes(String.Join(Environment.NewLine,installed.Select(pair=>pair.Key+"\t"+pair.Value))));
                Replace(State(stage),versionFile,Path.Combine(Path.GetDirectoryName(backup),"versions.txt"),changes);
            }
        } catch {
            Restore(changes);
            Directory.Move(backup,folder);
            throw;
        } finally {
            try {if(Directory.Exists(stage))Directory.Delete(stage,true);}
            catch(IOException) { }
            catch(UnauthorizedAccessException) { }
        }
    }
    internal static bool SyncInstalled(string game) {
        string uploader=SyncRemoval.UploaderFolder;
        string config=Path.Combine(uploader,"native-game.txt");
        return File.Exists(Path.Combine(uploader,"TwilightHammerUploader.exe"))&&File.Exists(config)&&
            File.ReadAllText(config).Trim().TrimEnd('\\').Equals(game.TrimEnd('\\'),StringComparison.OrdinalIgnoreCase);
    }
    internal static string SyncEdition(string game) {
        string file=Path.Combine(game,"Interface","AddOns","TwilightHammerSync","Edition.lua");
        if(!File.Exists(file))return null;
        try {
            var match=Regex.Match(File.ReadAllText(file),@"(?m)^\s*TwilightHammerSync_Edition\s*=\s*['""](guild|auctions?|bank)['""]\s*$");
            switch(match.Groups[1].Value) {
                case "guild": return "Guild Sync";
                case "auction":
                case "auctions": return "Auction Only";
                case "bank": return "Bank and Guild Sync";
                default: return null;
            }
        } catch(IOException) {return null;}
          catch(UnauthorizedAccessException) {return null;}
    }
    internal static Dictionary<string,string> ComponentStatuses(string game) {
        var statuses=new Dictionary<string,string>();
        if(!Directory.Exists(game))return statuses;
        foreach(var package in ToolkitPackages.Catalog) {
            if(!ComponentExists(game,package))continue;
            statuses[package.Name]=package.Folder=="game"&&!DependencyPresent(game,package)
                ?"Files present":"Installed";
        }
        if(File.Exists(Path.Combine(game,"Interface","AddOns","TwilightHammerSync","TwilightHammerSync.toc")))
            statuses["Twilight Hammer Sync"]="Addon installed";
        return statuses;
    }
    private static string ModLibrary(Package package) {
        return package.Name=="SuperWoW"?"SuperWoWhook.dll":package.Name+".dll";
    }
    internal static bool DependencyPresent(string game,Package package) {
        if(!ComponentExists(game,package))return false;
        if(package.Name=="VanillaFixes")return true;
        if(package.Folder=="game") {
            string dll=ModLibrary(package);
            string list=Path.Combine(game,"dlls.txt");
            try {
                return File.Exists(Path.Combine(game,dll))&&File.Exists(list)&&
                    File.ReadLines(list).Any(line=>line.Trim().Equals(dll,StringComparison.OrdinalIgnoreCase));
            } catch(IOException) {return false;}
              catch(UnauthorizedAccessException) {return false;}
        }
        return true;
    }
    internal static void InstallRepository(string game,string stage,string source,string[] addons) {
        var repositories=Repositories(game);
        foreach(string addon in addons)
            repositories[addon]=new RepositoryAddon{Source=source,Fingerprint=RepositoryFingerprint(stage,addon)};
        SaveRepositories(stage,repositories);
        string backup=Path.Combine(game,".twilight-toolkit","backups","repository-"+Guid.NewGuid().ToString("N"));
        var files=Directory.EnumerateFiles(stage,"*",SearchOption.AllDirectories)
            .Select(file=>new {Source=file,Relative=file.Substring(stage.Length+1)}).ToArray();
        foreach(var file in files) {
            CheckForLinks(game,ToolkitPackages.SafePath(game,file.Relative));
            CheckForLinks(game,ToolkitPackages.SafePath(backup,file.Relative));
        }
        var changes=new List<KeyValuePair<string,string>>();
        try {
            foreach(var file in files)
                Replace(file.Source,ToolkitPackages.SafePath(game,file.Relative),ToolkitPackages.SafePath(backup,file.Relative),changes);
        } catch {
            Restore(changes);
            throw;
        }
    }
    private static void SaveRepositories(string stage,Dictionary<string,RepositoryAddon> repositories) {
        ToolkitPackages.Save(stage,Path.Combine(".twilight-toolkit","repositories.json"),
            Encoding.UTF8.GetBytes(new JavaScriptSerializer().Serialize(repositories)));
    }
    internal static string RepositoryFingerprint(string stage,string addon) {
        string folder=ToolkitPackages.SafePath(stage,Path.Combine("Interface","AddOns",addon));
        return ToolkitPackages.Hash(Encoding.UTF8.GetBytes(String.Join("\n",Directory.GetFiles(folder,"*",SearchOption.AllDirectories)
            .OrderBy(file=>file,StringComparer.OrdinalIgnoreCase)
            .Select(file=>file.Substring(folder.Length+1)+":"+ToolkitPackages.Hash(File.ReadAllBytes(file))))));
    }
    private static void CheckForLinks(string game,string path) {
        string root=Path.GetFullPath(game).TrimEnd(Path.DirectorySeparatorChar);
        for(string current=path;!current.Equals(root,StringComparison.OrdinalIgnoreCase);current=Path.GetDirectoryName(current)) {
            try {
                if((File.GetAttributes(current)&FileAttributes.ReparsePoint)!=0)
                    throw new IOException("The addon destination contains a linked file or folder.");
            } catch(FileNotFoundException) { }
              catch(DirectoryNotFoundException) { }
        }
    }
    internal static void Run(string game,string edition,Package[] packages,ClientTweaks tweaks,Action<string> progress) {
        bool requiresClose=RequiresClosedGame(edition,packages,tweaks);
        if(requiresClose)EnsureClosed();
        string work=Path.Combine(Path.GetTempPath(),"TwilightToolkit-"+Guid.NewGuid().ToString("N"));
        string stage=Path.Combine(work,"stage");
        string backup=Path.Combine(game,".twilight-toolkit","backups",DateTime.UtcNow.ToString("yyyyMMdd-HHmmss")+"-"+Guid.NewGuid().ToString("N").Substring(0,6));
        string sidekick=SidekickFolder;
        Directory.CreateDirectory(stage);
        var installed=Installed(game);
        var changes=new List<KeyValuePair<string,string>>();
        try {
            tweaks.Stage(game,stage);
            foreach(var package in packages) {
                progress("Downloading "+package.Name+"…");
                try {
                    var release=ToolkitPackages.Resolve(package);
                    ToolkitPackages.Stage(package,release,stage);
                    installed[package.Name]=release.Version;
                } catch(Exception error) {
                    throw new IOException(package.Name+": "+error.Message,error);
                }
            }
            if(packages.Length>0&&File.Exists(RepositoryState(game))) {
                var repositories=Repositories(game);
                bool changed=false;
                foreach(var package in packages) {
                    string toc=Path.Combine(stage,"Interface","AddOns",package.Folder,package.Folder+".toc");
                    if(File.Exists(toc))changed=repositories.Remove(package.Folder)||changed;
                }
                if(changed)SaveRepositories(stage,repositories);
            }
            string[] dlls=Directory.GetFiles(stage,"*.dll");
            string[] loaders={"nampower.dll","ClassicAPI.dll","UnitXP_SP3.dll","VanillaHelpers.dll","SuperWoWhook.dll"};
            dlls=dlls.Select(Path.GetFileName).Where(file=>loaders.Contains(file,StringComparer.OrdinalIgnoreCase)).ToArray();
            if(dlls.Length>0) {
                string path=Path.Combine(game,"dlls.txt");
                var lines=File.Exists(path)?File.ReadAllLines(path).ToList():new List<string>();
                foreach(string dll in dlls)if(!lines.Any(line=>String.Equals(line.Trim(),dll,StringComparison.OrdinalIgnoreCase)))lines.Add(dll);
                ToolkitPackages.Save(stage,"dlls.txt",Encoding.UTF8.GetBytes(String.Join(Environment.NewLine,lines)+Environment.NewLine));
            }
            if(packages.Length>0)ToolkitPackages.Save(stage,Path.Combine(".twilight-toolkit","versions.txt"),
                Encoding.UTF8.GetBytes(String.Join(Environment.NewLine,installed.Select(kv=>kv.Key+"\t"+kv.Value))));
            if(requiresClose)EnsureClosed();
            progress("Installing selected components…");
            foreach(var file in Directory.EnumerateFiles(stage,"*",SearchOption.AllDirectories)) {
                string relative=file.Substring(stage.Length+1);
                if(relative.StartsWith("sidekick"+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)) {
                    string target=ToolkitPackages.SafePath(sidekick,Path.GetFileName(file));
                    Replace(file,target,ToolkitPackages.SafePath(backup,Path.Combine("sidekick",Path.GetFileName(file))),changes);
                    string programs=Environment.GetFolderPath(Environment.SpecialFolder.Programs);
                    string link=Path.Combine(programs,"Chronicle Sidekick.lnk");
                    string stagedLink=Path.Combine(work,"Chronicle Sidekick.lnk");
                    dynamic shell=Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell"));
                    dynamic shortcut=shell.CreateShortcut(stagedLink);
                    shortcut.TargetPath=target;
                    shortcut.WorkingDirectory=sidekick;
                    shortcut.Save();
                    Replace(stagedLink,link,ToolkitPackages.SafePath(backup,"Chronicle Sidekick.lnk"),changes);
                } else {
                    Replace(file,ToolkitPackages.SafePath(game,relative),ToolkitPackages.SafePath(backup,relative),changes);
                }
            }
            if(edition!="Skip Sync") {
                string name=edition=="Auction Only"?"auction":edition=="Bank and Guild Sync"?"bank":"guild";
                string setup=Path.Combine(work,"TwilightSyncSetup.exe");
                using(var resource=Assembly.GetExecutingAssembly().GetManifestResourceStream(name+".exe"))
                using(var output=File.Create(setup))resource.CopyTo(output);
                using(var child=Process.Start(new ProcessStartInfo(setup,"--install \""+game+"\"") {WorkingDirectory=game,UseShellExecute=false})) {
                    child.WaitForExit();
                    if(child.ExitCode==2)throw new OperationCanceledException("Sync setup cancelled.");
                    if(child.ExitCode!=0)throw new IOException("Twilight Hammer Sync setup failed.");
                }
            }
        } catch {
            Restore(changes);
            throw;
        } finally {
            try {Directory.Delete(work,true);} catch(IOException) { } catch(UnauthorizedAccessException) { }
        }
    }
    internal static bool RequiresClosedGame(string edition,Package[] packages,ClientTweaks tweaks) {
        return edition!="Skip Sync"||tweaks.Enabled||packages.Any(package=>package.Folder=="game"||package.Folder=="Data");
    }
    private static void Restore(List<KeyValuePair<string,string>> changes) {
        for(int i=changes.Count-1;i>=0;i--) {
            var change=changes[i];
            try {
                if(change.Value==null)File.Delete(change.Key);
                else File.Copy(change.Value,change.Key,true);
            } catch(IOException) { }
              catch(UnauthorizedAccessException) { }
        }
    }
    private static void Replace(string from,string to,string backup,List<KeyValuePair<string,string>> changes) {
        Directory.CreateDirectory(Path.GetDirectoryName(to));
        if(File.Exists(to)) {
            Directory.CreateDirectory(Path.GetDirectoryName(backup));
            File.Copy(to,backup);
        } else backup=null;
        changes.Add(new KeyValuePair<string,string>(to,backup));
        string temp=to+".th-"+Guid.NewGuid().ToString("N")+".tmp";
        try {
            File.Copy(from,temp);
            if(File.Exists(to))File.Replace(temp,to,null);
            else File.Move(temp,to);
        } finally {
            if(File.Exists(temp))File.Delete(temp);
        }
    }
    private static void EnsureClosed() {
        if(new[]{"WoW","WoW_Modernized","WoW_tweaked"}.Any(name=>Process.GetProcessesByName(name).Length>0))
            throw new IOException("Close OctoWoW before changing client mods, data patches, client tweaks, or Sync.");
    }
}

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;

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
    internal static void InstallRepository(string game,string stage) {
        EnsureClosed();
        string backup=Path.Combine(game,".twilight-toolkit","backups","repository-"+Guid.NewGuid().ToString("N"));
        var files=Directory.EnumerateFiles(stage,"*",SearchOption.AllDirectories)
            .Select(source=>new {Source=source,Relative=source.Substring(stage.Length+1)}).ToArray();
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
        EnsureClosed();
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
            EnsureClosed();
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
            throw new IOException("Close OctoWoW before installing or applying client tweaks.");
    }
}

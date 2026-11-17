using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

internal sealed class ToolkitUpdateResult {
    internal readonly List<string> Lines=new List<string>();
    internal readonly Dictionary<string,bool> Available=new Dictionary<string,bool>();
    internal string ToolkitUrl;
    internal Release ToolkitRelease;
    internal string Summary {
        get {
            int count=Available.Values.Count(value=>value);
            return Lines[0]+(count==0?"":" "+count+(count==1?" component update available.":" component updates available."));
        }
    }
}

internal static class ToolkitUpdates {
    internal static ToolkitUpdateResult Check(string game,Action<string> progress) {
        var result=new ToolkitUpdateResult();
        progress("Checking the Toolkit release on GitHub…");
        try {
            var release=ToolkitPackages.Resolve(ToolkitPackages.Self);
            Version candidate;
            if(!Version.TryParse(release.Version.TrimStart('v'),out candidate))
                result.Lines.Add("Toolkit: the latest release has no comparable version.");
            else if(candidate>new Version(ToolkitPackages.Version)) {
                result.ToolkitUrl=ToolkitPackages.GitHub+"/releases/tag/"+Uri.EscapeDataString(release.Version);
                result.ToolkitRelease=release;
                result.Lines.Add("Toolkit "+release.Version+" is available.");
            } else result.Lines.Add("Toolkit is current.");
        } catch {result.Lines.Add("Could not verify a Toolkit release on GitHub.");}

        if(game==null){ToolkitLog.Write("updates",result.Summary);return result;}
        var recorded=ToolkitInstall.Installed(game);
        var addons=ToolkitInstall.Addons(game);
        var repositories=ToolkitInstall.Repositories(game);
        var targets=ToolkitPackages.Catalog.Where(p=>!repositories.ContainsKey(p.Folder)&&
            (recorded.ContainsKey(p.Name)||ToolkitInstall.ComponentExists(game,p))).ToArray();
        if(targets.Length==0)result.Lines.Add("No catalog addons or client tools found in the selected game folder.");
        foreach(var package in targets) {
            progress("Checking "+package.Name+"…");
            try {
                if(!ToolkitInstall.ComponentExists(game,package)) {
                    result.Lines.Add(package.Name+": local files missing. Reinstall to restore them.");
                    continue;
                }
                var release=ToolkitPackages.Resolve(package);
                string installed;
                string status;
                bool tracked=recorded.TryGetValue(package.Name,out installed);
                if(tracked&&release.Version==installed)
                    status="up to date";
                else if(!tracked||File.Exists(Path.Combine(game,"Interface","AddOns",package.Folder,package.Folder+".toc")))
                    status=CompareInstalled(game,package,release,tracked);
                else
                    status="update available";
                if(status=="update available")result.Available[package.Name]=true;
                else if(status=="up to date"||status=="matches upstream")result.Available[package.Name]=false;
                result.Lines.Add(package.Name+": "+status);
            } catch(Exception error) {
                result.Lines.Add(package.Name+": could not verify ("+error.Message+")");
            }
        }
        foreach(var group in addons.Where(addon=>addon.Source!=null).GroupBy(addon=>addon.Source)) {
            string stage=Path.Combine(Path.GetTempPath(),"TwilightToolkit-Check-"+Guid.NewGuid().ToString("N"));
            try {
                progress("Checking "+String.Join(", ",group.Select(addon=>addon.Name))+"…");
                var found=ToolkitPackages.StageRepository(group.Key,stage);
                foreach(var addon in group) {
                    if(!found.Contains(addon.Name,StringComparer.OrdinalIgnoreCase)) {
                        result.Lines.Add(addon.Name+": no longer present in the repository.");
                        continue;
                    }
                    bool newer=ToolkitInstall.RepositoryFingerprint(stage,addon.Name)!=repositories[addon.Name].Fingerprint;
                    result.Available["git:"+addon.Name]=newer;
                    result.Lines.Add(addon.Name+": "+(newer?"update available":"up to date"));
                }
            } catch(Exception error) {
                result.Lines.Add(group.Key+": could not verify ("+error.Message+")");
            } finally {
                try {if(Directory.Exists(stage))Directory.Delete(stage,true);}
                catch(IOException) { }
                catch(UnauthorizedAccessException) { }
            }
        }
        foreach(string line in result.Lines)ToolkitLog.Write("updates",line,line.Contains("could not verify")?"warn":"info");
        return result;
    }

    private static string CompareInstalled(string game,Package package,Release release,bool tracked) {
        string stage=Path.Combine(Path.GetTempPath(),"TwilightToolkit-Check-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(stage);
        try {
            ToolkitPackages.Stage(package,release,stage);
            if(MatchesUpstream(game,stage,ToolkitInstall.SidekickFolder))return "matches upstream";
            string relative=Path.Combine("Interface","AddOns",package.Folder,package.Folder+".toc");
            string local=Path.Combine(game,relative), upstream=Path.Combine(stage,relative);
            Version installed,latest;
            if(File.Exists(local)&&File.Exists(upstream)&&
               Version.TryParse((ToolkitInstall.TocVersion(local)??"").TrimStart('v','V'),out installed)&&
               Version.TryParse((ToolkitInstall.TocVersion(upstream)??"").TrimStart('v','V'),out latest))
                return latest>installed?"update available":"up to date";
            return tracked?"update available":"differs from latest upstream files";
        } finally {
            try {Directory.Delete(stage,true);}
            catch(IOException) {}
            catch(UnauthorizedAccessException) {}
        }
    }

    internal static bool MatchesUpstream(string game,string stage,string sidekick) {
        var files=Directory.GetFiles(stage,"*",SearchOption.AllDirectories);
        return files.Length>0&&files.All(file=>{
            string relative=file.Substring(stage.Length+1);
            string local=relative.StartsWith("sidekick"+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)?
                ToolkitPackages.SafePath(sidekick,Path.GetFileName(file)):ToolkitPackages.SafePath(game,relative);
            if(!File.Exists(local)||new FileInfo(local).Length!=new FileInfo(file).Length)return false;
            return ToolkitPackages.Hash(File.ReadAllBytes(local))==ToolkitPackages.Hash(File.ReadAllBytes(file));
        });
    }
}

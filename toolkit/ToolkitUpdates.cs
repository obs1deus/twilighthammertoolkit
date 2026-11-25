using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

internal sealed class ToolkitUpdateVersion {
    public string Current {get;set;}
    public string Latest {get;set;}
}

internal sealed class ToolkitUpdateResult {
    internal readonly List<string> Lines=new List<string>();
    internal readonly Dictionary<string,bool> Available=new Dictionary<string,bool>();
    internal readonly Dictionary<string,string> UpdateIdentities=new Dictionary<string,string>();
    internal readonly Dictionary<string,ToolkitUpdateVersion> Versions=new Dictionary<string,ToolkitUpdateVersion>();
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
    internal static ToolkitUpdateResult Check(string game,Action<string> progress,Action<string,string,ToolkitUpdateVersion> updateFound=null) {
        var result=new ToolkitUpdateResult();
        progress("Checking the Launcher release on GitHub…");
        try {
            var release=ToolkitPackages.Resolve(ToolkitPackages.Self);
            Version candidate;
            if(!Version.TryParse(release.Version.TrimStart('v'),out candidate))
                result.Lines.Add("Launcher: the latest release has no comparable version.");
            else if(candidate>new Version(ToolkitPackages.Version)) {
                result.ToolkitUrl=ToolkitPackages.GitHub+"/releases/tag/"+Uri.EscapeDataString(release.Version);
                result.ToolkitRelease=release;
                result.Lines.Add("Launcher "+release.Version+" is available.");
            } else result.Lines.Add("Launcher is current.");
        } catch {result.Lines.Add("Could not verify a Launcher release on GitHub.");}

        if(game==null){ToolkitLog.Write("updates",result.Summary);return result;}
        var recorded=ToolkitInstall.Installed(game);
        var addons=ToolkitInstall.Addons(game);
        var repositories=ToolkitInstall.Repositories(game);
        var installedAddonNames=new HashSet<string>(addons.Select(addon=>addon.CatalogName),StringComparer.OrdinalIgnoreCase);
        var targets=ToolkitPackages.Catalog.Where(p=>!repositories.ContainsKey(p.Folder)&&
            (recorded.ContainsKey(p.Name)||ToolkitInstall.ComponentExists(game,p)))
            .OrderBy(p=>!installedAddonNames.Contains(p.Name)).ToArray();
        if(targets.Length==0)result.Lines.Add("No catalog addons or client tools found in the selected game folder.");
        foreach(var package in targets) {
            progress("Checking "+package.Name+"…");
            try {
                if(!ToolkitInstall.ComponentExists(game,package)) {
                    result.Available[package.Name]=false;
                    result.Lines.Add(package.Name+": local files missing. Reinstall to restore them.");
                    continue;
                }
                var release=ToolkitPackages.Resolve(package);
                string installed, status, stagedVersion=null;
                bool tracked=recorded.TryGetValue(package.Name,out installed);
                if(tracked&&release.Version==installed)
                    status="up to date";
                else if(!tracked||File.Exists(Path.Combine(game,"Interface","AddOns",package.Folder,package.Folder+".toc")))
                    status=CompareInstalled(game,package,release,tracked,out stagedVersion);
                else
                    status="update available";
                if(status=="update available") {
                    result.Available[package.Name]=true;
                    result.UpdateIdentities[package.Name]=release.Version+"\t"+release.Digest+"\t"+release.Url;
                    var addon=addons.FirstOrDefault(row=>row.CatalogName==package.Name);
                    result.Versions[package.Name]=DescribeVersion(addon==null?null:addon.Version,stagedVersion,installed,release.Version);
                    if(updateFound!=null)updateFound(package.Name,result.UpdateIdentities[package.Name],result.Versions[package.Name]);
                }
                else result.Available[package.Name]=false;
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
                        result.Available["git:"+addon.Name]=false;
                        result.Lines.Add(addon.Name+": no longer present in the repository.");
                        continue;
                    }
                    string fingerprint=ToolkitInstall.RepositoryFingerprint(stage,addon.Name);
                    string currentFingerprint=repositories[addon.Name].Fingerprint;
                    bool newer=fingerprint!=currentFingerprint;
                    string key="git:"+addon.Name;
                    result.Available[key]=newer;
                    if(newer) {
                        result.UpdateIdentities[key]=group.Key+"\t"+fingerprint;
                        result.Versions[key]=DescribeRepositoryVersion(addon,stage,currentFingerprint,fingerprint);
                        if(updateFound!=null)updateFound(key,result.UpdateIdentities[key],result.Versions[key]);
                    }
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

    internal static ToolkitUpdateVersion DescribeRepositoryVersion(InstalledAddon addon,string stage,string currentFingerprint,string latestFingerprint) {
        string toc=ToolkitPackages.SafePath(stage,Path.Combine("Interface","AddOns",addon.Name,addon.Name+".toc"));
        return DescribeVersion(addon.Version,File.Exists(toc)?ToolkitInstall.TocVersion(toc):null,currentFingerprint,latestFingerprint);
    }

    private static ToolkitUpdateVersion DescribeVersion(string current,string latest,string currentBuild,string latestBuild) {
        string from=ReadableVersion(current), to=ReadableVersion(latest);
        string fromBuild=ReadableVersion(currentBuild), toBuild=ReadableVersion(latestBuild);
        from=from??fromBuild;
        to=to??toBuild;
        // Two different source builds can publish the same version in the addon manifest.
        if(from!=null&&String.Equals(from,to,StringComparison.OrdinalIgnoreCase)) {
            if(fromBuild!=null&&fromBuild!=from)from+=" ("+fromBuild+")";
            if(toBuild!=null&&toBuild!=to)to+=" ("+toBuild+")";
        }
        return new ToolkitUpdateVersion{Current=from,Latest=to};
    }

    private static string ReadableVersion(string raw) {
        if(String.IsNullOrWhiteSpace(raw))return null;
        string value=raw.Trim();
        string hash=(value.StartsWith("W/",StringComparison.OrdinalIgnoreCase)?value.Substring(2):value).Trim('"');
        if(Regex.IsMatch(hash,@"^[a-fA-F0-9]{16,128}$"))return "Build "+hash.Substring(0,8).ToLowerInvariant();
        var pinned=Regex.Match(value,@"^(.+)-([a-fA-F0-9]{12})$");
        if(pinned.Success)return ReadableVersion(pinned.Groups[1].Value)+" (Build "+pinned.Groups[2].Value.Substring(0,8).ToLowerInvariant()+")";
        return Regex.IsMatch(value,@"^\d+\.\d")?"v"+value:value;
    }

    private static string CompareInstalled(string game,Package package,Release release,bool tracked,out string stagedVersion) {
        stagedVersion=null;
        string stage=Path.Combine(Path.GetTempPath(),"TwilightToolkit-Check-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(stage);
        try {
            ToolkitPackages.Stage(package,release,stage);
            if(MatchesUpstream(game,stage,ToolkitInstall.SidekickFolder))return "matches upstream";
            string relative=Path.Combine("Interface","AddOns",package.Folder,package.Folder+".toc");
            string local=Path.Combine(game,relative), upstream=Path.Combine(stage,relative);
            if(File.Exists(upstream))stagedVersion=ToolkitInstall.TocVersion(upstream);
            Version installed,latest;
            if(File.Exists(local)&&stagedVersion!=null&&
               Version.TryParse((ToolkitInstall.TocVersion(local)??"").TrimStart('v','V'),out installed)&&
               Version.TryParse(stagedVersion.TrimStart('v','V'),out latest))
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

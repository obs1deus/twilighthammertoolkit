using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

internal sealed class ToolkitUpdateResult {
    internal readonly List<string> Lines=new List<string>();
    internal string ToolkitUrl;
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
                result.Lines.Add("Toolkit "+release.Version+" is available.");
            } else result.Lines.Add("Toolkit is current.");
        } catch {result.Lines.Add("Could not verify a Toolkit release on GitHub.");}

        var recorded=ToolkitInstall.Installed(game);
        var targets=ToolkitPackages.Catalog.Where(p=>recorded.ContainsKey(p.Name)||ToolkitInstall.ComponentExists(game,p)).ToArray();
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
                if(recorded.TryGetValue(package.Name,out installed))
                    status=release.Version==installed?"up to date":"update available";
                else
                    status=CompareUntracked(game,package,release);
                result.Lines.Add(package.Name+": "+status);
            } catch(Exception error) {
                result.Lines.Add(package.Name+": could not verify ("+error.Message+")");
            }
        }
        return result;
    }

    private static string CompareUntracked(string game,Package package,Release release) {
        string stage=Path.Combine(Path.GetTempPath(),"TwilightToolkit-Check-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(stage);
        try {
            ToolkitPackages.Stage(package,release,stage);
            return MatchesUpstream(game,stage,ToolkitInstall.SidekickFolder)?"matches upstream":"differs from latest upstream files";
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

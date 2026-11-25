using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Web.Script.Serialization;

internal sealed class HistoryFile {
    public string Path {get;set;}
    public string Backup {get;set;}
    public string Before {get;set;}
    public string After {get;set;}
}
internal sealed class HistoryEntry {
    public string Id {get;set;}
    public string Date {get;set;}
    public string Title {get;set;}
    public bool Restored {get;set;}
    public List<HistoryFile> Files {get;set;}
}
internal static class ToolkitHistory {
    private static readonly JavaScriptSerializer Json=new JavaScriptSerializer{MaxJsonLength=16*1024*1024};
    private static string Root(string game) {return ToolkitPackages.SafePath(game,System.IO.Path.Combine(".twilight-toolkit","history"));}
    private static string Hash(string file) {
        if(!File.Exists(file))return null;
        using(var input=File.OpenRead(file))
        using(var hash=SHA256.Create())return BitConverter.ToString(hash.ComputeHash(input)).Replace("-","").ToLowerInvariant();
    }
    private static string Receipt(string game,string id) {
        Guid parsed;if(!Guid.TryParseExact(id,"N",out parsed))throw new IOException("Invalid history entry.");
        string file=System.IO.Path.Combine(Root(game),id+".json");ToolkitInstall.CheckForLinks(game,file);return file;
    }
    private static bool Supported(string relative) {
        if(String.IsNullOrWhiteSpace(relative)||relative.Split('/','\\').Any(part=>part==".."||part=="."||part.Length==0))return false;
        string path=relative.Replace('\\','/');
        return path.StartsWith("Interface/AddOns/",StringComparison.OrdinalIgnoreCase)||path.StartsWith("Data/",StringComparison.OrdinalIgnoreCase)||path.StartsWith("WDB/",StringComparison.OrdinalIgnoreCase)||
            path.Equals("WTF/Config.wtf",StringComparison.OrdinalIgnoreCase)||path.Equals(".twilight-toolkit/tweak-safety.txt",StringComparison.OrdinalIgnoreCase)||
            path.Equals(".twilight-toolkit/tweak-script-memory.txt",StringComparison.OrdinalIgnoreCase)||
            MpqDiscovery.IsReceipt(path)||DxvkSettings.IsManagedPath(path)||
            new[]{"dlls.txt","WoW_Modernized.exe","WowPresence.exe","WowPresence/discord_application_id","WowPresence/discord_broadcast_flags",".modernization_tool/WowPresence/discord_application_id",".modernization_tool/WowPresence/discord_broadcast_flags",".twilight-toolkit/discord-presence.txt",".twilight-toolkit/clear-wdb.txt",".twilight-toolkit/versions.txt",".twilight-toolkit/repositories.json",".twilight-toolkit/toolkit-client.sha256"}.Contains(path,StringComparer.OrdinalIgnoreCase)||
            (!path.Contains('/')&&path.EndsWith(".dll",StringComparison.OrdinalIgnoreCase))||path.Equals("VanillaFixes.exe",StringComparison.OrdinalIgnoreCase);
    }
    private static void Save(string game,HistoryEntry entry) {
        string file=Receipt(game,entry.Id);Directory.CreateDirectory(Root(game));
        string temp=file+".tmp";File.WriteAllText(temp,Json.Serialize(entry));
        if(File.Exists(file))File.Replace(temp,file,null);else File.Move(temp,file);
    }
    internal static void Record(string game,string title,IEnumerable<KeyValuePair<string,string>> changes) {
        string prefix=System.IO.Path.GetFullPath(game).TrimEnd(System.IO.Path.DirectorySeparatorChar)+System.IO.Path.DirectorySeparatorChar;
        var entry=new HistoryEntry{Id=Guid.NewGuid().ToString("N"),Date=DateTime.UtcNow.ToString("o"),Title=title,Files=new List<HistoryFile>()};
        foreach(var change in changes) {
            string target=System.IO.Path.GetFullPath(change.Key);
            if(!target.StartsWith(prefix,StringComparison.OrdinalIgnoreCase))continue;
            string relative=target.Substring(prefix.Length);
            if(!Supported(relative))continue;
            string backup=change.Value==null?null:System.IO.Path.GetFullPath(change.Value);
            if(backup!=null&&!backup.StartsWith(prefix+".twilight-toolkit"+System.IO.Path.DirectorySeparatorChar+"backups"+System.IO.Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))
                throw new IOException("The change backup is outside the game backup folder.");
            if(backup!=null&&!File.Exists(backup))throw new IOException("A previous-version backup is missing.");
            string before=backup==null?null:Hash(backup),after=Hash(target);
            if(before==after)continue;
            entry.Files.Add(new HistoryFile{Path=relative,Backup=backup==null?null:backup.Substring(prefix.Length),Before=before,After=after});
        }
        if(entry.Files.Count>0)Save(game,entry);
    }
    internal static HistoryEntry Load(string game,string id) {
        var entry=Json.Deserialize<HistoryEntry>(File.ReadAllText(Receipt(game,id)));
        if(entry==null||entry.Id!=id||entry.Files==null||entry.Files.Count==0||entry.Files.Count>50000)throw new IOException("The history entry is incomplete.");
        if(entry.Files.GroupBy(file=>file.Path,StringComparer.OrdinalIgnoreCase).Any(group=>group.Count()>1))throw new IOException("The history entry has duplicate paths.");
        foreach(var file in entry.Files) {
            if(!Supported(file.Path))throw new IOException("This history entry contains an unsupported path.");
            ToolkitInstall.CheckForLinks(game,ToolkitPackages.SafePath(game,file.Path));
            if(file.Backup!=null) {
                if(!file.Backup.Replace('\\','/').StartsWith(".twilight-toolkit/backups/",StringComparison.OrdinalIgnoreCase))throw new IOException("Invalid backup location.");
                string backupRoot=ToolkitPackages.SafePath(game,System.IO.Path.Combine(".twilight-toolkit","backups"))+System.IO.Path.DirectorySeparatorChar;
                if(!ToolkitPackages.SafePath(game,file.Backup).StartsWith(backupRoot,StringComparison.OrdinalIgnoreCase))throw new IOException("Invalid backup location.");
                ToolkitInstall.CheckForLinks(game,ToolkitPackages.SafePath(game,file.Backup));
            }
        }
        return entry;
    }
    internal static HistoryEntry[] List(string game) {
        if(!Directory.Exists(Root(game)))return new HistoryEntry[0];
        var result=new List<HistoryEntry>();
        foreach(string file in Directory.GetFiles(Root(game),"*.json")) {
            try {result.Add(Load(game,System.IO.Path.GetFileNameWithoutExtension(file)));}
            catch(IOException) { } catch(ArgumentException) { } catch(InvalidOperationException) { }
        }
        return result.OrderByDescending(entry=>entry.Date).Take(100).ToArray();
    }
    private static string RestoreTarget(string game,HistoryFile file) {
        string target=ToolkitPackages.SafePath(game,file.Path);
        if(file.After!=null&&MpqPatches.IsCustomPath(file.Path)&&!File.Exists(target)) {
            string alternate=file.Path.EndsWith(MpqPatches.DisabledSuffix,StringComparison.OrdinalIgnoreCase)
                ?target.Substring(0,target.Length-MpqPatches.DisabledSuffix.Length):target+MpqPatches.DisabledSuffix;
            ToolkitInstall.CheckForLinks(game,alternate);
            if(File.Exists(alternate))return alternate;
        }
        return target;
    }
    internal static string[] Conflicts(string game,HistoryEntry entry) {
        return entry.Files.Where(file=>Hash(RestoreTarget(game,file))!=file.After||
            (file.Before!=null&&(file.Backup==null||Hash(ToolkitPackages.SafePath(game,file.Backup))!=file.Before))).Select(file=>file.Path).ToArray();
    }
    internal static void Restore(string game,string id,Action verifyGameClosed=null) {
        var entry=Load(game,id);
        using(entry.Files.Any(file=>MpqPatches.IsCustomPath(file.Path)||DxvkSettings.IsManagedPath(file.Path)||ClientMods.IsGluePath(file.Path))?OctoPatch.OpenPatchLock(game):null)
            RestoreEntry(game,entry,verifyGameClosed);
    }
    private static void RestoreEntry(string game,HistoryEntry entry,Action verifyGameClosed) {
        if(entry.Restored)throw new IOException("These changes have already been restored.");
        if(Conflicts(game,entry).Length>0)throw new IOException("Files changed after this entry, or a backup is missing. Restore newer changes first.");
        (verifyGameClosed??ToolkitInstall.EnsureClosed)();
        string backup=ToolkitPackages.SafePath(game,System.IO.Path.Combine(".twilight-toolkit","backups","rollback-"+Guid.NewGuid().ToString("N")));
        var changes=new List<KeyValuePair<string,string>>();
        try {
            foreach(var file in entry.Files) {
                string target=RestoreTarget(game,file);
                string saved=ToolkitPackages.SafePath(backup,Path.Combine(Path.GetDirectoryName(file.Path),Path.GetFileName(target)));
                ToolkitInstall.CheckForLinks(game,saved);
                if(file.Before==null) {
                    if(File.Exists(target)) {
                        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(saved));File.Copy(target,saved);
                        changes.Add(new KeyValuePair<string,string>(target,saved));File.Delete(target);
                    }
                } else ToolkitInstall.Replace(ToolkitPackages.SafePath(game,file.Backup),target,saved,changes);
            }
            Record(game,"Restore: "+entry.Title,changes);
            entry.Restored=true;Save(game,entry);
            ToolkitLog.Write("history","Restored "+entry.Title+".");
        } catch {ToolkitInstall.Restore(changes);throw;}
    }
}

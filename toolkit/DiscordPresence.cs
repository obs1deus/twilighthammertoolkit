using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;

internal sealed class PresenceState {
    public bool installed {get;set;}
    public bool configured {get;set;}
    public bool enabled {get;set;}
    public int flags {get;set;}
}
internal static class DiscordPresence {
    private const string Owner=".twilight-toolkit/discord-presence.txt";
    private static string PathAt(string game,string relative){return ToolkitPackages.SafePath(game,relative.Replace('/',Path.DirectorySeparatorChar));}
    internal static PresenceState State(string game) {
        bool configured=File.Exists(PathAt(game,Owner));int flags=0;bool enabled=true;
        if(configured) {
            var lines=File.ReadAllLines(PathAt(game,Owner));
            enabled=lines.Contains("enabled=1");
            int parsed;string value=lines.FirstOrDefault(line=>line.StartsWith("flags="));
            if(value!=null&&Int32.TryParse(value.Substring(6),out parsed)&&parsed>=0&&parsed<=127)flags=parsed;
        }
        return new PresenceState{installed=File.Exists(PathAt(game,"WowPresence.dll"))&&File.Exists(PathAt(game,"WowPresence.exe")),configured=configured,enabled=enabled,flags=flags};
    }
    internal static void StageArchive(byte[] bytes,string stage) {
        using(var zip=new ZipArchive(new MemoryStream(bytes)))foreach(string name in new[]{"WowPresence.dll","WowPresence.exe"}) {
            var entries=zip.Entries.Where(entry=>entry.FullName==name).ToArray();
            if(entries.Length!=1||entries[0].Length>8*1024*1024)throw new IOException("The Discord Presence package is incomplete.");
            using(var input=entries[0].Open())using(var data=new MemoryStream()) {
                input.CopyTo(data);byte[] image=data.ToArray();
                if(name.EndsWith(".dll"))ClientMods.ValidateDll(image);
                else {int pe=image.Length>=64?BitConverter.ToInt32(image,0x3c):-1;if(pe<0||pe>image.Length-24||image[0]!=0x4d||image[1]!=0x5a||BitConverter.ToUInt32(image,pe)!=0x4550||BitConverter.ToUInt16(image,pe+4)!=0x14c)throw new IOException("Invalid Discord Presence companion executable.");}
                ToolkitPackages.Save(stage,name,image);
            }
        }
    }
    internal static void StageSettings(string game,string stage,bool enabled,int flags) {
        if(flags<0||flags>127)throw new IOException("Invalid Discord Presence detail options.");
        if(enabled&&!(File.Exists(PathAt(stage,"WowPresence.dll"))&&File.Exists(PathAt(stage,"WowPresence.exe")))&&!State(game).installed)
            throw new IOException("Install Discord Presence before enabling it.");
        // The upstream runtime prefers its older managed directory when it exists.
        string folder=Directory.Exists(PathAt(game,".modernization_tool/WowPresence"))?".modernization_tool/WowPresence":"WowPresence";
        ToolkitInstall.CheckForLinks(game,PathAt(game,folder));
        string idFile=folder+"/discord_application_id";
        string appId=File.Exists(PathAt(game,idFile))?File.ReadAllText(PathAt(game,idFile)).Trim():"";
        if(appId.Length<16||appId.Length>20||!appId.All(Char.IsDigit))appId="1544072796098011176";
        ToolkitPackages.Save(stage,idFile.Replace('/',Path.DirectorySeparatorChar),Encoding.ASCII.GetBytes(appId+"\n"));
        ToolkitPackages.Save(stage,(folder+"/discord_broadcast_flags").Replace('/',Path.DirectorySeparatorChar),Encoding.ASCII.GetBytes(flags+"\n"));
        ToolkitPackages.Save(stage,Owner.Replace('/',Path.DirectorySeparatorChar),Encoding.ASCII.GetBytes("enabled="+(enabled?"1":"0")+"\nflags="+flags+"\n"));
        string loader=File.Exists(PathAt(stage,"dlls.txt"))?PathAt(stage,"dlls.txt"):PathAt(game,"dlls.txt");
        var lines=File.Exists(loader)?File.ReadAllLines(loader).ToList():new System.Collections.Generic.List<string>();
        lines.RemoveAll(line=>line.Trim().Equals("WowPresence.dll",StringComparison.OrdinalIgnoreCase));
        if(enabled)lines.Add("WowPresence.dll");
        ToolkitPackages.Save(stage,"dlls.txt",Encoding.UTF8.GetBytes(String.Join(Environment.NewLine,lines)+Environment.NewLine));
    }
}

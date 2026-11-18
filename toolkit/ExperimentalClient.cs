using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;

internal static class ExperimentalClient {
    internal const string DownloadUrl = "https://github.com/obs1deus/twilighthammertoolkit/releases/download/benilla-octo-2026-11-18/Benilla-Octo-Windows-x64.zip";
    internal const string PackageHash = "d01dd0c2603dfae1ca181dabce15cae84d562cc1a09c8f05a5c51ba352031390";
    internal const string SourceUrl = "https://octowow.st/git/grom/benilla-octo";
    private static readonly string[] Files = {"Benilla-Octo.exe", "realmlist.wtf", "LICENSE-MIT", "LICENSE-APACHE", "THIRD-PARTY.md"};

    internal static string Root(string game) {
        return ToolkitPackages.SafePath(game, Path.Combine(".twilight-toolkit", "clients", "Benilla-Octo"));
    }
    private static string BuildFolder(string game) {
        return Path.Combine(Root(game), "build-" + PackageHash.Substring(0, 12));
    }
    internal static string Executable(string game) {
        return Path.Combine(BuildFolder(game), "Benilla-Octo.exe");
    }
    internal static bool Installed(string game) {
        if(String.IsNullOrWhiteSpace(game))return false;
        string folder=BuildFolder(game);
        return Files.All(name=>File.Exists(Path.Combine(folder,name))) &&
            new FileInfo(Executable(game)).Length==133928960;
    }
    internal static void Install(string game,Action<long,long> progress) {
        byte[] bytes=ToolkitPackages.Download(DownloadUrl,80*1024*1024,progress:progress);
        InstallArchive(game,bytes);
    }
    internal static void InstallArchive(string game,byte[] bytes) {
        if(!Environment.Is64BitOperatingSystem)throw new IOException("Benilla-Octo requires 64-bit Windows.");
        if(!Directory.Exists(Path.Combine(game,"Data")))throw new IOException("Choose an OctoWoW folder containing its Data folder.");
        if(!ToolkitPackages.Hash(bytes).Equals(PackageHash,StringComparison.OrdinalIgnoreCase))
            throw new IOException("The Benilla download did not match its published checksum. Download it again.");
        string root=Root(game),target=BuildFolder(game);
        ToolkitInstall.CheckForLinks(game,target);
        Directory.CreateDirectory(root);
        string stage=Path.Combine(root,"install-"+Guid.NewGuid().ToString("N"));
        string backup=null;
        Directory.CreateDirectory(stage);
        try {
            using(var data=new MemoryStream(bytes,false))
            using(var archive=new ZipArchive(data,ZipArchiveMode.Read)) {
                if(archive.Entries.Count!=Files.Length||!Files.All(name=>archive.Entries.Count(entry=>entry.FullName==name)==1))
                    throw new IOException("The Benilla archive has unexpected contents.");
                foreach(string name in Files) {
                    var entry=archive.GetEntry(name);
                    if(entry.Length>150*1024*1024)throw new IOException("The Benilla package exceeds the installation limit.");
                    using(var input=entry.Open())
                    using(var output=File.Create(Path.Combine(stage,name)))input.CopyTo(output);
                }
            }
            if(new FileInfo(Path.Combine(stage,"Benilla-Octo.exe")).Length!=133928960)
                throw new IOException("The Benilla executable is incomplete.");
            if(Directory.Exists(target)) {
                backup=Path.Combine(root,"previous-"+Guid.NewGuid().ToString("N"));
                Directory.Move(target,backup);
            }
            try {Directory.Move(stage,target);}
            catch {if(backup!=null&&!Directory.Exists(target))Directory.Move(backup,target);throw;}
        } finally {
            if(Directory.Exists(stage))Directory.Delete(stage,true);
        }
    }
    internal static ProcessStartInfo StartInfo(string game) {
        if(!Installed(game))throw new IOException("Install Benilla-Octo from Experimental first.");
        string home=Path.Combine(Root(game),"benilla-config");
        ToolkitInstall.CheckForLinks(game,home);
        Directory.CreateDirectory(Path.Combine(home,"AddOns"));
        var start=new ProcessStartInfo(Executable(game)){WorkingDirectory=BuildFolder(game),UseShellExecute=false};
        start.EnvironmentVariables["WOW_DATA"]=Path.Combine(Path.GetFullPath(game),"Data");
        start.EnvironmentVariables["WOW_HOST"]="play.octowow.st";
        start.EnvironmentVariables["BENILLA_HOME"]=home;
        return start;
    }
}

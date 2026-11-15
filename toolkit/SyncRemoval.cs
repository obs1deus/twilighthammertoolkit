using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Threading;

internal static class SyncRemoval {
    private const string TaskName="Twilight Hammer Saved Capture Sync";
    internal static readonly string UploaderFolder=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"TwilightHammerSync");
    private static readonly string[] UploaderFiles={
        "TwilightHammerUploader.exe","agent.mjs","Run.ps1","HiddenLauncher.exe","LaunchHidden.vbs",
        "native-game.txt","native-approval.dpapi","config.json","key.dpapi"
    };
    private static readonly string[] AddonFiles={
        "TwilightHammerSync.toc","TwilightHammerSync.lua","AegisBridge.lua","AuctionBridges.lua","Edition.lua"
    };

    internal static void Run(string game,bool removeAddon) {
        if(removeAddon&&(Process.GetProcessesByName("WoW").Length>0||
            Process.GetProcessesByName("WoW_Modernized").Length>0||Process.GetProcessesByName("WoW_tweaked").Length>0))
            throw new IOException("Close OctoWoW before removing the WoW addon.");
        RemoveTask();
        RemoveFiles(UploaderFolder,game,removeAddon);
    }

    private static void RemoveTask() {
        dynamic service=Activator.CreateInstance(Type.GetTypeFromProgID("Schedule.Service"));
        service.Connect();
        dynamic folder=service.GetFolder("\\");
        dynamic task;
        try {task=folder.GetTask(TaskName);}
        catch(COMException error) {
            if((uint)error.ErrorCode==0x80070002U)return;
            throw;
        }
        string account=Convert.ToString(task.Definition.Principal.UserId);
        using(var current=WindowsIdentity.GetCurrent()) {
            string sid=current.User.Value;
            if(!account.Equals(current.Name,StringComparison.OrdinalIgnoreCase)&&!account.Equals(sid,StringComparison.OrdinalIgnoreCase)) {
                try {account=((SecurityIdentifier)new NTAccount(account).Translate(typeof(SecurityIdentifier))).Value;}
                catch(IdentityNotMappedException) {}
                if(!account.Equals(sid,StringComparison.OrdinalIgnoreCase))
                    throw new IOException("This scheduled task belongs to a different Windows account.");
            }
        }
        task.Enabled=false;
        try {task.Stop(0);}
        catch(COMException error) {
            if((uint)error.ErrorCode!=0x8004130BU)throw;
        }
        folder.DeleteTask(TaskName,0);
    }

    internal static void RemoveFiles(string uploader,string game,bool removeAddon) {
        foreach(string name in UploaderFiles)Delete(Path.Combine(uploader,name));
        if(!removeAddon)return;
        string addon=Path.Combine(game,"Interface","AddOns","TwilightHammerSync");
        if(!Directory.Exists(addon))return;
        foreach(string name in AddonFiles)Delete(Path.Combine(addon,name));
        using(var entries=Directory.EnumerateFileSystemEntries(addon).GetEnumerator()) {
            if(entries.MoveNext())return;
        }
        Directory.Delete(addon,false);
    }
    private static void Delete(string path) {
        if(!File.Exists(path))return;
        for(int attempt=0;;attempt++) {
            try {File.Delete(path);return;}
            catch(IOException) {if(attempt>=4)throw;Thread.Sleep(150);}
        }
    }
}

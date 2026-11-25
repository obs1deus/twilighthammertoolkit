using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

internal static class ClientCache {
    internal static bool Enabled(string game) {
        string marker=Path.Combine(game,".twilight-toolkit","clear-wdb.txt");
        return !File.Exists(marker)||File.ReadAllText(marker).Trim()=="1";
    }
    internal static void BeforeLaunch(string game) {
        if(!Enabled(game))return;
        string root=ToolkitPackages.SafePath(game,"WDB");
        ToolkitInstall.CheckForLinks(game,root);
        if(!Directory.Exists(root))return;
        var pending=new Queue<string>();var files=new List<string>();pending.Enqueue(root);
        long size=0;
        while(pending.Count>0) {
            string directory=pending.Dequeue();ToolkitInstall.CheckForLinks(game,directory);
            foreach(string file in Directory.GetFiles(directory)) {
                ToolkitInstall.CheckForLinks(game,file);files.Add(file);size+=new FileInfo(file).Length;
                if(files.Count>100000||size>512L*1024*1024)throw new IOException("WDB cache is unexpectedly large. Review it before clearing.");
            }
            foreach(string child in Directory.GetDirectories(directory))pending.Enqueue(child);
        }
        if(files.Count==0)return;
        string backup=ToolkitPackages.SafePath(game,Path.Combine(".twilight-toolkit","backups","wdb-"+Guid.NewGuid().ToString("N")));
        var changes=new List<KeyValuePair<string,string>>();
        try {
            foreach(string file in files) {
                string relative=file.Substring(root.Length+1),saved=ToolkitPackages.SafePath(backup,Path.Combine("WDB",relative));
                ToolkitInstall.CheckForLinks(game,saved);Directory.CreateDirectory(Path.GetDirectoryName(saved));
                File.Copy(file,saved);changes.Add(new KeyValuePair<string,string>(file,saved));File.Delete(file);
            }
            ToolkitHistory.Record(game,"Clear WDB cache before launch",changes);
            ToolkitLog.Write("launcher","Backed up and cleared "+files.Count+" WDB cache files.");
        } catch {ToolkitInstall.Restore(changes);throw;}
    }
}

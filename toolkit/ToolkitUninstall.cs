using System;
using System.Diagnostics;
using System.IO;
using System.Windows.Forms;

internal static class ToolkitUninstall {
    internal static void Start() {
        string target=Application.ExecutablePath;
        string helper=Path.Combine(Path.GetTempPath(),"TwilightHammerToolkit.uninstall-"+Guid.NewGuid().ToString("N")+".exe");
        File.Copy(target,helper);
        try {
            using(var process=Process.GetCurrentProcess())
                Process.Start(new ProcessStartInfo(helper,"--finish-toolkit-uninstall "+process.Id+" \""+target+"\""){UseShellExecute=false});
        } catch {File.Delete(helper);throw;}
    }
    internal static void Finish(string[] args) {
        try {
            int pid;
            string helper=Path.GetFullPath(Application.ExecutablePath);
            string prefix="TwilightHammerToolkit.uninstall-";
            string name=Path.GetFileNameWithoutExtension(helper);
            Guid id;
            if(args.Length!=3||!Int32.TryParse(args[1],out pid)||pid==Process.GetCurrentProcess().Id||
                !Path.GetDirectoryName(helper).Equals(Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar),StringComparison.OrdinalIgnoreCase)||
                !name.StartsWith(prefix,StringComparison.Ordinal)||!Guid.TryParseExact(name.Substring(prefix.Length),"N",out id))
                throw new IOException("Invalid launcher uninstall request.");
            string target=Path.GetFullPath(args[2]);
            if(target.Equals(helper,StringComparison.OrdinalIgnoreCase)||!File.Exists(target)||
                !ToolkitPackages.Hash(File.ReadAllBytes(target)).Equals(ToolkitPackages.Hash(File.ReadAllBytes(helper)),StringComparison.Ordinal))
                throw new IOException("The launcher executable could not be verified.");
            Process parent=null;
            try {parent=Process.GetProcessById(pid);} catch(ArgumentException) { }
            using(parent) {
                if(parent!=null) {
                    if(!parent.HasExited&&!Path.GetFullPath(parent.MainModule.FileName).Equals(target,StringComparison.OrdinalIgnoreCase))
                        throw new IOException("The launcher process could not be verified.");
                    if(!parent.WaitForExit(20000))throw new IOException("Close the launcher window and try again.");
                }
            }
            File.Delete(target);
            ToolkitLog.Write("toolkit","Uninstalled launcher executable: "+target);
            MessageBox.Show("The launcher was removed. Your game files, addons, Benilla, Sync uploader, and launcher preferences and logs were kept.",
                "Launcher uninstalled",MessageBoxButtons.OK,MessageBoxIcon.Information);
        } catch(Exception error) {
            ToolkitLog.Write("toolkit","Launcher uninstall failed: "+error.Message,"error");
            MessageBox.Show(error.Message,"Launcher uninstall failed",MessageBoxButtons.OK,MessageBoxIcon.Error);
        }
    }
}

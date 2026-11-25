using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows.Forms;

internal static class ToolkitSelfUpdate {
    internal static string Prepare(Release release,string target) {
        if(!release.Digest.StartsWith("sha256:",StringComparison.OrdinalIgnoreCase)||release.Digest.Length!=71)
            throw new IOException("GitHub did not publish a checksum for this update.");
        byte[] executable=ToolkitPackages.Download(release.Url,30*1024*1024);
        if(!ToolkitPackages.Hash(executable).Equals(release.Digest.Substring(7),StringComparison.OrdinalIgnoreCase))
            throw new IOException("The downloaded update did not match its published checksum.");
        string staged=target+".update-"+Guid.NewGuid().ToString("N")+".exe";
        try {
            File.WriteAllBytes(staged,executable);
            var metadata=AssemblyName.GetAssemblyName(staged);
            Version expected;
            if(metadata.Name!=Assembly.GetExecutingAssembly().GetName().Name||
               !Version.TryParse(release.Version.TrimStart('v'),out expected)||
               FileVersionInfo.GetVersionInfo(staged).ProductVersion!=expected.ToString())
                throw new IOException("The downloaded executable does not match the Launcher release.");
            return staged;
        } catch {
            if(File.Exists(staged))File.Delete(staged);
            throw;
        }
    }

    internal static void Start(string staged,string target) {
        int pid;
        using(var current=Process.GetCurrentProcess())pid=current.Id;
        Process.Start(new ProcessStartInfo(staged,"--apply-toolkit-update "+pid+" \""+target+"\""){UseShellExecute=false});
    }

    internal static void Apply(string[] args) {
        string source=Application.ExecutablePath;
        string temporary=null;
        try {
            int pid;
            if(args.Length!=3||!Int32.TryParse(args[1],out pid))throw new IOException("Invalid update arguments.");
            string target=Path.GetFullPath(args[2]);
            if(!source.StartsWith(target+".update-",StringComparison.OrdinalIgnoreCase)||
               !File.Exists(target)||AssemblyName.GetAssemblyName(target).Name!=Assembly.GetExecutingAssembly().GetName().Name)
                throw new IOException("The original Launcher executable could not be verified.");
            Process parent=null;
            try {parent=Process.GetProcessById(pid);} catch(ArgumentException) { }
            using(parent) {
                if(parent!=null&&!parent.WaitForExit(20000))throw new IOException("Close the old Launcher window and try again.");
            }
            temporary=target+"."+Guid.NewGuid().ToString("N")+".tmp";
            string backup=temporary+".previous";
            File.Copy(source,temporary);
            File.Replace(temporary,target,backup);
            try {
                Process.Start(new ProcessStartInfo(target,"--clean-toolkit-update \""+source+"\""){UseShellExecute=false});
            } catch {
                File.Replace(backup,target,null);
                throw;
            }
            ToolkitLog.Write("selfUpdater","Updated Launcher to "+ToolkitPackages.Version+".");
        } catch(Exception error) {
            ToolkitLog.Write("selfUpdater","Could not apply the Launcher update ("+error.GetType().Name+").","error");
            MessageBox.Show(error.Message,"Launcher update failed",MessageBoxButtons.OK,MessageBoxIcon.Error);
        } finally {
            if(temporary!=null&&File.Exists(temporary))File.Delete(temporary);
        }
    }

    internal static void Clean(string path) {
        string prefix=Application.ExecutablePath+".update-";
        if(!path.StartsWith(prefix,StringComparison.OrdinalIgnoreCase)||
           !path.EndsWith(".exe",StringComparison.OrdinalIgnoreCase))return;
        Guid id;
        if(!Guid.TryParseExact(path.Substring(prefix.Length,path.Length-prefix.Length-4),"N",out id))return;
        for(int attempt=0;attempt<8;attempt++) {
            try {File.Delete(path);return;}
            catch(IOException) {System.Threading.Thread.Sleep(100);}
            catch(UnauthorizedAccessException) {return;}
        }
    }
}

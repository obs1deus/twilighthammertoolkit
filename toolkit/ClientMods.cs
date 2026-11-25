using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

internal static class ClientMods {
    internal static bool Pinned(string name) {return ClientModSources.Entries.ContainsKey(name);}
    internal static Release Resolve(string name) {
        var source=ClientModSources.Entries[name];
        return new Release{File=source[0],Url=source[1],Digest="sha256:"+source[2],Version=source[3]+"-"+source[2].Substring(0,12)};
    }
    internal static string Library(Package package) {
        if(package.Name=="Discord Presence")return "WowPresence.dll";
        if(Pinned(package.Name))return ClientModSources.Entries[package.Name][0];
        return package.Name=="SuperWoW"?"SuperWoWhook.dll":package.Name+".dll";
    }
    internal static void ValidateDll(byte[] data) {
        if(data.Length<256||data[0]!=0x4d||data[1]!=0x5a)throw new IOException("The downloaded plugin is not a Windows DLL.");
        int pe=BitConverter.ToInt32(data,0x3c);
        if(pe<0||pe>data.Length-24||BitConverter.ToUInt32(data,pe)!=0x4550||BitConverter.ToUInt16(data,pe+4)!=0x14c||
            (BitConverter.ToUInt16(data,pe+22)&0x2000)==0)
            throw new IOException("The downloaded plugin is not a 32-bit Windows DLL.");
    }
    internal static void StageMultiMonitor(byte[] bytes,string stage) {
        using(var zip=new ZipArchive(new MemoryStream(bytes)))foreach(string name in new[]{"VanillaMultiMonitorFix.dll","VMMFix_preferred_monitor.txt"}) {
            var entry=zip.GetEntry(name);
            if(entry==null||entry.Length>8*1024*1024)throw new IOException("The monitor-fix package is incomplete.");
            using(var input=entry.Open())using(var data=new MemoryStream()) {
                input.CopyTo(data);byte[] image=data.ToArray();if(name.EndsWith(".dll"))ValidateDll(image);
                ToolkitPackages.Save(stage,name,image);
            }
        }
    }
    private static readonly string[] LoginFiles={"AutoLogin.lua","AutoLogin.xml","GlueXML.toc"};
    private const string UltrawideScript="UltrawideLogin.xml";
    private static readonly string GlueFolder=Path.Combine("Data","Interface","GlueXML");
    private static readonly string GlueToc=Path.Combine(GlueFolder,"GlueXML.toc");
    private static readonly string UltrawidePath=Path.Combine(GlueFolder,UltrawideScript);
    internal static bool IsGluePath(string relative) {
        string path=relative.Replace('/',Path.DirectorySeparatorChar);
        return path.StartsWith(GlueFolder+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)&&
            (LoginFiles.Contains(Path.GetFileName(path),StringComparer.OrdinalIgnoreCase)||path.Equals(UltrawidePath,StringComparison.OrdinalIgnoreCase));
    }
    internal static bool LoginInstalled(string game) {
        return LoginFiles.All(name=>File.Exists(Path.Combine(game,"Data","Interface","GlueXML",name)))&&
            File.ReadLines(Path.Combine(game,"Data","Interface","GlueXML","GlueXML.toc")).Any(line=>line.Trim().Equals("AutoLogin.xml",StringComparison.OrdinalIgnoreCase));
    }
    internal static void StageDisableLogin(string game,string stage) {
        string relative=Path.Combine("Data","Interface","GlueXML","GlueXML.toc");
        string path=Path.Combine(game,relative);
        if(File.Exists(path)) {
            var lines=File.ReadAllLines(path).Where(line=>!line.Trim().Equals("AutoLogin.xml",StringComparison.OrdinalIgnoreCase));
            ToolkitPackages.Save(stage,relative,Encoding.UTF8.GetBytes(String.Join(Environment.NewLine,lines)+Environment.NewLine));
        }
    }
    internal static void StageLogin(byte[] bytes,string stage) {
        using(var zip=new ZipArchive(new MemoryStream(bytes))) {
            foreach(string name in LoginFiles) {
                string suffix="/Data/Interface/GlueXML/"+name;
                var entries=zip.Entries.Where(e=>e.FullName.EndsWith(suffix,StringComparison.Ordinal)).ToArray();
                if(entries.Length!=1||entries[0].Length>2*1024*1024)throw new IOException("The Auto Login source archive is incomplete.");
                using(var stream=entries[0].Open())using(var data=new MemoryStream()) {
                    stream.CopyTo(data);
                    ToolkitPackages.Save(stage,Path.Combine("Data","Interface","GlueXML",name),data.ToArray());
                }
            }
        }
    }
    private static byte[] AppendTocEntry(byte[] bytes,string name) {
        using(var reader=new StreamReader(new MemoryStream(bytes))) {
            string content=reader.ReadToEnd();
            if(content.Split('\r','\n').Any(line=>line.Trim().Equals(name,StringComparison.OrdinalIgnoreCase)))return bytes;
            string newline=content.Contains("\n")&&!content.Contains("\r\n")?"\n":Environment.NewLine;
            string separator=content.Length==0||content.EndsWith("\n")||content.EndsWith("\r")?"":newline;
            return bytes.Concat(reader.CurrentEncoding.GetBytes(separator+name+newline)).ToArray();
        }
    }
    private static byte[] UltrawideAsset() {
        using(var input=typeof(ClientMods).Assembly.GetManifestResourceStream(UltrawideScript))
        using(var bytes=new MemoryStream()) {
            if(input==null)throw new IOException("The ultrawide login support file is missing from the launcher.");
            input.CopyTo(bytes);
            return bytes.ToArray();
        }
    }
    internal static void StageUltrawideLogin(string game,string stage) {
        string toc=ToolkitPackages.SafePath(game,GlueToc);
        ToolkitInstall.CheckForLinks(game,toc);
        ToolkitInstall.CheckForLinks(game,ToolkitPackages.SafePath(game,UltrawidePath));
        if(!File.Exists(toc))toc=ToolkitPackages.SafePath(stage,GlueToc);
        byte[] withLogin=AppendTocEntry(File.ReadAllBytes(toc),"AutoLogin.xml");
        ToolkitPackages.Save(stage,GlueToc,AppendTocEntry(withLogin,UltrawideScript));
        ToolkitPackages.Save(stage,UltrawidePath,UltrawideAsset());
    }
    internal static bool EnsureUltrawideLogin(string game,Action verifyGameClosed=null) {
        string toc=ToolkitPackages.SafePath(game,GlueToc);
        ToolkitInstall.CheckForLinks(game,toc);
        if(!File.Exists(toc))return false;
        using(OctoPatch.OpenPatchLock(game)) {
            if(!LoginInstalled(game))return false;
            string script=ToolkitPackages.SafePath(game,UltrawidePath);
            ToolkitInstall.CheckForLinks(game,script);
            byte[] original=File.ReadAllBytes(toc),updated=AppendTocEntry(original,UltrawideScript),asset=UltrawideAsset();
            bool tocChanged=!Object.ReferenceEquals(original,updated);
            bool scriptChanged=!File.Exists(script)||!File.ReadAllBytes(script).SequenceEqual(asset);
            if(!tocChanged&&!scriptChanged)return false;
            if(ToolkitHistory.List(game).Any(entry=>entry.Restored&&entry.Files.Any(file=>
                file.Path.Replace('/',Path.DirectorySeparatorChar).Equals(UltrawidePath,StringComparison.OrdinalIgnoreCase))))return false;
            (verifyGameClosed??ToolkitInstall.EnsureClosed)();
            string id=Guid.NewGuid().ToString("N");
            string stage=Path.Combine(Path.GetTempPath(),"TwilightLogin-"+id);
            string backup=ToolkitPackages.SafePath(game,Path.Combine(".twilight-toolkit","backups","ultrawide-"+id));
            var changes=new List<KeyValuePair<string,string>>();
            try {
                // Install the resource first so the TOC always points to an existing script.
                if(scriptChanged)StageLoginChange(game,stage,backup,UltrawidePath,asset,changes);
                if(tocChanged)StageLoginChange(game,stage,backup,GlueToc,updated,changes);
                ToolkitHistory.Record(game,"Ultrawide login side panels",changes);
                return true;
            } catch {
                ToolkitInstall.Restore(changes);
                throw;
            } finally {
                try {if(Directory.Exists(stage))Directory.Delete(stage,true);} catch(IOException) { } catch(UnauthorizedAccessException) { }
            }
        }
    }
    private static void StageLoginChange(string game,string stage,string backup,string relative,byte[] contents,
        List<KeyValuePair<string,string>> changes) {
        string target=ToolkitPackages.SafePath(game,relative),saved=ToolkitPackages.SafePath(backup,relative);
        ToolkitInstall.CheckForLinks(game,target);
        ToolkitInstall.CheckForLinks(game,saved);
        ToolkitPackages.Save(stage,relative,contents);
        ToolkitInstall.Replace(ToolkitPackages.SafePath(stage,relative),target,saved,changes);
    }
    private static readonly int[] GlueOffsets={0x2f113a,0x2f113b,0x2f1158,0x2f11a7,0x2f11f0,0x2f11f1};
    private static readonly byte[] GlueBytes={0xeb,0x19,0x03,0x03,0xeb,0xb2};
    internal static void PrepareLoginClient(string game,string stage) {
        string staged=Path.Combine(stage,"WoW_Modernized.exe");
        string path=File.Exists(staged)?staged:Path.Combine(game,"WoW_Modernized.exe");
        if(!File.Exists(path))throw new IOException("Auto Login requires the supported client with custom login-screen support (WoW_Modernized.exe).");
        byte[] image=File.ReadAllBytes(path);
        ClientTweaks.ValidateImage(game,image);
        bool patched=GlueOffsets.Select((offset,index)=>image[offset]==GlueBytes[index]).All(value=>value);
        if(!patched) {
            for(int i=0;i<GlueOffsets.Length;i++)image[GlueOffsets[i]]=GlueBytes[i];
            ToolkitPackages.Save(stage,"WoW_Modernized.exe",image);
            ToolkitPackages.Save(stage,Path.Combine(".twilight-toolkit","toolkit-client.sha256"),Encoding.ASCII.GetBytes(ToolkitPackages.Hash(image)));
        }
    }
    internal static string EncryptionKey() {
        string key=Environment.GetEnvironmentVariable("WOW_ENCRYPTION_KEY");
        if(String.IsNullOrEmpty(key))key=Environment.GetEnvironmentVariable("WOW_ENCRYPTION_KEY",EnvironmentVariableTarget.User);
        if(String.IsNullOrEmpty(key)) {
            byte[] bytes=new byte[32];using(var random=RandomNumberGenerator.Create())random.GetBytes(bytes);
            key=BitConverter.ToString(bytes).Replace("-","");
            Environment.SetEnvironmentVariable("WOW_ENCRYPTION_KEY",key,EnvironmentVariableTarget.User);
        }
        Environment.SetEnvironmentVariable("WOW_ENCRYPTION_KEY",key);
        return key;
    }
    internal static void ConfigureLaunch(string game,string executable,ProcessStartInfo start) {
        DxvkSettings.ConfigureLaunch(game,start);
        if(!Path.GetFileName(executable).Equals("VanillaFixes.exe",StringComparison.OrdinalIgnoreCase))return;
        string client=Path.Combine(game,"WoW_Modernized.exe");
        if(File.Exists(client))start.Arguments="\""+client+"\"";
        else if(LoginInstalled(game))throw new IOException("Auto Login is installed, but its supported client is missing.");
        if(LoginInstalled(game))start.EnvironmentVariables["WOW_ENCRYPTION_KEY"]=EncryptionKey();
    }
}

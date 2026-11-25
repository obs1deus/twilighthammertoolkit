using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

internal static class DxvkSettings {
    internal const string Library="d3d9.dll";
    internal const string Bundled="DXVK (VanillaFixes)";
    internal const string Modern="DXVK 2.7.1";
    internal const string Cursor="Bigger Mouse Cursor (DXVK)";
    internal const string StatePath=".twilight-toolkit/dxvk/active.txt";
    private const int MaximumLibrary=16*1024*1024;
    private const int MaximumConfig=256*1024;
    private const string Configuration="dxvk.conf";
    private static readonly Dictionary<string,string[]> Sources=new Dictionary<string,string[]> {
        {Bundled,new[]{"vanillafixes-1.5.3-dxvk.zip","https://github.com/hannesmann/vanillafixes/releases/download/v1.5.3/vanillafixes-1.5.3-dxvk.zip","a02a238fdefc9eb5f70a3c85c0c803d6b8667f951f531f3ad37b44f83fdb4b17","v2.6.1"}},
        {Modern,new[]{"dxvk-2.7.1.tar.gz","https://github.com/doitsujin/dxvk/releases/download/v2.7.1/dxvk-2.7.1.tar.gz","d85ce7c79f57ecd765aaa1b9e7007cb875e6fde9f6d331df799bce73d513ce87","v2.7.1"}},
        {Cursor,new[]{Library,"https://raw.githubusercontent.com/RetroCro/TurtleWoW-Mods/99356bb58dedbdf7b756c1cfed7abaf33ac0ccab/Archive/d3d9.dll","768672bb9a1464e7f64f15793d226ace379a7f6830ba774bd3f72026cc6b2219","99356bb"}}
    };

    internal static bool IsVariant(string name) {return Sources.ContainsKey(name);}
    internal static bool IsManagedPath(string relative) {
        string path=relative.Replace('\\','/');
        return path.Equals(Library,StringComparison.OrdinalIgnoreCase)||path.Equals(StatePath,StringComparison.OrdinalIgnoreCase)||
            Regex.IsMatch(path,@"^\.twilight-toolkit/dxvk/[0-9a-f]{16}/(?:d3d9\.dll|dxvk\.conf|sha256\.txt)$",RegexOptions.IgnoreCase);
    }

    internal static Release Resolve(string name) {
        var source=Sources[name];
        return new Release {File=source[0],Url=source[1],Digest="sha256:"+source[2],Version=source[3]};
    }

    private static string CachePath(string name,string file) {
        return Path.Combine(".twilight-toolkit","dxvk",Sources[name][2].Substring(0,16),file);
    }

    private static string CheckedPath(string root,string relative) {
        string path=ToolkitPackages.SafePath(root,relative);
        ToolkitInstall.CheckForLinks(root,path);
        return path;
    }

    private static string FileHash(string path) {
        var file=new FileInfo(path);
        return file.Exists&&file.Length<=MaximumLibrary?ToolkitPackages.Hash(File.ReadAllBytes(path)):null;
    }

    internal static bool Cached(string game,string name) {
        try {
            string receipt=CheckedPath(game,CachePath(name,"sha256.txt"));
            if(!File.Exists(receipt)||new FileInfo(receipt).Length!=64)return false;
            return File.ReadAllText(receipt)==FileHash(CheckedPath(game,CachePath(name,Library)));
        } catch(IOException) {return false;}
          catch(UnauthorizedAccessException) {return false;}
    }

    private static string[] Owner(string game) {
        string path=CheckedPath(game,StatePath);
        if(!File.Exists(path)||new FileInfo(path).Length>256)return null;
        string[] state=File.ReadAllText(path).Split('\t');
        return state.Length==2&&IsVariant(state[0])&&Regex.IsMatch(state[1],"^[0-9a-f]{64}$")?state:null;
    }

    internal static string Active(string game) {
        try {
            var owner=Owner(game);
            return owner!=null&&owner[1]==FileHash(CheckedPath(game,Library))?owner[0]:null;
        } catch(IOException) {return null;}
          catch(UnauthorizedAccessException) {return null;}
    }

    internal static void StagePackage(string name,byte[] bytes,string stage) {
        byte[] library=bytes;
        if(name==Modern)library=ReadTarLibrary(bytes);
        if(name==Bundled)using(var archive=new ZipArchive(new MemoryStream(bytes))) {
            library=ReadZipFile(archive,Library,MaximumLibrary);
            ToolkitPackages.Save(stage,CachePath(name,Configuration),ReadZipFile(archive,Configuration,MaximumConfig));
        }
        ClientMods.ValidateDll(library);
        ToolkitPackages.Save(stage,CachePath(name,Library),library);
        ToolkitPackages.Save(stage,CachePath(name,"sha256.txt"),Encoding.ASCII.GetBytes(ToolkitPackages.Hash(library)));
    }

    private static byte[] ReadZipFile(ZipArchive archive,string name,int limit) {
        var entry=archive.Entries.SingleOrDefault(item=>item.Name.Equals(name,StringComparison.OrdinalIgnoreCase));
        if(entry==null||entry.Length<=0||entry.Length>limit)throw new IOException("The VanillaFixes DXVK package is incomplete.");
        var data=new byte[(int)entry.Length];
        using(var input=entry.Open())ReadExactly(input,data,data.Length);
        return data;
    }

    private static byte[] ReadTarLibrary(byte[] compressed) {
        using(var input=new GZipStream(new MemoryStream(compressed),CompressionMode.Decompress)) {
            var buffer=new byte[8192];
            int remaining=128*1024*1024;
            while(remaining>512) {
                ReadExactly(input,buffer,512);
                string name=Encoding.ASCII.GetString(buffer,0,100).TrimEnd('\0');
                if(name.Length==0)break;
                string octalSize=Encoding.ASCII.GetString(buffer,124,12).Trim('\0',' ');
                if(!Regex.IsMatch(octalSize,"^[0-7]{1,11}$"))break;
                long headerSize=Convert.ToInt64(octalSize,8);
                if(headerSize>remaining-512)break;
                int size=(int)headerSize,padded=(size+511)/512*512;
                remaining-=512+padded;
                if(remaining<0)break;
                if(name.EndsWith("/x32/"+Library,StringComparison.Ordinal)) {
                    if(size<=0||size>MaximumLibrary||(buffer[156]!=0&&buffer[156]!='0'))break;
                    var data=new byte[size];
                    ReadExactly(input,data,size);
                    return data;
                }
                for(int left=padded;left>0;) {
                    int chunk=Math.Min(left,buffer.Length);
                    ReadExactly(input,buffer,chunk);
                    left-=chunk;
                }
            }
        }
        throw new IOException("The DXVK package does not contain its 32-bit D3D9 library.");
    }

    private static void ReadExactly(Stream input,byte[] data,int count) {
        for(int offset=0;offset<count;) {
            int read=input.Read(data,offset,count-offset);
            if(read==0)throw new IOException("The renderer package ended unexpectedly.");
            offset+=read;
        }
    }

    internal static void Normalize(string game,Dictionary<string,bool> changes) {
        var selected=changes.Where(pair=>pair.Value&&IsVariant(pair.Key)).Select(pair=>pair.Key).ToArray();
        if(selected.Length>1)throw new IOException("Choose one DXVK renderer.");
        bool nextCursor=selected.Contains(Cursor),bigCursor;
        bool nextBigCursor=changes.TryGetValue("BigCursor",out bigCursor)&&bigCursor;
        if(nextCursor&&nextBigCursor)throw new IOException("Choose one larger cursor option.");
        if(selected.Length==0&&!nextBigCursor)return;
        string active=Active(game);
        if((selected.Length>0&&active!=null&&active!=selected[0])||(nextBigCursor&&active==Cursor))changes[active]=false;
        if(nextCursor&&ToolkitInstall.DependencyPresent(game,ToolkitPackages.Catalog.Single(package=>package.Name=="BigCursor")))
            changes["BigCursor"]=false;
    }

    internal static bool StageSwitch(string game,string stage,Dictionary<string,bool> changes) {
        string target=CheckedPath(game,Library);
        string active=Active(game);
        string desired=changes.Where(pair=>pair.Value&&IsVariant(pair.Key)).Select(pair=>pair.Key).SingleOrDefault();
        if((File.Exists(target)||Directory.Exists(target))&&active==null)
            throw new IOException("This game folder has a D3D9 library managed outside Twilight Hammer. Move or disable it with the tool that installed it before changing DXVK here.");
        bool disable=active!=null&&changes.ContainsKey(active)&&!changes[active];
        if(desired==null&&!disable)return false;
        if(desired==null) {
            ToolkitPackages.Save(stage,StatePath,new byte[0]);
            return true;
        }

        string packageRoot=Cached(stage,desired)?stage:game;
        if(!Cached(packageRoot,desired))throw new IOException("Download the selected DXVK renderer before enabling it.");
        byte[] library=File.ReadAllBytes(CheckedPath(packageRoot,CachePath(desired,Library)));
        ClientMods.ValidateDll(library);
        ToolkitPackages.Save(stage,Library,library);
        ToolkitPackages.Save(stage,StatePath,Encoding.UTF8.GetBytes(desired+"\t"+ToolkitPackages.Hash(library)));
        return false;
    }

    internal static void ConfigureLaunch(string game,ProcessStartInfo start) {
        string active=Active(game);
        if(active==Bundled&&String.IsNullOrEmpty(start.EnvironmentVariables["DXVK_CONFIG_FILE"])&&
            !File.Exists(Path.Combine(game,Configuration))) {
            string config=CheckedPath(game,CachePath(Bundled,Configuration));
            if(File.Exists(config))start.EnvironmentVariables["DXVK_CONFIG_FILE"]=config;
        }
        if(active==Cursor) {
            const string cursorSetting="d3d9.enlargeHardwareCursor = 2";
            string existing=start.EnvironmentVariables["DXVK_CONFIG"];
            start.EnvironmentVariables["DXVK_CONFIG"]=String.IsNullOrWhiteSpace(existing)?cursorSetting:existing+"; "+cursorSetting;
        }
    }
}

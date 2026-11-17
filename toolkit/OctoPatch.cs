using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Web.Script.Serialization;

internal sealed class OctoPatchInfo {
    public string State {get;set;}
    public string Revision {get;set;}
    public long ArchiveBytes {get;set;}
    public int ArchiveCount {get;set;}
}

internal static class OctoPatch {
    private const string TorrentUrl="https://dl.octowow.st/download/client.torrent";
    private const string FileRoot="https://dl.octowow.st/client/client/";
    private const int MetadataLimit=4*1024*1024;
    private const string StateFile=".twilight-toolkit\\octowow-client.torrent";

    private static readonly HashSet<string> CoreBinaries=new HashSet<string>(
        new[]{"WoW.exe","DivxDecoder.dll","dbghelp.dll","fmod.dll","ijl15.dll","unicows.dll"},StringComparer.OrdinalIgnoreCase);

    private sealed class ClientFile {
        internal string Path;
        internal long Start, Length;
        internal long End {get {return Start+Length;}}
        internal bool Managed {
            get {
                return CoreBinaries.Contains(Path)||
                    (Path.StartsWith("Interface/AddOns/Blizzard_",StringComparison.OrdinalIgnoreCase)&&
                     Path.EndsWith(".pub",StringComparison.OrdinalIgnoreCase))||
                    (Path.StartsWith("Data/",StringComparison.OrdinalIgnoreCase)&&
                     !Path.Equals("Data/patch-O.mpq",StringComparison.OrdinalIgnoreCase));
            }
        }
    }

    private sealed class Torrent {
        internal byte[] Data;
        internal string Revision;
        internal int PieceSize;
        internal byte[] Pieces;
        internal long Length;
        internal readonly List<ClientFile> Files=new List<ClientFile>();
    }

    internal static OctoPatchInfo Check(string game) {
        var next=DownloadTorrent();
        var previous=ReadBaseline(game,next);
        var files=Plan(game,next,previous);
        return new OctoPatchInfo {
            State=files.Count>0?"available":previous==null?"unverified":"current",
            Revision=next.Revision.Substring(0,8),
            ArchiveCount=files.Count,
            ArchiveBytes=files.Sum(file=>file.Length)
        };
    }

    internal static OctoPatchInfo Apply(string game,Action<string,long,long> progress) {
        ToolkitInstall.EnsureClosed();
        string state=ToolkitPackages.SafePath(game,StateFile);
        string lockFile=ToolkitPackages.SafePath(game,@".twilight-toolkit\game-patch.lock");
        ToolkitInstall.CheckForLinks(game,state);
        ToolkitInstall.CheckForLinks(game,lockFile);
        Directory.CreateDirectory(Path.GetDirectoryName(state));
        using(var patchLock=new FileStream(lockFile,FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None))
            return ApplyCore(game,state,progress);
    }

    private static OctoPatchInfo ApplyCore(string game,string state,Action<string,long,long> progress) {
        progress("Checking for game updates…",0,0);
        var next=DownloadTorrent();
        var previous=ReadBaseline(game,next);
        var files=Plan(game,next,previous);
        if(previous==null) {
            files=new List<ClientFile>();
            foreach(var file in next.Files.Where(item=>item.Managed)) {
                progress("Verifying "+file.Path+"…",0,0);
                if(!MatchesLocal(game,next,file))files.Add(file);
            }
        }

        long total=files.Sum(file=>file.Length),done=0;
        var drive=new DriveInfo(Path.GetPathRoot(game));
        long backupBytes=0;
        foreach(var file in files) {
            var existing=new FileInfo(ToolkitPackages.SafePath(game,file.Path));
            if(existing.Exists)backupBytes+=existing.Length;
        }
        long largest=files.Count==0?0:files.Max(file=>file.Length);
        if(drive.AvailableFreeSpace<total+backupBytes+largest+256L*1024*1024)
            throw new IOException("There is not enough free space to stage the update and back up the existing files.");

        string id=Guid.NewGuid().ToString("N");
        string stage=ToolkitPackages.SafePath(game,Path.Combine(".twilight-toolkit","patch-"+id));
        string backup=ToolkitPackages.SafePath(game,Path.Combine(".twilight-toolkit","backups","game-patch-"+id));
        ToolkitInstall.CheckForLinks(game,stage);
        ToolkitInstall.CheckForLinks(game,backup);
        var changes=new List<KeyValuePair<string,string>>();
        Directory.CreateDirectory(stage);
        try {
            foreach(var file in files) {
                string target=ToolkitPackages.SafePath(stage,file.Path);
                Directory.CreateDirectory(Path.GetDirectoryName(target));
                using(var output=File.Create(target)) {
                    EachBatch(next,file,(start,bytes)=>{
                        ReadBatch(next,start,bytes,null,null);
                        VerifyPieces(next,start,bytes);
                        long begin=Math.Max(start,file.Start),end=Math.Min(start+bytes.Length,file.End);
                        output.Write(bytes,(int)(begin-start),(int)(end-begin));
                        done+=end-begin;
                        progress("Downloading "+file.Path+"…",done,total);
                    });
                }
            }

            ToolkitInstall.EnsureClosed();
            progress("Applying verified game files…",total,total);
            foreach(var file in files) {
                string target=ToolkitPackages.SafePath(game,file.Path);
                ToolkitInstall.CheckForLinks(game,target);
                ToolkitInstall.Replace(ToolkitPackages.SafePath(stage,file.Path),target,
                    ToolkitPackages.SafePath(backup,file.Path),changes);
            }
            string metadata=Path.Combine(stage,"client.torrent");
            File.WriteAllBytes(metadata,next.Data);
            ToolkitInstall.Replace(metadata,state,Path.Combine(backup,"client.torrent"),changes);
        } catch {
            ToolkitInstall.Restore(changes);
            throw;
        } finally {
            try {Directory.Delete(stage,true);}
            catch(IOException) {}
            catch(UnauthorizedAccessException) {}
        }
        return new OctoPatchInfo {State="current",Revision=next.Revision.Substring(0,8)};
    }

    private static List<ClientFile> Plan(string game,Torrent next,Torrent previous) {
        var old=previous==null?null:previous.Files.ToDictionary(file=>file.Path,StringComparer.OrdinalIgnoreCase);
        return next.Files.Where(file=>{
            if(!file.Managed)return false;
            var local=new FileInfo(ToolkitPackages.SafePath(game,file.Path));
            if(!local.Exists||local.Length!=file.Length)return true;
            if(previous==null||previous.Revision==next.Revision)return false;
            ClientFile former;
            if(!old.TryGetValue(file.Path,out former)||former.Length!=file.Length)return true;
            if(file.Length==0)return false;
            if(previous.PieceSize!=next.PieceSize||
               former.Start%previous.PieceSize!=file.Start%next.PieceSize)return true;
            int pieces=(int)((file.End-1)/next.PieceSize-file.Start/next.PieceSize+1);
            return !previous.Pieces.Skip((int)(former.Start/previous.PieceSize)*20).Take(pieces*20)
                .SequenceEqual(next.Pieces.Skip((int)(file.Start/next.PieceSize)*20).Take(pieces*20));
        }).ToList();
    }

    private static bool MatchesLocal(string game,Torrent torrent,ClientFile file) {
        string path=ToolkitPackages.SafePath(game,file.Path);
        ToolkitInstall.CheckForLinks(game,path);
        var local=new FileInfo(path);
        if(!local.Exists||local.Length!=file.Length)return false;
        using(var input=File.OpenRead(path)) {
            try {
                EachBatch(torrent,file,(start,bytes)=>{
                    ReadBatch(torrent,start,bytes,file,input);
                    VerifyPieces(torrent,start,bytes);
                });
            } catch(BadPiece) {return false;}
        }
        return true;
    }

    private static void EachBatch(Torrent torrent,ClientFile file,Action<long,byte[]> action) {
        if(file.Length==0)return;
        long start=file.Start/torrent.PieceSize*torrent.PieceSize;
        long stop=Math.Min(torrent.Length,((file.End-1)/torrent.PieceSize+1)*torrent.PieceSize);
        while(start<stop) {
            int size=(int)Math.Min(4L*torrent.PieceSize,stop-start);
            action(start,new byte[size]);
            start+=size;
        }
    }

    private static void ReadBatch(Torrent torrent,long start,byte[] bytes,ClientFile local,Stream input) {
        long stop=start+bytes.Length;
        foreach(var source in torrent.Files) {
            long begin=Math.Max(start,source.Start),end=Math.Min(stop,source.End);
            if(end<=begin)continue;
            int dst=(int)(begin-start),count=(int)(end-begin);
            long offset=begin-source.Start;
            if(source==local) {
                input.Position=offset;
                ReadExactly(input,bytes,dst,count);
                continue;
            }
            var request=Request(FileRoot+String.Join("/",source.Path.Split('/').Select(Uri.EscapeDataString)));
            request.AddRange(offset,offset+count-1);
            using(var response=(HttpWebResponse)request.GetResponse())
            using(var stream=response.GetResponseStream()) {
                string expected=String.Format(CultureInfo.InvariantCulture,"bytes {0}-{1}/{2}",offset,offset+count-1,source.Length);
                if(response.StatusCode!=HttpStatusCode.PartialContent||
                   !String.Equals(response.Headers["Content-Range"],expected,StringComparison.OrdinalIgnoreCase))
                    throw new IOException("OctoWoW's CDN did not return the requested byte range for "+source.Path+".");
                ReadExactly(stream,bytes,dst,count);
            }
        }
    }

    private sealed class BadPiece : IOException {
        internal BadPiece():base("Downloaded data did not match OctoWoW's published patch. Retry the update.") {}
    }

    private static void VerifyPieces(Torrent torrent,long start,byte[] bytes) {
        using(var sha=SHA1.Create()) {
            for(int offset=0;offset<bytes.Length;offset+=torrent.PieceSize) {
                var hash=sha.ComputeHash(bytes,offset,Math.Min(torrent.PieceSize,bytes.Length-offset));
                int index=(int)((start+offset)/torrent.PieceSize)*20;
                for(int i=0;i<20;i++)if(hash[i]!=torrent.Pieces[index+i])throw new BadPiece();
            }
        }
    }

    private static void ReadExactly(Stream stream,byte[] bytes,int offset,int count) {
        while(count>0) {
            int read=stream.Read(bytes,offset,count);
            if(read==0)throw new IOException("The update download ended unexpectedly.");
            offset+=read;
            count-=read;
        }
    }

    private static HttpWebRequest Request(string url) {
        var request=(HttpWebRequest)WebRequest.Create(url);
        request.Timeout=15000;
        request.ReadWriteTimeout=30000;
        request.AllowAutoRedirect=false;
        return request;
    }

    private static Torrent DownloadTorrent() {
        using(var response=(HttpWebResponse)Request(TorrentUrl).GetResponse())
        using(var input=response.GetResponseStream())
        using(var output=new MemoryStream()) {
            if(response.StatusCode!=HttpStatusCode.OK)throw new IOException("Patch feed returned an unexpected response.");
            var buffer=new byte[8192];
            int count;
            while((count=input.Read(buffer,0,buffer.Length))>0) {
                if(output.Length+count>MetadataLimit)throw new IOException("Patch metadata is too large.");
                output.Write(buffer,0,count);
            }
            return Parse(output.ToArray());
        }
    }

    private static Torrent ReadBaseline(string game,Torrent next) {
        var own=ReadLocalTorrent(ToolkitPackages.SafePath(game,StateFile));
        if(own!=null&&own.Revision==next.Revision)return own;
        string profile=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),"octo-launcher");
        string settings=Path.Combine(profile,"settings.json");
        if(File.Exists(settings)) {
            try {
                var json=new JavaScriptSerializer().Deserialize<Dictionary<string,object>>(File.ReadAllText(settings));
                string folder=Field(json,"clientDir") as string, hash=Field(json,"syncedTorrentHash") as string;
                if(!String.IsNullOrEmpty(folder)&&String.Equals(Path.GetFullPath(folder).TrimEnd('\\'),game.TrimEnd('\\'),StringComparison.OrdinalIgnoreCase)) {
                    var official=ReadLocalTorrent(Path.Combine(profile,"client-synced.torrent"));
                    if(official!=null&&official.Revision==hash&&(own==null||official.Revision==next.Revision))return official;
                    if(own==null&&hash==next.Revision)return next;
                }
            } catch(ArgumentException) { }
              catch(InvalidOperationException) { }
              catch(IOException) { }
              catch(UnauthorizedAccessException) { }
        }
        return own;
    }

    private static Torrent ReadLocalTorrent(string path) {
        try {
            var info=new FileInfo(path);
            return info.Exists&&info.Length<=MetadataLimit?Parse(File.ReadAllBytes(path)):null;
        } catch(IOException) {return null;}
          catch(UnauthorizedAccessException) {return null;}
    }

    private static Torrent Parse(byte[] data) {
        var root=new TorrentReader(data).Read(0) as Dictionary<string,object>;
        var info=Field(root,"info") as Dictionary<string,object>;
        var files=Field(info,"files") as List<object>;
        var pieces=Field(info,"pieces") as string;
        object size=Field(info,"piece length");
        if(files==null||files.Count==0||pieces==null||!(size is long)||(long)size<=0||(long)size>8*1024*1024||
           !Equals(Field(info,"name"),"client"))
            throw new IOException("OctoWoW returned invalid patch metadata.");
        var torrent=new Torrent {Data=data,PieceSize=(int)(long)size,Pieces=Encoding.GetEncoding(28591).GetBytes(pieces)};
        using(var sha=SHA1.Create())torrent.Revision=BitConverter.ToString(sha.ComputeHash(data)).Replace("-","").ToLowerInvariant();
        var seen=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach(var item in files) {
            var entry=item as Dictionary<string,object>;
            var parts=Field(entry,"path") as List<object>;
            object length=Field(entry,"length");
            if(parts==null||parts.Count==0||!(length is long)||(long)length<0)throw new IOException("Invalid patch file.");
            var names=parts.Select(part=>part is string?Encoding.UTF8.GetString(Encoding.GetEncoding(28591).GetBytes((string)part)):null).ToArray();
            if(names.Any(name=>String.IsNullOrEmpty(name)||name=="."||name==".."||name.TrimEnd('.', ' ')!=name||
               name.IndexOfAny(Path.GetInvalidFileNameChars())>=0))throw new IOException("Invalid patch path.");
            string file=String.Join("/",names);
            if(!seen.Add(file))throw new IOException("Duplicate patch path.");
            torrent.Files.Add(new ClientFile{Path=file,Length=(long)length,Start=torrent.Length});
            torrent.Length=checked(torrent.Length+(long)length);
        }
        if(torrent.Length==0||((torrent.Length-1)/torrent.PieceSize+1)*20!=torrent.Pieces.Length)
            throw new IOException("Invalid patch checksums.");
        return torrent;
    }

    private static object Field(Dictionary<string,object> dictionary,string name) {
        object value;
        return dictionary!=null&&dictionary.TryGetValue(name,out value)?value:null;
    }
    private sealed class TorrentReader {
        private readonly byte[] data;
        private int offset;
        internal TorrentReader(byte[] data) {this.data=data;}
        internal object Read(int depth) {
            if(depth>16||offset>=data.Length)throw Invalid();
            byte kind=data[offset];
            if(kind=='d'||kind=='l') {
                offset++;
                var dictionary=kind=='d'?new Dictionary<string,object>():null;
                var list=kind=='l'?new List<object>():null;
                while(offset<data.Length&&data[offset]!='e') {
                    if(dictionary==null)list.Add(Read(depth+1));
                    else {
                        string key=Read(depth+1) as string;
                        if(key==null)throw Invalid();
                        dictionary[key]=Read(depth+1);
                    }
                }
                if(offset>=data.Length)throw Invalid();
                offset++;
                return (object)dictionary??list;
            }
            if(kind=='i') {
                offset++;
                return Number((byte)'e');
            }
            long size=Number((byte)':');
            if(size<0||size>data.Length-offset)throw Invalid();
            string value=Encoding.GetEncoding(28591).GetString(data,offset,(int)size);
            offset+=(int)size;
            return value;
        }
        private long Number(byte terminator) {
            int start=offset;
            while(offset<data.Length&&data[offset]!=terminator&&offset-start<21)offset++;
            long value;
            if(offset>=data.Length||data[offset]!=terminator||
               !Int64.TryParse(Encoding.ASCII.GetString(data,start,offset-start),NumberStyles.AllowLeadingSign,CultureInfo.InvariantCulture,out value))
                throw Invalid();
            offset++;
            return value;
        }
        private static IOException Invalid() {return new IOException("OctoWoW returned invalid patch metadata.");}
    }
}

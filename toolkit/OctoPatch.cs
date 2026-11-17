using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
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
    private const int MetadataLimit=4*1024*1024;
    internal static OctoPatchInfo Check(string game) {
        var request=(HttpWebRequest)WebRequest.Create(TorrentUrl);
        request.Timeout=12000;
        request.ReadWriteTimeout=12000;
        request.AllowAutoRedirect=false;
        byte[] data;
        using(var response=(HttpWebResponse)request.GetResponse())
        using(var input=response.GetResponseStream())
        using(var output=new MemoryStream()) {
            if(response.StatusCode!=HttpStatusCode.OK)throw new IOException("Patch feed returned an unexpected response.");
            var buffer=new byte[8192];
            int count;
            while((count=input.Read(buffer,0,buffer.Length))>0) {
                if(output.Length+count>MetadataLimit)throw new IOException("Patch metadata is too large.");
                output.Write(buffer,0,count);
            }
            data=output.ToArray();
        }

        string baseline=null;
        bool unfinished=false;
        string appData=Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        string settings=Path.Combine(appData,"octo-launcher","settings.json");
        if(File.Exists(settings)) {
            try {
                var json=new JavaScriptSerializer().Deserialize<Dictionary<string,object>>(File.ReadAllText(settings));
                object folder,hash;
                if(json!=null&&json.TryGetValue("clientDir",out folder)&&folder is string&&
                   String.Equals(Path.GetFullPath((string)folder).TrimEnd('\\'),game.TrimEnd('\\'),StringComparison.OrdinalIgnoreCase)&&
                   json.TryGetValue("syncedTorrentHash",out hash))
                    baseline=hash as string;
                object activeFolder,selection;
                unfinished=json!=null&&json.TryGetValue("activeClientDir",out activeFolder)&&activeFolder is string&&
                    String.Equals(Path.GetFullPath((string)activeFolder).TrimEnd('\\'),game.TrimEnd('\\'),StringComparison.OrdinalIgnoreCase)&&
                    json.TryGetValue("activeSelection",out selection)&&selection is System.Collections.ICollection&&
                    ((System.Collections.ICollection)selection).Count>0;
            } catch(ArgumentException) { }
              catch(InvalidOperationException) { }
              catch(IOException) { }
              catch(UnauthorizedAccessException) { }
        }
        var result=Compare(game,data,baseline);
        if(unfinished)result.State="available";
        return result;
    }

    internal static OctoPatchInfo Compare(string game,byte[] data,string baseline) {
        string hash;
        using(var sha=SHA1.Create())
            hash=BitConverter.ToString(sha.ComputeHash(data)).Replace("-","").ToLowerInvariant();
        var root=new TorrentReader(data).Read(0) as Dictionary<string,object>;
        var info=Field(root,"info") as Dictionary<string,object>;
        var files=Field(info,"files") as List<object>;
        if(files==null||files.Count==0)throw new IOException("OctoWoW returned invalid patch metadata.");
        var result=new OctoPatchInfo {Revision=hash.Substring(0,8)};
        foreach(var item in files) {
            var entry=item as Dictionary<string,object>;
            var path=Field(entry,"path") as List<object>;
            object length=Field(entry,"length");
            if(path==null||path.Count!=2||!String.Equals(path[0] as string,"Data",StringComparison.OrdinalIgnoreCase))continue;
            string name=path[1] as string;
            if(String.IsNullOrEmpty(name)||name.IndexOfAny(Path.GetInvalidFileNameChars())>=0||
               !name.EndsWith(".mpq",StringComparison.OrdinalIgnoreCase)||
               name.Equals("patch-O.mpq",StringComparison.OrdinalIgnoreCase)||!(length is long)||(long)length<0)continue;
            var local=new FileInfo(Path.Combine(game,"Data",name));
            if(local.Exists&&local.Length==(long)length)continue;
            result.ArchiveCount++;
            result.ArchiveBytes=checked(result.ArchiveBytes+(long)length);
        }
        result.State=result.ArchiveCount>0||(!String.IsNullOrEmpty(baseline)&&!String.Equals(hash,baseline,StringComparison.OrdinalIgnoreCase))
            ?"available":String.IsNullOrEmpty(baseline)?"unverified":"current";
        return result;
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
            string value=Encoding.UTF8.GetString(data,offset,(int)size);
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

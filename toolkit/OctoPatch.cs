using System;
using System.Collections.Generic;
using System.Globalization;
using System.Diagnostics;
using System.Threading;
using System.IO;
using System.Linq;
using System.Net;
using System.Runtime.ExceptionServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

internal sealed class OctoPatchInfo {
    internal string ReleaseId;
    public string State {get;set;}
    public string CurrentRevision {get;set;}
    public string Revision {get;set;}
    public long ArchiveBytes {get;set;}
    public int ArchiveCount {get;set;}
    public int ReleaseCount {get;set;}
    public long ReleaseBytes {get;set;}
    public string PublishedUtc {get;set;}
    public string Verification {get;set;}
    public List<OctoPatchFile> Files {get;set;}
}
internal sealed class OctoPatchFile {
    public string Path {get;set;}
    public string Reason {get;set;}
    public long Bytes {get;set;}
}

internal static class OctoPatch {
    [ThreadStatic] private static CancellationToken cancellation;
    [ThreadStatic] private static Action<string,long,long> verificationProgress;
    internal static OctoPatchInfo CheckDetailed(string game,Action<string,long,long> progress,CancellationToken token) {
        cancellation=token;verificationProgress=progress;
        try {return Check(game,true,null);}
        catch(WebException) {cancellation.ThrowIfCancellationRequested();throw;}
        catch(IOException) {cancellation.ThrowIfCancellationRequested();throw;}
        finally {cancellation=CancellationToken.None;verificationProgress=null;}
    }
    internal static OctoPatchInfo ApplyDetailed(string game,Action<string,long,long> progress,CancellationToken token) {
        cancellation=token;
        try {token.ThrowIfCancellationRequested();return Apply(game,progress);}
        catch(WebException) {cancellation.ThrowIfCancellationRequested();throw;}
        catch(IOException) {cancellation.ThrowIfCancellationRequested();throw;}
        finally {cancellation=CancellationToken.None;}
    }
    private const string TorrentUrl="https://dl.octowow.st/download/client.torrent";
    private const string FileRoot="https://dl.octowow.st/client/client/";
    private const int MetadataLimit=4*1024*1024;
    private const string StateFile=".twilight-toolkit\\octowow-client.torrent";
    private const string VerificationFile=@".twilight-toolkit\octowow-verification.json";
    private const string DownloadCache=@".twilight-toolkit\game-patch-download-v1";
    private const string PreparedFile="prepared-file.tmp";
    private const string PreparedManifest="prepared-manifest.tmp";
    private const int DownloadWorkers=4;
    private const int PiecesPerDownload=4;

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
        internal string ReleaseId,PublishedUtc;
        internal int PieceSize;
        internal byte[] Pieces;
        internal long Length;
        internal readonly List<ClientFile> Files=new List<ClientFile>();
    }
    private sealed class PatchBatch {
        internal long Start,Required;
        internal int Length;
        internal string File;
    }
    private sealed class SavedVerification {
        public string Release {get;set;}
        public string[] Repairs {get;set;}
    }

    internal static OctoPatchInfo Check(string game,bool verify=false,Action<string> progress=null) {
        var next=DownloadTorrent();
        if(verify) {
            using(OpenPatchLock(game)) {
                var baseline=ReadBaseline(game,next);
                var checkedFiles=VerifiedPlan(game,next,progress);
                cancellation.ThrowIfCancellationRequested();
                SaveVerification(game,next,checkedFiles.Select(file=>file.Path));
                return Describe(game,next,baseline,checkedFiles,true);
            }
        }
        var previous=ReadBaseline(game,next);
        var repairs=ReadVerification(game,next);
        var files=PlanUpdate(game,next,previous,repairs);
        var result=Describe(game,next,previous??(repairs!=null&&repairs.Count==0?next:null),files,false);
        if(repairs!=null)
            foreach(var file in result.Files)
                if(repairs.Contains(file.Path)&&file.Reason=="Release changed")file.Reason="Checksum mismatch";
        return result;
    }

    private static string VerificationPath(string game) {
        string path=ToolkitPackages.SafePath(game,VerificationFile);
        ToolkitInstall.CheckForLinks(game,path);
        return path;
    }

    private static HashSet<string> ReadVerification(string game,Torrent release) {
        string path=VerificationPath(game);
        try {
            var info=new FileInfo(path);
            if(!info.Exists||info.Length>MetadataLimit)return null;
            var report=new JavaScriptSerializer().Deserialize<SavedVerification>(File.ReadAllText(path));
            if(report==null||report.Release!=release.ReleaseId||report.Repairs==null)return null;
            return new HashSet<string>(report.Repairs,StringComparer.OrdinalIgnoreCase);
        } catch(IOException) {return null;}
          catch(UnauthorizedAccessException) {return null;}
          catch(ArgumentException) {return null;}
          catch(InvalidOperationException) {return null;}
    }

    private static void SaveVerification(string game,Torrent release,IEnumerable<string> repairs) {
        string path=VerificationPath(game);
        string temporary=path+"."+Guid.NewGuid().ToString("N")+".tmp";
        try {
            File.WriteAllText(temporary,new JavaScriptSerializer().Serialize(
                new SavedVerification{Release=release.ReleaseId,Repairs=repairs.ToArray()}));
            if(File.Exists(path))File.Replace(temporary,path,null);
            else File.Move(temporary,path);
        } finally {if(File.Exists(temporary))File.Delete(temporary);}
    }

    private static List<ClientFile> VerifiedPlan(string game,Torrent next,Action<string> progress) {
        return VerifyPlan(game,next,progress,cancellation);
    }

    private static List<ClientFile> VerifyPlan(string game,Torrent next,Action<string> progress,CancellationToken token) {
        var files=new List<ClientFile>();
        var managed=next.Files.Where(item=>item.Managed).ToArray();
        long total=managed.Sum(item=>item.Length),done=0;
        var timer=Stopwatch.StartNew();long last=0;
        for(int index=0;index<managed.Length;index++) {
            token.ThrowIfCancellationRequested();
            var file=managed[index];
            string label="Verifying "+(index+1)+" of "+managed.Length+" · "+file.Path;
            if(progress!=null)progress(label+" · "+(total==0?100:100*done/total)+"%");
            if(verificationProgress!=null)verificationProgress(label,done,total);
            if(!MatchesLocalProgress(game,next,file,bytes=>{
                if(timer.ElapsedMilliseconds-last>=200) {
                    last=timer.ElapsedMilliseconds;
                    if(progress!=null)progress(label+" · "+(total==0?100:100*Math.Min(total,done+bytes)/total)+"%");
                    if(verificationProgress!=null)verificationProgress(label,Math.Min(total,done+bytes),total);
                }
            },token))files.Add(file);
            done+=file.Length;
        }
        if(progress!=null)progress("Verification complete · "+managed.Length+" files checked · "+files.Count+" to download");
        if(verificationProgress!=null)verificationProgress("Verification complete",total,total);
        return files;
    }

    private static OctoPatchInfo Describe(string game,Torrent next,Torrent previous,List<ClientFile> files,bool verified) {
        return new OctoPatchInfo {
            ReleaseId=next.ReleaseId,
            State=files.Count>0?"available":previous==null&&!verified?"unverified":"current",
            CurrentRevision=verified&&files.Count==0?next.ReleaseId.Substring(0,8):
                previous==null?null:previous.ReleaseId.Substring(0,8),
            Revision=next.ReleaseId.Substring(0,8),
            ArchiveCount=files.Count,
            ArchiveBytes=files.Sum(file=>file.Length),
            ReleaseCount=next.Files.Count,ReleaseBytes=next.Length,PublishedUtc=next.PublishedUtc,
            Verification=verified?"All managed game files checked against release checksums.":
                previous==null?"No previous sync recorded; checking file sizes. Use Verify Files to detect same-size changes.":
                "Checking file sizes and changes since the previous release. Use Verify Files for a full checksum check.",
            Files=files.Select(file=>{
                var local=new FileInfo(ToolkitPackages.SafePath(game,file.Path));
                return new OctoPatchFile {Path=file.Path,Bytes=file.Length,
                    Reason=!local.Exists?"Missing":local.Length!=file.Length?"Size mismatch":verified?"Checksum mismatch":"Release changed"};
            }).ToList()
        };
    }

    internal static FileStream OpenPatchLock(string game) {
        string lockFile=ToolkitPackages.SafePath(game,@".twilight-toolkit\game-patch.lock");
        ToolkitInstall.CheckForLinks(game,lockFile);
        Directory.CreateDirectory(Path.GetDirectoryName(lockFile));
        return new FileStream(lockFile,FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None);
    }

    internal static OctoPatchInfo Apply(string game,Action<string,long,long> progress) {
        ToolkitInstall.EnsureClosed();
        string state=ToolkitPackages.SafePath(game,StateFile);
        ToolkitInstall.CheckForLinks(game,state);
        using(OpenPatchLock(game)) {
            progress("Checking for game updates…",0,0);
            var next=DownloadTorrent();
            return ApplyCore(game,state,progress,next,(start,bytes,token)=>DownloadBatch(next,start,bytes,token),
                cancellation,ToolkitInstall.EnsureClosed);
        }
    }

    private static OctoPatchInfo ApplyCore(string game,string state,Action<string,long,long> progress,Torrent next,
        Action<long,byte[],CancellationToken> downloadBatch,CancellationToken token,Action verifyGameClosed) {
        token.ThrowIfCancellationRequested();
        var previous=ReadBaseline(game,next);
        var repairs=ReadVerification(game,next);
        var files=PlanUpdate(game,next,previous,repairs);
        bool knownBaseline=previous!=null||repairs!=null;

        long total=files.Sum(file=>file.Length);
        string cacheRoot=ToolkitPackages.SafePath(game,DownloadCache);
        string cache=ToolkitPackages.SafePath(cacheRoot,next.ReleaseId);
        ToolkitInstall.CheckForLinks(game,cache);
        PruneOldPatchDownloads(game,cacheRoot,cache);
        Directory.CreateDirectory(cache);
        string prepared=Path.Combine(cache,PreparedFile);
        string metadata=Path.Combine(cache,PreparedManifest);
        DeleteStagedFile(game,prepared);
        DeleteStagedFile(game,metadata);
        var batches=BuildPatchBatches(next,files);
        var missing=new List<PatchBatch>();
        long saved=0;
        var cacheTimer=Stopwatch.StartNew();
        foreach(var batch in batches) {
            token.ThrowIfCancellationRequested();
            string path=PatchBatchPath(cache,batch.Start);
            ToolkitInstall.CheckForLinks(game,path);
            DeleteStagedFile(game,path+".tmp");
            if(!VerifySavedBatch(next,batch,path,token)) {
                missing.Add(batch);
            } else {
                saved+=batch.Required;
                if(cacheTimer.ElapsedMilliseconds>=200) {
                    progress("Checking saved downloads…",0,0);
                    cacheTimer.Restart();
                }
            }
        }
        var drive=new DriveInfo(Path.GetPathRoot(game));
        long backupBytes=0;
        foreach(var file in files) {
            var existing=new FileInfo(ToolkitPackages.SafePath(game,file.Path));
            if(existing.Exists)backupBytes+=existing.Length;
        }
        long largest=files.Count==0?0:files.Max(file=>file.Length);
        if(drive.AvailableFreeSpace<missing.Sum(batch=>(long)batch.Length)+backupBytes+largest+256L*1024*1024)
            throw new IOException("There is not enough free space to stage the update and back up the existing files.");
        if(total!=0) {
            progress(saved>0?"Downloading game files · Resuming saved progress…":"Downloading game files…",saved,total);
            DownloadPatchBatches(game,cache,next,missing,downloadBatch,progress,saved,total,token);
        }

        string id=Guid.NewGuid().ToString("N");
        string backup=ToolkitPackages.SafePath(game,Path.Combine(".twilight-toolkit","backups","game-patch-"+id));
        ToolkitInstall.CheckForLinks(game,backup);
        var changes=new List<KeyValuePair<string,string>>();
        try {
            verifyGameClosed();
            token.ThrowIfCancellationRequested();
            progress("Applying verified game files…",total,total);
            foreach(var file in files) {
                string target=ToolkitPackages.SafePath(game,file.Path);
                ToolkitInstall.CheckForLinks(game,target);
                using(var output=new FileStream(prepared,FileMode.CreateNew,FileAccess.Write,FileShare.None)) {
                    for(long start=PatchBatchStart(next,file);file.Length>0&&start<file.End;start+=PatchBatchStride(next)) {
                        string path=PatchBatchPath(cache,start);
                        ToolkitInstall.CheckForLinks(game,path);
                        var bytes=ReadPatchBatch(path,PatchBatchLength(next,start),CancellationToken.None);
                        VerifyPieces(next,start,bytes);
                        long begin=Math.Max(start,file.Start),end=Math.Min(start+bytes.Length,file.End);
                        output.Write(bytes,(int)(begin-start),(int)(end-begin));
                    }
                    output.Flush(true);
                }
                ToolkitInstall.Replace(prepared,target,ToolkitPackages.SafePath(backup,file.Path),changes,true);
            }
            if(knownBaseline) {
                File.WriteAllBytes(metadata,next.Data);
                ToolkitInstall.Replace(metadata,state,Path.Combine(backup,"client.torrent"),changes,true);
            }
            if(repairs!=null)File.Delete(VerificationPath(game));
        } catch {
            ToolkitInstall.Restore(changes);
            throw;
        } finally {
            TryDeleteStagedFile(game,prepared);
            TryDeleteStagedFile(game,metadata);
        }
        DeletePatchDownloads(game,cache);
        var result=Describe(game,next,knownBaseline?next:null,new List<ClientFile>(),false);
        if(files.Count>0)result.Verification=knownBaseline?"Downloaded files were verified against the release checksums.":
            "Downloaded files were verified. Use Verify Files to check the whole installation.";
        return result;
    }

    private static long PatchBatchStride(Torrent torrent) {
        return (long)PiecesPerDownload*torrent.PieceSize;
    }

    private static long PatchBatchStart(Torrent torrent,ClientFile file) {
        return file.Start/PatchBatchStride(torrent)*PatchBatchStride(torrent);
    }

    private static int PatchBatchLength(Torrent torrent,long start) {
        return (int)Math.Min(PatchBatchStride(torrent),torrent.Length-start);
    }

    private static List<PatchBatch> BuildPatchBatches(Torrent torrent,List<ClientFile> files) {
        // Align to the same global offsets even if the set of files needing repair changes between attempts.
        var batches=new SortedDictionary<long,PatchBatch>();
        foreach(var file in files) {
            if(file.Length==0)continue;
            for(long start=PatchBatchStart(torrent,file);start<file.End;start+=PatchBatchStride(torrent)) {
                PatchBatch batch;
                if(!batches.TryGetValue(start,out batch)) {
                    batch=new PatchBatch{Start=start,Length=PatchBatchLength(torrent,start),File=file.Path};
                    batches.Add(start,batch);
                }
                batch.Required+=Math.Min(file.End,start+batch.Length)-Math.Max(file.Start,start);
            }
        }
        return batches.Values.ToList();
    }

    private static string PatchBatchPath(string cache,long start) {
        return Path.Combine(cache,start.ToString(CultureInfo.InvariantCulture)+".part");
    }

    private static byte[] ReadPatchBatch(string path,int size,CancellationToken token) {
        using(var input=File.OpenRead(path)) {
            if(input.Length!=size)throw new BadPiece();
            var bytes=new byte[size];
            ReadExactly(input,bytes,0,size,token);
            return bytes;
        }
    }

    private static bool VerifySavedBatch(Torrent torrent,PatchBatch batch,string path,CancellationToken token) {
        if(!File.Exists(path))return false;
        try {
            VerifyPieces(torrent,batch.Start,ReadPatchBatch(path,batch.Length,token));
            return true;
        } catch(BadPiece) {
            File.Delete(path);
            return false;
        }
    }

    private static void DeleteStagedFile(string game,string path) {
        ToolkitInstall.CheckForLinks(game,path);
        if(File.Exists(path))File.Delete(path);
    }

    private static void TryDeleteStagedFile(string game,string path) {
        try {DeleteStagedFile(game,path);}
        catch(IOException) {}
        catch(UnauthorizedAccessException) {}
    }

    private static void SaveVerifiedBatch(string game,string path,byte[] bytes) {
        string temporary=path+".tmp";
        ToolkitInstall.CheckForLinks(game,path);
        ToolkitInstall.CheckForLinks(game,temporary);
        try {
            using(var output=new FileStream(temporary,FileMode.CreateNew,FileAccess.Write,FileShare.None)) {
                output.Write(bytes,0,bytes.Length);
                output.Flush(true);
            }
            File.Move(temporary,path);
        } finally {
            if(File.Exists(temporary))File.Delete(temporary);
        }
    }

    private static void DownloadPatchBatches(string game,string cache,Torrent torrent,List<PatchBatch> missing,
        Action<long,byte[],CancellationToken> downloadBatch,Action<string,long,long> progress,long done,long total,CancellationToken token) {
        var progressLock=new object();
        var timer=Stopwatch.StartNew();
        Exception failure=null;
        using(var linked=CancellationTokenSource.CreateLinkedTokenSource(token)) {
            var options=new ParallelOptions{MaxDegreeOfParallelism=DownloadWorkers,CancellationToken=linked.Token};
            try {
                Parallel.ForEach(missing,options,batch=>{
                    try {
                        linked.Token.ThrowIfCancellationRequested();
                        var bytes=new byte[batch.Length];
                        downloadBatch(batch.Start,bytes,linked.Token);
                        VerifyPieces(torrent,batch.Start,bytes);
                        SaveVerifiedBatch(game,PatchBatchPath(cache,batch.Start),bytes);
                        lock(progressLock) {
                            done+=batch.Required;
                            if(done==total||timer.ElapsedMilliseconds>=200) {
                                progress("Downloading "+batch.File+"…",done,total);
                                timer.Restart();
                            }
                        }
                    } catch(Exception error) {
                        if(!linked.IsCancellationRequested&&Interlocked.CompareExchange(ref failure,error,null)==null)
                            linked.Cancel();
                        throw;
                    }
                });
            } catch(AggregateException errors) {
                token.ThrowIfCancellationRequested();
                ExceptionDispatchInfo.Capture(failure??errors.Flatten().InnerExceptions[0]).Throw();
                throw;
            } catch(OperationCanceledException) {
                token.ThrowIfCancellationRequested();
                if(failure!=null)ExceptionDispatchInfo.Capture(failure).Throw();
                throw;
            }
        }
    }

    private static void PruneOldPatchDownloads(string game,string root,string keep) {
        if(!Directory.Exists(root))return;
        foreach(string directory in Directory.EnumerateDirectories(root)) {
            string name=Path.GetFileName(directory);
            if(directory.Equals(keep,StringComparison.OrdinalIgnoreCase)||name.Length!=40||
                !name.All(Uri.IsHexDigit))continue;
            DeletePatchDownloads(game,directory);
        }
    }

    private static void DeletePatchDownloads(string game,string directory) {
        try {
            if(!Directory.Exists(directory))return;
            ToolkitInstall.CheckForLinks(game,directory);
            TryDeleteStagedFile(game,Path.Combine(directory,PreparedFile));
            TryDeleteStagedFile(game,Path.Combine(directory,PreparedManifest));
            foreach(string file in Directory.EnumerateFiles(directory)) {
                string name=Path.GetFileName(file);
                int separator=name.IndexOf('.');
                long offset;
                if(separator<=0||!Int64.TryParse(name.Substring(0,separator),NumberStyles.None,CultureInfo.InvariantCulture,out offset))continue;
                string suffix=name.Substring(separator);
                if(suffix!=".part"&&suffix!=".part.tmp")continue;
                ToolkitInstall.CheckForLinks(game,file);
                File.Delete(file);
            }
            Directory.Delete(directory);
        } catch(IOException) {}
          catch(UnauthorizedAccessException) {}
    }

    private static List<ClientFile> Plan(string game,Torrent next,Torrent previous) {
        var old=previous==null?null:previous.Files.ToDictionary(file=>file.Path,StringComparer.OrdinalIgnoreCase);
        return next.Files.Where(file=>{
            if(!file.Managed)return false;
            var local=new FileInfo(ToolkitPackages.SafePath(game,file.Path));
            if(!local.Exists||local.Length!=file.Length)return true;
            if(previous==null||previous.ReleaseId==next.ReleaseId)return false;
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

    private static List<ClientFile> PlanUpdate(string game,Torrent next,Torrent previous,HashSet<string> repairs) {
        // An explicit verification confirms the current release contents, including files
        // that may have been updated outside the launcher since the baseline was recorded.
        if(repairs==null)return Plan(game,next,previous);
        var required=new HashSet<ClientFile>(Plan(game,next,next));
        return next.Files.Where(file=>file.Managed&&(required.Contains(file)||repairs.Contains(file.Path))).ToList();
    }

    private static bool MatchesLocal(string game,Torrent torrent,ClientFile file) {
        return MatchesLocalProgress(game,torrent,file,null,cancellation);
    }
    private static bool MatchesLocalProgress(string game,Torrent torrent,ClientFile file,Action<long> progress,CancellationToken token) {
        string path=ToolkitPackages.SafePath(game,file.Path);
        ToolkitInstall.CheckForLinks(game,path);
        var local=new FileInfo(path);
        if(!local.Exists||local.Length!=file.Length)return false;
        using(var input=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read,1024*1024,FileOptions.SequentialScan)) {
            long checkedBytes=0;
            try {
                EachBatch(torrent,file,(start,bytes)=>{
                    ReadBatch(torrent,start,bytes,file,input,true,token);
                    try {VerifyPieces(torrent,start,bytes);}
                    catch(BadPiece) {
                        // A piece can cross file boundaries. Recheck only those
                        // boundaries against CDN bytes to attribute damage correctly.
                        if(start>=file.Start&&start+bytes.Length<=file.End)throw;
                        ReadBatch(torrent,start,bytes,file,input,false,token);
                        VerifyPieces(torrent,start,bytes);
                    }
                    checkedBytes+=Math.Min(start+bytes.Length,file.End)-Math.Max(start,file.Start);
                    if(progress!=null)progress(checkedBytes);
                },token);
            } catch(BadPiece) {return false;}
        }
        return true;
    }

    private static void EachBatch(Torrent torrent,ClientFile file,Action<long,byte[]> action,CancellationToken token) {
        if(file.Length==0)return;
        long start=file.Start/torrent.PieceSize*torrent.PieceSize;
        long stop=Math.Min(torrent.Length,((file.End-1)/torrent.PieceSize+1)*torrent.PieceSize);
        while(start<stop) {
            token.ThrowIfCancellationRequested();
            int size=(int)Math.Min(4L*torrent.PieceSize,stop-start);
            action(start,new byte[size]);
            start+=size;
        }
    }

    private static void DownloadBatch(Torrent torrent,long start,byte[] bytes,CancellationToken token) {
        for(int attempt=1;;attempt++) {
            try {
                ReadBatch(torrent,start,bytes,null,null,false,token);
                return;
            } catch(WebException error) {
                bool retry=IsTransientDownloadError(error);
                if(error.Response!=null)error.Response.Dispose();
                token.ThrowIfCancellationRequested();
                if(!retry||attempt>=3)throw;
                if(token.WaitHandle.WaitOne(500*attempt))token.ThrowIfCancellationRequested();
            }
        }
    }

    private static bool IsTransientDownloadError(WebException error) {
        var response=error.Response as HttpWebResponse;
        if(response!=null) {
            int status=(int)response.StatusCode;
            return status==408||status==429||status>=500&&status<600;
        }
        return error.Status==WebExceptionStatus.Timeout||error.Status==WebExceptionStatus.ConnectFailure||
            error.Status==WebExceptionStatus.ConnectionClosed||error.Status==WebExceptionStatus.KeepAliveFailure||
            error.Status==WebExceptionStatus.ReceiveFailure||error.Status==WebExceptionStatus.SendFailure;
    }

    private static void ReadBatch(Torrent torrent,long start,byte[] bytes,ClientFile local,Stream input,bool preferLocal,CancellationToken token) {
        long stop=start+bytes.Length;
        foreach(var source in torrent.Files) {
            long begin=Math.Max(start,source.Start),end=Math.Min(stop,source.End);
            if(end<=begin)continue;
            int dst=(int)(begin-start),count=(int)(end-begin);
            long offset=begin-source.Start;
            if(source==local) {
                input.Position=offset;
                ReadExactly(input,bytes,dst,count,token);
                continue;
            }
            if(local!=null&&preferLocal) {
                // Derive the game root from the verified file's relative path.
                string game=((FileStream)input).Name;
                foreach(string part in local.Path.Split('/'))game=Path.GetDirectoryName(game);
                string neighbor=ToolkitPackages.SafePath(game,source.Path);
                ToolkitInstall.CheckForLinks(game,neighbor);
                var info=new FileInfo(neighbor);
                if(info.Exists&&info.Length==source.Length) {
                    using(var adjacent=new FileStream(neighbor,FileMode.Open,FileAccess.Read,FileShare.Read)) {
                        adjacent.Position=offset;ReadExactly(adjacent,bytes,dst,count,token);
                    }
                    continue;
                }
            }
            token.ThrowIfCancellationRequested();
            var request=Request(FileRoot+String.Join("/",source.Path.Split('/').Select(Uri.EscapeDataString)));
            if(local==null&&request.ServicePoint.ConnectionLimit<DownloadWorkers)
                request.ServicePoint.ConnectionLimit=DownloadWorkers;
            request.AddRange(offset,offset+count-1);
            using(var cancelled=token.Register(request.Abort))
            using(var response=(HttpWebResponse)request.GetResponse())
            using(var stream=response.GetResponseStream()) {
                string expected=String.Format(CultureInfo.InvariantCulture,"bytes {0}-{1}/{2}",offset,offset+count-1,source.Length);
                if(response.StatusCode!=HttpStatusCode.PartialContent||
                   !String.Equals(response.Headers["Content-Range"],expected,StringComparison.OrdinalIgnoreCase))
                    throw new IOException("OctoWoW's CDN did not return the requested byte range for "+source.Path+".");
                ReadExactly(stream,bytes,dst,count,token);
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

    private static void ReadExactly(Stream stream,byte[] bytes,int offset,int count,CancellationToken token) {
        while(count>0) {
            token.ThrowIfCancellationRequested();
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
        cancellation.ThrowIfCancellationRequested();
        var request=Request(TorrentUrl);
        using(var cancelled=cancellation.Register(request.Abort))
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
            var torrent=Parse(output.ToArray());
            DateTime published;
            if(String.IsNullOrEmpty(torrent.PublishedUtc)&&DateTime.TryParse(response.Headers["Last-Modified"],
                CultureInfo.InvariantCulture,DateTimeStyles.AdjustToUniversal|DateTimeStyles.AssumeUniversal,out published))
                torrent.PublishedUtc=published.ToUniversalTime().ToString("o",CultureInfo.InvariantCulture);
            return torrent;
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
        var reader=new TorrentReader(data);
        var root=reader.Read(0) as Dictionary<string,object>;
        var info=Field(root,"info") as Dictionary<string,object>;
        var files=Field(info,"files") as List<object>;
        var pieces=Field(info,"pieces") as string;
        object size=Field(info,"piece length");
        if(files==null||files.Count==0||pieces==null||!(size is long)||(long)size<=0||(long)size>8*1024*1024||
           !Equals(Field(info,"name"),"client"))
            throw new IOException("OctoWoW returned invalid patch metadata.");
        var torrent=new Torrent {Data=data,PieceSize=(int)(long)size,Pieces=Encoding.GetEncoding(28591).GetBytes(pieces)};
        using(var sha=SHA1.Create())torrent.Revision=BitConverter.ToString(sha.ComputeHash(data)).Replace("-","").ToLowerInvariant();
        if(reader.InfoLength<=0)throw new IOException("Missing torrent info checksum.");
        using(var sha=SHA1.Create())torrent.ReleaseId=BitConverter.ToString(sha.ComputeHash(data,reader.InfoStart,reader.InfoLength)).Replace("-","").ToLowerInvariant();
        object created=Field(root,"creation date");
        if(created is long) {
            try {torrent.PublishedUtc=new DateTime(1970,1,1,0,0,0,DateTimeKind.Utc).AddSeconds((long)created).ToString("o",CultureInfo.InvariantCulture);}
            catch(ArgumentOutOfRangeException) { }
        }
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
        internal int InfoStart,InfoLength;
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
                        int start=offset;
                        dictionary[key]=Read(depth+1);
                        if(depth==0&&key=="info"){InfoStart=start;InfoLength=offset-start;}
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

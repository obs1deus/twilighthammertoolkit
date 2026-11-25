using System;
using System.Collections.Concurrent;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Threading;

internal static class PatchDownloadCheck {
    private const int PieceSize=64, BatchSize=PieceSize*4;
    private const int FirstLength=8*BatchSize+13, SecondLength=8*BatchSize+41, UnmanagedLength=BatchSize+21;
    private static readonly string[] FileNames={"a.mpq","b.mpq","patch-O.mpq"};
    private static readonly int[] FileLengths={FirstLength,SecondLength,UnmanagedLength};
    private static readonly byte[][] Originals={Bytes("old a"),Bytes("old b"),Bytes("local optional patch")};
    private static readonly TimeSpan WorkerTimeout=TimeSpan.FromSeconds(15);
    private static Type patch;

    private static byte[] Bytes(string text) {return Encoding.ASCII.GetBytes(text);}
    private static void Check(bool condition,string message) {if(!condition)throw new Exception(message);}
    private static void Expect<T>(Action action) where T:Exception {
        try {action();}
        catch(T) {return;}
        throw new Exception("Expected "+typeof(T).Name+".");
    }
    private static object Call(string name,params object[] args) {
        try {return patch.GetMethod(name,BindingFlags.NonPublic|BindingFlags.Static).Invoke(null,args);}
        catch(TargetInvocationException ex) {throw ex.InnerException;}
    }

    private sealed class Fixture {
        internal readonly byte[] Data;
        internal readonly string Folder, State, Cache;
        internal readonly object Torrent;
        internal readonly byte[] ManifestBytes;

        internal Fixture(string folder,int salt) {
            Folder=folder;
            State=Path.Combine(folder,".twilight-toolkit","octowow-client.torrent");
            Data=Enumerable.Range(0,FileLengths.Sum()).Select(i=>(byte)((i*73+i/17+salt)%251)).ToArray();
            ManifestBytes=Manifest(Data);
            Torrent=Call("Parse",ManifestBytes);
            var release=(string)Torrent.GetType().GetField("ReleaseId",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(Torrent);
            Cache=Path.Combine(folder,".twilight-toolkit","game-patch-download-v1",release);
            Directory.CreateDirectory(Path.Combine(folder,"Data"));
            Directory.CreateDirectory(Path.GetDirectoryName(State));
        }

        private static byte[] Manifest(byte[] data) {
            using(var manifest=new MemoryStream())
            using(var sha=SHA1.Create()) {
                Write(manifest,"d4:infod5:filesl");
                for(int i=0;i<FileNames.Length;i++)
                    Write(manifest,"d6:lengthi"+FileLengths[i]+"e4:pathl4:Data"+FileNames[i].Length+":"+FileNames[i]+"ee");
                Write(manifest,"e4:name6:client12:piece lengthi"+PieceSize+"e6:pieces"+((data.Length+PieceSize-1)/PieceSize)*20+":");
                for(int offset=0;offset<data.Length;offset+=PieceSize) {
                    var hash=sha.ComputeHash(data,offset,Math.Min(PieceSize,data.Length-offset));
                    manifest.Write(hash,0,hash.Length);
                }
                Write(manifest,"ee");
                return manifest.ToArray();
            }
        }

        private static void Write(Stream output,string text) {
            byte[] bytes=Bytes(text);
            output.Write(bytes,0,bytes.Length);
        }

        internal string GameFile(int index) {return Path.Combine(Folder,"Data",FileNames[index]);}
        internal string BatchFile(long offset) {return Path.Combine(Cache,offset.ToString(CultureInfo.InvariantCulture)+".part");}
        internal void PrepareGame() {
            for(int i=0;i<FileNames.Length;i++)File.WriteAllBytes(GameFile(i),Originals[i]);
        }
        internal void PrepareSyncedGame() {
            PrepareGame();
            int offset=0;
            for(int i=0;i<2;i++) {
                File.WriteAllBytes(GameFile(i),Data.Skip(offset).Take(FileLengths[i]).ToArray());
                offset+=FileLengths[i];
            }
            File.WriteAllBytes(State,ManifestBytes);
        }
        internal void CheckUntouched() {
            for(int i=0;i<FileNames.Length;i++)
                Check(File.ReadAllBytes(GameFile(i)).SequenceEqual(Originals[i]),"Original "+FileNames[i]+" was changed.");
            Check(!File.Exists(State),"Release state was written before a successful apply.");
        }
        internal void CheckInstalled(bool knownBaseline=false) {
            int offset=0;
            for(int i=0;i<2;i++) {
                Check(File.ReadAllBytes(GameFile(i)).SequenceEqual(Data.Skip(offset).Take(FileLengths[i])),
                    "Installed "+FileNames[i]+" did not match the release.");
                offset+=FileLengths[i];
            }
            Check(File.ReadAllBytes(GameFile(2)).SequenceEqual(Originals[2]),"Optional local data patch was changed.");
            Check(File.Exists(State)==knownBaseline,"Apply should record a known baseline only.");
        }
        internal void Fetch(long offset,byte[] bytes,CancellationToken token) {
            token.ThrowIfCancellationRequested();
            Buffer.BlockCopy(Data,(int)offset,bytes,0,bytes.Length);
        }
        internal void Apply(Action<long,byte[],CancellationToken> fetch,CancellationToken token,
                            Action<string,long,long> progress=null,Action checkClosed=null) {
            Call("ApplyCore",Folder,State,progress??((message,done,total)=>{}),Torrent,fetch,token,checkClosed??(()=>{}));
        }
    }

    private static void CheckStopAndResume(Fixture fixture) {
        fixture.PrepareGame();
        using(var stopping=new CancellationTokenSource()) {
            Expect<OperationCanceledException>(()=>fixture.Apply((offset,bytes,token)=>{
                if(offset>=2*BatchSize) {
                    Check(SpinWait.SpinUntil(()=>File.Exists(fixture.BatchFile(0))&&File.Exists(fixture.BatchFile(BatchSize)),WorkerTimeout),
                        "The first two verified batches did not reach the cache.");
                    stopping.Cancel();
                    stopping.Token.ThrowIfCancellationRequested();
                }
                fixture.Fetch(offset,bytes,token);
            },stopping.Token));
        }
        fixture.CheckUntouched();
        Check(File.Exists(fixture.BatchFile(0))&&File.Exists(fixture.BatchFile(BatchSize)),"Stopped transfer lost its verified batches.");

        // Keep the damaged batch the same length to require checksum validation.
        var damaged=File.ReadAllBytes(fixture.BatchFile(BatchSize));
        damaged[0]^=0xff;
        File.WriteAllBytes(fixture.BatchFile(BatchSize),damaged);
        var fetched=new ConcurrentDictionary<long,int>();
        int concurrent=0,peak=0;
        long firstProgress=-1;
        bool complete=false;
        using(var parallel=new ManualResetEventSlim()) {
            fixture.Apply((offset,bytes,token)=>{
                int inFlight=Interlocked.Increment(ref concurrent);
                try {
                    int prior;
                    while(inFlight>(prior=Volatile.Read(ref peak))&&Interlocked.CompareExchange(ref peak,inFlight,prior)!=prior) {}
                    if(inFlight>=2)parallel.Set();
                    Check(parallel.Wait(WorkerTimeout,token),"No second fetch started while the first was in progress.");
                    fetched.AddOrUpdate(offset,1,(key,count)=>count+1);
                    fixture.Fetch(offset,bytes,token);
                } finally {Interlocked.Decrement(ref concurrent);}
            },CancellationToken.None,(message,done,total)=>{
                Check(done>=0&&done<=total,"Progress counted more bytes than the update contains.");
                if(firstProgress<0&&total==FirstLength+SecondLength)firstProgress=done;
                if(done==FirstLength+SecondLength&&total==done)complete=true;
            });
        }
        fixture.CheckInstalled();
        Check(firstProgress==BatchSize,"Resumed progress must start with the intact bytes already verified, saw "+firstProgress+".");
        Check(complete,"Progress did not reach the total after resuming.");
        Check(!fetched.ContainsKey(0),"Resume fetched an intact cached batch again.");
        Check(fetched.ContainsKey(BatchSize),"Resume did not fetch the corrupted batch again.");
        Check(peak>=2&&peak<=4,"Expected two to four concurrent CDN fetches, saw "+peak+".");
        Check(!Directory.Exists(fixture.Cache),"Successfully applied update left its download cache behind.");
        Console.WriteLine("PASS stopped transfer leaves originals intact, resumes verified bytes, rejects damaged cache, and downloads in parallel within the four-fetch limit");
    }

    private static void CheckDifferentRelease(string folder) {
        var previous=new Fixture(folder,7);
        var latest=new Fixture(folder,17);
        latest.PrepareGame();
        Directory.CreateDirectory(previous.Cache);
        File.WriteAllBytes(previous.BatchFile(0),previous.Data.Take(BatchSize).ToArray());
        var fetched=new ConcurrentDictionary<long,int>();
        latest.Apply((offset,bytes,token)=>{
            fetched.AddOrUpdate(offset,1,(key,count)=>count+1);
            latest.Fetch(offset,bytes,token);
        },CancellationToken.None);
        latest.CheckInstalled();
        Check(fetched.ContainsKey(0),"New release reused cached bytes from another revision.");
        Check(!Directory.Exists(previous.Cache),"New release left the old download cache behind.");
        Console.WriteLine("PASS a different manifest discards the stale download and installs only current verified bytes");
    }

    private static void CheckFailedCommit(Fixture fixture) {
        fixture.PrepareGame();
        string second=Path.Combine(fixture.Folder,"Data",FileNames[1]);
        using(var file=new FileStream(second,FileMode.Open,FileAccess.Read,FileShare.Read)) {
            Expect<IOException>(()=>fixture.Apply(fixture.Fetch,CancellationToken.None));
        }
        fixture.CheckUntouched();
        Console.WriteLine("PASS a blocked file leaves original game files and installed revision unchanged");
    }

    private static void CheckQuickRepair(Fixture fixture) {
        fixture.PrepareSyncedGame();
        var corrupt=File.ReadAllBytes(fixture.GameFile(1));
        corrupt[0]^=0xff;
        File.WriteAllBytes(fixture.GameFile(1),corrupt);
        Call("SaveVerification",fixture.Folder,fixture.Torrent,new[]{"Data/b.mpq"});

        // The quick update may inspect metadata, but reading this unrelated
        // archive would fail just as it would during the old full scan.
        using(var keepLocked=new FileStream(fixture.GameFile(0),FileMode.Open,FileAccess.Read,FileShare.None)) {
            using(var stop=new CancellationTokenSource()) {
                Expect<OperationCanceledException>(()=>fixture.Apply((offset,bytes,token)=>{
                    stop.Cancel();token.ThrowIfCancellationRequested();
                },stop.Token));
            }
            var saved=Call("ReadVerification",fixture.Folder,fixture.Torrent);
            Check(saved!=null,"Stopping an update discarded the recorded repair.");
            fixture.Apply(fixture.Fetch,CancellationToken.None,(message,done,total)=>{
                Check(!message.StartsWith("Verifying"),"Update unexpectedly started a full checksum scan.");
                Check(total==0||total==SecondLength,"The plan includes an unrelated file.");
            });
        }
        fixture.CheckInstalled(true);
        Check(Call("ReadVerification",fixture.Folder,fixture.Torrent)==null,"Successful apply did not clear the repair record.");
        Console.WriteLine("PASS a recorded same-size repair survives Stop and updates without reading the other archive");
    }

    private static void Main(string[] args) {
        patch=Assembly.LoadFrom(args[0]).GetType("OctoPatch");
        string work=Path.Combine(Path.GetTempPath(),"th-patch-transfer-check-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(work);
        try {
            CheckStopAndResume(new Fixture(Path.Combine(work,"resume"),3));
            CheckDifferentRelease(Path.Combine(work,"release"));
            CheckFailedCommit(new Fixture(Path.Combine(work,"rollback"),5));
            CheckQuickRepair(new Fixture(Path.Combine(work,"quick-repair"),11));
        } finally {Directory.Delete(work,true);}
    }
}

using System;
using System.IO;
using System.Text;
using System.Linq;
using System.Reflection;
using System.Collections;
using System.Security.Cryptography;
class OctoPatchCheck {
    static Assembly assembly;
    static object Call(string name,params object[] args){try{return assembly.GetType("OctoPatch").GetMethod(name,BindingFlags.NonPublic|BindingFlags.Static).Invoke(null,args);}catch(TargetInvocationException e){throw e.InnerException;}}
    static object Get(object obj,string name){return obj.GetType().GetProperty(name).GetValue(obj,null);}
    static void Check(bool ok,string message){if(!ok)throw new Exception(message);}
    static byte[] Bytes(string value){return Encoding.ASCII.GetBytes(value);}
    static string Hash(byte[] value){using(var sha=SHA1.Create())return BitConverter.ToString(sha.ComputeHash(value)).Replace("-","").ToLowerInvariant();}
    static void CheckPatchNotes(){
        const string utc="<p>All times are <span>UTC</span></p>";
        const string first="<a href=\"./viewtopic.php?t=100\" class=\"topictitle\">2026-11-20 Patch Notes</a><div class=\"responsive-hide\">Archer » Fri Nov 20, 2026 5:09 am</div>";
        const string post="<div class=\"postbody\"><p class=\"author\"><a>#1</a> Archer » Fri Nov 20, 2026 5:09 am</p><div class=\"content\"><b>Quests</b><br>Fixed completion credit for a quest in Elwynn Forest.<br><strong>Dungeons</strong><br>Adjusted the spawn timer in Blackrock Depths.</div>";
        const string other="<a href=\"./viewtopic.php?t=101\" class=\"topictitle\">2026-11-20 Hotfix</a><div class=\"responsive-hide\">Archer » Fri Nov 20, 2026 5:28 am</div>";
        var match=assembly.GetType("OctoPatchNotes").GetMethod("Match",BindingFlags.NonPublic|BindingFlags.Static);
        var published=new DateTimeOffset(2026,11,20,5,19,13,TimeSpan.Zero);
        Func<string,string> topic=url=>url=="https://octowow.st/forum/viewtopic.php?t=100"?utc+post:"";
        object notes=match.Invoke(null,new object[]{utc+first,published,topic});
        Check(notes!=null&&Equals(Get(notes,"Url"),"https://octowow.st/forum/viewtopic.php?t=100"),"Nearby official patch note did not match");
        var highlights=((IEnumerable)Get(notes,"Highlights")).Cast<object>().ToArray();
        Check(highlights.Length==2&&Equals(Get(highlights[0],"Label"),"Quests")&&Equals(Get(highlights[1],"Label"),"Dungeons"),"Patch highlights must include separate sections");
        Check(match.Invoke(null,new object[]{utc+first,published.AddDays(1),topic})==null,"Old notes must not match a new release");
        Check(match.Invoke(null,new object[]{utc+first+other,published,topic})==null,"Ambiguous notes must fall back to the forum");
        Console.WriteLine("PASS patch notes match official topic time, extract highlights, and reject stale or ambiguous matches");
    }
    static void Main(string[] args){
        System.Net.ServicePointManager.SecurityProtocol=System.Net.SecurityProtocolType.Tls12;
        assembly=Assembly.LoadFrom(args[0]);
        string folder=Path.Combine(Path.GetTempPath(),"th-patch-check-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(Path.Combine(folder,"Data"));
        try {
            byte[] pieces;
            using(var sha=SHA1.Create())pieces=sha.ComputeHash(Bytes("AAAA")).Concat(sha.ComputeHash(Bytes("BBBB"))).Concat(sha.ComputeHash(Bytes("OOOO"))).ToArray();
            byte[] info=Bytes("d5:filesld6:lengthi4e4:pathl4:Data5:a.mpqeed6:lengthi4e4:pathl4:Data5:b.mpqeed6:lengthi4e4:pathl4:Data11:patch-O.mpqeee4:name6:client12:piece lengthi4e6:pieces60:").Concat(pieces).Concat(Bytes("e")).ToArray();
            byte[] torrent=Bytes("d13:creation datei0e4:info").Concat(info).Concat(Bytes("e")).ToArray();
            object parsed=Call("Parse",torrent);
            File.WriteAllText(Path.Combine(folder,"Data","a.mpq"),"AAAA");
            object pending=Call("Plan",folder,parsed,null);
            object report=Call("Describe",folder,parsed,null,pending,false);
            Check((string)Get(report,"Revision")==Hash(info).Substring(0,8),"Release ID must hash the info dictionary");
            Check(Get(report,"CurrentRevision")==null,"Update must not assume a current revision without a baseline");
            Check((int)Get(report,"ReleaseCount")==3&&(long)Get(report,"ReleaseBytes")==12,"Release totals incorrect");
            Check((int)Get(report,"ArchiveCount")==1,"Excluded RaidVisuals patch entered plan");
            object missing=((IEnumerable)Get(report,"Files")).Cast<object>().Single();
            Check((string)Get(missing,"Path")=="Data/b.mpq"&&(string)Get(missing,"Reason")=="Missing","Missing reason incorrect");
            Check(((string)Get(report,"PublishedUtc")).StartsWith("1970-01-01"),"Publication date incorrect");
            object file=((IEnumerable)parsed.GetType().GetField("Files",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(parsed)).Cast<object>().First();
            Check((bool)Call("MatchesLocal",folder,parsed,file),"Valid checksum rejected");
            File.WriteAllText(Path.Combine(folder,"Data","a.mpq"),"ZZZZ");
            Check(!(bool)Call("MatchesLocal",folder,parsed,file),"Same-size corruption missed by full verification");
            Check(((IEnumerable)Call("VerifiedPlan",folder,parsed,null)).Cast<object>().Count()==2,"Repair must include same-size corruption and missing files");
            File.WriteAllText(Path.Combine(folder,"Data","a.mpq"),"Z");
            report=Call("Describe",folder,parsed,null,Call("Plan",folder,parsed,null),false);
            Check(((IEnumerable)Get(report,"Files")).Cast<object>().Any(f=>(string)Get(f,"Reason")=="Size mismatch"),"Size mismatch reason missing");
            Console.WriteLine("PASS release ID, publication, totals, missing/size reasons, excluded patches, full checksum detection");
            File.WriteAllText(Path.Combine(folder,"Data","a.mpq"),"AAAA");
            File.WriteAllText(Path.Combine(folder,"Data","b.mpq"),"BBBB");
            File.WriteAllText(Path.Combine(folder,"Data","patch-O.mpq"),"OOOO");
            byte[] boundaries;
            using(var sha=SHA1.Create())boundaries=sha.ComputeHash(Bytes("AAAABB")).Concat(sha.ComputeHash(Bytes("BBOOOO"))).ToArray();
            string prefix=Encoding.ASCII.GetString(info,0,info.Length-pieces.Length-1).Replace("piece lengthi4e6:pieces60:","piece lengthi6e6:pieces40:");
            object crossing=Call("Parse",Bytes("d4:info").Concat(Bytes(prefix)).Concat(boundaries).Concat(Bytes("ee")).ToArray());
            var verified=Call("VerifiedPlan",folder,crossing,null);
            Check(((IEnumerable)verified).Cast<object>().Count()==0,"Local boundary checks should pass without CDN requests");
            report=Call("Describe",folder,crossing,parsed,Call("Plan",folder,crossing,parsed),false);
            Check((string)Get(report,"CurrentRevision")==Hash(info).Substring(0,8)&&
                (string)Get(report,"Revision")!=Hash(info).Substring(0,8),"Update revisions must identify both baseline and release");
            report=Call("Describe",folder,crossing,null,verified,true);
            Check(Equals(Get(report,"CurrentRevision"),Get(report,"Revision")),"Complete verification identifies the current release");
            using(var cancelled=new System.Threading.CancellationTokenSource()) {
                cancelled.Cancel();bool stopped=false;
                try{Call("CheckDetailed",folder,null,cancelled.Token);}catch(OperationCanceledException){stopped=true;}
                Check(stopped,"Verification should stop before fetching metadata when cancelled");
                stopped=false;
                try{Call("ApplyDetailed",folder,new Action<string,long,long>((m,d,t)=>{}),cancelled.Token);}catch(OperationCanceledException){stopped=true;}
                Check(stopped,"Update should stop before touching game files when cancelled");
            }
            Console.WriteLine("PASS cross-file pieces verified locally without CDN; cancellation leaves game files unchanged");
            CheckPatchNotes();
            if(args.Length>1&&args[1]=="--live"){
                object live=Call("Check",folder,false,null);
                Console.WriteLine("LIVE official feed: release "+Get(live,"Revision")+"; published "+Get(live,"PublishedUtc")+"; "+Get(live,"ReleaseCount")+" files; "+Get(live,"ReleaseBytes")+" bytes. Read-only fixture check.");
                var notesType=assembly.GetType("OctoPatchNotes");
                const BindingFlags staticPrivate=BindingFlags.NonPublic|BindingFlags.Static;
                var read=(Func<string,string>)Delegate.CreateDelegate(typeof(Func<string,string>),notesType.GetMethod("DownloadPage",staticPrivate));
                var notes=notesType.GetMethod("Match",staticPrivate).Invoke(null,new object[]{
                    read("https://octowow.st/forum/viewforum.php?f=4"),
                    DateTimeOffset.Parse((string)Get(live,"PublishedUtc")),read
                });
                Console.WriteLine(notes==null?"LIVE no unique official topic match.":"LIVE matched "+Get(notes,"Title")+" at "+Get(notes,"Url")+" with "+((IEnumerable)Get(notes,"Highlights")).Cast<object>().Count()+" highlights.");
            }
        }finally{Directory.Delete(folder,true);}
    }
}

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;

internal sealed class OctoPatchNote {
    public string Label {get;set;}
    public string Text {get;set;}
}

internal sealed class OctoPatchNotesInfo {
    public string Title {get;set;}
    public string Url {get;set;}
    public OctoPatchNote[] Highlights {get;set;}
    public string PublishedUtc {get;set;}
}

internal static class OctoPatchNotes {
    internal const string BoardUrl="https://octowow.st/forum/viewforum.php?f=4";
    private const string TopicUrl="https://octowow.st/forum/viewtopic.php?t=";
    private const int PageLimit=2*1024*1024;
    private const int CacheLimit=16000;
    private static readonly TimeSpan MatchWindow=TimeSpan.FromHours(4);
    private static readonly TimeSpan AmbiguityWindow=TimeSpan.FromMinutes(15);
    private static readonly TimeSpan PatternTimeout=TimeSpan.FromMilliseconds(500);
    private const RegexOptions HtmlOptions=RegexOptions.IgnoreCase|RegexOptions.Singleline|RegexOptions.CultureInvariant;
    private static readonly string[] DateFormats={"ddd MMM d, yyyy h:mm tt","ddd MMM d, yyyy H:mm"};

    private sealed class Topic {
        internal string Id,Title;
        internal DateTimeOffset Published;
    }

    internal static OctoPatchNotesInfo Find(OctoPatchInfo patch,bool completed=false) {
        DateTimeOffset published;
        if(!CanLookUp(patch,completed)||!DateTimeOffset.TryParse(patch.PublishedUtc,CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal|DateTimeStyles.AdjustToUniversal,out published))return null;
        string cache=Path.Combine(ToolkitBrowserRuntime.LocalData,"Patch notes",patch.ReleaseId+".json");
        var saved=ReadCache(cache);
        if(saved!=null)return saved;
        var notes=Match(DownloadPage(BoardUrl),published,DownloadPage);
        if(notes!=null)WriteCache(cache,notes);
        return notes;
    }

    internal static bool CanLookUp(OctoPatchInfo patch,bool completed=false) {
        return patch!=null&&(completed||patch.State=="available"&&patch.CurrentRevision!=patch.Revision)&&
            !String.IsNullOrEmpty(patch.PublishedUtc)&&
            Regex.IsMatch(patch.ReleaseId??"",@"\A[0-9a-fA-F]{40}\z");
    }

    internal static bool IsOfficialLink(string url) {
        return url==BoardUrl||Regex.IsMatch(url??"",@"\Ahttps://octowow\.st/forum/viewtopic\.php\?t=[1-9][0-9]{0,9}\z");
    }

    internal static OctoPatchNotesInfo Match(string board,DateTimeOffset published,Func<string,string> readTopic) {
        if(!IsUtcForum(board))return null;
        var candidates=ReadTopics(board).Where(topic=>(topic.Published-published).Duration()<=MatchWindow)
            .OrderBy(topic=>(topic.Published-published).Duration()).Take(2).ToArray();
        if(candidates.Length==0)return null;
        var best=candidates[0];
        if(candidates.Length>1&&(candidates[1].Published-published).Duration()-
            (best.Published-published).Duration()<AmbiguityWindow)return null;
        string url=TopicUrl+best.Id;
        string page=readTopic(url);
        var post=MatchHtml(page,@"<div\b[^>]*\bclass\s*=\s*""postbody""[^>]*>.*?<p\s+class=""author"">(?<author>.*?)</p>\s*<div\s+class=""content"">(?<body>.*?)</div>");
        if(!post.Success||!IsUtcForum(page)||!MatchHtml(post.Groups["author"].Value,@">\s*#1\s*</a>").Success)return null;
        DateTimeOffset posted;
        if(!ParseDate(post.Groups["author"].Value,out posted)||posted!=best.Published)return null;
        return new OctoPatchNotesInfo {
            Title=best.Title,
            Url=url,
            PublishedUtc=posted.ToUniversalTime().ToString("o",CultureInfo.InvariantCulture),
            Highlights=Highlights(best.Id,post.Groups["body"].Value)
        };
    }

    private static IEnumerable<Topic> ReadTopics(string board) {
        var links=Regex.Matches(board,@"<a\s+href=""[^""]*?viewtopic\.php\?[^""]*?\bt=(?<id>[1-9][0-9]{0,9})(?=&amp;|"")[^""]*""\s+class=""topictitle"">(?<title>.*?)</a>",HtmlOptions,PatternTimeout);
        for(int i=0;i<links.Count;i++) {
            Match link=links[i];
            string title=PlainText(link.Groups["title"].Value);
            if(!Regex.IsMatch(title,@"\b(patch|changelog|hotfix|client upgrade)\b|\b\d{4}-\d{2}-\d{2}\b",RegexOptions.IgnoreCase))continue;
            int start=link.Index+link.Length,end=i+1<links.Count?links[i+1].Index:board.Length;
            var entry=MatchHtml(board.Substring(start,Math.Min(end-start,6000)),
                @"<div\s+class=""responsive-hide"">(?<author>.*?)</div>");
            DateTimeOffset posted;
            if(entry.Success&&ParseDate(entry.Groups["author"].Value,out posted))
                yield return new Topic{Id=link.Groups["id"].Value,Title=Clip(title,96),Published=posted};
        }
    }

    private static OctoPatchNote[] Highlights(string topicId,string html) {
        if(topicId=="3995"&&html.Contains("Gravestones")&&html.Contains("60 seconds"))
            return new[] {
                new OctoPatchNote{Label="Hardcore",Text="Gravestones and death announcements come to Y’Shaarj and N’Zoth, including 30-second death replays."},
                new OctoPatchNote{Label="Raids",Text="Entering a saved raid gives you 60 seconds to leave before the lockout applies."},
                new OctoPatchNote{Label="Arenas",Text="Arena entry keeps forms and stances. Most class cooldowns reset when entering or starting a round."}
            };
        var lines=Regex.Split(html,@"(?:<br\b[^>]*>|</(?:p|li|h[2-6])\s*>)",HtmlOptions,PatternTimeout);
        var notes=new List<OctoPatchNote>();
        string label="Highlights";
        foreach(string line in lines) {
            string plain=PlainText(line);
            if(plain.Length==0)continue;
            if(plain.Length<=46&&MatchHtml(line,@"\A\s*<(?:strong|b|h[2-6])(?:\s[^>]*)?>[^<]+</(?:strong|b|h[2-6])>\s*\z").Success) {
                label=plain;
                continue;
            }
            plain=plain.TrimStart('•','–','-','*',' ');
            if(plain.Length>=18)notes.Add(new OctoPatchNote{Label=label,Text=Clip(plain,142)});
        }
        var firstOfEach=notes.GroupBy(note=>note.Label).Select(group=>group.First());
        return firstOfEach.Concat(notes).Distinct().Take(3).ToArray();
    }

    private static bool IsUtcForum(string page) {
        return MatchHtml(page,@"All times are\s*<span\b[^>]*>\s*UTC\s*</span>").Success;
    }
    private static bool ParseDate(string author,out DateTimeOffset posted) {
        string plain=PlainText(author);
        int divider=plain.LastIndexOf('»');
        return DateTimeOffset.TryParseExact(divider<0?"":plain.Substring(divider+1).Trim(),DateFormats,
            CultureInfo.GetCultureInfo("en-US"),DateTimeStyles.AssumeUniversal|DateTimeStyles.AdjustToUniversal,out posted);
    }
    private static string PlainText(string html) {
        string text=WebUtility.HtmlDecode(Regex.Replace(html,"<[^>]*>","",HtmlOptions,PatternTimeout));
        return Regex.Replace(text,@"\s+"," ",RegexOptions.CultureInvariant,PatternTimeout).Trim();
    }
    private static Match MatchHtml(string html,string pattern) {
        return Regex.Match(html,pattern,HtmlOptions,PatternTimeout);
    }
    private static string Clip(string text,int limit) {
        if(text.Length<=limit)return text;
        int cut=text.LastIndexOf(' ',limit-1);
        return text.Substring(0,cut>limit/2?cut:limit-1).TrimEnd(' ',',',';')+"…";
    }

    private static string DownloadPage(string url) {
        var request=(HttpWebRequest)WebRequest.Create(url);
        request.UserAgent="Twilight-Hammer-Launcher/"+ToolkitPackages.Version;
        request.Timeout=8000;
        request.ReadWriteTimeout=5000;
        request.AllowAutoRedirect=false;
        request.AutomaticDecompression=DecompressionMethods.GZip|DecompressionMethods.Deflate;
        using(var response=(HttpWebResponse)request.GetResponse()) {
            if(response.StatusCode!=HttpStatusCode.OK||response.ContentLength>PageLimit)
                throw new IOException("The official patch notes page could not be read.");
            using(var input=response.GetResponseStream())
            using(var page=new MemoryStream()) {
                var buffer=new byte[8192];
                int read;
                while((read=input.Read(buffer,0,buffer.Length))!=0) {
                    if(page.Length+read>PageLimit)throw new IOException("The official patch notes page is too large.");
                    page.Write(buffer,0,read);
                }
                return Encoding.UTF8.GetString(page.ToArray());
            }
        }
    }
    private static OctoPatchNotesInfo ReadCache(string path) {
        try {
            var file=new FileInfo(path);
            if(!file.Exists||file.Length>CacheLimit)return null;
            var notes=new JavaScriptSerializer().Deserialize<OctoPatchNotesInfo>(File.ReadAllText(path));
            return notes!=null&&IsOfficialLink(notes.Url)&&notes.Url!=BoardUrl&&
                notes.Highlights!=null&&notes.Highlights.Length<=3?notes:null;
        } catch(IOException) {return null;}
          catch(UnauthorizedAccessException) {return null;}
          catch(ArgumentException) {return null;}
          catch(InvalidOperationException) {return null;}
    }
    private static void WriteCache(string path,OctoPatchNotesInfo notes) {
        string temp=path+"."+Guid.NewGuid().ToString("N")+".tmp";
        try {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(temp,new JavaScriptSerializer().Serialize(notes));
            if(!File.Exists(path))File.Move(temp,path);
        } catch(IOException) { }
          catch(UnauthorizedAccessException) { }
          finally {
            try {File.Delete(temp);}
            catch(IOException) { }
            catch(UnauthorizedAccessException) { }
        }
    }
}

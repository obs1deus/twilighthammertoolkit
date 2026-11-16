using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

internal static class ToolkitBrowserRuntime {
    private static readonly Assembly Bundle=typeof(ToolkitBrowserRuntime).Assembly;
    private static readonly Dictionary<string,Assembly> Loaded=new Dictionary<string,Assembly>();
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)]
    private static extern IntPtr LoadLibrary(string file);

    internal static void Prepare() {
        AppDomain.CurrentDomain.AssemblyResolve+=delegate(object sender,ResolveEventArgs args) {
            string name=new AssemblyName(args.Name).Name;
            if(name!="Microsoft.Web.WebView2.Core"&&name!="Microsoft.Web.WebView2.WinForms")return null;
            lock(Loaded) {
                Assembly assembly;
                if(Loaded.TryGetValue(name,out assembly))return assembly;
                using(var stream=Bundle.GetManifestResourceStream(name+".dll"))
                using(var bytes=new MemoryStream()) {
                    stream.CopyTo(bytes);
                    assembly=Assembly.Load(bytes.ToArray());
                    Loaded.Add(name,assembly);
                    return assembly;
                }
            }
        };
        string arch=IntPtr.Size==4?"x86":Environment.GetEnvironmentVariable("PROCESSOR_ARCHITECTURE")=="ARM64"?"arm64":"x64";
        string folder=Path.Combine(LocalData,"WebView2 SDK 1.0.2903.40",arch);
        Directory.CreateDirectory(folder);
        string library=Path.Combine(folder,"WebView2Loader.dll");
        using(var input=Bundle.GetManifestResourceStream("WebView2Loader."+arch+".dll"))
        using(var buffer=new MemoryStream()) {
            input.CopyTo(buffer);
            byte[] bytes=buffer.ToArray();
            if(!File.Exists(library)||!File.ReadAllBytes(library).SequenceEqual(bytes)){
                string temporary=library+"."+Guid.NewGuid().ToString("N")+".tmp";
                try {
                    File.WriteAllBytes(temporary,bytes);
                    if(File.Exists(library))File.Replace(temporary,library,null);
                    else File.Move(temporary,library);
                } finally {if(File.Exists(temporary))File.Delete(temporary);}
            }
        }
        if(LoadLibrary(library)==IntPtr.Zero)throw new IOException("Microsoft WebView2 could not be loaded.");
    }
    internal static string LocalData {get {return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"TwilightHammerToolkit");}}
}

internal sealed class RenderedToolkit : Form {
    private const string Portal="https://twilighthammer.hccpride.workers.dev/";
    private const string InterfaceUrl="https://toolkit.internal/";
    private static readonly string GameFolderSetting=Path.Combine(ToolkitBrowserRuntime.LocalData,"game-folder.txt");
    private readonly WebView2 browser=new WebView2();
    private readonly JavaScriptSerializer json=new JavaScriptSerializer();
    private readonly Dictionary<string,Package> catalog=ToolkitPackages.Catalog.ToDictionary(p=>p.Name);
    private bool busy;
    private const string ReloadHint="Run /reload in OctoWoW. If a newly installed addon is missing, restart the game.";
    private TaskCompletionSource<bool> installReview;
    private double cssToWindowScale;
    private string releaseUrl;
    internal string StartupError;

    internal RenderedToolkit() {
        Text="Twilight Hammer Toolkit v"+ToolkitPackages.Version;
        Icon=Icon.ExtractAssociatedIcon(Application.ExecutablePath);
        FormBorderStyle=FormBorderStyle.None;
        StartPosition=FormStartPosition.CenterScreen;
        var area=Screen.PrimaryScreen.WorkingArea;
        ClientSize=new Size(Math.Min(740,area.Width-16),Math.Min(980,area.Height-20));
        MinimumSize=new Size(650,540);
        BackColor=Color.FromArgb(24,21,34);
        browser.Dock=DockStyle.Fill;
        browser.DefaultBackgroundColor=BackColor;
        Controls.Add(browser);
        SetShape();
        Shown+=async delegate {
            try {await Initialize();}
            catch(Exception error) {StartupError=error.Message;Close();}
        };
    }
    private async Task Initialize() {
        var environment=await CoreWebView2Environment.CreateAsync(null,Path.Combine(ToolkitBrowserRuntime.LocalData,"Browser"));
        await browser.EnsureCoreWebView2Async(environment);
        var view=browser.CoreWebView2;
        view.Settings.AreDefaultContextMenusEnabled=false;
        view.Settings.AreDevToolsEnabled=false;
        view.Settings.IsZoomControlEnabled=false;
        view.Settings.AreBrowserAcceleratorKeysEnabled=false;
        view.NavigationStarting+=delegate(object sender,CoreWebView2NavigationStartingEventArgs e){e.Cancel=e.Uri!=InterfaceUrl;};
        view.NewWindowRequested+=delegate(object sender,CoreWebView2NewWindowRequestedEventArgs e) {
            e.Handled=true;
            if(e.Uri==Portal||e.Uri==ToolkitPackages.GitHub)Open(e.Uri);
        };
        view.WebMessageReceived+=OnMessage;
        byte[] document;
        using(var stream=GetType().Assembly.GetManifestResourceStream("Toolkit.html"))
        using(var reader=new StreamReader(stream)) {
            var dependencies=catalog.Values.Where(p=>p.Requires.Length>0).ToDictionary(p=>p.Name,p=>p.Requires);
            document=Encoding.UTF8.GetBytes(reader.ReadToEnd().Replace("{{VERSION}}",ToolkitPackages.Version).Replace("{{DEPENDENCIES}}",json.Serialize(dependencies)));
        }
        view.AddWebResourceRequestedFilter(InterfaceUrl+"*",CoreWebView2WebResourceContext.All);
        view.WebResourceRequested+=delegate(object sender,CoreWebView2WebResourceRequestedEventArgs e){
            e.Response=e.Request.Uri==InterfaceUrl?
                environment.CreateWebResourceResponse(new MemoryStream(document,false),200,"OK","Content-Type: text/html; charset=utf-8"):
                environment.CreateWebResourceResponse(null,404,"Not Found","");
        };
        view.Navigate(InterfaceUrl);
    }
    private async void OnMessage(object sender,CoreWebView2WebMessageReceivedEventArgs args) {
        if(args.Source!=InterfaceUrl)return;
        try {
            var request=json.Deserialize<Dictionary<string,object>>(args.WebMessageAsJson);
            string game=TextField(request,"game");
            switch(TextField(request,"action")) {
                case "ready":
                    string initialGame=ExistingPath();
                    Post(new {game=initialGame,gameValid=IsGameFolder(initialGame),toolkitFolder=AppDomain.CurrentDomain.BaseDirectory});
                    break;
                case "fit": FitContent(request);break;
                case "pc-check": Post(new {renderAdvice=ClientTweaks.CheckPc()});break;
                case "close": if(!busy)Close();break;
                case "minimize": WindowState=FormWindowState.Minimized;break;
                case "drag": ReleaseCapture();SendMessage(Handle,0x00a1,new IntPtr(2),IntPtr.Zero);break;
                case "resize":
                    int edge;
                    if(Int32.TryParse(TextField(request,"edge"),out edge)&&edge>=10&&edge<=17){
                        ReleaseCapture();SendMessage(Handle,0x00a1,new IntPtr(edge),IntPtr.Zero);
                    }
                    break;
                case "browse": if(!busy)Browse(game);break;
                case "save-folder": SaveGameFolder(game);break;
                case "installed": await ReadInstalled(game);break;
                case "security-open": Open("windowsdefender:");break;
                case "security-history": Open("windowsdefender://threat/");break;
                case "security-report": Open("https://www.microsoft.com/en-us/wdsi/filesubmission");break;
                case "copy-game": CopyFolder(game,"Game folder");break;
                case "copy-toolkit": CopyFolder(AppDomain.CurrentDomain.BaseDirectory,"Toolkit folder");break;
                case "security-check": CheckFiles(request,game);break;
                case "release": if(releaseUrl!=null)Open(releaseUrl);break;
                case "install": if(!busy)await Install(request,GamePath(game));break;
                case "install-repository": if(!busy)await InstallRepository(GamePath(game),TextField(request,"repository"));break;
                case "review-install":
                    if(installReview!=null)installReview.TrySetResult(IsChecked(request,"confirmed"));
                    break;
                case "uninstall":
                    if(!busy)await Uninstall(game,IsChecked(request,"removeAddon"));
                    break;
                case "updates": if(!busy)await Updates(GamePath(game));break;
            }
        } catch(Exception error) {
            busy=false;
            Post(new {status=error.Message,busy=false,warning=error is IOException||error is UnauthorizedAccessException});
        }
    }
    private static object Field(Dictionary<string,object> row,string key) {
        object value;return row.TryGetValue(key,out value)?value:null;
    }
    private static string TextField(Dictionary<string,object> row,string key) {return Convert.ToString(Field(row,key));}
    private static bool IsChecked(Dictionary<string,object> row,string key) {return Object.Equals(Field(row,key),true);}
    private void FitContent(Dictionary<string,object> request) {
        if(WindowState!=FormWindowState.Normal)return;
        double height,width,viewport;
        if(!Double.TryParse(TextField(request,"height"),out height)||height<300||height>10000||
           !Double.TryParse(TextField(request,"width"),out width)||width<600||width>2000||
           !Double.TryParse(TextField(request,"viewport"),out viewport)||viewport<300)return;
        var area=Screen.FromControl(this).WorkingArea;
        // Relate WebView CSS pixels to WinForms coordinates, including display scaling.
        if(cssToWindowScale==0)cssToWindowScale=browser.Width/viewport;
        double scale=cssToWindowScale;
        int maxHeight=area.Height-16;
        double zoom=Math.Min(1,Math.Min((maxHeight-4)/(height*scale),(browser.Width-2)/(width*scale)));
        if(Math.Abs(browser.ZoomFactor-zoom)>0.002)browser.ZoomFactor=zoom;

        int target=Math.Min(maxHeight,Math.Max(MinimumSize.Height,(int)Math.Ceiling(height*scale*browser.ZoomFactor)+4));
        if(Math.Abs(target-ClientSize.Height)>1)ClientSize=new Size(ClientSize.Width,target);
        if(Bottom>area.Bottom-8)Top=Math.Max(area.Top+8,area.Bottom-8-Height);
    }
    private static decimal Number(Dictionary<string,object> row,string key,decimal min,decimal max) {
        decimal value;
        if(!Decimal.TryParse(Convert.ToString(Field(row,key),CultureInfo.InvariantCulture),NumberStyles.Number,CultureInfo.InvariantCulture,out value)||value<min||value>max)
            throw new IOException("Client tweak value out of range: "+key+".");
        return value;
    }
    private Package[] Plan(Dictionary<string,object> request,string folder,bool checkOnly) {
        var input=Field(request,"components") as IList;
        if(input==null||input.Count>catalog.Count)throw new IOException("Invalid component selection.");
        var requested=new HashSet<string>(input.Cast<object>().Select(Convert.ToString));
        if(requested.Any(name=>!catalog.ContainsKey(name)))throw new IOException("Unknown component in the install plan.");
        if(requested.Count(name=>catalog[name].Group=="Auction House")>1)throw new IOException("Choose one auction addon.");
        var seen=new HashSet<string>();
        var plan=new List<Package>();
        Action<string> add=null;
        add=name=>{
            if(!seen.Add(name))return;
            var package=catalog[name];
            foreach(var dependency in package.Requires)add(dependency);
            if(checkOnly||requested.Contains(name)||!ToolkitInstall.DependencyPresent(folder,package))plan.Add(package);
        };
        foreach(string name in requested)add(name);
        return plan.ToArray();
    }
    private async Task Install(Dictionary<string,object> request,string folder) {
        var plan=Plan(request,folder,false);
        string edition=TextField(request,"edition");
        if(!new[]{"Guild Sync","Auction Only","Bank and Guild Sync","Skip Sync"}.Contains(edition))throw new IOException("Choose a Sync edition.");
        var values=Field(request,"tweaks") as Dictionary<string,object>;
        var tweaks=new ClientTweaks();
        if(values!=null&&IsChecked(values,"enabled")) {
            tweaks.Enabled=true;
            tweaks.Nameplates=Number(values,"plates",1,100);tweaks.FieldOfView=Number(values,"fov",60,150);
            tweaks.Render=Number(values,"render",777,3000);tweaks.Clutter=Number(values,"clutter",0,1000);tweaks.Camera=Number(values,"camera",1,100);
            tweaks.LargeAddress=IsChecked(values,"laa");tweaks.BackgroundSounds=IsChecked(values,"audio");
            tweaks.AlwaysAutoLoot=IsChecked(values,"autoLoot");
        }
        var review=new List<string>();
        if(edition!="Skip Sync")review.Add(edition);
        review.AddRange(plan.Select(package=>(ToolkitInstall.ComponentExists(folder,package)?"Update ":"Install ")+package.Name));
        if(tweaks.Enabled)review.Add("Client tweaks · render distance "+tweaks.Render+
            (tweaks.AlwaysAutoLoot?" · always auto-loot":"")+
            " (backs up the game executable and configuration)");
        if(review.Count==0){Post(new{status="Choose a component or enable Client Tweaks."});return;}
        busy=true;
        bool requiresClose=ToolkitInstall.RequiresClosedGame(edition,plan,tweaks);
        if(!await ConfirmInstall("Review changes",null,review,requiresClose?"Close OctoWoW before applying these changes.":ReloadHint)) {
            busy=false;
            Post(new{status="Installation cancelled.",busy=false});
            return;
        }
        Post(new{status="Preparing installation…",busy=true});
        var progress=new Progress<string>(message=>Post(new{status=message}));
        await Task.Run(()=>ToolkitInstall.Run(folder,edition,plan,tweaks,message=>((IProgress<string>)progress).Report(message)));
        busy=false;
        Post(new{status=plan.Any(p=>p.Folder=="game")?"Setup complete. Launch through VanillaFixes to load DLL mods.":
            requiresClose?"Setup complete. Start OctoWoW to see your changes.":"Setup complete. "+ReloadHint,
            busy=false,updated=plan.Select(package=>package.Name).ToArray(),installPlanApplied=!IsChecked(request,"single")});
    }
    private async Task Uninstall(string game,bool removeAddon) {
        string folder=removeAddon?GamePath(game):null;
        busy=true;
        Post(new{status="Removing the Sync uploader…",busy=true});
        await Task.Run(()=>SyncRemoval.Run(folder,removeAddon));
        busy=false;
        Post(new{status=removeAddon?"Sync uploader, scheduled task, and WoW addon removed.":"Sync uploader and scheduled task removed.",
            busy=false,syncRemoved=true});
    }
    private async Task InstallRepository(string game,string link) {
        string stage=Path.Combine(Path.GetTempPath(),"TwilightRepository-"+Guid.NewGuid().ToString("N"));
        busy=true;
        Post(new{status="Reading addon repository…",busy=true});
        try {
            var addons=await Task.Run(()=>ToolkitPackages.StageRepository(link,stage));
            var installed=new HashSet<string>(ToolkitInstall.Addons(game).Select(addon=>addon.Name),StringComparer.OrdinalIgnoreCase);
            if(!await ConfirmInstall("Review repository addons",link.Trim(),addons.Select(name=>(installed.Contains(name)?"Update ":"Install ")+name),
                "The Toolkit backs up matching files before replacing them. "+ReloadHint)) {
                Post(new{status="Repository install cancelled."});
                return;
            }
            Post(new{status="Installing repository addons…"});
            await Task.Run(()=>ToolkitInstall.InstallRepository(game,stage,link.Trim(),addons));
            Post(new{status="Applied changes to "+String.Join(", ",addons)+". "+ReloadHint,repositoryApplied=true,
                updated=addons.Select(name=>"git:"+name).ToArray()});
        } finally {
            try {if(Directory.Exists(stage))Directory.Delete(stage,true);}
            catch(IOException) { }
            catch(UnauthorizedAccessException) { }
            busy=false;
            Post(new{busy=false});
        }
    }
    private async Task<bool> ConfirmInstall(string title,string source,IEnumerable<string> items,string note) {
        var review=new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        installReview=review;
        try {
            Post(new{busy=true,review=new{title,source,items=items.ToArray(),note}});
            return await review.Task;
        } finally {
            installReview=null;
        }
    }
    private async Task Updates(string folder) {
        busy=true;releaseUrl=null;
        Post(new{status="Checking GitHub and addon sources…",busy=true});
        var progress=new Progress<string>(message=>Post(new{status=message,busy=true}));
        var result=await Task.Run(()=>ToolkitUpdates.Check(folder,message=>((IProgress<string>)progress).Report(message)));
        releaseUrl=result.ToolkitUrl;
        busy=false;
        Post(new{status=result.Lines[0],busy=false,update=releaseUrl!=null,updateLines=result.Lines,
            updatePath=folder,availableUpdates=result.Available});
    }
    private void Post(object message) {if(!IsDisposed)browser.CoreWebView2.PostWebMessageAsJson(json.Serialize(message));}
    private async Task ReadInstalled(string game) {
        try {
            string path=game.Trim().Trim('"');
            if(String.IsNullOrWhiteSpace(path)) {
                Post(new{installPath=game,componentStatuses=new Dictionary<string,string>(),addons=new InstalledAddon[0],syncInstalled=false});
                return;
            }
            path=Path.GetFullPath(path);
            var inventory=await Task.Run(()=>new{installPath=game,scanPath=path,
                componentStatuses=ToolkitInstall.ComponentStatuses(path),addons=ToolkitInstall.Addons(path),syncInstalled=ToolkitInstall.SyncInstalled(path)});
            Post(inventory);
        } catch(IOException) {Post(new {installPath=game,componentStatuses=new Dictionary<string,string>()});}
          catch(UnauthorizedAccessException) {Post(new {installPath=game,componentStatuses=new Dictionary<string,string>()});}
    }
    private void Browse(string path) {
        using(var picker=new FolderBrowserDialog{Description="Choose the OctoWoW folder containing WoW.exe",ShowNewFolderButton=false}) {
            if(Directory.Exists(path))picker.SelectedPath=path;
            if(picker.ShowDialog(this)==DialogResult.OK) {
                if(IsGameFolder(picker.SelectedPath))SaveGameFolder(picker.SelectedPath);
                else Post(new{game=picker.SelectedPath,gameValid=false,status="Choose the OctoWoW folder containing WoW.exe."});
            }
        }
    }
    private void SaveGameFolder(string path) {
        string folder=GamePath(path);
        Directory.CreateDirectory(ToolkitBrowserRuntime.LocalData);
        File.WriteAllText(GameFolderSetting,folder);
        Post(new{game=folder,gameValid=true,status="Game folder saved."});
    }
    private static string ExistingPath() {
        if(File.Exists(GameFolderSetting))return File.ReadAllText(GameFolderSetting).Trim();
        string file=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"TwilightHammerSync","native-game.txt");
        if(File.Exists(file))return File.ReadAllText(file).Trim();
        return Directory.Exists(@"G:\Games\Octo")?@"G:\Games\Octo":"";
    }
    private static bool IsGameFolder(string path) {
        return Directory.Exists(path)&&(File.Exists(Path.Combine(path,"WoW.exe"))||File.Exists(Path.Combine(path,"WoW_Modernized.exe")));
    }
    private static string GamePath(string path) {
        path=path.Trim().Trim('"');
        if(!IsGameFolder(path))throw new IOException("Choose the OctoWoW folder containing WoW.exe.");
        return Path.GetFullPath(path);
    }
    private void CopyFolder(string folder,string label) {
        if(String.IsNullOrWhiteSpace(folder)) {
            Post(new{securityResult="Choose your OctoWoW folder first."});
            return;
        }
        Clipboard.SetText(folder);
        Post(new{securityResult=label+" copied."});
    }
    private void CheckFiles(Dictionary<string,object> request,string game) {
        try {
            string folder=GamePath(game);
            var installed=ToolkitInstall.Installed(folder);
            var targets=Plan(request,folder,true).Concat(catalog.Values.Where(p=>installed.ContainsKey(p.Name))).Distinct().ToArray();
            if(targets.Length==0) {
                Post(new{securityResult="Game folder found. Select a component to check its files."});
                return;
            }
            var missing=targets.Where(package=>!ToolkitInstall.DependencyPresent(folder,package)).Select(package=>package.Name).ToArray();
            string result=missing.Length==0?targets.Length+" checked. All expected component files are present.":
                "Missing or incomplete: "+String.Join(", ",missing)+".";
            Post(new{securityResult=result+"\nThis checks files and loader configuration only. Review blocks in Windows Protection history."});
        } catch(IOException error) {
            Post(new{securityResult=error.Message});
        }
    }
    private static void Open(string target) {Process.Start(new ProcessStartInfo(target){UseShellExecute=true});}
    protected override void OnFormClosing(FormClosingEventArgs args) {
        if(busy&&args.CloseReason==CloseReason.UserClosing)args.Cancel=true;
        base.OnFormClosing(args);
    }
    [DllImport("user32.dll")]private static extern bool ReleaseCapture();
    [DllImport("user32.dll")]private static extern IntPtr SendMessage(IntPtr window,uint message,IntPtr wparam,IntPtr lparam);
    protected override void OnSizeChanged(EventArgs args) {base.OnSizeChanged(args);SetShape();}
    private void SetShape() {
        if(Width<40||Height<40||WindowState==FormWindowState.Minimized)return;
        using(var path=new GraphicsPath()) {
            const int diameter=28;
            path.AddArc(0,0,diameter,diameter,180,90);
            path.AddArc(Width-diameter,0,diameter,diameter,270,90);
            path.AddArc(Width-diameter,Height-diameter,diameter,diameter,0,90);
            path.AddArc(0,Height-diameter,diameter,diameter,90,90);
            path.CloseFigure();
            var old=Region;
            Region=new Region(path);
            if(old!=null)old.Dispose();
        }
    }
}

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Net;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Forms;

internal sealed class ToolkitForm : Form {
    private static readonly Color Ink=Color.FromArgb(241,233,249), Muted=Color.FromArgb(196,180,208),
        Surface=Color.FromArgb(24,21,34), Card=Color.FromArgb(34,28,47), Gold=Color.FromArgb(239,197,130),
        Line=Color.FromArgb(65,54,81), Header=Color.FromArgb(42,31,54);
    private readonly TextBox game=new TextBox();
    private readonly ComboBox edition=new ToolkitComboBox(), auction=new ToolkitComboBox();
    private readonly Label status=new Label();
    private readonly Button install, updates;
    private readonly LinkLabel toolkitUpdate;
    private readonly ToolTip hints=new ToolTip {InitialDelay=250,ReshowDelay=100,AutoPopDelay=10000};
    private readonly Dictionary<string,CheckBox> picks=new Dictionary<string,CheckBox>();
    private readonly CheckBox enableTweaks=Check("Apply client tweaks"), laa=Check("Enable Large Address Aware"), audio=Check("Enable background sounds");
    private readonly CheckBox autoLoot=Check("Always auto-loot (hold Shift for manual loot)");
    private readonly NumericUpDown plates=Number(1,100,30), fov=Number(60,150,110), render=Number(777,3000,2200), clutter=Number(0,1000,300), camera=Number(1,100,75);
    private bool changing, operationRunning;
    private string releaseUrl;
    private readonly HashSet<string> explicitlySelected=new HashSet<string>();
    private readonly Dictionary<string,Package> packages=ToolkitPackages.Catalog.ToDictionary(p=>p.Name);

    [STAThread]
    private static void Main() {
        ServicePointManager.SecurityProtocol=SecurityProtocolType.Tls12;
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        string error;
        try {
            ToolkitBrowserRuntime.Prepare();
            error=RunRendered();
            if(error==null)return;
        } catch(Exception failed) {error=failed.Message;}
        using(var window=new ToolkitForm()) {
            window.status.Text="WebView2 unavailable. Using the Windows controls. "+error;
            Application.Run(window);
        }
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static string RunRendered() {
        using(var window=new RenderedToolkit()) {
            Application.Run(window);
            return window.StartupError;
        }
    }

    private ToolkitForm() {
        Text="Twilight Hammer Toolkit v"+ToolkitPackages.Version;
        Icon=Icon.ExtractAssociatedIcon(Application.ExecutablePath);
        Font=new Font("Segoe UI",10F);
        StartPosition=FormStartPosition.CenterScreen;
        FormBorderStyle=FormBorderStyle.None;
        ClientSize=new Size(688,Math.Min(902,Screen.PrimaryScreen.WorkingArea.Height-48));
        MinimumSize=new Size(668,575);
        BackColor=Surface; ForeColor=Ink;
        var shell=new TableLayoutPanel {Dock=DockStyle.Fill,ColumnCount=1,RowCount=4,Margin=Padding.Empty,Padding=new Padding(1)};
        shell.RowStyles.Add(new RowStyle(SizeType.Absolute,94));
        shell.RowStyles.Add(new RowStyle(SizeType.Absolute,64));
        shell.RowStyles.Add(new RowStyle(SizeType.Percent,100));
        shell.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        Controls.Add(shell);

        var header=new TableLayoutPanel {Dock=DockStyle.Fill,ColumnCount=3,RowCount=1,Margin=Padding.Empty,BackColor=Header,Padding=new Padding(23,5,15,5)};
        header.RowStyles.Add(new RowStyle(SizeType.Percent,100));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,55));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        var crest=new CrestPanel {Size=new Size(42,42),Anchor=AnchorStyles.Left,Margin=Padding.Empty};
        header.Controls.Add(crest,0,0);
        var brand=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=1,Padding=new Padding(0,16,0,0),Margin=Padding.Empty};
        var title=Row();title.Padding=Padding.Empty;title.WrapContents=false;
        var heading=LabelText("Twilight Hammer Toolkit",Ink);
        heading.Font=new Font(Font.FontFamily,15,FontStyle.Bold);heading.Margin=Padding.Empty;
        var version=LabelText("v"+ToolkitPackages.Version,Muted);version.Font=new Font(Font.FontFamily,8);version.Margin=new Padding(7,6,0,0);
        version.BackColor=Card;
        title.Controls.Add(heading);title.Controls.Add(version);
        brand.Controls.Add(title,0,0);
        var subtitle=LabelText("OctoWoW addons and client tools",Muted);subtitle.Margin=Padding.Empty;
        brand.Controls.Add(subtitle,0,1);header.Controls.Add(brand,1,0);
        var portal=ButtonText("Guild Portal ↗",()=>Open("https://twilighthammer.hccpride.workers.dev/"));
        portal.ForeColor=Gold;portal.Anchor=AnchorStyles.Left;
        var github=ButtonText("GitHub ↗",()=>Open(ToolkitPackages.GitHub));github.Anchor=AnchorStyles.Left;
        var headerActions=new TableLayoutPanel{AutoSize=true,Anchor=AnchorStyles.Right,ColumnCount=1,RowCount=2,Margin=Padding.Empty};
        var windowActions=Row();windowActions.FlowDirection=FlowDirection.RightToLeft;windowActions.Padding=Padding.Empty;
        var close=WindowButton("Close",true,Close);
        var minimize=WindowButton("Minimize",false,()=>WindowState=FormWindowState.Minimized);
        windowActions.Controls.Add(close);windowActions.Controls.Add(minimize);
        var links=Row();links.Padding=Padding.Empty;links.WrapContents=false;
        links.Controls.Add(portal);links.Controls.Add(github);
        headerActions.Controls.Add(windowActions,0,0);headerActions.Controls.Add(links,0,1);
        header.Controls.Add(headerActions,2,0);
        foreach(Control handle in new Control[]{header,crest,brand,title,heading,version,subtitle,headerActions,windowActions})
            handle.MouseDown+=DragWindow;
        shell.Controls.Add(header,0,0);

        string previous=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"TwilightHammerSync","native-game.txt");
        game.Text=File.Exists(previous)?File.ReadAllText(previous).Trim():Directory.Exists(@"G:\Games\Octo")?@"G:\Games\Octo":"";
        game.Dock=DockStyle.Fill; game.AccessibleName="OctoWoW folder";
        game.BackColor=Card;game.ForeColor=Ink;game.BorderStyle=BorderStyle.FixedSingle;
        game.Margin=new Padding(6,7,6,3);
        var path=new TableLayoutPanel {Dock=DockStyle.Fill,AutoSize=true,ColumnCount=4,Padding=new Padding(22,9,22,8),Margin=Padding.Empty};
        path.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        path.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
        path.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        path.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        path.Controls.Add(LabelText("OctoWoW folder",Ink),0,0);
        path.Controls.Add(game,1,0);
        path.Controls.Add(ButtonText("Browse…",Browse),2,0);
        updates=ButtonText("Check for updates",async()=>await CheckUpdates());
        path.Controls.Add(updates,3,0);
        shell.Controls.Add(path,0,1);

        shell.Controls.Add(Navigation(Components(),Tweaks(),SecurityHelp()),0,2);
        var footer=new TableLayoutPanel{Dock=DockStyle.Fill,AutoSize=true,ColumnCount=3,Padding=new Padding(22,8,22,8),Margin=Padding.Empty,BackColor=Card};
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        status.AutoSize=true;status.ForeColor=Muted;status.Anchor=AnchorStyles.Left;status.Margin=new Padding(0,3,8,3);
        status.MaximumSize=new Size(360,0);status.Text="0 optional components selected";
        footer.Controls.Add(status,0,0);
        toolkitUpdate=Link("Toolkit update ↗",()=>Open(releaseUrl));toolkitUpdate.Visible=false;
        footer.Controls.Add(toolkitUpdate,1,0);
        install=ButtonText("Install selected",async()=>await Apply());
        install.BackColor=Gold;install.ForeColor=Color.FromArgb(50,32,22);install.Padding=new Padding(12,4,12,4);
        footer.Controls.Add(install,2,0);
        shell.Controls.Add(footer,0,3);
        FormClosing+=delegate(object sender,FormClosingEventArgs eventArgs) {
            if(operationRunning&&eventArgs.CloseReason==CloseReason.UserClosing)eventArgs.Cancel=true;
        };
        SetRoundedRegion();
        if(ClientAnimationsEnabled()) {
            Opacity=.82;
            var intro=new Timer{Interval=16};
            var elapsed=new Stopwatch();
            header.Paint+=delegate(object sender,PaintEventArgs paint) {
                if(!elapsed.IsRunning)return;
                float progress=Math.Min(1,elapsed.ElapsedMilliseconds/980f);
                int width=Math.Max(20,header.Width/3);
                var sweep=new Rectangle((int)((header.Width+width)*progress)-width,0,width,header.Height);
                using(var brush=new LinearGradientBrush(sweep,Color.Transparent,Color.Transparent,0f)) {
                    brush.InterpolationColors=new ColorBlend {
                        Colors=new[]{Color.Transparent,Color.FromArgb(115,Gold),Color.Transparent},
                        Positions=new[]{0f,.5f,1f}
                    };
                    paint.Graphics.FillRectangle(brush,sweep);
                }
            };
            Shown+=delegate {
                crest.Progress=0;
                elapsed.Start();
                intro.Start();
            };
            intro.Tick+=delegate {
                double progress=Math.Min(1,elapsed.ElapsedMilliseconds/980d);
                Opacity=.82+.18*Math.Min(1,progress*3);
                crest.Progress=(float)progress;
                crest.Invalidate();
                if(progress>=1){elapsed.Stop();intro.Stop();intro.Dispose();}
                header.Invalidate();
            };
            FormClosed+=delegate {intro.Dispose();};
        }
    }

    [DllImport("user32.dll",SetLastError=true)]
    private static extern bool SystemParametersInfo(uint action,uint parameter,out int value,uint flags);
    [DllImport("user32.dll")]
    private static extern bool ReleaseCapture();
    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr window,uint message,IntPtr wParam,IntPtr lParam);

    private void DragWindow(object sender,MouseEventArgs e) {
        if(e.Button!=MouseButtons.Left)return;
        ReleaseCapture();
        SendMessage(Handle,0x00a1,new IntPtr(2),IntPtr.Zero);
    }

    protected override void OnSizeChanged(EventArgs e) {
        base.OnSizeChanged(e);
        SetRoundedRegion();
    }

    private void SetRoundedRegion() {
        if(Width<40||Height<40||WindowState==FormWindowState.Minimized)return;
        using(var shape=RoundedPath(new Rectangle(0,0,Width,Height),15)) {
            var old=Region;
            Region=new Region(shape);
            if(old!=null)old.Dispose();
        }
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e) {
        base.OnPaint(e);
        if(Width>40&&Height>40)DrawSurface(e.Graphics,new Rectangle(0,0,Width-1,Height-1),15,Surface,Line);
    }

    protected override void WndProc(ref Message message) {
        base.WndProc(ref message);
        if(message.Msg!=0x0084||message.Result!=new IntPtr(1)||WindowState!=FormWindowState.Normal)return;
        int coordinate=message.LParam.ToInt32();
        var point=PointToClient(new Point((short)(coordinate&0xffff),(short)(coordinate>>16)));
        const int edge=6;
        bool left=point.X<edge,right=point.X>=ClientSize.Width-edge,top=point.Y<edge,bottom=point.Y>=ClientSize.Height-edge;
        if(top)message.Result=new IntPtr(left?13:right?14:12);
        else if(bottom)message.Result=new IntPtr(left?16:right?17:15);
        else if(left||right)message.Result=new IntPtr(left?10:11);
    }

    private Button WindowButton(string name,bool close,Action action) {
        var button=new ChromeButton(close) {Size=new Size(30,25),Margin=new Padding(2,0,0,2),
            AccessibleName=name,BackColor=Header,ForeColor=Muted};
        button.Click+=delegate {action();};
        hints.SetToolTip(button,name);
        return button;
    }

    private static bool ClientAnimationsEnabled() {
        const uint getClientAreaAnimation=0x1042;
        int enabled;
        return !SystemParametersInfo(getClientAreaAnimation,0,out enabled,0)||enabled!=0;
    }

    private Control Navigation(params TabPage[] pages) {
        var layout=new TableLayoutPanel {Dock=DockStyle.Fill,ColumnCount=1,RowCount=2};
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute,57));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent,100));
        var nav=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=2,RowCount=1,Padding=new Padding(21,4,21,5)};
        nav.RowStyles.Add(new RowStyle(SizeType.Percent,100));
        nav.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));nav.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        var sections=Row();sections.Padding=Padding.Empty;sections.AutoSize=false;
        nav.Controls.Add(sections,0,0);
        var content=new Panel {Dock=DockStyle.Fill,BackColor=Surface};
        var buttons=new List<Button>();
        Action initial=null;
        foreach(TabPage page in pages) {
            string title=page.Text;
            var view=new Panel {Dock=DockStyle.Fill,Visible=false};
            while(page.Controls.Count>0)view.Controls.Add(page.Controls[0]);
            content.Controls.Add(view);
            Action activate=()=>{
                foreach(Control child in content.Controls)child.Visible=child==view;
                foreach(Button tab in buttons)tab.BackColor=tab.Text==title?Card:Surface;
                view.BringToFront();
            };
            var button=ButtonText(title,activate);
            if(initial==null)initial=activate;
            buttons.Add(button);
            if(title=="Windows Security") {
                button.FlatAppearance.BorderSize=0;button.ForeColor=Muted;button.Anchor=AnchorStyles.Right;
                nav.Controls.Add(button,1,0);
            } else sections.Controls.Add(button);
            page.Dispose();
        }
        layout.Controls.Add(nav,0,0);
        layout.Controls.Add(content,0,1);
        initial();
        return layout;
    }

    private TabPage Components() {
        var page=Page("AddOns & Tools");
        var scroll=new Panel{Dock=DockStyle.Fill,AutoScroll=true};
        var content=new TableLayoutPanel{Dock=DockStyle.Top,AutoSize=true,AutoSizeMode=AutoSizeMode.GrowAndShrink,ColumnCount=1,Padding=new Padding(22,4,22,10)};
        content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
        scroll.Controls.Add(content);
        page.Controls.Add(scroll);
        var sync=new RoundedTable{AutoSize=true,Dock=DockStyle.Fill,ColumnCount=2,Padding=new Padding(11,6,11,6),Margin=new Padding(0,0,0,12),BackColor=Card,Stroke=Line,Radius=8};
        sync.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));sync.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        var syncCopy=Stack();syncCopy.Dock=DockStyle.Fill;syncCopy.AutoScroll=false;syncCopy.AutoSize=true;syncCopy.Padding=Padding.Empty;
        var syncTitle=LabelText("Twilight Hammer Sync",Ink);syncTitle.Font=new Font(Font,FontStyle.Bold);syncTitle.Margin=new Padding(0,3,0,1);
        var syncSubtitle=LabelText("Addon and background uploader",Muted);syncSubtitle.Margin=new Padding(0,0,0,2);
        syncCopy.Controls.Add(syncTitle);syncCopy.Controls.Add(syncSubtitle);sync.Controls.Add(syncCopy,0,0);
        syncCopy.Controls.Add(Link("Uninstall Sync",async()=>await UninstallSync()));
        StylePicker(edition);
        edition.Items.AddRange(new object[]{"Guild Sync","Auction Only","Bank and Guild Sync","Skip Sync"});
        edition.SelectedIndex=3;edition.AccessibleName="Twilight Hammer Sync edition";
        edition.Width=165;edition.Anchor=AnchorStyles.Right;sync.Controls.Add(edition,1,0);
        content.Controls.Add(sync,0,0);
        content.Controls.Add(Group("Must-have raid addons",3),0,1);
        var columns=new TableLayoutPanel {AutoSize=true,Dock=DockStyle.Fill,ColumnCount=2,Margin=new Padding(0,5,0,3)};
        columns.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,50));
        columns.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,50));
        columns.Controls.Add(Group("Raid & character addons",1),0,0);
        columns.Controls.Add(Group("Client tools & compatibility",1),1,0);
        var market=Row();
        market.Margin=new Padding(0,12,0,0);
        market.Paint+=delegate(object sender,PaintEventArgs args) {using(var pen=new Pen(Line))args.Graphics.DrawLine(pen,0,0,market.Width,0);};
        market.Controls.Add(LabelText("Auction House addon",Ink));
        StylePicker(auction);
        auction.Items.Add("Keep my current setup");
        auction.Items.AddRange(packages.Values.Where(p=>p.Group=="Auction House").Select(p=>(object)p.Name).ToArray());
        auction.SelectedIndex=0;
        auction.SelectedIndexChanged+=delegate {
            hints.SetToolTip(auction,auction.SelectedIndex==0?"Keep your existing auction addon.":packages[(string)auction.SelectedItem].Help);
            SelectionChanged();
        };
        auction.AccessibleName="Auction House addon";
        market.Controls.Add(auction);
        content.Controls.Add(columns,0,2);
        content.Controls.Add(market,0,3);
        edition.SelectedIndexChanged+=delegate {SelectionChanged();};
        return page;
    }

    private Control Group(string title,int columns) {
        bool featured=columns==3;
        var panel=new RoundedTable{AutoSize=true,Anchor=AnchorStyles.Top|AnchorStyles.Left|AnchorStyles.Right,ColumnCount=columns,Padding=featured?new Padding(6,2,6,7):Padding.Empty,
            Margin=featured?new Padding(0,4,0,12):new Padding(0,3,9,3),BackColor=featured?Color.FromArgb(48,36,49):Surface,
            Stroke=featured?Color.FromArgb(104,81,53):Color.Empty,Radius=featured?10:0};
        for(int i=0;i<columns;i++)panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100f/columns));
        var name=LabelText(featured?"Must Haves":title=="Raid & character addons"?"Raid & Character Addons":"Client Tools & Compatibility",featured?Gold:Ink);
        name.Font=new Font(Font,FontStyle.Bold);
        panel.Controls.Add(name,0,0);panel.SetColumnSpan(name,columns);
        int index=0;
        foreach(var package in ToolkitPackages.Catalog.Where(p=>p.Group==title).OrderBy(p=>p.Name=="VanillaFixes"?16.5:Array.IndexOf(ToolkitPackages.Catalog,p))) {
            var check=Check(package.Name);
            check.Dock=DockStyle.Fill;check.Margin=new Padding(3,0,0,0);
            check.AccessibleDescription=package.Help;
            picks.Add(package.Name,check);
            hints.SetToolTip(check,package.Help);
            check.CheckedChanged+=delegate {
                if(changing)return;
                if(check.Checked)explicitlySelected.Add(package.Name);
                else explicitlySelected.Remove(package.Name);
                SelectionChanged();
            };
            string detail=package.Name=="TWThreat"?"Threat meter":package.Name=="BigWigs"?"prodigeomix":
                package.Name=="Chronicle Sidekick"?"Windows app":
                package.Name=="UnitXP_SP3"?"brues-code":null;
            var row=new RoundedTable{ColumnCount=2,RowCount=featured?2:1,Dock=DockStyle.Fill,
                Height=featured?54:30,Margin=featured?new Padding(2,2,2,0):Padding.Empty,Padding=featured?new Padding(4,4,4,3):Padding.Empty,
                BackColor=Surface,Stroke=featured?Line:Color.Empty,Radius=featured?7:0};
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,22));
            row.RowStyles.Add(new RowStyle(SizeType.Absolute,featured?24:30));
            if(featured)row.RowStyles.Add(new RowStyle(SizeType.Percent,100));
            if(!featured)row.Paint+=delegate(object sender,PaintEventArgs args) {
                using(var pen=new Pen(Line))args.Graphics.DrawLine(pen,0,0,row.Width,0);
            };
            if(featured)check.Font=new Font(Font,FontStyle.Bold);
            if(featured&&detail!=null) {
                var caption=LabelText(detail,Muted);caption.Font=new Font(Font.FontFamily,8.25f);caption.Margin=new Padding(22,0,0,0);
                row.Controls.Add(caption,0,1);
            }
            row.Controls.Add(check,0,0);
            var info=new InfoMarker {Anchor=AnchorStyles.None,ForeColor=Muted,AccessibleName=package.Name+" information"};
            hints.SetToolTip(info,package.Help);
            row.Controls.Add(info,1,0);
            panel.Controls.Add(row,index%columns,1+index/columns);
            index++;
        }
        return panel;
    }

    private TabPage Tweaks() {
        var page=Page("Client Tweaks");
        var content=Stack();
        page.Controls.Add(content);
        content.Controls.Add(enableTweaks);
        content.Controls.Add(LabelText("Applies to the supported OctoWoW Modernized client. Setup backs up changed files.",Muted));
        var controls=new TableLayoutPanel{ColumnCount=2,AutoSize=true,Width=650};
        controls.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,68));
        controls.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,32));
        int row=0;
        foreach(var pair in new[]{Tuple.Create("Nameplate range (yards)",plates),Tuple.Create("Field of view (degrees)",fov),
            Tuple.Create("Render distance",render),Tuple.Create("Ground clutter distance",clutter),Tuple.Create("Camera distance",camera)}) {
            controls.Controls.Add(LabelText(pair.Item1,Ink),0,row);
            pair.Item2.AccessibleName=pair.Item1;
            controls.Controls.Add(pair.Item2,1,row++);
        }
        content.Controls.Add(LabelText("Vanilla defaults: nameplates 20, FoV 90°, render 777, clutter 70, camera 50.",Muted));
        content.Controls.Add(LabelText("High render distances can cause crashes when entering the world.",Muted));
        content.Controls.Add(controls);content.Controls.Add(autoLoot);content.Controls.Add(laa);content.Controls.Add(audio);
        content.Controls.Add(LabelText("Unchecked switches keep the client's current setting.",Muted));
        var adviceLabel=LabelText("Estimate a starting render distance from RAM and CPU threads.",Muted);
        ClientTweaks.RenderAdvice advice=null;
        var useAdvice=ButtonText("Use recommendation",()=>{if(advice!=null){render.Value=advice.distance;adviceLabel.Text="Render distance set to "+advice.distance+"."; }});
        useAdvice.Visible=false;
        var pc=Row();
        pc.Controls.Add(ButtonText("Check my PC",()=>{
            advice=ClientTweaks.CheckPc();adviceLabel.Text="Suggested "+advice.distance+" · "+advice.ramGiB+" GB RAM · "+advice.threads+" logical CPU threads.";
            useAdvice.Visible=true;
        }));
        pc.Controls.Add(useAdvice);content.Controls.Add(pc);content.Controls.Add(adviceLabel);
        laa.Checked=true;audio.Checked=true;
        enableTweaks.CheckedChanged+=delegate {controls.Enabled=laa.Enabled=audio.Enabled=autoLoot.Enabled=enableTweaks.Checked;};
        controls.Enabled=laa.Enabled=audio.Enabled=autoLoot.Enabled=false;
        return page;
    }

    private TabPage SecurityHelp() {
        var page=Page("Windows Security");
        var content=Stack();
        page.Controls.Add(content);
        var title=LabelText("If Windows blocks a mod",Gold);
        title.Font=new Font(Font.FontFamily,14,FontStyle.Bold);
        content.Controls.Add(title);
        content.Controls.Add(LabelText("Open Protection History, find the entry, and compare the detected file and publisher with the upstream release. Report a suspected false positive to the publisher or Microsoft.",Ink));
        content.Controls.Add(LabelText("Toolkit leaves your antivirus settings alone. If a blocked download prevents setup, rerun with that component unchecked.",Muted));
        var actions=Row();
        actions.Controls.Add(ButtonText("Open Windows Security ↗",()=>Open("windowsdefender:")));
        actions.Controls.Add(ButtonText("Virus & threat protection ↗",()=>Open("windowsdefender://threat/")));
        actions.Controls.Add(ButtonText("Copy game folder",()=>{if(game.TextLength>0)Clipboard.SetText(game.Text);}));
        content.Controls.Add(actions);
        content.Controls.Add(Link("Submit a file for Microsoft review ↗",()=>Open("https://www.microsoft.com/en-us/wdsi/filesubmission")));
        return page;
    }

    private void SelectionChanged() {
        if(changing)return;
        changing=true;
        try {
            var required=new HashSet<string>();
            Action<Package> visit=null;
            visit=package=>{
                foreach(string name in package.Requires)if(required.Add(name))visit(packages[name]);
            };
            foreach(string name in explicitlySelected)visit(packages[name]);
            foreach(var pick in picks) {
                if(required.Contains(pick.Key)){pick.Value.Checked=true;pick.Value.Enabled=false;}
                else if(!pick.Value.Enabled){pick.Value.Enabled=true;pick.Value.Checked=explicitlySelected.Contains(pick.Key);}
            }
            status.Text=picks.Count(p=>p.Value.Checked)+(auction.SelectedIndex>0?1:0)+" optional components selected.";
        } finally {changing=false;}
    }

    private Package[] Plan(string folder,bool includeInstalledDependencies=false) {
        var planned=new List<Package>();
        var visited=new HashSet<Package>();
        Action<Package> visit=null;
        visit=p=>{
            if(!visited.Add(p))return;
            foreach(string name in p.Requires)visit(packages[name]);
            if(includeInstalledDependencies||p.Group=="Auction House"||explicitlySelected.Contains(p.Name)||!ToolkitInstall.DependencyPresent(folder,p))
                planned.Add(p);
        };
        foreach(var picked in picks.Where(pair=>pair.Value.Checked))visit(packages[picked.Key]);
        if(auction.SelectedIndex>0)visit(packages[(string)auction.SelectedItem]);
        return planned.ToArray();
    }
    private string GamePath() {
        string folder=game.Text.Trim().Trim('"');
        if(!Directory.Exists(folder)||(!File.Exists(Path.Combine(folder,"WoW.exe"))&&!File.Exists(Path.Combine(folder,"WoW_Modernized.exe"))))
            throw new IOException("Choose the OctoWoW folder containing WoW.exe.");
        return Path.GetFullPath(folder);
    }
    private void Busy(bool busy) {
        operationRunning=busy;
        install.Enabled=updates.Enabled=!busy;
        UseWaitCursor=busy;
    }
    private async Task Apply() {
        try {
            string folder=GamePath();
            var plan=Plan(folder);
            string selected=(string)edition.SelectedItem;
            var tweak=new ClientTweaks{Enabled=enableTweaks.Checked,LargeAddress=laa.Checked,BackgroundSounds=audio.Checked,AlwaysAutoLoot=autoLoot.Checked,
                Nameplates=plates.Value,FieldOfView=fov.Value,Render=render.Value,Clutter=clutter.Value,Camera=camera.Value};
            if(plan.Length==0&&selected=="Skip Sync"&&!tweak.Enabled){status.Text="Select a component or enable Client Tweaks.";return;}
            if(tweak.Enabled && MessageBox.Show(this,"Apply these tweaks to WoW_Modernized.exe and Config.wtf? A backup will be saved in the game folder.","Apply client tweaks",MessageBoxButtons.YesNo,MessageBoxIcon.Question)!=DialogResult.Yes)return;
            Busy(true);
            var progress=new Progress<string>(message=>status.Text=message);
            await Task.Run(()=>ToolkitInstall.Run(folder,selected,plan,tweak,message=>((IProgress<string>)progress).Report(message)));
            status.Text="Setup complete."+(plan.Any(p=>p.Folder=="game")?" Use VanillaFixes to load the selected client mods.":"");
        } catch(Exception error) {
            status.Text=error.Message;
            MessageBox.Show(this,error.Message,Text,MessageBoxButtons.OK,MessageBoxIcon.Information);
        } finally {Busy(false);}
    }

    private async Task UninstallSync() {
        if(operationRunning)return;
        var choice=MessageBox.Show(this,
            "Remove the uploader and its scheduled task?\n\nYes: also remove the WoW addon.\nNo: keep the WoW addon.\nCancel: leave Sync installed.",
            "Uninstall Sync",MessageBoxButtons.YesNoCancel,MessageBoxIcon.Question);
        if(choice==DialogResult.Cancel)return;
        try {
            bool removeAddon=choice==DialogResult.Yes;
            string folder=removeAddon?GamePath():null;
            Busy(true);
            status.Text="Removing the Sync uploader…";
            await Task.Run(()=>SyncRemoval.Run(folder,removeAddon));
            edition.SelectedItem="Skip Sync";
            status.Text=removeAddon?"Sync uploader, scheduled task, and WoW addon removed.":"Sync uploader and scheduled task removed.";
        } catch(Exception error){status.Text=error.Message;}
        finally{Busy(false);}
    }

    private async Task CheckUpdates() {
        try {
            string folder=GamePath();
            Busy(true);status.Text="Checking GitHub and addon sources…";toolkitUpdate.Visible=false;releaseUrl=null;
            var progress=new Progress<string>(message=>status.Text=message);
            var result=await Task.Run(()=>ToolkitUpdates.Check(folder,message=>((IProgress<string>)progress).Report(message)));
            releaseUrl=result.ToolkitUrl;
            toolkitUpdate.Visible=releaseUrl!=null;
            status.Text=result.Lines[0];
            MessageBox.Show(this,String.Join(Environment.NewLine,result.Lines),"Update results",MessageBoxButtons.OK,MessageBoxIcon.Information);
        } catch(Exception error) {status.Text=error.Message;}
        finally {Busy(false);}
    }

    private void Browse() {
        using(var picker=new FolderBrowserDialog{Description="Choose the OctoWoW folder containing WoW.exe",ShowNewFolderButton=false}) {
            if(Directory.Exists(game.Text))picker.SelectedPath=game.Text;
            if(picker.ShowDialog(this)==DialogResult.OK)game.Text=picker.SelectedPath;
        }
    }
    private static TabPage Page(string label){return new TabPage(label){BackColor=Surface,ForeColor=Ink};}
    private static FlowLayoutPanel Stack(){return new FlowLayoutPanel{Dock=DockStyle.Fill,AutoScroll=true,FlowDirection=FlowDirection.TopDown,WrapContents=false,Padding=new Padding(10)};}
    private static FlowLayoutPanel Row(){return new FlowLayoutPanel{Dock=DockStyle.Fill,AutoSize=true,WrapContents=true,Padding=new Padding(0,5,0,5)};}
    private static Label LabelText(string text,Color color){return new Label{Text=text,UseMnemonic=false,AutoSize=true,ForeColor=color,Margin=new Padding(3,7,6,3),MaximumSize=new Size(625,0)};}
    private static CheckBox Check(string text){return new ToolkitCheckBox{Text=text,UseMnemonic=false,AutoSize=true,ForeColor=Ink,Margin=new Padding(5,1,5,1)};}
    private static void StylePicker(ComboBox picker) {
        picker.DropDownStyle=ComboBoxStyle.DropDownList;
        picker.Width=220;picker.FlatStyle=FlatStyle.Flat;picker.BackColor=Card;picker.ForeColor=Ink;
        picker.DrawMode=DrawMode.OwnerDrawFixed;
        picker.DrawItem+=delegate(object sender,DrawItemEventArgs item) {
            bool selected=(item.State&DrawItemState.Selected)!=0;
            using(var fill=new SolidBrush(selected?Color.FromArgb(75,54,93):Card))item.Graphics.FillRectangle(fill,item.Bounds);
            if(item.Index>=0)TextRenderer.DrawText(item.Graphics,Convert.ToString(picker.Items[item.Index]),picker.Font,item.Bounds,Ink,
                TextFormatFlags.Left|TextFormatFlags.VerticalCenter|TextFormatFlags.EndEllipsis|TextFormatFlags.NoPrefix);
            item.DrawFocusRectangle();
        };
    }
    private static NumericUpDown Number(int min,int max,int value){return new NumericUpDown{Minimum=min,Maximum=max,Value=value,Width=92};}
    private static Button ButtonText(string text,Action action) {
        var button=new ToolkitButton{Text=text,UseMnemonic=false,AutoSize=true,FlatStyle=FlatStyle.Flat,BackColor=Card,ForeColor=Ink,Padding=new Padding(6,2,6,2)};
        button.FlatAppearance.BorderColor=Color.FromArgb(108,88,127);
        button.Click+=delegate {action();};return button;
    }
    private static LinkLabel Link(string text,Action action) {
        var link=new LinkLabel{Text=text,AutoSize=true,LinkColor=Gold,ActiveLinkColor=Color.White,VisitedLinkColor=Gold,Margin=new Padding(7,10,7,3)};
        link.LinkClicked+=delegate {action();};return link;
    }
    private sealed class InfoMarker : Control {
        internal InfoMarker(){Size=new Size(16,16);SetStyle(ControlStyles.UserPaint|ControlStyles.OptimizedDoubleBuffer,true);}
        protected override void OnPaint(PaintEventArgs e) {
            e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;
            using(var pen=new Pen(ForeColor))e.Graphics.DrawEllipse(pen,1,1,13,13);
            using(var font=new Font("Segoe UI",7,FontStyle.Bold))
                TextRenderer.DrawText(e.Graphics,"i",font,new Rectangle(0,0,16,15),ForeColor,TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter|TextFormatFlags.NoPadding);
        }
    }
    private sealed class ToolkitButton : Button {
        private bool hover,pressed;
        protected override void OnMouseEnter(EventArgs e){base.OnMouseEnter(e);hover=true;Invalidate();}
        protected override void OnMouseLeave(EventArgs e){base.OnMouseLeave(e);hover=pressed=false;Invalidate();}
        protected override void OnMouseDown(MouseEventArgs e){base.OnMouseDown(e);pressed=e.Button==MouseButtons.Left;Invalidate();}
        protected override void OnMouseUp(MouseEventArgs e){base.OnMouseUp(e);pressed=false;Invalidate();}
        protected override void OnPaint(PaintEventArgs e) {
            e.Graphics.Clear(Parent==null?Surface:Parent.BackColor);
            Color fill=pressed?ControlPaint.Dark(BackColor,.12f):hover?ControlPaint.Light(BackColor,.16f):BackColor;
            DrawSurface(e.Graphics,new Rectangle(0,0,Width-1,Height-1),7,fill,
                FlatAppearance.BorderSize==0?Color.Empty:FlatAppearance.BorderColor);
            TextRenderer.DrawText(e.Graphics,Text,Font,ClientRectangle,Enabled?ForeColor:Muted,
                TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter|TextFormatFlags.NoPrefix);
            if(Focused&&ShowFocusCues)ControlPaint.DrawFocusRectangle(e.Graphics,Rectangle.Inflate(ClientRectangle,-4,-4));
        }
    }
    private sealed class ChromeButton : Button {
        private readonly bool close;
        private bool hover;
        internal ChromeButton(bool close) {
            this.close=close;
            FlatStyle=FlatStyle.Flat;
            UseMnemonic=false;
        }
        protected override void OnMouseEnter(EventArgs e){base.OnMouseEnter(e);hover=true;Invalidate();}
        protected override void OnMouseLeave(EventArgs e){base.OnMouseLeave(e);hover=false;Invalidate();}
        protected override void OnPaint(PaintEventArgs e) {
            e.Graphics.Clear(Parent==null?Header:Parent.BackColor);
            Color fill=hover?(close?Color.FromArgb(124,47,68):Color.FromArgb(64,48,78)):Card;
            DrawSurface(e.Graphics,new Rectangle(0,0,Width-1,Height-1),6,fill,Line);
            int x=Width/2,y=Height/2;
            e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;
            using(var pen=new Pen(hover?Ink:ForeColor,1.35f)) {
                if(close) {
                    e.Graphics.DrawLine(pen,x-4,y-4,x+4,y+4);
                    e.Graphics.DrawLine(pen,x+4,y-4,x-4,y+4);
                } else e.Graphics.DrawLine(pen,x-4,y+2,x+4,y+2);
            }
            if(Focused&&ShowFocusCues)ControlPaint.DrawFocusRectangle(e.Graphics,Rectangle.Inflate(ClientRectangle,-3,-3));
        }
    }
    private sealed class ToolkitCheckBox : CheckBox {
        protected override void OnPaint(PaintEventArgs e) {
            e.Graphics.Clear(Parent==null?Surface:Parent.BackColor);
            int y=(Height-13)/2;
            DrawSurface(e.Graphics,new Rectangle(2,y,13,13),3,Checked?Gold:Card,Checked?Gold:Muted);
            if(Checked)using(var pen=new Pen(Surface,1.8f))
                e.Graphics.DrawLines(pen,new[]{new Point(5,y+7),new Point(8,y+10),new Point(12,y+4)});
            var text=new Rectangle(23,0,Math.Max(0,Width-23),Height);
            TextRenderer.DrawText(e.Graphics,Text,Font,text,Enabled?ForeColor:Muted,
                TextFormatFlags.Left|TextFormatFlags.VerticalCenter|TextFormatFlags.NoPrefix|TextFormatFlags.EndEllipsis);
            if(Focused&&ShowFocusCues)ControlPaint.DrawFocusRectangle(e.Graphics,Rectangle.Inflate(ClientRectangle,-1,-1));
        }
        public override Size GetPreferredSize(Size proposed) {
            var size=base.GetPreferredSize(proposed);
            return new Size(size.Width+7,size.Height);
        }
    }
    private sealed class ToolkitComboBox : ComboBox {
        protected override void WndProc(ref Message message) {
            const int paint=0x000f;
            const int printClient=0x0318;
            base.WndProc(ref message);
            if((message.Msg!=paint&&message.Msg!=printClient)||Width<25||Height<17)return;
            using(var g=message.Msg==printClient&&message.WParam!=IntPtr.Zero?Graphics.FromHdc(message.WParam):Graphics.FromHwnd(Handle)) {
                g.Clear(Parent==null?Surface:Parent.BackColor);
                DrawSurface(g,new Rectangle(0,0,Width-1,Height-1),6,Card,Focused?Gold:Line);
                TextRenderer.DrawText(g,Text,Font,new Rectangle(9,0,Width-31,Height),Ink,
                    TextFormatFlags.Left|TextFormatFlags.VerticalCenter|TextFormatFlags.NoPrefix|TextFormatFlags.EndEllipsis);
                int x=Width-14,y=Height/2-1;
                using(var pen=new Pen(Ink,1.3f))g.DrawLines(pen,new[]{new Point(x-3,y),new Point(x,y+3),new Point(x+3,y)});
            }
        }
    }
    private sealed class RoundedTable : TableLayoutPanel {
        internal Color Stroke;
        internal int Radius;
        protected override void OnPaintBackground(PaintEventArgs e) {
            if(Radius==0||Width<2*Radius||Height<2*Radius){base.OnPaintBackground(e);return;}
            e.Graphics.Clear(Parent==null?Surface:Parent.BackColor);
            DrawSurface(e.Graphics,new Rectangle(0,0,Width-1,Height-1),Radius,BackColor,Stroke);
        }
    }
    private sealed class CrestPanel : Panel {
        internal float Progress=1;
        protected override void OnPaint(PaintEventArgs e) {
            DrawSurface(e.Graphics,new Rectangle(0,0,Width-1,Height-1),9,Card,Color.FromArgb(104,81,53));
            var state=e.Graphics.Save();
            e.Graphics.TranslateTransform(Width/2f,Height/2f);
            float swing=(float)(-30*Math.Cos(Progress*10)*Math.Pow(1-Progress,2));
            e.Graphics.RotateTransform(swing);
            float scale=.82f+.18f*Progress+.16f*(float)Math.Sin(Math.PI*Progress);
            e.Graphics.ScaleTransform(scale,scale);
            using(var font=new Font("Segoe UI Symbol",15))
            using(var brush=new SolidBrush(Gold))
            using(var format=new StringFormat{Alignment=StringAlignment.Center,LineAlignment=StringAlignment.Center})
                e.Graphics.DrawString("⚒",font,brush,new PointF(0,0),format);
            e.Graphics.Restore(state);
        }
    }
    private static void DrawSurface(Graphics graphics,Rectangle bounds,int radius,Color fill,Color stroke) {
        if(bounds.Width<1||bounds.Height<1)return;
        var mode=graphics.SmoothingMode;
        graphics.SmoothingMode=SmoothingMode.AntiAlias;
        using(var shape=RoundedPath(bounds,radius)) {
            using(var brush=new SolidBrush(fill))graphics.FillPath(brush,shape);
            if(!stroke.IsEmpty)using(var pen=new Pen(stroke))graphics.DrawPath(pen,shape);
        }
        graphics.SmoothingMode=mode;
    }
    private static GraphicsPath RoundedPath(Rectangle bounds,int radius) {
        var shape=new GraphicsPath();
        int d=Math.Max(1,Math.Min(2*radius,Math.Min(bounds.Width,bounds.Height)));
        int x=bounds.Left,y=bounds.Top,w=bounds.Right,h=bounds.Bottom;
        shape.AddArc(x,y,d,d,180,90);shape.AddArc(w-d,y,d,d,270,90);
        shape.AddArc(w-d,h-d,d,d,0,90);shape.AddArc(x,h-d,d,d,90,90);shape.CloseFigure();
        return shape;
    }
    private static void Open(string url) {
        if(String.IsNullOrEmpty(url))return;
        try {Process.Start(new ProcessStartInfo(url){UseShellExecute=true});}
        catch(Exception error){MessageBox.Show(error.Message,"Twilight Hammer Toolkit");}
    }
}

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace TM4CDers {
public sealed class MainForm : Form {
    readonly Color accent=Color.FromArgb(0,126,112);
    Label title, subtitle, state, hint; ToolStrip toolbar; ThemeButton themeToggle; readonly ToolTip themeTip=new ToolTip(); PictureBox logo; GroupBox scope; ListBox stages; ThemedTextView log;
    CheckBox flash, accept; FamilySelector familySelect; Label familySummary; SetupButton tm4cOnly; Button start, withoutBoard, load, cancel;
    ToolStripDropDownButton tools; ToolStripMenuItem open, logs, licenses, help;
    LinkLabel tr, en;
    readonly List<string> history=new List<string>();
    readonly System.Windows.Forms.Timer timer=new System.Windows.Forms.Timer();
    bool scanBusy,busy,completed; readonly bool busyPreview,uiPreview; string stateTr,stateEn; CancellationTokenSource cancellation; Engine engine;
    Board lastBoard=new Board();
    public MainForm(string[] args) {
        busyPreview=args.Contains("--ui-busy-preview");
        uiPreview=args.Contains("--ui-preview");
        Theme.Load(args);
        using(var stream=new MemoryStream(Assets.Read("tiva-setup.ico")))Icon=new Icon(stream);
        Size=new Size(980,790); MinimumSize=Size; StartPosition=FormStartPosition.CenterScreen;
        Font=new Font("Segoe UI",10); BackColor=Color.FromArgb(244,248,247);
        using(var stream=new MemoryStream(Assets.Read("tiva-setup.png")))using(var image=Image.FromStream(stream))logo=new PictureBox{Image=new Bitmap(image),SizeMode=PictureBoxSizeMode.Zoom,Location=new Point(28,22),Size=new Size(56,56)};Controls.Add(logo);
        themeToggle=new ThemeButton{Location=new Point(730,27),Size=new Size(46,34),Anchor=AnchorStyles.Top|AnchorStyles.Right};themeToggle.Click+=delegate{Theme.Select(!Theme.Dark);ApplyTheme();};Controls.Add(themeToggle);
        title=new Label {Text="Tiva Setup",Font=new Font("Segoe UI",25,FontStyle.Bold),AutoSize=true,Location=new Point(100,17)}; Controls.Add(title);
        subtitle=new Label{AutoSize=true,Location=new Point(100,73)};Controls.Add(subtitle);
        toolbar=new ToolStrip{Location=new Point(792,27),Size=new Size(144,34),AutoSize=false,GripStyle=ToolStripGripStyle.Hidden,BackColor=BackColor,Anchor=AnchorStyles.Top|AnchorStyles.Right,Font=Font,Renderer=new ThemeMenuRenderer()};
        tools=new ToolStripDropDownButton{AutoSize=false,Size=new Size(138,30),DropDownDirection=ToolStripDropDownDirection.BelowLeft};toolbar.Items.Add(tools);Controls.Add(toolbar);
        open=new ToolStripMenuItem();open.Click+=delegate{Process.Start(new ProcessStartInfo(Path.Combine(engine.CcsInstallRoot,"ccs/eclipse/ccstudio.exe"),"-data "+Assets.Quote(engine.Workspace)){UseShellExecute=true});};
        logs=new ToolStripMenuItem();logs.Click+=delegate{Process.Start(new ProcessStartInfo(engine.LogDirectory){UseShellExecute=true});};
        licenses=new ToolStripMenuItem();licenses.Click+=delegate{ShowLicenses();};
        help=new ToolStripMenuItem();help.Click+=delegate{ShowText(L.T("Kısa kullanım","Quick guide"),Assets.Text(L.English ? "help.en.txt" : "help.tr.txt"));};
        tools.DropDownItems.AddRange(new ToolStripItem[]{open,logs,new ToolStripSeparator(),licenses,help});
        scope=new SetupGroup{Location=new Point(28,108),Size=new Size(910,80),Anchor=AnchorStyles.Top|AnchorStyles.Left|AnchorStyles.Right};Controls.Add(scope);
        familySelect=new FamilySelector{Location=new Point(18,28),Size=new Size(162,36),SelectedFamilies=DeviceFamilies.FromArgs(args)};scope.Controls.Add(familySelect);
        familySummary=new Label{Location=new Point(200,28),Size=new Size(500,36),TextAlign=ContentAlignment.MiddleLeft,AutoEllipsis=true,Anchor=AnchorStyles.Top|AnchorStyles.Left|AnchorStyles.Right};scope.Controls.Add(familySummary);
        tm4cOnly=new SetupButton{Text="TM4C123",Location=new Point(728,28),Size=new Size(162,36),Anchor=AnchorStyles.Top|AnchorStyles.Right,Cursor=Cursors.Hand};scope.Controls.Add(tm4cOnly);
        tm4cOnly.Click+=delegate{familySelect.SelectedFamilies=new[]{"PF_TM4C"};};
        familySelect.SelectionChanged+=delegate{UpdateSelection();};
        state=new Label{Location=new Point(28,211),Size=new Size(910,49),Font=new Font("Segoe UI",12,FontStyle.Bold),ForeColor=accent,Anchor=AnchorStyles.Top|AnchorStyles.Left|AnchorStyles.Right};Controls.Add(state);
        stages=new ListBox{Location=new Point(28,275),Size=new Size(293,252),BorderStyle=BorderStyle.None,BackColor=BackColor};Controls.Add(stages);
        log=new ThemedTextView{Location=new Point(337,275),Size=new Size(601,252),BackColor=Theme.Surface,Anchor=AnchorStyles.Top|AnchorStyles.Bottom|AnchorStyles.Left|AnchorStyles.Right};Controls.Add(log);
        flash=new SetupCheckBox{Checked=!args.Contains("--ui-no-flash"),AutoSize=true,Location=new Point(28,551),Anchor=AnchorStyles.Bottom|AnchorStyles.Left};Controls.Add(flash);
        accept=new SetupCheckBox{Checked=args.Contains("--ui-accepted"),AutoSize=true,Location=new Point(28,589),Anchor=AnchorStyles.Bottom|AnchorStyles.Left};Controls.Add(accept);
        start=ActionButton(28,200,true);start.Click+=async delegate{await BeginInstall(false,false);};
        withoutBoard=ActionButton(244,220,false);withoutBoard.Click+=async delegate{await BeginInstall(true,false);};
        load=ActionButton(480,220,false);load.Click+=async delegate{await BeginInstall(false,true);};
        cancel=ActionButton(716,150,false);cancel.Visible=false;cancel.Click+=delegate{cancellation.Cancel();Append("Durdurma istendi. Devam eden yükleyici bitince duracak.");cancel.Enabled=false;};
        hint=new Label{Location=new Point(28,702),Size=new Size(728,32),ForeColor=Color.DimGray,Font=new Font("Segoe UI",9),Anchor=AnchorStyles.Bottom|AnchorStyles.Left};Controls.Add(hint);
        tr=LanguageLink("TR",820);en=LanguageLink("EN",892);
        var divider=new Label{Text="/",AutoSize=true,Location=new Point(869,707),ForeColor=Color.Silver,Anchor=AnchorStyles.Bottom|AnchorStyles.Right};Controls.Add(divider);
        tr.Click+=delegate{ChangeLanguage(false);};en.Click+=delegate{ChangeLanguage(true);};
        engine=new Engine();open.Enabled=engine.Ready("ccs");
        SetState("Kartı bağlayın veya kartsız devam edin.","Connect your board or continue without it.");
        accept.CheckedChanged+=delegate{UpdateStart();};
        timer.Interval=2000;timer.Tick+=async delegate{await Scan();};
        Shown+=async delegate{Theme.Caption(this);if(busyPreview){SetChoiceAvailability(false);SetState("Görünüm testi: kurulum sırasında kilitlenen seçimler.","Appearance test: options disabled during setup.");cancel.Visible=true;cancel.Enabled=false;return;}timer.Start();await Scan();if(args.Contains("--ui-start"))await BeginInstall(args.Contains("--ui-offline"),args.Contains("--ui-load"));};
        FormClosing+=delegate(object sender,FormClosingEventArgs e){if(busy){e.Cancel=true;MessageBox.Show(this,L.T("Önce Durdur’a basın. Devam eden yükleyici tamamlanınca pencereyi kapatabilirsiniz.","Select Stop first. You can close the window once the current installer finishes."),"Tiva Setup");}};
        FormClosed+=delegate{timer.Dispose();themeTip.Dispose();logo.Image.Dispose();Icon.Dispose();};
        ApplyLanguage();UpdateStart();
        using(var payload=Assets.Payload())Append(payload==null ? "Hafif sürüm. Paketler USB’den veya resmî TI kaynaklarından alınır." : "Çevrimdışı paket hazır. Kurulum dosyaları bu EXE’nin içinde.");
    }
    Button ActionButton(int x,int width,bool primary){var b=new SetupButton{Primary=primary,Size=new Size(width,52),Location=new Point(x,641),Anchor=AnchorStyles.Bottom|AnchorStyles.Left,FlatStyle=FlatStyle.Flat,BackColor=primary ? accent : Color.White,ForeColor=primary ? Color.White : Color.FromArgb(40,56,53)};b.FlatAppearance.BorderColor=primary ? accent : Color.FromArgb(204,215,212);Controls.Add(b);return b;}
    LinkLabel LanguageLink(string text,int x){var l=new LinkLabel{Text=text,AutoSize=false,Size=new Size(43,29),Location=new Point(x,706),TextAlign=ContentAlignment.MiddleCenter,LinkBehavior=LinkBehavior.HoverUnderline,Anchor=AnchorStyles.Bottom|AnchorStyles.Right,LinkColor=accent,ActiveLinkColor=accent,VisitedLinkColor=accent};Controls.Add(l);return l;}
    void ChangeLanguage(bool english){L.Select(english);ApplyLanguage();}
    void ApplyLanguage(){
        Text="Tiva Setup 3.4";subtitle.Text="TM4C123G LaunchPad  •  CCS 12.8.1  •  TivaWare 2.2.0.295";
        scope.Text=L.T("CCS aygıt desteği","CCS device support");familySelect.RefreshLanguage();UpdateSelection();tm4cOnly.AccessibleName=L.T("Yalnız TM4C123 seç","Select TM4C123 only");
        tools.Text=L.T("Araçlar","Tools");open.Text=L.T("CCS’yi aç","Open CCS");logs.Text=L.T("Kayıtlar ve yedekler","Logs and backups");licenses.Text=L.T("Lisanslar","Licenses");help.Text=L.T("Kullanım rehberi","Quick guide");
        flash.Text=L.T("RGB programını bağlı karta yükle (önce mevcut program yedeklenir)","Flash RGB to the connected board (back up its current program first)");
        accept.Text=L.T("TI yazılım lisanslarını okudum ve kabul ediyorum.","I have read and accept the TI software licenses.");
        start.Text=L.T("Kurulumu başlat","Start setup");withoutBoard.Text=L.T("Kartsız devam et","Continue without board");load.Text=L.T("Karta yükle","Flash board");cancel.Text=L.T("Durdur","Stop");
        hint.Text=L.T("Kartı sonra bağlayabilirsiniz. Yardım ve lisanslar: Araçlar menüsü.","Connect your board later. Find help and licenses in Tools.");
        tr.Font=new Font(Font.FontFamily,10,L.English ? FontStyle.Regular : FontStyle.Bold);en.Font=new Font(Font.FontFamily,10,L.English ? FontStyle.Bold : FontStyle.Regular);
        tr.LinkColor=L.English ? Color.DimGray : accent;en.LinkColor=L.English ? accent : Color.DimGray;
        int selected=stages.SelectedIndex;stages.Items.Clear();
        string[] trs={"1  Kart bağlantısı","2  Kurulum dosyaları","3  ICDI USB sürücüsü","4  Code Composer Studio","5  TivaWare ve LM Flash","6  DriverLib projesi","7  RGB programını derle","8  Kartı yedekle ve yükle","9  Hazır"};
        string[] ens={"1  Board connection","2  Installer files","3  ICDI USB driver","4  Code Composer Studio","5  TivaWare and LM Flash","6  DriverLib project","7  Build RGB program","8  Back up and flash board","9  Ready"};
        stages.Items.AddRange(L.English ? ens : trs);stages.SelectedIndex=selected;
        RenderState();log.Text=String.Join(Environment.NewLine,history.Select(L.Translate))+(history.Count>0 ? Environment.NewLine : "");log.SelectionStart=log.TextLength;log.ScrollToCaret();ApplyTheme();
    }
    void ApplyTheme(){
        Theme.Apply(this);state.ForeColor=Theme.Accent;hint.ForeColor=Theme.Muted;subtitle.ForeColor=Theme.Muted;
        familySummary.ForeColor=familySelect.Enabled ? Theme.Text : Theme.Muted;
        tr.LinkColor=L.English ? Theme.Muted : Theme.Accent;en.LinkColor=L.English ? Theme.Accent : Theme.Muted;
        tr.ActiveLinkColor=en.ActiveLinkColor=Theme.Accent;tr.VisitedLinkColor=en.VisitedLinkColor=Theme.Accent;
        toolbar.Renderer=new ThemeMenuRenderer();tools.DropDown.BackColor=Theme.Background;tools.DropDown.ForeColor=Theme.Text;
        foreach(ToolStripItem item in tools.DropDownItems){item.BackColor=Theme.Background;item.ForeColor=Theme.Text;}
        string action=Theme.Dark ? L.T("Gündüz moduna geç","Switch to light mode") : L.T("Gece moduna geç","Switch to dark mode");
        themeToggle.AccessibleName=action;themeTip.SetToolTip(themeToggle,action);themeToggle.Invalidate();
    }
    void SetState(string trText,string enText){stateTr=trText;stateEn=enText;RenderState();}
    void RenderState(){state.Text=stateEn==null ? L.Translate(stateTr) : L.T(stateTr,stateEn);}
    async Task Scan(){if(scanBusy||busy)return;scanBusy=true;try{lastBoard=await Task.Run(()=>Assets.ScanBoard());if(!Tm4cSelected){
        SetState(engine.Ready("ccs") ? "CCS kurulu; mevcut kurulum kullanılacak." : "CCS kurulumu için aile seçin ve kartsız devam edin.",engine.Ready("ccs") ? "CCS is installed; the existing installation will be used." : "Select CCS families and continue without a board.");
    }else if(engine.HasPreparedProject&&(!completed||!engine.Flash)){
        if(lastBoard.Count>1)SetState(lastBoard.Detail,null);
        else if(lastBoard.Connected)SetState("Proje hazır. Karta yükle düğmesini kullanabilirsiniz.","Project ready. Use Flash board when you are ready.");
        else SetState("Proje hazır. Kartı sonra DEBUG girişinden bağlayabilirsiniz.","Project ready. Connect the board to DEBUG USB later.");
    }else if(!completed){if(lastBoard.Connected)SetState(lastBoard.Detail,null);else SetState("Kart bağlı değil. Kartsız devam edebilirsiniz.","No board connected. You can continue without one.");}UpdateStart();}finally{scanBusy=false;}}
    bool Tm4cSelected {get{return familySelect.SelectedFamilies.Contains("PF_TM4C");}}
    void UpdateSelection(){familySummary.Text=familySelect.Summary;tm4cOnly.Primary=familySelect.SelectedFamilies.Length==1&&Tm4cSelected;tm4cOnly.Invalidate();if(!Tm4cSelected)flash.Checked=false;flash.Enabled=!busy&&!busyPreview&&Tm4cSelected;UpdateStart();}
    void SetChoiceAvailability(bool enabled){flash.Enabled=enabled&&Tm4cSelected;accept.Enabled=enabled;familySelect.Enabled=enabled;tm4cOnly.Enabled=enabled;familySummary.ForeColor=enabled ? Theme.Text : Theme.Muted;}
    void UpdateStart(){if(busyPreview||uiPreview){start.Enabled=withoutBoard.Enabled=load.Enabled=false;return;}bool selected=familySelect.SelectedFamilies.Length>0;start.Enabled=!busy&&selected&&accept.Checked&&lastBoard.Connected&&lastBoard.Count==1;withoutBoard.Enabled=!busy&&selected&&accept.Checked&&lastBoard.Count<=1;load.Enabled=!busy&&Tm4cSelected&&accept.Checked&&lastBoard.Healthy&&lastBoard.Count==1&&engine.HasPreparedProject;}
    void Append(string text){if(InvokeRequired){BeginInvoke(new Action<string>(Append),text);return;}history.Add(text);log.AppendText(L.Translate(text)+Environment.NewLine);}
    void Mark(int index,string text){if(InvokeRequired){BeginInvoke(new Action<int,string>(Mark),index,text);return;}stages.SelectedIndex=index;SetState(text,null);}
    bool Internet(string names){return (bool)Invoke(new Func<bool>(()=>MessageBox.Show(this,L.T("Eksik dosyalar: ","Missing files: ")+names+L.T("\n\nİnternetten indirilsin mi? TivaWare için kişisel TI girişinizi tarayıcıda tamamlayabilirsiniz.","\n\nDownload from the internet? You can complete your personal TI sign-in for TivaWare in the browser."),L.T("Kurulum dosyaları","Installer files"),MessageBoxButtons.YesNo,MessageBoxIcon.Question)==DialogResult.Yes));}
    string Pick(Package p){return (string)Invoke(new Func<string>(()=>{Process.Start(new ProcessStartInfo(p.Page){UseShellExecute=true});MessageBox.Show(this,p.Name+L.T(" için resmî indirme sayfası açıldı. Kendi TI hesabınızla giriş yapın; e-posta kodunu ve kişisel formu tarayıcıda tamamlayın. İndirme bitince dosyayı seçin.\n\nBeklenen dosya: "," official download page opened. Sign in with your own TI account and complete the email code and personal form in the browser. Once downloaded, select the file.\n\nExpected file: ")+p.File,L.T("TI indirmesi","TI download"));using(var d=new OpenFileDialog{Title=L.T("Dosyayı seçin: ","Select file: ")+p.Name,FileName=p.File,Filter=L.T("Kurulum dosyası|","Installer file|")+p.File+L.T("|Tüm dosyalar|*.*","|All files|*.*"),InitialDirectory=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),"Downloads")})return d.ShowDialog(this)==DialogResult.OK ? d.FileName : null;}));}
    async Task BeginInstall(bool noBoard,bool loadOnly){
        if(familySelect.SelectedFamilies.Length==0){Append("En az bir CCS aygıt ailesi seçin.");return;}
        if(!Assets.Admin()){
            string uiArgs="--ui-start --ui-accepted"+(L.English ? " --ui-en" : " --ui-tr")+(Theme.Dark ? " --ui-dark" : " --ui-light")+" --ui-families="+String.Join(",",familySelect.SelectedFamilies)+(noBoard ? " --ui-offline" : "")+(loadOnly ? " --ui-load" : "")+(!flash.Checked ? " --ui-no-flash" : "");
            try{Process.Start(new ProcessStartInfo(Application.ExecutablePath,uiArgs){UseShellExecute=true,Verb="runas",WorkingDirectory=Path.GetDirectoryName(Application.ExecutablePath)});Close();}catch(System.ComponentModel.Win32Exception){Append("Yönetici izni verilmedi. Sağ tık → Yönetici olarak çalıştır.");}return;
        }
        busy=true;UpdateStart();cancel.Visible=true;cancel.Enabled=true;SetChoiceAvailability(false);open.Enabled=false;cancellation=new CancellationTokenSource();
        engine=new Engine{Flash=!noBoard&&Tm4cSelected&&flash.Checked,AllowNoBoard=noBoard,SelectedFamilies=familySelect.SelectedFamilies,Report=Append,Step=Mark,AskInternet=Internet,SelectFile=Pick};
        try{if(loadOnly){await Task.Run(()=>engine.FlashPrepared(cancellation.Token));engine.Flash=true;}else await Task.Run(()=>engine.Run(cancellation.Token,false));completed=true;open.Enabled=true;
            if(engine.Flash)SetState("Hazır. RGB yüklendi; renk sırasını kartta kontrol edin.","Ready. RGB flashed; check the color sequence on your board.");
            else if(!engine.Tm4cSelected)SetState("Hazır. CCS kullanılabilir. TM4C RGB adımları atlandı.","Ready. CCS is available. TM4C RGB steps were skipped.");
            else SetState("Hazır. DriverLib projesi derlendi. Kartı sonra bağlayıp yükleyebilirsiniz.","Ready. DriverLib project built. Connect your board and flash it later.");
        }catch(OperationCanceledException){SetState("İşlem durduruldu. Tamamlanan kurulumlar korunuyor.","Setup stopped. Completed installations were kept.");Append(stateTr);}
        catch(Exception e){SetState("Kurulum tamamlanamadı.","Setup could not be completed.");Append(e.Message);MessageBox.Show(this,L.Translate(e.Message)+L.T("\n\nAraçlar → Kayıtlar ve yedekler menüsünden ayrıntıları açabilirsiniz.","\n\nOpen Tools → Logs and backups for details."),L.T("Kurulum durumu","Setup status"),MessageBoxButtons.OK,MessageBoxIcon.Warning);}
        finally{cancellation.Cancel();busy=false;cancel.Visible=false;SetChoiceAvailability(true);open.Enabled=engine.Ready("ccs");UpdateStart();}
    }
    string Lines(string text){return text.Replace("\r\n","\n").Replace("\n","\r\n");}
    ThemedTextView ReadOnlyText(string text){return new ThemedTextView{Dock=DockStyle.Fill,Font=new Font("Segoe UI",10),Text=Lines(text)};}
    void ShowText(string caption,string text){using(var f=new Form{Text=caption,Size=new Size(860,650),StartPosition=FormStartPosition.CenterParent,Icon=Icon}){var t=ReadOnlyText(text);f.Controls.Add(t);Theme.Apply(f);f.Shown+=delegate{Theme.Caption(f);t.Select(0,0);};f.ShowDialog(this);}}
    void ShowLicenses(){using(var f=new Form{Text=L.T("Yazılım lisansları","Software licenses"),Size=new Size(900,650),StartPosition=FormStartPosition.CenterParent,Icon=Icon}){
        var tabs=new SetupTabs{Dock=DockStyle.Fill,Font=Font};f.Controls.Add(tabs);
        tabs.AddPage(L.T("Açıklama","Overview"),ReadOnlyText(Assets.Text(L.English ? "licenses.en.txt" : "licenses.tr.txt")));
        tabs.AddPage(L.T("Özgün lisans metinleri","Original license texts"),ReadOnlyText(Assets.Text("licenses.txt")));Theme.Apply(f);f.Shown+=delegate{Theme.Caption(f);};f.ShowDialog(this);
    }}
}
}

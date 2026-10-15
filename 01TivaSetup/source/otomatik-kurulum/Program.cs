using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Management;
using System.Net;
using System.Reflection;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using Microsoft.Win32;

[assembly: AssemblyTitle("Tiva Setup")]
[assembly: AssemblyProduct("Tiva Setup")]
[assembly: AssemblyDescription("Bilingual setup tool for Tiva C / TM4C development")]
[assembly: AssemblyVersion("3.4.0.0")]
[assembly: AssemblyFileVersion("3.4.0.0")]

namespace TM4CDers {
public class Package {
    public string Id, Name, File, Hash, Url, Page;
}
public class Board {
    public bool Connected, Healthy;
    public int Count;
    public string Detail;
}
public static class Assets {
    public static byte[] Read(string name) {
        using (var s = Assembly.GetExecutingAssembly().GetManifestResourceStream(name)) {
            if (s == null) throw new IOException("Uygulama kaynağı eksik: " + name);
            using (var m = new MemoryStream()) { s.CopyTo(m); return m.ToArray(); }
        }
    }
    public static string Text(string name) { return Encoding.UTF8.GetString(Read(name)); }
    public static Package[] Packages() { return new JavaScriptSerializer().Deserialize<Package[]>(Text("packages.json")); }
    public static bool Admin() { return new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator); }
    public static string Hash(Stream stream) { using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "").ToLowerInvariant(); }
    public static string HashFile(string file) { using (var s = File.OpenRead(file)) return Hash(s); }
    public static string Quote(string value) {
        var b = new StringBuilder("\""); int slashes = 0;
        foreach (char c in value) {
            if (c == '\\') { slashes++; continue; }
            if (c == '"') { b.Append('\\', slashes * 2 + 1); b.Append(c); slashes = 0; continue; }
            b.Append('\\', slashes); slashes = 0; b.Append(c);
        }
        b.Append('\\', slashes * 2); b.Append('"'); return b.ToString();
    }
    public static string Argument(string value) {
        // msiexec does not accept quoted option names. Quote values only when needed.
        return value.Length == 0 || value.Any(c => Char.IsWhiteSpace(c) || c == '"') ? Quote(value) : value;
    }
    public static Board ScanBoard() {
        var result = new Board { Detail = "Kartı DEBUG girişinden bağlayınız. POWER SELECT = DEBUG." };
        try {
            var ids = new List<string>(); var errors = new List<uint>();
            using (var q = new ManagementObjectSearcher("SELECT PNPDeviceID, ConfigManagerErrorCode FROM Win32_PnPEntity WHERE PNPDeviceID LIKE '%VID_1CBE&PID_00FD%'"))
            using (var rows = q.Get()) foreach (ManagementObject row in rows) {
                ids.Add(Convert.ToString(row["PNPDeviceID"])); errors.Add(Convert.ToUInt32(row["ConfigManagerErrorCode"])); row.Dispose();
            }
            result.Count = ids.Count(s => !s.Contains("&MI_"));
            if (result.Count == 0 && ids.Count > 0) result.Count = 1;
            result.Connected = ids.Count > 0;
            result.Healthy = ids.Any(s => s.Contains("&MI_02")) && ids.Any(s => s.Contains("&MI_03")) && errors.All(e => e == 0);
            if (result.Count > 1) result.Detail = "Birden fazla kart bağlı. Yalnızca kurulacak kartı bağlı bırakınız.";
            else if (result.Connected) result.Detail = result.Healthy ? "Kart bağlandı. ICDI USB bağlantısı hazır." : "Kart görüldü. USB sürücüsü kurulumda hazırlanacak.";
        } catch (Exception e) { result.Detail = "Kart kontrolü yapılamadı: " + e.Message; }
        return result;
    }
    public static ZipArchive Payload() {
        var f = new FileStream(Application.ExecutablePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (f.Length < 24) { f.Dispose(); return null; }
        f.Position = f.Length - 24;
        var footer = new byte[24]; if (f.Read(footer, 0, 24) != 24) { f.Dispose(); return null; }
        if (Encoding.ASCII.GetString(footer, 0, 16) != "TM4C_PAYLOAD_V1!") { f.Dispose(); return null; }
        long length = BitConverter.ToInt64(footer, 16);
        if (length <= 0 || length > f.Length - 24) { f.Dispose(); throw new InvalidDataException("Paket son bilgisi bozuk."); }
        return new ZipArchive(new SliceStream(f, f.Length - 24 - length, length), ZipArchiveMode.Read);
    }
    public static string SafePath(string directory, string name) {
        string root = Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (name.IndexOf(':') >= 0 || Path.IsPathRooted(name)) throw new InvalidDataException("Arşiv yolu geçersiz.");
        string path = Path.GetFullPath(Path.Combine(root, name.Replace('/', Path.DirectorySeparatorChar)));
        if (!path.StartsWith(root, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Arşiv klasör dışına yazmaya çalışıyor.");
        for (string p = Path.GetDirectoryName(path); p != null && p.StartsWith(root, StringComparison.OrdinalIgnoreCase); p = Path.GetDirectoryName(p))
            if (Directory.Exists(p) && (File.GetAttributes(p) & FileAttributes.ReparsePoint) != 0) throw new IOException("Arşiv hedefinde bağlantı klasörü var.");
        if (File.Exists(path) && (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) throw new IOException("Arşiv hedefinde bağlantı dosyası var.");
        return path;
    }
    public static void Extract(string archive, string directory, CancellationToken token) {
        if (Directory.Exists(directory) && (File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0) throw new IOException("Hedef klasör bağlantı olamaz.");
        Directory.CreateDirectory(directory);
        using (var zip = ZipFile.OpenRead(archive)) {
            if (zip.Entries.Sum(e => e.Length) > 12L * 1024 * 1024 * 1024) throw new InvalidDataException("Arşiv beklenenden büyük.");
            foreach (var entry in zip.Entries) {
                token.ThrowIfCancellationRequested(); string path = SafePath(directory, entry.FullName);
                if (entry.Name.Length == 0) { Directory.CreateDirectory(path); continue; }
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                using (var src = entry.Open()) using (var dst = new FileStream(path, FileMode.Create, FileAccess.Write)) src.CopyTo(dst);
            }
        }
    }
}
public sealed class SliceStream : Stream {
    readonly Stream source; readonly long start, length; long position;
    public SliceStream(Stream source, long start, long length) { this.source = source; this.start = start; this.length = length; }
    public override bool CanRead { get { return true; } } public override bool CanSeek { get { return true; } } public override bool CanWrite { get { return false; } }
    public override long Length { get { return length; } } public override long Position { get { return position; } set { Seek(value, SeekOrigin.Begin); } }
    public override int Read(byte[] buffer, int offset, int count) { source.Position = start + position; int n = source.Read(buffer, offset, (int)Math.Min(count, length - position)); position += n; return n; }
    public override long Seek(long offset, SeekOrigin origin) { long next = (origin == SeekOrigin.Begin ? 0 : origin == SeekOrigin.Current ? position : length) + offset; if (next < 0 || next > length) throw new IOException("Paket sınırı dışında okuma."); position = next; return next; }
    protected override void Dispose(bool disposing) { if (disposing) source.Dispose(); base.Dispose(disposing); }
    public override void Flush() {} public override void SetLength(long value) { throw new NotSupportedException(); } public override void Write(byte[] b, int o, int c) { throw new NotSupportedException(); }
}
public sealed class Engine {
    public readonly string Root = Path.Combine(Path.GetPathRoot(Environment.SystemDirectory), "ti");
    public string Workspace, Project, OutFile, LogDirectory;
    public Action<string> Report = delegate {};
    public Action<int, string> Step = delegate {};
    public Func<string, bool> AskInternet = delegate { return false; };
    public Func<Package, string> SelectFile = delegate { return null; };
    public bool Flash = true, CreateShortcut = true, AllDevices, AllowNoBoard;
    public string[] SelectedFamilies = new[]{"PF_TM4C"};
    string FamilyState {get{return Path.Combine(CcsInstallRoot,".tiva-setup-ccs-families.json");}}
    string[] RequestedFamilies {get{return AllDevices ? (string[])DeviceFamilies.Ids.Clone() : DeviceFamilies.Normalize(SelectedFamilies);}}
    public bool Tm4cSelected {get{return RequestedFamilies.Contains("PF_TM4C");}}
    public bool RgbToolsReady {get{return Ready("ccs")&&File.Exists(Path.Combine(Ccs,"tools/compiler/ti-cgt-arm_20.2.7.LTS/bin/armcl.exe"))&&File.Exists(Path.Combine(Ccs,"ccs_base/common/targetdb/devices/tm4c123gh6pm.xml"));}}
    string detectedCcsRoot;
    public string CcsInstallRoot {get{return detectedCcsRoot??(detectedCcsRoot=FindCcsRoot());}}
    string FindCcsRoot(){
        var candidates=new List<string>{Path.Combine(Root,"ccs1281")};
        try{if(Directory.Exists(Root))candidates.AddRange(Directory.GetDirectories(Root,"ccs*"));}catch(IOException){}catch(UnauthorizedAccessException){}
        foreach(var hive in new[]{RegistryHive.LocalMachine,RegistryHive.CurrentUser})foreach(var view in new[]{RegistryView.Registry64,RegistryView.Registry32}){
            try{using(var baseKey=RegistryKey.OpenBaseKey(hive,view))using(var uninstall=baseKey.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall")){
                if(uninstall==null)continue;
                foreach(string name in uninstall.GetSubKeyNames())using(var key=uninstall.OpenSubKey(name)){
                    if(key==null)continue;string display=Convert.ToString(key.GetValue("DisplayName")),version=Convert.ToString(key.GetValue("DisplayVersion")),location=Convert.ToString(key.GetValue("InstallLocation"));
                    if(display.IndexOf("Code Composer",StringComparison.OrdinalIgnoreCase)>=0&&(version.StartsWith("12.8.1",StringComparison.Ordinal)||display.Contains("12.8.1"))&&!String.IsNullOrWhiteSpace(location))candidates.Add(location);
                }
            }}catch(System.Security.SecurityException){}catch(UnauthorizedAccessException){}catch(IOException){}
        }
        foreach(string value in candidates.Distinct(StringComparer.OrdinalIgnoreCase)){
            string prefix=value.TrimEnd(Path.DirectorySeparatorChar,Path.AltDirectorySeparatorChar);
            if(Path.GetFileName(prefix).Equals("ccs",StringComparison.OrdinalIgnoreCase))prefix=Path.GetDirectoryName(prefix);
            string ccs=Path.Combine(prefix,"ccs");
            if(File.Exists(Path.Combine(ccs,"eclipse/ccstudio.exe"))&&File.Exists(Path.Combine(ccs,"eclipse/eclipsec.exe"))&&File.Exists(Path.Combine(ccs,"doc/Code_Composer_Studio_12.8.1_manifest.html")))return prefix;
        }
        return Path.Combine(Root,"ccs1281");
    }
    string StateFile { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"TM4C-Ders-Kurulum","hazir-proje-12.json"); } }
    public bool HasPreparedProject { get { return !String.IsNullOrEmpty(Project) && File.Exists(OutFile) && RgbToolsReady; } }
    public static string[] CcsArguments(string prefix, bool all) {
        return CcsArguments(prefix,all ? DeviceFamilies.Ids : new[]{"PF_TM4C"});
    }
    public static string[] CcsArguments(string prefix, IEnumerable<string> families) {
        string[] selected=DeviceFamilies.Normalize(families);
        if(selected.Length==0)throw new ArgumentException("Select at least one CCS device family.");
        var args=new List<string>{"--mode","unattended","--prefix",prefix};
        if(selected.Length!=DeviceFamilies.Ids.Length)args.AddRange(new[]{"--enable-components",String.Join(",",selected)});
        return args.ToArray();
    }
    bool DriverStaged() {
        string store=Path.Combine(Environment.SystemDirectory,"DriverStore/FileRepository");
        return Directory.Exists(store) && Directory.EnumerateDirectories(store,"stellaris_icdi_debug.inf_*").Any(p=>File.Exists(Path.Combine(p,"stellaris_icdi_debug.inf")));
    }
    readonly object logLock = new object();
    string cache, logFile;
    public Engine() {
        Workspace = Path.Combine(Root, "workspace_tm4c_ccs1281");
        cache = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TM4C-Ders-Kurulum", "paketler");
        LogDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TM4C-Ders-Kurulum", "kayitlar", DateTime.Now.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N").Substring(0,6));
        Directory.CreateDirectory(LogDirectory); logFile = Path.Combine(LogDirectory, "kurulum.log");
        if(File.Exists(StateFile)) try {
            var saved=new JavaScriptSerializer().Deserialize<Dictionary<string,string>>(File.ReadAllText(StateFile));
            string name=saved["Project"];
            if(System.Text.RegularExpressions.Regex.IsMatch(name,@"^TivaWare_RGB(?:_\d+)?$")) {
                string dir=Path.Combine(Workspace,name), output=Path.Combine(dir,"Debug",name+".out");
                if(File.Exists(Path.Combine(dir,".tm4c-otokurulum"))&&File.Exists(output)){Project=name;OutFile=output;}
            }
        } catch { }
    }
    void Log(string text) { lock (logLock) File.AppendAllText(logFile, DateTime.Now.ToString("HH:mm:ss") + " " + L.Translate(text) + Environment.NewLine, Encoding.UTF8); Report(text); }
    string Ccs { get { return Path.Combine(CcsInstallRoot, "ccs"); } }
    string Sdk { get { return Path.Combine(Root, "TivaWare_C_Series-2.2.0.295"); } }
    string Eclipse { get { return Path.Combine(Ccs, "eclipse", "eclipsec.exe"); } }
    string Lm { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Texas Instruments", "Stellaris", "LM Flash Programmer", "LMFlash.exe"); } }
    public bool Ready(string id) {
        if (id == "ccs") return File.Exists(Eclipse) && File.Exists(Path.Combine(Ccs,"eclipse/ccstudio.exe"));
        if (id == "tiva") { var expected = new JavaScriptSerializer().Deserialize<Dictionary<string,string>>(Assets.Text("sdk-hashes.json")); return expected.All(pair => File.Exists(Path.Combine(Sdk,pair.Key)) && Assets.HashFile(Path.Combine(Sdk,pair.Key)) == pair.Value); }
        if (id == "icdi") { var b=Assets.ScanBoard(); return b.Connected ? b.Healthy : DriverStaged(); }
        if (id == "lm") return File.Exists(Lm);
        return false;
    }
    public Package[] NeededPackages(){return Assets.Packages().Where(p=>(p.Id=="ccs"||Tm4cSelected)&&!Ready(p.Id)).ToArray();}
    public async Task Run(CancellationToken token, bool testPrepare) {
        if(RequestedFamilies.Length==0)throw new InvalidOperationException("En az bir CCS aygıt ailesi seçin.");
        Log("Tiva Setup 3.4 / CCS 12.8.1 - " + (testPrepare ? "proje hazırlama testi" : "kurulum"));
        Step(0,"Kart kontrol ediliyor"); var board = Assets.ScanBoard();
        if ((!board.Connected && !AllowNoBoard) || board.Count > 1) throw new InvalidOperationException(board.Detail);
        if(!board.Connected||!Tm4cSelected) Flash=false;
        Log(board.Connected ? board.Detail : "Kart olmadan kurulum. Proje hazırlanacak; karta yükleme daha sonra yapılabilir."); token.ThrowIfCancellationRequested();
        string[] installFamilies=RequestedFamilies;
        var needed = NeededPackages();
        if (testPrepare && needed.Length > 0) throw new InvalidOperationException("Test için bileşenler kurulu olmalı; sistem kurulumu yapılmadı.");
        Step(1,"Kurulum dosyaları hazırlanıyor");
        Directory.CreateDirectory(cache);
        var files = new Dictionary<string,string>(); var missing = new List<Package>();
        foreach (var p in needed) { string file = Locate(p); if (file == null) missing.Add(p); else files[p.Id] = file; }
        if (missing.Count > 0) {
            if (!AskInternet(String.Join(", ", missing.Select(p => p.Name)))) throw new OperationCanceledException("İndirme seçilmedi. Dosyaları USB’ye koyup yeniden başlatabilirsiniz.");
            var automatic = missing.Where(p => !String.IsNullOrEmpty(p.Url)).ToArray();
            var downloads = automatic.Select(p => Download(p, token)).ToArray();
            // Start public downloads together; browser authentication stays with the user.
            foreach (var p in missing.Where(p => String.IsNullOrEmpty(p.Url))) {
                string chosen = SelectFile(p); if (chosen == null) throw new OperationCanceledException("Dosya seçimi iptal edildi.");
                Check(p,chosen); files[p.Id] = chosen;
            }
            var results = await Task.WhenAll(downloads);
            for (int i=0;i<automatic.Length;i++) files[automatic[i].Id] = results[i];
        }
        Step(2,"ICDI sürücüsü hazırlanıyor");
        if (!Tm4cSelected)Log("TM4C seçilmedi; ICDI sürücüsü adımı atlandı.");
        else if (!Ready("icdi")) {
            if (!files.ContainsKey("icdi")) throw new IOException("Kart bağlantısı değişti. DEBUG USB’yi yeniden bağlayıp kurulumu tekrar başlatınız.");
            token.ThrowIfCancellationRequested(); string folder = Path.Combine(cache,"icdi"); Assets.Extract(files["icdi"],folder,token);
            string inf = Directory.GetFiles(folder,"stellaris_icdi_debug.inf",SearchOption.AllDirectories).Single();
            RunProcess(Path.Combine(Environment.SystemDirectory,"pnputil.exe"), new[]{"/add-driver",inf,"/install"},"icdi",new[]{0,3010},TimeSpan.FromMinutes(3));
            for(int i=0;i<30 && Assets.ScanBoard().Connected && !Ready("icdi");i++) { token.ThrowIfCancellationRequested(); await Task.Delay(1000,token); }
            if (!Ready("icdi")) throw new InvalidOperationException("ICDI sürücüsü hazır değil. Kayıtları inceleyip kartı DEBUG’dan yeniden bağlayınız.");
            if(!Assets.ScanBoard().Connected)Log("ICDI sürücüsü Windows’a eklendi. Kart takıldığında kullanılacak.");
        } else Log("ICDI sürücüsü hazır; atlandı.");
        Step(3,"Code Composer Studio hazırlanıyor");
        if (!Ready("ccs")) {
            token.ThrowIfCancellationRequested(); if (new DriveInfo(Path.GetPathRoot(Root)).AvailableFreeSpace < 10L*1024*1024*1024) throw new IOException("Kurulum için en az 10 GB boş alan gerekli.");
            if (Directory.Exists(Ccs) && !Ready("ccs")) throw new IOException("CCS 12.8.1 klasörü var ama gerekli bileşenler eksik. Mevcut kurulumu CCS yükleyicisiyle onarın; uygulama bu klasörü silmez.");
            string folder = Path.Combine(cache,"ccs1281"); Assets.Extract(files["ccs"],folder,token);
            string setup = Directory.GetFiles(folder,"ccs_setup_12.8.1.00005.exe",SearchOption.AllDirectories).Single();
            Log(installFamilies.Length==DeviceFamilies.Ids.Length ? "CCS: tüm TI aygıt aileleri kurulacak." : "CCS: seçilen aileler: "+String.Join(", ",installFamilies.Select(id=>DeviceFamilies.Names[Array.IndexOf(DeviceFamilies.Ids,id)])));
            RunProcess(setup,CcsArguments(CcsInstallRoot,installFamilies),"ccs",new[]{0,3010},TimeSpan.FromMinutes(90));
            if (!Ready("ccs")) throw new IOException("CCS kurulum kontrolü başarısız. Günlüğü inceleyiniz.");
            File.WriteAllText(FamilyState,new JavaScriptSerializer().Serialize(installFamilies));
            if(installFamilies.Length==DeviceFamilies.Ids.Length)File.WriteAllText(Path.Combine(CcsInstallRoot,".tm4c-all-devices"),"12.8.1");
        } else Log("CCS 12.8.1 kurulu; yükleyici çalıştırılmadı. Aile seçimi mevcut kurulumu değiştirmez.");
        if(!Tm4cSelected){
            Log("TM4C seçilmedi; TivaWare, LM Flash ve RGB adımları atlandı.");
            if(CreateShortcut&&!testPrepare)Shortcut();Step(8,"Kurulum tamamlandı");
            File.WriteAllText(Path.Combine(LogDirectory,"sonuc.json"),new JavaScriptSerializer().Serialize(new{Success=true,CcsInstallRoot=CcsInstallRoot,SelectedFamilies=RequestedFamilies,RgbPrepared=false,Flash=false}),Encoding.UTF8);return;
        }
        if(!RgbToolsReady)throw new IOException("CCS kurulu; TM4C desteği veya ARM 20.2.7.LTS eksik. Mevcut CCS yükleyicisinden bu bileşenleri ekleyin. Otomatik yeniden kurulum yapılmadı.");
        Step(4,"TivaWare ve LM Flash hazırlanıyor"); token.ThrowIfCancellationRequested();
        if (!Ready("tiva")) {
            if (Directory.Exists(Sdk) && Directory.EnumerateFileSystemEntries(Sdk).Any()) throw new IOException("TivaWare klasörü var ama içeriği beklenen sürümle uyuşmuyor. Mevcut dosyalar değiştirilmedi.");
            string staging=Sdk+".tm4c-"+Guid.NewGuid().ToString("N");
            Assets.Extract(files["tiva"],staging,token);
            var expected=new JavaScriptSerializer().Deserialize<Dictionary<string,string>>(Assets.Text("sdk-hashes.json"));
            if(!expected.All(pair=>File.Exists(Path.Combine(staging,pair.Key))&&Assets.HashFile(Path.Combine(staging,pair.Key))==pair.Value)) throw new IOException("TivaWare dosyaları doğrulanamadı.");
            token.ThrowIfCancellationRequested(); if(Directory.Exists(Sdk))Directory.Delete(Sdk,false); Directory.Move(staging,Sdk); Log("TivaWare 2.2.0.295 hazır.");
        } else Log("TivaWare 2.2.0.295 doğrulandı; atlandı.");
        if (!Ready("lm")) {
            string folder = Path.Combine(cache,"lm"); Assets.Extract(files["lm"],folder,token);
            string msi = Directory.GetFiles(folder,"LMFlashProgrammer.msi",SearchOption.AllDirectories).Single();
            RunProcess(Path.Combine(Environment.SystemDirectory,"msiexec.exe"),new[]{"/i",msi,"/qn","/norestart","/L*v",Path.Combine(LogDirectory,"lm-msi.log")},"lm",new[]{0,3010},TimeSpan.FromMinutes(10));
            if (!Ready("lm")) throw new IOException("LM Flash kurulum kontrolü başarısız.");
        } else Log("LM Flash hazır; atlandı.");
        Step(5,"DriverLib projesi oluşturuluyor"); token.ThrowIfCancellationRequested();
        string workspaceLock=Path.Combine(Workspace,".metadata/.lock");
        if(File.Exists(workspaceLock)) { try { using(var check=new FileStream(workspaceLock,FileMode.Open,FileAccess.ReadWrite,FileShare.None)){} } catch(IOException) { throw new IOException("Kurulumun çalışma alanı CCS’de açık. Dosyalarınızı kaydedip CCS’yi kapatınız; ardından kurulumu tekrar başlatınız."); } }
        Directory.CreateDirectory(Workspace); Project = ChooseProject();
        string projectDir = Path.Combine(Workspace,Project);
        string support = Path.Combine(LogDirectory,"proje"); Directory.CreateDirectory(support);
        string main = Path.Combine(support,"main.c"); File.WriteAllBytes(main,Assets.Read("main.c"));
        string projectSpec = Path.Combine(support,Project+".projectspec");
        string path = Sdk.Replace('\\','/');
        string spec = "<?xml version=\"1.0\" encoding=\"UTF-8\"?><projectSpec><project name=\""+Project+"\" device=\"TM4C123GH6PM\" connection=\"Stellaris_ICDI_Connection.xml\" cgtVersion=\"20.2.7.LTS\" compilerBuildOptions=\"--define=PART_TM4C123GH6PM --define=TARGET_IS_TM4C123_RB1 --include_path="+path+"\" linkerBuildOptions=\"--library="+path+"/driverlib/ccs/Debug/driverlib.lib\"><file path=\"main.c\" targetName=\"main.c\" action=\"copy\" openOnCreation=\"true\"/></project></projectSpec>";
        File.WriteAllText(projectSpec,spec,new UTF8Encoding(false));
        if (!Directory.Exists(projectDir)) RunProcess(Eclipse,new[]{"-noSplash","-data",Workspace,"-application","com.ti.ccstudio.apps.projectCreate","-ccs.projectSpec",projectSpec},"project-create",new[]{0},TimeSpan.FromMinutes(5));
        if (!File.Exists(Path.Combine(projectDir,".project"))) throw new IOException("CCS projesi oluşturulamadı.");
        Step(6,"İlk RGB programı derleniyor"); token.ThrowIfCancellationRequested();
        RunProcess(Eclipse,new[]{"-noSplash","-data",Workspace,"-application","com.ti.ccstudio.apps.projectBuild","-ccs.projects",Project,"-ccs.configuration","Debug","-ccs.listProblems"},"project-build",new[]{0},TimeSpan.FromMinutes(10));
        OutFile = Path.Combine(projectDir,"Debug",Project+".out");
        if (!File.Exists(OutFile) || new FileInfo(OutFile).Length < 1024) throw new IOException("Derleme çıktı dosyası bulunamadı.");
        string map = Path.Combine(projectDir,"Debug",Project+".map");
        if (!File.Exists(map) || !File.ReadAllText(map).Contains("driverlib.lib")) throw new IOException("DriverLib bağlantısı doğrulanamadı.");
        File.WriteAllText(Path.Combine(projectDir,".tm4c-otokurulum"),"1.0",Encoding.UTF8);
        Log("Proje ve DriverLib bağlantısı doğrulandı: " + OutFile);
        File.WriteAllText(StateFile,new JavaScriptSerializer().Serialize(new Dictionary<string,string>{{"Project",Project},{"Version","12.8.1"}}),Encoding.UTF8);
        Step(7, Flash ? "Kart yedekleniyor, RGB programı yükleniyor" : "Karta yükleme seçilmedi");
        if (Flash && !testPrepare) {
            await FlashPrepared(token);
        } else if(!Flash)Log("Karta yazılmadı. Kartı bağlayıp Karta programı yükle düğmesini kullanabilirsiniz.");
        token.ThrowIfCancellationRequested(); if (CreateShortcut && !testPrepare) Shortcut();
        Step(8,"Kurulum tamamlandı"); Log("HAZIR - Çalışma alanı: " + Workspace);
        File.WriteAllText(Path.Combine(LogDirectory,"sonuc.json"),new JavaScriptSerializer().Serialize(new { Success=true,Workspace=Workspace,Project=Project,OutFile=OutFile,Flash=Flash&&!testPrepare,AllDevices=RequestedFamilies.Length==DeviceFamilies.Ids.Length,SelectedFamilies=RequestedFamilies,Date=DateTimeOffset.Now }),Encoding.UTF8);
    }
    public Task FlashPrepared(CancellationToken token) {
            token.ThrowIfCancellationRequested(); var board = Assets.ScanBoard(); if (!board.Healthy || board.Count != 1) throw new IOException("Kartı DEBUG girişinden bağlayınız. Yalnızca tek kart bağlı olmalı ve ICDI sürücüsü hazır olmalı.");
            if(!HasPreparedProject)throw new IOException("Önce kurulumu tamamlayınız. Hazır CCS 12 projesi bulunamadı.");
            Step(7,"Kart yedekleniyor, RGB programı yükleniyor");
            string config = Directory.GetFiles(Path.Combine(Workspace,Project,"targetConfigs"),"*.ccxml").Single();
            string script = Path.Combine(LogDirectory,"flash.js"); File.WriteAllBytes(script,Assets.Read("flash.js"));
            string backup = Path.Combine(LogDirectory,"kart-onceki-program.bin");
            string rhino = Assets.Quote(script) + " " + Assets.Quote(config) + " " + Assets.Quote(OutFile) + " " + Assets.Quote(backup);
            string output = RunProcess(Eclipse,new[]{"-noSplash","-data",Path.Combine(LogDirectory,"dss-workspace"),"-application","com.ti.ccstudio.apps.runScript","-product","com.ti.ccstudio.branding.product","-dss.rhinoArgs",rhino},"flash",new[]{0},TimeSpan.FromMinutes(5));
            if (!output.Contains("TM4C_FLASH_AND_RUN_OK")) throw new IOException("Karta yükleme sonucu doğrulanamadı.");
            Log("Kartın 256 KB yedeği alındı; RGB döngüsü doğrulandı. RESET ile bağımsız çalıştırılabilir.");
            Step(8,"RGB programı yüklendi"); return Task.FromResult(0);
    }
    string ChooseProject() {
        for (int i=0;i<100;i++) {
            string name = "TivaWare_RGB" + (i==0 ? "" : "_"+(i+1)); string dir = Path.Combine(Workspace,name);
            if (!Directory.Exists(dir)) return name;
            string marker = Path.Combine(dir,".tm4c-otokurulum"), main = Path.Combine(dir,"main.c");
            if (File.Exists(marker) && File.Exists(main) && File.ReadAllBytes(main).SequenceEqual(Assets.Read("main.c"))) return name;
        }
        throw new IOException("Proje adı seçilemedi. Mevcut projeler korunuyor.");
    }
    void Check(Package p,string file) { if (!File.Exists(file) || Assets.HashFile(file) != p.Hash) throw new InvalidDataException(p.Name + " dosyası beklenen SHA-256 değeriyle uyuşmuyor. Kurulumda kullanılmadı."); }
    string Locate(Package p) {
        string cached = Path.Combine(cache,p.File);
        if (File.Exists(cached)) { try { Check(p,cached); Log(p.Name+": doğrulanmış yerel dosya bulundu."); return cached; } catch(InvalidDataException) { Log(p.Name+": eski önbellek atlandı."); } }
        using (var zip = Assets.Payload()) if (zip != null) {
            var entry = zip.GetEntry(p.File);
            if (entry != null) {
                Log(p.Name+": EXE içindeki çevrimdışı dosya hazırlanıyor.");
                string partial = cached+".partial";
                using(var src=entry.Open()) using(var dst=new FileStream(partial,FileMode.Create,FileAccess.Write)) src.CopyTo(dst);
                Check(p,partial); if(File.Exists(cached)) File.Delete(cached); File.Move(partial,cached); return cached;
            }
        }
        foreach(string directory in SearchDirectories()) {
            string file=Path.Combine(directory,p.File); if(!File.Exists(file)) continue;
            try { Check(p,file); Log(p.Name+": USB / klasör dosyası bulundu."); return file; } catch(InvalidDataException) { Log(p.Name+": farklı sürüm dosyası atlandı."); }
        }
        return null;
    }
    public Dictionary<string,string> VerifySources() {
        Directory.CreateDirectory(cache); var result=new Dictionary<string,string>();
        foreach(var p in Assets.Packages()) { string file=Locate(p); if(file==null)throw new IOException(p.File+" bulunamadı.");Check(p,file);result[p.Id]=file; }
        return result;
    }
    IEnumerable<string> SearchDirectories() {
        var roots=new List<string>{Path.GetDirectoryName(Application.ExecutablePath),Environment.CurrentDirectory,Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),"Downloads")};
        roots.AddRange(DriveInfo.GetDrives().Where(d=>d.DriveType==DriveType.Removable&&d.IsReady).Select(d=>d.RootDirectory.FullName));
        foreach(string root in roots.Distinct(StringComparer.OrdinalIgnoreCase)) foreach(string folder in new[]{"","paketler","indirilenler","TM4C-Paketler"}) yield return Path.Combine(root,folder);
    }
    async Task<string> Download(Package p,CancellationToken token) {
        string dest=Path.Combine(cache,p.File), partial=dest+".partial";
        Log(p.Name+": resmî kaynaktan indiriliyor.");
        ServicePointManager.SecurityProtocol=SecurityProtocolType.Tls12;
        using(var client=new WebClient()) using(token.Register(client.CancelAsync)) {
            client.Headers[HttpRequestHeader.UserAgent]="TivaSetup/3.4";
            try { await client.DownloadFileTaskAsync(new Uri(p.Url),partial); token.ThrowIfCancellationRequested(); Check(p,partial); }
            catch { if(File.Exists(partial)) File.Delete(partial); throw; }
        }
        if(File.Exists(dest)) File.Delete(dest); File.Move(partial,dest); Log(p.Name+": indirme doğrulandı."); return dest;
    }
    public string RunProcess(string exe,string[] args,string label,int[] accepted,TimeSpan timeout) {
        string targetLog=Path.Combine(LogDirectory,label+".log"); Log(label+" işlemi başladı.");
        var text=new StringBuilder(); var sync=new object();
        using(var p=new Process()) {
            p.StartInfo=new ProcessStartInfo(exe,String.Join(" ",args.Select(Assets.Argument))) { UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true,WorkingDirectory=Path.GetDirectoryName(exe) };
            DataReceivedEventHandler capture=delegate(object sender,DataReceivedEventArgs e) { if(e.Data!=null) lock(sync) text.AppendLine(e.Data); };
            p.OutputDataReceived+=capture; p.ErrorDataReceived+=capture;
            p.Start(); p.BeginOutputReadLine(); p.BeginErrorReadLine();
            var elapsed=Stopwatch.StartNew(); long lastMinute=0;
            while(!p.WaitForExit(1000)) {
                long minute=(long)elapsed.Elapsed.TotalMinutes;
                if(minute>lastMinute){lastMinute=minute;Log(label+" işlemi sürüyor ("+minute+" dakika). Yükleyici tamamlanınca devam edilecek.");}
                if(elapsed.Elapsed>timeout) { lock(sync) File.WriteAllText(targetLog,text.ToString(),Encoding.UTF8); throw new TimeoutException(label+" beklenenden uzun sürdü. İşlem zorla kapatılmadı; yeniden başlatmadan önce yükleyicinin bitmesini bekleyiniz."); }
            }
            p.WaitForExit(); string output; lock(sync) output=text.ToString(); File.WriteAllText(targetLog,output,Encoding.UTF8);
            if(!accepted.Contains(p.ExitCode)) throw new IOException(label+" başarısız (kod "+p.ExitCode+"). Ayrıntı: "+targetLog);
            if(p.ExitCode==3010) Log("Windows yeniden başlatma isteği bildirdi.");
            Log(label+" tamamlandı."); return output;
        }
    }
    void Shortcut() {
        var shell=Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell"));
        var link=shell.GetType().InvokeMember("CreateShortcut",BindingFlags.InvokeMethod,null,shell,new object[]{Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),"Tiva Setup - CCS.lnk")});
        link.GetType().InvokeMember("TargetPath",BindingFlags.SetProperty,null,link,new object[]{Path.Combine(Ccs,"eclipse/ccstudio.exe")});
        link.GetType().InvokeMember("Arguments",BindingFlags.SetProperty,null,link,new object[]{"-data "+Assets.Quote(Workspace)});
        link.GetType().InvokeMember("WorkingDirectory",BindingFlags.SetProperty,null,link,new object[]{Ccs});
        link.GetType().InvokeMember("Save",BindingFlags.InvokeMethod,null,link,null);
    }
}
public static class Program {
    [STAThread] public static int Main(string[] args) {
        L.Initialize(args);
        if(args.Length>0 && !args[0].StartsWith("--ui-",StringComparison.Ordinal)){
            try{
                if(args[0]=="--localization-test"){
                    string raw="ccs başarısız (kod 1639). Ayrıntı: C:\\ti\\kurulum\\ccs.log";
                    L.English=true;
                    if(L.Translate(raw)!="ccs failed (code 1639). Details: C:\\ti\\kurulum\\ccs.log")throw new Exception("Localized diagnostic changed its file path or failed to translate.");
                    if(L.Translate("ccs işlemi sürüyor (25 dakika). Yükleyici tamamlanınca devam edilecek.")!="ccs is running (25 min). Setup will continue when the installer finishes.")throw new Exception("Localized progress formatting failed.");
                    L.English=false;if(L.Translate(raw)!=raw)throw new Exception("Turkish diagnostic changed.");
                    foreach(string resource in new[]{"help.en.txt","help.tr.txt","licenses.en.txt","licenses.tr.txt","licenses.txt"})if(Assets.Text(resource).Length<100)throw new Exception("Missing text: "+resource);
                    File.WriteAllText(Path.Combine(Path.GetDirectoryName(Application.ExecutablePath),"localization-test.txt"),"PASS: dynamic diagnostics / path preservation / TR restoration / embedded help and licenses",Encoding.UTF8);return 0;
                }
                if(args[0]=="--source-test"){
                    var engine=new Engine();File.WriteAllText(Path.Combine(Path.GetDirectoryName(Application.ExecutablePath),"kaynak-test.json"),new JavaScriptSerializer().Serialize(engine.VerifySources()),Encoding.UTF8);return 0;
                }
                if(args[0]=="--setup-plan-test"){
                    var plans=new List<object>();
                    foreach(var families in new[]{new[]{"PF_TM4C"},new[]{"PF_C28"},DeviceFamilies.Ids}){
                        var engine=new Engine{SelectedFamilies=families};var needed=engine.NeededPackages();
                        if(engine.Ready("ccs")&&needed.Any(p=>p.Id=="ccs"))throw new Exception("Existing CCS was scheduled for reinstallation.");
                        if(!engine.Tm4cSelected&&needed.Any(p=>p.Id!="ccs"))throw new Exception("Non-TM4C setup scheduled TM4C packages.");
                        plans.Add(new{SelectedFamilies=families,Installed=Assets.Packages().ToDictionary(p=>p.Id,p=>engine.Ready(p.Id)),Needed=needed.Select(p=>p.Id).ToArray(),engine.CcsInstallRoot,engine.RgbToolsReady});
                    }
                    File.WriteAllText(Path.Combine(Path.GetDirectoryName(Application.ExecutablePath),"setup-plan-test.json"),new JavaScriptSerializer().Serialize(plans),Encoding.UTF8);return 0;
                }
                if(args[0]=="--existing-ccs-test"){
                    var engine=new Engine{SelectedFamilies=new[]{"PF_C28"},Flash=false,AllowNoBoard=true,CreateShortcut=false};
                    if(!engine.Ready("ccs")||engine.NeededPackages().Length!=0)throw new Exception("Test requires existing CCS; no installer was run.");
                    var reports=new List<string>();engine.Report=reports.Add;
                    engine.Run(CancellationToken.None,false).GetAwaiter().GetResult();
                    if(reports.Any(s=>s.Contains("işlemi başladı.")))throw new Exception("Existing-only setup launched a process.");
                    File.WriteAllText(Path.Combine(Path.GetDirectoryName(Application.ExecutablePath),"existing-ccs-test.json"),new JavaScriptSerializer().Serialize(new{Success=true,Needed=engine.NeededPackages().Length,engine.CcsInstallRoot,engine.LogDirectory,Reports=reports}),Encoding.UTF8);return 0;
                }
                if(args[0]=="--install-no-board-test"){
                    var engine=new Engine{Flash=false,AllowNoBoard=true,AllDevices=args.Contains("--all-devices"),CreateShortcut=false};
                    engine.Run(CancellationToken.None,false).GetAwaiter().GetResult();
                    File.WriteAllText(Path.Combine(Path.GetDirectoryName(Application.ExecutablePath),"kartsiz-kurulum-test.json"),new JavaScriptSerializer().Serialize(new{Success=true,engine.Workspace,engine.OutFile,engine.LogDirectory,engine.AllDevices,engine.HasPreparedProject}),Encoding.UTF8);return 0;
                }
                if(args[0]=="--flash-prepared-test"){
                    var engine=new Engine{CreateShortcut=false};
                    engine.FlashPrepared(CancellationToken.None).GetAwaiter().GetResult();
                    File.WriteAllText(Path.Combine(Path.GetDirectoryName(Application.ExecutablePath),"sonradan-yukleme-test.json"),new JavaScriptSerializer().Serialize(new{Success=true,engine.Project,engine.OutFile,engine.LogDirectory}),Encoding.UTF8);return 0;
                }
                if(args[0]=="--verify"){
                    var results=new Dictionary<string,string>();using(var zip=Assets.Payload()){
                        if(zip==null)throw new IOException("Çevrimdışı paket yok.");
                        foreach(var p in Assets.Packages()){var e=zip.GetEntry(p.File);if(e==null)throw new IOException(p.File+" eksik.");using(var s=e.Open()){string hash=Assets.Hash(s);if(hash!=p.Hash)throw new IOException(p.File+" bozuk.");results[p.Id]=hash;}}
                    }File.WriteAllText(Path.Combine(Path.GetDirectoryName(Application.ExecutablePath),"paket-dogrulama.json"),new JavaScriptSerializer().Serialize(results),Encoding.UTF8);return 0;
                }
                if(args[0]=="--prepare-test" || args[0]=="--flash-test"){
                    bool flash=args[0]=="--flash-test";
                    var engine=new Engine{Flash=flash,CreateShortcut=false,AllowNoBoard=!flash,AllDevices=args.Contains("--all-devices")};
                    if(Assets.Packages().Any(p=>!engine.Ready(p.Id)))throw new IOException("Test için kurulu bileşenler hazır değil. Sistem kurulumu yapılmadı.");
                    engine.Run(CancellationToken.None,!flash).GetAwaiter().GetResult();File.WriteAllText(Path.Combine(Path.GetDirectoryName(Application.ExecutablePath),flash?"kart-test.json":"proje-test.json"),new JavaScriptSerializer().Serialize(new{Success=true,engine.Workspace,engine.OutFile,engine.LogDirectory}),Encoding.UTF8);return 0;
                }
                if(args[0]=="--self-test"){
                    foreach(string malicious in new[]{"../escape","C:/escape","/escape","a/../../escape"}){bool rejected=false;try{Assets.SafePath(Path.GetTempPath(),malicious);}catch(InvalidDataException){rejected=true;}if(!rejected)throw new Exception("Güvensiz arşiv yolu kabul edildi.");}
                    if(Assets.Quote("C:\\a b\\")!="\"C:\\a b\\\\\"")throw new Exception("Argüman kaçışı hatalı.");
                    if(Assets.Argument("/qn")!="/qn" || Assets.Argument("/L*v")!="/L*v" || Assets.Argument("C:\\a b\\x.msi")!="\"C:\\a b\\x.msi\"")throw new Exception("MSI argüman biçimi hatalı.");
                    if(!Engine.CcsArguments("C:\\ti\\ccs1281",false).Contains("PF_TM4C") || Engine.CcsArguments("C:\\ti\\ccs1281",true).Contains("--enable-components"))throw new Exception("CCS aygıt seçimi hatalı.");
                    if(!Engine.CcsArguments("C:\\ti\\ccs1281",new[]{"PF_C28","PF_MSPM0","PF_C28"}).Contains("PF_MSPM0,PF_C28"))throw new Exception("Multi-family arguments failed.");
                    string[] restored=DeviceFamilies.FromArgs(new[]{"--ui-families=PF_TM4C,PF_C28"});if(!restored.SequenceEqual(new[]{"PF_TM4C","PF_C28"}))throw new Exception("Elevated family selection restoration failed.");
                    bool invalid=false;try{DeviceFamilies.Normalize(new[]{"PF_FAKE"});}catch(ArgumentException){invalid=true;}if(!invalid)throw new Exception("Invalid family accepted.");
                    if(DeviceFamilies.Normalize(new[]{"PF_C28"}).Contains("PF_TM4C"))throw new Exception("TM4C was forced into selection.");
                    if(DeviceFamilies.FromArgs(new[]{"--ui-families="}).Length!=0)throw new Exception("Empty selection restoration failed.");
                    bool empty=false;try{Engine.CcsArguments("C:\\ti\\ccs1281",new string[0]);}catch(ArgumentException){empty=true;}if(!empty)throw new Exception("Empty selection would install every family.");
                    File.WriteAllText(Path.Combine(Path.GetDirectoryName(Application.ExecutablePath),"self-test.txt"),"PASS: path traversal / argument quoting / board scan\r\n"+Assets.ScanBoard().Detail,Encoding.UTF8);return 0;
                }
                throw new ArgumentException("Bilinmeyen seçenek.");
            }catch(Exception e){File.WriteAllText(Path.Combine(Path.GetDirectoryName(Application.ExecutablePath),"test-error.txt"),e.ToString(),Encoding.UTF8);return 1;}
        }
        bool created;using(var mutex=new Mutex(true,args.Contains("--ui-busy-preview")||args.Contains("--ui-preview") ? "Local\\TivaSetupPreview" : "Local\\TivaSetup",out created)){
            if(!created){bool acquired=false;try{acquired=mutex.WaitOne(3000);}catch(AbandonedMutexException){acquired=true;}if(!acquired){MessageBox.Show(L.T("Tiva Setup zaten açık.","Tiva Setup is already open."));return 1;}}
        Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);Application.Run(new MainForm(args));return 0;
        }
    }
}
}

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace TM4CDers {
public static class DeviceFamilies {
    // Product-family packages supplied by the CCS 12.8.1 Windows distribution.
    public static readonly string[] Ids={"PF_TM4C","PF_MSP430","PF_MSP432","PF_MSPM0","PF_WCONN","PF_C28","PF_PGA","PF_HERCULES","PF_SITARA","PF_SITARA_MCU","PF_OMAPL","PF_DAVINCI","PF_OMAP","PF_TDA_DRA","PF_C55","PF_C6000SC","PF_C66AK_KEYSTONE","PF_MMWAVE","PF_C64MC","PF_DIGITAL_POWER"};
    public static readonly string[] Names={"Tiva C / TM4C","MSP430","MSP432","MSPM0","SimpleLink / Wireless MCUs","C2000","PGA","Hercules","Sitara AM3x / AM4x / AM5x / AM6x","Sitara AM2x","OMAP-L1x","DaVinci","OMAP","TDAx / Jacinto DRAx","C55x DSP","C6000 DSP","66AK2x / C66x KeyStone","mmWave","C64x DSP","UCD Digital Power"};
    public static string[] Normalize(IEnumerable<string> ids){
        var selected=new HashSet<string>(ids??new[]{"PF_TM4C"},StringComparer.Ordinal);
        if(selected.Any(id=>!Ids.Contains(id)))throw new ArgumentException("Unknown CCS device family.");
        return Ids.Where(selected.Contains).ToArray();
    }
    public static string[] FromArgs(string[] args){var value=args.FirstOrDefault(a=>a.StartsWith("--ui-families=",StringComparison.Ordinal));return args.Contains("--ui-all") ? (string[])Ids.Clone() : Normalize(value==null ? null : value.Substring(14).Split(new[]{','},StringSplitOptions.RemoveEmptyEntries));}
}
public sealed class FamilySelector : SetupButton {
    string[] selected={"PF_TM4C"};
    readonly ToolStripDropDown popup=new ToolStripDropDown{AutoClose=true,Padding=new Padding(1),Margin=Padding.Empty,Renderer=new ThemeMenuRenderer()};
    readonly Panel body=new Panel{Size=new Size(680,408)};
    readonly SetupCheckBox all=new SetupCheckBox{Location=new Point(18,14),AutoSize=true,ThreeState=true};
    readonly List<SetupCheckBox> options=new List<SetupCheckBox>();
    readonly Label note=new Label{Location=new Point(18,330),Size=new Size(640,24),Font=new Font("Segoe UI",9)};
    readonly SetupButton done=new SetupButton{Location=new Point(548,363),Size=new Size(112,32),Primary=true};
    bool updating;
    public event EventHandler SelectionChanged;
    public string[] SelectedFamilies {get{return (string[])selected.Clone();}set{selected=DeviceFamilies.Normalize(value);Sync();Changed();}}
    public FamilySelector(){
        Font=new Font("Segoe UI",10);Cursor=Cursors.Hand;
        body.Font=new Font("Segoe UI",9.5f);body.Controls.Add(all);body.Controls.Add(note);body.Controls.Add(done);
        for(int i=0;i<DeviceFamilies.Ids.Length;i++){
            var choice=new SetupCheckBox{AutoSize=false,Size=new Size(319,27),Location=new Point(18+(i/10)*330,52+(i%10)*27),Tag=DeviceFamilies.Ids[i]};
            choice.CheckedChanged+=delegate{if(updating)return;selected=DeviceFamilies.Normalize(options.Where(c=>c.Checked).Select(c=>(string)c.Tag));Sync();Changed();};
            options.Add(choice);body.Controls.Add(choice);
        }
        all.Click+=delegate{if(updating)return;selected=selected.Length==DeviceFamilies.Ids.Length ? new string[0] : (string[])DeviceFamilies.Ids.Clone();Sync();Changed();};
        done.Click+=delegate{popup.Close();Focus();};
        popup.Items.Add(new ToolStripControlHost(body){AutoSize=false,Size=body.Size,Margin=Padding.Empty,Padding=Padding.Empty});
        Click+=delegate{RefreshLanguage();Theme.Apply(body);note.ForeColor=Theme.Muted;popup.BackColor=Theme.Background;popup.Show(this,new Point(0,Height+5));};
        RefreshLanguage();
    }
    void Changed(){var handler=SelectionChanged;if(handler!=null)handler(this,EventArgs.Empty);}
    void Sync(){updating=true;try{for(int i=0;i<options.Count;i++)options[i].Checked=selected.Contains(DeviceFamilies.Ids[i]);all.CheckState=selected.Length==DeviceFamilies.Ids.Length ? CheckState.Checked : selected.Length==0 ? CheckState.Unchecked : CheckState.Indeterminate;}finally{updating=false;}}
    public string Summary {get{if(selected.Length==0)return L.T("Aile seçilmedi","No family selected");if(selected.Length==DeviceFamilies.Ids.Length)return L.T("Tüm TI aileleri","All TI families");string text=String.Join(" + ",selected.Take(2).Select(id=>DeviceFamilies.Names[Array.IndexOf(DeviceFamilies.Ids,id)]));return selected.Length<=2 ? text : text+" + "+(selected.Length-2)+L.T(" aile"," more");}}
    public void RefreshLanguage(){
        Text=L.T("Seç  ▾","Select  ▾");AccessibleName=L.T("CCS aygıt ailelerini seç","Select CCS device families");
        all.Text=L.T("Tümünü seç","Select all");done.Text=L.T("Tamam","Done");
        note.Text=L.T("Seçimler yeni CCS kurulumu içindir. Kurulu CCS yeniden kurulmaz.","Selections apply to a new CCS installation. Existing CCS is kept.");
        for(int i=0;i<options.Count;i++)options[i].Text=DeviceFamilies.Names[i];
        Sync();
    }
    protected override void Dispose(bool disposing){if(disposing)popup.Dispose();base.Dispose(disposing);}
}
}

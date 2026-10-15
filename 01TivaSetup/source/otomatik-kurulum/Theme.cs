using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace TM4CDers {
public static class Theme {
    public static bool Dark;
    static string Settings { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"TivaSetup","theme.txt"); } }
    public static Color Background { get { return Dark ? Color.FromArgb(17,24,39) : Color.FromArgb(244,248,247); } }
    public static Color Surface { get { return Dark ? Color.FromArgb(29,41,57) : Color.White; } }
    public static Color Text { get { return Dark ? Color.FromArgb(230,237,245) : Color.FromArgb(40,56,53); } }
    public static Color Muted { get { return Dark ? Color.FromArgb(160,177,196) : Color.DimGray; } }
    public static Color Accent { get { return Dark ? Color.FromArgb(81,212,189) : Color.FromArgb(0,126,112); } }
    public static Color Border { get { return Dark ? Color.FromArgb(65,81,101) : Color.FromArgb(204,215,212); } }
    public static void Load(string[] args){
        try{Dark=File.Exists(Settings)&&File.ReadAllText(Settings).Trim()=="DARK";}catch(IOException){}catch(UnauthorizedAccessException){}
        if(args.Contains("--ui-dark"))Dark=true;if(args.Contains("--ui-light"))Dark=false;
    }
    public static void Select(bool dark){Dark=dark;try{Directory.CreateDirectory(Path.GetDirectoryName(Settings));File.WriteAllText(Settings,dark ? "DARK" : "LIGHT");}catch(IOException){}catch(UnauthorizedAccessException){}}
    public static void Apply(Control root){
        root.BackColor=root is TextBoxBase || root is ThemedTextView ? Surface : Background;root.ForeColor=Text;
        foreach(Control child in root.Controls)Apply(child);
        var button=root as Button;if(button!=null){button.BackColor=Surface;button.FlatAppearance.BorderColor=Border;}
        var form=root as Form;if(form!=null&&form.IsHandleCreated)Caption(form);
        var viewer=root as ThemedTextView;if(viewer!=null)viewer.RefreshTheme();
        root.Invalidate();
    }
    [DllImport("dwmapi.dll")]static extern int DwmSetWindowAttribute(IntPtr window,int attribute,ref int value,int size);
    static int ColorRef(Color c){return c.R|(c.G<<8)|(c.B<<16);}
    public static void Caption(Form form){
        // Documented DWM attributes; unsupported Windows versions keep their normal frame.
        try{int dark=Dark ? 1 : 0, bg=Dark ? ColorRef(Background) : -1, fg=Dark ? ColorRef(Text) : -1;
            DwmSetWindowAttribute(form.Handle,20,ref dark,4);DwmSetWindowAttribute(form.Handle,35,ref bg,4);DwmSetWindowAttribute(form.Handle,36,ref fg,4);
        }catch(DllNotFoundException){}catch(EntryPointNotFoundException){}
    }
}
public sealed class SetupGroup : GroupBox {
    protected override void OnPaint(PaintEventArgs e){
        e.Graphics.Clear(Theme.Background);int top=Font.Height/2;
        using(var p=new Pen(Theme.Border))e.Graphics.DrawRectangle(p,0,top,Width-1,Height-top-1);
        var size=TextRenderer.MeasureText(Text,Font);using(var b=new SolidBrush(Theme.Background))e.Graphics.FillRectangle(b,9,0,size.Width+6,size.Height);
        TextRenderer.DrawText(e.Graphics,Text,Font,new Point(12,0),Theme.Text,TextFormatFlags.NoPadding);
    }
}
static class ChoicePaint {
    static int Glyph(Control control){return Math.Max(14,control.Font.Height);}
    public static Size Preferred(Control control){Size text=TextRenderer.MeasureText(control.Text,control.Font);return new Size(Glyph(control)+8+text.Width+6,Math.Max(Glyph(control),text.Height)+6);}
    public static void Draw(Control control,Graphics g,bool radio,bool check,bool mixed,bool focus){
        g.Clear(Theme.Background);g.SmoothingMode=SmoothingMode.AntiAlias;
        int size=Glyph(control), top=(control.Height-size)/2;
        var box=new Rectangle(2,top,size,size);
        Color ink=control.Enabled ? Theme.Text : Theme.Muted;
        Color line=control.Enabled&&check ? Theme.Accent : Theme.Muted;
        using(var brush=new SolidBrush(Theme.Surface)){
            if(radio)g.FillEllipse(brush,box);else g.FillRectangle(brush,box);
        }
        using(var pen=new Pen(line,1.5f)){
            if(radio)g.DrawEllipse(pen,box);else g.DrawRectangle(pen,box);
        }
        if(radio&&check){using(var b=new SolidBrush(line))g.FillEllipse(b,Rectangle.Inflate(box,-size/4,-size/4));}
        if(!radio&&(check||mixed)){
            using(var b=new SolidBrush(control.Enabled ? Theme.Accent : Theme.Border))g.FillRectangle(b,Rectangle.Inflate(box,-1,-1));
            Color tick=control.Enabled ? (Theme.Dark ? Theme.Background : Color.White) : ink;
            using(var p=new Pen(tick,2f)){p.StartCap=LineCap.Round;p.EndCap=LineCap.Round;
                if(mixed)g.DrawLine(p,box.Left+4,box.Top+size/2,box.Right-4,box.Top+size/2);
                else g.DrawLines(p,new[]{new Point(box.Left+size/4,box.Top+size/2),new Point(box.Left+size*2/5,box.Top+size*3/4),new Point(box.Left+size*4/5,box.Top+size/4)});
            }
        }
        var textBounds=new Rectangle(size+10,0,Math.Max(0,control.Width-size-10),control.Height);
        TextRenderer.DrawText(g,control.Text,control.Font,textBounds,ink,TextFormatFlags.Left|TextFormatFlags.VerticalCenter|TextFormatFlags.SingleLine);
        if(focus)ControlPaint.DrawFocusRectangle(g,Rectangle.Inflate(textBounds,-2,-2),ink,Theme.Background);
    }
}
public sealed class SetupCheckBox : CheckBox {
    public SetupCheckBox(){FlatStyle=FlatStyle.Flat;SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer,true);}
    public override Size GetPreferredSize(Size proposedSize){return ChoicePaint.Preferred(this);}
    protected override void OnEnabledChanged(EventArgs e){base.OnEnabledChanged(e);Invalidate();}
    protected override void OnCheckedChanged(EventArgs e){base.OnCheckedChanged(e);Invalidate();}
    protected override void OnPaint(PaintEventArgs e){ChoicePaint.Draw(this,e.Graphics,false,Checked,CheckState==CheckState.Indeterminate,Focused&&ShowFocusCues);}
}
public sealed class SetupRadioButton : RadioButton {
    public SetupRadioButton(){FlatStyle=FlatStyle.Flat;SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer,true);}
    public override Size GetPreferredSize(Size proposedSize){return ChoicePaint.Preferred(this);}
    protected override void OnEnabledChanged(EventArgs e){base.OnEnabledChanged(e);Invalidate();}
    protected override void OnCheckedChanged(EventArgs e){base.OnCheckedChanged(e);Invalidate();}
    protected override void OnPaint(PaintEventArgs e){ChoicePaint.Draw(this,e.Graphics,true,Checked,false,Focused&&ShowFocusCues);}
}
public class SetupButton : Button {
    public bool Primary;
    bool hover;
    public SetupButton(){FlatStyle=FlatStyle.Flat;SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer,true);}
    protected override void OnMouseEnter(EventArgs e){hover=true;Invalidate();base.OnMouseEnter(e);}
    protected override void OnMouseLeave(EventArgs e){hover=false;Invalidate();base.OnMouseLeave(e);}
    protected override void OnEnabledChanged(EventArgs e){Invalidate();base.OnEnabledChanged(e);}
    protected override void OnPaint(PaintEventArgs e){
        Color bg=Primary&&Enabled ? Theme.Accent : Theme.Surface;
        if(hover&&Enabled)bg=ControlPaint.Light(bg,.12f);
        e.Graphics.Clear(bg);
        using(var pen=new Pen(Primary&&Enabled ? Theme.Accent : Theme.Border))e.Graphics.DrawRectangle(pen,0,0,Width-1,Height-1);
        Color fg=!Enabled ? Theme.Muted : Primary ? (Theme.Dark ? Color.FromArgb(17,24,39) : Color.White) : Theme.Text;
        TextRenderer.DrawText(e.Graphics,Text,Font,ClientRectangle,fg,TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter|TextFormatFlags.SingleLine);
        if(Focused&&ShowFocusCues)ControlPaint.DrawFocusRectangle(e.Graphics,Rectangle.Inflate(ClientRectangle,-5,-5),fg,bg);
    }
}
public sealed class ThemeButton : Button {
    public ThemeButton(){FlatStyle=FlatStyle.Flat;Cursor=Cursors.Hand;SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer,true);}
    protected override void OnPaint(PaintEventArgs e){
        e.Graphics.Clear(Theme.Background);e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;
        float size=28, x=(Width-size)/2, y=(Height-size)/2;
        var saved=e.Graphics.Save();e.Graphics.TranslateTransform(x,y);e.Graphics.ScaleTransform(size/32,size/32);
        using(var pen=new Pen(Theme.Accent,2.2f)){pen.StartCap=LineCap.Round;pen.EndCap=LineCap.Round;
            if(Theme.Dark){e.Graphics.DrawEllipse(pen,10,10,12,12);for(int i=0;i<8;i++){double a=i*Math.PI/4;e.Graphics.DrawLine(pen,16+(float)Math.Cos(a)*11,16+(float)Math.Sin(a)*11,16+(float)Math.Cos(a)*14,16+(float)Math.Sin(a)*14);}}
            else using(var p=new GraphicsPath()){
                p.AddBezier(22,4,14,2,6,9,7,17);p.AddBezier(7,17,8,26,20,30,27,22);p.AddBezier(27,22,19,24,12,18,13,11);p.AddBezier(13,11,14,7,18,4,22,4);
                e.Graphics.DrawPath(pen,p);
            }
        }
        e.Graphics.Restore(saved);
        if(Focused&&ShowFocusCues)ControlPaint.DrawFocusRectangle(e.Graphics,Rectangle.Inflate(ClientRectangle,-4,-4),Theme.Accent,Theme.Background);
    }
}
public sealed class ThemeMenuRenderer : ToolStripProfessionalRenderer {
    protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e){e.Graphics.Clear(Theme.Background);}
    protected override void OnRenderDropDownButtonBackground(ToolStripItemRenderEventArgs e){
        using(var b=new SolidBrush(e.Item.Selected||e.Item.Pressed ? Theme.Surface : Theme.Background))e.Graphics.FillRectangle(b,new Rectangle(Point.Empty,e.Item.Size));
        OnRenderArrow(new ToolStripArrowRenderEventArgs(e.Graphics,e.Item,new Rectangle(e.Item.Width-18,(e.Item.Height-8)/2,12,8),Theme.Text,ArrowDirection.Down));
    }
    protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e){using(var b=new SolidBrush(e.Item.Selected ? Theme.Surface : Theme.Background))e.Graphics.FillRectangle(b,new Rectangle(Point.Empty,e.Item.Size));}
    protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e){e.TextColor=e.Item.Enabled ? Theme.Text : Theme.Muted;base.OnRenderItemText(e);}
    protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e){e.ArrowColor=Theme.Text;base.OnRenderArrow(e);}
    protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e){using(var p=new Pen(Theme.Border))e.Graphics.DrawRectangle(p,0,0,e.ToolStrip.Width-1,e.ToolStrip.Height-1);}
    protected override void OnRenderImageMargin(ToolStripRenderEventArgs e){using(var b=new SolidBrush(Theme.Background))e.Graphics.FillRectangle(b,e.AffectedBounds);}
    protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e){using(var p=new Pen(Theme.Border))e.Graphics.DrawLine(p,8,e.Item.Height/2,e.Item.Width-8,e.Item.Height/2);}
}
}

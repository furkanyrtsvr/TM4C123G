using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace TM4CDers {
// Keep native read-only text selection, copying and wrapping; draw the chrome ourselves.
public sealed class ThemedTextView : UserControl {
    sealed class Editor : TextBox {
        public Action Scrolled;public Action<int> Wheel;
        protected override void WndProc(ref Message m){
            if(m.Msg==0x20A&&Wheel!=null){Wheel((short)((m.WParam.ToInt64()>>16)&0xFFFF));m.Result=IntPtr.Zero;return;}
            base.WndProc(ref m);
            if(Scrolled!=null&&(m.Msg==0x115||m.Msg==0x20A||m.Msg==0x100||m.Msg==0xB6||m.Msg==0xB7))Scrolled();
        }
    }
    [DllImport("user32.dll",CharSet=CharSet.Auto)]static extern IntPtr SendMessage(IntPtr h,int msg,IntPtr w,IntPtr l);
    readonly Editor editor; readonly TextScrollBar bar;
    bool layoutBusy,measure=true;int measuredWidth,measuredHeight,wheelRemainder;
    public ThemedTextView(){
        SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer,true);
        editor=new Editor{Multiline=true,ReadOnly=true,WordWrap=true,ScrollBars=ScrollBars.None,BorderStyle=BorderStyle.None};
        bar=new TextScrollBar();bar.PositionChanged=ScrollTo;editor.Scrolled=RefreshScroll;editor.Wheel=ScrollWheel;
        editor.TextChanged+=delegate{measure=true;RefreshScroll();};editor.Resize+=delegate{RefreshScroll();};
        Controls.Add(editor);Controls.Add(bar);TabStop=false;
        editor.BackColor=Theme.Surface;editor.ForeColor=Theme.Text;
    }
    public override string Text{get{return editor==null ? "" : editor.Text;}set{if(editor!=null){editor.Text=value??"";RefreshScroll();}}}
    public int TextLength{get{return editor.TextLength;}}
    public int SelectionStart{get{return editor.SelectionStart;}set{editor.SelectionStart=value;}}
    public void Select(int start,int length){editor.Select(start,length);}
    public void AppendText(string value){editor.AppendText(value);ScrollToCaret();}
    public void ScrollToCaret(){editor.ScrollToCaret();RefreshScroll();}
    public void RefreshTheme(){BackColor=editor.BackColor=Theme.Surface;editor.ForeColor=Theme.Text;bar.Invalidate();Invalidate();}
    protected override void OnFontChanged(EventArgs e){base.OnFontChanged(e);if(editor!=null){editor.Font=Font;measure=true;RefreshScroll();}}
    protected override void OnSizeChanged(EventArgs e){base.OnSizeChanged(e);measure=true;RefreshScroll();}
    protected override void OnHandleCreated(EventArgs e){base.OnHandleCreated(e);RefreshScroll();}
    protected override void OnVisibleChanged(EventArgs e){base.OnVisibleChanged(e);measure=true;RefreshScroll();}
    protected override void OnPaint(PaintEventArgs e){e.Graphics.Clear(Theme.Surface);using(var p=new Pen(Theme.Border))e.Graphics.DrawRectangle(p,0,0,Width-1,Height-1);}
    int FirstLine{get{return (int)SendMessage(editor.Handle,0xCE,IntPtr.Zero,IntPtr.Zero);}}
    int LineCount{get{return (int)SendMessage(editor.Handle,0xBA,IntPtr.Zero,IntPtr.Zero);}}
    int VisibleLines{get{return Math.Max(1,editor.ClientSize.Height/Math.Max(1,editor.Font.Height));}}
    void ScrollWheel(int delta){wheelRemainder+=delta;int ticks=wheelRemainder/120;wheelRemainder-=ticks*120;
        int lines=SystemInformation.MouseWheelScrollLines;if(ticks!=0)ScrollTo(FirstLine-ticks*(lines<0 ? VisibleLines : lines));}
    void ScrollTo(int first){first=Math.Max(0,Math.Min(Math.Max(0,LineCount-VisibleLines),first));int delta=first-FirstLine;SendMessage(editor.Handle,0xB6,IntPtr.Zero,new IntPtr(delta));RefreshScroll();}
    void RefreshScroll(){
        if(editor==null||layoutBusy||IsDisposed||!IsHandleCreated||!Visible||Width<10||Height<10)return;
        layoutBusy=true;try{
            int inset=4, barWidth=Math.Max(16,Font.Height), visible=Math.Max(1,(Height-2*inset)/Math.Max(1,editor.Font.Height));
            // Reflow only when content or size changes; scrolling must not resize the editor.
            if(measure||measuredWidth!=Width||measuredHeight!=Height){
                editor.SetBounds(inset,inset,Math.Max(1,Width-2*inset),Math.Max(1,Height-2*inset));
                bool overflow=LineCount>visible;
                if(overflow)editor.Width=Math.Max(1,Width-2*inset-barWidth);
                bar.Visible=overflow;bar.SetBounds(Width-barWidth-1,1,barWidth,Height-2);
                measure=false;measuredWidth=Width;measuredHeight=Height;
            }
            int lines=LineCount;
            bar.SetRange(lines,visible,FirstLine);
        }finally{layoutBusy=false;}
    }
}
public sealed class TextScrollBar : Control {
    public Action<int> PositionChanged;
    int total=1,page=1,value,dragOffset;bool dragging,hover;
    int Maximum{get{return Math.Max(0,total-page);}}
    Rectangle Thumb{
        get{int height=Math.Min(Height-8,Math.Max(28,(int)((long)Math.Max(1,Height-8)*page/Math.Max(1,total))));
            int travel=Math.Max(0,Height-8-height), y=4+(Maximum==0 ? 0 : (int)((long)travel*Math.Min(value,Maximum)/Maximum));
            return new Rectangle(4,y,Math.Max(3,Width-8),Math.Max(1,height));}
    }
    public TextScrollBar(){SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer,true);TabStop=false;Cursor=Cursors.Hand;}
    public void SetRange(int lines,int visible,int first){total=Math.Max(1,lines);page=Math.Max(1,visible);value=Math.Max(0,Math.Min(Maximum,first));Invalidate();}
    protected override void OnPaint(PaintEventArgs e){
        e.Graphics.Clear(Theme.Surface);e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;
        Rectangle r=Thumb;using(var b=new SolidBrush(dragging||hover ? Theme.Accent : Theme.Muted)){
            using(var path=new GraphicsPath()){int d=Math.Min(r.Width,8);path.AddArc(r.Left,r.Top,d,d,180,90);path.AddArc(r.Right-d,r.Top,d,d,270,90);path.AddArc(r.Right-d,r.Bottom-d,d,d,0,90);path.AddArc(r.Left,r.Bottom-d,d,d,90,90);path.CloseFigure();e.Graphics.FillPath(b,path);}
        }
    }
    void Change(int position){if(PositionChanged!=null)PositionChanged(Math.Max(0,Math.Min(Maximum,position)));}
    protected override void OnMouseDown(MouseEventArgs e){base.OnMouseDown(e);if(e.Button!=MouseButtons.Left)return;
        if(Thumb.Contains(e.Location)){dragging=true;dragOffset=e.Y-Thumb.Top;Capture=true;Invalidate();}
        else Change(value+(e.Y<Thumb.Top ? -page : page));
    }
    protected override void OnMouseMove(MouseEventArgs e){base.OnMouseMove(e);hover=Thumb.Contains(e.Location);
        if(dragging){int travel=Math.Max(1,Height-8-Thumb.Height);Change((int)Math.Round((double)(e.Y-dragOffset-4)*Maximum/travel));}Invalidate();
    }
    protected override void OnMouseUp(MouseEventArgs e){base.OnMouseUp(e);dragging=false;Capture=false;Invalidate();}
    protected override void OnMouseCaptureChanged(EventArgs e){base.OnMouseCaptureChanged(e);if(!Capture){dragging=false;Invalidate();}}
    protected override void OnMouseLeave(EventArgs e){base.OnMouseLeave(e);hover=false;Invalidate();}
    protected override void OnMouseWheel(MouseEventArgs e){base.OnMouseWheel(e);int n=SystemInformation.MouseWheelScrollLines;Change(value-e.Delta/120*(n<0 ? page : n));}
}
public sealed class SetupTabs : UserControl {
    sealed class HeaderButton : Button {
        public bool Selected;
        public HeaderButton(){FlatStyle=FlatStyle.Flat;SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer,true);}
        protected override void OnPaint(PaintEventArgs e){e.Graphics.Clear(Selected ? Theme.Surface : Theme.Background);
            TextRenderer.DrawText(e.Graphics,Text,Font,ClientRectangle,Selected ? Theme.Accent : Theme.Text,TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter);
            if(Selected)using(var p=new Pen(Theme.Accent,2))e.Graphics.DrawLine(p,4,Height-2,Width-4,Height-2);
            if(Focused&&ShowFocusCues)ControlPaint.DrawFocusRectangle(e.Graphics,Rectangle.Inflate(ClientRectangle,-4,-4),Theme.Accent,Theme.Background);
        }
    }
    readonly FlowLayoutPanel headers=new FlowLayoutPanel{Dock=DockStyle.Top,Height=44,Padding=new Padding(5,4,5,4),WrapContents=false};
    readonly Panel content=new Panel{Dock=DockStyle.Fill,Padding=new Padding(1)};
    readonly System.Collections.Generic.List<HeaderButton> buttons=new System.Collections.Generic.List<HeaderButton>();
    readonly System.Collections.Generic.List<Control> pages=new System.Collections.Generic.List<Control>();
    public SetupTabs(){Controls.Add(content);Controls.Add(headers);}
    public void AddPage(string caption,Control body){
        int index=pages.Count;body.Dock=DockStyle.Fill;content.Controls.Add(body);pages.Add(body);
        var button=new HeaderButton{Text=caption,Font=Font,Margin=new Padding(0),Size=new Size(TextRenderer.MeasureText(caption,Font).Width+24,36)};
        button.Click+=delegate{SelectPage(index);};headers.Controls.Add(button);buttons.Add(button);SelectPage(0);
    }
    void SelectPage(int index){for(int i=0;i<pages.Count;i++){pages[i].Visible=i==index;buttons[i].Selected=i==index;buttons[i].Invalidate();}}
}
}

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace QuietReader {
    // iOS-like look for the dialogs and menus. The process is system-DPI aware, so every pixel size goes through S().
    public static class Ui {
        public static readonly float Scale;
        static Ui() { using(Graphics g=Graphics.FromHwnd(IntPtr.Zero)) Scale=Math.Max(1f,g.DpiX/96f); }
        public static int S(float v) { return (int)Math.Round(v*Scale); }
        public static readonly Color Background=Color.FromArgb(242,242,247), Card=Color.White, Separator=Color.FromArgb(220,220,225),
            Accent=Color.FromArgb(0,122,255), Text=Color.FromArgb(28,28,30), Secondary=Color.FromArgb(138,138,142),
            Fill=Color.FromArgb(227,227,232), Pressed=Color.FromArgb(229,229,234), Hover=Color.FromArgb(244,244,247), Green=Color.FromArgb(52,199,89);
        public const string FontName="Microsoft YaHei UI";
        public static Font Font(float size) { return new Font(FontName,size); }
        public static Font Bold(float size) { return new Font(FontName,size,FontStyle.Bold); }
        public const TextFormatFlags Line=TextFormatFlags.NoPrefix|TextFormatFlags.SingleLine|TextFormatFlags.VerticalCenter|TextFormatFlags.EndEllipsis|TextFormatFlags.NoPadding;
        public static GraphicsPath Round(RectangleF r,float radius) {
            GraphicsPath p=new GraphicsPath();float d=Math.Min(radius*2,Math.Min(r.Width,r.Height));
            if(d<=0) {p.AddRectangle(r);return p;}
            p.AddArc(r.X,r.Y,d,d,180,90);p.AddArc(r.Right-d,r.Y,d,d,270,90);p.AddArc(r.Right-d,r.Bottom-d,d,d,0,90);p.AddArc(r.X,r.Bottom-d,d,d,90,90);
            p.CloseFigure();return p;
        }
        public static void FillRound(Graphics g,Color c,RectangleF r,float radius) {
            SmoothingMode old=g.SmoothingMode;g.SmoothingMode=SmoothingMode.AntiAlias;
            using(GraphicsPath p=Round(r,radius)) using(SolidBrush b=new SolidBrush(c)) g.FillPath(b,p);
            g.SmoothingMode=old;
        }
        public static void Chevron(Graphics g,Color c,int x,int y,int size) {
            SmoothingMode old=g.SmoothingMode;g.SmoothingMode=SmoothingMode.AntiAlias;
            using(Pen p=new Pen(c,Math.Max(1.5f,Scale*1.6f)) {StartCap=LineCap.Round,EndCap=LineCap.Round,LineJoin=LineJoin.Round})
                g.DrawLines(p,new PointF[]{new PointF(x,y-size),new PointF(x+size,y),new PointF(x,y+size)});
            g.SmoothingMode=old;
        }
        public static void Check(Graphics g,Color c,int x,int y,int size) {
            SmoothingMode old=g.SmoothingMode;g.SmoothingMode=SmoothingMode.AntiAlias;
            using(Pen p=new Pen(c,Math.Max(1.8f,Scale*2f)) {StartCap=LineCap.Round,EndCap=LineCap.Round,LineJoin=LineJoin.Round})
                g.DrawLines(p,new PointF[]{new PointF(x,y),new PointF(x+size*0.38f,y+size*0.4f),new PointF(x+size,y-size*0.5f)});
            g.SmoothingMode=old;
        }
        [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr h,int attribute,ref int value,int size);
        [DllImport("uxtheme.dll",CharSet=CharSet.Unicode)] static extern int SetWindowTheme(IntPtr h,string app,string idList);
        // Windows 11 rounds and shadows the window; older Windows keeps square corners.
        public static void RoundWindow(IntPtr h,bool small) {
            try {int v=small?3:2;DwmSetWindowAttribute(h,33,ref v,4);} catch(DllNotFoundException) { } catch(EntryPointNotFoundException) { }
        }
        public static void ModernScrollbars(Control c) {
            c.HandleCreated+=delegate {try {SetWindowTheme(c.Handle,"Explorer",null);} catch(DllNotFoundException) { } catch(EntryPointNotFoundException) { }};
        }
        [DllImport("user32.dll")] static extern bool SetWindowDisplayAffinity(IntPtr h,uint affinity);
        // When on, every window of the reader (main window, sheets, menus) is left out of screenshots, recordings and screen sharing.
        public static bool HideFromCapture;
        public static bool Protect(IntPtr h) {
            // 0x11 WDA_EXCLUDEFROMCAPTURE (Windows 10 2004+) shows what is behind; older systems get 0x1, a black box.
            uint mode=!HideFromCapture?0u:Environment.OSVersion.Version.Build>=19041?0x11u:0x1u;
            try {return SetWindowDisplayAffinity(h,mode);} catch(EntryPointNotFoundException) {return false;}
        }
        public static void StyleMenu(ToolStripDropDown menu) {
            menu.Renderer=new MenuRenderer();menu.Font=Font(9.5f);menu.Padding=new Padding(S(6),S(6),S(6),S(6));menu.DropShadowEnabled=true;
            menu.HandleCreated+=delegate {RoundWindow(menu.Handle,false);Protect(menu.Handle);};
            menu.ItemAdded+=delegate(object s,ToolStripItemEventArgs e) {
                e.Item.Padding=new Padding(S(4),S(5),S(4),S(5));e.Item.Margin=new Padding(0,0,0,0);
                ToolStripMenuItem item=e.Item as ToolStripMenuItem;
                if(item!=null) {item.ShowShortcutKeys=true;StyleMenu(item.DropDown);}
            };
            ToolStripDropDownMenu dd=menu as ToolStripDropDownMenu;
            if(dd!=null) {dd.ShowImageMargin=false;dd.ShowCheckMargin=true;}
        }
    }
    sealed class MenuRenderer : ToolStripRenderer {
        protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e) { e.Graphics.Clear(Color.FromArgb(250,250,252)); }
        protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e) {
            if(Environment.OSVersion.Version.Build>=22000) return; // DWM draws the border on Windows 11.
            using(Pen p=new Pen(Ui.Separator)) e.Graphics.DrawRectangle(p,0,0,e.ToolStrip.Width-1,e.ToolStrip.Height-1);
        }
        protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e) {
            if(!e.Item.Selected || !e.Item.Enabled) return;
            Ui.FillRound(e.Graphics,Ui.Pressed,new RectangleF(Ui.S(2),0,e.Item.Width-Ui.S(4),e.Item.Height),Ui.S(6));
        }
        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e) {
            ToolStripMenuItem item=e.Item as ToolStripMenuItem;
            bool shortcut=item!=null && !string.IsNullOrEmpty(item.ShortcutKeyDisplayString) && e.Text==item.ShortcutKeyDisplayString;
            e.TextColor=!e.Item.Enabled?Ui.Secondary:shortcut?Ui.Secondary:Ui.Text;
            if(shortcut) using(Font f=Ui.Font(8.5f)) {e.TextFont=f;base.OnRenderItemText(e);return;}
            base.OnRenderItemText(e);
        }
        protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e) {
            Rectangle r=e.ImageRectangle;int size=Ui.S(11);Ui.Check(e.Graphics,Ui.Accent,r.X+(r.Width-size)/2,r.Y+r.Height/2+Ui.S(1),size);
        }
        protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e) {
            int y=e.Item.Height/2;using(Pen p=new Pen(Ui.Separator)) e.Graphics.DrawLine(p,Ui.S(10),y,e.Item.Width-Ui.S(10),y);
        }
        protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e) {
            Rectangle r=e.ArrowRectangle;int size=Ui.S(4);Ui.Chevron(e.Graphics,Ui.Secondary,r.X+r.Width/2-size/2,r.Y+r.Height/2,size);
        }
    }

    // Borderless rounded sheet with a centered title and text buttons, like an iOS navigation bar.
    public class Sheet : Form {
        protected readonly int HeaderHeight=Ui.S(50);
        protected readonly TextButton LeftButton, RightButton;
        [DllImport("user32.dll")] static extern bool ReleaseCapture();
        [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr h,int msg,IntPtr w,IntPtr l);
        public Sheet(string title,string left,string right) {
            Text=title;FormBorderStyle=FormBorderStyle.None;ShowInTaskbar=false;StartPosition=FormStartPosition.CenterParent;
            AutoScaleMode=AutoScaleMode.None;BackColor=Ui.Background;ForeColor=Ui.Text;Font=Ui.Font(10);KeyPreview=true;DoubleBuffered=true;
            Padding=new Padding(0,HeaderHeight,0,0);
            if(left!=null) {LeftButton=new TextButton(left,false);LeftButton.Click+=delegate {DialogResult=DialogResult.Cancel;Close();};Controls.Add(LeftButton);}
            if(right!=null) {RightButton=new TextButton(right,true);Controls.Add(RightButton);}
        }
        protected override CreateParams CreateParams {get {CreateParams cp=base.CreateParams;cp.ClassStyle|=0x20000;return cp;}}
        protected override void OnHandleCreated(EventArgs e) {base.OnHandleCreated(e);Ui.RoundWindow(Handle,false);Ui.Protect(Handle);}
        // Centred on a reader near the screen edge, a tall sheet could open partly off-screen.
        protected override void OnShown(EventArgs e) {
            base.OnShown(e);Rectangle wa=Screen.FromControl(this).WorkingArea;
            Location=new Point(Math.Max(wa.Left,Math.Min(wa.Right-Width,Left)),Math.Max(wa.Top,Math.Min(wa.Bottom-Height,Top)));
        }
        protected override void OnLayout(LayoutEventArgs e) {
            base.OnLayout(e);
            if(LeftButton!=null) LeftButton.Location=new Point(Ui.S(14),(HeaderHeight-LeftButton.Height)/2);
            if(RightButton!=null) RightButton.Location=new Point(ClientSize.Width-Ui.S(14)-RightButton.Width,(HeaderHeight-RightButton.Height)/2);
        }
        protected override void OnPaint(PaintEventArgs e) {
            base.OnPaint(e);
            using(Font f=Ui.Bold(10.5f)) TextRenderer.DrawText(e.Graphics,Text,f,new Rectangle(Ui.S(80),0,ClientSize.Width-Ui.S(160),HeaderHeight),Ui.Text,Ui.Line|TextFormatFlags.HorizontalCenter);
            if(Environment.OSVersion.Version.Build<22000) using(Pen p=new Pen(Ui.Separator)) e.Graphics.DrawRectangle(p,0,0,Width-1,Height-1);
        }
        protected override void OnMouseDown(MouseEventArgs e) {
            base.OnMouseDown(e);
            if(e.Button==MouseButtons.Left && e.Y<HeaderHeight) {ReleaseCapture();SendMessage(Handle,0xA1,(IntPtr)2,IntPtr.Zero);}
        }
        protected override bool ProcessCmdKey(ref Message msg,Keys key) {
            if(key==Keys.Escape) {DialogResult=DialogResult.Cancel;Close();return true;}
            return base.ProcessCmdKey(ref msg,key);
        }
        // Multi-line footer under a card, sized to its text.
        public static Label Note(string text,int left,int top,int width) {
            Font f=Ui.Font(8.5f);int h=TextRenderer.MeasureText(text,f,new Size(Math.Max(1,width-Ui.S(8)),int.MaxValue),TextFormatFlags.WordBreak|TextFormatFlags.NoPrefix).Height+Ui.S(4);
            return new Label {Text=text,Left=left,Top=top,Width=width,Height=h,ForeColor=Ui.Secondary,Font=f,BackColor=Color.Transparent};
        }
        // Grouped-list caption (small grey text above or below a card).
        public static Label Caption(string text,int left,int top,int width) {
            return new Label {Text=text,Left=left,Top=top,Width=width,Height=Ui.S(22),ForeColor=Ui.Secondary,Font=Ui.Font(8.5f),BackColor=Color.Transparent,AutoEllipsis=true};
        }
    }

    public class TextButton : Control {
        readonly bool primary;bool hover,down;
        public TextButton(string text,bool primary) {
            this.primary=primary;Text=text;Font=primary?Ui.Bold(10):Ui.Font(10);Cursor=Cursors.Hand;TabStop=false;
            SetStyle(ControlStyles.SupportsTransparentBackColor|ControlStyles.OptimizedDoubleBuffer|ControlStyles.AllPaintingInWmPaint|ControlStyles.UserPaint,true);
            SetStyle(ControlStyles.Selectable,false);BackColor=Color.Transparent;
            Size s=TextRenderer.MeasureText(text,Font,Size.Empty,TextFormatFlags.NoPrefix);Size=new Size(s.Width+Ui.S(8),Ui.S(32));
        }
        protected override void OnMouseEnter(EventArgs e) {hover=true;Invalidate();base.OnMouseEnter(e);}
        protected override void OnMouseLeave(EventArgs e) {hover=false;down=false;Invalidate();base.OnMouseLeave(e);}
        protected override void OnMouseDown(MouseEventArgs e) {down=true;Invalidate();base.OnMouseDown(e);}
        protected override void OnMouseUp(MouseEventArgs e) {down=false;Invalidate();base.OnMouseUp(e);}
        protected override void OnPaint(PaintEventArgs e) {
            Color c=Enabled?Ui.Accent:Ui.Secondary;if(down) c=Color.FromArgb(110,c);else if(hover) c=Color.FromArgb(190,c);
            TextRenderer.DrawText(e.Graphics,Text,Font,ClientRectangle,c,TextFormatFlags.NoPrefix|TextFormatFlags.VerticalCenter|TextFormatFlags.HorizontalCenter|TextFormatFlags.SingleLine);
        }
    }

    // Rounded grey search field with a magnifier, placeholder and clear button.
    public class SearchField : Control {
        public readonly TextBox Box;
        [DllImport("user32.dll",CharSet=CharSet.Unicode)] static extern IntPtr SendMessage(IntPtr h,int msg,IntPtr w,string l);
        public SearchField(string placeholder) {
            SetStyle(ControlStyles.OptimizedDoubleBuffer|ControlStyles.AllPaintingInWmPaint|ControlStyles.UserPaint|ControlStyles.ResizeRedraw,true);
            Height=Ui.S(36);BackColor=Ui.Background;
            Box=new TextBox {BorderStyle=BorderStyle.None,BackColor=Ui.Fill,ForeColor=Ui.Text,Font=Ui.Font(10)};
            Box.HandleCreated+=delegate {SendMessage(Box.Handle,0x1501,(IntPtr)1,placeholder);};
            Box.TextChanged+=delegate {Invalidate();};Controls.Add(Box);Cursor=Cursors.IBeam;
        }
        bool HasClear {get {return Box.TextLength>0;}}
        Rectangle ClearRect {get {int d=Ui.S(16);return new Rectangle(Width-Ui.S(10)-d,(Height-d)/2,d,d);}}
        protected override void OnLayout(LayoutEventArgs e) {
            base.OnLayout(e);if(Box==null) return;int left=Ui.S(32);
            Box.SetBounds(left,(Height-Box.PreferredHeight)/2+Ui.S(1),Math.Max(10,Width-left-Ui.S(34)),Box.PreferredHeight);
        }
        protected override void OnPaint(PaintEventArgs e) {
            Graphics g=e.Graphics;g.Clear(BackColor);Ui.FillRound(g,Ui.Fill,new RectangleF(0,0,Width-1,Height-1),Ui.S(10));
            g.SmoothingMode=SmoothingMode.AntiAlias;int r=Ui.S(5),cx=Ui.S(15),cy=Height/2-Ui.S(1);
            using(Pen p=new Pen(Ui.Secondary,Math.Max(1.5f,Ui.Scale*1.6f)) {StartCap=LineCap.Round,EndCap=LineCap.Round}) {
                g.DrawEllipse(p,cx-r,cy-r,r*2,r*2);g.DrawLine(p,cx+r*0.72f,cy+r*0.72f,cx+r*1.55f,cy+r*1.55f);
            }
            if(HasClear) {
                Rectangle c=ClearRect;using(SolidBrush b=new SolidBrush(Color.FromArgb(174,174,178))) g.FillEllipse(b,c);
                float k=c.Width*0.28f;using(Pen p=new Pen(Ui.Fill,Math.Max(1.5f,Ui.Scale*1.5f)))
                {g.DrawLine(p,c.Left+k,c.Top+k,c.Right-k,c.Bottom-k);g.DrawLine(p,c.Right-k,c.Top+k,c.Left+k,c.Bottom-k);}
            }
        }
        protected override void OnMouseDown(MouseEventArgs e) {
            base.OnMouseDown(e);
            if(HasClear && ClearRect.Contains(e.Location)) Box.Clear();
            Box.Focus();
        }
    }

    // iOS segmented control.
    public class Segmented : Control {
        readonly string[] items;int selected;
        public event EventHandler Changed;
        public Segmented(params string[] items) {
            this.items=items;Height=Ui.S(32);Font=Ui.Font(9.5f);Cursor=Cursors.Hand;BackColor=Ui.Background;
            SetStyle(ControlStyles.OptimizedDoubleBuffer|ControlStyles.AllPaintingInWmPaint|ControlStyles.UserPaint|ControlStyles.ResizeRedraw,true);SetStyle(ControlStyles.Selectable,false);
        }
        public int Selected {get {return selected;} set {if(value==selected) return;selected=value;Invalidate();if(Changed!=null) Changed(this,EventArgs.Empty);}}
        protected override void OnPaint(PaintEventArgs e) {
            Graphics g=e.Graphics;g.Clear(BackColor);Ui.FillRound(g,Ui.Fill,new RectangleF(0,0,Width-1,Height-1),Ui.S(9));
            float w=(Width-Ui.S(4))/(float)items.Length;
            RectangleF knob=new RectangleF(Ui.S(2)+w*selected,Ui.S(2),w,Height-Ui.S(4)-1);
            Ui.FillRound(g,Color.FromArgb(18,0,0,0),new RectangleF(knob.X,knob.Y+Ui.S(1),knob.Width,knob.Height),Ui.S(7));
            Ui.FillRound(g,Color.White,knob,Ui.S(7));
            for(int i=0;i<items.Length;i++) using(Font f=i==selected?Ui.Bold(9.5f):Ui.Font(9.5f))
                TextRenderer.DrawText(g,items[i],f,Rectangle.Round(new RectangleF(Ui.S(2)+w*i,0,w,Height)),Ui.Text,Ui.Line|TextFormatFlags.HorizontalCenter);
        }
        protected override void OnMouseDown(MouseEventArgs e) {base.OnMouseDown(e);Selected=Math.Max(0,Math.Min(items.Length-1,(int)((e.X-Ui.S(2))/((Width-Ui.S(4))/(float)items.Length))));}
    }

    public class Switch : Control {
        bool on;
        public event EventHandler Changed;
        public Switch() {
            Size=new Size(Ui.S(46),Ui.S(28));Cursor=Cursors.Hand;BackColor=Ui.Card;
            SetStyle(ControlStyles.OptimizedDoubleBuffer|ControlStyles.AllPaintingInWmPaint|ControlStyles.UserPaint,true);SetStyle(ControlStyles.Selectable,false);
        }
        public bool On {get {return on;} set {if(on==value) return;on=value;Invalidate();if(Changed!=null) Changed(this,EventArgs.Empty);}}
        protected override void OnPaint(PaintEventArgs e) {
            Graphics g=e.Graphics;g.Clear(BackColor);float h=Height-1;
            Ui.FillRound(g,on?Ui.Green:Color.FromArgb(225,225,230),new RectangleF(0,0,Width-1,h),h/2);
            float k=h-Ui.S(4),x=on?Width-1-Ui.S(2)-k:Ui.S(2);
            g.SmoothingMode=SmoothingMode.AntiAlias;
            using(SolidBrush s=new SolidBrush(Color.FromArgb(30,0,0,0))) g.FillEllipse(s,x,Ui.S(2)+Ui.S(1),k,k);
            using(SolidBrush b=new SolidBrush(Color.White)) g.FillEllipse(b,x,Ui.S(2),k,k);
        }
        protected override void OnMouseUp(MouseEventArgs e) {base.OnMouseUp(e);if(ClientRectangle.Contains(e.Location)) On=!On;}
    }

    public class Slider : Control {
        int value,min,max;bool dragging;
        public event EventHandler Changed;
        public Slider(int min,int max,int value) {
            this.min=min;this.max=max;this.value=Math.Max(min,Math.Min(max,value));Height=Ui.S(30);Cursor=Cursors.Hand;BackColor=Ui.Card;
            SetStyle(ControlStyles.OptimizedDoubleBuffer|ControlStyles.AllPaintingInWmPaint|ControlStyles.UserPaint|ControlStyles.ResizeRedraw,true);
        }
        public int Value {get {return value;} set {value=Math.Max(min,Math.Min(max,value));if(value==this.value) return;this.value=value;Invalidate();if(Changed!=null) Changed(this,EventArgs.Empty);}}
        int Knob {get {return Height-Ui.S(4);}}
        protected override void OnPaint(PaintEventArgs e) {
            Graphics g=e.Graphics;g.Clear(BackColor);int k=Knob,track=Ui.S(4),y=(Height-track)/2;
            float span=Width-k,x=span*(value-min)/(float)(max-min);
            Ui.FillRound(g,Color.FromArgb(225,225,230),new RectangleF(k/2f,y,span,track),track/2f);
            Ui.FillRound(g,Ui.Accent,new RectangleF(k/2f,y,x,track),track/2f);
            g.SmoothingMode=SmoothingMode.AntiAlias;float ky=(Height-k)/2f;
            using(SolidBrush s=new SolidBrush(Color.FromArgb(34,0,0,0))) g.FillEllipse(s,x,ky+Ui.S(1),k-1,k-1);
            using(SolidBrush b=new SolidBrush(Color.White)) g.FillEllipse(b,x,ky,k-1,k-1);
            using(Pen p=new Pen(Color.FromArgb(20,0,0,0))) g.DrawEllipse(p,x,ky,k-1,k-1);
        }
        void Seek(int px) {Value=min+(int)Math.Round((max-min)*Math.Max(0,Math.Min(1,(px-Knob/2f)/(Width-Knob))));}
        protected override void OnMouseDown(MouseEventArgs e) {base.OnMouseDown(e);dragging=true;Seek(e.X);}
        protected override void OnMouseMove(MouseEventArgs e) {base.OnMouseMove(e);if(dragging) Seek(e.X);}
        protected override void OnMouseUp(MouseEventArgs e) {base.OnMouseUp(e);dragging=false;}
    }

    // Rounded white card holding stacked rows separated by inset hairlines.
    public class Group : Control {
        public Group() {
            SetStyle(ControlStyles.OptimizedDoubleBuffer|ControlStyles.AllPaintingInWmPaint|ControlStyles.UserPaint|ControlStyles.ResizeRedraw,true);
            BackColor=Ui.Background;
        }
        public Row Add(Row row) {row.Top=Controls.Count==0?0:Controls[Controls.Count-1].Bottom;row.Width=Width;row.Anchor=AnchorStyles.Left|AnchorStyles.Right|AnchorStyles.Top;Controls.Add(row);Height=row.Bottom;return row;}
        protected override void OnPaint(PaintEventArgs e) {
            e.Graphics.Clear(BackColor);Ui.FillRound(e.Graphics,Ui.Card,new RectangleF(0,0,Width-1,Height-1),Ui.S(10));
        }
        protected override void OnLayout(LayoutEventArgs e) {
            base.OnLayout(e);int y=0,r=Ui.S(10);
            foreach(Control c in Controls) {c.SetBounds(0,y+(c==Controls[0]?Ui.S(1):0),Width,c.Height);y=c.Bottom;Row row=c as Row;if(row!=null) row.Divider=c!=Controls[Controls.Count-1];}
            // Keep rows inside the rounded corners.
            if(Controls.Count>0) {
                Controls[0].Region=TopOrBottom(Controls[0],r,true,Controls.Count==1);
                if(Controls.Count>1) Controls[Controls.Count-1].Region=TopOrBottom(Controls[Controls.Count-1],r,false,false);
            }
        }
        static Region TopOrBottom(Control c,int r,bool top,bool both) {
            using(GraphicsPath p=new GraphicsPath()) {
                RectangleF b=new RectangleF(0,top?0:-r*2,c.Width,c.Height+(both?0:r*2));
                if(both) b=new RectangleF(0,0,c.Width,c.Height);
                using(GraphicsPath round=Ui.Round(b,r)) return new Region(round);
            }
        }
    }
    public class Row : Control {
        public string Title, Detail;public bool Chevron, Checked, Divider;bool hover,down;
        public bool Clickable;
        public Row(string title) {
            Title=title;Height=Ui.S(44);BackColor=Ui.Card;Font=Ui.Font(10);
            SetStyle(ControlStyles.OptimizedDoubleBuffer|ControlStyles.AllPaintingInWmPaint|ControlStyles.UserPaint|ControlStyles.ResizeRedraw,true);SetStyle(ControlStyles.Selectable,false);
        }
        public Control Accessory {
            set {Controls.Add(value);value.BackColor=Ui.Card;PerformLayout();}
        }
        protected override void OnLayout(LayoutEventArgs e) {
            base.OnLayout(e);
            foreach(Control c in Controls) {
                if(c is Slider) c.SetBounds(Ui.S(12),Ui.S(38),Width-Ui.S(24),Ui.S(32));
                else c.Location=new Point(Width-Ui.S(16)-c.Width,(Height-c.Height)/2);
            }
        }
        protected override void OnMouseEnter(EventArgs e) {hover=true;if(Clickable) Invalidate();base.OnMouseEnter(e);}
        protected override void OnMouseLeave(EventArgs e) {hover=false;down=false;if(Clickable) Invalidate();base.OnMouseLeave(e);}
        protected override void OnMouseDown(MouseEventArgs e) {down=true;if(Clickable) Invalidate();base.OnMouseDown(e);}
        protected override void OnMouseUp(MouseEventArgs e) {down=false;if(Clickable) Invalidate();base.OnMouseUp(e);}
        protected override void OnPaint(PaintEventArgs e) {
            Graphics g=e.Graphics;g.Clear(Clickable && down?Ui.Pressed:Clickable && hover?Ui.Hover:Ui.Card);
            int right=Width-Ui.S(16),titleHeight=Controls.Count>0 && Controls[0] is Slider?Ui.S(40):Height;
            if(Chevron) {Ui.Chevron(g,Color.FromArgb(196,196,199),right-Ui.S(5),Height/2,Ui.S(5));right-=Ui.S(16);}
            if(Checked) {Ui.Check(g,Ui.Accent,right-Ui.S(14),Height/2+Ui.S(1),Ui.S(13));right-=Ui.S(24);}
            foreach(Control c in Controls) if(!(c is Slider)) right=Math.Min(right,c.Left-Ui.S(10));
            int detailWidth=0;
            if(!string.IsNullOrEmpty(Detail)) {
                detailWidth=Math.Min(TextRenderer.MeasureText(Detail,Font,Size.Empty,TextFormatFlags.NoPrefix|TextFormatFlags.NoPadding).Width+Ui.S(2),(right-Ui.S(16))/2);
                TextRenderer.DrawText(g,Detail,Font,new Rectangle(right-detailWidth,0,detailWidth,titleHeight),Ui.Secondary,Ui.Line|TextFormatFlags.Right);
            }
            TextRenderer.DrawText(g,Title,Font,new Rectangle(Ui.S(16),0,right-detailWidth-Ui.S(24),titleHeight),Ui.Text,Ui.Line);
            if(Divider) using(Pen p=new Pen(Ui.Separator)) g.DrawLine(p,Ui.S(16),Height-1,Width,Height-1);
        }
    }

    // Owner-drawn list: one- or two-line rows, hover, check mark, optional highlighted match.
    public class RowList : ListBox {
        public sealed class Item {
            public string Title, Detail;public int Offset;public bool Current;public int MatchStart=-1, MatchLength;
            public override string ToString() {return Title;}
        }
        int hover=-1;
        public string EmptyText="";
        // The native scrollbar is pushed outside the card; a thin iOS-style indicator is drawn instead.
        public int VisibleWidth;
        int ShownWidth {get {return VisibleWidth>0?Math.Min(VisibleWidth,ClientSize.Width):ClientSize.Width;}}
        public event EventHandler Activated;
        public RowList(bool twoLine) {
            DrawMode=DrawMode.OwnerDrawFixed;BorderStyle=BorderStyle.None;IntegralHeight=false;Font=Ui.Font(10);
            BackColor=Ui.Card;ItemHeight=Ui.S(twoLine?58:42); 
            SetStyle(ControlStyles.OptimizedDoubleBuffer|ControlStyles.AllPaintingInWmPaint,true);
        }
        public void SetTwoLine(bool twoLine) {ItemHeight=Ui.S(twoLine?58:42);}
        protected override void OnDrawItem(DrawItemEventArgs e) {
            if(e.Index<0 || e.Index>=Items.Count) return;
            Item it=(Item)Items[e.Index];Graphics g=e.Graphics;Rectangle r=e.Bounds;
            bool selected=(e.State&DrawItemState.Selected)!=0;
            using(SolidBrush b=new SolidBrush(selected?Ui.Pressed:e.Index==hover?Ui.Hover:Ui.Card)) g.FillRectangle(b,r);
            int left=r.X+Ui.S(16),edge=Math.Min(r.Right,ShownWidth),right=edge-Ui.S(16);
            if(it.Current) {Ui.Check(g,Ui.Accent,right-Ui.S(13),r.Y+r.Height/2+Ui.S(1),Ui.S(13));right-=Ui.S(24);}
            Color titleColor=it.Current?Ui.Accent:Ui.Text;
            if(string.IsNullOrEmpty(it.Detail)) TextRenderer.DrawText(g,it.Title,Font,new Rectangle(left,r.Y,right-left,r.Height),titleColor,Ui.Line);
            else {
                Rectangle top=new Rectangle(left,r.Y+Ui.S(7),right-left,Ui.S(24));
                if(it.MatchStart>=0) DrawHighlighted(g,it,top);else TextRenderer.DrawText(g,it.Title,Font,top,titleColor,Ui.Line);
                using(Font small=Ui.Font(8.5f)) TextRenderer.DrawText(g,it.Detail,small,new Rectangle(left,r.Y+Ui.S(31),right-left,Ui.S(20)),Ui.Secondary,Ui.Line);
            }
            using(Pen p=new Pen(Ui.Separator)) g.DrawLine(p,left,r.Bottom-1,edge,r.Bottom-1);
        }
        void DrawHighlighted(Graphics g,Item it,Rectangle box) {
            const TextFormatFlags f=TextFormatFlags.NoPrefix|TextFormatFlags.SingleLine|TextFormatFlags.VerticalCenter|TextFormatFlags.NoPadding;
            string[] parts={it.Title.Substring(0,it.MatchStart),it.Title.Substring(it.MatchStart,it.MatchLength),it.Title.Substring(it.MatchStart+it.MatchLength)};
            int x=box.X;
            using(Font bold=new Font(Font,FontStyle.Bold))
            for(int i=0;i<3 && x<box.Right;i++) {
                if(parts[i].Length==0) continue;Font font=i==1?bold:Font;
                int w=TextRenderer.MeasureText(g,parts[i],font,new Size(int.MaxValue,box.Height),f).Width;
                Rectangle area=new Rectangle(x,box.Y,Math.Min(w,box.Right-x),box.Height);
                if(i==1) Ui.FillRound(g,Color.FromArgb(255,236,153),new RectangleF(area.X-1,area.Y+Ui.S(2),area.Width+2,area.Height-Ui.S(4)),Ui.S(3));
                TextRenderer.DrawText(g,parts[i],font,area,Ui.Text,f|(i==2?TextFormatFlags.EndEllipsis:0));x+=w;
            }
        }
        protected override void OnPaint(PaintEventArgs e) {base.OnPaint(e);}
        protected override void WndProc(ref Message m) {
            base.WndProc(ref m);
            if(m.Msg==0xF && Items.Count==0 && EmptyText.Length>0) using(Graphics g=CreateGraphics()) using(Font f=Ui.Font(9.5f))
                TextRenderer.DrawText(g,EmptyText,f,new Rectangle(0,Ui.S(30),ShownWidth,Ui.S(40)),Ui.Secondary,TextFormatFlags.HorizontalCenter|TextFormatFlags.NoPrefix);
            if(m.Msg==0xF) DrawIndicator();
            if(m.Msg==0x115 || m.Msg==0x20A) Invalidate();
        }
        void DrawIndicator() {
            int total=Items.Count*ItemHeight,h=ClientSize.Height;if(total<=h || h<=0) return;
            int bar=Math.Max(Ui.S(28),h*h/total),y=(int)((long)(h-bar)*TopIndex/Math.Max(1,Items.Count-h/ItemHeight));
            using(Graphics g=CreateGraphics()) Ui.FillRound(g,Color.FromArgb(120,142,142,147),new RectangleF(ShownWidth-Ui.S(6),Math.Min(h-bar,y)+Ui.S(2),Ui.S(3),bar-Ui.S(4)),Ui.S(1.5f));
        }
        protected override void OnSelectedIndexChanged(EventArgs e) {base.OnSelectedIndexChanged(e);Invalidate();}
        protected override void OnMouseMove(MouseEventArgs e) {
            base.OnMouseMove(e);int i=IndexFromPoint(e.Location);
            if(i!=hover) {int old=hover;hover=i;if(old>=0 && old<Items.Count) Invalidate(GetItemRectangle(old));if(i>=0) Invalidate(GetItemRectangle(i));}
        }
        protected override void OnMouseLeave(EventArgs e) {base.OnMouseLeave(e);if(hover>=0 && hover<Items.Count) Invalidate(GetItemRectangle(hover));hover=-1;}
        protected override void OnMouseClick(MouseEventArgs e) {base.OnMouseClick(e);if(e.Button==MouseButtons.Left && IndexFromPoint(e.Location)>=0 && Activated!=null) Activated(this,EventArgs.Empty);}
        protected override void OnKeyDown(KeyEventArgs e) {base.OnKeyDown(e);if(e.KeyCode==Keys.Enter && Activated!=null) {e.Handled=true;Activated(this,EventArgs.Empty);}}
        public void CenterOn(int index) {
            if(index<0) return;SelectedIndex=index;int visible=Math.Max(1,ClientSize.Height/ItemHeight);TopIndex=Math.Max(0,index-visible/2);
        }
    }

    // White rounded card hosting a list; the list sits inside the straight part so the corners stay round.
    public class ListCard : Control {
        public readonly RowList List;
        public ListCard(RowList list) {
            List=list;Controls.Add(list);BackColor=Ui.Background;
            SetStyle(ControlStyles.OptimizedDoubleBuffer|ControlStyles.AllPaintingInWmPaint|ControlStyles.UserPaint|ControlStyles.ResizeRedraw,true);
        }
        protected override void OnLayout(LayoutEventArgs e) {base.OnLayout(e);int r=Ui.S(10);List.VisibleWidth=Width;List.SetBounds(0,r,Width+SystemInformation.VerticalScrollBarWidth,Math.Max(0,Height-2*r));}
        protected override void OnPaint(PaintEventArgs e) {e.Graphics.Clear(BackColor);Ui.FillRound(e.Graphics,Ui.Card,new RectangleF(0,0,Width-1,Height-1),Ui.S(10));}
    }
}

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace QuietReader {
    // Everything a skin needs to draw one frame.
    public sealed class Frame {
        public Graphics G;public Size Client;public Book Book;public Page Page;public Font Font;
        public int Pixel, LineHeight, FirstNumber;
        public bool Hovering, HideText, SaveFailed;
        public string Percent;
        public int Measure(string s) {return TextRenderer.MeasureText(s,Font,new Size(int.MaxValue,int.MaxValue),TextFormatFlags.NoPadding|TextFormatFlags.NoPrefix|TextFormatFlags.SingleLine).Width;}
    }
    // A reading style: where the text goes, how paragraphs are spaced, and what is drawn around it.
    public abstract class Skin {
        public const TextFormatFlags Flags=TextFormatFlags.NoPadding|TextFormatFlags.NoPrefix|TextFormatFlags.SingleLine|TextFormatFlags.PreserveGraphicsClipping;
        public abstract Color Back {get;}
        public abstract Color Fore {get;}
        public abstract Rectangle TextArea(Size client);
        // Line height relative to the note style (editors and spreadsheets use roomier rows).
        public virtual double Spacing {get {return 1.0;}}
        // Disguises stay fully opaque: a see-through "editor" gives itself away.
        public virtual bool Opaque {get {return false;}}
        public virtual int Gap(int lineHeight) {return Math.Max(3,lineHeight/4);}
        public virtual void Chrome(Frame f) { }
        public virtual void Overlay(Frame f) { }
        // Vertical offset of the glyphs inside a line box (used to centre text in Excel rows).
        public virtual int Baseline(Frame f) {return 0;}
        public virtual void Line(Frame f,VisualLine line,int x,int y) {
            TextRenderer.DrawText(f.G,f.Book.Text.Substring(line.Start,line.End-line.Start),f.Font,new Point(x,y),Fore,Flags);
        }
        public static Skin Create(ReaderState s) {
            Color fore=Color.FromArgb(s.TextArgb),back=Color.FromArgb(s.BackArgb);
            if(s.Skin=="Excel") return new ExcelSkin();
            if(s.Skin=="Code" || s.Skin=="CodeLight") return new CodeSkin(s.Skin=="CodeLight");
            if(s.Skin=="Clear") return new ClearSkin(fore,back);
            if(s.Skin=="Line") return new LineSkin(fore,back);
            return new NoteSkin(fore,back);
        }
        protected static void Text(Graphics g,string s,Font font,Rectangle r,Color c,TextFormatFlags extra) {
            TextRenderer.DrawText(g,s,font,r,c,TextFormatFlags.NoPrefix|TextFormatFlags.SingleLine|TextFormatFlags.VerticalCenter|TextFormatFlags.EndEllipsis|extra);
        }
    }

    public class NoteSkin : Skin {
        readonly Color fore,back;
        public NoteSkin(Color fore,Color back) {this.fore=fore;this.back=back;}
        public override Color Back {get {return back;}}
        public override Color Fore {get {return fore;}}
        public override Rectangle TextArea(Size c) {return new Rectangle(Ui.S(18),Ui.S(20),Math.Max(1,c.Width-Ui.S(36)),Math.Max(1,c.Height-Ui.S(40)));}
        Color Muted {get {return Blend(fore,back,0.45);}}
        public static Color Blend(Color a,Color b,double t) {return Color.FromArgb((int)(a.R+(b.R-a.R)*t),(int)(a.G+(b.G-a.G)*t),(int)(a.B+(b.B-a.B)*t));}
        public override void Chrome(Frame f) {
            using(Pen p=new Pen(Blend(back,fore,0.12))) f.G.DrawRectangle(p,0,0,f.Client.Width-1,f.Client.Height-1);
            using(Pen p=new Pen(Blend(back,fore,0.22),Ui.S(2))) f.G.DrawLine(p,f.Client.Width/2-Ui.S(13),Ui.S(8),f.Client.Width/2+Ui.S(13),Ui.S(8));
        }
        public override void Overlay(Frame f) {
            if(!(f.Hovering || f.SaveFailed) || f.Book==null) return;
            // Hover shows where you are: chapter on the left, progress on the right.
            using(Font small=Ui.Font(8)) {
                int h=TextRenderer.MeasureText("国",small).Height,y=f.Client.Height-h-Ui.S(3),w=f.Client.Width;
                int pw=TextRenderer.MeasureText(f.Percent,small,Size.Empty,TextFormatFlags.NoPadding).Width;
                using(SolidBrush bg=new SolidBrush(back)) f.G.FillRectangle(bg,1,y-Ui.S(2),w-2,h+Ui.S(4));
                if(f.SaveFailed) {Text(f.G,"进度未保存 · 请检查本地写入权限",small,new Rectangle(Ui.S(18),y,w-Ui.S(36),h),Color.Firebrick,TextFormatFlags.NoPadding);return;}
                Text(f.G,f.Book.ChapterTitleAt(f.Page==null || f.Page.Lines.Count==0?0:f.Page.Lines[0].Start),small,new Rectangle(Ui.S(18),y,w-Ui.S(48)-pw,h),Muted,TextFormatFlags.NoPadding);
                Text(f.G,f.Percent,small,new Rectangle(w-Ui.S(18)-pw,y,pw,h),Muted,TextFormatFlags.NoPadding|TextFormatFlags.Right);
            }
        }
    }

    // One borderless line: drag anywhere to move, scroll to advance. Taller windows simply show more lines.
    public sealed class LineSkin : NoteSkin {
        public LineSkin(Color fore,Color back):base(fore,back) { }
        public static int Margin {get {return Ui.S(3);}}
        public override Rectangle TextArea(Size c) {return new Rectangle(Ui.S(10),Margin,Math.Max(1,c.Width-Ui.S(20)),Math.Max(1,c.Height-2*Margin));}
        public override int Gap(int lineHeight) {return 0;}
        public override int Baseline(Frame f) {return Math.Max(0,(f.LineHeight-f.Font.Height)/2);}
        public override void Chrome(Frame f) { }
        public override void Overlay(Frame f) {
            if(!f.Hovering || f.Book==null) return;
            using(Font small=Ui.Font(8)) {
                int pw=TextRenderer.MeasureText(f.Percent,small,Size.Empty,TextFormatFlags.NoPadding).Width+Ui.S(16);
                Rectangle r=new Rectangle(f.Client.Width-pw,0,pw,f.Client.Height);
                using(SolidBrush bg=new SolidBrush(Back)) f.G.FillRectangle(bg,r);
                Text(f.G,f.Percent,small,r,Blend(Fore,Back,0.45),TextFormatFlags.HorizontalCenter);
            }
        }
    }

    // Text only: the background is see-through (and click-through) until the mouse comes over the window.
    public sealed class ClearSkin : NoteSkin {
        // Near-white key: anti-aliased glyph edges blend toward it, which looks clean over light windows such as Excel.
        public static readonly Color Key=Color.FromArgb(254,254,253);
        public bool Solid;
        public ClearSkin(Color fore,Color back):base(fore,back) { }
        public override Color Back {get {return Solid?base.Back:Key;}}
        public override void Chrome(Frame f) {if(Solid) base.Chrome(f);}
        public override void Overlay(Frame f) {if(Solid) base.Overlay(f);}
        public override void Line(Frame f,VisualLine line,int x,int y) {
            // ClearType adds coloured fringes against a colour key; grey-scale anti-aliasing does not.
            if(!Solid) f.G.TextRenderingHint=System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
            base.Line(f,line,x,y);
        }
    }

    // Looks like an Excel 2016 worksheet: formula bar, column letters, row numbers, grid and sheet tabs.
    public sealed class ExcelSkin : Skin {
        public override double Spacing {get {return 1.25;}}
        public override bool Opaque {get {return true;}}
        static readonly Color Grid=Color.FromArgb(218,220,224), Header=Color.FromArgb(240,240,240), HeaderText=Color.FromArgb(68,68,68),
            HeaderLine=Color.FromArgb(200,200,200), Green=Color.FromArgb(33,115,70), Selected=Color.FromArgb(210,210,210);
        int Formula {get {return Ui.S(28);}} int Columns {get {return Ui.S(22);}} int Tabs {get {return Ui.S(26);}} int ColumnWidth {get {return Ui.S(76);}}
        // Fixed widths keep the text area (and so the line wrapping) stable while scrolling.
        readonly int rowHeader;
        public ExcelSkin() {using(Font ui=Ui.Font(8.5f)) rowHeader=TextRenderer.MeasureText("88888",ui).Width+Ui.S(10);}
        public override Color Back {get {return Color.White;}}
        public override Color Fore {get {return Color.Black;}}
        public override int Gap(int lineHeight) {return 0;}
        public override Rectangle TextArea(Size c) {
            int top=Formula+Columns;return new Rectangle(rowHeader+Ui.S(4),top,Math.Max(1,c.Width-rowHeader-Ui.S(8)),Math.Max(1,c.Height-top-Tabs));
        }
        public override int Baseline(Frame f) {return Math.Max(0,(f.LineHeight-f.Font.Height)/2);}
        public override void Chrome(Frame f) {
            Graphics g=f.G;int w=f.Client.Width,h=f.Client.Height;
            using(Font ui=Ui.Font(8.5f)) {
                Rectangle ta=TextArea(f.Client);int colTop=Formula,top=ta.Y,bottom=h-Tabs;
                // Formula bar: name box, fx, and the selected cell's text.
                using(Pen line=new Pen(HeaderLine)) {
                    g.DrawLine(line,0,Formula-1,w,Formula-1);
                    int nameW=Ui.S(70);Text(g,"A"+f.FirstNumber,ui,new Rectangle(Ui.S(6),0,nameW-Ui.S(6),Formula),Color.Black,0);
                    g.DrawLine(line,nameW,Ui.S(5),nameW,Formula-Ui.S(5));
                    using(Font fx=new Font(Ui.FontName,8.5f,FontStyle.Italic)) Text(g,"fx",fx,new Rectangle(nameW+Ui.S(10),0,Ui.S(24),Formula),Color.FromArgb(110,110,110),0);
                    g.DrawLine(line,nameW+Ui.S(36),Ui.S(5),nameW+Ui.S(36),Formula-Ui.S(5));
                    if(!f.HideText && f.Page!=null && f.Page.Lines.Count>0) {
                        VisualLine first=f.Page.Lines[0];
                        Text(g,f.Book.Text.Substring(first.Start,first.End-first.Start),ui,new Rectangle(nameW+Ui.S(44),0,w-nameW-Ui.S(50),Formula),Color.Black,0);
                    }
                }
                // Column letters.
                using(SolidBrush hb=new SolidBrush(Header)) g.FillRectangle(hb,0,colTop,w,Columns);
                using(SolidBrush sel=new SolidBrush(Selected)) g.FillRectangle(sel,rowHeader,colTop,ColumnWidth,Columns);
                using(Pen line=new Pen(HeaderLine)) {
                    g.DrawLine(line,0,colTop+Columns-1,w,colTop+Columns-1);
                    for(int c=0,x=rowHeader;x<w;c++,x+=ColumnWidth) {
                        g.DrawLine(line,x,colTop,x,colTop+Columns-1);
                        Text(g,((char)('A'+c%26)).ToString(),ui,new Rectangle(x,colTop,ColumnWidth,Columns),HeaderText,TextFormatFlags.HorizontalCenter);
                    }
                    using(Pen green=new Pen(Green,Ui.S(2))) g.DrawLine(green,rowHeader,colTop+Columns-Ui.S(1),rowHeader+ColumnWidth,colTop+Columns-Ui.S(1));
                }
                // Rows scroll with the text; the grid is hidden under overflowing text, as Excel does.
                g.SetClip(new Rectangle(0,top,w,bottom-top));
                using(SolidBrush hb=new SolidBrush(Header)) g.FillRectangle(hb,0,top,rowHeader,bottom-top);
                using(Pen grid=new Pen(Grid)) using(Pen line=new Pen(HeaderLine)) {
                    for(int k=0;;k++) {
                        int y=top-f.Pixel+k*f.LineHeight;if(y>bottom) break;
                        int textEnd=0;
                        if(!f.HideText && f.Page!=null && k<f.Page.Lines.Count) {
                            VisualLine l=f.Page.Lines[k];textEnd=ta.X+f.Measure(f.Book.Text.Substring(l.Start,l.End-l.Start))+Ui.S(4);
                        }
                        for(int x=rowHeader+ColumnWidth;x<w;x+=ColumnWidth) if(x>textEnd) g.DrawLine(grid,x,y,x,y+f.LineHeight);
                        g.DrawLine(grid,rowHeader,y+f.LineHeight-1,w,y+f.LineHeight-1);
                        if(k==0) using(SolidBrush sel=new SolidBrush(Selected)) g.FillRectangle(sel,0,y,rowHeader,f.LineHeight);
                        g.DrawLine(line,0,y+f.LineHeight-1,rowHeader,y+f.LineHeight-1);
                        Text(g,(f.FirstNumber+k).ToString(),ui,new Rectangle(0,y,rowHeader-Ui.S(1),f.LineHeight),HeaderText,TextFormatFlags.HorizontalCenter);
                    }
                    g.DrawLine(line,rowHeader-1,top,rowHeader-1,bottom);
                }
                g.ResetClip();
            }
        }
        public override void Overlay(Frame f) {
            Graphics g=f.G;int w=f.Client.Width,h=f.Client.Height;Rectangle ta=TextArea(f.Client);
            // Selected cell A{first row}: green frame on the first visible row.
            g.SetClip(new Rectangle(0,ta.Y,w,h-ta.Y-Tabs));
            using(Pen green=new Pen(Green,Ui.S(2))) g.DrawRectangle(green,rowHeader+Ui.S(1),ta.Y-f.Pixel+Ui.S(1),ColumnWidth-Ui.S(2),f.LineHeight-Ui.S(2));
            g.ResetClip();
            int y=h-Tabs;
            using(SolidBrush bg=new SolidBrush(Header)) g.FillRectangle(bg,0,y,w,Tabs);
            using(Pen line=new Pen(HeaderLine)) g.DrawLine(line,0,y,w,y);
            using(Font ui=Ui.Font(8.5f)) using(Font bold=new Font(Ui.FontName,8.5f,FontStyle.Bold)) {
                Text(g,"◀  ▶",ui,new Rectangle(Ui.S(8),y,Ui.S(40),Tabs),Color.FromArgb(150,150,150),0);
                int x=Ui.S(54),tw=Ui.S(70);
                using(SolidBrush white=new SolidBrush(Color.White)) g.FillRectangle(white,x,y,tw,Tabs-Ui.S(3));
                Text(g,"Sheet1",bold,new Rectangle(x,y,tw,Tabs-Ui.S(2)),Green,TextFormatFlags.HorizontalCenter);
                using(Pen green=new Pen(Green,Ui.S(2))) g.DrawLine(green,x+Ui.S(10),y+Tabs-Ui.S(4),x+tw-Ui.S(10),y+Tabs-Ui.S(4));
                Text(g,"Sheet2",ui,new Rectangle(x+tw,y,tw,Tabs),HeaderText,TextFormatFlags.HorizontalCenter);
                Text(g,"⊕",ui,new Rectangle(x+tw*2,y,Ui.S(24),Tabs),Color.FromArgb(120,120,120),TextFormatFlags.HorizontalCenter);
                if(f.Hovering && f.Book!=null) Text(g,f.Percent,ui,new Rectangle(w-Ui.S(90),y,Ui.S(80),Tabs),HeaderText,TextFormatFlags.Right);
            }
        }
    }

    // Looks like VS Code (Dark+ or Light+): tabs, breadcrumbs, line numbers, light syntax colouring and the blue status bar.
    public sealed class CodeSkin : Skin {
        readonly Color Bg,Plain,Str,Func,Keyword,Number,Current,TabBar,OtherTab,TabText,OtherTabText,Crumb,CurrentLine,Guide,Tag,OtherTag;
        int Tabs {get {return Ui.S(34);}} int Crumbs {get {return Ui.S(22);}} int Status {get {return Ui.S(22);}}
        readonly int gutter;
        public CodeSkin(bool light) {
            using(Font n=Ui.Font(9)) gutter=TextRenderer.MeasureText("88888",n).Width+Ui.S(24);
            if(light) {
                Bg=Color.White;Plain=Color.FromArgb(36,36,36);Str=Color.FromArgb(163,21,21);Func=Color.FromArgb(121,94,38);Keyword=Color.FromArgb(0,0,255);
                Number=Color.FromArgb(35,120,147);Current=Color.FromArgb(11,33,107);TabBar=Color.FromArgb(243,243,243);OtherTab=Color.FromArgb(236,236,236);
                TabText=Color.FromArgb(51,51,51);OtherTabText=Color.FromArgb(120,120,120);Crumb=Color.FromArgb(97,97,97);CurrentLine=Color.FromArgb(238,238,238);
                Guide=Color.FromArgb(211,211,211);Tag=Color.FromArgb(104,33,122);OtherTag=Color.FromArgb(150,110,165);
            } else {
                Bg=Color.FromArgb(30,30,30);Plain=Color.FromArgb(212,212,212);Str=Color.FromArgb(206,145,120);Func=Color.FromArgb(220,220,170);Keyword=Color.FromArgb(86,156,214);
                Number=Color.FromArgb(133,133,133);Current=Color.FromArgb(198,198,198);TabBar=Color.FromArgb(37,37,38);OtherTab=Color.FromArgb(45,45,45);
                TabText=Color.White;OtherTabText=Color.FromArgb(150,150,150);Crumb=Color.FromArgb(169,169,169);CurrentLine=Color.FromArgb(40,40,40);
                Guide=Color.FromArgb(64,64,64);Tag=Color.FromArgb(160,116,196);OtherTag=Color.FromArgb(120,90,150);
            }
        }
        public override double Spacing {get {return 1.4;}}
        public override bool Opaque {get {return true;}}
        public override int Baseline(Frame f) {return Math.Max(0,(f.LineHeight-f.Font.Height)/2);}
        public override Color Back {get {return Bg;}}
        public override Color Fore {get {return Plain;}}
        public override int Gap(int lineHeight) {return 0;}
        public override Rectangle TextArea(Size c) {
            // The fake indentation shrinks in narrow windows so the text keeps its width.
            int top=Tabs+Crumbs+Ui.S(4),left=gutter+(c.Width<Ui.S(380)?Ui.S(12):Ui.S(30));
            return new Rectangle(left,top,Math.Max(1,c.Width-left-Ui.S(16)),Math.Max(1,c.Height-top-Status));
        }
        public override void Chrome(Frame f) {
            Graphics g=f.G;int w=f.Client.Width,h=f.Client.Height;
            using(Font ui=Ui.Font(8.5f)) {
                using(SolidBrush b=new SolidBrush(TabBar)) g.FillRectangle(b,0,0,w,Tabs);
                int tw=Ui.S(150);
                using(SolidBrush b=new SolidBrush(Bg)) g.FillRectangle(b,0,0,tw,Tabs);
                using(SolidBrush b=new SolidBrush(OtherTab)) g.FillRectangle(b,tw,0,tw-Ui.S(16),Tabs);
                using(Font tag=new Font(Ui.FontName,7f,FontStyle.Bold)) {
                    Text(g,"C#",tag,new Rectangle(Ui.S(12),0,Ui.S(22),Tabs),Tag,0);
                    Text(g,"C#",tag,new Rectangle(tw+Ui.S(12),0,Ui.S(22),Tabs),OtherTag,0);
                }
                Text(g,"NovelParser.cs",ui,new Rectangle(Ui.S(36),0,tw-Ui.S(60),Tabs),TabText,0);
                Text(g,"×",ui,new Rectangle(tw-Ui.S(24),0,Ui.S(16),Tabs),OtherTabText,TextFormatFlags.HorizontalCenter);
                Text(g,"Program.cs",ui,new Rectangle(tw+Ui.S(36),0,tw-Ui.S(60),Tabs),OtherTabText,0);
                Text(g,"src  ›  Services  ›  NovelParser.cs  ›  ParseChapter",ui,new Rectangle(Ui.S(16),Tabs,w-Ui.S(32),Crumbs),Crumb,0);
                Rectangle ta=TextArea(f.Client);
                g.SetClip(new Rectangle(0,ta.Y,w,ta.Height));
                // Current-line frame and indent guide, as in the editor.
                using(Pen cur=new Pen(CurrentLine,Ui.S(2))) g.DrawRectangle(cur,gutter,ta.Y-f.Pixel,w-gutter-Ui.S(2),f.LineHeight);
                using(Pen guide=new Pen(Guide)) if(ta.X-gutter>Ui.S(20)) g.DrawLine(guide,ta.X-Ui.S(16),ta.Y,ta.X-Ui.S(16),ta.Bottom);
                if(f.Page!=null) {
                    for(int i=0;i<f.Page.Lines.Count;i++) {
                        VisualLine l=f.Page.Lines[i];int y=ta.Y+l.Y-f.Pixel;if(y>ta.Bottom) break;
                        if(f.HideText && i>0) break;
                        if(!f.Book.IsParagraphStart(l.Start) && !f.HideText) continue;
                        string n=f.HideText?"1":(f.Book.ParagraphIndexAt(l.Start)+1).ToString();
                        using(Font nf=Ui.Font(9)) TextRenderer.DrawText(g,n,nf,new Rectangle(0,y+Baseline(f),gutter-Ui.S(10),f.Font.Height),i==0?Current:Number,Flags|TextFormatFlags.Right|TextFormatFlags.Bottom);
                    }
                }
                g.ResetClip();
            }
        }
        public override void Overlay(Frame f) {
            Graphics g=f.G;int w=f.Client.Width,y=f.Client.Height-Status;
            using(SolidBrush b=new SolidBrush(Color.FromArgb(0,122,204))) g.FillRectangle(b,0,y,w,Status);
            using(Font ui=Ui.Font(8f)) {
                int ln=f.Book==null || f.Page==null || f.Page.Lines.Count==0 || f.HideText?1:f.Book.ParagraphIndexAt(f.Page.Lines[0].Start)+1;
                // Narrow windows drop the less important items instead of overlapping them.
                string[] right={"Ln "+ln+", Col 1    Spaces: 4    UTF-8    CRLF    C#","Ln "+ln+", Col 1    UTF-8    C#","Ln "+ln+", Col 1    C#","Ln "+ln};
                string[] left={"⎇ main    ⊗ 0  ⚠ 0","⎇ main",""};
                int li=0,ri=0,pad=Ui.S(10),gap=Ui.S(24);
                while(Width(left[li],ui)+Width(right[ri],ui)+2*pad+gap>w) {if(li<left.Length-1) li++;else if(ri<right.Length-1) ri++;else break;}
                Text(g,left[li],ui,new Rectangle(pad,y,w-2*pad,Status),Color.White,0);
                Text(g,right[ri],ui,new Rectangle(pad,y,w-2*pad,Status),Color.White,TextFormatFlags.Right);
            }
        }
        // Dialogue reads as string literals, 【system text】 as calls, chapter titles as keywords.
        public override void Line(Frame f,VisualLine line,int x,int y) {
            string text=f.Book.Text;int start=line.Start,end=line.End;
            int para=start==0?0:text.LastIndexOf('\n',start-1)+1;
            int ci=f.Book.ChapterIndexAt(start);
            if(ci>=0 && f.Book.Chapters[ci].Offset==para) {Draw(f,text.Substring(start,end-start),x,y,Keyword);return;}
            int quote=0,bracket=0;
            for(int i=para;i<start;i++) Track(text[i],ref quote,ref bracket);
            // Opening marks take the new colour, closing marks keep the old one; equal colours are drawn as one run.
            int runStart=start;Color runColor=Color.Empty;
            for(int i=start;i<end;i++) {
                int q=quote,b=bracket;Track(text[i],ref q,ref b);
                Color c=(q>quote || b>bracket)?ColorFor(q,b):ColorFor(quote,bracket);quote=q;bracket=b;
                if(i==start) runColor=c;
                else if(c!=runColor) {x=Draw(f,text.Substring(runStart,i-runStart),x,y,runColor);runStart=i;runColor=c;}
            }
            if(end>start) Draw(f,text.Substring(runStart,end-runStart),x,y,runColor);
        }
        static int Width(string s,Font f) {return s.Length==0?0:TextRenderer.MeasureText(s,f,Size.Empty,TextFormatFlags.NoPadding|TextFormatFlags.NoPrefix).Width;}
        static void Track(char c,ref int quote,ref int bracket) {
            if(c=='“' || c=='「') quote++;else if((c=='”' || c=='」') && quote>0) quote--;
            else if(c=='【') bracket++;else if(c=='】' && bracket>0) bracket--;
        }
        Color ColorFor(int quote,int bracket) {return quote>0?Str:bracket>0?Func:Plain;}
        static int Draw(Frame f,string s,int x,int y,Color c) {
            if(s.Length==0) return x;
            TextRenderer.DrawText(f.G,s,f.Font,new Point(x,y),c,Flags);return x+f.Measure(s);
        }
    }
}

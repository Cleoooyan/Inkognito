using System;
using System.Collections.Generic;
using System.Drawing;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace QuietReader {
    // Chapter list and full-text search share one sheet, switched by a segmented control.
    public class ChapterSheet : Sheet {
        public const int SearchLimit=300;
        static string lastQuery="";
        readonly Book book;
        readonly int current;
        readonly Segmented tabs;
        readonly SearchField search;
        readonly Label count;
        readonly ListCard card;
        readonly RowList list;
        readonly Timer debounce=new Timer {Interval=220};
        string chapterQuery="";
        public int SelectedOffset {get;private set;}
        public ChapterSheet(Book book,int current,bool searchMode):base("目录",null,"完成") {
            this.book=book;this.current=current;
            ClientSize=new Size(Ui.S(400),Ui.S(580));MinimumSize=new Size(Ui.S(320),Ui.S(360));
            RightButton.Click+=delegate {DialogResult=DialogResult.Cancel;Close();};
            tabs=new Segmented("章节目录","全文搜索");search=new SearchField("");count=Caption("",0,0,10);
            list=new RowList(false);card=new ListCard(list);
            Controls.Add(tabs);Controls.Add(search);Controls.Add(count);Controls.Add(card);
            list.Activated+=delegate {Choose();};
            search.Box.TextChanged+=delegate {if(tabs.Selected==0) FillChapters();else {debounce.Stop();debounce.Start();}};
            debounce.Tick+=delegate {debounce.Stop();FillSearch();};
            search.Box.KeyDown+=delegate(object s,KeyEventArgs e) {
                if(e.KeyCode==Keys.Down || e.KeyCode==Keys.Up) {
                    if(list.Items.Count>0) list.SelectedIndex=Math.Max(0,Math.Min(list.Items.Count-1,list.SelectedIndex+(e.KeyCode==Keys.Down?1:-1)));
                    e.Handled=true;
                } else if(e.KeyCode==Keys.Enter) {
                    e.Handled=true;e.SuppressKeyPress=true;
                    if(tabs.Selected==1 && debounce.Enabled) {debounce.Stop();FillSearch();} else Choose();
                }
            };
            tabs.Changed+=delegate {Switch();};
            if(searchMode) tabs.Selected=1;else Switch();
        }
        protected override void OnShown(EventArgs e) {base.OnShown(e);search.Box.Focus();if(tabs.Selected==0) list.CenterOn(list.SelectedIndex);}
        protected override void OnLayout(LayoutEventArgs e) {
            base.OnLayout(e);if(tabs==null) return;
            int m=Ui.S(16),w=ClientSize.Width-2*m,y=HeaderHeight;
            tabs.SetBounds(m,y,w,tabs.Height);y=tabs.Bottom+Ui.S(10);
            search.SetBounds(m,y,w,search.Height);y=search.Bottom+Ui.S(6);
            count.Visible=tabs.Selected==1;
            if(count.Visible) {count.SetBounds(m+Ui.S(4),y,w,Ui.S(22));y=count.Bottom+Ui.S(2);} else y+=Ui.S(6);
            card.SetBounds(m,y,w,ClientSize.Height-y-m);
        }
        void Switch() {
            bool find=tabs.Selected==1;debounce.Stop();
            SetTitle(find?"全文搜索":"目录");
            search.Box.Text="";SendCue(find?"搜索人名、关键词…":"搜索章号或篇名");
            list.SetTwoLine(find);PerformLayout();
            if(find) {search.Box.Text=lastQuery;FillSearch();} else {search.Box.Text=chapterQuery;FillChapters();list.CenterOn(list.SelectedIndex);}
            search.Box.SelectAll();search.Box.Focus();
        }
        void SetTitle(string t) {Text=t;Invalidate(new Rectangle(0,0,ClientSize.Width,HeaderHeight));}
        [System.Runtime.InteropServices.DllImport("user32.dll",CharSet=System.Runtime.InteropServices.CharSet.Unicode)] static extern IntPtr SendMessage(IntPtr h,int msg,IntPtr w,string l);
        void SendCue(string text) {if(search.Box.IsHandleCreated) SendMessage(search.Box.Handle,0x1501,(IntPtr)1,text);else search.Box.HandleCreated+=delegate {SendMessage(search.Box.Handle,0x1501,(IntPtr)1,text);};}
        void FillChapters() {
            string q=search.Box.Text.Trim();chapterQuery=q;list.BeginUpdate();list.Items.Clear();
            int at=book.ChapterIndexAt(current),select=-1;
            if(q.Length==0 || "书籍信息 / 简介".Contains(q)) {list.Items.Add(new RowList.Item {Title="书籍信息 / 简介",Offset=0,Current=at<0});if(at<0) select=0;}
            int n;bool numeric=int.TryParse(q,out n);
            for(int i=0;i<book.Chapters.Count;i++) {
                Chapter c=book.Chapters[i];
                if(q.Length>0 && !(numeric?c.Title.StartsWith("第"+n+"章") || Regex.IsMatch(c.Title,"^"+n+@"\s*[、.．]"):c.Title.IndexOf(q,StringComparison.OrdinalIgnoreCase)>=0)) continue;
                if(i==at) select=list.Items.Count;
                list.Items.Add(new RowList.Item {Title=c.Title,Offset=c.Offset,Current=i==at});
            }
            list.EmptyText="没有匹配的章节";
            if(list.Items.Count>0) list.SelectedIndex=q.Length==0 && select>=0?select:0;
            list.EndUpdate();
        }
        void FillSearch() {
            string q=search.Box.Text.Trim();lastQuery=q;list.BeginUpdate();list.Items.Clear();bool more=false;
            if(q.Length>0) foreach(SearchHit h in book.Search(q,SearchLimit,out more))
                list.Items.Add(new RowList.Item {Title=h.Snippet,Detail=h.Chapter,Offset=h.Offset,MatchStart=h.MatchStart,MatchLength=h.MatchLength});
            list.EmptyText=q.Length==0?"输入文字后在全书中查找":"全书没有找到“"+q+"”";
            if(list.Items.Count>0) list.SelectedIndex=0;list.EndUpdate();
            count.Text=q.Length==0?"":more?"超过 "+SearchLimit+" 处，只列出前 "+SearchLimit+" 处":"找到 "+list.Items.Count+" 处";
        }
        void Choose() {RowList.Item it=list.SelectedItem as RowList.Item;if(it!=null) {SelectedOffset=it.Offset;DialogResult=DialogResult.OK;Close();}}
        protected override void Dispose(bool disposing) {if(disposing) debounce.Dispose();base.Dispose(disposing);}
    }

    public class EncodingSheet : Sheet {
        static readonly string[] Names={"auto","utf-8","gb18030","utf-16","utf-16BE","big5"};
        static readonly string[] Labels={"自动检测（推荐）","UTF-8","GB18030 / GBK","UTF-16 LE","UTF-16 BE","Big5（繁体）"};
        public string EncodingName {get;private set;}
        public EncodingSheet(string current):base("文本编码","取消",null) {
            EncodingName=current;int m=Ui.S(16),w=Ui.S(340);
            Label tip=Caption("显示乱码时，换一种编码重新打开",m+Ui.S(4),HeaderHeight,w-Ui.S(8));Controls.Add(tip);
            Group g=new Group {Left=m,Top=tip.Bottom+Ui.S(2),Width=w-2*m};Controls.Add(g);
            for(int i=0;i<Names.Length;i++) {
                string name=Names[i];Row r=g.Add(new Row(Labels[i]) {Checked=string.Equals(name,current,StringComparison.OrdinalIgnoreCase),Clickable=true});
                r.Click+=delegate {EncodingName=name;DialogResult=DialogResult.OK;Close();};
            }
            ClientSize=new Size(w,g.Bottom+m);
        }
    }

    public class SettingsSheet : Sheet {
        public string FontName {get;private set;}
        public float FontSize {get;private set;}
        public int Alpha {get {return alpha.Value;}}
        public bool Pin {get {return pin.On;}}
        public bool HideOnLeave {get {return away.On;}}
        public bool HideFromCapture {get {return capture.On;}}
        public int AutoSpeed {get {return speed.Value;}}
        public string Hotkey {get;private set;}
        public string Skin {get;private set;}
        public int TextArgb {get;private set;}
        public int BackArgb {get;private set;}
        readonly Slider alpha, speed;
        readonly Switch pin, away, capture;
        // Colour presets for the note and clear styles: name, text, background.
        static readonly object[,] Palettes={{"浅灰",0x484B48,0xF4F4F1},{"纯白",0x282828,0xFFFFFF},{"纸张",0x4A3C2A,0xF6EFE0},{"护眼绿",0x324632,0xDCECD7},{"深色",0xC8C8C4,0x222326}};
        public static string HotkeyLabel(string option) {
            return option=="Grave1"?"` + 1（按住 ` 再按 1）":option=="WinGrave"?"Win + `":option=="TabQ"?"Tab + Q（按住 Tab 再按 Q）":ReaderState.TextFor(option);
        }
        public SettingsSheet(ReaderState state):base("设置","取消","完成") {
            FontName=state.FontName;FontSize=state.FontSize;Hotkey=state.Hotkey;Skin=state.Skin;TextArgb=state.TextArgb;BackArgb=state.BackArgb;
            RightButton.Click+=delegate {DialogResult=DialogResult.OK;Close();};
            int m=Ui.S(16),w=Ui.S(380),gw=w-2*m;
            Label c1=Caption("阅读",m+Ui.S(4),HeaderHeight,gw);Controls.Add(c1);
            Group g1=new Group {Left=m,Top=c1.Bottom,Width=gw};Controls.Add(g1);
            Row font=g1.Add(new Row("字体") {Detail=FontText(),Chevron=true,Clickable=true});
            font.Click+=delegate {
                using(FontDialog d=new FontDialog {Font=new Font(FontName,FontSize),MinSize=8,MaxSize=32,ShowEffects=false,FontMustExist=true})
                    if(d.ShowDialog(this)==DialogResult.OK) {FontName=d.Font.Name;FontSize=d.Font.SizeInPoints;font.Detail=FontText();font.Invalidate();}
            };
            Row opacity=g1.Add(new Row("窗口透明度") {Detail=state.Opacity+"%",Height=Ui.S(78)});
            alpha=new Slider(30,100,state.Opacity);opacity.Accessory=alpha;
            alpha.Changed+=delegate {opacity.Detail=alpha.Value+"%";opacity.Invalidate();};
            Row auto=g1.Add(new Row("自动滚动速度") {Detail=state.AutoSpeed+" 档",Height=Ui.S(78)});
            speed=new Slider(1,10,state.AutoSpeed);auto.Accessory=speed;
            speed.Changed+=delegate {auto.Detail=speed.Value+" 档";auto.Invalidate();};

            Label c2=Caption("外观",m+Ui.S(4),g1.Bottom+Ui.S(14),gw);Controls.Add(c2);
            Group g2=new Group {Left=m,Top=c2.Bottom,Width=gw};Controls.Add(g2);
            Row style=g2.Add(new Row("样式") {Detail=ReaderState.SkinName(Skin),Chevron=true,Clickable=true});
            Row colors=g2.Add(new Row("配色") {Detail=PaletteName(),Chevron=true,Clickable=true});
            style.Click+=delegate {
                string[] labels=new string[ReaderState.Skins.Length];for(int i=0;i<labels.Length;i++) labels[i]=ReaderState.SkinName(ReaderState.Skins[i]);
                Pick(style,labels,Array.IndexOf(ReaderState.Skins,Skin),delegate(int i) {Skin=ReaderState.Skins[i];style.Detail=labels[i];colors.Detail=PaletteName();});
            };
            colors.Click+=delegate {
                int n=Palettes.GetLength(0);string[] labels=new string[n+2];int current=-1;
                for(int i=0;i<n;i++) {labels[i]=(string)Palettes[i,0];if(Same(i)) current=i;}
                labels[n]="自定义文字颜色…";labels[n+1]="自定义背景颜色…";
                Pick(colors,labels,current,delegate(int i) {
                    if(i<n) {TextArgb=unchecked((int)0xFF000000)|(int)Palettes[i,1];BackArgb=unchecked((int)0xFF000000)|(int)Palettes[i,2];}
                    else using(ColorDialog d=new ColorDialog {Color=Color.FromArgb(i==n?TextArgb:BackArgb),FullOpen=true})
                        if(d.ShowDialog(this)==DialogResult.OK) {if(i==n) TextArgb=d.Color.ToArgb();else BackArgb=d.Color.ToArgb();}
                    colors.Detail=PaletteName();colors.Invalidate();
                });
            };
            Row top=g2.Add(new Row("窗口置顶"));pin=new Switch {On=state.Topmost};top.Accessory=pin;
            Label n2=Note("Excel、代码编辑器用自己的配色，并且始终不透明。",m+Ui.S(4),g2.Bottom+Ui.S(6),gw-Ui.S(8));
            Controls.Add(n2);
            Label c4=Caption("隐蔽",m+Ui.S(4),n2.Bottom+Ui.S(8),gw);Controls.Add(c4);
            Group g4=new Group {Left=m,Top=c4.Bottom,Width=gw};Controls.Add(g4);
            Row awayRow=g4.Add(new Row("鼠标移开隐藏文字"));away=new Switch {On=state.HideOnLeave};awayRow.Accessory=away;
            Row captureRow=g4.Add(new Row("截图和共享屏幕时隐藏"));capture=new Switch {On=state.HideFromCapture};captureRow.Accessory=capture;
            Label n4=Note("开着时，开会共享屏幕、截图、录屏都拍不到阅读器；你自己截图也拍不到。",m+Ui.S(4),g4.Bottom+Ui.S(6),gw-Ui.S(8));
            Controls.Add(n4);

            Label c3=Caption("全局隐藏 / 恢复",m+Ui.S(4),n4.Bottom+Ui.S(8),gw);Controls.Add(c3);
            Group g3=new Group {Left=m,Top=c3.Bottom,Width=gw};Controls.Add(g3);
            Row key=g3.Add(new Row("快捷键") {Detail=ReaderState.TextFor(Hotkey),Chevron=true,Clickable=true});
            key.Click+=delegate {
                string[] labels=new string[ReaderState.HotkeyOptions.Length];for(int i=0;i<labels.Length;i++) labels[i]=HotkeyLabel(ReaderState.HotkeyOptions[i]);
                Pick(key,labels,Array.IndexOf(ReaderState.HotkeyOptions,Hotkey),delegate(int i) {Hotkey=ReaderState.HotkeyOptions[i];key.Detail=ReaderState.TextFor(Hotkey);});
            };
            Label note=Note("按住第一个键不放，再按第二个键。双击托盘图标也能恢复。",m+Ui.S(4),g3.Bottom+Ui.S(6),gw-Ui.S(8));
            Controls.Add(note);
            ClientSize=new Size(w,note.Bottom+m);
        }
        // iOS-style pull-down menu under a row.
        void Pick(Row row,string[] labels,int selected,Action<int> chosen) {
            ContextMenuStrip pick=new ContextMenuStrip();Ui.StyleMenu(pick);
            for(int i=0;i<labels.Length;i++) {
                int index=i;ToolStripMenuItem item=new ToolStripMenuItem(labels[i]) {Checked=i==selected};
                item.Click+=delegate {chosen(index);row.Invalidate();};pick.Items.Add(item);
            }
            pick.Closed+=delegate {BeginInvoke((Action)delegate {pick.Dispose();});};
            pick.Show(row,new Point(row.Width-Ui.S(16)-pick.PreferredSize.Width,row.Height));
        }
        bool Same(int i) {return (TextArgb&0xFFFFFF)==(int)Palettes[i,1] && (BackArgb&0xFFFFFF)==(int)Palettes[i,2];}
        string PaletteName() {
            if(Skin=="Excel" || Skin.StartsWith("Code")) return "跟随样式";
            for(int i=0;i<Palettes.GetLength(0);i++) if(Same(i)) return (string)Palettes[i,0];
            return "自定义";
        }
        string FontText() {return (FontName=="Microsoft YaHei UI"?"微软雅黑":FontName)+" · "+FontSize.ToString("0.#");}
    }

    // Highlights of one book at a time; the book row switches between books that have any.
    public class HighlightSheet : Sheet {
        readonly ReaderState state;
        readonly Group group;
        readonly Row bookRow;
        readonly Label count;
        readonly TextButton copyAll;
        readonly RowList list;
        readonly ListCard card;
        string identity;
        public bool Changed {get;private set;}
        public string SelectedIdentity {get;private set;}
        public string SelectedPath {get;private set;}
        public int SelectedOffset {get;private set;}
        public HighlightSheet(ReaderState state,Book current):base("我的划线",null,"完成") {
            this.state=state;
            ClientSize=new Size(Ui.S(400),Ui.S(580));MinimumSize=new Size(Ui.S(320),Ui.S(360));
            RightButton.Click+=delegate {DialogResult=DialogResult.Cancel;Close();};
            List<Bookmark> books=Books(current);
            identity=current!=null?current.Identity:books.Count>0?books[0].Identity:null;
            group=new Group {Width=Ui.S(368)};bookRow=group.Add(new Row("书") {Chevron=true,Clickable=true});
            bookRow.Click+=delegate {
                List<Bookmark> all=Books(current);if(all.Count<2) return;
                ContextMenuStrip pick=new ContextMenuStrip();Ui.StyleMenu(pick);
                foreach(Bookmark b in all) {
                    string id=b.Identity;ToolStripMenuItem item=new ToolStripMenuItem(NameOf(b)) {Checked=id==identity,ShortcutKeyDisplayString=b.Highlights.Count+" 条"};
                    item.Click+=delegate {identity=id;Fill();};pick.Items.Add(item);
                }
                pick.Closed+=delegate {BeginInvoke((Action)delegate {pick.Dispose();});};
                pick.Show(bookRow,new Point(bookRow.Width-Ui.S(16)-pick.PreferredSize.Width,bookRow.Height));
            };
            count=Caption("",0,0,10);copyAll=new TextButton("复制全部",false);
            list=new RowList(true);card=new ListCard(list);
            Controls.Add(group);Controls.Add(count);Controls.Add(copyAll);Controls.Add(card);
            list.Activated+=delegate {
                RowList.Item it=list.SelectedItem as RowList.Item;Bookmark b=Current;if(it==null || b==null) return;
                SelectedIdentity=b.Identity;SelectedPath=b.Path;SelectedOffset=it.Offset;DialogResult=DialogResult.OK;Close();
            };
            list.MouseUp+=delegate(object s,MouseEventArgs e) {
                int i=list.IndexFromPoint(e.Location);if(e.Button!=MouseButtons.Right || i<0) return;
                list.SelectedIndex=i;Highlight h=Find(((RowList.Item)list.Items[i]).Offset);if(h==null) return;
                ContextMenuStrip m=new ContextMenuStrip();Ui.StyleMenu(m);
                ToolStripMenuItem copy=new ToolStripMenuItem("复制");copy.Click+=delegate {SetClipboard(h.Text,"已复制 1 条");};m.Items.Add(copy);
                ToolStripMenuItem del=new ToolStripMenuItem("删除划线");del.Click+=delegate {state.RemoveHighlights(identity,h.Start,h.Start);Changed=true;Fill();};m.Items.Add(del);
                m.Closed+=delegate {BeginInvoke((Action)delegate {m.Dispose();});};m.Show(list,e.Location);
            };
            copyAll.Click+=delegate {
                Bookmark b=Current;if(b==null || b.Highlights.Count==0) return;
                System.Text.StringBuilder sb=new System.Text.StringBuilder("《"+NameOf(b)+"》 划线\n");string chapter=null;
                foreach(Highlight h in b.Highlights) {
                    if(h.Chapter!=chapter) {chapter=h.Chapter;sb.Append("\n").Append(chapter).Append("\n");}
                    sb.Append(h.Text).Append("\n");
                }
                SetClipboard(sb.ToString().TrimEnd(),"已复制全部 "+b.Highlights.Count+" 条");
            };
            Fill();
        }
        Bookmark Current {get {return identity==null?null:state.Books.Find(x=>x.Identity==identity);}}
        Highlight Find(int start) {Bookmark b=Current;return b==null?null:b.Highlights.Find(h=>h.Start==start);}
        // Books with highlights, most recently read first; the open book is always included.
        List<Bookmark> Books(Book current) {
            List<Bookmark> list=state.Books.FindAll(x=>x.Highlights.Count>0 || (current!=null && x.Identity==current.Identity));
            list.Sort((a,b)=>b.LastRead.CompareTo(a.LastRead));return list;
        }
        static string NameOf(Bookmark b) {return !string.IsNullOrEmpty(b.Name)?b.Name:System.IO.Path.GetFileNameWithoutExtension(b.Path??"");}
        void SetClipboard(string text,string done) {
            try {Clipboard.SetText(text);count.Text=done;} catch(System.Runtime.InteropServices.ExternalException) {count.Text="复制失败，剪贴板被别的程序占用";}
        }
        void Fill() {
            Bookmark b=Current;list.BeginUpdate();list.Items.Clear();
            bookRow.Detail=b==null?"还没有打开书":NameOf(b);bookRow.Chevron=Books(null).Count>1 || (b!=null && Books(null).Count==1 && Books(null)[0].Identity!=b.Identity);bookRow.Invalidate();
            if(b!=null) foreach(Highlight h in b.Highlights)
                list.Items.Add(new RowList.Item {Title=h.Text.Replace('\n',' '),Detail=h.Chapter+"   ·   "+new DateTime(h.Created).ToString("M月d日"),Offset=h.Start});
            list.EmptyText="还没有划线。在正文里按住鼠标拖选文字，松开后点「划线」。";
            list.EndUpdate();
            int n=b==null?0:b.Highlights.Count;count.Text=n==0?"":"共 "+n+" 条 · 单击跳到原文，右键复制或删除";copyAll.Visible=n>0;
        }
        protected override void OnLayout(LayoutEventArgs e) {
            base.OnLayout(e);if(card==null) return;
            int m=Ui.S(16),w=ClientSize.Width-2*m,y=HeaderHeight;
            group.SetBounds(m,y,w,group.Height);y=group.Bottom+Ui.S(8);
            copyAll.Height=Ui.S(22);copyAll.Location=new Point(ClientSize.Width-m-copyAll.Width,y);
            count.SetBounds(m+Ui.S(4),y,w-copyAll.Width-Ui.S(8),Ui.S(22));y=count.Bottom+Ui.S(4);
            card.SetBounds(m,y,w,ClientSize.Height-y-m);
        }
    }

    public class HelpSheet : Sheet {
        public HelpSheet(string shortcut):base("使用帮助",null,"好") {
            RightButton.Click+=delegate {DialogResult=DialogResult.OK;Close();};
            int m=Ui.S(16),w=Ui.S(380),gw=w-2*m;
            string[,] rows={{"逐行滚动","触控板 / 滚轮"},{"下一屏","PageDown / 空格"},{"上一屏","PageUp"},{"上一章 / 下一章","Ctrl + ← / →"},{"返回 / 前进（跳转后）","Alt + ← / →"},{"自动滚动","Ctrl + P，Ctrl + ↑↓ 调速"},
                {"划线 / 复制","拖选文字后点选"},{"我的划线","Ctrl + L"},
                {"目录","Ctrl + J"},{"全文搜索","Ctrl + F"},{"打开 TXT","Ctrl + O"},{"设置","Ctrl + ,"},{"隐藏窗口","Esc"},{"全局隐藏 / 恢复",shortcut}};
            Group g=new Group {Left=m,Top=HeaderHeight+Ui.S(4),Width=gw};Controls.Add(g);
            for(int i=0;i<rows.GetLength(0);i++) g.Add(new Row(rows[i,0]) {Detail=rows[i,1],Height=Ui.S(40)});
            Label note=Note("拖动顶部短线（或按住 Alt 拖动正文）移动窗口，拖动四边调整大小。阅读进度自动保存；右键打开全部功能，托盘图标双击恢复。",m+Ui.S(4),g.Bottom+Ui.S(8),gw-Ui.S(8));
            Controls.Add(note);ClientSize=new Size(w,note.Bottom+m);
        }
    }
}

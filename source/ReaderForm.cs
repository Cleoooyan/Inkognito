using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;
namespace QuietReader {
    public class ReaderForm : Form {
        public Book CurrentBook { get; private set; }
        public int CurrentOffset { get; private set; }
        readonly string statePath;
        readonly ReaderState settings;
        readonly Stack<int> history=new Stack<int>();
        readonly SmoothScroll smooth=new SmoothScroll();
        readonly Timer scrollTimer=new Timer {Interval=15};
        // How far the first visible line has scrolled up, in pixels.
        int pixel;
        Page drawPage;
        Skin skin;
        // Mouse-over state comes from polling: in the clear style the transparent background never receives mouse events.
        bool mouseInside, textHidden;
        readonly Timer hoverTimer=new Timer {Interval=80};
        long leftAt;
        readonly System.Diagnostics.Stopwatch clock=System.Diagnostics.Stopwatch.StartNew();
        // Positions before jumps (contents, search, chapter keys) for Alt+Left / Alt+Right.
        readonly Stack<int> back=new Stack<int>(), forward=new Stack<int>();
        // Auto-scroll: lines per minute for speed 1-10, played out as pixels.
        static readonly int[] AutoLinesPerMinute={4,6,8,10,13,16,20,25,30,40};
        bool autoScroll;double autoCarry;long autoLast;
        readonly Timer autoTimer=new Timer {Interval=30};
        // Text selection by dragging over the text (offsets into the book text); cleared on click or when another book opens.
        int selFrom=-1, selTo=-1;bool selecting;
        static readonly Color Underline=Color.FromArgb(255,149,0);
        // Short message drawn at the bottom of the window.
        string toast;long toastUntil;
        readonly List<IntPtr> hiddenOwned=new List<IntPtr>();
        readonly NotifyIcon tray;
        readonly Timer saveTimer;
        Font readingFont;
        Page page;
        bool ready, hotkeyRegistered, saveFailed;
        ChordHook chordHook;
        // Shortcut actually working this session; a temporary hook failure must not overwrite the saved choice.
        string activeHotkey="Grave1";
        string ActiveShortcut {get {return ReaderState.TextFor(activeHotkey);}}
        protected bool QuietNotices;
        readonly ContextMenuStrip menu;
        const int HotkeyId=19462;
        const TextFormatFlags TextFlags=TextFormatFlags.NoPadding|TextFormatFlags.NoPrefix|TextFormatFlags.SingleLine;
        [DllImport("user32.dll")] static extern bool RegisterHotKey(IntPtr h,int id,uint modifiers,uint key);
        [DllImport("user32.dll")] static extern bool UnregisterHotKey(IntPtr h,int id);
        [DllImport("user32.dll")] static extern bool ReleaseCapture();
        [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr h,int msg,IntPtr w,IntPtr l);
        delegate bool EnumWindowCallback(IntPtr h,IntPtr parameter);
        [DllImport("user32.dll")] static extern bool EnumThreadWindows(uint thread,EnumWindowCallback callback,IntPtr parameter);
        [DllImport("kernel32.dll")] static extern uint GetCurrentThreadId();
        [DllImport("user32.dll")] static extern IntPtr GetWindow(IntPtr h,uint command);
        [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr h);
        [DllImport("user32.dll")] static extern bool IsWindow(IntPtr h);
        [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr h,int command);
        [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr h);

        public ReaderForm(string statePath,string initialPath) {
            this.statePath=statePath;settings=StateStore.Load(statePath);Ui.HideFromCapture=settings.HideFromCapture;
            Text="便笺";FormBorderStyle=FormBorderStyle.None;ShowInTaskbar=false;TopMost=settings.Topmost;
            StartPosition=FormStartPosition.Manual;MinimumSize=new Size(Ui.S(240),Ui.S(130));
            AutoScaleMode=AutoScaleMode.None;Bounds=VisibleBounds(new Rectangle(settings.X,settings.Y,settings.Width,settings.Height));
            SetStyle(ControlStyles.AllPaintingInWmPaint|ControlStyles.UserPaint|ControlStyles.OptimizedDoubleBuffer|ControlStyles.ResizeRedraw,true);
            ApplyFont();ApplySkin();AllowDrop=true;
            menu=new ContextMenuStrip();Ui.StyleMenu(menu);
            ToolStripMenuItem toc=AddMenu("目录","Ctrl+J",delegate { ShowChapters(false); });
            ToolStripMenuItem find=AddMenu("全文搜索","Ctrl+F",delegate { ShowChapters(true); });
            ToolStripMenuItem prev=AddMenu("上一章","Ctrl+←",delegate { JumpChapter(-1); });
            ToolStripMenuItem next=AddMenu("下一章","Ctrl+→",delegate { JumpChapter(1); });
            ToolStripMenuItem goBack=AddMenu("返回跳转前的位置","Alt+←",delegate { GoBack(); });
            ToolStripMenuItem auto=AddMenu("自动滚动","Ctrl+P",delegate { ToggleAuto(); });
            ToolStripMenuItem notes=AddMenu("我的划线","Ctrl+L",delegate { ShowHighlights(); });
            menu.Items.Add(new ToolStripSeparator());
            AddMenu("打开 TXT / EPUB…","Ctrl+O",delegate { PickBook(); });
            ToolStripMenuItem recent=AddMenu("最近阅读",null,null);
            ToolStripMenuItem encoding=AddMenu("乱码？换个编码…",null,delegate { ChangeEncoding(); });
            menu.Items.Add(new ToolStripSeparator());
            AddMenu("设置…","Ctrl+,",delegate { ShowSettings(); });
            ToolStripMenuItem style=AddMenu("样式",null,null);
            foreach(string option in ReaderState.Skins) {
                string o=option;ToolStripMenuItem item=new ToolStripMenuItem(ReaderState.SkinName(o));
                item.Click+=delegate { settings.Skin=o;ApplySkin();Persist(); };style.DropDownItems.Add(item);
            }
            ToolStripMenuItem away=AddMenu("鼠标移开隐藏文字",null,delegate { settings.HideOnLeave=!settings.HideOnLeave;textHidden=settings.HideOnLeave && !mouseInside;Invalidate();Persist(); });
            ToolStripMenuItem capture=AddMenu("防截屏",null,delegate { SetCaptureHidden(!settings.HideFromCapture); });
            ToolStripMenuItem pin=AddMenu("置顶",null,delegate { TopMost=!TopMost;settings.Topmost=TopMost;Persist(); });
            ToolStripMenuItem hide=AddMenu("隐藏","",delegate { ToggleVisibility(); });
            AddMenu("使用帮助",null,delegate { Help(); });menu.Items.Add(new ToolStripSeparator());
            AddMenu("退出",null,delegate { Close(); });ContextMenuStrip=menu;
            menu.Opening+=delegate {
                bool has=CurrentBook!=null;toc.Enabled=find.Enabled=prev.Enabled=next.Enabled=encoding.Enabled=has;
                foreach(ToolStripMenuItem item in style.DropDownItems) item.Checked=item.Text==ReaderState.SkinName(settings.Skin);
                goBack.Enabled=back.Count>0;auto.Enabled=has;auto.Checked=autoScroll;notes.Enabled=has || settings.Books.Exists(x=>x.Highlights.Count>0);
                capture.Checked=settings.HideFromCapture;capture.ShortcutKeyDisplayString=settings.HideFromCapture?"截图看不到":"可以截图";
                away.Checked=settings.HideOnLeave;encoding.Enabled=has && CurrentBook.EncodingName!="EPUB";
                pin.Checked=TopMost;hide.Text=Visible?"隐藏":"显示";hide.ShortcutKeyDisplayString=ActiveShortcut;
                recent.DropDownItems.Clear();
                foreach(Bookmark b in settings.Recent(has?CurrentBook.Identity:null,8)) {
                    string path=b.Path;bool exists=File.Exists(path);
                    ToolStripMenuItem item=new ToolStripMenuItem(Shorten(string.IsNullOrEmpty(b.Name)?Path.GetFileNameWithoutExtension(path):b.Name,22)) {
                        ShortcutKeyDisplayString=exists?(b.Length>0?Math.Min(100,b.Offset*100L/b.Length)+"%":""):"文件已移走",Enabled=exists};
                    item.Click+=delegate { OpenBook(path,"auto"); };recent.DropDownItems.Add(item);
                }
                if(recent.DropDownItems.Count==0) recent.DropDownItems.Add(new ToolStripMenuItem("暂无其他书") {Enabled=false});
            };
            tray=new NotifyIcon { Icon=SystemIcons.Application,Text="便笺 · 双击恢复",Visible=true,ContextMenuStrip=menu };
            tray.DoubleClick+=delegate { Restore(); };
            scrollTimer.Tick+=delegate { if(smooth.Active) ScrollPixels(smooth.Take());else scrollTimer.Stop(); };
            hoverTimer.Tick+=delegate { PollMouse();if(toast!=null && clock.ElapsedMilliseconds>=toastUntil) {toast=null;Invalidate();} };hoverTimer.Start();
            autoTimer.Tick+=delegate { AutoTick(); };
            saveTimer=new Timer {Interval=5000};saveTimer.Tick+=delegate { Persist(); };saveTimer.Start();
            DragEnter+=delegate(object s,DragEventArgs e) { e.Effect=e.Data.GetDataPresent(DataFormats.FileDrop)?DragDropEffects.Copy:DragDropEffects.None; };
            DragDrop+=delegate(object s,DragEventArgs e) { string[] files=e.Data.GetData(DataFormats.FileDrop) as string[];if(files!=null && files.Length>0) OpenBook(files[0],"auto"); };
            ready=true;
            if(!string.IsNullOrEmpty(initialPath)) OpenBook(initialPath,"auto");
            else if(!string.IsNullOrEmpty(settings.LastPath) && File.Exists(settings.LastPath)) OpenBook(settings.LastPath,"auto");
            else {
                // A novel placed next to the program opens on first start; the bundled instructions are not a novel.
                foreach(string file in Directory.GetFiles(AppDomain.CurrentDomain.BaseDirectory,"*.txt"))
                    if(!Path.GetFileName(file).StartsWith("使用说明")) {OpenBook(file,"auto");break;}
            }
        }
        ToolStripMenuItem AddMenu(string title,string shortcut,Action action) {
            ToolStripMenuItem item=new ToolStripMenuItem(title) {ShortcutKeyDisplayString=shortcut};
            if(action!=null) item.Click+=delegate { action(); };menu.Items.Add(item);return item;
        }
        static string Shorten(string s,int max) { return s.Length<=max?s:s.Substring(0,max-1)+"…"; }
        static Rectangle VisibleBounds(Rectangle b) {
            Rectangle work=Screen.FromRectangle(b).WorkingArea;
            b.Width=Math.Min(b.Width,work.Width);b.Height=Math.Min(b.Height,work.Height);
            b.X=Math.Max(work.Left,Math.Min(work.Right-b.Width,b.X));b.Y=Math.Max(work.Top,Math.Min(work.Bottom-b.Height,b.Y));return b;
        }
        void ApplyFont() {
            Font next;
            try { next=new Font(settings.FontName,settings.FontSize,FontStyle.Regular,GraphicsUnit.Point); }
            catch(ArgumentException) { next=new Font("Microsoft YaHei UI",10.5f); }
            if(readingFont!=null) readingFont.Dispose();readingFont=next;ResetLayout();pixel=0;history.Clear();
        }
        void ApplySkin() {
            skin=Skin.Create(settings);ResetLayout();pixel=0;smooth.Stop();
            textHidden=settings.HideOnLeave && !mouseInside;UpdateBackground();
            Opacity=skin.Opaque?1.0:settings.Opacity/100.0;
            // The one-line style shrinks the window to a single line and restores the old height afterwards.
            int lineWindow=LineHeight+2*LineSkin.Margin;
            if(skin is LineSkin) {
                if(Height>Ui.S(130)) settings.FullHeight=Height;
                MinimumSize=new Size(Ui.S(160),lineWindow);Height=lineWindow;
            } else {
                // Check before raising the minimum size, which would already grow the window to the minimum.
                bool wasLine=Height<Ui.S(130);
                MinimumSize=new Size(Ui.S(240),Ui.S(130));
                if(wasLine) Height=settings.FullHeight>=Ui.S(130)?settings.FullHeight:Ui.S(300);
            }
            if(IsHandleCreated) Ui.Protect(Handle);
        }
        protected override void OnHandleCreated(EventArgs e) { base.OnHandleCreated(e);Ui.Protect(Handle); }
        // Clear style: see-through (and click-through) while the mouse is away, a normal note while it is over the window
        // so the window can still be dragged, resized and right-clicked.
        void UpdateBackground() {
            ClearSkin clear=skin as ClearSkin;
            if(clear!=null) clear.Solid=mouseInside;
            BackColor=skin.Back;ForeColor=skin.Fore;
            Color key=clear!=null && !clear.Solid?ClearSkin.Key:Color.Empty;
            if(TransparencyKey!=key) TransparencyKey=key;
            // Switching the layered style can drop the capture setting, so apply it again.
            if(IsHandleCreated) Ui.Protect(Handle);
            Invalidate();
        }
        void SetInside(bool inside) {
            if(inside) leftAt=0;
            if(inside==mouseInside) return;
            mouseInside=inside;textHidden=settings.HideOnLeave && !inside;UpdateBackground();
        }
        void PollMouse() {
            bool inside=Visible && (Bounds.Contains(Cursor.Position) || menu.Visible);
            if(inside) {SetInside(true);return;}
            // A short grace period so brushing past the edge does not flash the text.
            if(mouseInside) {if(leftAt==0) leftAt=clock.ElapsedMilliseconds;else if(clock.ElapsedMilliseconds-leftAt>=250) SetInside(false);}
        }
        int LineHeight { get { return (int)Math.Round(Math.Max(16,(int)Math.Ceiling(readingFont.GetHeight())+4)*skin.Spacing); } }
        Rectangle TextArea { get { return skin.TextArea(ClientSize); } }
        int Gap { get { return skin.Gap(LineHeight); } }
        int ContentHeight { get { return TextArea.Height; } }
        int Measure(string s) { return TextRenderer.MeasureText(s,readingFont,new Size(int.MaxValue,int.MaxValue),TextFlags).Width; }
        Page BuildPage(int offset,int height) { return PageLayout.Build(CurrentBook.Text,offset,TextArea.Width,height,Measure,LineHeight,Gap); }
        Page CurrentPage { get { if(page==null && CurrentBook!=null) page=BuildPage(CurrentOffset,ContentHeight);return page; } }
        // The drawn page reaches a few lines past the bottom so partly scrolled lines are visible.
        Page DrawPage { get { if(drawPage==null && CurrentBook!=null) drawPage=BuildPage(CurrentOffset,ContentHeight+LineHeight*3);return drawPage; } }
        void ResetLayout() { page=null;drawPage=null; }
        public int ScrollPixel { get { return pixel; } }
        public void OpenBook(string path,string encoding) {
            try {
                if(encoding=="auto") encoding=settings.EncodingFor(Book.IdentityOf(path));
                Book loaded=Book.Load(path,encoding);
                if(loaded.Text.Length==0) throw new InvalidDataException("这个 TXT 没有可阅读的文字。");
                Persist();CurrentBook=loaded;settings.LastPath=loaded.Path;
                CurrentOffset=settings.HasPosition(loaded.Identity)?Math.Min(settings.Position(loaded.Identity),loaded.Text.Length-1):(loaded.Chapters.Count>0?loaded.Chapters[0].Offset:0);
                // A newer copy of a book read before (e.g. the serial downloaded again) continues at the same passage.
                Bookmark older=settings.PreviousCopy(loaded);List<Highlight> movedHighlights=null;
                if(older!=null) {
                    CurrentOffset=BookMatch.Relocate(loaded,older);int added=older.ChapterCount>0?loaded.Chapters.Count-older.ChapterCount:0;
                    settings.Books.Remove(older);movedHighlights=BookMatch.MoveHighlights(loaded,older.Highlights);
                    ShowToast("已接着上次读到的地方"+(added>0?" · 新增 "+added+" 章":""));
                }
                settings.SetEncoding(loaded.Identity,encoding);settings.Opened(loaded);
                if(movedHighlights!=null) settings.HighlightsOf(loaded.Identity).AddRange(movedHighlights);
                selFrom=selTo=-1;back.Clear();forward.Clear();history.Clear();ResetLayout();pixel=0;smooth.Stop();Invalidate();Persist();
            } catch(Exception ex) { MessageBox.Show(this,"无法打开文件：\n"+ex.Message,"便笺",MessageBoxButtons.OK,MessageBoxIcon.Information); }
        }
        void PickBook() {
            Restore();using(OpenFileDialog d=new OpenFileDialog {Filter="小说（TXT / EPUB）|*.txt;*.epub|所有文件|*.*",Title="打开本地小说"}) {
                if(d.ShowDialog(this)==DialogResult.OK) OpenBook(d.FileName,"auto");
            }
        }
        // Encoding is detected automatically; this is only for the rare garbled file.
        void ChangeEncoding() {
            if(CurrentBook==null) return;Restore();
            using(EncodingSheet d=new EncodingSheet(settings.EncodingFor(CurrentBook.Identity)))
                if(d.ShowDialog(this)==DialogResult.OK) OpenBook(CurrentBook.Path,d.EncodingName);
        }
        void Navigate(int pos,bool remember) {
            if(CurrentBook==null) return;
            pos=Math.Max(0,Math.Min(pos,CurrentBook.Text.Length-1));
            smooth.Stop();
            if(pos==CurrentOffset && pixel==0) return;
            if(remember) history.Push(CurrentOffset);CurrentOffset=pos;pixel=0;ResetLayout();Invalidate();Persist();
        }
        void NextPage() { Page p=CurrentPage;if(p!=null && p.End<CurrentBook.Text.Length) Navigate(p.Next,true); }
        int PreviousStart(int pos) { return PreviousStart(pos,ContentHeight); }
        int PreviousStart(int pos,int availableHeight) {
            if(pos<=0) return 0;
            int begin;
            // One line back only needs the paragraph before pos; laying out 6000 characters per line made scrolling up slow.
            if(availableHeight<=LineHeight*2) begin=pos<2?0:CurrentBook.Text.LastIndexOf('\n',pos-2)+1;
            else {begin=Math.Max(0,pos-6000);if(begin>0) {int newline=CurrentBook.Text.IndexOf('\n',begin);if(newline>=0 && newline<pos) begin=newline+1;}}
            Page all=PageLayout.Build(CurrentBook.Text.Substring(begin,pos-begin),0,TextArea.Width,int.MaxValue,Measure,LineHeight,Gap);int y=0,result=begin;
            for(int i=all.Lines.Count-1;i>=0;i--) {
                VisualLine line=all.Lines[i];
                int h=LineHeight+(line.Next>line.End?Gap:0);
                if(y+h>availableHeight && y>0) break;
                y+=h;result=begin+line.Start;
            }
            return result;
        }
        void PreviousPage() {
            if(CurrentBook==null) return;
            int pos=history.Count>0?history.Pop():PreviousStart(CurrentOffset);Navigate(pos,false);
        }
        static int FirstAdvance(Page p,int lineHeight) { return p.Lines.Count>1?p.Lines[1].Y-p.Lines[0].Y:lineHeight; }
        // Moves the view by whole pixels, stepping CurrentOffset across line starts. Progress is saved by the timer, not per frame.
        void ScrollPixels(int dy) {
            if(CurrentBook==null || dy==0) return;
            history.Clear();pixel+=dy;int length=CurrentBook.Text.Length;
            while(pixel>0) {
                Page p=DrawPage;
                if(p.End>=length) {
                    // The end of the book is in view: stop once its last line reaches the bottom.
                    int max=p.Lines.Count==0?0:Math.Max(0,p.Lines[p.Lines.Count-1].Y+LineHeight-ContentHeight);
                    if(pixel>max) {pixel=max;smooth.Stop();}
                }
                int advance=FirstAdvance(p,LineHeight);
                if(pixel<advance || p.Lines.Count<2) break;
                CurrentOffset=p.Lines[1].Start;pixel-=advance;ResetLayout();
            }
            while(pixel<0) {
                if(CurrentOffset<=0) {pixel=0;smooth.Stop();break;}
                int prev=PreviousStart(CurrentOffset,LineHeight);
                pixel+=FirstAdvance(BuildPage(prev,LineHeight*3),LineHeight);CurrentOffset=prev;ResetLayout();
            }
            Invalidate();
        }
        protected void FinishScroll() { ScrollPixels(smooth.TakeAll());scrollTimer.Stop(); }
        void ShowChapters(bool search) {
            if(CurrentBook==null) return;Restore();
            using(ChapterSheet d=new ChapterSheet(CurrentBook,CurrentOffset,search)) if(d.ShowDialog(this)==DialogResult.OK) JumpTo(d.SelectedOffset);
        }
        void JumpChapter(int step) {
            if(CurrentBook==null || CurrentBook.Chapters.Count==0) return;
            int i=CurrentBook.ChapterIndexAt(CurrentOffset)+step;
            if(step<0 && i<0) {JumpTo(0);return;}
            if(i>=0 && i<CurrentBook.Chapters.Count) JumpTo(CurrentBook.Chapters[i].Offset);
        }
        // Jumps remember where you were, so Alt+Left can bring you back after looking something up.
        void JumpTo(int pos) {
            if(CurrentBook==null) return;
            if(pos!=CurrentOffset) {back.Push(CurrentOffset);forward.Clear();}
            history.Clear();Navigate(pos,false);
        }
        void GoBack() {if(back.Count==0 || CurrentBook==null) return;forward.Push(CurrentOffset);history.Clear();Navigate(back.Pop(),false);ShowToast("已返回跳转前的位置");}
        void GoForward() {if(forward.Count==0 || CurrentBook==null) return;back.Push(CurrentOffset);history.Clear();Navigate(forward.Pop(),false);}
        void ToggleAuto() {
            if(CurrentBook==null) return;autoScroll=!autoScroll;
            if(autoScroll) {autoCarry=0;autoLast=clock.ElapsedMilliseconds;autoTimer.Start();ShowToast("自动滚动 · 速度 "+settings.AutoSpeed+"（Ctrl+↑↓ 调速）");}
            else {autoTimer.Stop();ShowToast("自动滚动已停止");}
        }
        void ChangeAutoSpeed(int step) {
            settings.AutoSpeed=Math.Max(1,Math.Min(10,settings.AutoSpeed+step));Persist();
            ShowToast("自动滚动速度 "+settings.AutoSpeed+(autoScroll?"":"（Ctrl+P 开始）"));
        }
        // Pauses while the window is hidden, the text is hidden on mouse-leave, or a menu or dialog is open.
        void AutoTick() {
            long now=clock.ElapsedMilliseconds,dt=now-autoLast;autoLast=now;
            if(!autoScroll || CurrentBook==null) {autoTimer.Stop();return;}
            if(!Visible || textHidden || menu.Visible || Application.OpenForms.Count>1) return;
            autoCarry+=dt/60000.0*AutoLinesPerMinute[settings.AutoSpeed-1]*LineHeight;
            int px=(int)autoCarry;if(px<=0) return;autoCarry-=px;
            int before=CurrentOffset,beforePixel=pixel;ScrollPixels(px);
            if(CurrentOffset==before && pixel==beforePixel) {autoScroll=false;autoTimer.Stop();ShowToast("已经到结尾了");}
        }
        bool HasSelection {get {return CurrentBook!=null && selFrom>=0 && selTo>=0 && selFrom!=selTo;}}
        // Book offset under a point: the nearest visible line, then the character boundary closest to x.
        int HitOffset(Point p) {
            if(CurrentBook==null || textHidden || skin is LineSkin) return -1;
            Rectangle ta=TextArea;
            if(!selecting && !ta.Contains(p)) return -1;
            List<VisualLine> lines=DrawPage.Lines;if(lines.Count==0) return -1;
            VisualLine hit=null;
            foreach(VisualLine l in lines) {
                int top=ta.Y+l.Y-pixel;if(top>ta.Bottom) break;
                hit=l;if(p.Y<top+LineHeight) break;
            }
            string text=CurrentBook.Text.Substring(hit.Start,hit.End-hit.Start);int dx=p.X-ta.X;
            if(dx<=0) return hit.Start;
            int lo=0,hi=text.Length;
            while(lo<hi) {int mid=(lo+hi+1)/2;if(Measure(text.Substring(0,mid))<=dx) lo=mid;else hi=mid-1;}
            if(lo<text.Length) {int left=Measure(text.Substring(0,lo)),right=Measure(text.Substring(0,lo+1));if(dx-left>(right-left)/2) lo++;}
            return hit.Start+lo;
        }
        int XOf(VisualLine line,int pos,int x) {return x+(pos<=line.Start?0:Measure(CurrentBook.Text.Substring(line.Start,pos-line.Start)));}
        // Background pass: the selection; foreground pass: underlines of saved highlights.
        void PaintMarks(Graphics g,VisualLine line,int x,int y,int baseline,bool background) {
            if(background) {
                if(!HasSelection) return;
                int s=Math.Max(Math.Min(selFrom,selTo),line.Start),e=Math.Min(Math.Max(selFrom,selTo),line.End);if(e<=s) return;
                int x1=XOf(line,s,x),x2=XOf(line,e,x);
                using(SolidBrush b=new SolidBrush(Color.FromArgb(80,Ui.Accent))) g.FillRectangle(b,x1,y+baseline,x2-x1,readingFont.Height);
                return;
            }
            int thick=Math.Max(2,Ui.S(1.5f)),under=y+baseline+readingFont.Height-thick/2;
            foreach(Highlight h in settings.HighlightsOf(CurrentBook.Identity)) {
                if(h.Start>=line.End) break;
                int s=Math.Max(h.Start,line.Start),e=Math.Min(h.End,line.End);if(e<=s) continue;
                using(Pen p=new Pen(Underline,thick)) g.DrawLine(p,XOf(line,s,x),under,XOf(line,e,x),under);
            }
        }
        void Copy(string text,string done) {
            try {Clipboard.SetText(text);ShowToast(done);} catch(System.Runtime.InteropServices.ExternalException) {ShowToast("复制失败，剪贴板被别的程序占用");}
        }
        void CopySelection() {int a=Math.Min(selFrom,selTo),b=Math.Max(selFrom,selTo);Copy(CurrentBook.Text.Substring(a,b-a),"已复制");}
        ContextMenuStrip PopupMenu() {
            ContextMenuStrip m=new ContextMenuStrip();Ui.StyleMenu(m);
            m.Closed+=delegate {BeginInvoke((Action)delegate {m.Dispose();});};return m;
        }
        void AddItem(ContextMenuStrip m,string title,Action action) {ToolStripMenuItem item=new ToolStripMenuItem(title);item.Click+=delegate {action();};m.Items.Add(item);}
        void ShowSelectionMenu(int a,int b) {
            ContextMenuStrip m=PopupMenu();Book book=CurrentBook;
            AddItem(m,"划线",delegate {settings.AddHighlight(book,a,b);selFrom=selTo=-1;Persist();Invalidate();ShowToast("已划线 · Ctrl+L 查看全部划线");});
            AddItem(m,"复制",delegate {CopySelection();selFrom=selTo=-1;Invalidate();});
            if(settings.HighlightsOf(book.Identity).Exists(h=>h.Start<b && h.End>a))
                AddItem(m,"删除这里的划线",delegate {settings.RemoveHighlights(book.Identity,a,b);selFrom=selTo=-1;Persist();Invalidate();});
            m.Show(Cursor.Position);
        }
        void ShowHighlightMenu(Highlight h) {
            ContextMenuStrip m=PopupMenu();Book book=CurrentBook;
            AddItem(m,"复制这条划线",delegate {Copy(h.Text,"已复制");});
            AddItem(m,"删除划线",delegate {settings.RemoveHighlights(book.Identity,h.Start,h.Start);Persist();Invalidate();});
            m.Show(Cursor.Position);
        }
        void ShowHighlights() {
            Restore();
            using(HighlightSheet d=new HighlightSheet(settings,CurrentBook)) {
                DialogResult r=d.ShowDialog(this);
                if(d.Changed) {Persist();Invalidate();}
                if(r!=DialogResult.OK) return;
                if(CurrentBook==null || CurrentBook.Identity!=d.SelectedIdentity) {
                    if(!File.Exists(d.SelectedPath)) {ShowToast("找不到这本书的文件了");return;}
                    OpenBook(d.SelectedPath,"auto");
                }
                if(CurrentBook!=null && CurrentBook.Identity==d.SelectedIdentity) {
                    int at=Math.Min(d.SelectedOffset,CurrentBook.Text.Length);
                    JumpTo(at==0?0:CurrentBook.Text.LastIndexOf('\n',at-1)+1);
                }
            }
        }
        // Applies to every window of the reader at once (main window, menus, sheets), not only ones created afterwards.
        static void ProtectMenu(ToolStripDropDown d) {
            if(d.IsHandleCreated) Ui.Protect(d.Handle);
            foreach(ToolStripItem item in d.Items) {ToolStripMenuItem m=item as ToolStripMenuItem;if(m!=null && m.HasDropDownItems) ProtectMenu(m.DropDown);}
        }
        protected void SetCaptureHidden(bool on) {
            settings.HideFromCapture=on;Ui.HideFromCapture=on;
            if(IsHandleCreated) Ui.Protect(Handle);
            foreach(Form owned in OwnedForms) if(owned.IsHandleCreated) Ui.Protect(owned.Handle);
            ProtectMenu(menu);
            Persist();ShowToast(on?"已开启防截屏：截图、共享屏幕看不到阅读器":"已关闭防截屏：现在可以截图了");
        }
        void ShowToast(string text) {toast=text;toastUntil=clock.ElapsedMilliseconds+2600;Invalidate();}
        void ShowSettings() {
            Restore();using(SettingsSheet d=new SettingsSheet(settings)) {
                if(d.ShowDialog(this)!=DialogResult.OK) return;
                settings.FontName=d.FontName;settings.FontSize=d.FontSize;settings.Opacity=d.Alpha;settings.Topmost=d.Pin;settings.Hotkey=d.Hotkey;
                settings.Skin=d.Skin;settings.TextArgb=d.TextArgb;settings.BackArgb=d.BackArgb;settings.HideOnLeave=d.HideOnLeave;
                settings.AutoSpeed=d.AutoSpeed;
                if(d.HideFromCapture!=settings.HideFromCapture) SetCaptureHidden(d.HideFromCapture);
                ApplyFont();ApplySkin();TopMost=settings.Topmost;RegisterShortcut();Persist();Invalidate();
            }
        }
        void Help() {
            Restore();using(HelpSheet d=new HelpSheet(ActiveShortcut)) d.ShowDialog(this);
        }
        void Persist() {
            if(!ready) return;
            settings.X=Left;settings.Y=Top;settings.Width=Width;settings.Height=Height;
            if(CurrentBook!=null) settings.Remember(CurrentBook,CurrentOffset);
            try { StateStore.Save(statePath,settings);if(saveFailed) {saveFailed=false;Invalidate();} }
            catch(Exception ex) {
                if(!saveFailed && tray!=null) tray.ShowBalloonTip(5000,"便笺：进度未保存",ex.Message,ToolTipIcon.Warning);
                saveFailed=true;Invalidate();
            }
        }
        void ToggleVisibility() {
            if(Visible) {
                Persist();hiddenOwned.Clear();
                EnumThreadWindows(GetCurrentThreadId(),delegate(IntPtr h,IntPtr unused) {
                    if(h==Handle || !IsWindowVisible(h)) return true;
                    IntPtr owner=GetWindow(h,4);
                    for(int i=0;i<32 && owner!=IntPtr.Zero;i++) {
                        if(owner==Handle) {hiddenOwned.Add(h);ShowWindow(h,0);break;}owner=GetWindow(owner,4);
                    }
                    return true;
                },IntPtr.Zero);
                Hide();
            } else Restore();
        }
        void Restore() {
            Bounds=VisibleBounds(Bounds);Show();WindowState=FormWindowState.Normal;
            for(int i=hiddenOwned.Count-1;i>=0;i--) if(IsWindow(hiddenOwned[i])) ShowWindow(hiddenOwned[i],5);
            if(hiddenOwned.Count>0 && IsWindow(hiddenOwned[0])) SetForegroundWindow(hiddenOwned[0]);else {Activate();Focus();}
            hiddenOwned.Clear();
        }
        void RegisterShortcut() {
            if(chordHook!=null) {chordHook.Dispose();chordHook=null;}
            if(hotkeyRegistered) UnregisterHotKey(Handle,HotkeyId);
            string requested=settings.Hotkey;hotkeyRegistered=false;
            bool chord=requested=="Grave1" || requested=="TabQ";
            if(chord) {
                // ` is VK_OEM_3 (0xC0); 1 is 0x31.
                chordHook=requested=="Grave1"?new ChordHook(Handle,0xC0,0x31):new ChordHook(Handle,9,81);
                if(chordHook.Installed) {hotkeyRegistered=true;activeHotkey=requested;Persist();return;}
                chordHook.Dispose();chordHook=null;
            }
            List<string> candidates=new List<string>();
            foreach(string option in ReaderState.HotkeyOptions) if(option!="Grave1" && option!="TabQ" && option!=requested) candidates.Add(option);
            if(!chord) candidates.Insert(0,requested);
            foreach(string option in candidates) {
                // 0x4000 no repeat; 0x8 Win, 0x3 Ctrl+Alt.
                uint modifiers=option=="WinGrave"?0x4008u:0x4003u;
                Keys key=option=="WinGrave"?Keys.Oemtilde:option=="Space"?Keys.Space:option=="Z"?Keys.Z:Keys.Q;
                // A failed keyboard hook may be temporary, so the saved chord choice is kept for the next start.
                if(RegisterHotKey(Handle,HotkeyId,modifiers,(uint)key)) {hotkeyRegistered=true;activeHotkey=option;if(!chord) settings.Hotkey=option;break;}
            }
            if(QuietNotices) {Persist();return;}
            if(!hotkeyRegistered) tray.ShowBalloonTip(6000,"便笺：快捷键均被占用","双击托盘图标可恢复；关闭占用软件后，在设置中保存一次可重试。",ToolTipIcon.Warning);
            else if(activeHotkey!=requested) tray.ShowBalloonTip(6000,"便笺：已切换快捷键",ReaderState.TextFor(requested)+(chord?" 暂时不可用（下次启动会再试）":" 被占用")+"，已改为 "+ActiveShortcut+"。",ToolTipIcon.Info);
            Persist();
        }
        protected override void OnShown(EventArgs e) { base.OnShown(e);RegisterShortcut(); }
        protected override void OnResize(EventArgs e) { ResetLayout();pixel=0;smooth.Stop();history.Clear();base.OnResize(e); }
        protected override void OnMouseWheel(MouseEventArgs e) {
            if(CurrentBook!=null) {
                // Windows default speed: the system "lines per notch" setting (3 unless changed).
                int lines=SystemInformation.MouseWheelScrollLines;if(lines<=0 || lines>10) lines=3;
                smooth.Add(e.Delta,lines*LineHeight,ContentHeight*2);if(smooth.Active && !scrollTimer.Enabled) scrollTimer.Start();
            }
            HandledMouseEventArgs handled=e as HandledMouseEventArgs;if(handled!=null) handled.Handled=true;
            base.OnMouseWheel(e);
        }
        protected override void OnMouseEnter(EventArgs e) { SetInside(true);base.OnMouseEnter(e); }
        protected override void OnMouseDown(MouseEventArgs e) {
            base.OnMouseDown(e);
            if(e.Button==MouseButtons.Left && (e.Y<TextArea.Y || skin is LineSkin || (ModifierKeys&Keys.Alt)!=0)) { ReleaseCapture();SendMessage(Handle,0xA1,(IntPtr)2,IntPtr.Zero);Persist(); }
            else if(e.Button==MouseButtons.Left) {
                Focus();int at=HitOffset(e.Location);
                if(at>=0) {selecting=true;selFrom=selTo=at;Capture=true;Invalidate();}
            }
        }
        protected override void OnMouseMove(MouseEventArgs e) {
            base.OnMouseMove(e);
            if(selecting) {int at=HitOffset(e.Location);if(at>=0 && at!=selTo) {selTo=at;Invalidate();}return;}
            Cursor=CurrentBook!=null && !textHidden && !(skin is LineSkin) && TextArea.Contains(e.Location)?Cursors.IBeam:Cursors.Default;
        }
        protected override void OnMouseUp(MouseEventArgs e) {
            base.OnMouseUp(e);if(!selecting) return;selecting=false;Capture=false;
            int a=Math.Min(selFrom,selTo),b=Math.Max(selFrom,selTo);
            if(b>a) {ShowSelectionMenu(a,b);return;}
            selFrom=selTo=-1;Invalidate();
            Highlight h=CurrentBook==null?null:settings.HighlightAt(CurrentBook.Identity,a);
            if(h!=null) ShowHighlightMenu(h);
        }
        protected override bool ProcessCmdKey(ref Message msg,Keys key) {
            if(key==Keys.Escape) {ToggleVisibility();return true;}
            if(key==Keys.PageDown || key==Keys.Space || key==Keys.Down) {NextPage();return true;}
            if(key==Keys.PageUp || key==Keys.Up) {PreviousPage();return true;}
            if(key==(Keys.Control|Keys.J)) {ShowChapters(false);return true;}
            if(key==(Keys.Control|Keys.F)) {ShowChapters(true);return true;}
            if(key==(Keys.Control|Keys.L)) {ShowHighlights();return true;}
            if(key==(Keys.Control|Keys.C) && HasSelection) {CopySelection();return true;}
            if(key==(Keys.Control|Keys.Left)) {JumpChapter(-1);return true;}
            if(key==(Keys.Control|Keys.Right)) {JumpChapter(1);return true;}
            if(key==(Keys.Alt|Keys.Left)) {GoBack();return true;}
            if(key==(Keys.Alt|Keys.Right)) {GoForward();return true;}
            if(key==(Keys.Control|Keys.P)) {ToggleAuto();return true;}
            if(key==(Keys.Control|Keys.Up)) {ChangeAutoSpeed(1);return true;}
            if(key==(Keys.Control|Keys.Down)) {ChangeAutoSpeed(-1);return true;}
            if(key==(Keys.Control|Keys.O)) {PickBook();return true;}
            if(key==(Keys.Control|Keys.Oemcomma)) {ShowSettings();return true;}
            if(key==(Keys.Control|Keys.Home)) {JumpTo(0);return true;}
            if(key==(Keys.Control|Keys.End) && CurrentBook!=null) {JumpTo(PreviousStart(CurrentBook.Text.Length));return true;}
            return base.ProcessCmdKey(ref msg,key);
        }
        protected override void WndProc(ref Message m) {
            if(m.Msg==0x8001) {ToggleVisibility();return;}
            if(m.Msg==0x312 && m.WParam.ToInt32()==HotkeyId) {ToggleVisibility();return;}
            if(m.Msg==0x84) {
                long xy=m.LParam.ToInt64();Point p=PointToClient(new Point((short)(xy&0xffff),(short)((xy>>16)&0xffff)));
                int edge=Ui.S(6);bool l=p.X<edge,r=p.X>=ClientSize.Width-edge,t=p.Y<edge,b=p.Y>=ClientSize.Height-edge;
                int hit=t?(l?13:r?14:12):b?(l?16:r?17:15):l?10:r?11:1;
                m.Result=(IntPtr)hit;return;
            }
            base.WndProc(ref m);
        }
        protected override void OnPaint(PaintEventArgs e) {
            base.OnPaint(e);
            Frame f=new Frame {G=e.Graphics,Client=ClientSize,Book=CurrentBook,Font=readingFont,Pixel=pixel,LineHeight=LineHeight,
                Hovering=mouseInside,HideText=textHidden,SaveFailed=saveFailed,FirstNumber=1,Percent=""};
            if(CurrentBook!=null) {
                f.Page=DrawPage;f.FirstNumber=CurrentBook.ParagraphIndexAt(CurrentOffset)+1;
                f.Percent=((double)CurrentPage.End/CurrentBook.Text.Length).ToString("P1");
            }
            skin.Chrome(f);
            Rectangle ta=TextArea;
            if(CurrentBook==null) {
                TextRenderer.DrawText(e.Graphics,"右键打开 TXT / EPUB，或把文件拖进来\n\n"+ActiveShortcut+"  隐藏 / 恢复\n托盘图标双击也能恢复",readingFont,ta,skin.Fore,TextFormatFlags.NoPrefix|TextFormatFlags.WordBreak);
            } else if(!textHidden) {
                // Lines slide out at the top edge of the text area. The clip starts exactly where the first line sits, so a line is
                // fully hidden by the time scrolling drops it (Skin.Flags includes PreserveGraphicsClipping; TextRenderer ignores the clip otherwise).
                e.Graphics.SetClip(new Rectangle(ta.X,ta.Y,ClientSize.Width-ta.X,ta.Height));int baseline=skin.Baseline(f);// punctuation may hang past the right edge
                foreach(VisualLine line in f.Page.Lines) {
                    int y=ta.Y+line.Y-pixel;if(y>ta.Bottom) break;
                    PaintMarks(e.Graphics,line,ta.X,y,baseline,true);
                    skin.Line(f,line,ta.X,y+baseline);
                }
                foreach(VisualLine line in f.Page.Lines) {int y=ta.Y+line.Y-pixel;if(y>ta.Bottom) break;PaintMarks(e.Graphics,line,ta.X,y,baseline,false);}
                e.Graphics.ResetClip();e.Graphics.TextRenderingHint=System.Drawing.Text.TextRenderingHint.SystemDefault;
            }
            skin.Overlay(f);
            if(toast!=null) using(Font small=Ui.Font(8.5f)) {
                Size t=TextRenderer.MeasureText(toast,small,Size.Empty,TextFormatFlags.NoPadding|TextFormatFlags.NoPrefix);
                int w=Math.Min(ClientSize.Width-Ui.S(16),t.Width+Ui.S(28)),h=t.Height+Ui.S(12);
                Rectangle r=new Rectangle((ClientSize.Width-w)/2,Math.Max(0,Math.Min(ClientSize.Height-h-Ui.S(28),(ClientSize.Height-h)/2+Ui.S(60))),w,h);
                Ui.FillRound(e.Graphics,Color.FromArgb(52,52,56),r,Math.Min(h/2f,Ui.S(14)));
                TextRenderer.DrawText(e.Graphics,toast,small,r,Color.White,TextFormatFlags.NoPrefix|TextFormatFlags.SingleLine|TextFormatFlags.VerticalCenter|TextFormatFlags.HorizontalCenter|TextFormatFlags.EndEllipsis);
            }
        }
        protected override void OnFormClosing(FormClosingEventArgs e) { Persist();base.OnFormClosing(e); }
        protected override void Dispose(bool disposing) {
            if(disposing) {
                ready=false;if(hotkeyRegistered && IsHandleCreated) UnregisterHotKey(Handle,HotkeyId);
                if(chordHook!=null) chordHook.Dispose();
                scrollTimer.Dispose();hoverTimer.Dispose();autoTimer.Dispose();if(saveTimer!=null) saveTimer.Dispose();if(tray!=null) {tray.Visible=false;tray.Dispose();}
                if(readingFont!=null) readingFont.Dispose();if(menu!=null) menu.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}

using System;
using System.IO;
using System.Windows.Forms;
using System.Drawing;
using System.Runtime.InteropServices;
using QuietReader;

class ProbeForm : ReaderForm {
    public ProbeForm(string state,string book):base(state,book) { QuietNotices=true; }
    public void Key(Keys key) { Message m=new Message();base.ProcessCmdKey(ref m,key);Application.DoEvents(); }
    public void Wheel(int delta) { base.OnMouseWheel(new MouseEventArgs(MouseButtons.None,0,50,50,delta));Application.DoEvents(); }
    public void Finish() { FinishScroll();Application.DoEvents(); }
    public void Drag(Point a,Point b) { OnMouseDown(new MouseEventArgs(MouseButtons.Left,1,a.X,a.Y,0));OnMouseMove(new MouseEventArgs(MouseButtons.Left,0,b.X,b.Y,0));OnMouseUp(new MouseEventArgs(MouseButtons.Left,1,b.X,b.Y,0));Application.DoEvents(); }
    public void GlobalToggle() { Message m=Message.Create(Handle,0x312,(IntPtr)19462,IntPtr.Zero);base.WndProc(ref m);Application.DoEvents(); }
    public void Style(string name) { foreach(ToolStripItem i in ContextMenuStrip.Items) {ToolStripMenuItem m=i as ToolStripMenuItem;if(m!=null && m.Text=="样式") m.DropDownItems[Array.IndexOf(ReaderState.Skins,name)].PerformClick();} Application.DoEvents(); }
}
class UITests {
    [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll")] static extern bool RegisterHotKey(IntPtr h,int id,uint modifiers,uint key);
    [DllImport("user32.dll")] static extern bool UnregisterHotKey(IntPtr h,int id);
    static void Check(bool value,string msg) { if(!value) throw new Exception(msg); }
    [STAThread] static int Main(string[] args) {
        Application.EnableVisualStyles();string dir=Path.Combine(Path.GetTempPath(),"QuietReaderUI-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(dir);
        try {
            bool blocked=RegisterHotKey(IntPtr.Zero,3456,0x4003,(uint)Keys.Space);
            try {
                string conflict=Path.Combine(dir,"conflict.xml");
                StateStore.Save(conflict,new ReaderState {Hotkey="Space"});
                using(ProbeForm f=new ProbeForm(conflict,args[0])) {
                    f.Show();Application.DoEvents();
                    Check(StateStore.Load(conflict).Hotkey!="Space","occupied shortcut switches to a persisted fallback");
                    f.Close();
                }
            } finally {if(blocked) UnregisterHotKey(IntPtr.Zero,3456);}
            using(ProbeForm f=new ProbeForm(Path.Combine(dir,"touchpad.xml"),args[0])) {
                f.Show();Application.DoEvents();int first=f.CurrentOffset;
                f.Wheel(-15);f.Finish();
                Check(f.CurrentOffset>first || f.ScrollPixel>0,"small touchpad movement scrolls immediately");
                for(int i=0;i<7;i++) f.Wheel(-15);f.Finish();
                Check(f.CurrentOffset>first,"one notch moves past the first line");
                int moved=f.CurrentOffset,px=f.ScrollPixel;
                for(int i=0;i<8;i++) f.Wheel(15);f.Finish();
                Check(f.CurrentOffset==first && f.ScrollPixel==0,"scrolling back returns exactly");
                for(int i=0;i<20;i++) f.Wheel(-1200);f.Finish();
                Check(f.CurrentOffset>moved,"flick scrolls far");
                int before=f.CurrentOffset;
                f.Key(Keys.PageDown);Check(f.CurrentOffset>before && f.ScrollPixel==0,"PageDown still advances a screen");
                f.Key(Keys.PageUp);Check(f.CurrentOffset==before,"PageUp screen history unaffected");f.Close();
            }
            foreach(string skin in ReaderState.Skins) {
                string skinState=Path.Combine(dir,skin+".xml");StateStore.Save(skinState,new ReaderState {Skin=skin,HideOnLeave=skin=="Code"});
                using(ProbeForm f=new ProbeForm(skinState,args[0])) {
                    f.Show();Application.DoEvents();int first=f.CurrentOffset;
                    f.Key(Keys.PageDown);Check(f.CurrentOffset>first,skin+": PageDown advances");
                    f.Key(Keys.PageUp);Check(f.CurrentOffset==first,skin+": PageUp returns exactly");
                    f.Wheel(-120);f.Finish();f.Wheel(120);f.Finish();Check(f.CurrentOffset==first && f.ScrollPixel==0,skin+": scroll round trip");
                    if(skin=="Clear") Check(f.TransparencyKey!=Color.Empty || f.Bounds.Contains(Cursor.Position),"clear style is see-through while the mouse is away");
                    f.Close();
                }
            }
            string state=Path.Combine(dir,"state.xml");int saved;
            using(ProbeForm f=new ProbeForm(state,args[0])) {
                f.Show();Application.DoEvents();
                Check(StateStore.Load(state).Hotkey=="Grave1","` + 1 default installed and persisted");
                Check(f.FormBorderStyle==FormBorderStyle.None && !f.ShowInTaskbar && f.TopMost,"frameless, taskbar and topmost");
                Check(f.CurrentBook.Chapters.Count>=3,"book loaded");int first=f.CurrentOffset;
                Check(first==f.CurrentBook.Chapters[0].Offset,"starts at chapter 1");
                f.Key(Keys.PageDown);Check(f.CurrentOffset>first,"next page advances");
                f.Key(Keys.PageUp);Check(f.CurrentOffset==first,"previous page returns exactly");
                f.Wheel(-120);f.Finish();saved=f.CurrentOffset;Check(saved>first,"wheel scrolls");
                f.Width=550;Application.DoEvents();Check(f.CurrentOffset==saved,"resize preserves anchor");
                int tall=f.Height;f.Style("Line");Check(f.Height<tall,"one-line style shrinks");f.Style("Note");Check(f.Height==tall,"leaving one-line restores height: "+f.Height+" vs "+tall);
                Clipboard.Clear();f.Drag(new Point(Ui.S(40),Ui.S(32)),new Point(Ui.S(200),Ui.S(32)));
                f.Key(Keys.Control|Keys.C);string copied=Clipboard.GetText();
                Check(copied.Length>=3 && f.CurrentBook.Text.Contains(copied),"drag-select then Ctrl+C copies book text: ["+copied+"]");
                f.Key(Keys.Escape);Check(!f.Visible,"Escape hides");f.Show();
                f.Close();
            }
            using(ProbeForm f=new ProbeForm(state,args[0])) {
                f.Show();Application.DoEvents();
                using(Form modal=new Form()) using(Timer timer=new Timer {Interval=100}) {
                    Exception problem=null;
                    timer.Tick+=delegate {
                        timer.Stop();
                        try {
                            f.GlobalToggle();Check(!IsWindowVisible(f.Handle) && !IsWindowVisible(modal.Handle),"global hide includes modal");
                            f.GlobalToggle();Check(IsWindowVisible(f.Handle) && IsWindowVisible(modal.Handle),"global restore includes modal");
                        } catch(Exception e) {problem=e;}
                        finally {modal.Close();}
                    };
                    timer.Start();modal.ShowDialog(f);if(problem!=null) throw problem;
                }
                f.Close();
            }
            using(Font font=new Font("Microsoft YaHei UI",10.5f)) {
                Page p=PageLayout.Build(new string('甲',100000),0,404,260,s=>TextRenderer.MeasureText(s,font,new Size(int.MaxValue,int.MaxValue),TextFormatFlags.NoPadding|TextFormatFlags.NoPrefix|TextFormatFlags.SingleLine).Width,22);
                Check(p.End<100000 && p.Lines.Count>1,"very long line must not be swallowed by GDI width overflow");
            }
            using(ProbeForm f=new ProbeForm(state,args[0])) {
                f.Show();Application.DoEvents();Check(f.CurrentOffset==saved,"resume after restart");
                Check(f.Width==550,"window settings restored");
                f.Key(Keys.Control|Keys.End);int end=f.CurrentOffset;f.Key(Keys.PageDown);Check(f.CurrentOffset==end,"EOF cannot loop");
                f.Key(Keys.PageUp);Check(f.CurrentOffset<end,"back from EOF");f.Close();
            }
            Console.WriteLine("PASS native window, touchpad fractional input and burst limiting, screen paging, hide, resize, restart, EOF, long-line GDI and modal hide/restore");return 0;
        } catch(Exception e) { Console.WriteLine("FAIL "+e);return 1; }
        finally { Directory.Delete(dir,true); }
    }
}

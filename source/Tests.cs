using System;
using System.IO;
using System.Text;
using QuietReader;

class Tests {
    static int passed, failed;
    static string dir;
    static void Check(bool value, string message) { if(!value) throw new Exception(message); }
    static void Run(string name, Action test) {
        try { test(); Console.WriteLine("PASS " + name); passed++; }
        catch(Exception e) { Console.WriteLine("FAIL " + name + ": " + e.Message); failed++; }
    }
    static string Fixture(string name, string text, Encoding encoding) {
        string p=Path.Combine(dir,name); File.WriteAllText(p,text,encoding); return p;
    }
    [STAThread] static int Main(string[] args) {
        dir=Path.Combine(Path.GetTempPath(),"QuietReaderTests-"+Guid.NewGuid().ToString("N")); Directory.CreateDirectory(dir);
        try {
            Run("Smooth scroll: small touchpad movement moves at once, full distance is delivered",delegate {
                SmoothScroll s=new SmoothScroll();s.Add(-15,90,1000); // 1/8 notch at 3 lines x 30px
                int first=s.Take();Check(first>0,"small movement is visible right away, got "+first);
                int total=first;for(int i=0;i<50 && s.Active;i++) total+=s.Take();
                Check(total==11,"1/8 notch of 90px ~ 11px, got "+total);
                s=new SmoothScroll();s.Add(-120,90,1000);total=0;int frames=0;
                while(s.Active && frames<100) {total+=s.Take();frames++;}
                Check(total==90 && frames<=15,"one notch = 3 lines within ~0.2 s, got "+total+" in "+frames);
            });
            Run("Smooth scroll: reversing and momentum limit",delegate {
                SmoothScroll s=new SmoothScroll();s.Add(-1200,90,300);
                int total=0;while(s.Active) total+=s.Take();Check(total==300,"flick capped at limit, got "+total);
                s.Add(-120,90,1000);s.Take();s.Add(120,90,1000);
                Check(s.Take()<0,"reverse responds immediately");
                s.Stop();Check(!s.Active && s.Take()==0,"stop clears");
            });
            Run("Tab Q consumes chord, toggles once, preserves standalone keys",delegate {
                KeyChord c=new KeyChord(9,81);
                Check(c.Process(81,true,false)==ChordAction.None,"ordinary Q down");c.Process(81,false,false);
                Check(c.Process(9,true,false)==ChordAction.Suppress,"defer Tab");
                Check(c.Process(81,true,false)==(ChordAction.Suppress|ChordAction.Toggle),"chord toggles");
                Check(c.Process(81,true,false)==ChordAction.Suppress,"repeat Q does not toggle");
                Check(c.Process(9,false,false)==ChordAction.Suppress,"chord Tab does not move focus");
                Check(c.Process(81,false,false)==ChordAction.Suppress,"Q up consumed");
                c.Process(9,true,false);Check(c.Process(9,false,false)==(ChordAction.Suppress|ChordAction.ReplayLead),"standalone Tab replayed on release");
                Check(c.Process(9,true,true)==ChordAction.None,"Shift/Ctrl Tab untouched");c.Process(9,false,true);
                c.Process(9,true,false);Check(c.Process(65,true,false)==ChordAction.ReplayLead,"other key flushes Tab before forwarding key");
                c.Process(65,false,false);Check(c.Process(9,false,false)==ChordAction.Suppress,"no extra Tab up after replay");
                c.Process(9,true,false);c.Process(81,true,false);c.Process(81,false,false);
                Check(c.Process(81,true,false)==(ChordAction.Suppress|ChordAction.Toggle),"fresh Q press can toggle again while Tab held");
            });
            Run("` + 1 chord toggles and replays a lone `",delegate {
                KeyChord c=new KeyChord(0xC0,0x31);
                Check(c.Process(0xC0,true,false)==ChordAction.Suppress,"defer `");
                Check(c.Process(0x31,true,false)==(ChordAction.Suppress|ChordAction.Toggle),"` + 1 toggles");
                c.Process(0x31,false,false);Check(c.Process(0xC0,false,false)==ChordAction.Suppress,"chord ` swallowed");
                c.Process(0xC0,true,false);Check(c.Process(0xC0,false,false)==(ChordAction.Suppress|ChordAction.ReplayLead),"lone ` typed on release");
                Check(c.Process(0x31,true,false)==ChordAction.None,"plain 1 untouched");c.Process(0x31,false,false);
                Check(c.Process(0xC0,true,true)==ChordAction.None,"Shift+` (~) untouched");
            });
            Run("EPUB text, chapters from table of contents, paragraph index",delegate {
                string p=Path.Combine(dir,"book.epub");
                using(var zip=new System.IO.Compression.ZipArchive(File.Create(p),System.IO.Compression.ZipArchiveMode.Create)) {
                    Action<string,string> add=(name,text)=>{using(var w=new StreamWriter(zip.CreateEntry(name).Open(),new UTF8Encoding(false))) w.Write(text);};
                    add("mimetype","application/epub+zip");
                    add("META-INF/container.xml","<?xml version=\"1.0\"?><container xmlns=\"urn:oasis:names:tc:opendocument:xmlns:container\" version=\"1.0\"><rootfiles><rootfile full-path=\"OEBPS/content.opf\" media-type=\"application/oebps-package+xml\"/></rootfiles></container>");
                    add("OEBPS/content.opf","<?xml version=\"1.0\"?><package xmlns=\"http://www.idpf.org/2007/opf\" version=\"2.0\"><manifest><item id=\"ncx\" href=\"toc.ncx\" media-type=\"application/x-dtbncx+xml\"/><item id=\"c1\" href=\"Text/c%201.xhtml\" media-type=\"application/xhtml+xml\"/><item id=\"c2\" href=\"Text/c2.xhtml\" media-type=\"application/xhtml+xml\"/></manifest><spine toc=\"ncx\"><itemref idref=\"c1\"/><itemref idref=\"c2\"/></spine></package>");
                    add("OEBPS/toc.ncx","<?xml version=\"1.0\"?><ncx xmlns=\"http://www.daisy.org/z3986/2005/ncx/\"><navMap><navPoint><navLabel><text>第一章 出发</text></navLabel><content src=\"Text/c%201.xhtml\"/></navPoint><navPoint><navLabel><text>第二章 归来</text></navLabel><content src=\"Text/c2.xhtml#top\"/></navPoint></navMap></ncx>");
                    add("OEBPS/Text/c 1.xhtml","<html><head><title>x</title><style>p{}</style></head><body><h1>第一章 出发</h1><p>他说：&ldquo;走吧&rdquo;</p><p>天亮了&amp;风停了<br/>第二行</p></body></html>");
                    add("OEBPS/Text/c2.xhtml","<html><body><p>没有标题的开头</p><p><img src=\"a.png\"/>结尾</p></body></html>");
                }
                Book b=Book.Load(p,"auto");
                Check(b.Chapters.Count==2 && b.Chapters[0].Title=="第一章 出发" && b.Chapters[1].Title=="第二章 归来","chapters from ncx: "+b.Chapters.Count);
                Check(b.Text=="第一章 出发\n他说：“走吧”\n天亮了&风停了\n第二行\n第二章 归来\n没有标题的开头\n结尾","text: "+b.Text.Replace("\n","|"));
                Check(b.Text.Substring(b.Chapters[1].Offset).StartsWith("第二章 归来"),"missing heading inserted at chapter start");
                Check(b.ParagraphIndexAt(0)==0 && b.ParagraphIndexAt(b.Chapters[1].Offset)==4 && b.ParagraphIndexAt(b.Text.Length-1)==6,"paragraph index");
            });
            Run("Updated copy of a serial resumes at the same passage",delegate {
                Check(BookMatch.Key("工位上的猫_20261019_090514")=="工位上的猫","timestamp stripped");
                Check(BookMatch.Key("某书(1)")==BookMatch.Key("某书 - 最新") && BookMatch.Key("某书")=="某书","copy markers stripped");
                string intro="书名：测试连载\n作者：某人\n简介："+new string('简',60)+"\n";
                Book v1=Book.Load(Fixture("serial_20261001_080000.txt",intro+"1、开始\n第一章正文\n2、相遇\n她在路口遇见了小当家，两人一起出发\n后面的正文\n3、旧章\n旧内容",new UTF8Encoding(false)),"auto");
                ReaderState s=new ReaderState();s.Opened(v1);int at=v1.Text.IndexOf("她在路口");s.Remember(v1,at);
                Book v2=Book.Load(Fixture("serial_20261019_090514.txt",intro+"1、开始\n第一章正文（修订）多了几个字\n2、相遇\n她在路口遇见了小当家，两人一起出发\n后面的正文\n3、旧章\n旧内容\n4、新章\n新内容\n5、又一章\n更多",new UTF8Encoding(false)),"auto");
                Bookmark old=s.PreviousCopy(v2);Check(old!=null,"newer copy recognised by name and opening");
                int moved=BookMatch.Relocate(v2,old);Check(v2.Text.Substring(moved).StartsWith("她在路口"),"same passage found, got "+moved);
                Check(v2.Chapters.Count-old.ChapterCount==2,"two new chapters");
                Book other=Book.Load(Fixture("另一本.txt","完全不同的书\n1、甲\n乙\n2、丙\n丁\n3、戊",new UTF8Encoding(false)),"auto");
                Check(s.PreviousCopy(other)==null,"different book not matched");
                Check(s.PreviousCopy(v1)==null,"same file is not its own older copy");
            });
            Run("Highlights: add, merge, remove, save, carry over to a newer copy",delegate {
                Book b=Book.Load(Fixture("hl.txt","简介\n1、开始\n她在路口遇见了小当家\n2、相遇\n两人一起出发\n3、结束\n完",new UTF8Encoding(false)),"auto");
                ReaderState s=new ReaderState();s.Opened(b);
                int at=b.Text.IndexOf("遇见");Highlight h=s.AddHighlight(b,at,at+2);
                Check(h.Text=="遇见" && h.Chapter=="1、开始","text and chapter kept");
                s.AddHighlight(b,at+1,at+5);Check(s.HighlightsOf(b.Identity).Count==1 && s.HighlightsOf(b.Identity)[0].Text=="遇见了小当","overlap merges");
                int go=b.Text.IndexOf("出发");s.AddHighlight(b,go,go+2);
                Check(s.HighlightsOf(b.Identity)[1].Chapter=="2、相遇","sorted, second chapter");
                Check(s.HighlightAt(b.Identity,go+1)!=null && s.HighlightAt(b.Identity,go+2)==null,"hit test");
                string p=Path.Combine(dir,"hl.xml");StateStore.Save(p,s);ReaderState r=StateStore.Load(p);
                Check(r.HighlightsOf(b.Identity).Count==2 && r.HighlightsOf(b.Identity)[1].Text=="出发","saved and loaded");
                Check(r.RemoveHighlights(b.Identity,go+1,go+1)==1 && r.HighlightsOf(b.Identity).Count==1,"click removes one");
                Book v2=Book.Load(Fixture("hl2.txt","简介（新版多了一句）\n1、开始\n她在路口遇见了小当家\n2、相遇\n两人一起出发\n3、结束\n完\n4、新章\n新",new UTF8Encoding(false)),"auto");
                var moved=BookMatch.MoveHighlights(v2,s.HighlightsOf(b.Identity));
                Check(moved.Count==2 && v2.Text.Substring(moved[0].Start,moved[0].Length)=="遇见了小当" && moved[1].Chapter=="2、相遇","moved to the new copy");
            });
            Run("Chapter lookup, search and recent books",delegate {
                Book b=Book.Load(Fixture("find.txt","简介\n1、开始\n戚许出门了\n2、相遇\n她遇见了小当家\n3、结束\n小当家又来了",new UTF8Encoding(false)),"auto");
                Check(b.ChapterIndexAt(0)==-1 && b.ChapterTitleAt(0)=="书籍信息 / 简介","before first chapter");
                Check(b.ChapterIndexAt(b.Chapters[1].Offset+3)==1,"inside chapter 2");
                bool more;var hits=b.Search("小当家",10,out more);
                Check(hits.Count==2 && !more,"two hits, got "+hits.Count);
                Check(hits[0].Chapter=="2、相遇" && b.Text.Substring(hits[0].Offset).StartsWith("她遇见"),"hit points at paragraph");
                Check(hits[0].Snippet.Substring(hits[0].MatchStart,hits[0].MatchLength)=="小当家","match range");
                hits=b.Search("小当家",1,out more);Check(hits.Count==1 && more,"limit reports more");
                ReaderState s=new ReaderState();s.Opened(b);
                Check(s.Recent(null,5).Count==1 && s.Recent(b.Identity,5).Count==0 && s.Recent(null,5)[0].Name=="find","recent list");
                string p=Path.Combine(dir,"recent.xml");StateStore.Save(p,s);Check(StateStore.Load(p).Recent(null,5)[0].Path==b.Path,"recent persisted");
            });
            Run("Upgrade old shortcut to Tab Q without losing progress",delegate {
                string p=Path.Combine(dir,"upgrade.xml");
                File.WriteAllText(p,"<ReaderState><Hotkey>Z</Hotkey><FontSize>14</FontSize><Books><Bookmark><Identity>abc</Identity><Offset>456</Offset></Bookmark></Books></ReaderState>");
                ReaderState s=StateStore.Load(p);Check(s.Hotkey=="Grave1" && s.Position("abc")==456 && s.FontSize==14,"upgrade retains reading state");
                s.Hotkey="Q";StateStore.Save(p,s);Check(StateStore.Load(p).Hotkey=="Q","later explicit selection retained");
                Check(new ReaderState().Hotkey=="Grave1","new default");
                File.WriteAllText(p,"<ReaderState><Hotkey>TabQ</Hotkey><HotkeyVersion>1</HotkeyVersion></ReaderState>");Check(StateStore.Load(p).Hotkey=="Grave1","older defaults move to ` + 1");
                s=new ReaderState();s.Hotkey="TabQ";StateStore.Save(p,s);Check(StateStore.Load(p).Hotkey=="TabQ","Tab+Q still selectable");
            });
            Run("UTF8 paragraphs and chapter boundaries",delegate {
                Book b=Book.Load(Fixture("a.txt","书名：测试\r\n\r\n第1章 开始\r\n\r\n正文甲\r\n\r\n\r\n第2章 结束\n正文乙",new UTF8Encoding(false)),"auto");
                Check(b.EncodingName=="UTF-8","UTF8 detection"); Check(b.Chapters.Count==2,"chapter count");
                Check(b.Text=="书名：测试\n第1章 开始\n正文甲\n第2章 结束\n正文乙","paragraph normalization");
                Check(b.Text.Substring(b.Chapters[1].Offset).StartsWith("第2章"),"chapter offset");
            });
            Run("Numbered chapter titles skip in-text lists",delegate {
                Book b=Book.Load(Fixture("num.txt","简介\n1、公路求生\n正文\n2、开始上路\n奖励如下：\n1、能量石\n2、求生币\n3、乌漆嘛黑，让你变美！\n正文\n5、工作证\n正文\n6、30分钟\n1.5倍奖励",new UTF8Encoding(false)),"auto");
                Check(b.Chapters.Count==5,"expected 5, got "+b.Chapters.Count);
                Check(b.Chapters[2].Title=="3、乌漆嘛黑，让你变美！" && b.Chapters[3].Title=="5、工作证","titles and small gap");
                Check(b.Text.Substring(b.Chapters[1].Offset).StartsWith("2、开始上路"),"numbered offset");
            });
            Run("Chinese encodings and BOM",delegate {
                foreach(Encoding enc in new Encoding[]{new UnicodeEncoding(false,true,true),new UnicodeEncoding(true,true,true),new UTF32Encoding(false,true,true),Encoding.GetEncoding("gb18030")}) {
                    Book b=Book.Load(Fixture(enc.CodePage+".txt","第1章 开始\n中文正文",enc),"auto");
                    Check(b.Text=="第1章 开始\n中文正文","decode "+enc.EncodingName);
                }
            });
            Run("Renamed file retains identity",delegate {
                string p=Fixture("id.txt","第1章 甲\n正文",Encoding.UTF8); Book a=Book.Load(p,"auto");
                string q=Path.Combine(dir,"renamed.txt"); File.Copy(p,q); Book b=Book.Load(q,"auto");
                Check(a.Identity.Length==64 && a.Identity==b.Identity,"identity must be content hash");
            });
            Run("Pagination wraps without dropping text",delegate {
                string s="一二三四五六七八九十\n甲乙丙丁";
                Page p=PageLayout.Build(s,0,4,30,x=>x.Length,10);
                Check(p.Lines.Count==3,"3 visible lines"); Check(p.Lines[0].End==4,"wrap width");
                Check(p.Next==8 && p.End==11,"one line overlap and newline consumed");
                Page q=PageLayout.Build(s,p.Next,4,30,x=>x.Length,10);
                Check(q.End==s.Length,"tail included");
            });
            Run("Closing punctuation never starts a line",delegate {
                Page p=PageLayout.Build("一二三，。四五",0,3,100,x=>x.Length,10);
                Check(p.Lines[0].End==5 && p.Lines[1].Start==5,"punctuation hangs on the previous line");
                p=PageLayout.Build("一二三四五六",0,3,100,x=>x.Length,10);Check(p.Lines[0].End==3,"plain text unchanged");
            });
            Run("Narrow windows always advance, surrogate safe",delegate {
                string s="😀甲乙"; Page p=PageLayout.Build(s,0,0,1,x=>x.Length,10);
                Check(p.Lines.Count==1 && p.Lines[0].End==2,"surrogate preserved"); Check(p.Next>=2,"always advance");
                Check(PageLayout.Build("",0,10,10,x=>x.Length,10).End==0,"empty text");
            });
            Run("Bookmarks roundtrip and previous-state recovery",delegate {
                ReaderState s=new ReaderState(); s.SetPosition("abc",123); s.Opacity=54; s.FontSize=14; s.LastPath="中文.txt";
                string p=Path.Combine(dir,"state.xml"); StateStore.Save(p,s);
                ReaderState loaded=StateStore.Load(p); Check(loaded.Position("abc")==123 && loaded.Opacity==54 && loaded.LastPath=="中文.txt","roundtrip");
                s.SetPosition("abc",456); StateStore.Save(p,s); File.WriteAllText(p,"broken xml");
                Check(StateStore.Load(p).Position("abc")==123,"backup recovery");
            });
            Run("Malformed settings constrained",delegate {
                ReaderState s=new ReaderState();s.Opacity=0;s.Width=-1;s.Height=1;s.FontSize=999;s.Hotkey="bad";
                string p=Path.Combine(dir,"bad.xml"); StateStore.Save(p,s); ReaderState r=StateStore.Load(p);
                Check(r.Opacity>=30 && r.Width>=240 && r.Height>=24 && r.FontSize<=32 && r.Hotkey=="Grave1","clamp");
            });
            Run("Per-book manual encoding survives restart and rename",delegate {
                Book b=Book.Load(Fixture("traditional.txt","第1章 開始\n中文正文",Encoding.GetEncoding("big5")),"big5");
                ReaderState s=new ReaderState();s.SetPosition(b.Identity,3);s.SetEncoding(b.Identity,"big5");
                string p=Path.Combine(dir,"enc.xml");StateStore.Save(p,s);s=StateStore.Load(p);
                Check(s.EncodingFor(b.Identity)=="big5","remember explicit encoding per content");
                Check(Book.Load(b.Path,s.EncodingFor(Book.IdentityOf(b.Path))).Text=="第1章 開始\n中文正文","same bytes correctly reopened");
            });
            Run("Nil settings recover backup",delegate {
                string p=Path.Combine(dir,"nil.xml");ReaderState s=new ReaderState();s.SetPosition("nil",89);
                StateStore.Save(p,s);StateStore.Save(p,s);
                File.WriteAllText(p,"<ReaderState xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\" xsi:nil=\"true\" />");
                Check(StateStore.Load(p).Position("nil")==89,"nil primary recovers backup");
            });
            if(args.Length>0) Run("Sample novel chapters are sequential",delegate {
                Book b=Book.Load(args[0],"auto"); Check(b.Chapters.Count>=3,"expected chapters, got "+b.Chapters.Count);
                for(int i=0;i<b.Chapters.Count;i++) Check(b.Chapters[i].Title.StartsWith("第"+(i+1)+"章 "),"sequence "+i);
                Check(b.Text.Substring(b.Chapters[0].Offset).StartsWith(b.Chapters[0].Title),"first chapter offset");
            });
        } finally { Directory.Delete(dir,true); }
        Console.WriteLine(passed+" passed, "+failed+" failed"); return failed==0?0:1;
    }
}

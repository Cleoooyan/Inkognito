using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Security.Cryptography;
using System.Xml;
using System.Xml.Serialization;

namespace QuietReader {
    // Smooth scrolling: wheel / touchpad deltas become a pixel distance that is played out over a few frames.
    public sealed class SmoothScroll {
        double pending;
        // delta is in wheel units (120 per notch); positive pixels scroll forward.
        public void Add(int delta,double pixelsPerNotch,double limit) {
            if(delta==0) return;
            double px=-delta/120.0*pixelsPerNotch;
            if(pending!=0 && Math.Sign(px)!=Math.Sign(pending)) pending=0; // reversing responds at once
            pending=Math.Max(-limit,Math.Min(limit,pending+px));
        }
        public bool Active {get {return Math.Abs(pending)>=1;}}
        // Pixels to move this frame; sub-pixel remainders wait for the next movement.
        public int Take() {
            if(!Active) return 0;
            double step=pending*0.35;
            if(Math.Abs(step)<1) step=Math.Sign(pending);
            int px=(int)Math.Round(step);pending-=px;return px;
        }
        public int TakeAll() {int px=(int)Math.Truncate(pending);pending-=px;return px;}
        public void Stop() {pending=0;}
    }
    [Flags] public enum ChordAction { None=0, Suppress=1, ReplayLead=2, Toggle=4 }
    // Hold a lead key (Tab, `) and press a trigger key (Q, 1). The lead key alone is replayed on release.
    public sealed class KeyChord {
        // Only retain chord state; no typed text is stored.
        public readonly int Lead, Trigger;
        int tabMode; // 0 up, 1 deferred, 2 chord, 3 replayed tap, 4 passed through
        bool qDown,qConsumed;
        public KeyChord(int lead,int trigger) {Lead=lead;Trigger=trigger;}
        public ChordAction Process(int key,bool down,bool modified) {
            if(key==Lead) {
                if(down) {
                    if(tabMode==0) {tabMode=modified?4:1;return modified?ChordAction.None:ChordAction.Suppress;}
                    if(tabMode==1) {tabMode=4;return ChordAction.ReplayLead;}
                    return tabMode==4?ChordAction.None:ChordAction.Suppress;
                }
                int prior=tabMode;tabMode=0;
                if(prior==1) return ChordAction.Suppress|ChordAction.ReplayLead;
                return prior==2 || prior==3?ChordAction.Suppress:ChordAction.None;
            }
            if(key==Trigger) {
                if(!down) {qDown=false;bool consumed=qConsumed;qConsumed=false;return consumed?ChordAction.Suppress:ChordAction.None;}
                if(qDown) return qConsumed?ChordAction.Suppress:ChordAction.None;
                qDown=true;
                if((tabMode==1 || tabMode==2) && !modified) {tabMode=2;qConsumed=true;return ChordAction.Suppress|ChordAction.Toggle;}
            }
            if(down && tabMode==1) {tabMode=3;return ChordAction.ReplayLead;}
            if(down && tabMode==2) tabMode=3;
            return ChordAction.None;
        }
    }
    public sealed class Chapter { public string Title; public int Offset; }
    public sealed class SearchHit { public int Offset, MatchStart, MatchLength; public string Snippet, Chapter; }
    public sealed class Book {
        public string Text = "", EncodingName = "", Identity = "", Path = "";
        public List<Chapter> Chapters = new List<Chapter>();
        public string Name {get {return System.IO.Path.GetFileNameWithoutExtension(Path);}}
        // Index of the chapter containing offset; -1 before the first chapter (title page / blurb).
        public int ChapterIndexAt(int offset) {
            int lo=0,hi=Chapters.Count-1,found=-1;
            while(lo<=hi) {int mid=(lo+hi)/2;if(Chapters[mid].Offset<=offset) {found=mid;lo=mid+1;} else hi=mid-1;}
            return found;
        }
        int[] paragraphs;
        // Zero-based paragraph number at offset (used for fake line / row numbers in the disguise styles).
        public int ParagraphIndexAt(int offset) {
            if(paragraphs==null) {
                List<int> starts=new List<int> {0};
                for(int i=Text.IndexOf('\n');i>=0;i=Text.IndexOf('\n',i+1)) starts.Add(i+1);
                paragraphs=starts.ToArray();
            }
            int found=Array.BinarySearch(paragraphs,offset);return found>=0?found:~found-1;
        }
        public bool IsParagraphStart(int offset) {return offset==0 || (offset<=Text.Length && Text[offset-1]=='\n');}
        public string ChapterTitleAt(int offset) {int i=ChapterIndexAt(offset);return i<0?"书籍信息 / 简介":Chapters[i].Title;}
        // Snippets stay inside one paragraph; Offset is the paragraph start so the hit shows at the top of the page.
        public List<SearchHit> Search(string query,int limit,out bool more) {
            List<SearchHit> hits=new List<SearchHit>();more=false;
            if(string.IsNullOrEmpty(query)) return hits;
            int pos=0;
            while((pos=Text.IndexOf(query,pos,StringComparison.OrdinalIgnoreCase))>=0) {
                if(hits.Count==limit) {more=true;break;}
                int para=pos==0?0:Text.LastIndexOf('\n',pos-1)+1;
                int paraEnd=Text.IndexOf('\n',pos);if(paraEnd<0) paraEnd=Text.Length;
                int from=Math.Max(para,pos-16),to=Math.Min(paraEnd,pos+query.Length+40);
                string snippet=(from>para?"…":"")+Text.Substring(from,to-from)+(to<paraEnd?"…":"");
                hits.Add(new SearchHit {Offset=para,MatchStart=pos-from+(from>para?1:0),MatchLength=query.Length,Snippet=snippet,Chapter=ChapterTitleAt(pos)});
                pos+=query.Length;
            }
            return hits;
        }
        public static string IdentityOf(string path) {
            using(FileStream file=File.OpenRead(path)) using(SHA256 sha=SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(file)).Replace("-","").ToLowerInvariant();
        }
        static bool Prefix(byte[] bytes, params byte[] prefix) {
            if(bytes.Length<prefix.Length) return false;
            for(int i=0;i<prefix.Length;i++) if(bytes[i]!=prefix[i]) return false;
            return true;
        }
        public static Book Load(string path, string encoding) {
            if(path.EndsWith(".epub",StringComparison.OrdinalIgnoreCase)) return Epub.Load(path);
            FileInfo info=new FileInfo(path);
            if(info.Length>50*1024*1024) throw new InvalidDataException("请选择小于 50 MiB 的 TXT / EPUB 文件。");
            byte[] bytes=File.ReadAllBytes(path); string text, name; Encoding decoder; int skip=0;
            if(encoding!="auto") { decoder=Encoding.GetEncoding(encoding,EncoderFallback.ExceptionFallback,DecoderFallback.ExceptionFallback); name=decoder.WebName; }
            else if(Prefix(bytes,0xff,0xfe,0,0)) { decoder=new UTF32Encoding(false,false,true);skip=4;name="UTF-32 LE"; }
            else if(Prefix(bytes,0,0,0xfe,0xff)) { decoder=new UTF32Encoding(true,false,true);skip=4;name="UTF-32 BE"; }
            else if(Prefix(bytes,0xff,0xfe)) { decoder=new UnicodeEncoding(false,false,true);skip=2;name="UTF-16 LE"; }
            else if(Prefix(bytes,0xfe,0xff)) { decoder=new UnicodeEncoding(true,false,true);skip=2;name="UTF-16 BE"; }
            else {
                decoder=new UTF8Encoding(false,true); name="UTF-8";
                try { decoder.GetString(bytes); }
                catch(DecoderFallbackException) { decoder=Encoding.GetEncoding("gb18030",EncoderFallback.ExceptionFallback,DecoderFallback.ExceptionFallback);name="GB18030"; }
                if(name=="UTF-8" && Prefix(bytes,0xef,0xbb,0xbf)) skip=3;
            }
            text=decoder.GetString(bytes,skip,bytes.Length-skip).TrimStart('\ufeff');
            if(text.IndexOf('\0')>=0) throw new InvalidDataException("文件包含 NUL，可能不是 TXT 或编码不正确。请指定编码重新打开。");
            Book b=new Book();b.Path=System.IO.Path.GetFullPath(path); b.EncodingName=name;
            using(SHA256 sha=SHA256.Create()) b.Identity=BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-","").ToLowerInvariant();
            StringBuilder sb=new StringBuilder(text.Length);
            Regex heading=new Regex(@"^(?:第[零〇一二三四五六七八九十百千万两\d]+[章节回卷部篇集].*|(?:序章|楔子|引子|尾声|后记|番外)(?:\s.*|[：:、].*|\d.*)?)$");
            // Some books title chapters as "1、标题" / "1. 标题". Numbered lists inside the text use the
            // same shape, so only accept numbers that keep climbing (small gaps allowed for missing chapters).
            Regex numbered=new Regex(@"^(\d{1,5})\s*(?:、|[.．](?!\d))\s*\S");
            List<Chapter> numberedChapters=new List<Chapter>(); int lastNumber=0;
            foreach(string raw in text.Replace("\r\n","\n").Replace('\r','\n').Split('\n')) {
                string line=raw.Trim(); if(line.Length==0) continue;
                if(sb.Length>0) sb.Append('\n');
                if(line.Length<=65 && heading.IsMatch(line)) b.Chapters.Add(new Chapter {Title=line,Offset=sb.Length});
                else if(line.Length<=40) {
                    Match m=numbered.Match(line); int n;
                    if(m.Success && int.TryParse(m.Groups[1].Value,out n) && n>lastNumber && n<=lastNumber+(lastNumber==0?3:10)) {
                        numberedChapters.Add(new Chapter {Title=line,Offset=sb.Length}); lastNumber=n;
                    }
                }
                sb.Append(line);
            }
            if(numberedChapters.Count>=3 && numberedChapters.Count>b.Chapters.Count) b.Chapters=numberedChapters;
            b.Text=sb.ToString();return b;
        }
    }
    public sealed class VisualLine { public int Start, End, Next, Y; }
    public sealed class Page {
        public List<VisualLine> Lines = new List<VisualLine>();
        public int Next, End;
    }
    public static class PageLayout {
        const string NoLineStart="，。、；：？！”’）》」』】〉…—,.;:?!)]}%";
        // gap: extra space after a paragraph; -1 means the note style default (a quarter line).
        public static Page Build(string text, int start, int width, int height, Func<string,int> measure, int lineHeight, int gap=-1) {
            if(gap<0) gap=Math.Max(3,lineHeight/4);
            Page page=new Page();int pos=Math.Max(0,Math.Min(start,text.Length));
            if(pos>0 && pos<text.Length && char.IsLowSurrogate(text[pos]) && char.IsHighSurrogate(text[pos-1])) pos--;
            int y=0;lineHeight=Math.Max(1,lineHeight);width=Math.Max(1,width);
            while(pos<text.Length && (y+lineHeight<=height || page.Lines.Count==0)) {
                if(text[pos]=='\n') { pos++;y+=gap;continue; }
                int end=text.IndexOf('\n',pos);if(end<0) end=text.Length;
                // Probe from a small prefix: GDI can return zero for enormous strings.
                // Bound each measurement so a failed native width cannot swallow a whole paragraph.
                int lo=1,hi=1,best=0,remaining=end-pos;
                while(hi<remaining && hi<512 && measure(text.Substring(pos,hi))<=width) hi=Math.Min(remaining,hi*2);
                while(lo<=hi) { int mid=(lo+hi)/2; if(measure(text.Substring(pos,mid))<=width) { best=mid;lo=mid+1; } else hi=mid-1; }
                if(best==0) best=1;
                // Chinese line breaking: closing punctuation never starts a line; it hangs at the end of the previous one.
                for(int hang=0;hang<2 && pos+best<end && NoLineStart.IndexOf(text[pos+best])>=0;hang++) best++;
                if(pos+best<end && char.IsHighSurrogate(text[pos+best-1]) && char.IsLowSurrogate(text[pos+best])) { if(best>1) best--;else best=2; }
                int next=pos+best;bool newline=next<text.Length && text[next]=='\n';
                if(newline) next++;
                page.Lines.Add(new VisualLine {Start=pos,End=pos+best,Next=next,Y=y});pos=next;
                y+=lineHeight+(newline?gap:0);
            }
            page.End=pos;
            page.Next=page.Lines.Count>1?page.Lines[page.Lines.Count-1].Start:pos;
            return page;
        }
    }
    public sealed class Bookmark {
        public string Identity="", Encoding="auto", Path="", Name="";
        public int Offset, Length;
        public long LastRead;
        // Used to recognise a newer copy of the same serial and find the same passage in it.
        public string Head="", Anchor="", ChapterTitle="";
        public int ChapterCount;
        public List<Highlight> Highlights=new List<Highlight>();
    }
    // An underlined passage. Text and chapter are kept so the list works without opening the book.
    public sealed class Highlight {
        public int Start, Length;
        public string Text="", Chapter="";
        public long Created;
        public int End {get {return Start+Length;}}
    }
    // Picks up a newer version of the same book (a re-downloaded serial) at the passage last read.
    public static class BookMatch {
        static readonly Regex Suffix=new Regex(@"(?:[\s_\-.]*(?:\d{8}(?:[_\-]?\d{4,6})?|\d{4}[\-_.]\d{1,2}[\-_.]\d{1,2}|[(（]\d+[)）]|副本|copy|最新|更新|完结|全本))+$",RegexOptions.IgnoreCase);
        // File name without dates, copy numbers and "latest / complete" tags.
        public static string Key(string name) {
            string n=name??"";n=Suffix.Replace(n,"");return n.Trim().ToLowerInvariant();
        }
        public const int HeadLength=200, AnchorLength=40;
        public static string HeadOf(Book b) {return b.Text.Substring(0,Math.Min(HeadLength,b.Text.Length));}
        public static bool SameBook(Bookmark old,Book b) {
            if(old.Identity==b.Identity) return false;
            if(!string.IsNullOrEmpty(old.Head) && old.Head.Length>=50 && b.Text.StartsWith(old.Head,StringComparison.Ordinal)) return true;
            string oldName=!string.IsNullOrEmpty(old.Name)?old.Name:System.IO.Path.GetFileNameWithoutExtension(old.Path??"");
            string key=Key(oldName);return key.Length>=2 && key==Key(b.Name);
        }
        // Underlines from an older copy, found again by their text (the occurrence nearest the old position).
        public static List<Highlight> MoveHighlights(Book b,List<Highlight> old) {
            List<Highlight> moved=new List<Highlight>();
            foreach(Highlight h in old) {
                if(string.IsNullOrEmpty(h.Text)) continue;int best=-1;
                for(int i=b.Text.IndexOf(h.Text,StringComparison.Ordinal);i>=0;i=b.Text.IndexOf(h.Text,i+1,StringComparison.Ordinal))
                    if(best<0 || Math.Abs(i-h.Start)<Math.Abs(best-h.Start)) best=i;
                if(best>=0) moved.Add(new Highlight {Start=best,Length=h.Text.Length,Text=h.Text,Chapter=b.ChapterTitleAt(best),Created=h.Created});
            }
            moved.Sort((x,y)=>x.Start.CompareTo(y.Start));return moved;
        }
        // The passage read last time, nearest to its old offset; then the same chapter title; then the old offset.
        public static int Relocate(Book b,Bookmark old) {
            foreach(int len in new int[]{AnchorLength,16}) {
                if(string.IsNullOrEmpty(old.Anchor) || old.Anchor.Length<len) continue;
                string a=old.Anchor.Substring(0,len);int best=-1;
                for(int i=b.Text.IndexOf(a,StringComparison.Ordinal);i>=0;i=b.Text.IndexOf(a,i+1,StringComparison.Ordinal))
                    if(best<0 || Math.Abs(i-old.Offset)<Math.Abs(best-old.Offset)) best=i;
                if(best>=0) return best;
            }
            if(!string.IsNullOrEmpty(old.ChapterTitle)) foreach(Chapter c in b.Chapters) if(c.Title==old.ChapterTitle) return c.Offset;
            return Math.Max(0,Math.Min(old.Offset,b.Text.Length-1));
        }
    }
    public sealed class ReaderState {
        public string LastPath="", FontName="Microsoft YaHei UI", Hotkey="Grave1";
        public int HotkeyVersion;
        public string ShortcutText {get {return TextFor(Hotkey);}}
        public static readonly string[] HotkeyOptions={"Grave1","WinGrave","TabQ","Space","Z","Q"};
        public static string TextFor(string hotkey) {return hotkey=="Grave1"?"` + 1":hotkey=="WinGrave"?"Win + `":hotkey=="TabQ"?"Tab + Q":"Ctrl + Alt + "+hotkey;}
        public float FontSize=10.5f;
        public int X=100,Y=100,Width=440,Height=300,Opacity=92;
        public bool Topmost=true;
        // Style: Note (default), Clear (text only, background see-through), Excel, Code (dark), CodeLight.
        public string Skin="Note";
        public static readonly string[] Skins={"Note","Clear","Line","Excel","Code","CodeLight"};
        public static string SkinName(string skin) {return skin=="Clear"?"透明（只显示文字）":skin=="Excel"?"Excel 表格":skin=="Code"?"代码编辑器（深色）":skin=="CodeLight"?"代码编辑器（白色）":skin=="Line"?"单行":"便笺";}
        // Note / Clear colours as ARGB; the disguise styles use their own palette.
        public int TextArgb=unchecked((int)0xFF484B48), BackArgb=unchecked((int)0xFFF4F4F1);
        public bool HideOnLeave;
        // Keep the reader out of screenshots, recordings and shared screens (Windows 10 2004+).
        public bool HideFromCapture=true;
        // Auto-scroll speed 1-10; FullHeight is the window height to restore when leaving the one-line style.
        public int AutoSpeed=5, FullHeight;
        public List<Bookmark> Books=new List<Bookmark>();
        public int Position(string id) { Bookmark b=Books.Find(x=>x.Identity==id);return b==null?0:Math.Max(0,b.Offset); }
        public bool HasPosition(string id) { return Books.Exists(x=>x.Identity==id); }
        public string EncodingFor(string id) { Bookmark b=Books.Find(x=>x.Identity==id);return b==null || string.IsNullOrEmpty(b.Encoding)?"auto":b.Encoding; }
        public void SetEncoding(string id,string encoding) { Bookmark b=Books.Find(x=>x.Identity==id);if(b==null) {b=new Bookmark {Identity=id};Books.Add(b);}b.Encoding=encoding; }
        public void SetPosition(string id, int offset) {
            Bookmark b=Books.Find(x=>x.Identity==id);
            if(b==null) { b=new Bookmark {Identity=id};Books.Add(b); } b.Offset=Math.Max(0,offset);
        }
        // Remember where a book lives so it can appear under "最近阅读".
        public void Opened(Book book) {
            SetPosition(book.Identity,Position(book.Identity));Bookmark b=Books.Find(x=>x.Identity==book.Identity);
            b.Path=book.Path;b.Name=book.Name;b.Length=book.Text.Length;b.LastRead=DateTime.UtcNow.Ticks;
        }
        // Saves the position together with what is needed to find it again in a newer copy of the book.
        public void Remember(Book book,int offset) {
            SetPosition(book.Identity,offset);Bookmark b=Books.Find(x=>x.Identity==book.Identity);
            offset=Math.Max(0,Math.Min(offset,book.Text.Length));
            b.Anchor=book.Text.Substring(offset,Math.Min(BookMatch.AnchorLength,book.Text.Length-offset));
            b.ChapterTitle=book.ChapterIndexAt(offset)<0?"":book.ChapterTitleAt(offset);b.ChapterCount=book.Chapters.Count;b.Head=BookMatch.HeadOf(book);
        }
        // The most recently read bookmark that looks like an older copy of this book, or null.
        public Bookmark PreviousCopy(Book book) {
            if(HasPosition(book.Identity)) return null;
            Bookmark best=null;
            foreach(Bookmark b in Books) if(BookMatch.SameBook(b,book) && (best==null || b.LastRead>best.LastRead)) best=b;
            return best;
        }
        public List<Highlight> HighlightsOf(string id) {Bookmark b=Books.Find(x=>x.Identity==id);return b==null?new List<Highlight>():b.Highlights;}
        public Highlight HighlightAt(string id,int pos) {return HighlightsOf(id).Find(h=>h.Start<=pos && pos<h.End);}
        // Underlines [start,end); overlapping or touching underlines merge into one. Kept sorted by position.
        public Highlight AddHighlight(Book book,int start,int end) {
            start=Math.Max(0,Math.Min(start,book.Text.Length));end=Math.Max(0,Math.Min(end,book.Text.Length));
            if(end<=start) return null;
            Bookmark b=Books.Find(x=>x.Identity==book.Identity);if(b==null) {b=new Bookmark {Identity=book.Identity};Books.Add(b);}
            foreach(Highlight old in b.Highlights.FindAll(h=>h.Start<=end && h.End>=start)) {start=Math.Min(start,old.Start);end=Math.Max(end,old.End);b.Highlights.Remove(old);}
            Highlight added=new Highlight {Start=start,Length=end-start,Text=book.Text.Substring(start,end-start),Chapter=book.ChapterTitleAt(start),Created=DateTime.Now.Ticks};
            b.Highlights.Add(added);b.Highlights.Sort((x,y)=>x.Start.CompareTo(y.Start));return added;
        }
        // Removes underlines touching [start,end) (a click is start==end inside one).
        public int RemoveHighlights(string id,int start,int end) {
            Bookmark b=Books.Find(x=>x.Identity==id);if(b==null) return 0;
            return b.Highlights.RemoveAll(h=>end>start?(h.Start<end && h.End>start):(h.Start<=start && start<h.End));
        }
        public List<Bookmark> Recent(string exceptIdentity,int count) {
            List<Bookmark> list=Books.FindAll(x=>!string.IsNullOrEmpty(x.Path) && x.Identity!=exceptIdentity);
            list.Sort((a,b)=>b.LastRead.CompareTo(a.LastRead));
            if(list.Count>count) list.RemoveRange(count,list.Count-count);return list;
        }
        public void Validate() {
            Width=Math.Max(240,Math.Min(2000,Width));Height=Math.Max(24,Math.Min(2000,Height));AutoSpeed=Math.Max(1,Math.Min(10,AutoSpeed));
            Opacity=Math.Max(30,Math.Min(100,Opacity));
            if(float.IsNaN(FontSize)||float.IsInfinity(FontSize)) FontSize=10.5f;
            FontSize=Math.Max(8,Math.Min(32,FontSize));
            if(string.IsNullOrWhiteSpace(FontName)) FontName="Microsoft YaHei UI";
            if(Array.IndexOf(HotkeyOptions,Hotkey)<0) Hotkey="Grave1";
            HotkeyVersion=3;
            if(Array.IndexOf(Skins,Skin)<0) Skin="Note";
            TextArgb|=unchecked((int)0xFF000000);BackArgb|=unchecked((int)0xFF000000);
            if(Books==null) Books=new List<Bookmark>();Books.RemoveAll(x=>x==null || x.Identity==null);
            foreach(Bookmark b in Books) {if(b.Highlights==null) b.Highlights=new List<Highlight>();b.Highlights.RemoveAll(h=>h==null || h.Length<=0);}
        }
    }
    public static class StateStore {
        public static ReaderState Load(string path) {
            foreach(string candidate in new string[]{path,path+".bak"}) {
                try {
                    using(XmlReader r=XmlReader.Create(candidate,new XmlReaderSettings { DtdProcessing=DtdProcessing.Prohibit,XmlResolver=null })) {
                        ReaderState s=(ReaderState)new XmlSerializer(typeof(ReaderState)).Deserialize(r);if(s==null) continue;
                        // v3: user chose ` + 1 (2026-10-19); earlier defaults were Tab+Q and Win+`.
                        if(s.HotkeyVersion<3) s.Hotkey="Grave1";
                        s.Validate();return s;
                    }
                } catch(IOException) { } catch(InvalidOperationException) { } catch(XmlException) { } catch(UnauthorizedAccessException) { }
            }
            return new ReaderState();
        }
        public static void Save(string path, ReaderState state) {
            state.Validate();Directory.CreateDirectory(System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(path)));
            string tmp=path+"."+Guid.NewGuid().ToString("N")+".tmp";
            try {
                using(FileStream fs=new FileStream(tmp,FileMode.CreateNew,FileAccess.Write,FileShare.None)) {
                    new XmlSerializer(typeof(ReaderState)).Serialize(fs,state);fs.Flush(true);
                }
                if(File.Exists(path)) File.Replace(tmp,path,path+".bak",true);else File.Move(tmp,path);
            } finally { if(File.Exists(tmp)) File.Delete(tmp); }
        }
    }
}

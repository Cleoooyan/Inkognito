using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;

namespace QuietReader {
    // Minimal EPUB 2/3 text extraction: spine order, chapter titles from the table of contents (or the first heading).
    // Images, styles and scripts are dropped; the reader only shows text.
    public static class Epub {
        static readonly Regex Head=new Regex(@"<(head|script|style)\b.*?</\1\s*>",RegexOptions.IgnoreCase|RegexOptions.Singleline);
        static readonly Regex Block=new Regex(@"<br\s*/?>|</?(p|div|h[1-6]|li|tr|blockquote|section|article|dt|dd)\b[^>]*>",RegexOptions.IgnoreCase);
        static readonly Regex Tag=new Regex(@"<[^>]+>");
        static readonly Regex Heading=new Regex(@"<h[1-6]\b[^>]*>(.*?)</h[1-6]\s*>",RegexOptions.IgnoreCase|RegexOptions.Singleline);
        public static Book Load(string path) {
            byte[] bytes=File.ReadAllBytes(path);
            Book b=new Book {Path=System.IO.Path.GetFullPath(path),EncodingName="EPUB"};
            using(SHA256 sha=SHA256.Create()) b.Identity=BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-","").ToLowerInvariant();
            using(ZipArchive zip=new ZipArchive(new MemoryStream(bytes),ZipArchiveMode.Read)) {
                XmlDocument container=Xml(zip,"META-INF/container.xml");
                if(container==null) throw new InvalidDataException("不是有效的 EPUB（缺少 container.xml）。");
                XmlNode rootfile=container.SelectSingleNode("//*[local-name()='rootfile']");
                string opfPath=rootfile==null?null:Attr(rootfile,"full-path");
                XmlDocument opf=opfPath==null?null:Xml(zip,opfPath);
                if(opf==null) throw new InvalidDataException("不是有效的 EPUB（找不到 OPF）。");
                string baseDir=Dir(opfPath);
                Dictionary<string,string> hrefs=new Dictionary<string,string>();string ncx=null,nav=null;
                foreach(XmlNode item in opf.SelectNodes("//*[local-name()='manifest']/*[local-name()='item']")) {
                    string id=Attr(item,"id"),href=Attr(item,"href");if(id==null || href==null) continue;
                    string full=Join(baseDir,href);hrefs[id]=full;
                    if((Attr(item,"media-type")??"")=="application/x-dtbncx+xml") ncx=full;
                    if((" "+(Attr(item,"properties")??"")+" ").Contains(" nav ")) nav=full;
                }
                Dictionary<string,string> titles=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
                if(nav!=null) ReadNav(zip,nav,titles);
                if(titles.Count==0 && ncx!=null) ReadNcx(zip,ncx,titles);
                StringBuilder sb=new StringBuilder();
                foreach(XmlNode itemref in opf.SelectNodes("//*[local-name()='spine']/*[local-name()='itemref']")) {
                    string idref=Attr(itemref,"idref"),file;
                    if(idref==null || !hrefs.TryGetValue(idref,out file)) continue;
                    string html=Read(zip,file);if(html==null) continue;
                    string title;titles.TryGetValue(file,out title);
                    if(title==null) {Match h=Heading.Match(html);if(h.Success) title=Clean(h.Groups[1].Value);}
                    List<string> lines=Paragraphs(html);
                    if(lines.Count==0) continue;
                    if(sb.Length>0) sb.Append('\n');
                    if(!string.IsNullOrEmpty(title)) {
                        b.Chapters.Add(new Chapter {Title=title,Offset=sb.Length});
                        // Most chapter files start with their own heading; only add the title when it is missing.
                        if(!lines[0].Contains(title) && !title.Contains(lines[0])) sb.Append(title).Append('\n');
                    }
                    sb.Append(string.Join("\n",lines));
                }
                b.Text=sb.ToString();
            }
            if(b.Text.Length==0) throw new InvalidDataException("这个 EPUB 里没有可阅读的文字。");
            return b;
        }
        static List<string> Paragraphs(string html) {
            string body=Block.Replace(Head.Replace(html,""),"\n");
            List<string> lines=new List<string>();
            foreach(string raw in WebUtility.HtmlDecode(Tag.Replace(body,"")).Split('\n')) {
                string line=raw.Replace(' ',' ').Trim();if(line.Length>0) lines.Add(line);
            }
            return lines;
        }
        static string Clean(string html) {return Regex.Replace(WebUtility.HtmlDecode(Tag.Replace(html,"")),@"\s+"," ").Trim();}
        static void ReadNav(ZipArchive zip,string nav,Dictionary<string,string> titles) {
            string html=Read(zip,nav);if(html==null) return;
            Match toc=Regex.Match(html,@"<nav\b[^>]*toc[^>]*>(.*?)</nav>",RegexOptions.IgnoreCase|RegexOptions.Singleline);
            foreach(Match a in Regex.Matches(toc.Success?toc.Groups[1].Value:html,@"<a\b[^>]*href\s*=\s*""([^""]*)""[^>]*>(.*?)</a>",RegexOptions.IgnoreCase|RegexOptions.Singleline))
                Add(titles,Join(Dir(nav),a.Groups[1].Value),Clean(a.Groups[2].Value));
        }
        static void ReadNcx(ZipArchive zip,string ncx,Dictionary<string,string> titles) {
            XmlDocument doc=Xml(zip,ncx);if(doc==null) return;
            foreach(XmlNode point in doc.SelectNodes("//*[local-name()='navPoint']")) {
                XmlNode text=point.SelectSingleNode("*[local-name()='navLabel']/*[local-name()='text']"),content=point.SelectSingleNode("*[local-name()='content']");
                if(text!=null && content!=null && Attr(content,"src")!=null) Add(titles,Join(Dir(ncx),Attr(content,"src")),text.InnerText.Trim());
            }
        }
        // The first table-of-contents entry pointing into a file names that file's chapter.
        static void Add(Dictionary<string,string> titles,string target,string title) {
            int hash=target.IndexOf('#');if(hash>=0) target=target.Substring(0,hash);
            if(title.Length>0 && !titles.ContainsKey(target)) titles[target]=title;
        }
        static string Attr(XmlNode n,string name) {XmlAttribute a=n.Attributes==null?null:n.Attributes[name];return a==null?null:a.Value;}
        static string Dir(string path) {int i=path.LastIndexOf('/');return i<0?"":path.Substring(0,i+1);}
        static string Join(string dir,string href) {
            int hash=href.IndexOf('#');string fragment=hash>=0?href.Substring(hash):"";if(hash>=0) href=href.Substring(0,hash);
            List<string> parts=new List<string>();
            foreach(string p in (dir+Uri.UnescapeDataString(href)).Split('/')) {
                if(p.Length==0 || p==".") continue;
                if(p=="..") {if(parts.Count>0) parts.RemoveAt(parts.Count-1);continue;}
                parts.Add(p);
            }
            return string.Join("/",parts)+fragment;
        }
        static ZipArchiveEntry Entry(ZipArchive zip,string name) {
            ZipArchiveEntry e=zip.GetEntry(name);if(e!=null) return e;
            foreach(ZipArchiveEntry x in zip.Entries) if(string.Equals(x.FullName,name,StringComparison.OrdinalIgnoreCase)) return x;
            return null;
        }
        static string Read(ZipArchive zip,string name) {
            ZipArchiveEntry e=Entry(zip,name);if(e==null) return null;
            using(StreamReader r=new StreamReader(e.Open(),Encoding.UTF8,true)) return r.ReadToEnd();
        }
        static XmlDocument Xml(ZipArchive zip,string name) {
            string text=Read(zip,name);if(text==null) return null;
            XmlDocument doc=new XmlDocument {XmlResolver=null};
            try {doc.LoadXml(text);return doc;} catch(XmlException) {return null;}
        }
    }
}

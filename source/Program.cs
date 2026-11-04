using System;
using System.IO;
using System.Threading;
using System.Windows.Forms;

namespace QuietReader {
    static class Program {
        [STAThread] static void Main(string[] args) {
            Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);
            string state=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"QuietReader","state.xml");
            string book=null;
            for(int i=0;i<args.Length;i++) {if(args[i]=="--state" && i+1<args.Length) state=Path.GetFullPath(args[++i]);else book=args[i];}
            bool created;
            using(Mutex mutex=new Mutex(true,"Local\\QuietReader-"+StableName(state),out created)) {
                if(!created) {MessageBox.Show("便笺已经在运行。\n请按全局快捷键或双击托盘图标恢复。","便笺");return;}
                try {using(ReaderForm form=new ReaderForm(state,book)) Application.Run(form);}
                catch(Exception e) {MessageBox.Show("便笺无法继续运行：\n"+e.Message,"便笺",MessageBoxButtons.OK,MessageBoxIcon.Error);}
            }
        }
        static string StableName(string s) {using(System.Security.Cryptography.SHA256 hash=System.Security.Cryptography.SHA256.Create()) return BitConverter.ToString(hash.ComputeHash(System.Text.Encoding.UTF8.GetBytes(s.ToUpperInvariant()))).Replace("-","").Substring(0,24);}
    }
}

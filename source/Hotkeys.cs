using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace QuietReader {
    // Active only when a held-key chord (` + 1, Tab + Q) is selected; never records or transmits input.
    public sealed class ChordHook : IDisposable {
        const uint Marker=0x51524541;
        readonly IntPtr target;
        readonly HookCallback callback;
        readonly KeyChord chord;
        readonly ushort leadScan;
        readonly Thread worker;
        readonly ManualResetEvent started=new ManualResetEvent(false);
        volatile bool stopping;
        uint workerId;
        readonly bool elevated;
        IntPtr hook;
        delegate IntPtr HookCallback(int code,IntPtr message,IntPtr data);
        [StructLayout(LayoutKind.Sequential)] struct KeyboardData { public uint Key,Scan,Flags,Time;public UIntPtr Extra; }
        [StructLayout(LayoutKind.Sequential)] struct KeyboardInput {public ushort Key,Scan;public uint Flags,Time;public UIntPtr Extra;}
        [StructLayout(LayoutKind.Sequential)] struct MouseInput {public int X,Y;public uint Data,Flags,Time;public UIntPtr Extra;}
        [StructLayout(LayoutKind.Explicit)] struct InputUnion {[FieldOffset(0)] public KeyboardInput Keyboard;[FieldOffset(0)] public MouseInput Mouse;}
        [StructLayout(LayoutKind.Sequential)] struct Input {public uint Type;public InputUnion Data;}
        [StructLayout(LayoutKind.Sequential)] struct NativeMessage {public IntPtr Window;public uint Message;public UIntPtr W;public IntPtr L;public uint Time;public int X,Y;public uint Private;}
        [DllImport("user32.dll",SetLastError=true)] static extern IntPtr SetWindowsHookEx(int id,HookCallback callback,IntPtr module,uint thread);
        [DllImport("user32.dll")] static extern bool UnhookWindowsHookEx(IntPtr h);
        [DllImport("user32.dll")] static extern IntPtr CallNextHookEx(IntPtr h,int code,IntPtr message,IntPtr data);
        [DllImport("kernel32.dll",CharSet=CharSet.Auto)] static extern IntPtr GetModuleHandle(string name);
        [DllImport("user32.dll")] static extern short GetAsyncKeyState(int key);
        [DllImport("user32.dll")] static extern uint MapVirtualKey(uint code,uint type);
        [DllImport("user32.dll")] static extern bool PostMessage(IntPtr h,uint message,IntPtr w,IntPtr l);
        [DllImport("user32.dll",SetLastError=true)] static extern uint SendInput(uint count,Input[] inputs,int size);
        [DllImport("kernel32.dll")] static extern uint GetCurrentThreadId();
        [DllImport("user32.dll")] static extern bool PostThreadMessage(uint thread,uint message,IntPtr w,IntPtr l);
        [DllImport("user32.dll")] static extern bool PeekMessage(out NativeMessage message,IntPtr window,uint min,uint max,uint flags);
        [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr window,out uint process);
        [DllImport("kernel32.dll")] static extern IntPtr OpenProcess(uint access,bool inherit,uint process);
        [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr handle);
        [DllImport("advapi32.dll")] static extern bool OpenProcessToken(IntPtr process,uint access,out IntPtr token);
        [DllImport("advapi32.dll")] static extern bool GetTokenInformation(IntPtr token,int type,out uint value,uint size,out uint returned);
        public ChordHook(IntPtr target,int lead,int trigger) {
            chord=new KeyChord(lead,trigger);leadScan=(ushort)MapVirtualKey((uint)lead,0);
            this.target=target;callback=Handle;
            using(System.Diagnostics.Process current=System.Diagnostics.Process.GetCurrentProcess()) {bool value;TryElevated(current.Handle,out value);elevated=value;}
            worker=new Thread(Run) {IsBackground=true,Name="Chord shortcut"};worker.SetApartmentState(ApartmentState.STA);worker.Start();
            if(!started.WaitOne(2000)) Dispose();
        }
        void Run() {
            try {
                workerId=GetCurrentThreadId();NativeMessage message;PeekMessage(out message,IntPtr.Zero,0,0,0);
                if(!stopping) hook=SetWindowsHookEx(13,callback,GetModuleHandle(null),0);
                started.Set();if(hook!=IntPtr.Zero && !stopping) Application.Run();
            } finally {if(hook!=IntPtr.Zero) {UnhookWindowsHookEx(hook);hook=IntPtr.Zero;}started.Set();GC.KeepAlive(callback);}
        }
        public bool Installed {get {return !stopping && hook!=IntPtr.Zero;}}
        static bool TryElevated(IntPtr process,out bool elevated) {
            elevated=false;IntPtr token;if(!OpenProcessToken(process,8,out token)) return false;
            try {uint value,length;bool ok=GetTokenInformation(token,20,out value,4,out length);elevated=value!=0;return ok;}
            finally {CloseHandle(token);}
        }
        bool CanReplayTab() {
            if(elevated) return true;
            uint pid;GetWindowThreadProcessId(GetForegroundWindow(),out pid);
            IntPtr process=OpenProcess(0x1000,false,pid);if(process==IntPtr.Zero) return false;
            try {bool targetElevated;return TryElevated(process,out targetElevated) && !targetElevated;}
            finally {CloseHandle(process);}
        }
        static bool Modified(int key,bool down) {
            return (down && (key==16 || key==17 || key==18 || key==91 || key==92 || (key>=160 && key<=165))) ||
                (GetAsyncKeyState(16)&0x8000)!=0 || (GetAsyncKeyState(17)&0x8000)!=0 || (GetAsyncKeyState(18)&0x8000)!=0 ||
                (GetAsyncKeyState(91)&0x8000)!=0 || (GetAsyncKeyState(92)&0x8000)!=0;
        }
        static Input Key(ushort key,ushort scan,uint flags) {return new Input {Type=1,Data=new InputUnion {Keyboard=new KeyboardInput {Key=key,Scan=scan,Flags=flags,Extra=(UIntPtr)Marker}}};}
        IntPtr Handle(int code,IntPtr message,IntPtr data) {
            if(code<0) return CallNextHookEx(hook,code,message,data);
            KeyboardData k=(KeyboardData)Marshal.PtrToStructure(data,typeof(KeyboardData));
            if(k.Extra==(UIntPtr)Marker) return CallNextHookEx(hook,code,message,data);
            int msg=message.ToInt32();bool down=msg==0x100 || msg==0x104;
            if(!down && msg!=0x101 && msg!=0x105) return CallNextHookEx(hook,code,message,data);
            bool modified=Modified((int)k.Key,down);
            // Do not defer keys in an elevated app that rejects our replay input.
            if(k.Key==chord.Lead && down && !modified && !CanReplayTab()) modified=true;
            ChordAction action=chord.Process((int)k.Key,down,modified);
            if((action&ChordAction.Toggle)!=0) PostMessage(target,0x8001,IntPtr.Zero,IntPtr.Zero);
            if((action&ChordAction.ReplayLead)!=0) {
                bool suppress=(action&ChordAction.Suppress)!=0;
                Input[] keys=new Input[suppress?2:3];keys[0]=Key((ushort)chord.Lead,leadScan,0);keys[1]=Key((ushort)chord.Lead,leadScan,2);
                if(!suppress) keys[2]=Key((ushort)k.Key,(ushort)k.Scan,(down?0u:2u)|(k.Flags&1));
                if(SendInput((uint)keys.Length,keys,Marshal.SizeOf(typeof(Input)))==(uint)keys.Length) return (IntPtr)1;
                // Input into a higher-privilege app may be refused; preserve the original event.
                return CallNextHookEx(hook,code,message,data);
            }
            return (action&ChordAction.Suppress)!=0?(IntPtr)1:CallNextHookEx(hook,code,message,data);
        }
        public void Dispose() {
            stopping=true;if(workerId!=0) PostThreadMessage(workerId,0x12,IntPtr.Zero,IntPtr.Zero);
            if(worker!=null && worker!=Thread.CurrentThread && worker.IsAlive) worker.Join(1000);
            if(worker!=null && !worker.IsAlive) started.Dispose();
            GC.KeepAlive(callback);
        }
    }
}

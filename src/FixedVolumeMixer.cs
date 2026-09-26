using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using Microsoft.Win32;

namespace FixedVolumeMixer {
    [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")] class DeviceEnumerator {}
    [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)] interface IDeviceEnumerator {
        [PreserveSig] int EnumAudioEndpoints(int flow, uint state, out IDeviceCollection devices);
        [PreserveSig] int GetDefaultAudioEndpoint(int flow, int role, out IDevice device);
    }
    [ComImport, Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)] interface IDeviceCollection {
        [PreserveSig] int GetCount(out uint count);
        [PreserveSig] int Item(uint index, out IDevice device);
    }
    [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)] interface IDevice {
        [PreserveSig] int Activate(ref Guid iid, uint context, IntPtr parameters, [MarshalAs(UnmanagedType.IUnknown)] out object obj);
        [PreserveSig] int OpenPropertyStore(int mode, out IntPtr store);
        [PreserveSig] int GetId([MarshalAs(UnmanagedType.LPWStr)] out string id);
        [PreserveSig] int GetState(out uint state);
    }
    [ComImport, Guid("77AA99A0-1BD6-484F-8BC7-2C654C9A9B6F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)] interface ISessionManager {
        [PreserveSig] int GetAudioSessionControl(ref Guid session, uint flags, out IntPtr control);
        [PreserveSig] int GetSimpleAudioVolume(ref Guid session, uint flags, out ISimpleVolume volume);
        [PreserveSig] int GetSessionEnumerator(out ISessionEnumerator enumerator);
        [PreserveSig] int RegisterSessionNotification(ISessionNotification notification);
        [PreserveSig] int UnregisterSessionNotification(ISessionNotification notification);
    }
    [ComImport, Guid("E2F5BB11-0570-40CA-ACDD-3AA01277DEE8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)] interface ISessionEnumerator {
        [PreserveSig] int GetCount(out int count);
        [PreserveSig] int GetSession(int index, [MarshalAs(UnmanagedType.IUnknown)] out object control);
    }
    [ComImport, Guid("BFB7FF88-7239-4FC9-8FA2-07C950BE9C6D"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)] interface ISessionControl {
        [PreserveSig] int GetState(out int state);
        [PreserveSig] int GetDisplayName([MarshalAs(UnmanagedType.LPWStr)] out string name);
        [PreserveSig] int SetDisplayName([MarshalAs(UnmanagedType.LPWStr)] string name, ref Guid context);
        [PreserveSig] int GetIconPath([MarshalAs(UnmanagedType.LPWStr)] out string path);
        [PreserveSig] int SetIconPath([MarshalAs(UnmanagedType.LPWStr)] string path, ref Guid context);
        [PreserveSig] int GetGroupingParam(out Guid grouping);
        [PreserveSig] int SetGroupingParam(ref Guid grouping, ref Guid context);
        [PreserveSig] int RegisterAudioSessionNotification(IntPtr notification);
        [PreserveSig] int UnregisterAudioSessionNotification(IntPtr notification);
        [PreserveSig] int GetSessionIdentifier([MarshalAs(UnmanagedType.LPWStr)] out string id);
        [PreserveSig] int GetSessionInstanceIdentifier([MarshalAs(UnmanagedType.LPWStr)] out string id);
        [PreserveSig] int GetProcessId(out uint pid);
        [PreserveSig] int IsSystemSoundsSession();
    }
    [ComImport, Guid("87CE5498-68D6-44E5-9215-6DA47EF883D8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)] interface ISimpleVolume {
        [PreserveSig] int SetMasterVolume(float volume, ref Guid context);
        [PreserveSig] int GetMasterVolume(out float volume);
        [PreserveSig] int SetMute([MarshalAs(UnmanagedType.Bool)] bool mute, ref Guid context);
        [PreserveSig] int GetMute([MarshalAs(UnmanagedType.Bool)] out bool mute);
    }
    [ComVisible(true), Guid("641DD20B-4D41-49CC-ABA3-174B9477BB08"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)] public interface ISessionNotification {
        [PreserveSig] int OnSessionCreated([MarshalAs(UnmanagedType.IUnknown)] object control);
    }
    [ComVisible(true), ClassInterface(ClassInterfaceType.None)] public class SessionNotification : ISessionNotification {
        public Action<object> Created;
        public int OnSessionCreated(object control) { try { if (Created != null) Created(control); } catch (Exception e) { Log.Error(e); } return 0; }
    }
    [ComImport, Guid("C02216F6-8C67-4B5B-9D00-D008E73E0064"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)] interface IMeter {
        [PreserveSig] int GetPeakValue(out float peak);
    }

    public class AppSetting {
        public string Key { get; set; }
        public string Name { get; set; }
        public decimal Volume { get; set; }
        public bool Muted { get; set; }
        public AppSetting Copy() { return (AppSetting)MemberwiseClone(); }
    }
    public class SavedSettings { public int Version = 1; public List<AppSetting> Apps = new List<AppSetting>(); }
    static class Log {
        public static string DirectoryPath;
        public static void Error(Exception e) { try { if (DirectoryPath != null) File.AppendAllText(Path.Combine(DirectoryPath, "errors.log"), DateTime.Now.ToString("s") + " " + e.Message + Environment.NewLine); } catch {} }
    }
    class AppState {
        public AppSetting Setting;
        public int Revision;
        public bool Connected, Playing;
        public float Peak;
        public string Error;
    }
    class Model {
        public readonly object Gate = new object();
        public readonly Dictionary<string, AppState> Apps = new Dictionary<string, AppState>(StringComparer.OrdinalIgnoreCase);
        readonly string file;
        int dirty;
        public string Device = "출력 장치 확인 중…";
        public string Problem;
        public Model(string directory, bool seeds) {
            Directory.CreateDirectory(directory); file = Path.Combine(directory, "settings.json");
            if (File.Exists(file)) {
                try {
                    var saved = new JavaScriptSerializer().Deserialize<SavedSettings>(File.ReadAllText(file));
                    foreach (var a in saved.Apps) if (!String.IsNullOrWhiteSpace(a.Key) && !Apps.ContainsKey(a.Key)) {
                        a.Volume = Math.Max(0, Math.Min(100, a.Volume));
                        Apps.Add(a.Key, new AppState { Setting = a });
                    }
                } catch (Exception e) { Problem = "저장 파일을 읽지 못했습니다. 원본은 settings.unreadable.json에 보관했습니다."; File.Copy(file, Path.Combine(directory,"settings.unreadable.json"), true); Log.Error(e); }
            }
            if (seeds) {
                Add("powershell.exe", "PowerShell · Codex 알림", 1, false);
                Add("phoneexperiencehost.exe", "휴대폰과 연결", 1, false);
            }
        }
        public AppState Add(string key, string name, decimal volume, bool mute) {
            lock (Gate) {
                AppState a;
                if (Apps.TryGetValue(key, out a)) return a;
                a = new AppState { Setting = new AppSetting { Key=key, Name=name, Volume=volume, Muted=mute } };
                Apps.Add(key,a); dirty++; return a;
            }
        }
        public void Set(string key, decimal volume, bool mute) {
            lock (Gate) { var a=Apps[key]; a.Setting.Volume=volume; a.Setting.Muted=mute; a.Revision++; dirty++; }
        }
        public void Adopt(AppState a, float volume, bool mute) {
            a.Setting.Volume=Math.Round((decimal)volume*100,1); a.Setting.Muted=mute; a.Revision++; dirty++;
        }
        public void Save() {
            lock (Gate) {
                if (dirty==0) return;
                try {
                    var data=new SavedSettings { Apps=Apps.Values.Select(a=>a.Setting.Copy()).ToList() };
                    string temp=file+".tmp";
                    File.WriteAllText(temp,new JavaScriptSerializer().Serialize(data),new UTF8Encoding(false));
                    if (File.Exists(file)) File.Replace(temp,file,null); else File.Move(temp,file);
                    dirty=0;
                } catch(Exception e) { Problem="설정을 저장하지 못했습니다: "+e.Message; Log.Error(e); }
            }
        }
    }
    class TrackedSession {
        public object Raw;
        public ISessionControl Control;
        public ISimpleVolume Volume;
        public IMeter Meter;
        public AppState App;
        public int Revision;
        public float LastVolume;
        public bool LastMute;
    }
    class AudioEngine : IDisposable {
        readonly Model model;
        readonly Dictionary<string,TrackedSession> sessions = new Dictionary<string,TrackedSession>();
        readonly Thread worker;
        volatile bool stop;
        IDeviceEnumerator enumerator;
        IDevice device;
        ISessionManager manager;
        SessionNotification notifications;
        string deviceId;
        static Guid context=new Guid("CF8E1F1B-96F2-4D18-BD3E-DF838CA7D4A2");
        [DllImport("ole32.dll")] static extern int CoInitializeEx(IntPtr reserved,uint mode);
        [DllImport("ole32.dll")] static extern void CoUninitialize();
        public AudioEngine(Model model) { this.model=model; worker=new Thread(Run);worker.IsBackground=true;worker.SetApartmentState(ApartmentState.MTA);worker.Start(); }
        static void Check(int hr) { Marshal.ThrowExceptionForHR(hr); }
        static void Release(object obj) { if(obj!=null && Marshal.IsComObject(obj)) try { Marshal.ReleaseComObject(obj); } catch {} }
        void Run() {
            int hr=CoInitializeEx(IntPtr.Zero,0);
            try {
                enumerator=(IDeviceEnumerator)new DeviceEnumerator(); int tick=0;
                while(!stop) {
                    try {
                        if(tick%10==0) { Connect(); if(manager!=null) Enumerate(); }
                        Poll();
                        if(tick%5==0) model.Save();
                    } catch(Exception e) { lock(model.Gate) model.Problem="오디오 연결을 다시 확인하고 있습니다."; Log.Error(e); Disconnect(); }
                    tick++; Thread.Sleep(100);
                }
            } finally { Disconnect(); Release(enumerator); model.Save(); if(hr>=0) CoUninitialize(); }
        }
        void Connect() {
            IDevice next; int hr=enumerator.GetDefaultAudioEndpoint(0,1,out next);
            if(hr<0) { Disconnect();lock(model.Gate)model.Device="연결된 기본 출력 장치가 없습니다"; return; }
            string id;Check(next.GetId(out id));
            if(id==deviceId && manager!=null) { Release(next);return; }
            Disconnect(); device=next;deviceId=id;
            Guid iid=typeof(ISessionManager).GUID;object obj;Check(device.Activate(ref iid,23,IntPtr.Zero,out obj));manager=(ISessionManager)obj;
            notifications=new SessionNotification { Created=AddSession };
            Check(manager.RegisterSessionNotification(notifications));
            string title="Windows 기본 출력";
            try {
                string guid=id.Substring(id.LastIndexOf('{'));
                using(var key=Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\MMDevices\Audio\Render\"+guid+@"\Properties")) {
                    if(key!=null) title=Convert.ToString(key.GetValue("{a45c254e-df1c-4efd-8020-67d146a850e0},2",title));
                }
            }catch{}
            lock(model.Gate) {model.Device=title; model.Problem=null;}
        }
        void Enumerate() {
            ISessionEnumerator list=null;
            try { Check(manager.GetSessionEnumerator(out list));int count;Check(list.GetCount(out count));for(int i=0;i<count;i++){object raw; if(list.GetSession(i,out raw)>=0) AddSession(raw);} }
            finally {Release(list);}
        }
        void AddSession(object raw) {
            bool retained=false;
            try {
                lock(model.Gate) {
                    if(stop || manager==null) return;
                    var control=(ISessionControl)raw;string id;Check(control.GetSessionInstanceIdentifier(out id));
                    if(sessions.ContainsKey(id)) return;
                    int state;Check(control.GetState(out state));if(state==2)return;
                    uint pid;Check(control.GetProcessId(out pid));
                    string key,name;
                    if(control.IsSystemSoundsSession()==0) {key="@system";name="Windows 시스템 소리";}
                    else {
                        if(pid==0 || pid==(uint)Process.GetCurrentProcess().Id)return;
                        using(var p=Process.GetProcessById((int)pid)) {key=p.ProcessName.ToLowerInvariant()+".exe"; name=p.ProcessName;}
                        if(key=="powershell.exe") name="PowerShell · Codex 알림";
                        if(key=="phoneexperiencehost.exe") name="휴대폰과 연결";
                    }
                    var simple=(ISimpleVolume)raw;float volume;bool mute;Check(simple.GetMasterVolume(out volume));Check(simple.GetMute(out mute));
                    AppState app;bool known=model.Apps.TryGetValue(key,out app);
                    if(!known) app=model.Add(key,name,Math.Round((decimal)volume*100,1),mute);
                    var session=new TrackedSession {Raw=raw,Control=control,Volume=simple,App=app,Revision=-1,LastVolume=volume,LastMute=mute};
                    try {session.Meter=(IMeter)raw;}catch{}
                    if(known) Apply(session); else session.Revision=app.Revision;
                    sessions.Add(id,session);retained=true; app.Connected=true;
                }
            } catch(Exception e) { Log.Error(e); }
            finally {if(!retained)Release(raw);}
        }
        void Apply(TrackedSession s) {
            float value=(float)(s.App.Setting.Volume/100m);bool mute=s.App.Setting.Muted;
            if(Math.Abs(s.LastVolume-value)>0.00001f)Check(s.Volume.SetMasterVolume(value,ref context));
            if(s.LastMute!=mute)Check(s.Volume.SetMute(mute,ref context));
            s.LastVolume=value;s.LastMute=mute;s.Revision=s.App.Revision;s.App.Error=null;
        }
        void Poll() {
            lock(model.Gate) {
                foreach(var a in model.Apps.Values) {a.Connected=false;a.Playing=false;a.Peak=0;}
                var expired=new List<string>();
                foreach(var pair in sessions) {
                    var s=pair.Value;
                    try {
                        int state;Check(s.Control.GetState(out state));if(state==2){expired.Add(pair.Key);continue;}
                        float v;bool m;Check(s.Volume.GetMasterVolume(out v));Check(s.Volume.GetMute(out m));
                        if(s.Revision!=s.App.Revision)Apply(s);
                        else if(Math.Abs(v-s.LastVolume)>0.0001f || m!=s.LastMute) {model.Adopt(s.App,v,m);s.LastVolume=v;s.LastMute=m;s.Revision=s.App.Revision;}
                        s.App.Connected=true;s.App.Playing|=state==1;
                        if(s.Meter!=null) {float peak;if(s.Meter.GetPeakValue(out peak)>=0)s.App.Peak=Math.Max(s.App.Peak,peak);}
                    } catch(Exception e) {s.App.Error="볼륨 적용을 다시 시도합니다";Log.Error(e);expired.Add(pair.Key);}
                }
                foreach(string id in expired) {Release(sessions[id].Raw);sessions.Remove(id);}
            }
        }
        void Disconnect() {
            // Never hold the model lock while unregistering a callback.
            var old=manager;manager=null;
            if(old!=null && notifications!=null)try{old.UnregisterSessionNotification(notifications);}catch{}
            lock(model.Gate) {foreach(var s in sessions.Values)Release(s.Raw);sessions.Clear();foreach(var a in model.Apps.Values){a.Connected=false;a.Playing=false;a.Peak=0;}}
            notifications=null;Release(old);Release(device);device=null;deviceId=null;
        }
        public void Dispose() {stop=true;worker.Join(2500);}
    }

    static class Ui {
        static readonly float scale=GetScale();
        static float GetScale(){using(var g=Graphics.FromHwnd(IntPtr.Zero))return g.DpiX/96f;}
        public static int Px(int value){return (int)Math.Round(value*scale);}
    }
    static class AppIcons {
        public static readonly Icon Window = Load(48);
        public static readonly Icon Tray = Load(Ui.Px(16));
        static Icon Load(int size) {
            using(var stream=typeof(AppIcons).Assembly.GetManifestResourceStream("FixedVolumeMixer.AppIcon")) {
                if(stream==null)return (Icon)SystemIcons.Application.Clone();
                using(var icon=new Icon(stream,size,size))return (Icon)icon.Clone();
            }
        }
    }
    class AppRow : UserControl {
        readonly Model model; readonly AppState app;
        readonly Label title,detail,status,percent;
        readonly TrackBar slider;
        readonly NumericUpDown number;
        readonly CheckBox mute;
        bool updating;
        public AppRow(Model model,AppState app) {
            this.model=model;this.app=app;AutoScaleMode=AutoScaleMode.None;Height=Ui.Px(108);BackColor=Color.White;Margin=new Padding(0,0,0,Ui.Px(10));Padding=new Padding(Ui.Px(12));
            title=new Label {Left=16,Top=12,Height=24,AutoEllipsis=true,Font=new Font("맑은 고딕",10.5f,FontStyle.Bold)};
            detail=new Label {Left=16,Top=39,Height=20,ForeColor=Color.FromArgb(115,121,132),Font=new Font("맑은 고딕",8.5f)};
            status=new Label {Top=15,Width=120,Height=22,TextAlign=ContentAlignment.MiddleRight};
            slider=new TrackBar {Left=10,Top=65,Minimum=0,Maximum=1000,TickStyle=TickStyle.None,SmallChange=1,LargeChange=10,Height=30};
            number=new NumericUpDown {Top=66,Width=76,Minimum=0,Maximum=100,DecimalPlaces=1,Increment=0.1m,TextAlign=HorizontalAlignment.Right};
            percent=new Label {Top=68,Width=23,Height=24,Text="%"};
            mute=new CheckBox {Top=68,Width=90,Text="음소거",AutoSize=false};
            Controls.AddRange(new Control[]{title,detail,status,slider,number,percent,mute});
            slider.ValueChanged+=delegate {if(updating)return;number.Value=slider.Value/10m;Commit();};
            number.ValueChanged+=delegate {if(updating)return;updating=true;slider.Value=(int)(number.Value*10);updating=false;Commit();};
            mute.CheckedChanged+=delegate {if(!updating)Commit();};
            Resize+=delegate {LayoutRow();};
            RefreshState();
        }
        void LayoutRow(){
            title.SetBounds(Ui.Px(16),Ui.Px(12),Math.Max(Ui.Px(150),Width-Ui.Px(190)),Ui.Px(24));
            detail.SetBounds(Ui.Px(16),Ui.Px(39),Width-Ui.Px(32),Ui.Px(20));
            status.SetBounds(Width-Ui.Px(140),Ui.Px(15),Ui.Px(120),Ui.Px(22));
            mute.SetBounds(Width-Ui.Px(103),Ui.Px(68),Ui.Px(90),Ui.Px(24));
            number.SetBounds(Width-Ui.Px(213),Ui.Px(66),Ui.Px(76),Ui.Px(27));
            percent.SetBounds(Width-Ui.Px(133),Ui.Px(68),Ui.Px(23),Ui.Px(24));
            slider.SetBounds(Ui.Px(10),Ui.Px(65),Math.Max(Ui.Px(120),Width-Ui.Px(234)),Ui.Px(30));
        }
        void Commit(){model.Set(app.Setting.Key,number.Value,mute.Checked);model.Save();}
        public void RefreshState() {
            lock(model.Gate) {
                updating=true;
                title.Text=app.Setting.Name;
                detail.Text=app.Setting.Key=="@system"?"Windows에서 재생하는 시스템 소리":app.Setting.Key+"  ·  이 프로세스의 모든 소리";
                if(!slider.Capture && !number.ContainsFocus) {slider.Value=(int)(app.Setting.Volume*10);number.Value=app.Setting.Volume;}
                mute.Checked=app.Setting.Muted;
                status.Text=app.Error!=null?"적용 재시도":app.Playing?"● 재생 중":app.Connected?"재생 대기":"종료됨 · 저장됨";
                status.ForeColor=app.Error!=null?Color.DarkOrange:app.Playing?Color.FromArgb(0,128,100):Color.FromArgb(115,121,132);
                updating=false;
            }
        }
    }
    class MixerForm : Form {
        readonly Model model; AudioEngine engine;
        readonly FlowLayoutPanel list;
        readonly Dictionary<string,AppRow> rows=new Dictionary<string,AppRow>();
        readonly Label device,footer;
        readonly NotifyIcon tray;
        readonly System.Windows.Forms.Timer timer;
        bool quitting;
        readonly bool preview;
        public MixerForm(Model model,bool preview) {
            SuspendLayout();
            this.model=model;this.preview=preview;
            Text="고정 볼륨 믹서";Font=new Font("맑은 고딕",9f);BackColor=Color.FromArgb(243,245,249);ClientSize=new Size(730,640);MinimumSize=new Size(620,430);StartPosition=FormStartPosition.CenterScreen;
            AutoScaleDimensions=new SizeF(96f,96f);AutoScaleMode=AutoScaleMode.Dpi;Icon=AppIcons.Window;
            var header=new Panel {Dock=DockStyle.Top,Height=132,Padding=new Padding(20),BackColor=Color.White};
            header.Controls.Add(new Label {Text="고정 볼륨 믹서",Left=22,Top=17,Width=350,Height=31,Font=new Font("맑은 고딕",17f,FontStyle.Bold)});
            header.Controls.Add(new Label {Text="소리가 끝나도 앱은 남습니다. 다음 재생에도 저장한 볼륨을 적용합니다.",Left=24,Top=56,Width=660,Height=23,ForeColor=Color.FromArgb(90,100,115)});
            device=new Label {Left=24,Top=94,Width=390,Height=24};header.Controls.Add(device);
            var add=new Button {Text="앱 미리 추가",Width=114,Height=30,Top=91,FlatStyle=FlatStyle.Flat};header.Controls.Add(add);add.Click+=AddApp;
            header.Resize+=delegate{add.Left=header.ClientSize.Width-add.Width-Ui.Px(22);device.Width=Math.Max(Ui.Px(250),add.Left-Ui.Px(35));};
            var bottom=new Panel {Dock=DockStyle.Bottom,Height=83,Padding=new Padding(18,10,18,10),BackColor=Color.White};
            var startup=new CheckBox {Left=22,Top=12,Width=270,Height=25,Text="Windows 시작 시 실행"};
            using(var key=Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run"))startup.Checked=key!=null && key.GetValue("FixedVolumeMixer")!=null;
            startup.CheckedChanged+=delegate {
                if(preview)return;
                try {using(var key=Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run")) {if(startup.Checked)key.SetValue("FixedVolumeMixer","\""+Application.ExecutablePath+"\" --tray");else key.DeleteValue("FixedVolumeMixer",false);}}
                catch(Exception e){MessageBox.Show(e.Message,"자동 실행 설정 실패");}
            };
            bottom.Controls.Add(startup);
            var quit=new Button {Text="완전히 종료",Top=10,Width=114,Height=29,FlatStyle=FlatStyle.Flat};bottom.Controls.Add(quit);quit.Click+=delegate{quitting=true;Close();};
            bottom.Resize+=delegate{quit.Left=bottom.ClientSize.Width-quit.Width-Ui.Px(22);};
            footer=new Label {Left=22,Top=48,Width=680,Height=24,Anchor=AnchorStyles.Left|AnchorStyles.Right|AnchorStyles.Bottom,ForeColor=Color.FromArgb(100,110,122)};bottom.Controls.Add(footer);
            list=new FlowLayoutPanel {Dock=DockStyle.Fill,FlowDirection=FlowDirection.TopDown,WrapContents=false,AutoScroll=true,Padding=new Padding(16,15,16,8)};
            Controls.Add(list);Controls.Add(bottom);Controls.Add(header);
            list.Resize+=delegate{foreach(var r in rows.Values)r.Width=Math.Max(Ui.Px(490),list.ClientSize.Width-list.Padding.Horizontal-Ui.Px(14));};
            var menu=new ContextMenuStrip();menu.Items.Add("믹서 열기",null,delegate{Reveal();});menu.Items.Add("완전히 종료",null,delegate{quitting=true;Close();});
            tray=new NotifyIcon {Icon=AppIcons.Tray,Text="고정 볼륨 믹서",ContextMenuStrip=menu,Visible=!preview};tray.DoubleClick+=delegate{Reveal();};
            timer=new System.Windows.Forms.Timer {Interval=200};timer.Tick+=delegate{RefreshRows();};timer.Start();
            if(!preview)engine=new AudioEngine(model);
            ResumeLayout(true);
            RefreshRows();
        }
        void AddApp(object sender,EventArgs e) {
            using(var dialog=new OpenFileDialog {Title="볼륨을 미리 정할 프로그램 선택",Filter="프로그램 (*.exe)|*.exe"})if(dialog.ShowDialog(this)==DialogResult.OK){
                string key=Path.GetFileName(dialog.FileName).ToLowerInvariant();model.Add(key,Path.GetFileNameWithoutExtension(dialog.FileName),100,false);model.Save();RefreshRows();
            }
        }
        public void Reveal(){Show();WindowState=FormWindowState.Normal;Activate();}
        public void RefreshRows() {
            lock(model.Gate) {
                foreach(var a in model.Apps.Values) {
                    AppRow row;if(!rows.TryGetValue(a.Setting.Key,out row)){row=new AppRow(model,a);row.Width=Math.Max(Ui.Px(490),list.ClientSize.Width-list.Padding.Horizontal-Ui.Px(14));rows.Add(a.Setting.Key,row);list.Controls.Add(row);}
                    row.RefreshState();
                }
                device.Text="현재 기본 출력: "+model.Device;
                footer.Text=model.Problem??"자동 저장  ·  창을 닫아도 트레이에서 계속 적용합니다.";
            }
        }
        protected override void OnFormClosing(FormClosingEventArgs e) {
            if(!preview && !quitting && e.CloseReason==CloseReason.UserClosing){e.Cancel=true;Hide();return;}
            timer.Stop();if(engine!=null){engine.Dispose();engine=null;}model.Save();tray.Visible=false;tray.Dispose();base.OnFormClosing(e);
        }
        public void Finish(){quitting=true;Close();}
    }
    static class Program {
        [DllImport("user32.dll")] static extern bool SetProcessDPIAware();
        [DllImport("user32.dll",CharSet=CharSet.Unicode)] static extern IntPtr FindWindow(string className,string title);
        [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr h,int cmd);
        [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr h);
        [STAThread] static void Main(string[] args) {
            SetProcessDPIAware();Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);
            if(args.Length>=2 && args[0]=="--self-test") {SelfTest(args[1]);return;}
            if(args.Length>=2 && args[0]=="--render-preview") {
                var model=new Model(Path.Combine(Path.GetDirectoryName(Path.GetFullPath(args[1])),"preview-settings"),true);model.Device="Audioengine A2+";model.Add("chrome.exe","Chrome",100,false);model.Add("@system","Windows 시스템 소리",2,false);
                using(var form=new MixerForm(model,true)){form.Show();Application.DoEvents();form.RefreshRows();Application.DoEvents();using(var bitmap=new Bitmap(form.Width,form.Height)){form.DrawToBitmap(bitmap,new Rectangle(Point.Empty,bitmap.Size));bitmap.Save(args[1]);}form.Finish();}return;
            }
            bool fresh;using(var mutex=new Mutex(true,@"Local\Hyunseo.FixedVolumeMixer",out fresh)) {
                if(!fresh){var window=FindWindow(null,"고정 볼륨 믹서");if(window!=IntPtr.Zero){ShowWindow(window,9);SetForegroundWindow(window);}return;}
                string directory=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"FixedVolumeMixer");Directory.CreateDirectory(directory);Log.DirectoryPath=directory;
                Application.ThreadException+=delegate(object sender,ThreadExceptionEventArgs e){Log.Error(e.Exception);MessageBox.Show(e.Exception.Message,"고정 볼륨 믹서");};
                using(var form=new MixerForm(new Model(directory,true),false)) {
                    if(args.Contains("--tray"))form.Shown+=delegate{form.Hide();};
                    Application.Run(form);
                }
            }
        }
        static void SelfTest(string output) {
            var results=new List<string>();
            try {
                string dir=Path.Combine(Path.GetDirectoryName(Path.GetFullPath(output)),"self-test-"+Guid.NewGuid().ToString("N"));
                var model=new Model(dir,false);model.Add("transient.exe","짧은 알림",1,false);model.Set("transient.exe",2.5m,true);model.Save();
                var reload=new Model(dir,false);if(reload.Apps["transient.exe"].Setting.Volume!=2.5m || !reload.Apps["transient.exe"].Setting.Muted)throw new Exception("Persistence mismatch");results.Add("PASS: inactive row and volume/mute survive reload");
                IDevice d;var devices=(IDeviceEnumerator)new DeviceEnumerator();Marshal.ThrowExceptionForHR(devices.GetDefaultAudioEndpoint(0,1,out d));Guid iid=typeof(ISessionManager).GUID;object raw;Marshal.ThrowExceptionForHR(d.Activate(ref iid,23,IntPtr.Zero,out raw));
                Guid testSession=Guid.NewGuid();ISimpleVolume v;Marshal.ThrowExceptionForHR(((ISessionManager)raw).GetSimpleAudioVolume(ref testSession,0,out v));float original;bool muted;v.GetMasterVolume(out original);v.GetMute(out muted);Guid context=Guid.Empty;
                try {Marshal.ThrowExceptionForHR(v.SetMasterVolume(.013f,ref context));Marshal.ThrowExceptionForHR(v.SetMute(true,ref context));float actual;bool actualMute;v.GetMasterVolume(out actual);v.GetMute(out actualMute);if(Math.Abs(actual-.013f)>.00001 || !actualMute)throw new Exception("Audio control mismatch");results.Add("PASS: real Core Audio volume/mute round trip on isolated test session");}
                finally{v.SetMasterVolume(original,ref context);v.SetMute(muted,ref context);Marshal.ReleaseComObject(v);Marshal.ReleaseComObject(raw);Marshal.ReleaseComObject(d);Marshal.ReleaseComObject(devices);}
                results.Add("PASS: no changes to existing user audio sessions");
            }catch(Exception e){results.Add("FAIL: "+e.ToString());Environment.ExitCode=1;}
            File.WriteAllLines(output,results,new UTF8Encoding(false));
        }
    }
}

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
namespace FixedVolumeMixer {
    static class MixerIntegrationTest {
        static void Require(bool ok,string message){if(!ok)throw new Exception(message);}
        static float Probe(string executable,string report,bool external){
            var start=new ProcessStartInfo(executable,"\""+report+"\""+(external?" external":"")){UseShellExecute=false,CreateNoWindow=true,WindowStyle=ProcessWindowStyle.Hidden};
            using(var child=Process.Start(start)){Require(child.WaitForExit(8000),"Probe timeout");Require(child.ExitCode==0,"Probe failed");}
            return float.Parse(File.ReadAllText(report),System.Globalization.CultureInfo.InvariantCulture);
        }
        [MTAThread] static void Main(string[] args){
            var lines=new List<string>();string root=args[0];
            try {
                string directory=Path.Combine(root,"integration-settings-"+Guid.NewGuid().ToString("N"));var model=new Model(directory,false);
                model.Add("mixersessionprobe.exe","짧은 알림 테스트",.7m,false);
                using(var engine=new AudioEngine(model)){
                    Thread.Sleep(1400);
                    float first=Probe(Path.Combine(root,"MixerSessionProbe.exe"),Path.Combine(root,"probe-first.txt"),false);
                    Require(Math.Abs(first-.007f)<.0001,"New session did not receive saved volume: "+first);lines.Add("PASS: new transient process receives 0.7% volume");
                    Thread.Sleep(1300);
                    lock(model.Gate)Require(model.Apps.ContainsKey("mixersessionprobe.exe"),"Row disappeared");lines.Add("PASS: row survives process exit");
                    model.Set("mixersessionprobe.exe",.4m,false);model.Save();
                    float second=Probe(Path.Combine(root,"MixerSessionProbe.exe"),Path.Combine(root,"probe-second.txt"),true);
                    Require(Math.Abs(second-.004f)<.0001,"Offline edit did not apply: "+second);lines.Add("PASS: volume edited while offline applies to next process");
                    Thread.Sleep(300);lock(model.Gate)Require(Math.Abs(model.Apps["mixersessionprobe.exe"].Setting.Volume-17m)<.01m,"External change not adopted");lines.Add("PASS: external volume change is reflected and saved");
                    Probe(Path.Combine(root,"MixerDiscovered.exe"),Path.Combine(root,"probe-discovery.txt"),false);
                    Thread.Sleep(1000);lock(model.Gate)Require(model.Apps.ContainsKey("mixerdiscovered.exe"),"Auto discovery missing");lines.Add("PASS: unknown short-lived application automatically retained");
                }
                var reload=new Model(directory,false);Require(reload.Apps.ContainsKey("mixerdiscovered.exe"),"Discovery not persisted");Require(reload.Apps["mixersessionprobe.exe"].Setting.Volume==17m,"Restart persistence failed");lines.Add("PASS: settings and discovered app list survive restart");
            }catch(Exception e){lines.Add("FAIL: "+e.ToString());Environment.ExitCode=1;}
            File.WriteAllLines(Path.Combine(root,"integration-results.txt"),lines);foreach(var line in lines)Console.WriteLine(line);
        }
    }
}

using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
class ProcessFixture {
    static int Main(string[] args) {
        switch (args[0]) {
            case "ok": Console.WriteLine("fixture-ok"); return 0;
            case "fail": return 17;
            case "sleep": File.WriteAllText(args[1], Process.GetCurrentProcess().Id.ToString()); Thread.Sleep(30000); return 0;
            case "overflow": for (int i = 0; i < 20000; i++) Console.WriteLine(new string('x', 100)); return 0;
            default: return 1;
        }
    }
}

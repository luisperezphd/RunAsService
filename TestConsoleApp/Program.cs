using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace TestConsoleApp {
    class Program {
        // This console app can be used to test RunAsService
        // Once you start the service you can check to make sure 
        // that the file is getting written to continously.
        static void Main() {
            while(true) {
                File.AppendAllText(@"c:\temp\TestConsoleApp.txt", $"{DateTime.Now}\n");
                Thread.Sleep(2000);
            }
        }
    }
}

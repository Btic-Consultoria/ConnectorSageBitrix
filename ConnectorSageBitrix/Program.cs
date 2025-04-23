using System;
using System.ServiceProcess;
using System.IO;

namespace ConnectorSageBitrix
{
    static class Program
    {
        static void Main(string[] args)
        {
            // For testing/console mode
            if (args.Length > 0 && args[0].ToLower() == "-test")
            {
                // Create log directory
                string logDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "ConnectorSageBitrix");
                Directory.CreateDirectory(logDir);

                // Write startup log
                File.AppendAllText(
                    Path.Combine(logDir, "connector-startup.log"),
                    $"Application starting (console mode) at {DateTime.Now:yyyy-MM-dd HH:mm:ss}\r\n"
                );

                // Create and run service in console mode
                SyncService service = new SyncService();
                service.StartService(args);

                Console.WriteLine("Press any key to stop the service...");
                Console.ReadKey();
                service.StopService();
            }
            else
            {
                // Run as a Windows service
                ServiceBase[] ServicesToRun = new ServiceBase[]
                {
                    new SyncService()
                };
                ServiceBase.Run(ServicesToRun);
            }
        }
    }
}
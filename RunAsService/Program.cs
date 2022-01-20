using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.IO;
using System.Reflection;
using System.Diagnostics;
using System.ServiceProcess;


namespace RunAsService {
    class RunAsService { 
        // TODO: Consolidate this and ConsoleAppServiceProxy into one
        // TODO: Detect if this executable has moved and update the paths
        // TODO: Add command line options: install uninstall fixservices run help
        // TODO: Confirm that it's a console application?
        // TODO: Working directory as a parameter

        #region DLLImport

        [DllImport("advapi32.dll", SetLastError = true)] public static extern IntPtr OpenSCManager(string lpMachineName, string lpSCDB, int scParameter);
        [DllImport("advapi32.dll", SetLastError = true)] public static extern IntPtr CreateService(IntPtr SC_HANDLE, string lpSvcName, string lpDisplayName, int dwDesiredAccess, int dwServiceType, int dwStartType, int dwErrorControl, string lpPathName, string lpLoadOrderGroup, int lpdwTagId, string lpDependencies, string lpServiceStartName, string lpPassword);
        [DllImport("advapi32.dll")] public static extern void CloseServiceHandle(IntPtr SCHANDLE);
        [DllImport("advapi32.dll", SetLastError = true)] public static extern int StartService(IntPtr SVHANDLE, int dwNumServiceArgs, string lpServiceArgVectors);
        [DllImport("advapi32.dll", SetLastError = true)] public static extern int DeleteService(IntPtr SVHANDLE);
        [DllImport("advapi32.dll", SetLastError = true)] public static extern IntPtr OpenService(IntPtr SCHANDLE, string lpSvcName, int dwNumServiceArgs);
        [DllImport("kernel32.dll")] public static extern int GetLastError();

        #endregion

        #region Helpers

        private static string AtIndexOrNull(string[] elements, int index, Func<string, string> processValue = null) {
            if(index < 0) throw new Exception("Invalid Index");
            if(index >= elements.Length) return null;

            var value = elements[index];

            if(processValue != null) return processValue(value);

            return value;
        }
        
        #endregion

        #region Emulate Linq (for older versions of .NET)

        public delegate TReturn Func<TReturn>();
        public delegate TReturn Func<T, TReturn>(T p);

        public static IEnumerable<T> Skip<T>(IEnumerable<T> @this, int count) {
            var enumerator = @this.GetEnumerator();
            for(int i = 0; i < count; i++) {
                if(!enumerator.MoveNext()) yield break;
            }

            while(enumerator.MoveNext()) yield return enumerator.Current;
        }

        public static T[] ToArray<T>(IEnumerable<T> @this) {
            return new List<T>(@this).ToArray();
        }

        public static bool Any<T>(IEnumerable<T> @this, Func<T, bool> condition) {
            foreach(var element in @this) {
                if(condition(element)) return true;
            }

            return false;
        }

        #endregion

        #region Main method + testing code

        private static bool IsExecutablePath(string path) {
            var extension = Path.GetExtension(path);
            return File.Exists(path) && (extension == ".exe" || extension == ".com" || extension == ".bat" || extension == ".cmd");
        }

        private static int GetExecutablePathIndex(string[] elements) {
            for(var i = 0; i < elements.Length; i++) {
                var element = elements[i];
                if(IsExecutablePath(element)) return i;
            }

            return -1;
        }

        private static bool StringIsPath(string path){
            return Path.GetDirectoryName(path) == path;  // return it if it's a valid path string.
        }

        [STAThread]
        static void Main(string[] args) {
            const string CONST_RunAsServiceAction = "runasservice";

            var action = AtIndexOrNull(args, 0, o => o.ToLower().Trim());
            var actionParameters = ToArray(Skip(args, 1));

            switch(action) {
                case "-i":
                case "i":
                case "-install":
                case "install":
                    #region
                    {
                        var executablePathIndex = GetExecutablePathIndex(actionParameters);
                        var workDir = "";

                        if(executablePathIndex == -1) {
                            Console.WriteLine("You must specify the path to a console application executable. Nothing in the parameters you specified pointed to an executable.");
                            Console.WriteLine();
                            WriteHelpMessage();
                            return;
                        }

                        if (executablePathIndex > 3) {
                            // 0 is Service Name, 1 is the Display Name, working dir can be either 1 or 2, ExecutablePathIndex can be 0, 1, 2, 3
                            Console.WriteLine("You included too many parameters before specifying the executable path, perhaps your service name or display name contains spaces and you didn't put quotes around them. ");
                            Console.WriteLine();
                            WriteHelpMessage();
                            return;
                        }


                        var executablePath = actionParameters[executablePathIndex];
                        // var executableArguments = string.Join(" ", ToArray(Skip(actionParameters, executablePathIndex + 1)));
                        // Use the raw commandline instead of processed one like above. This will preserve the quotes: ""
                        string cmdLine = @Environment.CommandLine;
                        string executableFullPath = Path.GetFullPath(executablePath);
                        var startArg = cmdLine.IndexOf(executablePath) + executablePath.Length; // Start of raw arguments, leading space included.
                        var executablePathAndArguments = executableFullPath + cmdLine.Substring(startArg);

                        

                        string serviceName = "";
                        string serviceDisplayName = "";
                        switch (executablePathIndex) {
                            case 0:
                                // no argument before the executable.
                                serviceName = serviceDisplayName = Path.GetFileNameWithoutExtension(executablePath);
                                break;

                            case 1:
                                // 1 arg before executable
                                if (StringIsPath(actionParameters[0])) {
                                    // WorkDir Executable
                                    serviceName = serviceDisplayName = Path.GetFileNameWithoutExtension(executablePath);
                                    workDir = actionParameters[0];
                                }
                                else {
                                    // serviceName Executable
                                    serviceName = serviceDisplayName = actionParameters[0];
                                }
                                break;

                            case 2:
                                // 2 args before the executable
                                // ServiceName DisplayName/WorkDir Executable
                                serviceName = actionParameters[0];
                                if (StringIsPath(actionParameters[1])) {
                                    workDir = actionParameters[1];
                                    serviceDisplayName = serviceName;
                                }
                                else {
                                    serviceDisplayName = actionParameters[1];
                                }
                                break;

                            case 3:
                                // ServiceName DisplayName WorkDir Executable
                                serviceName = actionParameters[0];
                                serviceDisplayName = actionParameters[1];
                                workDir = actionParameters[2];
                                break;
                        }
                        var thisAppPath = GetThisExecutableFullPath();
                        var executableProxyPath = thisAppPath + " " + CONST_RunAsServiceAction + " ";

                        if (workDir != ""){
                            workDir = (workDir[0] == '"') ? workDir : '"' + workDir + '"'; // Add the quotes if it's not there yet.
                            // Add the workDir as the 1st parameter and the executable and args follows
                            executablePathAndArguments = workDir + " " + executablePathAndArguments;
                        }

                        if(Any(ServiceController.GetServices(), o => o.ServiceName.Trim().ToLower() == serviceName.Trim().ToLower())) {
                            Console.WriteLine("A service with that name already exists, you must first uninstall the existing service.");
                            Console.WriteLine("You can use the following command to uninstall this service:");
                            Console.WriteLine($"     {GetThisExecutableFilename()} uninstall {serviceName}");
                            return;
                        }

                        InstallService(executableProxyPath + executablePathAndArguments, serviceName, serviceDisplayName);

                        Console.WriteLine("Your service has been installed, to start your service type: ");
                        Console.WriteLine($@"      net start ""{serviceName}""");
                    } break;

                    #endregion
                case "u":
                case "-u":
                case "-uninstall":
                case "uninstall":
                    #region
                    { // scope
                        if(actionParameters.Length == 0) {
                            Console.WriteLine("You need to specify the name of the service after 'uninstall'.");
                            Console.WriteLine();
                            WriteHelpMessage();
                            return;
                        }

                        var serviceNameOrPath = string.Join(" ", actionParameters);

                        string serviceName;
                        if (File.Exists(serviceNameOrPath)) {
                            serviceName = Path.GetFileNameWithoutExtension(serviceNameOrPath);
                        } else {
                            serviceName = serviceNameOrPath;
                        }

                        if (!Any(ServiceController.GetServices(), o => o.ServiceName.Trim().ToLower() == serviceName.Trim().ToLower())) {
                            Console.WriteLine($"No service with the name '{serviceName}' exists.");
                            return;
                        }

                        if(!IsServiceStopped(serviceName)) {
                            Console.WriteLine("That service is currently running or paused, you must first stop the service before it can be removed.");
                            return;
                        }

                        try {
                            UninstallService(serviceName);
                        } catch(AlreadyMarkedForDeletion) {
                            Console.WriteLine("Failed to uninstall service because it is already queued to be deleted, this means that for some reason the orignal attempt to unstall the service couldn't uninstall it completly, I suspect restarting the computer shoudl fix it.");
                            return;
                        }

                        Console.WriteLine($@"The service ""{serviceName}"" has been successfully uninstalled.");
                    } break;
                    #endregion
                case "-fix":
                case "fix":
                case "-fixservice":
                case "-fixservices":
                case "fixservice":
                case "fixservices":
                    #region
                    {
                        // TODO: Are you sure - this will update any services installed using RunAsService to use RunAsService from this location (don't provide this option unless you will also provide a -y switch to skip it)
                        if(actionParameters.Length != 0) {
                            Console.WriteLine("There should be no parameters after 'fixservices'");
                            Console.WriteLine();
                            WriteHelpMessage();
                            return;
                        }

                        var services = ServiceController.GetServices();
                        var thisExecutableName = GetThisExecutableFilename().ToLower() + ".exe";
                        var serviceCount = 0;

                        foreach(var service in services) {
                            var path = ((string)GetServiceRegistryValue(service.ServiceName, "ImagePath")).ToLower();

                            var executableNameIndex = path.IndexOf(thisExecutableName);

                            if(executableNameIndex == -1) continue;

                            var everythingAfter = path.Substring(executableNameIndex + 1);
                            var newPath = GetThisExecutableFullPath() + everythingAfter;

                            Console.WriteLine("Fixing the path for: " + service.ServiceName);

                            SetServiceRegistryValue(service.ServiceName, "ImagePath", newPath);
                            serviceCount++;
                        }

                        Console.WriteLine($"Done. {serviceCount} services fixed.");
                    }
                    break;
                    #endregion
                case CONST_RunAsServiceAction: // NOTE, this is an internal action used by the installed services, not called directly by the users
                    #region
                    {
                        // actionParameters: 0 could be the working dir, console executable is 0 or 1, then follow by args.
                        if (actionParameters.Length == 0 ) {
                            WriteHelpMessage();
                            return;
                        }

                        string executablePath;

                        var prompt = $"The frist and second parameter after '{CONST_RunAsServiceAction}' should follow this syntax: [workingPath] fullPathToExecutable, but there was a problem in the paths.";
                        var workDir = ""; var skipArg = 1;

                        if (StringIsPath(actionParameters[0]) && actionParameters.Length > 1){
                            // WorkDir Executable...
                            workDir = actionParameters[0];
                            executablePath = actionParameters[1];
                            skipArg = 2;
                        }
                        else {
                            // Executable
                            executablePath = actionParameters[0];
                        }

                        if (!IsExecutablePath(executablePath)) {
                            Console.WriteLine(prompt);
                            WriteHelpMessage();
                            return;
                        }
                        // If empty, get the workDir from executable
                        workDir = ( workDir=="" ? Path.GetDirectoryName(executablePath) : workDir );
                        
                        var arguments = ToArray(Skip(actionParameters, skipArg));

                        ServiceBase.Run(new Service(executablePath, arguments, workDir));
                    }
                    break;
                    #endregion
                default:
                    if(args.Length != 0) {
                        Console.WriteLine("There appears to be something wrong with the parameters you specified.");
                        Console.WriteLine();
                    }

                    WriteHelpMessage();
                    return;
            }
        }

        private static string GetThisExecutableFullPath() { return Assembly.GetExecutingAssembly().Location; }
        private static string GetThisExecutableFilename() { return Path.GetFileNameWithoutExtension(GetThisExecutableFullPath()); }

        private static void WriteHelpMessage() {
            var executableName = GetThisExecutableFilename();

            Console.Write($@"
This tool allows you to setup a regular console application to run as 
it service. Below you will find descriptions and examples of how to do 
this.

IMPORTANT: Any services you install using this tool will require that 
this tool remain on that computer in the same location in order for 
those services to continue functioning. Therefore before installing 
any services you should make sure this tool is somewhere where it can 
remain permanently. If you do end up moving this tool use the 
'fixservices' action to fix the existing services.
(details on how to use 'fixservices' can be found below)

{executableName}
    Typing just the name of the tool without specifying any parameters.
    Or specifying incorrect paramters will bring you to this help 
    screen you are currently reading.

{executableName} install [Name] [Display Name] [PathToWorkDirectory] PathToExecutable [Args]
    Name
        The name of the service, if none is specified the name will 
        default to the name of the executable.

        You might choose to give it a different name than the 
        executable to keep some kind of existing convention, make it 
        friendlier or make it easier to use with commands like 
        'net start' and 'net stop'
    
    Display Name
        This is how the service name will be displayed in the windows
        services list. If no display name is specified it will default
        to Name, if Name is not specified, it will default to the name
        of the executable.

        Generally the display name is longer and more descriptive than 
        the name and gives the user a better idea of what the service 
        is and/or does.

    PathToWorkDirectory
        The working directory you want the application to work on.
        In Windows usually the executable cannot write any data to it's
        own location. So setting a different working path allows the
        console to read/write data in that specified location.

        It should be quoted like ""c:\\my working path""

        If not specify the working directory, then the program will
        assume it to be where the executable is.

    PathToExecutable
        The location of the application you want to run as a service.
        
        Note, the tool will check if this executable exists, if it
        doesn't find it will not install it.

    Args
        Any arguments that you want to pass to your executable.

{executableName} uninstall NameOrPath
    NameOrPath
        The name of the service you would like to uninstall or the path
        to the executable.

        Using the path of the executable only works if you only used
        the path of the executable when in you installed the service.

        That is it only works if you didn't specify a Name or Display name
        when you performed the install action.

{executableName} fixservices
        Use this action if you move this tool. This is because 
        services installed using this tool rely on this tool remaining
        on that computer and at the same location. If you do not call 
        'fixservices' after moving this tool the existing services 
        installed using this tool will stop functioning.

EXAMPLES

{executableName} install ""c:\my apps\Myapp.exe""
        Installs Myapp as a service called 'Myapp'

{executableName} install ""c:\my data path"" ""c:\my apps\Myapp.exe""
        Installs Myapp as a service called 'Myapp' with working directory
        as 'c:\my data path'

{executableName} install ""c:\my apps\Myapp.exe"" arg0 arg1
        Installs Myapp as a service called 'Myapp' passes
        the arg0 and arg1 to Myapp.exe when it's started.

{executableName} install ""c:\my data path"" ""c:\my apps\Myapp.exe"" arg0 arg1
        Installs Myapp as a service called 'Myapp' passes the arg0
        and arg1 to Myapp.exe when it's started, using 'c:\my data path'
        as the working directory.

{executableName} install ""My Service"" ""c:\my apps\Myapp.exe""
        Installs Myapp as a service called ""My Service""

{executableName} install ""My Service"" ""c:\my data path"" ""c:\my apps\Myapp.exe""
        Installs Myapp as a service called ""My Service""
        using 'c:\my data path' as the working directory.

{executableName} install ""My Service"" ""My Super Cool Service"" ""c:\my apps\Myapp.exe""
        Installs Myapp as a service internally called ""My Service""
        when using commands like 'net start' and 'net stop' and shows
        up as ""My Super Cool Service"" in Window's services list.

{executableName} install ""My Service"" ""My Super Cool Service"" ""c:\my data path"" ""c:\my apps\Myapp.exe""
        Installs Myapp as a service internally called ""My Service""
        when using commands like 'net start' and 'net stop' and shows
        up as ""My Super Cool Service"" in Window's services list.
        Its working directory will be 'c:\my data path'

{executableName} uninstall ""My Service""
        Uninstalls the service.

{executableName} fixservices
        Updates services installed using {executableName} to point to the
        new location.

NOTES

You can use Windows built in commands to start and stop your services. 
For example you can use:
    net start ""My Service""
and
    net stop ""My Service""

Where ""My Service"" would be replaced with the name of your service.

Or you can run the ""Services"" in 'Control Panel->Administrative Tools'
to start/stop the service by name.

CREDITS

This tool was created by Luis Perez on April 2011. For more 
information visit RunAsService.com. Thank you.
");
        }

        #endregion

        private static bool IsServiceStopped(string serviceName) {
            return new ServiceController(serviceName).Status == ServiceControllerStatus.Stopped;
        }

        private static void SetServiceRegistryValue(string serviceName, string registryKeyName, object value) {
            var systemKey = Microsoft.Win32.Registry.LocalMachine.OpenSubKey("System");
            var currentControlSetKey = systemKey.OpenSubKey("CurrentControlSet");
            var servicesKey = currentControlSetKey.OpenSubKey("Services");
            var serviceKey = servicesKey.OpenSubKey(serviceName, true);
            serviceKey.SetValue(registryKeyName, value);
        }

        private static object GetServiceRegistryValue(string serviceName, string registryKeyName) {
            var systemKey = Microsoft.Win32.Registry.LocalMachine.OpenSubKey("System");
            var currentControlSetKey = systemKey.OpenSubKey("CurrentControlSet");
            var servicesKey = currentControlSetKey.OpenSubKey("Services");
            var serviceKey = servicesKey.OpenSubKey(serviceName);
            return serviceKey.GetValue(registryKeyName);
        }

        private static void InstallService(string path, string name, string displayName) { // This method installs and runs the service in the service control manager.
            #region Constants

            const int CONST_ScManager_CreateService = 0x0002; // SC_MANAGER_CREATE_SERVICE
            const int CONST_Win32OwnProcess = 0x00000010; // SERVICE_WIN32_OWN_PROCESS
            const int CONST_ErrorNormal = 0x00000001; // SERVICE_ERROR_NORMAL
            const int CONST_StandardRightsRequired = 0xF0000; // STANDARD_RIGHTS_REQUIRED
            const int CONST_QueryConfig = 0x0001; // SERVICE_QUERY_CONFIG
            const int CONST_ChangeConfig = 0x0002; // SERVICE_CHANGE_CONFIG
            const int CONST_QueryStatus = 0x0004; // SERVICE_QUERY_STATUS
            const int CONST_EnumDependents = 0x0008; // SERVICE_ENUMERATE_DEPENDENTS
            const int CONST_Start = 0x0010; // SERVICE_START
            const int CONST_Stop = 0x0020; // SERVICE_STOP
            const int CONST_PauseContinue = 0x0040; // SERVICE_PAUSE_CONTINUE
            const int CONST_Interrogate = 0x0080; // SERVICE_INTERROGATE
            const int CONST_UserDefinedControl = 0x0100; // SERVICE_USER_DEFINED_CONTROL
            const int CONST_AllAccess = ( // SERVICE_ALL_ACCESS
                    CONST_StandardRightsRequired 
                    | CONST_QueryConfig 
                    | CONST_ChangeConfig 
                    | CONST_QueryStatus 
                    | CONST_EnumDependents 
                    | CONST_Start 
                    | CONST_Stop 
                    | CONST_PauseContinue 
                    | CONST_Interrogate 
                    | CONST_UserDefinedControl
                    );
            const int CONST_AutoStart = 0x00000002; // SERVICE_AUTO_START

            #endregion 

            var scManagerHandle = OpenSCManager(null, null, CONST_ScManager_CreateService);

            if (scManagerHandle.ToInt32() == 0) {
                var errorCode = GetLastError();

                if (errorCode == 5) throw new Exception("Could not install service, you have to run this installer as an administrator"); // access denied

                throw new Exception(GetLastError().ToString()); //Console.WriteLine("SCM not opened successfully");
            }
            
            var serviceHandle = CreateService(scManagerHandle, name, displayName, CONST_AllAccess, CONST_Win32OwnProcess, CONST_AutoStart, CONST_ErrorNormal, path, null, 0, null, null, null);

            if (serviceHandle.ToInt32() == 0) {
                CloseServiceHandle(scManagerHandle);
                throw new Exception();
            }

            CloseServiceHandle(scManagerHandle);
        }

        private static void UninstallService(string name) { // uninstalls the service from the service conrol manager.
            const int CONST_GenericWrite = 0x40000000;
            var scManagerHandle = OpenSCManager(null, null, CONST_GenericWrite);

            if (scManagerHandle.ToInt32() == 0) throw new Exception();

            const int CONST_Delete = 0x10000;

            var serviceHandle = OpenService(scManagerHandle, name, CONST_Delete);
            if (serviceHandle.ToInt32() == 0) {
                var errorCode = GetLastError();
                const int CONST_ErrorServiceDoesNotExist = 1060;
                if (errorCode == CONST_ErrorServiceDoesNotExist) throw new Exception("No service with that name exist.");
                throw new Exception("Error calling OpenService() - " + errorCode.ToString());
            }

            var returnCode = DeleteService(serviceHandle);

            if (returnCode == 0) {
                var errorCode = GetLastError();
                CloseServiceHandle(scManagerHandle);

                if(errorCode == 1072) throw new AlreadyMarkedForDeletion();

                throw new Exception("Error calling DeleteService() - " + errorCode.ToString());
            } 

            CloseServiceHandle(scManagerHandle);
            return;
        }
    }

    #region Exceptions

    public abstract class RunAsServiceException : Exception { // this just serves as a base class
        public RunAsServiceException(string message)
            : base(message) {
            // do nothing - calling base constructor
        }
    }

    public class AlreadyMarkedForDeletion : RunAsServiceException {
        public AlreadyMarkedForDeletion()
            : base("Error calling DeleteService() - Service already marked for deletion - 1072") {
            // do nothing - calling base constructor
        }
    }

    #endregion

    public partial class Service : ServiceBase {
        private Process process;
        private string workingDirectory;
        private string consoleApplicationExecutablePath;
        private string[] arguments;

        public Service(string consoleApplicationExecutablePath, string[] arguments, string workDir="") {
            this.consoleApplicationExecutablePath = consoleApplicationExecutablePath;
            this.arguments = arguments;
            this.workingDirectory = workDir;
        }

        protected override void OnStart(string[] args) {
            var oProcessStartInfo = new ProcessStartInfo(consoleApplicationExecutablePath, string.Join(" ", arguments)) {
                UseShellExecute = false,
                CreateNoWindow = true,
                ErrorDialog = false,
                WindowStyle = ProcessWindowStyle.Hidden,
                WorkingDirectory = workingDirectory,
            };

            process = Process.Start(oProcessStartInfo);
        }

        protected override void OnStop() {
            process.Kill();
            process = null;
        }
    }
}

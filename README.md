# RunAsService
RunAsService is a command line tool that allows you to setup a regular  console application to run as a service.

This tool requires that .NET Framework 4.5 be already installed on your computer.
If you do not have .NET Framework 4.5 this tool will display a message and not run.

## IMPORTANT: Any services you install using this tool will
require that this tool remain on that computer in the same
location in order for those services to continue functioning. Therefore
before installing any services you should make sure this tool is
somewhere where it can remain permanently. If you do end up moving
this tool use the 'fixservices' action to fix the existing services.
(details on how to use 'fixservices' can be found below)

## Usage / Syntax
```
RunAsService
    Typing just the name of the tool without specifying any parameters.
    Or specifying incorrect paramters will bring you to this help 
    screen you are currently reading.

RunAsService install [Name] [Display Name] [Work Dir] PathToExecutable [Args]
    Name
        The name of the service, if none is specified the name
        will default to the name of the executable.
        You might choose to give it a different name than
        the executable to keep some kind of existing convention,
        make it friendlier or make it easier to use with commands like 
        'net start' and 'net stop'
    
    Display Name
        This is how the service name will be displayed in the windows
        services list. If no display name is specified it will default
        to Name, if Name is not specified, it will default to the name
        of the executable.

        Generally the display name is longer and more descriptive
        than the name and gives the user a better idea of what
        the service is and/or does.
    Work Dir
        This is the working directory, where the executable reads and
        writes data. If you omit it, the default working directory is
        where the executable is.
    PathToExecutable
        The location of the application you want to run as a service.
        
        Note, the tool will check if this executable exists, if it
        doesn't find it will not install it.

    Args
        Any arguments that you want to pass to your executable.

RunAsService uninstall NameOrPath
    NameOrPath
        The name of the service you would like to uninstall or the path
        to the executable.

        Using the path of the executable only works if you only used
        the path of the executable when in you installed the service.

        That is it only works if you didn't specify a Name or Display name
        when you performed the install action.

RunAsService fixservices
        Use this action if you move this tool. This is because 
        services installed using this tool rely on this tool remaining
        on that computer and at the same location. If you do not call 
        'fixservices' after moving this tool the existing services 
        installed using this tool will stop functioning.
```
## EXAMPLES

* `RunAsService install "c:\my apps\Myapp.exe"` <br>
        Installs Myapp as a service called 'Myapp'<br>
        
* `RunAsService install "c:\my apps\Myapp.exe" arg0 arg1` <br>
        Installs Myapp as a service called 'Myapp' passes
        the arg0 and arg1 to Myapp.exe when it's started.

* `RunAsService install "c:\my data\path" "c:\my apps\Myapp.exe" arg0 arg1` <br>
        Installs Myapp as a service called 'Myapp' passes
        the arg0 and arg1 to Myapp.exe when it's started.<br>
        Use "c:\my data\path" as the working directory.
        
* `RunAsService install "My Service" "c:\my apps\Myapp.exe"` <br>
        Installs Myapp as a service called "My Service"

* `RunAsService install "My Service" "My Super Cool Service" "c:\my apps\Myapp.exe"` <br>
        Installs Myapp as a service internally called "My Service"
        when using commands like 'net start' and 'net stop' and shows
        up as "My Super Cool Service" in Window's services list.

* `RunAsService uninstall "My Service"` <br>
        Uninstalls the service.

* `RunAsService fixservices` <br>
        Updates services installed using RunAsService to point to the
        new location.

## NOTES

You can use Windows built in commands to start and stop your services. For example you can use:

    net start "My Service"

and

    net stop "My Service"

Where "My Service" would be replaced with the name of your service.

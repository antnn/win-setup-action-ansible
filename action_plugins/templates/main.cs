using System;
using System.IO;
using System.Diagnostics;
using System.Management;
using System.Reflection;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Web.Script.Serialization; // keep FullName, otherwise - undefined reference
using Dict = System.Collections.Generic.Dictionary<string, object>;
using Microsoft.Win32;


internal class JSONComparer : IComparer<object>
{
    public int Compare(object a, object b)
    {
        Int32 _a = (Int32)((Dict)a)["index"];
        Int32 _b = (Int32)((Dict)b)["index"];
        return (_a.CompareTo(_b));
    }
}

internal interface IAction
{
    void Invoke();
}
public abstract class ActionBase : IAction
{
    // Assuming ExpandString is a method that expands environment variables or similar
    protected string ExpandString(string input)
    {
        return Environment.ExpandEnvironmentVariables(input);
    }

    public void Invoke()
    {
        throw new NotImplementedException();
    }
}


internal class FileAction : IAction
{
    private IDictionary<string, object> action;
    private string path;
    private bool parents;
    private string value;
    public enum State
    {
        Directory,
        Touch,
        Present,
        Absent
    }
    private State state;
    public FileAction(IDictionary<string, object> action)
    {
        this.action = action;
        this.path = (string)action["path"];
        this.parents = (bool)action["parents"];
        this.state = (State)Enum.Parse(typeof(State), (string)action["state"], true);
        this.value = (string)action["value"];

    }
    void IAction.Invoke()
    {
        switch (state)
        {
            case State.Directory:
                Directory.CreateDirectory(this.path);
                break;

            case State.Touch:
                File.Create(path);
                break;

            case State.Present:
                File.WriteAllText(path, this.value);
                break;

            case State.Absent:
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
                else if (Directory.Exists(path))
                {
                    Directory.Delete(path, true);
                }
                else
                {
                    throw new IOException("Path does not exist");
                }
                break;
        }
    }
}

public class RegistryAction : ActionBase
{
    public enum RegistryState
    {
        Present,
        Absent,
        Property
    }
    private string path;
    private object value;
    private bool force;
    private RegistryValueKind itemType;
    private RegistryState state;
    private bool? recurse;

    public RegistryAction(Dictionary<string, object> item)
    {
        if (item == null || !item.ContainsKey("state") || string.IsNullOrEmpty(item["state"].ToString()))
        {
            throw new ArgumentException("The 'state' property is required.");
        }

        path = ExpandString(item["path"].ToString());
        value = item["value"];
        force = item.ContainsKey("force") && Convert.ToBoolean(item["force"]);
        itemType = item.ContainsKey("type") ? ParseRegistryItemType(item["type"].ToString()) : RegistryValueKind.String;
        state = ParseRegistryState(item["state"].ToString());
        recurse = item.ContainsKey("recurse") ? (bool?)Convert.ToBoolean(item["recurse"]) : null;
    }
    private static RegistryState ParseRegistryState(string state)
    {
        RegistryState parsedState;
        try
        {
            parsedState = (RegistryState)Enum.Parse(typeof(RegistryState), state, true);
        }
        catch (ArgumentException)
        {
            throw new ArgumentException("Invalid registry state: " + state);
        }
        return parsedState;
    }

    private static RegistryValueKind ParseRegistryItemType(string type)
    {
        RegistryValueKind parsedType;
        try
        {
            parsedType = (RegistryValueKind)Enum.Parse(typeof(RegistryValueKind), type, true);
        }
        catch (ArgumentException)
        {
            throw new ArgumentException("Invalid registry item type: " + type);
        }
        return parsedType;
    }

    public void Execute()
    {
        switch (state)
        {
            case RegistryState.Present:
                CreateOrUpdateRegistryKey();
                break;
            case RegistryState.Property:
                CreateOrUpdateRegistryValue();
                break;
            case RegistryState.Absent:
                DeleteRegistryKeyOrValue();
                break;
            default:
                throw new InvalidOperationException("Unsupported registry state: " + state);
        }
    }
    private void CreateOrUpdateRegistryKey()
    {
        var baseKey = RegistryKey.OpenRemoteBaseKey(RegistryHive.LocalMachine, null);
        var key = force ? baseKey.CreateSubKey(path, RegistryKeyPermissionCheck.ReadWriteSubTree) : baseKey.OpenSubKey(path, RegistryKeyPermissionCheck.ReadWriteSubTree);

        if (key == null)
        {
            throw new InvalidOperationException("Failed to create or open registry key: " + path);
        }

        // If value is provided, set it as the default value for the key
        if (value != null)
        {
            key.SetValue(null, value, itemType);
        }

        key.Close();
        baseKey.Close();
    }


    private void CreateOrUpdateRegistryValue()
    {
        var subKeyPath = GetSubKeyPath(path);
        var valueName = GetValueName(path);

        using (var baseKey = RegistryKey.OpenRemoteBaseKey(RegistryHive.LocalMachine, null))
        {
            using (var key = baseKey.OpenSubKey(subKeyPath, true))
            {
                if (key == null)
                {
                    throw new InvalidOperationException("Failed to open registry key: " + subKeyPath);
                }
                key.SetValue(valueName, value, itemType);
            }
        }
    }


    private void DeleteRegistryKeyOrValue()
    {
        var subKeyPath = GetSubKeyPath(path);
        var valueName = GetValueName(path);

        using (var baseKey = RegistryKey.OpenRemoteBaseKey(RegistryHive.LocalMachine, null))
        {
            using (var key = baseKey.OpenSubKey(subKeyPath, true))
            {
                if (key == null)
                {
                    throw new InvalidOperationException("Failed to open registry key: " + subKeyPath);
                }

                if (string.IsNullOrEmpty(valueName))
                {
                    key.DeleteSubKeyTree(subKeyPath);
                }
                else
                {
                    key.DeleteValue(valueName);
                }
            }
        }
    }


    private static string GetSubKeyPath(string fullPath)
    {
        var lastIndex = fullPath.LastIndexOf('\\');
        return lastIndex > 0 ? fullPath.Substring(0, lastIndex) : fullPath;
    }

    private static string GetValueName(string fullPath)
    {
        var lastIndex = fullPath.LastIndexOf('\\');
        return lastIndex > 0 ? fullPath.Substring(lastIndex + 1) : string.Empty;
    }

    new public void Invoke()
    {
        Execute();
    }
}


internal class UnzipAction : IAction
{
    private string zipPath;
    private string extractPath;

    public UnzipAction(IDictionary<string, object> action)
    {
        this.zipPath = (string)action["path"];
        this.extractPath = (string)action["destination"];
    }

    public void Invoke()
    {
        // Validate paths exist before creating Folders
        if (!File.Exists(zipPath))
        {
            throw new FileNotFoundException("Zip file not found");
        }

        if (!Directory.Exists(extractPath))
        {
            throw new DirectoryNotFoundException("Extract directory not found");
        }
        Type type = Type.GetTypeFromProgID("Shell.Application");
        Shell32.IShellDispatch shell = (Shell32.IShellDispatch)Activator.CreateInstance(type);

        Shell32.Folder source = shell.NameSpace(zipPath);
        Shell32.Folder destination = shell.NameSpace(extractPath);
        foreach (object item in source.Items())
        {
            destination.CopyHere(item, 8 | 16 | 512 | 1024);
        }

    }
}

public class ExeAction : IAction
{
    protected string packagePath;
    protected string arguments;

    public ExeAction(IDictionary<string, object> action)
    {
        this.packagePath = (string)action["path"];
        this.arguments = (string)action["args"];
    }

    public void Invoke()
    {
        Console.WriteLine("Installing: " + this.packagePath);
        System.Diagnostics.ProcessStartInfo startInfo = new System.Diagnostics.ProcessStartInfo();
        startInfo.FileName = this.packagePath;
        startInfo.Arguments = this.arguments;
        startInfo.WindowStyle = System.Diagnostics.ProcessWindowStyle.Hidden;
        System.Diagnostics.Process.Start(startInfo).WaitForExit();
    }
}

public class MsuAction : ExeAction
{
    public MsuAction(IDictionary<string, object> action)
        : base(action)
    {
        var package = (string)action["path"];
        this.packagePath = "wusa.exe";
        this.arguments = package + " " + (string)action["args"];
    }
}

public class MsiAction : IAction
{
    [DllImport("msi.dll", CharSet = CharSet.Auto, SetLastError = true)]
    static extern UInt32 MsiInstallProduct([MarshalAs(UnmanagedType.LPTStr)] string packagePath,
        [MarshalAs(UnmanagedType.LPTStr)] string commandLine);

    private string packagePath;
    private string arguments;

    public MsiAction(IDictionary<string, object> action)
    {
        this.packagePath = (string)action["path"];
        this.arguments = (string)action["args"];
    }

    public void Invoke()
    {
        Console.WriteLine("Installing: " + this.packagePath);

        System.Text.StringBuilder sb = new System.Text.StringBuilder(arguments + " ACTION=ADMIN");
        //args format Property=Setting Property=Setting.
        uint result = MsiInstallProduct(packagePath, sb.ToString());
        if (result != 0)
        {
            throw new Exception("MsiInstallProduct failed. Error: " + result);
        }
    }
}
class DismAction : IAction
{
    private const string DismAssembly = "DismApi.dll";
    private const string DISM_ONLINE_IMAGE = "DISM_{53BFAE52-B167-4E2F-A258-0A37B57FF845}"; // Placeholder value, you need to use the actual constant from the DISM API
    private string packagePath;
    private bool ignoreCheck;
    private bool preventPending;

    [DllImport(DismAssembly, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Error)]
    public static extern int DismOpenSession(string imagePath, string windowsDirectory, string systemDrive, out IntPtr session);

    [DllImport(DismAssembly, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Error)]
    public static extern int DismAddPackage(IntPtr session, string packagePath, bool ignoreCheck, bool preventPending, IntPtr progress, IntPtr userData, IntPtr callback);

    [DllImport(DismAssembly, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Error)]
    public static extern int DismCloseSession(IntPtr session);

    public DismAction(IDictionary<string, object> action)
    {
        if (action.ContainsKey("path"))
        {
            this.packagePath = (string)action["path"];
        }
        else
        {
            throw new ArgumentException("The action dictionary must contain a 'path' key.");
        }

        this.ignoreCheck = false;
        this.preventPending = false;

        if (action.ContainsKey("ignoreCheck"))
        {
            this.ignoreCheck = (bool)action["ignoreCheck"];
        }

        if (action.ContainsKey("preventPending"))
        {
            this.preventPending = (bool)action["preventPending"];
        }
    }

    public void Invoke()
    {
        IntPtr session;
        int result = DismOpenSession(DISM_ONLINE_IMAGE, null, null, out session);
        if (result != 0)
        {
            throw new Exception("Failed to open DISM session for the online image.");
        }

        try
        {
            result = DismAddPackage(session, packagePath, ignoreCheck, preventPending, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
            if (result != 0)
            {
                throw new Exception("Failed to add package to the online image.");
            }
        }
        finally
        {
            result = DismCloseSession(session);
            if (result != 0)
            {
                throw new Exception("Failed to close DISM session for the online image.");
            }
        }
    }
}

public class CopyAction : ActionBase
{
    private string source;
    private string destination;
    private bool force;

    public CopyAction(IDictionary<string, object> action)
    {
        if (action == null)
        {
            throw new ArgumentNullException("action");
        }

        if (!action.ContainsKey("src") || !action.ContainsKey("dest"))
        {
            throw new ArgumentException("The action dictionary must contain 'src' and 'dest' keys.");
        }

        this.source = ExpandString((string)action["src"]);
        this.destination = ExpandString((string)action["dest"]);

        // 'force' parameter is optional; default to false if not provided
        this.force = false;
        if (action.ContainsKey("force"))
        {
            this.force = (bool)action["force"];
        }
    }

    public void Invoke()
    {
        if (string.Equals(source, destination, StringComparison.OrdinalIgnoreCase))
        {
            Console.WriteLine("Source and destination are the same. No action taken.");
            return;
        }

        Console.WriteLine("Copying: " + source + " to " + destination);
        try
        {
            // Use the 'force' parameter to decide whether to overwrite existing files
            File.Copy(source, destination, force);
        }
        catch (Exception ex)
        {
            // Handle any exceptions that may occur during the copy
            Console.WriteLine("An error occurred while copying the file: " + ex.Message);
        }
    }
}

public class CmdAction : ActionBase
{
    private string command;

    public CmdAction(IDictionary<string, object> action)
    {
        if (action == null)
        {
            throw new ArgumentNullException("action");
        }

        if (!action.ContainsKey("cmd"))
        {
            throw new ArgumentException("The action dictionary must contain a 'cmd' key.");
        }

        this.command = ExpandString((string)action["cmd"]);
    }

    public void Invoke()
    {
        Console.WriteLine("Running command: " + command);
        try
        {
            ProcessStartInfo startInfo = new ProcessStartInfo();
            startInfo.FileName = "cmd.exe";
            startInfo.Arguments = "/C " + command;
            startInfo.WindowStyle = ProcessWindowStyle.Hidden;
            Process process = Process.Start(startInfo);
            process.WaitForExit();
        }
        catch (Exception ex)
        {
            // Handle any exceptions that may occur during the command execution
            Console.WriteLine("An error occurred while running the command: " + ex.Message);
        }
    }
}



public class PathAction : ActionBase
{
    private string pathToModify;
    private PathState state;

    // Define an enum for the state
    public enum PathState
    {
        Present,
        Absent
    }

    public PathAction(IDictionary<string, object> action)
    {
        if (action == null || !action.ContainsKey("path"))
        {
            throw new ArgumentException("The action dictionary must contain a 'path' key.");
        }

        this.pathToModify = ExpandString((string)action["path"]);
        // Set "Present" as the default state if "state" key is not provided
        this.state = PathState.Present;
        if (action.ContainsKey("state"))
        {
            string stateStr = (string)action["state"];
            try
            {
                state = (PathState)Enum.Parse(typeof(PathState), stateStr, true);
            }
            catch (ArgumentException)
            {
                // Handle the case where the string does not represent a valid state
                throw new ArgumentException("Invalid state value. Only 'Present' or 'Absent' are supported.");
            }
        }
    }

    public void Invoke()
    {
        string currentPath = Environment.GetEnvironmentVariable("PATH", EnvironmentVariableTarget.Machine);
        string[] paths = currentPath.Split(new char[] { ';' }, StringSplitOptions.RemoveEmptyEntries);
        var pathList = new List<string>(paths);

        switch (state)
        {
            case PathState.Present:
                if (!pathList.Contains(pathToModify))
                {
                    pathList.Add(pathToModify);
                    string newPath = string.Join(";", pathList.ToArray());
                    Environment.SetEnvironmentVariable("PATH", newPath, EnvironmentVariableTarget.Machine);
                }
                break;
            case PathState.Absent:
                if (pathList.Contains(pathToModify))
                {
                    pathList.Remove(pathToModify);
                    string newPath = string.Join(";", pathList.ToArray());
                    Environment.SetEnvironmentVariable("PATH", newPath, EnvironmentVariableTarget.Machine);
                }
                break;
            default:
                throw new ArgumentException("Invalid state value. Only 'Present' or 'Absent' are supported.");
        }
    }
}



public class Autostart : ActionBase
{
    private string entry;
    private string interpreter;
    private string args;
    private string destination;

    public Autostart(IDictionary<string, object> item)
    {
        if (item == null)
        {
            throw new ArgumentNullException("item");
        }

        if (!item.ContainsKey("file") || !item.ContainsKey("interpreter") || !item.ContainsKey("destination"))
        {
            throw new ArgumentException("The item dictionary must contain 'name', 'interpreter', and 'destination' keys.");
        }

        this.entry = (string)item["file"];
        this.interpreter = (string)item["interpreter"];
        this.destination = ExpandString((string)item["destination"]);
        this.args = item.ContainsKey("args") ? (string)item["args"] : string.Empty;
    }

    public void Invoke()
    {
        string value = string.Format("cmd /C \"{0}\" \"{1}\\{2}\" {3}", interpreter, destination, entry, args);

        try
        {
            using (RegistryKey key = Registry.LocalMachine.OpenSubKey("Software\\Microsoft\\Windows\\CurrentVersion\\Run", true))
            {
                if (key != null)
                {
                    key.SetValue(entry, value, RegistryValueKind.String);
                }
                else
                {
                    throw new Exception("Unable to open registry key for modification.");
                }
            }
        }
        catch (Exception ex)
        {
            // Handle any exceptions that may occur during the registry operation
            Console.WriteLine("An error occurred while setting the autostart entry: " + ex.Message);
        }
    }
}



internal class CustomDispatchConverter : JavaScriptConverter
{
    public override object Deserialize(IDictionary<string, object> dictionary, Type type, JavaScriptSerializer serializer)
    {
        IAction action = null;
        if (dictionary.ContainsKey("file"))
        {
            action = new FileAction((Dictionary<string, object>)dictionary["file"]);
        }
        else if (dictionary.ContainsKey("zip"))
        {
            action = new UnzipAction((Dictionary<string, object>)dictionary["zip"]);
        }
        else if (dictionary.ContainsKey("msi"))
        {
            action = new MsiAction((Dictionary<string, object>)dictionary["msi"]);
        }
        else if (dictionary.ContainsKey("exe"))
        {
            action = new ExeAction((Dictionary<string, object>)dictionary["exe"]);
        }
        else if (dictionary.ContainsKey("msu"))
        {
            action = new ExeAction((Dictionary<string, object>)dictionary["msu"]);
        }
        else if (dictionary.ContainsKey("cab"))
        {
            action = new DismAction((Dictionary<string, object>)dictionary["cab"]);
        }
        else if (dictionary.ContainsKey("copy"))
        {
            action = new CopyAction((Dictionary<string, object>)dictionary["copy"]);
        }
        else if (dictionary.ContainsKey("cmd"))
        {
            action = new DismAction((Dictionary<string, object>)dictionary["cmd"]);
        }
        else if (dictionary.ContainsKey("registry"))
        {
            action = new DismAction((Dictionary<string, object>)dictionary["registry"]);
        }
        else if (dictionary.ContainsKey("path"))
        {
            action = new PathAction((Dictionary<string, object>)dictionary["path"]);
        }
        else if (dictionary.ContainsKey("autostart"))
        {
            action = new PathAction((Dictionary<string, object>)dictionary["path"]);
        }
        else
        {
            throw new Exception("Unknown ansible action type");
        }
        return action;
    }

    public override IDictionary<string, object> Serialize(object obj, JavaScriptSerializer serializer)
    {
        Dictionary<string, object> dict = new Dictionary<string, object>();
        return dict;
    }
    public override IEnumerable<Type> SupportedTypes
    {
        get { return new[] { typeof(Dictionary<string, object>) }; }
    }
}





public class WinImageBuilderAutomation
{
    public static void Main()
    {
        var mainPs1Autostart = new Dictionary<string, object>
        {
            { "file", "start.ps1" },
            { "interpreter", "powershell.exe -NoExit -ExecutionPolicy Bypass -File" },
            { "destination", Environment.ExpandEnvironmentVariables("%CONFIGDRIVE%") }
        };

        Autostart autostartAction = new Autostart(mainPs1Autostart);
        autostartAction.Invoke();
        SetNetworksLocationToPrivate();

    }
    public static void SetNetworksLocationToPrivate()
    {
        INetworkListManager nlm = (INetworkListManager)new NetworkListManagerClass();
        IEnumerable networks = nlm.GetNetworks(NetworkConnectivityLevels.All);
        foreach (INetwork network in networks)
        {
            network.SetCategory(NetworkCategory.Private);
        }
    }
    public static void EnableAdministratorAccount(string accountName)
    {
        using (ManagementObjectSearcher searcher = new ManagementObjectSearcher(
                "SELECT * FROM Win32_UserAccount WHERE Name='" + accountName + "'"))
        {
            ManagementObjectCollection accounts = (ManagementObjectCollection)searcher.Get();
            foreach (ManagementObject account in accounts)
            {
                if (account != null)
                {
                    if ((bool)account["Disabled"])
                    {
                        //Console.WriteLine("Enabling {accountName} "+account);
                        account["Disabled"] = false;
                        account.Put();
                    }
                    else
                    {
                        //Console.WriteLine(accountName" account is already enabled");
                    }
                }
            }
        }
    }
    public static void WaitProcess(string name)
    {
        int instanceCount;
        do
        {
            System.Threading.Thread.Sleep(2000);
            instanceCount = System.Diagnostics.Process.GetProcessesByName(name).Length;
        }
        while (instanceCount > 0);
    }
    static void AutoStart(Dictionary<string, string> item)
    {

    }
}

internal class SingleInstance : IDisposable
{
    private string path;
    private FileStream file;
    public SingleInstance(string path)
    {
        this.path = path;
        try
        {
            this.file = File.Open(path, FileMode.CreateNew);
        }
        catch (IOException ex)
        {
            throw new Exception("Only single instance allowed", ex);
        }
    }

    public void Dispose()
    {
        file.Dispose();
        File.Delete(this.path);
        path = null;
    }
}



public enum NetworkCategory { Public, Private, Authenticated }
[Flags]
public enum NetworkConnectivityLevels { Connected = 1, Disconnected = 2, All = 3 }
[ComImport]
[Guid("DCB00000-570F-4A9B-8D69-199FDBA5723B")]
[TypeLibType((short)0x1040)]
internal interface INetworkListManager
{
    [return: MarshalAs(UnmanagedType.Interface)]
    [MethodImpl(MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Runtime)]
    IEnumerable GetNetworks([In] NetworkConnectivityLevels Flags);
}
[ComImport, ClassInterface((short)0), Guid("DCB00C01-570F-4A9B-8D69-199FDBA5723B")]
internal class NetworkListManagerClass { }
[ComImport]
[TypeLibType((short)0x1040)]
[InterfaceType(ComInterfaceType.InterfaceIsIDispatch)]
[Guid("DCB00002-570F-4A9B-8D69-199FDBA5723B")]
internal interface INetwork
{
    [MethodImpl(MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Runtime)]
    void SetCategory([In] NetworkCategory NewCategory);
}

namespace Shell32
{
    [Guid("FAC32C80-CBE4-11CE-8350-444553540000")]
    [InterfaceType(ComInterfaceType.InterfaceIsIDispatch)]
    public interface FolderItem { }

    [ComImport]
    [Guid("744129E0-CBE5-11CE-8350-444553540000")]
    [TypeLibType(4160)]
    [InterfaceType(ComInterfaceType.InterfaceIsDual)]
    public interface FolderItems : IEnumerable
    {
        [DispId(1610743808)]
        int Count
        {
            [MethodImpl(MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Runtime)]
            [DispId(1610743808)]
            get;
        }

        [DispId(1610743809)]
        object Application
        {
            [MethodImpl(MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Runtime)]
            [DispId(1610743809)]
            [return: MarshalAs(UnmanagedType.IDispatch)]
            get;
        }

        [DispId(1610743810)]
        object Parent
        {
            [MethodImpl(MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Runtime)]
            [DispId(1610743810)]
            [return: MarshalAs(UnmanagedType.IDispatch)]
            get;
        }

        [MethodImpl(MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Runtime)]
        [DispId(1610743811)]
        [return: MarshalAs(UnmanagedType.Interface)]
        FolderItem Item([Optional][In][MarshalAs(UnmanagedType.Struct)] object index);

        [MethodImpl(MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Runtime)]
        [DispId(-4)]
        [return: MarshalAs(UnmanagedType.CustomMarshaler, MarshalType = "System.Runtime.InteropServices.CustomMarshalers.EnumeratorToEnumVariantMarshaler, CustomMarshalers")]
        new IEnumerator GetEnumerator();
    }


    [ComImport, Guid("BBCBDE60-C3FF-11CE-8350-444553540000")]
    [InterfaceType(ComInterfaceType.InterfaceIsIDispatch)]
    public interface Folder
    {
        [return: MarshalAs(UnmanagedType.Interface)]
        [MethodImpl(MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Runtime), DispId(0x60020004)]
        FolderItems Items();
        [MethodImpl(MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Runtime), DispId(0x60020008)]
        void CopyHere([In, MarshalAs(UnmanagedType.Struct)] object vItem, [In, Optional, MarshalAs(UnmanagedType.Struct)] object vOptions);
    }


    [ComImport, Guid("D8F015C0-C278-11CE-A49E-444553540000"), TypeLibType((short)0x1050)]
    [InterfaceType(ComInterfaceType.InterfaceIsIDispatch)]
    public interface IShellDispatch
    {
        [return: MarshalAs(UnmanagedType.Interface)]
        [MethodImpl(MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Runtime), DispId(0x60020002)]
        Folder NameSpace([In, MarshalAs(UnmanagedType.Struct)] object vDir);
    }
}







/* using System;
using System.IO;
using System.Management;
using System.Reflection;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Web.Script.Serialization; // keep FullName, otherwise - undefined reference
using Dict = System.Collections.Generic.Dictionary<string, object>;
using Microsoft.Win32;


internal class JSONComparer : IComparer<object>
{
    public int Compare(object a, object b)
    {
        Int32 _a = (Int32)((Dict)a)["index"];
        Int32 _b = (Int32)((Dict)b)["index"];
        return (_a.CompareTo(_b));
    }
}

internal interface IAction
{
    void Invoke();
}
public partial class ActionBase : IAction
{
    // Assuming ExpandString is a method that expands environment variables or similar
    public string ExpandString(string input)
    {
        return Environment.ExpandEnvironmentVariables(input);
    }
}


internal class FileAction : IAction
{
    private IDictionary<string, object> action;
    private string path;
    private bool parents;
    private string value;
    public enum State
    {
        Directory,
        Touch,
        Present,
        Absent
    }
    private State state;
    public FileAction(IDictionary<string, object> action)
    {
        this.action = action;
        this.path = (string)action["path"];
        this.parents = (bool)action["parents"];
        this.state = (State)Enum.Parse(typeof(State), (string)action["state"], true);
        this.value = (string)action["value"];

    }
    void IAction.Invoke()
    {
        switch (state)
        {
            case State.Directory:
                Directory.CreateDirectory(this.path);
                break;

            case State.Touch:
                File.Create(path);
                break;

            case State.Present:
                File.WriteAllText(path, this.value);
                break;

            case State.Absent:
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
                else if (Directory.Exists(path))
                {
                    Directory.Delete(path, true);
                }
                else
                {
                    throw new IOException("Path does not exist");
                }
                break;
        }
    }
}

public class RegistryAction : ActionBase
{
    public enum RegistryState
    {
        Present,
        Absent,
        Property
    }
    private string path;
    private object value;
    private bool force;
    private RegistryValueKind itemType;
    private RegistryState state;
    private bool? recurse;

    public RegistryAction(Dictionary<string, object> item)
    {
        if (item == null || !item.ContainsKey("state") || string.IsNullOrEmpty(item["state"].ToString()))
        {
            throw new ArgumentException("The 'state' property is required.");
        }

        path = ExpandString(item["path"]);
        value = item["value"];
        force = item.ContainsKey("force") && Convert.ToBoolean(item["force"]);
        itemType = item.ContainsKey("type") ? ParseRegistryItemType(item["type"].ToString()) : RegistryValueKind.String;
        state = ParseRegistryState(item["state"].ToString());
        recurse = item.ContainsKey("recurse") ? (bool?)Convert.ToBoolean(item["recurse"]) : null;
    }
    private static RegistryState ParseRegistryState(string state)
    {
        return Enum.TryParse<RegistryState>(state, true, out var parsedState)
            ? parsedState
            : throw new ArgumentException("Invalid registry state: " + state);
    }

    private static RegistryValueKind ParseRegistryItemType(string type)
    {
        return Enum.TryParse<RegistryValueKind>(type, true, out var parsedType)
            ? parsedType
            : throw new ArgumentException("Invalid registry item type: " + type);
    }

    public void Execute()
    {
        switch (state)
        {
            case RegistryState.Present:
                CreateOrUpdateRegistryKey();
                break;
            case RegistryState.Property:
                CreateOrUpdateRegistryValue();
                break;
            case RegistryState.Absent:
                DeleteRegistryKeyOrValue();
                break;
            default:
                throw new InvalidOperationException("Unsupported registry state: " + state);
        }
    }

    private void CreateOrUpdateRegistryKey()
    {
        using (var key = force ? RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Default).CreateSubKey(path, true)
                    : RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Default).OpenSubKey(path, true))
        {
            if (key == null)
            {
                throw new InvalidOperationException("Failed to create or open registry key: " + path);
            }
            // If value is provided, set it as the default value for the key
            if (value != null)
            {
                key.SetValue(null, value, itemType);
            }
        }
    }

    private void CreateOrUpdateRegistryValue()
    {
        var subKeyPath = GetSubKeyPath(path);
        var valueName = GetValueName(path);

        using (var key = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Default).OpenSubKey(subKeyPath, true))
        {
            if (key == null)
            {
                throw new InvalidOperationException("Failed to open registry key: " + subKeyPath);
            }
            key.SetValue(valueName, value, itemType);
        }
    }

    private void DeleteRegistryKeyOrValue()
    {
        var subKeyPath = GetSubKeyPath(path);
        var valueName = GetValueName(path);

        using (var key = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Default).OpenSubKey(subKeyPath, true))
        {
            if (key == null)
            {
                throw new InvalidOperationException("Failed to open registry key: " + subKeyPath);
            }
            if (string.IsNullOrEmpty(valueName))
            {
                if (recurse.HasValue && recurse.Value)
                {
                    key.DeleteSubKeyTree(valueName, false);
                }
                else
                {
                    key.DeleteSubKey(valueName, false);
                }
            }
            else
            {
                key.DeleteValue(valueName, false);
            }
        }
    }

    private static string GetSubKeyPath(string fullPath)
    {
        var lastIndex = fullPath.LastIndexOf('\\');
        return lastIndex > 0 ? fullPath.Substring(0, lastIndex) : fullPath;
    }

    private static string GetValueName(string fullPath)
    {
        var lastIndex = fullPath.LastIndexOf('\\');
        return lastIndex > 0 ? fullPath.Substring(lastIndex + 1) : string.Empty;
    }

    void IAction.Invoke()
    {
        Execute();
    }
}


internal class UnzipAction : IAction
{
    private string zipPath;
    private string extractPath;

    public UnzipAction(IDictionary<string, object> action)
    {
        this.zipPath = (string)action["path"];
        this.extractPath = (string)action["destination"];
    }

    public void Invoke()
    {
        // Validate paths exist before creating Folders
        if (!File.Exists(zipPath))
        {
            throw new FileNotFoundException("Zip file not found");
        }

        if (!Directory.Exists(extractPath))
        {
            throw new DirectoryNotFoundException("Extract directory not found");
        }
        Type type = Type.GetTypeFromProgID("Shell.Application");
        Shell32.IShellDispatch shell = (Shell32.IShellDispatch)Activator.CreateInstance(type);

        Shell32.Folder source = shell.NameSpace(zipPath);
        Shell32.Folder destination = shell.NameSpace(extractPath);
        foreach (object item in source.Items())
        {
            destination.CopyHere(item, 8 | 16 | 512 | 1024);
        }

    }
}

public class ExeAction : IAction
{
    protected string packagePath;
    protected string arguments;

    public ExeAction(IDictionary<string, object> action)
    {
        this.packagePath = (string)action["path"];
        this.arguments = (string)action["args"];
    }

    public void Invoke()
    {
        Console.WriteLine("Installing: " + this.packagePath);
        System.Diagnostics.ProcessStartInfo startInfo = new System.Diagnostics.ProcessStartInfo();
        startInfo.FileName = this.packagePath;
        startInfo.Arguments = this.arguments;
        startInfo.WindowStyle = System.Diagnostics.ProcessWindowStyle.Hidden;
        System.Diagnostics.Process.Start(startInfo).WaitForExit();
    }
}

public class MsuAction : ExeAction
{
    public MsuAction(IDictionary<string, object> action) : base(action)
    {
        var package = (string)action["path"];
        this.packagePath = "wusa.exe";
        this.arguments = package + " " + (string)action["args"];
    }
}

public class MsiAction : IAction
{
    [DllImport("msi.dll", CharSet = CharSet.Auto, SetLastError = true)]
    static extern UInt32 MsiInstallProduct([MarshalAs(UnmanagedType.LPTStr)] string packagePath,
        [MarshalAs(UnmanagedType.LPTStr)] string commandLine);

    private string packagePath;
    private string arguments;

    public MsiAction(IDictionary<string, object> action)
    {
        this.packagePath = (string)action["path"];
        this.arguments = (string)action["args"];
    }

    public void Invoke()
    {
        Console.WriteLine("Installing: " + this.packagePath);

        System.Text.StringBuilder sb = new System.Text.StringBuilder(arguments + " ACTION=ADMIN");
        //args format Property=Setting Property=Setting.
        uint result = MsiInstallProduct(packagePath, sb.ToString());
        if (result != 0)
        {
            throw new Exception("MsiInstallProduct failed. Error: " + result);
        }
    }
}

class DismAction : IAction
{
    private const string DismAssembly = "DismApi.dll";
    private const string DISM_ONLINE_IMAGE = "DISM_{53BFAE52-B167-4E2F-A258-0A37B57FF845}";// Placeholder value, you need to use the actual constant from the DISM API
    private string packagePath;
    private bool ignoreCheck;
    private bool preventPending;

    [DllImport(DismAssembly, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Error)]
    public static extern int DismOpenSession(string imagePath, string windowsDirectory, string systemDrive, out IntPtr session);

    [DllImport(DismAssembly, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Error)]
    public static extern int DismAddPackage(IntPtr session, string packagePath, bool ignoreCheck, bool preventPending, IntPtr progress, IntPtr userData, IntPtr callback);

    [DllImport(DismAssembly, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Error)]
    public static extern int DismCloseSession(IntPtr session);

    public DismAction(IDictionary<string, object> action)
    {
        if (action.ContainsKey("path"))
        {
            this.packagePath = (string)action["path"];
        }
        else
        {
            throw new ArgumentException("The action dictionary must contain a 'path' key.");
        }
        this.ignoreCheck = action.TryGetValue("ignoreCheck", out object ignoreCheckObj) && ignoreCheckObj is bool
            ? (bool)ignoreCheckObj
            : false;

        // Get 'preventPending' from the dictionary or default to false if not provided
        this.preventPending = action.TryGetValue("preventPending", out object preventPendingObj) && preventPendingObj is bool
            ? (bool)preventPendingObj
            : false;
    }

    public void Invoke()
    {
        IntPtr session;
        int result = DismOpenSession(DISM_ONLINE_IMAGE, null, null, out session);
        if (result != 0)
        {
            throw new Exception("Failed to open DISM session for the online image.");
        }

        try
        {
            result = DismAddPackage(session, packagePath, false, false, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
            if (result != 0)
            {
                throw new Exception("Failed to add package to the online image.");
            }
        }
        finally
        {
            result = DismCloseSession(session);
            if (result != 0)
            {
                throw new Exception("Failed to close DISM session for the online image.");
            }
        }
    }
}

public class CopyAction : ActionBase
{
    private string source;
    private string destination;
    private bool force;

    public CopyAction(IDictionary<string, object> action)
    {
        if (action == null)
        {
            throw new ArgumentNullException(nameof(action));
        }

        if (!action.ContainsKey("src") || !action.ContainsKey("dest"))
        {
            throw new ArgumentException("The action dictionary must contain 'src' and 'dest' keys.");
        }

        this.source = ExpandString((string)action["src"]);
        this.destination = ExpandString((string)action["dest"]);

        // 'force' parameter is optional; default to false if not provided
        this.force = action.TryGetValue("force", out object forceObj) && forceObj is bool && (bool)forceObj;
    }

    public void Invoke()
    {
        if (string.Equals(source, destination, StringComparison.OrdinalIgnoreCase))
        {
            Console.WriteLine("Source and destination are the same. No action taken.");
            return;
        }

        Console.WriteLine("Copying: " + source + " to " + destination);
        try
        {
            // Use the 'force' parameter to decide whether to overwrite existing files
            File.Copy(source, destination, force);
        }
        catch (Exception ex)
        {
            // Handle any exceptions that may occur during the copy
            Console.WriteLine("An error occurred while copying the file: " + ex.Message);
        }
    }
}

public class CmdAction : ActionBase
{
    private string command;

    public CmdAction(IDictionary<string, object> action)
    {
        if (action == null)
        {
            throw new ArgumentNullException(nameof(action));
        }

        if (!action.ContainsKey("cmd"))
        {
            throw new ArgumentException("The action dictionary must contain a 'cmd' key.");
        }

        this.command = ExpandString((string)action["cmd"]);
    }

    public void Invoke()
    {
        Console.WriteLine("Running command: " + command);
        try
        {
            System.Diagnostics.ProcessStartInfo startInfo = new System.Diagnostics.ProcessStartInfo();
            startInfo.FileName = "cmd.exe";
            startInfo.Arguments = "/C " + command;
            startInfo.WindowStyle = System.Diagnostics.ProcessWindowStyle.Hidden;
            System.Diagnostics.Process process = System.Diagnostics.Process.Start(startInfo);
            process.WaitForExit();
        }
        catch (Exception ex)
        {
            // Handle any exceptions that may occur during the command execution
            Console.WriteLine("An error occurred while running the command: " + ex.Message);
        }
    }
}

public class PathAction : ActionBase
{
    private string pathToModify;
    private PathState state;

    // Define an enum for the state
    public enum PathState
    {
        Present,
        Absent
    }

    public PathAction(IDictionary<string, object> action)
    {
        if (action == null || !action.ContainsKey("path"))
        {
            throw new ArgumentException("The action dictionary must contain a 'path' key.");
        }

        this.pathToModify = ExpandString((string)action["path"]);
        // Set "Present" as the default state if "state" key is not provided
        if (action.ContainsKey("state"))
        {
            state = Enum.TryParse<PathState>((string)action["state"], true, out var result) ? result : PathState.Present;
        }
        else
        {
            state = PathState.Present;
        }
    }

    public void Invoke()
    {
        string currentPath = Environment.GetEnvironmentVariable("PATH", EnvironmentVariableTarget.Machine);
        string[] paths = currentPath.Split(';');
        var pathList = new List<string>(paths);

        switch (state)
        {
            case PathState.Present:
                if (!pathList.Contains(pathToModify))
                {
                    pathList.Add(pathToModify);
                    string newPath = string.Join(";", pathList);
                    Environment.SetEnvironmentVariable("PATH", newPath, EnvironmentVariableTarget.Machine);
                }
                break;
            case PathState.Absent:
                if (pathList.Contains(pathToModify))
                {
                    pathList.Remove(pathToModify);
                    string newPath = string.Join(";", pathList);
                    Environment.SetEnvironmentVariable("PATH", newPath, EnvironmentVariableTarget.Machine);
                }
                break;
            default:
                throw new ArgumentException("Invalid state value. Only 'Present' or 'Absent' are supported.");
        }
    }
}


internal class CustomDispatchConverter : JavaScriptConverter
{
    public override object Deserialize(IDictionary<string, object> dictionary, Type type, JavaScriptSerializer serializer)
    {
        IAction action = null;
        if (dictionary.ContainsKey("file"))
        {
            action = new FileAction((Dictionary<string, object>)dictionary["file"]);
        }
        else if (dictionary.ContainsKey("zip"))
        {
            action = new UnzipAction((Dictionary<string, object>)dictionary["zip"]);
        }
        else if (dictionary.ContainsKey("msi"))
        {
            action = new MsiAction((Dictionary<string, object>)dictionary["msi"]);
        }
        else if (dictionary.ContainsKey("exe"))
        {
            action = new ExeAction((Dictionary<string, object>)dictionary["exe"]);
        }
        else if (dictionary.ContainsKey("msu"))
        {
            action = new ExeAction((Dictionary<string, object>)dictionary["msu"]);
        }
        else if (dictionary.ContainsKey("cab"))
        {
            action = new DismAction((Dictionary<string, object>)dictionary["cab"]);
        }
        else if (dictionary.ContainsKey("copy"))
        {
            action = new CopyAction((Dictionary<string, object>)dictionary["copy"]);
        }
        else if (dictionary.ContainsKey("cmd"))
        {
            action = new DismAction((Dictionary<string, object>)dictionary["cmd"]);
        }
        else if (dictionary.ContainsKey("registry"))
        {
            action = new DismAction((Dictionary<string, object>)dictionary["registry"]);
        }
        else if (dictionary.ContainsKey("path"))
        {
            action = new PathAction((Dictionary<string, object>)dictionary["path"]);
        }
        else
        {
            throw new Exception("Unknown ansible action type");
        }
        return action;
    }

    public override IDictionary<string, object> Serialize(object obj, JavaScriptSerializer serializer)
    {
        Dictionary<string, object> dict = new Dictionary<string, object>();
        return dict;
    }
    public override IEnumerable<Type> SupportedTypes
    {
        get { return new[] { typeof(Dictionary<string, object>) }; }
    }
}





public class WinImageBuilderAutomation
{
    public static void Main()
    {
        SetNetworksLocationToPrivate();

        var u = new UnzipAction(new Dict { { "path", "C:\\1.zip" },
            { "destination", "c:\\folder" } });
        u.Invoke();

    }
    public static void SetNetworksLocationToPrivate()
    {
        INetworkListManager nlm = (INetworkListManager)new NetworkListManagerClass();
        IEnumerable networks = nlm.GetNetworks(NetworkConnectivityLevels.All);
        foreach (INetwork network in networks)
        {
            network.SetCategory(NetworkCategory.Private);
        }
    }
    public static void EnableAdministratorAccount(string accountName)
    {
        using (ManagementObjectSearcher searcher = new ManagementObjectSearcher(
                "SELECT * FROM Win32_UserAccount WHERE Name='" + accountName + "'"))
        {
            ManagementObjectCollection accounts = (ManagementObjectCollection)searcher.Get();
            foreach (ManagementObject account in accounts)
            {
                if (account != null)
                {
                    if ((bool)account["Disabled"])
                    {
                        //Console.WriteLine("Enabling {accountName} "+account);
                        account["Disabled"] = false;
                        account.Put();
                    }
                    else
                    {
                        //Console.WriteLine(accountName" account is already enabled");
                    }
                }
            }
        }
    }
    public static void WaitProcess(string name)
    {
        int instanceCount;
        do
        {
            System.Threading.Thread.Sleep(2000);
            instanceCount = System.Diagnostics.Process.GetProcessesByName(name).Length;
        }
        while (instanceCount > 0);
    }
    static void AutoStart(Dictionary<string, string> item)
    {
        string entry = item["name"];
        string interpreter = item["interpreter"];
        string args = item["args"];
        string dest = ExpandString(item["destination"]);
        string value = "cmd /C "
            + interpreter + " \"" + dest + "\\" + entry + "\" " + args;

        Microsoft.Win32.Registry.SetValue(
          @"HKEY_LOCAL_MACHINE\Software\Microsoft\Windows\CurrentVersion\Run",
          entry,
          value,
          Microsoft.Win32.RegistryValueKind.String);

    }
}

internal class SingleInstance : IDisposable
{
    private string path;
    private FileStream file;
    public SingleInstance(string path)
    {
        this.path = path;
        try
        {
            this.file = File.Open(path, FileMode.CreateNew);
        }
        catch (IOException ex)
        {
            throw new Exception("Only single instance allowed", ex);
        }
    }

    public void Dispose()
    {
        file.Dispose();
        File.Delete(this.path);
        path = null;
    }
}



public enum NetworkCategory { Public, Private, Authenticated }
[Flags]
public enum NetworkConnectivityLevels { Connected = 1, Disconnected = 2, All = 3 }
[ComImport]
[Guid("DCB00000-570F-4A9B-8D69-199FDBA5723B")]
[TypeLibType((short)0x1040)]
internal interface INetworkListManager
{
    [return: MarshalAs(UnmanagedType.Interface)]
    [MethodImpl(MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Runtime)]
    IEnumerable GetNetworks([In] NetworkConnectivityLevels Flags);
}
[ComImport, ClassInterface((short)0), Guid("DCB00C01-570F-4A9B-8D69-199FDBA5723B")]
internal class NetworkListManagerClass { }
[ComImport]
[TypeLibType((short)0x1040)]
[InterfaceType(ComInterfaceType.InterfaceIsIDispatch)]
[Guid("DCB00002-570F-4A9B-8D69-199FDBA5723B")]
internal interface INetwork
{
    [MethodImpl(MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Runtime)]
    void SetCategory([In] NetworkCategory NewCategory);
}

namespace Shell32
{
    [Guid("FAC32C80-CBE4-11CE-8350-444553540000")]
    [InterfaceType(ComInterfaceType.InterfaceIsIDispatch)]
    public interface FolderItem { }

    [ComImport]
    [Guid("744129E0-CBE5-11CE-8350-444553540000")]
    [TypeLibType(4160)]
    [InterfaceType(ComInterfaceType.InterfaceIsDual)]
    public interface FolderItems : IEnumerable
    {
        [DispId(1610743808)]
        int Count
        {
            [MethodImpl(MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Runtime)]
            [DispId(1610743808)]
            get;
        }

        [DispId(1610743809)]
        object Application
        {
            [MethodImpl(MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Runtime)]
            [DispId(1610743809)]
            [return: MarshalAs(UnmanagedType.IDispatch)]
            get;
        }

        [DispId(1610743810)]
        object Parent
        {
            [MethodImpl(MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Runtime)]
            [DispId(1610743810)]
            [return: MarshalAs(UnmanagedType.IDispatch)]
            get;
        }

        [MethodImpl(MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Runtime)]
        [DispId(1610743811)]
        [return: MarshalAs(UnmanagedType.Interface)]
        FolderItem Item([Optional][In][MarshalAs(UnmanagedType.Struct)] object index);

        [MethodImpl(MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Runtime)]
        [DispId(-4)]
        [return: MarshalAs(UnmanagedType.CustomMarshaler, MarshalType = "System.Runtime.InteropServices.CustomMarshalers.EnumeratorToEnumVariantMarshaler, CustomMarshalers")]
        new IEnumerator GetEnumerator();
    }


    [ComImport, Guid("BBCBDE60-C3FF-11CE-8350-444553540000")]
    [InterfaceType(ComInterfaceType.InterfaceIsIDispatch)]
    public interface Folder
    {
        [return: MarshalAs(UnmanagedType.Interface)]
        [MethodImpl(MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Runtime), DispId(0x60020004)]
        FolderItems Items();
        [MethodImpl(MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Runtime), DispId(0x60020008)]
        void CopyHere([In, MarshalAs(UnmanagedType.Struct)] object vItem, [In, Optional, MarshalAs(UnmanagedType.Struct)] object vOptions);
    }


    [ComImport, Guid("D8F015C0-C278-11CE-A49E-444553540000"), TypeLibType((short)0x1050)]
    [InterfaceType(ComInterfaceType.InterfaceIsIDispatch)]
    public interface IShellDispatch
    {
        [return: MarshalAs(UnmanagedType.Interface)]
        [MethodImpl(MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Runtime), DispId(0x60020002)]
        Folder NameSpace([In, MarshalAs(UnmanagedType.Struct)] object vDir);
    }
}

 */
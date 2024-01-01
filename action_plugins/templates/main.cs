using System;
using System.IO;
using System.Diagnostics;
using System.Management;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Web.Script.Serialization; // keep FullName, otherwise - undefined reference
using Microsoft.Win32;


public class WinImageBuilderAutomation
{
    public static void Main()
    {
        var configDrivePath = "C:\\";//"{{config_drive}}";
        var mainPs1Autostart = new Dictionary<string, object>
        {
            { "file", "start.ps1" },
            { "interpreter", "powershell.exe -NoExit -ExecutionPolicy Bypass -File" },
            { "destination", configDrivePath },
            {"args", "" }
        };

        AutostartAction autostartAction = new AutostartAction(mainPs1Autostart);
        autostartAction.Invoke();

        string packageJsonPath = Path.Combine(configDrivePath, "package.json");

        string packageJsonContent = File.ReadAllText(packageJsonPath);
        JavaScriptSerializer serializer = new JavaScriptSerializer();

        var converters = new List<JavaScriptConverter> { new CustomDispatchConverter() };
        serializer.RegisterConverters(converters);

        var actions = serializer.Deserialize<List<ActionBase>>(packageJsonContent);

        actions.Sort(new ActionComparer()); // sort by Index property (priority)
        IDictionary<int, IAction> indexTracker = new Dictionary<int, IAction>(); //chech for duplicate indexes
        IAction existingAction;
        foreach (IAction action in actions)
        {
            if (indexTracker.TryGetValue(action.Index, out existingAction))
            {
                throw new InvalidOperationException("Duplicate index found: " + action.Index.ToString() + " for actions '" + existingAction.ToString() + "' and '" + action.ToString() + "'.");
            }
            else
            {
                indexTracker.Add(action.Index, action);
                action.Invoke();
            }
        }



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
}

internal class ActionComparer : IComparer<ActionBase>
{
    public int Compare(ActionBase a, ActionBase b)
    {
        int indexA = a.Index;
        int indexB = b.Index;
        return indexA.CompareTo(indexB);
    }
}



internal class CustomDispatchConverter : JavaScriptConverter
{
    public override object Deserialize(IDictionary<string, object> dictionary, Type type, JavaScriptSerializer serializer)
    {

        object indexValue;

        if (!dictionary.TryGetValue("index", out indexValue))
        {
            throw new ArgumentException("The 'index' field is missing.");
        }

        if (!(indexValue is int))
        {
            throw new ArgumentException("The 'index' field is invalid.");
        }

        IAction action = CreateActionFromDictionary(dictionary);

        action.Index = (int)indexValue;
        return action;
    }
    private IAction CreateActionFromDictionary(IDictionary<string, object> dictionary)
    {
        object actionData;

        if (dictionary.TryGetValue("file", out actionData))
        {
            return new FileAction((Dictionary<string, object>)actionData);
        }
        if (dictionary.TryGetValue("zip", out actionData))
        {
            return new UnzipAction((Dictionary<string, object>)actionData);
        }
        if (dictionary.TryGetValue("msi", out actionData))
        {
            return new MsiAction((Dictionary<string, object>)actionData);
        }
        if (dictionary.TryGetValue("exe", out actionData))
        {
            return new ExeAction((Dictionary<string, object>)actionData);
        }
        if (dictionary.TryGetValue("msu", out actionData))
        {
            return new MsuAction((Dictionary<string, object>)actionData);
        }
        if (dictionary.TryGetValue("cab", out actionData))
        {
            return new DismAction((Dictionary<string, object>)actionData);
        }
        if (dictionary.TryGetValue("copy", out actionData))
        {
            return new CopyAction((Dictionary<string, object>)actionData);
        }
        if (dictionary.TryGetValue("cmd", out actionData))
        {
            return new CmdAction((Dictionary<string, object>)actionData);
        }
        if (dictionary.TryGetValue("registry", out actionData))
        {
            return new RegistryAction((Dictionary<string, object>)actionData);
        }
        if (dictionary.TryGetValue("path", out actionData))
        {
            return new PathAction((Dictionary<string, object>)actionData);
        }
        if (dictionary.TryGetValue("autostart", out actionData))
        {
            return new AutostartAction((Dictionary<string, object>)actionData);
        }

        throw new Exception("Unknown action type");
    }


    public override IDictionary<string, object> Serialize(object obj, JavaScriptSerializer serializer)
    {
        throw new NotImplementedException();
    }
    public override IEnumerable<Type> SupportedTypes
    {
        get { return new List<Type>(new List<Type>(new[] { typeof(object) })); }
    }

}




internal interface IAction
{
    int Index { get; set; }
    void Invoke();
}
internal abstract class ActionBase : IAction
{
    public ActionBase() { }
    public int Index { get; set; }

    // Assuming ExpandString is a method that expands environment variables or similar
    public static string ExpandString(string input)
    {
        return Environment.ExpandEnvironmentVariables(input);
    }

    public abstract void Invoke();

}


internal class FileAction : ActionBase
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
        try
        {
            path = (string)action["path"];
            state = (State)Enum.Parse(typeof(State), (string)action["state"], true);
        }
        catch (Exception ex)
        {
            throw new ArgumentException("Invalid argument or missing key", ex);
        }
    }
    public override void Invoke()
    {
        switch (state)
        {
            case State.Directory:
                Directory.CreateDirectory(path);
                break;

            case State.Touch:
                File.Create(path);
                break;

            case State.Present:
                File.WriteAllText(path, value);
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

internal class RegistryAction : ActionBase
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

        path = ExpandString((string)item["path"]);
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

    public override void Invoke()
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
}


internal class UnzipAction : ActionBase
{
    private string zipPath;
    private string extractPath;

    public UnzipAction(IDictionary<string, object> action)
    {
        try
        {
            zipPath = (string)action["path"];
            extractPath = (string)action["dest"];
        }
        catch (Exception ex)
        {
            throw new ArgumentException("Invalid argument or missing key", ex);
        }
    }


    public override void Invoke()
    {
        if (!File.Exists(zipPath))
        {
            throw new FileNotFoundException("Zip file not found");
        }

        if (!Directory.Exists(extractPath))
        {
            throw new DirectoryNotFoundException("Destination directory not found");
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

internal class ExeAction : ActionBase
{
    protected string packagePath;
    protected string arguments;

    public ExeAction(IDictionary<string, object> action)
    {
        try
        {
            packagePath = (string)action["path"];
            arguments = (string)action["args"];
        }
        catch (Exception ex)
        {
            throw new ArgumentException("Invalid argument or missing key", ex);
        }
    }


    public override void Invoke()
    {
        Console.WriteLine("Installing: " + packagePath);
        ProcessStartInfo startInfo = new ProcessStartInfo();
        startInfo.FileName = packagePath;
        startInfo.Arguments = arguments;
        startInfo.WindowStyle = ProcessWindowStyle.Hidden;
        Process.Start(startInfo).WaitForExit();
    }
}

internal class MsuAction : ActionBase
{
    private string packagePath;
    private string arguments;
    public MsuAction(IDictionary<string, object> action)
    {
        packagePath = "wusa.exe";
        try
        {
            string package = (string)action["path"];
            arguments = package + " " + (string)action["args"];
        }
        catch (Exception ex)
        {
            throw new ArgumentException("Invalid argument or missing key", ex);
        }
    }

    public override void Invoke()
    {
        Console.WriteLine("Installing: " + arguments);
        ProcessStartInfo startInfo = new ProcessStartInfo();
        startInfo.FileName = packagePath;
        startInfo.Arguments = arguments;
        startInfo.WindowStyle = ProcessWindowStyle.Hidden;
        Process.Start(startInfo).WaitForExit();
        //aditional waiting on wusa.exe
        WaitProcess(packagePath);
    }
    private void WaitProcess(string name)
    {
        int instanceCount;
        do
        {
            System.Threading.Thread.Sleep(2000);
            instanceCount = Process.GetProcessesByName(name).Length;
        }
        while (instanceCount > 0);
    }
}

internal class MsiAction : ActionBase
{
    [DllImport("msi.dll", CharSet = CharSet.Auto, SetLastError = true)]
    static extern UInt32 MsiInstallProduct([MarshalAs(UnmanagedType.LPTStr)] string packagePath,
        [MarshalAs(UnmanagedType.LPTStr)] string commandLine);

    private string packagePath;
    private string arguments;

    public MsiAction(IDictionary<string, object> action)
    {
        try
        {
            packagePath = (string)action["path"];
            arguments = (string)action["args"];
        }
        catch (Exception ex)
        {
            throw new ArgumentException("Invalid argument or missing key", ex);
        }
    }

    public override void Invoke()
    {
        Console.WriteLine("Installing: " + packagePath);

        System.Text.StringBuilder sb = new System.Text.StringBuilder(arguments + " ACTION=ADMIN");
        //args format Property=Setting Property=Setting.
        uint result = MsiInstallProduct(packagePath, sb.ToString());
        if (result != 0)
        {
            throw new Exception("MsiInstallProduct failed. Error: " + result);
        }
    }
}
class DismAction : ActionBase
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
        object pathValue;
        if (action.TryGetValue("path", out pathValue))
        {
            packagePath = (string)pathValue;
        }
        else
        {
            throw new ArgumentException("The action dictionary must contain a 'path' key.");
        }

        ignoreCheck = false;
        preventPending = false;

        object ignoreCheckValue;
        if (action.TryGetValue("ignoreCheck", out ignoreCheckValue))
        {
            ignoreCheck = (bool)ignoreCheckValue;
        }

        object preventPendingValue;
        if (action.TryGetValue("preventPending", out preventPendingValue))
        {
            preventPending = (bool)preventPendingValue;
        }
    }

    public override void Invoke()
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

internal class CopyAction : ActionBase
{
    private string source;
    private string destination;
    private bool force;

    public CopyAction(IDictionary<string, object> action)
    {
        try
        {
            source = ExpandString((string)action["src"]);
            destination = ExpandString((string)action["dest"]);
        }
        catch (Exception ex)
        {
            throw new ArgumentException("Invalid argument or missing key", ex);
        }

        // 'force' parameter is optional; default to false if not provided
        force = false;
        object forceValue;
        if (action.TryGetValue("ignoreCheck", out forceValue))
        {
            force = (bool)forceValue;
        }
    }

    public override void Invoke()
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

internal class CmdAction : ActionBase
{
    private string command;

    public CmdAction(IDictionary<string, object> action)
    {
        try
        {
            command = ExpandString((string)action["cmd"]);
        }
        catch (Exception ex)
        {
            throw new ArgumentException("Invalid argument or missing key", ex);
        }
    }


    public override void Invoke()
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



internal class PathAction : ActionBase
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
        string stateStr;
        try
        {
            pathToModify = ExpandString((string)action["path"]);
            stateStr = (string)action["state"];
        }
        catch (Exception ex)
        {
            throw new ArgumentException("Invalid argument or missing key", ex);
        }

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

    public override void Invoke()
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



internal class AutostartAction : ActionBase
{
    private string entry;
    private string interpreter;
    private string args;
    private string destination;

    public AutostartAction(IDictionary<string, object> item)
    {
        try
        {
            entry = ExpandString((string)item["file"]);
            interpreter = ExpandString((string)item["interpreter"]);
            destination = ExpandString((string)item["destination"]);
            args = ExpandString((string)item["args"]);
        }
        catch (Exception ex)
        {
            throw new ArgumentException("Invalid argument or missing key", ex);
        }
    }

    public override void Invoke()
    {
        string value = string.Format("cmd /C \"{0}\" \"{1}\\{2}\" {3}", interpreter, destination, entry, args);

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
            file = File.Open(path, FileMode.CreateNew);
        }
        catch (IOException ex)
        {
            throw new Exception("Only single instance allowed", ex);
        }
    }

    public void Dispose()
    {
        file.Dispose();
        File.Delete(path);
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

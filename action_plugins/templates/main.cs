using System;
using System.IO;
using System.Management;
using System.Reflection;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Web.Script.Serialization; // keep FullName, otherwise - undefined reference
using Dict = System.Collections.Generic.Dictionary<string, object>;


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

internal class RegistryAction : IAction
{
    private IDictionary<string, object> action;
    private string path;
    private string value;

    public enum State
    {
        Present,
        Property,
        Absent
    }

    private State state;

    public RegistryAction(IDictionary<string, object> action)
    {
        this.action = action;
        this.path = (string)action["path"];
        this.value = (string)action["value"];
        this.state = (State)Enum.Parse(typeof(State), (string)action["state"], true);
    }

    public void Invoke()
    {
        switch (state)
        {
            case State.Present:
                //Registry.SetValue(path, value);
                break;

            case State.Property:
                //Registry.SetValue(path, value, RegistryValueKind.DWord);
                break;

            case State.Absent:
                //Registry.SetValue(path, "");
                break;
        }
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
    //DISM_ONLINE_IMAGE
     private const string DismAssembly = "DismApi.dll";

    [DllImport(DismAssembly, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Error)]
    public static extern int DismOpenSession(out IntPtr session);

    [DllImport(DismAssembly, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Error)]
    public static extern int DismAddPackage(IntPtr session, string packagePath, bool ignoreCheck, bool preventPending, IntPtr progress, IntPtr userData, IntPtr callback);

    [DllImport(DismAssembly, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Error)]
    public static extern int DismCloseSession(IntPtr session);

    public DismAction() {}
    public void Invoke()
    {
        throw new NotImplementedException();
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
            // handle cab 
        }
        else if (dictionary.ContainsKey("copy"))
        {
            // handle copy
        }
        else if (dictionary.ContainsKey("cmd"))
        {
            // handle cmd
        }
        else if (dictionary.ContainsKey("registry"))
        {
            // handle registry
        }
        else if (dictionary.ContainsKey("path"))
        {
            // handle path
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
                        //Console.WriteLine($"Enabling {accountName} account");
                        account["Disabled"] = false;
                        account.Put();
                    }
                    else
                    {
                        //Console.WriteLine($"{accountName} account is already enabled");
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
    static string ExpandString(string input)
    {
        return Environment.ExpandEnvironmentVariables(input);
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
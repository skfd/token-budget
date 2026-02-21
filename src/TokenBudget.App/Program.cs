using System;
using System.Runtime.InteropServices;
using System.Threading;

namespace TokenBudget.App;

internal class Program
{
    // CRITICAL: This GUID must match [Guid] on WidgetProvider class AND Package.appxmanifest
    private static readonly Guid CLSID_WidgetProvider = new("9F910C81-08A4-461F-93A6-96809C70A95D");

    [MTAThread]
    static void Main(string[] args)
    {
        // Initialize WinRT COM wrappers — required for MarshalInspectable to work
        WinRT.ComWrappersSupport.InitializeComWrappers();

        // Register COM class factory
        Guid clsid = CLSID_WidgetProvider;
        int hr = Ole32.CoRegisterClassObject(
            ref clsid,
            new WidgetProviderFactory<WidgetProvider>(),
            Ole32.CLSCTX_LOCAL_SERVER,
            Ole32.REGCLS_MULTIPLEUSE | Ole32.REGCLS_SUSPENDED,
            out int cookie);

        if (hr < 0)
            Marshal.ThrowExceptionForHR(hr);

        hr = Ole32.CoResumeClassObjects();
        if (hr < 0)
        {
            Ole32.CoRevokeClassObject(cookie);
            Marshal.ThrowExceptionForHR(hr);
        }

        // Wait indefinitely — the process stays alive to service COM calls.
        // MTA doesn't need a message pump.
        using var exitEvent = new ManualResetEvent(false);
        exitEvent.WaitOne();

        Ole32.CoRevokeClassObject(cookie);
    }
}

internal static class Ole32
{
    public const int CLSCTX_LOCAL_SERVER = 0x4;
    public const int REGCLS_MULTIPLEUSE = 0x1;
    public const int REGCLS_SUSPENDED = 0x4;

    [DllImport("ole32.dll")]
    public static extern int CoRegisterClassObject(
        ref Guid rclsid,
        [MarshalAs(UnmanagedType.Interface)] object pUnk,
        int dwClsContext,
        int flags,
        out int lpdwRegister);

    [DllImport("ole32.dll")]
    public static extern int CoRevokeClassObject(int dwRegister);

    [DllImport("ole32.dll")]
    public static extern int CoResumeClassObjects();
}

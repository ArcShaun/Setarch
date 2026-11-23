using System.Runtime.InteropServices;

namespace SetArch.App.Services;

/// <summary>
/// AutoCAD rejects COM calls while it is busy (RPC_E_CALL_REJECTED). An OLE message filter on the
/// calling STA thread makes COM retry those calls automatically instead of throwing.
/// </summary>
[ComImport, Guid("00000016-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IOleMessageFilter
{
    [PreserveSig] int HandleInComingCall(int dwCallType, IntPtr hTaskCaller, int dwTickCount, IntPtr lpInterfaceInfo);
    [PreserveSig] int RetryRejectedCall(IntPtr hTaskCallee, int dwTickCount, int dwRejectType);
    [PreserveSig] int MessagePending(IntPtr hTaskCallee, int dwTickCount, int dwPendingType);
}

internal sealed class RetryMessageFilter : IOleMessageFilter
{
    private const int ServerCallRetryLater = 2;
    private const int GiveUpAfterMs = 180_000;

    [DllImport("ole32.dll")]
    private static extern int CoRegisterMessageFilter(IOleMessageFilter? newFilter, out IOleMessageFilter? oldFilter);

    public static void Register() => CoRegisterMessageFilter(new RetryMessageFilter(), out _);
    public static void Revoke() => CoRegisterMessageFilter(null, out _);

    public int HandleInComingCall(int dwCallType, IntPtr hTaskCaller, int dwTickCount, IntPtr lpInterfaceInfo) => 0;

    public int RetryRejectedCall(IntPtr hTaskCallee, int dwTickCount, int dwRejectType) =>
        dwRejectType == ServerCallRetryLater && dwTickCount < GiveUpAfterMs ? 150 : -1;

    public int MessagePending(IntPtr hTaskCallee, int dwTickCount, int dwPendingType) => 2; // PENDINGMSG_WAITDEFPROCESS
}

internal static class ComSupport
{
    [DllImport("ole32.dll", CharSet = CharSet.Unicode)]
    private static extern int CLSIDFromProgID(string progId, out Guid clsid);

    [DllImport("oleaut32.dll", PreserveSig = false)]
    private static extern void GetActiveObject(ref Guid rclsid, IntPtr reserved, [MarshalAs(UnmanagedType.IUnknown)] out object ppunk);

    /// <summary>Equivalent of the .NET Framework Marshal.GetActiveObject, which .NET 8 no longer has.</summary>
    public static object? TryGetActiveObject(string progId)
    {
        if (CLSIDFromProgID(progId, out var clsid) != 0) return null;
        try
        {
            GetActiveObject(ref clsid, IntPtr.Zero, out var obj);
            return obj;
        }
        catch (COMException)
        {
            return null;
        }
    }

    /// <summary>Runs <paramref name="action"/> on a dedicated STA thread with the retry message filter installed.</summary>
    public static Task RunSta(Action action)
    {
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            RetryMessageFilter.Register();
            try
            {
                action();
                tcs.SetResult();
            }
            catch (Exception ex)
            {
                tcs.SetException(ex);
            }
            finally
            {
                RetryMessageFilter.Revoke();
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.IsBackground = true;
        thread.Start();
        return tcs.Task;
    }
}

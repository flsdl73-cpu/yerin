using System.Runtime.InteropServices;
using System.Security.Principal;
namespace AbyssAuto;

internal static class Native
{
    [StructLayout(LayoutKind.Sequential)] internal struct Rect { public int Left, Top, Right, Bottom; }
    [DllImport("dwmapi.dll")] internal static extern int DwmGetWindowAttribute(nint hwnd, uint attribute, out Rect rect, uint size);
    [DllImport("user32.dll")] internal static extern nint GetForegroundWindow();
    [DllImport("user32.dll")] internal static extern bool GetCursorPos(out Point point);
    [DllImport("user32.dll")] internal static extern bool RegisterHotKey(nint hwnd, int id, uint modifiers, uint key);
    [DllImport("user32.dll")] internal static extern bool UnregisterHotKey(nint hwnd, int id);
    internal delegate bool EnumWindow(nint hwnd, nint param);
    [DllImport("user32.dll")] internal static extern bool EnumWindows(EnumWindow callback, nint param);
    [DllImport("user32.dll", CharSet=CharSet.Unicode)] internal static extern int GetWindowText(nint hwnd, System.Text.StringBuilder text, int max);
    [DllImport("user32.dll")] internal static extern bool IsWindowVisible(nint hwnd);
    #if !DRIVER_FREE
    [DllImport("interception.dll", CallingConvention=CallingConvention.Cdecl)] internal static extern nint interception_create_context();
    [DllImport("interception.dll", CallingConvention=CallingConvention.Cdecl)] internal static extern void interception_destroy_context(nint context);
    [DllImport("interception.dll", CallingConvention=CallingConvention.Cdecl)] internal static extern int interception_send(nint context, int device, ref KeyStroke stroke, uint count);
    [DllImport("interception.dll", CallingConvention=CallingConvention.Cdecl)] internal static extern int interception_send(nint context, int device, ref MouseStroke stroke, uint count);
    [DllImport("interception.dll", CallingConvention=CallingConvention.Cdecl)] internal static extern uint interception_get_hardware_id(nint context, int device, nint buffer, uint size);
    [StructLayout(LayoutKind.Sequential)] internal struct KeyStroke { public ushort Code, State; public uint Information; }
    [StructLayout(LayoutKind.Sequential)] internal struct MouseStroke { public ushort State, Flags; public short Rolling; public int X, Y; public uint Information; }
    #endif
}
public sealed class GameWindow(Scenario settings)
{
    public nint Handle { get; private set; }
    public Rectangle Bounds()
    {
        var matches = new List<nint>();
        Native.EnumWindows((hwnd, _) => {
            var title = new System.Text.StringBuilder(256);
            Native.GetWindowText(hwnd, title, title.Capacity);
            if (Native.IsWindowVisible(hwnd) && title.ToString() == settings.WindowTitle) matches.Add(hwnd);
            return true;
        }, 0);
        if (matches.Count != 1) throw new InvalidOperationException("게임 창을 정확히 하나 열어주세요.");
        Handle = matches[0];
        if (Native.DwmGetWindowAttribute(Handle,9,out var r,16)!=0) throw new InvalidOperationException("제목 표시줄 포함 가시 창 경계 확인 실패");
        var bounds = Rectangle.FromLTRB(r.Left,r.Top,r.Right,r.Bottom);
        if (bounds.Width != settings.Width || bounds.Height != settings.Height)
            throw new InvalidOperationException($"창 크기 {bounds.Width}×{bounds.Height}: 검증 기준 {settings.Width}×{settings.Height}과 다릅니다. 자동 비율 변환하지 않습니다.");
        if (bounds.Left < 0 || bounds.Top < 0 || !(Screen.PrimaryScreen?.Bounds.Contains(bounds) ?? false))
            throw new InvalidOperationException("게임 창을 주 모니터 화면 안에 완전히 배치해주세요.");
        return bounds;
    }
    public void RequireActive()
    {
        Bounds();
        if (Native.GetForegroundWindow() != Handle) throw new InvalidOperationException("게임이 활성 창이 아니므로 입력을 중단합니다.");
    }
    public Bitmap Capture()
    {
        RequireActive();
        var r = Bounds();
        var bitmap = new Bitmap(r.Width, r.Height);
        try { using var g = Graphics.FromImage(bitmap); g.CopyFromScreen(r.Location, Point.Empty, r.Size); return bitmap; }
        catch { bitmap.Dispose(); throw; }
    }
}
public interface IInput : IDisposable
{
    Task Key(ushort code, CancellationToken token);
    Task Click(Rectangle detected, CancellationToken token);
}
#if !DRIVER_FREE
public sealed class InterceptionInput : IInput
{
    private readonly nint context;
    private readonly int keyboard, mouse;
    private readonly GameWindow window;
    public InterceptionInput(GameWindow window)
    {
        this.window = window;
        using var identity = WindowsIdentity.GetCurrent();
        if (!new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator))
            throw new InvalidOperationException("Interception 입력은 관리자 권한으로 실행해주세요.");
        context = Native.interception_create_context();
        if (context == 0) throw new InvalidOperationException("Interception 컨텍스트 생성 실패");
        keyboard = Enumerable.Range(1,10).FirstOrDefault(Available);
        mouse = Enumerable.Range(11,10).FirstOrDefault(Available);
        if (keyboard == 0 || mouse == 0) { Dispose(); throw new InvalidOperationException("Interception 드라이버/키보드/마우스 장치를 확인하세요. DLL만으로는 실행되지 않습니다."); }
    }
    private bool Available(int device)
    {
        var buffer=Marshal.AllocHGlobal(2048);
        try { return Native.interception_get_hardware_id(context,device,buffer,2048)>0; }
        finally { Marshal.FreeHGlobal(buffer); }
    }
    private void Send(ref Native.KeyStroke stroke) { if (Native.interception_send(context, keyboard, ref stroke,1) != 1) throw new IOException("키 입력 전달 실패"); }
    private void Send(ref Native.MouseStroke stroke) { if (Native.interception_send(context, mouse, ref stroke,1) != 1) throw new IOException("마우스 입력 전달 실패"); }
    public async Task Key(ushort code, CancellationToken token)
    {
        token.ThrowIfCancellationRequested(); window.RequireActive();
        var stroke = new Native.KeyStroke { Code = code };
        try { Send(ref stroke); await Task.Delay(Random.Shared.Next(40,111), token); }
        finally { stroke.State=1; Send(ref stroke); }
    }
    public async Task Click(Rectangle detected, CancellationToken token)
    {
        token.ThrowIfCancellationRequested(); window.RequireActive();
        var origin = window.Bounds();
        var point = new Point(origin.Left + detected.Left + detected.Width/2, origin.Top + detected.Top + detected.Height/2);
        // Absolute coordinates in the primary monitor; verify the actual cursor before clicking.
        if (!Native.GetCursorPos(out var current)) throw new IOException("커서 조회 실패");
        var screen=Screen.PrimaryScreen!.Bounds;
        var move = new Native.MouseStroke { Flags=1, X=(int)Math.Round((point.X-screen.Left)*65535.0/(screen.Width-1)), Y=(int)Math.Round((point.Y-screen.Top)*65535.0/(screen.Height-1)) };
        Send(ref move);
        await Task.Delay(80,token);
        window.RequireActive(); token.ThrowIfCancellationRequested();
        if (!Native.GetCursorPos(out current) || Math.Abs(current.X-point.X)>3 || Math.Abs(current.Y-point.Y)>3)
            throw new InvalidOperationException("커서 위치가 검출 버튼과 다릅니다. 사용자 이동 또는 마우스 가속을 확인하세요.");
        var stroke = new Native.MouseStroke { State=1 };
        try { Send(ref stroke); await Task.Delay(Random.Shared.Next(30,111),token); }
        finally { stroke.State=2; Send(ref stroke); }
    }
    public void Dispose() { if (context != 0) Native.interception_destroy_context(context); }
}

#endif

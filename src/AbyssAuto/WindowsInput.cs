using System.ComponentModel;
using System.Runtime.InteropServices;
namespace AbyssAuto;

// user32 SendInput; no driver context, installation, service or child process.
public sealed class WindowsInput(GameWindow window) : IInput
{
    [DllImport("user32.dll",SetLastError=true)]
    private static extern uint SendInput(uint count,[In]WindowsInputPacket[] packets,int size);
    private readonly HashSet<ushort> ownedKeys=[];
    private bool ownedMouse;
    private static void Send(WindowsInputPacket packet)
    {
        if(SendInput(1,[packet],Marshal.SizeOf<WindowsInputPacket>())!=1)
            throw new IOException($"Windows 기본 입력 전달 실패 (오류 {Marshal.GetLastWin32Error()}). 게임과 프로그램의 실행 권한 수준을 확인해주세요.");
    }
    public async Task Key(ushort code,CancellationToken token)
    {
        token.ThrowIfCancellationRequested();window.RequireActive();
        Send(WindowsInputPackets.Key(code,false));ownedKeys.Add(code);
        try {await Task.Delay(Random.Shared.Next(40,111),token);}
        finally {Send(WindowsInputPackets.Key(code,true));ownedKeys.Remove(code);}
    }
    public async Task Click(Rectangle detected,CancellationToken token)
    {
        token.ThrowIfCancellationRequested();window.RequireActive();
        var origin=window.Bounds();var screen=Screen.PrimaryScreen!.Bounds;
        var point=new Point(origin.Left+detected.Left+detected.Width/2,origin.Top+detected.Top+detected.Height/2);
        Send(WindowsInputPackets.Move((int)Math.Round((point.X-screen.Left)*65535.0/(screen.Width-1)),(int)Math.Round((point.Y-screen.Top)*65535.0/(screen.Height-1))));
        await Task.Delay(80,token);window.RequireActive();token.ThrowIfCancellationRequested();
        if(!Native.GetCursorPos(out var current)||Math.Abs(current.X-point.X)>3||Math.Abs(current.Y-point.Y)>3)
            throw new InvalidOperationException("커서가 검출 버튼 위치와 달라 클릭을 중단합니다.");
        Send(WindowsInputPackets.MouseButton(false));ownedMouse=true;
        try {await Task.Delay(Random.Shared.Next(30,111),token);}
        finally {Send(WindowsInputPackets.MouseButton(true));ownedMouse=false;}
    }
    public void Dispose()
    {
        Exception? failure=null;
        foreach(var key in ownedKeys.ToArray())
            try {Send(WindowsInputPackets.Key(key,true));ownedKeys.Remove(key);}catch(Exception e){failure=e;}
        if(ownedMouse)try {Send(WindowsInputPackets.MouseButton(true));ownedMouse=false;}catch(Exception e){failure=e;}
        if(failure!=null)throw new IOException("남은 입력 해제 요청 실패",failure);
    }
}
public static class InputFactory
{
#if DRIVER_FREE
    public const string Description="Windows 기본 입력 · 드라이버 불필요";
    public static IInput Create(GameWindow window)=>new WindowsInput(window);
#else
    public const string Description="Interception 입력";
    public static IInput Create(GameWindow window)=>new InterceptionInput(window);
#endif
}

using System.Runtime.InteropServices;
namespace AbyssAuto;

[StructLayout(LayoutKind.Sequential)]
public struct WindowsMouseInput {public int X,Y;public uint Data,Flags,Time;public nuint ExtraInfo;}
[StructLayout(LayoutKind.Sequential)]
public struct WindowsKeyboardInput {public ushort VirtualKey,ScanCode;public uint Flags,Time;public nuint ExtraInfo;}
[StructLayout(LayoutKind.Explicit,Size=40)]
public struct WindowsInputPacket
{
    [FieldOffset(0)]public uint Type;
    [FieldOffset(8)]public WindowsMouseInput Mouse;
    [FieldOffset(8)]public WindowsKeyboardInput Keyboard;
}
public static class WindowsInputPackets
{
    public const nuint Marker=0x4D574159;
    public static WindowsInputPacket Key(ushort scan,bool release)=>new() {Type=1,Keyboard=new() {ScanCode=scan,Flags=0x0008u|(release?0x0002u:0u),ExtraInfo=Marker}};
    public static WindowsInputPacket MouseButton(bool release)=>new() {Type=0,Mouse=new() {Flags=release?0x0004u:0x0002u,ExtraInfo=Marker}};
    public static WindowsInputPacket Move(int x,int y)=>new() {Type=0,Mouse=new() {X=x,Y=y,Flags=0x8001,ExtraInfo=Marker}};
}

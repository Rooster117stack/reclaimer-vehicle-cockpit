using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;

// Data only: the parent releases a block only after every marker-block reference
// is restored. Otherwise it leaves the allocation intact until the game exits.
sealed class RemoteCameraData : IDisposable {
    [DllImport("kernel32.dll", SetLastError=true)]
    static extern IntPtr VirtualAllocEx(IntPtr process, IntPtr address, UIntPtr size, uint allocationType, uint protect);
    [DllImport("kernel32.dll", SetLastError=true)]
    static extern bool VirtualFreeEx(IntPtr process, IntPtr address, UIntPtr size, uint freeType);
    readonly IntPtr Handle;
    readonly Dictionary<ulong,int> Blocks=new Dictionary<ulong,int>();
    bool Disposed;

    internal RemoteCameraData(IntPtr processHandle) {
        if(processHandle==IntPtr.Zero)throw new ArgumentException("A game process handle is required.");
        Handle=processHandle;
    }
    internal ulong Reserve(ulong heap,int bytes) {
        if(Disposed)throw new ObjectDisposedException("RemoteCameraData");
        if(!Native.Ptr(heap)||bytes<=0||bytes>4*1024*1024)throw new ArgumentOutOfRangeException("bytes");
        int size=checked((bytes+4095)&~4095);
        const ulong step=64UL*1024*1024, compactLimit=(ulong)uint.MaxValue*4;
        ulong last=Math.Min(checked(heap+compactLimit),0x7fffffffffffUL);
        ulong hint=checked((heap+step-1)&~(step-1));
        for(int attempt=0;attempt<256&&hint<=last&&(ulong)size-1<=last-hint;attempt++,hint+=step) {
            IntPtr pointer=VirtualAllocEx(Handle,new IntPtr(unchecked((long)hint)),(UIntPtr)size,0x3000,0x04);
            if(pointer==IntPtr.Zero)continue;
            ulong address=unchecked((ulong)pointer.ToInt64());
            if(address>=heap&&address<=last&&(address-heap)%4==0&&(ulong)size-1<=last-address) {
                Blocks.Add(address,size);return address;
            }
            if(!VirtualFreeEx(Handle,pointer,UIntPtr.Zero,0x8000))
                throw new InvalidOperationException("Rejected camera allocation could not be released: "+Marshal.GetLastWin32Error());
        }
        throw new InvalidOperationException("No free data allocation is available in the camera compact-pointer range.");
    }
    internal void Write(ulong address,byte[] data) {
        if(Disposed)throw new ObjectDisposedException("RemoteCameraData");
        if(data==null||data.Length==0)throw new ArgumentException("Camera data is empty.");
        bool owned=Blocks.Any(b=>address>=b.Key&&data.Length<=b.Value&&address-b.Key<=(ulong)(b.Value-data.Length));
        if(!owned)throw new InvalidOperationException("Camera data write is outside this allocator's reserved blocks.");
        UIntPtr done;IntPtr pointer=new IntPtr(unchecked((long)address));
        if(!Native.WriteProcessMemory(Handle,pointer,data,(UIntPtr)data.Length,out done)||done.ToUInt64()!=(ulong)data.Length)
            throw new InvalidOperationException("Camera data write failed: "+Marshal.GetLastWin32Error());
        var readback=new byte[data.Length];
        if(!Native.ReadProcessMemory(Handle,pointer,readback,(UIntPtr)readback.Length,out done)
            ||done.ToUInt64()!=(ulong)readback.Length||!readback.SequenceEqual(data))
            throw new InvalidOperationException("Camera data readback failed.");
    }
    internal void Release(ulong address) {
        if(Disposed)throw new ObjectDisposedException("RemoteCameraData");
        if(!Blocks.ContainsKey(address))throw new InvalidOperationException("Camera allocation is not owned by this allocator.");
        if(!VirtualFreeEx(Handle,new IntPtr(unchecked((long)address)),UIntPtr.Zero,0x8000))
            throw new InvalidOperationException("Camera data release failed: "+Marshal.GetLastWin32Error());
        Blocks.Remove(address);
    }
    public void Dispose() { Disposed=true; }
}

using System;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;

// Four title-native float controls and the integer motion-blur pass gate, in
// writable .data of the exact Halo 3 image
// already identity-checked by CameraSession. No executable pages or files change.
// These are renderer-wide: while enabled they also affect the on-foot view.
sealed class CockpitMotionBlur : IDisposable {
    internal static readonly string[] Names={"motion_blur_scale_x","motion_blur_scale_y","motion_blur_max_x","motion_blur_max_y","motion_blur_max_viewport_count"};
    internal static readonly ulong[] NameRvas={0x7d3090,0x7d3078,0x7d2ff0,0x7d2fd8,0x7d3280};
    internal static readonly ulong[] TableRvas={0x8a3960,0x8a3978,0x8a39c0,0x8a3948,0x8a37c8};
    internal static readonly ulong[] ValueRvas={0x8ac5f4,0x8ac5e8,0x8ac600,0x8ac5f0,0x8ac5e4};
    internal static readonly ulong[] Types={6,6,6,6,8};
    readonly ulong Base;
    readonly Func<ulong,int,byte[]> Read;
    readonly Action<ulong,byte[]> Write;
    readonly Func<bool> Alive;
    readonly Action<string> Log;
    readonly IntPtr Handle;
    readonly byte[][] Originals=new byte[Names.Length][];
    readonly bool[] Owned=new bool[Names.Length];
    readonly byte[] Zero=new byte[4];
    bool Faulted,Disposed,Announced;

    internal CockpitMotionBlur(CameraSession session,Action<string> log) {
        Base=session.Base;Read=session.Read;Alive=()=>!session.Process.HasExited;Log=log;
        Handle=Native.OpenProcess(0x438,false,session.Process.Id);
        if(Handle==IntPtr.Zero)throw new InvalidOperationException("Cannot open camera motion controls: "+Marshal.GetLastWin32Error());
        Write=(address,data)=>{
            UIntPtr done;
            if(!Native.WriteProcessMemory(Handle,new IntPtr(unchecked((long)address)),data,(UIntPtr)data.Length,out done)||done.ToUInt64()!=(ulong)data.Length)
                throw new InvalidOperationException("Motion control write failed: "+Marshal.GetLastWin32Error());
        };
        try {Validate();}catch {Native.CloseHandle(Handle);throw;}
    }
    // Synthetic memory fixture constructor; production always uses CameraSession.
    internal CockpitMotionBlur(ulong moduleBase,Func<ulong,int,byte[]> read,Action<ulong,byte[]> write,Func<bool> alive,Action<string> log) {
        Base=moduleBase;Read=read;Write=write;Alive=alive;Log=log;Validate();
    }
    void Validate() {
        if(!Alive())throw new InvalidOperationException("Game closed.");
        for(int i=0;i<Names.Length;i++) {
            var text=Encoding.ASCII.GetBytes(Names[i]+"\0");
            if(!Read(Base+NameRvas[i],text.Length).SequenceEqual(text))throw new InvalidOperationException("Motion control name changed.");
            var entry=Read(Base+TableRvas[i],24);
            if(Native.U64(entry)!=Base+NameRvas[i]||Native.U64(entry,8)!=Types[i]||Native.U64(entry,16)!=Base+ValueRvas[i])
                throw new InvalidOperationException("Motion control ownership changed.");
        }
    }
    static bool Valid(int index,byte[] bytes) {
        if(Types[index]==8) {int count=BitConverter.ToInt32(bytes,0);return count>=0&&count<=4;}
        float value=BitConverter.ToSingle(bytes,0);return !float.IsNaN(value)&&!float.IsInfinity(value)&&value>=0&&value<=10;
    }
    void Set(int i,byte[] expected,byte[] replacement) {
        Validate();
        ulong address=Base+ValueRvas[i];
        if(!Read(address,4).SequenceEqual(expected))throw new InvalidOperationException("Motion control changed during edit.");
        Write(address,replacement);
        if(!Read(address,4).SequenceEqual(replacement))throw new InvalidOperationException("Motion control readback failed.");
    }
    internal void Tick(bool enabled) {
        if(Disposed)return;
        if(!enabled){Restore();if(!Owned.Any(x=>x))Faulted=false;return;}
        if(Faulted)return;
        try {
            Validate();
            var values=ValueRvas.Select(rva=>Read(Base+rva,4)).ToArray();
            if(values.Where((value,index)=>!Valid(index,value)).Any())throw new InvalidOperationException("Motion control value is outside the supported range.");
            // Validate the complete set before any mutation. A map's native loader
            // can reinitialize these globals; preserve its latest nonzero value.
            // Native view count is always 1..4. A maximum of zero takes the
            // engine's non-motion-blur shader branch, rather than only reducing
            // amplitudes in the still-selected multi-tap blur shader.
            for(int i=0;i<Names.Length;i++) {
                if(values[i].SequenceEqual(Zero))continue;
                Originals[i]=values[i];Owned[i]=true;
                Set(i,values[i],Zero);
            }
            if(!Announced){Log("Motion blur OFF: native pass gate and four amplitude controls verified (also affects on-foot view while cameras are enabled).");Announced=true;}
        }catch(Exception error){Faulted=true;Log("Motion blur suppression stopped: "+error.Message);Restore(true);}
    }
    internal void Restore() {Restore(false);}
    void Restore(bool failedTransaction) {
        if(Disposed)return;
        if(!Alive()){Array.Clear(Owned,0,Owned.Length);Announced=false;return;}
        for(int i=Names.Length-1;i>=0;i--) {
            if(!Owned[i])continue;
            try {
                Validate();
                var current=Read(Base+ValueRvas[i],4);
                // Restore only values still owned by this helper. Preserve a
                // native reload or another tool's newer nonzero setting.
                // A failed 4-byte write can leave a mixture of the old bytes
                // and zero bytes. Roll back that exact partial-write shape only
                // during the failed transaction, never during normal teardown.
                bool partial=failedTransaction&&current.Select((value,k)=>value==0||value==Originals[i][k]).All(value=>value);
                if(current.SequenceEqual(Zero)||partial)Set(i,current,Originals[i]);
                Owned[i]=false;
            }catch(Exception error){Log("Motion blur restore pending for "+Names[i]+": "+error.Message);}
        }
        if(!Owned.Any(x=>x)&&Announced){Log("Motion blur: original renderer values restored.");Announced=false;}
    }
    public void Dispose() {
        if(Disposed)return;
        Restore();
        if(Owned.Any(x=>x))Log("Some motion controls could not be confirmed restored; closing the game resets them.");
        Disposed=true;if(Handle!=IntPtr.Zero)Native.CloseHandle(Handle);
    }
}

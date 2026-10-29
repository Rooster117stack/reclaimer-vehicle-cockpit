using System;
using System.Linq;
using System.Runtime.InteropServices;

// Parent supplies true only for one validated local player driving a supported
// boost vehicle with cockpit mode enabled. These client-global binocular settings must
// be restored before use on foot, in other seats, or with multiple local players.
sealed class VehicleBoostZoom : IDisposable {
    internal static bool HasBoost(uint unitFlags,float peakPower) {
        return (unitFlags&(1u<<20))!=0&&!float.IsNaN(peakPower)
            &&!float.IsInfinity(peakPower)&&peakPower>0;
    }
    internal static bool IsPlayerDriver(uint seatFlags,int aiSeat) {
        return (seatFlags&4u)!=0&&(seatFlags&((1u<<11)|(1u<<16)))==0
            &&aiSeat!=3&&aiSeat!=4;
    }
    const ulong GlobalsRva=0xa46968,TableRva=0xa49018;
    static readonly byte[] Neutral=BitConverter.GetBytes(0).Concat(BitConverter.GetBytes(1f)).Concat(BitConverter.GetBytes(1f)).ToArray();
    readonly CameraSession Session;
    readonly Action<string> Log;
    readonly IntPtr Handle;
    Context Active;
    uint Datum;
    ulong Owner,Address;
    byte[] Row,Block,Original;
    bool Faulted,Disposed,ApplyWriteAttempted;

    internal VehicleBoostZoom(CameraSession session,Action<string> log) {
        Session=session;Log=log;
        Handle=Native.OpenProcess(0x438,false,session.Process.Id);
        if(Handle==IntPtr.Zero)throw new InvalidOperationException("Cannot open vehicle boost zoom controls: "+Marshal.GetLastWin32Error());
    }
    bool Alive() {return !Session.Process.HasExited;}
    byte[] Read(ulong address,int count) {return Session.Read(address,count);}
    ulong Pointer(ulong address) {
        ulong p=Native.U64(Read(address,8));
        if(!Native.Ptr(p))throw new InvalidOperationException("Vehicle boost zoom data is unavailable.");
        return p;
    }
    bool OwnerValid() {
        // Snapshot throws on an incomplete map; retain restoration ownership for
        // retry in that case. A confirmed different map/tag is never written.
        var now=Session.Snapshot();
        return Session.Same(Active)&&now.Index==Active.Index&&now.Heap==Active.Heap
            &&(Datum&65535)<now.Count
            &&Pointer(Session.Base+TableRva)==now.Table&&Pointer(now.Index+24)==now.Table
            &&Pointer(Session.Base+GlobalsRva)==Owner
            &&Read(now.Table+(ulong)(Datum&65535)*8,8).SequenceEqual(Row)
            &&Session.Tag(now,Datum,"matg")==Owner
            &&Read(Owner+0x434,12).SequenceEqual(Block);
    }
    static bool Supported(byte[] value) {
        int count=BitConverter.ToInt32(value,0);
        float min=BitConverter.ToSingle(value,4),max=BitConverter.ToSingle(value,8);
        return count>=0&&count<=16&&!float.IsNaN(min)&&!float.IsInfinity(min)
            &&!float.IsNaN(max)&&!float.IsInfinity(max)&&min>=1&&max>=min&&max<=64;
    }
    void Set(byte[] expected,byte[] replacement,bool apply=false) {
        if(!OwnerValid()||!Read(Address,12).SequenceEqual(expected))
            throw new InvalidOperationException("Vehicle boost zoom ownership or values changed.");
        UIntPtr done;
        if(apply)ApplyWriteAttempted=true;
        if(!Native.WriteProcessMemory(Handle,new IntPtr(unchecked((long)Address)),replacement,(UIntPtr)12,out done)||done.ToUInt64()!=12)
            throw new InvalidOperationException("Vehicle boost zoom write failed: "+Marshal.GetLastWin32Error());
        if(!Read(Address,12).SequenceEqual(replacement))throw new InvalidOperationException("Vehicle boost zoom readback failed.");
        if(apply)ApplyWriteAttempted=false;
    }
    void Forget() {Active=null;Original=null;Row=null;Block=null;Owner=Address=0;}
    internal void Tick(bool enabled) {
        if(Disposed)return;
        if(!enabled){Restore();if(Active==null)Faulted=false;return;}
        if(Faulted)return;
        ApplyWriteAttempted=false;
        try {
            if(!Alive()){Forget();return;}
            if(Active!=null) {
                if(!OwnerValid()){Forget();return;}
                if(Read(Address,12).SequenceEqual(Neutral))return;
                throw new InvalidOperationException("Vehicle boost zoom controls were changed externally; preserving them.");
            }
            var c=Session.Snapshot();ulong globals=Pointer(Session.Base+GlobalsRva);
            int matches=0;uint selected=0;
            for(int i=0;i<c.Count;i++) {
                if(Session.Group(c,i)!="matg")continue;
                uint d=((uint)BitConverter.ToUInt16(c.Tags,i*8+2)<<16)|(uint)i;
                if(Session.Tag(c,d,"matg")==globals){selected=d;matches++;}
            }
            if(matches!=1)throw new InvalidOperationException("No unique loaded globals tag for Vehicle boost zoom.");
            var block=Read(globals+0x434,12);
            if(Native.U32(block)!=1||Native.U32(block,4)==0)
                throw new InvalidOperationException("Unsupported Vehicle binocular information block.");
            ulong address=checked(c.Heap+(ulong)Native.U32(block,4)*4+0x84);
            if(!Native.Ptr(address))throw new InvalidOperationException("Invalid Vehicle binocular data address.");
            var before=Read(address,12);
            if(!Supported(before))throw new InvalidOperationException("Unsupported binocular magnification values.");
            if(before.SequenceEqual(Neutral))return;
            Active=c;Datum=selected;Owner=globals;Address=address;Original=before;Block=block;
            Row=c.Tags.Skip((int)(selected&65535)*8).Take(8).ToArray();
            Set(before,Neutral,true);
            Log("Boost cockpit: boost binocular zoom suppressed; original settings retained.");
        }catch(Exception error){Faulted=true;Log("Vehicle boost zoom suppression stopped: "+error.Message);Restore(ApplyWriteAttempted);ApplyWriteAttempted=false;}
    }
    internal void Restore() {Restore(false);}
    void Restore(bool failedTransaction) {
        if(Disposed||Active==null)return;
        try {
            if(!Alive()){Forget();return;}
            if(!OwnerValid()){Log("Vehicle boost zoom: map/tag changed; no stale restoration write.");Forget();return;}
            var current=Read(Address,12);
            // Only the immediate failed transaction may roll back a partial
            // write. Normal teardown preserves any foreign replacement.
            bool partial=failedTransaction&&current.Select((v,i)=>v==Original[i]||v==Neutral[i]).All(x=>x);
            if(current.SequenceEqual(Neutral)||partial) {
                if(!current.SequenceEqual(Original))Set(current,Original);
                Log("Boost cockpit: original binocular zoom settings restored.");
            } else if(!current.SequenceEqual(Original))Log("Vehicle boost zoom: newer external settings preserved.");
            Forget();
        }catch(Exception error){Log("Vehicle boost zoom restoration pending: "+error.Message);}
    }
    public void Dispose() {
        if(Disposed)return;
        Restore();
        if(Active!=null)Log("Vehicle boost zoom restoration could not be confirmed; restarting the game resets it.");
        Disposed=true;Native.CloseHandle(Handle);
    }
}

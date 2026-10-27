using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;

// Loaded data only. Disable each Warthog's engine-sound object function rather
// than a shared sound tag: HugeGrass cars reuse one of those same sound tags.
// Layouts: Assembly Halo3MCC vehi.xml; TagTool ScalarFunctionHeader.
sealed class WarthogEngineMute : IDisposable {
    sealed class Vehicle {
        internal uint Datum;
        internal ulong Address;
        internal float Maximum;
        internal byte[] TagRow;
        internal readonly List<Change> Changes=new List<Change>();
    }
    sealed class Change {
        internal Vehicle Owner;
        internal ulong Address;
        internal byte[] Before,After;
        internal string Label;
    }
    readonly CameraSession Session;
    readonly Action<string> Log;
    readonly IntPtr Handle;
    readonly Dictionary<uint,Vehicle> Vehicles=new Dictionary<uint,Vehicle>();
    readonly List<Change> Applied=new List<Change>();
    Context Active;
    bool Enabled;
    uint Target=uint.MaxValue;
    internal int VehicleCount {get{return Vehicles.Count;}}
    internal int MutedVehicleCount {get{return Applied.Select(x=>x.Owner.Datum).Distinct().Count();}}
    internal bool IsWarthog(uint datum) {return Vehicles.ContainsKey(datum);}
    internal bool CanMute(uint datum) {Vehicle v;return Vehicles.TryGetValue(datum,out v)&&v.Changes.Count>0;}
    internal float MaxAngularVelocity(uint datum) {Vehicle v;return Vehicles.TryGetValue(datum,out v)?v.Maximum:0f;}

    internal WarthogEngineMute(CameraSession session,Action<string> log) {
        Session=session;Log=log;
        Handle=Native.OpenProcess(0x438,false,session.Process.Id);
        if(Handle==IntPtr.Zero)throw new Exception("Cannot open vehicle sound data: "+Marshal.GetLastWin32Error());
    }
    static string[] TagNames(CameraSession session,Context context) {
        string path=CacheFiles.Find(session.Maps,context.Header);
        using(var f=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.ReadWrite)) {
            Func<long,int,byte[]> get=(position,length)=>{
                if(position<0||length<0||length>16*1024*1024||position+length>f.Length)throw new Exception("Invalid audio tag-name table.");
                f.Position=position;var result=new byte[length];int done=0;
                while(done<length){int n=f.Read(result,done,length-done);if(n==0)throw new EndOfStreamException();done+=n;}
                return result;
            };
            var header=get(0,0x4000);
            if(!CacheFiles.SameIdentity(header,context.Header))throw new Exception("Audio cache differs from the loaded map.");
            int count=checked((int)Native.U32(header,0x20));
            if(count<=0||count>65535)throw new Exception("Invalid audio tag-name count.");
            uint mask=Native.U32(header,0x4cc);
            var data=get(unchecked((uint)(Native.U32(header,0x24)+mask)),checked((int)Native.U32(header,0x28)));
            var offsets=get(unchecked((uint)(Native.U32(header,0x2c)+mask)),count*4);
            var names=new string[count];
            for(int i=0;i<count;i++) {
                uint offset=Native.U32(offsets,i*4);if(offset>=data.Length){names[i]="";continue;}
                int end=Array.IndexOf(data,(byte)0,(int)offset);if(end<0){names[i]="";continue;}
                names[i]=Encoding.ASCII.GetString(data,(int)offset,end-(int)offset).Replace('/','\\').ToLowerInvariant();
            }
            return names;
        }
    }
    static bool ChassisName(string name) {
        const string prefix="objects\\vehicles\\";
        if(!name.StartsWith(prefix,StringComparison.Ordinal))return false;
        string rest=name.Substring(prefix.Length);string[] parts=rest.Split('\\');
        return parts.Length==2&&(parts[0]=="warthog"||parts[0].StartsWith("warthog_",StringComparison.Ordinal))
            &&parts[1].StartsWith("warthog",StringComparison.Ordinal);
    }
    static bool EngineSoundName(string name) {
        return name=="sound\\vehicles\\warthog\\new_warthog_left\\new_warthog_left"
            ||name=="sound\\vehicles\\warthog\\new_warthog_right\\new_warthog_right"
            ||name=="sound\\vehicles\\warthog\\warthog_engine_lod\\warthog_engine_lod";
    }
    void Plan(Vehicle owner,ulong address,byte[] after,string label) {
        var before=Session.Read(address,after.Length);if(before.SequenceEqual(after))return;
        owner.Changes.Add(new Change {Owner=owner,Address=address,Before=before,After=after,Label=label});
    }
    void FindSoundFunction(Context c,Vehicle v,string[] paths,Dictionary<uint,string> strings) {
        Func<uint,string> name=id=>strings.ContainsKey(id)?strings[id]:"";
        int functionCount;ulong functions=Session.Block(c,v.Address+0xac,128,out functionCount);
        int found=0;
        for(int j=0;j<functionCount;j++) {
            ulong row=functions+(ulong)j*0x2c;var f=Session.Read(row,0x2c);
            uint output=Native.U32(f,8);
            if(name(Native.U32(f,4))!="engine_power"||name(output)!="sound_engine_power"
                ||name(Native.U32(f,12))!="driver_seat_occupied")continue;
            // This precise authored function is scalar, bounded 0..1, and Always
            // Active. In the pinned engine's evaluator (RVA 0x346118), threshold
            // comparison at 0x3462E3 controls the active boolean, not magnitude.
            // With Always Active/Always Exports clear, inactive outputs become
            // zero at 0x34637E. Scripted per-object overrides must be rejected by
            // the caller's telemetry gate before this feature is enabled.
            if(Native.U32(f)!=4||BitConverter.ToSingle(f,0x10)!=0f||Native.U32(f,0x28)!=0)continue;
            int bytes=checked((int)Native.U32(f,0x14));if(bytes<32||bytes>4096)continue;
            ulong mapping=checked(c.Heap+(ulong)Native.U32(f,0x20)*4);
            var curve=Session.Read(mapping,32);
            if(curve[0]>10||(curve[1]&4)==0||curve[2]!=0||BitConverter.ToSingle(curve,4)!=0f||BitConverter.ToSingle(curve,8)!=1f)continue;
            // Do not disable an exported function reused by another object
            // function, or by a non-engine attachment on a custom Warthog.
            bool safe=true;int sounds=0;
            for(int k=0;k<functionCount;k++) {
                if(k==j)continue;var other=Session.Read(functions+(ulong)k*0x2c,0x2c);
                if(Native.U32(other,4)==output||Native.U32(other,12)==output||Native.U32(other,0x28)==output)safe=false;
            }
            int attachmentCount;ulong attachments=Session.Block(c,v.Address+0xbc,256,out attachmentCount);
            for(int k=0;k<attachmentCount;k++) {
                var attachment=Session.Read(attachments+(ulong)k*0x20,0x20);
                if(Native.U32(attachment,0x18)!=output&&Native.U32(attachment,0x1c)!=output)continue;
                uint sound=Native.U32(attachment,12);int index=(int)(sound&65535);
                if(sound==uint.MaxValue||index>=paths.Length||!EngineSoundName(paths[index])){safe=false;continue;}
                ulong loop=Session.Tag(c,sound,"lsnd");
                if(BitConverter.ToUInt16(Session.Read(loop+0x24,2),0)!=23){safe=false;continue;}
                sounds++;
            }
            if(!safe||sounds!=3)continue;
            Plan(v,row,BitConverter.GetBytes(0u),"engine sound function activation flags");
            Plan(v,row+0x10,BitConverter.GetBytes(2f),"engine sound function activation threshold");
            found++;
        }
        if(found!=1)v.Changes.Clear();
    }
    internal void Refresh() {
        var c=Session.Snapshot();if(Active!=null&&Active.Key==c.Key)return;
        bool wanted=Enabled;uint target=Target;Restore();Vehicles.Clear();Active=null;
        string stem=Native.Z(c.Header,0xc0,32);if(stem=="mainmenu"||stem=="130_epilogue")return;
        string[] paths=TagNames(Session,c);var strings=Session.Strings(c);
        for(int i=0;i<c.Count&&i<paths.Length;i++) {
            if(!ChassisName(paths[i])||Session.Group(c,i)!="vehi")continue;
            uint datum=((uint)BitConverter.ToUInt16(c.Tags,i*8+2)<<16)|(uint)i;
            try {
                ulong address=Session.Tag(c,datum,"vehi");int jeepCount;ulong jeep=Session.Block(c,address+0x2cc,1,out jeepCount);
                if(jeepCount!=1)continue;
                float maximum=BitConverter.ToSingle(Session.Read(jeep+0x18,4),0);
                if(float.IsNaN(maximum)||float.IsInfinity(maximum)||maximum<1||maximum>100000)continue;
                var vehicle=new Vehicle {Datum=datum,Address=address,Maximum=maximum,TagRow=c.Tags.Skip(i*8).Take(8).ToArray()};
                FindSoundFunction(c,vehicle,paths,strings);Vehicles.Add(datum,vehicle);
            }catch(Exception ex){Log("Audio Warthog skipped: "+ex.Message);}
        }
        // Cache compilers can deduplicate entire function blocks. Never mutate a
        // block also owned by a non-Warthog vehicle (cars can reuse engine audio).
        var protectedFunctions=new HashSet<ulong>();
        for(int i=0;i<c.Count;i++) {
            if(Session.Group(c,i)!="vehi")continue;
            uint datum=((uint)BitConverter.ToUInt16(c.Tags,i*8+2)<<16)|(uint)i;
            if(Vehicles.ContainsKey(datum))continue;
            ulong vehicle=Session.Tag(c,datum,"vehi");int n;ulong functions=Session.Block(c,vehicle+0xac,128,out n);
            for(int j=0;j<n;j++){ulong row=functions+(ulong)j*0x2c;protectedFunctions.Add(row);protectedFunctions.Add(row+0x10);}
        }
        foreach(var vehicle in Vehicles.Values)if(vehicle.Changes.Any(x=>protectedFunctions.Contains(x.Address)))vehicle.Changes.Clear();
        if(!Session.Same(c))throw new Exception("Map changed during audio tag scan.");
        Active=c;Log("AUDIO TAGS "+Vehicles.Count+" Warthog chassis; "+Vehicles.Values.Count(v=>v.Changes.Count>0)+" verified engine-sound functions.");
        if(wanted)SetEnabled(true,target);
    }
    bool OwnerValid(Change change) {
        if(Active==null||!Session.Same(Active))return false;
        // Reclaimer may replace the table after entry while preserving its tag
        // heap. Validate ownership against the current table, never a stale one.
        var current=Session.Snapshot();var v=change.Owner;int index=(int)(v.Datum&65535);
        return current.Heap==Active.Heap&&CacheFiles.SameIdentity(current.Header,Active.Header)
            &&index<current.Count&&Session.Read(current.Table+(ulong)index*8,8).SequenceEqual(v.TagRow)
            &&Session.Tag(current,v.Datum,"vehi")==v.Address;
    }
    void Write(Change change,bool restore) {
        var expected=restore?change.After:change.Before;var replacement=restore?change.Before:change.After;
        if(!OwnerValid(change)||!Session.Read(change.Address,expected.Length).SequenceEqual(expected))throw new Exception("Engine sound data changed; edit skipped.");
        UIntPtr written;
        if(!Native.WriteProcessMemory(Handle,new IntPtr(unchecked((long)change.Address)),replacement,(UIntPtr)replacement.Length,out written)
            ||written.ToUInt64()!=(ulong)replacement.Length)throw new Exception("Engine sound data write failed.");
        if(!Session.Read(change.Address,replacement.Length).SequenceEqual(replacement))throw new Exception("Engine sound data readback failed.");
        Log((restore?"AUDIO RESTORE ":"AUDIO APPLY ")+change.Label+" @"+change.Address.ToString("X"));
    }
    internal void SetEnabled(bool enabled) {
        SetEnabled(enabled,uint.MaxValue);
    }
    internal void SetEnabled(bool enabled,uint targetTag) {
        if(!enabled){Restore();return;}
        if(Target!=targetTag)Restore();Target=targetTag;
        Enabled=true;if(Active==null)return;
        try {
            foreach(var vehicle in Vehicles.Values)foreach(var change in vehicle.Changes) {
                if(targetTag!=uint.MaxValue&&vehicle.Datum!=targetTag)continue;
                if(Applied.Any(x=>x.Address==change.Address))continue;
                Applied.Add(change);Write(change,false);
            }
        }catch{Restore();throw;}
    }
    internal void Restore() {
        Enabled=false;
        foreach(var change in Applied.AsEnumerable().Reverse()) {
            try{if(OwnerValid(change)&&Session.Read(change.Address,change.After.Length).SequenceEqual(change.After))Write(change,true);}
            catch(Exception ex){Log("Audio restoration skipped: "+ex.Message);}
        }
        Applied.Clear();
    }
    public void Dispose(){Restore();Native.CloseHandle(Handle);}
}

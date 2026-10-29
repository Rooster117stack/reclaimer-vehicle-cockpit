using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;

// Read-only listener/vehicle state for the exact engine already checked by CameraSession.
// Native structure evidence is recorded in test/audio-telemetry-evidence.md.
sealed class VehicleAudioFrame {
    internal bool Valid, InWarthog, HasNativeRpm, EngineRunning, EngineFunctionsOverridden;
    internal float Distance, Speed, EngineRpmNormalized, EnginePower;
    internal uint WarthogDatum=uint.MaxValue, WarthogTagDatum=uint.MaxValue;
    // Direct vehicle seat identity also gates the Ghost's client-only zoom fix.
    internal uint DirectVehicleTag=uint.MaxValue;
    internal int DirectVehicleSeat=-1;
    internal int Gear;
    internal float[] ListenerPosition, WarthogPosition;
    internal string Error;
    internal readonly List<VehicleEngineFrame> NearbyEngines=new List<VehicleEngineFrame>();
}

sealed class VehicleEngineFrame {
    internal uint WarthogDatum, WarthogTagDatum;
    internal float Distance, EngineRpmNormalized, EnginePower;
    internal bool HasNativeRpm, EngineRunning, EngineFunctionsOverridden;
}

sealed class VehicleAudioTelemetry {
    [StructLayout(LayoutKind.Sequential)] struct ThreadInfo {
        internal int ExitStatus;
        internal IntPtr Teb, ProcessId, ThreadId, Affinity;
        internal int Priority, BasePriority;
    }
    [DllImport("kernel32.dll",SetLastError=true)] static extern IntPtr OpenThread(uint access,bool inherit,uint id);
    [DllImport("ntdll.dll")] static extern int NtQueryInformationThread(IntPtr thread,int infoClass,out ThreadInfo info,int length,out int returned);
    readonly CameraSession Session;
    readonly Func<uint,bool> IsWarthog;
    readonly Func<uint,float> MaxAngularVelocity;
    ulong Tls;
    DateTime NextLocate=DateTime.MinValue;
    string LastReadError="Waiting for a local player.";
    const ulong TlsIndexRva=0xA39F9C;

    sealed class Table {
        internal ulong Header, Data, BitsAddress;
        internal int High, Capacity, Stride;
        internal byte[] Bits, Rows;
    }
    sealed class Body {
        internal uint Datum, Tag, Parent;
        internal ulong Address, Header;
        internal byte Type;
        internal byte[] Row, Data;
    }
    internal VehicleAudioTelemetry(CameraSession session,Func<uint,bool> isWarthog,Func<uint,float> maxAngularVelocity) {
        Session=session;IsWarthog=isWarthog;MaxAngularVelocity=maxAngularVelocity;
    }
    static float F(byte[] b,int o) {return BitConverter.ToSingle(b,o);}
    static bool Finite(float x) {return !float.IsNaN(x)&&!float.IsInfinity(x);}
    static bool Equal(byte[] a,byte[] b) {if(a.Length!=b.Length)return false;for(int i=0;i<a.Length;i++)if(a[i]!=b[i])return false;return true;}
    ulong P(ulong a) {ulong p=Native.U64(Session.Read(a,8));if(!Native.Ptr(p))throw new Exception("Waiting for local vehicle audio state.");return p;}
    Table ReadTable(ulong header,int stride,int maximum) {
        byte[] h=Session.Read(header,0x58);
        int capacity=BitConverter.ToInt32(h,0x20),high=BitConverter.ToInt32(h,0x3c);
        ulong data=Native.U64(h,0x48),bits=Native.U64(h,0x50);
        if(capacity<=0||capacity>maximum||BitConverter.ToInt32(h,0x24)!=stride||high<0||high>capacity||!Native.Ptr(data)||!Native.Ptr(bits))
            throw new Exception("Native audio object table changed.");
        return new Table {Header=header,Data=data,BitsAddress=bits,High=high,Capacity=capacity,Stride=stride,
            Bits=Session.Read(bits,Math.Max(4,((high+31)/32)*4)),Rows=high==0?new byte[0]:Session.Read(data,checked(high*stride))};
    }
    bool Active(Table t,int i) {return i>=0&&i<t.High&&(Native.U32(t.Bits,(i/32)*4)&(1u<<(i%32)))!=0;}
    uint Datum(Table t,int i) {return ((uint)BitConverter.ToUInt16(t.Rows,i*t.Stride)<<16)|(uint)i;}
    Body Object(Table t,uint datum,int bytes) {
        int index=(int)(datum&65535);
        if(datum==uint.MaxValue||!Active(t,index)||Datum(t,index)!=datum)throw new Exception("Vehicle or player was replaced.");
        var row=new byte[0x18];Buffer.BlockCopy(t.Rows,index*0x18,row,0,0x18);
        ulong address=Native.U64(row,0x10);
        if(!Native.Ptr(address))throw new Exception("Waiting for a live vehicle object.");
        byte[] data=Session.Read(address,bytes);
        if(!Equal(row,Session.Read(t.Data+(ulong)index*0x18,0x18)))throw new Exception("Vehicle identity changed during audio read.");
        return new Body {Datum=datum,Tag=Native.U32(data),Parent=Native.U32(data,0x10),Address=address,
            Header=t.Data+(ulong)index*0x18,Type=row[3],Row=row,Data=data};
    }
    static float[] Position(Body b) {
        float[] p={F(b.Data,0x50),F(b.Data,0x54),F(b.Data,0x58)};
        foreach(float x in p)if(!Finite(x)||Math.Abs(x)>100000)throw new Exception("Vehicle audio position is unavailable.");
        return p;
    }
    static float Distance(float[] a,float[] b) {double x=a[0]-b[0],y=a[1]-b[1],z=a[2]-b[2];return (float)Math.Sqrt(x*x+y*y+z*z);}
    void Locate() {
        if(DateTime.UtcNow<NextLocate)throw new Exception(LastReadError);
        NextLocate=DateTime.UtcNow.AddSeconds(1);
        uint index=Native.U32(Session.Read(Session.Base+TlsIndexRva,4));
        if(index>=1088)throw new Exception("Unsupported engine TLS layout.");
        var found=new HashSet<ulong>();
        // Process.Threads is cached. A helper attached in the menu otherwise
        // never sees the simulation thread created when a match starts.
        Session.Process.Refresh();
        foreach(ProcessThread thread in Session.Process.Threads) {
            IntPtr handle=OpenThread(0x40,false,(uint)thread.Id);if(handle==IntPtr.Zero)continue;
            try {
                ThreadInfo info;int returned;
                if(NtQueryInformationThread(handle,0,out info,Marshal.SizeOf(typeof(ThreadInfo)),out returned)!=0
                    ||info.ProcessId.ToInt64()!=Session.Process.Id||info.ThreadId.ToInt64()!=thread.Id)continue;
                ulong vector=P(unchecked((ulong)info.Teb.ToInt64())+0x58);
                ulong tls=P(vector+8*index);
                if(Native.U32(Session.Read(tls+0x18,4))!=1)continue;
                ReadTable(P(tls+0x38),0x18,65536);ReadTable(P(tls+0x10),0x2f8,128);
                byte[] mapping=Session.Read(P(tls+0x110),0xe8);
                int users=BitConverter.ToInt16(mapping,0);
                if(users<0||users>4)continue;
                found.Add(tls);
            }catch{}finally{Native.CloseHandle(handle);}
        }
        if(found.Count!=1)throw new Exception("Waiting for one local simulation context ("+found.Count+" found).");
        foreach(ulong tls in found)Tls=tls;
    }
    internal VehicleAudioFrame ReadFrame() {
        try {return ReadCore();}
        catch(Exception ex) {Tls=0;LastReadError=ex.Message;return new VehicleAudioFrame {Error=ex.Message};}
    }
    VehicleAudioFrame ReadCore() {
        if(Tls==0)Locate();
        if(Native.U32(Session.Read(Tls+0x18,4))!=1)throw new Exception("Audio simulation context ended.");
        ulong objects=P(Tls+0x38),players=P(Tls+0x10),mapping=P(Tls+0x110);
        byte[] map=Session.Read(mapping,0xe8);
        // One listener only: never guess which split-screen player owns the speakers.
        if(BitConverter.ToInt16(map,0)!=1)throw new Exception("Vehicle audio requires one local player.");
        uint player=Native.U32(map,4),unit=Native.U32(map,0x14);
        Table pt=ReadTable(players,0x2f8,128),ot=ReadTable(objects,0x18,65536);
        int pi=(int)(player&65535);
        if(!Active(pt,pi)||Datum(pt,pi)!=player||Native.U32(pt.Rows,pi*pt.Stride+0x28)!=unit
            ||(Native.U32(pt.Rows,pi*pt.Stride+4)&3)!=1)throw new Exception("Waiting for a living local player.");
        Body listener=Object(ot,unit,0x180);
        if(listener.Type!=0||Native.U32(listener.Data,0x178)!=player||(Native.U32(listener.Data,0x110)&4)!=0)
            throw new Exception("Waiting for a living local player.");
        uint parent=listener.Parent;Body attached=listener;Body seated=null;Body directVehicle=null;
        int directSeat=-1;
        // Gunner bipeds can be parented to a turret which is itself attached to the chassis.
        var visited=new HashSet<uint>();visited.Add(unit);
        for(int depth=0;parent!=uint.MaxValue&&depth<5;depth++) {
            if(!visited.Add(parent))throw new Exception("Invalid vehicle parent chain.");
            attached=Object(ot,parent,0x180);
            if(depth==0&&attached.Type==1) {
                directVehicle=attached;
                directSeat=BitConverter.ToInt16(Session.Read(listener.Address+0x24e,2),0);
            }
            if(attached.Type==1&&IsWarthog(attached.Tag))seated=attached;
            parent=attached.Parent;
        }
        if(parent!=uint.MaxValue)throw new Exception("Unsupported vehicle parent chain.");
        float[] listenerPos=Position(attached);
        Body chosen=null;float nearest=float.MaxValue;
        var nearby=new List<VehicleEngineFrame>();
        for(int i=0;i<ot.High;i++) {
            if(!Active(ot,i)||ot.Rows[i*0x18+3]!=1)continue;
            Body b;
            try {b=Object(ot,Datum(ot,i),0x180);}catch{continue;}
            if(b.Parent!=uint.MaxValue||!IsWarthog(b.Tag))continue;
            float distance=Distance(listenerPos,Position(b));
            if(distance<=20)nearby.Add(ReadEngine(b,distance));
            if(seated!=null&&b.Datum==seated.Datum){chosen=b;nearest=0;}
            else if(nearest>0&&distance<nearest){chosen=b;nearest=distance;}
        }
        var frame=new VehicleAudioFrame {Valid=true,Distance=float.MaxValue,ListenerPosition=listenerPos};
        if(directVehicle!=null) {
            frame.DirectVehicleTag=directVehicle.Tag;frame.DirectVehicleSeat=directSeat;
        }
        frame.NearbyEngines.AddRange(nearby);
        foreach(VehicleEngineFrame vehicle in nearby)if(vehicle.EngineFunctionsOverridden)frame.EngineFunctionsOverridden=true;
        if(chosen!=null) {
            byte[] engine=Session.Read(chosen.Address+0x470,0x64);
            float maximum=MaxAngularVelocity(chosen.Tag),angular=F(engine,4),power;
            // Engine power is a unit field beyond the common-object prefix.
            power=F(Session.Read(chosen.Address+0x2b4,4),0);
            bool native=engine[0x60]==1&&Finite(maximum)&&maximum>0&&maximum<100000
                &&Finite(angular)&&angular>=-0.001f&&angular<=maximum*4&&(Native.U32(chosen.Data,0xa8)&0x800)==0;
            if(!Finite(power)||power<-.001f||power>1.001f)throw new Exception("Vehicle engine power layout changed.");
            float vx=F(chosen.Data,0x74),vy=F(chosen.Data,0x78),vz=F(chosen.Data,0x7c);
            frame.InWarthog=seated!=null&&chosen.Datum==seated.Datum;
            frame.WarthogDatum=chosen.Datum;frame.WarthogTagDatum=chosen.Tag;frame.WarthogPosition=Position(chosen);
            frame.Distance=nearest;frame.EnginePower=Math.Max(0,Math.Min(1,power));frame.EngineRunning=frame.EnginePower>.001f;
            frame.HasNativeRpm=native;frame.EngineRpmNormalized=native?Math.Max(0,Math.Min(1,angular/maximum)):0;
            frame.Gear=unchecked((sbyte)engine[0xc]);
            if(Finite(vx)&&Finite(vy)&&Finite(vz))frame.Speed=(float)Math.Sqrt(vx*vx+vy*vy+vz*vz);
            if(!Equal(chosen.Row,Session.Read(chosen.Header,0x18))||Native.U32(Session.Read(chosen.Address,4))!=chosen.Tag)
                throw new Exception("Vehicle was replaced during audio read.");
        }
        if(P(Tls+0x38)!=objects||P(Tls+0x10)!=players||P(Tls+0x110)!=mapping
            ||Native.U32(Session.Read(mapping+0x14,4))!=unit||!Equal(listener.Row,Session.Read(listener.Header,0x18)))
            throw new Exception("Local player changed during audio read.");
        if(directVehicle!=null&&(!Equal(directVehicle.Row,Session.Read(directVehicle.Header,0x18))
            ||Native.U32(Session.Read(listener.Address+0x10,4))!=directVehicle.Datum
            ||BitConverter.ToInt16(Session.Read(listener.Address+0x24e,2),0)!=directSeat))
            throw new Exception("Local vehicle seat changed during read.");
        return frame;
    }
    VehicleEngineFrame ReadEngine(Body body,float distance) {
        byte[] state=Session.Read(body.Address+0x470,0x64);
        float angular=F(state,4),maximum=MaxAngularVelocity(body.Tag),power=F(Session.Read(body.Address+0x2b4,4),0);
        bool valid=state[0x60]==1&&Finite(maximum)&&maximum>0&&maximum<100000
            &&Finite(angular)&&angular>=-.001f&&angular<=maximum*4&&Finite(power)&&power>=-.001f&&power<=1.001f
            &&(Native.U32(body.Data,0xa8)&0x800)==0;
        if(!Equal(body.Row,Session.Read(body.Header,0x18))||Native.U32(Session.Read(body.Address,4))!=body.Tag)
            throw new Exception("Nearby vehicle was replaced during audio read.");
        return new VehicleEngineFrame {WarthogDatum=body.Datum,WarthogTagDatum=body.Tag,Distance=distance,
            EnginePower=valid?Math.Max(0,Math.Min(1,power)):0,EngineRunning=valid&&power>.001f,
            EngineFunctionsOverridden=(Native.U32(body.Data,0xa8)&0x800)!=0,
            HasNativeRpm=valid,EngineRpmNormalized=valid?Math.Max(0,Math.Min(1,angular/maximum)):0};
    }
}

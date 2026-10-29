using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;

// Reclaimer client versions are independent of the verified MCC Halo 3 engine layout.
// Cache layouts: XboxChaos/Assembly. Never changes code pages or files in MCC.
static class Native {
    [DllImport("kernel32.dll", SetLastError=true)] internal static extern IntPtr OpenProcess(uint rights, bool inherit, int pid);
    [DllImport("kernel32.dll")] internal static extern bool CloseHandle(IntPtr handle);
    [DllImport("kernel32.dll", SetLastError=true)] internal static extern bool ReadProcessMemory(IntPtr handle, IntPtr address, byte[] data, UIntPtr size, out UIntPtr done);
    [DllImport("kernel32.dll", SetLastError=true)] internal static extern bool WriteProcessMemory(IntPtr handle, IntPtr address, byte[] data, UIntPtr size, out UIntPtr done);
    internal static uint U32(byte[] b, int o=0) { return BitConverter.ToUInt32(b,o); }
    internal static ulong U64(byte[] b, int o=0) { return BitConverter.ToUInt64(b,o); }
    internal static string Z(byte[] b, int o, int length) { int n=Array.IndexOf(b,(byte)0,o,length);return Encoding.ASCII.GetString(b,o,n<0?length:n-o); }
    internal static bool Ptr(ulong p) { return p>=0x10000 && p<0x800000000000; }
    internal static string Hash(string path) { using(var s=File.OpenRead(path))using(var h=SHA256.Create())return BitConverter.ToString(h.ComputeHash(s)).Replace("-","").ToLowerInvariant(); }
}

sealed class Context {
    internal ulong Index, Heap, Table, Scenario;
    internal int Count;
    internal string Name, Key;
    internal byte[] Header, Tags, Classes;
}
sealed class Edit {
    internal ulong Address, Owner;
    internal uint Datum;
    internal byte[] Row, Before, After;
    internal string Label;
}

sealed class MarkerGroup {
    internal ulong Address;
    internal int Count;
}
sealed class ExtraMarker {
    internal uint Id;
    internal byte[] Data;
}
sealed class ModelMarkers {
    internal ulong Header, Owner, NodeData;
    internal uint Datum;
    internal int Count, Nodes;
    internal byte[] Data;
    internal readonly Dictionary<uint,MarkerGroup> Groups=new Dictionary<uint,MarkerGroup>();
    internal readonly List<ExtraMarker> Extra=new List<ExtraMarker>();
    internal readonly Dictionary<uint,uint> ClonedSeats=new Dictionary<uint,uint>();
}
sealed class CameraAllocation {
    internal ulong Address, Header;
    internal byte[] Before;
    internal bool Linked;
}

static class CacheFiles {
    internal static bool SameIdentity(byte[] a,byte[] b) {
        return Native.Z(a,0,4)=="daeh"&&Native.Z(b,0,4)=="daeh"&&Native.U32(a,4)==13&&Native.U32(b,4)==13
            &&Native.Z(a,0xc0,32)==Native.Z(b,0xc0,32)&&Native.Z(a,0xe0,256)==Native.Z(b,0xe0,256)
            &&Native.U32(a,0x30)==Native.U32(b,0x30)&&Native.U32(a,0x40)==Native.U32(b,0x40)&&Native.U64(a,0x50)==Native.U64(b,0x50);
    }
    static IEnumerable<string> Roots(string maps) {
        var steamapps=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for(var d=new DirectoryInfo(maps);d!=null;d=d.Parent)if(d.Name.Equals("steamapps",StringComparison.OrdinalIgnoreCase))steamapps.Add(d.FullName);
        using(var key=Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam")) {
            var path=key==null?null:key.GetValue("SteamPath") as string;
            if(!string.IsNullOrEmpty(path)&&Path.IsPathRooted(path))steamapps.Add(Path.Combine(path,"steamapps"));
        }
        foreach(string folder in steamapps.ToArray()) {
            string vdf=Path.Combine(folder,"libraryfolders.vdf");if(!File.Exists(vdf))continue;
            foreach(Match m in Regex.Matches(File.ReadAllText(vdf),"\"path\"\\s+\"([^\"]+)\"")) {
                string path=m.Groups[1].Value.Replace(@"\\",@"\");
                if(Path.IsPathRooted(path))steamapps.Add(Path.Combine(path,"steamapps"));
            }
        }
        foreach(string folder in steamapps)yield return Path.Combine(folder,@"workshop\content\976730");
        yield return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),@"My Games\Project Reclaimer\Mods");
        string profile=Environment.GetEnvironmentVariable("RECLAIMER_PROFILE_DIR");
        if(!string.IsNullOrEmpty(profile)&&Path.IsPathRooted(profile))yield return Path.Combine(profile,"Mods");
        yield return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),@"ProjectReclaimer\forge-maps\virtual");
    }
    internal static string Find(string maps,byte[] header) {
        string stem=Native.Z(header,0xc0,32);
        if(stem.IndexOfAny(Path.GetInvalidFileNameChars())>=0)throw new Exception("Invalid map name.");
        Func<string,bool> matches=p=>{
            try {using(var f=new FileStream(p,FileMode.Open,FileAccess.Read,FileShare.ReadWrite)) {
                var h=new byte[0x4000];return f.Read(h,0,h.Length)==h.Length&&SameIdentity(h,header);
            }}catch{return false;}
        };
        string stock=Path.Combine(maps,stem+".map");if(matches(stock))return stock;
        foreach(string root in Roots(maps)) {
            if(!Directory.Exists(root))continue;
            // Try the actual cache name first, then aliases. Virtual placeholders are safely rejected.
            foreach(string pattern in new[]{stem+".map","*.map"}) {
                string[] files;try{files=Directory.GetFiles(root,pattern,SearchOption.AllDirectories);}catch(IOException){continue;}catch(UnauthorizedAccessException){continue;}
                foreach(string file in files)if(matches(file))return file;
            }
        }
        throw new Exception("No matching map cache for "+stem+". Install its Workshop mod and retry.");
    }
}

sealed class CameraSession : IDisposable {
    const string EngineHash="b209d8454b12dc77e54ccd2c9924ec8d44b8619d21cf98e36ffaf601e67efb63";
    const ulong HeaderRva=0x20A9058, HeapRva=0x1FCF4C8, TableRva=0xA49018, ScenarioRva=0xA46A08;
    internal readonly Process Process;
    internal readonly ulong Base;
    internal readonly string Maps;
    readonly IntPtr Handle;
    readonly Action<string> Log;
    readonly RemoteCameraData ExtraData;
    readonly List<CameraAllocation> Allocations=new List<CameraAllocation>();
    bool Approximate;
    Context Active;
    readonly List<Edit> Applied=new List<Edit>();
    readonly Dictionary<uint,HashSet<int>> BoostDrivers=new Dictionary<uint,HashSet<int>>();
    string PendingKey;
    internal int SeatCount;
    internal bool IsBoostDriver(uint vehicleTag,int seat) {
        HashSet<int> seats;return Active!=null&&BoostDrivers.TryGetValue(vehicleTag,out seats)&&seats.Contains(seat)&&Same(Active);
    }
    void RegisterBoostDriver(uint vehicleTag,int seat) {
        HashSet<int> seats;
        if(!BoostDrivers.TryGetValue(vehicleTag,out seats))BoostDrivers[vehicleTag]=seats=new HashSet<int>();
        seats.Add(seat);
    }

    internal CameraSession(Process p, Action<string> log) {
        Process=p;Log=log;
        if(p.MainWindowHandle==IntPtr.Zero || !p.MainWindowTitle.StartsWith("Project Reclaimer"))throw new Exception("A visible Reclaimer game client is required.");
        if(p.ProcessName.IndexOf("reclaimer",StringComparison.OrdinalIgnoreCase)<0)throw new Exception("A Reclaimer client is required.");
        var m=p.Modules.Cast<ProcessModule>().SingleOrDefault(x=>x.ModuleName.Equals("halo3.dll",StringComparison.OrdinalIgnoreCase));
        if(m==null)throw new Exception("Waiting for a Halo 3 match.");
        if(Native.Hash(m.FileName)!=EngineHash)throw new Exception("Halo 3 engine changed. This helper needs a compatibility update; no camera changes applied.");
        Base=unchecked((ulong)m.BaseAddress.ToInt64());Maps=Path.Combine(Path.GetDirectoryName(m.FileName),"maps");
        Handle=Native.OpenProcess(0x438,false,p.Id);
        if(Handle==IntPtr.Zero)throw new Exception("Cannot open the Reclaimer game client: "+Marshal.GetLastWin32Error());
        ExtraData=new RemoteCameraData(Handle);
        Log("Attached client PID "+p.Id+"; Halo 3 engine hash verified. Client SHA256 "+Native.Hash(p.MainModule.FileName));
    }
    internal byte[] Read(ulong address, int size) {
        if(!Native.Ptr(address)||size<=0||size>4*1024*1024)throw new Exception("Invalid camera data range.");
        var b=new byte[size];UIntPtr n;
        if(!Native.ReadProcessMemory(Handle,new IntPtr(unchecked((long)address)),b,(UIntPtr)size,out n)||n.ToUInt64()!=(ulong)size)throw new Exception("Map is loading or its camera data is unavailable.");
        return b;
    }
    ulong P(ulong a) { var p=Native.U64(Read(a,8));if(!Native.Ptr(p))throw new Exception("Waiting for loaded map data.");return p; }
    ulong Expand(Context c,uint compact) { var a=unchecked(c.Heap+(ulong)compact*4);if(!Native.Ptr(a))throw new Exception("Invalid compact tag pointer.");return a; }
    internal Context Snapshot() {
        var h=Read(Base+HeaderRva,0x4000);
        if(Native.Z(h,0,4)!="daeh"||Native.U32(h,4)!=13||Native.Z(h,0x3ffc,4)!="toof")throw new Exception("Waiting for an MCC Halo 3 map.");
        var c=new Context {Header=h,Heap=P(Base+HeapRva),Table=P(Base+TableRva),Scenario=P(Base+ScenarioRva),Index=Native.U64(h,0x2e8),Name=Native.Z(h,0xe0,256)};
        var index=Read(c.Index,0x20);uint groups=Native.U32(index);c.Count=checked((int)Native.U32(index,16));
        if(groups==0||groups>512||c.Count<=0||c.Count>65535||Native.U64(index,24)!=c.Table)throw new Exception("Tag tables are changing.");
        c.Classes=Read(Native.U64(index,8),checked((int)groups*16));c.Tags=Read(c.Table,c.Count*8);
        // Reclaimer can replace its scenario and table after entry; camera tags stay in their heap.
        c.Key=string.Join(":",new object[]{c.Index,c.Heap,c.Table,c.Count,c.Name,Native.U64(h,0x50),Native.U32(h,0x30)});
        uint fingerprint=2166136261;
        foreach(byte value in c.Tags)fingerprint=unchecked((fingerprint^value)*16777619);
        c.Key+=":"+fingerprint;
        return c;
    }
    internal bool Same(Context c) {
        try {return P(Base+HeapRva)==c.Heap&&Native.U64(Read(Base+HeaderRva+0x2e8,8))==c.Index&&CacheFiles.SameIdentity(Read(Base+HeaderRva,0x4000),c.Header);}
        catch {return false;}
    }
    internal string Group(Context c,int i) {
        int g=BitConverter.ToUInt16(c.Tags,i*8);if(g*16+4>c.Classes.Length)return "";
        return new string(Encoding.ASCII.GetString(c.Classes,g*16,4).Reverse().ToArray());
    }
    internal ulong Tag(Context c,uint datum,string group) {
        int i=(int)(datum&65535);
        if(i>=c.Count||BitConverter.ToUInt16(c.Tags,i*8+2)!=(datum>>16)||Group(c,i)!=group)throw new Exception("Unexpected camera model reference.");
        return Expand(c,Native.U32(c.Tags,i*8+4));
    }
    internal ulong Block(Context c,ulong address,int maximum,out int count) {
        var b=Read(address,8);count=checked((int)Native.U32(b));
        if(count<0||count>maximum)throw new Exception("Unexpected camera block size.");
        return count==0?0:Expand(c,Native.U32(b,4));
    }
    internal Dictionary<uint,string> Strings(Context c) {
        string path=CacheFiles.Find(Maps,c.Header);Log("Map strings: "+path);
        using(var f=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.ReadWrite)) {
            Func<long,int,byte[]> get=(o,n)=>{if(o<0||n<0||n>8*1024*1024||o+n>f.Length)throw new Exception("Invalid map string table.");f.Position=o;var b=new byte[n];int used=0;while(used<n){int k=f.Read(b,used,n-used);if(k==0)throw new EndOfStreamException();used+=k;}return b;};
            var h=get(0,0x4000);
            if(Native.U32(h,4)!=13||Native.Z(h,0xe0,256)!=c.Name)throw new Exception("Loaded map differs from its string table.");
            uint mask=Native.U32(h,0x4cc);
            Func<int,long> off=o=>unchecked((uint)(Native.U32(h,o)+mask));
            int nsCount=checked((int)Native.U32(h,0x40));if(nsCount<1||nsCount>32)throw new Exception("Invalid string namespaces.");
            var namespaces=get(off(0x44),nsCount*4);uint[] sizes=new uint[nsCount];for(int j=0;j<nsCount;j++)sizes[j]=Native.U32(namespaces,j*4);
            uint total=0;foreach(uint z in sizes)total+=z;
            int count=checked((int)Native.U32(h,0x30));if(count<1||count>1000000)throw new Exception("Invalid string count.");
            var data=get(off(0x34),checked((int)Native.U32(h,0x38)));var offsets=get(off(0x3c),count*4);var answer=new Dictionary<uint,string>();
            for(uint i=0;i<count;i++) {
                uint o=Native.U32(offsets,(int)i*4);if(o>=data.Length)continue;int end=Array.IndexOf(data,(byte)0,(int)o);if(end<0)continue;
                uint sid=i;
                if(i>=total)sid=i-(total-sizes[0]);
                else if(i>=sizes[0]) {uint start=sizes[0];for(uint j=1;j<sizes.Length;j++){if(i<start+sizes[j]){sid=(j<<16)|(i-start);break;}start+=sizes[j];}}
                answer[sid]=Encoding.ASCII.GetString(data,(int)o,end-(int)o);
            }
            return answer;
        }
    }
    ModelMarkers Markers(Context c,ulong vehicle,uint datum,Dictionary<ulong,ModelMarkers> cache) {
        ulong model=Tag(c,Native.U32(Read(vehicle+0x40,4)),"hlmt");
        ulong render=Tag(c,Native.U32(Read(model+12,4)),"mode");
        ModelMarkers found;if(cache.TryGetValue(render,out found))return found;
        int count,nodes;ulong groups=Block(c,render+0x3c,2048,out count),nodeData=Block(c,render+0x30,255,out nodes);
        found=new ModelMarkers {Header=render+0x3c,Count=count,Nodes=nodes,NodeData=nodeData,Data=count==0?new byte[0]:Read(groups,count*16),Owner=vehicle,Datum=datum};
        for(int j=0;j<count;j++) {
            ulong g=groups+(ulong)j*16;int n;ulong pointer=Block(c,g+4,256,out n);
            if(n>0)found.Groups[Native.U32(Read(g,4))]=new MarkerGroup {Address=pointer,Count=n};
        }
        cache.Add(render,found);return found;
    }
    bool UsesSabreChaseMarker(ModelMarkers model,uint attachment,uint marker,Dictionary<uint,string> names) {
        Func<uint,string> name=id=>names.ContainsKey(id)?names[id]:"";
        if(name(attachment)!="sabre_d"||name(marker)!="sabre_d_camera"||model.Nodes<=4)return false;
        MarkerGroup seat,camera;
        if(!model.Groups.TryGetValue(attachment,out seat)||!model.Groups.TryGetValue(marker,out camera)||seat.Count!=1||camera.Count!=1)return false;
        var source=Read(seat.Address,0x24);var chase=Read(camera.Address,0x24);
        if(source[2]!=4||chase[2]!=3||source[0]!=255||source[1]!=255||chase[0]!=255||chase[1]!=255)return false;
        var cameraNode=Read(model.NodeData+3*0x60,0x60);var hullNode=Read(model.NodeData+4*0x60,0x60);
        if(name(Native.U32(cameraNode))!="camera"||name(Native.U32(hullNode))!="hull"
            ||BitConverter.ToInt16(cameraNode,4)!=0||BitConverter.ToInt16(hullNode,4)!=0)return false;
        Func<byte[],int,float,float,float,bool> near=(b,o,x,y,z)=>Math.Abs(BitConverter.ToSingle(b,o)-x)<0.001f
            &&Math.Abs(BitConverter.ToSingle(b,o+4)-y)<0.001f&&Math.Abs(BitConverter.ToSingle(b,o+8)-z)<0.001f;
        // This authored "camera" is a chase anchor behind the fighter, not its cockpit.
        return near(chase,4,0,0,0)&&near(cameraNode,12,-12.129789f,0,4.1f)
            &&near(source,4,1.817676f,0,1.093578f)&&near(hullNode,12,0,0,0);
    }
    uint AddCamera(ModelMarkers model,uint sourceKey,uint labelId,Dictionary<uint,string> names,byte[] data) {
        uint existing;if(model.ClonedSeats.TryGetValue(sourceKey,out existing))return existing;
        Func<uint,bool> unused=candidate=>candidate!=0&&candidate!=uint.MaxValue&&!model.Groups.ContainsKey(candidate)&&!model.Extra.Any(x=>x.Id==candidate);
        uint id=unused(labelId)?labelId:names.Keys.Where(unused).DefaultIfEmpty(uint.MaxValue).First();
        if(id==uint.MaxValue)return id;
        model.Extra.Add(new ExtraMarker {Id=id,Data=data});model.ClonedSeats.Add(sourceKey,id);return id;
    }
    uint ScopeCamera(ModelMarkers model,string label,string attachment,uint marker,uint labelId,Dictionary<uint,string> names) {
        Func<uint,string> name=id=>names.ContainsKey(id)?names[id]:"";
        if(label!="warthog_g"||attachment!="gunner"||name(marker)!="camera_gunner"||model.Nodes!=6)return uint.MaxValue;
        Func<string,byte[]> record=text=>{
            var found=model.Groups.Where(x=>name(x.Key)==text&&x.Value.Count==1).ToArray();
            return found.Length==1?Read(found[0].Value.Address,0x24):null;
        };
        var nodes=Enumerable.Range(0,model.Nodes).Select(n=>Read(model.NodeData+(ulong)n*0x60,0x60)).ToArray();
        byte[] result;
        if(!GaussScopeCamera.TryCreate(label,attachment,name(marker),record("camera_gunner"),record("fx_display_crate"),record("gunner"),
            nodes.Select(n=>name(Native.U32(n))).ToArray(),nodes,out result))return uint.MaxValue;
        return AddCamera(model,marker,labelId,names,result);
    }
    uint HeadCamera(Context c,ModelMarkers model,uint seatMarker,uint labelId,Dictionary<uint,string> names,float forwardOffset=0) {
        uint existing;if(model.ClonedSeats.TryGetValue(seatMarker,out existing))return existing;
        MarkerGroup attach;if(seatMarker==0||!model.Groups.TryGetValue(seatMarker,out attach))return uint.MaxValue;
        var data=Read(attach.Address,attach.Count*0x24);
        for(int n=0;n<attach.Count;n++) {
            int at=n*0x24;
            if(data[at+2]>=model.Nodes)return uint.MaxValue;
            for(int k=4;k<0x24;k+=4) {float v=BitConverter.ToSingle(data,at+k);if(float.IsNaN(v)||float.IsInfinity(v))return uint.MaxValue;}
            float z=BitConverter.ToSingle(data,at+12);
            float x=BitConverter.ToSingle(data,at+4);
            // Approximate seated head height, in the original attachment's node frame.
            Buffer.BlockCopy(BitConverter.GetBytes(x+forwardOffset),0,data,at+4,4);
            Buffer.BlockCopy(BitConverter.GetBytes(z+0.25f),0,data,at+12,4);
        }
        return AddCamera(model,seatMarker,labelId,names,data);
    }
    void ExtendMarkers(Context c,ModelMarkers model,List<Edit> planned,Dictionary<uint,string> names) {
        if(model.Extra.Count==0)return;
        int groupsSize=checked((model.Count+model.Extra.Count)*16);
        int bytes=checked(groupsSize+model.Extra.Sum(x=>x.Data.Length));
        ulong pointer=ExtraData.Reserve(c.Heap,bytes);
        var allocation=new CameraAllocation {Address=pointer,Header=model.Header,Before=Read(model.Header,8)};Allocations.Add(allocation);
        var data=new byte[bytes];Buffer.BlockCopy(model.Data,0,data,0,model.Data.Length);int offset=groupsSize;
        for(int i=0;i<model.Extra.Count;i++) {
            var marker=model.Extra[i];int group=(model.Count+i)*16;
            Buffer.BlockCopy(BitConverter.GetBytes(marker.Id),0,data,group,4);
            Buffer.BlockCopy(BitConverter.GetBytes(marker.Data.Length/0x24),0,data,group+4,4);
            Buffer.BlockCopy(BitConverter.GetBytes(checked((uint)((pointer+(ulong)offset-c.Heap)/4))),0,data,group+8,4);
            Buffer.BlockCopy(marker.Data,0,data,offset,marker.Data.Length);offset+=marker.Data.Length;
        }
        // Native marker groups use name order. Keep that order when the cache supplies all names.
        var records=Enumerable.Range(0,model.Count+model.Extra.Count).Select(i=>data.Skip(i*16).Take(16).ToArray()).ToArray();
        if(records.All(row=>names.ContainsKey(Native.U32(row)))) {
            records=records.OrderBy(row=>names[Native.U32(row)],StringComparer.Ordinal).ToArray();
            for(int i=0;i<records.Length;i++)Buffer.BlockCopy(records[i],0,data,i*16,16);
        }
        ExtraData.Write(pointer,data);
        var after=BitConverter.GetBytes(model.Count+model.Extra.Count).Concat(BitConverter.GetBytes(checked((uint)((pointer-c.Heap)/4)))).ToArray();
        Plan(c,planned,model.Datum,model.Owner,model.Header,after,"vehicle camera marker groups");
        Log("DATA "+model.Extra.Count+" approximate head cameras @"+pointer.ToString("X")+"; "+bytes+" bytes verified");
    }
    void Plan(Context c,List<Edit> list,uint datum,ulong owner,ulong address,byte[] after,string label) {
        var before=Read(address,after.Length);if(before.SequenceEqual(after))return;
        if(list.Any(x=>x.Address==address)||Applied.Any(x=>x.Address==address))return;
        list.Add(new Edit {Datum=datum,Owner=owner,Address=address,Before=before,After=after,Label=label,Row=c.Tags.Skip((int)(datum&65535)*8).Take(8).ToArray()});
    }
    void StabilizeCamera(Context c,List<Edit> list,uint datum,ulong vehicle,ulong seat,string label) {
        try {
        int count;ulong block=Block(c,seat+0x98,16,out count);
        if(count==0)return;
        var data=Read(block,count*0x4c);
        for(int offset=0;offset<data.Length;offset+=4) {
            float value=BitConverter.ToSingle(data,offset);
            if(float.IsNaN(value)||float.IsInfinity(value)||Math.Abs(value)>100000) {
                Log("SKIP "+label+" motion scaling: unexpected camera acceleration data");return;
            }
        }
        // Preserve the native block/count and curve shape. Only suppress the
        // axial/perpendicular camera displacement multipliers, never physics.
        for(int row=0;row<count;row++)foreach(int offset in new[]{0x10,0x28,0x40})
            Plan(c,list,datum,vehicle,block+(ulong)(row*0x4c+offset),new byte[8],label+" camera displacement");
        }catch(Exception error){Log("SKIP "+label+" motion scaling: "+error.Message);}
    }
    bool OwnerValid(Context c,Edit e) {
        if(!Same(c))return false;
        ulong table=P(Base+TableRva);
        return table==P(c.Index+24)&&(e.Datum&65535)<Native.U32(Read(c.Index+16,4))
            &&Read(table+(ulong)(e.Datum&65535)*8,8).SequenceEqual(e.Row)&&Tag(c,e.Datum,"vehi")==e.Owner;
    }
    void WriteChecked(Context c,Edit e,bool restore) {
        byte[] expected=restore?e.After:e.Before, replacement=restore?e.Before:e.After;
        if(!OwnerValid(c,e)||!Read(e.Address,expected.Length).SequenceEqual(expected))throw new Exception("Camera changed during edit; stopped.");
        UIntPtr n;
        if(!Native.WriteProcessMemory(Handle,new IntPtr(unchecked((long)e.Address)),replacement,(UIntPtr)replacement.Length,out n)||n.ToUInt64()!=(ulong)replacement.Length)throw new Exception("Camera data write failed: "+Marshal.GetLastWin32Error());
        if(!Read(e.Address,replacement.Length).SequenceEqual(replacement))throw new Exception("Camera readback failed.");
        Log((restore?"RESTORE ":"APPLY ")+e.Label+" @"+e.Address.ToString("X")+" "+BitConverter.ToString(expected)+" -> "+BitConverter.ToString(replacement));
    }
    internal string Tick(bool enabled,bool approximate) {
        if(Process.HasExited)throw new Exception("Game closed.");
        if(!enabled){Restore();return "OFF - original cameras restored.";}
        if(Approximate!=approximate){Restore();Approximate=approximate;}
        var c=Snapshot();
        if(Active!=null&&Active.Key==c.Key)return "ON - "+SeatCount+" vehicle seats | "+Path.GetFileName(c.Name);
        // Rebuild after tag-table changes so shared render models get one complete extension.
        if(Active!=null)Restore();
        string mapStem=Native.Z(c.Header,0xc0,32);
        if(mapStem=="130_epilogue"||mapStem=="mainmenu")return "Waiting for a game. Join a server normally.";
        if(PendingKey!=c.Key){PendingKey=c.Key;return "Waiting for the map to settle...";}
        BoostDrivers.Clear();
        var names=new Dictionary<uint,string>();
        try {names=Strings(c);}catch(Exception e){Log("Names unavailable; using configured marker IDs: "+e.Message);}
        Func<uint,string> name=id=>names.ContainsKey(id)?names[id]:"";
        var planned=new List<Edit>();var models=new Dictionary<ulong,ModelMarkers>();int supported=0,skipped=0,native=0,heads=0;
        for(int i=0;i<c.Count;i++) {
            if(Group(c,i)!="vehi")continue;
            uint datum=((uint)BitConverter.ToUInt16(c.Tags,i*8+2)<<16)|(uint)i;ulong vehicle=Tag(c,datum,"vehi");
            try {
                int count;ulong seats=Block(c,vehicle+0x258,64,out count);
                if(count==0)continue;
                var model=Markers(c,vehicle,datum,models);
                // Native Unit Has Boost flag and positive power identify stock
                // and modded boost vehicles without map names or model lists.
                bool hasBoost=VehicleBoostZoom.HasBoost(Native.U32(Read(vehicle+0xf8,4)),BitConverter.ToSingle(Read(vehicle+0x288,4),0));
                for(int j=0;j<count;j++) {
                    ulong seat=seats+(ulong)j*0xd4;var data=Read(seat,0xd4);uint flags=Native.U32(data),labelId=Native.U32(data,4),attachment=Native.U32(data,8);
                    int ai=BitConverter.ToInt16(data,0x40);
                    if((flags&((1u<<11)|(1u<<16)))!=0||ai==3||ai==4)continue;
                    bool boostDriver=hasBoost&&VehicleBoostZoom.IsPlayerDriver(flags,ai);
                    if((flags&16u)==0){if(boostDriver)RegisterBoostDriver(datum,j);supported++;native++;continue;}
                    string label=name(labelId),attachName=name(attachment);uint current=Native.U32(data,0x6c),marker=uint.MaxValue;
                    string configured=name(current);
                    bool cameraName=configured.IndexOf("camera",StringComparison.OrdinalIgnoreCase)>=0||configured.EndsWith("_cam",StringComparison.OrdinalIgnoreCase);
                    if(current!=0&&model.Groups.ContainsKey(current)&&current!=attachment&&(configured==""||cameraName))marker=current;
                    if(marker==uint.MaxValue) {
                        var candidates=new List<string>();
                        foreach(string stem in new[]{attachName,label})if(stem!="")candidates.AddRange(new[]{stem+"_camera",stem+"_cam","camera_"+stem});
                        if((flags&4u)!=0)candidates.AddRange(new[]{"camera_driver","driver_camera","driver_cam"});
                        else if((flags&((1u<<3)|(1u<<18)))!=0)candidates.AddRange(new[]{"camera_gunner","gunner_camera","gunner_cam"});
                        else candidates.AddRange(new[]{"camera_passenger","passenger_camera","camera_rider"});
                        marker=model.Groups.Keys.Where(id=>candidates.Contains(name(id))).DefaultIfEmpty(uint.MaxValue).First();
                    }
                    bool head=false,sabre=false;string profile=null;
                    MarkerGroup pilotSource,pilotCamera;
                    if(model.Groups.TryGetValue(attachment,out pilotSource)&&pilotSource.Count==1
                        &&model.Groups.TryGetValue(marker,out pilotCamera)&&pilotCamera.Count==1) {
                        float forwardOffset;string selected;
                        if(VehicleCameraProfiles.TryPilot(attachName,name(marker),unchecked((int)flags),
                            Read(pilotSource.Address,0x24),Read(pilotCamera.Address,0x24),
                            node=>node>=0&&node<model.Nodes?Read(model.NodeData+(ulong)node*0x60,0x60):null,
                            name,out forwardOffset,out selected)) {
                            marker=HeadCamera(c,model,attachment,labelId,names,forwardOffset);
                            head=marker!=uint.MaxValue;if(head)profile=selected;
                        }
                    }
                    if((flags&4u)!=0&&UsesSabreChaseMarker(model,attachment,marker,names)) {
                        // Correct this known chase anchor even when generic approximate cameras are off.
                        marker=HeadCamera(c,model,attachment,labelId,names);head=sabre=marker!=uint.MaxValue;
                    }
                    uint scope=ScopeCamera(model,label,attachName,marker,labelId,names);
                    if(scope!=uint.MaxValue){marker=scope;profile="Gauss native scope screen";}
                    if(marker==uint.MaxValue&&approximate){marker=HeadCamera(c,model,attachment,labelId,names);head=marker!=uint.MaxValue;}
                    if(marker==uint.MaxValue){skipped++;Log("SKIP vehicle "+i+" seat "+j+" "+label+": no usable camera/attachment marker");continue;}
                    string editLabel="vehicle "+i+" seat "+j+" "+label;
                    Plan(c,planned,datum,vehicle,seat+0x6c,BitConverter.GetBytes(marker),editLabel+" camera marker");
                    // Preserve the existing, geometry-guarded chaingun camera adjustment.
                    MarkerGroup mg;
                    if(label=="warthog_g"&&!head&&model.Groups.TryGetValue(marker,out mg)&&mg.Count==1) {
                        var mr=Read(mg.Address,0x24);
                        if(mr[2]==3&&Math.Abs(BitConverter.ToSingle(mr,4)+0.003213291f)<0.0001f&&Math.Abs(BitConverter.ToSingle(mr,12)+0.005836391f)<0.0001f)
                            Plan(c,planned,datum,vehicle,mg.Address+4,BitConverter.GetBytes(-0.32f).Concat(BitConverter.GetBytes(0f)).Concat(BitConverter.GetBytes(0.23f)).ToArray(),editLabel+" chaingun head position");
                    }
                    Plan(c,planned,datum,vehicle,seat+0x80,BitConverter.GetBytes(0),editLabel+" camera tracks");
                    StabilizeCamera(c,planned,datum,vehicle,seat,editLabel);
                    Plan(c,planned,datum,vehicle,seat,BitConverter.GetBytes(flags&~(16u|128u)),editLabel+" camera flags");
                    // Apply only after this driver's first-person camera is supported.
                    if(boostDriver)RegisterBoostDriver(datum,j);
                    Log("SEAT vehicle "+i+" seat "+j+" "+label+": "+(profile??(sabre?"Sabre pilot head (chase camera corrected)":head?"approximate seat head":name(marker)==""?"configured camera ID":name(marker))));
                    supported++;if(head)heads++;
                }
            } catch(Exception e){Log("SKIP vehicle "+i+": "+e.Message);}
        }
        if(!Same(c))throw new Exception("Map changed; waiting.");
        Active=c;SeatCount=supported;
        try {
            foreach(var model in models.Values)ExtendMarkers(c,model,planned,names);
            // Publish marker blocks before seat references. Restore reverses this order.
            foreach(var e in planned.OrderBy(x=>x.Label=="vehicle camera marker groups"?0:1)) {
                Applied.Add(e);
                // Treat a failed/partial pointer write as published until restoration proves otherwise.
                foreach(var allocation in Allocations.Where(x=>x.Header==e.Address))allocation.Linked=true;
                WriteChecked(c,e,false);
            }
        } catch {Restore();throw;}
        Log("READY "+c.Name+": "+supported+" seats ("+native+" already first person, "+heads+" approximate), "+skipped+" skipped, "+Applied.Count+" fields; all writes read back.");
        return supported==0?"No usable player vehicle cameras found.":"ON - "+supported+" vehicle seats ("+heads+" approximate) | "+Path.GetFileName(c.Name);
    }
    internal void Restore() {
        BoostDrivers.Clear();
        if(Active==null)return;
        foreach(var e in Applied.AsEnumerable().Reverse()) {
            try {if(OwnerValid(Active,e)&&Read(e.Address,e.After.Length).SequenceEqual(e.After))WriteChecked(Active,e,true);}
            catch(Exception ex){Log("Restore skipped: "+ex.Message);}
        }
        foreach(var allocation in Allocations) {
            try {
                if(!allocation.Linked||(Same(Active)&&Read(allocation.Header,8).SequenceEqual(allocation.Before)))ExtraData.Release(allocation.Address);
                else Log("Camera data retained until game exit because its marker reference could not be confirmed restored.");
            }catch(Exception e){Log("Camera data retained until game exit: "+e.Message);}
        }
        Allocations.Clear();Applied.Clear();Active=null;SeatCount=0;
    }
    public void Dispose(){Restore();ExtraData.Dispose();Native.CloseHandle(Handle);Process.Dispose();}
}

static class CameraHotkey {
    internal static string Format(Keys binding) {return new KeysConverter().ConvertToString(binding);}
    internal static Keys Validate(Keys binding) {
        Keys key=binding&Keys.KeyCode;
        if((binding&~(Keys.KeyCode|Keys.Control|Keys.Alt|Keys.Shift))!=0||key==Keys.None||key==Keys.ControlKey||key==Keys.ShiftKey||key==Keys.Menu||key==Keys.LWin||key==Keys.RWin||key==Keys.F12)
            throw new Exception("Choose F1-F11 or a shortcut with Ctrl, Alt or Shift. F12/Windows keys are reserved.");
        if((binding&Keys.Modifiers)==Keys.None&&(key<Keys.F1||key>Keys.F11))throw new Exception("Use Ctrl, Alt or Shift with ordinary keys; F8 is the default.");
        return binding;
    }
    internal static Keys Parse(string text) {return Validate((Keys)new KeysConverter().ConvertFromString(text));}
    internal static uint Modifiers(Keys binding) {return 0x4000u|((binding&Keys.Alt)!=0?1u:0u)|((binding&Keys.Control)!=0?2u:0u)|((binding&Keys.Shift)!=0?4u:0u);}
}

sealed class CameraWindow : Form {
    [DllImport("user32.dll",SetLastError=true)] static extern bool RegisterHotKey(IntPtr window,int id,uint modifiers,uint key);
    [DllImport("user32.dll")] static extern bool UnregisterHotKey(IntPtr window,int id);
    [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr window,out uint process);
    const int HotkeyId=0x481,RadioHotkeyId=0x482,StationHotkeyId=0x483;
    readonly Label Status=new Label(),KeyStatus=new Label(),AudioStatus=new Label();
    readonly CheckBox EnabledBox=new CheckBox(),ApproximateBox=new CheckBox(),EngineBox=new CheckBox(),RadioBox=new CheckBox();
    readonly TextBox HotkeyBox=new TextBox();
    readonly Timer Poll=new Timer(),KeyPoll=new Timer(),AudioPoll=new Timer();
    readonly string SettingsPath=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"camera-settings.ini");
    Keys Binding=Keys.F8,PendingBinding=Keys.F8;
    bool HotkeyRegistered,RegistrationAttempted,IsClosing,RadioKeyRegistered,StationKeyRegistered;
    CameraSession Session;
    CockpitMotionBlur MotionBlur;
    VehicleBoostZoom BoostZoom;
    VehicleAudioTelemetry Telemetry;
    WarthogEngineMute EngineMute;
    EngineLoopMixer EngineAudio;
    WarthogRadio Radio;
    string AudioError="",LastAudio="";
    DateTime LastAudioLog;
    string Last="";
    readonly string LogPath;
    readonly int RequestedPid;
    readonly DateTime Deadline;
    internal CameraWindow(string[] args) {
        Text="Vehicle Cockpit + Warthog Audio 0.4.6";ClientSize=new Size(600,465);FormBorderStyle=FormBorderStyle.FixedDialog;MaximizeBox=false;
        var folder=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"logs");Directory.CreateDirectory(folder);LogPath=Path.Combine(folder,"camera-"+DateTime.Now.ToString("yyyyMMdd-HHmmss")+".log");
        for(int i=0;i<args.Length-1;i++){if(args[i]=="--pid")RequestedPid=int.Parse(args[i+1]);if(args[i]=="--seconds")Deadline=DateTime.UtcNow.AddSeconds(int.Parse(args[i+1]));}
        try {
            if(File.Exists(SettingsPath)) {
                string value=File.ReadAllLines(SettingsPath).FirstOrDefault(x=>x.StartsWith("toggle_key=",StringComparison.OrdinalIgnoreCase));
                if(value!=null)Binding=CameraHotkey.Parse(value.Substring(11).Trim());
            }
        }catch(Exception e){Log("Invalid shortcut setting; using F8: "+e.Message);}
        if(Binding==Keys.F9||Binding==Keys.F10){Binding=Keys.F8;Log("F9/F10 are now radio keys; camera shortcut uses F8.");}
        PendingBinding=Binding;
        EnabledBox.Text="First-person vehicle cameras";EnabledBox.Checked=true;EnabledBox.SetBounds(18,18,470,28);EnabledBox.Font=new Font("Segoe UI",11);Controls.Add(EnabledBox);
        ApproximateBox.Text="Approximate head camera when no cockpit camera exists";ApproximateBox.Checked=true;ApproximateBox.SetBounds(20,48,565,28);Controls.Add(ApproximateBox);
        Status.SetBounds(20,83,560,62);Status.Text="Waiting for a Reclaimer game...";Status.Font=new Font("Segoe UI",10);Controls.Add(Status);
        var keyLabel=new Label {Text="Toggle key:"};keyLabel.SetBounds(20,153,75,24);Controls.Add(keyLabel);
        HotkeyBox.ReadOnly=true;HotkeyBox.Text=CameraHotkey.Format(Binding);HotkeyBox.SetBounds(98,149,205,25);Controls.Add(HotkeyBox);
        var save=new Button {Text="Save key"};save.SetBounds(315,147,100,29);Controls.Add(save);
        KeyStatus.SetBounds(20,184,560,34);KeyStatus.Text="Click the key box, press a shortcut, then Save key.";Controls.Add(KeyStatus);
        var note=new Label {Text="Toggle while the game is focused. Leave the helper open.\nUntick or close it to restore the original cameras."};note.SetBounds(20,232,560,48);Controls.Add(note);
        EngineBox.Text="Warthog engine recording (native RPM)";EngineBox.Checked=true;EngineBox.SetBounds(20,283,560,25);Controls.Add(EngineBox);
        RadioBox.Text="Radio on / off (F9)";RadioBox.SetBounds(20,315,260,25);Controls.Add(RadioBox);
        var next=new Button {Text="Next station (F10)"};next.SetBounds(315,312,180,29);Controls.Add(next);next.Click+=(s,e)=>NextStation();
        AudioStatus.SetBounds(20,351,560,96);AudioStatus.Text="Audio waits for a supported Warthog. Radio starts off.";Controls.Add(AudioStatus);
        EngineBox.CheckedChanged+=(s,e)=>UpdateAudio();
        RadioBox.CheckedChanged+=(s,e)=>{if(Radio!=null)Radio.Enabled=RadioBox.Checked;Log("RADIO "+(RadioBox.Checked?"ON":"OFF"));UpdateAudio();};
        HotkeyBox.Enter+=(s,e)=>{ReleaseHotkey();RegistrationAttempted=false;};
        HotkeyBox.Leave+=(s,e)=>{RegistrationAttempted=false;};
        HotkeyBox.KeyDown+=(s,e)=>{
            e.SuppressKeyPress=true;e.Handled=true;
            try {PendingBinding=CameraHotkey.Validate(e.KeyData);HotkeyBox.Text=CameraHotkey.Format(PendingBinding);KeyStatus.Text="Press Save key to keep "+HotkeyBox.Text+".";}
            catch(Exception error){KeyStatus.Text=error.Message;}
        };
        save.Click+=(s,e)=>SaveHotkey();
        EnabledBox.CheckedChanged+=(s,e)=>UpdateSession();ApproximateBox.CheckedChanged+=(s,e)=>UpdateSession();
        Poll.Interval=750;Poll.Tick+=(s,e)=>UpdateSession();Poll.Start();
        KeyPoll.Interval=120;KeyPoll.Tick+=(s,e)=>SyncHotkey();KeyPoll.Start();
        AudioPoll.Interval=50;AudioPoll.Tick+=(s,e)=>UpdateAudio();AudioPoll.Start();
        FormClosing+=(s,e)=>{IsClosing=true;Poll.Stop();KeyPoll.Stop();AudioPoll.Stop();ReleaseHotkey();CloseSession();if(EngineAudio!=null)EngineAudio.Dispose();if(Radio!=null)Radio.Dispose();};
        Log("Helper 0.4.6 Vehicle boost zoom correction. Verified MCC Halo 3 1.3528.0.0. Camera "+CameraHotkey.Format(Binding)+"; radio F9; station F10.");
    }
    bool AllowedForeground() {
        uint process;GetWindowThreadProcessId(GetForegroundWindow(),out process);
        return process==(uint)Process.GetCurrentProcess().Id||(Session!=null&&process==(uint)Session.Process.Id);
    }
    void ReleaseHotkey(){if(HotkeyRegistered){UnregisterHotKey(Handle,HotkeyId);HotkeyRegistered=false;}if(RadioKeyRegistered){UnregisterHotKey(Handle,RadioHotkeyId);RadioKeyRegistered=false;}if(StationKeyRegistered){UnregisterHotKey(Handle,StationHotkeyId);StationKeyRegistered=false;}}
    bool RegisterBinding(Keys binding){return RegisterHotKey(Handle,HotkeyId,CameraHotkey.Modifiers(binding),(uint)(binding&Keys.KeyCode));}
    void SyncHotkey() {
        if(IsClosing)return;
        if(HotkeyBox.Focused||!AllowedForeground()){ReleaseHotkey();RegistrationAttempted=false;return;}
        if(!RadioKeyRegistered)RadioKeyRegistered=RegisterHotKey(Handle,RadioHotkeyId,0x4000,(uint)Keys.F9);
        if(!StationKeyRegistered)StationKeyRegistered=RegisterHotKey(Handle,StationHotkeyId,0x4000,(uint)Keys.F10);
        if(HotkeyRegistered||RegistrationAttempted)return;
        RegistrationAttempted=true;HotkeyRegistered=RegisterBinding(Binding);
        if(!HotkeyRegistered){KeyStatus.Text="Toggle shortcut is in use. Choose another key; the checkbox still works.";Log("Shortcut registration failed: "+Marshal.GetLastWin32Error());}
    }
    void SaveHotkey() {
        if(PendingBinding==Keys.F9||PendingBinding==Keys.F10){KeyStatus.Text="F9 and F10 control the radio. Choose another camera shortcut.";return;}
        Keys old=Binding;ReleaseHotkey();RegistrationAttempted=true;
        if(!RegisterBinding(PendingBinding)) {
            int error=Marshal.GetLastWin32Error();HotkeyRegistered=RegisterBinding(old);
            KeyStatus.Text="Shortcut is unavailable; kept "+CameraHotkey.Format(old)+". Choose another key.";Log("Rebind refused: "+error);return;
        }
        HotkeyRegistered=true;
        try {
            File.WriteAllText(SettingsPath,"toggle_key="+CameraHotkey.Format(PendingBinding)+Environment.NewLine);
            Binding=PendingBinding;KeyStatus.Text="Saved "+CameraHotkey.Format(Binding)+". Toggles while the game is focused.";Log("Shortcut saved: "+CameraHotkey.Format(Binding));
        }catch(Exception e){ReleaseHotkey();HotkeyRegistered=RegisterBinding(old);KeyStatus.Text="Cannot save shortcut: "+e.Message;}
    }
    protected override void WndProc(ref Message message) {
        if(message.Msg==0x0312&&!HotkeyBox.Focused&&AllowedForeground()) {
            if(message.WParam.ToInt32()==RadioHotkeyId)RadioBox.Checked=!RadioBox.Checked;
            if(message.WParam.ToInt32()==StationHotkeyId)NextStation();
        }
        if(message.Msg==0x0312&&message.WParam.ToInt32()==HotkeyId&&!HotkeyBox.Focused&&AllowedForeground()) {
            Log("HOTKEY "+CameraHotkey.Format(Binding)+" -> "+(!EnabledBox.Checked?"ON":"OFF"));EnabledBox.Checked=!EnabledBox.Checked;
        }
        base.WndProc(ref message);
    }
    protected override void OnHandleDestroyed(EventArgs e){ReleaseHotkey();base.OnHandleDestroyed(e);}
    void Log(string text){File.AppendAllText(LogPath,DateTime.UtcNow.ToString("o")+" "+text+Environment.NewLine);}
    void ShowStatus(string text){Status.Text=text;if(text!=Last){Log(text);Last=text;}}
    void NextStation(){try{if(Radio!=null){Radio.Next();Log("RADIO STATION "+Radio.Station);}UpdateAudio();}catch(Exception e){AudioStatus.Text=e.Message;Log("RADIO "+e.Message);}}
    void CloseSession() {
        if(EngineAudio!=null)EngineAudio.Set(0,0);
        if(Radio!=null)try{Radio.Update(0);}catch{}
        Telemetry=null;
        if(BoostZoom!=null){BoostZoom.Dispose();BoostZoom=null;}
        if(MotionBlur!=null){MotionBlur.Dispose();MotionBlur=null;}
        if(EngineMute!=null){EngineMute.Dispose();EngineMute=null;}
        if(Session!=null){Session.Dispose();Session=null;}
    }
    void UpdateAudio() {
        if(IsClosing)return;
        try {
            if(Session==null||Telemetry==null||EngineMute==null) {
                if(BoostZoom!=null)BoostZoom.Tick(false);
                if(EngineAudio!=null)EngineAudio.Set(0,0);if(Radio!=null)Radio.Update(0);return;
            }
            var frame=Telemetry.ReadFrame();
            if(BoostZoom!=null)BoostZoom.Tick(EnabledBox.Checked&&frame.Valid&&Session.IsBoostDriver(frame.DirectVehicleTag,frame.DirectVehicleSeat));
            bool engine=EngineBox.Checked&&AudioError==""&&EngineAudio!=null&&EngineAudio.Error==null&&frame.Valid&&!frame.EngineFunctionsOverridden&&frame.NearbyEngines.Count>0&&frame.NearbyEngines.All(x=>x.HasNativeRpm&&EngineMute.CanMute(x.WarthogTagDatum));
            // Native caches share sound function blocks across Warthog variants.
            // Render every nearby running Warthog, not just the radio's closest one.
            EngineMute.SetEnabled(engine);
            if(EngineAudio!=null)EngineAudio.SetVoices(engine?frame.NearbyEngines.Where(x=>x.HasNativeRpm&&x.EngineRunning&&EngineMute.CanMute(x.WarthogTagDatum)).OrderBy(x=>x.Distance).Take(8).Select(x=>new EngineAudioVoice(x.WarthogDatum,x.EngineRpmNormalized,WarthogAudioLevels.EngineGain(frame.InWarthog&&frame.WarthogDatum==x.WarthogDatum,x.Distance)*x.EnginePower)):Enumerable.Empty<EngineAudioVoice>());
            if(Radio!=null){Radio.Enabled=RadioBox.Checked;Radio.Update(frame.Valid?WarthogAudioLevels.RadioGain(frame.InWarthog,frame.Distance):0);}
            string state=frame.Valid?(frame.InWarthog?"In Warthog":("Nearest Warthog: "+(frame.Distance*3).ToString("0.0")+" m")):frame.Error;
            string text=state+"\nEngine: "+(engine?"recording / native RPM "+(frame.EngineRpmNormalized*100).ToString("0")+"%":EngineBox.Checked?"waiting; original sound retained":"original Halo sound")+"\nRadio: "+(RadioBox.Checked&&Radio!=null?Radio.Station:"off");
            if(AudioError!="")text+="\n"+AudioError;
            if(EngineAudio!=null&&EngineAudio.Error!=null)text+="\n"+EngineAudio.Error;
            AudioStatus.Text=text;
            string key=frame.Valid+":"+frame.InWarthog+":"+frame.WarthogDatum+":"+engine+":"+RadioBox.Checked;
            if(key!=LastAudio||DateTime.UtcNow-LastAudioLog>TimeSpan.FromSeconds(5)) {Log("AUDIO "+text.Replace('\n',' '));LastAudio=key;LastAudioLog=DateTime.UtcNow;}
        }catch(Exception e) {
            if(BoostZoom!=null)BoostZoom.Tick(false);
            if(EngineAudio!=null)EngineAudio.Set(0,0);
            if(EngineMute!=null)EngineMute.Restore();
            if(Radio!=null)try{Radio.Update(0);}catch{}
            AudioStatus.Text="Audio paused: "+e.Message;
            if(LastAudio!=e.Message){Log("AUDIO PAUSED "+e.Message);LastAudio=e.Message;}
        }
    }
    void UpdateSession() {
        if(Deadline!=default(DateTime)&&DateTime.UtcNow>=Deadline){Close();return;}
        try {
            if(Session!=null&&Session.Process.HasExited)CloseSession();
            if(Session==null) {
                var list=Process.GetProcesses().Where(p=>{try{return p.ProcessName.IndexOf("reclaimer",StringComparison.OrdinalIgnoreCase)>=0&&p.MainWindowHandle!=IntPtr.Zero&&p.MainWindowTitle.StartsWith("Project Reclaimer")&&(RequestedPid==0||p.Id==RequestedPid);}catch{return false;}}).ToArray();
                if(list.Length!=1){ShowStatus(list.Length==0?"Waiting for a Reclaimer game window...":"Multiple game clients found. Close the extra client.");return;}
                Session=new CameraSession(list[0],Log);
                try{MotionBlur=new CockpitMotionBlur(Session,Log);}catch(Exception motionFailure){Log("Motion controls unavailable: "+motionFailure.Message);}
                try{BoostZoom=new VehicleBoostZoom(Session,Log);}catch(Exception zoomFailure){Log("Vehicle boost zoom correction unavailable: "+zoomFailure.Message);}
                EngineMute=new WarthogEngineMute(Session,Log);
                Telemetry=new VehicleAudioTelemetry(Session,EngineMute.IsWarthog,EngineMute.MaxAngularVelocity);
                if(Radio==null)Radio=new WarthogRadio(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"audio","radio"));
                if(EngineAudio==null&&AudioError=="")try{EngineAudio=new EngineLoopMixer(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"audio","engine"));}catch(Exception audioFailure){AudioError=audioFailure.Message;Log("AUDIO INIT "+AudioError);}
            }
            ShowStatus(Session.Tick(EnabledBox.Checked,ApproximateBox.Checked));
            if(BoostZoom!=null&&!EnabledBox.Checked)BoostZoom.Tick(false);
            if(MotionBlur!=null)MotionBlur.Tick(EnabledBox.Checked&&Session.SeatCount>0);
            try{EngineMute.Refresh();}catch(Exception audioFailure){EngineMute.Restore();AudioStatus.Text="Audio waiting: "+audioFailure.Message;}
        } catch(Exception e){if(BoostZoom!=null)BoostZoom.Tick(false);if(MotionBlur!=null)MotionBlur.Tick(false);ShowStatus(e.Message);}
    }
}

static class Program {
    [STAThread] static void Main(string[] args){
        bool first;
        using(var mutex=new System.Threading.Mutex(true,"Local\\ReclaimerWarthogCamera018",out first)) {
            if(!first){MessageBox.Show("The camera helper is already running.","Vehicle Cockpit");return;}
            Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);Application.Run(new CameraWindow(args));
        }
    }
}


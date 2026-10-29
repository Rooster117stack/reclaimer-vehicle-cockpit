using System;

// Native Gauss geometry only. The model's existing scope shader supplies the
// screen image; this does not synthesize a thermal effect or change aiming.
static class GaussScopeCamera {
    static bool Near(byte[] data,int offset,float expected) {
        float value=BitConverter.ToSingle(data,offset);
        return !float.IsNaN(value)&&!float.IsInfinity(value)&&Math.Abs(value-expected)<0.0001f;
    }
    static bool Position(byte[] data,int offset,float x,float y,float z) {
        return Near(data,offset,x)&&Near(data,offset+4,y)&&Near(data,offset+8,z);
    }
    static bool IdentityRotation(byte[] data,int offset) {
        return Near(data,offset,0)&&Near(data,offset+4,0)&&Near(data,offset+8,0)
            &&Math.Abs(Math.Abs(BitConverter.ToSingle(data,offset+12))-1f)<0.0001f;
    }
    static bool Marker(byte[] data,int node,float x,float y,float z) {
        return data!=null&&data.Length==0x24&&data[0]==255&&data[1]==255&&data[2]==node
            &&Position(data,4,x,y,z);
    }
    internal static bool TryCreate(string seatLabel,string attachmentName,string cameraName,
        byte[] cameraMarker,byte[] displayMarker,byte[] gunnerMarker,
        string[] nodeNames,byte[][] nodes,out byte[] result) {
        result=null;
        if(seatLabel!="warthog_g"||attachmentName!="gunner"||cameraName!="camera_gunner")return false;
        string[] expected={"stationary_mount","gun_mount_base","gun","cable","gauss_rifle_ammo","cable_end"};
        if(nodes==null||nodeNames==null||nodes.Length!=expected.Length||nodeNames.Length!=expected.Length)return false;
        int[] parents={-1,0,1,2,2,3};
        for(int i=0;i<expected.Length;i++) {
            if(nodeNames[i]!=expected[i]||nodes[i]==null||nodes[i].Length!=0x60
                ||BitConverter.ToInt16(nodes[i],4)!=parents[i]||!IdentityRotation(nodes[i],24))return false;
        }
        if(!Position(nodes[0],12,0,0,0)||!Position(nodes[1],12,0,0,0)
            ||!Position(nodes[2],12,0.27969998f,0,0.38026276f))return false;
        if(!Marker(cameraMarker,1,0.276487f,0,0.374426f)||!IdentityRotation(cameraMarker,16)
            ||!Marker(gunnerMarker,1,-0.12284023f,0,0.31693062f)||!IdentityRotation(gunnerMarker,16)
            ||!Marker(displayMarker,2,-0.06638203f,0.01306560f,0.34039128f))return false;
        if(!Near(displayMarker,16,-0.03105742f)||!Near(displayMarker,20,0.09883989f)
            ||!Near(displayMarker,24,-0.11924960f)||!Near(displayMarker,28,-0.98744410f))return false;
        result=(byte[])cameraMarker.Clone();
        result[2]=2;
        // H3EK gauss mesh 1/part 2 (gauss_alpha) is a plane facing -X.
        // Its decompressed center in gun-node coordinates is
        // (-0.20463564, 0.01081183, 0.24212941). Keep the eye 0.06 WU
        // behind it. Binding to the gun node makes the screen follow pitch.
        float[] position={-0.26463564f,0.01081183f,0.24212941f};
        for(int i=0;i<3;i++)Buffer.BlockCopy(BitConverter.GetBytes(position[i]),0,result,4+i*4,4);
        float[] rotation={0,0,0,-1};
        for(int i=0;i<4;i++)Buffer.BlockCopy(BitConverter.GetBytes(rotation[i]),0,result,16+i*4,4);
        return true;
    }
}

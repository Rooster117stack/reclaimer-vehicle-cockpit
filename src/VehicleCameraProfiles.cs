using System;

// Known imported aircraft sometimes name a distant chase anchor as a camera.
// Match its original geometry before substituting a private pilot-head marker.
// This class is pure: it never reads or writes a game process itself.
internal static class VehicleCameraProfiles {
    static bool Near(byte[] data,int offset,float expected,float tolerance) {
        if(data==null||offset<0||offset+4>data.Length)return false;
        float value=BitConverter.ToSingle(data,offset);
        return !float.IsNaN(value)&&!float.IsInfinity(value)&&Math.Abs(value-expected)<=tolerance;
    }
    static bool Position(byte[] data,int at,float x,float y,float z) {
        return Near(data,at,x,.001f)&&Near(data,at+4,y,.001f)&&Near(data,at+8,z,.001f);
    }
    static bool Rotation(byte[] data,int at,float x,float y,float z,float w) {
        return Near(data,at,x,.001f)&&Near(data,at+4,y,.001f)&&Near(data,at+8,z,.001f)&&Near(data,at+12,w,.001f);
    }
    static bool Marker(byte[] data,int node,float x,float y,float z,float qz,float qw,float scale) {
        return data!=null&&data.Length==0x24&&data[0]==255&&data[1]==255&&data[2]==node
            &&Position(data,4,x,y,z)&&Rotation(data,0x10,0,0,qz,qw)&&Near(data,0x20,scale,.000001f);
    }
    static bool Node(byte[] data,string expectedName,Func<uint,string> name,float x,float y,float z,float qz,float qw) {
        return data!=null&&data.Length>=0x60&&name(BitConverter.ToUInt32(data,0))==expectedName
            &&BitConverter.ToInt16(data,4)==0&&Position(data,12,x,y,z)
            &&Rotation(data,24,0,0,qz,qw)&&Near(data,40,1,.001f);
    }

    // Caller must require one record in both marker groups. Returned X offset is
    // in the attachment node frame; add +0.25 Z for the seated head, preserving
    // the original marker's node, quaternion, scale and permutation metadata.
    internal static bool TryPilot(string attachmentName,string cameraName,int flags,
        byte[] source,byte[] camera,Func<int,byte[]> getNode,Func<uint,string> name,
        out float forwardOffset,out string label) {
        forwardOffset=0;label=null;
        uint seatFlags=unchecked((uint)flags);
        if((seatFlags&4u)==0||(seatFlags&((1u<<11)|(1u<<16)))!=0||getNode==null||name==null)return false;
        if(attachmentName=="falcon_d"&&cameraName=="falcon_d_camera"
            &&Marker(source,3,.753436565f,0,-.596731186f,0,1,.01f)
            &&Marker(camera,3,-.131788149f,0,-.326271921f,0,1,.01f)
            &&Node(getNode(3),"hull",name,.131788597f,0,.926271975f,0,-1)) {
            forwardOffset=.10f;label="Falcon pilot cockpit";return true;
        }
        if(attachmentName=="driver"&&cameraName=="driver_camera"
            &&Marker(source,1,-1.911177635f,0,.174704626f,-1,0,.0001f)
            &&Marker(camera,1,10,0,2.85f,-1,0,.0001f)
            &&Node(getNode(1),"node",name,0,0,0,-1,0)) {
            // The Wombat's hull node is rotated 180 degrees around Z.
            forwardOffset=-.10f;label="Wombat pilot cockpit";return true;
        }
        return false;
    }
}

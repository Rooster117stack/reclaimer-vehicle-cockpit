using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

// Local output only. Never installs an audio driver or changes Windows/game volume.
sealed class EngineAudioVoice {
    internal float Rev,Gain;
    internal EngineAudioVoice(){}
    internal EngineAudioVoice(float rev,float gain){Rev=rev;Gain=gain;}
}

sealed class EngineLoopMixer : IDisposable {
    [StructLayout(LayoutKind.Sequential,Pack=2)] struct WaveFormat {
        internal ushort Format,Channels; internal uint Rate,BytesPerSecond;
        internal ushort Align,Bits,Extra;
    }
    [StructLayout(LayoutKind.Sequential)] struct WaveHeader {
        internal IntPtr Data; internal uint Length,Recorded; internal UIntPtr User;
        internal uint Flags,Loops; internal IntPtr Next; internal UIntPtr Reserved;
    }
    [DllImport("winmm.dll")] static extern uint waveOutOpen(out IntPtr device,uint id,ref WaveFormat format,IntPtr callback,IntPtr instance,uint flags);
    [DllImport("winmm.dll")] static extern uint waveOutPrepareHeader(IntPtr device,IntPtr header,uint size);
    [DllImport("winmm.dll")] static extern uint waveOutWrite(IntPtr device,IntPtr header,uint size);
    [DllImport("winmm.dll")] static extern uint waveOutUnprepareHeader(IntPtr device,IntPtr header,uint size);
    [DllImport("winmm.dll")] static extern uint waveOutReset(IntPtr device);
    [DllImport("winmm.dll")] static extern uint waveOutClose(IntPtr device);
    const int Frames=1024;
    readonly float[][] Loops;
    readonly int[][] Positions;
    readonly float[] Revs=new float[8],Volumes=new float[8];
    readonly IntPtr[] Headers=new IntPtr[4],Buffers=new IntPtr[4];
    readonly uint HeaderSize=(uint)Marshal.SizeOf(typeof(WaveHeader));
    readonly Thread Worker;
    IntPtr Device;
    volatile bool Stopping;
    volatile EngineAudioVoice[] Targets=new EngineAudioVoice[0];
    internal volatile string Error;
    internal EngineLoopMixer(string directory) {
        Loops=new[]{"idle.wav","low.wav","mid-low.wav","mid.wav","mid-high.wav","high.wav"}.Select(n=>ReadPcm(Path.Combine(directory,n))).ToArray();
        Positions=new int[8][];
        for(int v=0;v<Positions.Length;v++) {
            Positions[v]=new int[Loops.Length];
            for(int l=0;l<Loops.Length;l++)Positions[v][l]=(v*9973+l*1223)%Loops[l].Length;
        }
        var format=new WaveFormat{Format=1,Channels=1,Rate=44100,BytesPerSecond=88200,Align=2,Bits=16};
        Check(waveOutOpen(out Device,uint.MaxValue,ref format,IntPtr.Zero,IntPtr.Zero,0),"open audio output");
        try {
            for(int i=0;i<Headers.Length;i++) {
                Buffers[i]=Marshal.AllocHGlobal(Frames*2);Headers[i]=Marshal.AllocHGlobal((int)HeaderSize);
                Marshal.Copy(new byte[Frames*2],0,Buffers[i],Frames*2);
                Marshal.StructureToPtr(new WaveHeader{Data=Buffers[i],Length=Frames*2},Headers[i],false);
                Check(waveOutPrepareHeader(Device,Headers[i],HeaderSize),"prepare audio buffer");
                Check(waveOutWrite(Device,Headers[i],HeaderSize),"start audio buffer");
            }
            Worker=new Thread(Run){IsBackground=true,Name="Warthog engine audio"};Worker.Start();
        } catch {Dispose();throw;}
    }
    static void Check(uint result,string action){if(result!=0)throw new IOException("Could not "+action+" (WinMM "+result+").");}
    internal static float[] ReadPcm(string file) {
        using(var reader=new BinaryReader(File.OpenRead(file))) {
            if(Encoding.ASCII.GetString(reader.ReadBytes(4))!="RIFF")throw new IOException("Engine clip is not WAV.");
            reader.ReadUInt32();if(Encoding.ASCII.GetString(reader.ReadBytes(4))!="WAVE")throw new IOException("Invalid engine WAV.");
            bool valid=false;
            while(reader.BaseStream.Position+8<=reader.BaseStream.Length) {
                string kind=Encoding.ASCII.GetString(reader.ReadBytes(4));uint length=reader.ReadUInt32();long end=reader.BaseStream.Position+length;
                if(end>reader.BaseStream.Length)throw new IOException("Truncated engine WAV.");
                if(kind=="fmt ") {
                    if(length<16)throw new IOException("Invalid engine WAV format.");
                    ushort codec=reader.ReadUInt16(),channels=reader.ReadUInt16();uint rate=reader.ReadUInt32();reader.ReadUInt32();reader.ReadUInt16();ushort bits=reader.ReadUInt16();
                    valid=codec==1&&channels==1&&rate==44100&&bits==16;
                }
                if(kind=="data") {
                    if(!valid||length<8820||length>44100*2*30||length%2!=0)throw new IOException("Engine WAV must be mono PCM16 at 44100 Hz, 0.1-30 seconds.");
                    var values=new float[length/2];for(int i=0;i<values.Length;i++)values[i]=reader.ReadInt16()/32768f;return values;
                }
                reader.BaseStream.Position=end+(length&1);
            }
        }
        throw new IOException("No engine WAV samples.");
    }
    internal void Set(float rev,float volume) {
        SetVoices(new[]{new EngineAudioVoice(rev,volume)});
    }
    internal void SetVoices(IEnumerable<EngineAudioVoice> voices) {
        Targets=voices==null?new EngineAudioVoice[0]:voices.Where(v=>v!=null).Take(8)
            .Select(v=>new EngineAudioVoice(FiniteClamp(v.Rev),FiniteClamp(v.Gain))).ToArray();
    }
    static float FiniteClamp(float f){return float.IsNaN(f)||float.IsInfinity(f)?0:Math.Max(0,Math.Min(1,f));}
    internal void Render(short[] output) {
        EngineAudioVoice[] targets=Targets;
        for(int f=0;f<output.Length;f++) {
            float sample=0;
            for(int v=0;v<Positions.Length;v++) {
                float targetRev=v<targets.Length?targets[v].Rev:0,targetVolume=v<targets.Length?targets[v].Gain:0;
                Revs[v]+=(targetRev-Revs[v])*0.0007f;Volumes[v]+=(targetVolume-Volumes[v])*0.0015f;
                float band=Revs[v]*(Loops.Length-1);int lo=Math.Min(Loops.Length-2,(int)band);float fraction=band-lo;
                // Independent phase per nearby engine and equal-power band blending.
                sample+=(Loops[lo][Positions[v][lo]]*(float)Math.Sqrt(1-fraction)+Loops[lo+1][Positions[v][lo+1]]*(float)Math.Sqrt(fraction))*Volumes[v];
                for(int i=0;i<Loops.Length;i++)if(++Positions[v][i]>=Loops[i].Length)Positions[v][i]=0;
            }
            // Smooth limiter preserves headroom when several nearby engines add.
            output[f]=(short)(30000*Math.Tanh(sample*26000/30000));
        }
    }
    void Run() {
        var samples=new short[Frames];
        try {
            while(!Stopping) {
                for(int i=0;i<Headers.Length&&!Stopping;i++) {
                    var header=(WaveHeader)Marshal.PtrToStructure(Headers[i],typeof(WaveHeader));
                    if((header.Flags&1)==0)continue;
                    Render(samples);Marshal.Copy(samples,0,Buffers[i],Frames);
                    Check(waveOutWrite(Device,Headers[i],HeaderSize),"queue engine audio");
                }
                Thread.Sleep(4);
            }
        } catch(Exception e){Error=e.Message;Targets=new EngineAudioVoice[0];}
    }
    public void Dispose() {
        Stopping=true;if(Worker!=null&&Worker.IsAlive)Worker.Join();
        if(Device!=IntPtr.Zero)waveOutReset(Device);
        for(int i=0;i<Headers.Length;i++) {
            if(Headers[i]!=IntPtr.Zero){if(Device!=IntPtr.Zero)waveOutUnprepareHeader(Device,Headers[i],HeaderSize);Marshal.FreeHGlobal(Headers[i]);Headers[i]=IntPtr.Zero;}
            if(Buffers[i]!=IntPtr.Zero){Marshal.FreeHGlobal(Buffers[i]);Buffers[i]=IntPtr.Zero;}
        }
        if(Device!=IntPtr.Zero){waveOutClose(Device);Device=IntPtr.Zero;}
    }
}

sealed class WarthogRadio : IDisposable {
    [StructLayout(LayoutKind.Sequential,Pack=2)] struct WaveFormat {
        internal ushort Format,Channels; internal uint Rate,BytesPerSecond;
        internal ushort Align,Bits,Extra;
    }
    [StructLayout(LayoutKind.Sequential)] struct WaveHeader {
        internal IntPtr Data; internal uint Length,Recorded; internal UIntPtr User;
        internal uint Flags,Loops; internal IntPtr Next; internal UIntPtr Reserved;
    }
    [DllImport("winmm.dll")] static extern uint waveOutOpen(out IntPtr device,uint id,ref WaveFormat format,IntPtr callback,IntPtr instance,uint flags);
    [DllImport("winmm.dll")] static extern uint waveOutPrepareHeader(IntPtr device,IntPtr header,uint size);
    [DllImport("winmm.dll")] static extern uint waveOutWrite(IntPtr device,IntPtr header,uint size);
    [DllImport("winmm.dll")] static extern uint waveOutUnprepareHeader(IntPtr device,IntPtr header,uint size);
    [DllImport("winmm.dll")] static extern uint waveOutPause(IntPtr device);
    [DllImport("winmm.dll")] static extern uint waveOutRestart(IntPtr device);
    [DllImport("winmm.dll")] static extern uint waveOutReset(IntPtr device);
    [DllImport("winmm.dll")] static extern uint waveOutClose(IntPtr device);
    const int Frames=1024;
    readonly string[] Songs;
    readonly object Gate=new object();
    readonly IntPtr[] Headers=new IntPtr[4],Buffers=new IntPtr[4];
    readonly uint HeaderSize=(uint)Marshal.SizeOf(typeof(WaveHeader));
    readonly short[] Samples=new short[Frames*2];
    readonly Thread Worker;
    IntPtr Device;
    BinaryReader Source;
    long DataStart,DataBytes,LoopCount;
    volatile bool Stopping;
    bool Playing;
    int Index;
    float Volume,TargetVolume;
    internal volatile string Error;
    internal bool Enabled;
    internal string Station {get{lock(Gate)return Songs.Length==0?"No WAV stations":Path.GetFileNameWithoutExtension(Songs[Index]);}}
    internal WarthogRadio(string directory) {
        Songs=Directory.Exists(directory)?Directory.GetFiles(directory,"*.wav").OrderBy(x=>x,StringComparer.OrdinalIgnoreCase).ToArray():new string[0];
        Worker=new Thread(Run){IsBackground=true,Name="Warthog radio audio"};Worker.Start();
    }
    static void Check(uint result,string action){if(result!=0)throw new IOException("Radio could not "+action+" (WinMM "+result+").");}
    void OpenCurrent() {
        try {
            Source=new BinaryReader(File.OpenRead(Songs[Index]));
            if(Encoding.ASCII.GetString(Source.ReadBytes(4))!="RIFF")throw new IOException("Radio station is not WAV.");
            Source.ReadUInt32();if(Encoding.ASCII.GetString(Source.ReadBytes(4))!="WAVE")throw new IOException("Invalid radio WAV.");
            bool valid=false,found=false;
            while(Source.BaseStream.Position+8<=Source.BaseStream.Length) {
                string kind=Encoding.ASCII.GetString(Source.ReadBytes(4));uint length=Source.ReadUInt32();long end=Source.BaseStream.Position+length;
                if(end>Source.BaseStream.Length)throw new IOException("Truncated radio WAV.");
                if(kind=="fmt ") {
                    if(length<16)throw new IOException("Invalid radio WAV format.");
                    ushort codec=Source.ReadUInt16(),channels=Source.ReadUInt16();uint rate=Source.ReadUInt32();Source.ReadUInt32();ushort align=Source.ReadUInt16(),bits=Source.ReadUInt16();
                    valid=codec==1&&channels==2&&rate==44100&&align==4&&bits==16;
                }
                if(kind=="data") {
                    if(!valid||length<17640||length%4!=0)throw new IOException("Radio WAV must be stereo PCM16 at 44100 Hz.");
                    DataStart=Source.BaseStream.Position;DataBytes=length;LoopCount=0;found=true;break;
                }
                Source.BaseStream.Position=end+(length&1);
            }
            if(!found)throw new IOException("Radio WAV has no samples.");
            var format=new WaveFormat{Format=1,Channels=2,Rate=44100,BytesPerSecond=176400,Align=4,Bits=16};
            Check(waveOutOpen(out Device,uint.MaxValue,ref format,IntPtr.Zero,IntPtr.Zero,0),"open output");
            Check(waveOutPause(Device),"pause output setup");
            for(int i=0;i<Headers.Length;i++) {
                Buffers[i]=Marshal.AllocHGlobal(Frames*4);Headers[i]=Marshal.AllocHGlobal((int)HeaderSize);
                Marshal.StructureToPtr(new WaveHeader{Data=Buffers[i],Length=Frames*4},Headers[i],false);
                Check(waveOutPrepareHeader(Device,Headers[i],HeaderSize),"prepare buffer");
                Queue(i);
            }
        }catch{CloseCurrent();throw;}
    }
    void Queue(int index) {
        for(int i=0;i<Frames;i++) {
            if(Source.BaseStream.Position>=DataStart+DataBytes){Source.BaseStream.Position=DataStart;LoopCount++;}
            Volume+=(TargetVolume-Volume)*0.0015f;
            Samples[i*2]=(short)(Source.ReadInt16()*Volume);
            Samples[i*2+1]=(short)(Source.ReadInt16()*Volume);
        }
        Marshal.Copy(Samples,0,Buffers[index],Samples.Length);
        Check(waveOutWrite(Device,Headers[index],HeaderSize),"queue buffer");
    }
    internal void Next(){lock(Gate){if(Songs.Length==0)return;CloseCurrent();Index=(Index+1)%Songs.Length;Error=null;}}
    internal void Update(float gain) {
        lock(Gate) {
            if(Stopping)return;
            if(float.IsNaN(gain)||float.IsInfinity(gain))gain=0;
            if(!Enabled||Songs.Length==0||gain<=0.0001f) {
                if(Device!=IntPtr.Zero&&Playing){Check(waveOutPause(Device),"pause station");Playing=false;}return;
            }
            if(Error!=null)throw new IOException(Error);
            // 1/1000 resolution also permits a zero-output device smoke test.
            TargetVolume=(int)(Math.Max(0,Math.Min(1,gain))*1000)/1000f;
            if(Device==IntPtr.Zero)OpenCurrent();
            if(!Playing){Check(waveOutRestart(Device),"resume station");Playing=true;}
        }
    }
    void Run() {
        while(!Stopping) {
            lock(Gate) {
                if(Playing&&Device!=IntPtr.Zero) {
                    try {
                        for(int i=0;i<Headers.Length;i++) {
                            var header=(WaveHeader)Marshal.PtrToStructure(Headers[i],typeof(WaveHeader));
                            if((header.Flags&1)!=0)Queue(i);
                        }
                    }catch(Exception e){Error=e.Message;CloseCurrent();}
                }
            }
            Thread.Sleep(4);
        }
    }
    void CloseCurrent() {
        Playing=false;
        if(Device!=IntPtr.Zero)waveOutReset(Device);
        for(int i=0;i<Headers.Length;i++) {
            if(Headers[i]!=IntPtr.Zero){if(Device!=IntPtr.Zero)waveOutUnprepareHeader(Device,Headers[i],HeaderSize);Marshal.FreeHGlobal(Headers[i]);Headers[i]=IntPtr.Zero;}
            if(Buffers[i]!=IntPtr.Zero){Marshal.FreeHGlobal(Buffers[i]);Buffers[i]=IntPtr.Zero;}
        }
        if(Device!=IntPtr.Zero){waveOutClose(Device);Device=IntPtr.Zero;}
        if(Source!=null){Source.Dispose();Source=null;}
        Volume=0;
    }
    public void Dispose(){Stopping=true;if(Worker!=null&&Worker.IsAlive)Worker.Join();lock(Gate)CloseCurrent();}
}

static class WarthogAudioLevels {
    // Halo world unit ~= 3 m. Radio falls silent 4 units (~12 m) from the vehicle.
    internal static float RadioGain(bool seated,float distance) {
        if(seated)return 0.65f;
        if(float.IsNaN(distance)||float.IsInfinity(distance)||distance<0||distance>=4)return 0;
        float fade=1-distance/4;return 0.32f*fade*fade;
    }
    internal static float EngineGain(bool seated,float distance) {
        if(seated)return 0.65f;
        if(float.IsNaN(distance)||float.IsInfinity(distance)||distance<0||distance>=20)return 0;
        float fade=1-distance/20;return 0.5f*fade*fade;
    }
}

// Reads .mp3 (and other sound files Windows can play: .m4a, .wma, .aac ...) through Windows' own Media Foundation, so the
// program needs no extra library. Used to turn a player's MP3 into a .wav when they import a sound (the build reads .wav).
using System.Runtime.InteropServices;

namespace Bmb.Core;

public static class Mp3
{
    /// the file's sound as mono floats (-1..1) and its sample rate
    public static (float[] Mono, int Rate) Read(string path)
    {
        Check(MFStartup(0x00020070, 0), "start Media Foundation");
        try
        {
            Check(MFCreateSourceReaderFromURL(path, IntPtr.Zero, out var reader), "open the file");
            try
            {
                const int Audio = unchecked((int)0xFFFFFFFD);       // the first audio stream
                Check(MFCreateMediaType(out var want), "make a sound type");
                want.SetGUID(MT_MAJOR_TYPE, MediaType_Audio);
                want.SetGUID(MT_SUBTYPE, AudioFormat_Float);
                Check(reader.SetCurrentMediaType(Audio, IntPtr.Zero, want), "decode it to plain sound");
                Marshal.ReleaseComObject(want);
                Check(reader.GetCurrentMediaType(Audio, out var got), "read its format");
                got.GetUINT32(MT_AUDIO_NUM_CHANNELS, out int ch);
                got.GetUINT32(MT_AUDIO_SAMPLES_PER_SECOND, out int rate);
                Marshal.ReleaseComObject(got);
                if (ch <= 0 || rate <= 0) throw new InvalidDataException("the file has no sound");
                var mono = new List<float>();
                var buf = new float[0];
                while (true)
                {
                    Check(reader.ReadSample(Audio, 0, out _, out int flags, out _, out var sample), "read the sound");
                    if (sample != null)
                    {
                        Check(sample.ConvertToContiguousBuffer(out var mb), "read the sound");
                        Check(mb.Lock(out IntPtr p, out _, out int len), "read the sound");
                        int n = len / 4;
                        if (buf.Length < n) buf = new float[n];
                        Marshal.Copy(p, buf, 0, n);
                        mb.Unlock();
                        for (int i = 0; i + ch <= n; i += ch) { float s = 0; for (int c = 0; c < ch; c++) s += buf[i + c]; mono.Add(s / ch); }
                        Marshal.ReleaseComObject(mb); Marshal.ReleaseComObject(sample);
                        if (mono.Count > rate * 60) break;           // a minute is plenty for a car sound
                    }
                    if ((flags & 2) != 0) break;                     // end of the file
                }
                if (mono.Count == 0) throw new InvalidDataException("the file has no sound");
                return (mono.ToArray(), rate);
            }
            finally { Marshal.ReleaseComObject(reader); }
        }
        finally { MFShutdown(); }
    }

    static void Check(int hr, string what) { if (hr < 0) throw new InvalidDataException($"Windows could not {what} (0x{hr:X8})"); }

    static readonly Guid MT_MAJOR_TYPE = new("48eba18e-f8c9-4687-bf11-0a74c9f96a8f"), MT_SUBTYPE = new("f7e34c9a-42e8-4714-b74b-cb29d72c35e5"),
        MediaType_Audio = new("73647561-0000-0010-8000-00AA00389B71"), AudioFormat_Float = new("00000003-0000-0010-8000-00AA00389B71"),
        MT_AUDIO_NUM_CHANNELS = new("37e48bf5-645e-4c5b-89de-ada9e29b696a"), MT_AUDIO_SAMPLES_PER_SECOND = new("5faeeae7-0290-4c31-9e8a-c534f68d9dba");

    [DllImport("mfplat.dll")] static extern int MFStartup(int version, int flags);
    [DllImport("mfplat.dll")] static extern int MFShutdown();
    [DllImport("mfplat.dll")] static extern int MFCreateMediaType(out IMFMediaType type);
    [DllImport("mfreadwrite.dll", CharSet = CharSet.Unicode)] static extern int MFCreateSourceReaderFromURL(string url, IntPtr attributes, out IMFSourceReader reader);

    // only the slots used are typed; the others keep the table in order
    [ComImport, Guid("44ae0fa8-ea31-4109-8d2e-4cae4997c555"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMFMediaType
    {
        void GetItem(); void GetItemType(); void CompareItem(); void Compare();
        [PreserveSig] int GetUINT32([In, MarshalAs(UnmanagedType.LPStruct)] Guid key, out int value);
        void GetUINT64(); void GetDouble(); void GetGUID(); void GetStringLength(); void GetString(); void GetAllocatedString();
        void GetBlobSize(); void GetBlob(); void GetAllocatedBlob(); void GetUnknown(); void SetItem(); void DeleteItem(); void DeleteAllItems();
        void SetUINT32(); void SetUINT64(); void SetDouble();
        [PreserveSig] int SetGUID([In, MarshalAs(UnmanagedType.LPStruct)] Guid key, [In, MarshalAs(UnmanagedType.LPStruct)] Guid value);
    }

    [ComImport, Guid("70ae66f2-c809-4e4f-8915-bdcb406b7993"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMFSourceReader
    {
        void GetStreamSelection(); void SetStreamSelection(); void GetNativeMediaType();
        [PreserveSig] int GetCurrentMediaType(int stream, out IMFMediaType type);
        [PreserveSig] int SetCurrentMediaType(int stream, IntPtr reserved, IMFMediaType type);
        void SetCurrentPosition();
        [PreserveSig] int ReadSample(int stream, int control, out int actualStream, out int flags, out long time, out IMFSample? sample);
    }

    [ComImport, Guid("c40a00f2-b93a-4d80-ae8c-5a1c634f58e4"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMFSample
    {
        // IMFAttributes (30)
        void A0(); void A1(); void A2(); void A3(); void A4(); void A5(); void A6(); void A7(); void A8(); void A9();
        void A10(); void A11(); void A12(); void A13(); void A14(); void A15(); void A16(); void A17(); void A18(); void A19();
        void A20(); void A21(); void A22(); void A23(); void A24(); void A25(); void A26(); void A27(); void A28(); void A29();
        void GetSampleFlags(); void SetSampleFlags(); void GetSampleTime(); void SetSampleTime(); void GetSampleDuration(); void SetSampleDuration();
        void GetBufferCount(); void GetBufferByIndex();
        [PreserveSig] int ConvertToContiguousBuffer(out IMFMediaBuffer buffer);
    }

    [ComImport, Guid("045FA593-8799-42b8-BC8D-8968C6453507"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMFMediaBuffer
    {
        [PreserveSig] int Lock(out IntPtr buffer, out int max, out int current);
        [PreserveSig] int Unlock();
    }
}

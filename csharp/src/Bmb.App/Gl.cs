// A small OpenGL layer with no outside packages: a WinForms control with a WGL context, fixed-function drawing, a text font.
using System.Runtime.InteropServices;

namespace Bmb.App;

public static unsafe class GL
{
    const string O = "opengl32.dll";
    public const uint TRIANGLES = 4, LINES = 1, LINE_STRIP = 3, TRIANGLE_STRIP = 5, QUADS = 7, POINTS = 0, COLOR_BUFFER_BIT = 0x4000, DEPTH_BUFFER_BIT = 0x100, DEPTH_TEST = 0xB71,
        BLEND = 0xBE2, SRC_ALPHA = 0x302, ONE_MINUS_SRC_ALPHA = 0x303, PROJECTION = 0x1701, MODELVIEW = 0x1700, FLOAT = 0x1406, UNSIGNED_BYTE = 0x1401, VERTEX_ARRAY = 0x8074,
        COLOR_ARRAY = 0x8076, CULL_FACE = 0xB44, LINE_SMOOTH = 0xB20, POLYGON_OFFSET_FILL = 0x8037, COMPILE = 0x1300, MULTISAMPLE = 0x809D, FOG = 0xB60, TEXTURE_2D = 0xDE1, TEXTURE_COORD_ARRAY = 0x8078, RGB = 0x1907, UNPACK_ALIGNMENT = 0xCF5, TEXTURE_WRAP_S = 0x2802, TEXTURE_WRAP_T = 0x2803, TEXTURE_MAG_FILTER = 0x2800, TEXTURE_MIN_FILTER = 0x2801, REPEAT = 0x2901, LINEAR = 0x2601, LINEAR_MIPMAP_LINEAR = 0x2703, DEPTH_COMPONENT = 0x1902;
    [DllImport(O)] public static extern void glClear(uint m);
    [DllImport(O)] public static extern void glClearColor(float r, float g, float b, float a);
    [DllImport(O)] public static extern void glEnable(uint c);
    [DllImport(O)] public static extern void glDisable(uint c);
    [DllImport(O)] public static extern void glBlendFunc(uint a, uint b);
    [DllImport(O)] public static extern void glViewport(int x, int y, int w, int h);
    [DllImport(O)] public static extern void glMatrixMode(uint m);
    [DllImport(O)] public static extern void glLoadIdentity();
    [DllImport(O)] public static extern void glLoadMatrixf(float* m);
    [DllImport(O)] public static extern void glOrtho(double l, double r, double b, double t, double n, double f);
    [DllImport(O)] public static extern void glLineWidth(float w);
    [DllImport(O)] public static extern void glPointSize(float s);
    [DllImport(O)] public static extern void glPolygonOffset(float a, float b);
    [DllImport(O)] public static extern void glEnableClientState(uint a);
    [DllImport(O)] public static extern void glDisableClientState(uint a);
    [DllImport(O)] public static extern void glVertexPointer(int size, uint type, int stride, void* p);
    [DllImport(O)] public static extern void glColorPointer(int size, uint type, int stride, void* p);
    [DllImport(O)] public static extern void glDrawArrays(uint mode, int first, int count);
    [DllImport(O)] public static extern uint glGenLists(int n);
    [DllImport(O)] public static extern void glDeleteLists(uint l, int n);
    [DllImport(O)] public static extern void glNewList(uint l, uint mode);
    [DllImport(O)] public static extern void glEndList();
    [DllImport(O)] public static extern void glCallList(uint l);
    [DllImport(O)] public static extern void glCallLists(int n, uint type, void* lists);
    [DllImport(O)] public static extern void glListBase(uint b);
    [DllImport(O)] public static extern void glRasterPos2f(float x, float y);
    [DllImport(O)] public static extern void glColor4f(float r, float g, float b, float a);
    [DllImport(O)] public static extern void glGenTextures(int n, uint* t);
    [DllImport(O)] public static extern void glBindTexture(uint target, uint t);
    [DllImport(O)] public static extern void glPixelStorei(uint p, int v);
    [DllImport(O)] public static extern void glTexParameteri(uint target, uint p, uint v);
    [DllImport(O)] public static extern void glTexCoordPointer(int size, uint type, int stride, void* p);
    [DllImport("glu32.dll")] public static extern int gluBuild2DMipmaps(uint target, int comps, int w, int h, uint format, uint type, void* data);
    [DllImport(O)] public static extern void glDepthMask(byte f);
    [DllImport(O)] public static extern IntPtr wglCreateContext(IntPtr dc);
    [DllImport(O)] public static extern bool wglMakeCurrent(IntPtr dc, IntPtr rc);
    [DllImport(O)] public static extern bool wglDeleteContext(IntPtr rc);
    [DllImport(O)] public static extern IntPtr wglGetProcAddress(string name);
    [DllImport(O, CharSet = CharSet.Ansi)] public static extern bool wglUseFontBitmapsA(IntPtr dc, uint first, uint count, uint listBase);
    [DllImport(O)] public static extern bool wglUseFontBitmapsW(IntPtr dc, uint first, uint count, uint listBase);
    [DllImport(O)] public static extern IntPtr glGetString(uint n);
    [DllImport("gdi32.dll")] public static extern int ChoosePixelFormat(IntPtr dc, ref Pfd p);
    [DllImport("gdi32.dll")] public static extern bool SetPixelFormat(IntPtr dc, int f, ref Pfd p);
    [DllImport("gdi32.dll")] public static extern bool SwapBuffers(IntPtr dc);
    [DllImport("gdi32.dll")] public static extern IntPtr SelectObject(IntPtr dc, IntPtr o);
    [DllImport("user32.dll")] public static extern IntPtr GetDC(IntPtr w);
    [DllImport("user32.dll")] public static extern int ReleaseDC(IntPtr w, IntPtr dc);
    [DllImport("gdi32.dll")] public static extern bool DeleteObject(IntPtr o);

    [StructLayout(LayoutKind.Sequential)]
    public struct Pfd
    {
        public ushort nSize, nVersion; public uint dwFlags; public byte iPixelType, cColorBits, cRedBits, cRedShift, cGreenBits, cGreenShift, cBlueBits, cBlueShift, cAlphaBits, cAlphaShift,
            cAccumBits, cAccumRedBits, cAccumGreenBits, cAccumBlueBits, cAccumAlphaBits, cDepthBits, cStencilBits, cAuxBuffers, iLayerType, bReserved; public uint dwLayerMask, dwVisibleMask, dwDamageMask;
    }
}

/// vertices with colour, drawn from plain arrays. x y z r g b a per vertex
public sealed unsafe class Batch
{
    public float[] V = new float[7 * 4096]; public int N;
    void Room(int more) { if ((N + more) * 7 > V.Length) Array.Resize(ref V, Math.Max(V.Length * 2, (N + more) * 7)); }
    public void Clear() => N = 0;
    public void Add(float x, float y, float z, Rgba c)
    {
        Room(1);
        int i = N * 7; var v = V;
        v[i] = x; v[i + 1] = y; v[i + 2] = z; v[i + 3] = c.R; v[i + 4] = c.G; v[i + 5] = c.B; v[i + 6] = c.A;
        N++;
    }
    public void Add2(double x, double y, Rgba c) => Add((float)x, (float)y, 0, c);
    public void Tri(double x0, double y0, double x1, double y1, double x2, double y2, Rgba c) { Add2(x0, y0, c); Add2(x1, y1, c); Add2(x2, y2, c); }
    public void Quad(double x0, double y0, double x1, double y1, double x2, double y2, double x3, double y3, Rgba c) { Tri(x0, y0, x1, y1, x2, y2, c); Tri(x0, y0, x2, y2, x3, y3, c); }
    public void Line(double x0, double y0, double x1, double y1, Rgba c) { Add2(x0, y0, c); Add2(x1, y1, c); }

    /// a thick line as triangles (any width, looks the same on every graphics card)
    public void Thick(double x0, double y0, double x1, double y1, double w, Rgba c)
    {
        double dx = x1 - x0, dy = y1 - y0, l = Math.Sqrt(dx * dx + dy * dy);
        if (l < 1e-6) return;
        double nx = -dy / l * w / 2, ny = dx / l * w / 2;
        Quad(x0 + nx, y0 + ny, x1 + nx, y1 + ny, x1 - nx, y1 - ny, x0 - nx, y0 - ny, c);
    }

    public void Disc(double x, double y, double r, Rgba c, int seg = 14)
    {
        for (int i = 0; i < seg; i++)
        {
            double a = 2 * Math.PI * i / seg, b = 2 * Math.PI * (i + 1) / seg;
            Tri(x, y, x + r * Math.Cos(a), y + r * Math.Sin(a), x + r * Math.Cos(b), y + r * Math.Sin(b), c);
        }
    }

    public void Ring(double x, double y, double r, double w, Rgba c, int seg = 24)
    {
        for (int i = 0; i < seg; i++)
        {
            double a = 2 * Math.PI * i / seg, b = 2 * Math.PI * (i + 1) / seg;
            Thick(x + r * Math.Cos(a), y + r * Math.Sin(a), x + r * Math.Cos(b), y + r * Math.Sin(b), w, c);
        }
    }

    public void Rect(double x0, double y0, double x1, double y1, Rgba c) => Quad(x0, y0, x1, y0, x1, y1, x0, y1, c);

    public void Draw(uint mode) => DrawRange(mode, 0, N);

    public void DrawRange(uint mode, int first, int count)
    {
        if (count == 0) return;
        fixed (float* p = V)
        {
            GL.glEnableClientState(GL.VERTEX_ARRAY); GL.glEnableClientState(GL.COLOR_ARRAY);
            GL.glVertexPointer(3, GL.FLOAT, 28, p); GL.glColorPointer(4, GL.FLOAT, 28, p + 3);
            GL.glDrawArrays(mode, first, count);
            GL.glDisableClientState(GL.VERTEX_ARRAY); GL.glDisableClientState(GL.COLOR_ARRAY);
        }
    }
}

public readonly record struct Rgba(float R, float G, float B, float A = 1)
{
    public static Rgba Hex(string s) => new(Convert.ToInt32(s.Substring(1, 2), 16) / 255f, Convert.ToInt32(s.Substring(3, 2), 16) / 255f, Convert.ToInt32(s.Substring(5, 2), 16) / 255f);
    public Rgba Shade(float k) => new(R * k, G * k, B * k, A);
    public Rgba WithA(float a) => new(R, G, B, a);
}

public class GlControl : Control
{
    IntPtr _dc, _rc;
    uint _font;
    public bool Ready => _rc != IntPtr.Zero;
    public event Action? Render;
    public event Action? GlReady;
    public double LastFrameMs;
    readonly System.Diagnostics.Stopwatch _sw = new();
    delegate int SwapInterval(int i);

    public GlControl()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.Opaque | ControlStyles.Selectable, true);
        TabStop = true;
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        _dc = GL.GetDC(Handle);
        var p = new GL.Pfd { nSize = 40, nVersion = 1, dwFlags = 0x25, iPixelType = 0, cColorBits = 32, cDepthBits = 24, cAlphaBits = 8 };
        int f = GL.ChoosePixelFormat(_dc, ref p);
        GL.SetPixelFormat(_dc, f, ref p);
        _rc = GL.wglCreateContext(_dc);
        GL.wglMakeCurrent(_dc, _rc);
        var ptr = GL.wglGetProcAddress("wglSwapIntervalEXT");
        if (ptr != IntPtr.Zero) Marshal.GetDelegateForFunctionPointer<SwapInterval>(ptr)(1);
        using var font = new Font("Segoe UI", 9f, FontStyle.Regular, GraphicsUnit.Point);
        var old = GL.SelectObject(_dc, font.ToHfont());
        _font = GL.glGenLists(256 + 96);
        GL.wglUseFontBitmapsA(_dc, 0, 255, _font);
        GL.wglUseFontBitmapsW(_dc, 0x400, 96, _font + 256);       // Russian letters
        GL.SelectObject(_dc, old);
        GlReady?.Invoke();
    }

    protected override void OnHandleDestroyed(EventArgs e)
    {
        if (_rc != IntPtr.Zero) { GL.wglMakeCurrent(IntPtr.Zero, IntPtr.Zero); GL.wglDeleteContext(_rc); _rc = IntPtr.Zero; }
        if (_dc != IntPtr.Zero) { GL.ReleaseDC(Handle, _dc); _dc = IntPtr.Zero; }
        base.OnHandleDestroyed(e);
    }

    public void MakeCurrent() { if (Ready) GL.wglMakeCurrent(_dc, _rc); }

    protected override void OnPaint(PaintEventArgs e)
    {
        if (!Ready) return;
        _sw.Restart();
        GL.wglMakeCurrent(_dc, _rc);
        GL.glViewport(0, 0, Math.Max(Width, 1), Math.Max(Height, 1));
        Render?.Invoke();
        LastFrameMs = _sw.Elapsed.TotalMilliseconds;
        GL.SwapBuffers(_dc);
    }

    /// the self test's mouse: goes through the same events as a real one
    public void SimDown(int x, int y, MouseButtons b = MouseButtons.Left) => OnMouseDown(new MouseEventArgs(b, 1, x, y, 0));
    public void SimMove(int x, int y, MouseButtons b = MouseButtons.Left) => OnMouseMove(new MouseEventArgs(b, 0, x, y, 0));
    public void SimUp(int x, int y, MouseButtons b = MouseButtons.Left) => OnMouseUp(new MouseEventArgs(b, 1, x, y, 0));
    public void SimClick(int x, int y, MouseButtons b = MouseButtons.Left) { SimDown(x, y, b); SimUp(x, y, b); }

    protected override void OnResize(EventArgs e) { base.OnResize(e); Invalidate(); }
    protected override bool IsInputKey(Keys k) => true;
    protected override void OnMouseDown(MouseEventArgs e) { Focus(); base.OnMouseDown(e); }

    /// text in window pixels (2D projection set up by the caller)
    public unsafe void Text(float x, float y, string s, Rgba c)
    {
        if (s.Length == 0) return;
        s = Lang.T(s);
        GL.glColor4f(c.R, c.G, c.B, c.A);
        GL.glRasterPos2f(x, y);
        GL.glListBase(_font);
        var b = new ushort[s.Length];
        for (int i = 0; i < s.Length; i++) b[i] = (ushort)(s[i] < 256 ? s[i] : s[i] >= 0x400 && s[i] < 0x460 ? 256 + s[i] - 0x400 : '?');
        fixed (ushort* p = b) GL.glCallLists(b.Length, 0x1403, p);
    }

    public static void Ortho2D(int w, int h)
    {
        GL.glMatrixMode(GL.PROJECTION); GL.glLoadIdentity(); GL.glOrtho(0, w, h, 0, -1, 1);
        GL.glMatrixMode(GL.MODELVIEW); GL.glLoadIdentity();
    }

    /// the window's picture as RGB (for the self test's screenshots)
    public unsafe Bitmap Grab()
    {
        MakeCurrent();
        var bm = new Bitmap(Width, Height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        var d = bm.LockBits(new Rectangle(0, 0, Width, Height), System.Drawing.Imaging.ImageLockMode.WriteOnly, bm.PixelFormat);
        glReadPixels(0, 0, Width, Height, 0x80E1, GL.UNSIGNED_BYTE, (void*)d.Scan0);
        bm.UnlockBits(d);
        bm.RotateFlip(RotateFlipType.RotateNoneFlipY);
        return bm;
    }
    /// the depth at a pixel of the frame just drawn (0..1), y from the bottom
    public unsafe float ReadDepth(int x, int y)
    {
        float z = 1;
        glReadPixels(x, y, 1, 1, GL.DEPTH_COMPONENT, GL.FLOAT, &z);
        return z;
    }
    [DllImport("opengl32.dll")] static extern unsafe void glReadPixels(int x, int y, int w, int h, uint fmt, uint type, void* p);
}

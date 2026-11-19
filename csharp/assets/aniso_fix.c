/* aniso_fix.dll - Blur (retail 32-bit Blur.exe, Amax): turn on anisotropic texture filtering.
 *
 * Why: retail Blur PC never sets D3DSAMP_MAXANISOTROPY and never uses the anisotropic filter, so road and wall
 * textures go blurry at low angles (fact from BlurRebuilt's notes; our own code).
 *
 * How: the game's display object is [0x1143798]; its Direct3D 9 device is [[0x1143798] + 0xC60] (checked by the
 * code at 0x4A7864, which must hold exactly the expected bytes - any other exe = nothing is changed). A small thread
 * waits for the device, then replaces ONE entry in the device's method table: SetSamplerState (slot 69, +0x114).
 * Our version passes everything through, except: on a texture stage whose min filter is LINEAR and that uses
 * mipmaps (mip filter not NONE) the min filter becomes ANISOTROPIC and max anisotropy the chosen level. Stages
 * without mipmaps (render targets, shadow/depth maps, post effects) and POINT filters are left exactly as the
 * game set them.
 *
 * Settings: aniso_fix.ini, log: aniso_fix_log.txt - in the folder "aniso_fix" next to this DLL (amax\dlls\aniso_fix\),
 * or next to the DLL when that folder does not exist.
 *
 * build: zig cc -target x86-windows-gnu -shared -O2 -s -o aniso_fix.dll aniso_fix.c
 */
#include <windows.h>
#include <d3d9.h>
#include <stdarg.h>
#include <stdint.h>
#include <stdio.h>
#include <string.h>

#define VERSION "1.0"
#define P_DISPLAY 0x1143798u
#define DEVICE_OFS 0xC60u
#define SLOT_SET_SAMPLER 69
#define SLOT_GET_SAMPLER 68
#define SLOT_GET_CAPS 7
#define STAGES 16

static int cfg_level = 16;      /* 0/1 = off, else 2..16 */
static int cfg_log = 1;
static char dir[MAX_PATH];
static FILE *logf;
static int log_lines;

static void logmsg(const char *fmt, ...) {
    if (!cfg_log || !logf || log_lines > 500)
        return;
    SYSTEMTIME t;
    GetLocalTime(&t);
    fprintf(logf, "%02d:%02d:%02d ", t.wHour, t.wMinute, t.wSecond);
    va_list a;
    va_start(a, fmt);
    vfprintf(logf, fmt, a);
    va_end(a);
    fputc('\n', logf);
    fflush(logf);
    log_lines++;
}

static int region_ok(const void *p, size_t n, int write, int exec) {
    MEMORY_BASIC_INFORMATION m;
    if (!p || VirtualQuery(p, &m, sizeof(m)) != sizeof(m) || m.State != MEM_COMMIT)
        return 0;
    if (m.Protect & (PAGE_GUARD | PAGE_NOACCESS))
        return 0;
    if ((uintptr_t)p + n > (uintptr_t)m.BaseAddress + m.RegionSize)
        return 0;
    DWORD x = PAGE_EXECUTE | PAGE_EXECUTE_READ | PAGE_EXECUTE_READWRITE | PAGE_EXECUTE_WRITECOPY;
    DWORD w = PAGE_READWRITE | PAGE_WRITECOPY | PAGE_EXECUTE_READWRITE | PAGE_EXECUTE_WRITECOPY;
    if (exec)
        return (m.Protect & x) != 0;
    if (write)
        return (m.Protect & w) != 0;
    return 1;
}

/* ---------------------------------------------------------------- the replaced SetSamplerState */
typedef HRESULT(WINAPI *SetSS)(IDirect3DDevice9 *, DWORD, D3DSAMPLERSTATETYPE, DWORD);
typedef HRESULT(WINAPI *GetSS)(IDirect3DDevice9 *, DWORD, D3DSAMPLERSTATETYPE, DWORD *);
typedef HRESULT(WINAPI *GetCaps)(IDirect3DDevice9 *, D3DCAPS9 *);

static volatile SetSS orig_set;
static void **hooked_vtbl;
static int level;                 /* the level used, clamped to the card's maximum (first call) */
static int level_ready;
static volatile LONG upgraded, calls;

typedef struct { int min, mip, aniso; int eff_on; } Stage;   /* what the GAME asked for; -1 = not seen */
static Stage st[STAGES];

static int want_on(IDirect3DDevice9 *dev, DWORD s) {
    Stage *p = &st[s];
    if (level < 2 || p->min != D3DTEXF_LINEAR)
        return 0;
    if (p->mip < 0) {               /* never seen: ask the device once (fails on a "pure" device -> assume mipmaps) */
        DWORD v = D3DTEXF_LINEAR;
        GetSS get = (GetSS)hooked_vtbl[SLOT_GET_SAMPLER];
        if (get && get(dev, s, D3DSAMP_MIPFILTER, &v) == D3D_OK)
            p->mip = (int)v;
        else
            p->mip = D3DTEXF_LINEAR;
    }
    return p->mip != D3DTEXF_NONE;
}

static void first_call(IDirect3DDevice9 *dev) {
    level_ready = 1;
    level = cfg_level;
    D3DCAPS9 caps;
    GetCaps gc = (GetCaps)hooked_vtbl[SLOT_GET_CAPS];
    memset(&caps, 0, sizeof(caps));
    if (gc && gc(dev, &caps) == D3D_OK) {
        if (!(caps.TextureFilterCaps & D3DPTFILTERCAPS_MINFANISOTROPIC)) {
            level = 0;
            logmsg("the graphics card does not offer anisotropic filtering - left off");
        } else if (caps.MaxAnisotropy > 0 && (DWORD)level > caps.MaxAnisotropy) {
            level = (int)caps.MaxAnisotropy;
        }
    }
    logmsg("first sampler call: using %dx (card maximum %lu)", level, (unsigned long)caps.MaxAnisotropy);
}

static HRESULT WINAPI my_set(IDirect3DDevice9 *dev, DWORD s, D3DSAMPLERSTATETYPE type, DWORD value) {
    SetSS o = orig_set;
    if (!level_ready)
        first_call(dev);
    calls++;
    if (s >= STAGES || level < 2 ||
        (type != D3DSAMP_MINFILTER && type != D3DSAMP_MIPFILTER && type != D3DSAMP_MAXANISOTROPY))
        return o(dev, s, type, value);
    Stage *p = &st[s];
    HRESULT r = D3D_OK;
    if (type == D3DSAMP_MIPFILTER) {
        p->mip = (int)value;
        r = o(dev, s, type, value);
    } else if (type == D3DSAMP_MINFILTER) {
        p->min = (int)value;
    } else {
        p->aniso = (int)value;
    }
    int on = want_on(dev, s);
    /* send the effective min filter / max anisotropy: always for the state the game set, and both when this
       stage switched between on and off (the game only re-sends states that it thinks changed) */
    int switched = on != p->eff_on;
    p->eff_on = on;
    if (type == D3DSAMP_MAXANISOTROPY || switched || type == D3DSAMP_MINFILTER) {
        DWORD a = on ? (DWORD)level : (DWORD)(p->aniso > 0 ? p->aniso : 1);
        HRESULT ra = o(dev, s, D3DSAMP_MAXANISOTROPY, a);
        if (type == D3DSAMP_MAXANISOTROPY)
            r = ra;
    }
    if (type == D3DSAMP_MINFILTER || (switched && p->min >= 0)) {
        DWORD m = on ? D3DTEXF_ANISOTROPIC : (DWORD)p->min;
        HRESULT rm = o(dev, s, D3DSAMP_MINFILTER, m);
        if (type == D3DSAMP_MINFILTER)
            r = rm;
        if (on && type == D3DSAMP_MINFILTER)
            upgraded++;
    }
    return r;
}

/* ---------------------------------------------------------------- finding the device and hooking it */
/* 0x4A7864: mov eax,[0x1143798]; lea edi,[eax+0x6B8]; mov esi,eax; cmp [edi],ebx; je +0x1D;
 *           mov eax,[eax+0xC60]; mov ecx,[eax]; mov edx,[ecx+0x114] */
static const uint8_t c_check[] = {0xA1, 0x98, 0x37, 0x14, 0x01, 0x8D, 0xB8, 0xB8, 0x06, 0x00, 0x00, 0x8B, 0xF0, 0x39,
                                  0x1F, 0x74, 0x1D, 0x8B, 0x80, 0x60, 0x0C, 0x00, 0x00, 0x8B, 0x08, 0x8B, 0x91, 0x14,
                                  0x01, 0x00, 0x00};
#define CHECK_AT 0x4A7864u

static int try_hook(void) {
    uint8_t **disp = (uint8_t **)P_DISPLAY;
    uint8_t *d = *(uint8_t *volatile *)disp;
    if (!d || !region_ok(d + DEVICE_OFS, 4, 0, 0))
        return 0;
    IDirect3DDevice9 *dev = *(IDirect3DDevice9 *volatile *)(d + DEVICE_OFS);
    if (!dev || !region_ok(dev, 4, 0, 0))
        return 0;
    void **vt = *(void ***)dev;
    if (!vt || !region_ok(vt, (SLOT_SET_SAMPLER + 1) * 4, 0, 0))
        return 0;
    if (vt == hooked_vtbl)
        return 1;                                         /* already ours */
    if (hooked_vtbl) {
        logmsg("the device changed to another method table - not hooked again (texture filter stays as the game sets it)");
        return 2;
    }
    void *cur = vt[SLOT_SET_SAMPLER];
    if (cur == (void *)my_set)
        return 1;
    if (!region_ok(cur, 1, 0, 1) || !region_ok(vt[SLOT_GET_SAMPLER], 1, 0, 1) || !region_ok(vt[SLOT_GET_CAPS], 1, 0, 1)) {
        logmsg("STOP: the device's methods are not in program code - nothing changed");
        return 2;
    }
    for (int i = 0; i < STAGES; i++)
        st[i].min = st[i].mip = st[i].aniso = -1, st[i].eff_on = 0;
    orig_set = (SetSS)cur;
    hooked_vtbl = vt;
    DWORD old;
    void **slot = &vt[SLOT_SET_SAMPLER];
    if (!VirtualProtect(slot, 4, PAGE_EXECUTE_READWRITE, &old)) {
        logmsg("ERROR: could not unprotect the device method table - nothing changed");
        hooked_vtbl = NULL;
        return 2;
    }
    InterlockedExchangePointer(slot, (void *)my_set);     /* one aligned 4-byte swap: the game sees old or new */
    VirtualProtect(slot, 4, old, &old);
    if (*slot != (void *)my_set) {
        logmsg("ERROR: the method table did not take the change");
        return 2;
    }
    logmsg("hooked SetSamplerState of device %p (method table %p, original %p)", (void *)dev, (void *)vt, cur);
    return 1;
}

static DWORD WINAPI watcher(LPVOID unused) {
    (void)unused;
    for (int n = 0;; n++) {
        if (try_hook() == 2)
            return 0;
        if (hooked_vtbl && (n == 120 || n == 600))   /* ~30 s and ~150 s after start */
            logmsg("sampler calls so far %ld, stages switched to anisotropic %ld", calls, upgraded);
        if (n > 600 && hooked_vtbl)
            return 0;                              /* hooked long ago; nothing more to watch */
        Sleep(250);
    }
}

static void load_settings(void) {
    char ini[MAX_PATH];
    snprintf(ini, sizeof(ini), "%saniso_fix.ini", dir);
    cfg_level = GetPrivateProfileIntA("fix", "Anisotropy", 16, ini);
    if (cfg_level < 2)
        cfg_level = 0;
    if (cfg_level > 16)
        cfg_level = 16;
    cfg_log = GetPrivateProfileIntA("fix", "Log", 1, ini) != 0;
}

static void install(void) {
    load_settings();
    if (cfg_log) {
        char lp[MAX_PATH];
        snprintf(lp, sizeof(lp), "%saniso_fix_log.txt", dir);
        logf = fopen(lp, "w");
    }
    logmsg("aniso_fix %s: anisotropy %dx%s", VERSION, cfg_level, cfg_level ? "" : " (off)");
    if (!cfg_level) {
        logmsg("off in aniso_fix.ini - nothing changed");
        return;
    }
    if ((uintptr_t)GetModuleHandleA(NULL) != 0x400000 || !region_ok((void *)CHECK_AT, sizeof(c_check), 0, 1) ||
        memcmp((void *)CHECK_AT, c_check, sizeof(c_check)) != 0) {
        logmsg("STOP: this is not the Blur.exe the fix knows (code at 0x4A7864 differs) - nothing changed");
        return;
    }
    if (!region_ok((void *)P_DISPLAY, 4, 0, 0)) {
        logmsg("STOP: display pointer 0x1143798 not readable - nothing changed");
        return;
    }
    HANDLE t = CreateThread(NULL, 0, watcher, NULL, 0, NULL);
    if (t)
        CloseHandle(t);
    else
        logmsg("ERROR: could not start the watcher thread - nothing changed");
}

BOOL WINAPI DllMain(HINSTANCE inst, DWORD reason, LPVOID unused) {
    (void)unused;
    if (reason == DLL_PROCESS_ATTACH) {
        DisableThreadLibraryCalls(inst);
        GetModuleFileNameA(inst, dir, sizeof(dir));
        char *s = strrchr(dir, '\\');
        if (s)
            s[1] = 0;
        else
            dir[0] = 0;
        char sub[MAX_PATH];
        snprintf(sub, sizeof(sub), "%saniso_fix", dir);
        DWORD a = GetFileAttributesA(sub);
        if (a != INVALID_FILE_ATTRIBUTES && (a & FILE_ATTRIBUTE_DIRECTORY))
            snprintf(dir, sizeof(dir), "%s\\", sub);
        install();
    }
    return TRUE;
}

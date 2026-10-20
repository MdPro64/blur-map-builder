// Dump decompiled C for addresses, string users, or callers.
// args: <outfile> then any of:
//   0xADDR          decompile the function containing ADDR
//   str:TEXT        decompile every function that references the string TEXT
//   callers:0xADDR  list (and decompile) callers of the function containing ADDR
//   fn:ADDR         create a function at ADDR (if missing) and decompile it
//@category Blur
import ghidra.app.script.GhidraScript;
import ghidra.app.decompiler.*;
import ghidra.program.model.address.*;
import ghidra.program.model.listing.*;
import ghidra.program.model.symbol.*;
import java.io.*;
import java.util.*;

public class BlurDump extends GhidraScript {
    DecompInterface dec;
    PrintWriter w;
    Set<Address> done = new HashSet<>();

    void decompile(Function f, String why) {
        if (f == null || !done.add(f.getEntryPoint())) return;
        DecompileResults r = dec.decompileFunction(f, 180, monitor);
        w.println("// ===== " + why + "  ->  " + f.getName() + " @ " + f.getEntryPoint());
        if (r.getDecompiledFunction() != null) w.println(r.getDecompiledFunction().getC());
        else w.println("// decompile failed: " + r.getErrorMessage());
        w.flush();
    }

    @Override
    public void run() throws Exception {
        String[] args = getScriptArgs();
        dec = new DecompInterface();
        dec.openProgram(currentProgram);
        w = new PrintWriter(new FileWriter(args[0]));
        for (int i = 1; i < args.length; i++) {
            String a = args[i];
            if (a.startsWith("str:")) {
                byte[] needle = (a.substring(4) + "\0").getBytes("US-ASCII");
                Address at = currentProgram.getMinAddress();
                while ((at = currentProgram.getMemory().findBytes(at, needle, null, true, monitor)) != null) {
                    w.println("// string '" + a.substring(4) + "' at " + at);
                    for (Reference ref : getReferencesTo(at)) {
                        Function f = getFunctionContaining(ref.getFromAddress());
                        w.println("//   used at " + ref.getFromAddress() + " in " + (f == null ? "?" : f.getName()));
                        decompile(f, "uses string at " + ref.getFromAddress());
                    }
                    at = at.add(1);
                }
            } else if (a.startsWith("refs:")) {
                // decompile every function that references a data address (or anything within +0x40 of it)
                Address base = toAddr(a.substring(5));
                for (int d = 0; d < 0x40; d++) {
                    for (Reference ref : getReferencesTo(base.add(d))) {
                        Function f = getFunctionContaining(ref.getFromAddress());
                        w.println("// ref to " + base.add(d) + " from " + ref.getFromAddress() + " in " + (f == null ? "?" : f.getName()));
                        decompile(f, "refs " + base.add(d));
                    }
                }
            } else if (a.startsWith("callers:")) {
                Function f = getFunctionContaining(toAddr(a.substring(8)));
                if (f == null) { w.println("// no function at " + a); continue; }
                for (Reference ref : getReferencesTo(f.getEntryPoint())) {
                    Function c = getFunctionContaining(ref.getFromAddress());
                    w.println("// caller of " + f.getName() + ": " + ref.getFromAddress() + " in " + (c == null ? "?" : c.getName()));
                }
            } else if (a.startsWith("fn:")) {
                // make a function at ADDR first (for code Ghidra's analysis missed), then decompile it
                Address at = toAddr(a.substring(3));
                disassemble(at);
                Function f = getFunctionAt(at);
                if (f == null) f = createFunction(at, null);
                decompile(f, a);
            } else {
                decompile(getFunctionContaining(toAddr(a)), a);
            }
        }
        w.close();
    }
}

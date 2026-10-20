// Decompile every function whose entry lies in [START, END). args: <outfile> START END  (hex, no 0x)
import ghidra.app.script.GhidraScript;
import ghidra.app.decompiler.*;
import ghidra.program.model.listing.*;
import ghidra.program.model.address.*;
import java.io.*;

public class RangeDump extends GhidraScript {
    public void run() throws Exception {
        String[] a = getScriptArgs();
        DecompInterface dec = new DecompInterface();
        dec.openProgram(currentProgram);
        PrintWriter w = new PrintWriter(new FileWriter(a[0]));
        Address s = toAddr(a[1]), e = toAddr(a[2]);
        for (Function f : currentProgram.getFunctionManager().getFunctions(new AddressSet(s, e), true)) {
            DecompileResults r = dec.decompileFunction(f, 60, monitor);
            w.println("// ===== " + f.getEntryPoint());
            w.println(r.decompileCompleted() ? r.getDecompiledFunction().getC() : "// failed");
        }
        w.close();
    }
}

// List functions whose instructions use ALL the given memory displacements, e.g. [reg+0xa4].
// args: <outfile> <hexoff> <hexoff> ...
//@category Blur
import ghidra.app.script.GhidraScript;
import ghidra.program.model.listing.*;
import ghidra.program.model.scalar.Scalar;
import java.io.*;
import java.util.*;

public class FindOffsets extends GhidraScript {
    @Override
    public void run() throws Exception {
        String[] args = getScriptArgs();
        Set<Long> want = new HashSet<>();
        for (int i = 1; i < args.length; i++) want.add(Long.decode(args[i]));
        PrintWriter w = new PrintWriter(new FileWriter(args[0]));
        Listing listing = currentProgram.getListing();
        for (Function f : listing.getFunctions(true)) {
            Set<Long> seen = new HashSet<>();
            for (Instruction ins : listing.getInstructions(f.getBody(), true)) {
                for (int op = 0; op < ins.getNumOperands(); op++) {
                    String rep = ins.getDefaultOperandRepresentation(op);
                    if (!rep.contains("[")) continue;
                    for (Object o : ins.getOpObjects(op)) {
                        if (o instanceof Scalar) {
                            long v = ((Scalar) o).getUnsignedValue();
                            if (want.contains(v)) seen.add(v);
                        }
                    }
                }
            }
            if (seen.size() == want.size()) w.println(f.getEntryPoint() + " " + f.getName());
        }
        w.close();
    }
}

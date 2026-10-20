"""Blur minimap shape files (ui/data/ipf/minimap/<city>_game.ipf and _ui.ipf, in root.pak).
Header: u32 0x100000, u32 1, u32 3, u32 0; then a tree of nodes. Node = {u32 kind (3 = group, 1 = shape),
u32 flags, u32 name length, name, ...}; the root node is {u32 len, name} + u32 child count; a group is
followed by u32 child count. Shape data length isn't decoded: a node ends where the next node starts.
Shapes are named after the ROUTE (usa_la_river_6, usa_la_river_6_alt; _ui file: usa_la_river_6_ui,
usa_la_river_6_start_line, each route inside its own <group>)."""
import re
import struct


def node_starts(d):
    """offset of every named node (12 bytes before its name)"""
    out = []
    for m in re.finditer(rb"[\x01\x03]\x00\x00\x00[\x00-\x0f]\x00\x00\x00([\x01-\x40])\x00\x00\x00", d):
        n = m.group(1)[0]
        name = d[m.end():m.end() + n]
        if re.fullmatch(rb"[A-Za-z0-9_<> ]+", name):
            out.append((m.start(), name.decode()))
    return out


def copy_route(d, old, new, parent_count_at):
    """Duplicate the consecutive nodes whose names start with `old` (e.g. 'usa_la_river_6'), renamed to
    `new` (same length), inserted right after the originals; the u32 child count at `parent_count_at`
    is raised by the number of copied nodes (for the game file) or by 1 (for a copied <group>)."""
    raise NotImplementedError

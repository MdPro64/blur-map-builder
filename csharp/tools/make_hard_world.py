"""a harder test world for scratch\\game: what the user's map does not use (sprints, bridges, more maps, a second location, all scenery,
obstacles, sides, a drawn AI line, own power-ups, width + height points). usage: make_hard_world.py WORLD_JSON_IN WORLD_JSON_OUT"""
import json
import sys

w = json.load(open(sys.argv[1]))
loc = w["locations"][0]
cross = [{"x": -900, "z": 0}, {"x": -300, "z": 100}, {"x": 500, "z": 0, "w": 50}, {"x": 900, "z": 500, "corner": 60}, {"x": 400, "z": 900},
         {"x": 0, "z": 400, "h": 3}, {"x": 150, "z": -300}, {"x": 300, "z": -900}, {"x": 900, "z": -1200}]
loc["maps"].append({"id": "sprint_bridge", "name": "Sprint bridge", "width": 40.0, "points": cross, "auto_bridges": True, "obstacles": True,
                    "scenery": {"ships": True, "cranes": True, "landmarks": True, "lamps": True, "billboards": True, "chevrons": True,
                                "signs": True, "buildings": True, "arch": True}, "pickup_step": 120.0, "pickup_per_row": 3})
second = [{"x": 0, "z": 3000}, {"x": 800, "z": 3100}, {"x": 1500, "z": 3600, "w": 80}, {"x": 800, "z": 4300, "h": 5}, {"x": -400, "z": 3900}]
loc["maps"].append({"id": "second", "name": "Second map", "width": 60.0, "points": second, "auto_bridges": True, "obstacles": False, "laps": 2,
                    "surface": {"road": "tarmac", "sides": 6.0, "side_type": "grass", "variety": False, "paint": {}},
                    "branches": [{"from_m": 500.0, "to_m": 1500.0, "width": 30.0, "points": [{"x": 1300, "z": 3100}, {"x": 1800, "z": 3300}]}]})
drawn = [[x, 40.0 if x % 400 else -40.0] for x in range(-1000, 1001, 100)]
w["locations"].append({"id": "desert", "name": "Desert run", "surface_feel": "extreme", "lighting": {"preset": "Brighton", "values": {"sun": [1.0, 0.9, 0.8, 3.0], "bloom": [0.5]}},
                       "maps": [{"id": "desert_1", "name": "Dirt road", "width": 70.0, "auto_bridges": False, "obstacles": True,
                                 "points": [[-1200, 0], {"x": 0, "z": 60, "h": 8}, [1200, 0], {"x": 1500, "z": 700}, {"x": 400, "z": 1000}],
                                 "surface": {"road": "dirt", "sides": 0.0, "side_type": "dirt", "variety": False, "paint": {"10,10": "grass", "11,10": "grass:0.60"}},
                                 "splits": [{"from_m": 600.0, "to_m": 1200.0, "lanes": 3}],
                                 "pickups": [{"kind": "row", "x": 0.0, "z": 60.0, "type": "Bolt"}, {"kind": "single", "x": 300.0, "z": 40.0}],
                                 "ai_path": {"mode": "drawn", "lane": 14.0, "points": drawn},
                                 "edits": {"lamp:2": {"dx": 3.0, "turn": 0.4, "solid": True}, "lamp:3": {"hidden": True}}}]})
json.dump(w, open(sys.argv[2], "w"), indent=1)

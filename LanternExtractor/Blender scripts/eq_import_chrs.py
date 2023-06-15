import bpy
import os
import math
import sqlite3
import itertools
import operator
import random

####### CONFIG #######
# The shortname of the zone 
zone_name = ""

# The Exports folder created by LanternExtractor
lantern_export_folder = "C:\\LanternExtractor\\Exports"

# CSV file for translating race ID and gender combinations to their actor model name
race_data_csv_location = "C:\\LanternExtractor\\RaceData.csv"

# Database packaged with LanternEQ application. For querying mobs that spawn in zone and their location data
db_location = "C:\\Lantern-0.1.6-Windows\\Lantern-0.1.6\\LanternEQ_Data\\StreamingAssets\\Database\\lantern_server.db"

# Import Static mobs - mobs that spawn in zone and don't move around
import_static = True

# Import mobs that spawn in zone and either have patrol or roaming patterns
import_patrols = False

# The extension of the glTF models exported by the Extractor. "gltf" or "glb"
model_extension = "gltf"

# The scale that converts EQ units to Blender units (meters)
zone_scalar = 0.2
####### CONFIG #######

class Constants:
    Character_Collection = "Characters"
    Static_Collection = "Static"
    Patrols_Collection = "Patrols"
    
    Db_Query = """with npc_id_list as
(
	select distinct n.id
	from alkabor_spawn2 s2
	join alkabor_spawngroup sg on s2.spawngroupID = sg.id
	join alkabor_spawnentry se on se.spawngroupID = sg.id
	join alkabor_npc_types n on se.npcID = n.id
	where s2.zone = '{0}'
		and s2.enabled = 1
		and (s2.pathgrid {1} 0 {2} sg.dist {1} 0.0)
		and n.race <> 127 -- invisible man
)
select distinct sg.id as sgID, 
	sg.spawn_limit as sg_limit, -- 1
	s2.id as s2ID, -- 2
	se.chance, -- 3
	n.id as npcId, -- 4
	n.name, -- 5
	n.race, -- 6
	n.gender, -- 7
	n.face % 255 as face, -- 8 
	n.texture, -- 9
	case when n.d_melee_texture1 > 999 
		then 0 
		else n.d_melee_texture1 end as d_melee_texture1, -- 10
	pri0.idfile, -- 11
	pri0.itemtype, -- 12
	case when n.d_melee_texture2 > 999 
		then 0 
		else n.d_melee_texture2 end as d_melee_texture2, -- 13
	sec0.idfile, -- 14
	sec0.itemtype, -- 15
	sec1.idfile, -- 16
	sec1.itemtype, -- 17
	n.helmtexture, -- 18
	s2.x, s2.y, s2.z, s2.heading, n.size, -- 19, 20, 21, 22, 23
	s2.pathgrid, -- 24
    sg.dist -- 25
from alkabor_spawn2 s2
join alkabor_spawngroup sg on s2.spawngroupID = sg.id
join alkabor_spawnentry se on se.spawngroupID = sg.id
join alkabor_npc_types n on se.npcID = n.id
join npc_id_list n0 on n.id = n0.id
left join 
( -- PRIMARY
	select id, idfile, itemtype
	from (
	  select distinct n.id, lte.probability, lde.chance, i.id as item_id, i.idfile, i.itemtype,
		dense_rank() over (partition by n.id order by n.id, lte.probability desc, lde.chance desc, i.id) as rn
		from npc_id_list n0
		join alkabor_npc_types n on n0.id = n.id
		join alkabor_loottable lt on n.loottable_id = lt.id
		join alkabor_loottable_entries lte on lte.loottable_id = lt.id
		join alkabor_lootdrop_entries lde on (lte.lootdrop_id = lde.lootdrop_id)
		join items i on (lde.item_id = i.id and i.slots & 8192 > 0)
		where lde.equip_item > 0
		  and length(i.idfile) < 7
		  and lte.probability > 24
		  and lde.chance > 49
		) s0
	where rn = 1 ) pri0 on n.id = pri0.id
left join 
( -- SECONDARY IF DUAL WIELD
	select id, idfile, itemtype
	from (
	  select distinct n.id, lte.probability, lde.chance, i.id as item_id, i.idfile, i.itemtype,
		dense_rank() over (partition by n.id order by n.id, lte.probability desc, lde.chance desc, i.id) as rn
		from npc_id_list n0
		join alkabor_npc_types n on n0.id = n.id
		join alkabor_loottable lt on n.loottable_id = lt.id
		join alkabor_loottable_entries lte on lte.loottable_id = lt.id
		join alkabor_lootdrop_entries lde on (lte.lootdrop_id = lde.lootdrop_id)
		join items i on (lde.item_id = i.id and i.slots & 24576 > 0)
		where n.class_ in (1, 4, 7, 8, 9, 20, 23, 26, 27, 28) -- can dual wield
		  and lde.equip_item > 0
		  and length(i.idfile) < 7
		  and lte.probability > 24
		  and lde.chance > 49
		) s0
	where rn = 2 ) sec0 on n.id = sec0.id
left join 
( -- SECONDARY IF SECONDARY ONLY SLOT
	select id, idfile, itemtype
	from (
	  select distinct n.id, lte.probability, lde.chance, i.id as item_id, i.idfile, i.itemtype,
		dense_rank() over (partition by n.id order by n.id, lte.probability desc, lde.chance desc, i.id) as rn
		from npc_id_list n0
		join alkabor_npc_types n on n0.id = n.id
		join alkabor_loottable lt on n.loottable_id = lt.id
		join alkabor_loottable_entries lte on lte.loottable_id = lt.id
		join alkabor_lootdrop_entries lde on (lte.lootdrop_id = lde.lootdrop_id)
		join items i on (lde.item_id = i.id and i.slots & 16834 > 0 and i.slots & 8192 = 0)
		where lde.equip_item > 0
		  and length(i.idfile) < 7
		  and lte.probability > 24
		  and lde.chance > 49
		) s0
	where rn = 1 ) sec1 on n.id = sec1.id
where s2.zone = '{0}'
    and s2.enabled = 1
    and (s2.pathgrid {1} 0 {2} sg.dist {1} 0.0)
order by sg.id, s2.id, n.id"""

def query_for_characters(db_path, db_query, zone_name, patrols):
    
    rows = []
    with sqlite3.connect(db_path) as db_connection:
        cursor = db_connection.cursor()
        pathgrid_comparator = '>' if patrols else '='
        pathing_where_clause_logic = 'or' if patrols else 'and'
        query = db_query.format(zone_name, pathgrid_comparator, pathing_where_clause_logic)
        for row in cursor.execute(query):
            rows.append(row)

    return rows
  
def pick_spawns_for_group(spawn_ids, limit):

    if limit == 0 or len(spawn_ids) == 1:
        return spawn_ids
    return random.choices(spawn_ids, k=limit)

def pick_spawn(rows):

    percentile = 0
    random_num = random.randint(0, 99)
    for row in rows:
        percentile = percentile + int(row[3]) # % chance
        if random_num < percentile:
            return row
    # shouldn't get here if chances always add up to 100, but just in case
    return rows[0]
    
def convert_heading_to_radians(heading):

    ## This is basic conversion but it turns out wrong, even negating
    # heading_degrees = (heading * 360.0) / 512.0

    ## Working backwards I came up with this ugly formula, which works!
    heading_degrees = (heading * 720.0 + 91800.0) / 512.0 % 360.0

    return math.radians(heading_degrees)

def get_scale_multiplier(race_id, db_scale, zone_scalar):

    scalar = 0.2 # default
    if race_id == 49:
        scalar = 0.1
    elif race_id == 158: # wurms
        scalar = 1.0
        db_scale = 1.0 # Size in DB for these is all over the place
    elif race_id == 196:
        scalar = 1.0
    elif race_id == 108:
        scalar = 0.05
    
    return scalar * db_scale * zone_scalar

def import_player_character(row, model_extension, models_path):

    name = str(row[5]).strip().strip('#')
    id = str(row[4])
    pc_model_name = "{0}_{1}.{2}".format(name, id, model_extension)
    model_path = os.path.join(models_path, pc_model_name)
    if not os.path.exists(model_path):
        print("Player character model file does not exist: " + model_path)
        return False
    
    bpy.ops.import_scene.gltf(filepath=model_path)
    return True

def import_npc(row, model_extension, models_path, db_race_translation_dict):

    race_id = int(row[6])
    gender = int(row[7])
    texture = int(row[9])

    # Fix elementals
    if race_id in [209, 210, 211, 212]:
        if race_id == 209:
            texture = 0
        elif race_id == 210:
            texture = 3
        elif race_id == 211:
            texture = 2
        elif race_id == 212:
            texture = 1
        race_id = 75

    race_identifier = db_race_translation_dict[(race_id, gender)]
    unique_npc_string = get_unique_npc_string(race_identifier, texture, row)
    npc_model_name = "{0}.{1}".format(unique_npc_string, model_extension)
    model_path = os.path.join(models_path, npc_model_name)
    
    if not os.path.exists(model_path):

        print("NPC model file does not exist: " + model_path)
        if '_' in unique_npc_string:
            print("Checking backup model files...")
            backup_npc_strings = get_backup_npc_strings(unique_npc_string)
            backup_found = False
            for backup_str in backup_npc_strings:
                npc_model_name = "{0}.{1}".format(backup_str, model_extension)
                model_path = os.path.join(models_path, npc_model_name)
                if os.path.exists(model_path):
                    backup_found = True
                    print("Using backup model at: " + model_path)
                    break
            if not backup_found:
                print("No backup model file found, skipping")
                return False
        else:
            return False
    
    bpy.ops.import_scene.gltf(filepath=model_path)
    return True

def get_unique_npc_string(name, texture, row):

    face = int(row[8])
    helm_texture = int(row[18])
    primary, secondary = get_primary_secondary_values(row)

    if (texture + face + helm_texture + primary + secondary) == 0:
        return name
    
    return "{0}_{1:02d}-{2:02d}-{3:02d}-{4:03d}-{5:03d}".format(name, texture, face, helm_texture, primary, secondary)

def get_backup_npc_strings(unique_npc_string):
    backup_strings = []
    backup_strings.append(unique_npc_string[:-3] + '000')
    backup_strings.append(unique_npc_string[:-7] + '000-000')
    backup_strings.append(unique_npc_string.split('_')[0])

    return backup_strings

def get_primary_secondary_values(row):
    
    primary = int(row[10])
    secondary = 0
    primary_item_type = -1
    if primary == 0 and row[11]:
        primary_str = str(row[11])
        if len(primary_str) > 2:
            primary = int(primary_str[2:])
    if row[12]:
        primary_item_type = int(row[12])
    if not primary_item_type in [1, 4, 5, 35]:
        secondary = int(row[13])
        if secondary == 0:
            if row[14]:
                secondary_str = str(row[14])
                if (len(secondary_str) > 2):
                    secondary = int(secondary_str[2:])
            if secondary == 0 and row[16]:
                secondary_str = str(row[16])
                if (len(secondary_str) > 2):
                    secondary = int(secondary_str[2:])

    return primary, secondary

def rename_imported_model_and_fix_duplication(chr_name, name_armature_dict, name_armature_object_list_dict, primary, secondary):

    mesh_obj = next(m for m in bpy.context.selected_objects if m.type == "MESH")
    armature_obj = next((a for a in bpy.context.selected_objects if a.type == "ARMATURE"), None)
    
    chr_skeleton_name = mesh_obj.name
    if ".0" in chr_skeleton_name:
        chr_skeleton_name = chr_skeleton_name[0:(chr_skeleton_name.rindex('.'))]
    
    if primary > 0 or secondary > 0:
        chr_skeleton_name = "{0}-{1:03d}-{2:03d}".format(chr_skeleton_name, primary, secondary)

    mesh_obj.name = chr_name

    if armature_obj:
        armature_obj.name = chr_name + "_A"
        if chr_skeleton_name in name_armature_dict:
            armature_obj.data = name_armature_dict[chr_skeleton_name]
            name_armature_object_list_dict[chr_skeleton_name].append(armature_obj)
        else:
            name_armature_dict[chr_skeleton_name] = armature_obj.data
            name_armature_object_list_dict[chr_skeleton_name] = [armature_obj]

    for material_slot in mesh_obj.material_slots:
        material_name = material_slot.material.name
        if len(material_name) > 3 and "." in material_name and material_name[-3:].isnumeric():
            original_material = bpy.data.materials.get(material_slot.material.name[:-4])
            if not original_material:
                continue
            material_slot.material = original_material

def set_transforms_on_imported_model(x, y, z, heading, scale_multiplier):

    obj = next((o for o in bpy.context.selected_objects if o.type == "ARMATURE"), bpy.context.selected_objects[0])
    obj.location = (y * -zone_scalar, x * -zone_scalar, z * zone_scalar)
    rotation = convert_heading_to_radians(heading)
    existing_rotation_mode = obj.rotation_mode
    obj.rotation_mode = "XYZ"
    obj.rotation_euler = (0, 0, rotation)
    obj.rotation_mode = existing_rotation_mode
    obj.scale = (scale_multiplier, scale_multiplier, scale_multiplier)
    
def link_anim_data(name_armature_object_list_dict):
    
    for key, armature_obj_list in name_armature_object_list_dict.items():
        if len(armature_obj_list) == 1:
            continue
        
        bpy.context.view_layer.objects.active = armature_obj_list[0]
        
        for armature_obj in armature_obj_list[1:]:
            armature_obj.select_set(True)
        
        bpy.ops.object.make_links_data(type='ANIMATION')
        
        bpy.ops.object.select_all(action='DESELECT')    

def delete_orphaned_data():

    for material in bpy.data.materials:
        if not material.users:
            bpy.data.materials.remove(material)

    for texture in bpy.data.textures:
        if not texture.users:
            bpy.data.textures.remove(texture)

    for image in bpy.data.images:
        if not image.users:
            bpy.data.images.remove(image)

    for armature in bpy.data.armatures:
        if not armature.users:
            bpy.data.armatures.remove(armature)
            
    for action in bpy.data.actions:
        if not action.users:
            bpy.data.actions.remove(action)

###### SCRIPT START ######

if not import_static and not import_patrols:
    raise Exception("import_static and import_patrols both false - nothing to export")

zone_chr_export_folder = os.path.join(lantern_export_folder, zone_name, "Characters")
if not os.path.exists(zone_chr_export_folder):
    raise Exception("Zone characters export folder does not exist at " + zone_chr_export_folder + "!")
if not os.path.exists(race_data_csv_location):
    raise Exception("RaceData.csv file does not exist at " + race_data_csv_location + "!")
if not os.path.exists(db_location):
    raise Exception("Database does not exist at " + db_location + "!")
    
# Load the RaceData csv into a dictionary
db_race_translation_dict = {}

print("Loading RaceData.csv...")
with open(race_data_csv_location) as f_stream:
    header_passed = False
    for line in f_stream:
        if not header_passed:
            header_passed = True
            continue
        
        race_info = line.strip().split(',')
        id_str = race_info[0]
        if not id_str:
            continue

        id = int(id_str)
        for i in range(2, 6):
            if race_info[i]:
                db_race_translation_dict[(id, i - 2)] = race_info[i].strip()

base_collection = bpy.data.collections["Collection"]
chr_collection = bpy.data.collections.get(Constants.Character_Collection)
if not chr_collection:
    chr_collection = bpy.data.collections.new(Constants.Character_Collection)
    base_collection.children.link(chr_collection)
if import_static:
    static_collection = bpy.data.collections.new(Constants.Static_Collection)
    chr_collection.children.link(static_collection)
if import_patrols:
    patrols_collection = bpy.data.collections.new(Constants.Patrols_Collection)
    chr_collection.children.link(patrols_collection)

bpy.ops.object.select_all(action='DESELECT')

chr_db_rows = []
print("Executing database query...")
if import_static:
    chr_db_rows.extend(query_for_characters(db_location, Constants.Db_Query, zone_name, False))
if import_patrols:
    chr_db_rows.extend(query_for_characters(db_location, Constants.Db_Query, zone_name, True))

if import_static and import_patrols: # order by in the query, but if both true, we have two sets of query results
    chr_db_rows = sorted(chr_db_rows, key=operator.itemgetter(0,2))

filtered_chr_db_rows = []
print("Filtering spawns...")
rows_grouped_by_spawngroup = itertools.groupby(chr_db_rows, operator.itemgetter(0))
for spawngroup, sg_rows in rows_grouped_by_spawngroup:
    sg_rows_grouped_by_spawn = itertools.groupby(sg_rows, operator.itemgetter(2))
    spawn_ids = []
    spawn_id_to_rows_dict = {}
    for spawn_id, s_rows in sg_rows_grouped_by_spawn:
        spawn_ids.append(spawn_id)
        spawn_id_to_rows_dict[spawn_id] = list(s_rows)
    limit = int(spawn_id_to_rows_dict[spawn_ids[0]][0][1])
    limited_spawn_ids = pick_spawns_for_group(spawn_ids, limit)
    for limited_spawn_id in limited_spawn_ids:
        spawn_rows = spawn_id_to_rows_dict[limited_spawn_id]
        filtered_chr_db_rows.append(pick_spawn(spawn_rows))

name_armature_dict = {}
name_armature_object_list_dict = {}
print("Importing character gltf models...")
for row in filtered_chr_db_rows:
    race = int(row[6])
    if race < 13 or race == 128:
        import_success = import_player_character(row, model_extension, zone_chr_export_folder)
    else:
        import_success = import_npc(row, model_extension, zone_chr_export_folder, db_race_translation_dict)

    if not import_success:
        continue

    scale_multiplier = get_scale_multiplier(race, float(row[23]), zone_scalar)
    set_transforms_on_imported_model(float(row[19]), float(row[20]), float(row[21]), float(row[22]), scale_multiplier)
    
    pathgrid = int(row[24])
    dist = float(row[25])
    for selected_obj in bpy.context.selected_objects:
        for collection in selected_obj.users_collection:
            collection.objects.unlink(selected_obj)
        if pathgrid == 0 and dist == 0.0:
            static_collection.objects.link(selected_obj)
        else:
            patrols_collection.objects.link(selected_obj)
    
    primary, secondary = get_primary_secondary_values(row)
    rename_imported_model_and_fix_duplication(str(row[5]).strip().strip('#'), name_armature_dict, name_armature_object_list_dict, primary, secondary)

    bpy.ops.object.select_all(action='DESELECT')

print("Condensing duplicate animation data...")
link_anim_data(name_armature_object_list_dict)
print("Cleaning up duplicated orphan data...")
delete_orphaned_data()
print("Done!")



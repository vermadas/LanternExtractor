import bpy
import os

animated_texture_csv_location = "C:\\LanternExtractor\\Blender scripts\\animatedTextures.csv"

def verify_multiple_frame_files_exist(image):

    blender_image_path = image.filepath
    image_file_path = os.path.normpath(bpy.path.abspath(blender_image_path, library=image.library))

    frame_number_index = image_file_path.rfind('1')
    second_frame_file_name = image_file_path[:frame_number_index] + "2" + image_file_path[frame_number_index+1:]

    return os.path.exists(second_frame_file_name)

scene_fps = bpy.context.scene.render.fps / bpy.context.scene.render.fps_base

material_anim_dict = {}

with open(animated_texture_csv_location) as f_stream:

    for line in f_stream:
 
        mat_anim_info = line.strip().split(',')
        mat_name = mat_anim_info[0]
        anim_frame_count = int(mat_anim_info[1])
        anim_frame_time = int(mat_anim_info[2])

        material_anim_dict[mat_name] = (anim_frame_count, anim_frame_time)

for mat in bpy.data.materials:
    
    if not mat.name in material_anim_dict:
        continue
    
    if not mat.node_tree:
        continue
    
    nodes = mat.node_tree.nodes
    links = mat.node_tree.links

    base_color_node = nodes.get("Image Texture")
    
    if not base_color_node:
        continue

    if not verify_multiple_frame_files_exist(base_color_node.image):
        print("Material " + mat.name + " does not have multiple image frames - skipping")
        continue
    
    frame_count = material_anim_dict[mat.name][0]
    anim_frame_time = material_anim_dict[mat.name][1]

    base_color_node.image.source = 'SEQUENCE'
    base_color_node.image_user.frame_duration = frame_count
    base_color_node.image_user.use_cyclic = True
    base_color_node.image_user.use_auto_refresh = True

    fcurve = base_color_node.image_user.driver_add("frame_offset")
    fcurve.driver.type = "SCRIPTED"

    frame_multiplier = (1000 / scene_fps) / anim_frame_time
    fcurve.driver.expression = "floor({0}*frame) % {1} - ((frame-1) % {1})".format(frame_multiplier, frame_count)
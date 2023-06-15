import bpy

# Strength of the emission applied to vertex colors
emission_strength = 0.01

for mat in bpy.data.materials:
    
    if not mat.node_tree:
        continue
    
    nodes = mat.node_tree.nodes
    links = mat.node_tree.links

    vertex_color_node = nodes.get("Color Attribute")
    base_color_node = nodes.get("Image Texture")
    
    if not vertex_color_node or not base_color_node:
        continue
    
    pbsdf_node = nodes.get("Principled BSDF")
    light_path_node = nodes.get("Light Path")
    existing_alpha_link = next((l for l in links if l.from_socket == base_color_node.outputs[1]), None)
    
    if pbsdf_node:
        links.new(base_color_node.outputs[0], pbsdf_node.inputs[0])
        links.new(vertex_color_node.outputs[0], pbsdf_node.inputs[19])
        pbsdf_node.inputs[20].default_value = emission_strength
        if existing_alpha_link:
            links.new(base_color_node.outputs[1], pbsdf_node.inputs[21])        
    elif light_path_node:
        emission_node = nodes.get("Emission")
        links.new(base_color_node.outputs[0], emission_node.inputs[0])
        output_node = nodes.get("Material Output")
        if existing_alpha_link:
            mix_to_out_link = next((l for l in links if l.to_socket == output_node.inputs[0]), None)
            last_mix_shader = next((n for n in nodes if n.outputs[0] == mix_to_out_link.from_socket), None)
            links.new(base_color_node.outputs[1], last_mix_shader.inputs[0])
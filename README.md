# LanternExtractor (vermadas fork)
EverQuest Trilogy Client file extractor that exports game data into formats usable in modern game engines. 

This project is part of the [LanternEQ Project](https://www.lanterneq.com) which aims to reverse engineer and re-implement classic EverQuest in the Unity Engine.

## Overview
There have been many fantastic tools over the years that extract EverQuest content. Sadly, as most of these were written 15+ years ago, they can be hard to find, buggy on modern hardware and sometimes written in legacy programming languages. LanternExtractor fixes this by combining all of this functionality and more into one simple tool.

Although the extractor supports multiple export formats, the main focus is exporting assets to a human readable intermediate text format which can then be reconstructed in game engines.

The extractor also supports:
  - Raw archive content extraction
  - OBJ export
  - glTF export

## Notes about this fork
I am maintaining this custom fork separate from the main project so I can add features to the Extractor that are somewhat extraneous to the primary goals and functionality of the LanternExtractor and its purpose in the LanternEQ Project.

Most, if not all, of the functionality I plan to add can also be accomplished by using the Extractor's Intermediate format export and Lantern Unity Tools. If you are familiar with Unity, or are willing to learn, you may be better off sticking with the main LanternExtractor and using the Intermediate => Unity workflow.

I will not be making any improvements or updates to the OBJ or Intermediate export formats unless they come from the main project. All new 3D model features will be utilizing the glTF format only.

## Features

The intermediate format supports:
- S3D file contents
- Zone data
  - Textured mesh
  - Collision mesh
  - Vertex colors
  - BSP tree (region data)
  - Ambient light
  - Light instances
  - Music instances
  - Sound instances
- Object data
  - Textured meshes
  - Collision meshes
  - Vertex animations
  - Skeletal animations
  - Instance list
  - Per instance vertex colors
- Character data
  - Textured meshes
  - Skeletal animations
  - Skins
- Equipment data
  - Texture mesh
  - Skeletal animations

### Features in this fork
- Connection to a server database
- Customized player character model exporting to glTF

## What’s Next
  - Particle systems
  - Post Velious zone support

### What's Next in this fork
  - Export zones with doors

## How To Use
Please visit the [wiki](https://github.com/LanternEQ/LanternExtractor/wiki) for more info.

## Thanks
- Windcatcher - WLD file format document without which this project wouldn't be possible.
- Harakiri - Private classic test server.
- clickclickmoon - S3D (PFS) format documentation

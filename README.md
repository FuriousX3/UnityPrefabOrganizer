A unity editor script to aid in preparing a prefab for export.
Menu can be found under Tools -> FuriousX -> Prefab Cleanup

The script will copy materials, textures, meshes, animator controllers, animation clips, audio clips, physics materials, and fonts into their own subfolders in the directory that the selected prefab is located.
Additionally the script will attempt to overwrite dependency references in the prefab to point to the copied assets resulting in a clean unitypackage structure when exporting.

Future updates will add support for VRChat specific assets (VRC Expression Menu and VRC Expression Parameters assets)

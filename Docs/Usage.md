# MaskMaker Usage Guide (Unity 2022.3)

This document explains how to use **MaskMaker** (`Tools > MaskMaker`). It is a tool that allows you to visually select UV islands of a mesh in the Unity Editor and export them as a mask image (PNG) or vertex colors.

---

## Key Features
- **UV Island Selection**: Select directly by clicking the mesh in the Scene View.
- **Work Copy Function (Auto)**: Automatically creates a static working copy to prevent misalignment due to mesh deformation.
- **Flexible Export**: Supports channel-specific writing (RGBA) and baking to vertex colors.
- **Automated Import Settings**: Exported textures automatically have `Read/Write Enabled` and `Streaming Mipmaps` enabled.

---

## Basic Steps
1. Open `Tools > MaskMaker` from the menu.
2. Drag and drop the target GameObject into the `Target Model` frame at the top of the window (or specify it in the Object field).
   - **Work Copy**: By default, a working copy (`[WorkCopy]`) is created and displayed in the scene to prevent position misalignment.
3. Select `Add` (Select) or `Remove` (Deselect) in `Edit Mode`.
   - You can switch modes with a hotkey (default `R`).
4. Click part of the mesh in the Scene View or `UV Preview` to select an island.
5. Click `Analyze UVs` in `Selection Actions` to analyze the UVs (required initially or when settings change).
6. Check settings in the `Quick Export` section and click `Save PNG` to export the mask.

---

## Section Descriptions

### 1. Target Model
- **Object**: The GameObject containing the target MeshRenderer or SkinnedMeshRenderer.
- **Create Work Copy / Remove Copy & Return**:
  - Creates a duplicate for work purposes unaffected by mesh deformation (such as Modular Avatar).
  - The original object is hidden, and clicking "Remove Copy & Return" after finishing work restores the original state.

### 2. Edit Mode
- **Add / Remove**: Switches the basic behavior upon clicking.
- **Hotkey**: By default, you can quickly switch this mode with the `R` key (changeable in Preferences).

### 3. UV Preview
- Displays the analyzed UVs.
- You can also directly click here to select or deselect islands.

### 4. Selection Actions
- **Analyze UVs**: Analyzes the UV layout. Execute this when changing the target or UV channel.
- **Invert / Select All / Clear**: Inverts selection, selects all, or clears selection.

### 5. Quick Export
- **Resolution**: Output texture size.
- **Invert Mask**: Swaps black (opaque) and white (transparent) in the final output.
- **Pixel Margin**: Expands black areas by a few pixels to prevent bleeding near UV seams.
- **Save PNG**: Saves the mask image.

### 6. Output Settings (Collapsible)
- **File Name**: Output file name (without extension).
- **Output Folder**: Destination folder (can be specified by dragging and dropping).
- **Use Main Texture Folder**: If checked, saves in the same location as the model's texture.
- **Save Inverted Mask**: Checks to output `_inv.png` simultaneously along with the normal mask.

### 7. Advanced Options (Collapsible)
- **🎨 Scene Overlay**: Display settings for the Scene View (line thickness, color, Z-fighting prevention, etc.).
- **📝 Channel Write**: Settings to write the mask only to specific RGBA channels. Overwriting existing PNGs is also possible.
- **🎯 Vertex Color Bake**: Bakes the selected range as vertex colors onto the mesh and saves it as a new asset.
- **⚙️ Preferences**: Language switching, hotkey settings, initial settings for Work Copy, etc.

---

## Troubleshooting

**Q. Clicking the mesh does not respond**
- Check if the target object is set correctly.
- If not using a Work Copy, the collider might not be generated correctly. Try pressing `Analyze UVs` again.
- Also, clicks from the back side of the mesh are not detected. Even if the shader is double-sided, please click on the front face.

**Q. Display and detection are misaligned on SkinnedMeshRenderer**
- If `Auto Work Copy` is enabled (default), a static copy is created so misalignment does not occur.
- Even without using Work Copy (when setting is OFF), detection is performed internally matching the current pose, but use of Work Copy is recommended if there is extreme deformation.

**Q. Mask image is blurry / Colors bleed at UV boundaries**
- Adjust the `Pixel Margin` value (default 2px). This slightly expands the black area to fill gaps in the UVs.

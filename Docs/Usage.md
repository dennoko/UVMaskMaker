# MaskMaker Usage Guide (Unity 2022.3)

This document explains how to use **MaskMaker** (`Tools > MaskMaker`). It is a tool that allows you to visually select UV islands of a mesh in the Unity Editor and export them as a mask image (PNG) or vertex colors.

---

## Key Features
- **UV Island Selection**: Select directly by clicking the mesh in the Scene View or the Preview area.
- **Hand-painting**: Use the brush tool on the preview to freely paint masks. Can be combined with island selection for merged output.
- **Automatic UV Analysis**: UV layout is automatically analyzed when the target is set, allowing you to start working immediately.
- **Work Copy Function**: Automatically creates a static working copy to prevent misalignment due to mesh deformation.
- **Flexible Export**: Supports channel-specific writing (RGBA) and baking to vertex colors.
- **Automated Import Settings**: Exported textures automatically have `Read/Write Enabled` and `Streaming Mipmaps` enabled.

---

## Basic Steps

### STEP 1: Set Target Model
1. Open `Tools > MaskMaker` from the menu.
2. Drag and drop the target GameObject into the `Target Model` frame.
3. Select the **Target Material** (if extracting from a specific submesh) and **UV Channel** (usually UV0).
   - *Note: UV analysis is executed automatically when the target is set.*
   - *Note: A working copy (`[WorkCopy]`) is created by default.*

### STEP 2: Island Selection & Painting
1. Click the mesh in the Scene View or the `PREVIEW` area at the top of the window to select islands.
2. Toggle between **Add** mode (add to selection) and **Remove** mode (deselect).
   - You can quickly switch modes with a hotkey (default `R`).
3. Use the `Invert`, `Select All`, and `Clear` buttons for batch operations.
4. **Paint Mode**: Select "Paint" in the toolbar below the preview to directly paint masks by left-click + drag.
   - **Brush Size**: Adjust brush size with the slider (1–100px).
   - **Eraser**: Toggle eraser mode to remove painted areas (island selections are not affected).
   - **Undo / Redo**: Undo/Redo for paint strokes only.
   - **Clear Paint**: Clears all hand-painted data.

### STEP 3: Export
1. Confirm the resolution, invert mask option, and pixel margin in the `Quick Export` section.
2. Click `Save PNG` to export the mask image.

---

## Section Descriptions

### 1. Target Model
- **Object**: The GameObject containing the target MeshRenderer or SkinnedMeshRenderer.
- **Target Material**: Focuses analysis on a specific submesh/material. Useful for models with overlapping UVs.
- **UV Channel**: Specifies the UV channel (UV0–UV7) used for analysis.
- **Create Work Copy / Remove Copy & Return**:
  - Creates a duplicate unaffected by mesh deformation (e.g., Modular Avatar).
  - Clicking "Remove Copy & Return" restores the session to the original focus.

### 2. Island Selection
- **Add / Remove**: Switches the basic behavior upon clicking.
- **Selection Count**: Displays the number of currently selected islands.
- **Batch Action Buttons**:
  - **Invert**: Selects unselected islands and deselects selected ones.
  - **Select All**: Selects all islands.
  - **Clear**: Deselects all islands.

### 3. PREVIEW
- Displays the analyzed UVs.
- **View Controls**: Use the mouse wheel to zoom and right-drag to pan.
- **size reset**: Resets the view to the initial position.
- You can also click directly here to select or deselect islands.

#### Hand-painting Details
In addition to island-based selection, you can draw freely on a pixel-by-pixel basis.

- **Switching Modes**: Select "Paint" in the toolbar below the preview to enter drawing mode (Switching back to "Select" will return to island selection).
- **Drawing and Erasing**: 
  - **Left Drag**: Paints the mask.
  - **Eraser**: When ON, your strokes will remove (unselect) the painted area.
- **Undo / Redo**: 
  - Each paint stroke can be individually reversed using `Undo` / `Redo`.
  - *Note: This is managed as a separate history from island selection changes.*
- **Merging with Island Selection**: 
  - The final mask is the result of an **OR operation** between **[Selected Islands] + [Hand-painted areas]**.
  - This means that even if you use the eraser on an area covered by a selected island, it will still be included in the output unless you deselect the island itself.

> **Tips**: It is most efficient to select the general shape using UV islands first, then use hand-painting for fine adjustments.

### 4. Quick Export
- **Resolution**: Output texture size.
- **Invert Mask**: Swaps black (opaque) and white (transparent) in the final output.
- **Pixel Margin**: Expands black areas by a few pixels to prevent bleeding near UV seams.

### 5. Output Settings (Advanced)
- **File Name**: Output filename (without extension).
- **Output Folder**: Destination folder. You can drag and drop folders or images into the frame.
- **Use Main Texture Folder**: Saves in the same location as the model's texture.
- **Also save inverted mask**: Outputs `_inv.png` alongside the normal mask.

---

## Advanced Options

### Scene Overlay
Display settings for the Scene View.
- **Draw on Top (X-Ray)**: Always displays wires/seams in the foreground.
- **Thickness / Depth Offset**: Adjusts line weight and prevents flickering (z-fighting).
- **Color Settings**: Customizes colors for selected islands and seams.

### Channel Write
Writes the mask only to specific RGB(A) channels.
- **Enabled**: Check to enable channel-specific writing.
- **Base PNG**: Specify an image to overwrite.

### Vertex Color Bake
Bakes the selection mask as vertex colors into the mesh and saves it as a new asset.

### Preferences
Global tool settings.
- **Language Mode**: Toggles between English and Japanese UI.
- **Toggle Hotkey**: Key used for mode switching (default `R`).
- **Auto Work Copy**: Toggles automatic creation of WorkCopy upon setting a target.

---

## Troubleshooting

**Q. Clicking the mesh does not respond**
- Ensure the target object (or WorkCopy) is correctly set.
- Ensure you are clicking the "front" face of the mesh (back faces are not detected).

**Q. White bleeding occurs at mask boundaries**
- Adjust the `Pixel Margin` value (recommended: 2px or more). This slightly expands the black area to fill UV gaps.

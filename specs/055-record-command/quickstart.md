# Quickstart: Visual Command Recorder

## Overview

The Visual Step Picker lets you record `PrimitiveTap`, `KeyInput`, and `Swipe` steps for a command by clicking/dragging/typing on a captured emulator screenshot, rather than entering image IDs or coordinates manually.

## How to Use

1. Open the command editor for any command.
2. Click **"Record steps"** in the step toolbar — the Visual Step Picker modal opens.
3. The current emulator screenshot loads automatically with matched image regions highlighted as labeled bounding boxes.
4. Interact to record steps:
   - **Tap**: Click inside a highlighted bounding box → records a `PrimitiveTap` step targeting that image, with your exact click position relative to the image center stored as the offset.
   - **Swipe**: Click-and-drag across the screenshot — drag ≥10px → records a `Swipe` step with start/end coordinates and gesture duration.
   - **Key press**: Press any key on your keyboard (including Enter and Escape) → records a `KeyInput` step. No keys are reserved as UI shortcuts.
5. Recorded steps appear in the list on the right as you capture them. You can:
   - **Delete** a step with its remove button.
   - **Reorder** steps by dragging.
6. To refresh the screenshot (e.g., after navigating in the game), click **"Re-capture"**. Input is briefly blocked while the new screenshot loads.
7. Click **"Confirm"** to append all recorded steps to the command, or **"Cancel"** to discard them.

## Notes

- Clicks outside all highlighted regions are ignored — only recognized image regions produce tap steps.
- If no reference images match the current screen, no overlays appear; you can still record key and swipe steps.
- Steps are always appended to the **end** of the command's existing step list, regardless of where you are in the editor.
- To close the picker without adding steps, use the **"Cancel"** button (not a keyboard shortcut).

# Copilot Instructions

## Project Guidelines
- User prefers not to modify third-party libraries when a local project-side workaround is possible.

## Plugin Usage Guidelines
- DeckHost should use two plugin categories: 
  - Device plugins for hardware support
  - Application/functional plugins for control features such as music control
- The designer should render what the device would show and support per-control text/image content on capable outputs.
- StreamDock SDK provides per-control rendering capabilities through setKeyImgFile/setKeyImgFileStream for individual button images. The current DeckHost implementation incorrectly renders all controls into a single 800x480 framebuffer sent to setBackgroundImgStream instead of using per-control rendering APIs.
- Allow dragging assignments off controls to remove them. Dropping an assignment outside a control should also remove it.
- Drag-and-drop should visually show the control as the drag element.
- Controls should have a right-click context menu with actions like Customize and Remove. 
- Customize should open a separate window with control-specific text/image options and use file browsing instead of manual image path entry.
- Support a Multi Action action type and group actions by plugin with collapsible groups.
- The app should have a hamburger menu showing the current page and a separate plugin manager for viewing/managing plugins.

## Device Control Guidelines
- Include RGB LED control through setLedBrightness, setLedColor, and setSingleLedColor.
- Implement input event callbacks via ReadController.
- Manage device configuration through Configer.
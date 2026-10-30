# TASK-008 - Interactive graph board

## Goal

Make the Todo Graph canvas usable as a board with pan, scrolling and zoom while preserving node selection.

## Result and verification

- Added zoom controls and Ctrl+mouse-wheel zoom from 40% to 200%; drag on the empty canvas pans the board. Node clicks still select nodes.
- Edge/browser test with Microsoft Edge confirmed zoom controls change scale, drag changes scroll position, and task selection remains usable.
- `node --check todo-graph/wwwroot/graph.js` passes.

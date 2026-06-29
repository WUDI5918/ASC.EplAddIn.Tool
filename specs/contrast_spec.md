# Specification: Low-Contrast Text Readability Improvements

## 1. Goal & Objectives
Improve the text readability and visual accessibility of the `AutoSchematicConfigurator` user interface (especially under Dark theme) by identifying and resolving all instances of low-contrast text against dark backgrounds (e.g., #1F1F1F, #222222, #1A1A1A, #282828).

## 2. Requirements & Standards
- **Contrast Ratio Compliance**: All readable text and informative icons must meet WCAG 2.1 AA contrast ratio guidelines (at least 4.5:1 for normal text, and 3:1 for large text or graphical indicators).
- **Consistent Color Palette**: Colors must align with the existing theme palette:
  - High-contrast text: `White` or `#E2E8F0` (light gray).
  - Muted/secondary text: `#94A3B8` (standard muted gray, mapped to the `TextMuted` resource).
  - Drag handle / spacing: `#888888` or `#94A3B8`.
- **Target Areas**:
  - Empty state icons (currently using `#555` which is extremely dark on dark backgrounds).
  - Empty state labels (currently using `#777` or `#555`).
  - Landing page description text (currently using `#475569` which is very low contrast on `#1F1F1F`).
  - Row indexes and drag indicators (currently using `#888`).

## 3. Out of Scope
- Changing the overall color scheme of the application window (we want to preserve the dark theme styling).
- Redesigning the layout of the workspaces.

# Technical Plan: Low-Contrast Text Readability Improvements

This plan describes the technical modifications to improve contrast ratios for text elements in `ConfiguratorWindow.xaml`.

## 1. Color Mapping and Resource Usage
Instead of using hardcoded low-contrast hex values, we will standardize on the `TextMuted` resource (`#94A3B8`) or standard light colors (`White`, `#E2E8F0`) where readability is required.

| Original Color | Target Color | Element Type | Rationale |
| :--- | :--- | :--- | :--- |
| `#555` | `#94A3B8` (or `{StaticResource TextMuted}`) | Empty State Info Icons (`ⓘ`) | Promotes icon visibility on dark backgrounds |
| `#555` | `#94A3B8` (or `{StaticResource TextMuted}`) | Empty State Labels ("No Script Typicals", etc.) | Text was virtually unreadable |
| `#777` | `#94A3B8` (or `{StaticResource TextMuted}`) | Standard Empty State Sub-labels | Enhances general readability |
| `#888` | `#94A3B8` (or `{StaticResource TextMuted}`) | Table Drag Indexes & Spacings | Standardizes muted labels contrast |
| `#475569` | `#94A3B8` (or `{StaticResource TextMuted}`) | Landing Page Subtitle Description | Fixes the very low contrast landing text |

## 2. Proposed Changes

### 2.1 WPF UI layout changes

#### [MODIFY] [ConfiguratorWindow.xaml](file:///c:/Users/62789/source/repos/ASC.EplAddIn.Tool/ConfiguratorWindow.xaml)
- Change all `Foreground="#555"` to `Foreground="#94A3B8"` or `Foreground="{StaticResource TextMuted}"` for TextBlocks containing text labels or information symbols.
- Change all `Foreground="#777"` to `Foreground="#94A3B8"` or `Foreground="{StaticResource TextMuted}"`.
- Change all `Foreground="#888"` to `Foreground="#94A3B8"` or `Foreground="{StaticResource TextMuted}"`.
- Change the landing page subtitle TextBlock's `Foreground="#475569"` to `Foreground="#94A3B8"` or `Foreground="{StaticResource TextMuted}"`.

## 3. Verification Plan

### Automated Tests
- Run `dotnet build` to verify the project compiles without XAML schema errors or reference issues.

### Manual Verification
- Launch the tool inside EPLAN to visually inspect the updated contrast of the landing page description, empty state lists, and table row indexes.

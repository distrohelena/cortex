---
name: Cortex Editor
description: Local graph operations with visible typed data and execution.
colors:
  primary: "#c2a9ff"
  background: "#171a21"
  panel: "#20242d"
  panel-raised: "#292e3a"
  foreground: "#e7eaf2"
  muted: "#b1b9c9"
  line: "#3b4251"
  error: "#ffa7ab"
  success: "#a8d6bb"
typography:
  body:
    fontFamily: "system-ui, -apple-system, Segoe UI, sans-serif"
    fontSize: "14px"
    fontWeight: 400
    lineHeight: 1.5
  heading:
    fontFamily: "system-ui, sans-serif"
    fontSize: "22px"
    fontWeight: 650
    lineHeight: 1.3
rounded:
  control: "6px"
spacing:
  small: "8px"
  medium: "16px"
  large: "24px"
components:
  button-primary:
    backgroundColor: "{colors.primary}"
    textColor: "{colors.background}"
    rounded: "{rounded.control}"
    padding: "6px 12px"
---

## Overview

The editor extends the incumbent `flow-graph` canvas. Operational clarity leads: graph in the center, available operations at left, parameters and actual outputs at right. Settings use the same workspace and controls rather than an unrelated dialog or theme.

## Colors

Lavender denotes actions, data wires and selection; execution wires retain the existing light neutral. Green marks completed node titles and pink marks failures. Plugin availability and saved-versus-active configuration are stated with text, not color alone.

## Typography

Use the platform sans for actions, parameters and transcripts. Reserve monospace/tabular numerals for schemas, paths, diagnostics and measurements. Full text remains readable prose; object arrays can be inspected as tables derived from their fields.

## Layout

Desktop uses a 200px palette, flexible canvas and 340px inspector. Below 820px the inspector moves under the canvas. Below 540px the palette becomes a collapsible row and settings/options form one column. Fit the graph initially; retain pan/zoom and expose Enquadrar.

## Components

Controls share borders, 6px radii, 34px minimum height and visible keyboard focus. Node cards remain the reusable canvas's components. Properties derive from metadata. Outputs, labels and diagnostics are text, preserving user content without executing markup.

## Motion

Only small color/background transitions accompany interaction, honoring reduced-motion preferences. Graph changes and execution status have no decorative entrance animation.

## Accessibility

Use labeled inputs, native checkboxes, focus-visible outlines, semantic output tables and live status notices. Busy state prevents graph/settings mutation. A pending restart is announced in text; cancel stays available for a running operation.

## Verification

Desktop and compact captures checked after a single correction batch. Framing, node drag, typed output inspection, GPU execution and settings were exercised in an owned headless browser. Compact canvas is an overview with pan/zoom; reading/editing uses the inspector. No new illustration or raster asset ships in the UI.

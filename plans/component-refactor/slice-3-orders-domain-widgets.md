---
name: slice-3-orders-domain-widgets
overview: Extract Orders-specific reusable widgets from orders.html after shared primitives are available, focusing on FDI/tooth selection, selected teeth preview, work-item picker, material/shade pickers, and delivery date picker.
todos:
  - id: extract-fdi-constants-and-range-utils
    content: Move tooth sequences, hitboxes, range/normalization helpers, and crop-bound logic into an Orders tooth module.
    status: pending
  - id: extract-selected-teeth-preview
    content: Extract readonly selected-teeth preview renderer with clear input/output API.
    status: pending
  - id: extract-selectable-fdi-chart
    content: Extract selectable FDI chart rendering/highlighting with callbacks for tooth pick events.
    status: pending
  - id: extract-work-item-picker
    content: Extract construction/work-item picker state/render/events while preserving overlap/range behavior.
    status: pending
  - id: extract-material-shade-pickers
    content: Extract material and shade picker render/event code using shared selected-mark primitive.
    status: pending
  - id: extract-delivery-date-picker
    content: Extract delivery date calendar/status rendering around month-calendar.js with app-provided data-loading callbacks.
    status: pending
  - id: validate-slice
    content: Run Web.Tests and manual smoke full new/edit order flows.
    status: pending
isProject: false
---

# Slice 3: Orders Domain Widgets

## Goal

Extract reusable Orders-specific widgets from `Web/wwwroot/orders.html` after shared primitives/utilities are available.

This is still not a full page/screen split. The goal is to reduce the most complex inline UI/state code while preserving behavior.

## Candidate New Files

Possible structure:

- `Web/wwwroot/js/orders-teeth.js`
- `Web/wwwroot/js/orders-tooth-chart.js`
- `Web/wwwroot/js/orders-selected-teeth-preview.js`
- `Web/wwwroot/js/orders-work-item-picker.js`
- `Web/wwwroot/js/orders-material-picker.js`
- `Web/wwwroot/js/orders-shade-picker.js`
- `Web/wwwroot/js/orders-delivery-date-picker.js`

Possible namespace:

```js
window.S3DOrders = window.S3DOrders || {};
S3DOrders.Teeth = { ... };
S3DOrders.ToothChart = { create(...) };
S3DOrders.SelectedTeethPreview = { render(...) };
S3DOrders.WorkItemPicker = { create(...) };
```

## General Design Rules

- Widgets should receive DOM containers and callbacks; avoid reading global page variables directly.
- Keep state ownership explicit:
  - either widget owns local state and emits changes,
  - or page owns state and passes it into `render(state)`.
- Prefer page-owned state for this refactor to reduce risk.
- Keep existing CSS classes and markup shape where practical.
- Avoid changing APIs/server payloads.
- Avoid changing route/navigation behavior.

## Widget 1: Teeth Constants and Utilities

Move pure data/functions first. Low risk and useful for both chart and preview.

Current candidates:

- `upperTeeth`
- `lowerTeeth`
- `teeth`
- `toothHitBoxes`
- `jawForTooth(t)`
- `fdiRangeTeeth(a,b)`
- `normalizeToothRange(a,b)`
- `selectedTeethCropBounds(nums,padding)`
- possibly `toothAtJawIndex`, `mapToothToJaw`

Suggested API:

```js
S3DOrders.Teeth.upper
S3DOrders.Teeth.lower
S3DOrders.Teeth.all
S3DOrders.Teeth.hitBoxes
S3DOrders.Teeth.jawFor(tooth)
S3DOrders.Teeth.range(start, end)
S3DOrders.Teeth.normalizeRange(start, end)
S3DOrders.Teeth.cropBounds(teeth, padding)
```

## Widget 2: Selected Teeth Preview

Extract rendering used by:

- order flow overview
- final/created confirmation
- order review

Current candidates:

- `renderSelectedTeethPreview`
- `previewMarkerHtml`
- associated crop/hitbox logic after moving pure utilities

Suggested API:

```js
S3DOrders.SelectedTeethPreview.render(container, {
  teeth: [11, 21],
  items: [{ toothStart, toothEnd, constructionType, locked }],
  labelPrefix: 'Selected teeth'
});
```

Keep output CSS classes unchanged initially.

## Widget 3: Selectable FDI Chart

Extract chart rendering and highlighting:

- `renderToothChart`
- `toothMapButtons`
- `chartWorkItemMarkerHtml`
- `syncToothPickerHighlight`

Suggested API:

```js
const chart = S3DOrders.ToothChart.create(container, {
  onPickTooth: tooth => {},
  getActiveRange: () => [...],
  getLockedItems: () => [...],
  label: 'FDI teeth numbering chart'
});
chart.render();
chart.syncHighlight();
```

## Widget 4: Work Item Picker

This is the highest-risk widget. Extract only after teeth utilities/chart/preview are stable.

Current behavior to preserve:

- construction cycle: Crown -> Bridge -> Inlay/Overlay
- crown/inlay-overlay single tooth behavior
- bridge range behavior
- jaw toggle
- start/end steppers
- add/remove work items
- locked previous work item markers
- overlap prevention
- `validateWorkItems()` semantics

Suggested API options:

### Page-owned state option preferred

```js
const picker = S3DOrders.WorkItemPicker.create(container, {
  getState: () => ({ workItems, activeIndex, construction, selectedJaw }),
  setState: patch => { ... },
  onChange: ({ changedStage }) => { resetForwardProgress(1); updateSummary(); },
  showError: showErr
});
picker.render();
picker.syncHighlight();
```

Avoid deeply embedding order flow logic inside the widget.

## Widget 5: Material and Shade Pickers

Extract lower-risk pickers:

- material choices render/event behavior
- shade groups render/event behavior
- selected mark helper from Slice 1

Suggested API:

```js
S3DOrders.MaterialPicker.bind(container, {
  value: () => material,
  onChange: m => setMaterial(m)
});

S3DOrders.ShadePicker.render(container, {
  value: shade.value,
  groups: SHADE_GROUPS,
  unspecifiedValue: 'unspecified',
  onChange: code => setShade(code)
});
```

## Widget 6: Delivery Date Picker

Extract carefully around existing `month-calendar.js`.

Current candidates:

- delivery calendar rendering
- date cell rendering
- unavailable reason tooltip binding
- selected date display update may remain in page or become widget output
- before-minimum override callback

Suggested API:

```js
const deliveryPicker = S3DOrders.DeliveryDatePicker.create(container, {
  getMonth: () => calendarMonth,
  setMonth: m => { calendarMonth = m; },
  getSelectedDate: () => deadline.value || selectedDate,
  onSelectDate: (iso, status) => requestDeliveryDateSelection(iso, status),
  loadStatuses: async ({ start, end }) => ..., 
  isLabOverride: status => ...
});
```

Keep API flexible. Do not hide server calls if that makes testing/debugging harder.

## Non-Goals

- Do not split `orders.html` into full page modules yet.
- Do not change routing behavior.
- Do not change API payload shape.
- Do not redesign the order flow.
- Do not migrate IAM/Invoices.

## Validation

Run:

```bash
dotnet test Web.Tests/Web.Tests.csproj --no-restore
```

Manual smoke full Orders flow:

- create order with single Crown
- create order with Bridge range
- create order with Inlay/Overlay
- add/remove multiple work items
- overlap prevention works
- jaw toggle/steppers work
- material/shade selection works
- delivery date calendar loads and selection works
- before-minimum lab override still prompts/works
- overview/review/created confirmation previews still display
- edit order still loads and saves

## Review Notes to Provide

- Which widgets were extracted.
- State ownership model chosen for each widget.
- Any globals removed or left intentionally.
- Any behavior intentionally deferred to Slice 4.

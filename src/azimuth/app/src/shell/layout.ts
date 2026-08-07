import type { IJsonModel } from 'flexlayout-react';

/**
 * The default pane arrangement, matching spec §7.1:
 *
 *   ┌ Explorer ─┐ ┌ Chart ──────────────────┐ ┌ Analyzer ──────┐
 *   │ scopes    │ │ candles · volume        │ │ trades table   │
 *   │ sessions  │ ├─────────────────────────┤ │                │
 *   │           │ │ Terminal                │ │                │
 *   └───────────┘ └─────────────────────────┘ └────────────────┘
 *
 * Every tab is `enableFloat` so it can be popped into its own OS window.
 */
export const DEFAULT_LAYOUT: IJsonModel = {
  global: {
    tabEnableFloat: true,
    tabEnableRename: false,
    tabSetEnableMaximize: true,
    tabSetMinWidth: 120,
    tabSetMinHeight: 80,
    splitterSize: 4,
    splitterExtra: 4,
  },
  borders: [],
  layout: {
    type: 'row',
    weight: 100,
    children: [
      {
        type: 'tabset',
        weight: 17,
        children: [
          { type: 'tab', name: 'Explorer', component: 'explorer', enableClose: false },
        ],
      },
      {
        type: 'row',
        weight: 55,
        children: [
          {
            type: 'tabset',
            weight: 68,
            children: [{ type: 'tab', name: 'Chart', component: 'chart', enableClose: false }],
          },
          {
            type: 'tabset',
            weight: 32,
            children: [{ type: 'tab', name: 'Terminal', component: 'terminal', enableClose: false }],
          },
        ],
      },
      {
        type: 'tabset',
        weight: 28,
        children: [
          { type: 'tab', name: 'Analyzer', component: 'analyzer', enableClose: false },
        ],
      },
    ],
  },
};
